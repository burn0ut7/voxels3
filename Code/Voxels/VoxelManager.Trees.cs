using System;

public sealed partial class VoxelManager
{
	[Property, Category( "Terrain Visuals" )]
	public bool SpawnTreesEnabled { get; set; } = true;
	/// <summary>Authored world-space spawn anchor, shared by the scene on every client.</summary>
	[Property, Category( "Terrain Visuals" )]
	public Vector3 SpawnTreeCenter { get; set; } = Vector3.Zero;
	private SpawnTreePopulation _spawnTrees;
	private Vector3 _spawnTreeAnchor;
	private Guid _spawnTreeWorld;

	private void UpdateSpawnTrees()
	{
		var anchor = SpawnTreeCenter.WithZ( 0f );
		if ( !SpawnTreesEnabled || (_spawnTrees is not null && (_spawnTreeWorld != CurrentField.WorldId || _spawnTreeAnchor != anchor)) )
		{
			_spawnTrees?.Dispose();
			_spawnTrees = null;
		}
		if ( !SpawnTreesEnabled ) return;
		if ( _spawnTrees is null )
		{
			_spawnTreeAnchor = anchor;
			_spawnTreeWorld = CurrentField.WorldId;
			_spawnTrees = new SpawnTreePopulation( Scene, _spawnTreeAnchor );
		}
		var ready = HasTerrainReplicaCoverage( TerrainCoverage( ActiveStreamingTarget.WorldPosition, false ) );
		_spawnTrees.Update( CurrentField, Scene.Camera?.WorldPosition ?? ActiveStreamingTarget.WorldPosition, ready,
			Networking.IsHost ? null : _terrainReplicaCoverage );
	}

	protected override void OnDisabled()
	{
		_spawnTrees?.Dispose();
		_spawnTrees = null;
	}

	[ConCmd( "voxel_trees_info" )]
	public static void LogTreesInfo( int candidate = -1, int variant = -1 )
	{
		if ( !TryGetActiveManager( "trees.inspect", out var manager ) ) return;
		var trees = manager._spawnTrees;
		if ( trees is null ) { Log.Info( "[SpawnTrees] disabled" ); return; }
		Log.Info( $"[SpawnTrees] status={trees.Status} trees={trees.Count} distanceVisible={trees.VisibleCount} " +
			$"detailedTrees={trees.DetailCount} distantTrees={trees.FarCount} crossfadeTrees={trees.CrossfadeCount} " +
			$"shadowTrees={trees.ShadowCount} importedModels={trees.ImportedModels} libraryVertices={trees.GeometryVertices} libraryIndices={trees.GeometryIndices} " +
			$"libraryMeshes={trees.GeometryMeshes} peakUpdateMs={trees.PeakUpdateMilliseconds:0.###} peakLoadMs={trees.PeakLoadMilliseconds:0.###} staleBatches={trees.StaleBatches}" );
		trees.LogNearestTrees( manager.Scene.Camera?.WorldPosition ?? manager.SpawnTreeCenter, candidate, variant );
	}

	/// <summary>Reload the shared imported library after authoring and recompiling tree assets.</summary>
	[ConCmd( "voxel_trees_reload" )]
	public static void ReloadImportedTrees()
	{
		if ( !TryGetActiveManager( "trees.reload", out var manager ) ) return;
		manager._spawnTrees?.Dispose();
		manager._spawnTrees = null;
		manager.SpawnTreesEnabled = true;
		Log.Info( "[SpawnTrees] reloading imported Blender models" );
	}
}
