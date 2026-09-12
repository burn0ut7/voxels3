/// <summary>Procedural strata over the canonical field. No material state or render dependencies.</summary>
internal static class ProceduralVoxelMaterials
{
	public const int DirtLayers = 8;
	public const float LayerSize = TerrainField.SampleSpacing;
	public const float SoilDepth = (1 + DirtLayers) * LayerSize;
	// Require a strong pointed-ridge contribution in mountain-dominant terrain.
	public const float SnowPeakFraction = 0.76f;
	public const float SnowMountainWeight = 0.75f;
	// Mountain bodies are stone nodes, including the shallow interior below snow.
	public const float MountainStoneWeight = 0.75f;
	// tan(45 degrees)^2. Sample slope on the fixed base lattice, not render triangles.
	public const float MountainGrassMaxSlopeSquared = 1f;

	public static bool TrySample( TerrainFieldSnapshot field, Vector3 position, out float density, out ushort materialId )
	{
		density = 0f;
		materialId = VoxelMaterials.Air;
		if ( !field.TryCaptureRegion( new SdfWorldAabb( position - Vector3.One * LayerSize,
			position + Vector3.One * LayerSize ), out var reader ) ) return false;
		density = reader.SampleWorld( position );
		var landform = RegionalLandforms.SampleNatural( position, field.Settings );
		var river = RiverWorld.For( field.Settings ).GetPatch( RiverNetwork.PatchAt( position ) )
			.SampleWorld( position, landform.Height, field.Settings.SeaLevel );
		var height = river.Height;
		var medium = SurfaceWater.Resolve( position.z, height, density, river.WaterHeight );
		if ( medium != WorldMedium.Solid )
		{
			materialId = medium == WorldMedium.Water ? VoxelMaterials.Water : VoxelMaterials.Air;
			return true;
		}
		var lattice = position / TerrainField.SampleSpacing;
		var placed = reader.SamplePlacedMaterial( new Vector3Int( (int)System.MathF.Floor( lattice.x ),
			(int)System.MathF.Floor( lattice.y ), (int)System.MathF.Floor( lattice.z ) ) );
		if ( placed != 0 )
		{
			materialId = placed;
			return true;
		}
		var depth = height - position.z;
		if ( depth >= SoilDepth )
		{
			materialId = VoxelMaterials.Stone;
			return true;
		}
		var above = reader.SampleWorld( position + Vector3.Up * LayerSize );
		materialId = SelectSolid( depth, landform, above <= 0f || height < river.WaterHeight, position, height, field.Settings );
		return true;
	}

	// Generation mirror lives in voxel_generated_materials.hlsl; drawing consumes generated weights.
	private static ushort SelectSolid( float depth, RegionalLandforms.Sample landform, bool covered,
		Vector3 position, float height, ProceduralTerrainSettings settings )
	{
		if ( depth >= SoilDepth ) return VoxelMaterials.Stone;
		if ( depth < 0f ) return VoxelMaterials.Dirt;
		if ( depth < LayerSize && !covered && landform.Mountains >= SnowMountainWeight && landform.PeakFraction >= SnowPeakFraction ) return VoxelMaterials.Snow;
		if ( depth < LayerSize && !covered )
		{
			if ( landform.Mountains >= MountainStoneWeight )
			{
				var dx = (RegionalLandforms.SampleWorld( position + new Vector3( LayerSize, 0f, 0f ), settings ).Height - height) / LayerSize;
				var dy = (RegionalLandforms.SampleWorld( position + new Vector3( 0f, LayerSize, 0f ), settings ).Height - height) / LayerSize;
				if ( dx * dx + dy * dy > MountainGrassMaxSlopeSquared ) return VoxelMaterials.Stone;
			}
			return VoxelMaterials.Grass;
		}
		if ( landform.Mountains >= MountainStoneWeight ) return VoxelMaterials.Stone;
		return VoxelMaterials.Dirt;
	}
}
