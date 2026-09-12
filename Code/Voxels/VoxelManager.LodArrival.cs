using System;
using System.Diagnostics;
using System.Text.Json;

public sealed partial class VoxelManager
{
	private const double LodArrivalSampleSeconds = 1;
	private const double LodArrivalWarningSeconds = 5;
	private const double LodArrivalLimitSeconds = 240;
	private const int LodArrivalMaximumSamples = 256;
	private const float LodArrivalMovementDistance = 16;
	private long _lodArrivalMotionTime;
	private long _lodArrivalNextSample;
	private long _lodPlacementStartTime;
	private Vector3 _lodArrivalMotionPosition;
	private Guid _lodArrivalWorld;
	private long _lodArrivalEpoch;
	private long _lodArrivalRevision;
	private long _lodArrivalConfiguration;
	private bool _lodArrivalExamined;
	private bool _lodArrivalSawMovement;
	private bool _lodArrivalHasSettled;
	private LodArrivalEpisode _lodArrival;
	private LodArrivalReport _lodArrivalLastReport;
	private LodArrivalReport _lodArrivalPendingReport;
	private System.Threading.Tasks.Task<string> _lodArrivalWrite;
	private double _lodArrivalNextWarning;
	private long _lodArrivalCoalescedWrites;

	[Property, ReadOnly, Category( "Diagnostics" )]
	public string LodArrivalStatus { get; private set; } = "Waiting for streaming target";
	[Property, ReadOnly, Category( "Diagnostics" )]
	public string LodArrivalReportPath { get; private set; } = "No arrival report saved";

	private void UpdateLodArrivalReporting()
	{
		PollLodArrivalWrite();
		if ( _gpuMesher is null || !_hasStreamingCenter ) return;
		var now = Stopwatch.GetTimestamp();
		var position = ActiveStreamingTarget.WorldPosition;
		if ( _lodArrivalMotionTime == 0 || _lodArrivalWorld != CurrentField.WorldId ||
			_lodArrivalEpoch != CurrentField.Epoch || _lodArrivalRevision != CurrentField.Revision ||
			_lodArrivalConfiguration != _requestedVisualConfigurationRevision )
		{
			FinishLodArrival( "context-changed" );
			_lodArrivalWorld = CurrentField.WorldId;
			_lodArrivalEpoch = CurrentField.Epoch;
			_lodArrivalRevision = CurrentField.Revision;
			_lodArrivalConfiguration = _requestedVisualConfigurationRevision;
			_lodArrivalMotionTime = now;
			_lodArrivalMotionPosition = position;
			_lodArrivalExamined = false;
			_lodArrivalSawMovement = false;
			_lodArrivalHasSettled = false;
		}
		if ( (position - _lodArrivalMotionPosition).LengthSquared >= LodArrivalMovementDistance * LodArrivalMovementDistance )
		{
			FinishLodArrival( "movement-resumed" );
			_lodArrivalMotionTime = now;
			_lodArrivalMotionPosition = position;
			_lodArrivalExamined = false;
			_lodArrivalSawMovement = true;
		}
		if ( now < _lodArrivalNextSample ) return;
		_lodArrivalNextSample = now + (long)(Stopwatch.Frequency * LodArrivalSampleSeconds);
		var quiet = Stopwatch.GetElapsedTime( _lodArrivalMotionTime, now ).TotalSeconds;
		if ( quiet < LodArrivalSampleSeconds )
		{
			LodArrivalStatus = "Moving; arrival timer starts when movement stops";
			return;
		}
		if ( _lodArrivalExamined && _lodArrival is null ) return;
		using var profiler = global::Sandbox.Diagnostics.Performance.Scope( VoxelPerformanceProfiler.LodArrivalReporting );
		var near = CaptureNearChunkReadiness();
		if ( _lodArrival is null )
		{
			_lodArrivalExamined = true;
			if ( _appliedVisualConfiguration.MinimumVisualLod != 0 )
			{
				LodArrivalStatus = "LOD0 is disabled by the visual configuration";
				return;
			}
			if ( near.Presented == near.Requested && !near.PlacementPending && near.VisualPending == 0 && near.TransitionPending == 0 )
			{
				LodArrivalStatus = "Nearby LOD0 ready; streaming settled";
				_lodArrivalHasSettled = true;
				return;
			}
			_lodArrival = new LodArrivalEpisode
			{
				Id = Guid.NewGuid().ToString( "N" ),
				Kind = !_lodArrivalHasSettled ? "before-first-settle" : _lodArrivalSawMovement ? "stationary-arrival" : "watch-start",
				StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds( -quiet ).ToString( "O" ),
				StartTimestamp = _lodArrivalMotionTime,
				World = CurrentField.WorldId, Epoch = CurrentField.Epoch, Revision = CurrentField.Revision,
				Settings = CurrentTerrainSettings, Configuration = _appliedVisualConfiguration,
				Position = position, CommitsAtStart = _clipboxPlacementCommits,
				SupersededAtStart = _clipboxPlacementSuperseded
			};
			_lodArrivalNextWarning = LodArrivalWarningSeconds;
		}
		var elapsed = Stopwatch.GetElapsedTime( _lodArrival.StartTimestamp, now ).TotalSeconds;
		var detailed = _lodArrival.Samples.Count == 0 || elapsed >= _lodArrivalNextWarning;
		var sample = CaptureLodArrivalSample( elapsed, near, detailed );
		_lodArrival.Samples.Add( sample );
		foreach ( var level in sample.Levels )
		{
			if ( level.Presented ) _lodArrival.FirstTargetPresentedSeconds[level.Level] ??= elapsed;
		}
		if ( near.TerrainPrepared == near.Requested ) _lodArrival.FirstPreparedSeconds ??= elapsed;
		if ( near.Presented == near.Requested ) _lodArrival.FirstPresentedSeconds ??= elapsed;
		LodArrivalStatus = $"Stationary {elapsed:0.0}s: nearby LOD0 {near.Presented}/{near.Requested}; " +
			$"prepared {near.TerrainPrepared}; pending meshes {near.VisualPending}, seams {near.TransitionPending}";
		if ( !near.PlacementPending && near.VisualPending == 0 && near.TransitionPending == 0 && near.Presented == near.Requested )
		{
			_lodArrivalHasSettled = true;
			FinishLodArrival( "settled" );
		}
		else if ( elapsed >= LodArrivalLimitSeconds || _lodArrival.Samples.Count >= LodArrivalMaximumSamples )
		{
			FinishLodArrival( "observation-limit-incomplete" );
		}
		else if ( elapsed >= _lodArrivalNextWarning )
		{
			_lodArrivalNextWarning = elapsed + LodArrivalWarningSeconds;
			var message = $"[VoxelWorld] lod.arrival.wait id={_lodArrival.Id} {LodArrivalStatus} " +
				$"nearPreparationMissing={near.PreparationMissing} nearMeshMissing={near.MeshMissing} " +
				$"nearPublicationBlocked={near.PublicationBlocked} nearWaterBlocked={near.WaterBlocked} " +
				$"stagedFine={sample.Levels[0].StagedAnchor} missingLevels=[{string.Join( ',', sample.MissingLevels )}] " +
				$"missingWater={sample.MissingWater} missingSeams={sample.MissingTransitions}";
			if ( near.Presented < near.Requested ) Log.Warning( message );
			else Log.Info( message );
			QueueLodArrivalReport( CreateLodArrivalReport( "waiting" ) );
		}
	}

	private NearChunkReadiness CaptureNearChunkReadiness()
	{
		var center = _streamingCenterCoordinate;
		var preparedCount = 0;
		var presented = 0;
		var preparationMissing = 0;
		var meshMissing = 0;
		var publicationBlocked = 0;
		var waterBlocked = 0;
		for ( var z = center.z - 1; z <= center.z + 1; z++ )
		for ( var y = center.y - 1; y <= center.y + 1; y++ )
		for ( var x = center.x - 1; x <= center.x + 1; x++ )
		{
			var coordinate = new Vector3Int( x, y, z );
			if ( !_renderPreparedChunks.Contains( coordinate ) ) { preparationMissing++; continue; }
			var descriptor = CreateRegularDescriptor( 0, coordinate, captureRegion: false );
			var empty = !_gpuMesher.Contains( descriptor.Key );
			if ( !empty && !_gpuMesher.IsResident( descriptor ) ) { meshMissing++; continue; }
			preparedCount++;
			if ( !_levels[0].Active.Contains( coordinate ) || !empty && !_gpuMesher.IsRegionPresented( descriptor ) )
			{
				publicationBlocked++;
				continue;
			}
			var size = descriptor.CellsPerAxis * descriptor.CellSize;
			var waterZ = (int)MathF.Ceiling( descriptor.TerrainSettings.SeaLevel / size ) - 1;
			if ( z == waterZ && (!_waterCoverage.TryGetValue( descriptor.Key, out var water ) || water.Descriptor != descriptor) )
			{
				waterBlocked++;
				continue;
			}
			presented++;
		}
		return new( center, 27, preparedCount, presented, _gpuMesher.AllPendingCount,
			_gpuMesher.TransitionPendingCount, HasClipboxPlacementWork,
			preparationMissing, meshMissing, publicationBlocked, waterBlocked );
	}

	private LodArrivalSample CaptureLodArrivalSample( double elapsed, NearChunkReadiness near, bool detailed )
	{
		var position = ActiveStreamingTarget.WorldPosition;
		var levels = new LodArrivalLevel[_appliedVisualConfiguration.MaximumVisualLod + 1];
		for ( var level = 0; level < levels.Length; level++ )
		{
			var size = CellSizeForLevel( level ) * _appliedCellsPerAxis;
			var coordinate = new Vector3Int( (int)MathF.Floor( position.x / size ),
				(int)MathF.Floor( position.y / size ), (int)MathF.Floor( position.z / size ) );
			var descriptor = CreateRegularDescriptor( level, coordinate, captureRegion: false );
			var state = _levels[level];
			var uniform = level == 0 && _renderPreparedChunks.Contains( coordinate ) && !_gpuMesher.Contains( descriptor.Key );
			var active = state.Active.Contains( coordinate ) || level == _partialOuterLevel && _partialOuterChunks.Contains( coordinate );
			levels[level] = new( level, coordinate, state.OuterAnchor, state.StagedOuterAnchor,
				state.OuterMinimum, state.OuterMaximum, _targetLevelAnchors[level], active,
				active && (uniform || _gpuMesher.IsRegionPresented( descriptor )),
				uniform ? new GpuRegionWorkStatus( "uniform-ready", 0 ) : _gpuMesher.InspectRegionWork( descriptor ),
				_gpuMesher.PendingLevelCount( level ) );
		}
		var readiness = detailed ? CapturePendingClipboxReadiness() : default;
		var player = Scene.GetAllComponents<PlayerController>().FirstOrDefault( value => !value.IsProxy && value.GameObject == ActiveStreamingTarget );
		Vector3? hitPosition = null;
		int? viewHitLevel = null;
		bool? viewHitInsideDesiredFine = null;
		if ( player is not null )
		{
			var hit = Scene.Trace.Ray( player.EyePosition,
				player.EyePosition + player.EyeTransform.Rotation.Forward * (RequiredCellsPerAxis * RequiredBaseCellSize * VisualChunkRadiusForConfiguration( _appliedVisualConfiguration )) )
				.WithTag( "voxel_terrain" ).Run();
			if ( hit.Hit )
			{
				hitPosition = hit.HitPosition;
				viewHitLevel = -1;
				for ( var level = 0; level < levels.Length; level++ )
				{
					var size = CellSizeForLevel( level ) * _appliedCellsPerAxis;
					var coordinate = new Vector3Int( (int)MathF.Floor( hit.HitPosition.x / size ),
						(int)MathF.Floor( hit.HitPosition.y / size ), (int)MathF.Floor( hit.HitPosition.z / size ) );
					if ( level == 0 )
					{
						var anchor = TargetOuterAnchor( 0, _targetVisualConfiguration );
						var extent = new Vector3Int( _targetVisualConfiguration.Lod0VisualHalfExtent );
						viewHitInsideDesiredFine = _targetVisualConfiguration.MinimumVisualLod == 0 &&
							IsInsideHalfOpenBox( coordinate, anchor - extent, anchor + extent );
					}
					if ( viewHitLevel == -1 && (_levels[level].Active.Contains( coordinate ) ||
						level == _partialOuterLevel && _partialOuterChunks.Contains( coordinate )) &&
						_gpuMesher.IsRegionPresented( CreateRegularDescriptor( level, coordinate, captureRegion: false ) ) ) viewHitLevel = level;
				}
			}
		}
		return new( elapsed, DateTimeOffset.UtcNow.ToString( "O" ), position, near, levels,
			detailed, readiness.MissingLevels?.ToArray() ?? Array.Empty<int>(),
			detailed ? readiness.MissingTransitions : -1, detailed ? readiness.MissingWater : -1,
			_clipboxPreparation is not null, SurfaceWaterPrepared,
			_clipboxPlacementPending && _lodPlacementStartTime != 0 ? Stopwatch.GetElapsedTime( _lodPlacementStartTime ).TotalSeconds : 0,
			_clipboxPlacementCommits, _clipboxPlacementSuperseded,
			player?.EyePosition, player?.EyeTransform.Rotation.Forward, hitPosition, viewHitLevel, viewHitInsideDesiredFine );
	}

	[ConCmd( "voxel_lod_report" )]
	public static void LogLodArrivalReportCommand()
	{
		if ( !TryGetActiveManager( "lod.arrival.report", out var manager ) || manager._gpuMesher is null ) return;
		var near = manager.CaptureNearChunkReadiness();
		var current = manager.CaptureLodArrivalSample( manager._lodArrival is null ? 0 :
			Stopwatch.GetElapsedTime( manager._lodArrival.StartTimestamp ).TotalSeconds, near, detailed: true );
		Log.Info( "[VoxelWorld] lod.arrival.inspect " + JsonSerializer.Serialize( new
		{
			manager.LodArrivalStatus, manager.LodArrivalReportPath, Current = current,
			LastReport = manager._lodArrivalLastReport?.Id, manager._lodArrivalCoalescedWrites
		}, PerformanceJsonOptions ) );
		if ( manager._lodArrival is not null ) manager.QueueLodArrivalReport( manager.CreateLodArrivalReport( "manual-snapshot" ) );
		else
		{
			manager.QueueLodArrivalReport( new LodArrivalReport( 1, Guid.NewGuid().ToString( "N" ),
				"manual-snapshot", "snapshot-only-no-arrival-timing", DateTimeOffset.UtcNow.ToString( "O" ), 0,
				manager.CurrentField.WorldId, manager.CurrentField.Epoch, manager.CurrentField.Revision,
				manager.CurrentTerrainSettings, manager._appliedVisualConfiguration, current.Position,
				manager._clipboxPlacementCommits, manager._clipboxPlacementSuperseded, null, null,
				new double?[SupportedVisualLevelCount], new[] { current },
				manager._playerFigureEightTestTask, manager._playerFigureEightTestRevision,
				(int)global::Sandbox.Screen.Width, (int)global::Sandbox.Screen.Height, manager._lodArrivalCoalescedWrites ) );
		}
	}

	private void FinishLodArrival( string outcome )
	{
		if ( _lodArrival is null ) return;
		var report = CreateLodArrivalReport( outcome );
		_lodArrivalLastReport = report;
		QueueLodArrivalReport( report );
		LodArrivalStatus = $"{outcome}: nearby LOD0 first observed at {report.FirstPresentedSeconds?.ToString( "0.0" ) ?? "unobserved"}s; {report.ElapsedSeconds:0.0}s observed";
		Log.Info( $"[VoxelWorld] lod.arrival.complete id={report.Id} {LodArrivalStatus}" );
		_lodArrival = null;
	}

	private LodArrivalReport CreateLodArrivalReport( string outcome ) => new(
		1, _lodArrival.Id, _lodArrival.Kind, outcome, _lodArrival.StartedAtUtc,
		Stopwatch.GetElapsedTime( _lodArrival.StartTimestamp ).TotalSeconds,
		_lodArrival.World, _lodArrival.Epoch, _lodArrival.Revision, _lodArrival.Settings, _lodArrival.Configuration,
		_lodArrival.Position, _lodArrival.CommitsAtStart, _lodArrival.SupersededAtStart,
		_lodArrival.FirstPreparedSeconds, _lodArrival.FirstPresentedSeconds,
		_lodArrival.FirstTargetPresentedSeconds.ToArray(),
		_lodArrival.Samples.ToArray(), _playerFigureEightTestTask, _playerFigureEightTestRevision,
		(int)global::Sandbox.Screen.Width, (int)global::Sandbox.Screen.Height, _lodArrivalCoalescedWrites );

	private void QueueLodArrivalReport( LodArrivalReport report )
	{
		if ( _lodArrivalPendingReport is not null )
		{
			_lodArrivalCoalescedWrites++;
			Log.Warning( $"[VoxelWorld] lod.arrival.report.coalesced previous={_lodArrivalPendingReport.Id} latest={report.Id} count={_lodArrivalCoalescedWrites}" );
		}
		_lodArrivalPendingReport = report;
		PollLodArrivalWrite();
	}

	private void PollLodArrivalWrite()
	{
		if ( _lodArrivalWrite is not null )
		{
			if ( !_lodArrivalWrite.IsCompleted ) return;
			if ( _lodArrivalWrite.IsCompletedSuccessfully )
			{
				LodArrivalReportPath = _lodArrivalWrite.Result;
				Log.Info( $"[VoxelWorld] lod.arrival.report.saved path=\"{LodArrivalReportPath}\"" );
			}
			else Log.Warning( $"[VoxelWorld] lod.arrival.report.failed {_lodArrivalWrite.Exception}" );
			_lodArrivalWrite = null;
		}
		if ( _lodArrivalPendingReport is null ) return;
		var report = _lodArrivalPendingReport;
		_lodArrivalPendingReport = null;
		_lodArrivalWrite = GameTask.RunInThreadAsync( () => WriteLodArrivalReport( report ) );
	}

	private static string WriteLodArrivalReport( LodArrivalReport report )
	{
		const string directory = "performance/lod-arrivals";
		var path = $"{directory}/{report.Id}.json";
		global::Sandbox.FileSystem.Data.CreateDirectory( directory );
		global::Sandbox.FileSystem.Data.WriteAllText( path, JsonSerializer.Serialize( report, PerformanceJsonOptions ) );
		return global::Sandbox.FileSystem.Data.GetFullPath( path ) ?? path;
	}

	private void FinishLodArrivalOnDestroy()
	{
		FinishLodArrival( "component-destroyed" );
		if ( _lodArrivalPendingReport is null ) return;
		var report = _lodArrivalPendingReport;
		_lodArrivalPendingReport = null;
		_ = SaveRemainingLodArrivalAsync( _lodArrivalWrite, report );
	}

	private static async System.Threading.Tasks.Task SaveRemainingLodArrivalAsync(
		System.Threading.Tasks.Task<string> previous, LodArrivalReport report )
	{
		try { if ( previous is not null ) await previous; }
		catch ( Exception error ) { Log.Warning( $"[VoxelWorld] lod.arrival.report.failed {error.Message}" ); }
		try
		{
			var path = await GameTask.RunInThreadAsync( () => WriteLodArrivalReport( report ) );
			Log.Info( $"[VoxelWorld] lod.arrival.report.saved path=\"{path}\"" );
		}
		catch ( Exception error ) { Log.Warning( $"[VoxelWorld] lod.arrival.report.failed {error.Message}" ); }
	}

	private sealed class LodArrivalEpisode
	{
		public string Id, Kind, StartedAtUtc;
		public Guid World;
		public long StartTimestamp, Epoch, Revision, CommitsAtStart, SupersededAtStart;
		public ProceduralTerrainSettings Settings;
		public VoxelVisualConfiguration Configuration;
		public Vector3 Position;
		public double? FirstPreparedSeconds, FirstPresentedSeconds;
		public readonly double?[] FirstTargetPresentedSeconds = new double?[SupportedVisualLevelCount];
		public readonly List<LodArrivalSample> Samples = new( LodArrivalMaximumSamples );
	}

	private sealed record LodArrivalReport( int SchemaVersion, string Id, string Kind, string Outcome, string StartedAtUtc,
		double ElapsedSeconds, Guid World, long Epoch, long Revision, ProceduralTerrainSettings Settings,
		VoxelVisualConfiguration Configuration, Vector3 ArrivalPosition, long CommitsAtStart, long SupersededAtStart,
		double? FirstPreparedSeconds, double? FirstPresentedSeconds, double?[] FirstTargetPresentedSecondsByLod, LodArrivalSample[] Samples,
		string PerformanceTask, string PerformanceRevision, int ScreenWidth, int ScreenHeight, long CoalescedWrites );
	private sealed record LodArrivalSample( double ElapsedSeconds, string CapturedAtUtc, Vector3 Position,
		NearChunkReadiness Near, LodArrivalLevel[] Levels, bool DetailedReadiness, int[] MissingLevels,
		int MissingTransitions, int MissingWater, bool PreparingPlacement, bool WaterReady, double PlacementAgeSeconds,
		long Commits, long Superseded, Vector3? EyePosition, Vector3? EyeForward, Vector3? CollisionOnlyViewHit,
		int? CollisionOnlyViewHitPresentedLod, bool? ViewHitInsideDesiredLod0 );
	private sealed record LodArrivalLevel( int Level, Vector3Int TargetRegion, Vector3Int CommittedAnchor,
		Vector3Int StagedAnchor, Vector3Int OuterMinimum, Vector3Int OuterMaximum, Vector3Int RequestedAnchor,
		bool Active, bool Presented, GpuRegionWorkStatus Work, int Pending );
	private readonly record struct NearChunkReadiness( Vector3Int Center, int Requested, int TerrainPrepared,
		int Presented, int VisualPending, int TransitionPending, bool PlacementPending,
		int PreparationMissing, int MeshMissing, int PublicationBlocked, int WaterBlocked );
}
