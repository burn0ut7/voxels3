using Editor.Mcp;
using System;

[McpToolset( "voxels3", "Voxels3 production smoke controls" )]
public static class VoxelMcpTools
{
	/// <summary>
	/// Start or cancel the automated figure-eight performance test at world Z zero.
	/// </summary>
	/// <param name="enabled">True to start a new test; false to cancel the active test.</param>
	/// <param name="speed">Horizontal movement speed in world units per second.</param>
	/// <param name="distance">Maximum X distance from the starting center; the Y reach is half this value.</param>
	/// <param name="loopCount">Number of complete figure-eight loops to measure before automatic completion.</param>
	/// <param name="task">Task or scenario label stored with the structured result.</param>
	/// <param name="revision">Externally supplied Git commit or other source revision label.</param>
	[McpTool( "player_figure_eight" )]
	public static string PlayerFigureEight(
		bool enabled = true,
		float speed = 2500f,
		float distance = 50000f,
		int loopCount = 1,
		string task = "",
		string revision = "" )
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before running the player figure-eight." );
		}

		var manager = FindManager();
		if ( !string.IsNullOrWhiteSpace( task ) )
		{
			manager.PerformanceTask = task.Trim();
		}

		if ( !string.IsNullOrWhiteSpace( revision ) )
		{
			manager.PerformanceRevision = revision.Trim();
		}

		var result = manager.ConfigurePlayerFigureEightTest( enabled, speed, distance, loopCount );
		return $"Player figure-eight performance test {result}.";
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
