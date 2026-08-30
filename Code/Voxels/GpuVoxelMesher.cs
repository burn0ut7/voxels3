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
	public const int MaximumClipLevels = 7;
	private const int VisibilityFrameCounterCount = 5;
	private const int VisibilityAggregateCounterCount = 10;
	private const int VertexArenaBytes = 32 * 1024 * 1024;
	private const int IndexArenaBytes = 16 * 1024 * 1024;
	private const int VertexArenaCapacity = VertexArenaBytes / TerrainVertexBytes;
	private const int IndexArenaCapacity = IndexArenaBytes / sizeof( uint );
	private const int IndirectArgumentStride = sizeof( uint ) * 5;
	private const int MaximumScheduleLatencySamples = 524288;
	private const int MaximumThroughputBatchSamples = 65536;
	private const int VisibilityVectorsPerRecord = 5;
	private const int DescriptorVectorOffset = 2;

	private readonly Scene _scene;
	private readonly ComputeShader _visibilityShader = new( "shaders/voxels/voxel_chunk_visibility_cs.shader" );
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_terrain.shader" );
	private readonly Sandbox.Rendering.CommandList _drawCommands = new( "Voxel Terrain Indexed Indirect Draws" );
	private readonly Dictionary<VoxelRenderRegionKey, ResidentMesh> _resident = new();
	private readonly Dictionary<VoxelRenderRegionKey, PendingMesh> _pending = new();
	private readonly Queue<PendingMesh> _gameplayDispatchQueue = new();
	private readonly Queue<PendingMesh> _warmDispatchQueue = new();
	private readonly Queue<PendingMesh> _transitionDispatchQueue = new();
	private readonly List<GeometryArena> _arenas = new();
	private readonly List<VoxelRenderRegionKey> _preparedResidentRemovals = new();
	private readonly HashSet<VoxelRenderRegionKey> _preparedCoverageChanges = new();
	private readonly List<VoxelRenderRegionKey> _preparedCoverageApplyQueue = new();
	private ScratchLane[] _scratchLanes;
	private readonly HashSet<InFlightIdentity> _cancelledInFlight = new();
	private readonly List<VisibilityBuffers> _retiredVisibilityBuffers = new();
	private readonly object _visibilityLock = new();
	private readonly ReadbackSceneObject _readbackObject;
	private readonly GpuBuffer<uint> _visibilityAggregateCounters = new( VisibilityAggregateCounterCount, GpuBuffer.UsageFlags.Structured, "Voxel Visibility Aggregate Counters" );
	private CameraComponent _camera;
	private int _cellsPerAxis;
	private int _visibilityCapacity;
	private int _pendingGameplayCount;
	private int _pendingWarmCount;
	private int _warmResidentCount;
	private uint _nextGeneration;
	private Vector4[] _visibilityBoundsData = Array.Empty<Vector4>();
	private GpuBuffer.IndirectDrawIndexedArguments[] _sourceArgumentData = Array.Empty<GpuBuffer.IndirectDrawIndexedArguments>();
	private VisibilityBuffers _visibilityBuffers;
	private bool _drawCommandsDirty;
	private bool _visibilityDescriptorsDirty;
	private bool _visibilityMeasurementActive;
	private bool _visibilitySettledCaptureActive;
	private bool _visibilityReadbackPending;
	private bool _visibilityReadbackInFlight;
	private VoxelClipBoxSelection _preparedClipSelection;
	private VoxelClipBoxSelection _publishedClipSelection;
	private VoxelClipBoxDelta _preparedClipDelta;
	private int _preparedClipPlacementRevision;
	private int _preparedMinimumLod = MaximumClipLevels;
	private int _publishedMinimumLod = MaximumClipLevels;
	private bool _preparedProgressiveRefinement;
	private int _preparedClipBank = 1;
	private int _publishedClipBank;
	private int _stalePublicationCount;
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
	private int _maximumDispatchesRequested = MaximumDispatchesPerUpdate;
	private int _processedRenderDispatches;
	private ulong _topologyDigest;
	private ulong _positionDigest;
	private float[] _scheduleLatencyMilliseconds = Array.Empty<float>();
	private int _scheduleLatencySampleCount;
	private int _scheduleLatencyTruncatedCount;
	private int _scheduleLatencyCancelledCount;
	private int _scheduleLatencySupersededCount;
	private bool _scheduleLatencyMeasurementActive;
	private ThroughputRecorder _throughput;
	private float _currentPlayerRouteDistance;
	private bool _disposed;

	public int ResidentCount => _resident.Count;
	public int GeometryResidentCount => _resident.Count( pair => pair.Value.Handle is not null );
	public int PendingCount => PendingGameplayCount + PendingWarmCount;
	public int PendingGameplayCount => _pendingGameplayCount + CountInFlight( GpuMeshResidency.Gameplay );
	public int PendingWarmCount => _pendingWarmCount + CountInFlight( GpuMeshResidency.Clip );
	public int WarmResidentCount => _warmResidentCount;
	public int ResidentRegularCount => _resident.Count( pair => pair.Key.MeshKind == VoxelRenderMeshKind.Regular );
	public int ActiveRegularCount => _resident.Count( pair =>
		pair.Key.MeshKind == VoxelRenderMeshKind.Regular && IsPublishedActive( pair.Value ) );
	public int ResidentTransitionCount => _resident.Count( pair =>
		pair.Key.MeshKind == VoxelRenderMeshKind.Transition );
	public int ActiveTransitionCount => _resident.Count( pair =>
		pair.Key.MeshKind == VoxelRenderMeshKind.Transition && IsPublishedActive( pair.Value ) );
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
	public long UniqueVertexCount => _arenas.Sum( arena => (long)arena.VertexUsed );
	public long IndexCount => _arenas.Sum( arena => (long)arena.IndexUsed );
	public long TriangleCount => IndexCount / 3;
	public long UsedVertexBytes => UniqueVertexCount * TerrainVertexBytes;
	public long UsedIndexBytes => IndexCount * sizeof( uint );
	public long CommittedVertexBytes => (long)_arenas.Count * VertexArenaBytes;
	public long CommittedIndexBytes => (long)_arenas.Count * IndexArenaBytes;
	public long TransientScratchBytes => _scratchLanes?.Sum( lane => lane.Scratch.CapacityBytes ) ?? 0;
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
	public long RenderSequence => System.Threading.Interlocked.Read( ref _renderSequence );
	public int StalePublicationCount => _stalePublicationCount;
	public long LogicalVisibilityBytes => _visibilityCapacity == 0 ? 0 :
		(long)_visibilityCapacity * (sizeof( float ) * VisibilityVectorsPerRecord * 4 + IndirectArgumentStride * 2) +
		sizeof( uint ) * (VisibilityFrameCounterCount + VisibilityAggregateCounterCount);

	public GpuPublishedCoverageValidation ValidatePublishedCoverage(
		VoxelClipBoxSelection selection,
		int minimumLod )
	{
		var expectedRegular = selection.GetActiveRegularCount( minimumLod );
		var expectedTransitions = selection.GetTransitionFaceCount( minimumLod );
		var actualRegular = 0;
		var actualTransitions = 0;
		var matchedRegular = 0;
		var matchedTransitions = 0;
		var unexpectedActive = 0;
		VoxelRenderRegionKey? firstUnexpected = null;
		foreach ( var pair in _resident )
		{
			if ( !IsPublishedActive( pair.Value ) ) continue;
			var key = pair.Key;
			var expected = false;
			if ( key.MeshKind == VoxelRenderMeshKind.Regular )
			{
				actualRegular++;
				expected = selection.ContainsPublishedActiveRegular( key, minimumLod );
				if ( expected ) matchedRegular++;
			}
			else
			{
				actualTransitions++;
				expected = selection.ContainsPublishedTransitionFace( key, minimumLod );
				if ( expected ) matchedTransitions++;
			}

			if ( expected ) continue;
			unexpectedActive++;
			firstUnexpected ??= key;
		}

		return new GpuPublishedCoverageValidation(
			expectedRegular,
			actualRegular,
			expectedTransitions,
			actualTransitions,
			Math.Max( 0, expectedRegular - matchedRegular ),
			Math.Max( 0, expectedTransitions - matchedTransitions ),
			unexpectedActive,
			firstUnexpected );
	}

	public GpuClipLevelMeasurement[] CaptureClipLevelMeasurements( VoxelClipBoxSelection selection )
	{
		if ( selection is null ) return Array.Empty<GpuClipLevelMeasurement>();
		var result = new GpuClipLevelMeasurement[selection.MaximumLod + 1];
		for ( var lod = 0; lod <= selection.MaximumLod; lod++ )
		{
			var regular = _resident.Where( pair =>
				pair.Key.Lod == lod && pair.Key.MeshKind == VoxelRenderMeshKind.Regular ).ToArray();
			var transitions = _resident.Where( pair =>
				pair.Key.Lod == lod && pair.Key.MeshKind == VoxelRenderMeshKind.Transition ).ToArray();
			ulong topologyDigest = 0;
			ulong positionDigest = 0;
			foreach ( var pair in regular.Concat( transitions ) )
			{
				topologyDigest ^= RegionDigest( pair.Key, pair.Value.Counts.TopologyDigest );
				positionDigest ^= RegionDigest( pair.Key, pair.Value.Counts.PositionDigest );
			}
			result[lod] = new GpuClipLevelMeasurement(
				lod,
				selection.ResidentRegular.Count( key => key.Lod == lod ),
				regular.Length,
				regular.Count( pair => IsPublishedActive( pair.Value ) ),
				regular.Count( pair => !IsPublishedActive( pair.Value ) ),
				selection.TransitionFaces.Count( key => key.Lod == lod ),
				transitions.Length,
				transitions.Count( pair => IsPublishedActive( pair.Value ) ),
				regular.Sum( pair => (long)pair.Value.Counts.IndexCount / 3 ),
				transitions.Sum( pair => (long)pair.Value.Counts.IndexCount / 3 ),
				regular.Sum( pair => (long)pair.Value.Counts.VertexCount * TerrainVertexBytes +
					(long)pair.Value.Counts.IndexCount * sizeof( uint ) ),
				transitions.Sum( pair => (long)pair.Value.Counts.VertexCount * TerrainVertexBytes +
					(long)pair.Value.Counts.IndexCount * sizeof( uint ) ),
				topologyDigest,
				positionDigest );
		}
		return result;
	}

	public GpuVoxelMesher( Scene scene, int cellsPerAxis )
	{
		_scene = scene;
		_cellsPerAxis = cellsPerAxis;
		_scratchLanes = CreateScratchLanes( cellsPerAxis );
		_readbackObject = new ReadbackSceneObject( scene.SceneWorld, this );
		Sandbox.Diagnostics.GpuProfilerStats.Enabled = true;
		AttachToMainCamera();
	}

	public void PrepareClipCoverage(
		VoxelClipBoxSelection selection,
		VoxelClipBoxDelta delta,
		int placementRevision,
		int minimumLod,
		bool progressiveRefinement )
	{
		_preparedClipSelection = selection ?? throw new ArgumentNullException( nameof( selection ) );
		if ( minimumLod < 0 || minimumLod > selection.MaximumLod )
			throw new ArgumentOutOfRangeException( nameof( minimumLod ) );
		delta ??= VoxelClipBoxDelta.Build( _publishedClipSelection, selection );
		_preparedClipDelta = delta;
		_preparedClipPlacementRevision = placementRevision;
		_preparedMinimumLod = minimumLod;
		_preparedProgressiveRefinement = progressiveRefinement;
		_preparedClipBank = 1 - _publishedClipBank;
		_preparedResidentRemovals.Clear();
		_preparedCoverageChanges.Clear();
		_preparedCoverageApplyQueue.Clear();
		foreach ( var key in delta.EnumerateLeavingRegular( 0 ) )
		{
			AddPreparedRemovalFamily( key );
		}
		for ( var lod = 1; lod <= delta.MaximumLod; lod++ )
		{
			foreach ( var key in delta.EnumerateLeavingRegular( lod ) ) AddPreparedRemovalFamily( key );
		}

		foreach ( var key in delta.EnumerateCoverageChanges() )
		{
			if ( progressiveRefinement && key.Lod < minimumLod ) continue;
			if ( _resident.TryGetValue( key, out var resident ) && resident.Handle is not null )
				_preparedCoverageChanges.Add( key );
		}
		if ( progressiveRefinement )
		{
			foreach ( var resident in _resident.Values )
			{
				if ( resident.Handle is null ) continue;
				var key = resident.Descriptor.Key;
				var oldRegularActive = _publishedClipSelection is not null &&
					_publishedClipSelection.ContainsPublishedActiveRegular( key, _publishedMinimumLod );
				var newRegularActive = selection.ContainsPublishedActiveRegular( key, minimumLod );
				var oldTransitionActive = _publishedClipSelection is not null &&
					_publishedClipSelection.ContainsPublishedTransitionFace( key, _publishedMinimumLod );
				var newTransitionActive = selection.ContainsPublishedTransitionFace( key, minimumLod );
				if ( oldRegularActive != newRegularActive || oldTransitionActive != newTransitionActive )
					_preparedCoverageChanges.Add( key );
			}
		}
		_preparedCoverageApplyQueue.AddRange( _preparedCoverageChanges );
	}

	public int ProcessPreparedClipCoverage( float budgetMilliseconds )
	{
		if ( _preparedCoverageApplyQueue.Count == 0 || budgetMilliseconds <= 0f ) return 0;
		var start = Stopwatch.GetTimestamp();
		var processed = 0;
		while ( _preparedCoverageApplyQueue.Count > 0 )
		{
			var last = _preparedCoverageApplyQueue.Count - 1;
			var key = _preparedCoverageApplyQueue[last];
			_preparedCoverageApplyQueue.RemoveAt( last );
			if ( _resident.TryGetValue( key, out var resident ) ) ApplyPreparedCoverage( resident );
			processed++;
			if ( (processed & 31) == 0 &&
				Stopwatch.GetElapsedTime( start ).TotalMilliseconds >= budgetMilliseconds ) break;
		}
		return processed;
	}

	private void AddPreparedRemovalFamily( VoxelRenderRegionKey regular )
	{
		_preparedResidentRemovals.Add( regular );
		for ( var face = VoxelTransitionFace.NegativeX;
			face <= VoxelTransitionFace.PositiveZ; face++ )
		{
			_preparedResidentRemovals.Add( VoxelRenderRegionKey.Transition(
				regular.Lod,
				regular.Coordinate,
				face ) );
		}
	}

	private void ApplyPreparedCoverage( ResidentMesh resident )
	{
		if ( _preparedClipSelection is null || resident.Handle is null ) return;
		var key = resident.Descriptor.Key;
		var regularOwner = VoxelRenderRegionKey.Regular( key.Lod, key.Coordinate );
		var residentMember = _preparedClipSelection.ContainsPublishedResidentRegular(
			regularOwner, _preparedMinimumLod );
		var targetActive = key.MeshKind == VoxelRenderMeshKind.Regular
			? residentMember && _preparedClipSelection.ContainsPublishedActiveRegular(
				key, _preparedMinimumLod )
			: residentMember && _preparedClipSelection.ContainsPublishedTransitionFace(
				key, _preparedMinimumLod );
		var transitionMask = _preparedClipSelection.TryGetTransitionMask(
			regularOwner, _preparedMinimumLod, out var mask )
			? mask
			: 0u;
		resident.SetCoverageBank( _preparedClipBank, residentMember, targetActive, transitionMask );
		_preparedCoverageChanges.Add( key );
		SetVisibilityCoverageRecord( resident );
	}

	private bool IsPublishedActive( ResidentMesh resident )
	{
		if ( resident.Handle is null )
		{
			if ( _publishedClipSelection is null ) return false;
			var key = resident.Descriptor.Key;
			var regularOwner = VoxelRenderRegionKey.Regular( key.Lod, key.Coordinate );
			if ( !_publishedClipSelection.ContainsPublishedResidentRegular(
				regularOwner, _publishedMinimumLod ) ) return false;
			return key.MeshKind == VoxelRenderMeshKind.Regular
				? _publishedClipSelection.ContainsPublishedActiveRegular( key, _publishedMinimumLod )
				: _publishedClipSelection.ContainsPublishedTransitionFace( key, _publishedMinimumLod );
		}
		var residentMember = resident.GetCoverageResident( _publishedClipBank );
		var targetActive = resident.GetCoverageActive( _publishedClipBank );
		return residentMember && targetActive;
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

	public void Schedule( VoxelChunk chunk, int contentRevision, float playerRouteDistance,
		GpuMeshResidency residency = GpuMeshResidency.Gameplay )
	{
		var descriptor = GpuSdfDescriptor.FromChunk( chunk, contentRevision );
		if ( chunk.DensityClassification != ChunkDensityClassification.PotentiallySurfaceContaining )
		{
			PublishEmpty( descriptor, residency );
			return;
		}
		Schedule( descriptor, playerRouteDistance, residency );
	}

	public void BeginClipMeasurement()
	{
		_stalePublicationCount = 0;
	}

	public int CountPublishedTransitionMaskMismatches()
	{
		var mismatches = 0;
		foreach ( var pair in _resident )
		{
			if ( pair.Key.MeshKind != VoxelRenderMeshKind.Regular ||
				!IsPublishedActive( pair.Value ) ) continue;
			var mask = pair.Value.Handle is null && _publishedClipSelection is not null
				? _publishedClipSelection.TryGetTransitionMask(
					VoxelRenderRegionKey.Regular( pair.Key.Lod, pair.Key.Coordinate ),
					_publishedMinimumLod,
					out var logicalMask ) ? logicalMask : 0u
				: pair.Value.GetTransitionMask( _publishedClipBank );
			for ( var face = VoxelTransitionFace.NegativeX;
				face <= VoxelTransitionFace.PositiveZ; face++ )
			{
				var transition = VoxelRenderRegionKey.Transition(
					pair.Key.Lod,
					pair.Key.Coordinate,
					face );
				var transitionActive = _resident.TryGetValue( transition, out var transitionResident ) &&
					IsPublishedActive( transitionResident );
				var maskActive = (mask & VoxelClipBoxSelection.FaceBit( face )) != 0;
				if ( transitionActive != maskActive ) mismatches++;
			}
		}
		foreach ( var pair in _resident )
		{
			if ( pair.Key.MeshKind != VoxelRenderMeshKind.Transition ||
				!IsPublishedActive( pair.Value ) ) continue;
			var owner = VoxelRenderRegionKey.Regular( pair.Key.Lod, pair.Key.Coordinate );
			if ( !_resident.TryGetValue( owner, out var regular ) || !IsPublishedActive( regular ) )
				mismatches++;
		}
		return mismatches;
	}

	public void Schedule(
		GpuSdfDescriptor descriptor,
		float playerRouteDistance,
		GpuMeshResidency residency = GpuMeshResidency.Clip )
	{
		var extent = descriptor.CellsPerAxis * descriptor.CellSize;
		var origin = new Vector3(
			descriptor.RegionCoordinate.x * extent,
			descriptor.RegionCoordinate.y * extent,
			descriptor.RegionCoordinate.z * extent );
		if ( ProceduralTerrainSdf.TryClassifyGlobalVerticalRange(
			new SdfWorldAabb( origin, origin + new Vector3( extent ) ),
			descriptor.TerrainSettings,
			out _ ) )
		{
			PublishEmpty( descriptor, residency );
			return;
		}
		if ( _resident.TryGetValue( descriptor.Key, out var resident ) &&
			GeometryEquivalent( resident.Descriptor, descriptor ) )
		{
			resident.Descriptor = descriptor;
			if ( residency == GpuMeshResidency.Gameplay ) SetResidency( resident, residency );
			return;
		}
		if ( _pending.TryGetValue( descriptor.Key, out var pending ) &&
			GeometryEquivalent( pending.Descriptor, descriptor ) )
		{
			QueuePending( pending with { Descriptor = descriptor, Residency = residency } );
			return;
		}
		foreach ( var lane in _scratchLanes )
		{
			for ( var index = 0; index < lane.CountInFlight.Count; index++ )
			{
				var inFlight = lane.CountInFlight[index];
				if ( !GeometryEquivalent( inFlight.Descriptor, descriptor ) ) continue;
				lane.CountInFlight[index] = inFlight with
				{
					Descriptor = descriptor,
					Residency = residency == GpuMeshResidency.Gameplay
						? GpuMeshResidency.Gameplay
						: inFlight.Residency
				};
				return;
			}
			for ( var index = 0; index < lane.EmitInFlight.Count; index++ )
			{
				var inFlight = lane.EmitInFlight[index];
				if ( !GeometryEquivalent( inFlight.Descriptor, descriptor ) ) continue;
				lane.EmitInFlight[index] = inFlight with
				{
					Descriptor = descriptor,
					Residency = residency == GpuMeshResidency.Gameplay
						? GpuMeshResidency.Gameplay
						: inFlight.Residency
				};
				return;
			}
		}
		_throughput?.RecordScheduled();
		QueuePending( new PendingMesh(
			descriptor,
			residency,
			Stopwatch.GetTimestamp(),
			playerRouteDistance ) );
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

	private float GetScheduleLatencyPercentile( double percentile )
	{
		if ( _scheduleLatencySampleCount == 0 ) return 0;
		var index = Math.Clamp( (int)Math.Ceiling( _scheduleLatencySampleCount * percentile ) - 1, 0, _scheduleLatencySampleCount - 1 );
		return _scheduleLatencyMilliseconds[index];
	}

	public void SetResidency( VoxelRenderRegionKey key, GpuMeshResidency residency )
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

	public void Remove( VoxelRenderRegionKey key )
	{
		RemovePending( key );
		CancelInFlight( key );
		if ( !_resident.Remove( key, out var resident ) ) return;
		ReleaseResident( resident );
		_drawCommandsDirty = true;
	}

	private void PublishEmpty( GpuSdfDescriptor descriptor, GpuMeshResidency residency )
	{
		if ( _resident.TryGetValue( descriptor.Key, out var existing ) &&
			GeometryEquivalent( existing.Descriptor, descriptor ) )
		{
			existing.Descriptor = descriptor;
			return;
		}
		RemovePending( descriptor.Key );
		CancelInFlight( descriptor.Key );
		if ( _resident.Remove( descriptor.Key, out var previous ) ) ReleaseResident( previous );
		var resident = new ResidentMesh( descriptor, residency, null, default )
		{
		};
		ApplyPreparedCoverage( resident );
		_resident.Add( descriptor.Key, resident );
		if ( residency == GpuMeshResidency.Clip ) _warmResidentCount++;
		_topologyDigest ^= RegionDigest( descriptor.Key, resident.Counts.TopologyDigest );
		_positionDigest ^= RegionDigest( descriptor.Key, resident.Counts.PositionDigest );
		_drawCommandsDirty = true;
	}

	private static bool GeometryEquivalent( GpuSdfDescriptor left, GpuSdfDescriptor right ) =>
		left.Key == right.Key && left.CellsPerAxis == right.CellsPerAxis &&
		left.CellSize == right.CellSize && left.TerrainSettings == right.TerrainSettings &&
		left.GeneratorVersion == right.GeneratorVersion && left.ContentRevision == right.ContentRevision;

	public bool IsClipCoverageReady(
		VoxelClipBoxSelection selection,
		int minimumLod,
		int contentRevision,
		int placementRevision,
		out int missingRegions )
	{
		missingRegions = 0;
		if ( !ReferenceEquals( selection, _preparedClipSelection ) ||
			placementRevision != _preparedClipPlacementRevision ||
			minimumLod != _preparedMinimumLod ||
			_preparedClipDelta is null )
		{
			missingRegions = selection.ResidentRegularCount + selection.LogicalTransitionFaceCount;
			return false;
		}
		if ( _preparedCoverageApplyQueue.Count > 0 )
		{
			missingRegions = _preparedCoverageApplyQueue.Count;
			return false;
		}
		if ( _preparedProgressiveRefinement )
		{
			foreach ( var key in selection.EnumerateResidentRegular( minimumLod ) )
			{
				if ( !_resident.TryGetValue( key, out var resident ) ||
					resident.Descriptor.ContentRevision != contentRevision ) missingRegions++;
			}
			foreach ( var key in selection.EnumerateTransitionFaces( minimumLod + 1 ) )
			{
				if ( !_resident.TryGetValue( key, out var resident ) ||
					resident.Descriptor.ContentRevision != contentRevision ) missingRegions++;
			}
			return missingRegions == 0;
		}
		for ( var lod = 0; lod <= _preparedClipDelta.MaximumLod; lod++ )
		{
			foreach ( var key in _preparedClipDelta.EnumerateEnteringRegular( lod ) )
			{
				if ( !_resident.TryGetValue( key, out var resident ) ||
					resident.Descriptor.ContentRevision != contentRevision ) missingRegions++;
			}
		}
		for ( var lod = 1; lod <= _preparedClipDelta.MaximumLod; lod++ )
		{
			foreach ( var key in _preparedClipDelta.GetEnteringTransitions( lod ) )
			{
				if ( !_resident.TryGetValue( key, out var resident ) ||
					resident.Descriptor.ContentRevision != contentRevision ) missingRegions++;
			}
		}
		return missingRegions == 0;
	}

	public GpuClipCommitResult CommitClipCoverage(
		VoxelClipBoxSelection selection,
		int minimumLod,
		int placementRevision )
	{
		if ( !ReferenceEquals( selection, _preparedClipSelection ) ||
			placementRevision != _preparedClipPlacementRevision ||
			minimumLod != _preparedMinimumLod ||
			_preparedCoverageApplyQueue.Count > 0 )
		{
			_stalePublicationCount++;
			throw new InvalidOperationException( "Stale clip-box placement cannot be published." );
		}
		_publishedClipBank = _preparedClipBank;
		_publishedClipSelection = selection;
		_publishedMinimumLod = minimumLod;
		var synchronizedBank = 1 - _publishedClipBank;
		var coverageStart = Stopwatch.GetTimestamp();
		var changedRecords = 0;
		var changedGeometryRecords = 0;
		foreach ( var key in _preparedCoverageChanges )
		{
			if ( !_resident.TryGetValue( key, out var resident ) ) continue;
			changedRecords++;
			if ( resident.Handle is not null ) changedGeometryRecords++;
			resident.CopyCoverageBank( _publishedClipBank, synchronizedBank );
			SetVisibilityCoverageRecord( resident );
		}
		var coverageMilliseconds =
			(float)Stopwatch.GetElapsedTime( coverageStart ).TotalMilliseconds;
		_preparedCoverageChanges.Clear();
		_drawCommandsDirty = true;
		var drawCommit = CommitDrawCommands();
		return new GpuClipCommitResult(
			coverageMilliseconds,
			drawCommit.Milliseconds,
			changedRecords,
			changedGeometryRecords );
	}

	public void RetireCommittedClipCoverage()
	{
		foreach ( var key in _preparedResidentRemovals )
		{
			if ( _resident.Remove( key, out var resident ) ) ReleaseResident( resident );
		}
		_preparedResidentRemovals.Clear();
	}

	public void Reset( int cellsPerAxis )
	{
		Clear();
		if ( cellsPerAxis == _cellsPerAxis ) return;
		DisposeArenas();
		DisposeVisibilityBuffers();
		DisposeScratchLanes();
		_cellsPerAxis = cellsPerAxis;
		_scratchLanes = CreateScratchLanes( cellsPerAxis );
	}

	public int ProcessPending( int maximumDispatches )
	{
		AttachToMainCamera();
		FinalizeEmits();
		_maximumDispatchesRequested = Math.Clamp( maximumDispatches, 0, MaximumDispatchesPerUpdate );
		var processed = System.Threading.Interlocked.Exchange( ref _processedRenderDispatches, 0 );
		CommitDrawCommands();
		return processed;
	}

	private void ProcessGpuRenderTick()
	{
		if ( _disposed || _scratchLanes is null ) return;
		foreach ( var lane in _scratchLanes )
		{
			if ( lane.CountInFlight.Count > 0 && lane.Scratch.TryTakeCounts(
				out var counts,
				out var count,
				out var readbackMilliseconds,
				out var callbackWaitMilliseconds ) )
			{
				AllocateAndEmit( lane, counts, count, readbackMilliseconds, callbackWaitMilliseconds );
				break;
			}
		}

		var targetLane = _scratchLanes.FirstOrDefault( lane => lane.IsIdle );
		if ( targetLane is null ) return;
		var requests = targetLane.Requests;
		var processed = 0;
		VoxelRenderMeshKind? batchKind = null;
		while ( processed < _maximumDispatchesRequested && TryDequeuePending( batchKind, out var pending ) )
		{
			batchKind ??= pending.Descriptor.MeshKind;
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
		}
	}

	private static GpuTerrainRequest CreateRequest( InFlightMesh inFlight, int requestIndex )
	{
		var descriptor = inFlight.Descriptor;
		var size = descriptor.CellsPerAxis * descriptor.CellSize;
		var origin = new Vector3(
			descriptor.RegionCoordinate.x * size,
			descriptor.RegionCoordinate.y * size,
			descriptor.RegionCoordinate.z * size );
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
			RequestIndex = (uint)requestIndex,
			PackedIdentity = PackIdentity( descriptor.Key )
		};
	}

	private static uint PackIdentity( VoxelRenderRegionKey key ) =>
		((uint)key.MeshKind & 0x01u) |
		(((uint)(byte)key.Face & 0xFFu) << 8) |
		(((uint)key.Lod & 0xFFu) << 16);

	private void AllocateAndEmit( ScratchLane lane, GpuTerrainCountResult[] counts, int count,
		double readbackMilliseconds, double callbackWaitMilliseconds )
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
				handle = Acquire( checked( (int)result.VertexCount ), checked( (int)result.IndexCount ), source.Generation );
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
					Enabled = 1,
					Reserved = (uint)candidate.Handle.GlobalSlot
				};
			}
			var arenaEmitMilliseconds = lane.Scratch.SubmitEmitPass(
				allocations, count, arena.Vertices, arena.Indices );
			_emitSubmissionMilliseconds += arenaEmitMilliseconds;
			emitMilliseconds += (float)arenaEmitMilliseconds;
		}
		lane.Scratch.CompleteEmit();
		lane.SubmittedRenderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
		lane.EmitSubmittedTimestamp = Stopwatch.GetTimestamp();
		_throughput?.RecordBatchCompleted(
			(float)readbackMilliseconds,
			(float)callbackWaitMilliseconds,
			allocationMilliseconds,
			emitMilliseconds );
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
				if ( _cancelledInFlight.Remove( new InFlightIdentity( key, completed.Generation ) ) )
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
				ApplyPreparedCoverage( resident );
				_resident.Add( key, resident );
				if ( residency == GpuMeshResidency.Clip ) _warmResidentCount++;
				if ( completed.Handle is not null )
				{
					completed.Handle.Arena.ActiveResidentCount++;
					SetVisibilityRecord( resident );
				}
				_topologyDigest ^= RegionDigest( key, completed.Counts.TopologyDigest );
				_positionDigest ^= RegionDigest( key, completed.Counts.PositionDigest );
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
			_drawCommandsDirty = true;
			CommitDrawCommands();
		}
	}

	private static ulong RegionDigest( VoxelRenderRegionKey key, uint digest )
	{
		var coordinate = key.Coordinate;
		ulong value = digest ^ (uint)coordinate.x * 0x9E3779B1u ^
			(uint)coordinate.y * 0x85EBCA77u ^ (uint)coordinate.z * 0xC2B2AE3Du ^
			(uint)key.Lod * 0x27D4EB2Du ^ (uint)key.MeshKind * 0x165667B1u ^
			(uint)(byte)key.Face * 0xD3A2646Cu;
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

	private GeometryHandle Acquire( int vertexCount, int indexCount, uint generation )
	{
		foreach ( var arena in _arenas )
		{
			if ( arena.TryAcquire( vertexCount, indexCount, generation, out var reused ) )
			{
				_poolReuseCount++;
				return reused;
			}
		}
		if ( vertexCount > VertexArenaCapacity || indexCount > IndexArenaCapacity )
			throw new InvalidOperationException( $"Terrain region geometry ({vertexCount} vertices, {indexCount} indices) exceeds an arena." );
		var created = new GeometryArena( _arenas.Count );
		_arenas.Add( created );
		_poolAllocationCount++;
		EnsureVisibilityCapacity( _arenas.Count * RegionsPerSlab );
		if ( !created.TryAcquire( vertexCount, indexCount, generation, out var allocation ) )
			throw new InvalidOperationException( "A new terrain geometry arena rejected a valid allocation." );
		_drawCommandsDirty = true;
		return allocation;
	}

	private void ReleaseResident( ResidentMesh resident )
	{
		if ( resident.Residency == GpuMeshResidency.Clip ) _warmResidentCount--;
		_topologyDigest ^= RegionDigest( resident.Descriptor.Key, resident.Counts.TopologyDigest );
		_positionDigest ^= RegionDigest( resident.Descriptor.Key, resident.Counts.PositionDigest );
		if ( resident.Handle is null ) return;
		ClearVisibilityRecord( resident.Handle.GlobalSlot );
		resident.Handle.Arena.ActiveResidentCount--;
		Release( resident.Handle );
	}

	private static void Release( GeometryHandle handle ) => handle?.Arena.Release( handle );

	private void QueuePending( PendingMesh pending )
	{
		if ( _scheduleLatencyMeasurementActive && _pending.ContainsKey( pending.Descriptor.Key ) )
			_scheduleLatencySupersededCount++;
		RemovePending( pending.Descriptor.Key );
		_pending[pending.Descriptor.Key] = pending;
		if ( pending.Descriptor.MeshKind == VoxelRenderMeshKind.Transition )
		{
			_pendingWarmCount++;
			_transitionDispatchQueue.Enqueue( pending );
		}
		else if ( pending.Residency == GpuMeshResidency.Gameplay )
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

	private bool TryDequeuePending( VoxelRenderMeshKind? requiredKind, out PendingMesh pending )
	{
		if ( requiredKind is null or VoxelRenderMeshKind.Regular )
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
		}
		if ( requiredKind is null or VoxelRenderMeshKind.Transition )
		{
			while ( _transitionDispatchQueue.TryDequeue( out var transition ) )
			{
				if ( _pending.TryGetValue( transition.Descriptor.Key, out var current ) && current == transition )
				{
					_pending.Remove( transition.Descriptor.Key );
					_pendingWarmCount--;
					pending = transition;
					return true;
				}
			}
		}
		pending = default;
		return false;
	}

	private void RemovePending( VoxelRenderRegionKey key )
	{
		if ( !_pending.Remove( key, out var pending ) ) return;
		if ( pending.Descriptor.MeshKind == VoxelRenderMeshKind.Transition ) _pendingWarmCount--;
		else if ( pending.Residency == GpuMeshResidency.Gameplay ) _pendingGameplayCount--;
		else _pendingWarmCount--;
	}

	private void CancelInFlight( VoxelRenderRegionKey key )
	{
		foreach ( var lane in _scratchLanes ?? Array.Empty<ScratchLane>() )
		{
			foreach ( var value in lane.CountInFlight )
			{
				if ( value.Descriptor.Key == key )
					_cancelledInFlight.Add( new InFlightIdentity( key, value.Generation ) );
			}
			foreach ( var value in lane.EmitInFlight )
			{
				if ( value.Descriptor.Key == key )
					_cancelledInFlight.Add( new InFlightIdentity( key, value.Generation ) );
			}
		}
	}

	private int CountInFlight( GpuMeshResidency residency ) =>
		_scratchLanes?.Sum( lane => lane.CountInFlight.Count( value => value.Residency == residency ) +
			lane.EmitInFlight.Count( value => value.Residency == residency ) ) ?? 0;

	private void SetResidency( ResidentMesh resident, GpuMeshResidency residency )
	{
		if ( resident.Residency == residency ) return;
		if ( resident.Residency == GpuMeshResidency.Clip ) _warmResidentCount--;
		if ( residency == GpuMeshResidency.Clip ) _warmResidentCount++;
		resident.Residency = residency;
		if ( resident.Handle is not null ) SetVisibilityRecord( resident );
	}

	public DrawCommandCommitResult CommitDrawCommands()
	{
		var start = Stopwatch.GetTimestamp();
		UploadVisibilityDescriptors();
		if ( !_drawCommandsDirty )
			return new DrawCommandCommitResult( false, (float)Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
		_drawCommands.Reset();
		if ( _visibilityCapacity > 0 )
		{
			_drawCommands.Attributes.Set( "VisibilityBounds", _visibilityBuffers.Bounds );
			_drawCommands.Attributes.Set( "SourceIndirectArguments", _visibilityBuffers.SourceArguments );
			_drawCommands.Attributes.Set( "VisibleIndirectArguments", _visibilityBuffers.VisibleArguments );
			_drawCommands.Attributes.Set( "VisibilityFrameCounters", _visibilityBuffers.FrameCounters );
			_drawCommands.Attributes.Set( "VisibilityAggregateCounters", _visibilityAggregateCounters );
			_drawCommands.Attributes.Set( "TerrainRecordDescriptors", _visibilityBuffers.Bounds );
			_drawCommands.Attributes.Set( "VisibilitySlotCount", _visibilityCapacity );
			_drawCommands.Attributes.Set( "VisibilityPass", 0 );
			_drawCommands.Attributes.Set( "MeasureVisibility", _visibilityMeasurementActive ? 1 : 0 );
			_drawCommands.Attributes.Set( "CaptureSettledDiagnostics", _visibilitySettledCaptureActive ? 1 : 0 );
			_drawCommands.Attributes.Set( "ClipPublicationBank", _publishedClipBank );
			_drawCommands.Attributes.Set( "ClipMinimumLod", _publishedMinimumLod );
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
			foreach ( var arena in _arenas )
			{
				if ( arena.ActiveResidentCount == 0 ) continue;
				_drawCommands.ResourceBarrierTransition( arena.Vertices, ResourceState.VertexOrIndexBuffer );
				_drawCommands.ResourceBarrierTransition( arena.Indices, ResourceState.VertexOrIndexBuffer );
				_drawCommands.DrawIndexedInstancedIndirect(
					arena.Vertices,
					arena.Indices,
					_material,
					_visibilityBuffers.VisibleArguments,
					(uint)(arena.Index * RegionsPerSlab),
					null,
					Graphics.PrimitiveType.Triangles,
					RegionsPerSlab,
					IndirectArgumentStride );
			}
		}
		_drawCommandsDirty = false;
		return new DrawCommandCommitResult( true, (float)Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
	}

	public void BeginVisibilityMeasurement()
	{
		lock ( _visibilityLock )
		{
			_completedVisibilityMeasurement = null;
			_visibilityReadbackPending = false;
			_visibilityReadbackInFlight = false;
		}
		Span<uint> counters = stackalloc uint[VisibilityAggregateCounterCount];
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
				System.Threading.Interlocked.Read( ref _renderSequence ) <= _visibilityReadbackRequestedRenderSequence + 1 ) return;
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
			var minimum = data.Length >= 10 && frames > 0 && data[3] != uint.MaxValue ? data[3] : 0;
			lock ( _visibilityLock )
			{
				_completedVisibilityMeasurement = new GpuVisibilityMeasurement(
					frames,
					data.Length >= 10 ? data[1] : 0,
					data.Length >= 10 ? data[2] : 0,
					minimum,
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
		if ( requiredCapacity <= _visibilityCapacity ) return;
		var newCapacity = Math.Max( RegionsPerSlab, _visibilityCapacity );
		while ( newCapacity < requiredCapacity ) newCapacity = checked( newCapacity * 2 );
		if ( _visibilityBuffers is not null ) _retiredVisibilityBuffers.Add( _visibilityBuffers );
		var oldBounds = _visibilityBoundsData;
		var oldArguments = _sourceArgumentData;
		_visibilityCapacity = newCapacity;
		_visibilityBoundsData = new Vector4[newCapacity * VisibilityVectorsPerRecord];
		_sourceArgumentData = new GpuBuffer.IndirectDrawIndexedArguments[newCapacity];
		oldBounds.CopyTo( _visibilityBoundsData, 0 );
		oldArguments.CopyTo( _sourceArgumentData, 0 );
		var bounds = new GpuBuffer<Vector4>( newCapacity * VisibilityVectorsPerRecord,
			GpuBuffer.UsageFlags.Structured, "Voxel Terrain Record Metadata" );
		var source = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>( newCapacity,
			GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "Voxel Source Indexed Arguments" );
		var visible = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>( newCapacity,
			GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "Voxel Visible Indexed Arguments" );
		var frame = new GpuBuffer<uint>( VisibilityFrameCounterCount, GpuBuffer.UsageFlags.Structured, "Voxel Visibility Frame Counters" );
		source.SetData( _sourceArgumentData );
		visible.SetData( _sourceArgumentData );
		_visibilityBuffers = new VisibilityBuffers( bounds, source, visible, frame );
		_visibilityDescriptorsDirty = false;
		_drawCommandsDirty = true;
	}

	private void SetVisibilityRecord( ResidentMesh resident )
	{
		if ( resident.Handle is null ) return;
		var descriptor = resident.Descriptor;
		var size = descriptor.CellsPerAxis * descriptor.CellSize;
		var origin = new Vector3(
			descriptor.RegionCoordinate.x * size,
			descriptor.RegionCoordinate.y * size,
			descriptor.RegionCoordinate.z * size );
		var padding = new Vector3( descriptor.CellSize );
		var slot = resident.Handle.GlobalSlot;
		var index = slot * VisibilityVectorsPerRecord;
		var activeResidency = resident.Residency == GpuMeshResidency.Clip ? 2f : 1f;
		_visibilityBoundsData[index] = new Vector4( origin - padding, activeResidency );
		_visibilityBoundsData[index + 1] = new Vector4(
			origin + new Vector3( size ) + padding,
			resident.Counts.ActiveCells );
		_sourceArgumentData[slot] = new GpuBuffer.IndirectDrawIndexedArguments
		{
			IndexCount = (uint)resident.Handle.Indices.Count,
			InstanceCount = 1,
			FirstIndex = (uint)resident.Handle.Indices.Offset,
			BaseVertex = resident.Handle.Vertices.Offset,
			FirstInstance = (uint)slot
		};
		var descriptorOffset = index + DescriptorVectorOffset;
		_visibilityBoundsData[descriptorOffset] = new Vector4( origin, descriptor.CellSize );
		_visibilityBoundsData[descriptorOffset + 1] = new Vector4( size, size, size, descriptor.Lod );
		_visibilityBoundsData[descriptorOffset + 2] = new Vector4(
			BitConverter.UInt32BitsToSingle( resident.GetTransitionMask( 0 ) ),
			BitConverter.UInt32BitsToSingle( resident.GetTransitionMask( 1 ) ),
			BitConverter.UInt32BitsToSingle( resident.CoverageBits ),
			BitConverter.UInt32BitsToSingle( (uint)descriptor.MeshKind ) );
		_visibilityDescriptorsDirty = true;
	}

	private void SetVisibilityCoverageRecord( ResidentMesh resident )
	{
		if ( resident.Handle is null ) return;
		var descriptorIndex = resident.Handle.GlobalSlot * VisibilityVectorsPerRecord +
			DescriptorVectorOffset + 2;
		_visibilityBoundsData[descriptorIndex] = new Vector4(
			BitConverter.UInt32BitsToSingle( resident.GetTransitionMask( 0 ) ),
			BitConverter.UInt32BitsToSingle( resident.GetTransitionMask( 1 ) ),
			BitConverter.UInt32BitsToSingle( resident.CoverageBits ),
			BitConverter.UInt32BitsToSingle( (uint)resident.Descriptor.MeshKind ) );
		_visibilityDescriptorsDirty = true;
	}

	private void ClearVisibilityRecord( int slot )
	{
		var index = slot * VisibilityVectorsPerRecord;
		Array.Clear( _visibilityBoundsData, index, VisibilityVectorsPerRecord );
		_sourceArgumentData[slot] = default;
		_visibilityDescriptorsDirty = true;
	}

	private void UploadVisibilityDescriptors()
	{
		if ( !_visibilityDescriptorsDirty || _visibilityBuffers is null ) return;
		_visibilityBuffers.Bounds.SetData( _visibilityBoundsData );
		_visibilityBuffers.SourceArguments.SetData( _sourceArgumentData );
		_visibilityDescriptorsDirty = false;
	}

	public void Clear()
	{
		_preparedClipSelection = null;
		_publishedClipSelection = null;
		_preparedClipDelta = null;
		_preparedResidentRemovals.Clear();
		_preparedCoverageChanges.Clear();
		_preparedCoverageApplyQueue.Clear();
		_preparedClipPlacementRevision = 0;
		_publishedClipBank = 0;
		_preparedClipBank = 1;
		_preparedMinimumLod = MaximumClipLevels;
		_publishedMinimumLod = MaximumClipLevels;
		_preparedProgressiveRefinement = false;
		if ( _scheduleLatencyMeasurementActive )
			_scheduleLatencyCancelledCount += _pending.Count + (_scratchLanes?.Sum( lane =>
				lane.CountInFlight.Count + lane.EmitInFlight.Count ) ?? 0);
		_pending.Clear();
		_gameplayDispatchQueue.Clear();
		_warmDispatchQueue.Clear();
		_transitionDispatchQueue.Clear();
		_pendingGameplayCount = 0;
		_pendingWarmCount = 0;
		_cancelledInFlight.Clear();
		foreach ( var lane in _scratchLanes ?? Array.Empty<ScratchLane>() )
		{
			foreach ( var candidate in lane.EmitInFlight ) Release( candidate.Handle );
			lane.EmitInFlight.Clear();
			lane.CountInFlight.Clear();
		}
		foreach ( var resident in _resident.Values ) ReleaseResident( resident );
		_resident.Clear();
		_warmResidentCount = 0;
		_drawCommandsDirty = true;
		CommitDrawCommands();
	}

	public void Dispose()
	{
		if ( _disposed ) return;
		_disposed = true;
		Clear();
		if ( _camera is not null )
		{
			_camera.RemoveCommandList( _drawCommands );
			_camera = null;
		}
		DisposeArenas();
		DisposeVisibilityBuffers();
		DisposeScratchLanes();
		_visibilityAggregateCounters.Dispose();
		_readbackObject?.Delete();
	}

	private void DisposeScratchLanes()
	{
		if ( _scratchLanes is null ) return;
		foreach ( var lane in _scratchLanes ) lane.Scratch.Dispose();
	}

	private void AttachToMainCamera()
	{
		CameraComponent selected = null;
		foreach ( var candidate in _scene.GetAllComponents<CameraComponent>() )
		{
			selected ??= candidate;
			if ( candidate.IsMainCamera ) { selected = candidate; break; }
		}
		if ( selected == _camera ) return;
		if ( _camera is not null )
		{
			_camera.RemoveCommandList( _drawCommands );
		}
		_camera = selected;
		if ( _camera is not null )
		{
			_camera.AddCommandList( _drawCommands, Sandbox.Rendering.Stage.AfterOpaque, 0 );
		}
	}

	private void DisposeArenas()
	{
		foreach ( var arena in _arenas ) arena.Dispose();
		_arenas.Clear();
	}

	private void DisposeVisibilityBuffers()
	{
		_visibilityBuffers?.Dispose();
		_visibilityBuffers = null;
		foreach ( var retired in _retiredVisibilityBuffers ) retired.Dispose();
		_retiredVisibilityBuffers.Clear();
		_visibilityCapacity = 0;
		_visibilityBoundsData = Array.Empty<Vector4>();
		_sourceArgumentData = Array.Empty<GpuBuffer.IndirectDrawIndexedArguments>();
		_visibilityDescriptorsDirty = false;
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
		public GpuSdfDescriptor Descriptor { get; set; }
		public GpuMeshResidency Residency { get; set; }
		public GeometryHandle Handle { get; }
		public GpuTerrainCountResult Counts { get; }
		public uint CoverageBits { get; private set; }
		private uint TransitionMask0 { get; set; }
		private uint TransitionMask1 { get; set; }
		public ResidentMesh( GpuSdfDescriptor descriptor, GpuMeshResidency residency, GeometryHandle handle, GpuTerrainCountResult counts )
		{
			Descriptor = descriptor; Residency = residency; Handle = handle; Counts = counts;
		}

		public bool GetCoverageResident( int bank ) =>
			(CoverageBits & (1u << bank)) != 0;

		public bool GetCoverageActive( int bank ) =>
			(CoverageBits & (1u << (bank + 2))) != 0;

		public uint GetTransitionMask( int bank ) =>
			bank == 0 ? TransitionMask0 : TransitionMask1;

		public void SetCoverageBank( int bank, bool resident, bool active, uint transitionMask )
		{
			var residentBit = 1u << bank;
			var activeBit = 1u << (bank + 2);
			CoverageBits = resident ? CoverageBits | residentBit : CoverageBits & ~residentBit;
			CoverageBits = active ? CoverageBits | activeBit : CoverageBits & ~activeBit;
			if ( bank == 0 ) TransitionMask0 = transitionMask;
			else TransitionMask1 = transitionMask;
		}

		public void CopyCoverageBank( int sourceBank, int destinationBank )
		{
			SetCoverageBank(
				destinationBank,
				GetCoverageResident( sourceBank ),
				GetCoverageActive( sourceBank ),
				GetTransitionMask( sourceBank ) );
		}
	}

	private sealed class ScratchLane
	{
		public GpuTerrainScratch Scratch { get; }
		public GpuTerrainRequest[] Requests { get; } = new GpuTerrainRequest[MaximumRegionsPerBatch];
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

	private readonly record struct PendingMesh( GpuSdfDescriptor Descriptor, GpuMeshResidency Residency,
		long ScheduledTimestamp, float ScheduledRouteDistance );
	private readonly record struct InFlightMesh( GpuSdfDescriptor Descriptor, GpuMeshResidency Residency,
		uint Generation, long ScheduledTimestamp, float ScheduledRouteDistance );
	private readonly record struct CandidateMesh( GpuSdfDescriptor Descriptor, GpuMeshResidency Residency,
		uint Generation, long ScheduledTimestamp, float ScheduledRouteDistance,
		GeometryHandle Handle, GpuTerrainCountResult Counts );
	private readonly record struct InFlightIdentity( VoxelRenderRegionKey Key, uint Generation );

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
		public void Dispose()
		{
			Bounds.Dispose(); SourceArguments.Dispose(); VisibleArguments.Dispose(); FrameCounters.Dispose();
		}
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
			System.Threading.Interlocked.Increment( ref _owner._renderSequence );
			_owner.ProcessGpuRenderTick();
			_owner.ProcessVisibilityReadback();
		}
	}
}

internal readonly record struct DrawCommandCommitResult( bool Rebuilt, float Milliseconds );
internal readonly record struct GpuClipCommitResult(
	float CoverageMilliseconds,
	float DrawCommandMilliseconds,
	int ChangedRecords,
	int ChangedGeometryRecords );
internal readonly record struct GpuClipLevelMeasurement(
	int Lod,
	int DesiredRegular,
	int ResidentRegular,
	int ActiveRegular,
	int InactiveRegular,
	int DesiredTransitions,
	int ResidentTransitions,
	int ActiveTransitions,
	long RegularTriangles,
	long TransitionTriangles,
	long RegularBytes,
	long TransitionBytes,
	ulong TopologyDigest,
	ulong PositionDigest );
internal readonly record struct GpuMeshScheduleLatencyMeasurement(
	int Samples, int TruncatedSamples, float P50Milliseconds, float P95Milliseconds,
	float P99Milliseconds, float MaximumMilliseconds, int Cancelled, int Superseded );
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
	uint SettledMaximumActiveCells, long LogicalBufferBytes, long ScalarReadbacks )
{
	public float AverageResident => FrameCount > 0 ? (float)ResidentTotal / FrameCount : 0;
	public float AverageVisible => FrameCount > 0 ? (float)VisibleTotal / FrameCount : 0;
	public float AverageWarm => FrameCount > 0 ? (float)WarmTotal / FrameCount : 0;
	public float AverageCulled => MathF.Max( 0, AverageResident - AverageVisible );
	public float CulledPercent => AverageResident > 0 ? AverageCulled * 100 / AverageResident : 0;
}
internal readonly record struct GpuPublishedCoverageValidation(
	int ExpectedRegular,
	int ActualRegular,
	int ExpectedTransitions,
	int ActualTransitions,
	int MissingRegular,
	int MissingTransitions,
	int UnexpectedActive,
	VoxelRenderRegionKey? FirstUnexpected )
{
	public int IdentityMismatches => checked( MissingRegular + MissingTransitions + UnexpectedActive );
}
internal enum GpuMeshResidency { Gameplay, Clip }
