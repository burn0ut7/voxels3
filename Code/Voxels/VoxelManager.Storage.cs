using System;
using System.Diagnostics;

public sealed partial class VoxelManager
{
	private const double TerrainAutosaveSeconds = 30;
	private long _terrainAutosaveDue;
	private string _terrainSaveFailure;
	private string _terrainResetSavePath;

	[Property, Category( "World Saving" )]
	public bool AutoSaveTerrain { get; set; } = true;

	[Property, ReadOnly, Category( "World Saving" )]
	public string TerrainSaveSlot => _terrainField is null ? "No world" :
		(_terrainResetSavePath ?? _terrainField.Checkpoint?.Root ?? TerrainFieldStore.RootPath( CurrentField.WorldId.ToString( "N" ) ))["terrain/".Length..];

	[Property, ReadOnly, Category( "World Saving" )]
	public string TerrainSaveStatus => _terrainField is null ? (_terrainSaveFailure is null ? "Loading world..." : $"Load failed: {_terrainSaveFailure}") : !Networking.IsHost ? "Server manages saving" :
		_terrainSaveTask is not null ? "Saving..." : _terrainSaveFailure is not null ? $"Save failed: {_terrainSaveFailure}" :
		_terrainField.Checkpoint?.Identity.Revision == CurrentField.Revision ? "Saved" : CurrentField.Revision == 0 ? "No edits to save" :
		AutoSaveTerrain ? "Unsaved changes - autosave pending" : "Unsaved changes";

	[Button( "Save now" ), Category( "World Saving" )]
	public void SaveTerrainNow()
	{
		try { StartTerrainSave( TerrainFieldStore.RootPath( TerrainSaveSlot ) ); }
		catch ( Exception exception ) { Log.Warning( $"[TerrainEdit] save.rejected reason={exception.Message}" ); }
	}

	[Button( "Clear / Reset world" ), Category( "World Saving" )]
	public void ResetTerrainWorld()
	{
		try { StartTerrainRestore( null, clear: true ); }
		catch ( Exception exception ) { Log.Warning( $"[TerrainEdit] reset.rejected reason={exception.Message}" ); }
	}

	[ConCmd( "voxel_terrain_reset" )]
	public static void ResetTerrainCommand()
	{
		if ( TryGetActiveManager( "terrain.reset", out var manager ) ) manager.ResetTerrainWorld();
	}

	private void SaveTerrainOnUnload()
	{
		if ( Scene.IsEditor || _terrainField is null || !Networking.IsHost || _terrainAuthorityLost ) return;
		try
		{
			// Scene work is cancelled first. The backend lock lets active I/O finish
			// before publishing the final committed state with an uncancelled token.
			var source = CurrentField;
			var saved = TerrainFieldStore.Save( _terrainResetSavePath ?? TerrainFieldStore.RootPath( TerrainSaveSlot ),
				source, System.Threading.CancellationToken.None );
			_terrainField.MarkSaved( saved, source );
		}
		catch ( Exception exception )
		{
			// Native shutdown can destroy the editor's log UI before components.
			// Reporting a failed save must not interrupt the remaining teardown.
			try { Log.Error( $"[TerrainStorage] unload.save_failed reason={exception.Message}" ); }
			catch ( Exception ) { }
		}
	}

	private void StartTerrainRestore( string path, bool clear = false )
	{
		if ( _terrainField is null || _terrainAuthorityLost || _terrainEditCancellation.IsCancellationRequested ||
			_deformationBenchmark is not null || !Networking.IsHost || _playerFigureEightTestRunning || _performanceVisibilityPending ||
			_performanceCompletionPhase != PerformanceCompletionPhase.None || _terrainEditTask is not null || _terrainEditQueue.Count > 0 ||
			_terrainSaveTask is not null || _gpuMesher.EditRebuildPending || _collision.EditRebuildPending )
			throw new InvalidOperationException( "Only an idle host mutation boundary may load or reset terrain." );
		var source = CurrentField;
		var savePath = TerrainFieldStore.RootPath( TerrainSaveSlot );
		_activeTerrainEditCancellation = System.Threading.CancellationTokenSource.CreateLinkedTokenSource( _terrainEditCancellation.Token );
		var cancellation = _activeTerrainEditCancellation.Token;
		_activeTerrainEdit = new TerrainEditIntent( ++_nextTerrainEditId, default, 0, 0, Stopwatch.GetTimestamp() );
		_terrainRestorePending = true;
		_terrainResetSavePath = clear ? savePath : null;
		_terrainEditTask = Task.RunInThreadAsync( () =>
		{
			var checkpoint = clear ? null : TerrainFieldStore.Open( path, source.Settings, cancellation );
			var restored = clear ? new TerrainFieldSnapshot( source.Settings, checked( source.Revision + 1 ),
				new Dictionary<Vector3Int, TerrainFieldPage>(), source.WorldId ) : TerrainFieldStore.ReadDirectory( checkpoint, cancellation );
			return TerrainField.PrepareReplacement( source, restored, cancellation, resetEpoch: true, checkpoint: checkpoint );
		} );
		Log.Info( $"[TerrainEdit] {(clear ? "reset" : "load")}.started path={path ?? savePath} request={_activeTerrainEdit.Id}" );
	}

	private void StartTerrainSave( string path )
	{
		if ( _terrainField is null || !Networking.IsHost || _terrainAuthorityLost || _terrainEditCancellation.IsCancellationRequested ||
			_deformationBenchmark is not null || _playerFigureEightTestRunning || _performanceVisibilityPending ||
			_performanceCompletionPhase != PerformanceCompletionPhase.None || _terrainSaveTask is not null || _terrainRestorePending )
			throw new InvalidOperationException( "Only the host may save, with one save at a time and no world restore or benchmark in progress." );
		var snapshot = CurrentField;
		var cancellation = _terrainEditCancellation.Token;
		_terrainSaveSource = snapshot;
		_terrainSaveTask = Task.RunInThreadAsync( () => TerrainFieldStore.Save( path, snapshot, cancellation ) );
		_terrainAutosaveDue = 0;
		Log.Info( $"[TerrainEdit] save.started path={path} revision={snapshot.Revision}" );
	}

	private void UpdateTerrainAutosave()
	{
		if ( _terrainField is null || !Networking.IsHost || !AutoSaveTerrain || _terrainAuthorityLost ||
			CurrentField.Revision == 0 || _terrainField.Checkpoint?.Identity.Revision == CurrentField.Revision )
		{
			_terrainAutosaveDue = 0;
			return;
		}
		if ( _terrainSaveTask is not null || _terrainRestorePending || _terrainEditCancellation.IsCancellationRequested ||
			_deformationBenchmark is not null || _playerFigureEightTestRunning || _performanceVisibilityPending ||
			_performanceCompletionPhase != PerformanceCompletionPhase.None ) return;
		var now = Stopwatch.GetTimestamp();
		if ( _terrainAutosaveDue == 0 ) _terrainAutosaveDue = now + (long)(Stopwatch.Frequency * TerrainAutosaveSeconds);
		if ( now < _terrainAutosaveDue ) return;
		StartTerrainSave( TerrainFieldStore.RootPath( TerrainSaveSlot ) );
	}

	private System.Threading.Tasks.Task<TerrainReadResult[]> _terrainReadTask;
	private TerrainField _terrainReadOwner;
	private TerrainReadResult[] _terrainReadResults;
	private int _terrainReadResultIndex;
	private double _terrainReadIntegrationMaximumMilliseconds;
	private long _terrainReadCapacityRetryAt;
	private TerrainFieldSnapshot _terrainSweepSnapshot;
	private KeyValuePair<Vector3Int, TerrainFieldPage>[] _terrainSweepPages;
	private int _terrainSweepIndex;
	private double _terrainSweepMaximumMilliseconds;
	private readonly record struct TerrainReadResult( TerrainField.ReadRequest Request, TerrainFieldPage Page, string Error );

	private void SweepTerrainStorage()
	{
		if ( !Networking.IsHost || _terrainField is null || !_hasStreamingCenter ) return;
		var field = CurrentField;
		if ( field.PageCount == 0 )
		{
			_terrainSweepSnapshot = null; _terrainSweepPages = null; _terrainSweepIndex = 0;
			return;
		}
		var start = Stopwatch.GetTimestamp();
		if ( !ReferenceEquals( _terrainSweepSnapshot, field ) )
		{
			_terrainSweepSnapshot = field;
			_terrainSweepPages = field.Pages.ToArray();
			_terrainSweepIndex = 0;
		}
		for ( var count = 0; count < Math.Min( 8, _terrainSweepPages.Length ); count++ )
		{
			var pair = _terrainSweepPages[_terrainSweepIndex++];
			if ( _terrainSweepIndex == _terrainSweepPages.Length ) _terrainSweepIndex = 0;
			// Cold pages have nothing to evict. Pin/install stamps their grace time
			// when samples return, so they do not need interest checks while cold.
			if ( !pair.Value.IsResident ) continue;
			var size = TerrainField.SampleSpacing * TerrainField.SamplesPerPageAxis;
			var origin = new Vector3( pair.Key.x, pair.Key.y, pair.Key.z ) * size;
			var bounds = new SdfWorldAabb( origin, origin + Vector3.One * size );
			_terrainField.SweepPage( pair.Key, pair.Value, field.Epoch, IsTerrainStorageRequired( bounds ), start );
			if ( Stopwatch.GetElapsedTime( start ).TotalMilliseconds >= 1 ) break;
		}
		_terrainSweepMaximumMilliseconds = Math.Max( _terrainSweepMaximumMilliseconds, Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
	}

	private bool IsTerrainStorageRequired( SdfWorldAabb page )
	{
		if ( _streamingRenderRadius >= 0 && StorageIntersectsBox( page, _streamingCenterCoordinate - new Vector3Int( _streamingRenderRadius ),
			_streamingCenterCoordinate + new Vector3Int( _streamingRenderRadius + 1 ), _appliedCellSize ) ) return true;
		foreach ( var level in _levels )
		{
			var cell = CellSizeForLevel( level.Level );
			if ( level.HasPlacement && level.VisualEnabled && StorageIntersectsBox( page, level.OuterMinimum, level.OuterMaximum, cell ) ) return true;
			if ( _clipboxPlacementPending && level.StagedVisualEnabled && StorageIntersectsBox( page, level.StagedOuterMinimum, level.StagedOuterMaximum, cell ) ) return true;
		}
		foreach ( var center in _terrainPlayerInterests )
			if ( StorageIntersectsBox( page, center - new Vector3Int( _appliedGameplayRadius ), center + new Vector3Int( _appliedGameplayRadius + 1 ), _appliedCellSize ) ) return true;
		foreach ( var coordinate in _terrainActorInterests )
			if ( StorageIntersectsBox( page, coordinate, coordinate + new Vector3Int( 1 ), _appliedCellSize ) ) return true;
		foreach ( var edit in _terrainEditQueue )
			if ( TerrainFieldChange.Intersects( page, new SdfWorldAabb( edit.Center - Vector3.One * (edit.Radius + TerrainField.SampleSpacing),
				edit.Center + Vector3.One * (edit.Radius + TerrainField.SampleSpacing) ) ) ) return true;
		return _terrainEditTask is not null && _activeTerrainEdit.Strength != 0 && TerrainFieldChange.Intersects( page,
			new SdfWorldAabb( _activeTerrainEdit.Center - Vector3.One * (_activeTerrainEdit.Radius + TerrainField.SampleSpacing),
				_activeTerrainEdit.Center + Vector3.One * (_activeTerrainEdit.Radius + TerrainField.SampleSpacing) ) );
	}

	private bool StorageIntersectsBox( SdfWorldAabb page, Vector3Int minimum, Vector3Int maximum, float cell )
	{
		var size = _appliedCellsPerAxis * cell;
		var halo = Vector3.One * (cell + TerrainField.SampleSpacing);
		return TerrainFieldChange.Intersects( page, new SdfWorldAabb( new Vector3( minimum.x, minimum.y, minimum.z ) * size - halo,
			new Vector3( maximum.x, maximum.y, maximum.z ) * size + halo ) );
	}

	private void UpdateTerrainStorage()
	{
		if ( _terrainReadTask?.IsCompleted == true )
		{
			try { _terrainReadResults = _terrainReadTask.GetAwaiter().GetResult(); }
			catch ( OperationCanceledException ) { }
			catch ( Exception exception ) { Log.Error( $"[TerrainStorage] read.failed reason={exception.Message}" ); }
			_terrainReadTask = null;
			_terrainReadResultIndex = 0;
		}
		if ( _terrainReadResults is not null )
		{
			var start = Stopwatch.GetTimestamp();
			var loaded = false;
			while ( _terrainReadResultIndex < _terrainReadResults.Length )
			{
				var result = _terrainReadResults[_terrainReadResultIndex];
				_terrainReadResults[_terrainReadResultIndex++] = default;
				if ( ReferenceEquals( _terrainReadOwner, _terrainField ) )
				{
					loaded |= _terrainReadOwner.CompleteRead( result.Request, result.Page, result.Error );
					if ( result.Error is not null ) Log.Error( $"[TerrainStorage] page.read_failed coordinate={result.Request.Coordinate} reason={result.Error}" );
				}
				if ( Stopwatch.GetElapsedTime( start ).TotalMilliseconds >= 1 ) break;
			}
			_terrainReadIntegrationMaximumMilliseconds = Math.Max( _terrainReadIntegrationMaximumMilliseconds,
				Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
			if ( loaded ) _collision?.NotifyFieldPagesLoaded();
			if ( _terrainReadResultIndex == _terrainReadResults.Length )
			{
				_terrainReadResults = null;
				_terrainReadOwner = null;
			}
		}
		if ( _terrainField is null || _terrainReadTask is not null || _terrainReadResults is not null || _terrainEditCancellation.IsCancellationRequested ) return;
		if ( Stopwatch.GetTimestamp() < _terrainReadCapacityRetryAt ) return;
		var requests = _terrainField.TakeReadBatch( out var reservation );
		if ( _terrainField.ReadCapacityDeferred ) _terrainReadCapacityRetryAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 10;
		if ( requests.Length == 0 ) return;
		_terrainReadOwner = _terrainField;
		var cancellation = _terrainEditCancellation.Token;
		_terrainReadTask = Task.RunInThreadAsync( () =>
		{
			using var reservedSamples = reservation;
			var results = new TerrainReadResult[requests.Length];
			for ( var index = 0; index < requests.Length; index++ )
			{
				cancellation.ThrowIfCancellationRequested();
				var request = requests[index];
				try { results[index] = new TerrainReadResult( request, TerrainFieldStore.ReadPage( request.Root, request.Stored, reservedSamples ), null ); }
				catch ( Exception exception ) { results[index] = new TerrainReadResult( request, null, exception.Message ); }
			}
			return results;
		} );
	}
}
