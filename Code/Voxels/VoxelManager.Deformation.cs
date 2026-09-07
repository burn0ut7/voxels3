using System;
using System.Diagnostics;
using System.IO;
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
	private System.Threading.Tasks.Task<string> _terrainSaveTask;
	private bool _terrainRestorePending;
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
			try { Log.Info( $"[TerrainEdit] save.complete path={_terrainSaveTask.GetAwaiter().GetResult()}" ); }
			catch ( Exception exception ) { Log.Error( $"[TerrainEdit] save.failed reason={exception.Message}" ); }
			_terrainSaveTask = null;
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
						if ( TerrainFieldChange.Intersects( change.AffectedBounds, new SdfWorldAabb( bounds.Mins, bounds.Maxs ) ) )
							throw new InvalidOperationException( "An actor intersects the restore area. Move clear before loading." );
					}
				}
				if ( !_terrainField.TryCommit( change ) ) throw new InvalidOperationException( "Terrain mutation source changed before commit." );
				if ( _terrainIncoming?.RequestId == _activeTerrainEdit.Id ) _terrainIncoming.FieldCommitted = true;
				RecordDeformationCommit( _activeTerrainEdit.Id, change );
				_terrainEditsCommitted++;
				_terrainEditedSamples += change.ChangedSamples;
				_terrainEditLastCommitMilliseconds = (float)Stopwatch.GetElapsedTime( _activeTerrainEdit.RequestedAt ).TotalMilliseconds;
				if ( change.ChangedSamples > 0 )
				{
					_terrainEditVisualDependencies = _gpuMesher.InvalidateField( change, _playerFigureEightRouteDistance );
					_terrainEditCollisionDependencies = _collision.InvalidateField( change );
					// Prepared LOD0 empty regions have no mesh record until their first surface.
					var dirtyBounds = change.DependencyPageBounds;
					foreach ( var coordinate in _renderPreparedChunks )
					{
						var size = _appliedCellsPerAxis * _appliedCellSize;
						var origin = new Vector3( coordinate.x, coordinate.y, coordinate.z ) * size;
						if ( !TerrainFieldChange.Intersects( new SdfWorldAabb( origin - Vector3.One * _appliedCellSize,
							origin + Vector3.One * (size + _appliedCellSize) ), dirtyBounds ) ) continue;
						var descriptor = CreateRegularDescriptor( 0, coordinate );
						if ( descriptor.EditRevision != change.Result.Revision || _gpuMesher.Contains( descriptor ) ) continue;
						_gpuMesher.Schedule( descriptor, _playerFigureEightRouteDistance,
							IsGameplayCoordinate( coordinate ) ? GpuMeshResidency.Gameplay : GpuMeshResidency.Warm );
						_gpuMesher.SetRenderActive( descriptor.Key, _levels[0].Active.Contains( coordinate ) );
						_terrainEditVisualDependencies++;
					}
					_gpuMesher.SealFieldPublication();
					_lastClipboxReadinessResidentRevision = -1;
				}
				if ( _terrainIncoming?.RequestId == _activeTerrainEdit.Id ) _terrainIncoming.Committed = true;
			}
			catch ( OperationCanceledException ) { }
			catch ( Exception exception )
			{
				if ( _terrainIncoming?.RequestId == _activeTerrainEdit.Id ) FailTerrainReceive( exception.Message );
				_terrainEditsRejected++;
				_terrainEditFailure = exception.Message;
				Log.Error( $"[TerrainEdit] request.failed id={_activeTerrainEdit.Id} reason={exception.Message}" );
			}
			_terrainEditTask = null;
			_activeTerrainEditCancellation?.Dispose();
			_activeTerrainEditCancellation = null;
			_terrainRestorePending = false;
		}
		// Finish the coherent visual publication before the next commit. Unrelated streaming
		// and collision jobs must not serialize all world mutations.
		if ( _terrainEditQueue.Count == 0 || _gpuMesher.FieldPublicationPending ||
			!_terrainEditQueue.TryDequeue( out _activeTerrainEdit ) ) return;
		var source = CurrentField;
		var intent = _activeTerrainEdit;
		_activeTerrainEditCancellation = CancellationTokenSource.CreateLinkedTokenSource( _terrainEditCancellation.Token );
		var cancellation = _activeTerrainEditCancellation.Token;
		_terrainEditTask = Task.RunInThreadAsync( () => TerrainField.PrepareBrush(
			source, intent.Center, intent.Radius, intent.Strength, cancellation ) );
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

	private static string TerrainSavePath( string slot )
	{
		if ( string.IsNullOrWhiteSpace( slot ) || slot.Length > 32 ||
			slot.Any( character => !(character >= 'a' && character <= 'z') &&
				!(character >= '0' && character <= '9') && character != '-' && character != '_' ) )
			throw new ArgumentException( "Save names use 1-32 lowercase letters, digits, hyphens or underscores." );
		return $"terrain/{slot}.vxt";
	}

	[ConCmd( "voxel_terrain_save" )]
	public static void SaveTerrainCommand( string slot )
	{
		if ( !TryGetActiveManager( "terrain.save", out var manager ) ) return;
		try
		{
			if ( manager._deformationBenchmark is not null || !Networking.IsHost || manager._playerFigureEightTestRunning || manager._performanceVisibilityPending ||
				manager._performanceCompletionPhase != PerformanceCompletionPhase.None || manager._terrainSaveTask is not null ) throw new InvalidOperationException( "Only the host may save, with one save at a time." );
			var path = TerrainSavePath( slot );
			var snapshot = manager.CurrentField;
			var cancellation = manager._terrainEditCancellation.Token;
			manager._terrainSaveTask = manager.Task.RunInThreadAsync( () =>
			{
				FileSystem.Data.CreateDirectory( "terrain" );
				// CreateNew preserves any existing save, including when a write fails.
				using var stream = FileSystem.Data.OpenWrite( path, FileMode.CreateNew );
				TerrainFieldCodec.WriteSnapshot( stream, snapshot, cancellation );
				return path;
			} );
			Log.Info( $"[TerrainEdit] save.started path={path} revision={snapshot.Revision}" );
		}
		catch ( Exception exception ) { Log.Warning( $"[TerrainEdit] save.rejected reason={exception.Message}" ); }
	}

	[ConCmd( "voxel_terrain_load" )]
	public static void LoadTerrainCommand( string slot )
	{
		if ( !TryGetActiveManager( "terrain.load", out var manager ) ) return;
		try
		{
			if ( manager._deformationBenchmark is not null || !Networking.IsHost || manager._playerFigureEightTestRunning || manager._performanceVisibilityPending ||
				manager._performanceCompletionPhase != PerformanceCompletionPhase.None || manager._terrainEditTask is not null || manager._terrainEditQueue.Count > 0 ||
				manager._terrainSaveTask is not null || manager._gpuMesher.EditRebuildPending || manager._collision.EditRebuildPending )
				throw new InvalidOperationException( "Only an idle host mutation boundary may load terrain." );
			var path = TerrainSavePath( slot );
			var source = manager.CurrentField;
			manager._activeTerrainEditCancellation = CancellationTokenSource.CreateLinkedTokenSource( manager._terrainEditCancellation.Token );
			var cancellation = manager._activeTerrainEditCancellation.Token;
			manager._activeTerrainEdit = new TerrainEditIntent( ++manager._nextTerrainEditId, default, 0, 0, Stopwatch.GetTimestamp() );
			manager._terrainRestorePending = true;
			manager._terrainEditTask = manager.Task.RunInThreadAsync( () =>
			{
				using var stream = FileSystem.Data.OpenRead( path );
				var restored = TerrainFieldCodec.ReadSnapshot( stream, source.Settings, cancellation );
				return TerrainField.PrepareReplacement( source, restored, cancellation );
			} );
			Log.Info( $"[TerrainEdit] load.started path={path} request={manager._activeTerrainEdit.Id}" );
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
		Log.Info( $"[TerrainEdit] toolStatus={manager._terrainToolStatus} eye={player?.EyePosition} world={manager.CurrentField.WorldId} revision={manager.CurrentField.Revision} pages={manager.CurrentField.PageCount} " +
			$"bytes={manager.CurrentField.PageBytes} queued={manager._terrainEditQueue.Count} preparing={manager._terrainEditTask is not null} " +
			$"committed={manager._terrainEditsCommitted} rejected={manager._terrainEditsRejected} samples={manager._terrainEditedSamples} " +
			$"commitMs={manager._terrainEditLastCommitMilliseconds} visualDependencies={manager._terrainEditVisualDependencies} " +
			$"collisionDependencies={manager._terrainEditCollisionDependencies} visualPending={manager._gpuMesher.EditRebuildPending} " +
			$"collisionPending={manager._collision.EditRebuildPending} failure={manager._terrainEditFailure}" );
	}
}
