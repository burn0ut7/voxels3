using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

/// <summary>
/// Owns the canonical loaded voxel chunks and streams them around one world-space
/// target. Chunks are deterministic managed data rather than networked objects.
/// </summary>
public sealed class VoxelManager : Component
{
	private enum PerformanceCompletionPhase
	{
		None,
		AwaitingMovingSettle,
		AwaitingStationaryRenderAdvance,
		MeasuringStationary,
		AwaitingStationaryVisibility
	}

	private const float MainThreadIntegrationBudgetMilliseconds = 0.5f;
	private const float PerformanceWindowSeconds = 10f;
	private const float MemorySampleIntervalSeconds = 1f;
	private const int MaximumPerformanceFrameSamples = 524288;
	private const int MaximumFigureEightLoopCount = 8;
	private const int GameplayLoadRadius = 4;
	private const int PerformanceResultSchemaVersion = 14;
	private const int RenderWarmShellChunks = 1;
	private const int Lod0ActiveHalfExtent = 4;
	private const int Lod1CacheHalfExtent = 8;
	private const int Lod1HoleHalfExtent = 2;
	private const float Lod1CellSize = 32f;
	private const int Lod2CacheHalfExtent = 8;
	private const int Lod2NominalHoleHalfExtent = 4;
	private const float Lod2CellSize = 64f;
	private const int GenerationBatchSize = 256;
	private const string PerformanceResultsDirectory = "performance";
	private const string PerformanceResultsPath = "performance/results-v1.jsonl";
	private const string InspectorPerformanceTask = "PERFORMANCE-OVERVIEW-001/v4";
	private const string InspectorPerformanceRevision = "manual-inspector";
	private static readonly JsonSerializerOptions PerformanceJsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};
	private static readonly FixedVoxelPlacementInputs PlacementInputs = new(
		GameplayLoadRadius,
		Lod0ActiveHalfExtent,
		Lod1CacheHalfExtent,
		Lod1HoleHalfExtent,
		Lod2CacheHalfExtent,
		Lod2NominalHoleHalfExtent,
		GpuMeshLevel.Lod1 );

	private readonly Dictionary<Vector3Int, VoxelChunk> _loadedChunks = new();
	private readonly HashSet<Vector3Int> _desiredChunks = new();
	private HashSet<Vector3Int> _renderDesiredChunks = new();
	private HashSet<Vector3Int> _nextRenderDesiredChunks = new();
	private readonly HashSet<Vector3Int> _renderPreparedChunks = new();
	private readonly Queue<Vector3Int> _pendingChunks = new();
	private readonly Queue<VoxelChunk> _completedChunks = new();
	private readonly Queue<Vector3Int> _pendingWarmChunks = new();
	private readonly Queue<WarmChunkResult> _completedWarmChunks = new();
	private readonly List<Vector3Int> _coordinateBuffer = new();
	private readonly List<Vector3Int> _warmCoordinateBuffer = new();
	private readonly List<Vector3Int> _gameplayEnteringBuffer = new();
	private readonly List<Vector3Int> _gameplayLeavingBuffer = new();
	private readonly List<Vector3Int> _renderEnteringBuffer = new();
	private readonly List<Vector3Int> _renderLeavingBuffer = new();
	private readonly HashSet<Vector3Int> _coordinateSetBuffer = new();
	private HashSet<Vector3Int> _lod0RenderActive = new();
	private HashSet<Vector3Int> _nextLod0RenderActive = new();
	private readonly Lod1ClipboxState _lod1Clipbox = new();
	private readonly HashSet<Vector3Int> _nextLod1Cache = new();
	private readonly HashSet<Vector3Int> _nextLod1Active = new();
	private readonly List<Vector3Int> _lod1EnteringBuffer = new();
	private readonly List<Vector3Int> _lod1LeavingBuffer = new();
	private readonly HashSet<Lod0Lod1TransitionKey> _transitionDesired = new();
	private readonly HashSet<Lod0Lod1TransitionKey> _nextTransitionDesired = new();
	private readonly List<Lod0Lod1TransitionKey> _transitionEnteringBuffer = new();
	private readonly List<Lod0Lod1TransitionKey> _transitionLeavingBuffer = new();
	private readonly List<Lod0Lod1TransitionKey> _transitionRetainedBuffer = new();
	private readonly Lod2ClipboxState _lod2Clipbox = new();
	private readonly HashSet<Vector3Int> _nextLod2Cache = new();
	private readonly HashSet<Vector3Int> _nextLod2Active = new();
	private readonly List<Vector3Int> _lod2EnteringBuffer = new();
	private readonly List<Vector3Int> _lod2LeavingBuffer = new();
	private readonly float[] _performanceFrameMilliseconds = new float[MaximumPerformanceFrameSamples];
	private readonly float[] _sortedPerformanceFrameMilliseconds = new float[MaximumPerformanceFrameSamples];
	private readonly float[] _performanceGpuMilliseconds = new float[MaximumPerformanceFrameSamples];
	private readonly float[] _sortedPerformanceGpuMilliseconds = new float[MaximumPerformanceFrameSamples];
	private GpuVoxelMesher _gpuMesher;
	private long _gpuRenderUpdateEpoch;

	private bool _hasStreamingCenter;
	private bool _streamInProgress;
	private bool _hasObservedStreamingFrame;
	private bool _completionReady;
	private bool _initialLoadCompleted;
	private Vector3Int _streamingCenterCoordinate;
	private long _streamStartedTimestamp;
	private int _generatedThisStream;
	private int _retainedThisStream;
	private int _unloadedThisStream;
	private int _staleDiscardedThisStream;
	private float _generationMillisecondsThisStream;
	private float _integrationMillisecondsThisStream;
	private float _slowestIntegrationFrameMilliseconds;
	private float _maximumObservedFrameMilliseconds;
	private int _generationBatchesThisStream;
	private int _maximumGenerationBatchSizeThisStream;
	private float _firstGenerationBatchMilliseconds;
	private float _firstGameplayIntegrationMilliseconds;
	private bool _integratedBeforeWorkerCompleted;
	private int _appliedCellsPerAxis;
	private float _appliedCellSize;
	private int _appliedWorldSeed;
	private float _appliedSurfaceBaseHeight;
	private float _appliedSurfaceFrequency;
	private float _appliedSurfaceAmplitude;
	private int _streamRevision;
	private int _terrainContentRevision;
	private bool _workerCompleted;
	private CancellationTokenSource _generationCancellation;
	private System.Threading.Tasks.Task _generationTask = System.Threading.Tasks.Task.CompletedTask;
	private int _warmGenerationRevision;
	private bool _warmWorkerCompleted;
	private CancellationTokenSource _warmGenerationCancellation;
	private System.Threading.Tasks.Task _warmGenerationTask = System.Threading.Tasks.Task.CompletedTask;
	private string _lastConfigurationError = string.Empty;
	private GameObject _resolvedStreamingTarget;
	private bool _playerFigureEightEnabled;
	private GameObject _playerFigureEightTarget;
	private Rigidbody _playerFigureEightBody;
	private Vector2 _playerFigureEightCenter;
	private float _playerFigureEightParameter;
	private float _playerFigureEightRouteDistance;
	private Vector2 _playerFigureEightPreviousPosition;
	private bool _playerFigureEightTestRunning;
	private bool _playerFigureEightTestCompletionReady;
	private int _playerFigureEightCompletedLoops;
	private int _playerFigureEightTargetLoops;
	private float _playerFigureEightTestSpeed;
	private float _playerFigureEightTestDistance;
	private string _playerFigureEightTestTask;
	private string _playerFigureEightTestRevision;
	private float _performanceWindowElapsedSeconds;
	private float _memorySampleElapsedSeconds;
	private int _performanceObservedFrameCount;
	private int _performanceFrameSampleCount;
	private int _performanceTruncatedFrameSampleCount;
	private double _performanceFrameMillisecondsTotal;
	private double _performanceGpuFrameMillisecondsTotal;
	private int _performanceGpuFrameSampleCount;
	private double _performanceProcessMemoryBytesTotal;
	private ulong _performancePeakProcessMemoryBytes;
	private double _performanceGpuMemoryBytesTotal;
	private ulong _performancePeakGpuMemoryBytes;
	private ulong _performanceGpuMemoryBudgetBytes;
	private ulong _performanceStartProcessMemoryBytes;
	private ulong _performanceStartGpuMemoryBytes;
	private int _performanceMemorySampleCount;
	private int _performanceChunksIntegrated;
	private bool _performanceSnapshotReady;
	private bool _performanceVisibilityPending;
	private bool _performanceSettledCaptureRequested;
	private PerformanceCompletionPhase _performanceCompletionPhase;
	private long _performanceStationaryRenderSequenceTarget;
	private GpuVisibilityMeasurement _lastPerformanceVisibility;
	private GpuVisibilityMeasurement _lastStationaryVisibility;
	private PerformanceStationaryMetrics _lastStationaryMetrics = new();
	private float _lastPerformanceWindowSeconds;
	private int _lastPerformanceFrameSampleCount;
	private int _lastPerformanceTruncatedFrameSampleCount;
	private float _lastAverageFramesPerSecond;
	private float _lastP95FrameMilliseconds;
	private float _lastP99FrameMilliseconds;
	private float _lastAverageGpuFrameMilliseconds;
	private float _lastP95GpuFrameMilliseconds;
	private float _lastP99GpuFrameMilliseconds;
	private float _lastMaximumGpuFrameMilliseconds;
	private ulong _lastAverageProcessMemoryBytes;
	private ulong _lastPeakProcessMemoryBytes;
	private ulong _lastStartProcessMemoryBytes;
	private ulong _lastEndProcessMemoryBytes;
	private ulong _lastAverageGpuMemoryBytes;
	private ulong _lastPeakGpuMemoryBytes;
	private ulong _lastStartGpuMemoryBytes;
	private ulong _lastEndGpuMemoryBytes;
	private ulong _lastGpuMemoryBudgetBytes;
	private int _lastPerformanceChunksIntegrated;
	private long _performanceMesherDispatchStart;
	private long _performanceMesherPoolAllocationStart;
	private long _performanceMesherPoolReuseStart;
	private long _performanceMesherScalarReadbackStart;
	private long _lastPerformanceMeshDispatches;
	private long _lastPerformanceMeshPoolAllocations;
	private long _lastPerformanceMeshPoolReuses;
	private long _lastPerformanceMeshScalarReadbacks;
	private int _performancePeakMeshDispatchesPerUpdate;
	private int _lastPerformancePeakMeshDispatchesPerUpdate;
	private long _performanceTerrainSubmissionTotal;
	private int _performanceTerrainSubmissionMaximum;
	private long _performanceIndirectRecordTotal;
	private int _performanceIndirectRecordMaximum;
	private long _performanceTerrainBufferGroupTotal;
	private int _performanceTerrainBufferGroupMaximum;
	private int _performanceSubmissionSampleCount;
	private PerformanceSubmissionMetrics _lastPerformanceSubmission = new();
	private float _lastPerformanceChunksPerSecond;
	private bool _lastPerformanceWasFigureEightTest;
	private int _lastPerformanceCompletedLoops;
	private float _lastPerformanceTestSpeed;
	private float _lastPerformanceTestDistance;
	private PerformanceStreamingMetrics _performanceStreaming = new();
	private PerformanceStreamingMetrics _lastPerformanceStreaming = new();
	private PerformanceBoundsMetrics _performanceBounds = new();
	private PerformanceBoundsMetrics _lastPerformanceBounds = new();
	private GpuMeshScheduleLatencyMeasurement _lastPerformanceScheduleLatency;
	private GpuMeshThroughputMeasurement _lastPerformanceThroughput;
	private GpuTransitionMeasurement _lastPerformanceTransitions;
	private GpuLod2Measurement _lastPerformanceLod2;
	private int _lastTransitionEntered;
	private int _lastTransitionLeft;
	private int _lastTransitionRetained;
	private long _transitionEntered;
	private long _transitionLeft;
	private PerformanceProfilerMetrics _lastPerformanceProfiler = new();
	private GameObject ActiveStreamingTarget => StreamingTarget ?? _resolvedStreamingTarget ?? GameObject;
	private ProceduralTerrainSettings CurrentTerrainSettings => new(
		WorldSeed,
		SurfaceBaseHeight,
		SurfaceFrequency,
		SurfaceAmplitude );

	[Property, Category( "Chunk Configuration" ), Range( 4, 64 )]
	public int CellsPerAxis { get; set; } = 32;

	[Property, Category( "Chunk Configuration" ), Range( 1f, 128f )]
	public float CellSize { get; set; } = 16f;

	public const int LoadRadius = GameplayLoadRadius;
	private static int AuthoritativeGameplayRadius => PlacementInputs.GameplayRadius;

	[Property, Category( "Terrain Generation" )]
	public int WorldSeed { get; set; } = ProceduralTerrainSdf.DefaultWorldSeed;

	[Property, Category( "Terrain Generation" ), Range( -4096f, 4096f )]
	public float SurfaceBaseHeight { get; set; } = ProceduralTerrainSdf.DefaultSurfaceBaseHeight;

	[Property, Category( "Terrain Generation" ), Range( 0.0001f, 0.1f )]
	public float SurfaceFrequency { get; set; } = ProceduralTerrainSdf.DefaultSurfaceFrequency;

	[Property, Category( "Terrain Generation" ), Range( 0f, 4096f )]
	public float SurfaceAmplitude { get; set; } = ProceduralTerrainSdf.DefaultSurfaceAmplitude;

	[Property, Category( "Chunk Configuration" )]
	public GameObject StreamingTarget { get; set; }

	[Property, Category( "Smoke Test" ), Range( 1f, 10000f )]
	public float FigureEightSpeed { get; set; } = 2500f;

	[Property, Category( "Smoke Test" ), Range( 1f, 1000000f )]
	public float FigureEightDistance { get; set; } = 50000f;

	[Property, Category( "Smoke Test" ), Range( 1, MaximumFigureEightLoopCount )]
	public int FigureEightLoopCount { get; set; } = 1;

	[Property, Category( "Performance Test" )]
	public string PerformanceTask { get; set; } = InspectorPerformanceTask;

	[Property, Category( "Performance Test" )]
	public string PerformanceRevision { get; set; } = InspectorPerformanceRevision;

	[Property, Category( "Diagnostics" )]
	public bool VerboseLogging { get; set; } = false;

	[Property, ReadOnly, Category( "Performance Test" )]
	public string PerformanceResultsLocation { get; private set; } = PerformanceResultsPath;

	[Property, ReadOnly, Category( "Performance Test" )]
	public string LastPerformanceRunId { get; private set; } = "No saved run";

	[Property, ReadOnly, Category( "World Status" )]
	public string FramePerformance { get; private set; } = "Collecting first 10-second window";

	[Property, ReadOnly, Category( "World Status" )]
	public string ChunkStatus { get; private set; } = "Not initialized";

	[Property, ReadOnly, Category( "World Status" )]
	public string GeneratorStatus { get; private set; } = "Not initialized";

	[Property, ReadOnly, Category( "World Status" )]
	public string StreamingPerformance { get; private set; } = "No stream completed";

	[Property, ReadOnly, Category( "World Status" )]
	public string ProcessMemoryUsage { get; private set; } = "Not measured";

	public int LoadedChunkCount { get; private set; }

	public int PendingChunkCount { get; private set; }

	public string PlayerChunk { get; private set; } = "Not initialized";

	public string PlayerChunkData { get; private set; } = "Not initialized";

	public string LoadedChunkRange { get; private set; } = "Not initialized";

	public string LastStreamSummary { get; private set; } = "No stream completed";

	public float LastStreamSettleMilliseconds { get; private set; }

	public float LastChunkGenerationMilliseconds { get; private set; }

	public float SlowestChunkGenerationMilliseconds { get; private set; }

	public float LastStreamGenerationMilliseconds { get; private set; }

	public float LastBackgroundWorkerMilliseconds { get; private set; }

	public float LastStreamIntegrationMilliseconds { get; private set; }

	public float SlowestIntegrationFrameMilliseconds { get; private set; }

	public float MaximumObservedFrameMilliseconds { get; private set; }

	public float LastEffectiveChunksPerSecond { get; private set; }

	public float LastGenerationChunksPerSecond { get; private set; }

	public string StreamingTargetStatus { get; private set; } = "Manager object (no target assigned)";

	public int LastRetainedChunkCount { get; private set; }

	public int LastUnloadedChunkCount { get; private set; }

	public int LastGeneratedChunkCount { get; private set; }

	public int LastStaleDiscardedChunkCount { get; private set; }

	protected override async System.Threading.Tasks.Task OnLoad()
	{
		ResolveStreamingTarget();
		_gpuMesher = new GpuVoxelMesher( Scene, CellsPerAxis );
		ApplyConfigurationAndRebuild();

		while ( _streamInProgress )
		{
			if ( IntegrateCompletedChunks() )
			{
				RefreshReadableStatus();
			}

			if ( _completionReady )
			{
				CompleteStream();
				RefreshReadableStatus();
			}

			await Task.Yield();
		}

		_initialLoadCompleted = _loadedChunks.Count == _desiredChunks.Count &&
			_pendingChunks.Count == 0;
		if ( VerboseLogging )
		{
			Log.Info(
				$"[VoxelWorld] load.complete ready={_initialLoadCompleted} loaded={_loadedChunks.Count} " +
				$"pending={_pendingChunks.Count}" );
		}
	}

	protected override void OnStart()
	{
		if ( !_initialLoadCompleted )
		{
			Log.Error( "[VoxelWorld] start.rejected reason=\"initial chunk load did not complete\"" );
			return;
		}

		_performanceSnapshotReady = false;
		FramePerformance = "Collecting first 10-second window";
		ProcessMemoryUsage = "Collecting first 10-second window";
		ResetPerformanceWindow();
		RefreshReadableStatus();
	}

	protected override void OnUpdate()
	{
		using var profiler = global::Sandbox.Diagnostics.Performance.Scope(
			VoxelPerformanceProfiler.ManagerUpdate );
		TrySaveCompletedPerformanceTest();
		UpdatePlayerFigureEight();
		UpdatePerformanceOverview();

		if ( _streamInProgress )
		{
			if ( _hasObservedStreamingFrame )
			{
				_maximumObservedFrameMilliseconds = Math.Max(
					_maximumObservedFrameMilliseconds,
					RealTime.Delta * 1000f );
			}

			_hasObservedStreamingFrame = true;
			if ( _completionReady )
			{
				CompleteStream();
				RefreshReadableStatus();
			}
		}

		if ( !TryValidateConfiguration( out var configurationError ) )
		{
			if ( configurationError != _lastConfigurationError )
			{
				_generationCancellation?.Cancel();
				_warmGenerationCancellation?.Cancel();
				_streamRevision++;
				_warmGenerationRevision++;
				_gpuMesher?.Clear();
				_loadedChunks.Clear();
				_desiredChunks.Clear();
				_renderDesiredChunks.Clear();
				_nextRenderDesiredChunks.Clear();
				_renderPreparedChunks.Clear();
				_pendingChunks.Clear();
				_completedChunks.Clear();
				_pendingWarmChunks.Clear();
				_completedWarmChunks.Clear();
				_hasStreamingCenter = false;
				_streamInProgress = false;
				_lastConfigurationError = configurationError;
				Log.Warning( $"[VoxelWorld] configuration.invalid reason=\"{configurationError}\"" );
				RefreshReadableStatus();
			}

			TryCompletePlayerFigureEightTest();
			return;
		}

		_lastConfigurationError = string.Empty;

		if ( DataConfigurationChanged() )
		{
			ApplyConfigurationAndRebuild();
		}
		else
		{
			var targetPosition = ActiveStreamingTarget.WorldPosition;
			var targetCoordinate = WorldToChunkCoordinate( targetPosition );
			if ( !_hasStreamingCenter || targetCoordinate != _streamingCenterCoordinate )
			{
				RebuildDesiredChunks( targetCoordinate, "streaming target crossed a chunk boundary" );
			}
		}

		if ( IntegrateCompletedChunks() )
		{
			RefreshReadableStatus();
		}
		else if ( IntegrateCompletedWarmChunks() )
		{
			RefreshReadableStatus();
		}
		else
		{
			RefreshPlayerChunkStatus();
		}
		var meshDispatches = _gpuMesher.ProcessPending(
			GpuVoxelMesher.MaximumDispatchesPerUpdate,
			++_gpuRenderUpdateEpoch );
		if ( _playerFigureEightTestRunning )
		{
			_performancePeakMeshDispatchesPerUpdate = Math.Max(
				_performancePeakMeshDispatchesPerUpdate,
				meshDispatches );
		}
		TryCompletePlayerFigureEightTest();
		TrySaveCompletedPerformanceTest();
	}

	protected override void OnDestroy()
	{
		_playerFigureEightEnabled = false;
		_playerFigureEightTarget = null;
		_playerFigureEightBody = null;
		_playerFigureEightTestRunning = false;
		_playerFigureEightTestCompletionReady = false;
		_generationCancellation?.Cancel();
		_warmGenerationCancellation?.Cancel();
		_gpuMesher?.Dispose();
		_gpuMesher = null;
	}

	protected override void OnValidate()
	{
		RefreshReadableStatus();
	}

	[Button( "Run Performance Test" )]
	public void RunPerformanceTestFromInspector()
	{
		try
		{
			var task = PerformanceTask?.Trim();
			if ( string.IsNullOrWhiteSpace( task ) ||
				task.Equals( "unassigned", StringComparison.OrdinalIgnoreCase ) )
			{
				task = InspectorPerformanceTask;
			}

			var revision = PerformanceRevision?.Trim();
			if ( string.IsNullOrWhiteSpace( revision ) ||
				revision.Equals( "unassigned", StringComparison.OrdinalIgnoreCase ) )
			{
				revision = InspectorPerformanceRevision;
			}

			PerformanceTask = task;
			PerformanceRevision = revision;
			var result = StartPerformanceTest(
				FigureEightSpeed,
				FigureEightDistance,
				FigureEightLoopCount,
				task,
				revision );
			if ( VerboseLogging )
			{
				Log.Info( $"[VoxelWorld] performance.test {result}" );
			}
		}
		catch ( Exception exception )
		{
			Log.Warning( $"[VoxelWorld] performance.test.rejected reason=\"{exception.Message}\"" );
		}
	}

	private void StartPlayerFigureEight( float speed, float distance )
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before running the player figure-eight." );
		}

		if ( !float.IsFinite( speed ) || speed <= 0f )
		{
			throw new ArgumentOutOfRangeException( nameof( speed ), "Speed must be finite and greater than zero." );
		}

		if ( !float.IsFinite( distance ) || distance <= 0f )
		{
			throw new ArgumentOutOfRangeException( nameof( distance ), "Distance must be finite and greater than zero." );
		}

		var target = ActiveStreamingTarget;
		PlayerController player = null;
		foreach ( var candidate in Scene.GetAllComponents<PlayerController>() )
		{
			if ( candidate.GameObject == target && !candidate.IsProxy )
			{
				player = candidate;
				break;
			}
		}

		if ( player is null )
		{
			throw new InvalidOperationException( "The local player must be the VoxelManager streaming target." );
		}

		FigureEightSpeed = speed;
		FigureEightDistance = distance;
		var start = player.WorldPosition;
		_playerFigureEightTarget = player.GameObject;
		_playerFigureEightBody = player.Body;
		_playerFigureEightCenter = new Vector2( start.x, start.y );
		_playerFigureEightParameter = 0f;
		_playerFigureEightRouteDistance = 0f;
		_playerFigureEightPreviousPosition = new Vector2( start.x, start.y );
		_playerFigureEightEnabled = true;
		SetFigureEightPosition( start.x, start.y );
	}

	public string StartPerformanceTest(
		float speed,
		float distance,
		int loopCount,
		string task,
		string revision )
	{
		if ( _playerFigureEightTestRunning )
		{
			throw new InvalidOperationException( "A player figure-eight performance test is already running." );
		}

		if ( loopCount < 1 || loopCount > MaximumFigureEightLoopCount )
		{
			throw new ArgumentOutOfRangeException(
				nameof( loopCount ),
				$"Loop count must be between 1 and {MaximumFigureEightLoopCount}." );
		}

		var normalizedTask = RequirePerformanceContext( task, nameof( task ) );
		var normalizedRevision = RequirePerformanceContext( revision, nameof( revision ) );
		if ( _gpuMesher is null || _gpuMesher.AllPendingCount > 0 )
		{
			throw new InvalidOperationException( "All enabled visual LOD levels must be settled before the test starts." );
		}
		StartPlayerFigureEight( speed, distance );
		FigureEightLoopCount = loopCount;
		PerformanceTask = normalizedTask;
		PerformanceRevision = normalizedRevision;
		_playerFigureEightCompletedLoops = 0;
		_playerFigureEightTargetLoops = loopCount;
		_playerFigureEightTestSpeed = speed;
		_playerFigureEightTestDistance = distance;
		_playerFigureEightTestTask = normalizedTask;
		_playerFigureEightTestRevision = normalizedRevision;
		_playerFigureEightTestCompletionReady = false;
		_playerFigureEightTestRunning = true;
		_performanceSnapshotReady = false;
		_performanceVisibilityPending = false;
		_performanceCompletionPhase = PerformanceCompletionPhase.None;
		_lastPerformanceVisibility = default;
		_lastStationaryVisibility = default;
		_lastStationaryMetrics = new PerformanceStationaryMetrics();
		FramePerformance = $"Figure-eight test running: 0 of {loopCount} loops";
		ProcessMemoryUsage = "Collecting figure-eight test window";
		ResetPerformanceWindow();
		SamplePerformanceMemory();
		_gpuMesher?.BeginVisibilityMeasurement();
		_gpuMesher?.BeginScheduleLatencyMeasurement();
		_gpuMesher?.BeginThroughputMeasurement( CellsPerAxis * CellSize );
		_gpuMesher?.BeginTransitionMeasurement();
		_gpuMesher?.BeginLod2Measurement();
		Log.Info( string.Concat(
			FormattableString.Invariant(
				$"[VoxelWorld] performance.test.begin task=\"{EscapeLogValue( _playerFigureEightTestTask )}\" " ),
			FormattableString.Invariant(
				$"revision=\"{EscapeLogValue( _playerFigureEightTestRevision )}\" loops={loopCount} " ),
			FormattableString.Invariant( $"speed={speed:0.###} distance={distance:0.###} " ),
			FormattableString.Invariant(
				$"center=[{_playerFigureEightCenter.x:0.###},{_playerFigureEightCenter.y:0.###},0]" ) ) );
		return $"test started loops={loopCount} speed={speed} distance={distance}";
	}

	private string SavePerformanceResult()
	{
		if ( !_performanceSnapshotReady || !_lastPerformanceWasFigureEightTest )
		{
			throw new InvalidOperationException( "A complete performance-test snapshot is required before saving." );
		}

		var target = ActiveStreamingTarget;
		var targetPosition = target.WorldPosition;
		var sceneName = Scene?.Name ?? "unknown";
		var runId = Guid.NewGuid().ToString( "N" );
		var meshingGpuSmoothedMilliseconds = 0f;
		var meshingGpuMaximumMilliseconds = 0f;
		var meshingGpuProfilerPath = string.Empty;
		foreach ( var path in global::Sandbox.Diagnostics.GpuProfilerStats.Entries )
		{
			if ( !path.Contains( "Voxel Terrain Meshing", StringComparison.Ordinal ) )
			{
				continue;
			}

			meshingGpuSmoothedMilliseconds = Math.Max(
				meshingGpuSmoothedMilliseconds,
				global::Sandbox.Diagnostics.GpuProfilerStats.GetSmoothedDuration( path ) );
			meshingGpuMaximumMilliseconds = Math.Max(
				meshingGpuMaximumMilliseconds,
				global::Sandbox.Diagnostics.GpuProfilerStats.GetMaxDuration( path ) );
			meshingGpuProfilerPath = path;
		}

		var result = new PerformanceTestResult
		{
			SchemaVersion = PerformanceResultSchemaVersion,
			RunId = runId,
			CapturedAtUtc = DateTimeOffset.UtcNow.ToString( "O" ),
			Outcome = "completed",
			Source = new PerformanceTestSource
			{
				Task = _playerFigureEightTestTask,
				Revision = _playerFigureEightTestRevision
			},
			Test = new PerformanceTestDefinition
			{
				Name = "player-figure-eight",
				CompletedLoops = _lastPerformanceCompletedLoops,
				Speed = _lastPerformanceTestSpeed,
				Distance = _lastPerformanceTestDistance,
				WorldHeight = 0f,
				DurationSeconds = _lastPerformanceWindowSeconds,
				StartCenter = new PerformanceVector2
				{
					X = _playerFigureEightCenter.x,
					Y = _playerFigureEightCenter.y
				}
			},
			World = new PerformanceWorldContext
			{
				Scene = sceneName,
				CellsPerAxis = CellsPerAxis,
				CellSize = CellSize,
				LoadRadius = AuthoritativeGameplayRadius,
				Generator = "deterministic-simplex-caves",
				WorldSeed = WorldSeed,
				GeneratorVersion = ProceduralTerrainSdf.CurrentVersion,
				SurfaceBaseHeight = SurfaceBaseHeight,
				SurfaceFrequency = SurfaceFrequency,
				SurfaceAmplitude = SurfaceAmplitude,
				StreamingCenter = new PerformanceVector3Int
				{
					X = _streamingCenterCoordinate.x,
					Y = _streamingCenterCoordinate.y,
					Z = _streamingCenterCoordinate.z
				},
				TargetPosition = new PerformanceVector3
				{
					X = targetPosition.x,
					Y = targetPosition.y,
					Z = targetPosition.z
				}
			},
			Frame = new PerformanceFrameMetrics
			{
				Samples = _lastPerformanceFrameSampleCount,
				TruncatedSamples = _lastPerformanceTruncatedFrameSampleCount,
				AverageFps = _lastAverageFramesPerSecond,
				P95Milliseconds = _lastP95FrameMilliseconds,
				P99Milliseconds = _lastP99FrameMilliseconds,
				AverageGpuMilliseconds = _lastAverageGpuFrameMilliseconds,
				P95GpuMilliseconds = _lastP95GpuFrameMilliseconds,
				P99GpuMilliseconds = _lastP99GpuFrameMilliseconds,
				MaximumGpuMilliseconds = _lastMaximumGpuFrameMilliseconds
			},
			Stationary = _lastStationaryMetrics,
			Memory = new PerformanceMemoryMetrics
			{
				StartProcessBytes = _lastStartProcessMemoryBytes,
				EndProcessBytes = _lastEndProcessMemoryBytes,
				AverageProcessBytes = _lastAverageProcessMemoryBytes,
				PeakProcessBytes = _lastPeakProcessMemoryBytes,
				StartGpuBytes = _lastStartGpuMemoryBytes,
				EndGpuBytes = _lastEndGpuMemoryBytes,
				AverageGpuBytes = _lastAverageGpuMemoryBytes,
				PeakGpuBytes = _lastPeakGpuMemoryBytes,
				GpuBudgetBytes = _lastGpuMemoryBudgetBytes
			},
			Chunks = new PerformanceChunkMetrics
			{
				Loaded = _loadedChunks.Count,
				Pending = _pendingChunks.Count,
				Integrated = _lastPerformanceChunksIntegrated,
				IntegratedPerSecond = _lastPerformanceChunksPerSecond,
				LastStreamGenerated = LastGeneratedChunkCount,
				LastStreamSettleMilliseconds = LastStreamSettleMilliseconds,
				LastEffectivePerSecond = LastEffectiveChunksPerSecond,
				LastGenerationPerSecond = LastGenerationChunksPerSecond
			},
			Meshing = new PerformanceMeshingMetrics
			{
				ConfiguredMaximumDispatchesPerUpdate = GpuVoxelMesher.MaximumDispatchesPerUpdate,
				ObservedMaximumDispatchesPerUpdate = _lastPerformancePeakMeshDispatchesPerUpdate,
				Dispatches = _lastPerformanceMeshDispatches,
				Resident = _gpuMesher?.ResidentCount ?? 0,
				GameplayResident = (_gpuMesher?.ResidentCount ?? 0) - (_gpuMesher?.WarmResidentCount ?? 0),
				WarmResident = _gpuMesher?.WarmResidentCount ?? 0,
				Lod0Resident = _gpuMesher?.Lod0ResidentCount ?? 0,
				Lod1Resident = _gpuMesher?.Lod1ResidentCount ?? 0,
				Lod2Resident = _gpuMesher?.Lod2ResidentCount ?? 0,
				Pending = _gpuMesher?.PendingCount ?? 0,
				AllPending = _gpuMesher?.AllPendingCount ?? 0,
				GameplayPending = _gpuMesher?.PendingGameplayCount ?? 0,
				WarmPending = _gpuMesher?.PendingWarmCount ?? 0,
				Lod1Pending = _gpuMesher?.PendingLod1Count ?? 0,
				Lod2Pending = _gpuMesher?.PendingLod2Count ?? 0,
				PoolAvailable = _gpuMesher?.PoolCount ?? 0,
				LogicalCapacityBytes = _gpuMesher?.LogicalCapacityBytes ?? 0,
				ReservedActiveCellCapacity = _gpuMesher?.ReservedActiveCellCapacity ?? 0,
				ReservedActiveCellCapacityBytes = _gpuMesher?.ReservedActiveCellCapacityBytes ?? 0,
				SettledSlabs = _gpuMesher?.TerrainIndirectApiSubmissionCount ?? 0,
				SettledSurfaceMeshes = _lastPerformanceVisibility.SettledSurfaceMeshes,
				SettledWarmSurfaceMeshes = _lastPerformanceVisibility.SettledWarmSurfaceMeshes,
				TotalActiveCells = _lastPerformanceVisibility.SettledActiveCells,
				AverageActiveCellsPerSurfaceChunk =
					_lastPerformanceVisibility.SettledSurfaceMeshes > 0
						? (float)_lastPerformanceVisibility.SettledActiveCells /
							_lastPerformanceVisibility.SettledSurfaceMeshes
						: 0f,
				MaximumActiveCellsPerSurfaceChunk =
					_lastPerformanceVisibility.SettledMaximumActiveCells,
				ActiveCellUtilizationPercent = (_gpuMesher?.ReservedActiveCellCapacity ?? 0) > 0
					? (float)(_lastPerformanceVisibility.SettledActiveCells * 100d /
						(_gpuMesher?.ReservedActiveCellCapacity ?? 0))
					: 0f,
				PoolAllocations = _lastPerformanceMeshPoolAllocations,
				PoolReuses = _lastPerformanceMeshPoolReuses,
				GameThreadAllocatedBytes = null,
				ScalarReadbacks = _lastPerformanceMeshScalarReadbacks,
				GeometryReadbacks = _gpuMesher?.GeometryReadbackCount ?? 0,
				OrdinaryRenderSdfEvaluations = GpuVoxelMesher.OrdinaryRenderSdfEvaluationCount,
				UniqueVertices = _gpuMesher?.UniqueVertexCount ?? 0,
				Triangles = _gpuMesher?.TriangleCount ?? 0,
				Indices = _gpuMesher?.IndexCount ?? 0,
				UsedVertexBytes = _gpuMesher?.UsedVertexBytes ?? 0,
				UsedIndexBytes = _gpuMesher?.UsedIndexBytes ?? 0,
				CommittedVertexBytes = _gpuMesher?.CommittedVertexBytes ?? 0,
				CommittedIndexBytes = _gpuMesher?.CommittedIndexBytes ?? 0,
				ArenaCount = _gpuMesher?.ArenaCount ?? 0,
				FreeRangeCount = _gpuMesher?.FreeRangeCount ?? 0,
				LargestFreeVertexRange = _gpuMesher?.LargestFreeVertexRange ?? 0,
				LargestFreeIndexRange = _gpuMesher?.LargestFreeIndexRange ?? 0,
				FragmentationPercent = _gpuMesher?.FragmentationPercent ?? 0,
				TransientScratchBytes = _gpuMesher?.TransientScratchBytes ?? 0,
				Lod2TransientScratchBytes = _gpuMesher?.Lod2TransientScratchBytes ?? 0,
				AllocationCountReadbacks = _gpuMesher?.CountReadbackCount ?? 0,
				AllocationCountReadbackBytes = _gpuMesher?.CountReadbackBytes ?? 0,
				AllocationCountReadbackMilliseconds = _gpuMesher?.CountReadbackMilliseconds ?? 0,
				CountStageSubmissionMilliseconds = _gpuMesher?.CountSubmissionMilliseconds ?? 0,
				EmitStageSubmissionMilliseconds = _gpuMesher?.EmitSubmissionMilliseconds ?? 0,
				TopologyDigest = _gpuMesher?.TopologyDigest ?? string.Empty,
				PositionDigest = _gpuMesher?.PositionDigest ?? string.Empty,
				Lod0TopologyDigest = _gpuMesher?.Lod0TopologyDigest ?? string.Empty,
				Lod0PositionDigest = _gpuMesher?.Lod0PositionDigest ?? string.Empty,
				Lod1TopologyDigest = _gpuMesher?.Lod1TopologyDigest ?? string.Empty,
				Lod1PositionDigest = _gpuMesher?.Lod1PositionDigest ?? string.Empty,
				Lod2TopologyDigest = _gpuMesher?.Lod2TopologyDigest ?? string.Empty,
				Lod2PositionDigest = _gpuMesher?.Lod2PositionDigest ?? string.Empty,
				GpuProfilerPath = meshingGpuProfilerPath,
				AverageGpuMilliseconds = meshingGpuSmoothedMilliseconds,
				MaximumGpuMilliseconds = meshingGpuMaximumMilliseconds,
				ScheduleToRenderable = new PerformanceLatencyMetrics
				{
					Samples = _lastPerformanceScheduleLatency.Samples,
					TruncatedSamples = _lastPerformanceScheduleLatency.TruncatedSamples,
					P50Milliseconds = _lastPerformanceScheduleLatency.P50Milliseconds,
					P95Milliseconds = _lastPerformanceScheduleLatency.P95Milliseconds,
					P99Milliseconds = _lastPerformanceScheduleLatency.P99Milliseconds,
					MaximumMilliseconds = _lastPerformanceScheduleLatency.MaximumMilliseconds,
					Cancelled = _lastPerformanceScheduleLatency.Cancelled,
					Superseded = _lastPerformanceScheduleLatency.Superseded
				},
				Throughput = CreateThroughputMetrics( _lastPerformanceThroughput )
			},
			Visibility = CreateVisibilityMetrics( _lastPerformanceVisibility ),
			Submission = _lastPerformanceSubmission,
			Streaming = _lastPerformanceStreaming,
			Bounds = _lastPerformanceBounds,
			Profiler = _lastPerformanceProfiler,
			Transitions = new PerformanceTransitionMetrics
			{
				Desired = _lastPerformanceTransitions.Desired,
				Ready = _lastPerformanceTransitions.Ready,
				Drawable = _lastPerformanceTransitions.Drawable,
				Pending = _lastPerformanceTransitions.Pending,
				LastEntered = _lastTransitionEntered,
				LastLeft = _lastTransitionLeft,
				LastRetained = _lastTransitionRetained,
				Entered = _transitionEntered,
				Left = _transitionLeft,
				Scheduled = _lastPerformanceTransitions.Scheduled,
				Published = _lastPerformanceTransitions.Published,
				Cancelled = _lastPerformanceTransitions.Cancelled,
				Stale = _lastPerformanceTransitions.Stale,
				ActiveCells = _lastPerformanceTransitions.ActiveCells,
				Vertices = _lastPerformanceTransitions.Vertices,
				Indices = _lastPerformanceTransitions.Indices,
				Triangles = _lastPerformanceTransitions.Indices / 3,
				UsedVertexBytes = _lastPerformanceTransitions.Vertices * GpuVoxelMesher.TerrainVertexBytes,
				UsedIndexBytes = _lastPerformanceTransitions.Indices * sizeof( uint ),
				TransientScratchBytes = _gpuMesher?.TransitionTransientScratchBytes ?? 0,
				TopologyDigest = _lastPerformanceTransitions.TopologyDigest,
				PositionDigest = _lastPerformanceTransitions.PositionDigest,
				FineFaceMismatchCount = _lastPerformanceTransitions.FineFaceMismatchCount,
				CoarseFaceMismatchCount = _lastPerformanceTransitions.CoarseFaceMismatchCount,
				LateralEdgeDigest = _lastPerformanceTransitions.LateralEdgeDigest,
				LateralMismatchCount = _lastPerformanceTransitions.LateralMismatchCount,
				InvalidTableCount = _lastPerformanceTransitions.InvalidTableCount,
				Faces = _lastPerformanceTransitions.Faces.Select( face =>
					new PerformanceTransitionFaceMetrics
					{
						Lod1Coordinate = ToPerformanceVector( face.Key.Lod1Coordinate ),
						Face = face.Key.Face.ToString(),
						Generation = face.Generation,
						Arena = face.Arena,
						Slot = face.Slot,
						VertexOffset = face.VertexOffset,
						VertexCount = face.VertexCount,
						IndexOffset = face.IndexOffset,
						IndexCount = face.IndexCount,
						ActiveCells = face.ActiveCells,
						ScheduleToPublicationMilliseconds = face.ScheduleToPublicationMilliseconds,
						TopologyDigest = face.TopologyDigest.ToString( "X8" ),
						PositionDigest = face.PositionDigest.ToString( "X8" ),
						FineFaceMismatchCount = face.FineFaceMismatchCount,
						CoarseFaceMismatchCount = face.CoarseFaceMismatchCount,
						MinimumUDigest = face.MinimumUDigest,
						MaximumUDigest = face.MaximumUDigest,
						MinimumVDigest = face.MinimumVDigest,
						MaximumVDigest = face.MaximumVDigest,
						InvalidTableCount = face.InvalidTableCount
					} ).ToArray(),
				ScheduleToPublication = CreateDistributionMetrics(
					_lastPerformanceTransitions.ScheduleToPublication )
			},
			Lod2 = new PerformanceLod2Metrics
			{
				Scheduled = _lastPerformanceLod2.Scheduled,
				Published = _lastPerformanceLod2.Published,
				Cancelled = _lastPerformanceLod2.Cancelled,
				Superseded = _lastPerformanceLod2.Superseded,
				OpportunisticServices = _lastPerformanceLod2.OpportunisticServices,
				ForcedServices = _lastPerformanceLod2.ForcedServices,
				MaximumEligibleServiceGapMilliseconds = _lastPerformanceLod2.MaximumServiceGapMilliseconds,
				QueueDepth = CreateQueueDepthMetrics( _lastPerformanceLod2.Queue ),
				ScheduleToRenderable = new PerformanceLatencyMetrics
				{
					Samples = _lastPerformanceLod2.ScheduleToRenderable.Samples,
					TruncatedSamples = _lastPerformanceLod2.ScheduleToRenderable.TruncatedSamples,
					P50Milliseconds = _lastPerformanceLod2.ScheduleToRenderable.P50Milliseconds,
					P95Milliseconds = _lastPerformanceLod2.ScheduleToRenderable.P95Milliseconds,
					P99Milliseconds = _lastPerformanceLod2.ScheduleToRenderable.P99Milliseconds,
					MaximumMilliseconds = _lastPerformanceLod2.ScheduleToRenderable.MaximumMilliseconds,
					Cancelled = _lastPerformanceLod2.ScheduleToRenderable.Cancelled,
					Superseded = _lastPerformanceLod2.ScheduleToRenderable.Superseded
				}
			},
			Clipbox = new PerformanceClipboxMetrics
			{
				Lod0GameplayRadius = AuthoritativeGameplayRadius,
				Lod0GameplayCoordinates = _desiredChunks.Count,
				Lod0ActiveCoordinates = _lod0RenderActive.Count,
				Lod1Anchor = ToPerformanceVector( _lod1Clipbox.Anchor ),
				Lod1OuterMinimum = ToPerformanceVector( _lod1Clipbox.OuterMinimum ),
				Lod1OuterMaximum = ToPerformanceVector( _lod1Clipbox.OuterMaximum ),
				Lod1HoleMinimum = ToPerformanceVector( _lod1Clipbox.HoleMinimum ),
				Lod1HoleMaximum = ToPerformanceVector( _lod1Clipbox.HoleMaximum ),
				Lod1CachedCoordinates = _lod1Clipbox.DesiredCache.Count,
				Lod1ActiveCoordinates = _lod1Clipbox.Active.Count,
				Lod1Pending = _gpuMesher?.PendingLod1Count ?? 0,
				Lod1Resident = _gpuMesher?.Lod1ResidentCount ?? 0,
				FullUpdates = _lod1Clipbox.FullUpdates,
				IncrementalUpdates = _lod1Clipbox.IncrementalUpdates,
				EnteredRegions = _lod1Clipbox.EnteredRegions,
				LeftRegions = _lod1Clipbox.LeftRegions,
				LastEnteredRegions = _lod1Clipbox.LastEnteredRegions,
				LastLeftRegions = _lod1Clipbox.LastLeftRegions,
				Lod2Anchor = ToPerformanceVector( _lod2Clipbox.Anchor ),
				Lod2OuterMinimum = ToPerformanceVector( _lod2Clipbox.OuterMinimum ),
				Lod2OuterMaximum = ToPerformanceVector( _lod2Clipbox.OuterMaximum ),
				Lod2NearCoverageMinimum = ToPerformanceVector( _lod2Clipbox.NearCoverageMinimum ),
				Lod2NearCoverageMaximum = ToPerformanceVector( _lod2Clipbox.NearCoverageMaximum ),
				Lod2NominalHoleHalfExtent = PlacementInputs.Lod2NominalHoleHalfExtent,
				Lod2CachedCoordinates = _lod2Clipbox.DesiredCache.Count,
				Lod2ActiveCoordinates = _lod2Clipbox.Active.Count,
				Lod2Pending = _gpuMesher?.PendingLod2Count ?? 0,
				Lod2Resident = _gpuMesher?.Lod2ResidentCount ?? 0,
				Lod2FullUpdates = _lod2Clipbox.FullUpdates,
				Lod2IncrementalUpdates = _lod2Clipbox.IncrementalUpdates,
				Lod2EnteredRegions = _lod2Clipbox.EnteredRegions,
				Lod2LeftRegions = _lod2Clipbox.LeftRegions,
				Lod2ActivatedRegions = _lod2Clipbox.ActivatedRegions,
				Lod2DeactivatedRegions = _lod2Clipbox.DeactivatedRegions,
				Lod2LastEnteredRegions = _lod2Clipbox.LastEnteredRegions,
				Lod2LastLeftRegions = _lod2Clipbox.LastLeftRegions,
				Lod2LastActivatedRegions = _lod2Clipbox.LastActivatedRegions,
				Lod2LastDeactivatedRegions = _lod2Clipbox.LastDeactivatedRegions
			}
		};

		var json = JsonSerializer.Serialize( result, PerformanceJsonOptions );
		var bytes = Encoding.UTF8.GetBytes( json + "\n" );
		global::Sandbox.FileSystem.Data.CreateDirectory( PerformanceResultsDirectory );
		using ( var stream = global::Sandbox.FileSystem.Data.OpenWrite( PerformanceResultsPath, FileMode.Append ) )
		{
			stream.Write( bytes, 0, bytes.Length );
			stream.Flush();
		}

		PerformanceResultsLocation =
			global::Sandbox.FileSystem.Data.GetFullPath( PerformanceResultsPath ) ?? PerformanceResultsPath;
		LastPerformanceRunId = runId;
		return runId;
	}

	[ConCmd( "voxel_chunk_info" )]
	public static void LogChunkInfoCommand( int x, int y, int z )
	{
		if ( TryGetActiveManager( "chunk.inspect", out var manager ) )
		{
			manager.LogChunkData( new Vector3Int( x, y, z ) );
		}
	}

	[ConCmd( "voxel_lod_info" )]
	public static void LogLodInfoCommand()
	{
		if ( TryGetActiveManager( "lod.inspect", out var manager ) )
		{
			manager.LogLodPlacement( "command" );
		}
	}

	[ConCmd( "voxel_mesh_audit" )]
	public static void LogMeshAuditCommand( int regionsPerLevel = 8, string selection = "nearest" )
	{
		if ( TryGetActiveManager( "mesh.audit", out var manager ) )
		{
			manager._gpuMesher?.RequestMeshAudit(
				manager.ActiveStreamingTarget.WorldPosition,
				regionsPerLevel,
				selection );
		}
	}

	private static bool TryGetActiveManager( string operation, out VoxelManager manager )
	{
		manager = null;
		foreach ( var candidate in Game.ActiveScene.GetAllComponents<VoxelManager>() )
		{
			if ( manager is not null )
			{
				Log.Warning( $"[VoxelWorld] {operation}.rejected reason=\"multiple active VoxelManager components\"" );
				manager = null;
				return false;
			}

			manager = candidate;
		}

		if ( manager is not null )
		{
			return true;
		}

		Log.Warning( $"[VoxelWorld] {operation}.rejected reason=\"no active VoxelManager component\"" );
		return false;
	}

	private void UpdatePlayerFigureEight()
	{
		using var profiler = global::Sandbox.Diagnostics.Performance.Scope(
			VoxelPerformanceProfiler.FigureEightMovement );
		if ( !_playerFigureEightEnabled )
		{
			return;
		}

		if ( !_playerFigureEightTarget.IsValid() )
		{
			_playerFigureEightEnabled = false;
			_playerFigureEightTarget = null;
			_playerFigureEightBody = null;
			if ( _playerFigureEightTestRunning )
			{
				_playerFigureEightTestRunning = false;
				_playerFigureEightTestCompletionReady = false;
				Log.Error( "[VoxelWorld] performance.test.failed reason=\"player target became invalid\"" );
			}
			return;
		}

		var speed = _playerFigureEightTestRunning ? _playerFigureEightTestSpeed : FigureEightSpeed;
		var distance = _playerFigureEightTestRunning ? _playerFigureEightTestDistance : FigureEightDistance;
		var tangentX = MathF.Cos( _playerFigureEightParameter );
		var tangentY = MathF.Cos( 2f * _playerFigureEightParameter );
		var tangentLength = MathF.Sqrt( tangentX * tangentX + tangentY * tangentY );
		_playerFigureEightParameter +=
			speed * RealTime.Delta / (distance * tangentLength);

		while ( _playerFigureEightParameter >= MathF.Tau )
		{
			_playerFigureEightParameter -= MathF.Tau;
			if ( _playerFigureEightTestRunning )
			{
				_playerFigureEightCompletedLoops++;
				FramePerformance =
					$"Figure-eight test running: {_playerFigureEightCompletedLoops} of {_playerFigureEightTargetLoops} loops";
				if ( _playerFigureEightCompletedLoops >= _playerFigureEightTargetLoops )
				{
					_playerFigureEightParameter = 0f;
					_playerFigureEightEnabled = false;
					_playerFigureEightTestCompletionReady = true;
					break;
				}
			}
		}

		var sine = MathF.Sin( _playerFigureEightParameter );
		var cosine = MathF.Cos( _playerFigureEightParameter );
		var nextPosition = new Vector2(
			_playerFigureEightCenter.x + distance * sine,
			_playerFigureEightCenter.y + distance * sine * cosine );
		var routeDelta = nextPosition - _playerFigureEightPreviousPosition;
		_playerFigureEightRouteDistance += MathF.Sqrt(
			routeDelta.x * routeDelta.x + routeDelta.y * routeDelta.y );
		_playerFigureEightPreviousPosition = nextPosition;
		_gpuMesher?.SetPlayerRouteDistance( _playerFigureEightRouteDistance );
		SetFigureEightPosition( nextPosition.x, nextPosition.y );
	}

	private void SetFigureEightPosition( float x, float y )
	{
		_playerFigureEightTarget.WorldPosition = new Vector3( x, y, 0f );
		if ( !_playerFigureEightBody.IsValid() )
		{
			return;
		}

		var velocity = _playerFigureEightBody.Velocity;
		_playerFigureEightBody.Velocity = new Vector3( velocity.x, velocity.y, 0f );
	}

	private void TryCompletePlayerFigureEightTest()
	{
		if ( !_playerFigureEightTestCompletionReady )
		{
			return;
		}

		_playerFigureEightTestCompletionReady = false;
		CompletePerformanceWindow();
		_gpuMesher?.EndMovingThroughputWindow( _lastPerformanceWindowSeconds );
		_playerFigureEightTestRunning = false;
		_playerFigureEightTarget = null;
		_playerFigureEightBody = null;
		_gpuMesher?.StopVisibilityMeasurement();
		_performanceVisibilityPending = true;
		_performanceCompletionPhase = PerformanceCompletionPhase.AwaitingMovingSettle;
		_performanceSettledCaptureRequested = false;
		if ( !_performanceSnapshotReady )
		{
			_performanceVisibilityPending = false;
			Log.Error( "[VoxelWorld] performance.test.failed reason=\"no complete performance snapshot\"" );
			return;
		}

		FramePerformance += "; awaiting GPU visibility counters";
	}

	private void TrySaveCompletedPerformanceTest()
	{
		if ( !_performanceVisibilityPending ||
			_gpuMesher is null )
		{
			return;
		}

		if ( _performanceCompletionPhase == PerformanceCompletionPhase.AwaitingMovingSettle )
		{
			if ( _pendingWarmChunks.Count > 0 ||
				_completedWarmChunks.Count > 0 ||
				!_warmWorkerCompleted ||
				_gpuMesher.PendingCount > 0 )
			{
				return;
			}
			_gpuMesher.MarkThroughputSettled();

			if ( !_performanceSettledCaptureRequested )
			{
				_performanceSettledCaptureRequested = true;
				_gpuMesher.CaptureSettledVisibilityMeasurement();
				return;
			}

			if ( !_gpuMesher.TryTakeVisibilityMeasurement( out _lastPerformanceVisibility ) ) return;
			_performanceStationaryRenderSequenceTarget = _gpuMesher.RenderSequence + 2;
			_performanceCompletionPhase = PerformanceCompletionPhase.AwaitingStationaryRenderAdvance;
			FramePerformance += "; terrain settled; awaiting stationary render boundary";
			return;
		}

		if ( _performanceCompletionPhase == PerformanceCompletionPhase.AwaitingStationaryRenderAdvance )
		{
			if ( _gpuMesher.RenderSequence < _performanceStationaryRenderSequenceTarget ) return;
			ResetPerformanceWindow();
			SamplePerformanceMemory();
			_gpuMesher.BeginVisibilityMeasurement();
			_performanceCompletionPhase = PerformanceCompletionPhase.MeasuringStationary;
			FramePerformance += $"; measuring {PerformanceWindowSeconds:0}-second stationary window";
			return;
		}

		if ( _performanceCompletionPhase == PerformanceCompletionPhase.MeasuringStationary )
		{
			if ( _performanceWindowElapsedSeconds < PerformanceWindowSeconds ) return;
			CaptureStationaryPerformanceWindow();
			_gpuMesher.StopVisibilityMeasurement();
			_gpuMesher.CaptureSettledVisibilityMeasurement();
			_performanceCompletionPhase = PerformanceCompletionPhase.AwaitingStationaryVisibility;
			return;
		}

		if ( _performanceCompletionPhase != PerformanceCompletionPhase.AwaitingStationaryVisibility ||
			!_gpuMesher.TryTakeVisibilityMeasurement( out _lastStationaryVisibility ) ) return;

		_lastStationaryMetrics = new PerformanceStationaryMetrics
		{
			DurationSeconds = _lastStationaryMetrics.DurationSeconds,
			Frame = _lastStationaryMetrics.Frame,
			Memory = _lastStationaryMetrics.Memory,
			Visibility = CreateVisibilityMetrics( _lastStationaryVisibility )
		};
		_performanceVisibilityPending = false;
		_performanceCompletionPhase = PerformanceCompletionPhase.None;
		_lastPerformanceScheduleLatency = _gpuMesher.CompleteScheduleLatencyMeasurement();
		_lastPerformanceThroughput = _gpuMesher.CompleteThroughputMeasurement();
		_lastPerformanceTransitions = _gpuMesher.CompleteTransitionMeasurement();
		_lastPerformanceLod2 = _gpuMesher.CompleteLod2Measurement();

		try
		{
			var runId = SavePerformanceResult();
			FramePerformance += $"; saved run {runId}";
			Log.Info(
				$"[VoxelWorld] performance.result.saved runId=\"{runId}\" " +
				$"task=\"{EscapeLogValue( _playerFigureEightTestTask )}\" " +
				$"revision=\"{EscapeLogValue( _playerFigureEightTestRevision )}\" " +
				$"path=\"{EscapeLogValue( PerformanceResultsLocation )}\"" );
		}
		catch ( Exception exception )
		{
			FramePerformance = $"Performance test completed but save failed: {exception.Message}";
			Log.Error( $"[VoxelWorld] performance.result.failed reason=\"{EscapeLogValue( exception.Message )}\"" );
		}
	}

	private void UpdatePerformanceOverview()
	{
		using var profiler = global::Sandbox.Diagnostics.Performance.Scope(
			VoxelPerformanceProfiler.PerformanceSampling );
		var frameMilliseconds = (float)(global::Sandbox.Diagnostics.PerformanceStats.FrameTime * 1000d);
		if ( float.IsFinite( frameMilliseconds ) && frameMilliseconds > 0f )
		{
			_performanceObservedFrameCount++;
			_performanceFrameMillisecondsTotal += frameMilliseconds;
			if ( _performanceFrameSampleCount < _performanceFrameMilliseconds.Length )
			{
				_performanceFrameMilliseconds[_performanceFrameSampleCount++] = frameMilliseconds;
			}
			else
			{
				_performanceTruncatedFrameSampleCount++;
			}
		}

		var gpuFrameMilliseconds = global::Sandbox.Diagnostics.PerformanceStats.GpuFrametime;
		if ( float.IsFinite( gpuFrameMilliseconds ) && gpuFrameMilliseconds >= 0f )
		{
			_performanceGpuFrameMillisecondsTotal += gpuFrameMilliseconds;
			_performanceGpuFrameSampleCount++;
			if ( _performanceGpuFrameSampleCount <= _performanceGpuMilliseconds.Length )
			{
				_performanceGpuMilliseconds[_performanceGpuFrameSampleCount - 1] = gpuFrameMilliseconds;
			}
		}
		if ( _playerFigureEightTestRunning )
		{
			_gpuMesher?.SampleThroughputQueueDepth();
			_performanceStreaming.PeakGameplayMeshBacklog = Math.Max(
				_performanceStreaming.PeakGameplayMeshBacklog,
				_gpuMesher?.PendingGameplayCount ?? 0 );
			_performanceStreaming.PeakWarmMeshBacklog = Math.Max(
				_performanceStreaming.PeakWarmMeshBacklog,
				_gpuMesher?.PendingWarmCount ?? 0 );
			var terrainSubmissions = _gpuMesher?.TerrainIndirectApiSubmissionCount ?? 0;
			var indirectRecords = _gpuMesher?.IndirectArgumentRecordCount ?? 0;
			var terrainBufferGroups = _gpuMesher?.TerrainBufferGroupCount ?? 0;
			_performanceTerrainSubmissionTotal += terrainSubmissions;
			_performanceTerrainSubmissionMaximum = Math.Max(
				_performanceTerrainSubmissionMaximum,
				terrainSubmissions );
			_performanceIndirectRecordTotal += indirectRecords;
			_performanceIndirectRecordMaximum = Math.Max(
				_performanceIndirectRecordMaximum,
				indirectRecords );
			_performanceTerrainBufferGroupTotal += terrainBufferGroups;
			_performanceTerrainBufferGroupMaximum = Math.Max(
				_performanceTerrainBufferGroupMaximum,
				terrainBufferGroups );
			_performanceSubmissionSampleCount++;
		}

		var deltaSeconds = RealTime.Delta;
		_performanceWindowElapsedSeconds += deltaSeconds;
		_memorySampleElapsedSeconds += deltaSeconds;
		if ( _memorySampleElapsedSeconds >= MemorySampleIntervalSeconds )
		{
			_memorySampleElapsedSeconds = 0f;
			SamplePerformanceMemory();
		}

		if ( !_playerFigureEightTestRunning &&
			!_performanceVisibilityPending &&
			_performanceWindowElapsedSeconds >= PerformanceWindowSeconds )
		{
			CompletePerformanceWindow();
		}
	}

	private void CompletePerformanceWindow()
	{
		if ( _performanceFrameSampleCount == 0 || _performanceMemorySampleCount == 0 )
		{
			Log.Error(
				$"[VoxelWorld] performance.snapshot.incomplete " +
				$"frameSamples={_performanceFrameSampleCount} " +
				$"observedFrames={_performanceObservedFrameCount} " +
				$"memorySamples={_performanceMemorySampleCount} " +
				$"elapsedSeconds={_performanceWindowElapsedSeconds:0.######} " +
				$"memoryElapsedSeconds={_memorySampleElapsedSeconds:0.######}" );
			ResetPerformanceWindow();
			return;
		}

		Array.Copy(
			_performanceFrameMilliseconds,
			_sortedPerformanceFrameMilliseconds,
			_performanceFrameSampleCount );
		Array.Sort( _sortedPerformanceFrameMilliseconds, 0, _performanceFrameSampleCount );
		var p95Index = Math.Clamp(
			(int)Math.Ceiling( _performanceFrameSampleCount * 0.95d ) - 1,
			0,
			_performanceFrameSampleCount - 1 );
		var p99Index = Math.Clamp(
			(int)Math.Ceiling( _performanceFrameSampleCount * 0.99d ) - 1,
			0,
			_performanceFrameSampleCount - 1 );

		_lastPerformanceWindowSeconds = _performanceWindowElapsedSeconds;
		_lastPerformanceFrameSampleCount = _performanceObservedFrameCount;
		_lastPerformanceTruncatedFrameSampleCount = _performanceTruncatedFrameSampleCount;
		_lastAverageFramesPerSecond = (float)(
			_performanceObservedFrameCount * 1000d / _performanceFrameMillisecondsTotal );
		_lastP95FrameMilliseconds = _sortedPerformanceFrameMilliseconds[p95Index];
		_lastP99FrameMilliseconds = _sortedPerformanceFrameMilliseconds[p99Index];
		_lastAverageGpuFrameMilliseconds = _performanceGpuFrameSampleCount > 0
			? (float)(_performanceGpuFrameMillisecondsTotal / _performanceGpuFrameSampleCount)
			: 0f;
		CaptureGpuPercentiles(
			out _lastP95GpuFrameMilliseconds,
			out _lastP99GpuFrameMilliseconds,
			out _lastMaximumGpuFrameMilliseconds );
		_lastAverageProcessMemoryBytes = (ulong)(
			_performanceProcessMemoryBytesTotal / _performanceMemorySampleCount );
		_lastPeakProcessMemoryBytes = _performancePeakProcessMemoryBytes;
		_lastStartProcessMemoryBytes = _performanceStartProcessMemoryBytes;
		_lastEndProcessMemoryBytes = global::Sandbox.Diagnostics.PerformanceStats.ApproximateProcessMemoryUsage;
		_lastAverageGpuMemoryBytes = (ulong)(
			_performanceGpuMemoryBytesTotal / _performanceMemorySampleCount );
		_lastPeakGpuMemoryBytes = _performancePeakGpuMemoryBytes;
		_lastStartGpuMemoryBytes = _performanceStartGpuMemoryBytes;
		_lastEndGpuMemoryBytes = global::Sandbox.Graphics.VideoMemoryUsed;
		_lastGpuMemoryBudgetBytes = _performanceGpuMemoryBudgetBytes;
		_lastPerformanceChunksIntegrated = _performanceChunksIntegrated;
		_lastPerformanceChunksPerSecond =
			_performanceChunksIntegrated / _performanceWindowElapsedSeconds;
		_lastPerformanceMeshDispatches = (_gpuMesher?.DispatchCount ?? 0) - _performanceMesherDispatchStart;
		_lastPerformanceMeshPoolAllocations =
			(_gpuMesher?.PoolAllocationCount ?? 0) - _performanceMesherPoolAllocationStart;
		_lastPerformanceMeshPoolReuses =
			(_gpuMesher?.PoolReuseCount ?? 0) - _performanceMesherPoolReuseStart;
		_lastPerformanceMeshScalarReadbacks =
			(_gpuMesher?.ScalarReadbackCount ?? 0) - _performanceMesherScalarReadbackStart;
		_lastPerformanceWasFigureEightTest = _playerFigureEightTestRunning;
		_lastPerformancePeakMeshDispatchesPerUpdate = _performancePeakMeshDispatchesPerUpdate;
		_lastPerformanceSubmission = new PerformanceSubmissionMetrics
		{
			AverageTerrainIndirectApiSubmissionsPerFrame = _performanceSubmissionSampleCount > 0
				? (float)_performanceTerrainSubmissionTotal / _performanceSubmissionSampleCount
				: 0f,
			MaximumTerrainIndirectApiSubmissionsPerFrame = _performanceTerrainSubmissionMaximum,
			AverageIndirectArgumentRecordsPerFrame = _performanceSubmissionSampleCount > 0
				? (float)_performanceIndirectRecordTotal / _performanceSubmissionSampleCount
				: 0f,
			MaximumIndirectArgumentRecordsPerFrame = _performanceIndirectRecordMaximum,
			AverageTerrainBufferGroups = _performanceSubmissionSampleCount > 0
				? (float)_performanceTerrainBufferGroupTotal / _performanceSubmissionSampleCount
				: 0f,
			MaximumTerrainBufferGroups = _performanceTerrainBufferGroupMaximum
		};
		_lastPerformanceCompletedLoops = _playerFigureEightTestRunning ? _playerFigureEightCompletedLoops : 0;
		_lastPerformanceTestSpeed = _playerFigureEightTestRunning ? _playerFigureEightTestSpeed : 0f;
		_lastPerformanceTestDistance = _playerFigureEightTestRunning ? _playerFigureEightTestDistance : 0f;
		_lastPerformanceStreaming = _performanceStreaming;
		_lastPerformanceBounds = _performanceBounds;
		_lastPerformanceProfiler = _playerFigureEightTestRunning
			? VoxelPerformanceProfiler.Capture()
			: new PerformanceProfilerMetrics();
		_performanceSnapshotReady = true;

		FramePerformance =
			$"{_lastAverageFramesPerSecond:N1} FPS average; " +
			$"p95 {_lastP95FrameMilliseconds:N2} ms; p99 {_lastP99FrameMilliseconds:N2} ms; " +
			$"GPU {_lastAverageGpuFrameMilliseconds:N2} ms average";
		ProcessMemoryUsage =
			$"CPU {_lastAverageProcessMemoryBytes / (1024f * 1024f):N1} MiB average, " +
			$"{_lastPeakProcessMemoryBytes / (1024f * 1024f):N1} MiB peak; " +
			$"GPU {_lastAverageGpuMemoryBytes / (1024f * 1024f):N1} MiB average, " +
			$"{_lastPeakGpuMemoryBytes / (1024f * 1024f):N1} MiB peak";
		RefreshReadableStatus();
		ResetPerformanceWindow();
	}

	private void CaptureStationaryPerformanceWindow()
	{
		if ( _performanceFrameSampleCount == 0 || _performanceMemorySampleCount == 0 )
		{
			throw new InvalidOperationException( "The settled stationary performance window contained no samples." );
		}

		Array.Copy( _performanceFrameMilliseconds, _sortedPerformanceFrameMilliseconds, _performanceFrameSampleCount );
		Array.Sort( _sortedPerformanceFrameMilliseconds, 0, _performanceFrameSampleCount );
		var p95Index = Math.Clamp(
			(int)Math.Ceiling( _performanceFrameSampleCount * 0.95d ) - 1,
			0,
			_performanceFrameSampleCount - 1 );
		var p99Index = Math.Clamp(
			(int)Math.Ceiling( _performanceFrameSampleCount * 0.99d ) - 1,
			0,
			_performanceFrameSampleCount - 1 );
		CaptureGpuPercentiles( out var gpuP95, out var gpuP99, out var gpuMaximum );
		_lastStationaryMetrics = new PerformanceStationaryMetrics
		{
			DurationSeconds = _performanceWindowElapsedSeconds,
			Frame = new PerformanceFrameMetrics
			{
				Samples = _performanceObservedFrameCount,
				TruncatedSamples = _performanceTruncatedFrameSampleCount,
				AverageFps = (float)(_performanceObservedFrameCount * 1000d / _performanceFrameMillisecondsTotal),
				P95Milliseconds = _sortedPerformanceFrameMilliseconds[p95Index],
				P99Milliseconds = _sortedPerformanceFrameMilliseconds[p99Index],
				AverageGpuMilliseconds = _performanceGpuFrameSampleCount > 0
					? (float)(_performanceGpuFrameMillisecondsTotal / _performanceGpuFrameSampleCount)
					: 0f,
				P95GpuMilliseconds = gpuP95,
				P99GpuMilliseconds = gpuP99,
				MaximumGpuMilliseconds = gpuMaximum
			},
			Memory = new PerformanceMemoryMetrics
			{
				StartProcessBytes = _performanceStartProcessMemoryBytes,
				EndProcessBytes = global::Sandbox.Diagnostics.PerformanceStats.ApproximateProcessMemoryUsage,
				AverageProcessBytes = (ulong)(_performanceProcessMemoryBytesTotal / _performanceMemorySampleCount),
				PeakProcessBytes = _performancePeakProcessMemoryBytes,
				StartGpuBytes = _performanceStartGpuMemoryBytes,
				EndGpuBytes = global::Sandbox.Graphics.VideoMemoryUsed,
				AverageGpuBytes = (ulong)(_performanceGpuMemoryBytesTotal / _performanceMemorySampleCount),
				PeakGpuBytes = _performancePeakGpuMemoryBytes,
				GpuBudgetBytes = _performanceGpuMemoryBudgetBytes
			}
		};
	}

	private void CaptureGpuPercentiles( out float p95, out float p99, out float maximum )
	{
		var count = Math.Min( _performanceGpuFrameSampleCount, _performanceGpuMilliseconds.Length );
		if ( count == 0 )
		{
			p95 = 0f;
			p99 = 0f;
			maximum = 0f;
			return;
		}
		Array.Copy( _performanceGpuMilliseconds, _sortedPerformanceGpuMilliseconds, count );
		Array.Sort( _sortedPerformanceGpuMilliseconds, 0, count );
		p95 = _sortedPerformanceGpuMilliseconds[Math.Clamp( (int)Math.Ceiling( count * 0.95d ) - 1, 0, count - 1 )];
		p99 = _sortedPerformanceGpuMilliseconds[Math.Clamp( (int)Math.Ceiling( count * 0.99d ) - 1, 0, count - 1 )];
		maximum = _sortedPerformanceGpuMilliseconds[count - 1];
	}

	private static PerformanceVisibilityMetrics CreateVisibilityMetrics( GpuVisibilityMeasurement visibility ) => new()
	{
		Samples = visibility.FrameCount,
		AverageResidentMeshChunks = visibility.AverageResident,
		AverageVisibleMeshChunks = visibility.AverageVisible,
		AverageWarmMeshChunks = visibility.AverageWarm,
		AverageLod0ResidentMeshChunks = visibility.AverageLod0Resident,
		AverageLod1ResidentMeshChunks = visibility.AverageLod1Resident,
		AverageLod2ResidentMeshChunks = visibility.AverageLod2Resident,
		AverageLod0VisibleMeshChunks = visibility.AverageLod0Visible,
		AverageLod1VisibleMeshChunks = visibility.AverageLod1Visible,
		AverageLod2VisibleMeshChunks = visibility.AverageLod2Visible,
		SettledLod0SurfaceMeshes = visibility.SettledLod0SurfaceMeshes,
		SettledLod1SurfaceMeshes = visibility.SettledLod1SurfaceMeshes,
		SettledLod2SurfaceMeshes = visibility.SettledLod2SurfaceMeshes,
		MinimumVisibleMeshChunks = visibility.MinimumVisible,
		MaximumVisibleMeshChunks = visibility.MaximumVisible,
		AverageNonZeroIndirectDraws = visibility.AverageVisible,
		AverageCulledDraws = visibility.AverageCulled,
		CulledDrawPercentage = visibility.CulledPercent,
		LogicalBufferBytes = visibility.LogicalBufferBytes,
		ScalarReadbacks = visibility.ScalarReadbacks
	};

	private static PerformanceVector3Int ToPerformanceVector( Vector3Int value ) => new()
	{
		X = value.x,
		Y = value.y,
		Z = value.z
	};

	private static PerformanceMeshingThroughputMetrics CreateThroughputMetrics(
		GpuMeshThroughputMeasurement throughput ) => new()
	{
		ScratchLanes = throughput.ScratchLanes,
		RegionsScheduled = throughput.RegionsScheduled,
		RegionsCountSubmitted = throughput.RegionsCountSubmitted,
		RegionsPublished = throughput.RegionsPublished,
		RegionsScheduledPerSecond = throughput.RegionsScheduledPerSecond,
		RegionsCountSubmittedPerSecond = throughput.RegionsCountSubmittedPerSecond,
		RegionsPublishedPerSecond = throughput.RegionsPublishedPerSecond,
		BatchesSubmitted = throughput.BatchesSubmitted,
		BatchesCompleted = throughput.BatchesCompleted,
		BatchesSubmittedPerSecond = throughput.BatchesSubmittedPerSecond,
		BatchesCompletedPerSecond = throughput.BatchesCompletedPerSecond,
		AverageBatchOccupancy = throughput.AverageBatchOccupancy,
		MinimumBatchOccupancy = throughput.MinimumBatchOccupancy,
		MaximumBatchOccupancy = throughput.MaximumBatchOccupancy,
		BatchOccupancyHistogram = throughput.BatchOccupancyHistogram ?? Array.Empty<int>(),
		CountSubmissionMilliseconds = CreateDistributionMetrics( throughput.CountSubmissionMilliseconds ),
		CountReadbackMilliseconds = CreateDistributionMetrics( throughput.CountReadbackMilliseconds ),
		CountCallbackWaitMilliseconds = CreateDistributionMetrics( throughput.CountCallbackWaitMilliseconds ),
		CpuAllocationMilliseconds = CreateDistributionMetrics( throughput.CpuAllocationMilliseconds ),
		EmitSubmissionMilliseconds = CreateDistributionMetrics( throughput.EmitSubmissionMilliseconds ),
		EmitToPublicationMilliseconds = CreateDistributionMetrics( throughput.EmitToPublicationMilliseconds ),
		GameplayQueue = CreateQueueDepthMetrics( throughput.GameplayQueue ),
		WarmQueue = CreateQueueDepthMetrics( throughput.WarmQueue ),
		TotalQueue = CreateQueueDepthMetrics( throughput.TotalQueue ),
		PlayerRouteLagWorldUnits = CreateDistributionMetrics( throughput.PlayerRouteLagWorldUnits ),
		PlayerRouteLagChunks = CreateDistributionMetrics( throughput.PlayerRouteLagChunks ),
		PostLoopDrainMilliseconds = throughput.PostLoopDrainMilliseconds
	};

	private static PerformanceDistributionMetrics CreateDistributionMetrics( GpuMetricDistribution distribution ) => new()
	{
		Samples = distribution.Samples,
		TruncatedSamples = distribution.TruncatedSamples,
		Average = distribution.Average,
		P50 = distribution.P50,
		P95 = distribution.P95,
		P99 = distribution.P99,
		Maximum = distribution.Maximum
	};

	private static PerformanceQueueDepthMetrics CreateQueueDepthMetrics( GpuQueueDepthMeasurement queue ) => new()
	{
		Samples = queue.Samples,
		TruncatedSamples = queue.TruncatedSamples,
		Average = queue.Average,
		P50 = queue.P50,
		P95 = queue.P95,
		P99 = queue.P99,
		Maximum = queue.Maximum
	};

	private void SamplePerformanceMemory()
	{
		var processMemoryBytes = global::Sandbox.Diagnostics.PerformanceStats.ApproximateProcessMemoryUsage;
		var gpuMemoryBytes = global::Sandbox.Graphics.VideoMemoryUsed;
		_performanceProcessMemoryBytesTotal += processMemoryBytes;
		_performancePeakProcessMemoryBytes = Math.Max( _performancePeakProcessMemoryBytes, processMemoryBytes );
		_performanceGpuMemoryBytesTotal += gpuMemoryBytes;
		_performancePeakGpuMemoryBytes = Math.Max( _performancePeakGpuMemoryBytes, gpuMemoryBytes );
		_performanceGpuMemoryBudgetBytes = global::Sandbox.Graphics.VideoMemoryBudget;
		_performanceMemorySampleCount++;
	}

	private void ResetPerformanceWindow()
	{
		_performanceWindowElapsedSeconds = 0f;
		_memorySampleElapsedSeconds = 0f;
		_performanceObservedFrameCount = 0;
		_performanceFrameSampleCount = 0;
		_performanceTruncatedFrameSampleCount = 0;
		_performanceFrameMillisecondsTotal = 0d;
		_performanceGpuFrameMillisecondsTotal = 0d;
		_performanceGpuFrameSampleCount = 0;
		_performanceProcessMemoryBytesTotal = 0d;
		_performancePeakProcessMemoryBytes = 0;
		_performanceGpuMemoryBytesTotal = 0d;
		_performancePeakGpuMemoryBytes = 0;
		_performanceGpuMemoryBudgetBytes = 0;
		_performanceStartProcessMemoryBytes = global::Sandbox.Diagnostics.PerformanceStats.ApproximateProcessMemoryUsage;
		_performanceStartGpuMemoryBytes = global::Sandbox.Graphics.VideoMemoryUsed;
		_performanceMemorySampleCount = 0;
		_performanceChunksIntegrated = 0;
		_performanceMesherDispatchStart = _gpuMesher?.DispatchCount ?? 0;
		_performanceMesherPoolAllocationStart = _gpuMesher?.PoolAllocationCount ?? 0;
		_performanceMesherPoolReuseStart = _gpuMesher?.PoolReuseCount ?? 0;
		_performanceMesherScalarReadbackStart = _gpuMesher?.ScalarReadbackCount ?? 0;
		_performancePeakMeshDispatchesPerUpdate = 0;
		_performanceTerrainSubmissionTotal = 0;
		_performanceTerrainSubmissionMaximum = 0;
		_performanceIndirectRecordTotal = 0;
		_performanceIndirectRecordMaximum = 0;
		_performanceTerrainBufferGroupTotal = 0;
		_performanceTerrainBufferGroupMaximum = 0;
		_performanceSubmissionSampleCount = 0;
		_performanceStreaming = new PerformanceStreamingMetrics();
		_performanceBounds = new PerformanceBoundsMetrics();
	}

	private void RecordBoundsQuery(
		ChunkDensityClassification classification,
		float milliseconds,
		bool warm )
	{
		if ( warm )
		{
			_performanceBounds.WarmQueries++;
		}
		else
		{
			_performanceBounds.GameplayQueries++;
		}

		switch ( classification )
		{
			case ChunkDensityClassification.DefinitelySolid:
				if ( warm )
				{
					_performanceBounds.WarmDefinitelySolid++;
				}
				else
				{
					_performanceBounds.GameplayDefinitelySolid++;
				}
				break;
			case ChunkDensityClassification.DefinitelyAir:
				if ( warm )
				{
					_performanceBounds.WarmDefinitelyAir++;
				}
				else
				{
					_performanceBounds.GameplayDefinitelyAir++;
				}
				break;
			default:
				if ( warm )
				{
					_performanceBounds.WarmPotentiallySurfaceContaining++;
				}
				else
				{
					_performanceBounds.GameplayPotentiallySurfaceContaining++;
				}
				break;
		}

		_performanceBounds.TotalCpuMilliseconds += milliseconds;
		_performanceBounds.MaximumQueryMilliseconds = Math.Max(
			_performanceBounds.MaximumQueryMilliseconds,
			milliseconds );
	}

	private static string EscapeLogValue( string value )
	{
		return (value ?? string.Empty).Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" );
	}

	private static string RequirePerformanceContext( string value, string parameterName )
	{
		var normalized = value?.Trim();
		if ( string.IsNullOrWhiteSpace( normalized ) ||
			normalized.Equals( "unassigned", StringComparison.OrdinalIgnoreCase ) )
		{
			throw new ArgumentException(
				$"{parameterName} is required and cannot be 'unassigned'.",
				parameterName );
		}

		return normalized;
	}

	private void LogChunkData( Vector3Int coordinate )
	{
		if ( !_loadedChunks.TryGetValue( coordinate, out var chunk ) )
		{
			Log.Warning(
				$"[VoxelWorld] chunk.missing chunk=C[{coordinate.x},{coordinate.y},{coordinate.z}] " +
				$"loaded={_loadedChunks.Count}" );
			return;
		}

		chunk.TryGetSample( Vector3Int.Zero, out var originDensity, out var originMaterialId );
		chunk.TryGetSample(
			new Vector3Int( chunk.CellsPerAxis, 0, 0 ),
			out var positiveXDensity,
			out _ );
		chunk.TryGetSample(
			new Vector3Int( 0, chunk.CellsPerAxis, 0 ),
			out var positiveYDensity,
			out _ );
		chunk.TryGetSample(
			new Vector3Int( 0, 0, chunk.CellsPerAxis ),
			out var positiveZDensity,
			out _ );
		Log.Info(
			$"[VoxelWorld] chunk.inspect chunk={chunk.LogId} name=\"{chunk.HumanName}\" cellsPerAxis={chunk.CellsPerAxis} " +
			$"samplesPerAxis={chunk.SamplesPerAxis} sampleCount={chunk.SampleCount} " +
			$"worldSeed={WorldSeed} generatorVersion={ProceduralTerrainSdf.CurrentVersion} " +
			$"surfaceBaseHeight={SurfaceBaseHeight} surfaceFrequency={SurfaceFrequency} " +
			$"surfaceAmplitude={SurfaceAmplitude} " +
			$"densityMin={chunk.MinimumDensity} densityMax={chunk.MaximumDensity} " +
			$"originDensity={originDensity} originMaterial=\"{VoxelChunk.GetMaterialName( originMaterialId )}\" " +
			$"originMaterialId={originMaterialId} positiveXFaceDensity={positiveXDensity} " +
			$"positiveYFaceDensity={positiveYDensity} positiveZFaceDensity={positiveZDensity}" );
	}

	private void LogLodPlacement( string reason )
	{
		var targetPosition = ActiveStreamingTarget.WorldPosition;
		Log.Info(
			$"[VoxelWorld] lod.inspect reason=\"{reason}\" target={FormatWorldPosition( targetPosition )} " +
			$"cellsPerAxis={CellsPerAxis} maximumVisualLevel={PlacementInputs.MaximumVisualLevel}" );

		if ( !_hasStreamingCenter || !_lod1Clipbox.HasAnchor )
		{
			Log.Info(
				$"[VoxelWorld] lod.inspect.pending gameplayCenterReady={_hasStreamingCenter} " +
				$"lod1AnchorReady={_lod1Clipbox.HasAnchor} lod2AnchorReady={_lod2Clipbox.HasAnchor}" );
			return;
		}

		var gameplayMinimum = _streamingCenterCoordinate - new Vector3Int( AuthoritativeGameplayRadius );
		var gameplayMaximum = _streamingCenterCoordinate + new Vector3Int( AuthoritativeGameplayRadius + 1 );
		var lod0Anchor = _lod1Clipbox.Anchor * 2;
		var lod0Minimum = lod0Anchor - new Vector3Int( PlacementInputs.Lod0VisualHalfExtent );
		var lod0Maximum = lod0Anchor + new Vector3Int( PlacementInputs.Lod0VisualHalfExtent );
		Log.Info(
			$"[VoxelWorld] lod.inspect.level level=Lod0 cellSize={CellSize:0.###} " +
			$"regionSize={CellsPerAxis * CellSize:0.###} gameplayAnchor={FormatRegionCoordinate( _streamingCenterCoordinate )} " +
			$"gameplayRegions={FormatRegionBox( gameplayMinimum, gameplayMaximum )} " +
			$"gameplayWorld={FormatWorldBox( gameplayMinimum, gameplayMaximum, CellSize )} " +
			$"gameplayDesired={_desiredChunks.Count} loaded={_loadedChunks.Count} " +
			$"visualAnchor={FormatRegionCoordinate( lod0Anchor )} " +
			$"visualRegions={FormatRegionBox( lod0Minimum, lod0Maximum )} " +
			$"visualWorld={FormatWorldBox( lod0Minimum, lod0Maximum, CellSize )} " +
			$"visualActive={_lod0RenderActive.Count} residentIncludingWarm={_gpuMesher?.Lod0ResidentCount ?? 0} " +
			$"pendingGameplay={_gpuMesher?.PendingGameplayCount ?? 0} pendingWarm={_gpuMesher?.PendingWarmCount ?? 0}" );

		Log.Info(
			$"[VoxelWorld] lod.inspect.level level=Lod1 cellSize={Lod1CellSize:0.###} " +
			$"regionSize={CellsPerAxis * Lod1CellSize:0.###} anchor={FormatRegionCoordinate( _lod1Clipbox.Anchor )} " +
			$"outerRegions={FormatRegionBox( _lod1Clipbox.OuterMinimum, _lod1Clipbox.OuterMaximum )} " +
			$"outerWorld={FormatWorldBox( _lod1Clipbox.OuterMinimum, _lod1Clipbox.OuterMaximum, Lod1CellSize )} " +
			$"holeRegions={FormatRegionBox( _lod1Clipbox.HoleMinimum, _lod1Clipbox.HoleMaximum )} " +
			$"holeWorld={FormatWorldBox( _lod1Clipbox.HoleMinimum, _lod1Clipbox.HoleMaximum, Lod1CellSize )} " +
			$"cached={_lod1Clipbox.DesiredCache.Count} active={_lod1Clipbox.Active.Count} " +
			$"resident={_gpuMesher?.Lod1ResidentCount ?? 0} pending={_gpuMesher?.PendingLod1Count ?? 0} " +
			$"lastEnter={_lod1Clipbox.LastEnteredRegions} lastLeave={_lod1Clipbox.LastLeftRegions}" );

		Log.Info(
			$"[VoxelWorld] lod.inspect.transition levels=Lod0-Lod1 desired={_transitionDesired.Count} " +
			$"ready={_gpuMesher?.TransitionReadyCount ?? 0} pending={_gpuMesher?.TransitionPendingCount ?? 0} " +
			$"lastEnter={_lastTransitionEntered} lastLeave={_lastTransitionLeft}" );

		if ( !_lod2Clipbox.HasAnchor )
		{
			Log.Info( "[VoxelWorld] lod.inspect.level level=Lod2 state=disabled-or-pending" );
			return;
		}

		var lod2NominalHoleMinimum = _lod2Clipbox.Anchor -
			new Vector3Int( PlacementInputs.Lod2NominalHoleHalfExtent );
		var lod2NominalHoleMaximum = _lod2Clipbox.Anchor +
			new Vector3Int( PlacementInputs.Lod2NominalHoleHalfExtent );
		Log.Info(
			$"[VoxelWorld] lod.inspect.level level=Lod2 cellSize={Lod2CellSize:0.###} " +
			$"regionSize={CellsPerAxis * Lod2CellSize:0.###} anchor={FormatRegionCoordinate( _lod2Clipbox.Anchor )} " +
			$"outerRegions={FormatRegionBox( _lod2Clipbox.OuterMinimum, _lod2Clipbox.OuterMaximum )} " +
			$"outerWorld={FormatWorldBox( _lod2Clipbox.OuterMinimum, _lod2Clipbox.OuterMaximum, Lod2CellSize )} " +
			$"nominalHoleRegions={FormatRegionBox( lod2NominalHoleMinimum, lod2NominalHoleMaximum )} " +
			$"nominalHoleWorld={FormatWorldBox( lod2NominalHoleMinimum, lod2NominalHoleMaximum, Lod2CellSize )} " +
			$"nearCoverageLod1Regions={FormatRegionBox( _lod2Clipbox.NearCoverageMinimum, _lod2Clipbox.NearCoverageMaximum )} " +
			$"nearCoverageWorld={FormatWorldBox( _lod2Clipbox.NearCoverageMinimum, _lod2Clipbox.NearCoverageMaximum, Lod1CellSize )} " +
			$"cached={_lod2Clipbox.DesiredCache.Count} active={_lod2Clipbox.Active.Count} " +
			$"excluded={_lod2Clipbox.DesiredCache.Count - _lod2Clipbox.Active.Count} " +
			$"resident={_gpuMesher?.Lod2ResidentCount ?? 0} pending={_gpuMesher?.PendingLod2Count ?? 0} " +
			$"lastEnter={_lod2Clipbox.LastEnteredRegions} lastLeave={_lod2Clipbox.LastLeftRegions} " +
			$"lastActivate={_lod2Clipbox.LastActivatedRegions} lastDeactivate={_lod2Clipbox.LastDeactivatedRegions}" );
	}

	private static string FormatRegionCoordinate( Vector3Int coordinate )
	{
		return $"C[{coordinate.x},{coordinate.y},{coordinate.z}]";
	}

	private static string FormatRegionBox( Vector3Int minimum, Vector3Int maximum )
	{
		return $"[{FormatRegionCoordinate( minimum )},{FormatRegionCoordinate( maximum )})";
	}

	private string FormatWorldBox( Vector3Int minimum, Vector3Int maximum, float cellSize )
	{
		var regionSize = CellsPerAxis * cellSize;
		return $"[{FormatWorldPosition( new Vector3( minimum.x, minimum.y, minimum.z ) * regionSize )}," +
			$"{FormatWorldPosition( new Vector3( maximum.x, maximum.y, maximum.z ) * regionSize )})";
	}

	private static string FormatWorldPosition( Vector3 position )
	{
		return $"W[{position.x:0.###},{position.y:0.###},{position.z:0.###}]";
	}

	private void ResolveStreamingTarget()
	{
		if ( StreamingTarget is not null )
		{
			_resolvedStreamingTarget = StreamingTarget;
			if ( VerboseLogging )
			{
				Log.Info( $"[VoxelWorld] target.resolve mode=assigned name=\"{StreamingTarget.Name}\"" );
			}
			return;
		}

		GameObject localPlayer = null;
		foreach ( var controller in Scene.GetAllComponents<PlayerController>() )
		{
			if ( controller.IsProxy )
			{
				continue;
			}

			if ( localPlayer is not null && localPlayer != controller.GameObject )
			{
				_resolvedStreamingTarget = GameObject;
				Log.Warning(
					"[VoxelWorld] target.resolve.rejected reason=\"multiple locally controlled PlayerController components\" " +
					$"fallback=\"{GameObject.Name}\"" );
				return;
			}

			localPlayer = controller.GameObject;
		}

		_resolvedStreamingTarget = localPlayer ?? GameObject;
		if ( VerboseLogging )
		{
			Log.Info(
				$"[VoxelWorld] target.resolve mode={(localPlayer is null ? "manager-fallback" : "local-player")} " +
				$"name=\"{_resolvedStreamingTarget.Name}\"" );
		}
	}

	private bool TryValidateConfiguration( out string error )
	{
		if ( CellsPerAxis < 4 || CellsPerAxis > 64 )
		{
			error = "Cells Per Axis must be between 4 and 64.";
			return false;
		}

		if ( !float.IsFinite( CellSize ) || CellSize < 1f || CellSize > 128f )
		{
			error = "Cell Size must be finite and between 1 and 128 world units.";
			return false;
		}

		if ( WorldSeed < -16777216 || WorldSeed > 16777216 )
		{
			error = "World Seed must be between -16,777,216 and 16,777,216 for exact GPU transport.";
			return false;
		}

		if ( !float.IsFinite( SurfaceBaseHeight ) ||
			SurfaceBaseHeight < -4096f ||
			SurfaceBaseHeight > 4096f )
		{
			error = "Surface Base Height must be finite and between -4,096 and 4,096.";
			return false;
		}

		if ( !float.IsFinite( SurfaceFrequency ) || SurfaceFrequency < 0.0001f || SurfaceFrequency > 0.1f )
		{
			error = "Surface Frequency must be finite and between 0.0001 and 0.1.";
			return false;
		}

		if ( !float.IsFinite( SurfaceAmplitude ) || SurfaceAmplitude < 0f || SurfaceAmplitude > 4096f )
		{
			error = "Surface Amplitude must be finite and between 0 and 4,096.";
			return false;
		}

		error = string.Empty;
		return true;
	}

	private bool DataConfigurationChanged()
	{
		return CellsPerAxis != _appliedCellsPerAxis ||
			CellSize != _appliedCellSize ||
			WorldSeed != _appliedWorldSeed ||
			SurfaceBaseHeight != _appliedSurfaceBaseHeight ||
			SurfaceFrequency != _appliedSurfaceFrequency ||
			SurfaceAmplitude != _appliedSurfaceAmplitude;
	}

	private void ApplyConfigurationAndRebuild()
	{
		if ( !TryValidateConfiguration( out var configurationError ) )
		{
			_lastConfigurationError = configurationError;
			Log.Warning( $"[VoxelWorld] configuration.invalid reason=\"{configurationError}\"" );
			return;
		}

		_appliedCellsPerAxis = CellsPerAxis;
		_appliedCellSize = CellSize;
		_appliedWorldSeed = WorldSeed;
		_appliedSurfaceBaseHeight = SurfaceBaseHeight;
		_appliedSurfaceFrequency = SurfaceFrequency;
		_appliedSurfaceAmplitude = SurfaceAmplitude;

		_generationCancellation?.Cancel();
		_warmGenerationCancellation?.Cancel();
		_streamRevision++;
		_warmGenerationRevision++;
		_terrainContentRevision++;
		_gpuMesher.Reset( CellsPerAxis );
		_loadedChunks.Clear();
		_desiredChunks.Clear();
		_renderDesiredChunks.Clear();
		_nextRenderDesiredChunks.Clear();
		_renderPreparedChunks.Clear();
		_lod0RenderActive.Clear();
		_nextLod0RenderActive.Clear();
		_lod1Clipbox.Clear();
		_nextLod1Cache.Clear();
		_nextLod1Active.Clear();
		_lod2Clipbox.Clear();
		_nextLod2Cache.Clear();
		_nextLod2Active.Clear();
		_transitionDesired.Clear();
		_nextTransitionDesired.Clear();
		_pendingChunks.Clear();
		_completedChunks.Clear();
		_pendingWarmChunks.Clear();
		_completedWarmChunks.Clear();
		_hasStreamingCenter = false;
		_streamInProgress = false;

		var targetPosition = ActiveStreamingTarget.WorldPosition;
		RebuildDesiredChunks( WorldToChunkCoordinate( targetPosition ), "configuration applied" );
	}

	private Vector3Int WorldToChunkCoordinate( Vector3 worldPosition )
	{
		var chunkWorldSize = CellsPerAxis * CellSize;
		return new Vector3Int(
			(int)MathF.Floor( worldPosition.x / chunkWorldSize ),
			(int)MathF.Floor( worldPosition.y / chunkWorldSize ),
			(int)MathF.Floor( worldPosition.z / chunkWorldSize ) );
	}

	private static Vector3Int WorldToLod1Anchor( Vector3 worldPosition )
	{
		const float regionSize = 32f * Lod1CellSize;
		const float halfRegion = regionSize * 0.5f;
		return new Vector3Int(
			(int)MathF.Floor( (worldPosition.x + halfRegion) / regionSize ),
			(int)MathF.Floor( (worldPosition.y + halfRegion) / regionSize ),
			(int)MathF.Floor( (worldPosition.z + halfRegion) / regionSize ) );
	}

	private static Vector3Int WorldToLod2Anchor( Vector3 worldPosition )
	{
		const float regionSize = 32f * Lod2CellSize;
		const float halfRegion = regionSize * 0.5f;
		return new Vector3Int(
			(int)MathF.Floor( (worldPosition.x + halfRegion) / regionSize ),
			(int)MathF.Floor( (worldPosition.y + halfRegion) / regionSize ),
			(int)MathF.Floor( (worldPosition.z + halfRegion) / regionSize ) );
	}

	private void RebuildDesiredChunks( Vector3Int center, string reason )
	{
		var synchronousStart = Stopwatch.GetTimestamp();
		var previousCenter = _streamingCenterCoordinate;
		var hadPreviousCenter = _hasStreamingCenter;
		var desiredUpdateStart = Stopwatch.GetTimestamp();
		var delta = hadPreviousCenter ? center - previousCenter : Vector3Int.Zero;
		var renderRadius = AuthoritativeGameplayRadius + RenderWarmShellChunks;
		var incremental = hadPreviousCenter && center != previousCenter &&
			Math.Abs( delta.x ) <= 1 && Math.Abs( delta.y ) <= 1 && Math.Abs( delta.z ) <= 1 &&
			_desiredChunks.Count == GetCubeCoordinateCount( AuthoritativeGameplayRadius ) &&
			_renderDesiredChunks.Count == GetCubeCoordinateCount( renderRadius );

		_gameplayEnteringBuffer.Clear();
		_gameplayLeavingBuffer.Clear();
		_renderEnteringBuffer.Clear();
		_renderLeavingBuffer.Clear();
		if ( incremental )
		{
			SlideDesiredWindow( _desiredChunks, previousCenter, center, AuthoritativeGameplayRadius,
				_gameplayEnteringBuffer, _gameplayLeavingBuffer );
			SlideDesiredWindow( _renderDesiredChunks, previousCenter, center, renderRadius,
				_renderEnteringBuffer, _renderLeavingBuffer );
		}
		else
		{
			_desiredChunks.Clear();
			for ( var z = -AuthoritativeGameplayRadius; z <= AuthoritativeGameplayRadius; z++ )
			{
				for ( var y = -AuthoritativeGameplayRadius; y <= AuthoritativeGameplayRadius; y++ )
				{
					for ( var x = -AuthoritativeGameplayRadius; x <= AuthoritativeGameplayRadius; x++ )
					{
						_desiredChunks.Add( new Vector3Int( center.x + x, center.y + y, center.z + z ) );
					}
				}
			}

			_nextRenderDesiredChunks.Clear();
			for ( var z = -renderRadius; z <= renderRadius; z++ )
			{
				for ( var y = -renderRadius; y <= renderRadius; y++ )
				{
					for ( var x = -renderRadius; x <= renderRadius; x++ )
					{
						_nextRenderDesiredChunks.Add(
							new Vector3Int( center.x + x, center.y + y, center.z + z ) );
					}
				}
			}
		}

		_streamingCenterCoordinate = center;
		_hasStreamingCenter = true;
		var desiredUpdateMilliseconds = (float)Stopwatch.GetElapsedTime( desiredUpdateStart ).TotalMilliseconds;

		_coordinateBuffer.Clear();
		if ( incremental )
		{
			_coordinateBuffer.AddRange( _gameplayLeavingBuffer );
		}
		else
		{
			foreach ( var coordinate in _loadedChunks.Keys )
			{
				if ( !_desiredChunks.Contains( coordinate ) )
				{
					_coordinateBuffer.Add( coordinate );
				}
			}
		}

		var unloadedCount = 0;
		foreach ( var coordinate in _coordinateBuffer )
		{
			if ( (incremental ? _renderDesiredChunks : _nextRenderDesiredChunks).Contains( coordinate ) )
			{
				_gpuMesher.SetResidency( new GpuMeshRegionKey( GpuMeshLevel.Lod0, coordinate ), GpuMeshResidency.Warm );
			}
			else
			{
				_gpuMesher.Remove( new GpuMeshRegionKey( GpuMeshLevel.Lod0, coordinate ) );
				_renderPreparedChunks.Remove( coordinate );
			}
			if ( _loadedChunks.Remove( coordinate ) )
			{
				unloadedCount++;
			}
		}

		_coordinateBuffer.Clear();
		if ( incremental )
		{
			_coordinateBuffer.AddRange( _renderLeavingBuffer );
		}
		else
		{
			foreach ( var coordinate in _renderDesiredChunks )
			{
				if ( !_nextRenderDesiredChunks.Contains( coordinate ) )
				{
					_coordinateBuffer.Add( coordinate );
				}
			}
		}

		foreach ( var coordinate in _coordinateBuffer )
		{
			_gpuMesher.Remove( new GpuMeshRegionKey( GpuMeshLevel.Lod0, coordinate ) );
			_renderPreparedChunks.Remove( coordinate );
		}

		if ( !incremental )
		{
			var previousRenderDesired = _renderDesiredChunks;
			_renderDesiredChunks = _nextRenderDesiredChunks;
			_nextRenderDesiredChunks = previousRenderDesired;
		}
		UpdateClipboxPlacement( ActiveStreamingTarget.WorldPosition );
		var drawCommit = _gpuMesher.DrainDrawCommandCommitResult();

		var prioritizationStart = Stopwatch.GetTimestamp();
		_coordinateBuffer.Clear();
		_coordinateSetBuffer.Clear();
		if ( incremental )
		{
			foreach ( var coordinate in _pendingChunks )
			{
				if ( _desiredChunks.Contains( coordinate ) && !_loadedChunks.ContainsKey( coordinate ) &&
					_coordinateSetBuffer.Add( coordinate ) )
				{
					_coordinateBuffer.Add( coordinate );
				}
			}
			foreach ( var coordinate in _gameplayEnteringBuffer )
			{
				if ( !_loadedChunks.ContainsKey( coordinate ) && _coordinateSetBuffer.Add( coordinate ) )
				{
					_coordinateBuffer.Add( coordinate );
				}
			}
		}
		else
		{
			foreach ( var coordinate in _desiredChunks )
			{
				if ( !_loadedChunks.ContainsKey( coordinate ) )
				{
					_coordinateBuffer.Add( coordinate );
				}
			}
		}
		SortNearestFirst( _coordinateBuffer, center );

		_pendingChunks.Clear();
		foreach ( var coordinate in _coordinateBuffer )
		{
			_pendingChunks.Enqueue( coordinate );
		}

		_warmCoordinateBuffer.Clear();
		_coordinateSetBuffer.Clear();
		if ( incremental )
		{
			foreach ( var coordinate in _pendingWarmChunks )
			{
				if ( _renderDesiredChunks.Contains( coordinate ) && !_desiredChunks.Contains( coordinate ) &&
					!_renderPreparedChunks.Contains( coordinate ) && _coordinateSetBuffer.Add( coordinate ) )
				{
					_warmCoordinateBuffer.Add( coordinate );
				}
			}
			foreach ( var coordinate in _renderEnteringBuffer )
			{
				if ( !_desiredChunks.Contains( coordinate ) && !_renderPreparedChunks.Contains( coordinate ) &&
					_coordinateSetBuffer.Add( coordinate ) )
				{
					_warmCoordinateBuffer.Add( coordinate );
				}
			}
		}
		else
		{
			foreach ( var coordinate in _renderDesiredChunks )
			{
				if ( !_desiredChunks.Contains( coordinate ) && !_renderPreparedChunks.Contains( coordinate ) )
				{
					_warmCoordinateBuffer.Add( coordinate );
				}
			}
		}
		_warmGenerationCancellation?.Cancel();
		_warmGenerationRevision++;
		_pendingWarmChunks.Clear();
		_completedWarmChunks.Clear();
		SortNearestFirst( _warmCoordinateBuffer, center );

		foreach ( var coordinate in _warmCoordinateBuffer )
		{
			_pendingWarmChunks.Enqueue( coordinate );
		}
		var prioritizationMilliseconds = (float)Stopwatch.GetElapsedTime( prioritizationStart ).TotalMilliseconds;

		_generatedThisStream = 0;
		_retainedThisStream = _loadedChunks.Count;
		_unloadedThisStream = unloadedCount;
		_staleDiscardedThisStream = 0;
		_generationMillisecondsThisStream = 0f;
		_integrationMillisecondsThisStream = 0f;
		_slowestIntegrationFrameMilliseconds = 0f;
		_maximumObservedFrameMilliseconds = 0f;
		_generationBatchesThisStream = 0;
		_maximumGenerationBatchSizeThisStream = 0;
		_firstGenerationBatchMilliseconds = 0f;
		_firstGameplayIntegrationMilliseconds = 0f;
		_integratedBeforeWorkerCompleted = false;
		_hasObservedStreamingFrame = false;
		_completionReady = false;
		SlowestChunkGenerationMilliseconds = 0f;
		LastBackgroundWorkerMilliseconds = 0f;
		_streamStartedTimestamp = Stopwatch.GetTimestamp();
		_streamInProgress = true;

		if ( VerboseLogging )
		{
			Log.Info(
				$"[VoxelWorld] stream.begin center=C[{center.x},{center.y},{center.z}] reason=\"{reason}\" " +
				$"loadRadius={AuthoritativeGameplayRadius} retained={_loadedChunks.Count} " +
				$"unloaded={unloadedCount} queued={_pendingChunks.Count} desired={_desiredChunks.Count}" );
		}
		RefreshReadableStatus();

		if ( _pendingChunks.Count == 0 )
		{
			_generationCancellation?.Cancel();
			_streamRevision++;
			_completedChunks.Clear();
			_workerCompleted = true;
			CompleteStream();
		}
		else
		{
			StartBackgroundGeneration( _coordinateBuffer.ToArray() );
		}

		StartWarmGeneration( _warmCoordinateBuffer.ToArray() );

		var totalMilliseconds = (float)Stopwatch.GetElapsedTime( synchronousStart ).TotalMilliseconds;
		var gameplayTouched = incremental
			? _gameplayEnteringBuffer.Count + _gameplayLeavingBuffer.Count
			: _desiredChunks.Count;
		var renderTouched = incremental
			? _renderEnteringBuffer.Count + _renderLeavingBuffer.Count
			: _renderDesiredChunks.Count;
		if ( _playerFigureEightTestRunning )
		{
			if ( incremental )
			{
				_performanceStreaming.IncrementalUpdates++;
			}
			else
			{
				_performanceStreaming.FullUpdates++;
			}
			_performanceStreaming.TotalSynchronousMilliseconds += totalMilliseconds;
			_performanceStreaming.MaximumSynchronousMilliseconds = Math.Max(
				_performanceStreaming.MaximumSynchronousMilliseconds,
				totalMilliseconds );
			_performanceStreaming.TotalDesiredUpdateMilliseconds += desiredUpdateMilliseconds;
			_performanceStreaming.TotalPrioritizationMilliseconds += prioritizationMilliseconds;
			_performanceStreaming.TotalDrawCommitMilliseconds += drawCommit.Milliseconds;
			_performanceStreaming.DrawRebuilds += drawCommit.Rebuilt ? 1 : 0;
			_performanceStreaming.GameplayCoordinatesTouched += gameplayTouched;
			_performanceStreaming.RenderCoordinatesTouched += renderTouched;
		}

		if ( VerboseLogging )
		{
			Log.Info(
				$"[VoxelWorld] stream.window mode={(incremental ? "incremental" : "full")} " +
				$"center=C[{center.x},{center.y},{center.z}] " +
				$"previous=C[{previousCenter.x},{previousCenter.y},{previousCenter.z}] " +
				$"delta=C[{delta.x},{delta.y},{delta.z}] reason=\"{reason}\" " +
				$"totalMs={totalMilliseconds:0.0000} desiredMs={desiredUpdateMilliseconds:0.0000} " +
				$"prioritizeMs={prioritizationMilliseconds:0.0000} drawCommitMs={drawCommit.Milliseconds:0.0000} " +
				$"drawRebuilt={drawCommit.Rebuilt} gameplayEntering={_gameplayEnteringBuffer.Count} " +
				$"gameplayLeaving={_gameplayLeavingBuffer.Count} renderEntering={_renderEnteringBuffer.Count} " +
				$"renderLeaving={_renderLeavingBuffer.Count} " +
				$"gameplayTouched={gameplayTouched} renderTouched={renderTouched} " +
				$"gameplayQueued={_pendingChunks.Count} warmQueued={_pendingWarmChunks.Count} " +
				$"generationBatchSize={GenerationBatchSize}" );
		}
	}

	private void UpdateClipboxPlacement( Vector3 viewerPosition )
	{
		var anchor = WorldToLod1Anchor( viewerPosition );
		var lod0Lod1Changed = !_lod1Clipbox.HasAnchor || anchor != _lod1Clipbox.Anchor;
		if ( lod0Lod1Changed )
		{
			UpdateLod0Lod1Placement( anchor );
		}
		var lod2Changed = UpdateLod2Placement( viewerPosition );
		if ( VerboseLogging && (lod0Lod1Changed || lod2Changed) )
		{
			LogLodPlacement( "placement.update" );
		}
	}

	private void UpdateLod0Lod1Placement( Vector3Int anchor )
	{

		_nextLod0RenderActive.Clear();
		AddHalfOpenBox( _nextLod0RenderActive,
			anchor * 2 - new Vector3Int( PlacementInputs.Lod0VisualHalfExtent ),
			anchor * 2 + new Vector3Int( PlacementInputs.Lod0VisualHalfExtent ) );
		foreach ( var coordinate in _lod0RenderActive )
		{
			if ( !_nextLod0RenderActive.Contains( coordinate ) )
				_gpuMesher.SetRenderActive( new GpuMeshRegionKey( GpuMeshLevel.Lod0, coordinate ), false );
		}
		foreach ( var coordinate in _nextLod0RenderActive )
		{
			if ( !_lod0RenderActive.Contains( coordinate ) )
				_gpuMesher.SetRenderActive( new GpuMeshRegionKey( GpuMeshLevel.Lod0, coordinate ), true );
		}
		SwapSets( ref _lod0RenderActive, ref _nextLod0RenderActive );

		_nextLod1Cache.Clear();
		_nextLod1Active.Clear();
		var outerMinimum = anchor - new Vector3Int( PlacementInputs.Lod1CacheHalfExtent );
		var outerMaximum = anchor + new Vector3Int( PlacementInputs.Lod1CacheHalfExtent );
		var holeMinimum = anchor - new Vector3Int( PlacementInputs.Lod1HoleHalfExtent );
		var holeMaximum = anchor + new Vector3Int( PlacementInputs.Lod1HoleHalfExtent );
		AddHalfOpenBox( _nextLod1Cache, outerMinimum, outerMaximum );
		foreach ( var coordinate in _nextLod1Cache )
		{
			if ( !IsInsideHalfOpenBox( coordinate, holeMinimum, holeMaximum ) )
				_nextLod1Active.Add( coordinate );
		}

		_lod1EnteringBuffer.Clear();
		_lod1LeavingBuffer.Clear();
		foreach ( var coordinate in _lod1Clipbox.DesiredCache )
		{
			if ( !_nextLod1Cache.Contains( coordinate ) ) _lod1LeavingBuffer.Add( coordinate );
		}
		foreach ( var coordinate in _nextLod1Cache )
		{
			if ( !_lod1Clipbox.DesiredCache.Contains( coordinate ) ) _lod1EnteringBuffer.Add( coordinate );
		}

		foreach ( var coordinate in _lod1Clipbox.Active )
		{
			if ( !_nextLod1Active.Contains( coordinate ) )
				_gpuMesher.SetRenderActive( new GpuMeshRegionKey( GpuMeshLevel.Lod1, coordinate ), false );
		}
		foreach ( var coordinate in _nextLod1Active )
		{
			if ( !_lod1Clipbox.Active.Contains( coordinate ) )
				_gpuMesher.SetRenderActive( new GpuMeshRegionKey( GpuMeshLevel.Lod1, coordinate ), true );
		}
		foreach ( var coordinate in _lod1LeavingBuffer )
			_gpuMesher.Remove( new GpuMeshRegionKey( GpuMeshLevel.Lod1, coordinate ) );

		SortNearestFirst( _lod1EnteringBuffer, anchor );
		foreach ( var coordinate in _lod1EnteringBuffer )
		{
			var descriptor = new GpuSdfDescriptor(
				new GpuMeshRegionKey( GpuMeshLevel.Lod1, coordinate ),
				CellsPerAxis,
				Lod1CellSize,
				CurrentTerrainSettings,
				ProceduralTerrainSdf.CurrentVersion,
				_terrainContentRevision );
			_gpuMesher.Schedule( descriptor, _playerFigureEightRouteDistance, GpuMeshResidency.Lod1 );
		}

		_nextTransitionDesired.Clear();
		AddTransitionFaces( _nextTransitionDesired, holeMinimum, holeMaximum );
		_transitionEnteringBuffer.Clear();
		_transitionLeavingBuffer.Clear();
		foreach ( var key in _transitionDesired )
		{
			if ( !_nextTransitionDesired.Contains( key ) ) _transitionLeavingBuffer.Add( key );
		}
		foreach ( var key in _nextTransitionDesired )
		{
			if ( !_transitionDesired.Contains( key ) ) _transitionEnteringBuffer.Add( key );
		}
		_lastTransitionEntered = _transitionEnteringBuffer.Count;
		_lastTransitionLeft = _transitionLeavingBuffer.Count;
		_lastTransitionRetained = _nextTransitionDesired.Count - _transitionEnteringBuffer.Count;
		GpuTransitionIdentitySnapshot retainedBefore = default;
		if ( VerboseLogging )
		{
			_transitionRetainedBuffer.Clear();
			foreach ( var key in _nextTransitionDesired )
			{
				if ( _transitionDesired.Contains( key ) ) _transitionRetainedBuffer.Add( key );
			}
			retainedBefore = _gpuMesher.CaptureTransitionIdentity( _transitionRetainedBuffer );
		}
		_transitionEntered += _lastTransitionEntered;
		_transitionLeft += _lastTransitionLeft;
		foreach ( var key in _transitionLeavingBuffer ) _gpuMesher.RemoveTransition( key );
		SortTransitionsNearestFirst( _transitionEnteringBuffer, anchor );
		foreach ( var key in _transitionEnteringBuffer )
		{
			_gpuMesher.SetTransitionActive( key, true );
			_gpuMesher.ScheduleTransition(
				new GpuTransitionDescriptor(
					key,
					CellsPerAxis,
					CellSize,
					Lod1CellSize,
					CurrentTerrainSettings,
					ProceduralTerrainSdf.CurrentVersion,
					_terrainContentRevision ),
				_playerFigureEightRouteDistance );
		}
		_transitionDesired.Clear();
		_transitionDesired.UnionWith( _nextTransitionDesired );
		if ( VerboseLogging )
		{
			var retainedAfter = _gpuMesher.CaptureTransitionIdentity( _transitionRetainedBuffer );
			Log.Info(
				$"[VoxelWorld] transition.update anchor=[{anchor.x},{anchor.y},{anchor.z}] " +
				$"desired={_transitionDesired.Count} entered={_lastTransitionEntered} " +
				$"left={_lastTransitionLeft} retained={_lastTransitionRetained} " +
				$"residentRetained={retainedAfter.Count} " +
				$"identityBefore={retainedBefore.Digest:X16} " +
				$"identityAfter={retainedAfter.Digest:X16} " +
				$"identityPreserved={retainedBefore == retainedAfter} " +
				$"ready={_gpuMesher.TransitionReadyCount} " +
				$"pending={_gpuMesher.TransitionPendingCount}" );
		}

		_lod1Clipbox.RecordUpdate(
			anchor, outerMinimum, outerMaximum, holeMinimum, holeMaximum,
			_lod1EnteringBuffer.Count, _lod1LeavingBuffer.Count,
			_lod1Clipbox.HasAnchor && IsAdjacent( _lod1Clipbox.Anchor, anchor ) );
		_lod1Clipbox.DesiredCache.Clear();
		_lod1Clipbox.DesiredCache.UnionWith( _nextLod1Cache );
		_lod1Clipbox.Active.Clear();
		_lod1Clipbox.Active.UnionWith( _nextLod1Active );
	}

	private bool UpdateLod2Placement( Vector3 viewerPosition )
	{
		if ( PlacementInputs.MaximumVisualLevel != GpuMeshLevel.Lod2 || !_lod1Clipbox.HasAnchor ) return false;
		var anchor = WorldToLod2Anchor( viewerPosition );
		var nearMinimum = _lod1Clipbox.OuterMinimum;
		var nearMaximum = _lod1Clipbox.OuterMaximum;
		if ( _lod2Clipbox.HasAnchor && anchor == _lod2Clipbox.Anchor &&
			nearMinimum == _lod2Clipbox.NearCoverageMinimum &&
			nearMaximum == _lod2Clipbox.NearCoverageMaximum ) return false;

		var outerMinimum = anchor - new Vector3Int( PlacementInputs.Lod2CacheHalfExtent );
		var outerMaximum = anchor + new Vector3Int( PlacementInputs.Lod2CacheHalfExtent );
		_nextLod2Cache.Clear();
		_nextLod2Active.Clear();
		AddHalfOpenBox( _nextLod2Cache, outerMinimum, outerMaximum );
		foreach ( var coordinate in _nextLod2Cache )
		{
			if ( !IsLod2RegionContainedByNearCoverage( coordinate, nearMinimum, nearMaximum ) )
				_nextLod2Active.Add( coordinate );
		}

		_lod2EnteringBuffer.Clear();
		_lod2LeavingBuffer.Clear();
		foreach ( var coordinate in _lod2Clipbox.DesiredCache )
		{
			if ( !_nextLod2Cache.Contains( coordinate ) ) _lod2LeavingBuffer.Add( coordinate );
		}
		foreach ( var coordinate in _nextLod2Cache )
		{
			if ( !_lod2Clipbox.DesiredCache.Contains( coordinate ) ) _lod2EnteringBuffer.Add( coordinate );
		}

		var activated = 0;
		var deactivated = 0;
		foreach ( var coordinate in _lod2Clipbox.Active )
		{
			if ( _nextLod2Active.Contains( coordinate ) ) continue;
			_gpuMesher.SetRenderActive( new GpuMeshRegionKey( GpuMeshLevel.Lod2, coordinate ), false );
			deactivated++;
		}
		foreach ( var coordinate in _nextLod2Active )
		{
			if ( _lod2Clipbox.Active.Contains( coordinate ) ) continue;
			_gpuMesher.SetRenderActive( new GpuMeshRegionKey( GpuMeshLevel.Lod2, coordinate ), true );
			activated++;
		}
		foreach ( var coordinate in _lod2LeavingBuffer )
			_gpuMesher.Remove( new GpuMeshRegionKey( GpuMeshLevel.Lod2, coordinate ) );

		SortNearestFirst( _lod2EnteringBuffer, anchor );
		foreach ( var coordinate in _lod2EnteringBuffer )
		{
			_gpuMesher.Schedule(
				new GpuSdfDescriptor(
					new GpuMeshRegionKey( GpuMeshLevel.Lod2, coordinate ),
					CellsPerAxis,
					Lod2CellSize,
					CurrentTerrainSettings,
					ProceduralTerrainSdf.CurrentVersion,
					_terrainContentRevision ),
				_playerFigureEightRouteDistance,
				GpuMeshResidency.Lod2 );
		}

		_lod2Clipbox.RecordUpdate(
			anchor,
			outerMinimum,
			outerMaximum,
			nearMinimum,
			nearMaximum,
			_lod2EnteringBuffer.Count,
			_lod2LeavingBuffer.Count,
			activated,
			deactivated,
			_lod2Clipbox.HasAnchor && IsAdjacent( _lod2Clipbox.Anchor, anchor ) );
		_lod2Clipbox.DesiredCache.Clear();
		_lod2Clipbox.DesiredCache.UnionWith( _nextLod2Cache );
		_lod2Clipbox.Active.Clear();
		_lod2Clipbox.Active.UnionWith( _nextLod2Active );
		return true;
	}

	private bool IsLod2RegionContainedByNearCoverage( Vector3Int coordinate,
		Vector3Int nearMinimum, Vector3Int nearMaximum )
	{
		var lod2Size = CellsPerAxis * Lod2CellSize;
		var lod1Size = CellsPerAxis * Lod1CellSize;
		var minimum = new Vector3(
			coordinate.x * lod2Size,
			coordinate.y * lod2Size,
			coordinate.z * lod2Size );
		var maximum = minimum + new Vector3( lod2Size );
		var coverageMinimum = new Vector3(
			nearMinimum.x * lod1Size,
			nearMinimum.y * lod1Size,
			nearMinimum.z * lod1Size );
		var coverageMaximum = new Vector3(
			nearMaximum.x * lod1Size,
			nearMaximum.y * lod1Size,
			nearMaximum.z * lod1Size );
		return minimum.x >= coverageMinimum.x && maximum.x <= coverageMaximum.x &&
			minimum.y >= coverageMinimum.y && maximum.y <= coverageMaximum.y &&
			minimum.z >= coverageMinimum.z && maximum.z <= coverageMaximum.z;
	}

	private static void AddTransitionFaces( HashSet<Lod0Lod1TransitionKey> keys,
		Vector3Int minimum, Vector3Int maximum )
	{
		for ( var z = minimum.z; z < maximum.z; z++ )
		for ( var y = minimum.y; y < maximum.y; y++ )
		{
			keys.Add( new Lod0Lod1TransitionKey(
				new Vector3Int( minimum.x - 1, y, z ), Lod0Lod1TransitionFace.PositiveX ) );
			keys.Add( new Lod0Lod1TransitionKey(
				new Vector3Int( maximum.x, y, z ), Lod0Lod1TransitionFace.NegativeX ) );
		}
		for ( var z = minimum.z; z < maximum.z; z++ )
		for ( var x = minimum.x; x < maximum.x; x++ )
		{
			keys.Add( new Lod0Lod1TransitionKey(
				new Vector3Int( x, minimum.y - 1, z ), Lod0Lod1TransitionFace.PositiveY ) );
			keys.Add( new Lod0Lod1TransitionKey(
				new Vector3Int( x, maximum.y, z ), Lod0Lod1TransitionFace.NegativeY ) );
		}
		for ( var y = minimum.y; y < maximum.y; y++ )
		for ( var x = minimum.x; x < maximum.x; x++ )
		{
			keys.Add( new Lod0Lod1TransitionKey(
				new Vector3Int( x, y, minimum.z - 1 ), Lod0Lod1TransitionFace.PositiveZ ) );
			keys.Add( new Lod0Lod1TransitionKey(
				new Vector3Int( x, y, maximum.z ), Lod0Lod1TransitionFace.NegativeZ ) );
		}
	}

	private static bool IsAdjacent( Vector3Int first, Vector3Int second )
	{
		var delta = second - first;
		return Math.Abs( delta.x ) <= 1 && Math.Abs( delta.y ) <= 1 && Math.Abs( delta.z ) <= 1;
	}

	private static void SortTransitionsNearestFirst( List<Lod0Lod1TransitionKey> keys,
		Vector3Int center )
	{
		keys.Sort( ( left, right ) =>
		{
			var leftCoordinate = left.Lod1Coordinate;
			var rightCoordinate = right.Lod1Coordinate;
			var leftDistance = Math.Abs( leftCoordinate.x - center.x ) +
				Math.Abs( leftCoordinate.y - center.y ) + Math.Abs( leftCoordinate.z - center.z );
			var rightDistance = Math.Abs( rightCoordinate.x - center.x ) +
				Math.Abs( rightCoordinate.y - center.y ) + Math.Abs( rightCoordinate.z - center.z );
			var comparison = leftDistance.CompareTo( rightDistance );
			if ( comparison != 0 ) return comparison;
			comparison = leftCoordinate.z.CompareTo( rightCoordinate.z );
			if ( comparison != 0 ) return comparison;
			comparison = leftCoordinate.y.CompareTo( rightCoordinate.y );
			if ( comparison != 0 ) return comparison;
			comparison = leftCoordinate.x.CompareTo( rightCoordinate.x );
			return comparison != 0 ? comparison : left.Face.CompareTo( right.Face );
		} );
	}

	private static bool IsInsideHalfOpenBox( Vector3Int coordinate, Vector3Int minimum, Vector3Int maximum ) =>
		coordinate.x >= minimum.x && coordinate.x < maximum.x &&
		coordinate.y >= minimum.y && coordinate.y < maximum.y &&
		coordinate.z >= minimum.z && coordinate.z < maximum.z;

	private static void AddHalfOpenBox( HashSet<Vector3Int> coordinates, Vector3Int minimum, Vector3Int maximum )
	{
		for ( var z = minimum.z; z < maximum.z; z++ )
		for ( var y = minimum.y; y < maximum.y; y++ )
		for ( var x = minimum.x; x < maximum.x; x++ )
			coordinates.Add( new Vector3Int( x, y, z ) );
	}

	private static void SwapSets( ref HashSet<Vector3Int> first, ref HashSet<Vector3Int> second ) =>
		(first, second) = (second, first);

	private static int GetCubeCoordinateCount( int radius )
	{
		var side = checked( radius * 2 + 1 );
		return checked( side * side * side );
	}

	private static void SlideDesiredWindow(
		HashSet<Vector3Int> coordinates,
		Vector3Int previousCenter,
		Vector3Int center,
		int radius,
		List<Vector3Int> entering,
		List<Vector3Int> leaving )
	{
		var delta = center - previousCenter;
		for ( var axis = 0; axis < 3; axis++ )
		{
			var axisDelta = axis == 0 ? delta.x : axis == 1 ? delta.y : delta.z;
			if ( axisDelta == 0 )
			{
				continue;
			}

			var sign = Math.Sign( axisDelta );
			var previousAxis = axis == 0 ? previousCenter.x : axis == 1 ? previousCenter.y : previousCenter.z;
			var centerAxis = axis == 0 ? center.x : axis == 1 ? center.y : center.z;
			ApplyWindowFace(
				coordinates, previousCenter, center, radius, axis,
				previousAxis - sign * radius, false, leaving );
			ApplyWindowFace(
				coordinates, center, previousCenter, radius, axis,
				centerAxis + sign * radius, true, entering );
		}
	}

	private static void ApplyWindowFace(
		HashSet<Vector3Int> coordinates,
		Vector3Int center,
		Vector3Int comparisonCenter,
		int radius,
		int axis,
		int fixedCoordinate,
		bool add,
		List<Vector3Int> changed )
	{
		for ( var second = -radius; second <= radius; second++ )
		{
			for ( var first = -radius; first <= radius; first++ )
			{
				var coordinate = axis switch
				{
					0 => new Vector3Int( fixedCoordinate, center.y + first, center.z + second ),
					1 => new Vector3Int( center.x + first, fixedCoordinate, center.z + second ),
					_ => new Vector3Int( center.x + first, center.y + second, fixedCoordinate )
				};
				if ( IsInsideCube( coordinate, comparisonCenter, radius ) )
				{
					continue;
				}

				var changedSet = add ? coordinates.Add( coordinate ) : coordinates.Remove( coordinate );
				if ( changedSet )
				{
					changed.Add( coordinate );
				}
			}
		}
	}

	private static bool IsInsideCube( Vector3Int coordinate, Vector3Int center, int radius )
	{
		return Math.Abs( coordinate.x - center.x ) <= radius &&
			Math.Abs( coordinate.y - center.y ) <= radius &&
			Math.Abs( coordinate.z - center.z ) <= radius;
	}

	private static void SortNearestFirst( List<Vector3Int> coordinates, Vector3Int center )
	{
		coordinates.Sort( ( left, right ) =>
		{
			var leftDistance = Math.Abs( left.x - center.x ) + Math.Abs( left.y - center.y ) + Math.Abs( left.z - center.z );
			var rightDistance = Math.Abs( right.x - center.x ) + Math.Abs( right.y - center.y ) + Math.Abs( right.z - center.z );
			var distanceComparison = leftDistance.CompareTo( rightDistance );
			if ( distanceComparison != 0 )
			{
				return distanceComparison;
			}

			var zComparison = left.z.CompareTo( right.z );
			if ( zComparison != 0 )
			{
				return zComparison;
			}

			var yComparison = left.y.CompareTo( right.y );
			return yComparison != 0 ? yComparison : left.x.CompareTo( right.x );
		} );
	}

	private void StartBackgroundGeneration( Vector3Int[] coordinates )
	{
		_generationCancellation?.Cancel();
		var previousTask = _generationTask ?? System.Threading.Tasks.Task.CompletedTask;
		var cancellation = new CancellationTokenSource();
		_generationCancellation = cancellation;
		var revision = ++_streamRevision;
		_workerCompleted = false;
		_completedChunks.Clear();
		_generationTask = GenerateChunksInBackground(
			previousTask,
			coordinates,
			CellsPerAxis,
			CellSize,
			CurrentTerrainSettings,
			revision,
			cancellation.Token );
	}

	private async System.Threading.Tasks.Task GenerateChunksInBackground(
		System.Threading.Tasks.Task previousTask,
		Vector3Int[] coordinates,
		int cellsPerAxis,
		float cellSize,
		ProceduralTerrainSettings terrainSettings,
		int revision,
		CancellationToken cancellationToken )
	{
		try
		{
			await previousTask;
			if ( cancellationToken.IsCancellationRequested )
			{
				return;
			}

			var totalWorkerMilliseconds = 0f;
			var batchIndex = 0;
			for ( var offset = 0; offset < coordinates.Length; offset += GenerationBatchSize )
			{
				var batchOffset = offset;
				var batchCount = Math.Min( GenerationBatchSize, coordinates.Length - batchOffset );
				var batch = await Task.RunInThreadAsync( () =>
				{
					var workerStart = Stopwatch.GetTimestamp();
					var chunks = new List<VoxelChunk>( batchCount );
					var generationMilliseconds = 0f;
					var lastChunkMilliseconds = 0f;
					var slowestChunkMilliseconds = 0f;
					for ( var index = 0; index < batchCount; index++ )
					{
						if ( cancellationToken.IsCancellationRequested )
						{
							break;
						}

						var generationStart = Stopwatch.GetTimestamp();
						var chunk = new VoxelChunk(
							coordinates[batchOffset + index],
							cellsPerAxis,
							cellSize,
							terrainSettings );
						chunks.Add( chunk );
						lastChunkMilliseconds = (float)Stopwatch.GetElapsedTime( generationStart ).TotalMilliseconds;
						generationMilliseconds += lastChunkMilliseconds;
						slowestChunkMilliseconds = Math.Max( slowestChunkMilliseconds, lastChunkMilliseconds );
					}

					return (
						Chunks: chunks,
						GenerationMilliseconds: generationMilliseconds,
						LastChunkMilliseconds: lastChunkMilliseconds,
						SlowestChunkMilliseconds: slowestChunkMilliseconds,
						WorkerMilliseconds: (float)Stopwatch.GetElapsedTime( workerStart ).TotalMilliseconds );
				} );

				await Task.MainThread();
				if ( cancellationToken.IsCancellationRequested || revision != _streamRevision )
				{
					_staleDiscardedThisStream += batch.Chunks.Count;
					if ( _playerFigureEightTestRunning )
					{
						_performanceBounds.StaleOrCancelledQueries += batch.Chunks.Count;
					}
					if ( VerboseLogging && batch.Chunks.Count > 0 )
					{
						Log.Info(
							$"[VoxelWorld] stream.stale revision={revision} currentRevision={_streamRevision} " +
							$"discarded={batch.Chunks.Count}" );
					}
					return;
				}

				totalWorkerMilliseconds += batch.WorkerMilliseconds;
				_generationMillisecondsThisStream += batch.GenerationMilliseconds;
				LastChunkGenerationMilliseconds = batch.LastChunkMilliseconds;
				SlowestChunkGenerationMilliseconds = Math.Max(
					SlowestChunkGenerationMilliseconds,
					batch.SlowestChunkMilliseconds );
				foreach ( var chunk in batch.Chunks )
				{
					if ( _playerFigureEightTestRunning )
					{
						RecordBoundsQuery(
							chunk.DensityClassification,
							chunk.DensityRangeEvaluationMilliseconds,
							false );
					}
					_completedChunks.Enqueue( chunk );
				}
				batchIndex++;
				_generationBatchesThisStream = batchIndex;
				_maximumGenerationBatchSizeThisStream = Math.Max(
					_maximumGenerationBatchSizeThisStream,
					batch.Chunks.Count );
				if ( batchIndex == 1 )
				{
					_firstGenerationBatchMilliseconds =
						(float)Stopwatch.GetElapsedTime( _streamStartedTimestamp ).TotalMilliseconds;
				}
				if ( _playerFigureEightTestRunning )
				{
					_performanceStreaming.GenerationBatches++;
					_performanceStreaming.MaximumGenerationBatchSize = Math.Max(
						_performanceStreaming.MaximumGenerationBatchSize,
						batch.Chunks.Count );
					if ( batchIndex == 1 )
					{
						_performanceStreaming.MaximumFirstGameplayBatchMilliseconds = Math.Max(
							_performanceStreaming.MaximumFirstGameplayBatchMilliseconds,
							(float)Stopwatch.GetElapsedTime( _streamStartedTimestamp ).TotalMilliseconds );
					}
				}
				if ( VerboseLogging )
				{
					Log.Info(
						$"[VoxelWorld] stream.batch revision={revision} index={batchIndex} " +
						$"count={batch.Chunks.Count} published={Math.Min( coordinates.Length, batchOffset + batch.Chunks.Count )} " +
						$"total={coordinates.Length}" );
				}
			}

			LastBackgroundWorkerMilliseconds = totalWorkerMilliseconds;
			_workerCompleted = true;
		}
		catch ( System.Threading.Tasks.TaskCanceledException )
		{
		}
		catch ( Exception exception )
		{
			await Task.MainThread();
			if ( revision != _streamRevision )
			{
				return;
			}

			_completedChunks.Clear();
			_pendingChunks.Clear();
			_streamInProgress = false;
			LastStreamSummary = $"Background generation failed: {exception.Message}";
			Log.Error(
				exception,
				$"[VoxelWorld] stream.failed revision={revision} error=\"{exception.Message}\"" );
			RefreshReadableStatus();
		}
	}

	private void StartWarmGeneration( Vector3Int[] coordinates )
	{
		_warmGenerationCancellation?.Cancel();
		var cancellation = new CancellationTokenSource();
		_warmGenerationCancellation = cancellation;
		var revision = ++_warmGenerationRevision;
		_warmWorkerCompleted = coordinates.Length == 0;
		_completedWarmChunks.Clear();
		if ( coordinates.Length == 0 )
		{
			_pendingWarmChunks.Clear();
			return;
		}

		var previousTerrainTask = _generationTask ?? System.Threading.Tasks.Task.CompletedTask;
		var previousWarmTask = _warmGenerationTask ?? System.Threading.Tasks.Task.CompletedTask;
		_warmGenerationTask = GenerateWarmChunksInBackground(
			previousTerrainTask,
			previousWarmTask,
			coordinates,
			CellsPerAxis,
			CellSize,
			CurrentTerrainSettings,
			revision,
			cancellation.Token );
	}

	private async System.Threading.Tasks.Task GenerateWarmChunksInBackground(
		System.Threading.Tasks.Task previousTerrainTask,
		System.Threading.Tasks.Task previousWarmTask,
		Vector3Int[] coordinates,
		int cellsPerAxis,
		float cellSize,
		ProceduralTerrainSettings terrainSettings,
		int revision,
		CancellationToken cancellationToken )
	{
		try
		{
			await previousTerrainTask;
			await previousWarmTask;
			if ( cancellationToken.IsCancellationRequested )
			{
				return;
			}

			var rejectedSolid = 0;
			var rejectedAir = 0;
			var potential = 0;
			var constructed = 0;
			for ( var offset = 0; offset < coordinates.Length; offset += GenerationBatchSize )
			{
				var batchOffset = offset;
				var batchCount = Math.Min( GenerationBatchSize, coordinates.Length - batchOffset );
				var batch = await Task.RunInThreadAsync( () =>
				{
					var results = new List<WarmChunkResult>( batchCount );
					for ( var index = 0; index < batchCount; index++ )
					{
						if ( cancellationToken.IsCancellationRequested )
						{
							break;
						}

						var coordinate = coordinates[batchOffset + index];
						var boundsStart = Stopwatch.GetTimestamp();
						var densityRange = VoxelChunk.ClassifyDensityRange(
							coordinate, cellsPerAxis, cellSize, terrainSettings );
						var boundsMilliseconds = (float)Stopwatch.GetElapsedTime(
							boundsStart ).TotalMilliseconds;
						var chunk = densityRange.Classification ==
							ChunkDensityClassification.PotentiallySurfaceContaining
							? new VoxelChunk(
								coordinate,
								cellsPerAxis,
								cellSize,
								terrainSettings,
								densityRange )
							: null;
						results.Add( new WarmChunkResult(
							coordinate,
							densityRange.Classification,
							chunk,
							boundsMilliseconds ) );
					}
					return results;
				} );

				await Task.MainThread();
				if ( cancellationToken.IsCancellationRequested || revision != _warmGenerationRevision )
				{
					if ( _playerFigureEightTestRunning )
					{
						_performanceBounds.StaleOrCancelledQueries += batch.Count;
					}
					return;
				}

				foreach ( var result in batch )
				{
					if ( _playerFigureEightTestRunning )
					{
						RecordBoundsQuery(
							result.Classification,
							result.BoundsMilliseconds,
							true );
					}
					_completedWarmChunks.Enqueue( result );
					switch ( result.Classification )
					{
						case ChunkDensityClassification.DefinitelySolid:
							rejectedSolid++;
							break;
						case ChunkDensityClassification.DefinitelyAir:
							rejectedAir++;
							break;
						default:
							potential++;
							constructed++;
							break;
					}
				}
			}

			_warmWorkerCompleted = true;
			if ( _playerFigureEightTestRunning )
			{
				_performanceStreaming.WarmCoordinatesClassified +=
					rejectedSolid + rejectedAir + potential;
				_performanceStreaming.WarmRejectedSolid += rejectedSolid;
				_performanceStreaming.WarmRejectedAir += rejectedAir;
				_performanceStreaming.WarmPotentiallySurfaceContaining += potential;
				_performanceStreaming.WarmTransientChunksConstructed += constructed;
			}
			if ( VerboseLogging )
			{
				Log.Info(
					$"[VoxelWorld] render.warm.generation revision={revision} candidates={coordinates.Length} " +
					$"rejectedSolid={rejectedSolid} rejectedAir={rejectedAir} potential={potential} " +
					$"constructed={constructed}" );
			}
		}
		catch ( System.Threading.Tasks.TaskCanceledException )
		{
		}
		catch ( Exception exception )
		{
			await Task.MainThread();
			if ( revision == _warmGenerationRevision )
			{
				_pendingWarmChunks.Clear();
				_completedWarmChunks.Clear();
				_warmWorkerCompleted = true;
				Log.Error(
					exception,
					$"[VoxelWorld] render.warm.failed revision={revision} error=\"{exception.Message}\"" );
			}
		}
	}

	private bool IntegrateCompletedChunks()
	{
		var integrationStart = Stopwatch.GetTimestamp();
		var integratedCount = 0;
		while ( _completedChunks.TryDequeue( out var chunk ) )
		{
			if ( !_pendingChunks.TryDequeue( out var pendingCoordinate ) || pendingCoordinate != chunk.Coordinate )
			{
				Log.Error(
					$"[VoxelWorld] stream.integration.invalid chunk={chunk.LogId} reason=\"pending order mismatch\"" );
				continue;
			}

			if ( _desiredChunks.Contains( chunk.Coordinate ) && !_loadedChunks.ContainsKey( chunk.Coordinate ) )
			{
				if ( _generatedThisStream == 0 )
				{
					_firstGameplayIntegrationMilliseconds =
						(float)Stopwatch.GetElapsedTime( _streamStartedTimestamp ).TotalMilliseconds;
					_integratedBeforeWorkerCompleted = !_workerCompleted;
				}
				_loadedChunks.Add( chunk.Coordinate, chunk );
				_renderPreparedChunks.Add( chunk.Coordinate );
				_gpuMesher.Schedule(
					chunk,
					_terrainContentRevision,
					_playerFigureEightRouteDistance,
					GpuMeshResidency.Gameplay );
				_gpuMesher.SetRenderActive(
					new GpuMeshRegionKey( GpuMeshLevel.Lod0, chunk.Coordinate ),
					_lod0RenderActive.Contains( chunk.Coordinate ) );
				integratedCount++;
				_generatedThisStream++;

			}

			if ( Stopwatch.GetElapsedTime( integrationStart ).TotalMilliseconds >= MainThreadIntegrationBudgetMilliseconds )
			{
				break;
			}
		}

		if ( integratedCount > 0 )
		{
			_performanceChunksIntegrated += integratedCount;
			var integrationMilliseconds = (float)Stopwatch.GetElapsedTime( integrationStart ).TotalMilliseconds;
			_integrationMillisecondsThisStream += integrationMilliseconds;
			_slowestIntegrationFrameMilliseconds = Math.Max(
				_slowestIntegrationFrameMilliseconds,
				integrationMilliseconds );
		}

		if ( _streamInProgress && _workerCompleted && _completedChunks.Count == 0 && _pendingChunks.Count == 0 )
		{
			_completionReady = true;
		}

		return integratedCount > 0;
	}

	private bool IntegrateCompletedWarmChunks()
	{
		var integrationStart = Stopwatch.GetTimestamp();
		var processedCount = 0;
		while ( _completedWarmChunks.TryDequeue( out var result ) )
		{
			if ( !_pendingWarmChunks.TryDequeue( out var pendingCoordinate ) ||
				pendingCoordinate != result.Coordinate )
			{
				Log.Error(
					$"[VoxelWorld] render.warm.integration.invalid chunk=C[{result.Coordinate.x}," +
					$"{result.Coordinate.y},{result.Coordinate.z}] " +
					"reason=\"pending order mismatch\"" );
				continue;
			}

			if ( _renderDesiredChunks.Contains( result.Coordinate ) )
			{
				_renderPreparedChunks.Add( result.Coordinate );
				if ( result.Chunk is not null )
				{
					var residency = _loadedChunks.ContainsKey( result.Coordinate )
						? GpuMeshResidency.Gameplay
						: GpuMeshResidency.Warm;
					_gpuMesher.Schedule(
						result.Chunk,
						_terrainContentRevision,
						_playerFigureEightRouteDistance,
						residency );
					_gpuMesher.SetRenderActive(
						new GpuMeshRegionKey( GpuMeshLevel.Lod0, result.Coordinate ),
						residency == GpuMeshResidency.Gameplay && _lod0RenderActive.Contains( result.Coordinate ) );
				}
				processedCount++;
			}

			if ( Stopwatch.GetElapsedTime( integrationStart ).TotalMilliseconds >=
				MainThreadIntegrationBudgetMilliseconds )
			{
				break;
			}
		}

		return processedCount > 0;
	}

	private void CompleteStream()
	{
		_streamInProgress = false;
		_completionReady = false;
		LastStreamSettleMilliseconds = (float)Stopwatch.GetElapsedTime( _streamStartedTimestamp ).TotalMilliseconds;
		LastRetainedChunkCount = _retainedThisStream;
		LastUnloadedChunkCount = _unloadedThisStream;
		LastGeneratedChunkCount = _generatedThisStream;
		LastStaleDiscardedChunkCount = _staleDiscardedThisStream;
		LastStreamGenerationMilliseconds = _generationMillisecondsThisStream;
		LastStreamIntegrationMilliseconds = _integrationMillisecondsThisStream;
		SlowestIntegrationFrameMilliseconds = _slowestIntegrationFrameMilliseconds;
		MaximumObservedFrameMilliseconds = _maximumObservedFrameMilliseconds;
		LastEffectiveChunksPerSecond = LastStreamSettleMilliseconds > 0f
			? _generatedThisStream * 1000f / LastStreamSettleMilliseconds
			: 0f;
		LastGenerationChunksPerSecond = LastStreamGenerationMilliseconds > 0f
			? _generatedThisStream * 1000f / LastStreamGenerationMilliseconds
			: 0f;
		LastStreamSummary =
			$"Loaded {_loadedChunks.Count}; retained {_retainedThisStream}; unloaded {_unloadedThisStream}; " +
			$"generated {_generatedThisStream}; stale {_staleDiscardedThisStream}; " +
			$"{LastEffectiveChunksPerSecond:0.0} chunks/sec effective; " +
			$"{LastGenerationChunksPerSecond:0.0} chunks/sec generation";
		if ( VerboseLogging )
		{
			var processMemoryBytes = global::Sandbox.Diagnostics.PerformanceStats.ApproximateProcessMemoryUsage;
			var probeChunkId = "missing";
			var surfaceProbeDensity = float.NaN;
			var oneCellUpProbeDensity = float.NaN;
			var surfaceProbeMaterialId = byte.MaxValue;
			var oneCellUpProbeMaterialId = byte.MaxValue;
			if ( _loadedChunks.TryGetValue( _streamingCenterCoordinate, out var probeChunk ) )
			{
				probeChunkId = probeChunk.LogId;
				probeChunk.TryGetSample( Vector3Int.Zero, out surfaceProbeDensity, out surfaceProbeMaterialId );
				probeChunk.TryGetSample( Vector3Int.OneZ, out oneCellUpProbeDensity, out oneCellUpProbeMaterialId );
			}

			Log.Info(
				$"[VoxelWorld] stream.complete center=C[{_streamingCenterCoordinate.x},{_streamingCenterCoordinate.y},{_streamingCenterCoordinate.z}] " +
				$"rangeMin=C[{_streamingCenterCoordinate.x - AuthoritativeGameplayRadius},{_streamingCenterCoordinate.y - AuthoritativeGameplayRadius},{_streamingCenterCoordinate.z - AuthoritativeGameplayRadius}] " +
				$"rangeMax=C[{_streamingCenterCoordinate.x + AuthoritativeGameplayRadius},{_streamingCenterCoordinate.y + AuthoritativeGameplayRadius},{_streamingCenterCoordinate.z + AuthoritativeGameplayRadius}] " +
				$"loaded={_loadedChunks.Count} pending={_pendingChunks.Count} retained={_retainedThisStream} " +
				$"unloaded={_unloadedThisStream} generated={_generatedThisStream} staleDiscarded={_staleDiscardedThisStream} " +
				$"settleMs={LastStreamSettleMilliseconds:0.###} workerMs={LastBackgroundWorkerMilliseconds:0.###} " +
				$"generationMs={LastStreamGenerationMilliseconds:0.###} integrationMs={LastStreamIntegrationMilliseconds:0.###} " +
				$"generationBatches={_generationBatchesThisStream} maxBatchSize={_maximumGenerationBatchSizeThisStream} " +
				$"firstBatchMs={_firstGenerationBatchMilliseconds:0.###} " +
				$"firstIntegrationMs={_firstGameplayIntegrationMilliseconds:0.###} " +
				$"integratedBeforeWorkerCompleted={_integratedBeforeWorkerCompleted} " +
				$"slowestIntegrationFrameMs={SlowestIntegrationFrameMilliseconds:0.###} " +
				$"maxObservedFrameMs={MaximumObservedFrameMilliseconds:0.###} " +
				$"effectiveChunksPerSecond={LastEffectiveChunksPerSecond:0.###} " +
				$"generationChunksPerSecond={LastGenerationChunksPerSecond:0.###} " +
				$"processMemoryBytes={processMemoryBytes} " +
				$"slowestChunkMs={SlowestChunkGenerationMilliseconds:0.###} " +
				$"probeChunk={probeChunkId} probeCell0=L[0,0,0] probeDensity0={surfaceProbeDensity} " +
				$"probeMaterial0=\"{VoxelChunk.GetMaterialName( surfaceProbeMaterialId )}\" probeMaterialId0={surfaceProbeMaterialId} " +
				$"probeCellUp=L[0,0,1] probeDensityUp={oneCellUpProbeDensity} " +
				$"probeMaterialUp=\"{VoxelChunk.GetMaterialName( oneCellUpProbeMaterialId )}\" probeMaterialIdUp={oneCellUpProbeMaterialId}" );
		}
	}

	private void RefreshReadableStatus()
	{
		LoadedChunkCount = _loadedChunks.Count;
		PendingChunkCount = _pendingChunks.Count;
		GeneratorStatus =
			$"Simplex noodle-and-cheese caves v{ProceduralTerrainSdf.CurrentVersion}; seed {WorldSeed}; " +
			$"base {SurfaceBaseHeight:0.##}, f {SurfaceFrequency:0.######}, " +
			$"amplitude {SurfaceAmplitude:0.##}";
		ChunkStatus = _performanceSnapshotReady
			? $"{LoadedChunkCount:N0} loaded; {PendingChunkCount:N0} queued; " +
				$"{_lastPerformanceChunksPerSecond:N1} chunks/sec over {_lastPerformanceWindowSeconds:N1} sec; " +
				$"{_gpuMesher?.ResidentCount ?? 0:N0} GPU meshes " +
				$"({_gpuMesher?.WarmResidentCount ?? 0:N0} warm)"
			: $"{LoadedChunkCount:N0} loaded; {PendingChunkCount:N0} queued; " +
				$"{_gpuMesher?.ResidentCount ?? 0:N0} GPU meshes " +
				$"({_gpuMesher?.WarmResidentCount ?? 0:N0} warm); " +
				$"{_gpuMesher?.PendingGameplayCount ?? 0:N0} gameplay and " +
				$"{_gpuMesher?.PendingWarmCount ?? 0:N0} warm meshes queued";
		StreamingPerformance = LastStreamSettleMilliseconds > 0f
			? $"{LastEffectiveChunksPerSecond:N1} chunks/sec; {LastStreamSettleMilliseconds:N3} ms last stream"
			: "No stream completed";
		LoadedChunkRange = _hasStreamingCenter
			? $"X {_streamingCenterCoordinate.x - AuthoritativeGameplayRadius} through {_streamingCenterCoordinate.x + AuthoritativeGameplayRadius}; " +
				$"Y {_streamingCenterCoordinate.y - AuthoritativeGameplayRadius} through {_streamingCenterCoordinate.y + AuthoritativeGameplayRadius}; " +
				$"Z {_streamingCenterCoordinate.z - AuthoritativeGameplayRadius} through {_streamingCenterCoordinate.z + AuthoritativeGameplayRadius}"
			: "Not initialized";
		RefreshPlayerChunkStatus();
	}

	private void RefreshPlayerChunkStatus()
	{
		var targetObject = ActiveStreamingTarget;
		var targetCoordinate = WorldToChunkCoordinate( targetObject.WorldPosition );
		StreamingTargetStatus =
			$"{targetObject.Name} at X {targetObject.WorldPosition.x:0.##}, " +
			$"Y {targetObject.WorldPosition.y:0.##}, Z {targetObject.WorldPosition.z:0.##}";
		PlayerChunk = $"Chunk X {targetCoordinate.x}, Y {targetCoordinate.y}, Z {targetCoordinate.z}";

		if ( _loadedChunks.TryGetValue( targetCoordinate, out var playerChunk ) )
		{
			PlayerChunkData =
				$"Loaded; {playerChunk.CellsPerAxis} cells per axis; {playerChunk.SampleCount:N0} logical samples; " +
				$"density {playerChunk.MinimumDensity:0.###} to {playerChunk.MaximumDensity:0.###}";
		}
		else if ( _desiredChunks.Contains( targetCoordinate ) )
		{
			PlayerChunkData = "Queued for loading";
		}
		else
		{
			PlayerChunkData = "Outside the active load radius";
		}
	}

}

internal readonly record struct WarmChunkResult(
	Vector3Int Coordinate,
	ChunkDensityClassification Classification,
	VoxelChunk Chunk,
	float BoundsMilliseconds );

internal readonly record struct FixedVoxelPlacementInputs(
	int GameplayRadius,
	int Lod0VisualHalfExtent,
	int Lod1CacheHalfExtent,
	int Lod1HoleHalfExtent,
	int Lod2CacheHalfExtent,
	int Lod2NominalHoleHalfExtent,
	GpuMeshLevel MaximumVisualLevel );

internal sealed class Lod2ClipboxState
{
	public bool HasAnchor { get; private set; }
	public Vector3Int Anchor { get; private set; }
	public Vector3Int OuterMinimum { get; private set; }
	public Vector3Int OuterMaximum { get; private set; }
	public Vector3Int NearCoverageMinimum { get; private set; }
	public Vector3Int NearCoverageMaximum { get; private set; }
	public HashSet<Vector3Int> DesiredCache { get; } = new();
	public HashSet<Vector3Int> Active { get; } = new();
	public int FullUpdates { get; private set; }
	public int IncrementalUpdates { get; private set; }
	public long EnteredRegions { get; private set; }
	public long LeftRegions { get; private set; }
	public long ActivatedRegions { get; private set; }
	public long DeactivatedRegions { get; private set; }
	public int LastEnteredRegions { get; private set; }
	public int LastLeftRegions { get; private set; }
	public int LastActivatedRegions { get; private set; }
	public int LastDeactivatedRegions { get; private set; }

	public void RecordUpdate(
		Vector3Int anchor,
		Vector3Int outerMinimum,
		Vector3Int outerMaximum,
		Vector3Int nearCoverageMinimum,
		Vector3Int nearCoverageMaximum,
		int entered,
		int left,
		int activated,
		int deactivated,
		bool incremental )
	{
		HasAnchor = true;
		Anchor = anchor;
		OuterMinimum = outerMinimum;
		OuterMaximum = outerMaximum;
		NearCoverageMinimum = nearCoverageMinimum;
		NearCoverageMaximum = nearCoverageMaximum;
		LastEnteredRegions = entered;
		LastLeftRegions = left;
		LastActivatedRegions = activated;
		LastDeactivatedRegions = deactivated;
		EnteredRegions += entered;
		LeftRegions += left;
		ActivatedRegions += activated;
		DeactivatedRegions += deactivated;
		if ( incremental ) IncrementalUpdates++;
		else FullUpdates++;
	}

	public void Clear()
	{
		HasAnchor = false;
		Anchor = default;
		OuterMinimum = default;
		OuterMaximum = default;
		NearCoverageMinimum = default;
		NearCoverageMaximum = default;
		DesiredCache.Clear();
		Active.Clear();
		FullUpdates = 0;
		IncrementalUpdates = 0;
		EnteredRegions = 0;
		LeftRegions = 0;
		ActivatedRegions = 0;
		DeactivatedRegions = 0;
		LastEnteredRegions = 0;
		LastLeftRegions = 0;
		LastActivatedRegions = 0;
		LastDeactivatedRegions = 0;
	}
}

internal sealed class Lod1ClipboxState
{
	public bool HasAnchor { get; private set; }
	public Vector3Int Anchor { get; private set; }
	public Vector3Int OuterMinimum { get; private set; }
	public Vector3Int OuterMaximum { get; private set; }
	public Vector3Int HoleMinimum { get; private set; }
	public Vector3Int HoleMaximum { get; private set; }
	public HashSet<Vector3Int> DesiredCache { get; } = new();
	public HashSet<Vector3Int> Active { get; } = new();
	public int FullUpdates { get; private set; }
	public int IncrementalUpdates { get; private set; }
	public long EnteredRegions { get; private set; }
	public long LeftRegions { get; private set; }
	public int LastEnteredRegions { get; private set; }
	public int LastLeftRegions { get; private set; }

	public void RecordUpdate( Vector3Int anchor, Vector3Int outerMinimum, Vector3Int outerMaximum,
		Vector3Int holeMinimum, Vector3Int holeMaximum, int entered, int left, bool incremental )
	{
		HasAnchor = true;
		Anchor = anchor;
		OuterMinimum = outerMinimum;
		OuterMaximum = outerMaximum;
		HoleMinimum = holeMinimum;
		HoleMaximum = holeMaximum;
		LastEnteredRegions = entered;
		LastLeftRegions = left;
		EnteredRegions += entered;
		LeftRegions += left;
		if ( incremental ) IncrementalUpdates++;
		else FullUpdates++;
	}

	public void Clear()
	{
		HasAnchor = false;
		Anchor = default;
		OuterMinimum = default;
		OuterMaximum = default;
		HoleMinimum = default;
		HoleMaximum = default;
		DesiredCache.Clear();
		Active.Clear();
		FullUpdates = 0;
		IncrementalUpdates = 0;
		EnteredRegions = 0;
		LeftRegions = 0;
		LastEnteredRegions = 0;
		LastLeftRegions = 0;
	}
}
