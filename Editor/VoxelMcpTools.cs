using Editor.Mcp;
using System;

[McpToolset( "voxels3", "Voxels3 production smoke controls" )]
public static class VoxelMcpTools
{
	[EditorEvent.Frame]
	public static void RepairEjectedRenderCamera()
	{
		if ( !Game.IsPlaying ) return;
		var sceneView = Editor.SceneViewWidget.Current;
		if ( sceneView?.CurrentView != Editor.SceneViewWidget.ViewMode.GameEjected ) return;
		if ( TryRepairEjectedCamera( sceneView ) )
		{
			Log.Info(
				"[VoxelWorld] editor.camera.recreated reason=\"ejected camera scene has no main camera\"" );
		}
	}

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
	/// Request terrain configuration changes through the running component's normal validated properties.
	/// </summary>
	/// <param name="gameplayRadius">Authoritative gameplay radius.</param>
	/// <param name="minimumVisualLod">Lowest enabled visual level.</param>
	/// <param name="maximumVisualLod">Highest enabled visual level.</param>
	/// <param name="lod0VisualHalfExtent">LOD0 visual half extent in regions.</param>
	/// <param name="lodCacheHalfExtent">Shared coarse cache half extent in regions.</param>
	/// <param name="cellsPerAxis">Regular region cell count; only 32 is supported.</param>
	/// <param name="baseCellSize">LOD0 cell size; only 16 is supported.</param>
	[McpTool( "set_terrain_configuration" )]
	public static object SetTerrainConfiguration(
		int gameplayRadius = 4,
		int minimumVisualLod = 0,
		int maximumVisualLod = 2,
		int lod0VisualHalfExtent = 4,
		int lodCacheHalfExtent = 8,
		int cellsPerAxis = 32,
		float baseCellSize = 16f )
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before changing terrain configuration." );
		}

		var manager = FindManager();
		manager.GameplayRadius = gameplayRadius;
		manager.MinimumVisualLod = minimumVisualLod;
		manager.MaximumVisualLod = maximumVisualLod;
		manager.Lod0VisualHalfExtent = lod0VisualHalfExtent;
		manager.LodCacheHalfExtent = lodCacheHalfExtent;
		manager.CellsPerAxis = cellsPerAxis;
		manager.CellSize = baseCellSize;
		return new
		{
			manager.GameplayRadius,
			manager.MinimumVisualLod,
			manager.MaximumVisualLod,
			manager.Lod0VisualHalfExtent,
			manager.LodCacheHalfExtent,
			manager.CellsPerAxis,
			manager.CellSize
		};
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

		var recreatedStaleCamera = ejected && sceneView.CurrentView == desiredView &&
			TryRepairEjectedCamera( sceneView );

		if ( sceneView.CurrentView != desiredView )
		{
			throw new InvalidOperationException(
				$"Editor camera mode did not change to {desiredView}; current mode is {sceneView.CurrentView}." );
		}

		return $"Editor camera mode is {sceneView.CurrentView}; " +
			$"recreatedStaleCamera={recreatedStaleCamera}.";
	}

	/// <summary>
	/// Position the actual detached game viewport camera. The stock editor-camera MCP tools target
	/// Application.Editor.Camera, which is intentionally null while the scene view is GameEjected.
	/// </summary>
	/// <param name="position">World position as 'x,y,z'.</param>
	/// <param name="angles">View angles as 'pitch,yaw,roll'.</param>
	/// <param name="fieldOfView">Perspective vertical field of view in degrees.</param>
	[McpTool( "set_ejected_camera" )]
	public static object SetEjectedCamera(
		string position,
		string angles,
		[Sandbox.Range( 10f, 140f )] float fieldOfView = 60f )
	{
		var viewport = GetEjectedViewport();
		var state = viewport.State;
		state.View = Editor.SceneViewportWidget.ViewMode.Perspective;
		state.CameraPosition = Vector3.Parse( position );
		state.CameraRotation = Rotation.From( Angles.Parse( angles ) );

		var camera = viewport.Renderer.Camera;
		camera.WorldPosition = state.CameraPosition;
		camera.WorldRotation = state.CameraRotation;
		camera.FieldOfView = fieldOfView;
		var rendererScene = viewport.Renderer.Scene;
		return new
		{
			Position = camera.WorldPosition,
			Angles = camera.WorldRotation.Angles(),
			camera.FieldOfView,
			CameraSceneIsGameScene = ReferenceEquals( camera.Scene, Game.ActiveScene ),
			CameraSceneCameraValid = camera.Scene.IsValid() && camera.Scene.Camera.IsValid(),
			CameraSceneCameraIsGameSceneCamera = camera.Scene.IsValid() &&
				ReferenceEquals( camera.Scene.Camera, Game.ActiveScene.Camera ),
			RendererSceneIsGameScene = ReferenceEquals( rendererScene, Game.ActiveScene ),
			RendererSceneCameraIsGameSceneCamera = rendererScene.IsValid() &&
				ReferenceEquals( rendererScene.Camera, Game.ActiveScene.Camera ),
			RendererSceneCameraValid = rendererScene.IsValid() && rendererScene.Camera.IsValid()
		};
	}

	/// <summary>
	/// Render the actual detached game viewport camera, including inherited runtime camera command lists.
	/// </summary>
	[McpTool.ReadOnly( "ejected_camera_screenshot" )]
	public static object EjectedCameraScreenshot(
		[Sandbox.Range( 16, 4096 )] int width = 1280,
		[Sandbox.Range( 16, 4096 )] int height = 720 )
	{
		var viewport = GetEjectedViewport();
		var bitmap = new Bitmap( width, height );
		viewport.Renderer.Camera.RenderToBitmap( bitmap, false );
		return bitmap;
	}

	private static Editor.SceneViewportWidget GetEjectedViewport()
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before using the ejected camera." );
		}

		var sceneView = Editor.SceneViewWidget.Current
			?? throw new InvalidOperationException( "No active scene view is available." );
		if ( sceneView.CurrentView != Editor.SceneViewWidget.ViewMode.GameEjected )
		{
			throw new InvalidOperationException( "The scene view must be in GameEjected mode." );
		}

		var viewport = sceneView.GetGameTarget()
			?? throw new InvalidOperationException( "No detached game viewport is available." );
		if ( !viewport.Renderer.Camera.IsValid() )
		{
			throw new InvalidOperationException( "The detached game viewport has no valid camera." );
		}

		return viewport;
	}

	private static bool TryRepairEjectedCamera( Editor.SceneViewWidget sceneView )
	{
		var viewport = sceneView.GetGameTarget();
		var camera = viewport?.Renderer?.Camera;
		if ( !camera.IsValid() || (camera.Scene.IsValid() && camera.Scene.Camera.IsValid()) ) return false;

		camera.GameObject.DestroyImmediate();
		sceneView.ToggleEject();
		sceneView.ToggleEject();
		return true;
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
