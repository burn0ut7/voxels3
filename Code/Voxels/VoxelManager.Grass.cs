public sealed partial class VoxelManager
{
	/// <summary>Read bounded GPU grass counts, including lifetime capacity overflows.</summary>
	[ConCmd( "voxel_grass_info" )]
	public static void LogGrassInfoCommand()
	{
		if ( TryGetActiveManager( "grass.inspect", out var manager ) )
		{
			manager._gpuMesher?.RequestGrassStatistics();
		}
	}
}
