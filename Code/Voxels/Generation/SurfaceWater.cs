/// <summary>The medium at a point; water never changes solid density or collision.</summary>
public enum WorldMedium
{
	Air,
	Solid,
	Water
}

/// <summary>Static surface reservoirs above the pre-cave seabed. No simulation or mutable state.</summary>
internal static class SurfaceWater
{
	public const int CurrentVersion = 7;
	public const float DefaultSeaLevel = 0f;
	public const float MinimumSeaLevel = -8192f;
	public const float MaximumSeaLevel = 8192f;

	// Exact sea level is the top boundary; underground voids below the original
	// seabed stay dry, including holes opened by later edits. Solid wins ties.
	public static WorldMedium Resolve( float z, float surfaceHeight, float solidDensity, float waterHeight )
	{
		if ( solidDensity <= 0f ) return WorldMedium.Solid;
		return surfaceHeight < z && z < waterHeight ? WorldMedium.Water : WorldMedium.Air;
	}
}
