using System;
using System.Diagnostics;
using Sandbox.Rendering;

internal sealed class GpuVoxelMesher : IDisposable
{
	// Persistent geometry is disposable revisioned cache state; the SDF remains canonical.
	public const int MaximumRegionsPerBatch = 8;
	public const int MaximumDispatchesPerUpdate = MaximumRegionsPerBatch;
	public const int ScratchLaneCount = 3;
	public const int RegionsPerSlab = 256;
	public const int TerrainVertexBytes = 24;
	private const int VertexArenaBytes = 32 * 1024 * 1024;
	private const int IndexArenaBytes = 16 * 1024 * 1024;
	private const int VertexArenaCapacity = VertexArenaBytes / TerrainVertexBytes;
	private const int IndexArenaCapacity = IndexArenaBytes / sizeof( uint );
	private const int IndirectArgumentStride = sizeof( uint ) * 5;
	private const int MaximumScheduleLatencySamples = 524288;
	private const int MaximumThroughputBatchSamples = 65536;
	private const double Lod2MaximumServiceDelayMilliseconds = 250.0;

	private readonly Scene _scene;
	private readonly ComputeShader _visibilityShader = new( "shaders/voxels/voxel_chunk_visibility_cs.shader" );
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_terrain.shader" );
	private readonly Dictionary<GpuMeshRegionKey, ResidentMesh> _resident = new();
	private readonly Dictionary<GpuMeshRegionKey, PendingMesh> _pending = new();
	private readonly Queue<PendingMesh> _gameplayDispatchQueue = new();
	private readonly Queue<PendingMesh> _lod1DispatchQueue = new();
	private readonly Queue<PendingMesh> _warmDispatchQueue = new();
	private readonly Queue<PendingMesh> _lod2DispatchQueue = new();
	private readonly List<GeometryArena> _arenas = new();
	private ScratchLane[] _scratchLanes;
	private ScratchLane _lod2ScratchLane;
	private readonly HashSet<GpuMeshRegionKey> _cancelledInFlight = new();
	private readonly HashSet<GpuMeshRegionKey> _renderActive = new();
	private readonly HashSet<CameraComponent> _currentRenderCameras = new();
	private readonly HashSet<CameraComponent> _leavingRenderCameras = new();
	private readonly Dictionary<CameraComponent, RenderCameraState> _renderCameraStates = new();
	private readonly List<RenderCameraState> _retiredRenderCameraStates = new();
	private readonly object _renderCameraLock = new();
	private readonly Dictionary<Lod0Lod1TransitionKey, ResidentTransition> _transitionResident = new();
	private readonly Dictionary<Lod0Lod1TransitionKey, GpuTransitionDescriptor> _transitionDesiredDescriptors = new();
	private readonly Dictionary<Lod0Lod1TransitionKey, PendingTransition> _transitionPending = new();
	private readonly Queue<PendingTransition> _transitionDispatchQueue = new();
	private readonly HashSet<uint> _transitionCancelledGenerations = new();
	private readonly HashSet<Lod0Lod1TransitionKey> _transitionRenderActive = new();
	private TransitionScratchLane[] _transitionScratchLanes;
	private readonly object _visibilityLock = new();
	private readonly object _visibilityDescriptorLock = new();
	private readonly ReadbackSceneObject _readbackObject;
	private CameraComponent _camera;
	private RenderCameraState _visibilityReadbackState;
	private int _cellsPerAxis;
	private int _visibilityCapacity;
	private int _pendingGameplayCount;
	private int _pendingLod1Count;
	private int _pendingWarmCount;
	private int _pendingLod2Count;
	private int _warmResidentCount;
	private int _lod1ResidentCount;
	private int _lod2ResidentCount;
	private uint _nextGeneration;
	private Vector4[] _visibilityBoundsData = Array.Empty<Vector4>();
	private GpuBuffer.IndirectDrawIndexedArguments[] _sourceArgumentData = Array.Empty<GpuBuffer.IndirectDrawIndexedArguments>();
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
	private long _countReadbackCount;
	private long _countReadbackBytes;
	private double _countReadbackMilliseconds;
	private double _countSubmissionMilliseconds;
	private double _emitSubmissionMilliseconds;
	private long _visibilityScalarReadbackCount;
	private long _renderSequence;
	private long _updateEpoch;
	private long _claimedRenderEpoch;
	private int _renderTickInProgress;
	private long _suppressedRenderCallbackCount;
	private long _busyRenderCallbackCount;
	private long _reportedSuppressedRenderCallbackCount;
	private long _lastRenderDiagnosticTimestamp;
	private int _renderDiagnosticReportCount;
	private long _drawCommandResetCount;
	private long _visibilityDescriptorUploadCount;
	private int _drawCommandDiagnosticReportCount;
	private int _cameraBindingDiagnosticCount;
	private long _drawCommitStopwatchTicks;
	private int _drawCommitRebuildCount;
	private int _renderViewKind;
	private int _renderHandoffTraceBudget;
	private long _renderHandoffCount;
	private int _nextRenderCameraStateId;
	private int _maximumDispatchesRequested = MaximumDispatchesPerUpdate;
	private int _processedRenderDispatches;
	private ulong _topologyDigest;
	private ulong _positionDigest;
	private ulong _lod0TopologyDigest;
	private ulong _lod0PositionDigest;
	private ulong _lod1TopologyDigest;
	private ulong _lod1PositionDigest;
	private ulong _lod2TopologyDigest;
	private ulong _lod2PositionDigest;
	private float[] _scheduleLatencyMilliseconds = Array.Empty<float>();
	private int _scheduleLatencySampleCount;
	private int _scheduleLatencyTruncatedCount;
	private int _scheduleLatencyCancelledCount;
	private int _scheduleLatencySupersededCount;
	private bool _scheduleLatencyMeasurementActive;
	private ThroughputRecorder _throughput;
	private float _currentPlayerRouteDistance;
	private long _transitionScheduledCount;
	private long _transitionPublishedCount;
	private long _transitionCancelledCount;
	private long _transitionStaleCount;
	private ulong _transitionTopologyDigest;
	private ulong _transitionPositionDigest;
	private uint _transitionFineFaceMismatchCount;
	private uint _transitionCoarseFaceMismatchCount;
	private uint _transitionLateralEdgeDigest;
	private uint _transitionInvalidTableCount;
	private float[] _transitionLatencyMilliseconds = Array.Empty<float>();
	private int _transitionLatencySampleCount;
	private int _transitionLatencyTruncatedCount;
	private bool _transitionMeasurementActive;
	private long _lod2LastServiceTimestamp;
	private long _lod2EligibleSinceTimestamp;
	private long _lod2ScheduledCount;
	private long _lod2PublishedCount;
	private long _lod2CancelledCount;
	private long _lod2SupersededCount;
	private long _lod2OpportunisticServiceCount;
	private long _lod2ForcedServiceCount;
	private float _lod2MaximumServiceGapMilliseconds;
	private float[] _lod2LatencyMilliseconds = Array.Empty<float>();
	private int _lod2LatencySampleCount;
	private int _lod2LatencyTruncatedCount;
	private MetricSamples _lod2QueueDepth;
	private bool _lod2MeasurementActive;
	private bool _disposed;

	public int ResidentCount => _resident.Count;
	public int PendingCount => PendingGameplayCount + PendingLod1Count + PendingWarmCount;
	public int AllPendingCount => PendingCount + PendingLod2Count;
	public int PendingGameplayCount => _pendingGameplayCount + CountInFlight( GpuMeshResidency.Gameplay );
	public int PendingLod1Count => _pendingLod1Count + CountInFlight( GpuMeshResidency.Lod1 );
	public int PendingWarmCount => _pendingWarmCount + CountInFlight( GpuMeshResidency.Warm );
	public int PendingLod2Count => _pendingLod2Count + CountInFlight( GpuMeshResidency.Lod2 );
	public int WarmResidentCount => _warmResidentCount;
	public int Lod1ResidentCount => _lod1ResidentCount;
	public int Lod2ResidentCount => _lod2ResidentCount;
	public int Lod0ResidentCount => ResidentCount - Lod1ResidentCount - Lod2ResidentCount;
	public int Lod0ActiveCount => _renderActive.Count( key => key.Level == GpuMeshLevel.Lod0 );
	public int Lod1ActiveCount => _renderActive.Count( key => key.Level == GpuMeshLevel.Lod1 );
	public int Lod2ActiveCount => _renderActive.Count( key => key.Level == GpuMeshLevel.Lod2 );
	public int TransitionDesiredCount => _transitionRenderActive.Count;
	public int TransitionReadyCount => _transitionResident.Count;
	public int TransitionDrawableCount => _transitionResident.Count( value => value.Value.Handle is not null );
	public int TransitionPendingCount => _transitionPending.Count + (_transitionScratchLanes?.Sum( lane =>
		lane.CountInFlight.Count + lane.EmitInFlight.Count ) ?? 0);
	public long TransitionScheduledCount => _transitionScheduledCount;
	public long TransitionPublishedCount => _transitionPublishedCount;
	public long TransitionCancelledCount => _transitionCancelledCount;
	public long TransitionStaleCount => _transitionStaleCount;
	public long TransitionUniqueVertexCount => _transitionResident.Values.Sum( value => (long)(value.Handle?.Vertices.Count ?? 0) );
	public long TransitionIndexCount => _transitionResident.Values.Sum( value => (long)(value.Handle?.Indices.Count ?? 0) );
	public long TransitionActiveCellCount => _transitionResident.Values.Sum( value => (long)value.Counts.ActiveCells );
	public string TransitionTopologyDigest => _transitionTopologyDigest.ToString( "X16" );
	public string TransitionPositionDigest => _transitionPositionDigest.ToString( "X16" );
	public uint TransitionFineFaceMismatchCount => _transitionFineFaceMismatchCount;
	public uint TransitionCoarseFaceMismatchCount => _transitionCoarseFaceMismatchCount;
	public uint TransitionLateralEdgeDigest => _transitionLateralEdgeDigest;
	public uint TransitionLateralMismatchCount => CountTransitionLateralMismatches();
	public uint TransitionInvalidTableCount => _transitionInvalidTableCount;
	public int PoolCount => _arenas.Sum( arena => arena.FreeSlotCount );
	public int AllocatedResourceCount => _arenas.Count * RegionsPerSlab;
	public long DispatchCount => _dispatchCount;
	public long PoolAllocationCount => _poolAllocationCount;
	public long PoolReuseCount => _poolReuseCount;
	public long ScalarReadbackCount => _scalarReadbackCount;
	public long CountReadbackCount => _countReadbackCount;
	public long CountReadbackBytes => _countReadbackBytes;
	public double CountReadbackMilliseconds => _countReadbackMilliseconds;
	public double CountSubmissionMilliseconds => _countSubmissionMilliseconds;
	public double EmitSubmissionMilliseconds => _emitSubmissionMilliseconds;
	public long VisibilityScalarReadbackCount => _visibilityScalarReadbackCount;
	public const long GeometryReadbackCount = 0;
	public const long OrdinaryRenderSdfEvaluationCount = 0;
	public int TerrainIndirectApiSubmissionCount => _arenas.Count( arena => arena.ActiveResidentCount > 0 );
	public int IndirectArgumentRecordCount => TerrainIndirectApiSubmissionCount * RegionsPerSlab;
	public int TerrainBufferGroupCount => _arenas.Count;
	public long LogicalCapacityBytes => UsedVertexBytes + UsedIndexBytes;
	public long ReservedActiveCellCapacity => 0;
	public long ReservedActiveCellCapacityBytes => 0;
	public long UniqueVertexCount => _arenas.Sum( arena => (long)arena.VertexUsed ) - TransitionAllocatedVertexCount;
	public long IndexCount => _arenas.Sum( arena => (long)arena.IndexUsed ) - TransitionAllocatedIndexCount;
	public long TriangleCount => IndexCount / 3;
	public long UsedVertexBytes => UniqueVertexCount * TerrainVertexBytes;
	public long UsedIndexBytes => IndexCount * sizeof( uint );
	public long CommittedVertexBytes => (long)_arenas.Count * VertexArenaBytes;
	public long CommittedIndexBytes => (long)_arenas.Count * IndexArenaBytes;
	public long TransientScratchBytes => _scratchLanes?.Sum( lane => lane.Scratch.CapacityBytes ) ?? 0;
	public long TransitionTransientScratchBytes => _transitionScratchLanes?.Sum( lane => lane.Scratch.CapacityBytes ) ?? 0;
	public long Lod2TransientScratchBytes => _lod2ScratchLane?.Scratch.CapacityBytes ?? 0;
	private long TransitionAllocatedVertexCount => TransitionUniqueVertexCount +
		(_transitionScratchLanes?.Sum( lane => lane.EmitInFlight.Sum(
			value => (long)(value.Handle?.Vertices.Count ?? 0) ) ) ?? 0);
	private long TransitionAllocatedIndexCount => TransitionIndexCount +
		(_transitionScratchLanes?.Sum( lane => lane.EmitInFlight.Sum(
			value => (long)(value.Handle?.Indices.Count ?? 0) ) ) ?? 0);
	public int ArenaCount => _arenas.Count;
	public int FreeRangeCount => _arenas.Sum( arena => arena.VertexFreeRangeCount + arena.IndexFreeRangeCount );
	public int LargestFreeVertexRange => _arenas.Count == 0 ? 0 : _arenas.Max( arena => arena.VertexLargestFree );
	public int LargestFreeIndexRange => _arenas.Count == 0 ? 0 : _arenas.Max( arena => arena.IndexLargestFree );
	public float FragmentationPercent
	{
		get
		{
			var free = _arenas.Sum( arena =>
				(long)arena.VertexFree * TerrainVertexBytes + (long)arena.IndexFree * sizeof( uint ) );
			var largest = _arenas.Sum( arena =>
				(long)arena.VertexLargestFree * TerrainVertexBytes + (long)arena.IndexLargestFree * sizeof( uint ) );
			return free > 0 ? (float)(free - largest) * 100f / free : 0f;
		}
	}
	public string TopologyDigest => _topologyDigest.ToString( "X16" );
	public string PositionDigest => _positionDigest.ToString( "X16" );
	public string Lod0TopologyDigest => _lod0TopologyDigest.ToString( "X16" );
	public string Lod0PositionDigest => _lod0PositionDigest.ToString( "X16" );
	public string Lod1TopologyDigest => _lod1TopologyDigest.ToString( "X16" );
	public string Lod1PositionDigest => _lod1PositionDigest.ToString( "X16" );
	public string Lod2TopologyDigest => _lod2TopologyDigest.ToString( "X16" );
	public string Lod2PositionDigest => _lod2PositionDigest.ToString( "X16" );
	public long RenderSequence => System.Threading.Interlocked.Read( ref _renderSequence );
	public long LogicalVisibilityBytes => _visibilityCapacity == 0 ? 0 :
		(long)_visibilityCapacity * (sizeof( float ) * 8 + IndirectArgumentStride * 2) + sizeof( uint ) * 31;

	public GpuVoxelMesher( Scene scene, int cellsPerAxis )
	{
		_scene = scene;
		_cellsPerAxis = cellsPerAxis;
		_scratchLanes = CreateScratchLanes( cellsPerAxis );
		_lod2ScratchLane = new ScratchLane( cellsPerAxis );
		_transitionScratchLanes = CreateTransitionScratchLanes();
		_lod2LastServiceTimestamp = Stopwatch.GetTimestamp();
		_readbackObject = new ReadbackSceneObject( scene.SceneWorld, this );
		Sandbox.Diagnostics.GpuProfilerStats.Enabled = true;
		RefreshRenderCameras();
	}

	public void BeginThroughputMeasurement( float chunkWorldSize )
	{
		_throughput = new ThroughputRecorder( chunkWorldSize );
		_currentPlayerRouteDistance = 0f;
	}

	public void SetPlayerRouteDistance( float worldDistance )
	{
		_currentPlayerRouteDistance = worldDistance;
	}

	public void SampleThroughputQueueDepth()
	{
		_throughput?.SampleQueueDepth( PendingGameplayCount, PendingWarmCount );
		if ( _lod2MeasurementActive ) _lod2QueueDepth?.Record( PendingLod2Count );
	}

	public void EndMovingThroughputWindow( float durationSeconds )
	{
		_throughput?.EndMovingWindow( durationSeconds );
	}

	public void MarkThroughputSettled()
	{
		_throughput?.MarkSettled();
	}

	public GpuMeshThroughputMeasurement CompleteThroughputMeasurement()
	{
		var result = _throughput?.Complete() ?? default;
		_throughput = null;
		return result;
	}

	private static ScratchLane[] CreateScratchLanes( int cellsPerAxis )
	{
		var lanes = new ScratchLane[ScratchLaneCount];
		for ( var index = 0; index < lanes.Length; index++ )
		{
			lanes[index] = new ScratchLane( cellsPerAxis );
		}
		return lanes;
	}

	private static TransitionScratchLane[] CreateTransitionScratchLanes()
	{
		var lanes = new TransitionScratchLane[ScratchLaneCount];
		for ( var index = 0; index < lanes.Length; index++ )
		{
			lanes[index] = new TransitionScratchLane();
		}
		return lanes;
	}

	public void Schedule( VoxelChunk chunk, int sourceRevision, float playerRouteDistance,
		GpuMeshResidency residency = GpuMeshResidency.Gameplay )
	{
		if ( chunk.DensityClassification != ChunkDensityClassification.PotentiallySurfaceContaining )
		{
			Remove( new GpuMeshRegionKey( GpuMeshLevel.Lod0, chunk.Coordinate ) );
			return;
		}
		var descriptor = GpuSdfDescriptor.FromChunk( chunk, sourceRevision );
		Schedule( descriptor, playerRouteDistance, residency );
	}

	public void Schedule( GpuSdfDescriptor descriptor, float playerRouteDistance, GpuMeshResidency residency )
	{
		if ( _resident.TryGetValue( descriptor.Key, out var resident ) && resident.Descriptor == descriptor )
		{
			SetResidency( resident, residency );
			return;
		}
		if ( residency == GpuMeshResidency.Lod2 )
		{
			if ( _lod2MeasurementActive ) _lod2ScheduledCount++;
		}
		else
		{
			_throughput?.RecordScheduled();
		}
		QueuePending( new PendingMesh(
			descriptor,
			residency,
			Stopwatch.GetTimestamp(),
			playerRouteDistance ) );
	}

	public void ScheduleTransition( GpuTransitionDescriptor descriptor, float playerRouteDistance )
	{
		if ( _transitionDesiredDescriptors.TryGetValue( descriptor.Key, out var desired ) &&
			desired == descriptor ) return;
		_transitionDesiredDescriptors[descriptor.Key] = descriptor;
		RemovePendingTransition( descriptor.Key );
		var pending = new PendingTransition(
			descriptor,
			Stopwatch.GetTimestamp(),
			playerRouteDistance );
		_transitionPending[descriptor.Key] = pending;
		_transitionDispatchQueue.Enqueue( pending );
		_transitionScheduledCount++;
	}

	public void SetTransitionActive( Lod0Lod1TransitionKey key, bool active )
	{
		var changed = active ? _transitionRenderActive.Add( key ) : _transitionRenderActive.Remove( key );
		if ( !changed || !_transitionResident.TryGetValue( key, out var resident ) || resident.Handle is null ) return;
		SetTransitionVisibilityActive( resident, active );
		MarkDrawCommandsDirty();
	}

	public void RemoveTransition( Lod0Lod1TransitionKey key )
	{
		_transitionRenderActive.Remove( key );
		_transitionDesiredDescriptors.Remove( key );
		RemovePendingTransition( key );
		foreach ( var lane in _transitionScratchLanes ?? Array.Empty<TransitionScratchLane>() )
		{
			foreach ( var value in lane.CountInFlight )
			{
				if ( value.Descriptor.Key == key ) _transitionCancelledGenerations.Add( value.Generation );
			}
			foreach ( var value in lane.EmitInFlight )
			{
				if ( value.Descriptor.Key == key ) _transitionCancelledGenerations.Add( value.Generation );
			}
		}
		if ( !_transitionResident.Remove( key, out var resident ) ) return;
		ReleaseTransitionResident( resident );
		MarkDrawCommandsDirty();
	}

	public GpuTransitionIdentitySnapshot CaptureTransitionIdentity(
		IEnumerable<Lod0Lod1TransitionKey> keys )
	{
		var count = 0;
		ulong digest = 0;
		foreach ( var key in keys
			.OrderBy( value => value.Lod1Coordinate.x )
			.ThenBy( value => value.Lod1Coordinate.y )
			.ThenBy( value => value.Lod1Coordinate.z )
			.ThenBy( value => value.Face ) )
		{
			if ( !_transitionResident.TryGetValue( key, out var resident ) ) continue;
			var handle = resident.Handle;
			uint allocation = handle is null ? 0 :
				(uint)handle.Arena.Index * 0x9E3779B1u ^
				(uint)handle.Slot * 0x85EBCA77u ^
				(uint)handle.Vertices.Offset * 0xC2B2AE3Du ^
				(uint)handle.Vertices.Count * 0x27D4EB2Du ^
				(uint)handle.Indices.Offset * 0x165667B1u ^
				(uint)handle.Indices.Count * 0xD3A2646Cu;
			digest ^= TransitionCoordinateDigest(
				key, (handle?.Generation ?? resident.Counts.Generation) ^ allocation );
			count++;
		}
		return new GpuTransitionIdentitySnapshot( count, digest );
	}

	public void BeginTransitionMeasurement()
	{
		_transitionScheduledCount = 0;
		_transitionPublishedCount = 0;
		_transitionCancelledCount = 0;
		_transitionStaleCount = 0;
		_transitionLatencyMilliseconds = new float[MaximumScheduleLatencySamples];
		_transitionLatencySampleCount = 0;
		_transitionLatencyTruncatedCount = 0;
		_transitionMeasurementActive = true;
	}

	public GpuTransitionMeasurement CompleteTransitionMeasurement()
	{
		_transitionMeasurementActive = false;
		Array.Sort( _transitionLatencyMilliseconds, 0, _transitionLatencySampleCount );
		var result = new GpuTransitionMeasurement(
			TransitionDesiredCount,
			TransitionReadyCount,
			TransitionDrawableCount,
			TransitionPendingCount,
			_transitionScheduledCount,
			_transitionPublishedCount,
			_transitionCancelledCount,
			_transitionStaleCount,
			TransitionUniqueVertexCount,
			TransitionIndexCount,
			TransitionActiveCellCount,
			TransitionTopologyDigest,
			TransitionPositionDigest,
			_transitionFineFaceMismatchCount,
			_transitionCoarseFaceMismatchCount,
			_transitionLateralEdgeDigest,
			TransitionLateralMismatchCount,
			_transitionInvalidTableCount,
			CreateTransitionFaceMeasurements(),
			new GpuMetricDistribution(
				_transitionLatencySampleCount,
				_transitionLatencyTruncatedCount,
				_transitionLatencySampleCount > 0 ? _transitionLatencyMilliseconds.Take( _transitionLatencySampleCount ).Average() : 0,
				GetTransitionLatencyPercentile( 0.50 ),
				GetTransitionLatencyPercentile( 0.95 ),
				GetTransitionLatencyPercentile( 0.99 ),
				_transitionLatencySampleCount > 0 ? _transitionLatencyMilliseconds[_transitionLatencySampleCount - 1] : 0 ) );
		_transitionLatencyMilliseconds = Array.Empty<float>();
		return result;
	}

	private GpuTransitionFaceMeasurement[] CreateTransitionFaceMeasurements() =>
		_transitionResident
			.OrderBy( pair => pair.Key.Lod1Coordinate.x )
			.ThenBy( pair => pair.Key.Lod1Coordinate.y )
			.ThenBy( pair => pair.Key.Lod1Coordinate.z )
			.ThenBy( pair => pair.Key.Face )
			.Select( pair =>
			{
				var resident = pair.Value;
				var handle = resident.Handle;
				return new GpuTransitionFaceMeasurement(
					pair.Key,
					handle?.Generation ?? resident.Counts.Generation,
					handle?.Arena.Index ?? -1,
					handle?.Slot ?? -1,
					handle?.Vertices.Offset ?? 0,
					handle?.Vertices.Count ?? 0,
					handle?.Indices.Offset ?? 0,
					handle?.Indices.Count ?? 0,
					resident.Counts.ActiveCells,
					resident.ScheduleToPublicationMilliseconds,
					resident.Counts.TopologyDigest,
					resident.Counts.PositionDigest,
					resident.Counts.FineFaceMismatchCount,
					resident.Counts.CoarseFaceMismatchCount,
					resident.Counts.MinimumUDigest,
					resident.Counts.MaximumUDigest,
					resident.Counts.MinimumVDigest,
					resident.Counts.MaximumVDigest,
					resident.Counts.InvalidTableCount );
			})
			.ToArray();

	private float GetTransitionLatencyPercentile( double percentile )
	{
		if ( _transitionLatencySampleCount == 0 ) return 0;
		var index = Math.Clamp( (int)Math.Ceiling( _transitionLatencySampleCount * percentile ) - 1,
			0, _transitionLatencySampleCount - 1 );
		return _transitionLatencyMilliseconds[index];
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
			GetScheduleLatencyPercentile( 0.50 ),
			GetScheduleLatencyPercentile( 0.95 ),
			GetScheduleLatencyPercentile( 0.99 ),
			_scheduleLatencySampleCount > 0 ? _scheduleLatencyMilliseconds[_scheduleLatencySampleCount - 1] : 0,
			_scheduleLatencyCancelledCount,
			_scheduleLatencySupersededCount );
		_scheduleLatencyMilliseconds = Array.Empty<float>();
		return result;
	}

	public void BeginLod2Measurement()
	{
		_lod2ScheduledCount = 0;
		_lod2PublishedCount = 0;
		_lod2CancelledCount = 0;
		_lod2SupersededCount = 0;
		_lod2OpportunisticServiceCount = 0;
		_lod2ForcedServiceCount = 0;
		_lod2MaximumServiceGapMilliseconds = 0f;
		_lod2LatencyMilliseconds = new float[MaximumScheduleLatencySamples];
		_lod2LatencySampleCount = 0;
		_lod2LatencyTruncatedCount = 0;
		_lod2QueueDepth = new MetricSamples( MaximumScheduleLatencySamples );
		_lod2MeasurementActive = true;
	}

	public GpuLod2Measurement CompleteLod2Measurement()
	{
		_lod2MeasurementActive = false;
		Array.Sort( _lod2LatencyMilliseconds, 0, _lod2LatencySampleCount );
		var result = new GpuLod2Measurement(
			_lod2ScheduledCount,
			_lod2PublishedCount,
			_lod2CancelledCount,
			_lod2SupersededCount,
			_lod2OpportunisticServiceCount,
			_lod2ForcedServiceCount,
			_lod2MaximumServiceGapMilliseconds,
			_lod2QueueDepth?.CompleteQueue() ?? default,
			new GpuMeshScheduleLatencyMeasurement(
				_lod2LatencySampleCount,
				_lod2LatencyTruncatedCount,
				GetLod2LatencyPercentile( 0.50 ),
				GetLod2LatencyPercentile( 0.95 ),
				GetLod2LatencyPercentile( 0.99 ),
				GetLod2LatencyPercentile( 1.0 ),
				(int)Math.Min( int.MaxValue, _lod2CancelledCount ),
				(int)Math.Min( int.MaxValue, _lod2SupersededCount ) ) );
		_lod2LatencyMilliseconds = Array.Empty<float>();
		_lod2QueueDepth = null;
		return result;
	}

	private float GetLod2LatencyPercentile( double percentile )
	{
		if ( _lod2LatencySampleCount == 0 ) return 0f;
		var index = Math.Clamp(
			(int)Math.Ceiling( _lod2LatencySampleCount * percentile ) - 1,
			0,
			_lod2LatencySampleCount - 1 );
		return _lod2LatencyMilliseconds[index];
	}

	private float GetScheduleLatencyPercentile( double percentile )
	{
		if ( _scheduleLatencySampleCount == 0 ) return 0;
		var index = Math.Clamp( (int)Math.Ceiling( _scheduleLatencySampleCount * percentile ) - 1, 0, _scheduleLatencySampleCount - 1 );
		return _scheduleLatencyMilliseconds[index];
	}

	public void SetResidency( GpuMeshRegionKey key, GpuMeshResidency residency )
	{
		if ( _resident.TryGetValue( key, out var resident ) )
		{
			SetResidency( resident, residency );
			return;
		}
		if ( _pending.TryGetValue( key, out var pending ) && pending.Residency != residency )
		{
			QueuePending( pending with { Residency = residency } );
			return;
		}
		foreach ( var lane in _scratchLanes )
		{
			for ( var index = 0; index < lane.CountInFlight.Count; index++ )
			{
				if ( lane.CountInFlight[index].Descriptor.Key == key )
				{
					lane.CountInFlight[index] = lane.CountInFlight[index] with { Residency = residency };
					return;
				}
			}
			for ( var index = 0; index < lane.EmitInFlight.Count; index++ )
			{
				if ( lane.EmitInFlight[index].Descriptor.Key == key )
				{
					lane.EmitInFlight[index] = lane.EmitInFlight[index] with { Residency = residency };
					return;
				}
			}
		}
	}

	public void SetRenderActive( GpuMeshRegionKey key, bool active )
	{
		var changed = active ? _renderActive.Add( key ) : _renderActive.Remove( key );
		if ( !changed || !_resident.TryGetValue( key, out var resident ) || resident.Handle is null ) return;
		SetVisibilityActive( resident, active );
		MarkDrawCommandsDirty();
	}

	public bool Contains( GpuMeshRegionKey key ) => _resident.ContainsKey( key ) || _pending.ContainsKey( key ) ||
		(_scratchLanes?.Any( lane => lane.CountInFlight.Any( value => value.Descriptor.Key == key ) ||
			lane.EmitInFlight.Any( value => value.Descriptor.Key == key ) ) ?? false) ||
		(_lod2ScratchLane is not null && (_lod2ScratchLane.CountInFlight.Any( value => value.Descriptor.Key == key ) ||
			_lod2ScratchLane.EmitInFlight.Any( value => value.Descriptor.Key == key )));

	public void Remove( GpuMeshRegionKey key )
	{
		_renderActive.Remove( key );
		if ( _lod2MeasurementActive && _pending.TryGetValue( key, out var pending ) &&
			pending.Residency == GpuMeshResidency.Lod2 )
		{
			_lod2CancelledCount++;
		}
		RemovePending( key );
		if ( _scratchLanes.Any( lane =>
			lane.CountInFlight.Any( value => value.Descriptor.Key == key ) ||
			lane.EmitInFlight.Any( value => value.Descriptor.Key == key ) ) ||
			(_lod2ScratchLane is not null && (_lod2ScratchLane.CountInFlight.Any( value => value.Descriptor.Key == key ) ||
				_lod2ScratchLane.EmitInFlight.Any( value => value.Descriptor.Key == key ))) )
		{
			_cancelledInFlight.Add( key );
		}
		if ( !_resident.Remove( key, out var resident ) ) return;
		ReleaseResident( resident );
		MarkDrawCommandsDirty();
	}

	public void Reset( int cellsPerAxis )
	{
		Clear();
		DisposeLod2ScratchLane();
		_lod2ScratchLane = new ScratchLane( cellsPerAxis );
		_lod2LastServiceTimestamp = Stopwatch.GetTimestamp();
		if ( cellsPerAxis == _cellsPerAxis ) return;
		DisposeArenas();
		DisposeVisibilityBuffers();
		DisposeScratchLanes();
		DisposeTransitionScratchLanes();
		_cellsPerAxis = cellsPerAxis;
		_scratchLanes = CreateScratchLanes( cellsPerAxis );
		_transitionScratchLanes = CreateTransitionScratchLanes();
	}

	public int ProcessPending( int maximumDispatches, long updateEpoch )
	{
		RefreshRenderCameras();
		FinalizeEmits();
		FinalizeLod2Emit();
		FinalizeTransitionEmits();
		_maximumDispatchesRequested = Math.Clamp( maximumDispatches, 0, MaximumDispatchesPerUpdate );
		System.Threading.Interlocked.Exchange( ref _updateEpoch, updateEpoch );
		var processed = System.Threading.Interlocked.Exchange( ref _processedRenderDispatches, 0 );
		ReportSuppressedRenderCallbacks();
		return processed;
	}

	public DrawCommandCommitResult DrainDrawCommandCommitResult()
	{
		var ticks = System.Threading.Interlocked.Exchange( ref _drawCommitStopwatchTicks, 0 );
		var rebuilds = System.Threading.Interlocked.Exchange( ref _drawCommitRebuildCount, 0 );
		return new DrawCommandCommitResult(
			rebuilds > 0,
			(float)(ticks * 1000.0 / Stopwatch.Frequency) );
	}

	private bool TryBeginGpuRenderTick()
	{
		var updateEpoch = System.Threading.Interlocked.Read( ref _updateEpoch );
		if ( updateEpoch <= System.Threading.Interlocked.Read( ref _claimedRenderEpoch ) )
		{
			System.Threading.Interlocked.Increment( ref _suppressedRenderCallbackCount );
			return false;
		}

		if ( System.Threading.Interlocked.CompareExchange( ref _renderTickInProgress, 1, 0 ) != 0 )
		{
			System.Threading.Interlocked.Increment( ref _busyRenderCallbackCount );
			return false;
		}

		updateEpoch = System.Threading.Interlocked.Read( ref _updateEpoch );
		if ( updateEpoch <= System.Threading.Interlocked.Read( ref _claimedRenderEpoch ) )
		{
			System.Threading.Interlocked.Exchange( ref _renderTickInProgress, 0 );
			System.Threading.Interlocked.Increment( ref _suppressedRenderCallbackCount );
			return false;
		}

		System.Threading.Interlocked.Exchange( ref _claimedRenderEpoch, updateEpoch );
		return true;
	}

	private void ObserveRenderView()
	{
		var mainCameraPosition = _camera.IsValid() ? _camera.WorldPosition : Vector3.Zero;
		var renderCameraPosition = Graphics.CameraPosition;
		var viewKind = _camera.IsValid() && renderCameraPosition.AlmostEqual( mainCameraPosition, 0.25f ) ? 1 : 2;
		var previousKind = System.Threading.Interlocked.Exchange( ref _renderViewKind, viewKind );
		if ( previousKind == 0 || previousKind == viewKind ) return;

		var handoff = System.Threading.Interlocked.Increment( ref _renderHandoffCount );
		if ( handoff > 8 ) return;
		System.Threading.Interlocked.Exchange( ref _renderHandoffTraceBudget, 48 );
		Log.Info(
			$"[VoxelWorld] gpu.render.view_handoff total={handoff} " +
			$"from={(previousKind == 1 ? "main" : "other")} to={(viewKind == 1 ? "main" : "other")} " +
			$"renderCamera={renderCameraPosition} mainCamera={mainCameraPosition} " +
			$"updateEpoch={System.Threading.Interlocked.Read( ref _updateEpoch )} " +
			$"claimedEpoch={System.Threading.Interlocked.Read( ref _claimedRenderEpoch )} " +
			$"renderSequence={System.Threading.Interlocked.Read( ref _renderSequence )}" );
	}

	private void CommitDrawCommandsForView( RenderCameraState requestedState )
	{
		lock ( _renderCameraLock )
		{
			if ( requestedState.Camera.IsValid() &&
				_renderCameraStates.TryGetValue( requestedState.Camera, out var currentState ) &&
				ReferenceEquals( requestedState, currentState ) )
			{
				if ( _visibilityMeasurementActive || _visibilitySettledCaptureActive )
					_visibilityReadbackState = requestedState;
				CommitDrawCommandsLocked( requestedState );
			}
		}
	}

	private void TraceRenderHandoffPhase( string phase )
	{
		var remaining = System.Threading.Interlocked.Decrement( ref _renderHandoffTraceBudget );
		if ( remaining < 0 ) return;
		Log.Info(
			$"[VoxelWorld] gpu.render.handoff_phase phase=\"{phase}\" remaining={remaining} " +
			$"view={(_renderViewKind == 1 ? "main" : "other")} " +
			$"updateEpoch={System.Threading.Interlocked.Read( ref _updateEpoch )} " +
			$"claimedEpoch={System.Threading.Interlocked.Read( ref _claimedRenderEpoch )} " +
			$"renderSequence={System.Threading.Interlocked.Read( ref _renderSequence )} " +
			$"regularPending={PendingCount} lod2Pending={PendingLod2Count} " +
			$"transitionPending={TransitionPendingCount}" );
	}

	private void EndGpuRenderTick()
	{
		System.Threading.Interlocked.Exchange( ref _renderTickInProgress, 0 );
	}

	private void ReportSuppressedRenderCallbacks()
	{
		var suppressed = System.Threading.Interlocked.Read( ref _suppressedRenderCallbackCount );
		if ( suppressed == _reportedSuppressedRenderCallbackCount || _renderDiagnosticReportCount >= 10 ) return;

		var now = Stopwatch.GetTimestamp();
		if ( _lastRenderDiagnosticTimestamp != 0 &&
			Stopwatch.GetElapsedTime( _lastRenderDiagnosticTimestamp, now ).TotalSeconds < 1.0 ) return;

		var newlySuppressed = suppressed - _reportedSuppressedRenderCallbackCount;
		_reportedSuppressedRenderCallbackCount = suppressed;
		_lastRenderDiagnosticTimestamp = now;
		_renderDiagnosticReportCount++;
		Log.Info(
			$"[VoxelWorld] gpu.render.extra_views suppressedTotal={suppressed} suppressedSinceLast={newlySuppressed} " +
			$"busyTotal={System.Threading.Interlocked.Read( ref _busyRenderCallbackCount )} " +
			$"updateEpoch={System.Threading.Interlocked.Read( ref _updateEpoch )} " +
			$"claimedEpoch={System.Threading.Interlocked.Read( ref _claimedRenderEpoch )} " +
			$"renderSequence={System.Threading.Interlocked.Read( ref _renderSequence )}" );
	}

	private void ProcessGpuRenderTick()
	{
		if ( _disposed || _scratchLanes is null ) return;
		TraceRenderHandoffPhase( "gpu.begin" );
		var regularGpuWorkSubmitted = false;
		foreach ( var lane in _scratchLanes )
		{
			if ( lane.CountInFlight.Count > 0 && lane.Scratch.TryTakeCounts(
				out var counts,
				out var count,
				out var readbackMilliseconds,
				out var callbackWaitMilliseconds ) )
			{
				AllocateAndEmit( lane, counts, count, readbackMilliseconds, callbackWaitMilliseconds );
				regularGpuWorkSubmitted = true;
				break;
			}
		}

		var targetLane = _scratchLanes.FirstOrDefault( lane => lane.IsIdle );
		if ( targetLane is not null )
		{
			var requests = new GpuTerrainRequest[MaximumDispatchesPerUpdate];
			var processed = 0;
			while ( processed < _maximumDispatchesRequested && TryDequeuePending( out var pending ) )
			{
				var generation = ++_nextGeneration;
				var inFlight = new InFlightMesh(
					pending.Descriptor,
					pending.Residency,
					generation,
					pending.ScheduledTimestamp,
					pending.ScheduledRouteDistance );
				targetLane.CountInFlight.Add( inFlight );
				requests[processed] = CreateRequest( inFlight, processed );
				processed++;
			}
			if ( processed > 0 )
			{
				if ( !targetLane.Scratch.TrySubmitCount( requests, processed, out var submissionMilliseconds ) )
					throw new InvalidOperationException( "Voxel terrain scratch rejected an idle count batch." );
				_countSubmissionMilliseconds += submissionMilliseconds;
				_throughput?.RecordBatchSubmitted( processed, (float)submissionMilliseconds );
				System.Threading.Interlocked.Add( ref _processedRenderDispatches, processed );
				regularGpuWorkSubmitted = true;
			}
		}

		var transitionGpuWorkSubmitted = !regularGpuWorkSubmitted && ProcessTransitionGpuRenderTick();
		TraceRenderHandoffPhase(
			$"gpu.foreground.end regularSubmitted={regularGpuWorkSubmitted} transitionSubmitted={transitionGpuWorkSubmitted}" );
		ProcessLod2AfterForeground( regularGpuWorkSubmitted || transitionGpuWorkSubmitted );
		TraceRenderHandoffPhase( "gpu.lod2.end" );
	}

	private bool ProcessTransitionGpuRenderTick()
	{
		if ( _transitionScratchLanes is null ) return false;
		foreach ( var lane in _transitionScratchLanes )
		{
			if ( lane.CountInFlight.Count > 0 && lane.Scratch.TryTakeCounts(
				out var counts,
				out var count,
				out var readbackMilliseconds,
				out var callbackWaitMilliseconds ) )
			{
				AllocateAndEmitTransitions( lane, counts, count, readbackMilliseconds, callbackWaitMilliseconds );
				return true;
			}
		}
		foreach ( var lane in _transitionScratchLanes )
		{
			if ( lane.CountInFlight.Count > 0 && lane.Scratch.TryContinueCount() ) return true;
		}

		var targetLane = _transitionScratchLanes.FirstOrDefault( lane => lane.IsIdle );
		if ( targetLane is null ) return false;
		var requests = new GpuTransitionRequest[MaximumDispatchesPerUpdate];
		var processed = 0;
		while ( processed < MaximumDispatchesPerUpdate && TryDequeuePendingTransition( out var pending ) )
		{
			var generation = ++_nextGeneration;
			var inFlight = new InFlightTransition(
				pending.Descriptor,
				generation,
				pending.ScheduledTimestamp,
				pending.ScheduledRouteDistance );
			targetLane.CountInFlight.Add( inFlight );
			requests[processed] = CreateTransitionRequest( inFlight, processed );
			processed++;
		}
		if ( processed == 0 ) return false;
		if ( !targetLane.Scratch.TrySubmitCount( requests, processed, out _ ) )
			throw new InvalidOperationException( "Voxel transition scratch rejected an idle count batch." );
		return true;
	}

	private void ProcessLod2AfterForeground( bool foregroundSubmitted )
	{
		if ( _lod2ScratchLane is null ) return;
		var eligible = _pendingLod2Count > 0 || _lod2ScratchLane.CountInFlight.Count > 0;
		if ( !eligible )
		{
			_lod2EligibleSinceTimestamp = 0;
			return;
		}

		var now = Stopwatch.GetTimestamp();
		if ( _lod2EligibleSinceTimestamp == 0 ) _lod2EligibleSinceTimestamp = now;
		var serviceGapStart = Math.Max( _lod2LastServiceTimestamp, _lod2EligibleSinceTimestamp );
		var serviceGap = Stopwatch.GetElapsedTime( serviceGapStart, now ).TotalMilliseconds;
		var forced = foregroundSubmitted && _lod2ScratchLane.CountInFlight.Count > 0 &&
			serviceGap >= Lod2MaximumServiceDelayMilliseconds;
		if ( foregroundSubmitted && !forced ) return;
		if ( !ProcessLod2GpuRenderTick() ) return;

		if ( _lod2MeasurementActive )
		{
			_lod2MaximumServiceGapMilliseconds = Math.Max(
				_lod2MaximumServiceGapMilliseconds,
				(float)serviceGap );
			if ( forced ) _lod2ForcedServiceCount++;
			else _lod2OpportunisticServiceCount++;
		}
		_lod2LastServiceTimestamp = now;
		_lod2EligibleSinceTimestamp = 0;
	}

	private bool ProcessLod2GpuRenderTick()
	{
		var lane = _lod2ScratchLane;
		if ( lane.CountInFlight.Count > 0 && lane.Scratch.TryTakeCounts(
			out var counts,
			out var count,
			out var readbackMilliseconds,
			out var callbackWaitMilliseconds ) )
		{
			AllocateAndEmit( lane, counts, count, readbackMilliseconds, callbackWaitMilliseconds, true );
			return true;
		}

		if ( !lane.IsIdle ) return false;
		var requests = new GpuTerrainRequest[MaximumDispatchesPerUpdate];
		var processed = 0;
		while ( processed < MaximumDispatchesPerUpdate && TryDequeuePendingLod2( out var pending ) )
		{
			var generation = ++_nextGeneration;
			var inFlight = new InFlightMesh(
				pending.Descriptor,
				pending.Residency,
				generation,
				pending.ScheduledTimestamp,
				pending.ScheduledRouteDistance );
			lane.CountInFlight.Add( inFlight );
			requests[processed] = CreateRequest( inFlight, processed );
			processed++;
		}
		if ( processed == 0 ) return false;
		if ( !lane.Scratch.TrySubmitCount( requests, processed, out _ ) )
			throw new InvalidOperationException( "LOD2 terrain scratch rejected an idle count batch." );
		return true;
	}

	private static GpuTerrainRequest CreateRequest( InFlightMesh inFlight, int requestIndex )
	{
		var descriptor = inFlight.Descriptor;
		var size = descriptor.CellsPerAxis * descriptor.CellSize;
		var origin = new Vector3(
			descriptor.ChunkCoordinate.x * size,
			descriptor.ChunkCoordinate.y * size,
			descriptor.ChunkCoordinate.z * size );
		return new GpuTerrainRequest
		{
			OriginAndCellSize = new Vector4( origin, descriptor.CellSize ),
			Terrain = new Vector4(
				descriptor.TerrainSettings.WorldSeed,
				descriptor.TerrainSettings.SurfaceBaseHeight,
				descriptor.TerrainSettings.SurfaceFrequency,
				descriptor.TerrainSettings.SurfaceAmplitude ),
			CellsPerAxis = descriptor.CellsPerAxis,
			Generation = inFlight.Generation,
			RequestIndex = (uint)requestIndex
		};
	}

	private static GpuTransitionRequest CreateTransitionRequest( InFlightTransition inFlight, int requestIndex )
	{
		var descriptor = inFlight.Descriptor;
		var size = descriptor.CellsPerAxis * descriptor.CoarseCellSize;
		var regionOrigin = new Vector3(
			descriptor.Key.Lod1Coordinate.x * size,
			descriptor.Key.Lod1Coordinate.y * size,
			descriptor.Key.Lod1Coordinate.z * size );
		Vector3 origin;
		Vector3 basisU;
		Vector3 basisV;
		Vector3 normal;
		switch ( descriptor.Key.Face )
		{
			case Lod0Lod1TransitionFace.NegativeX:
				origin = regionOrigin + new Vector3( 0, 0, size );
				basisU = new Vector3( 0, 1, 0 );
				basisV = new Vector3( 0, 0, -1 );
				normal = new Vector3( -1, 0, 0 );
				break;
			case Lod0Lod1TransitionFace.PositiveX:
				origin = regionOrigin + new Vector3( size, 0, 0 );
				basisU = new Vector3( 0, 1, 0 );
				basisV = new Vector3( 0, 0, 1 );
				normal = new Vector3( 1, 0, 0 );
				break;
			case Lod0Lod1TransitionFace.NegativeY:
				origin = regionOrigin + new Vector3( size, 0, 0 );
				basisU = new Vector3( 0, 0, 1 );
				basisV = new Vector3( -1, 0, 0 );
				normal = new Vector3( 0, -1, 0 );
				break;
			case Lod0Lod1TransitionFace.PositiveY:
				origin = regionOrigin + new Vector3( 0, size, 0 );
				basisU = new Vector3( 0, 0, 1 );
				basisV = new Vector3( 1, 0, 0 );
				normal = new Vector3( 0, 1, 0 );
				break;
			case Lod0Lod1TransitionFace.NegativeZ:
				origin = regionOrigin + new Vector3( 0, size, 0 );
				basisU = new Vector3( 1, 0, 0 );
				basisV = new Vector3( 0, -1, 0 );
				normal = new Vector3( 0, 0, -1 );
				break;
			default:
				origin = regionOrigin + new Vector3( 0, 0, size );
				basisU = new Vector3( 1, 0, 0 );
				basisV = new Vector3( 0, 1, 0 );
				normal = new Vector3( 0, 0, 1 );
				break;
		}
		return new GpuTransitionRequest
		{
			OriginAndFineCellSize = new Vector4( origin, descriptor.FineCellSize ),
			Terrain = new Vector4(
				descriptor.TerrainSettings.WorldSeed,
				descriptor.TerrainSettings.SurfaceBaseHeight,
				descriptor.TerrainSettings.SurfaceFrequency,
				descriptor.TerrainSettings.SurfaceAmplitude ),
			BasisUAndCoarseCellSize = new Vector4( basisU, descriptor.CoarseCellSize ),
			BasisVAndCellsPerAxis = new Vector4( basisV, descriptor.CellsPerAxis ),
			NormalAndFace = new Vector4( normal, (int)descriptor.Key.Face ),
			Generation = inFlight.Generation,
			RequestIndex = (uint)requestIndex
		};
	}

	private void AllocateAndEmit( ScratchLane lane, GpuTerrainCountResult[] counts, int count,
		double readbackMilliseconds, double callbackWaitMilliseconds, bool lod2 = false )
	{
		if ( count != lane.CountInFlight.Count ) throw new InvalidOperationException( "Voxel terrain count batch length changed." );
		_countReadbackCount++;
		_scalarReadbackCount++;
		_countReadbackBytes += count * 32;
		_countReadbackMilliseconds += readbackMilliseconds;
		var allocationStart = Stopwatch.GetTimestamp();
		var arenas = new HashSet<GeometryArena>();
		for ( var index = 0; index < count; index++ )
		{
			var source = lane.CountInFlight[index];
			var result = counts[index];
			if ( result.Generation != source.Generation || result.RequestIndex != (uint)index )
				throw new InvalidOperationException( "Stale voxel terrain count metadata." );
			GeometryHandle handle = null;
			if ( result.IndexCount > 0 )
			{
				handle = Acquire(
					checked( (int)result.VertexCount ),
					checked( (int)result.IndexCount ),
					source.Generation,
					!lod2 );
				arenas.Add( handle.Arena );
			}
			lane.EmitInFlight.Add( new CandidateMesh(
				source.Descriptor,
				source.Residency,
				source.Generation,
				source.ScheduledTimestamp,
				source.ScheduledRouteDistance,
				handle,
				result ) );
		}
		lane.CountInFlight.Clear();
		var allocationMilliseconds = (float)Stopwatch.GetElapsedTime( allocationStart ).TotalMilliseconds;
		var emitMilliseconds = 0f;
		foreach ( var arena in arenas )
		{
			var allocations = new GpuTerrainAllocationDescriptor[count];
			for ( var index = 0; index < count; index++ )
			{
				var candidate = lane.EmitInFlight[index];
				if ( candidate.Handle?.Arena != arena ) continue;
				allocations[index] = new GpuTerrainAllocationDescriptor
				{
					VertexOffset = (uint)candidate.Handle.Vertices.Offset,
					VertexCapacity = (uint)candidate.Handle.Vertices.Count,
					IndexOffset = (uint)candidate.Handle.Indices.Offset,
					IndexCapacity = (uint)candidate.Handle.Indices.Count,
					Generation = candidate.Generation,
					RequestIndex = (uint)index,
					Enabled = 1
				};
			}
			var arenaEmitMilliseconds = lane.Scratch.SubmitEmitPass( allocations, count, arena.Vertices, arena.Indices );
			_emitSubmissionMilliseconds += arenaEmitMilliseconds;
			emitMilliseconds += (float)arenaEmitMilliseconds;
		}
		lane.Scratch.CompleteEmit();
		lane.SubmittedRenderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
		lane.EmitSubmittedTimestamp = Stopwatch.GetTimestamp();
		if ( !lod2 )
		{
			_throughput?.RecordBatchCompleted(
				(float)readbackMilliseconds,
				(float)callbackWaitMilliseconds,
				allocationMilliseconds,
				emitMilliseconds );
		}
	}

	private void FinalizeEmits()
	{
		if ( _scratchLanes is null ) return;
		var renderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
		var changed = false;
		foreach ( var lane in _scratchLanes )
		{
			if ( lane.EmitInFlight.Count == 0 || renderSequence <= lane.SubmittedRenderSequence ) continue;
			_throughput?.RecordEmitPublished(
				(float)Stopwatch.GetElapsedTime( lane.EmitSubmittedTimestamp ).TotalMilliseconds );
			foreach ( var completed in lane.EmitInFlight )
			{
				var key = completed.Descriptor.Key;
				if ( _cancelledInFlight.Remove( key ) )
				{
					if ( _scheduleLatencyMeasurementActive ) _scheduleLatencyCancelledCount++;
					Release( completed.Handle );
					continue;
				}
				var residency = completed.Residency;
				if ( _pending.TryGetValue( key, out var replacement ) )
				{
					if ( replacement.Descriptor != completed.Descriptor )
					{
						if ( _scheduleLatencyMeasurementActive ) _scheduleLatencySupersededCount++;
						Release( completed.Handle );
						continue;
					}
					residency = replacement.Residency;
					RemovePending( key );
				}
				if ( _resident.Remove( key, out var previous ) ) ReleaseResident( previous );
				var resident = new ResidentMesh( completed.Descriptor, residency, completed.Handle, completed.Counts );
				_resident.Add( key, resident );
				if ( residency == GpuMeshResidency.Warm ) _warmResidentCount++;
				if ( residency == GpuMeshResidency.Lod1 ) _lod1ResidentCount++;
				if ( completed.Handle is not null )
				{
					completed.Handle.Arena.ActiveResidentCount++;
					SetVisibilityActive( resident, _renderActive.Contains( key ) );
				}
				_topologyDigest ^= CoordinateDigest( key, completed.Counts.TopologyDigest );
				_positionDigest ^= CoordinateDigest( key, completed.Counts.PositionDigest );
				if ( key.Level == GpuMeshLevel.Lod0 )
				{
					_lod0TopologyDigest ^= CoordinateDigest( key, completed.Counts.TopologyDigest );
					_lod0PositionDigest ^= CoordinateDigest( key, completed.Counts.PositionDigest );
				}
				else
				{
					_lod1TopologyDigest ^= CoordinateDigest( key, completed.Counts.TopologyDigest );
					_lod1PositionDigest ^= CoordinateDigest( key, completed.Counts.PositionDigest );
				}
				RecordScheduleLatency( completed.ScheduledTimestamp );
				_dispatchCount++;
				_throughput?.RecordPublished(
					MathF.Max( 0f, _currentPlayerRouteDistance - completed.ScheduledRouteDistance ) );
				changed = true;
			}
			lane.EmitInFlight.Clear();
		}
		if ( changed )
		{
		MarkDrawCommandsDirty();
		}
	}

	private void FinalizeLod2Emit()
	{
		var lane = _lod2ScratchLane;
		if ( lane is null || lane.EmitInFlight.Count == 0 ) return;
		var renderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
		if ( renderSequence <= lane.SubmittedRenderSequence ) return;

		var changed = false;
		foreach ( var completed in lane.EmitInFlight )
		{
			var key = completed.Descriptor.Key;
			if ( _cancelledInFlight.Remove( key ) )
			{
				if ( _lod2MeasurementActive ) _lod2CancelledCount++;
				Release( completed.Handle );
				continue;
			}

			var residency = completed.Residency;
			if ( _pending.TryGetValue( key, out var replacement ) )
			{
				if ( replacement.Descriptor != completed.Descriptor )
				{
					if ( _lod2MeasurementActive ) _lod2SupersededCount++;
					Release( completed.Handle );
					continue;
				}
				residency = replacement.Residency;
				RemovePending( key );
			}

			if ( _resident.Remove( key, out var previous ) ) ReleaseResident( previous );
			var resident = new ResidentMesh( completed.Descriptor, residency, completed.Handle, completed.Counts );
			_resident.Add( key, resident );
			_lod2ResidentCount++;
			if ( completed.Handle is not null )
			{
				completed.Handle.Arena.ActiveResidentCount++;
				SetVisibilityActive( resident, _renderActive.Contains( key ) );
			}
			_topologyDigest ^= CoordinateDigest( key, completed.Counts.TopologyDigest );
			_positionDigest ^= CoordinateDigest( key, completed.Counts.PositionDigest );
			_lod2TopologyDigest ^= CoordinateDigest( key, completed.Counts.TopologyDigest );
			_lod2PositionDigest ^= CoordinateDigest( key, completed.Counts.PositionDigest );
			RecordLod2Latency( completed.ScheduledTimestamp );
			if ( _lod2MeasurementActive ) _lod2PublishedCount++;
			_dispatchCount++;
			changed = true;
		}
		lane.EmitInFlight.Clear();
		if ( changed )
		{
			MarkDrawCommandsDirty();
		}
	}

	private void AllocateAndEmitTransitions( TransitionScratchLane lane, GpuTransitionCountResult[] counts,
		int count, double readbackMilliseconds, double callbackWaitMilliseconds )
	{
		if ( count != lane.CountInFlight.Count )
			throw new InvalidOperationException( "Voxel transition count batch length changed." );
		var arenas = new HashSet<GeometryArena>();
		for ( var index = 0; index < count; index++ )
		{
			var source = lane.CountInFlight[index];
			var result = counts[index];
			if ( result.Generation != source.Generation || result.RequestIndex != (uint)index )
				throw new InvalidOperationException( "Stale voxel transition count metadata." );
			GeometryHandle handle = null;
			if ( result.IndexCount > 0 && result.InvalidTableCount == 0 )
			{
				handle = Acquire( checked( (int)result.VertexCount ), checked( (int)result.IndexCount ),
					source.Generation, recordRegularTelemetry: false );
				arenas.Add( handle.Arena );
			}
			lane.EmitInFlight.Add( new CandidateTransition(
				source.Descriptor,
				source.Generation,
				source.ScheduledTimestamp,
				source.ScheduledRouteDistance,
				handle,
				result ) );
		}
		lane.CountInFlight.Clear();
		foreach ( var arena in arenas )
		{
			var allocations = new GpuTerrainAllocationDescriptor[count];
			for ( var index = 0; index < count; index++ )
			{
				var candidate = lane.EmitInFlight[index];
				if ( candidate.Handle?.Arena != arena ) continue;
				allocations[index] = new GpuTerrainAllocationDescriptor
				{
					VertexOffset = (uint)candidate.Handle.Vertices.Offset,
					VertexCapacity = (uint)candidate.Handle.Vertices.Count,
					IndexOffset = (uint)candidate.Handle.Indices.Offset,
					IndexCapacity = (uint)candidate.Handle.Indices.Count,
					Generation = candidate.Generation,
					RequestIndex = (uint)index,
					Enabled = 1
				};
			}
			lane.Scratch.SubmitEmitPass( allocations, count, arena.Vertices, arena.Indices );
		}
		lane.Scratch.CompleteEmit();
		lane.SubmittedRenderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
		lane.EmitSubmittedTimestamp = Stopwatch.GetTimestamp();
	}

	private void FinalizeTransitionEmits()
	{
		if ( _transitionScratchLanes is null ) return;
		var renderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
		var changed = false;
		foreach ( var lane in _transitionScratchLanes )
		{
			if ( lane.EmitInFlight.Count == 0 || renderSequence <= lane.SubmittedRenderSequence ) continue;
			foreach ( var completed in lane.EmitInFlight )
			{
				var key = completed.Descriptor.Key;
				if ( _transitionCancelledGenerations.Remove( completed.Generation ) )
				{
					_transitionCancelledCount++;
					Release( completed.Handle );
					continue;
				}
				if ( !_transitionDesiredDescriptors.TryGetValue( key, out var desired ) ||
					desired != completed.Descriptor )
				{
					_transitionStaleCount++;
					Release( completed.Handle );
					continue;
				}
				if ( _transitionPending.TryGetValue( key, out var replacement ) )
				{
					if ( replacement.Descriptor != completed.Descriptor )
					{
						_transitionStaleCount++;
						Release( completed.Handle );
						continue;
					}
					RemovePendingTransition( key );
				}
				if ( !_transitionRenderActive.Contains( key ) )
				{
					_transitionCancelledCount++;
					Release( completed.Handle );
					continue;
				}
				if ( _transitionResident.Remove( key, out var previous ) ) ReleaseTransitionResident( previous );
				var latencyMilliseconds = (float)Stopwatch.GetElapsedTime(
					completed.ScheduledTimestamp ).TotalMilliseconds;
				var resident = new ResidentTransition(
					completed.Descriptor, completed.Handle, completed.Counts, latencyMilliseconds );
				_transitionResident.Add( key, resident );
				if ( completed.Handle is not null )
				{
					completed.Handle.Arena.ActiveResidentCount++;
					SetTransitionVisibilityActive( resident, true );
				}
				_transitionTopologyDigest ^= TransitionCoordinateDigest( key, completed.Counts.TopologyDigest );
				_transitionPositionDigest ^= TransitionCoordinateDigest( key, completed.Counts.PositionDigest );
				_transitionFineFaceMismatchCount += completed.Counts.FineFaceMismatchCount;
				_transitionCoarseFaceMismatchCount += completed.Counts.CoarseFaceMismatchCount;
				_transitionLateralEdgeDigest ^= TransitionCombinedLateralDigest( completed.Counts );
				_transitionInvalidTableCount += completed.Counts.InvalidTableCount;
				_transitionPublishedCount++;
				RecordTransitionLatency( latencyMilliseconds );
				changed = true;
			}
			lane.EmitInFlight.Clear();
		}
		if ( changed )
		{
			MarkDrawCommandsDirty();
		}
	}

	private static ulong TransitionCoordinateDigest( Lod0Lod1TransitionKey key, uint digest )
	{
		var coordinate = key.Lod1Coordinate;
		ulong value = digest ^ (uint)coordinate.x * 0x9E3779B1u ^
			(uint)coordinate.y * 0x85EBCA77u ^ (uint)coordinate.z * 0xC2B2AE3Du ^
			(uint)key.Face * 0x27D4EB2Du;
		value ^= value >> 30;
		value *= 0xBF58476D1CE4E5B9ul;
		value ^= value >> 27;
		value *= 0x94D049BB133111EBul;
		return value ^ (value >> 31);
	}

	private void RecordTransitionLatency( float milliseconds )
	{
		if ( !_transitionMeasurementActive ) return;
		if ( _transitionLatencySampleCount < _transitionLatencyMilliseconds.Length )
			_transitionLatencyMilliseconds[_transitionLatencySampleCount++] = milliseconds;
		else
			_transitionLatencyTruncatedCount++;
	}

	private static ulong CoordinateDigest( GpuMeshRegionKey key, uint digest )
	{
		var coordinate = key.Coordinate;
		ulong value = digest ^ (uint)coordinate.x * 0x9E3779B1u ^
			(uint)coordinate.y * 0x85EBCA77u ^ (uint)coordinate.z * 0xC2B2AE3Du ^
			(uint)key.Level * 0x27D4EB2Du;
		value ^= value >> 30;
		value *= 0xBF58476D1CE4E5B9ul;
		value ^= value >> 27;
		value *= 0x94D049BB133111EBul;
		return value ^ (value >> 31);
	}

	private void RecordScheduleLatency( long scheduledTimestamp )
	{
		if ( !_scheduleLatencyMeasurementActive ) return;
		var milliseconds = (float)Stopwatch.GetElapsedTime( scheduledTimestamp ).TotalMilliseconds;
		if ( _scheduleLatencySampleCount < _scheduleLatencyMilliseconds.Length )
			_scheduleLatencyMilliseconds[_scheduleLatencySampleCount++] = milliseconds;
		else
			_scheduleLatencyTruncatedCount++;
	}

	private void RecordLod2Latency( long scheduledTimestamp )
	{
		if ( !_lod2MeasurementActive ) return;
		var milliseconds = (float)Stopwatch.GetElapsedTime( scheduledTimestamp ).TotalMilliseconds;
		if ( _lod2LatencySampleCount < _lod2LatencyMilliseconds.Length )
			_lod2LatencyMilliseconds[_lod2LatencySampleCount++] = milliseconds;
		else
			_lod2LatencyTruncatedCount++;
	}

	private GeometryHandle Acquire( int vertexCount, int indexCount, uint generation,
		bool recordRegularTelemetry = true )
	{
		foreach ( var arena in _arenas )
		{
			if ( arena.TryAcquire( vertexCount, indexCount, generation, out var reused ) )
			{
				if ( recordRegularTelemetry ) _poolReuseCount++;
				return reused;
			}
		}
		if ( vertexCount > VertexArenaCapacity || indexCount > IndexArenaCapacity )
			throw new InvalidOperationException( $"Terrain region geometry ({vertexCount} vertices, {indexCount} indices) exceeds an arena." );
		var created = new GeometryArena( _arenas.Count );
		_arenas.Add( created );
		if ( recordRegularTelemetry ) _poolAllocationCount++;
		EnsureVisibilityCapacity( _arenas.Count * RegionsPerSlab );
		if ( !created.TryAcquire( vertexCount, indexCount, generation, out var allocation ) )
			throw new InvalidOperationException( "A new terrain geometry arena rejected a valid allocation." );
		MarkDrawCommandsDirty();
		return allocation;
	}

	private void ReleaseResident( ResidentMesh resident )
	{
		if ( resident.Residency == GpuMeshResidency.Warm ) _warmResidentCount--;
		if ( resident.Residency == GpuMeshResidency.Lod1 ) _lod1ResidentCount--;
		if ( resident.Residency == GpuMeshResidency.Lod2 ) _lod2ResidentCount--;
		_topologyDigest ^= CoordinateDigest( resident.Descriptor.Key, resident.Counts.TopologyDigest );
		_positionDigest ^= CoordinateDigest( resident.Descriptor.Key, resident.Counts.PositionDigest );
		if ( resident.Descriptor.Key.Level == GpuMeshLevel.Lod0 )
		{
			_lod0TopologyDigest ^= CoordinateDigest( resident.Descriptor.Key, resident.Counts.TopologyDigest );
			_lod0PositionDigest ^= CoordinateDigest( resident.Descriptor.Key, resident.Counts.PositionDigest );
		}
		else if ( resident.Descriptor.Key.Level == GpuMeshLevel.Lod1 )
		{
			_lod1TopologyDigest ^= CoordinateDigest( resident.Descriptor.Key, resident.Counts.TopologyDigest );
			_lod1PositionDigest ^= CoordinateDigest( resident.Descriptor.Key, resident.Counts.PositionDigest );
		}
		else
		{
			_lod2TopologyDigest ^= CoordinateDigest( resident.Descriptor.Key, resident.Counts.TopologyDigest );
			_lod2PositionDigest ^= CoordinateDigest( resident.Descriptor.Key, resident.Counts.PositionDigest );
		}
		if ( resident.Handle is null ) return;
		SetVisibilityActive( resident, false );
		resident.Handle.Arena.ActiveResidentCount--;
		Release( resident.Handle );
	}

	private void ReleaseTransitionResident( ResidentTransition resident )
	{
		_transitionTopologyDigest ^= TransitionCoordinateDigest(
			resident.Descriptor.Key, resident.Counts.TopologyDigest );
		_transitionPositionDigest ^= TransitionCoordinateDigest(
			resident.Descriptor.Key, resident.Counts.PositionDigest );
		_transitionFineFaceMismatchCount -= resident.Counts.FineFaceMismatchCount;
		_transitionCoarseFaceMismatchCount -= resident.Counts.CoarseFaceMismatchCount;
		_transitionLateralEdgeDigest ^= TransitionCombinedLateralDigest( resident.Counts );
		_transitionInvalidTableCount -= resident.Counts.InvalidTableCount;
		if ( resident.Handle is null ) return;
		SetTransitionVisibilityActive( resident, false );
		resident.Handle.Arena.ActiveResidentCount--;
		Release( resident.Handle );
	}

	private uint CountTransitionLateralMismatches()
	{
		uint mismatches = 0;
		foreach ( var pair in _transitionResident )
		{
			GetTransitionFaceAxes( pair.Key.Face, out var u, out var v );
			var uNeighborKey = pair.Key with { Lod1Coordinate = pair.Key.Lod1Coordinate + u };
			if ( _transitionResident.TryGetValue( uNeighborKey, out var uNeighbor ) &&
				pair.Value.Counts.MaximumUDigest != uNeighbor.Counts.MinimumUDigest )
			{
				mismatches++;
			}
			var vNeighborKey = pair.Key with { Lod1Coordinate = pair.Key.Lod1Coordinate + v };
			if ( _transitionResident.TryGetValue( vNeighborKey, out var vNeighbor ) &&
				pair.Value.Counts.MaximumVDigest != vNeighbor.Counts.MinimumVDigest )
			{
				mismatches++;
			}
		}
		return mismatches;
	}

	private static uint TransitionCombinedLateralDigest( GpuTransitionCountResult counts ) =>
		counts.MinimumUDigest ^ counts.MaximumUDigest ^ counts.MinimumVDigest ^ counts.MaximumVDigest;

	private static void GetTransitionFaceAxes( Lod0Lod1TransitionFace face,
		out Vector3Int u, out Vector3Int v )
	{
		switch ( face )
		{
			case Lod0Lod1TransitionFace.NegativeX:
				u = new Vector3Int( 0, 1, 0 ); v = new Vector3Int( 0, 0, -1 ); return;
			case Lod0Lod1TransitionFace.PositiveX:
				u = new Vector3Int( 0, 1, 0 ); v = new Vector3Int( 0, 0, 1 ); return;
			case Lod0Lod1TransitionFace.NegativeY:
				u = new Vector3Int( 0, 0, 1 ); v = new Vector3Int( -1, 0, 0 ); return;
			case Lod0Lod1TransitionFace.PositiveY:
				u = new Vector3Int( 0, 0, 1 ); v = new Vector3Int( 1, 0, 0 ); return;
			case Lod0Lod1TransitionFace.NegativeZ:
				u = new Vector3Int( 1, 0, 0 ); v = new Vector3Int( 0, -1, 0 ); return;
			default:
				u = new Vector3Int( 1, 0, 0 ); v = new Vector3Int( 0, 1, 0 ); return;
		}
	}

	private static void Release( GeometryHandle handle ) => handle?.Arena.Release( handle );

	private void QueuePending( PendingMesh pending )
	{
		if ( _pending.ContainsKey( pending.Descriptor.Key ) )
		{
			if ( pending.Residency == GpuMeshResidency.Lod2 )
			{
				if ( _lod2MeasurementActive ) _lod2SupersededCount++;
			}
			else if ( _scheduleLatencyMeasurementActive )
			{
				_scheduleLatencySupersededCount++;
			}
		}
		RemovePending( pending.Descriptor.Key );
		_pending[pending.Descriptor.Key] = pending;
		if ( pending.Residency == GpuMeshResidency.Gameplay )
		{
			_pendingGameplayCount++;
			_gameplayDispatchQueue.Enqueue( pending );
		}
		else if ( pending.Residency == GpuMeshResidency.Lod1 )
		{
			_pendingLod1Count++;
			_lod1DispatchQueue.Enqueue( pending );
		}
		else if ( pending.Residency == GpuMeshResidency.Lod2 )
		{
			_pendingLod2Count++;
			_lod2DispatchQueue.Enqueue( pending );
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
			if ( _pending.TryGetValue( gameplay.Descriptor.Key, out var current ) && current == gameplay )
			{
				_pending.Remove( gameplay.Descriptor.Key );
				_pendingGameplayCount--;
				pending = gameplay;
				return true;
			}
		}
		while ( _lod1DispatchQueue.TryDequeue( out var lod1 ) )
		{
			if ( _pending.TryGetValue( lod1.Descriptor.Key, out var current ) && current == lod1 )
			{
				_pending.Remove( lod1.Descriptor.Key );
				_pendingLod1Count--;
				pending = lod1;
				return true;
			}
		}
		while ( _warmDispatchQueue.TryDequeue( out var warm ) )
		{
			if ( _pending.TryGetValue( warm.Descriptor.Key, out var current ) && current == warm )
			{
				_pending.Remove( warm.Descriptor.Key );
				_pendingWarmCount--;
				pending = warm;
				return true;
			}
		}
		pending = default;
		return false;
	}

	private void RemovePending( GpuMeshRegionKey key )
	{
		if ( !_pending.Remove( key, out var pending ) ) return;
		if ( pending.Residency == GpuMeshResidency.Gameplay ) _pendingGameplayCount--;
		else if ( pending.Residency == GpuMeshResidency.Lod1 ) _pendingLod1Count--;
		else if ( pending.Residency == GpuMeshResidency.Lod2 ) _pendingLod2Count--;
		else _pendingWarmCount--;
	}

	private bool TryDequeuePendingLod2( out PendingMesh pending )
	{
		while ( _lod2DispatchQueue.TryDequeue( out var lod2 ) )
		{
			if ( _pending.TryGetValue( lod2.Descriptor.Key, out var current ) && current == lod2 )
			{
				_pending.Remove( lod2.Descriptor.Key );
				_pendingLod2Count--;
				pending = lod2;
				return true;
			}
		}
		pending = default;
		return false;
	}

	private bool TryDequeuePendingTransition( out PendingTransition pending )
	{
		while ( _transitionDispatchQueue.TryDequeue( out var queued ) )
		{
			if ( _transitionPending.TryGetValue( queued.Descriptor.Key, out var current ) && current == queued )
			{
				_transitionPending.Remove( queued.Descriptor.Key );
				pending = queued;
				return true;
			}
		}
		pending = default;
		return false;
	}

	private void RemovePendingTransition( Lod0Lod1TransitionKey key ) => _transitionPending.Remove( key );

	private int CountInFlight( GpuMeshResidency residency )
	{
		var count = _scratchLanes?.Sum( lane => lane.CountInFlight.Count( value => value.Residency == residency ) +
			lane.EmitInFlight.Count( value => value.Residency == residency ) ) ?? 0;
		if ( _lod2ScratchLane is not null )
		{
			count += _lod2ScratchLane.CountInFlight.Count( value => value.Residency == residency );
			count += _lod2ScratchLane.EmitInFlight.Count( value => value.Residency == residency );
		}
		return count;
	}

	private void SetResidency( ResidentMesh resident, GpuMeshResidency residency )
	{
		if ( resident.Residency == residency ) return;
		if ( resident.Residency == GpuMeshResidency.Warm ) _warmResidentCount--;
		if ( resident.Residency == GpuMeshResidency.Lod1 ) _lod1ResidentCount--;
		if ( resident.Residency == GpuMeshResidency.Lod2 ) _lod2ResidentCount--;
		if ( residency == GpuMeshResidency.Warm ) _warmResidentCount++;
		if ( residency == GpuMeshResidency.Lod1 ) _lod1ResidentCount++;
		if ( residency == GpuMeshResidency.Lod2 ) _lod2ResidentCount++;
		resident.Residency = residency;
		if ( resident.Handle is not null ) SetVisibilityActive( resident, true );
	}

	private void MarkDrawCommandsDirty()
	{
		lock ( _renderCameraLock )
		{
			foreach ( var state in _renderCameraStates.Values ) state.CommandsDirty = true;
		}
	}

	private void MarkVisibilityDescriptorsDirty()
	{
		lock ( _renderCameraLock )
		{
			foreach ( var state in _renderCameraStates.Values )
			{
				state.DescriptorsDirty = true;
				state.CommandsDirty = true;
			}
		}
	}

	private void EnsureVisibilityBuffers( RenderCameraState state )
	{
		if ( _visibilityCapacity == 0 || state.VisibilityCapacity == _visibilityCapacity ) return;
		if ( state.Visibility is not null ) state.RetiredVisibility.Add( state.Visibility );
		var bounds = new GpuBuffer<Vector4>( _visibilityCapacity * 2,
			GpuBuffer.UsageFlags.Structured, "Voxel View Visibility Bounds" );
		var source = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>( _visibilityCapacity,
			GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments,
			"Voxel View Source Indexed Arguments" );
		var visible = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>( _visibilityCapacity,
			GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments,
			"Voxel View Visible Indexed Arguments" );
		var frame = new GpuBuffer<uint>( 11, GpuBuffer.UsageFlags.Structured,
			"Voxel View Visibility Frame Counters" );
		state.Visibility = new VisibilityBuffers( bounds, source, visible, frame );
		state.VisibilityCapacity = _visibilityCapacity;
		state.DescriptorsDirty = true;
		state.CommandsDirty = true;
	}

	private DrawCommandCommitResult CommitDrawCommandsLocked( RenderCameraState state )
	{
		var start = Stopwatch.GetTimestamp();
		EnsureVisibilityBuffers( state );
		if ( state.AggregateResetPending )
		{
			Span<uint> counters = stackalloc uint[20];
			counters[3] = uint.MaxValue;
			state.AggregateCounters.SetData( counters );
			state.AggregateResetPending = false;
		}
		UploadVisibilityDescriptors( state );
		if ( !state.CommandsDirty )
			return new DrawCommandCommitResult( false, 0f );
		var commands = state.Commands;
		var visibility = state.Visibility;
		commands.Reset();
		_drawCommandResetCount++;
		if ( _drawCommandDiagnosticReportCount < 32 )
		{
			_drawCommandDiagnosticReportCount++;
			Log.Info(
				$"[VoxelWorld] gpu.render.command_list_reset total={_drawCommandResetCount} " +
				$"descriptorUploads={_visibilityDescriptorUploadCount} arenas={_arenas.Count} " +
				$"visibilityCapacity={_visibilityCapacity} measurement={_visibilityMeasurementActive} " +
				$"settledCapture={_visibilitySettledCaptureActive} " +
				$"updateEpoch={System.Threading.Interlocked.Read( ref _updateEpoch )} " +
				$"renderSequence={System.Threading.Interlocked.Read( ref _renderSequence )}" );
		}
		if ( _visibilityCapacity > 0 )
		{
			commands.Attributes.Set( "VisibilityBounds", visibility.Bounds );
			commands.Attributes.Set( "SourceIndirectArguments", visibility.SourceArguments );
			commands.Attributes.Set( "VisibleIndirectArguments", visibility.VisibleArguments );
			commands.Attributes.Set( "VisibilityFrameCounters", visibility.FrameCounters );
			commands.Attributes.Set( "VisibilityAggregateCounters", state.AggregateCounters );
			commands.Attributes.Set( "VisibilitySlotCount", _visibilityCapacity );
			commands.Attributes.Set( "VisibilityPass", 0 );
			commands.Attributes.Set( "MeasureVisibility", _visibilityMeasurementActive ? 1 : 0 );
			commands.Attributes.Set( "CaptureSettledDiagnostics", _visibilitySettledCaptureActive ? 1 : 0 );
			commands.ResourceBarrierTransition( visibility.Bounds, ResourceState.GenericRead );
			commands.ResourceBarrierTransition( visibility.SourceArguments, ResourceState.GenericRead );
			commands.ResourceBarrierTransition( visibility.VisibleArguments, ResourceState.UnorderedAccess );
			commands.ResourceBarrierTransition( visibility.FrameCounters, ResourceState.UnorderedAccess );
			commands.Clear( visibility.FrameCounters, 0 );
			commands.DispatchCompute( _visibilityShader, _visibilityCapacity, 1, 1 );
			commands.UavBarrier( visibility.VisibleArguments );
			commands.UavBarrier( visibility.FrameCounters );
			if ( _visibilityMeasurementActive || _visibilitySettledCaptureActive )
			{
				commands.ResourceBarrierTransition( visibility.FrameCounters, ResourceState.GenericRead );
				commands.ResourceBarrierTransition( state.AggregateCounters, ResourceState.UnorderedAccess );
				commands.Attributes.Set( "VisibilityPass", 1 );
				commands.DispatchCompute( _visibilityShader, 1, 1, 1 );
				commands.UavBarrier( state.AggregateCounters );
			}
			commands.ResourceBarrierTransition( visibility.VisibleArguments, ResourceState.IndirectArgument );
			foreach ( var arena in _arenas )
			{
				if ( arena.ActiveResidentCount == 0 ) continue;
				commands.ResourceBarrierTransition( arena.Vertices, ResourceState.VertexOrIndexBuffer );
				commands.ResourceBarrierTransition( arena.Indices, ResourceState.VertexOrIndexBuffer );
				commands.DrawIndexedInstancedIndirect(
					arena.Vertices,
					arena.Indices,
					_material,
					visibility.VisibleArguments,
					(uint)(arena.Index * RegionsPerSlab),
					null,
					Graphics.PrimitiveType.Triangles,
					RegionsPerSlab,
					IndirectArgumentStride );
			}
		}
		state.CommandsDirty = false;
		var elapsedTicks = Stopwatch.GetTimestamp() - start;
		System.Threading.Interlocked.Add( ref _drawCommitStopwatchTicks, elapsedTicks );
		System.Threading.Interlocked.Increment( ref _drawCommitRebuildCount );
		return new DrawCommandCommitResult(
			true,
			(float)(elapsedTicks * 1000.0 / Stopwatch.Frequency) );
	}

	public void BeginVisibilityMeasurement()
	{
		lock ( _visibilityLock )
		{
			_completedVisibilityMeasurement = null;
			_visibilityReadbackPending = false;
			_visibilityReadbackInFlight = false;
		}
		_visibilityMeasurementActive = true;
		_visibilitySettledCaptureActive = false;
		lock ( _renderCameraLock )
		{
			foreach ( var state in _renderCameraStates.Values )
			{
				state.AggregateResetPending = true;
				state.CommandsDirty = true;
			}
		}
	}

	public void StopVisibilityMeasurement()
	{
		_visibilityMeasurementActive = false;
		MarkDrawCommandsDirty();
	}

	public void CaptureSettledVisibilityMeasurement()
	{
		_visibilitySettledCaptureActive = true;
		MarkDrawCommandsDirty();
		lock ( _visibilityLock )
		{
			_visibilityReadbackPending = true;
			_visibilityReadbackRequestedRenderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
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
			MarkDrawCommandsDirty();
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
				System.Threading.Interlocked.Read( ref _renderSequence ) <= _visibilityReadbackRequestedRenderSequence + 1 ||
				_visibilityReadbackState is null ) return;
			_visibilityReadbackPending = false;
			_visibilityReadbackInFlight = true;
			counters = _visibilityReadbackState.AggregateCounters;
			logicalBytes = LogicalVisibilityBytes;
			_visibilityScalarReadbackCount++;
			_scalarReadbackCount++;
		}
		counters.GetDataAsync<uint>( data =>
		{
			var frames = data.Length >= 20 ? data[0] : 0;
			var minimum = data.Length >= 20 && frames > 0 && data[3] != uint.MaxValue ? data[3] : 0;
			lock ( _visibilityLock )
			{
				_completedVisibilityMeasurement = new GpuVisibilityMeasurement(
					frames,
					data.Length >= 20 ? data[1] : 0,
					data.Length >= 20 ? data[2] : 0,
					minimum,
					data.Length >= 20 ? data[4] : 0,
					data.Length >= 20 ? data[5] : 0,
					data.Length >= 20 ? data[6] : 0,
					data.Length >= 20 ? data[7] : 0,
					data.Length >= 20 ? data[8] : 0,
					data.Length >= 20 ? data[9] : 0,
					data.Length >= 20 ? data[10] : 0,
					data.Length >= 20 ? data[11] : 0,
					data.Length >= 20 ? data[12] : 0,
					data.Length >= 20 ? data[13] : 0,
					data.Length >= 20 ? data[14] : 0,
					data.Length >= 20 ? data[15] : 0,
					data.Length >= 20 ? data[16] : 0,
					data.Length >= 20 ? data[17] : 0,
					data.Length >= 20 ? data[18] : 0,
					logicalBytes,
					1 );
				_visibilityReadbackInFlight = false;
			}
		} );
	}

	private void EnsureVisibilityCapacity( int requiredCapacity )
	{
		lock ( _visibilityDescriptorLock )
		{
			if ( requiredCapacity <= _visibilityCapacity ) return;
			EnsureVisibilityCapacityLocked( requiredCapacity );
		}
		MarkVisibilityDescriptorsDirty();
	}

	private void EnsureVisibilityCapacityLocked( int requiredCapacity )
	{
		if ( requiredCapacity <= _visibilityCapacity ) return;
		var newCapacity = Math.Max( RegionsPerSlab, _visibilityCapacity );
		while ( newCapacity < requiredCapacity ) newCapacity = checked( newCapacity * 2 );
		var oldBounds = _visibilityBoundsData;
		var oldArguments = _sourceArgumentData;
		_visibilityCapacity = newCapacity;
		_visibilityBoundsData = new Vector4[newCapacity * 2];
		_sourceArgumentData = new GpuBuffer.IndirectDrawIndexedArguments[newCapacity];
		oldBounds.CopyTo( _visibilityBoundsData, 0 );
		oldArguments.CopyTo( _sourceArgumentData, 0 );
	}

	private void SetVisibilityActive( ResidentMesh resident, bool active )
	{
		if ( resident.Handle is null ) return;
		lock ( _visibilityDescriptorLock ) SetVisibilityActiveLocked( resident, active );
		MarkVisibilityDescriptorsDirty();
	}

	private void SetVisibilityActiveLocked( ResidentMesh resident, bool active )
	{
		if ( resident.Handle is null ) return;
		var descriptor = resident.Descriptor;
		var size = descriptor.CellsPerAxis * descriptor.CellSize;
		var origin = new Vector3(
			descriptor.ChunkCoordinate.x * size,
			descriptor.ChunkCoordinate.y * size,
			descriptor.ChunkCoordinate.z * size );
		var padding = new Vector3( descriptor.CellSize );
		var slot = resident.Handle.GlobalSlot;
		var index = slot * 2;
		var activeResidency = resident.Residency switch
		{
			GpuMeshResidency.Warm => 2f,
			GpuMeshResidency.Lod1 => 3f,
			GpuMeshResidency.Lod2 => 5f,
			_ => 1f
		};
		_visibilityBoundsData[index] = new Vector4( origin - padding, active ? activeResidency : 0 );
		_visibilityBoundsData[index + 1] = new Vector4(
			origin + new Vector3( size ) + padding,
			active ? resident.Counts.ActiveCells : 0 );
		_sourceArgumentData[slot] = active
			? new GpuBuffer.IndirectDrawIndexedArguments
			{
				IndexCount = (uint)resident.Handle.Indices.Count,
				InstanceCount = 1,
				FirstIndex = (uint)resident.Handle.Indices.Offset,
				BaseVertex = resident.Handle.Vertices.Offset,
				FirstInstance = 0
			}
			: default;
	}

	private void SetTransitionVisibilityActive( ResidentTransition resident, bool active )
	{
		if ( resident.Handle is null ) return;
		lock ( _visibilityDescriptorLock ) SetTransitionVisibilityActiveLocked( resident, active );
		MarkVisibilityDescriptorsDirty();
	}

	private void SetTransitionVisibilityActiveLocked( ResidentTransition resident, bool active )
	{
		if ( resident.Handle is null ) return;
		var descriptor = resident.Descriptor;
		var size = descriptor.CellsPerAxis * descriptor.CoarseCellSize;
		var minimum = new Vector3(
			descriptor.Key.Lod1Coordinate.x * size,
			descriptor.Key.Lod1Coordinate.y * size,
			descriptor.Key.Lod1Coordinate.z * size );
		var maximum = minimum + new Vector3( size );
		var padding = descriptor.FineCellSize;
		switch ( descriptor.Key.Face )
		{
			case Lod0Lod1TransitionFace.NegativeX:
				maximum.x = minimum.x;
				break;
			case Lod0Lod1TransitionFace.PositiveX:
				minimum.x = maximum.x;
				break;
			case Lod0Lod1TransitionFace.NegativeY:
				maximum.y = minimum.y;
				break;
			case Lod0Lod1TransitionFace.PositiveY:
				minimum.y = maximum.y;
				break;
			case Lod0Lod1TransitionFace.NegativeZ:
				maximum.z = minimum.z;
				break;
			case Lod0Lod1TransitionFace.PositiveZ:
				minimum.z = maximum.z;
				break;
		}
		var slot = resident.Handle.GlobalSlot;
		var index = slot * 2;
		_visibilityBoundsData[index] = new Vector4( minimum - new Vector3( padding ), active ? 4f : 0f );
		_visibilityBoundsData[index + 1] = new Vector4(
			maximum + new Vector3( padding ), active ? resident.Counts.ActiveCells : 0 );
		_sourceArgumentData[slot] = active
			? new GpuBuffer.IndirectDrawIndexedArguments
			{
				IndexCount = (uint)resident.Handle.Indices.Count,
				InstanceCount = 1,
				FirstIndex = (uint)resident.Handle.Indices.Offset,
				BaseVertex = resident.Handle.Vertices.Offset,
				FirstInstance = 0
			}
			: default;
	}

	private void UploadVisibilityDescriptors( RenderCameraState state )
	{
		lock ( _visibilityDescriptorLock )
		{
			if ( !state.DescriptorsDirty || state.Visibility is null ) return;
			state.Visibility.Bounds.SetData( _visibilityBoundsData );
			state.Visibility.SourceArguments.SetData( _sourceArgumentData );
			state.Visibility.VisibleArguments.SetData( _sourceArgumentData );
			_visibilityDescriptorUploadCount++;
			state.DescriptorsDirty = false;
		}
	}

	public void Clear()
	{
		if ( _scheduleLatencyMeasurementActive )
			_scheduleLatencyCancelledCount += PendingCount;
		if ( _lod2MeasurementActive ) _lod2CancelledCount += PendingLod2Count;
		_pending.Clear();
		_gameplayDispatchQueue.Clear();
		_lod1DispatchQueue.Clear();
		_warmDispatchQueue.Clear();
		_lod2DispatchQueue.Clear();
		_pendingGameplayCount = 0;
		_pendingLod1Count = 0;
		_pendingWarmCount = 0;
		_pendingLod2Count = 0;
		_cancelledInFlight.Clear();
		foreach ( var lane in _scratchLanes ?? Array.Empty<ScratchLane>() )
		{
			foreach ( var candidate in lane.EmitInFlight ) Release( candidate.Handle );
			lane.EmitInFlight.Clear();
			lane.CountInFlight.Clear();
		}
		foreach ( var resident in _resident.Values ) ReleaseResident( resident );
		_resident.Clear();
		_renderActive.Clear();
		_transitionPending.Clear();
		_transitionDispatchQueue.Clear();
		_transitionDesiredDescriptors.Clear();
		_transitionCancelledGenerations.Clear();
		foreach ( var lane in _transitionScratchLanes ?? Array.Empty<TransitionScratchLane>() )
		{
			foreach ( var candidate in lane.EmitInFlight ) Release( candidate.Handle );
			lane.EmitInFlight.Clear();
			lane.CountInFlight.Clear();
		}
		if ( _lod2ScratchLane is not null )
		{
			foreach ( var candidate in _lod2ScratchLane.EmitInFlight ) Release( candidate.Handle );
			_lod2ScratchLane.EmitInFlight.Clear();
			_lod2ScratchLane.CountInFlight.Clear();
		}
		foreach ( var resident in _transitionResident.Values ) ReleaseTransitionResident( resident );
		_transitionResident.Clear();
		_transitionRenderActive.Clear();
		_warmResidentCount = 0;
		_lod1ResidentCount = 0;
		_lod2ResidentCount = 0;
		_topologyDigest = 0;
		_positionDigest = 0;
		_lod0TopologyDigest = 0;
		_lod0PositionDigest = 0;
		_lod1TopologyDigest = 0;
		_lod1PositionDigest = 0;
		_lod2TopologyDigest = 0;
		_lod2PositionDigest = 0;
		_transitionTopologyDigest = 0;
		_transitionPositionDigest = 0;
		_transitionFineFaceMismatchCount = 0;
		_transitionCoarseFaceMismatchCount = 0;
		_transitionLateralEdgeDigest = 0;
		_transitionInvalidTableCount = 0;
		_lod2EligibleSinceTimestamp = 0;
		_lod2LastServiceTimestamp = Stopwatch.GetTimestamp();
		MarkDrawCommandsDirty();
	}

	public void Dispose()
	{
		if ( _disposed ) return;
		_disposed = true;
		Clear();
		lock ( _renderCameraLock )
		{
			var commitTags = _renderCameraStates.Values.Select( state => state.CommitTag ).ToArray();
			foreach ( var state in _renderCameraStates.Values )
			{
				if ( state.Camera.IsValid() )
				{
					foreach ( var commitTag in commitTags )
					{
						if ( commitTag is not null ) state.Camera.RenderExcludeTags.Remove( commitTag );
					}
					state.Camera.RemoveCommandList( state.Commands );
				}
				state.Dispose();
			}
			foreach ( var state in _retiredRenderCameraStates ) state.Dispose();
			_renderCameraStates.Clear();
			_retiredRenderCameraStates.Clear();
		}
		_currentRenderCameras.Clear();
		_leavingRenderCameras.Clear();
		_camera = null;
		DisposeArenas();
		DisposeVisibilityBuffers();
		DisposeScratchLanes();
		DisposeLod2ScratchLane();
		DisposeTransitionScratchLanes();
		_readbackObject?.Delete();
	}

	private void DisposeScratchLanes()
	{
		if ( _scratchLanes is null ) return;
		foreach ( var lane in _scratchLanes ) lane.Scratch.Dispose();
	}

	private void DisposeLod2ScratchLane()
	{
		_lod2ScratchLane?.Scratch.Dispose();
		_lod2ScratchLane = null;
	}

	private void DisposeTransitionScratchLanes()
	{
		if ( _transitionScratchLanes is null ) return;
		foreach ( var lane in _transitionScratchLanes ) lane.Scratch.Dispose();
	}

	private void RefreshRenderCameras()
	{
		lock ( _renderCameraLock )
		{
			_currentRenderCameras.Clear();
			CameraComponent selected = null;
			foreach ( var candidate in _scene.GetAllComponents<CameraComponent>() )
			{
				_currentRenderCameras.Add( candidate );
				selected ??= candidate;
				if ( candidate.IsMainCamera ) selected = candidate;
			}
			_camera = selected;

			_leavingRenderCameras.Clear();
			_leavingRenderCameras.UnionWith( _renderCameraStates.Keys );
			_leavingRenderCameras.ExceptWith( _currentRenderCameras );
			var leavingCount = _leavingRenderCameras.Count;
			foreach ( var camera in _leavingRenderCameras )
			{
				var state = _renderCameraStates[camera];
				foreach ( var currentCamera in _currentRenderCameras )
				{
					if ( currentCamera.IsValid() ) currentCamera.RenderExcludeTags.Remove( state.CommitTag );
				}
				if ( camera.IsValid() )
				{
					foreach ( var otherState in _renderCameraStates.Values )
					{
						if ( !ReferenceEquals( state, otherState ) )
							camera.RenderExcludeTags.Remove( otherState.CommitTag );
					}
					camera.RemoveCommandList( state.Commands );
				}
				state.DetachCommitObject();
				_renderCameraStates.Remove( camera );
				_retiredRenderCameraStates.Add( state );
			}

			_currentRenderCameras.ExceptWith( _renderCameraStates.Keys );
			var enteringCount = _currentRenderCameras.Count;
			foreach ( var camera in _currentRenderCameras )
			{
				var state = new RenderCameraState(
					camera,
					new DrawCommitSceneObject(
						_scene.SceneWorld,
						this,
						$"voxel_view_commit_{++_nextRenderCameraStateId}" ) );
				foreach ( var otherState in _renderCameraStates.Values )
				{
					camera.RenderExcludeTags.Add( otherState.CommitTag );
					if ( otherState.Camera.IsValid() )
						otherState.Camera.RenderExcludeTags.Add( state.CommitTag );
				}
				camera.AddCommandList( state.Commands, Sandbox.Rendering.Stage.AfterOpaque, 0 );
				_renderCameraStates.Add( camera, state );
			}
			_visibilityReadbackState = _camera.IsValid() && _renderCameraStates.TryGetValue( _camera, out var mainState )
				? mainState
				: null;
			if ( (enteringCount > 0 || leavingCount > 0) && _cameraBindingDiagnosticCount < 16 )
			{
				_cameraBindingDiagnosticCount++;
				Log.Info(
					$"[VoxelWorld] gpu.render.camera_bindings attached={_renderCameraStates.Count} " +
					$"entered={enteringCount} left={leavingCount} mainCameraValid={_camera.IsValid()}" );
			}
		}
	}

	private void DisposeArenas()
	{
		foreach ( var arena in _arenas ) arena.Dispose();
		_arenas.Clear();
	}

	private void DisposeVisibilityBuffers()
	{
		lock ( _renderCameraLock )
		{
			foreach ( var state in _renderCameraStates.Values ) state.DisposeVisibility();
			foreach ( var state in _retiredRenderCameraStates ) state.DisposeVisibility();
		}
		_visibilityCapacity = 0;
		_visibilityBoundsData = Array.Empty<Vector4>();
		_sourceArgumentData = Array.Empty<GpuBuffer.IndirectDrawIndexedArguments>();
	}

	private sealed class ThroughputRecorder
	{
		private readonly float _chunkWorldSize;
		private readonly MetricSamples _countSubmission = new( MaximumThroughputBatchSamples );
		private readonly MetricSamples _readback = new( MaximumThroughputBatchSamples );
		private readonly MetricSamples _callbackWait = new( MaximumThroughputBatchSamples );
		private readonly MetricSamples _allocation = new( MaximumThroughputBatchSamples );
		private readonly MetricSamples _emitSubmission = new( MaximumThroughputBatchSamples );
		private readonly MetricSamples _emitPublication = new( MaximumThroughputBatchSamples );
		private readonly MetricSamples _routeLag = new( MaximumScheduleLatencySamples );
		private readonly MetricSamples _gameplayQueue = new( MaximumScheduleLatencySamples );
		private readonly MetricSamples _warmQueue = new( MaximumScheduleLatencySamples );
		private readonly MetricSamples _totalQueue = new( MaximumScheduleLatencySamples );
		private readonly int[] _occupancy = new int[MaximumRegionsPerBatch + 1];
		private bool _moving = true;
		private float _movingSeconds;
		private long _drainStartedTimestamp;
		private float _postLoopDrainMilliseconds;
		private long _scheduled;
		private long _countSubmitted;
		private long _published;
		private long _batchesSubmitted;
		private long _batchesCompleted;
		private int _minimumOccupancy = int.MaxValue;
		private int _maximumOccupancy;

		public ThroughputRecorder( float chunkWorldSize )
		{
			_chunkWorldSize = chunkWorldSize;
		}

		public void RecordScheduled()
		{
			if ( _moving ) _scheduled++;
		}

		public void RecordBatchSubmitted( int occupancy, float submissionMilliseconds )
		{
			if ( !_moving ) return;
			_countSubmitted += occupancy;
			_batchesSubmitted++;
			_minimumOccupancy = Math.Min( _minimumOccupancy, occupancy );
			_maximumOccupancy = Math.Max( _maximumOccupancy, occupancy );
			_occupancy[Math.Clamp( occupancy, 0, MaximumRegionsPerBatch )]++;
			_countSubmission.Record( submissionMilliseconds );
		}

		public void RecordBatchCompleted( float readbackMilliseconds, float callbackWaitMilliseconds,
			float allocationMilliseconds, float emitSubmissionMilliseconds )
		{
			if ( !_moving ) return;
			_batchesCompleted++;
			_readback.Record( readbackMilliseconds );
			_callbackWait.Record( callbackWaitMilliseconds );
			_allocation.Record( allocationMilliseconds );
			_emitSubmission.Record( emitSubmissionMilliseconds );
		}

		public void RecordEmitPublished( float milliseconds )
		{
			if ( _moving ) _emitPublication.Record( milliseconds );
		}

		public void RecordPublished( float routeLagWorldUnits )
		{
			if ( _moving ) _published++;
			_routeLag.Record( routeLagWorldUnits );
		}

		public void SampleQueueDepth( int gameplay, int warm )
		{
			if ( !_moving ) return;
			_gameplayQueue.Record( gameplay );
			_warmQueue.Record( warm );
			_totalQueue.Record( gameplay + warm );
		}

		public void EndMovingWindow( float durationSeconds )
		{
			if ( !_moving ) return;
			_moving = false;
			_movingSeconds = durationSeconds;
			_drainStartedTimestamp = Stopwatch.GetTimestamp();
		}

		public GpuMeshThroughputMeasurement Complete()
		{
			var seconds = MathF.Max( _movingSeconds, float.Epsilon );
			var occupancyHistogram = new int[_occupancy.Length];
			Array.Copy( _occupancy, occupancyHistogram, _occupancy.Length );
			var occupancyTotal = 0L;
			for ( var value = 1; value < _occupancy.Length; value++ )
			{
				occupancyTotal += (long)value * _occupancy[value];
			}
			var lagWorld = _routeLag.Complete();
			return new GpuMeshThroughputMeasurement(
				ScratchLaneCount,
				_scheduled,
				_countSubmitted,
				_published,
				_scheduled / seconds,
				_countSubmitted / seconds,
				_published / seconds,
				_batchesSubmitted,
				_batchesCompleted,
				_batchesSubmitted / seconds,
				_batchesCompleted / seconds,
				_batchesSubmitted > 0 ? (float)occupancyTotal / _batchesSubmitted : 0f,
				_minimumOccupancy == int.MaxValue ? 0 : _minimumOccupancy,
				_maximumOccupancy,
				occupancyHistogram,
				_countSubmission.Complete(),
				_readback.Complete(),
				_callbackWait.Complete(),
				_allocation.Complete(),
				_emitSubmission.Complete(),
				_emitPublication.Complete(),
				_gameplayQueue.CompleteQueue(),
				_warmQueue.CompleteQueue(),
				_totalQueue.CompleteQueue(),
				lagWorld,
				lagWorld.Scale( _chunkWorldSize > 0f ? 1f / _chunkWorldSize : 0f ),
				_postLoopDrainMilliseconds );
		}

		public void MarkSettled()
		{
			if ( _drainStartedTimestamp == 0 || _postLoopDrainMilliseconds > 0f ) return;
			_postLoopDrainMilliseconds = (float)Stopwatch.GetElapsedTime( _drainStartedTimestamp ).TotalMilliseconds;
		}
	}

	private sealed class MetricSamples
	{
		private readonly float[] _values;
		private int _count;
		private int _truncated;
		private double _total;

		public MetricSamples( int capacity )
		{
			_values = new float[capacity];
		}

		public void Record( float value )
		{
			if ( !float.IsFinite( value ) || value < 0f ) return;
			_total += value;
			if ( _count < _values.Length ) _values[_count++] = value;
			else _truncated++;
		}

		public GpuMetricDistribution Complete()
		{
			if ( _count == 0 ) return new GpuMetricDistribution( 0, _truncated, 0, 0, 0, 0, 0 );
			Array.Sort( _values, 0, _count );
			return new GpuMetricDistribution(
				_count,
				_truncated,
				(float)(_total / (_count + _truncated)),
				Percentile( 0.50 ),
				Percentile( 0.95 ),
				Percentile( 0.99 ),
				_values[_count - 1] );
		}

		public GpuQueueDepthMeasurement CompleteQueue()
		{
			var distribution = Complete();
			return new GpuQueueDepthMeasurement(
				distribution.Samples,
				distribution.TruncatedSamples,
				distribution.Average,
				distribution.P50,
				distribution.P95,
				distribution.P99,
				(int)distribution.Maximum );
		}

		private float Percentile( double percentile )
		{
			var index = Math.Clamp( (int)Math.Ceiling( _count * percentile ) - 1, 0, _count - 1 );
			return _values[index];
		}
	}

	private sealed class ResidentMesh
	{
		public GpuSdfDescriptor Descriptor { get; }
		public GpuMeshResidency Residency { get; set; }
		public GeometryHandle Handle { get; }
		public GpuTerrainCountResult Counts { get; }
		public ResidentMesh( GpuSdfDescriptor descriptor, GpuMeshResidency residency, GeometryHandle handle, GpuTerrainCountResult counts )
		{
			Descriptor = descriptor; Residency = residency; Handle = handle; Counts = counts;
		}
	}

	private sealed class ResidentTransition
	{
		public GpuTransitionDescriptor Descriptor { get; }
		public GeometryHandle Handle { get; }
		public GpuTransitionCountResult Counts { get; }
		public float ScheduleToPublicationMilliseconds { get; }
		public ResidentTransition( GpuTransitionDescriptor descriptor, GeometryHandle handle,
			GpuTransitionCountResult counts, float scheduleToPublicationMilliseconds )
		{
			Descriptor = descriptor;
			Handle = handle;
			Counts = counts;
			ScheduleToPublicationMilliseconds = scheduleToPublicationMilliseconds;
		}
	}

	private sealed class ScratchLane
	{
		public GpuTerrainScratch Scratch { get; }
		public List<InFlightMesh> CountInFlight { get; } = new( MaximumRegionsPerBatch );
		public List<CandidateMesh> EmitInFlight { get; } = new( MaximumRegionsPerBatch );
		public long SubmittedRenderSequence { get; set; }
		public long EmitSubmittedTimestamp { get; set; }
		public bool IsIdle => CountInFlight.Count == 0 && EmitInFlight.Count == 0 && Scratch.IsIdle;

		public ScratchLane( int cellsPerAxis )
		{
			Scratch = new GpuTerrainScratch( cellsPerAxis );
		}
	}

	private sealed class TransitionScratchLane
	{
		public GpuTransitionScratch Scratch { get; } = new();
		public List<InFlightTransition> CountInFlight { get; } = new( MaximumRegionsPerBatch );
		public List<CandidateTransition> EmitInFlight { get; } = new( MaximumRegionsPerBatch );
		public long SubmittedRenderSequence { get; set; }
		public long EmitSubmittedTimestamp { get; set; }
		public bool IsIdle => CountInFlight.Count == 0 && EmitInFlight.Count == 0 && Scratch.IsIdle;
	}

	private readonly record struct PendingMesh( GpuSdfDescriptor Descriptor, GpuMeshResidency Residency,
		long ScheduledTimestamp, float ScheduledRouteDistance );
	private readonly record struct InFlightMesh( GpuSdfDescriptor Descriptor, GpuMeshResidency Residency,
		uint Generation, long ScheduledTimestamp, float ScheduledRouteDistance );
	private readonly record struct CandidateMesh( GpuSdfDescriptor Descriptor, GpuMeshResidency Residency,
		uint Generation, long ScheduledTimestamp, float ScheduledRouteDistance,
		GeometryHandle Handle, GpuTerrainCountResult Counts );
	private readonly record struct PendingTransition( GpuTransitionDescriptor Descriptor,
		long ScheduledTimestamp, float ScheduledRouteDistance );
	private readonly record struct InFlightTransition( GpuTransitionDescriptor Descriptor,
		uint Generation, long ScheduledTimestamp, float ScheduledRouteDistance );
	private readonly record struct CandidateTransition( GpuTransitionDescriptor Descriptor,
		uint Generation, long ScheduledTimestamp, float ScheduledRouteDistance,
		GeometryHandle Handle, GpuTransitionCountResult Counts );

	private sealed class GeometryHandle
	{
		public GeometryArena Arena { get; }
		public int Slot { get; }
		public uint Generation { get; }
		public GpuTerrainRange Vertices { get; }
		public GpuTerrainRange Indices { get; }
		public int GlobalSlot => Arena.Index * RegionsPerSlab + Slot;
		public GeometryHandle( GeometryArena arena, int slot, uint generation, GpuTerrainRange vertices, GpuTerrainRange indices )
		{
			Arena = arena; Slot = slot; Generation = generation; Vertices = vertices; Indices = indices;
		}
	}

	private sealed class GeometryArena : IDisposable
	{
		private readonly bool[] _occupied = new bool[RegionsPerSlab];
		private readonly Stack<int> _freeSlots = new( RegionsPerSlab );
		private readonly GpuTerrainRangeAllocator _vertices = new( VertexArenaCapacity );
		private readonly GpuTerrainRangeAllocator _indices = new( IndexArenaCapacity );
		public int Index { get; }
		public int ActiveResidentCount { get; set; }
		public int FreeSlotCount => _freeSlots.Count;
		public int VertexUsed => _vertices.UsedCount;
		public int IndexUsed => _indices.UsedCount;
		public int VertexFree => _vertices.FreeCount;
		public int IndexFree => _indices.FreeCount;
		public int VertexLargestFree => _vertices.LargestFreeRange;
		public int IndexLargestFree => _indices.LargestFreeRange;
		public int VertexFreeRangeCount => _vertices.FreeRangeCount;
		public int IndexFreeRangeCount => _indices.FreeRangeCount;
		public GpuBuffer<TerrainVertex> Vertices { get; }
		public GpuBuffer<uint> Indices { get; }

		public GeometryArena( int index )
		{
			Index = index;
			Vertices = new GpuBuffer<TerrainVertex>( VertexArenaCapacity,
				GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Vertex, $"Voxel Terrain Arena Vertices {index}" );
			Indices = new GpuBuffer<uint>( IndexArenaCapacity,
				GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Index, $"Voxel Terrain Arena Indices {index}" );
			for ( var slot = RegionsPerSlab - 1; slot >= 0; slot-- ) _freeSlots.Push( slot );
		}

		public bool TryAcquire( int vertexCount, int indexCount, uint generation, out GeometryHandle handle )
		{
			if ( _freeSlots.Count == 0 || !_vertices.TryAllocate( vertexCount, out var vertices ) )
			{
				handle = null;
				return false;
			}
			if ( !_indices.TryAllocate( indexCount, out var indices ) )
			{
				_vertices.Release( vertices );
				handle = null;
				return false;
			}
			var slot = _freeSlots.Pop();
			_occupied[slot] = true;
			handle = new GeometryHandle( this, slot, generation, vertices, indices );
			return true;
		}

		public void Release( GeometryHandle handle )
		{
			if ( handle.Arena != this || handle.Slot < 0 || handle.Slot >= RegionsPerSlab || !_occupied[handle.Slot] )
				throw new InvalidOperationException( "Invalid terrain geometry handle release." );
			_occupied[handle.Slot] = false;
			_vertices.Release( handle.Vertices );
			_indices.Release( handle.Indices );
			_freeSlots.Push( handle.Slot );
		}

		public void Dispose() { Vertices.Dispose(); Indices.Dispose(); }
	}

	private sealed class RenderCameraState : IDisposable
	{
		public CameraComponent Camera { get; }
		public DrawCommitSceneObject CommitObject { get; private set; }
		public string CommitTag => CommitObject?.CommitTag;
		public Sandbox.Rendering.CommandList Commands { get; } =
			new( "Voxel Terrain Indexed Indirect Draws" );
		public List<VisibilityBuffers> RetiredVisibility { get; } = new();
		public GpuBuffer<uint> AggregateCounters { get; } = new(
			20, GpuBuffer.UsageFlags.Structured, "Voxel View Visibility Aggregate Counters" );
		public VisibilityBuffers Visibility { get; set; }
		public int VisibilityCapacity { get; set; }
		public bool CommandsDirty { get; set; } = true;
		public bool DescriptorsDirty { get; set; } = true;
		public bool AggregateResetPending { get; set; } = true;

		public RenderCameraState( CameraComponent camera, DrawCommitSceneObject commitObject )
		{
			Camera = camera;
			CommitObject = commitObject;
			commitObject.State = this;
		}

		public void DetachCommitObject()
		{
			CommitObject?.Delete();
			CommitObject = null;
		}

		public void DisposeVisibility()
		{
			Visibility?.Dispose();
			Visibility = null;
			foreach ( var retired in RetiredVisibility ) retired.Dispose();
			RetiredVisibility.Clear();
			VisibilityCapacity = 0;
			CommandsDirty = true;
			DescriptorsDirty = true;
		}

		public void Dispose()
		{
			DetachCommitObject();
			DisposeVisibility();
			AggregateCounters.Dispose();
		}
	}

	private sealed class VisibilityBuffers : IDisposable
	{
		public GpuBuffer<Vector4> Bounds { get; }
		public GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> SourceArguments { get; }
		public GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> VisibleArguments { get; }
		public GpuBuffer<uint> FrameCounters { get; }
		public VisibilityBuffers( GpuBuffer<Vector4> bounds, GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> source,
			GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> visible, GpuBuffer<uint> frame )
		{
			Bounds = bounds; SourceArguments = source; VisibleArguments = visible; FrameCounters = frame;
		}
		public void Dispose() { Bounds.Dispose(); SourceArguments.Dispose(); VisibleArguments.Dispose(); FrameCounters.Dispose(); }
	}

	private sealed class ReadbackSceneObject : SceneCustomObject
	{
		private readonly GpuVoxelMesher _owner;
		public ReadbackSceneObject( SceneWorld world, GpuVoxelMesher owner ) : base( world )
		{
			_owner = owner;
			Bounds = BBox.FromPositionAndSize( Vector3.Zero, Vector3.One * 1_000_000_000f );
		}

		public override void RenderSceneObject()
		{
			_owner.ObserveRenderView();
			if ( _owner.TryBeginGpuRenderTick() )
			{
				try
				{
					System.Threading.Interlocked.Increment( ref _owner._renderSequence );
					_owner.TraceRenderHandoffPhase( "callback.claimed" );
					_owner.ProcessGpuRenderTick();
					_owner.TraceRenderHandoffPhase( "visibility.readback.begin" );
					_owner.ProcessVisibilityReadback();
					_owner.TraceRenderHandoffPhase( "visibility.readback.end" );
				}
				finally
				{
					_owner.EndGpuRenderTick();
				}
			}
		}
	}

	private sealed class DrawCommitSceneObject : SceneCustomObject
	{
		private readonly GpuVoxelMesher _owner;
		public string CommitTag { get; }
		public RenderCameraState State { get; set; }

		public DrawCommitSceneObject( SceneWorld world, GpuVoxelMesher owner, string commitTag ) : base( world )
		{
			_owner = owner;
			CommitTag = commitTag;
			Tags.Add( commitTag );
			Bounds = BBox.FromPositionAndSize( Vector3.Zero, Vector3.One * 1_000_000_000f );
		}

		public override void RenderSceneObject()
		{
			if ( State is not null ) _owner.CommitDrawCommandsForView( State );
		}
	}

}

internal readonly record struct DrawCommandCommitResult( bool Rebuilt, float Milliseconds );
internal readonly record struct GpuMeshScheduleLatencyMeasurement(
	int Samples, int TruncatedSamples, float P50Milliseconds, float P95Milliseconds,
	float P99Milliseconds, float MaximumMilliseconds, int Cancelled, int Superseded );
internal readonly record struct GpuLod2Measurement(
	long Scheduled,
	long Published,
	long Cancelled,
	long Superseded,
	long OpportunisticServices,
	long ForcedServices,
	float MaximumServiceGapMilliseconds,
	GpuQueueDepthMeasurement Queue,
	GpuMeshScheduleLatencyMeasurement ScheduleToRenderable );
internal readonly record struct GpuTransitionMeasurement(
	int Desired,
	int Ready,
	int Drawable,
	int Pending,
	long Scheduled,
	long Published,
	long Cancelled,
	long Stale,
	long Vertices,
	long Indices,
	long ActiveCells,
	string TopologyDigest,
	string PositionDigest,
	uint FineFaceMismatchCount,
	uint CoarseFaceMismatchCount,
	uint LateralEdgeDigest,
	uint LateralMismatchCount,
	uint InvalidTableCount,
	GpuTransitionFaceMeasurement[] Faces,
	GpuMetricDistribution ScheduleToPublication );
internal readonly record struct GpuTransitionIdentitySnapshot( int Count, ulong Digest );
internal readonly record struct GpuTransitionFaceMeasurement(
	Lod0Lod1TransitionKey Key,
	uint Generation,
	int Arena,
	int Slot,
	int VertexOffset,
	int VertexCount,
	int IndexOffset,
	int IndexCount,
	uint ActiveCells,
	float ScheduleToPublicationMilliseconds,
	uint TopologyDigest,
	uint PositionDigest,
	uint FineFaceMismatchCount,
	uint CoarseFaceMismatchCount,
	uint MinimumUDigest,
	uint MaximumUDigest,
	uint MinimumVDigest,
	uint MaximumVDigest,
	uint InvalidTableCount );
internal readonly record struct GpuMetricDistribution(
	int Samples, int TruncatedSamples, float Average, float P50, float P95, float P99, float Maximum )
{
	public GpuMetricDistribution Scale( float scale ) => new(
		Samples, TruncatedSamples, Average * scale, P50 * scale, P95 * scale, P99 * scale, Maximum * scale );
}
internal readonly record struct GpuQueueDepthMeasurement(
	int Samples, int TruncatedSamples, float Average, float P50, float P95, float P99, int Maximum );
internal readonly record struct GpuMeshThroughputMeasurement(
	int ScratchLanes,
	long RegionsScheduled,
	long RegionsCountSubmitted,
	long RegionsPublished,
	float RegionsScheduledPerSecond,
	float RegionsCountSubmittedPerSecond,
	float RegionsPublishedPerSecond,
	long BatchesSubmitted,
	long BatchesCompleted,
	float BatchesSubmittedPerSecond,
	float BatchesCompletedPerSecond,
	float AverageBatchOccupancy,
	int MinimumBatchOccupancy,
	int MaximumBatchOccupancy,
	int[] BatchOccupancyHistogram,
	GpuMetricDistribution CountSubmissionMilliseconds,
	GpuMetricDistribution CountReadbackMilliseconds,
	GpuMetricDistribution CountCallbackWaitMilliseconds,
	GpuMetricDistribution CpuAllocationMilliseconds,
	GpuMetricDistribution EmitSubmissionMilliseconds,
	GpuMetricDistribution EmitToPublicationMilliseconds,
	GpuQueueDepthMeasurement GameplayQueue,
	GpuQueueDepthMeasurement WarmQueue,
	GpuQueueDepthMeasurement TotalQueue,
	GpuMetricDistribution PlayerRouteLagWorldUnits,
	GpuMetricDistribution PlayerRouteLagChunks,
	float PostLoopDrainMilliseconds );
internal readonly record struct GpuVisibilityMeasurement(
	uint FrameCount, uint ResidentTotal, uint VisibleTotal, uint MinimumVisible, uint MaximumVisible,
	uint WarmTotal, uint SettledSurfaceMeshes, uint SettledWarmSurfaceMeshes, uint SettledActiveCells,
	uint SettledMaximumActiveCells, uint Lod0ResidentTotal, uint Lod1ResidentTotal,
	uint Lod0VisibleTotal, uint Lod1VisibleTotal, uint SettledLod0SurfaceMeshes,
	uint SettledLod1SurfaceMeshes, uint Lod2ResidentTotal, uint Lod2VisibleTotal,
	uint SettledLod2SurfaceMeshes, long LogicalBufferBytes, long ScalarReadbacks )
{
	public float AverageResident => FrameCount > 0 ? (float)ResidentTotal / FrameCount : 0;
	public float AverageVisible => FrameCount > 0 ? (float)VisibleTotal / FrameCount : 0;
	public float AverageWarm => FrameCount > 0 ? (float)WarmTotal / FrameCount : 0;
	public float AverageLod0Resident => FrameCount > 0 ? (float)Lod0ResidentTotal / FrameCount : 0;
	public float AverageLod1Resident => FrameCount > 0 ? (float)Lod1ResidentTotal / FrameCount : 0;
	public float AverageLod2Resident => FrameCount > 0 ? (float)Lod2ResidentTotal / FrameCount : 0;
	public float AverageLod0Visible => FrameCount > 0 ? (float)Lod0VisibleTotal / FrameCount : 0;
	public float AverageLod1Visible => FrameCount > 0 ? (float)Lod1VisibleTotal / FrameCount : 0;
	public float AverageLod2Visible => FrameCount > 0 ? (float)Lod2VisibleTotal / FrameCount : 0;
	public float AverageCulled => MathF.Max( 0, AverageResident - AverageVisible );
	public float CulledPercent => AverageResident > 0 ? AverageCulled * 100 / AverageResident : 0;
}
internal enum GpuMeshResidency { Gameplay, Lod1, Lod2, Warm }
