using System;
using Sandbox.Rendering;

internal sealed class GpuVoxelMesher : IDisposable
{
	public const int VerticesPerActiveCell = 15;
	public const int MaximumDispatchesPerUpdate = 8;

	private readonly Scene _scene;
	private readonly ComputeShader _computeShader = new( "shaders/voxels/voxel_regular_mesher_cs.shader" );
	private readonly ComputeShader _visibilityShader = new( "shaders/voxels/voxel_chunk_visibility_cs.shader" );
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_terrain.shader" );
	private readonly Sandbox.Rendering.CommandList _meshCommands = new( "Voxel Terrain Meshing" );
	private readonly Sandbox.Rendering.CommandList _drawCommands = new( "Voxel Terrain Indirect Draws" );
	private readonly Dictionary<Vector3Int, MeshResource> _resident = new();
	private readonly Dictionary<Vector3Int, PendingMesh> _pending = new();
	private readonly Queue<PendingMesh> _gameplayDispatchQueue = new();
	private readonly Queue<PendingMesh> _warmDispatchQueue = new();
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
	private int _pendingGameplayCount;
	private int _pendingWarmCount;
	private int _warmResidentCount;
	private Vector4[] _visibilityBoundsData = Array.Empty<Vector4>();
	private VisibilityBuffers _visibilityBuffers;
	private readonly GpuBuffer<uint> _visibilityAggregateCounters = new(
		10,
		GpuBuffer.UsageFlags.Structured,
		"Voxel Visibility Aggregate Counters" );
	private bool _drawCommandsDirty;
	private bool _visibilityDescriptorsDirty;
	private bool _visibilityCountsNeedRefresh;
	private bool _visibilityMeasurementActive;
	private bool _visibilitySettledCaptureActive;
	private bool _visibilityReadbackPending;
	private bool _visibilityReadbackInFlight;
	private long _visibilityReadbackRequestedRenderSequence;
	private GpuVisibilityMeasurement? _completedVisibilityMeasurement;
	private long _dispatchCount;
	private long _poolAllocationCount;
	private long _poolReuseCount;
	private long _scalarReadbackCount;
	private long _visibilityScalarReadbackCount;
	private long _renderSequence;
	private long _submittedRenderSequence;

	public int ResidentCount => _resident.Count;
	public int PendingCount => PendingGameplayCount + PendingWarmCount;
	public int PendingGameplayCount => _pendingGameplayCount + CountInFlight( GpuMeshResidency.Gameplay );
	public int PendingWarmCount => _pendingWarmCount + CountInFlight( GpuMeshResidency.Warm );
	public int WarmResidentCount => _warmResidentCount;
	public int PoolCount => _pool.Count;
	public int AllocatedResourceCount => _allocatedResourceCount;
	public long DispatchCount => _dispatchCount;
	public long PoolAllocationCount => _poolAllocationCount;
	public long PoolReuseCount => _poolReuseCount;
	public long ScalarReadbackCount => _scalarReadbackCount;
	public long VisibilityScalarReadbackCount => _visibilityScalarReadbackCount;
	public const long GeometryReadbackCount = 0;
	public long LogicalCapacityBytes => (long)_resident.Count * _capacity * sizeof( uint );
	public long ReservedActiveCellCapacity => (long)_allocatedResourceCount * _capacity;
	public long ReservedActiveCellCapacityBytes => ReservedActiveCellCapacity * sizeof( uint );
	public long LogicalVisibilityBytes => _visibilityCapacity == 0
		? 0
		: (long)_visibilityCapacity * (sizeof( float ) * 8 + sizeof( uint ) * 8) + sizeof( uint ) * 15;

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

	public void Schedule(
		VoxelChunk chunk,
		int worldSeed,
		int generatorVersion,
		int sourceRevision,
		GpuMeshResidency residency = GpuMeshResidency.Gameplay )
	{
		if ( chunk.DensityClassification != ChunkDensityClassification.PotentiallySurfaceContaining )
		{
			Remove( chunk.Coordinate );
			return;
		}

		var descriptor = GpuSdfDescriptor.FromChunk(
			chunk,
			worldSeed,
			generatorVersion,
			sourceRevision );
		if ( _resident.TryGetValue( chunk.Coordinate, out var resident ) &&
			resident.Descriptor == descriptor )
		{
			if ( residency == GpuMeshResidency.Gameplay )
			{
				SetResidency( resident, residency );
			}
			return;
		}

		QueuePending( new PendingMesh( descriptor, residency ) );
	}

	public void SetResidency( Vector3Int coordinate, GpuMeshResidency residency )
	{
		if ( _resident.TryGetValue( coordinate, out var resource ) )
		{
			SetResidency( resource, residency );
			return;
		}

		if ( _pending.TryGetValue( coordinate, out var pending ) && pending.Residency != residency )
		{
			QueuePending( pending with { Residency = residency } );
			return;
		}

		for ( var index = 0; index < _inFlight.Count; index++ )
		{
			if ( _inFlight[index].Descriptor.ChunkCoordinate == coordinate )
			{
				_inFlight[index] = _inFlight[index] with { Residency = residency };
				return;
			}
		}
	}

	public void Remove( Vector3Int coordinate )
	{
		RemovePending( coordinate );
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
		if ( resource.Residency == GpuMeshResidency.Warm )
		{
			_warmResidentCount--;
		}
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
		AttachToMainCamera();
		FinalizeInFlight();
		if ( _inFlight.Count > 0 )
		{
			return 0;
		}

		_meshCommands.Reset();
		var maximumPotentialAcquisitions = Math.Min( maximumDispatches, _pending.Count );
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
		while ( processed < maximumDispatches && TryDequeuePending( out var pending ) )
		{
			var descriptor = pending.Descriptor;
			var resource = Acquire();
			resource.Prepare( descriptor, pending.Residency );
			_meshCommands.Attributes.Set( "ActiveCells", resource.ActiveCells );
			var chunkWorldSize = descriptor.CellsPerAxis * descriptor.CellSize;
			_meshCommands.Attributes.Set(
				"ChunkWorldOrigin",
				new Vector3(
					descriptor.ChunkCoordinate.x * chunkWorldSize,
					descriptor.ChunkCoordinate.y * chunkWorldSize,
					descriptor.ChunkCoordinate.z * chunkWorldSize ) );
			_meshCommands.Attributes.Set( "CellsPerAxis", descriptor.CellsPerAxis );
			_meshCommands.Attributes.Set( "CellSize", descriptor.CellSize );
			_meshCommands.Attributes.Set(
				"GeneratorIdentity",
				new Vector2( descriptor.WorldSeed, descriptor.GeneratorVersion ) );
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
			_inFlight.Add( new InFlightMesh( descriptor, pending.Residency, resource ) );
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
			if ( _cancelledInFlight.Remove( coordinate ) )
			{
				_pool.Push( completed.Resource );
				continue;
			}

			var residency = completed.Residency;
			if ( _pending.TryGetValue( coordinate, out var replacement ) )
			{
				if ( replacement.Descriptor != completed.Descriptor )
				{
					_pool.Push( completed.Resource );
					continue;
				}

				residency = replacement.Residency;
				RemovePending( coordinate );
			}

			if ( _resident.Remove( coordinate, out var previous ) )
			{
				SetVisibilityActive( previous, false );
				if ( previous.Residency == GpuMeshResidency.Warm )
				{
					_warmResidentCount--;
				}
				_pool.Push( previous );
			}

			completed.Resource.Residency = residency;
			_resident.Add( coordinate, completed.Resource );
			if ( residency == GpuMeshResidency.Warm )
			{
				_warmResidentCount++;
			}
			SetVisibilityActive( completed.Resource, true );
			_dispatchCount++;
			_drawCommandsDirty = true;
		}

		_inFlight.Clear();
	}

	private void QueuePending( PendingMesh pending )
	{
		RemovePending( pending.Descriptor.ChunkCoordinate );
		_pending[pending.Descriptor.ChunkCoordinate] = pending;
		if ( pending.Residency == GpuMeshResidency.Gameplay )
		{
			_pendingGameplayCount++;
			_gameplayDispatchQueue.Enqueue( pending );
		}
		else
		{
			_pendingWarmCount++;
			_warmDispatchQueue.Enqueue( pending );
		}
	}

	private bool TryDequeuePending( out PendingMesh pending )
	{
		while ( _gameplayDispatchQueue.TryDequeue( out pending ) )
		{
			if ( _pending.TryGetValue( pending.Descriptor.ChunkCoordinate, out var current ) &&
				current == pending )
			{
				RemovePending( pending.Descriptor.ChunkCoordinate );
				return true;
			}
		}

		while ( _warmDispatchQueue.TryDequeue( out pending ) )
		{
			if ( _pending.TryGetValue( pending.Descriptor.ChunkCoordinate, out var current ) &&
				current == pending )
			{
				RemovePending( pending.Descriptor.ChunkCoordinate );
				return true;
			}
		}

		pending = default;
		return false;
	}

	private bool RemovePending( Vector3Int coordinate )
	{
		if ( !_pending.Remove( coordinate, out var pending ) )
		{
			return false;
		}

		if ( pending.Residency == GpuMeshResidency.Gameplay )
		{
			_pendingGameplayCount--;
		}
		else
		{
			_pendingWarmCount--;
		}
		return true;
	}

	private int CountInFlight( GpuMeshResidency residency )
	{
		var count = 0;
		foreach ( var mesh in _inFlight )
		{
			if ( mesh.Residency == residency )
			{
				count++;
			}
		}
		return count;
	}

	private void SetResidency( MeshResource resource, GpuMeshResidency residency )
	{
		if ( resource.Residency == residency )
		{
			return;
		}

		if ( resource.Residency == GpuMeshResidency.Warm )
		{
			_warmResidentCount--;
		}
		if ( residency == GpuMeshResidency.Warm )
		{
			_warmResidentCount++;
		}

		resource.Residency = residency;
		SetVisibilityActive( resource, true );
	}

	public DrawCommandCommitResult CommitDrawCommands()
	{
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		UploadVisibilityDescriptors();
		if ( !_drawCommandsDirty )
		{
			return new DrawCommandCommitResult(
				false,
				(float)System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
		}

		_drawCommands.Reset();
		_drawOrder.Clear();
		if ( _visibilityCapacity == 0 )
		{
			_drawCommandsDirty = false;
			return new DrawCommandCommitResult(
				true,
				(float)System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
		}

		_drawCommands.Attributes.Set( "VisibilityBounds", _visibilityBuffers.Bounds );
		_drawCommands.Attributes.Set( "SourceIndirectArguments", _visibilityBuffers.SourceArguments );
		_drawCommands.Attributes.Set( "VisibleIndirectArguments", _visibilityBuffers.VisibleArguments );
		_drawCommands.Attributes.Set( "VisibilityFrameCounters", _visibilityBuffers.FrameCounters );
		_drawCommands.Attributes.Set( "VisibilityAggregateCounters", _visibilityAggregateCounters );
		_drawCommands.Attributes.Set( "VisibilitySlotCount", _visibilityCapacity );
		_drawCommands.Attributes.Set( "VisibilityPass", 0 );
		_drawCommands.Attributes.Set( "MeasureVisibility", _visibilityMeasurementActive ? 1 : 0 );
		_drawCommands.Attributes.Set( "CaptureSettledDiagnostics", _visibilitySettledCaptureActive ? 1 : 0 );
		_drawCommands.ResourceBarrierTransition( _visibilityBuffers.Bounds, ResourceState.GenericRead );
		_drawCommands.ResourceBarrierTransition( _visibilityBuffers.SourceArguments, ResourceState.GenericRead );
		_drawCommands.ResourceBarrierTransition( _visibilityBuffers.VisibleArguments, ResourceState.UnorderedAccess );
		_drawCommands.ResourceBarrierTransition( _visibilityBuffers.FrameCounters, ResourceState.UnorderedAccess );
		_drawCommands.Clear( _visibilityBuffers.FrameCounters, 0 );
		_drawCommands.DispatchCompute( _visibilityShader, _visibilityCapacity, 1, 1 );
		_drawCommands.UavBarrier( _visibilityBuffers.VisibleArguments );
		_drawCommands.UavBarrier( _visibilityBuffers.FrameCounters );

		if ( _visibilityMeasurementActive || _visibilitySettledCaptureActive )
		{
			_drawCommands.ResourceBarrierTransition( _visibilityBuffers.FrameCounters, ResourceState.GenericRead );
			_drawCommands.ResourceBarrierTransition( _visibilityAggregateCounters, ResourceState.UnorderedAccess );
			_drawCommands.Attributes.Set( "VisibilityPass", 1 );
			_drawCommands.DispatchCompute( _visibilityShader, 1, 1, 1 );
			_drawCommands.UavBarrier( _visibilityAggregateCounters );
		}

		_drawCommands.ResourceBarrierTransition( _visibilityBuffers.VisibleArguments, ResourceState.IndirectArgument );

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
		return new DrawCommandCommitResult(
			true,
			(float)System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
	}

	private void ProcessVisibilityReadback()
	{
		BeginVisibilityReadbackIfRequested();
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
			var frames = data.Length >= 10 ? data[0] : 0;
			var residentTotal = data.Length >= 10 ? data[1] : 0;
			var visibleTotal = data.Length >= 10 ? data[2] : 0;
			var minimumVisible = data.Length >= 10 && frames > 0 && data[3] != uint.MaxValue ? data[3] : 0;
			var maximumVisible = data.Length >= 10 ? data[4] : 0;
			var warmTotal = data.Length >= 10 ? data[5] : 0;
			var settledSurfaceMeshes = data.Length >= 10 ? data[6] : 0;
			var settledWarmSurfaceMeshes = data.Length >= 10 ? data[7] : 0;
			var settledActiveCells = data.Length >= 10 ? data[8] : 0;
			var settledMaximumActiveCells = data.Length >= 10 ? data[9] : 0;
			lock ( _visibilityLock )
			{
				_completedVisibilityMeasurement = new GpuVisibilityMeasurement(
					frames,
					residentTotal,
					visibleTotal,
					minimumVisible,
					maximumVisible,
					warmTotal,
					settledSurfaceMeshes,
					settledWarmSurfaceMeshes,
					settledActiveCells,
					settledMaximumActiveCells,
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

		Span<uint> initialCounters = stackalloc uint[10];
		initialCounters[3] = uint.MaxValue;
		_visibilityAggregateCounters.SetData( initialCounters );
		_visibilityMeasurementActive = true;
		_visibilitySettledCaptureActive = false;
		_drawCommandsDirty = true;
		CommitDrawCommands();
	}

	public void StopVisibilityMeasurement()
	{
		_visibilityMeasurementActive = false;
		_drawCommandsDirty = true;
		CommitDrawCommands();
	}

	public void CaptureSettledVisibilityMeasurement()
	{
		_visibilitySettledCaptureActive = true;
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
			_visibilitySettledCaptureActive = false;
			_drawCommandsDirty = true;
			CommitDrawCommands();
			return true;
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
			5,
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
		var activeResidency = resource.Residency == GpuMeshResidency.Warm ? 2f : 1f;
		_visibilityBoundsData[index] = new Vector4( minimum, active ? activeResidency : 0f );
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

	public void Clear()
	{
		_meshCommands.Reset();
		_pending.Clear();
		_gameplayDispatchQueue.Clear();
		_warmDispatchQueue.Clear();
		_pendingGameplayCount = 0;
		_pendingWarmCount = 0;
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
		_warmResidentCount = 0;
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

	private readonly record struct PendingMesh( GpuSdfDescriptor Descriptor, GpuMeshResidency Residency );
	private readonly record struct InFlightMesh(
		GpuSdfDescriptor Descriptor,
		GpuMeshResidency Residency,
		MeshResource Resource );

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
			_owner.ProcessVisibilityReadback();
		}
	}

	private sealed class MeshResource : IDisposable
	{
		private readonly long _allocationId;

		public GpuSdfDescriptor Descriptor { get; private set; }
		public GpuMeshResidency Residency { get; set; }
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

		public void Prepare( GpuSdfDescriptor descriptor, GpuMeshResidency residency )
		{
			Descriptor = descriptor;
			Residency = residency;
			var chunkWorldSize = descriptor.CellsPerAxis * descriptor.CellSize;
			var chunkWorldOrigin = new Vector3(
				descriptor.ChunkCoordinate.x * chunkWorldSize,
				descriptor.ChunkCoordinate.y * chunkWorldSize,
				descriptor.ChunkCoordinate.z * chunkWorldSize );

			DrawAttributes.Set( "ChunkWorldOrigin", chunkWorldOrigin );
			DrawAttributes.Set( "CellsPerAxis", descriptor.CellsPerAxis );
			DrawAttributes.Set( "CellSize", descriptor.CellSize );
			DrawAttributes.Set(
				"GeneratorIdentity",
				new Vector2( descriptor.WorldSeed, descriptor.GeneratorVersion ) );
		}

		public void Dispose()
		{
			ActiveCells.Dispose();
			IndirectArguments.Dispose();
		}
	}
}

internal readonly record struct DrawCommandCommitResult( bool Rebuilt, float Milliseconds );

internal readonly record struct GpuVisibilityMeasurement(
	uint FrameCount,
	uint ResidentTotal,
	uint VisibleTotal,
	uint MinimumVisible,
	uint MaximumVisible,
	uint WarmTotal,
	uint SettledSurfaceMeshes,
	uint SettledWarmSurfaceMeshes,
	uint SettledActiveCells,
	uint SettledMaximumActiveCells,
	long LogicalBufferBytes,
	long ScalarReadbacks )
{
	public float AverageResident => FrameCount > 0 ? (float)ResidentTotal / FrameCount : 0f;
	public float AverageVisible => FrameCount > 0 ? (float)VisibleTotal / FrameCount : 0f;
	public float AverageWarm => FrameCount > 0 ? (float)WarmTotal / FrameCount : 0f;
	public float AverageCulled => MathF.Max( 0f, AverageResident - AverageVisible );
	public float CulledPercent => AverageResident > 0f ? AverageCulled * 100f / AverageResident : 0f;
}

internal enum GpuMeshResidency
{
	Gameplay,
	Warm
}
