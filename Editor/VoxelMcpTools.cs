using Editor.Mcp;
using System;

[McpToolset( "voxels3", "Voxels3 production smoke controls" )]
public static class VoxelMcpTools
{
	/// <summary>
	/// Start or stop moving the active local player around a horizontal figure-eight at world Z zero.
	/// </summary>
	/// <param name="enabled">True to start or reconfigure movement; false to stop at the current position.</param>
	/// <param name="speed">Horizontal movement speed in world units per second.</param>
	/// <param name="distance">Maximum X distance from the starting center; the Y reach is half this value.</param>
	[McpTool( "player_figure_eight" )]
	public static string PlayerFigureEight( bool enabled = true, float speed = 320f, float distance = 1024f )
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before running the player figure-eight." );
		}

		var manager = FindManager();
		var result = manager.ConfigurePlayerFigureEight( enabled, speed, distance );
		return $"Player figure-eight {result}.";
	}

	/// <summary>
	/// Log and return the latest complete three-pillar performance window.
	/// Task and revision are passive caller-supplied labels; no source-control or network lookup occurs.
	/// </summary>
	/// <param name="task">Task or scenario label stored with the structured record.</param>
	/// <param name="revision">Externally supplied Git commit or other source revision label.</param>
	[McpTool( "performance_overview" )]
	public static string PerformanceOverview( string task = "", string revision = "" )
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before logging a performance overview." );
		}

		return FindManager().WritePerformanceOverview( task, revision );
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
