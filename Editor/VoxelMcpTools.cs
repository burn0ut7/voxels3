using Editor.Mcp;
using System;

[McpToolset( "voxels3", "Voxels3 production smoke controls" )]
public static class VoxelMcpTools
{
	/// <summary>
	/// Open a project scene so production smoke runs do not depend on editor focus or keyboard input.
	/// </summary>
	/// <param name="resourcePath">Project-relative scene resource path.</param>
	[McpTool( "open_project_scene" )]
	public static string OpenProjectScene( string resourcePath = "scenes/basic_example.scene" )
	{
		if ( Game.IsPlaying )
		{
			throw new InvalidOperationException( "Stop play mode before opening a project scene." );
		}

		var asset = Editor.AssetSystem.FindByPath( resourcePath )
			?? throw new InvalidOperationException( $"Scene asset '{resourcePath}' was not found." );
		var scene = asset.LoadResource<SceneFile>()
			?? throw new InvalidOperationException( $"Asset '{resourcePath}' is not a scene." );
		Editor.EditorScene.OpenScene( scene );
		return $"Opened project scene {resourcePath}.";
	}

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

	/// <summary>
	/// Set whether the running game is viewed through the detached editor camera.
	/// </summary>
	/// <param name="ejected">True to detach into the editor camera; false to return to the game camera.</param>
	[McpTool( "set_editor_camera_ejected" )]
	public static string SetEditorCameraEjected( bool ejected = true )
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before changing the editor camera mode." );
		}

		var sceneView = Editor.SceneViewWidget.Current
			?? throw new InvalidOperationException( "No active scene view is available." );
		var desiredView = ejected
			? Editor.SceneViewWidget.ViewMode.GameEjected
			: Editor.SceneViewWidget.ViewMode.Game;

		if ( sceneView.CurrentView != desiredView )
		{
			sceneView.ToggleEject();
		}

		if ( sceneView.CurrentView != desiredView )
		{
			throw new InvalidOperationException(
				$"Editor camera mode did not change to {desiredView}; current mode is {sceneView.CurrentView}." );
		}

		return $"Editor camera mode is {sceneView.CurrentView}.";
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
