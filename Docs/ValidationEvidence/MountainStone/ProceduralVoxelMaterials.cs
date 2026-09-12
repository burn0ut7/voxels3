/// <summary>Procedural strata over the canonical field. No material state or render dependencies.</summary>
internal static class ProceduralVoxelMaterials
{
	public const int DirtLayers = 8;
	public const float LayerSize = TerrainField.SampleSpacing;
	public const float SoilDepth = (1 + DirtLayers) * LayerSize;
	// Require a strong pointed-ridge contribution in mountain-dominant terrain.
	public const float SnowPeakFraction = 0.76f;
	public const float SnowMountainWeight = 0.75f;

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
		var depth = height - position.z;
		if ( depth >= SoilDepth )
		{
			materialId = VoxelMaterials.Stone;
			return true;
		}
		var above = reader.SampleWorld( position + Vector3.Up * LayerSize );
		materialId = SelectSolid( depth, landform, above <= 0f || height < river.WaterHeight );
		return true;
	}

	// GPU draw mirror lives in voxel_materials.hlsl; numeric parameters/IDs are bound by the adapter.
	private static ushort SelectSolid( float depth, RegionalLandforms.Sample landform, bool covered )
	{
		if ( depth >= SoilDepth ) return VoxelMaterials.Stone;
		if ( depth >= 0f && depth < LayerSize && !covered )
		{
			return landform.Mountains >= SnowMountainWeight && landform.PeakFraction >= SnowPeakFraction
				? VoxelMaterials.Snow : VoxelMaterials.Grass;
		}
		return VoxelMaterials.Dirt;
	}
}
