using System;
using System.Diagnostics;
using Sandbox.Rendering;

internal sealed class GpuVoxelMesher : IDisposable
{
	public const int VerticesPerActiveCell = 15;
	public const int MaximumDispatchesPerUpdate = 8;
	public const int RegionsPerSlab = 256;

	private const int SdfVectorsPerRegion = 3;
	private const int IndirectArgumentStride = sizeof( uint ) * 4;
	private const int MaximumScheduleLatencySamples = 524288;

	private readonly Scene _scene;
	private readonly ComputeShader _computeShader = new( "shaders/voxels/voxel_regular_mesher_cs.shader" );
	private readonly ComputeShader _argumentShader = new( "shaders/voxels/voxel_slab_arguments_cs.shader" );
	private readonly ComputeShader _visibilityShader = new( "shaders/voxels/voxel_chunk_visibility_cs.shader" );
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_terrain.shader" );
	private readonly Sandbox.Rendering.CommandList _drawCommands = new( "Voxel Terrain Indirect Draws" );
	private readonly Dictionary<Vector3Int, ResidentMesh> _resident = new();
	private readonly Dictionary<Vector3Int, PendingMesh> _pending = new();
	private readonly Queue<PendingMesh> _gameplayDispatchQueue = new();
	private readonly Queue<PendingMesh> _warmDispatchQueue = new();
	private readonly List<MeshSlab> _slabs = new();
	private readonly List<InFlightMesh> _inFlight = new();
	private readonly HashSet<Vector3Int> _cancelledInFlight = new();
	private readonly List<VisibilityBuffers> _retiredVisibilityBuffers = new();
	private readonly object _visibilityLock = new();
	private readonly ReadbackSceneObject _readbackObject;
	private readonly GpuBuffer<uint> _visibilityAggregateCounters = new(
		10,
		GpuBuffer.UsageFlags.Structured,
		"Voxel Visibility Aggregate Counters" );

	private CameraComponent _camera;
	private int _capacity;
	private int _visibilityCapacity;
	private int _pendingGameplayCount;
	private int _pendingWarmCount;
	private int _warmResidentCount;
	private Vector4[] _visibilityBoundsData = Array.Empty<Vector4>();
	private VisibilityBuffers _visibilityBuffers;
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
	private float[] _scheduleLatencyMilliseconds = Array.Empty<float>();
	private int _scheduleLatencySampleCount;
	private int _scheduleLatencyTruncatedCount;
	private int _scheduleLatencyCancelledCount;
	private int _scheduleLatencySupersededCount;
	private bool _scheduleLatencyMeasurementActive;

	public int ResidentCount => _resident.Count;
	public int PendingCount => PendingGameplayCount + PendingWarmCount;
	public int PendingGameplayCount => _pendingGameplayCount + CountInFlight( GpuMeshResidency.Gameplay );
	public int PendingWarmCount => _pendingWarmCount + CountInFlight( GpuMeshResidency.Warm );
	public int WarmResidentCount => _warmResidentCount;
	public int PoolCount => CountFreeSlots();
	public int AllocatedResourceCount => _slabs.Count * RegionsPerSlab;
	public long DispatchCount => _dispatchCount;
	public long PoolAllocationCount => _poolAllocationCount;
	public long PoolReuseCount => _poolReuseCount;
	public long ScalarReadbackCount => _scalarReadbackCount;
	public long VisibilityScalarReadbackCount => _visibilityScalarReadbackCount;
	public const long GeometryReadbackCount = 0;
	public int TerrainIndirectApiSubmissionCount => CountActiveSlabs();
	public int IndirectArgumentRecordCount => CountActiveSlabs() * RegionsPerSlab;
	public int TerrainBufferGroupCount => _slabs.Count;
	public long LogicalCapacityBytes => (long)_resident.Count * _capacity * sizeof( uint );
	public long ReservedActiveCellCapacity => (long)_slabs.Count * RegionsPerSlab * _capacity;
	public long ReservedActiveCellCapacityBytes => ReservedActiveCellCapacity * sizeof( uint );
	public long LogicalVisibilityBytes => _visibilityCapacity == 0
		? 0
		: (long)_visibilityCapacity * (sizeof( float ) * 8 + sizeof( uint ) * 8) + sizeof( uint ) * 15;

	public GpuVoxelMesher( Scene scene, int cellsPerAxis )
	{
		_scene = scene;
		_capacity = checked( cellsPerAxis * cellsPerAxis * cellsPerAxis );
		_readbackObject = new ReadbackSceneObject( scene.SceneWorld, this );
		Sandbox.Diagnostics.GpuProfilerStats.Enabled = true;
		AttachToMainCamera();
	}

	public void Schedule( VoxelChunk chunk, int sourceRevision,
		GpuMeshResidency residency = GpuMeshResidency.Gameplay )
	{
		if ( chunk.DensityClassification != ChunkDensityClassification.PotentiallySurfaceContaining )
		{
			Remove( chunk.Coordinate );
			return;
		}

		var descriptor = GpuSdfDescriptor.FromChunk( chunk, sourceRevision );
		if ( _resident.TryGetValue( chunk.Coordinate, out var resident ) &&
			resident.Descriptor == descriptor )
		{
			if ( residency == GpuMeshResidency.Gameplay )
			{
				SetResidency( resident, residency );
			}
			return;
		}
		QueuePending( new PendingMesh( descriptor, residency, Stopwatch.GetTimestamp() ) );
	}

	public void BeginScheduleLatencyMeasurement()
	{
		_scheduleLatencyMilliseconds = new float[MaximumScheduleLatencySamples];
		_scheduleLatencySampleCount = 0;
		_scheduleLatencyTruncatedCount = 0;
		_scheduleLatencyCancelledCount = 0;
		_scheduleLatencySupersededCount = 0;
		_scheduleLatencyMeasurementActive = true;
	}

	public GpuMeshScheduleLatencyMeasurement CompleteScheduleLatencyMeasurement()
	{
		_scheduleLatencyMeasurementActive = false;
		Array.Sort( _scheduleLatencyMilliseconds, 0, _scheduleLatencySampleCount );
		var result = new GpuMeshScheduleLatencyMeasurement(
			_scheduleLatencySampleCount,
			_scheduleLatencyTruncatedCount,
			GetScheduleLatencyPercentile( 0.50d ),
			GetScheduleLatencyPercentile( 0.95d ),
			GetScheduleLatencyPercentile( 0.99d ),
			_scheduleLatencySampleCount > 0
				? _scheduleLatencyMilliseconds[_scheduleLatencySampleCount - 1]
				: 0f,
			_scheduleLatencyCancelledCount,
			_scheduleLatencySupersededCount );
		_scheduleLatencyMilliseconds = Array.Empty<float>();
		return result;
	}

	private float GetScheduleLatencyPercentile( double percentile )
	{
		if ( _scheduleLatencySampleCount == 0 )
		{
			return 0f;
		}
		var index = Math.Clamp(
			(int)Math.Ceiling( _scheduleLatencySampleCount * percentile ) - 1,
			0,
			_scheduleLatencySampleCount - 1 );
		return _scheduleLatencyMilliseconds[index];
	}

	public void SetResidency( Vector3Int coordinate, GpuMeshResidency residency )
	{
		if ( _resident.TryGetValue( coordinate, out var resident ) )
		{
			SetResidency( resident, residency );
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
		if ( !_resident.Remove( coordinate, out var resident ) )
		{
			return;
		}
		SetVisibilityActive( resident, false );
		if ( resident.Residency == GpuMeshResidency.Warm )
		{
			_warmResidentCount--;
		}
		resident.Handle.Slab.ActiveResidentCount--;
		Release( resident.Handle );
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
		DisposeSlabs();
		DisposeVisibilityBuffers();
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
		foreach ( var slab in _slabs )
		{
			slab.ResetRecordedCommands();
			slab.PendingJobs.Clear();
		}

		var processed = 0;
		while ( processed < maximumDispatches && TryDequeuePending( out var pending ) )
		{
			var handle = Acquire();
			var inFlight = new InFlightMesh(
				pending.Descriptor,
				pending.Residency,
				handle,
				pending.ScheduledTimestamp );
			handle.Slab.Prepare( handle.Slot, pending.Descriptor );
			handle.Slab.PendingJobs.Add( inFlight );
			_inFlight.Add( inFlight );
			processed++;
		}
		if ( processed == 0 && !_visibilityCountsNeedRefresh )
		{
			CommitDrawCommands();
			return 0;
		}

		foreach ( var slab in _slabs )
		{
			if ( slab.PendingJobs.Count > 0 || _visibilityCountsNeedRefresh )
			{
				RecordSlabMeshing( slab );
			}
		}
		_visibilityCountsNeedRefresh = false;
		if ( processed > 0 )
		{
			_submittedRenderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
		}
		CommitDrawCommands();
		return processed;
	}

	private void RecordSlabMeshing( MeshSlab slab )
	{
		var jobCount = slab.PendingJobs.Count;
		if ( jobCount > 0 )
		{
			Span<uint> slots = stackalloc uint[MaximumDispatchesPerUpdate];
			Span<uint> zero = stackalloc uint[1];
			for ( var index = 0; index < jobCount; index++ )
			{
				slots[index] = (uint)slab.PendingJobs[index].Handle.Slot;
				slab.ActiveCellCounts.SetData( zero, slab.PendingJobs[index].Handle.Slot );
			}
			slab.MeshingSlots.SetData( slots[..jobCount] );
			slab.SetMeshingJobCount( jobCount );
			slab.MeshCommands.ResourceBarrierTransition( slab.ActiveCells, ResourceState.UnorderedAccess );
			slab.MeshCommands.ResourceBarrierTransition(
				slab.ActiveCellCounts, ResourceState.UnorderedAccess );
			var cells = slab.PendingJobs[0].Descriptor.CellsPerAxis;
			slab.MeshCommands.DispatchCompute( _computeShader, cells, cells, cells * jobCount );
			slab.MeshCommands.UavBarrier( slab.ActiveCells );
			slab.MeshCommands.UavBarrier( slab.ActiveCellCounts );
			slab.MeshCommands.ResourceBarrierTransition( slab.ActiveCells, ResourceState.GenericRead );
			slab.MeshCommands.ResourceBarrierTransition( slab.ActiveCellCounts, ResourceState.GenericRead );
		}
		slab.ArgumentCommands.ResourceBarrierTransition(
			_visibilityBuffers.SourceArguments, ResourceState.UnorderedAccess );
		slab.ArgumentCommands.DispatchCompute( _argumentShader, RegionsPerSlab, 1, 1 );
		slab.ArgumentCommands.UavBarrier( _visibilityBuffers.SourceArguments );
		slab.ArgumentCommands.ResourceBarrierTransition(
			_visibilityBuffers.SourceArguments, ResourceState.GenericRead );
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
				if ( _scheduleLatencyMeasurementActive )
				{
					_scheduleLatencyCancelledCount++;
				}
				Release( completed.Handle );
				continue;
			}
			var residency = completed.Residency;
			if ( _pending.TryGetValue( coordinate, out var replacement ) )
			{
				if ( replacement.Descriptor != completed.Descriptor )
				{
					if ( _scheduleLatencyMeasurementActive )
					{
						_scheduleLatencySupersededCount++;
					}
					Release( completed.Handle );
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
				previous.Handle.Slab.ActiveResidentCount--;
				Release( previous.Handle );
			}
			var resident = new ResidentMesh( completed.Descriptor, residency, completed.Handle );
			_resident.Add( coordinate, resident );
			completed.Handle.Slab.ActiveResidentCount++;
			if ( residency == GpuMeshResidency.Warm )
			{
				_warmResidentCount++;
			}
			SetVisibilityActive( resident, true );
			if ( _scheduleLatencyMeasurementActive )
			{
				var milliseconds = (float)Stopwatch.GetElapsedTime(
					completed.ScheduledTimestamp ).TotalMilliseconds;
				if ( _scheduleLatencySampleCount < _scheduleLatencyMilliseconds.Length )
				{
					_scheduleLatencyMilliseconds[_scheduleLatencySampleCount++] = milliseconds;
				}
				else
				{
					_scheduleLatencyTruncatedCount++;
				}
			}
			_dispatchCount++;
			_drawCommandsDirty = true;
		}
		_inFlight.Clear();
	}

	private void QueuePending( PendingMesh pending )
	{
		if ( _scheduleLatencyMeasurementActive &&
			_pending.ContainsKey( pending.Descriptor.ChunkCoordinate ) )
		{
			_scheduleLatencySupersededCount++;
		}
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
		while ( _gameplayDispatchQueue.TryDequeue( out var gameplay ) )
		{
			if ( _pending.TryGetValue( gameplay.Descriptor.ChunkCoordinate, out var current ) &&
				current == gameplay )
			{
				_pending.Remove( gameplay.Descriptor.ChunkCoordinate );
				_pendingGameplayCount--;
				pending = gameplay;
				return true;
			}
		}
		while ( _warmDispatchQueue.TryDequeue( out var warm ) )
		{
			if ( _pending.TryGetValue( warm.Descriptor.ChunkCoordinate, out var current ) && current == warm )
			{
				_pending.Remove( warm.Descriptor.ChunkCoordinate );
				_pendingWarmCount--;
				pending = warm;
				return true;
			}
		}
		pending = default;
		return false;
	}

	private void RemovePending( Vector3Int coordinate )
	{
		if ( !_pending.Remove( coordinate, out var pending ) )
		{
			return;
		}
		if ( pending.Residency == GpuMeshResidency.Gameplay )
		{
			_pendingGameplayCount--;
		}
		else
		{
			_pendingWarmCount--;
		}
	}

	private int CountInFlight( GpuMeshResidency residency )
	{
		var count = 0;
		foreach ( var inFlight in _inFlight )
		{
			count += inFlight.Residency == residency ? 1 : 0;
		}
		return count;
	}

	private void SetResidency( ResidentMesh resident, GpuMeshResidency residency )
	{
		if ( resident.Residency == residency )
		{
			return;
		}
		if ( resident.Residency == GpuMeshResidency.Warm )
		{
			_warmResidentCount--;
		}
		if ( residency == GpuMeshResidency.Warm )
		{
			_warmResidentCount++;
		}
		resident.Residency = residency;
		SetVisibilityActive( resident, true );
	}

	public DrawCommandCommitResult CommitDrawCommands()
	{
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		UploadVisibilityDescriptors();
		if ( !_drawCommandsDirty )
		{
			return new DrawCommandCommitResult( false,
				(float)System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
		}
		_drawCommands.Reset();
		if ( _visibilityCapacity == 0 )
		{
			_drawCommandsDirty = false;
			return new DrawCommandCommitResult( true,
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
		_drawCommands.ResourceBarrierTransition( _visibilityBuffers.SourceArguments, ResourceState.IndirectArgument );
		foreach ( var slab in _slabs )
		{
			if ( slab.ActiveResidentCount == 0 )
			{
				continue;
			}
			_drawCommands.DrawInstancedIndirect(
				_material,
				_visibilityBuffers.VisibleArguments,
				(uint)(slab.Index * RegionsPerSlab),
				slab.DrawAttributes,
				Graphics.PrimitiveType.Triangles,
				RegionsPerSlab,
				IndirectArgumentStride );
		}
		_drawCommandsDirty = false;
		return new DrawCommandCommitResult( true,
			(float)System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
	}

	public void BeginVisibilityMeasurement()
	{
		lock ( _visibilityLock )
		{
			_completedVisibilityMeasurement = null;
			_visibilityReadbackPending = false;
			_visibilityReadbackInFlight = false;
		}
		Span<uint> counters = stackalloc uint[10];
		counters[3] = uint.MaxValue;
		_visibilityAggregateCounters.SetData( counters );
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

	private void ProcessVisibilityReadback()
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
			var minimumVisible = data.Length >= 10 && frames > 0 && data[3] != uint.MaxValue ? data[3] : 0;
			lock ( _visibilityLock )
			{
				_completedVisibilityMeasurement = new GpuVisibilityMeasurement(
					frames,
					data.Length >= 10 ? data[1] : 0,
					data.Length >= 10 ? data[2] : 0,
					minimumVisible,
					data.Length >= 10 ? data[4] : 0,
					data.Length >= 10 ? data[5] : 0,
					data.Length >= 10 ? data[6] : 0,
					data.Length >= 10 ? data[7] : 0,
					data.Length >= 10 ? data[8] : 0,
					data.Length >= 10 ? data[9] : 0,
					logicalBytes,
					1 );
				_visibilityReadbackInFlight = false;
			}
		} );
	}

	private void EnsureVisibilityCapacity( int requiredCapacity )
	{
		if ( requiredCapacity <= _visibilityCapacity )
		{
			return;
		}
		var newCapacity = Math.Max( RegionsPerSlab, _visibilityCapacity );
		while ( newCapacity < requiredCapacity )
		{
			newCapacity = checked( newCapacity * 2 );
		}
		if ( _visibilityBuffers is not null )
		{
			_retiredVisibilityBuffers.Add( _visibilityBuffers );
		}
		_visibilityCapacity = newCapacity;
		_visibilityBoundsData = new Vector4[newCapacity * 2];
		var bounds = new GpuBuffer<Vector4>( newCapacity * 2, GpuBuffer.UsageFlags.Structured,
			"Voxel Visibility Bounds" );
		var source = new GpuBuffer<GpuBuffer.IndirectDrawArguments>( newCapacity,
			GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments,
			"Voxel Source Indirect Arguments" );
		var visible = new GpuBuffer<GpuBuffer.IndirectDrawArguments>( newCapacity,
			GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments,
			"Voxel Visible Indirect Arguments" );
		var frame = new GpuBuffer<uint>( 5, GpuBuffer.UsageFlags.Structured,
			"Voxel Visibility Frame Counters" );
		var initial = new GpuBuffer.IndirectDrawArguments[newCapacity];
		for ( var index = 0; index < initial.Length; index++ )
		{
			initial[index].VertexCount = VerticesPerActiveCell;
			initial[index].FirstVertex = (uint)((index % RegionsPerSlab) * VerticesPerActiveCell);
		}
		source.SetData( initial );
		visible.SetData( initial );
		_visibilityBuffers = new VisibilityBuffers( bounds, source, visible, frame );
		foreach ( var resident in _resident.Values )
		{
			WriteVisibilityBounds( resident, true );
		}
		foreach ( var slab in _slabs )
		{
			slab.BindSourceArguments( source );
		}
		_visibilityDescriptorsDirty = true;
		_visibilityCountsNeedRefresh = true;
		_drawCommandsDirty = true;
	}

	private void SetVisibilityActive( ResidentMesh resident, bool active )
	{
		WriteVisibilityBounds( resident, active );
		_visibilityDescriptorsDirty = true;
	}

	private void WriteVisibilityBounds( ResidentMesh resident, bool active )
	{
		var descriptor = resident.Descriptor;
		var size = descriptor.CellsPerAxis * descriptor.CellSize;
		var origin = new Vector3(
			descriptor.ChunkCoordinate.x * size,
			descriptor.ChunkCoordinate.y * size,
			descriptor.ChunkCoordinate.z * size );
		var padding = new Vector3( descriptor.CellSize );
		var index = resident.Handle.GlobalSlot * 2;
		var activeResidency = resident.Residency == GpuMeshResidency.Warm ? 2f : 1f;
		_visibilityBoundsData[index] = new Vector4( origin - padding, active ? activeResidency : 0f );
		_visibilityBoundsData[index + 1] = new Vector4( origin + new Vector3( size ) + padding, 0f );
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

	private MeshHandle Acquire()
	{
		foreach ( var slab in _slabs )
		{
			if ( slab.TryAcquire( out var reused ) )
			{
				_poolReuseCount++;
				return reused;
			}
		}
		var created = new MeshSlab( _slabs.Count, _capacity );
		_slabs.Add( created );
		_poolAllocationCount++;
		EnsureVisibilityCapacity( _slabs.Count * RegionsPerSlab );
		created.BindSourceArguments( _visibilityBuffers.SourceArguments );
		created.Attach( _camera );
		if ( !created.TryAcquire( out var allocated ) )
		{
			throw new InvalidOperationException( "A new voxel mesh slab has no free slot." );
		}
		return allocated;
	}

	private void Release( MeshHandle handle ) => handle.Slab.Release( handle );

	private int CountFreeSlots()
	{
		var count = 0;
		foreach ( var slab in _slabs )
		{
			count += slab.FreeCount;
		}
		return count;
	}

	private int CountActiveSlabs()
	{
		var count = 0;
		foreach ( var slab in _slabs )
		{
			count += slab.ActiveResidentCount > 0 ? 1 : 0;
		}
		return count;
	}

	public void Clear()
	{
		if ( _scheduleLatencyMeasurementActive )
		{
			_scheduleLatencyCancelledCount += _pending.Count + _inFlight.Count;
		}
		_pending.Clear();
		_gameplayDispatchQueue.Clear();
		_warmDispatchQueue.Clear();
		_pendingGameplayCount = 0;
		_pendingWarmCount = 0;
		_cancelledInFlight.Clear();
		foreach ( var inFlight in _inFlight )
		{
			Release( inFlight.Handle );
		}
		_inFlight.Clear();
		foreach ( var resident in _resident.Values )
		{
			SetVisibilityActive( resident, false );
			resident.Handle.Slab.ActiveResidentCount--;
			Release( resident.Handle );
		}
		_resident.Clear();
		_warmResidentCount = 0;
		foreach ( var slab in _slabs )
		{
			slab.ResetRecordedCommands();
		}
		_drawCommandsDirty = true;
		CommitDrawCommands();
	}

	public void Dispose()
	{
		Clear();
		if ( _camera is not null )
		{
			foreach ( var slab in _slabs )
			{
				slab.Detach( _camera );
			}
			_camera.RemoveCommandList( _drawCommands );
			_camera = null;
		}
		DisposeSlabs();
		DisposeVisibilityBuffers();
		_visibilityAggregateCounters.Dispose();
		_readbackObject?.Delete();
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
		if ( _camera is not null )
		{
			foreach ( var slab in _slabs )
			{
				slab.Detach( _camera );
			}
			_camera.RemoveCommandList( _drawCommands );
		}
		_camera = selected;
		if ( _camera is not null )
		{
			foreach ( var slab in _slabs )
			{
				slab.Attach( _camera );
			}
			_camera.AddCommandList( _drawCommands, Sandbox.Rendering.Stage.AfterOpaque, 0 );
		}
	}

	private void DisposeSlabs()
	{
		foreach ( var slab in _slabs )
		{
			slab.Detach( _camera );
			slab.Dispose();
		}
		_slabs.Clear();
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

	private sealed class ResidentMesh
	{
		public GpuSdfDescriptor Descriptor { get; }
		public GpuMeshResidency Residency { get; set; }
		public MeshHandle Handle { get; }

		public ResidentMesh( GpuSdfDescriptor descriptor, GpuMeshResidency residency, MeshHandle handle )
		{
			Descriptor = descriptor;
			Residency = residency;
			Handle = handle;
		}
	}

	private readonly record struct PendingMesh(
		GpuSdfDescriptor Descriptor,
		GpuMeshResidency Residency,
		long ScheduledTimestamp );
	private readonly record struct InFlightMesh(
		GpuSdfDescriptor Descriptor,
		GpuMeshResidency Residency,
		MeshHandle Handle,
		long ScheduledTimestamp );
	private readonly record struct MeshHandle( MeshSlab Slab, int Slot, uint Generation )
	{
		public int GlobalSlot => Slab.Index * RegionsPerSlab + Slot;
	}

	private sealed class MeshSlab : IDisposable
	{
		private readonly int _capacity;
		private readonly bool[] _occupied = new bool[RegionsPerSlab];
		private readonly uint[] _generations = new uint[RegionsPerSlab];
		private readonly Stack<int> _free = new( RegionsPerSlab );
		private GpuBuffer<GpuBuffer.IndirectDrawArguments> _sourceArguments;

		public int Index { get; }
		public int ActiveResidentCount { get; set; }
		public int FreeCount => _free.Count;
		public Sandbox.Rendering.CommandList MeshCommands { get; }
		public Sandbox.Rendering.CommandList ArgumentCommands { get; }
		public GpuBuffer<uint> ActiveCells { get; }
		public GpuBuffer<uint> ActiveCellCounts { get; }
		public GpuBuffer<Vector4> SdfParameters { get; }
		public GpuBuffer<uint> MeshingSlots { get; }
		public RenderAttributes DrawAttributes { get; } = new();
		public List<InFlightMesh> PendingJobs { get; } = new( MaximumDispatchesPerUpdate );

		public MeshSlab( int index, int capacity )
		{
			Index = index;
			_capacity = capacity;
			MeshCommands = new Sandbox.Rendering.CommandList( $"Voxel Terrain Meshing Slab {index}" );
			ArgumentCommands = new Sandbox.Rendering.CommandList( $"Voxel Terrain Arguments Slab {index}" );
			ActiveCells = new GpuBuffer<uint>( checked( RegionsPerSlab * capacity ),
				GpuBuffer.UsageFlags.Structured, $"Voxel Slab Active Cells {index}" );
			ActiveCellCounts = new GpuBuffer<uint>( RegionsPerSlab,
				GpuBuffer.UsageFlags.Structured, $"Voxel Slab Active Cell Counts {index}" );
			SdfParameters = new GpuBuffer<Vector4>( RegionsPerSlab * SdfVectorsPerRegion,
				GpuBuffer.UsageFlags.Structured, $"Voxel Slab SDF Parameters {index}" );
			MeshingSlots = new GpuBuffer<uint>( MaximumDispatchesPerUpdate,
				GpuBuffer.UsageFlags.Structured, $"Voxel Slab Meshing Slots {index}" );
			for ( var slot = RegionsPerSlab - 1; slot >= 0; slot-- )
			{
				_free.Push( slot );
			}
			ResetRecordedCommands();
			DrawAttributes.Set( "SlabActiveCells", ActiveCells );
			DrawAttributes.Set( "SlabSdfParameters", SdfParameters );
			DrawAttributes.Set( "SlabRegionCapacity", capacity );
		}

		public void BindSourceArguments( GpuBuffer<GpuBuffer.IndirectDrawArguments> source )
		{
			_sourceArguments = source;
			ArgumentCommands.Attributes.Set( "SourceIndirectArguments", source );
		}

		public void ResetRecordedCommands()
		{
			MeshCommands.Reset();
			ArgumentCommands.Reset();
			MeshCommands.Attributes.Set( "SlabActiveCells", ActiveCells );
			MeshCommands.Attributes.Set( "SlabActiveCellCounts", ActiveCellCounts );
			MeshCommands.Attributes.Set( "SlabSdfParameters", SdfParameters );
			MeshCommands.Attributes.Set( "MeshingSlots", MeshingSlots );
			MeshCommands.Attributes.Set( "SlabRegionCapacity", _capacity );
			ArgumentCommands.Attributes.Set( "SlabActiveCellCounts", ActiveCellCounts );
			ArgumentCommands.Attributes.Set( "SlabRegionCapacity", _capacity );
			ArgumentCommands.Attributes.Set( "SlabGlobalSlotOffset", Index * RegionsPerSlab );
			if ( _sourceArguments is not null )
			{
				ArgumentCommands.Attributes.Set( "SourceIndirectArguments", _sourceArguments );
			}
		}

		public void SetMeshingJobCount( int count )
		{
			MeshCommands.Attributes.Set( "MeshingJobCount", count );
		}

		public void Attach( CameraComponent camera )
		{
			if ( camera is null )
				return;
			camera.AddCommandList( MeshCommands, Sandbox.Rendering.Stage.AfterDepthPrepass, -101 );
			camera.AddCommandList( ArgumentCommands, Sandbox.Rendering.Stage.AfterDepthPrepass, -100 );
		}

		public void Detach( CameraComponent camera )
		{
			if ( camera is null )
				return;
			camera.RemoveCommandList( MeshCommands );
			camera.RemoveCommandList( ArgumentCommands );
		}

		public bool TryAcquire( out MeshHandle handle )
		{
			if ( !_free.TryPop( out var slot ) )
			{
				handle = default;
				return false;
			}
			_occupied[slot] = true;
			_generations[slot]++;
			handle = new MeshHandle( this, slot, _generations[slot] );
			return true;
		}

		public void Release( MeshHandle handle )
		{
			if ( handle.Slab != this || handle.Slot < 0 || handle.Slot >= RegionsPerSlab ||
				!_occupied[handle.Slot] || _generations[handle.Slot] != handle.Generation )
			{
				throw new InvalidOperationException( "Invalid or stale voxel slab handle release." );
			}
			_occupied[handle.Slot] = false;
			_free.Push( handle.Slot );
		}

		public void Prepare( int slot, GpuSdfDescriptor descriptor )
		{
			var size = descriptor.CellsPerAxis * descriptor.CellSize;
			var origin = new Vector3(
				descriptor.ChunkCoordinate.x * size,
				descriptor.ChunkCoordinate.y * size,
				descriptor.ChunkCoordinate.z * size );
			Span<Vector4> parameters = stackalloc Vector4[SdfVectorsPerRegion];
			parameters[0] = new Vector4( origin, descriptor.CellSize );
			parameters[1] = new Vector4(
				descriptor.TerrainSettings.WorldSeed,
				descriptor.TerrainSettings.SurfaceBaseHeight,
				descriptor.TerrainSettings.SurfaceFrequency,
				descriptor.TerrainSettings.SurfaceAmplitude );
			parameters[2] = new Vector4( descriptor.CellsPerAxis, 0f, 0f, 0f );
			SdfParameters.SetData( parameters, slot * SdfVectorsPerRegion );
		}

		public void Dispose()
		{
			ActiveCells.Dispose();
			ActiveCellCounts.Dispose();
			SdfParameters.Dispose();
			MeshingSlots.Dispose();
		}
	}

	private sealed class VisibilityBuffers : IDisposable
	{
		public GpuBuffer<Vector4> Bounds { get; }
		public GpuBuffer<GpuBuffer.IndirectDrawArguments> SourceArguments { get; }
		public GpuBuffer<GpuBuffer.IndirectDrawArguments> VisibleArguments { get; }
		public GpuBuffer<uint> FrameCounters { get; }

		public VisibilityBuffers( GpuBuffer<Vector4> bounds,
			GpuBuffer<GpuBuffer.IndirectDrawArguments> source,
			GpuBuffer<GpuBuffer.IndirectDrawArguments> visible,
			GpuBuffer<uint> frame )
		{
			Bounds = bounds;
			SourceArguments = source;
			VisibleArguments = visible;
			FrameCounters = frame;
		}

		public void Dispose()
		{
			Bounds.Dispose();
			SourceArguments.Dispose();
			VisibleArguments.Dispose();
			FrameCounters.Dispose();
		}
	}

	private sealed class ReadbackSceneObject : SceneCustomObject
	{
		private readonly GpuVoxelMesher _owner;

		public ReadbackSceneObject( SceneWorld world, GpuVoxelMesher owner ) : base( world )
		{
			_owner = owner;
		}

		public override void RenderSceneObject()
		{
			System.Threading.Interlocked.Increment( ref _owner._renderSequence );
			_owner.ProcessVisibilityReadback();
		}
	}
}

internal readonly record struct DrawCommandCommitResult( bool Rebuilt, float Milliseconds );

internal readonly record struct GpuMeshScheduleLatencyMeasurement(
	int Samples,
	int TruncatedSamples,
	float P50Milliseconds,
	float P95Milliseconds,
	float P99Milliseconds,
	float MaximumMilliseconds,
	int Cancelled,
	int Superseded );

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
