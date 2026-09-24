/// <summary>Procedural strata over the canonical field. No material state or render dependencies.</summary>
internal static class ProceduralVoxelMaterials
{
	public const int DirtLayers = 8;
	public const float LayerSize = TerrainField.SampleSpacing;
	public const float SoilDepth = (1 + DirtLayers) * LayerSize;
	// Minimum/maximum depth, XY wavelength, independent seed salt.
	public static readonly Vector4 Snow = new( 5f * LayerSize, 10f * LayerSize, 2048f, 62119f );
	// Mountain bodies are stone nodes, including the shallow interior below snow.
	public const float MountainStoneWeight = 0.75f;
	// tan(45 degrees)^2. Sample slope on the fixed base lattice, not render triangles.
	public const float MountainGrassMaxSlopeSquared = 1f;

	/// <summary>Coherent biome strata, biased toward the maximum depth; mirrored in generation HLSL.</summary>
	public static float SampleBiomeLayerDepth( Vector3 position, ProceduralTerrainSettings settings, Vector4 recipe )
	{
		var noise = RegionalLandforms.Noise( position, recipe.z,
			unchecked((uint)settings.WorldSeed) ^ (uint)recipe.w, out _ );
		return recipe.y - (recipe.y - recipe.x) * noise * noise * noise;
	}

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
		var height = TerrainBiomes.RefineHeight( position, field.Settings, landform.Height, river.Height, landform.Mountains );
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
		var habitat = TerrainBiomes.SampleWorld( position, field.Settings, landform.Mountains );
		var cover = TerrainBiomes.CoverThreshold( position, field.Settings );
		var above = depth < LayerSize ? reader.SampleWorld( position + Vector3.Up * LayerSize ) : -1f;
		var exposed = depth >= 0f && depth < LayerSize && above > 0f && height >= river.WaterHeight;
		if ( depth >= 0f && depth < SoilDepth &&
			habitat.MarshWeight( landform.Height, landform.Mountains, field.Settings.SeaLevel ) > cover )
		{
			materialId = exposed && height > river.WaterHeight + 12f ? VoxelMaterials.Grass : VoxelMaterials.Dirt;
			return true;
		}
		if ( depth >= 0f && height >= river.WaterHeight && habitat.Snow > cover &&
			depth < SampleBiomeLayerDepth( position, field.Settings, Snow ) )
		{
			materialId = VoxelMaterials.Snow;
			return true;
		}
		var desertDepth = ProceduralSand.SampleDesertLayerDepth( position, landform.Height, habitat.Desert, cover, field.Settings );
		if ( (depth >= 0f && depth < desertDepth) ||
			ProceduralSand.Contains( position, depth, height, landform.Height, field.Settings, landform.Mountains,
				habitat.MarshWeight( landform.Height, landform.Mountains, field.Settings.SeaLevel ), cover ) )
		{
			materialId = VoxelMaterials.Sand;
			return true;
		}
		if ( depth >= SoilDepth )
		{
			materialId = VoxelMaterials.Stone;
			return true;
		}
		materialId = SelectSolid( depth, landform, !exposed, position, height, field.Settings );
		return true;
	}

	// Generation mirror lives in voxel_generated_materials.hlsl; drawing consumes generated weights.
	private static ushort SelectSolid( float depth, RegionalLandforms.Sample landform, bool covered,
		Vector3 position, float height, ProceduralTerrainSettings settings )
	{
		if ( depth >= SoilDepth ) return VoxelMaterials.Stone;
		if ( depth < 0f ) return VoxelMaterials.Dirt;
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
