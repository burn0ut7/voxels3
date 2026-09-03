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
	private const int DefaultGameplayRadius = 4;
	private const int MaximumSupportedVisualLod = TerrainClipboxLimits.MaximumSupportedVisualLod;
	private const int SupportedVisualLevelCount = TerrainClipboxLimits.SupportedVisualLevelCount;
	private const int PerformanceResultSchemaVersion = 18;
	private const int RenderWarmShellChunks = 1;
	private const int RequiredCellsPerAxis = 32;
	private const float RequiredBaseCellSize = 16f;
	private const int DefaultMinimumVisualLod = 0;
	private const int DefaultMaximumVisualLod = 2;
	private const int DefaultLod0VisualHalfExtent = 4;
	private const int DefaultLodCacheHalfExtent = 8;
	private const int GenerationBatchSize = 256;
	private const string PerformanceResultsDirectory = "performance";
	private const string PerformanceResultsPath = "performance/results-v1.jsonl";
	private const string InspectorPerformanceTask = "PERFORMANCE-OVERVIEW-001/v4";
	private const string InspectorPerformanceRevision = "manual-inspector";
	private static readonly JsonSerializerOptions PerformanceJsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};
	private static readonly VoxelVisualConfiguration DefaultVisualConfiguration = new(
		DefaultMinimumVisualLod,
		DefaultMaximumVisualLod,
		DefaultLod0VisualHalfExtent,
		DefaultLodCacheHalfExtent );

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
	private readonly TerrainClipboxLevelState[] _levels = CreateClipboxLevels();
	private readonly TerrainTransitionPairState[] _transitionPairs = CreateTransitionPairs();
	private readonly Vector3Int[] _targetLevelAnchors = new Vector3Int[SupportedVisualLevelCount];
	private readonly Vector3Int[] _candidateLevelAnchors = new Vector3Int[SupportedVisualLevelCount];
	private readonly int[] _pendingMissingByLevel = new int[SupportedVisualLevelCount];
	private readonly List<GpuTransitionKey> _transitionRetainedBuffer = new();
	private readonly List<GpuTransitionKey> _transitionScheduleBuffer = new();
	private bool _clipboxPlacementPending;
	private bool _clipboxPlacementTargetAvailable;
	private VoxelVisualConfiguration _appliedVisualConfiguration = DefaultVisualConfiguration;
	private VoxelVisualConfiguration _targetVisualConfiguration = DefaultVisualConfiguration;
	private VoxelVisualConfiguration _stagedVisualConfiguration = DefaultVisualConfiguration;
	private long _requestedVisualConfigurationRevision;
	private long _targetVisualConfigurationRevision;
	private long _stagedVisualConfigurationRevision;
	private long _appliedVisualConfigurationRevision;
	private int _appliedGameplayRadius = DefaultGameplayRadius;
	private long _clipboxPlacementRequests;
	private long _clipboxPlacementCommits;
	private long _clipboxPlacementSuperseded;
	private long _clipboxPlacementDeferredUpdates;
	private long _clipboxPlacementReadinessBlocks;
	private long _clipboxPlacementUnsafeCommits;
	private readonly long[] _clipboxClassificationQueries = new long[SupportedVisualLevelCount];
	private readonly long[] _clipboxRejectedSolid = new long[SupportedVisualLevelCount];
	private readonly long[] _clipboxRejectedAir = new long[SupportedVisualLevelCount];
	private double _clipboxClassificationMilliseconds;
	private long _renderPreparedRevision;
	private long _lastClipboxReadinessResidentRevision = -1;
	private long _lastClipboxReadinessRenderPreparedRevision = -1;
	private long _performanceClipboxPlacementRequestStart;
	private long _performanceClipboxPlacementCommitStart;
	private long _performanceClipboxPlacementSupersededStart;
	private long _performanceClipboxPlacementDeferredStart;
	private long _performanceClipboxPlacementReadinessBlockStart;
	private long _performanceClipboxPlacementUnsafeCommitStart;
	private readonly long[] _performanceClassificationQueryStart = new long[SupportedVisualLevelCount];
	private readonly long[] _performanceRejectedSolidStart = new long[SupportedVisualLevelCount];
	private readonly long[] _performanceRejectedAirStart = new long[SupportedVisualLevelCount];
	private double _performanceClipboxClassificationMillisecondsStart;
	private float _performanceClipboxMaximumClassificationMilliseconds;
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
	private int _appliedCellsPerAxis = RequiredCellsPerAxis;
	private float _appliedCellSize = RequiredBaseCellSize;
	private int _appliedWorldSeed = ProceduralTerrainSdf.DefaultWorldSeed;
	private float _appliedSurfaceBaseHeight = ProceduralTerrainSdf.DefaultSurfaceBaseHeight;
	private float _appliedSurfaceFrequency = ProceduralTerrainSdf.DefaultSurfaceFrequency;
	private float _appliedSurfaceAmplitude = ProceduralTerrainSdf.DefaultSurfaceAmplitude;
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
	private GpuOuterLevelMeasurement _lastPerformanceOuter;
	private PerformanceProfilerMetrics _lastPerformanceProfiler = new();
	private GameObject ActiveStreamingTarget => StreamingTarget ?? _resolvedStreamingTarget ?? GameObject;
	private ProceduralTerrainSettings CurrentTerrainSettings => new(
		_appliedWorldSeed,
		_appliedSurfaceBaseHeight,
		_appliedSurfaceFrequency,
		_appliedSurfaceAmplitude );

	[Property, Category( "Chunk Configuration" )]
	public int CellsPerAxis { get; set; } = 32;

	[Property, Category( "Chunk Configuration" )]
	public float CellSize { get; set; } = 16f;

	[Property, Category( "Chunk Configuration" )]
	public int GameplayRadius { get; set; } = DefaultGameplayRadius;

	[Property, Category( "Terrain Visuals" )]
	public int MaximumVisualLod { get; set; } = DefaultMaximumVisualLod;

	[Property, Category( "Diagnostics" )]
	public int MinimumVisualLod { get; set; } = DefaultMinimumVisualLod;

	[Property, Category( "Terrain Visuals" )]
	public int Lod0VisualHalfExtent { get; set; } = DefaultLod0VisualHalfExtent;

	[Property, Category( "Terrain Visuals" )]
	public int LodCacheHalfExtent { get; set; } = DefaultLodCacheHalfExtent;

	public int LoadRadius => _appliedGameplayRadius;
	private int AuthoritativeGameplayRadius => _appliedGameplayRadius;

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
		_gpuMesher = new GpuVoxelMesher( Scene, RequiredCellsPerAxis );
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

		if ( !TryValidateConfiguration( out var visualConfiguration, out var configurationError ) )
		{
			if ( configurationError != _lastConfigurationError )
			{
				_lastConfigurationError = configurationError;
				Log.Warning(
					$"[VoxelWorld] configuration.rejected reason=\"{configurationError}\" " +
					$"appliedVisualRevision={_appliedVisualConfigurationRevision}" );
			}

			var gameplayChanged = GameplayRadius >= 0 && GameplayRadius <= 128 &&
				GameplayRadius != _appliedGameplayRadius;
			if ( gameplayChanged ) _appliedGameplayRadius = GameplayRadius;
			var targetPosition = ActiveStreamingTarget.WorldPosition;
			var targetCoordinate = WorldToChunkCoordinate( targetPosition );
			if ( gameplayChanged || !_hasStreamingCenter || targetCoordinate != _streamingCenterCoordinate )
			{
				RebuildDesiredChunks(
					targetCoordinate,
					gameplayChanged
						? "gameplay radius applied"
						: "streaming target crossed a chunk boundary",
					_targetVisualConfiguration );
			}
			else
			{
				UpdateClipboxPlacement( targetPosition, _targetVisualConfiguration );
			}
		}
		else
		{
			_lastConfigurationError = string.Empty;
			if ( DataConfigurationChanged() )
			{
				ApplyConfigurationAndRebuild();
			}
			else
			{
				var gameplayChanged = GameplayRadius != _appliedGameplayRadius;
				var visualChanged = visualConfiguration != _targetVisualConfiguration;
				if ( gameplayChanged ) _appliedGameplayRadius = GameplayRadius;
				var targetPosition = ActiveStreamingTarget.WorldPosition;
				var targetCoordinate = WorldToChunkCoordinate( targetPosition );
				if ( gameplayChanged || visualChanged || !_hasStreamingCenter ||
					targetCoordinate != _streamingCenterCoordinate )
				{
					var reason = gameplayChanged
						? "gameplay radius applied"
						: visualChanged
							? "visual configuration requested"
							: "streaming target crossed a chunk boundary";
					RebuildDesiredChunks( targetCoordinate, reason,
						visualConfiguration );
				}
				else
				{
					UpdateClipboxPlacement( targetPosition, visualConfiguration );
				}
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
		if ( _clipboxPlacementPending ) TryCommitPendingClipboxPlacement();
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
		if ( _gpuMesher is null || _gpuMesher.AllPendingCount > 0 ||
			_gpuMesher.TransitionPendingCount > 0 || HasClipboxPlacementWork )
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
		_gpuMesher?.BeginThroughputMeasurement( _appliedCellsPerAxis * _appliedCellSize );
		_gpuMesher?.BeginTransitionMeasurement();
		_gpuMesher?.BeginOuterLevelMeasurement();
		_performanceClipboxPlacementRequestStart = _clipboxPlacementRequests;
		_performanceClipboxPlacementCommitStart = _clipboxPlacementCommits;
		_performanceClipboxPlacementSupersededStart = _clipboxPlacementSuperseded;
		_performanceClipboxPlacementDeferredStart = _clipboxPlacementDeferredUpdates;
		_performanceClipboxPlacementReadinessBlockStart = _clipboxPlacementReadinessBlocks;
		_performanceClipboxPlacementUnsafeCommitStart = _clipboxPlacementUnsafeCommits;
		Array.Copy(
			_clipboxClassificationQueries,
			_performanceClassificationQueryStart,
			SupportedVisualLevelCount );
		Array.Copy(
			_clipboxRejectedSolid,
			_performanceRejectedSolidStart,
			SupportedVisualLevelCount );
		Array.Copy(
			_clipboxRejectedAir,
			_performanceRejectedAirStart,
			SupportedVisualLevelCount );
		_performanceClipboxClassificationMillisecondsStart = _clipboxClassificationMilliseconds;
		_performanceClipboxMaximumClassificationMilliseconds = 0f;
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
				CellsPerAxis = _appliedCellsPerAxis,
				BaseCellSize = _appliedCellSize,
				GameplayRadius = AuthoritativeGameplayRadius,
				MinimumVisualLod = _appliedVisualConfiguration.MinimumVisualLod,
				MaximumVisualLod = _appliedVisualConfiguration.MaximumVisualLod,
				Lod0VisualHalfExtent = _appliedVisualConfiguration.Lod0VisualHalfExtent,
				LodCacheHalfExtent = _appliedVisualConfiguration.LodCacheHalfExtent,
				VisualConfigurationRevision = _appliedVisualConfigurationRevision,
				Generator = "deterministic-simplex-caves",
				WorldSeed = _appliedWorldSeed,
				GeneratorVersion = ProceduralTerrainSdf.CurrentVersion,
				SurfaceBaseHeight = _appliedSurfaceBaseHeight,
				SurfaceFrequency = _appliedSurfaceFrequency,
				SurfaceAmplitude = _appliedSurfaceAmplitude,
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
				GameplayResident = _gpuMesher?.GameplayResidentCount ?? 0,
				WarmResident = _gpuMesher?.WarmResidentCount ?? 0,
				Pending = _gpuMesher?.PendingCount ?? 0,
				AllPending = _gpuMesher?.AllPendingCount ?? 0,
				GameplayPending = _gpuMesher?.PendingGameplayCount ?? 0,
				WarmPending = _gpuMesher?.PendingWarmCount ?? 0,
				NearVisualPending = _gpuMesher?.PendingNearVisualCount ?? 0,
				OuterVisualPending = _gpuMesher?.PendingOuterVisualCount ?? 0,
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
				DedicatedOuterTransientScratchBytes =
					_gpuMesher?.DedicatedOuterTransientScratchBytes ?? 0,
				TransitionTransientScratchBytes = _gpuMesher?.TransitionTransientScratchBytes ?? 0,
				AllocationCountReadbacks = _gpuMesher?.CountReadbackCount ?? 0,
				AllocationCountReadbackBytes = _gpuMesher?.CountReadbackBytes ?? 0,
				AllocationCountReadbackMilliseconds = _gpuMesher?.CountReadbackMilliseconds ?? 0,
				CountStageSubmissionMilliseconds = _gpuMesher?.CountSubmissionMilliseconds ?? 0,
				EmitStageSubmissionMilliseconds = _gpuMesher?.EmitSubmissionMilliseconds ?? 0,
				TopologyDigest = _gpuMesher?.TopologyDigest ?? string.Empty,
				PositionDigest = _gpuMesher?.PositionDigest ?? string.Empty,
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
			Hierarchy = new PerformanceHierarchyMetrics
			{
				PlacementPending = HasClipboxPlacementWork,
				RequestedVisualConfigurationRevision = _requestedVisualConfigurationRevision,
				StagedVisualConfigurationRevision = _stagedVisualConfigurationRevision,
				AppliedVisualConfigurationRevision = _appliedVisualConfigurationRevision,
				PlacementRequests = _clipboxPlacementRequests - _performanceClipboxPlacementRequestStart,
				PlacementCommits = _clipboxPlacementCommits - _performanceClipboxPlacementCommitStart,
				PlacementSuperseded = _clipboxPlacementSuperseded - _performanceClipboxPlacementSupersededStart,
				PlacementDeferredUpdates = _clipboxPlacementDeferredUpdates - _performanceClipboxPlacementDeferredStart,
				PlacementReadinessBlocks = _clipboxPlacementReadinessBlocks - _performanceClipboxPlacementReadinessBlockStart,
				PlacementUnsafeCommits = _clipboxPlacementUnsafeCommits - _performanceClipboxPlacementUnsafeCommitStart,
				ClassificationMilliseconds = (float)(_clipboxClassificationMilliseconds -
					_performanceClipboxClassificationMillisecondsStart),
				MaximumClassificationMilliseconds = _performanceClipboxMaximumClassificationMilliseconds,
				GameplayCoordinates = _desiredChunks.Count,
				GameplayPending = _pendingChunks.Count,
				TransitionDesired = _lastPerformanceTransitions.Desired,
				TransitionReady = _lastPerformanceTransitions.Ready,
				TransitionDrawable = _lastPerformanceTransitions.Drawable,
				TransitionPending = _lastPerformanceTransitions.Pending,
				TransitionActiveCells = _lastPerformanceTransitions.ActiveCells,
				TransitionVertices = _lastPerformanceTransitions.Vertices,
				TransitionIndices = _lastPerformanceTransitions.Indices,
				TransitionTopologyDigest = _lastPerformanceTransitions.TopologyDigest,
				TransitionPositionDigest = _lastPerformanceTransitions.PositionDigest,
				TransitionFineFaceMismatchCount = _lastPerformanceTransitions.FineFaceMismatchCount,
				TransitionCoarseFaceMismatchCount = _lastPerformanceTransitions.CoarseFaceMismatchCount,
				TransitionLateralMismatchCount = _lastPerformanceTransitions.LateralMismatchCount,
				TransitionInvalidTableCount = _lastPerformanceTransitions.InvalidTableCount,
				TransitionEntered = _transitionPairs.Sum( pair => pair.Entered ),
				TransitionLeft = _transitionPairs.Sum( pair => pair.Left ),
				LastTransitionEntered = _transitionPairs.Sum( pair => pair.LastEntered ),
				LastTransitionLeft = _transitionPairs.Sum( pair => pair.LastLeft ),
				LastTransitionRetained = _transitionPairs.Sum( pair => pair.LastRetained )
			},
			Levels = CreatePerformanceLevelMetrics(),
			TransitionPairs = CreatePerformanceTransitionPairMetrics(),
			OuterWork = new PerformanceOuterWorkMetrics
			{
				Scheduled = _lastPerformanceOuter.Scheduled,
				Published = _lastPerformanceOuter.Published,
				Cancelled = _lastPerformanceOuter.Cancelled,
				Superseded = _lastPerformanceOuter.Superseded,
				OpportunisticServices = _lastPerformanceOuter.OpportunisticServices,
				ForcedServices = _lastPerformanceOuter.ForcedServices,
				MaximumEligibleServiceGapMilliseconds = _lastPerformanceOuter.MaximumServiceGapMilliseconds,
				QueueDepth = CreateQueueDepthMetrics( _lastPerformanceOuter.Queue ),
				ScheduleToRenderable = CreateLatencyMetrics(
					_lastPerformanceOuter.ScheduleToRenderable )
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
				_gpuMesher.AllPendingCount > 0 ||
				_gpuMesher.TransitionPendingCount > 0 ||
				HasClipboxPlacementWork )
			{
				return;
			}
			_gpuMesher.MarkThroughputSettled();

			if ( !_performanceSettledCaptureRequested )
			{
				var trimmedArenas = _gpuMesher.TrimEmptyTrailingArenas();
				if ( trimmedArenas > 0 )
				{
					Log.Info(
						$"[VoxelWorld] gpu.geometry.trimmed emptyTrailingArenas={trimmedArenas} " +
						$"remainingArenas={_gpuMesher.ArenaCount}" );
				}
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
		_lastPerformanceOuter = _gpuMesher.CompleteOuterLevelMeasurement();

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
		MinimumVisibleMeshChunks = visibility.MinimumVisible,
		MaximumVisibleMeshChunks = visibility.MaximumVisible,
		AverageNonZeroIndirectDraws = visibility.AverageVisible,
		AverageCulledDraws = visibility.AverageCulled,
		CulledDrawPercentage = visibility.CulledPercent,
		LogicalBufferBytes = visibility.LogicalBufferBytes,
		ScalarReadbacks = visibility.ScalarReadbacks
	};

	private PerformanceLevelMetrics[] CreatePerformanceLevelMetrics()
	{
		var count = _appliedVisualConfiguration.MaximumVisualLod + 1;
		var result = new PerformanceLevelMetrics[count];
		for ( var level = 0; level < count; level++ )
		{
			var state = _levels[level];
			var schedule = _gpuMesher?.LevelScheduleMeasurement( level ) ?? default;
			var classificationQueries = _clipboxClassificationQueries[level] -
				_performanceClassificationQueryStart[level];
			var rejectedSolid = _clipboxRejectedSolid[level] - _performanceRejectedSolidStart[level];
			var rejectedAir = _clipboxRejectedAir[level] - _performanceRejectedAirStart[level];
			result[level] = new PerformanceLevelMetrics
			{
				Level = level,
				VisualEnabled = state.VisualEnabled,
				CellSize = CellSizeForLevel( level ),
				RegionSize = _appliedCellsPerAxis * CellSizeForLevel( level ),
				Anchor = ToPerformanceVector( state.Anchor ),
				OuterAnchor = ToPerformanceVector( state.OuterAnchor ),
				OuterMinimum = ToPerformanceVector( state.OuterMinimum ),
				OuterMaximum = ToPerformanceVector( state.OuterMaximum ),
				HoleMinimum = ToPerformanceVector( state.HoleMinimum ),
				HoleMaximum = ToPerformanceVector( state.HoleMaximum ),
				CachedCoordinates = state.DesiredCache.Count,
				ActiveCoordinates = state.Active.Count,
				Resident = _gpuMesher?.ResidentLevelCount( level ) ?? 0,
				Pending = _gpuMesher?.PendingLevelCount( level ) ?? 0,
				FullUpdates = state.FullUpdates,
				IncrementalUpdates = state.IncrementalUpdates,
				EnteredRegions = state.EnteredRegions,
				LeftRegions = state.LeftRegions,
				ActivatedRegions = state.ActivatedRegions,
				DeactivatedRegions = state.DeactivatedRegions,
				LastEnteredRegions = state.LastEnteredRegions,
				LastLeftRegions = state.LastLeftRegions,
				LastActivatedRegions = state.LastActivatedRegions,
				LastDeactivatedRegions = state.LastDeactivatedRegions,
				ClassificationQueries = classificationQueries,
				RejectedSolid = rejectedSolid,
				RejectedAir = rejectedAir,
				PotentiallySurfaceContaining = classificationQueries - rejectedSolid - rejectedAir,
				Scheduled = schedule.Scheduled,
				Published = schedule.Published,
				Cancelled = schedule.Cancelled,
				Superseded = schedule.Superseded,
				ScheduleToRenderable = CreateLatencyMetrics( schedule.ScheduleToRenderable ),
				AverageResidentMeshChunks = GetVisibilityLevelAverage(
					_lastPerformanceVisibility.LevelResidentTotals,
					_lastPerformanceVisibility.FrameCount,
					level ),
				AverageVisibleMeshChunks = GetVisibilityLevelAverage(
					_lastPerformanceVisibility.LevelVisibleTotals,
					_lastPerformanceVisibility.FrameCount,
					level ),
				SettledSurfaceMeshes = GetVisibilityLevelValue(
					_lastPerformanceVisibility.SettledLevelSurfaceMeshes,
					level ),
				TopologyDigest = _gpuMesher?.LevelTopologyDigest( level ) ?? string.Empty,
				PositionDigest = _gpuMesher?.LevelPositionDigest( level ) ?? string.Empty
			};
		}
		return result;
	}

	private PerformanceTransitionPairMetrics[] CreatePerformanceTransitionPairMetrics()
	{
		return (_lastPerformanceTransitions.Pairs ?? Array.Empty<GpuTransitionPairMeasurement>())
			.Where( pair => pair.FineLevel >= _appliedVisualConfiguration.MinimumVisualLod &&
				pair.CoarseLevel <= _appliedVisualConfiguration.MaximumVisualLod )
			.OrderBy( pair => pair.CoarseLevel )
			.Select( pair =>
			{
				var state = _transitionPairs[pair.CoarseLevel - 1];
				return new PerformanceTransitionPairMetrics
				{
					FineLevel = pair.FineLevel,
					CoarseLevel = pair.CoarseLevel,
					Desired = pair.Desired,
					Ready = pair.Ready,
					Drawable = pair.Drawable,
					Pending = pair.Pending,
					LastEntered = state.LastEntered,
					LastLeft = state.LastLeft,
					LastRetained = state.LastRetained,
					Entered = state.Entered,
					Left = state.Left,
					Scheduled = pair.Scheduled,
					Published = pair.Published,
					Cancelled = pair.Cancelled,
					Stale = pair.Stale,
					ActiveCells = pair.ActiveCells,
					Vertices = pair.Vertices,
					Indices = pair.Indices,
					Triangles = pair.Indices / 3,
					UsedVertexBytes = pair.Vertices * GpuVoxelMesher.TerrainVertexBytes,
					UsedIndexBytes = pair.Indices * sizeof( uint ),
					TopologyDigest = pair.TopologyDigest,
					PositionDigest = pair.PositionDigest,
					FineFaceMismatchCount = pair.FineFaceMismatchCount,
					CoarseFaceMismatchCount = pair.CoarseFaceMismatchCount,
					LateralEdgeDigest = pair.LateralEdgeDigest,
					LateralMismatchCount = pair.LateralMismatchCount,
					InvalidTableCount = pair.InvalidTableCount,
					Faces = pair.Faces.Select( CreatePerformanceTransitionFaceMetrics ).ToArray(),
					ScheduleToPublication = CreateDistributionMetrics( pair.ScheduleToPublication )
				};
			} )
			.ToArray();
	}

	private static PerformanceTransitionFaceMetrics CreatePerformanceTransitionFaceMetrics(
		GpuTransitionFaceMeasurement face ) => new()
	{
		FineLevel = face.Key.FineLevel,
		CoarseLevel = face.Key.CoarseLevel,
		CoarseCoordinate = ToPerformanceVector( face.Key.CoarseCoordinate ),
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
	};

	private static PerformanceLatencyMetrics CreateLatencyMetrics(
		GpuMeshScheduleLatencyMeasurement latency ) => new()
	{
		Samples = latency.Samples,
		TruncatedSamples = latency.TruncatedSamples,
		P50Milliseconds = latency.P50Milliseconds,
		P95Milliseconds = latency.P95Milliseconds,
		P99Milliseconds = latency.P99Milliseconds,
		MaximumMilliseconds = latency.MaximumMilliseconds,
		Cancelled = latency.Cancelled,
		Superseded = latency.Superseded
	};

	private static float GetVisibilityLevelAverage(
		IReadOnlyList<uint> values,
		uint frames,
		int level ) => frames > 0 && values is not null && level < values.Count
			? (float)values[level] / frames
			: 0;

	private static uint GetVisibilityLevelValue( IReadOnlyList<uint> values, int level ) =>
		values is not null && level < values.Count ? values[level] : 0;

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
			$"worldSeed={_appliedWorldSeed} generatorVersion={ProceduralTerrainSdf.CurrentVersion} " +
			$"surfaceBaseHeight={_appliedSurfaceBaseHeight} surfaceFrequency={_appliedSurfaceFrequency} " +
			$"surfaceAmplitude={_appliedSurfaceAmplitude} " +
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
			$"cellsPerAxis={_appliedCellsPerAxis} baseCellSize={_appliedCellSize:0.###} " +
			$"gameplayRadius={AuthoritativeGameplayRadius} " +
			$"minimumVisualLevel={_appliedVisualConfiguration.MinimumVisualLod} " +
			$"maximumVisualLevel={_appliedVisualConfiguration.MaximumVisualLod} " +
			$"visualRevision={_appliedVisualConfigurationRevision}" );

		if ( !_hasStreamingCenter || !_levels[0].HasPlacement )
		{
			Log.Info(
				$"[VoxelWorld] lod.inspect.pending gameplayCenterReady={_hasStreamingCenter} " +
				$"placementReady={_levels[0].HasPlacement}" );
			return;
		}
		var handoffReadiness = CapturePendingClipboxReadiness();
		var maximumLag = 0;
		for ( var level = 0; level < SupportedVisualLevelCount; level++ )
		{
			var targetAnchor = _clipboxPlacementTargetAvailable
				? _targetLevelAnchors[level]
				: _levels[level].Anchor;
			maximumLag = Math.Max(
				maximumLag,
				MaximumAxisDistance( _levels[level].Anchor, targetAnchor ) );
		}
		Log.Info(
			$"[VoxelWorld] lod.inspect.handoff pending={HasClipboxPlacementWork} " +
			$"staging={_clipboxPlacementPending} " +
			$"requestedVisualRevision={_requestedVisualConfigurationRevision} " +
			$"stagedVisualRevision={_stagedVisualConfigurationRevision} " +
			$"appliedVisualRevision={_appliedVisualConfigurationRevision} " +
			$"maximumLevelLag={maximumLag} " +
			$"missingLevels=[{string.Join( ',', handoffReadiness.MissingLevels ?? Array.Empty<int>() )}] " +
			$"missingTransitions={handoffReadiness.MissingTransitions} " +
			$"requests={_clipboxPlacementRequests} commits={_clipboxPlacementCommits} " +
			$"superseded={_clipboxPlacementSuperseded} deferredUpdates={_clipboxPlacementDeferredUpdates} " +
			$"readinessBlocks={_clipboxPlacementReadinessBlocks} unsafeCommits={_clipboxPlacementUnsafeCommits} " +
			$"classified={_clipboxClassificationQueries.Sum()} " +
			$"rejected={_clipboxRejectedSolid.Sum() + _clipboxRejectedAir.Sum()} " +
			$"classificationMs={_clipboxClassificationMilliseconds:0.000}" );
		if ( _gpuMesher is not null )
		{
			Log.Info(
				$"[VoxelWorld] lod.inspect.arenas count={_gpuMesher.ArenaCount} " +
				$"freeSlots={_gpuMesher.PoolCount} usage={_gpuMesher.ArenaUsage}" );
		}

		var gameplayMinimum = _streamingCenterCoordinate - new Vector3Int( AuthoritativeGameplayRadius );
		var gameplayMaximum = _streamingCenterCoordinate + new Vector3Int( AuthoritativeGameplayRadius + 1 );
		Log.Info(
			$"[VoxelWorld] lod.inspect.gameplay cellSize={_appliedCellSize:0.###} " +
			$"regionSize={_appliedCellsPerAxis * _appliedCellSize:0.###} " +
			$"anchor={FormatRegionCoordinate( _streamingCenterCoordinate )} " +
			$"gameplayRegions={FormatRegionBox( gameplayMinimum, gameplayMaximum )} " +
			$"gameplayWorld={FormatWorldBox( gameplayMinimum, gameplayMaximum, _appliedCellSize )} " +
			$"gameplayDesired={_desiredChunks.Count} loaded={_loadedChunks.Count} " +
			$"pendingGameplay={_gpuMesher?.PendingGameplayCount ?? 0} pendingWarm={_gpuMesher?.PendingWarmCount ?? 0}" );

		for ( var level = 0; level <= _appliedVisualConfiguration.MaximumVisualLod; level++ )
		{
			var state = _levels[level];
			var cellSize = CellSizeForLevel( level );
			Log.Info(
				$"[VoxelWorld] lod.inspect.level level={level} visualEnabled={state.VisualEnabled} " +
				$"cellSize={cellSize:0.###} regionSize={_appliedCellsPerAxis * cellSize:0.###} " +
				$"anchor={FormatRegionCoordinate( state.Anchor )} " +
				$"outerAnchor={FormatRegionCoordinate( state.OuterAnchor )} " +
				$"outerRegions={FormatRegionBox( state.OuterMinimum, state.OuterMaximum )} " +
				$"outerWorld={FormatWorldBox( state.OuterMinimum, state.OuterMaximum, cellSize )} " +
				$"holeRegions={FormatRegionBox( state.HoleMinimum, state.HoleMaximum )} " +
				$"holeWorld={FormatWorldBox( state.HoleMinimum, state.HoleMaximum, cellSize )} " +
				$"cached={state.DesiredCache.Count} active={state.Active.Count} " +
				$"resident={_gpuMesher?.ResidentLevelCount( level ) ?? 0} " +
				$"pending={_gpuMesher?.PendingLevelCount( level ) ?? 0} " +
				$"lastEnter={state.LastEnteredRegions} lastLeave={state.LastLeftRegions} " +
				$"lastActivate={state.LastActivatedRegions} lastDeactivate={state.LastDeactivatedRegions}" );
		}

		foreach ( var pair in _transitionPairs.Where( pair =>
			pair.FineLevel >= _appliedVisualConfiguration.MinimumVisualLod &&
			pair.CoarseLevel <= _appliedVisualConfiguration.MaximumVisualLod ) )
		{
			Log.Info(
				$"[VoxelWorld] lod.inspect.transition fineLevel={pair.FineLevel} " +
				$"coarseLevel={pair.CoarseLevel} desired={pair.Desired.Count} " +
				$"ready={_gpuMesher?.TransitionReadyCountForPair( pair.CoarseLevel ) ?? 0} " +
				$"drawable={_gpuMesher?.TransitionDrawableCountForPair( pair.CoarseLevel ) ?? 0} " +
				$"pending={_gpuMesher?.TransitionPendingCountForCoarseLevel( pair.CoarseLevel ) ?? 0} " +
				$"lastEnter={pair.LastEntered} lastLeave={pair.LastLeft}" );
		}
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
		var regionSize = _appliedCellsPerAxis * cellSize;
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

	private bool TryValidateConfiguration(
		out VoxelVisualConfiguration visualConfiguration,
		out string error )
	{
		visualConfiguration = default;
		if ( CellsPerAxis != RequiredCellsPerAxis )
		{
			error = $"Cells Per Axis is fixed at {RequiredCellsPerAxis}; runtime changes are unsupported.";
			return false;
		}

		if ( !float.IsFinite( CellSize ) || CellSize != RequiredBaseCellSize )
		{
			error = $"Cell Size is fixed at {RequiredBaseCellSize:0.###}; runtime changes are unsupported.";
			return false;
		}

		if ( GameplayRadius < 0 || GameplayRadius > 128 )
		{
			error = "Gameplay Radius must be between 0 and 128.";
			return false;
		}

		if ( MaximumVisualLod < 0 || MaximumVisualLod > MaximumSupportedVisualLod )
		{
			error = $"Maximum Visual LOD must be between 0 and {MaximumSupportedVisualLod}; level 3 is not enabled.";
			return false;
		}

		if ( MinimumVisualLod < 0 || MinimumVisualLod > MaximumVisualLod )
		{
			error = "Minimum Visual LOD must be between 0 and Maximum Visual LOD.";
			return false;
		}

		if ( Lod0VisualHalfExtent <= 0 || (Lod0VisualHalfExtent & 1) != 0 )
		{
			error = "LOD0 Visual Half Extent must be positive and even.";
			return false;
		}

		if ( LodCacheHalfExtent <= 0 || (LodCacheHalfExtent & 1) != 0 )
		{
			error = "LOD Cache Half Extent must be positive and even.";
			return false;
		}

		if ( Lod0VisualHalfExtent / 2 + 1 > LodCacheHalfExtent )
		{
			error = "LOD0 visual coverage must fit within the aligned coarse cache.";
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

		visualConfiguration = new VoxelVisualConfiguration(
			MinimumVisualLod,
			MaximumVisualLod,
			Lod0VisualHalfExtent,
			LodCacheHalfExtent );
		error = string.Empty;
		return true;
	}

	private bool DataConfigurationChanged()
	{
		return WorldSeed != _appliedWorldSeed ||
			SurfaceBaseHeight != _appliedSurfaceBaseHeight ||
			SurfaceFrequency != _appliedSurfaceFrequency ||
			SurfaceAmplitude != _appliedSurfaceAmplitude;
	}

	private void ApplyConfigurationAndRebuild()
	{
		if ( !TryValidateConfiguration( out var visualConfiguration, out var configurationError ) )
		{
			_lastConfigurationError = configurationError;
			Log.Warning( $"[VoxelWorld] configuration.invalid reason=\"{configurationError}\"" );
			return;
		}

		_appliedCellsPerAxis = RequiredCellsPerAxis;
		_appliedCellSize = RequiredBaseCellSize;
		_appliedGameplayRadius = GameplayRadius;
		_appliedVisualConfiguration = visualConfiguration;
		_targetVisualConfiguration = visualConfiguration;
		_stagedVisualConfiguration = visualConfiguration;
		_requestedVisualConfigurationRevision++;
		_targetVisualConfigurationRevision = _requestedVisualConfigurationRevision;
		_appliedVisualConfigurationRevision = _requestedVisualConfigurationRevision;
		_stagedVisualConfigurationRevision = _requestedVisualConfigurationRevision;
		_appliedWorldSeed = WorldSeed;
		_appliedSurfaceBaseHeight = SurfaceBaseHeight;
		_appliedSurfaceFrequency = SurfaceFrequency;
		_appliedSurfaceAmplitude = SurfaceAmplitude;

		_generationCancellation?.Cancel();
		_warmGenerationCancellation?.Cancel();
		_streamRevision++;
		_warmGenerationRevision++;
		_terrainContentRevision++;
		_gpuMesher.Reset( _appliedCellsPerAxis );
		_loadedChunks.Clear();
		_desiredChunks.Clear();
		_renderDesiredChunks.Clear();
		_nextRenderDesiredChunks.Clear();
		_renderPreparedChunks.Clear();
		_renderPreparedRevision++;
		foreach ( var level in _levels ) level.Clear();
		foreach ( var pair in _transitionPairs ) pair.Clear();
		_clipboxPlacementPending = false;
		_clipboxPlacementTargetAvailable = false;
		Array.Clear( _targetLevelAnchors );
		_clipboxPlacementRequests = 0;
		_clipboxPlacementCommits = 0;
		_clipboxPlacementSuperseded = 0;
		_clipboxPlacementDeferredUpdates = 0;
		_clipboxPlacementReadinessBlocks = 0;
		_clipboxPlacementUnsafeCommits = 0;
		Array.Clear( _clipboxClassificationQueries );
		Array.Clear( _clipboxRejectedSolid );
		Array.Clear( _clipboxRejectedAir );
		_clipboxClassificationMilliseconds = 0;
		_performanceClipboxMaximumClassificationMilliseconds = 0f;
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
		var chunkWorldSize = _appliedCellsPerAxis * _appliedCellSize;
		return new Vector3Int(
			(int)MathF.Floor( worldPosition.x / chunkWorldSize ),
			(int)MathF.Floor( worldPosition.y / chunkWorldSize ),
			(int)MathF.Floor( worldPosition.z / chunkWorldSize ) );
	}

	private Vector3Int WorldToLevelAnchor( Vector3 worldPosition, int level )
	{
		var regionSize = _appliedCellsPerAxis * CellSizeForLevel( level );
		var halfRegion = regionSize * 0.5f;
		return new Vector3Int(
			(int)MathF.Floor( (worldPosition.x + halfRegion) / regionSize ),
			(int)MathF.Floor( (worldPosition.y + halfRegion) / regionSize ),
			(int)MathF.Floor( (worldPosition.z + halfRegion) / regionSize ) );
	}

	private float CellSizeForLevel( int level ) => _appliedCellSize * (1 << level);

	private static TerrainClipboxLevelState[] CreateClipboxLevels()
	{
		var levels = new TerrainClipboxLevelState[SupportedVisualLevelCount];
		for ( var level = 0; level < levels.Length; level++ )
		{
			levels[level] = new TerrainClipboxLevelState( level );
		}
		return levels;
	}

	private static TerrainTransitionPairState[] CreateTransitionPairs()
	{
		var pairs = new TerrainTransitionPairState[MaximumSupportedVisualLod];
		for ( var coarseLevel = 1; coarseLevel <= MaximumSupportedVisualLod; coarseLevel++ )
		{
			pairs[coarseLevel - 1] = new TerrainTransitionPairState(
				coarseLevel - 1,
				coarseLevel );
		}
		return pairs;
	}

	private void RebuildDesiredChunks(
		Vector3Int center,
		string reason,
		VoxelVisualConfiguration? visualConfiguration = null )
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
			if ( (incremental ? _renderDesiredChunks : _nextRenderDesiredChunks).Contains( coordinate ) ||
				RetainsLod0PlacementCoordinate( coordinate ) )
			{
				_gpuMesher.SetResidency( new GpuMeshRegionKey( 0, coordinate ), GpuMeshResidency.Warm );
			}
			else
			{
				_gpuMesher.Remove( new GpuMeshRegionKey( 0, coordinate ) );
				if ( _renderPreparedChunks.Remove( coordinate ) ) _renderPreparedRevision++;
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
			if ( RetainsLod0PlacementCoordinate( coordinate ) ) continue;
			_gpuMesher.Remove( new GpuMeshRegionKey( 0, coordinate ) );
			if ( _renderPreparedChunks.Remove( coordinate ) ) _renderPreparedRevision++;
		}

		if ( !incremental )
		{
			var previousRenderDesired = _renderDesiredChunks;
			_renderDesiredChunks = _nextRenderDesiredChunks;
			_nextRenderDesiredChunks = previousRenderDesired;
		}
		UpdateClipboxPlacement(
			ActiveStreamingTarget.WorldPosition,
			visualConfiguration ?? _targetVisualConfiguration );
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
				if ( (_renderDesiredChunks.Contains( coordinate ) ||
					RetainsLod0PlacementCoordinate( coordinate )) &&
					!_desiredChunks.Contains( coordinate ) &&
					!_renderPreparedChunks.Contains( coordinate ) && _coordinateSetBuffer.Add( coordinate ) )
				{
					_warmCoordinateBuffer.Add( coordinate );
				}
			}
			if ( _clipboxPlacementPending && _levels[0].PlacementChanged )
			{
				foreach ( var coordinate in _levels[0].NextActive )
				{
					if ( !_desiredChunks.Contains( coordinate ) &&
						!_renderPreparedChunks.Contains( coordinate ) &&
						_coordinateSetBuffer.Add( coordinate ) )
					{
						_warmCoordinateBuffer.Add( coordinate );
					}
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
				if ( !_desiredChunks.Contains( coordinate ) && !_renderPreparedChunks.Contains( coordinate ) &&
					_coordinateSetBuffer.Add( coordinate ) )
				{
					_warmCoordinateBuffer.Add( coordinate );
				}
			}
		}
		foreach ( var coordinate in _levels[0].Active )
		{
			if ( !_desiredChunks.Contains( coordinate ) &&
				!_renderPreparedChunks.Contains( coordinate ) &&
				_coordinateSetBuffer.Add( coordinate ) )
			{
				_warmCoordinateBuffer.Add( coordinate );
			}
		}
		if ( _clipboxPlacementPending && _levels[0].PlacementChanged )
		{
			foreach ( var coordinate in _levels[0].NextActive )
			{
				if ( !_desiredChunks.Contains( coordinate ) &&
					!_renderPreparedChunks.Contains( coordinate ) &&
					_coordinateSetBuffer.Add( coordinate ) )
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

	private void UpdateClipboxPlacement(
		Vector3 viewerPosition,
		VoxelVisualConfiguration visualConfiguration )
	{
		for ( var level = 1; level < SupportedVisualLevelCount; level++ )
		{
			_candidateLevelAnchors[level] = WorldToLevelAnchor( viewerPosition, level );
		}
		_candidateLevelAnchors[0] = _candidateLevelAnchors[1] * 2;

		var configurationChanged = !_clipboxPlacementTargetAvailable ||
			visualConfiguration != _targetVisualConfiguration;
		var targetChanged = configurationChanged || !_clipboxPlacementTargetAvailable;
		for ( var level = 0; level <= visualConfiguration.MaximumVisualLod; level++ )
		{
			targetChanged |= _candidateLevelAnchors[level] != _targetLevelAnchors[level];
		}
		if ( !targetChanged ) return;

		if ( _clipboxPlacementTargetAvailable && HasClipboxPlacementWork )
		{
			_clipboxPlacementSuperseded++;
		}
		if ( configurationChanged )
		{
			_targetVisualConfigurationRevision = ++_requestedVisualConfigurationRevision;
		}
		_targetVisualConfiguration = visualConfiguration;
		Array.Copy( _candidateLevelAnchors, _targetLevelAnchors, SupportedVisualLevelCount );
		_clipboxPlacementTargetAvailable = true;
		_clipboxPlacementRequests++;

		if ( !_levels[0].HasPlacement )
		{
			PrepareLodPlacement();
			return;
		}

		if ( _clipboxPlacementPending && MatchesCommittedPlacementTarget() )
		{
			CancelPendingClipboxPlacement();
		}
		PrepareNextClipboxPlacementStep();
	}

	private bool MatchesCommittedPlacementTarget()
	{
		if ( _targetVisualConfiguration != _appliedVisualConfiguration ) return false;
		for ( var level = 0; level <= _targetVisualConfiguration.MaximumVisualLod; level++ )
		{
			if ( !_levels[level].HasPlacement ||
				_targetLevelAnchors[level] != _levels[level].Anchor ) return false;
		}
		return true;
	}

	private void PrepareNextClipboxPlacementStep()
	{
		if ( !_clipboxPlacementTargetAvailable || _clipboxPlacementPending ||
			!_levels[0].HasPlacement || MatchesCommittedPlacementTarget() ) return;
		PrepareLodPlacement();
		if ( VerboseLogging ) LogLodPlacement( "placement.prepared" );
	}

	private void PrepareLodPlacement()
	{
		if ( _clipboxPlacementPending )
			throw new InvalidOperationException( "A clipbox placement step is already pending." );

		var configuration = _targetVisualConfiguration;
		for ( var level = 0; level < SupportedVisualLevelCount; level++ )
		{
			var state = _levels[level];
			var visualEnabled = level >= configuration.MinimumVisualLod &&
				level <= configuration.MaximumVisualLod;
			var anchor = level <= configuration.MaximumVisualLod
				? _targetLevelAnchors[level]
				: default;
			var outerAnchor = level == 0
				? anchor
				: level < configuration.MaximumVisualLod
					? _targetLevelAnchors[level + 1] * 2
					: anchor;
			var halfExtent = level == 0
				? configuration.Lod0VisualHalfExtent
				: configuration.LodCacheHalfExtent;
			var outerMinimum = visualEnabled
				? outerAnchor - new Vector3Int( halfExtent )
				: default;
			var outerMaximum = visualEnabled
				? outerAnchor + new Vector3Int( halfExtent )
				: default;
			var hasHole = visualEnabled && level > configuration.MinimumVisualLod;
			var holeMinimum = hasHole ? _levels[level - 1].StagedOuterMinimum / 2 : outerAnchor;
			var holeMaximum = hasHole ? _levels[level - 1].StagedOuterMaximum / 2 : outerAnchor;
			state.StagePlacement(
				visualEnabled,
				anchor,
				outerAnchor,
				outerMinimum,
				outerMaximum,
				holeMinimum,
				holeMaximum );
			if ( !state.PlacementChanged )
			{
				state.ClearStagedWork();
				continue;
			}

			state.NextDesiredCache.Clear();
			state.NextActive.Clear();
			if ( visualEnabled )
			{
				AddHalfOpenBox( state.NextDesiredCache, outerMinimum, outerMaximum );
				foreach ( var coordinate in state.NextDesiredCache )
				{
					if ( !hasHole || !IsInsideHalfOpenBox( coordinate, holeMinimum, holeMaximum ) )
					{
						state.NextActive.Add( coordinate );
					}
				}
			}
			CaptureSetDelta( state.DesiredCache, state.NextDesiredCache,
				state.Entering, state.Leaving );
			CaptureSetDelta( state.Active, state.NextActive,
				state.ActiveEntering, state.ActiveLeaving );
		}

		foreach ( var pair in _transitionPairs )
		{
			var enabled = pair.FineLevel >= configuration.MinimumVisualLod &&
				pair.CoarseLevel <= configuration.MaximumVisualLod;
			var coarseState = _levels[pair.CoarseLevel];
			pair.StagePlacement(
				enabled,
				coarseState.StagedHoleMinimum,
				coarseState.StagedHoleMaximum );
			if ( !pair.PlacementChanged )
			{
				pair.ClearStagedWork();
				continue;
			}
			pair.NextDesired.Clear();
			if ( enabled )
			{
				AddTransitionFaces(
					pair.NextDesired,
					pair.FineLevel,
					pair.CoarseLevel,
					coarseState.StagedHoleMinimum,
					coarseState.StagedHoleMaximum );
			}
			CaptureSetDelta( pair.Desired, pair.NextDesired, pair.Entering, pair.Leaving );
		}

		for ( var level = 1; level < SupportedVisualLevelCount; level++ )
		{
			var state = _levels[level];
			var stagedCache = state.PlacementChanged ? state.NextDesiredCache : state.DesiredCache;
			var stagedActive = state.PlacementChanged ? state.NextActive : state.Active;
			state.Readiness.Clear();
			foreach ( var coordinate in stagedCache )
			{
				var descriptor = CreateRegularDescriptor( level, coordinate );
				if ( _gpuMesher.IsResident( descriptor ) ) continue;
				if ( !_gpuMesher.Contains( descriptor ) )
				{
					var classification = ClassifyClipboxRegion( level, coordinate );
					if ( classification != ChunkDensityClassification.PotentiallySurfaceContaining )
					{
						_gpuMesher.PublishKnownEmpty( descriptor, GpuMeshResidency.Visual );
						continue;
					}
				}
				if ( stagedActive.Contains( coordinate ) ) state.Readiness.Add( coordinate );
			}
			SortNearestFirst( state.Entering, state.StagedOuterAnchor );
			foreach ( var coordinate in state.Entering )
			{
				var descriptor = CreateRegularDescriptor( level, coordinate );
				if ( !_gpuMesher.Contains( descriptor ) )
				{
					_gpuMesher.Schedule(
						descriptor,
						_playerFigureEightRouteDistance,
						GpuMeshResidency.Visual );
				}
			}
			SortNearestFirst( state.Readiness, state.StagedOuterAnchor );
			foreach ( var coordinate in state.Readiness )
			{
				var descriptor = CreateRegularDescriptor( level, coordinate );
				if ( !_gpuMesher.Contains( descriptor ) )
				{
					_gpuMesher.Schedule(
						descriptor,
						_playerFigureEightRouteDistance,
						GpuMeshResidency.Visual );
				}
			}
		}

		_transitionScheduleBuffer.Clear();
		foreach ( var pair in _transitionPairs )
		{
			var stagedDesired = pair.PlacementChanged ? pair.NextDesired : pair.Desired;
			pair.Readiness.Clear();
			foreach ( var key in stagedDesired )
			{
				if ( !_gpuMesher.IsTransitionResident( CreateTransitionDescriptor( key ) ) )
				{
					pair.Readiness.Add( key );
					_transitionScheduleBuffer.Add( key );
				}
			}
		}
		SortTransitionsNearestFirst( _transitionScheduleBuffer );
		foreach ( var key in _transitionScheduleBuffer )
		{
			_gpuMesher.ScheduleTransition(
				CreateTransitionDescriptor( key ),
				_playerFigureEightRouteDistance );
		}

		_stagedVisualConfiguration = configuration;
		_stagedVisualConfigurationRevision = _targetVisualConfigurationRevision;
		_lastClipboxReadinessResidentRevision = -1;
		_lastClipboxReadinessRenderPreparedRevision = -1;
		_clipboxPlacementPending = true;
		if ( !_levels[0].HasPlacement ) CommitPendingClipboxPlacement( bootstrap: true, default );
	}

	private void TryCommitPendingClipboxPlacement()
	{
		if ( _gpuMesher is null || !_clipboxPlacementPending ) return;
		var residentRevision = _gpuMesher.ResidentPublicationRevision;
		if ( residentRevision == _lastClipboxReadinessResidentRevision &&
			_renderPreparedRevision == _lastClipboxReadinessRenderPreparedRevision )
		{
			_clipboxPlacementDeferredUpdates++;
			return;
		}
		_lastClipboxReadinessResidentRevision = residentRevision;
		_lastClipboxReadinessRenderPreparedRevision = _renderPreparedRevision;
		var readiness = CapturePendingClipboxReadiness();
		if ( !readiness.IsReady )
		{
			_clipboxPlacementDeferredUpdates++;
			_clipboxPlacementReadinessBlocks++;
			return;
		}
		CommitPendingClipboxPlacement( bootstrap: false, readiness );
	}

	private void CommitPendingClipboxPlacement(
		bool bootstrap,
		PendingClipboxReadiness readiness )
	{
		if ( !_clipboxPlacementPending ) return;
		if ( !bootstrap && !readiness.IsReady )
		{
			_clipboxPlacementUnsafeCommits++;
			Log.Error(
				$"[VoxelWorld] lod.handoff.rejected missingLevels=[{string.Join( ',', readiness.MissingLevels )}] " +
				$"missingTransitions={readiness.MissingTransitions}" );
			return;
		}

		_transitionRetainedBuffer.Clear();
		if ( VerboseLogging )
		{
			foreach ( var pair in _transitionPairs )
			{
				var stagedDesired = pair.PlacementChanged ? pair.NextDesired : pair.Desired;
				foreach ( var key in stagedDesired )
				{
					if ( pair.Desired.Contains( key ) ) _transitionRetainedBuffer.Add( key );
				}
			}
		}
		var retainedBefore = VerboseLogging
			? _gpuMesher.CaptureTransitionIdentity( _transitionRetainedBuffer )
			: default;

		foreach ( var state in _levels )
		{
			foreach ( var coordinate in state.ActiveLeaving )
			{
				_gpuMesher.SetRenderActive( new GpuMeshRegionKey( state.Level, coordinate ), false );
			}
			foreach ( var coordinate in state.ActiveEntering )
			{
				_gpuMesher.SetRenderActive( new GpuMeshRegionKey( state.Level, coordinate ), true );
			}
		}
		foreach ( var pair in _transitionPairs )
		{
			foreach ( var key in pair.Leaving ) _gpuMesher.SetTransitionActive( key, false );
			foreach ( var key in pair.Entering ) _gpuMesher.SetTransitionActive( key, true );
		}

		foreach ( var state in _levels )
		{
			foreach ( var coordinate in state.Leaving )
			{
				if ( state.Level == 0 && _renderDesiredChunks.Contains( coordinate ) ) continue;
				_gpuMesher.Remove( new GpuMeshRegionKey( state.Level, coordinate ) );
				if ( state.Level == 0 && _renderPreparedChunks.Remove( coordinate ) )
				{
					_renderPreparedRevision++;
				}
			}
		}
		foreach ( var pair in _transitionPairs )
		{
			foreach ( var key in pair.Leaving ) _gpuMesher.RemoveTransition( key );
		}

		foreach ( var state in _levels )
		{
			if ( state.PlacementChanged ) state.CommitPlacement();
		}
		foreach ( var pair in _transitionPairs )
		{
			if ( pair.PlacementChanged ) pair.CommitPlacement();
		}
		_appliedVisualConfiguration = _stagedVisualConfiguration;
		_appliedVisualConfigurationRevision = _stagedVisualConfigurationRevision;
		_clipboxPlacementPending = false;
		_clipboxPlacementCommits++;

		if ( VerboseLogging )
		{
			var retainedAfter = _gpuMesher.CaptureTransitionIdentity( _transitionRetainedBuffer );
			Log.Info(
				$"[VoxelWorld] lod.handoff.committed bootstrap={bootstrap} " +
				$"visualRevision={_appliedVisualConfigurationRevision} " +
				$"levels={string.Join( ';', _levels.Select( state =>
					$"{state.Level}:{FormatRegionCoordinate( state.Anchor )}/{FormatRegionCoordinate( state.OuterAnchor )}" ) )} " +
				$"transitionEntered={_transitionPairs.Sum( pair => pair.LastEntered )} " +
				$"transitionLeft={_transitionPairs.Sum( pair => pair.LastLeft )} " +
				$"transitionRetained={_transitionPairs.Sum( pair => pair.LastRetained )} " +
				$"identityBefore={retainedBefore.Digest:X16} identityAfter={retainedAfter.Digest:X16} " +
				$"identityPreserved={retainedBefore == retainedAfter}" );
		}
		PrepareNextClipboxPlacementStep();
	}

	private void CancelPendingClipboxPlacement()
	{
		if ( !_clipboxPlacementPending ) return;
		foreach ( var state in _levels )
		{
			foreach ( var coordinate in state.Entering )
			{
				if ( state.Level == 0 && _renderDesiredChunks.Contains( coordinate ) ) continue;
				_gpuMesher.Remove( new GpuMeshRegionKey( state.Level, coordinate ) );
				if ( state.Level == 0 && _renderPreparedChunks.Remove( coordinate ) )
				{
					_renderPreparedRevision++;
				}
			}
		}
		foreach ( var pair in _transitionPairs )
		{
			foreach ( var key in pair.Entering ) _gpuMesher.RemoveTransition( key );
		}
		foreach ( var state in _levels ) state.CancelStagedPlacement();
		foreach ( var pair in _transitionPairs ) pair.CancelStagedPlacement();
		_stagedVisualConfiguration = _appliedVisualConfiguration;
		_stagedVisualConfigurationRevision = _appliedVisualConfigurationRevision;
		_clipboxPlacementPending = false;
		_clipboxPlacementSuperseded++;
	}

	private PendingClipboxReadiness CapturePendingClipboxReadiness()
	{
		if ( !_clipboxPlacementPending || _gpuMesher is null ) return default;
		Array.Clear( _pendingMissingByLevel );
		var missingTransitions = 0;
		foreach ( var coordinate in _levels[0].Entering )
		{
			if ( !_renderPreparedChunks.Contains( coordinate ) )
			{
				_pendingMissingByLevel[0]++;
				continue;
			}
			var descriptor = CreateRegularDescriptor( 0, coordinate );
			if ( _gpuMesher.Contains( descriptor ) && !_gpuMesher.IsResident( descriptor ) )
			{
				_pendingMissingByLevel[0]++;
			}
		}
		for ( var level = 1; level < SupportedVisualLevelCount; level++ )
		{
			foreach ( var coordinate in _levels[level].Readiness )
			{
				if ( !_gpuMesher.IsResident( CreateRegularDescriptor( level, coordinate ) ) )
				{
					_pendingMissingByLevel[level]++;
				}
			}
		}
		foreach ( var pair in _transitionPairs )
		{
			foreach ( var key in pair.Readiness )
			{
				if ( !_gpuMesher.IsTransitionResident( CreateTransitionDescriptor( key ) ) )
				{
					missingTransitions++;
				}
			}
		}
		return new PendingClipboxReadiness( _pendingMissingByLevel, missingTransitions );
	}

	private GpuSdfDescriptor CreateRegularDescriptor( int level, Vector3Int coordinate ) => new(
			new GpuMeshRegionKey( level, coordinate ),
			_appliedCellsPerAxis,
			CellSizeForLevel( level ),
			CurrentTerrainSettings,
		ProceduralTerrainSdf.CurrentVersion,
		_terrainContentRevision );

	private ChunkDensityClassification ClassifyClipboxRegion( int level, Vector3Int coordinate )
	{
		if ( level <= 0 || level >= SupportedVisualLevelCount )
		{
			throw new ArgumentOutOfRangeException( nameof(level), level,
				"Coarse clipbox classification requires a supported level above zero." );
		}
		var start = Stopwatch.GetTimestamp();
		var classification = VoxelChunk.ClassifyDensityRangeBroadPhase(
			coordinate,
			_appliedCellsPerAxis,
			CellSizeForLevel( level ),
			CurrentTerrainSettings );
		var milliseconds = (float)Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		_clipboxClassificationMilliseconds += milliseconds;
		_performanceClipboxMaximumClassificationMilliseconds = Math.Max(
			_performanceClipboxMaximumClassificationMilliseconds,
			milliseconds );

		_clipboxClassificationQueries[level]++;
		if ( classification == ChunkDensityClassification.DefinitelySolid )
			_clipboxRejectedSolid[level]++;
		else if ( classification == ChunkDensityClassification.DefinitelyAir )
			_clipboxRejectedAir[level]++;
		return classification;
	}

	private GpuTransitionDescriptor CreateTransitionDescriptor( GpuTransitionKey key ) => new(
		key,
		_appliedCellsPerAxis,
		CellSizeForLevel( key.FineLevel ),
		CellSizeForLevel( key.CoarseLevel ),
		CurrentTerrainSettings,
		ProceduralTerrainSdf.CurrentVersion,
		_terrainContentRevision );

	private bool RetainsLod0PlacementCoordinate( Vector3Int coordinate ) =>
		_levels[0].Active.Contains( coordinate ) ||
		(_clipboxPlacementPending && _levels[0].PlacementChanged &&
			_levels[0].NextActive.Contains( coordinate ));

	private bool HasClipboxPlacementWork =>
		_clipboxPlacementPending ||
		(_clipboxPlacementTargetAvailable && _levels[0].HasPlacement &&
			!MatchesCommittedPlacementTarget());

	private static int MaximumAxisDistance( Vector3Int first, Vector3Int second )
	{
		var delta = second - first;
		return Math.Max( Math.Abs( delta.x ), Math.Max( Math.Abs( delta.y ), Math.Abs( delta.z ) ) );
	}

	private static void AddTransitionFaces(
		HashSet<GpuTransitionKey> keys,
		int fineLevel,
		int coarseLevel,
		Vector3Int minimum,
		Vector3Int maximum )
	{
		for ( var z = minimum.z; z < maximum.z; z++ )
		for ( var y = minimum.y; y < maximum.y; y++ )
		{
			keys.Add( new GpuTransitionKey( fineLevel, coarseLevel,
				new Vector3Int( minimum.x - 1, y, z ), GpuTransitionFace.PositiveX ) );
			keys.Add( new GpuTransitionKey( fineLevel, coarseLevel,
				new Vector3Int( maximum.x, y, z ), GpuTransitionFace.NegativeX ) );
		}
		for ( var z = minimum.z; z < maximum.z; z++ )
		for ( var x = minimum.x; x < maximum.x; x++ )
		{
			keys.Add( new GpuTransitionKey( fineLevel, coarseLevel,
				new Vector3Int( x, minimum.y - 1, z ), GpuTransitionFace.PositiveY ) );
			keys.Add( new GpuTransitionKey( fineLevel, coarseLevel,
				new Vector3Int( x, maximum.y, z ), GpuTransitionFace.NegativeY ) );
		}
		for ( var y = minimum.y; y < maximum.y; y++ )
		for ( var x = minimum.x; x < maximum.x; x++ )
		{
			keys.Add( new GpuTransitionKey( fineLevel, coarseLevel,
				new Vector3Int( x, y, minimum.z - 1 ), GpuTransitionFace.PositiveZ ) );
			keys.Add( new GpuTransitionKey( fineLevel, coarseLevel,
				new Vector3Int( x, y, maximum.z ), GpuTransitionFace.NegativeZ ) );
		}
	}

	private static bool IsAdjacent( Vector3Int first, Vector3Int second ) =>
		IsAdjacentAtMost( first, second, 1 );

	private static bool IsAdjacentAtMost( Vector3Int first, Vector3Int second, int maximumDelta )
	{
		var delta = second - first;
		return Math.Abs( delta.x ) <= maximumDelta && Math.Abs( delta.y ) <= maximumDelta &&
			Math.Abs( delta.z ) <= maximumDelta;
	}

	private void SortTransitionsNearestFirst( List<GpuTransitionKey> keys )
	{
		keys.Sort( ( left, right ) =>
		{
			var comparison = left.CoarseLevel.CompareTo( right.CoarseLevel );
			if ( comparison != 0 ) return comparison;
			var state = _levels[left.CoarseLevel];
			var center = (state.StagedHoleMinimum + state.StagedHoleMaximum) / 2;
			var leftCoordinate = left.CoarseCoordinate;
			var rightCoordinate = right.CoarseCoordinate;
			var leftDistance = Math.Abs( leftCoordinate.x - center.x ) +
				Math.Abs( leftCoordinate.y - center.y ) + Math.Abs( leftCoordinate.z - center.z );
			var rightDistance = Math.Abs( rightCoordinate.x - center.x ) +
				Math.Abs( rightCoordinate.y - center.y ) + Math.Abs( rightCoordinate.z - center.z );
			comparison = leftDistance.CompareTo( rightDistance );
			if ( comparison != 0 ) return comparison;
			comparison = leftCoordinate.z.CompareTo( rightCoordinate.z );
			if ( comparison != 0 ) return comparison;
			comparison = leftCoordinate.y.CompareTo( rightCoordinate.y );
			if ( comparison != 0 ) return comparison;
			comparison = leftCoordinate.x.CompareTo( rightCoordinate.x );
			return comparison != 0 ? comparison : left.Face.CompareTo( right.Face );
		} );
	}

	private static void CaptureSetDelta<T>(
		HashSet<T> current,
		HashSet<T> next,
		List<T> entering,
		List<T> leaving )
	{
		entering.Clear();
		leaving.Clear();
		foreach ( var value in current )
		{
			if ( !next.Contains( value ) ) leaving.Add( value );
		}
		foreach ( var value in next )
		{
			if ( !current.Contains( value ) ) entering.Add( value );
		}
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
			_appliedCellsPerAxis,
			_appliedCellSize,
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
			_appliedCellsPerAxis,
			_appliedCellSize,
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
				if ( _renderPreparedChunks.Add( chunk.Coordinate ) ) _renderPreparedRevision++;
				_gpuMesher.Schedule(
					chunk,
					_terrainContentRevision,
					_playerFigureEightRouteDistance,
					GpuMeshResidency.Gameplay );
				_gpuMesher.SetRenderActive(
					new GpuMeshRegionKey( 0, chunk.Coordinate ),
					_levels[0].Active.Contains( chunk.Coordinate ) );
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

			if ( _renderDesiredChunks.Contains( result.Coordinate ) ||
				RetainsLod0PlacementCoordinate( result.Coordinate ) )
			{
				if ( _renderPreparedChunks.Add( result.Coordinate ) ) _renderPreparedRevision++;
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
						new GpuMeshRegionKey( 0, result.Coordinate ),
						_levels[0].Active.Contains( result.Coordinate ) );
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
			$"Simplex noodle-and-cheese caves v{ProceduralTerrainSdf.CurrentVersion}; seed {_appliedWorldSeed}; " +
			$"base {_appliedSurfaceBaseHeight:0.##}, f {_appliedSurfaceFrequency:0.######}, " +
			$"amplitude {_appliedSurfaceAmplitude:0.##}";
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

internal readonly record struct VoxelVisualConfiguration(
	int MinimumVisualLod,
	int MaximumVisualLod,
	int Lod0VisualHalfExtent,
	int LodCacheHalfExtent );

internal readonly record struct PendingClipboxReadiness(
	IReadOnlyList<int> MissingLevels,
	int MissingTransitions )
{
	public bool IsReady => MissingTransitions == 0 &&
		(MissingLevels is null || MissingLevels.All( value => value == 0 ));
}

internal sealed class TerrainClipboxLevelState
{
	public int Level { get; }
	public bool HasPlacement { get; private set; }
	public bool PlacementChanged { get; private set; }
	public bool VisualEnabled { get; private set; }
	public Vector3Int Anchor { get; private set; }
	public Vector3Int OuterAnchor { get; private set; }
	public Vector3Int OuterMinimum { get; private set; }
	public Vector3Int OuterMaximum { get; private set; }
	public Vector3Int HoleMinimum { get; private set; }
	public Vector3Int HoleMaximum { get; private set; }
	public bool StagedVisualEnabled { get; private set; }
	public Vector3Int StagedAnchor { get; private set; }
	public Vector3Int StagedOuterAnchor { get; private set; }
	public Vector3Int StagedOuterMinimum { get; private set; }
	public Vector3Int StagedOuterMaximum { get; private set; }
	public Vector3Int StagedHoleMinimum { get; private set; }
	public Vector3Int StagedHoleMaximum { get; private set; }
	public HashSet<Vector3Int> DesiredCache { get; private set; } = new();
	public HashSet<Vector3Int> Active { get; private set; } = new();
	public HashSet<Vector3Int> NextDesiredCache { get; private set; } = new();
	public HashSet<Vector3Int> NextActive { get; private set; } = new();
	public List<Vector3Int> Entering { get; } = new();
	public List<Vector3Int> Leaving { get; } = new();
	public List<Vector3Int> ActiveEntering { get; } = new();
	public List<Vector3Int> ActiveLeaving { get; } = new();
	public List<Vector3Int> Readiness { get; } = new();
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

	public TerrainClipboxLevelState( int level )
	{
		Level = level;
	}

	public void StagePlacement(
		bool visualEnabled,
		Vector3Int anchor,
		Vector3Int outerAnchor,
		Vector3Int outerMinimum,
		Vector3Int outerMaximum,
		Vector3Int holeMinimum,
		Vector3Int holeMaximum )
	{
		PlacementChanged = !HasPlacement ||
			VisualEnabled != visualEnabled ||
			Anchor != anchor ||
			OuterAnchor != outerAnchor ||
			OuterMinimum != outerMinimum ||
			OuterMaximum != outerMaximum ||
			HoleMinimum != holeMinimum ||
			HoleMaximum != holeMaximum;
		StagedVisualEnabled = visualEnabled;
		StagedAnchor = anchor;
		StagedOuterAnchor = outerAnchor;
		StagedOuterMinimum = outerMinimum;
		StagedOuterMaximum = outerMaximum;
		StagedHoleMinimum = holeMinimum;
		StagedHoleMaximum = holeMaximum;
	}

	public void ClearStagedWork()
	{
		Entering.Clear();
		Leaving.Clear();
		ActiveEntering.Clear();
		ActiveLeaving.Clear();
		Readiness.Clear();
	}

	public void CancelStagedPlacement()
	{
		PlacementChanged = false;
		StagedVisualEnabled = VisualEnabled;
		StagedAnchor = Anchor;
		StagedOuterAnchor = OuterAnchor;
		StagedOuterMinimum = OuterMinimum;
		StagedOuterMaximum = OuterMaximum;
		StagedHoleMinimum = HoleMinimum;
		StagedHoleMaximum = HoleMaximum;
		ClearStagedWork();
	}

	public void CommitPlacement()
	{
		var incremental = HasPlacement && VisualEnabled == StagedVisualEnabled &&
			IsAdjacentAtMost( Anchor, StagedAnchor, 1 ) &&
			IsAdjacentAtMost( OuterAnchor, StagedOuterAnchor, Level == 0 ? 2 : 1 );
		HasPlacement = true;
		VisualEnabled = StagedVisualEnabled;
		Anchor = StagedAnchor;
		OuterAnchor = StagedOuterAnchor;
		OuterMinimum = StagedOuterMinimum;
		OuterMaximum = StagedOuterMaximum;
		HoleMinimum = StagedHoleMinimum;
		HoleMaximum = StagedHoleMaximum;
		LastEnteredRegions = Entering.Count;
		LastLeftRegions = Leaving.Count;
		LastActivatedRegions = ActiveEntering.Count;
		LastDeactivatedRegions = ActiveLeaving.Count;
		EnteredRegions += Entering.Count;
		LeftRegions += Leaving.Count;
		ActivatedRegions += ActiveEntering.Count;
		DeactivatedRegions += ActiveLeaving.Count;
		if ( incremental ) IncrementalUpdates++;
		else FullUpdates++;

		var previousDesiredCache = DesiredCache;
		DesiredCache = NextDesiredCache;
		NextDesiredCache = previousDesiredCache;
		var previousActive = Active;
		Active = NextActive;
		NextActive = previousActive;
		PlacementChanged = false;
	}

	public void Clear()
	{
		HasPlacement = false;
		PlacementChanged = false;
		VisualEnabled = false;
		Anchor = default;
		OuterAnchor = default;
		OuterMinimum = default;
		OuterMaximum = default;
		HoleMinimum = default;
		HoleMaximum = default;
		StagedVisualEnabled = false;
		StagedAnchor = default;
		StagedOuterAnchor = default;
		StagedOuterMinimum = default;
		StagedOuterMaximum = default;
		StagedHoleMinimum = default;
		StagedHoleMaximum = default;
		DesiredCache.Clear();
		Active.Clear();
		NextDesiredCache.Clear();
		NextActive.Clear();
		Entering.Clear();
		Leaving.Clear();
		ActiveEntering.Clear();
		ActiveLeaving.Clear();
		Readiness.Clear();
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

	private static bool IsAdjacentAtMost(
		Vector3Int first,
		Vector3Int second,
		int maximumDelta )
	{
		var delta = second - first;
		return Math.Abs( delta.x ) <= maximumDelta &&
			Math.Abs( delta.y ) <= maximumDelta &&
			Math.Abs( delta.z ) <= maximumDelta;
	}
}

internal sealed class TerrainTransitionPairState
{
	public int FineLevel { get; }
	public int CoarseLevel { get; }
	public bool HasPlacement { get; private set; }
	public bool Enabled { get; private set; }
	public bool PlacementChanged { get; private set; }
	public Vector3Int HoleMinimum { get; private set; }
	public Vector3Int HoleMaximum { get; private set; }
	public bool StagedEnabled { get; private set; }
	public Vector3Int StagedHoleMinimum { get; private set; }
	public Vector3Int StagedHoleMaximum { get; private set; }
	public HashSet<GpuTransitionKey> Desired { get; private set; } = new();
	public HashSet<GpuTransitionKey> NextDesired { get; private set; } = new();
	public List<GpuTransitionKey> Entering { get; } = new();
	public List<GpuTransitionKey> Leaving { get; } = new();
	public List<GpuTransitionKey> Readiness { get; } = new();
	public int LastEntered { get; private set; }
	public int LastLeft { get; private set; }
	public int LastRetained { get; private set; }
	public long Entered { get; private set; }
	public long Left { get; private set; }

	public TerrainTransitionPairState( int fineLevel, int coarseLevel )
	{
		FineLevel = fineLevel;
		CoarseLevel = coarseLevel;
	}

	public void StagePlacement( bool enabled, Vector3Int holeMinimum, Vector3Int holeMaximum )
	{
		PlacementChanged = !HasPlacement ||
			Enabled != enabled ||
			HoleMinimum != holeMinimum ||
			HoleMaximum != holeMaximum;
		StagedEnabled = enabled;
		StagedHoleMinimum = holeMinimum;
		StagedHoleMaximum = holeMaximum;
	}

	public void ClearStagedWork()
	{
		Entering.Clear();
		Leaving.Clear();
		Readiness.Clear();
	}

	public void CancelStagedPlacement()
	{
		PlacementChanged = false;
		StagedEnabled = Enabled;
		StagedHoleMinimum = HoleMinimum;
		StagedHoleMaximum = HoleMaximum;
		ClearStagedWork();
	}

	public void CommitPlacement()
	{
		HasPlacement = true;
		Enabled = StagedEnabled;
		HoleMinimum = StagedHoleMinimum;
		HoleMaximum = StagedHoleMaximum;
		LastEntered = Entering.Count;
		LastLeft = Leaving.Count;
		LastRetained = NextDesired.Count - LastEntered;
		Entered += LastEntered;
		Left += LastLeft;
		var previousDesired = Desired;
		Desired = NextDesired;
		NextDesired = previousDesired;
		PlacementChanged = false;
	}

	public void Clear()
	{
		Desired.Clear();
		NextDesired.Clear();
		Entering.Clear();
		Leaving.Clear();
		Readiness.Clear();
		HasPlacement = false;
		Enabled = false;
		PlacementChanged = false;
		HoleMinimum = default;
		HoleMaximum = default;
		StagedEnabled = false;
		StagedHoleMinimum = default;
		StagedHoleMaximum = default;
		LastEntered = 0;
		LastLeft = 0;
		LastRetained = 0;
		Entered = 0;
		Left = 0;
	}
}
