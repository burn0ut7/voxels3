using System;
using System.Diagnostics;
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
	private const int MaximumPerformanceFrameSamples = 32768;

	private readonly Dictionary<Vector3Int, VoxelChunk> _loadedChunks = new();
	private readonly HashSet<Vector3Int> _desiredChunks = new();
	private readonly Queue<Vector3Int> _pendingChunks = new();
	private readonly Queue<VoxelChunk> _completedChunks = new();
	private readonly List<Vector3Int> _coordinateBuffer = new();
	private readonly float[] _performanceFrameMilliseconds = new float[MaximumPerformanceFrameSamples];
	private readonly float[] _sortedPerformanceFrameMilliseconds = new float[MaximumPerformanceFrameSamples];

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
	private int _appliedCellsPerAxis;
	private float _appliedCellSize;
	private int _appliedLoadRadius;
	private float _appliedTerrainSurfaceHeight;
	private int _streamRevision;
	private bool _workerCompleted;
	private CancellationTokenSource _generationCancellation;
	private System.Threading.Tasks.Task _generationTask = System.Threading.Tasks.Task.CompletedTask;
	private string _lastConfigurationError = string.Empty;
	private GameObject _resolvedStreamingTarget;
	private bool _playerFigureEightEnabled;
	private GameObject _playerFigureEightTarget;
	private Vector2 _playerFigureEightCenter;
	private float _playerFigureEightParameter;
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
	private int _performanceMemorySampleCount;
	private int _performanceChunksIntegrated;
	private bool _performanceSnapshotReady;
	private float _lastPerformanceWindowSeconds;
	private int _lastPerformanceFrameSampleCount;
	private int _lastPerformanceTruncatedFrameSampleCount;
	private float _lastAverageFramesPerSecond;
	private float _lastP95FrameMilliseconds;
	private float _lastP99FrameMilliseconds;
	private float _lastAverageGpuFrameMilliseconds;
	private ulong _lastAverageProcessMemoryBytes;
	private ulong _lastPeakProcessMemoryBytes;
	private ulong _lastAverageGpuMemoryBytes;
	private ulong _lastPeakGpuMemoryBytes;
	private ulong _lastGpuMemoryBudgetBytes;
	private int _lastPerformanceChunksIntegrated;
	private float _lastPerformanceChunksPerSecond;
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

	[Property, Category( "Debug Visualization" )]
	public bool ShowLoadedChunkBounds { get; set; } = false;

	[Property, Category( "Debug Visualization" )]
	public bool ShowLoadedChunkLabels { get; set; } = false;

	[Property, Category( "Debug Visualization" )]
	public bool LogChunkLifecycle { get; set; } = false;

	[Property, Category( "Smoke Test" ), Range( 1f, 2048f )]
	public float FigureEightSpeed { get; set; } = 320f;

	[Property, Category( "Smoke Test" ), Range( 1f, 8192f )]
	public float FigureEightDistance { get; set; } = 1024f;

	[Property, Category( "Performance Logging" )]
	public string PerformanceTask { get; set; } = "unassigned";

	[Property, Category( "Performance Logging" )]
	public string PerformanceRevision { get; set; } = "unassigned";

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
				break;
			}

			await Task.Yield();
		}

		_initialLoadCompleted = _loadedChunks.Count == _desiredChunks.Count && _pendingChunks.Count == 0;
		Log.Info(
			$"[VoxelWorld] load.complete ready={_initialLoadCompleted} loaded={_loadedChunks.Count} " +
			$"pending={_pendingChunks.Count}" );
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
				_loadedChunks.Clear();
				_desiredChunks.Clear();
				_pendingChunks.Clear();
				_completedChunks.Clear();
				_hasStreamingCenter = false;
				_streamInProgress = false;
				_lastConfigurationError = configurationError;
				Log.Warning( $"[VoxelWorld] configuration.invalid reason=\"{configurationError}\"" );
				RefreshReadableStatus();
			}

			DrawDebugOverlay();
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
		else
		{
			RefreshPlayerChunkStatus();
		}
		DrawDebugOverlay();
	}

	protected override void OnDestroy()
	{
		_playerFigureEightEnabled = false;
		_playerFigureEightTarget = null;
		_generationCancellation?.Cancel();
	}

	protected override void OnValidate()
	{
		RefreshReadableStatus();
	}

	[Button( "Log World Summary" )]
	public void LogWorldSummary()
	{
		Log.Info(
			$"[VoxelWorld] summary center=C[{_streamingCenterCoordinate.x},{_streamingCenterCoordinate.y},{_streamingCenterCoordinate.z}] " +
			$"loadRadius={LoadRadius} " +
			$"loaded={_loadedChunks.Count} pending={_pendingChunks.Count} " +
			$"cellSize={CellSize} cellsPerAxis={CellsPerAxis}" );
	}

	[Button( "Toggle Player Figure Eight" )]
	public void TogglePlayerFigureEight()
	{
		try
		{
			var result = ConfigurePlayerFigureEight(
				!_playerFigureEightEnabled,
				FigureEightSpeed,
				FigureEightDistance );
			Log.Info( $"[VoxelWorld] player.figure_eight {result}" );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"[VoxelWorld] player.figure_eight.rejected reason=\"{exception.Message}\"" );
		}
	}

	public string ConfigurePlayerFigureEight( bool enabled, float speed, float distance )
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before running the player figure-eight." );
		}

		if ( !enabled )
		{
			_playerFigureEightEnabled = false;
			_playerFigureEightTarget = null;
			return "stopped";
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
		_playerFigureEightCenter = new Vector2( start.x, start.y );
		_playerFigureEightParameter = 0f;
		_playerFigureEightEnabled = true;
		_playerFigureEightTarget.WorldPosition = new Vector3( start.x, start.y, 0f );

		return $"started speed={speed} distance={distance}";
	}

	[Button( "Log Performance Overview" )]
	public void LogPerformanceOverviewFromInspector()
	{
		try
		{
			WritePerformanceOverview( PerformanceTask, PerformanceRevision );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"[VoxelWorld] performance.overview.rejected reason=\"{exception.Message}\"" );
		}
	}

	public string WritePerformanceOverview( string task, string revision )
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before logging a performance overview." );
		}

		if ( !_performanceSnapshotReady )
		{
			throw new InvalidOperationException( "Wait for one complete 10-second performance window." );
		}

		if ( !string.IsNullOrWhiteSpace( task ) )
		{
			PerformanceTask = task.Trim();
		}

		if ( !string.IsNullOrWhiteSpace( revision ) )
		{
			PerformanceRevision = revision.Trim();
		}

		var target = ActiveStreamingTarget;
		var targetPosition = target.WorldPosition;
		var sceneName = Scene?.Name ?? "unknown";
		var line = string.Concat(
			FormattableString.Invariant( $"[VoxelWorld] performance.overview capturedAtUtc=\"{DateTimeOffset.UtcNow:O}\" " ),
			FormattableString.Invariant( $"scene=\"{EscapeLogValue( sceneName )}\" task=\"{EscapeLogValue( PerformanceTask )}\" " ),
			FormattableString.Invariant( $"revision=\"{EscapeLogValue( PerformanceRevision )}\" " ),
			FormattableString.Invariant( $"center=C[{_streamingCenterCoordinate.x},{_streamingCenterCoordinate.y},{_streamingCenterCoordinate.z}] " ),
			FormattableString.Invariant( $"targetX={targetPosition.x:0.###} targetY={targetPosition.y:0.###} targetZ={targetPosition.z:0.###} " ),
			FormattableString.Invariant( $"windowSeconds={_lastPerformanceWindowSeconds:0.###} frameSamples={_lastPerformanceFrameSampleCount} " ),
			FormattableString.Invariant( $"truncatedFrameSamples={_lastPerformanceTruncatedFrameSampleCount} " ),
			FormattableString.Invariant( $"averageFps={_lastAverageFramesPerSecond:0.###} p95FrameMs={_lastP95FrameMilliseconds:0.###} " ),
			FormattableString.Invariant( $"p99FrameMs={_lastP99FrameMilliseconds:0.###} averageGpuFrameMs={_lastAverageGpuFrameMilliseconds:0.###} " ),
			FormattableString.Invariant( $"averageProcessMemoryBytes={_lastAverageProcessMemoryBytes} peakProcessMemoryBytes={_lastPeakProcessMemoryBytes} " ),
			FormattableString.Invariant( $"averageGpuMemoryBytes={_lastAverageGpuMemoryBytes} peakGpuMemoryBytes={_lastPeakGpuMemoryBytes} " ),
			FormattableString.Invariant( $"gpuMemoryBudgetBytes={_lastGpuMemoryBudgetBytes} loadedChunks={_loadedChunks.Count} " ),
			FormattableString.Invariant( $"pendingChunks={_pendingChunks.Count} windowIntegratedChunks={_lastPerformanceChunksIntegrated} " ),
			FormattableString.Invariant( $"windowChunksPerSecond={_lastPerformanceChunksPerSecond:0.###} " ),
			FormattableString.Invariant( $"lastStreamGeneratedChunks={LastGeneratedChunkCount} lastStreamSettleMs={LastStreamSettleMilliseconds:0.###} " ),
			FormattableString.Invariant( $"lastEffectiveChunksPerSecond={LastEffectiveChunksPerSecond:0.###} " ),
			FormattableString.Invariant( $"lastGenerationChunksPerSecond={LastGenerationChunksPerSecond:0.###}" ) );
		Log.Info( line );
		return line;
	}

	[Button( "Log Player Chunk" )]
	public void LogPlayerChunk()
	{
		var targetPosition = ActiveStreamingTarget.WorldPosition;
		var coordinate = WorldToChunkCoordinate( targetPosition );
		Log.Info(
			$"[VoxelWorld] player.chunk target=\"{ActiveStreamingTarget.Name}\" " +
			$"position=[{targetPosition.x},{targetPosition.y},{targetPosition.z}] " +
			$"chunk=C[{coordinate.x},{coordinate.y},{coordinate.z}]" );
		LogChunkData( coordinate );
	}

	[ConCmd( "voxel_player_chunk" )]
	public static void LogPlayerChunkCommand()
	{
		if ( TryGetActiveManager( "player.chunk", out var manager ) )
		{
			manager.LogPlayerChunk();
		}
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
			return;
		}

		var tangentX = MathF.Cos( _playerFigureEightParameter );
		var tangentY = MathF.Cos( 2f * _playerFigureEightParameter );
		var tangentLength = MathF.Sqrt( tangentX * tangentX + tangentY * tangentY );
		_playerFigureEightParameter +=
			FigureEightSpeed * RealTime.Delta / (FigureEightDistance * tangentLength);

		if ( _playerFigureEightParameter >= MathF.Tau )
		{
			_playerFigureEightParameter -= MathF.Tau;
		}

		var sine = MathF.Sin( _playerFigureEightParameter );
		var cosine = MathF.Cos( _playerFigureEightParameter );
		_playerFigureEightTarget.WorldPosition = new Vector3(
			_playerFigureEightCenter.x + FigureEightDistance * sine,
			_playerFigureEightCenter.y + FigureEightDistance * sine * cosine,
			0f );
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

		var deltaSeconds = RealTime.Delta;
		_performanceWindowElapsedSeconds += deltaSeconds;
		_memorySampleElapsedSeconds += deltaSeconds;
		if ( _memorySampleElapsedSeconds >= MemorySampleIntervalSeconds )
		{
			_memorySampleElapsedSeconds = 0f;
			var processMemoryBytes = global::Sandbox.Diagnostics.PerformanceStats.ApproximateProcessMemoryUsage;
			var gpuMemoryBytes = global::Sandbox.Graphics.VideoMemoryUsed;
			_performanceProcessMemoryBytesTotal += processMemoryBytes;
			_performancePeakProcessMemoryBytes = Math.Max( _performancePeakProcessMemoryBytes, processMemoryBytes );
			_performanceGpuMemoryBytesTotal += gpuMemoryBytes;
			_performancePeakGpuMemoryBytes = Math.Max( _performancePeakGpuMemoryBytes, gpuMemoryBytes );
			_performanceGpuMemoryBudgetBytes = global::Sandbox.Graphics.VideoMemoryBudget;
			_performanceMemorySampleCount++;
		}

		if ( _performanceWindowElapsedSeconds >= PerformanceWindowSeconds )
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
		_lastAverageGpuMemoryBytes = (ulong)(
			_performanceGpuMemoryBytesTotal / _performanceMemorySampleCount );
		_lastPeakGpuMemoryBytes = _performancePeakGpuMemoryBytes;
		_lastGpuMemoryBudgetBytes = _performanceGpuMemoryBudgetBytes;
		_lastPerformanceChunksIntegrated = _performanceChunksIntegrated;
		_lastPerformanceChunksPerSecond =
			_performanceChunksIntegrated / _performanceWindowElapsedSeconds;
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
		_performanceMemorySampleCount = 0;
		_performanceChunksIntegrated = 0;
	}

	private static string EscapeLogValue( string value )
	{
		return (value ?? string.Empty).Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" );
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

	private void ResolveStreamingTarget()
	{
		if ( StreamingTarget is not null )
		{
			_resolvedStreamingTarget = StreamingTarget;
			Log.Info( $"[VoxelWorld] target.resolve mode=assigned name=\"{StreamingTarget.Name}\"" );
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
		Log.Info(
			$"[VoxelWorld] target.resolve mode={(localPlayer is null ? "manager-fallback" : "local-player")} " +
			$"name=\"{_resolvedStreamingTarget.Name}\"" );
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
		_streamRevision++;
		_loadedChunks.Clear();
		_desiredChunks.Clear();
		_pendingChunks.Clear();
		_completedChunks.Clear();
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
		_streamingCenterCoordinate = center;
		_hasStreamingCenter = true;
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

		_coordinateBuffer.Clear();
		foreach ( var coordinate in _loadedChunks.Keys )
		{
			if ( !_desiredChunks.Contains( coordinate ) )
			{
				_coordinateBuffer.Add( coordinate );
			}
		}

		var unloadedCount = _coordinateBuffer.Count;
		foreach ( var coordinate in _coordinateBuffer )
		{
			if ( LogChunkLifecycle && _loadedChunks.TryGetValue( coordinate, out var chunk ) )
			{
				Log.Info( $"[VoxelWorld] chunk.unload chunk={chunk.LogId} name=\"{chunk.HumanName}\"" );
			}

			_loadedChunks.Remove( coordinate );
		}

		_coordinateBuffer.Clear();
		foreach ( var coordinate in _desiredChunks )
		{
			if ( !_loadedChunks.ContainsKey( coordinate ) )
			{
				_coordinateBuffer.Add( coordinate );
			}
		}

		_coordinateBuffer.Sort( ( left, right ) =>
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

		_pendingChunks.Clear();
		foreach ( var coordinate in _coordinateBuffer )
		{
			_pendingChunks.Enqueue( coordinate );
		}

		_generatedThisStream = 0;
		_retainedThisStream = _loadedChunks.Count;
		_unloadedThisStream = unloadedCount;
		_staleDiscardedThisStream = 0;
		_generationMillisecondsThisStream = 0f;
		_integrationMillisecondsThisStream = 0f;
		_slowestIntegrationFrameMilliseconds = 0f;
		_maximumObservedFrameMilliseconds = 0f;
		_hasObservedStreamingFrame = false;
		_completionReady = false;
		SlowestChunkGenerationMilliseconds = 0f;
		LastBackgroundWorkerMilliseconds = 0f;
		_streamStartedTimestamp = Stopwatch.GetTimestamp();
		_streamInProgress = true;

		Log.Info(
			$"[VoxelWorld] stream.begin center=C[{center.x},{center.y},{center.z}] reason=\"{reason}\" " +
			$"loadRadius={LoadRadius} retained={_loadedChunks.Count} " +
			$"unloaded={unloadedCount} queued={_pendingChunks.Count} desired={_desiredChunks.Count}" );
		RefreshReadableStatus();

		if ( _pendingChunks.Count == 0 )
		{
			_generationCancellation?.Cancel();
			_streamRevision++;
			_completedChunks.Clear();
			_workerCompleted = true;
			CompleteStream();
			return;
		}

		StartBackgroundGeneration( _coordinateBuffer.ToArray() );
	}

	private void StartBackgroundGeneration( Vector3Int[] coordinates )
	{
		_generationCancellation?.Cancel();
		var previousTask = _generationTask;
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

			var batch = await Task.RunInThreadAsync( () =>
			{
				var workerStart = Stopwatch.GetTimestamp();
				var chunks = new List<VoxelChunk>( coordinates.Length );
				var generationMilliseconds = 0f;
				var lastChunkMilliseconds = 0f;
				var slowestChunkMilliseconds = 0f;
				foreach ( var coordinate in coordinates )
				{
					if ( cancellationToken.IsCancellationRequested )
					{
						break;
					}

					var generationStart = Stopwatch.GetTimestamp();
					var chunk = new VoxelChunk( coordinate, cellsPerAxis, cellSize, terrainSurfaceHeight );
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
				if ( batch.Chunks.Count > 0 )
				{
					Log.Info(
						$"[VoxelWorld] stream.stale revision={revision} currentRevision={_streamRevision} " +
						$"discarded={batch.Chunks.Count}" );
				}
				return;
			}

			LastBackgroundWorkerMilliseconds = batch.WorkerMilliseconds;
			_generationMillisecondsThisStream = batch.GenerationMilliseconds;
			LastChunkGenerationMilliseconds = batch.LastChunkMilliseconds;
			SlowestChunkGenerationMilliseconds = batch.SlowestChunkMilliseconds;
			foreach ( var chunk in batch.Chunks )
			{
				_completedChunks.Enqueue( chunk );
			}

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
			Log.Error( $"[VoxelWorld] stream.failed revision={revision} error=\"{exception.Message}\"" );
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
				_loadedChunks.Add( chunk.Coordinate, chunk );
				integratedCount++;
				_generatedThisStream++;

				if ( LogChunkLifecycle )
				{
					Log.Info(
						$"[VoxelWorld] chunk.load chunk={chunk.LogId} name=\"{chunk.HumanName}\" samples={chunk.SampleCount} " +
						$"densityMin={chunk.MinimumDensity} densityMax={chunk.MaximumDensity}" );
				}
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
		var processMemoryBytes = global::Sandbox.Diagnostics.PerformanceStats.ApproximateProcessMemoryUsage;
		LastStreamSummary =
			$"Loaded {_loadedChunks.Count}; retained {_retainedThisStream}; unloaded {_unloadedThisStream}; " +
			$"generated {_generatedThisStream}; stale {_staleDiscardedThisStream}; " +
			$"{LastEffectiveChunksPerSecond:0.0} chunks/sec effective; " +
			$"{LastGenerationChunksPerSecond:0.0} chunks/sec generation";
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

	private void RefreshReadableStatus()
	{
		LoadedChunkCount = _loadedChunks.Count;
		PendingChunkCount = _pendingChunks.Count;
		ChunkStatus = _performanceSnapshotReady
			? $"{LoadedChunkCount:N0} loaded; {PendingChunkCount:N0} queued; " +
				$"{_lastPerformanceChunksPerSecond:N1} chunks/sec over {_lastPerformanceWindowSeconds:N1} sec"
			: $"{LoadedChunkCount:N0} loaded; {PendingChunkCount:N0} queued";
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

	private void DrawDebugOverlay()
	{
		if ( !ShowLoadedChunkBounds && !ShowLoadedChunkLabels )
		{
			return;
		}

		var chunkWorldSize = CellsPerAxis * CellSize;
		var playerCoordinate = WorldToChunkCoordinate( ActiveStreamingTarget.WorldPosition );
		foreach ( var chunk in _loadedChunks.Values )
		{
			var minimum = new Vector3(
				chunk.Coordinate.x * chunkWorldSize,
				chunk.Coordinate.y * chunkWorldSize,
				chunk.Coordinate.z * chunkWorldSize );
			var maximum = minimum + new Vector3( chunkWorldSize );
			var isPlayerChunk = chunk.Coordinate == playerCoordinate;
			var color = isPlayerChunk ? Color.Yellow : Color.Cyan;

			if ( ShowLoadedChunkBounds )
			{
				DebugOverlay.Box( new BBox( minimum, maximum ), color, 0f, global::Transform.Zero, true );
			}

			if ( ShowLoadedChunkLabels )
			{
				DebugOverlay.Text( (minimum + maximum) * 0.5f, chunk.HumanName, 18f, TextFlag.Center, color, 0f, true );
			}
		}
	}
}
