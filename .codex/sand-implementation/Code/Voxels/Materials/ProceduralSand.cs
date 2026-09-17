/// <summary>Sand eligibility over the canonical landform; region sampling is material-independent.</summary>
internal static class ProceduralSand
{
	public const float ShoreMinimumHeight = -512f;
	public const float ShoreMaximumHeight = 64f;
	public const float PatchMaximumHeight = 96f;
	public const float SurfaceDepth = 96f;
	public const float BuriedMinimumDepth = ProceduralVoxelMaterials.SoilDepth;
	public const float BuriedMaximumDepth = 384f;
	public static readonly MaterialSpawnRegion SurfacePatches = new( new Vector3( 1024f, 1024f, 1f ), 0.35f, 17011u, true );
	public static readonly MaterialSpawnRegion BuriedDeposits = new( new Vector3( 768f, 768f, 384f ), 0.22f, 29137u, false );

	public static bool Contains( Vector3 position, float depth, float height, float mountains, ProceduralTerrainSettings settings )
	{
		if ( depth < 0f || depth > BuriedMaximumDepth ) return false;
		var relativeHeight = height - settings.SeaLevel;
		if ( relativeHeight < ShoreMinimumHeight || relativeHeight > PatchMaximumHeight ) return false;
		if ( depth < SurfaceDepth )
		{
			if ( relativeHeight <= ShoreMaximumHeight ) return true;
			return mountains < ProceduralVoxelMaterials.MountainStoneWeight && SurfacePatches.Contains( position, settings.WorldSeed );
		}
		return relativeHeight <= ShoreMaximumHeight && depth >= BuriedMinimumDepth && BuriedDeposits.Contains( position, settings.WorldSeed );
	}
}
