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

		VoxelManager manager = null;
		foreach ( var candidate in Game.ActiveScene.GetAllComponents<VoxelManager>() )
		{
			if ( manager is not null )
			{
				throw new InvalidOperationException( "Exactly one active VoxelManager is required." );
			}

			manager = candidate;
		}

		if ( manager is null )
		{
			throw new InvalidOperationException( "Exactly one active VoxelManager is required." );
		}

		var result = manager.ConfigurePlayerFigureEight( enabled, speed, distance );
		return $"Player figure-eight {result}.";
	}
}
