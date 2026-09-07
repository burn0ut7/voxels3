using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

public sealed partial class VoxelManager
{
	private DeformationBenchmark _deformationBenchmark;

	private sealed class DeformationObservation
	{
		public int Index { get; set; }
		public long RequestId { get; set; }
		public double ScheduledSeconds { get; set; }
		public double AttemptedSeconds { get; set; }
		public bool Accepted { get; set; }
		public string ToolStatus { get; set; }
		public bool CancelledBeforeCommit { get; set; }
		public int Revision { get; set; }
		public int ChangedSamples { get; set; }
		public int ChangedPages { get; set; }
		public double? FieldCommitMilliseconds { get; set; }
		public double? VisualPublicationMilliseconds { get; set; }
		public double? EditedVisualDrainMilliseconds { get; set; }
		public double? EditedCollisionDrainMilliseconds { get; set; }
		public double? ActorReleaseMilliseconds { get; set; }
		public long RequestedTimestamp;
	}

	private sealed class DeformationBenchmark
	{
		public string RunId = Guid.NewGuid().ToString( "N" );
		public string Scenario;
		public string Task;
		public string Revision;
		public int Count;
		public double Interval;
		public long Started = Stopwatch.GetTimestamp();
		public long WorkStarted;
		public long DrainedAt;
		public PlayerController Player;
		public Vector3 PlayerStart;
		public CameraComponent Camera;
		public Vector3 CameraStart;
		public Vector3 CameraForward;
		public bool PlayerInputEnabled;
		public bool PlayerLookEnabled;
		public bool PlayerEnabled;
		public int ControllerStateChanges;
		public bool DrivingPlayer;
		public Angles OriginalEyeAngles;
		public Vector3 LastPlayerPosition;
		public double PlayerTravelDistance;
		public float MaximumPlayerSpeed;
		public float MaximumCameraDisplacement;
		public float MaximumCameraAngle;
		public PerformanceStationaryMetrics Baseline;
		public readonly List<DeformationObservation> Observations = new();
		public readonly Dictionary<long, DeformationObservation> Requests = new();
		public readonly List<DeformationObservation> Pending = new();
		public int PeakQueue;
		public int PeakHeldBodies;
		public long StartingHoldSteps;
		public long PeakPageBytes;
		public long StartingCommitted;
		public long StartingRejected;
		public string Failure;
	}

	/// <summary>Explicit deformation-only workload; never invoked by tool mouse input.</summary>
	[ConCmd( "voxel_deformation_benchmark" )]
	public static void DeformationBenchmarkCommand( string scenario, string task, string revision )
	{
		if ( !TryGetActiveManager( "deformation.benchmark", out var manager ) ) return;
		try { manager.StartDeformationBenchmark( scenario, task, revision ); }
		catch ( Exception exception ) { Log.Warning( $"[DeformationBenchmark] rejected reason={exception.Message}" ); }
	}

	private void StartDeformationBenchmark( string scenario, string task, string revision )
	{
		if ( !Networking.IsHost || _deformationBenchmark is not null || _playerFigureEightEnabled ||
			_playerFigureEightTestRunning || _performanceVisibilityPending || _performanceCompletionPhase != PerformanceCompletionPhase.None ||
			_terrainSaveTask is not null || _terrainEditTask is not null || _terrainEditQueue.Count > 0 || CurrentField.Revision != 0 ||
			!_collision.Settled || _gpuMesher.AllPendingCount > 0 || _gpuMesher.TransitionPendingCount > 0 || HasClipboxPlacementWork )
			throw new InvalidOperationException( "Start on a fresh, fully settled host world with no other test or terrain operation active." );
		var workload = scenario switch
		{
			"tool-stationary" or "tool-cycle" or "tool-sweep" or "sprint-look" or "occupied" => (600, 0.1),
			"overload" => (600, 0.025),
			"large" or "distant" or "underground" or "boundaries" => (12, 0.5),
			_ => throw new ArgumentException( "Unknown deformation scenario." )
		};
		var player = Scene.GetAllComponents<PlayerController>().FirstOrDefault( value => !value.IsProxy );
		if ( !player.IsValid() || MathF.Abs( player.WorldPosition.x ) > 1f || MathF.Abs( player.WorldPosition.y ) > 1f )
			throw new InvalidOperationException( "The benchmark requires the player at the unchanged spawn; it never moves the player or camera." );
		_deformationBenchmark = new DeformationBenchmark
		{
			Scenario = scenario, Task = RequirePerformanceContext( task, nameof( task ) ),
			Revision = RequirePerformanceContext( revision, nameof( revision ) ), Count = workload.Item1, Interval = workload.Item2,
			Player = player, PlayerStart = player.WorldPosition, StartingHoldSteps = _collisionHoldSteps, StartingCommitted = _terrainEditsCommitted, StartingRejected = _terrainEditsRejected
		};
		ResetPerformanceWindow();
		SamplePerformanceMemory();
		Log.Info( $"[DeformationBenchmark] started runId={_deformationBenchmark.RunId} scenario={scenario} warmupSeconds=10 attempts={workload.Item1}" );
	}

	private void RecordDeformationCommit( long requestId, TerrainFieldChange change )
	{
		var benchmark = _deformationBenchmark;
		if ( benchmark is null ) return;
		if ( !benchmark.Requests.TryGetValue( requestId, out var observation ) )
		{
			benchmark.Failure = "An external terrain mutation contaminated the workload.";
			return;
		}
		observation.Revision = change.Result.Revision;
		observation.ChangedSamples = change.ChangedSamples;
		observation.ChangedPages = change.ChangedPages.Length;
		observation.FieldCommitMilliseconds = Stopwatch.GetElapsedTime( observation.RequestedTimestamp ).TotalMilliseconds;
		if ( change.ChangedSamples > 0 ) benchmark.Pending.Add( observation );
	}

	private void UpdateDeformationBenchmark()
	{
		var benchmark = _deformationBenchmark;
		if ( benchmark is null ) return;
		if ( !benchmark.Player.IsValid() || !benchmark.DrivingPlayer && (benchmark.Player.WorldPosition - benchmark.PlayerStart).Length > 2f )
			benchmark.Failure = "Player moved during the stationary workload.";
		if ( benchmark.Failure is not null )
		{
			FinishDeformationBenchmark( benchmark.Failure );
			return;
		}
		if ( benchmark.WorkStarted == 0 )
		{
			if ( Stopwatch.GetElapsedTime( benchmark.Started ).TotalSeconds < 10 ) return;
			CaptureStationaryPerformanceWindow();
			benchmark.Baseline = _lastStationaryMetrics;
			ResetPerformanceWindow(); SamplePerformanceMemory();
			_collision.BeginMeasurement();
			benchmark.WorkStarted = Stopwatch.GetTimestamp();
			benchmark.Camera = Scene.Camera;
			benchmark.CameraStart = benchmark.Camera.WorldPosition;
			benchmark.CameraForward = benchmark.Camera.WorldRotation.Forward;
			benchmark.PlayerInputEnabled = benchmark.Player.UseInputControls;
			benchmark.PlayerLookEnabled = benchmark.Player.UseLookControls;
			benchmark.PlayerEnabled = benchmark.Player.Enabled;
			benchmark.LastPlayerPosition = benchmark.Player.WorldPosition;
			if ( benchmark.Scenario == "sprint-look" )
			{
				benchmark.OriginalEyeAngles = benchmark.Player.EyeAngles;
				benchmark.Player.UseInputControls = false;
				benchmark.Player.UseLookControls = false;
				benchmark.DrivingPlayer = true;
			}
		}
		if ( benchmark.Player.UseInputControls != (benchmark.DrivingPlayer ? false : benchmark.PlayerInputEnabled) ||
			benchmark.Player.UseLookControls != (benchmark.DrivingPlayer ? false : benchmark.PlayerLookEnabled) || benchmark.Player.Enabled != benchmark.PlayerEnabled )
			benchmark.ControllerStateChanges++;
		if ( benchmark.Camera.IsValid() )
		{
			benchmark.MaximumCameraDisplacement = Math.Max( benchmark.MaximumCameraDisplacement,
				(benchmark.Camera.WorldPosition - benchmark.CameraStart).Length );
			var cosine = Math.Clamp( Vector3.Dot( benchmark.CameraForward, benchmark.Camera.WorldRotation.Forward ), -1f, 1f );
			benchmark.MaximumCameraAngle = Math.Max( benchmark.MaximumCameraAngle, MathF.Acos( cosine ) * (180f / MathF.PI) );
		}
		else benchmark.Failure = "The active camera was destroyed during the workload.";
		benchmark.PeakQueue = Math.Max( benchmark.PeakQueue, _terrainEditQueue.Count );
		benchmark.PeakHeldBodies = Math.Max( benchmark.PeakHeldBodies, _heldTerrainBodies.Count );
		benchmark.PeakPageBytes = Math.Max( benchmark.PeakPageBytes, CurrentField.PageBytes );
		for ( var pendingIndex = benchmark.Pending.Count - 1; pendingIndex >= 0; pendingIndex-- )
		{
			var pending = benchmark.Pending[pendingIndex];
			var elapsed = Stopwatch.GetElapsedTime( pending.RequestedTimestamp ).TotalMilliseconds;
			if ( _gpuMesher.LastFieldPublicationRevision >= pending.Revision )
				pending.VisualPublicationMilliseconds ??= Stopwatch.GetElapsedTime( pending.RequestedTimestamp,
					_gpuMesher.LastFieldPublicationTimestamp ).TotalMilliseconds;
			if ( !_gpuMesher.EditRebuildPending ) pending.EditedVisualDrainMilliseconds ??= elapsed;
			if ( !_collision.EditRebuildPending ) pending.EditedCollisionDrainMilliseconds ??= elapsed;
			if ( pending.EditedCollisionDrainMilliseconds.HasValue && _heldTerrainBodies.Count == 0 ) pending.ActorReleaseMilliseconds ??= elapsed;
			if ( pending.EditedVisualDrainMilliseconds.HasValue && pending.EditedCollisionDrainMilliseconds.HasValue &&
				pending.ActorReleaseMilliseconds.HasValue ) benchmark.Pending.RemoveAt( pendingIndex );
		}
		var seconds = Stopwatch.GetElapsedTime( benchmark.WorkStarted ).TotalSeconds;
		benchmark.PlayerTravelDistance += (benchmark.Player.WorldPosition - benchmark.LastPlayerPosition).Length;
		benchmark.LastPlayerPosition = benchmark.Player.WorldPosition;
		benchmark.MaximumPlayerSpeed = Math.Max( benchmark.MaximumPlayerSpeed, benchmark.Player.Body.Velocity.Length );
		if ( benchmark.DrivingPlayer )
		{
			var active = seconds < benchmark.Count * benchmark.Interval;
			// This explicit benchmark drives the standard controller, never the transform or physics body.
			benchmark.Player.WishVelocity = active ? new Vector3( MathF.Cos( (float)seconds * 0.5f ), MathF.Sin( (float)seconds * 0.5f ), 0f ) * 320f : Vector3.Zero;
			if ( active ) benchmark.Player.EyeAngles = new Angles( 30f + 25f * MathF.Sin( (float)seconds * 2f ), (float)seconds * 180f, 0f );
		}
		var index = benchmark.Observations.Count;
		if ( index < benchmark.Count && seconds >= index * benchmark.Interval )
		{
			var timestamp = Stopwatch.GetTimestamp();
			long requestId;
			bool accepted;
			if ( benchmark.Scenario is "tool-stationary" or "tool-cycle" or "tool-sweep" or "sprint-look" or "overload" )
			{
				var aim = new Vector3( 512f, benchmark.Scenario == "tool-sweep" ? MathF.Sin( index * 0.05f ) * 256f : 0f, 0f );
				accepted = TryUseTerrainTool( benchmark.Player, benchmark.DrivingPlayer ? Rotation.From( benchmark.Player.EyeAngles ).Forward : (aim - benchmark.Player.EyePosition).Normal,
					(benchmark.Scenario is "tool-cycle" or "sprint-look" or "overload") && index % 20 >= 10, out requestId );
			}
			else
			{
				var center = benchmark.Scenario switch
				{
					"occupied" => new Vector3( -256f, 256f, 0f ),
					"distant" => new Vector3( 400000f, 300000f, 0f ),
					"underground" => new Vector3( 2048f, 0f, -1536f ),
					"boundaries" => new Vector3( -512f, -512f, 0f ),
					_ => new Vector3( 2048f, 0f, 0f )
				};
				var radius = benchmark.Scenario is "boundaries" or "occupied" ? 128f : benchmark.Scenario == "large" ? 1024f : 512f;
				var strength = benchmark.Scenario == "occupied" ? (index % 20 < 10 ? 64f : -64f) : (index % 2 == 0 ? 1024f : -1024f);
				accepted = TryQueueTerrainEdit( center, radius, strength, out requestId );
			}
			var observation = new DeformationObservation
			{
				Index = index, RequestId = requestId, ScheduledSeconds = index * benchmark.Interval,
				AttemptedSeconds = seconds, Accepted = accepted, RequestedTimestamp = timestamp,
				ToolStatus = benchmark.Scenario is "tool-stationary" or "tool-cycle" or "tool-sweep" or "sprint-look" or "overload" ? _terrainToolStatus : null
			};
			benchmark.Observations.Add( observation );
			if ( accepted ) benchmark.Requests.Add( requestId, observation );
		}
		if ( benchmark.Observations.Count == benchmark.Count && _terrainEditQueue.Count == 0 && _terrainEditTask is null &&
			!_gpuMesher.EditRebuildPending && !_collision.EditRebuildPending && benchmark.Pending.Count == 0 )
		{
			if ( benchmark.DrainedAt == 0 ) benchmark.DrainedAt = Stopwatch.GetTimestamp();
			if ( Stopwatch.GetElapsedTime( benchmark.DrainedAt ).TotalSeconds >= 10 ) FinishDeformationBenchmark( null );
		}
		if ( _deformationBenchmark is not null && seconds > benchmark.Count * benchmark.Interval + 60 )
			FinishDeformationBenchmark( "Workload exceeded its 60-second final drain allowance; unfinished observations are retained." );
	}

	[ConCmd( "voxel_deformation_benchmark_stop" )]
	public static void StopDeformationBenchmarkCommand()
	{
		if ( TryGetActiveManager( "deformation.benchmark.stop", out var manager ) ) manager.FinishDeformationBenchmark( "Explicitly stopped." );
	}

	private void FinishDeformationBenchmark( string failure )
	{
		var benchmark = _deformationBenchmark;
		if ( benchmark is null ) return;
		if ( failure is not null )
		{
			foreach ( var observation in benchmark.Requests.Values )
				if ( !observation.FieldCommitMilliseconds.HasValue ) observation.CancelledBeforeCommit = true;
			var queued = _terrainEditQueue.Count;
			for ( var i = 0; i < queued; i++ )
			{
				var intent = _terrainEditQueue.Dequeue();
				if ( !benchmark.Requests.ContainsKey( intent.Id ) ) _terrainEditQueue.Enqueue( intent );
			}
			if ( _terrainEditTask is not null && benchmark.Requests.ContainsKey( _activeTerrainEdit.Id ) )
				_activeTerrainEditCancellation?.Cancel();
		}
		try
		{
			if ( _performanceFrameSampleCount > 0 && _performanceMemorySampleCount > 0 ) CaptureStationaryPerformanceWindow();
			else _lastStationaryMetrics = new PerformanceStationaryMetrics();
			var result = new
			{
				benchmark.RunId, Scenario = benchmark.Scenario + "/candidate-v2", benchmark.Task, benchmark.Revision,
				Failure = failure, Acceptance = "Unreviewed; this output alone is not feature acceptance.",
				Settings = CurrentField.Settings, GeneratorVersion = ProceduralTerrainSdf.CurrentVersion,
				PlayerStart = benchmark.PlayerStart, GameplayRadius = _appliedGameplayRadius, VisualRadius = VisualChunkRadius,
				benchmark.Baseline, WorkAndDrain = _lastStationaryMetrics, Collision = _collision.Capture(),
				Committed = _terrainEditsCommitted - benchmark.StartingCommitted, Rejected = _terrainEditsRejected - benchmark.StartingRejected,
				benchmark.PeakQueue, benchmark.PeakPageBytes, benchmark.PeakHeldBodies,
				benchmark.ControllerStateChanges, benchmark.MaximumCameraDisplacement, benchmark.MaximumCameraAngle,
				benchmark.PlayerTravelDistance, benchmark.MaximumPlayerSpeed,
				benchmark.PlayerInputEnabled, benchmark.PlayerLookEnabled, benchmark.PlayerEnabled,
				CameraStart = benchmark.CameraStart, CameraForward = benchmark.CameraForward,
				CollisionHoldSteps = _collisionHoldSteps - benchmark.StartingHoldSteps, Pending = _terrainEditQueue.Count + (_terrainEditTask is null ? 0 : 1),
				Observations = benchmark.Observations,
				LatencyMeaning = "Visual publication measures the coherent mesh group becoming publishable on the engine thread, not display scanout; if a later group supersedes it before observation, its timestamp is a conservative bound. Edited queue drain includes unrelated pending edited work. No-op edits retain null derivative timings; nonvisible edits must be excluded from visible latency summaries."
			};
			FileSystem.Data.CreateDirectory( "performance" );
			using var stream = FileSystem.Data.OpenWrite( "performance/deformation-results-v1.jsonl", FileMode.Append );
			var bytes = Encoding.UTF8.GetBytes( JsonSerializer.Serialize( result, PerformanceJsonOptions ) + "\n" );
			stream.Write( bytes, 0, bytes.Length );
			if ( Game.IsPlaying ) Log.Info( $"[DeformationBenchmark] saved runId={benchmark.RunId} failure={failure ?? "none"}" );
		}
		catch ( Exception exception ) { if ( Game.IsPlaying ) Log.Error( $"[DeformationBenchmark] save.failed runId={benchmark.RunId} reason={exception.Message}" ); }
		finally
		{
			if ( benchmark.DrivingPlayer && benchmark.Player.IsValid() )
			{
				benchmark.Player.WishVelocity = Vector3.Zero;
				benchmark.Player.EyeAngles = benchmark.OriginalEyeAngles;
				benchmark.Player.UseInputControls = benchmark.PlayerInputEnabled;
				benchmark.Player.UseLookControls = benchmark.PlayerLookEnabled;
			}
			_deformationBenchmark = null;
			ResetPerformanceWindow();
		}
	}
}
