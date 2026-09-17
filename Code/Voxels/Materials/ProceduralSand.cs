using System;

/// <summary>Patchy beaches and sparse river sediment over the canonical landform.</summary>
internal static class ProceduralSand
{
	// Wavelength, coverage cutoff, maximum dry-bank height, maximum layer depth.
	public static readonly Vector4 Ocean = new( 2048f, -0.10f, 48f, 96f );
	public static readonly Vector4 River = new( 512f, 0.40f, 12f, 64f );
	public const float MinimumLayerDepth = 3f * ProceduralVoxelMaterials.LayerSize;
	public const float MinimumRiverHeight = -256f;
	public const float RiverNaturalHeight = 32f;
	public const float RiverBlendHeight = 128f;
	public const float OceanReach = 640f;
	public const float RiverReach = 48f;
	public const uint CoverageSalt = 17011u;
	public const uint DetailSalt = 41333u;
	public const float BuriedMinimumDepth = ProceduralVoxelMaterials.SoilDepth;
	public const float BuriedMaximumDepth = 256f;
	public static readonly MaterialSpawnRegion BuriedDeposits = new( new Vector3( 768f, 768f, 384f ), 0.15f, 29137u, false );

	public static float SampleLayerDepth( Vector3 position, float height, float naturalHeight, ProceduralTerrainSettings settings )
	{
		var relativeHeight = height - settings.SeaLevel;
		if ( relativeHeight > Ocean.z || (relativeHeight < MinimumRiverHeight && naturalHeight > settings.SeaLevel) ) return 0f;
		var river = Math.Clamp( (naturalHeight - settings.SeaLevel - RiverNaturalHeight) / RiverBlendHeight, 0f, 1f );
		var recipe = Ocean + (River - Ocean) * river;
		if ( relativeHeight > recipe.z ) return 0f;
		var xy = new Vector3( position.x, position.y, 0f );
		var seed = unchecked((uint)settings.WorldSeed);
		var coverage = 0.75f * TerrainNoise.SimplexNoise3D( xy, recipe.x, seed ^ CoverageSalt ) +
			0.25f * TerrainNoise.SimplexNoise3D( xy, recipe.x * 0.25f, seed ^ DetailSalt );
		if ( coverage <= recipe.y ) return 0f;
		var strength = Math.Clamp( (coverage - recipe.y) * 2f, 0f, 1f );
		var bankHeight = recipe.z * (0.15f + 0.85f * strength);
		if ( relativeHeight > bankHeight ) return 0f;
		if ( relativeHeight >= 0f )
		{
			var reach = (OceanReach + (RiverReach - OceanReach) * river) * (0.25f + 0.75f * strength);
			var nearWater = false;
			for ( var direction = 0; direction < 4; direction++ )
			{
				var offset = direction switch
				{
					0 => new Vector3( reach, 0f, 0f ),
					1 => new Vector3( -reach, 0f, 0f ),
					2 => new Vector3( 0f, reach, 0f ),
					_ => new Vector3( 0f, -reach, 0f )
				};
				if ( RegionalLandforms.SampleWorld( position + offset, settings ).Height < settings.SeaLevel )
				{
					nearWater = true;
					break;
				}
			}
			if ( !nearWater ) return 0f;
		}
		return MinimumLayerDepth + (recipe.w - MinimumLayerDepth) * strength;
	}

	public static bool Contains( Vector3 position, float depth, float height, float naturalHeight, ProceduralTerrainSettings settings )
	{
		if ( depth < 0f || depth > BuriedMaximumDepth ) return false;
		var layerDepth = SampleLayerDepth( position, height, naturalHeight, settings );
		if ( layerDepth <= 0f ) return false;
		return depth < layerDepth || (depth >= BuriedMinimumDepth && BuriedDeposits.Contains( position, settings.WorldSeed ));
	}
}
