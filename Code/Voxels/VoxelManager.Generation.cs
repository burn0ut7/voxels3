using System;

public sealed partial class VoxelManager
{
	private ProceduralTerrainSettings? _pendingTerrainRecipe;
	private Guid _pendingTerrainRecipeWorld;
	private string _terrainRecipeStatus = "Edit controls, then apply to a new world.";
	private bool _landformSurveyRunning;

	/// <summary>Inspect a bounded vertical column of the active canonical field without changing it.</summary>
	public object InspectTerrainColumn( float x, float y, float minimumZ, float spacing, int count )
	{
		var maximumZ = minimumZ + (count - 1) * spacing;
		if ( _terrainField is null || count < 2 || count > 129 || !float.IsFinite( x ) || !float.IsFinite( y ) ||
			!float.IsFinite( minimumZ ) || !float.IsFinite( maximumZ ) || !float.IsFinite( spacing ) || spacing <= 0f ||
			MathF.Abs( x ) > TerrainField.MaximumWorldCoordinate || MathF.Abs( y ) > TerrainField.MaximumWorldCoordinate ||
			minimumZ < -TerrainField.MaximumWorldCoordinate || maximumZ > TerrainField.MaximumWorldCoordinate )
			throw new ArgumentException( "Column requires 2–129 finite samples inside the supported world coordinates." );
		var bounds = new SdfWorldAabb( new Vector3( x, y, minimumZ ), new Vector3( x, y, maximumZ ) );
		if ( !CurrentField.TryCaptureRegion( bounds, out var reader ) )
			throw new InvalidOperationException( "Column pages are loading; retry after normal storage integration." );
		var height = RegionalLandforms.SampleWorld( new Vector3( x, y, 0f ), reader.Settings ).Height;
		return new
		{
			reader.WorldId, reader.Revision, reader.Settings, Height = height,
			Samples = Enumerable.Range( 0, count ).Select( index =>
			{
				var z = minimumZ + index * spacing;
				var position = new Vector3( x, y, z );
				var density = reader.SampleWorld( position );
				return new { Z = z, SurfaceDensity = z - height,
					BaseDensity = ProceduralTerrainSdf.SampleWorld( position, reader.Settings ), Density = density,
					Medium = SurfaceWater.Resolve( z, height, density, reader.Settings.SeaLevel ).ToString() };
			} ).ToArray()
		};
	}

	/// <summary>Exports a bounded survey of the active unedited recipe for terrain authoring.</summary>
	public async System.Threading.Tasks.Task<string> ExportLandformSurvey( float minimumX = -131072f,
		float minimumY = -131072f, int pointsPerAxis = 65, float spacing = 4096f )
	{
		if ( Scene.IsEditor || _terrainField is null || _landformSurveyRunning || pointsPerAxis < 2 || pointsPerAxis > 129 ||
			!float.IsFinite( spacing ) || spacing < 16f || spacing > 65536f ||
			!float.IsFinite( minimumX ) || !float.IsFinite( minimumY ) ||
			minimumX < -TerrainField.MaximumWorldCoordinate || minimumY < -TerrainField.MaximumWorldCoordinate ||
			minimumX + (pointsPerAxis - 1) * spacing > TerrainField.MaximumWorldCoordinate ||
			minimumY + (pointsPerAxis - 1) * spacing > TerrainField.MaximumWorldCoordinate )
			throw new InvalidOperationException( "Survey requires a playable world and a bounded grid of 2–129 points per axis." );
		_landformSurveyRunning = true;
		var settings = CurrentTerrainSettings;
		var world = CurrentField.WorldId;
		var cancellation = _terrainEditCancellation.Token;
		try
		{
			return await Task.RunInThreadAsync( () =>
			{
				var text = new System.Text.StringBuilder();
				text.AppendLine( "x,y,height,land,mountains,plains,hills,slope,boundMin,boundMax,repeatHeight" );
				for ( var y = 0; y < pointsPerAxis; y++ )
				{
					cancellation.ThrowIfCancellationRequested();
					for ( var x = 0; x < pointsPerAxis; x++ )
					{
						var position = new Vector3( minimumX + x * spacing, minimumY + y * spacing, 0f );
						var sample = RegionalLandforms.SampleWorld( position, settings );
						var dx = (RegionalLandforms.SampleWorld( position + new Vector3( 16f, 0f, 0f ), settings ).Height -
							RegionalLandforms.SampleWorld( position - new Vector3( 16f, 0f, 0f ), settings ).Height) / 32f;
						var dy = (RegionalLandforms.SampleWorld( position + new Vector3( 0f, 16f, 0f ), settings ).Height -
							RegionalLandforms.SampleWorld( position - new Vector3( 0f, 16f, 0f ), settings ).Height) / 32f;
						var bounds = RegionalLandforms.BoundHeight( new SdfWorldAabb(
							position - new Vector3( 256f, 256f, 0f ), position + new Vector3( 256f, 256f, 0f ) ), settings );
						var repeated = RegionalLandforms.SampleWorld( position, settings ).Height;
						text.AppendLine( FormattableString.Invariant( $"{position.x:R},{position.y:R},{sample.Height:R},{sample.Land:R},{sample.Mountains:R},{sample.Plains:R},{sample.Hills:R},{MathF.Sqrt( dx * dx + dy * dy ):R},{bounds.Minimum:R},{bounds.Maximum:R},{repeated:R}" ) );
					}
				}
				var directory = $"terrain-surveys/{Guid.NewGuid():N}";
				FileSystem.Data.CreateDirectory( directory );
				FileSystem.Data.WriteAllText( $"{directory}/samples.csv", text.ToString() );
				FileSystem.Data.WriteAllText( $"{directory}/recipe.json", System.Text.Json.JsonSerializer.Serialize(
					new { World = world, Generator = ProceduralTerrainSdf.CurrentVersion, Settings = settings, minimumX, minimumY, pointsPerAxis, spacing } ) );
				return FileSystem.Data.GetFullPath( directory );
			} );
		}
		finally { _landformSurveyRunning = false; }
	}

	[Property, ReadOnly, Category( "Terrain Generation" )]
	public string TerrainRecipeStatus
	{
		get
		{
			if ( _pendingTerrainRecipe is not null || _terrainField is null ) return _terrainRecipeStatus;
			if ( !StagedTerrainSettings.IsValid ) return "Invalid recipe: check ranges and scale hierarchy.";
			if ( _terrainRecipeStatus.StartsWith( "Applied." ) || _terrainRecipeStatus.StartsWith( "Invalid recipe" ) ||
				_terrainRecipeStatus.StartsWith( "These settings" ) || _terrainRecipeStatus.StartsWith( "Edit controls" ) )
				return StagedTerrainSettings == CurrentTerrainSettings ? "These settings are already active." :
					"Unapplied changes. Apply to new world when ready.";
			return _terrainRecipeStatus;
		}
	}

	[Button( "Apply to new world" ), Category( "Terrain Generation" )]
	public void ApplyTerrainRecipe()
	{
		if ( !StagedTerrainSettings.IsValid )
		{
			_terrainRecipeStatus = "Invalid recipe: check ranges and keep continent >= 2 × mountain >= 4 × local scale.";
			return;
		}
		if ( !CanSwitchTerrainRecipe() )
		{
			_terrainRecipeStatus = "Apply requires an idle host with no connected guests, edits, saves or benchmark pending.";
			return;
		}
		if ( StagedTerrainSettings == CurrentTerrainSettings )
		{
			_terrainRecipeStatus = "These settings are already active.";
			return;
		}
		try
		{
			// Snapshot the requested recipe once. Later inspector edits cannot mutate it.
			_pendingTerrainRecipe = StagedTerrainSettings;
			_pendingTerrainRecipeWorld = CurrentField.WorldId;
			StartTerrainSave( TerrainFieldStore.RootPath( TerrainSaveSlot ) );
			_terrainRecipeStatus = "Saving the current world before applying...";
		}
		catch ( Exception exception )
		{
			_pendingTerrainRecipe = null;
			_terrainRecipeStatus = exception.Message;
		}
	}

	private bool CanSwitchTerrainRecipe() => _terrainField is not null && Networking.IsHost &&
		!_terrainAuthorityLost && !_terrainEditCancellation.IsCancellationRequested &&
		!Connection.All.Any( connection => connection.IsActive && connection != Connection.Local ) &&
		_pendingTerrainRecipe is null && _terrainEditTask is null && _terrainEditQueue.Count == 0 &&
		_terrainSaveTask is null && !_terrainRestorePending && _deformationBenchmark is null &&
		!_playerFigureEightTestRunning && !_performanceVisibilityPending &&
		_performanceCompletionPhase == PerformanceCompletionPhase.None &&
		!_gpuMesher.EditRebuildPending && !_collision.EditRebuildPending;

	private void UpdateTerrainRecipe()
	{
		if ( _pendingTerrainRecipe is not { } recipe || _terrainSaveTask is not null ) return;
		_pendingTerrainRecipe = null;
		if ( !CanSwitchTerrainRecipe() || CurrentField.WorldId != _pendingTerrainRecipeWorld ||
			_terrainSaveFailure is not null || _terrainField.Checkpoint?.Identity.Revision != CurrentField.Revision )
		{
			_terrainRecipeStatus = "Apply cancelled: the current world could not be saved or the session changed.";
			return;
		}
		if ( !TryValidateConfiguration( out _, out var error ) )
		{
			_terrainRecipeStatus = error;
			return;
		}
		_terrainField = new TerrainField( recipe );
		_terrainResetSavePath = null;
		_terrainAutosaveDue = 0;
		// Old collision may support ordinary edits, but belongs to another world here.
		_collision.Dispose();
		_collision = new VoxelCollisionWorld( this, RequiredCellsPerAxis, RequiredBaseCellSize );
		ApplyConfigurationAndRebuild( preserveTerrain: true );
		if ( _terrainHostPlayer.IsValid() )
		{
			PlaceTerrainPlayerAboveSurface( _terrainHostPlayer.GameObject );
			_terrainPlayerSpawn = _terrainHostPlayer.WorldTransform;
		}
		_terrainRecipeStatus = "Applied. Previous world saved; new world has its own save identity.";
		Log.Info( $"[Landforms] recipe.applied world={CurrentField.WorldId} settings={recipe}" );
	}

	private void PlaceTerrainPlayerAboveSurface( GameObject player )
	{
		var position = player.WorldPosition;
		var column = new SdfWorldAabb(
			new Vector3( position.x - 32f, position.y - 32f, -TerrainField.MaximumWorldCoordinate ),
			new Vector3( position.x + 32f, position.y + 32f, TerrainField.MaximumWorldCoordinate ) );
		var height = RegionalLandforms.BoundHeight( column, CurrentField.Settings ).Maximum;
		CurrentField.GetCorrectionRange( column, out var minimumCorrection, out _, includeRevision: false );
		// Negative corrections build terrain. This clearance includes saved edits,
		// without paging in a column or changing the field to create a spawn platform.
		position.z = height + MathF.Max( 0f, -minimumCorrection ) + 128f;
		player.WorldPosition = position;
		var body = player.Components.Get<Rigidbody>();
		if ( body.IsValid() ) body.Velocity = Vector3.Zero;
		// Existing collision readiness owns the subsequent fall onto real geometry.
	}
}
