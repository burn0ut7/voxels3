public sealed partial class VoxelManager
{
	public const float DefaultGrassRenderRangeMeters = 96f;
	public const float MaximumGrassRenderRangeMeters = 256f;
	private const string GrassRangePreferenceKey = "graphics.grass-range-metres";
	private float _grassRenderRangeMeters = DefaultGrassRenderRangeMeters;

	/// <summary>Local grass draw distance in metres. Zero disables grass; changes apply without rebuilding terrain.</summary>
	[Property, Category( "Terrain Visuals" ), Range( 0f, MaximumGrassRenderRangeMeters )]
	public float GrassRenderRangeMeters
	{
		get => _grassRenderRangeMeters;
		set
		{
			if ( float.IsFinite( value ) )
			{
				_grassRenderRangeMeters = System.Math.Clamp( value, 0f, MaximumGrassRenderRangeMeters );
			}
		}
	}

	/// <summary>Apply and remember this player's graphics preference, independently of scene saves.</summary>
	public void SaveGrassRenderRangePreference( float metres )
	{
		if ( !float.IsFinite( metres ) ) return;
		GrassRenderRangeMeters = metres;
		Game.Cookies.Set( GrassRangePreferenceKey, GrassRenderRangeMeters );
	}

	/// <summary>Read bounded GPU grass counts, including lifetime capacity overflows.</summary>
	[ConCmd( "voxel_grass_info" )]
	public static void LogGrassInfoCommand()
	{
		if ( TryGetActiveManager( "grass.inspect", out var manager ) )
		{
			Log.Info( $"[TerrainGrass] rangeMeters={manager.GrassRenderRangeMeters:0.##}" );
			manager._gpuMesher?.RequestGrassStatistics();
		}
	}
}
