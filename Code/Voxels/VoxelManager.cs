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
	private const int PerformanceResultSchemaVersion = 13;
	private const int GenerationBatchSize = 256;
	private const string PerformanceResultsDirectory = "performance";
	private const string PerformanceResultsPath = "performance/results-v1.jsonl";
	private const string InspectorPerformanceTask = "PERFORMANCE-OVERVIEW-001/v4";
	private const string InspectorPerformanceRevision = "manual-inspector";
	private static readonly JsonSerializerOptions PerformanceJsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly Dictionary<Vector3Int, VoxelChunk> _loadedChunks = new();
	private readonly HashSet<Vector3Int> _desiredChunks = new();
	private readonly Queue<Vector3Int> _pendingChunks = new();
	private readonly Queue<VoxelChunk> _completedChunks = new();
	private readonly List<Vector3Int> _coordinateBuffer = new();
	private readonly HashSet<Vector3Int> _coordinateSetBuffer = new();
	private VoxelClipBoxSelection _activeClipSelection;
	private VoxelClipBoxSelection _pendingClipSelection;
	private int _pendingClipMinimumLod;
	private int _clipPlacementRevision;
	private int _clipFallbackFrames;
	private int _clipCoverageMismatches;
	private int _clipAdjacencyViolations;
	private int _clipSelectionBudgetViolations;
	private int _clipIntegrationBudgetViolations;
	private float _maximumClipSelectionMilliseconds;
	private float _maximumClipIntegrationMilliseconds;
	private readonly float[] _performanceFrameMilliseconds = new float[MaximumPerformanceFrameSamples];
	private readonly float[] _sortedPerformanceFrameMilliseconds = new float[MaximumPerformanceFrameSamples];
	private readonly float[] _performanceGpuMilliseconds = new float[MaximumPerformanceFrameSamples];
	private readonly float[] _sortedPerformanceGpuMilliseconds = new float[MaximumPerformanceFrameSamples];
	private GpuVoxelMesher _gpuMesher;

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
	private int _appliedViewRadiusChunks;
	private int _appliedFullDetailRadiusChunks;
	private int _appliedWorldSeed;
	private float _appliedSurfaceBaseHeight;
	private float _appliedSurfaceFrequency;
	private float _appliedSurfaceAmplitude;
	private int _streamRevision;
	private int _terrainContentRevision;
	private bool _workerCompleted;
	private CancellationTokenSource _generationCancellation;
	private System.Threading.Tasks.Task _generationTask = System.Threading.Tasks.Task.CompletedTask;
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

	[Property, Category( "Chunk Configuration" ), Range( 4, 128 )]
	public int ViewRadiusChunks { get; set; } = 16;

	[Property, Category( "Chunk Configuration" ), Range( 2, 128 )]
	public int FullDetailRadiusChunks { get; set; } = 4;

	[Property, ReadOnly, Category( "Chunk Configuration" )]
	public int EffectiveMaximumLod { get; private set; } = 2;

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

	[Property, Category( "Diagnostics" )]
	public bool DrawClipBoxes { get; set; } = false;

	[Property, ReadOnly, Category( "World Status" )]
	public string ClipBoxStatus { get; private set; } = "Not initialized";

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
			_gpuMesher.ProcessPending( GpuVoxelMesher.MaximumDispatchesPerUpdate );
			TryAdvanceClipCoverage();
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
				_streamRevision++;
				_gpuMesher?.Clear();
				_loadedChunks.Clear();
				_desiredChunks.Clear();
				_pendingChunks.Clear();
				_completedChunks.Clear();
				_activeClipSelection = null;
				_pendingClipSelection = null;
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
			if ( ViewRadiusChunks != _appliedViewRadiusChunks ||
				FullDetailRadiusChunks != _appliedFullDetailRadiusChunks )
			{
				_appliedViewRadiusChunks = ViewRadiusChunks;
				_appliedFullDetailRadiusChunks = FullDetailRadiusChunks;
				var targetPosition = ActiveStreamingTarget.WorldPosition;
				RebuildDesiredChunks( WorldToChunkCoordinate( targetPosition ), "clip-box radius changed" );
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
		}

		if ( IntegrateCompletedChunks() )
		{
			RefreshReadableStatus();
		}
		else
		{
			RefreshPlayerChunkStatus();
		}
		var meshDispatches = _gpuMesher.ProcessPending( GpuVoxelMesher.MaximumDispatchesPerUpdate );
		TryAdvanceClipCoverage();
		if ( _playerFigureEightTestRunning )
		{
			_performancePeakMeshDispatchesPerUpdate = Math.Max(
				_performancePeakMeshDispatchesPerUpdate,
				meshDispatches );
		}
		TryCompletePlayerFigureEightTest();
		TrySaveCompletedPerformanceTest();
		DrawClipBoxOverlay();
	}

	private void DrawClipBoxOverlay()
	{
		if ( !DrawClipBoxes ) return;
		var selection = _pendingClipSelection ?? _activeClipSelection;
		if ( selection is null ) return;
		var colors = new[]
		{
			Color.Red, Color.Orange, Color.Yellow, Color.Green,
			Color.Cyan, Color.Blue, Color.Magenta
		};
		foreach ( var bounds in selection.Boxes )
		{
			var extent = CellsPerAxis * CellSize * (1 << bounds.Lod);
			var minimum = new Vector3(
				bounds.Minimum.x * extent,
				bounds.Minimum.y * extent,
				bounds.Minimum.z * extent );
			var maximum = new Vector3(
				bounds.Maximum.x * extent,
				bounds.Maximum.y * extent,
				bounds.Maximum.z * extent );
			DebugOverlay.Box( new BBox( minimum, maximum ), colors[bounds.Lod % colors.Length], 0f );
		}
	}

	protected override void OnDestroy()
	{
		_playerFigureEightEnabled = false;
		_playerFigureEightTarget = null;
		_playerFigureEightBody = null;
		_playerFigureEightTestRunning = false;
		_playerFigureEightTestCompletionReady = false;
		_generationCancellation?.Cancel();
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
		_clipFallbackFrames = 0;
		_clipCoverageMismatches = 0;
		_clipAdjacencyViolations = 0;
		_clipSelectionBudgetViolations = 0;
		_clipIntegrationBudgetViolations = 0;
		_maximumClipSelectionMilliseconds = 0f;
		_maximumClipIntegrationMilliseconds = 0f;
		_gpuMesher?.BeginClipMeasurement();
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
				ViewRadiusChunks = ViewRadiusChunks,
				FullDetailRadiusChunks = FullDetailRadiusChunks,
				EffectiveMaximumLod = EffectiveMaximumLod,
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
				Pending = _gpuMesher?.PendingCount ?? 0,
				GameplayPending = _gpuMesher?.PendingGameplayCount ?? 0,
				WarmPending = _gpuMesher?.PendingWarmCount ?? 0,
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
				GeometryReadbacks = GpuVoxelMesher.GeometryReadbackCount,
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
			ClipBoxes = CreateClipBoxMetrics( _lastPerformanceVisibility ),
			Profiler = _lastPerformanceProfiler
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
			if ( _pendingClipSelection is not null || _gpuMesher.PendingCount > 0 )
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

	private PerformanceClipBoxMetrics CreateClipBoxMetrics( GpuVisibilityMeasurement visibility )
	{
		var selection = _activeClipSelection ?? _pendingClipSelection;
		var captured = _gpuMesher?.CaptureClipLevelMeasurements( selection ) ??
			Array.Empty<GpuClipLevelMeasurement>();
		var levels = new PerformanceClipLevelMetrics[captured.Length];
		for ( var index = 0; index < captured.Length; index++ )
		{
			var source = captured[index];
			levels[index] = new PerformanceClipLevelMetrics
			{
				Lod = source.Lod,
				DesiredRegular = source.DesiredRegular,
				ResidentRegular = source.ResidentRegular,
				ActiveRegular = source.ActiveRegular,
				FallbackRegular = source.FallbackRegular,
				DesiredTransitions = source.DesiredTransitions,
				ResidentTransitions = source.ResidentTransitions,
				ActiveTransitions = source.ActiveTransitions,
				RegularTriangles = source.RegularTriangles,
				TransitionTriangles = source.TransitionTriangles,
				RegularBytes = source.RegularBytes,
				TransitionBytes = source.TransitionBytes,
				TopologyDigest = source.TopologyDigest.ToString( "X16" ),
				PositionDigest = source.PositionDigest.ToString( "X16" )
			};
		}
		return new PerformanceClipBoxMetrics
		{
			MaximumLod = selection?.MaximumLod ?? 0,
			ResidentRegular = selection?.ResidentRegularCount ?? 0,
			ActiveRegular = selection?.ActiveRegularCount ?? 0,
			LogicalTransitionFaces = selection?.LogicalTransitionFaceCount ?? 0,
			FallbackFrames = _clipFallbackFrames,
			CoverageMismatches = _clipCoverageMismatches,
			TransitionMaskMismatches = _gpuMesher?.CountPublishedTransitionMaskMismatches() ?? 0,
			AdjacencyViolations = _clipAdjacencyViolations,
			StalePublications = _gpuMesher?.StalePublicationCount ?? 0,
			SelectionBudgetViolations = _clipSelectionBudgetViolations,
			MaximumSelectionMilliseconds = _maximumClipSelectionMilliseconds,
			IntegrationBudgetViolations = _clipIntegrationBudgetViolations,
			MaximumIntegrationMilliseconds = _maximumClipIntegrationMilliseconds,
			ArenaSubmissions = _gpuMesher?.TerrainIndirectApiSubmissionCount ?? 0,
			Levels = levels
		};
	}

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
		if ( CellsPerAxis < 4 || CellsPerAxis > 64 || (CellsPerAxis & 1) != 0 )
		{
			error = "Cells Per Axis must be even and between 4 and 64.";
			return false;
		}

		if ( !float.IsFinite( CellSize ) || CellSize < 1f || CellSize > 128f )
		{
			error = "Cell Size must be finite and between 1 and 128 world units.";
			return false;
		}

		if ( ViewRadiusChunks < 4 || ViewRadiusChunks > 128 )
		{
			error = "View Radius Chunks must be between 4 and 128.";
			return false;
		}

		if ( FullDetailRadiusChunks < 2 || FullDetailRadiusChunks > ViewRadiusChunks ||
			(FullDetailRadiusChunks & 1) != 0 )
		{
			error = "Full Detail Radius Chunks must be positive, even, and no greater than the view radius.";
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
		_appliedViewRadiusChunks = ViewRadiusChunks;
		_appliedFullDetailRadiusChunks = FullDetailRadiusChunks;
		EffectiveMaximumLod = VoxelClipBoxSelection.CalculateMaximumLod(
			FullDetailRadiusChunks, ViewRadiusChunks );
		_appliedWorldSeed = WorldSeed;
		_appliedSurfaceBaseHeight = SurfaceBaseHeight;
		_appliedSurfaceFrequency = SurfaceFrequency;
		_appliedSurfaceAmplitude = SurfaceAmplitude;

		_generationCancellation?.Cancel();
		_streamRevision++;
		_terrainContentRevision++;
		_gpuMesher.Reset( CellsPerAxis );
		_loadedChunks.Clear();
		_desiredChunks.Clear();
		_pendingChunks.Clear();
		_completedChunks.Clear();
		_activeClipSelection = null;
		_pendingClipSelection = null;
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

	private void RebuildDesiredChunks( Vector3Int center, string reason )
	{
		using var profiler = global::Sandbox.Diagnostics.Performance.Scope(
			VoxelPerformanceProfiler.RebuildDesiredChunks );
		var synchronousStart = Stopwatch.GetTimestamp();
		var previousCenter = _streamingCenterCoordinate;
		var hadPreviousCenter = _hasStreamingCenter;
		var selectionStart = Stopwatch.GetTimestamp();
		var selection = VoxelClipBoxSelection.Build(
			center,
			FullDetailRadiusChunks,
			ViewRadiusChunks );
		var selectionMilliseconds =
			(float)Stopwatch.GetElapsedTime( selectionStart ).TotalMilliseconds;
		_maximumClipSelectionMilliseconds = Math.Max(
			_maximumClipSelectionMilliseconds,
			selectionMilliseconds );
		if ( selectionMilliseconds > MainThreadIntegrationBudgetMilliseconds )
		{
			_clipSelectionBudgetViolations++;
			Log.Warning(
				$"[VoxelWorld] clip.selection.over_budget milliseconds={selectionMilliseconds:0.0000} " +
				$"budget={MainThreadIntegrationBudgetMilliseconds:0.0000}" );
		}
		EffectiveMaximumLod = selection.MaximumLod;
		_streamingCenterCoordinate = center;
		_hasStreamingCenter = true;

		var comparedSelection = _pendingClipSelection ?? _activeClipSelection;
		if ( comparedSelection is not null && selection.PlacementEquals( comparedSelection ) )
		{
			RefreshReadableStatus();
			return;
		}

		var previousPending = _pendingClipSelection;
		_pendingClipSelection = selection;
		var delta = hadPreviousCenter ? center - previousCenter : Vector3Int.Zero;
		var adjacent = _activeClipSelection is not null &&
			Math.Abs( delta.x ) <= 1 && Math.Abs( delta.y ) <= 1 && Math.Abs( delta.z ) <= 1;
		_pendingClipMinimumLod = adjacent ? 0 : selection.MaximumLod;
		var placementRevision = ++_clipPlacementRevision;

		if ( previousPending is not null )
		{
			var retained = new HashSet<VoxelRenderRegionKey>( selection.ResidentRegular );
			retained.UnionWith( selection.TransitionFaces );
			if ( _activeClipSelection is not null )
			{
				retained.UnionWith( _activeClipSelection.ResidentRegular );
				retained.UnionWith( _activeClipSelection.TransitionFaces );
			}
			foreach ( var key in previousPending.ResidentRegular )
			{
				if ( !retained.Contains( key ) ) _gpuMesher.Remove( key );
			}
			foreach ( var key in previousPending.TransitionFaces )
			{
				if ( !retained.Contains( key ) ) _gpuMesher.Remove( key );
			}
		}
		_gpuMesher.PrepareClipCoverage( selection, placementRevision );

		var desiredStart = Stopwatch.GetTimestamp();
		_desiredChunks.Clear();
		var lod0Bounds = selection.Boxes[0];
		for ( var z = lod0Bounds.Minimum.z; z < lod0Bounds.Maximum.z; z++ )
		{
			for ( var y = lod0Bounds.Minimum.y; y < lod0Bounds.Maximum.y; y++ )
			{
				for ( var x = lod0Bounds.Minimum.x; x < lod0Bounds.Maximum.x; x++ )
				{
					_desiredChunks.Add( new Vector3Int( x, y, z ) );
				}
			}
		}
		var desiredMilliseconds = (float)Stopwatch.GetElapsedTime( desiredStart ).TotalMilliseconds;

		_coordinateBuffer.Clear();
		foreach ( var coordinate in _desiredChunks )
		{
			if ( !_loadedChunks.ContainsKey( coordinate ) ) _coordinateBuffer.Add( coordinate );
		}
		SortNearestFirst( _coordinateBuffer, center );
		_generationCancellation?.Cancel();
		_pendingChunks.Clear();
		_completedChunks.Clear();
		foreach ( var coordinate in _coordinateBuffer ) _pendingChunks.Enqueue( coordinate );

		_generatedThisStream = 0;
		_retainedThisStream = _loadedChunks.Count( pair => _desiredChunks.Contains( pair.Key ) );
		_unloadedThisStream = 0;
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

		ScheduleClipLevel( selection, _pendingClipMinimumLod, placementRevision );
		if ( adjacent )
		{
			for ( var lod = selection.MaximumLod; lod > 0; lod-- )
			{
				ScheduleClipLevel( selection, lod, placementRevision );
			}
		}

		if ( _pendingChunks.Count == 0 )
		{
			_streamRevision++;
			_workerCompleted = true;
			CompleteStream();
		}
		else
		{
			StartBackgroundGeneration( _coordinateBuffer.ToArray() );
		}

		var totalMilliseconds = (float)Stopwatch.GetElapsedTime( synchronousStart ).TotalMilliseconds;
		if ( _playerFigureEightTestRunning )
		{
			_performanceStreaming.IncrementalUpdates++;
			_performanceStreaming.TotalSynchronousMilliseconds += totalMilliseconds;
			_performanceStreaming.MaximumSynchronousMilliseconds = Math.Max(
				_performanceStreaming.MaximumSynchronousMilliseconds, totalMilliseconds );
			_performanceStreaming.TotalDesiredUpdateMilliseconds += desiredMilliseconds;
			_performanceStreaming.GameplayCoordinatesTouched += _desiredChunks.Count;
			_performanceStreaming.RenderCoordinatesTouched +=
				selection.ResidentRegularCount + selection.LogicalTransitionFaceCount;
		}

		if ( VerboseLogging )
		{
			Log.Info(
				$"[VoxelWorld] clip.request center=C[{center.x},{center.y},{center.z}] reason=\"{reason}\" " +
				$"placementRevision={placementRevision} adjacent={adjacent} maximumLod={selection.MaximumLod} " +
				$"viewRadius={ViewRadiusChunks} fullDetailRadius={FullDetailRadiusChunks} " +
				$"residentRegular={selection.ResidentRegularCount} activeRegular={selection.ActiveRegularCount} " +
				$"transitionFaces={selection.LogicalTransitionFaceCount} gameplayQueued={_pendingChunks.Count} " +
				$"selectionMs={selectionMilliseconds:0.0000} totalMs={totalMilliseconds:0.0000}" );
		}
		RefreshReadableStatus();
	}

	private void ScheduleClipLevel(
		VoxelClipBoxSelection selection,
		int lod,
		int placementRevision )
	{
		if ( lod == 0 )
		{
			foreach ( var pair in _loadedChunks )
			{
				if ( !_desiredChunks.Contains( pair.Key ) ) continue;
				_gpuMesher.Schedule(
					pair.Value,
					_terrainContentRevision,
					placementRevision,
					_playerFigureEightRouteDistance,
					GpuMeshResidency.Fallback );
			}
		}
		else
		{
			foreach ( var key in selection.ResidentRegular
				.Where( key => key.Lod == lod )
				.OrderBy( key => key.Coordinate.z )
				.ThenBy( key => key.Coordinate.y )
				.ThenBy( key => key.Coordinate.x ) )
			{
				_gpuMesher.Schedule(
					GpuSdfDescriptor.ForRenderRegion(
						key,
						CellsPerAxis,
						CellSize,
						CurrentTerrainSettings,
						_terrainContentRevision,
						placementRevision ),
					_playerFigureEightRouteDistance );
			}
		}

		var coarseTransitionLod = lod + 1;
		foreach ( var key in selection.TransitionFaces
			.Where( key => key.Lod == coarseTransitionLod )
			.OrderBy( key => key.Face )
			.ThenBy( key => key.Coordinate.z )
			.ThenBy( key => key.Coordinate.y )
			.ThenBy( key => key.Coordinate.x ) )
		{
			_gpuMesher.Schedule(
				GpuSdfDescriptor.ForRenderRegion(
					key,
					CellsPerAxis,
					CellSize,
					CurrentTerrainSettings,
					_terrainContentRevision,
					placementRevision ),
				_playerFigureEightRouteDistance );
		}
	}

	private void TryAdvanceClipCoverage()
	{
		if ( _pendingClipSelection is null || _gpuMesher is null ) return;
		var committedSelection = _pendingClipSelection;
		var committedMinimumLod = _pendingClipMinimumLod;
		if ( !_gpuMesher.IsClipCoverageReady(
			committedSelection,
			committedMinimumLod,
			_terrainContentRevision,
			_clipPlacementRevision,
			out var missingRegions ) )
		{
			_clipFallbackFrames++;
			ClipBoxStatus =
				$"Refining LOD {_pendingClipMinimumLod}; {missingRegions:N0} regions or seams pending";
			return;
		}
		var integrationStart = Stopwatch.GetTimestamp();
		_gpuMesher.CommitClipCoverage(
			committedSelection,
			committedMinimumLod,
			_clipPlacementRevision );
		var milliseconds = (float)Stopwatch.GetElapsedTime( integrationStart ).TotalMilliseconds;
		_maximumClipIntegrationMilliseconds = Math.Max( _maximumClipIntegrationMilliseconds, milliseconds );
		if ( milliseconds > MainThreadIntegrationBudgetMilliseconds )
		{
			_clipIntegrationBudgetViolations++;
			Log.Warning(
				$"[VoxelWorld] clip.integration.over_budget milliseconds={milliseconds:0.0000} " +
				$"budget={MainThreadIntegrationBudgetMilliseconds:0.0000}" );
		}
		_gpuMesher.RetireCommittedClipCoverage();

		if ( _pendingClipMinimumLod > 0 )
		{
			_pendingClipMinimumLod--;
			ScheduleClipLevel(
				_pendingClipSelection,
				_pendingClipMinimumLod,
				_clipPlacementRevision );
			ClipBoxStatus = $"Fallback committed; refining LOD {_pendingClipMinimumLod}";
		}
		else
		{
			_activeClipSelection = _pendingClipSelection;
			_pendingClipSelection = null;
			_coordinateBuffer.Clear();
			foreach ( var coordinate in _loadedChunks.Keys )
			{
				if ( !_desiredChunks.Contains( coordinate ) ) _coordinateBuffer.Add( coordinate );
			}
			foreach ( var coordinate in _coordinateBuffer )
			{
				_loadedChunks.Remove( coordinate );
				_unloadedThisStream++;
			}
			ClipBoxStatus =
				$"LOD0-{EffectiveMaximumLod}; {_activeClipSelection.ResidentRegularCount:N0} resident; " +
				$"{_activeClipSelection.ActiveRegularCount:N0} active; " +
				$"{_activeClipSelection.LogicalTransitionFaceCount:N0} transitions";
		}
		var expectedRegular = committedSelection.GetExpectedActiveRegularCount( committedMinimumLod );
		var expectedTransitions = committedSelection.GetExpectedActiveTransitionCount( committedMinimumLod );
		var activeRegularCount = _gpuMesher.ActiveRegularCount;
		var activeTransitionCount = _gpuMesher.ActiveTransitionCount;
		if ( activeRegularCount != expectedRegular ||
			activeTransitionCount != expectedTransitions )
		{
			_clipCoverageMismatches++;
			Log.Error(
				$"[VoxelWorld] clip.coverage.mismatch expectedRegular={expectedRegular} " +
				$"actualRegular={activeRegularCount} expectedTransitions={expectedTransitions} " +
				$"actualTransitions={activeTransitionCount}" );
		}
		var adjacencyViolations = committedSelection.CountAdjacencyViolations( committedMinimumLod );
		_clipAdjacencyViolations += adjacencyViolations;
		if ( adjacencyViolations > 0 )
		{
			Log.Error( $"[VoxelWorld] clip.adjacency.violation count={adjacencyViolations}" );
		}

		RefreshReadableStatus();
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
				_gpuMesher.Schedule(
					chunk,
					_terrainContentRevision,
					_clipPlacementRevision,
					_playerFigureEightRouteDistance,
					GpuMeshResidency.Fallback );
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

			var lod0Bounds = _activeClipSelection?.Boxes[0];
			Log.Info(
				$"[VoxelWorld] stream.complete center=C[{_streamingCenterCoordinate.x},{_streamingCenterCoordinate.y},{_streamingCenterCoordinate.z}] " +
				$"rangeMin=C[{lod0Bounds?.Minimum.x ?? 0},{lod0Bounds?.Minimum.y ?? 0},{lod0Bounds?.Minimum.z ?? 0}] " +
				$"rangeMaxExclusive=C[{lod0Bounds?.Maximum.x ?? 0},{lod0Bounds?.Maximum.y ?? 0},{lod0Bounds?.Maximum.z ?? 0}] " +
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
		var displayedBounds = (_pendingClipSelection ?? _activeClipSelection)?.Boxes[0];
		LoadedChunkRange = displayedBounds is { } bounds
			? $"X {bounds.Minimum.x} through {bounds.Maximum.x - 1}; " +
				$"Y {bounds.Minimum.y} through {bounds.Maximum.y - 1}; " +
				$"Z {bounds.Minimum.z} through {bounds.Maximum.z - 1} (half-open)"
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
