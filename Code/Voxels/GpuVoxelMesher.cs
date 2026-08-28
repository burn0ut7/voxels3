using System;
using System.Threading.Tasks;
using Sandbox.Rendering;

internal sealed class GpuVoxelMesher : IDisposable
{
	public const int VerticesPerActiveCell = 15;
	public const int MaximumDispatchesPerUpdate = 8;

	private readonly Scene _scene;
	private readonly ComputeShader _computeShader = new( "shaders/voxels/voxel_regular_mesher_cs.shader" );
	private readonly ComputeShader _diagnosticShader = new( "shaders/voxels/voxel_mesh_diagnostics_cs.shader" );
	private readonly ComputeShader _visibilityShader = new( "shaders/voxels/voxel_chunk_visibility_cs.shader" );
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_terrain.shader" );
	private readonly Sandbox.Rendering.CommandList _meshCommands = new( "Voxel Terrain Meshing" );
	private readonly Sandbox.Rendering.CommandList _drawCommands = new( "Voxel Terrain Indirect Draws" );
	private readonly Dictionary<Vector3Int, MeshResource> _resident = new();
	private readonly Dictionary<Vector3Int, GpuSdfDescriptor> _pending = new();
	private readonly Queue<GpuSdfDescriptor> _dispatchQueue = new();
	private readonly Queue<InspectionRequest> _inspectionQueue = new();
	private readonly Queue<ReadbackRequest> _readbackQueue = new();
	private readonly HashSet<Vector3Int> _pendingInspections = new();
	private readonly Dictionary<Vector3Int, string> _completedInspections = new();
	private readonly object _inspectionLock = new();
	private readonly object _visibilityLock = new();
	private readonly Stack<MeshResource> _pool = new();
	private readonly List<MeshResource> _drawOrder = new();
	private readonly List<InFlightMesh> _inFlight = new();
	private readonly HashSet<Vector3Int> _cancelledInFlight = new();
	private readonly List<VisibilityBuffers> _retiredVisibilityBuffers = new();
	private readonly ReadbackSceneObject _readbackObject;

	private CameraComponent _camera;
	private int _capacity;
	private int _allocatedResourceCount;
	private int _visibilityCapacity;
	private Vector4[] _visibilityBoundsData = Array.Empty<Vector4>();
	private VisibilityBuffers _visibilityBuffers;
	private readonly GpuBuffer<uint> _visibilityAggregateCounters = new(
		5,
		GpuBuffer.UsageFlags.Structured,
		"Voxel Visibility Aggregate Counters" );
	private bool _drawCommandsDirty;
	private bool _visibilityDescriptorsDirty;
	private bool _visibilityCountsNeedRefresh;
	private bool _visibilityMeasurementActive;
	private bool _visibilityReadbackPending;
	private bool _visibilityReadbackInFlight;
	private long _visibilityReadbackRequestedRenderSequence;
	private GpuVisibilityMeasurement? _completedVisibilityMeasurement;
	private long _dispatchCount;
	private long _poolAllocationCount;
	private long _poolReuseCount;
	private long _scalarReadbackCount;
	private long _visibilityScalarReadbackCount;
	private long _updateSequence;
	private long _renderSequence;
	private long _submittedRenderSequence;

	public int ResidentCount => _resident.Count;
	public int PendingCount => _pending.Count + _inFlight.Count;
	public int PoolCount => _pool.Count;
	public long DispatchCount => _dispatchCount;
	public long PoolAllocationCount => _poolAllocationCount;
	public long PoolReuseCount => _poolReuseCount;
	public long ScalarReadbackCount => _scalarReadbackCount;
	public long VisibilityScalarReadbackCount => _visibilityScalarReadbackCount;
	public const long GeometryReadbackCount = 0;
	public long LogicalCapacityBytes => (long)_resident.Count * _capacity * sizeof( uint );
	public long LogicalVisibilityBytes => _visibilityCapacity == 0
		? 0
		: (long)_visibilityCapacity * (sizeof( float ) * 8 + sizeof( uint ) * 8) + sizeof( uint ) * 7;

	// Installed s&box 26.08.19 ABI: four sequential uint fields. CopyStructureCount uses
	// byte offsets, while DrawInstancedIndirect uses an argument-element index.
	private const int IndirectArgumentStride = sizeof( uint ) * 4;
	private const int IndirectInstanceCountOffset = sizeof( uint );

	public GpuVoxelMesher( Scene scene, int cellsPerAxis )
	{
		_scene = scene;
		_capacity = checked( cellsPerAxis * cellsPerAxis * cellsPerAxis );
		_readbackObject = new ReadbackSceneObject( scene.SceneWorld, this );
		Sandbox.Diagnostics.GpuProfilerStats.Enabled = true;
		AttachToMainCamera();
	}

	public void Schedule( VoxelChunk chunk, float surfaceHeight, int sourceRevision )
	{
		if ( chunk.MaximumDensity <= 0f || chunk.MinimumDensity > 0f )
		{
			Remove( chunk.Coordinate );
			return;
		}

		var descriptor = GpuSdfDescriptor.FromChunk( chunk, surfaceHeight, sourceRevision );
		if ( _resident.TryGetValue( chunk.Coordinate, out var resident ) &&
			resident.Descriptor == descriptor )
		{
			return;
		}

		_pending[chunk.Coordinate] = descriptor;
		_dispatchQueue.Enqueue( descriptor );
	}

	public void Remove( Vector3Int coordinate )
	{
		_pending.Remove( coordinate );
		foreach ( var inFlight in _inFlight )
		{
			if ( inFlight.Descriptor.ChunkCoordinate == coordinate )
			{
				_cancelledInFlight.Add( coordinate );
				break;
			}
		}

		if ( !_resident.Remove( coordinate, out var resource ) )
		{
			return;
		}

		SetVisibilityActive( resource, false );
		_pool.Push( resource );
		_drawCommandsDirty = true;
	}

	public void Reset( int cellsPerAxis )
	{
		var capacity = checked( cellsPerAxis * cellsPerAxis * cellsPerAxis );
		Clear();
		if ( capacity == _capacity )
		{
			return;
		}

		DisposePool();
		_capacity = capacity;
	}

	public int ProcessPending( int maximumDispatches )
	{
		System.Threading.Interlocked.Increment( ref _updateSequence );
		AttachToMainCamera();
		FinalizeInFlight();
		if ( _inFlight.Count > 0 )
		{
			return 0;
		}

		_meshCommands.Reset();
		var maximumPotentialAcquisitions = Math.Min( maximumDispatches, _dispatchQueue.Count );
		var maximumNewResources = Math.Max( 0, maximumPotentialAcquisitions - _pool.Count );
		EnsureVisibilityCapacity( checked( _allocatedResourceCount + maximumNewResources ) );
		var copyVisibilityCounts = _visibilityCountsNeedRefresh || maximumPotentialAcquisitions > 0;
		if ( copyVisibilityCounts )
		{
			_meshCommands.ResourceBarrierTransition(
				_visibilityBuffers.SourceArguments,
				ResourceState.CopyDestination );
			if ( _visibilityCountsNeedRefresh )
			{
				foreach ( var resource in _resident.Values )
				{
					_meshCommands.CopyStructureCount(
						resource.ActiveCells,
						_visibilityBuffers.SourceArguments,
						resource.VisibilitySlot * IndirectArgumentStride + IndirectInstanceCountOffset );
				}

				_visibilityCountsNeedRefresh = false;
			}
		}

		var processed = 0;
		while ( processed < maximumDispatches && _dispatchQueue.TryDequeue( out var descriptor ) )
		{
			if ( !_pending.TryGetValue( descriptor.ChunkCoordinate, out var current ) || current != descriptor )
			{
				continue;
			}

			_pending.Remove( descriptor.ChunkCoordinate );
			var resource = Acquire();
			resource.Prepare( descriptor );
			_meshCommands.Attributes.Set( "ActiveCells", resource.ActiveCells );
			_meshCommands.Attributes.Set( "ChunkCoordinate", new Vector3(
				descriptor.ChunkCoordinate.x,
				descriptor.ChunkCoordinate.y,
				descriptor.ChunkCoordinate.z ) );
			_meshCommands.Attributes.Set( "CellsPerAxis", descriptor.CellsPerAxis );
			_meshCommands.Attributes.Set( "CellSize", descriptor.CellSize );
			_meshCommands.Attributes.Set( "SurfaceHeight", descriptor.SurfaceHeight );
			_meshCommands.ResourceBarrierTransition( resource.ActiveCells, ResourceState.UnorderedAccess );
			_meshCommands.SetCounterValue( resource.ActiveCells, 0 );
			_meshCommands.DispatchCompute(
				_computeShader,
				descriptor.CellsPerAxis,
				descriptor.CellsPerAxis,
				descriptor.CellsPerAxis );
			_meshCommands.UavBarrier( resource.ActiveCells );
			_meshCommands.ResourceBarrierTransition( resource.IndirectArguments, ResourceState.CopyDestination );
			_meshCommands.CopyStructureCount(
				resource.ActiveCells,
				resource.IndirectArguments,
				IndirectInstanceCountOffset );
			_meshCommands.CopyStructureCount(
				resource.ActiveCells,
				_visibilityBuffers.SourceArguments,
				resource.VisibilitySlot * IndirectArgumentStride + IndirectInstanceCountOffset );
			_meshCommands.ResourceBarrierTransition( resource.ActiveCells, ResourceState.GenericRead );
			_meshCommands.ResourceBarrierTransition( resource.IndirectArguments, ResourceState.IndirectArgument );
			_inFlight.Add( new InFlightMesh( descriptor, resource ) );
			processed++;
		}

		if ( copyVisibilityCounts )
		{
			_meshCommands.ResourceBarrierTransition(
				_visibilityBuffers.SourceArguments,
				ResourceState.GenericRead );
		}

		if ( processed > 0 )
		{
			_submittedRenderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
		}

		ProcessInspections( System.Threading.Interlocked.Read( ref _updateSequence ) );
		CommitDrawCommands();
		return processed;
	}

	private void FinalizeInFlight()
	{
		if ( _inFlight.Count == 0 ||
			System.Threading.Interlocked.Read( ref _renderSequence ) <= _submittedRenderSequence )
		{
			return;
		}

		foreach ( var completed in _inFlight )
		{
			var coordinate = completed.Descriptor.ChunkCoordinate;
			if ( _cancelledInFlight.Remove( coordinate ) ||
				(_pending.TryGetValue( coordinate, out var replacement ) && replacement != completed.Descriptor) )
			{
				_pool.Push( completed.Resource );
				continue;
			}

			if ( _resident.Remove( coordinate, out var previous ) )
			{
				SetVisibilityActive( previous, false );
				_pool.Push( previous );
			}

			_resident.Add( coordinate, completed.Resource );
			SetVisibilityActive( completed.Resource, true );
			_dispatchCount++;
			_drawCommandsDirty = true;
		}

		_inFlight.Clear();
	}

	public void CommitDrawCommands()
	{
		UploadVisibilityDescriptors();
		if ( !_drawCommandsDirty )
		{
			return;
		}

		_drawCommands.Reset();
		_drawOrder.Clear();
		if ( _visibilityCapacity == 0 )
		{
			_drawCommandsDirty = false;
			return;
		}

		foreach ( var resource in _resident.Values )
		{
			_drawOrder.Add( resource );
		}

		_drawOrder.Sort( static ( left, right ) =>
		{
			var z = left.Descriptor.ChunkCoordinate.z.CompareTo( right.Descriptor.ChunkCoordinate.z );
			if ( z != 0 )
			{
				return z;
			}

			var y = left.Descriptor.ChunkCoordinate.y.CompareTo( right.Descriptor.ChunkCoordinate.y );
			return y != 0
				? y
				: left.Descriptor.ChunkCoordinate.x.CompareTo( right.Descriptor.ChunkCoordinate.x );
		} );

		foreach ( var resource in _drawOrder )
		{
			_drawCommands.DrawInstancedIndirect(
				_material,
				_visibilityBuffers.VisibleArguments,
				(uint)resource.VisibilitySlot,
				resource.DrawAttributes,
				Graphics.PrimitiveType.Triangles );
		}

		_drawCommandsDirty = false;
	}

	public string Inspect( VoxelChunk chunk )
	{
		if ( chunk.MaximumDensity <= 0f )
		{
			return $"{chunk.LogId} classification=solid gpuResource=false scalarReadbacks={_scalarReadbackCount} geometryReadbacks=0";
		}

		if ( chunk.MinimumDensity > 0f )
		{
			return $"{chunk.LogId} classification=air gpuResource=false scalarReadbacks={_scalarReadbackCount} geometryReadbacks=0";
		}

		var request = new InspectionRequest(
			chunk.Coordinate,
			chunk.LogId,
			chunk.MinimumDensity,
			chunk.MaximumDensity );
		lock ( _inspectionLock )
		{
			if ( _completedInspections.Remove( chunk.Coordinate, out var completed ) )
			{
				return completed;
			}

			if ( !_pendingInspections.Add( chunk.Coordinate ) )
			{
				return $"{chunk.LogId} inspection=pending geometryReadbacks=0";
			}

			_inspectionQueue.Enqueue( request );
		}

		return $"{chunk.LogId} inspection=scheduled geometryReadbacks=0";
	}

	private void ProcessInspections( long submittedUpdateSequence )
	{
		while ( true )
		{
			InspectionRequest request;
			lock ( _inspectionLock )
			{
				if ( !_inspectionQueue.TryDequeue( out request ) )
				{
					return;
				}
			}

			if ( request.MaximumDensity <= 0f )
			{
				StoreInspection( request, $"{request.LogId} classification=solid gpuResource=false scalarReadbacks={_scalarReadbackCount} geometryReadbacks=0" );
				continue;
			}

			if ( request.MinimumDensity > 0f )
			{
				StoreInspection( request, $"{request.LogId} classification=air gpuResource=false scalarReadbacks={_scalarReadbackCount} geometryReadbacks=0" );
				continue;
			}

			if ( !_resident.TryGetValue( request.Coordinate, out var resource ) )
			{
				var state = _pending.ContainsKey( request.Coordinate ) ? "pending" : "missing";
				StoreInspection( request, $"{request.LogId} classification=surface gpuResource=false state={state} scalarReadbacks={_scalarReadbackCount} geometryReadbacks=0" );
				continue;
			}

			var statistics = resource.EnsureStatistics();
			var descriptor = resource.Descriptor;
			_meshCommands.Attributes.Set( "MeshStatistics", statistics );
			_meshCommands.Attributes.Set( "ChunkCoordinate", new Vector3(
				descriptor.ChunkCoordinate.x,
				descriptor.ChunkCoordinate.y,
				descriptor.ChunkCoordinate.z ) );
			_meshCommands.Attributes.Set( "CellsPerAxis", descriptor.CellsPerAxis );
			_meshCommands.Attributes.Set( "CellSize", descriptor.CellSize );
			_meshCommands.Attributes.Set( "SurfaceHeight", descriptor.SurfaceHeight );
			_meshCommands.Clear( statistics, 0 );
			_meshCommands.DispatchCompute(
				_diagnosticShader,
				descriptor.CellsPerAxis,
				descriptor.CellsPerAxis,
				descriptor.CellsPerAxis );

			lock ( _inspectionLock )
			{
				_readbackQueue.Enqueue( new ReadbackRequest(
					request,
					resource,
					statistics,
					submittedUpdateSequence ) );
			}
		}
	}

	private void ProcessReadbacks()
	{
		BeginVisibilityReadbackIfRequested();
		while ( true )
		{
			ReadbackRequest readback;
			lock ( _inspectionLock )
			{
				if ( !_readbackQueue.TryPeek( out readback ) ||
					System.Threading.Interlocked.Read( ref _updateSequence ) <= readback.SubmittedUpdateSequence )
				{
					return;
				}

				_readbackQueue.Dequeue();
			}

			var argumentsCompletion = new TaskCompletionSource<GpuBuffer.IndirectDrawArguments>( TaskCreationOptions.RunContinuationsAsynchronously );
			var statisticsCompletion = new TaskCompletionSource<(uint ActiveCells, uint Triangles, uint InvalidGradients)>( TaskCreationOptions.RunContinuationsAsynchronously );
			_scalarReadbackCount += 2;
			readback.Resource.IndirectArguments.GetDataAsync<GpuBuffer.IndirectDrawArguments>( data =>
			{
				argumentsCompletion.TrySetResult( data.Length > 0 ? data[0] : default );
			} );
			readback.Statistics.GetDataAsync<uint>( data =>
			{
				statisticsCompletion.TrySetResult( data.Length >= 3 ? (data[0], data[1], data[2]) : default );
			} );
			CompleteInspectionAsync( readback.Inspection, argumentsCompletion.Task, statisticsCompletion.Task );
		}
	}

	private void BeginVisibilityReadbackIfRequested()
	{
		GpuBuffer<uint> counters;
		long logicalBytes;
		lock ( _visibilityLock )
		{
			if ( !_visibilityReadbackPending || _visibilityReadbackInFlight ||
				System.Threading.Interlocked.Read( ref _renderSequence ) <=
					_visibilityReadbackRequestedRenderSequence + 1 )
			{
				return;
			}

			_visibilityReadbackPending = false;
			_visibilityReadbackInFlight = true;
			counters = _visibilityAggregateCounters;
			logicalBytes = LogicalVisibilityBytes;
			_visibilityScalarReadbackCount++;
			_scalarReadbackCount++;
		}

		counters.GetDataAsync<uint>( data =>
		{
			var frames = data.Length >= 5 ? data[0] : 0;
			var residentTotal = data.Length >= 5 ? data[1] : 0;
			var visibleTotal = data.Length >= 5 ? data[2] : 0;
			var minimumVisible = data.Length >= 5 && frames > 0 && data[3] != uint.MaxValue ? data[3] : 0;
			var maximumVisible = data.Length >= 5 ? data[4] : 0;
			lock ( _visibilityLock )
			{
				_completedVisibilityMeasurement = new GpuVisibilityMeasurement(
					frames,
					residentTotal,
					visibleTotal,
					minimumVisible,
					maximumVisible,
					logicalBytes,
					1 );
				_visibilityReadbackInFlight = false;
			}
		} );
	}

	public void BeginVisibilityMeasurement()
	{
		lock ( _visibilityLock )
		{
			_completedVisibilityMeasurement = null;
			_visibilityReadbackPending = false;
			_visibilityReadbackInFlight = false;
		}

		Span<uint> initialCounters = stackalloc uint[5];
		initialCounters[3] = uint.MaxValue;
		_visibilityAggregateCounters.SetData( initialCounters );
		_visibilityMeasurementActive = true;
		_drawCommandsDirty = true;
		CommitDrawCommands();
	}

	public void EndVisibilityMeasurement()
	{
		_visibilityMeasurementActive = false;
		_drawCommandsDirty = true;
		CommitDrawCommands();
		lock ( _visibilityLock )
		{
			_visibilityReadbackPending = true;
			_visibilityReadbackRequestedRenderSequence =
				System.Threading.Interlocked.Read( ref _renderSequence );
		}
	}

	public bool TryTakeVisibilityMeasurement( out GpuVisibilityMeasurement measurement )
	{
		lock ( _visibilityLock )
		{
			if ( _completedVisibilityMeasurement is not { } completed )
			{
				measurement = default;
				return false;
			}

			measurement = completed;
			_completedVisibilityMeasurement = null;
			return true;
		}
	}

	private async void CompleteInspectionAsync(
		InspectionRequest request,
		Task<GpuBuffer.IndirectDrawArguments> argumentsTask,
		Task<(uint ActiveCells, uint Triangles, uint InvalidGradients)> statisticsTask )
	{
		var arguments = await argumentsTask;
		var statistics = await statisticsTask;
		StoreInspection( request,
			$"{request.LogId} classification=surface gpuResource=true activeCells={arguments.InstanceCount} " +
			$"diagnosticActiveCells={statistics.ActiveCells} logicalTriangles={statistics.Triangles} " +
			$"invalidGradients={statistics.InvalidGradients} overflow=0 " +
			$"capacityRecords={_capacity} capacityBytes={_capacity * sizeof( uint )} " +
			$"scalarReadbacks={_scalarReadbackCount} geometryReadbacks=0" );
	}

	private void StoreInspection( InspectionRequest request, string result )
	{
		lock ( _inspectionLock )
		{
			_pendingInspections.Remove( request.Coordinate );
			_completedInspections[request.Coordinate] = result;
		}
	}

	private void EnsureVisibilityCapacity( int requiredCapacity )
	{
		if ( requiredCapacity <= _visibilityCapacity )
		{
			return;
		}

		var newCapacity = Math.Max( 64, _visibilityCapacity );
		while ( newCapacity < requiredCapacity )
		{
			newCapacity = checked( newCapacity * 2 );
		}

		if ( _visibilityBuffers is not null )
		{
			// Camera command lists execute on the render thread. Keep replaced buffers alive so a
			// previously queued list can finish without observing disposed native resources.
			_retiredVisibilityBuffers.Add( _visibilityBuffers );
		}

		_visibilityCapacity = newCapacity;
		_visibilityBoundsData = new Vector4[newCapacity * 2];
		var bounds = new GpuBuffer<Vector4>(
			newCapacity * 2,
			GpuBuffer.UsageFlags.Structured,
			"Voxel Visibility Bounds" );
		var sourceArguments = new GpuBuffer<GpuBuffer.IndirectDrawArguments>(
			newCapacity,
			GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments,
			"Voxel Source Indirect Arguments" );
		var visibleArguments = new GpuBuffer<GpuBuffer.IndirectDrawArguments>(
			newCapacity,
			GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments,
			"Voxel Visible Indirect Arguments" );
		var frameCounters = new GpuBuffer<uint>(
			2,
			GpuBuffer.UsageFlags.Structured,
			"Voxel Visibility Frame Counters" );
		var initialArguments = new GpuBuffer.IndirectDrawArguments[newCapacity];
		for ( var index = 0; index < initialArguments.Length; index++ )
		{
			initialArguments[index].VertexCount = VerticesPerActiveCell;
		}

		sourceArguments.SetData( initialArguments );
		visibleArguments.SetData( initialArguments );
		_visibilityBuffers = new VisibilityBuffers(
			bounds,
			sourceArguments,
			visibleArguments,
			frameCounters );

		foreach ( var resource in _resident.Values )
		{
			WriteVisibilityBounds( resource, true );
		}

		_visibilityDescriptorsDirty = true;
		_visibilityCountsNeedRefresh = true;
		_drawCommandsDirty = true;
	}

	private void SetVisibilityActive( MeshResource resource, bool active )
	{
		if ( resource.VisibilitySlot >= _visibilityCapacity )
		{
			return;
		}

		WriteVisibilityBounds( resource, active );
		_visibilityDescriptorsDirty = true;
	}

	private void WriteVisibilityBounds( MeshResource resource, bool active )
	{
		var descriptor = resource.Descriptor;
		var chunkWorldSize = descriptor.CellsPerAxis * descriptor.CellSize;
		var chunkWorldOrigin = new Vector3(
			descriptor.ChunkCoordinate.x * chunkWorldSize,
			descriptor.ChunkCoordinate.y * chunkWorldSize,
			descriptor.ChunkCoordinate.z * chunkWorldSize );
		var padding = new Vector3( descriptor.CellSize );
		var minimum = chunkWorldOrigin - padding;
		var maximum = chunkWorldOrigin + new Vector3( chunkWorldSize ) + padding;
		var index = resource.VisibilitySlot * 2;
		_visibilityBoundsData[index] = new Vector4( minimum, active ? 1f : 0f );
		_visibilityBoundsData[index + 1] = new Vector4( maximum, 0f );
	}

	private void UploadVisibilityDescriptors()
	{
		if ( !_visibilityDescriptorsDirty || _visibilityBuffers is null )
		{
			return;
		}

		_visibilityBuffers.Bounds.SetData( _visibilityBoundsData );
		_visibilityDescriptorsDirty = false;
	}

	private void DispatchVisibility()
	{
		UploadVisibilityDescriptors();
		if ( _visibilityBuffers is null || _visibilityCapacity == 0 || _camera is null )
		{
			return;
		}

		_visibilityShader.Attributes.Set( "VisibilityBounds", _visibilityBuffers.Bounds );
		_visibilityShader.Attributes.Set( "SourceIndirectArguments", _visibilityBuffers.SourceArguments );
		_visibilityShader.Attributes.Set( "VisibleIndirectArguments", _visibilityBuffers.VisibleArguments );
		_visibilityShader.Attributes.Set( "VisibilityFrameCounters", _visibilityBuffers.FrameCounters );
		_visibilityShader.Attributes.Set( "VisibilityAggregateCounters", _visibilityAggregateCounters );
		_visibilityShader.Attributes.Set( "VisibilitySlotCount", _visibilityCapacity );
		_visibilityShader.Attributes.Set( "VisibilityPass", 0 );
		_visibilityShader.Attributes.Set( "MeasureVisibility", _visibilityMeasurementActive ? 1 : 0 );
		Graphics.ResourceBarrierTransition( _visibilityBuffers.Bounds, ResourceState.GenericRead );
		Graphics.ResourceBarrierTransition( _visibilityBuffers.SourceArguments, ResourceState.GenericRead );
		Graphics.ResourceBarrierTransition( _visibilityBuffers.VisibleArguments, ResourceState.UnorderedAccess );
		Graphics.ResourceBarrierTransition( _visibilityBuffers.FrameCounters, ResourceState.UnorderedAccess );
		_visibilityBuffers.FrameCounters.Clear();
		_visibilityShader.Dispatch( _visibilityCapacity, 1, 1 );
		Graphics.UavBarrier( _visibilityBuffers.VisibleArguments );
		Graphics.UavBarrier( _visibilityBuffers.FrameCounters );
		Graphics.ResourceBarrierTransition( _visibilityBuffers.VisibleArguments, ResourceState.IndirectArgument );

		if ( !_visibilityMeasurementActive )
		{
			return;
		}

		Graphics.ResourceBarrierTransition( _visibilityBuffers.FrameCounters, ResourceState.GenericRead );
		Graphics.ResourceBarrierTransition( _visibilityAggregateCounters, ResourceState.UnorderedAccess );
		_visibilityShader.Attributes.Set( "VisibilityPass", 1 );
		_visibilityShader.Dispatch( 1, 1, 1 );
		Graphics.UavBarrier( _visibilityAggregateCounters );
	}

	public void Clear()
	{
		_meshCommands.Reset();
		_pending.Clear();
		_dispatchQueue.Clear();
		_cancelledInFlight.Clear();
		foreach ( var inFlight in _inFlight )
		{
			_pool.Push( inFlight.Resource );
		}

		_inFlight.Clear();
		foreach ( var resource in _resident.Values )
		{
			SetVisibilityActive( resource, false );
			_pool.Push( resource );
		}

		_resident.Clear();
		_drawCommandsDirty = true;
		CommitDrawCommands();
	}

	public void Dispose()
	{
		Clear();
		if ( _camera is not null )
		{
			_camera.RemoveCommandList( _meshCommands );
			_camera.RemoveCommandList( _drawCommands );
			_camera = null;
		}

		DisposePool();
		DisposeVisibilityBuffers();
		_visibilityAggregateCounters?.Dispose();
		_readbackObject?.Delete();
	}

	private MeshResource Acquire()
	{
		if ( _pool.TryPop( out var resource ) )
		{
			_poolReuseCount++;
			return resource;
		}

		_poolAllocationCount++;
		var allocated = new MeshResource( _capacity, _poolAllocationCount, _allocatedResourceCount );
		_allocatedResourceCount++;
		return allocated;
	}

	private void AttachToMainCamera()
	{
		CameraComponent selected = null;
		foreach ( var candidate in _scene.GetAllComponents<CameraComponent>() )
		{
			selected ??= candidate;
			if ( candidate.IsMainCamera )
			{
				selected = candidate;
				break;
			}
		}

		if ( selected == _camera )
		{
			return;
		}

		_camera?.RemoveCommandList( _meshCommands );
		_camera?.RemoveCommandList( _drawCommands );
		_camera = selected;
		_camera?.AddCommandList( _meshCommands, Sandbox.Rendering.Stage.AfterDepthPrepass, -100 );
		_camera?.AddCommandList( _drawCommands, Sandbox.Rendering.Stage.AfterOpaque, 0 );
	}

	private void DisposePool()
	{
		while ( _pool.TryPop( out var resource ) )
		{
			resource.Dispose();
		}

		_allocatedResourceCount = 0;
	}

	private void DisposeVisibilityBuffers()
	{
		_visibilityBuffers?.Dispose();
		_visibilityBuffers = null;
		foreach ( var retired in _retiredVisibilityBuffers )
		{
			retired.Dispose();
		}

		_retiredVisibilityBuffers.Clear();
		_visibilityCapacity = 0;
		_visibilityBoundsData = Array.Empty<Vector4>();
		_visibilityDescriptorsDirty = false;
		_visibilityCountsNeedRefresh = false;
	}

	private sealed class InspectionRequest
	{
		public Vector3Int Coordinate { get; }
		public string LogId { get; }
		public float MinimumDensity { get; }
		public float MaximumDensity { get; }
		public InspectionRequest( Vector3Int coordinate, string logId, float minimumDensity, float maximumDensity )
		{
			Coordinate = coordinate;
			LogId = logId;
			MinimumDensity = minimumDensity;
			MaximumDensity = maximumDensity;
		}
	}

	private readonly record struct ReadbackRequest(
		InspectionRequest Inspection,
		MeshResource Resource,
		GpuBuffer<uint> Statistics,
		long SubmittedUpdateSequence );
	private readonly record struct InFlightMesh( GpuSdfDescriptor Descriptor, MeshResource Resource );

	private sealed class VisibilityBuffers : IDisposable
	{
		public GpuBuffer<Vector4> Bounds { get; }
		public GpuBuffer<GpuBuffer.IndirectDrawArguments> SourceArguments { get; }
		public GpuBuffer<GpuBuffer.IndirectDrawArguments> VisibleArguments { get; }
		public GpuBuffer<uint> FrameCounters { get; }

		public VisibilityBuffers(
			GpuBuffer<Vector4> bounds,
			GpuBuffer<GpuBuffer.IndirectDrawArguments> sourceArguments,
			GpuBuffer<GpuBuffer.IndirectDrawArguments> visibleArguments,
			GpuBuffer<uint> frameCounters )
		{
			Bounds = bounds;
			SourceArguments = sourceArguments;
			VisibleArguments = visibleArguments;
			FrameCounters = frameCounters;
		}

		public void Dispose()
		{
			Bounds?.Dispose();
			SourceArguments?.Dispose();
			VisibleArguments?.Dispose();
			FrameCounters?.Dispose();
		}
	}

	private sealed class ReadbackSceneObject : SceneCustomObject
	{
		private readonly GpuVoxelMesher _owner;

		public ReadbackSceneObject( SceneWorld world, GpuVoxelMesher owner )
			: base( world )
		{
			_owner = owner;
		}

		public override void RenderSceneObject()
		{
			System.Threading.Interlocked.Increment( ref _owner._renderSequence );
			_owner.DispatchVisibility();
			_owner.ProcessReadbacks();
		}
	}

	private sealed class MeshResource : IDisposable
	{
		private readonly long _allocationId;
		private GpuBuffer<uint> _statistics;

		public GpuSdfDescriptor Descriptor { get; private set; }
		public int VisibilitySlot { get; }
		public GpuBuffer<uint> ActiveCells { get; }
		public GpuBuffer<GpuBuffer.IndirectDrawArguments> IndirectArguments { get; }
		public RenderAttributes DrawAttributes { get; } = new();

		public MeshResource( int capacity, long allocationId, int visibilitySlot )
		{
			_allocationId = allocationId;
			VisibilitySlot = visibilitySlot;
			ActiveCells = new GpuBuffer<uint>(
				capacity,
				GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Append,
				$"Voxel Active Cells {allocationId}" );
			IndirectArguments = new GpuBuffer<GpuBuffer.IndirectDrawArguments>(
				1,
				GpuBuffer.UsageFlags.IndirectDrawArguments,
				$"Voxel Indirect Arguments {allocationId}" );
			Span<GpuBuffer.IndirectDrawArguments> initialArguments = stackalloc GpuBuffer.IndirectDrawArguments[1];
			initialArguments[0] = new GpuBuffer.IndirectDrawArguments
			{
				VertexCount = VerticesPerActiveCell,
				InstanceCount = 0,
				FirstVertex = 0,
				FirstInstance = 0
			};
			IndirectArguments.SetData( initialArguments );
			DrawAttributes.Set( "ActiveCells", ActiveCells );
		}

		public void Prepare( GpuSdfDescriptor descriptor )
		{
			Descriptor = descriptor;
			var chunkWorldSize = descriptor.CellsPerAxis * descriptor.CellSize;
			var chunkWorldOrigin = new Vector3(
				descriptor.ChunkCoordinate.x * chunkWorldSize,
				descriptor.ChunkCoordinate.y * chunkWorldSize,
				descriptor.ChunkCoordinate.z * chunkWorldSize );

			DrawAttributes.Set( "ChunkCoordinate", descriptor.ChunkCoordinate );
			DrawAttributes.Set( "ChunkWorldOrigin", chunkWorldOrigin );
			DrawAttributes.Set( "CellsPerAxis", descriptor.CellsPerAxis );
			DrawAttributes.Set( "CellSize", descriptor.CellSize );
			DrawAttributes.Set( "SurfaceHeight", descriptor.SurfaceHeight );
		}

		public GpuBuffer<uint> EnsureStatistics()
		{
			_statistics ??= new GpuBuffer<uint>(
				3,
				GpuBuffer.UsageFlags.Structured,
				$"Voxel Mesh Diagnostics {_allocationId}" );
			return _statistics;
		}

		public void Dispose()
		{
			ActiveCells.Dispose();
			IndirectArguments.Dispose();
			_statistics?.Dispose();
		}
	}
}

internal readonly record struct GpuVisibilityMeasurement(
	uint FrameCount,
	uint ResidentTotal,
	uint VisibleTotal,
	uint MinimumVisible,
	uint MaximumVisible,
	long LogicalBufferBytes,
	long ScalarReadbacks )
{
	public float AverageResident => FrameCount > 0 ? (float)ResidentTotal / FrameCount : 0f;
	public float AverageVisible => FrameCount > 0 ? (float)VisibleTotal / FrameCount : 0f;
	public float AverageCulled => MathF.Max( 0f, AverageResident - AverageVisible );
	public float CulledPercent => AverageResident > 0f ? AverageCulled * 100f / AverageResident : 0f;
}
