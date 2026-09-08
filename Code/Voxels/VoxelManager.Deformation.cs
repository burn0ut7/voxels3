using System;
using System.Diagnostics;
using System.Threading;

public sealed partial class VoxelManager
{
	private const int MaximumQueuedTerrainEdits = 32;
	private const float TerrainToolTickSeconds = 0.1f;
	private const float TerrainToolReach = 2048f;
	private const float TerrainToolRadius = 128f;
	private const float TerrainToolStrength = 64f;
	private float _terrainToolElapsed = TerrainToolTickSeconds;
	private string _terrainToolStatus = "Idle";
	private readonly Queue<TerrainEditIntent> _terrainEditQueue = new();
	private readonly CancellationTokenSource _terrainEditCancellation = new();
	private System.Threading.Tasks.Task<TerrainFieldChange> _terrainEditTask;
	private TerrainEditIntent _activeTerrainEdit;
	private CancellationTokenSource _activeTerrainEditCancellation;
	private System.Threading.Tasks.Task<TerrainFieldStore.Checkpoint> _terrainSaveTask;
	private TerrainFieldSnapshot _terrainSaveSource;
	private bool _terrainRestorePending;
	private bool _terrainEditCapacityDeferred;
	private long _terrainEditCapacityRetryAt;
	private long _nextTerrainEditId;
	private long _terrainEditsCommitted;
	private long _terrainEditsRejected;
	private long _terrainEditedSamples;
	private string _terrainEditFailure;
	private float _terrainEditLastCommitMilliseconds;
	private int _terrainEditVisualDependencies;
	private int _terrainEditCollisionDependencies;

	private readonly record struct TerrainEditIntent( long Id, Vector3 Center, float Radius,
		float Strength, long RequestedAt );

	/// <summary>
	/// Trusted host gameplay entry point for world-space impacts and tools. Positive strength
	/// removes terrain; negative strength builds it. True means queued, not yet committed.
	/// Untrusted client requests must pass the host's tool validation before calling this.
	/// </summary>
	public bool TryQueueTerrainEdit( Vector3 center, float radius, float strength, out long requestId )
	{
		requestId = 0;
		if ( !Networking.IsHost || _terrainAuthorityLost || _playerFigureEightTestRunning || _performanceVisibilityPending ||
			_performanceCompletionPhase != PerformanceCompletionPhase.None || _terrainField is null || _terrainRestorePending || _terrainEditCancellation.IsCancellationRequested ||
			!TerrainField.IsValidBrush( center, radius, strength ) ||
			(strength < 0 && !CanBuildTerrain( center, radius )) || _terrainEditQueue.Count >= MaximumQueuedTerrainEdits )
		{
			_terrainEditsRejected++;
			return false;
		}
		requestId = ++_nextTerrainEditId;
		_terrainEditQueue.Enqueue( new TerrainEditIntent( requestId, center, radius, strength, Stopwatch.GetTimestamp() ) );
		return true;
	}

	private void UpdateTerrainEdits()
	{
		UpdateTerrainTool();
		if ( _terrainSaveTask is not null && _terrainSaveTask.IsCompleted )
		{
			try
			{
				var saved = _terrainSaveTask.GetAwaiter().GetResult();
				_terrainField.MarkSaved( saved, _terrainSaveSource );
				_terrainResetSavePath = null;
				_terrainSaveFailure = null;
				Log.Info( $"[TerrainEdit] save.complete path={saved.Root} world={saved.Identity.WorldId} revision={saved.Identity.Revision} checkpoint={saved.Sequence} liveRevision={CurrentField.Revision}" );
			}
			catch ( Exception exception )
			{
				_terrainSaveFailure = exception.Message;
				Log.Error( $"[TerrainEdit] save.failed reason={exception.Message}" );
			}
			_terrainSaveTask = null;
			_terrainSaveSource = null;
		}
		if ( _terrainEditTask is not null )
		{
			if ( !_terrainEditTask.IsCompleted ) return;
			try
			{
				var change = _terrainEditTask.GetAwaiter().GetResult();
				_activeTerrainEditCancellation?.Token.ThrowIfCancellationRequested();
				if ( _activeTerrainEdit.Strength < 0 && !CanBuildTerrain( _activeTerrainEdit.Center, _activeTerrainEdit.Radius ) )
					throw new InvalidOperationException( "An actor entered the build volume before commit." );
				if ( _terrainRestorePending && change.ChangedSamples > 0 )
				{
					foreach ( var body in Scene.GetAllComponents<Rigidbody>() )
					{
						if ( !body.PhysicsBody.IsValid() || !body.MotionEnabled ) continue;
						var bounds = body.PhysicsBody.GetBounds().Grow( TerrainField.SampleSpacing );
						if ( change.ReplacementSampleBounds.Any( region => TerrainFieldChange.Intersects( region, new SdfWorldAabb( bounds.Mins, bounds.Maxs ) ) ) )
							throw new InvalidOperationException( "An actor intersects the restore area. Move clear before loading." );
					}
				}
				if ( !_terrainField.TryCommit( change ) ) throw new InvalidOperationException( "Terrain mutation source changed before commit." );
				if ( _terrainIncoming?.RequestId == _activeTerrainEdit.Id ) _terrainIncoming.FieldCommitted = true;
				if ( Networking.IsHost && !_terrainRestorePending )
				{
					RecordDeformationCommit( _activeTerrainEdit.Id, change );
					_terrainEditsCommitted++;
					_terrainEditedSamples += change.ChangedSamples;
				}
				_terrainEditLastCommitMilliseconds = (float)Stopwatch.GetElapsedTime( _activeTerrainEdit.RequestedAt ).TotalMilliseconds;
				if ( !ReferenceEquals( change.Source, change.Result ) )
				{
					_terrainEditVisualDependencies = _gpuMesher.InvalidateField( change, _playerFigureEightRouteDistance );
					_terrainEditCollisionDependencies = _collision.InvalidateField( change );
					// Prepared LOD0 empty regions have no mesh record until their first surface.
					var dirtyBounds = change.DependencyPageBounds;
					foreach ( var coordinate in _renderPreparedChunks )
					{
						var size = _appliedCellsPerAxis * _appliedCellSize;
						var origin = new Vector3( coordinate.x, coordinate.y, coordinate.z ) * size;
						if ( change.Source.Epoch == change.Result.Epoch && !TerrainFieldChange.Intersects( new SdfWorldAabb( origin - Vector3.One * _appliedCellSize,
							origin + Vector3.One * (size + _appliedCellSize) ), dirtyBounds ) ) continue;
						var descriptor = CreateRegularDescriptor( 0, coordinate );
						if ( _gpuMesher.Contains( descriptor ) ) continue;
						_gpuMesher.Schedule( descriptor, _playerFigureEightRouteDistance,
							IsGameplayCoordinate( coordinate ) ? GpuMeshResidency.Gameplay : GpuMeshResidency.Warm );
						_gpuMesher.SetRenderActive( descriptor.Key, _levels[0].Active.Contains( coordinate ) );
						_terrainEditVisualDependencies++;
					}
					_gpuMesher.SealFieldPublication();
					_lastClipboxReadinessResidentRevision = -1;
				}
				if ( _terrainIncoming?.RequestId == _activeTerrainEdit.Id ) _terrainIncoming.Committed = true;
				if ( _terrainRestorePending )
				{
					_terrainSaveFailure = null;
					_terrainAutosaveDue = 0;
					Log.Info( $"[TerrainEdit] load.field_committed world={change.Result.WorldId} revision={change.Result.Revision} epoch={change.Result.Epoch} pages={change.Result.PageCount} checkpoint={change.Checkpoint?.Sequence}" );
				}
			}
			catch ( OperationCanceledException ) { _terrainResetSavePath = null; }
			catch ( Exception exception )
			{
				if ( _terrainIncoming?.RequestId == _activeTerrainEdit.Id ) FailTerrainReceive( exception.Message );
				_terrainEditsRejected++;
				_terrainEditFailure = exception.Message;
				_terrainResetSavePath = null;
				Log.Error( $"[TerrainEdit] request.failed id={_activeTerrainEdit.Id} reason={exception.Message}" );
			}
			_terrainEditTask = null;
			_activeTerrainEditCancellation?.Dispose();
			_activeTerrainEditCancellation = null;
			_terrainRestorePending = false;
			if ( _terrainResetSavePath is not null ) StartTerrainSave( _terrainResetSavePath );
		}
		// Finish the coherent visual publication before the next commit. Unrelated streaming
		// and collision jobs must not serialize all world mutations.
		if ( _terrainEditQueue.Count == 0 || _gpuMesher.FieldPublicationPending ||
			!_terrainEditQueue.TryPeek( out var intent ) ) return;
		if ( Stopwatch.GetTimestamp() < _terrainEditCapacityRetryAt ) return;
		var source = CurrentField;
		if ( !source.TryCaptureRegion( new SdfWorldAabb( intent.Center - Vector3.One * intent.Radius,
			intent.Center + Vector3.One * intent.Radius ), out var reader ) ) return;
		var minimum = (intent.Center - Vector3.One * intent.Radius) / TerrainField.SampleSpacing;
		var maximum = (intent.Center + Vector3.One * intent.Radius) / TerrainField.SampleSpacing;
		var pageCount = (((int)MathF.Floor( maximum.x ) >> TerrainField.PageShift) - ((int)MathF.Ceiling( minimum.x ) >> TerrainField.PageShift) + 1) *
			(((int)MathF.Floor( maximum.y ) >> TerrainField.PageShift) - ((int)MathF.Ceiling( minimum.y ) >> TerrainField.PageShift) + 1) *
			(((int)MathF.Floor( maximum.z ) >> TerrainField.PageShift) - ((int)MathF.Ceiling( minimum.z ) >> TerrainField.PageShift) + 1);
		_terrainEditCapacityDeferred = !TerrainFieldPage.TryReserveSamples( pageCount, out var reservation );
		if ( _terrainEditCapacityDeferred )
		{
			_terrainEditCapacityRetryAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 10;
			return;
		}
		_terrainEditQueue.Dequeue();
		_activeTerrainEdit = intent;
		_activeTerrainEditCancellation = CancellationTokenSource.CreateLinkedTokenSource( _terrainEditCancellation.Token );
		var cancellation = _activeTerrainEditCancellation.Token;
		_terrainEditTask = Task.RunInThreadAsync( () =>
		{
			using var reservedSamples = reservation;
			return TerrainField.PrepareBrush( source, reader, intent.Center, intent.Radius, intent.Strength, cancellation, reservedSamples );
		} );
	}

	private void UpdateTerrainTool()
	{
		_terrainToolElapsed = Math.Min( _terrainToolElapsed + RealTime.Delta, TerrainToolTickSeconds );
		if ( _deformationBenchmark is not null || _playerFigureEightEnabled || _terrainToolElapsed < TerrainToolTickSeconds ) return;
		var dig = Input.Down( "Attack1" );
		var build = Input.Down( "Attack2" );
		if ( dig == build || _terrainEditQueue.Count > 0 ) return;
		_terrainToolElapsed = 0;
		var player = Scene.GetAllComponents<PlayerController>().FirstOrDefault( value => !value.IsProxy );
		if ( !player.IsValid() ) return;
		if ( Networking.IsHost ) TryUseTerrainTool( player, player.EyeTransform.Rotation.Forward, build, out _ );
		else SendTerrainToolRequest( player.EyeTransform.Rotation.Forward, build );
	}

	private bool TryUseTerrainTool( PlayerController player, Vector3 direction, bool build, out long requestId )
	{
		requestId = 0;
		// Sample the current aim only when it can enter the pipeline, never queue stale tool targets.
		if ( _terrainEditTask is not null || _terrainEditQueue.Count > 0 || _gpuMesher.FieldPublicationPending )
		{
			_terrainToolStatus = "Previous edit publication pending";
			return false;
		}
		if ( !player.IsValid() || !float.IsFinite( direction.x ) || !float.IsFinite( direction.y ) ||
			!float.IsFinite( direction.z ) || direction.LengthSquared < 0.5f )
		{
			_terrainToolStatus = "Invalid player or aim";
			return false;
		}
		var eye = player.EyeTransform;
		var hit = Scene.Trace.Ray( eye.Position, eye.Position + direction.Normal * TerrainToolReach )
			.WithTag( "voxel_terrain" ).Run();
		if ( !hit.Hit ) { _terrainToolStatus = "No terrain within tool reach"; return false; }
		var coordinate = WorldToChunkCoordinate( hit.HitPosition );
		if ( !_collision.IsReady( coordinate, coordinate ) )
		{
			_terrainToolStatus = "Hit collision revision pending";
			return false;
		}
		var accepted = TryQueueTerrainEdit( hit.HitPosition, TerrainToolRadius, build ? -TerrainToolStrength : TerrainToolStrength, out requestId );
		_terrainToolStatus = accepted ? "Accepted" : "Mutation admission rejected";
		return accepted;
	}

	private bool CanBuildTerrain( Vector3 center, float radius )
	{
		var supportRadius = radius + TerrainField.SampleSpacing;
		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			if ( !player.IsValid() ) continue;
			var localCenter = center - player.WorldPosition;
			var nearest = player.BodyBox().ClosestPoint( localCenter );
			if ( (nearest - localCenter).LengthSquared <= supportRadius * supportRadius ) return false;
		}
		foreach ( var body in Scene.GetAllComponents<Rigidbody>() )
		{
			if ( !body.PhysicsBody.IsValid() || (!body.MotionEnabled && !_heldTerrainBodies.ContainsKey( body )) ) continue;
			var nearest = body.PhysicsBody.GetBounds().ClosestPoint( center );
			if ( (nearest - center).LengthSquared <= supportRadius * supportRadius ) return false;
		}
		return true;
	}

	[ConCmd( "voxel_terrain_save" )]
	public static void SaveTerrainCommand( string slot = "" )
	{
		if ( !TryGetActiveManager( "terrain.save", out var manager ) ) return;
		try
		{
			manager.StartTerrainSave( TerrainFieldStore.RootPath( string.IsNullOrWhiteSpace( slot ) ? manager.TerrainSaveSlot : slot ) );
		}
		catch ( Exception exception ) { Log.Warning( $"[TerrainEdit] save.rejected reason={exception.Message}" ); }
	}

	[ConCmd( "voxel_terrain_load" )]
	public static void LoadTerrainCommand( string slot )
	{
		if ( !TryGetActiveManager( "terrain.load", out var manager ) ) return;
		try
		{
			manager.StartTerrainRestore( TerrainFieldStore.RootPath( slot ) );
		}
		catch ( Exception exception )
		{
			if ( manager._terrainEditTask is null ) manager._terrainRestorePending = false;
			Log.Warning( $"[TerrainEdit] load.rejected reason={exception.Message}" );
		}
	}

	[ConCmd( "voxel_terrain_edit" )]
	public static void TerrainEditCommand( float x, float y, float z, float radius = 128f, float strength = 64f )
	{
		if ( !TryGetActiveManager( "terrain.edit", out var manager ) ) return;
		var accepted = manager.TryQueueTerrainEdit( new Vector3( x, y, z ), radius, strength, out var id );
		Log.Info( $"[TerrainEdit] request accepted={accepted} id={id}" );
	}

	[ConCmd( "voxel_terrain_edit_info" )]
	public static void TerrainEditInfoCommand()
	{
		if ( !TryGetActiveManager( "terrain.edit.inspect", out var manager ) ) return;
		var player = manager.Scene.GetAllComponents<PlayerController>().FirstOrDefault( value => !value.IsProxy );
		var checkpoint = manager._terrainField.Checkpoint;
		Log.Info( $"[TerrainEdit] toolStatus={manager._terrainToolStatus} eye={player?.EyePosition} world={manager.CurrentField.WorldId} revision={manager.CurrentField.Revision} pages={manager.CurrentField.PageCount} " +
			$"epoch={manager.CurrentField.Epoch} savedRevision={checkpoint?.Identity.Revision} checkpoint={checkpoint?.Sequence} saving={manager._terrainSaveTask is not null} saveSlot={manager.TerrainSaveSlot} saveStatus={manager.TerrainSaveStatus} " +
			$"bytes={manager.CurrentField.PageBytes} queued={manager._terrainEditQueue.Count} preparing={manager._terrainEditTask is not null} " +
			$"residentBytes={manager.CurrentField.ResidentPageBytes} retainedSampleBytes={TerrainFieldPage.RetainedSampleBytes} sampleBudget={TerrainField.MaximumSampleBytes} reservedSampleBytes={TerrainFieldPage.ReservedSampleBytes} readCapacityDeferred={manager._terrainField.ReadCapacityDeferred} editCapacityDeferred={manager._terrainEditCapacityDeferred} " +
			$"storageReads={manager._terrainField.PendingReads} loadedPages={manager._terrainField.LoadedPages} evictedPages={manager._terrainField.EvictedPages} reusedSamplePages={TerrainFieldPage.ReusedSamplePages} readIntegrationMaxMs={manager._terrainReadIntegrationMaximumMilliseconds} sweepMaxMs={manager._terrainSweepMaximumMilliseconds} storageFailure={manager._terrainField.ReadFailure} " +
			$"committed={manager._terrainEditsCommitted} rejected={manager._terrainEditsRejected} samples={manager._terrainEditedSamples} " +
			$"commitMs={manager._terrainEditLastCommitMilliseconds} visualDependencies={manager._terrainEditVisualDependencies} " +
			$"collisionDependencies={manager._terrainEditCollisionDependencies} visualPending={manager._gpuMesher.EditRebuildPending} " +
			$"collisionPending={manager._collision.EditRebuildPending} failure={manager._terrainEditFailure}" );
	}
}
