using Editor.Mcp;
using System;

[McpToolset( "voxels3", "Voxels3 production smoke controls" )]
public static class VoxelMcpTools
{
	/// <summary>
	/// Run the automated figure-eight performance test and save one structured result.
	/// </summary>
	/// <param name="task">Required task or scenario identifier stored with the result.</param>
	/// <param name="revision">Required Git commit or other source revision stored with the result.</param>
	/// <param name="speed">Horizontal movement speed in world units per second.</param>
	/// <param name="distance">Maximum X distance from the starting center; the Y reach is half this value.</param>
	/// <param name="loopCount">Number of complete figure-eight loops to measure before automatic completion.</param>
	[McpTool( "run_performance_test" )]
	public static string RunPerformanceTest(
		string task,
		string revision,
		float speed = 2500f,
		float distance = 50000f,
		int loopCount = 1 )
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before running the player figure-eight." );
		}

		var result = FindManager().StartPerformanceTest( speed, distance, loopCount, task, revision );
		return $"Performance test {result}.";
	}

	private static VoxelManager FindManager()
	{
		VoxelManager manager = null;
		foreach ( var candidate in Game.ActiveScene.GetAllComponents<VoxelManager>() )
		{
			if ( manager is not null )
			{
				throw new InvalidOperationException( "Exactly one active VoxelManager is required." );
			}

			manager = candidate;
		}

		return manager ?? throw new InvalidOperationException( "Exactly one active VoxelManager is required." );
	}
}
