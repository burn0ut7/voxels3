using System;
using System.Threading.Tasks;

internal sealed class GpuVoxelMesher : IDisposable
{
	public const int VerticesPerActiveCell = 15;
	public const int MaximumDispatchesPerUpdate = 8;

	private readonly Scene _scene;
	private readonly ComputeShader _computeShader = new( "shaders/voxels/voxel_regular_mesher_cs.shader" );
	private readonly ComputeShader _diagnosticShader = new( "shaders/voxels/voxel_mesh_diagnostics_cs.shader" );
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
	private readonly Stack<MeshResource> _pool = new();
	private readonly List<MeshResource> _drawOrder = new();
	private readonly List<InFlightMesh> _inFlight = new();
	private readonly HashSet<Vector3Int> _cancelledInFlight = new();
	private readonly ReadbackSceneObject _readbackObject;

	private CameraComponent _camera;
	private int _capacity;
	private bool _drawCommandsDirty;
	private long _dispatchCount;
	private long _poolAllocationCount;
	private long _poolReuseCount;
	private long _scalarReadbackCount;
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
	public const long GeometryReadbackCount = 0;
	public long LogicalCapacityBytes => (long)_resident.Count * _capacity * sizeof( uint );

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
			_meshCommands.SetCounterValue( resource.ActiveCells, 0 );
			_meshCommands.DispatchCompute(
				_computeShader,
				descriptor.CellsPerAxis,
				descriptor.CellsPerAxis,
				descriptor.CellsPerAxis );
			_meshCommands.CopyStructureCount( resource.ActiveCells, resource.IndirectArguments, sizeof( uint ) );
			_inFlight.Add( new InFlightMesh( descriptor, resource ) );
			processed++;
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
				_pool.Push( previous );
			}

			_resident.Add( coordinate, completed.Resource );
			_dispatchCount++;
			_drawCommandsDirty = true;
		}

		_inFlight.Clear();
	}

	public void CommitDrawCommands()
	{
		if ( !_drawCommandsDirty )
		{
			return;
		}

		_drawCommands.Reset();
		_drawOrder.Clear();
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
				resource.IndirectArguments,
				0,
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
		return new MeshResource( _capacity, _poolAllocationCount );
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

		_camera?.RemoveCommandList( _drawCommands );
		_camera?.RemoveCommandList( _meshCommands );
		_camera = selected;
		_camera?.AddCommandList( _meshCommands, Sandbox.Rendering.Stage.AfterDepthPrepass, -100 );
		_camera?.AddCommandList( _drawCommands, Sandbox.Rendering.Stage.AfterDepthPrepass, 0 );
	}

	private void DisposePool()
	{
		while ( _pool.TryPop( out var resource ) )
		{
			resource.Dispose();
		}
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
			_owner.ProcessReadbacks();
		}
	}

	private sealed class MeshResource : IDisposable
	{
		private readonly long _allocationId;
		private GpuBuffer<uint> _statistics;

		public GpuSdfDescriptor Descriptor { get; private set; }
		public GpuBuffer<uint> ActiveCells { get; }
		public GpuBuffer<GpuBuffer.IndirectDrawArguments> IndirectArguments { get; }
		public RenderAttributes DrawAttributes { get; } = new();

		public MeshResource( int capacity, long allocationId )
		{
			_allocationId = allocationId;
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
