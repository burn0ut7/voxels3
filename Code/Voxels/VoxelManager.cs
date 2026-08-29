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
	private const float MainThreadIntegrationBudgetMilliseconds = 0.5f;
	private const float PerformanceWindowSeconds = 10f;
	private const float MemorySampleIntervalSeconds = 1f;
	private const int MaximumPerformanceFrameSamples = 524288;
	private const int MaximumFigureEightLoopCount = 8;
	private const int PerformanceResultSchemaVersion = 4;
	private const int RenderWarmShellChunks = 1;
	private const int GenerationBatchSize = 256;
	private const string PerformanceResultsDirectory = "performance";
	private const string PerformanceResultsPath = "performance/results-v1.jsonl";
	private const string InspectorPerformanceTask = "PERFORMANCE-OVERVIEW-001/v3";
	private const string InspectorPerformanceRevision = "manual-inspector";
	private static readonly JsonSerializerOptions PerformanceJsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

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
	private readonly float[] _performanceFrameMilliseconds = new float[MaximumPerformanceFrameSamples];
	private readonly float[] _sortedPerformanceFrameMilliseconds = new float[MaximumPerformanceFrameSamples];
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
	private int _appliedLoadRadius;
	private float _appliedTerrainSurfaceHeight;
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
	private GpuVisibilityMeasurement _lastPerformanceVisibility;
	private float _lastPerformanceWindowSeconds;
	private int _lastPerformanceFrameSampleCount;
	private int _lastPerformanceTruncatedFrameSampleCount;
	private float _lastAverageFramesPerSecond;
	private float _lastP95FrameMilliseconds;
	private float _lastP99FrameMilliseconds;
	private float _lastAverageGpuFrameMilliseconds;
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
	private float _lastPerformanceChunksPerSecond;
	private bool _lastPerformanceWasFigureEightTest;
	private int _lastPerformanceCompletedLoops;
	private float _lastPerformanceTestSpeed;
	private float _lastPerformanceTestDistance;
	private PerformanceStreamingMetrics _performanceStreaming = new();
	private PerformanceStreamingMetrics _lastPerformanceStreaming = new();
	private GameObject ActiveStreamingTarget => StreamingTarget ?? _resolvedStreamingTarget ?? GameObject;

	[Property, Category( "Chunk Configuration" ), Range( 4, 64 )]
	public int CellsPerAxis { get; set; } = 32;

	[Property, Category( "Chunk Configuration" ), Range( 1f, 128f )]
	public float CellSize { get; set; } = 16f;

	[Property, Category( "Chunk Configuration" ), Range( 0, 128 )]
	public int LoadRadius { get; set; } = 16;

	[Property, Category( "Chunk Configuration" )]
	public float TerrainSurfaceHeight { get; set; } = 0f;

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
			if ( LoadRadius != _appliedLoadRadius )
			{
				_appliedLoadRadius = LoadRadius;
				var targetPosition = ActiveStreamingTarget.WorldPosition;
				RebuildDesiredChunks( WorldToChunkCoordinate( targetPosition ), "load radius changed" );
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
		else if ( IntegrateCompletedWarmChunks() )
		{
			RefreshReadableStatus();
		}
		else
		{
			RefreshPlayerChunkStatus();
		}
		_gpuMesher.ProcessPending( GpuVoxelMesher.MaximumDispatchesPerUpdate );
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
		_lastPerformanceVisibility = default;
		FramePerformance = $"Figure-eight test running: 0 of {loopCount} loops";
		ProcessMemoryUsage = "Collecting figure-eight test window";
		ResetPerformanceWindow();
		SamplePerformanceMemory();
		_gpuMesher?.BeginVisibilityMeasurement();
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
				LoadRadius = LoadRadius,
				TerrainSurfaceHeight = TerrainSurfaceHeight,
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
				AverageGpuMilliseconds = _lastAverageGpuFrameMilliseconds
			},
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
				Dispatches = _lastPerformanceMeshDispatches,
				Resident = _gpuMesher?.ResidentCount ?? 0,
				GameplayResident = (_gpuMesher?.ResidentCount ?? 0) - (_gpuMesher?.WarmResidentCount ?? 0),
				WarmResident = _gpuMesher?.WarmResidentCount ?? 0,
				Pending = _gpuMesher?.PendingCount ?? 0,
				GameplayPending = _gpuMesher?.PendingGameplayCount ?? 0,
				WarmPending = _gpuMesher?.PendingWarmCount ?? 0,
				PoolAvailable = _gpuMesher?.PoolCount ?? 0,
				LogicalCapacityBytes = _gpuMesher?.LogicalCapacityBytes ?? 0,
				PoolAllocations = _lastPerformanceMeshPoolAllocations,
				PoolReuses = _lastPerformanceMeshPoolReuses,
				GameThreadAllocatedBytes = null,
				ScalarReadbacks = _lastPerformanceMeshScalarReadbacks,
				GeometryReadbacks = GpuVoxelMesher.GeometryReadbackCount,
				GpuProfilerPath = meshingGpuProfilerPath,
				AverageGpuMilliseconds = meshingGpuSmoothedMilliseconds,
				MaximumGpuMilliseconds = meshingGpuMaximumMilliseconds
			},
			Visibility = new PerformanceVisibilityMetrics
			{
				Samples = _lastPerformanceVisibility.FrameCount,
				AverageResidentMeshChunks = _lastPerformanceVisibility.AverageResident,
				AverageVisibleMeshChunks = _lastPerformanceVisibility.AverageVisible,
				AverageWarmMeshChunks = _lastPerformanceVisibility.AverageWarm,
				MinimumVisibleMeshChunks = _lastPerformanceVisibility.MinimumVisible,
				MaximumVisibleMeshChunks = _lastPerformanceVisibility.MaximumVisible,
				AverageNonZeroIndirectDraws = _lastPerformanceVisibility.AverageVisible,
				AverageCulledDraws = _lastPerformanceVisibility.AverageCulled,
				CulledDrawPercentage = _lastPerformanceVisibility.CulledPercent,
				LogicalBufferBytes = _lastPerformanceVisibility.LogicalBufferBytes,
				ScalarReadbacks = _lastPerformanceVisibility.ScalarReadbacks
			},
			Streaming = _lastPerformanceStreaming
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

	[ConCmd( "voxel_stream_origin" )]
	public static void SetDebugStreamingOrigin( float x, float y, float z )
	{
		if ( !TryGetActiveManager( "debug.origin", out var manager ) )
		{
			return;
		}

		var requestedPosition = new Vector3( x, y, z );
		manager.ActiveStreamingTarget.WorldPosition = requestedPosition;

		Log.Info(
			$"[VoxelWorld] debug.origin.applied position=[{x},{y},{z}] " +
			$"target=\"{manager.ActiveStreamingTarget.Name}\"" );
	}

	[ConCmd( "voxel_verbose_logging" )]
	public static void SetVerboseLoggingCommand( bool enabled )
	{
		if ( !TryGetActiveManager( "logging.verbose", out var manager ) )
		{
			return;
		}

		manager.VerboseLogging = enabled;
		Log.Info( $"[VoxelWorld] logging.verbose enabled={enabled}" );
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
		SetFigureEightPosition(
			_playerFigureEightCenter.x + distance * sine,
			_playerFigureEightCenter.y + distance * sine * cosine );
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
		_playerFigureEightTestRunning = false;
		_playerFigureEightTarget = null;
		_playerFigureEightBody = null;
		_gpuMesher?.EndVisibilityMeasurement();
		_performanceVisibilityPending = true;
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

		// The measured loop is already complete. Wait for its final derived mesh work
		// to settle so the saved resident/backlog snapshot represents availability at
		// the completed route, without extending or changing the measured frame window.
		if ( _pendingWarmChunks.Count > 0 ||
			_completedWarmChunks.Count > 0 ||
			!_warmWorkerCompleted ||
			_gpuMesher.PendingCount > 0 ||
			!_gpuMesher.TryTakeVisibilityMeasurement( out _lastPerformanceVisibility ) )
		{
			return;
		}

		_performanceVisibilityPending = false;

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
		}
		if ( _playerFigureEightTestRunning )
		{
			_performanceStreaming.PeakGameplayMeshBacklog = Math.Max(
				_performanceStreaming.PeakGameplayMeshBacklog,
				_gpuMesher?.PendingGameplayCount ?? 0 );
			_performanceStreaming.PeakWarmMeshBacklog = Math.Max(
				_performanceStreaming.PeakWarmMeshBacklog,
				_gpuMesher?.PendingWarmCount ?? 0 );
		}

		var deltaSeconds = RealTime.Delta;
		_performanceWindowElapsedSeconds += deltaSeconds;
		_memorySampleElapsedSeconds += deltaSeconds;
		if ( _memorySampleElapsedSeconds >= MemorySampleIntervalSeconds )
		{
			_memorySampleElapsedSeconds = 0f;
			SamplePerformanceMemory();
		}

		if ( !_playerFigureEightTestRunning && _performanceWindowElapsedSeconds >= PerformanceWindowSeconds )
		{
			CompletePerformanceWindow();
		}
	}

	private void CompletePerformanceWindow()
	{
		if ( _performanceFrameSampleCount == 0 || _performanceMemorySampleCount == 0 )
		{
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
		_lastPerformanceCompletedLoops = _playerFigureEightTestRunning ? _playerFigureEightCompletedLoops : 0;
		_lastPerformanceTestSpeed = _playerFigureEightTestRunning ? _playerFigureEightTestSpeed : 0f;
		_lastPerformanceTestDistance = _playerFigureEightTestRunning ? _playerFigureEightTestDistance : 0f;
		_lastPerformanceStreaming = _performanceStreaming;
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
		_performanceStreaming = new PerformanceStreamingMetrics();
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

		chunk.TryGetSample( Vector3Int.Zero, out var minimumSampleDensity, out var minimumSampleMaterialId );
		chunk.TryGetSample(
			new Vector3Int( 0, 0, chunk.CellsPerAxis ),
			out var maximumSampleDensity,
			out var maximumSampleMaterialId );
		Log.Info(
			$"[VoxelWorld] chunk.inspect chunk={chunk.LogId} name=\"{chunk.HumanName}\" cellsPerAxis={chunk.CellsPerAxis} " +
			$"samplesPerAxis={chunk.SamplesPerAxis} sampleCount={chunk.SampleCount} " +
			$"densityMin={chunk.MinimumDensity} densityMax={chunk.MaximumDensity} " +
			$"minimumSample=L[0,0,0] minimumSampleDensity={minimumSampleDensity} " +
			$"minimumSampleMaterial=\"{VoxelChunk.GetMaterialName( minimumSampleMaterialId )}\" minimumSampleMaterialId={minimumSampleMaterialId} " +
			$"maximumSample=L[0,0,{chunk.CellsPerAxis}] maximumSampleDensity={maximumSampleDensity} " +
			$"maximumSampleMaterial=\"{VoxelChunk.GetMaterialName( maximumSampleMaterialId )}\" maximumSampleMaterialId={maximumSampleMaterialId}" );
	}

	public string InspectGpuMesh( int x, int y, int z )
	{
		var coordinate = new Vector3Int( x, y, z );
		if ( !_loadedChunks.TryGetValue( coordinate, out var chunk ) )
		{
			return $"C[{x},{y},{z}] loaded=false";
		}

		return _gpuMesher.Inspect( chunk );
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

		if ( LoadRadius < 0 || LoadRadius > 16 )
		{
			error = "Load Radius must be between 0 and 16 chunks.";
			return false;
		}

		if ( !float.IsFinite( TerrainSurfaceHeight ) )
		{
			error = "Terrain Surface Height must be finite.";
			return false;
		}

		error = string.Empty;
		return true;
	}

	private bool DataConfigurationChanged()
	{
		return CellsPerAxis != _appliedCellsPerAxis ||
			CellSize != _appliedCellSize ||
			TerrainSurfaceHeight != _appliedTerrainSurfaceHeight;
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
		_appliedLoadRadius = LoadRadius;
		_appliedTerrainSurfaceHeight = TerrainSurfaceHeight;

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

	private void RebuildDesiredChunks( Vector3Int center, string reason )
	{
		var synchronousStart = Stopwatch.GetTimestamp();
		var previousCenter = _streamingCenterCoordinate;
		var hadPreviousCenter = _hasStreamingCenter;
		var desiredUpdateStart = Stopwatch.GetTimestamp();
		var delta = hadPreviousCenter ? center - previousCenter : Vector3Int.Zero;
		var renderRadius = LoadRadius + RenderWarmShellChunks;
		var incremental = hadPreviousCenter && center != previousCenter &&
			Math.Abs( delta.x ) <= 1 && Math.Abs( delta.y ) <= 1 && Math.Abs( delta.z ) <= 1 &&
			_desiredChunks.Count == GetCubeCoordinateCount( LoadRadius ) &&
			_renderDesiredChunks.Count == GetCubeCoordinateCount( renderRadius );

		_gameplayEnteringBuffer.Clear();
		_gameplayLeavingBuffer.Clear();
		_renderEnteringBuffer.Clear();
		_renderLeavingBuffer.Clear();
		if ( incremental )
		{
			SlideDesiredWindow( _desiredChunks, previousCenter, center, LoadRadius,
				_gameplayEnteringBuffer, _gameplayLeavingBuffer );
			SlideDesiredWindow( _renderDesiredChunks, previousCenter, center, renderRadius,
				_renderEnteringBuffer, _renderLeavingBuffer );
		}
		else
		{
			_desiredChunks.Clear();
			for ( var z = -LoadRadius; z <= LoadRadius; z++ )
			{
				for ( var y = -LoadRadius; y <= LoadRadius; y++ )
				{
					for ( var x = -LoadRadius; x <= LoadRadius; x++ )
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
				_gpuMesher.SetResidency( coordinate, GpuMeshResidency.Warm );
			}
			else
			{
				_gpuMesher.Remove( coordinate );
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
			_gpuMesher.Remove( coordinate );
			_renderPreparedChunks.Remove( coordinate );
		}

		if ( !incremental )
		{
			var previousRenderDesired = _renderDesiredChunks;
			_renderDesiredChunks = _nextRenderDesiredChunks;
			_nextRenderDesiredChunks = previousRenderDesired;
		}
		var drawCommit = _gpuMesher.CommitDrawCommands();

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
				$"loadRadius={LoadRadius} retained={_loadedChunks.Count} " +
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
			TerrainSurfaceHeight,
			revision,
			cancellation.Token );
	}

	private async System.Threading.Tasks.Task GenerateChunksInBackground(
		System.Threading.Tasks.Task previousTask,
		Vector3Int[] coordinates,
		int cellsPerAxis,
		float cellSize,
		float terrainSurfaceHeight,
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
							coordinates[batchOffset + index], cellsPerAxis, cellSize, terrainSurfaceHeight );
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
			TerrainSurfaceHeight,
			revision,
			cancellation.Token );
	}

	private async System.Threading.Tasks.Task GenerateWarmChunksInBackground(
		System.Threading.Tasks.Task previousTerrainTask,
		System.Threading.Tasks.Task previousWarmTask,
		Vector3Int[] coordinates,
		int cellsPerAxis,
		float cellSize,
		float terrainSurfaceHeight,
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
						var densityRange = VoxelChunk.ClassifyDensityRange(
							coordinate, cellsPerAxis, cellSize, terrainSurfaceHeight );
						var chunk = densityRange.Classification ==
							ChunkDensityClassification.PotentiallySurfaceContaining
							? new VoxelChunk( coordinate, cellsPerAxis, cellSize, terrainSurfaceHeight )
							: null;
						results.Add( new WarmChunkResult( coordinate, densityRange.Classification, chunk ) );
					}
					return results;
				} );

				await Task.MainThread();
				if ( cancellationToken.IsCancellationRequested || revision != _warmGenerationRevision )
				{
					return;
				}

				foreach ( var result in batch )
				{
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
					TerrainSurfaceHeight,
					_terrainContentRevision,
					GpuMeshResidency.Gameplay );
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
						TerrainSurfaceHeight,
						_terrainContentRevision,
						residency );
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
				$"rangeMin=C[{_streamingCenterCoordinate.x - LoadRadius},{_streamingCenterCoordinate.y - LoadRadius},{_streamingCenterCoordinate.z - LoadRadius}] " +
				$"rangeMax=C[{_streamingCenterCoordinate.x + LoadRadius},{_streamingCenterCoordinate.y + LoadRadius},{_streamingCenterCoordinate.z + LoadRadius}] " +
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
			? $"X {_streamingCenterCoordinate.x - LoadRadius} through {_streamingCenterCoordinate.x + LoadRadius}; " +
				$"Y {_streamingCenterCoordinate.y - LoadRadius} through {_streamingCenterCoordinate.y + LoadRadius}; " +
				$"Z {_streamingCenterCoordinate.z - LoadRadius} through {_streamingCenterCoordinate.z + LoadRadius}"
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
	VoxelChunk Chunk );
