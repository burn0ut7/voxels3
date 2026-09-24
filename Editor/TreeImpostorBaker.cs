using Editor;
using Editor.Mcp;
using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>Derives distant views from the imported production model.</summary>
[McpToolset( "tree_assets", "Shared imported tree asset baking" )]
public static class TreeImpostorBaker
{
	/// <summary>Bake one elevation row without changing the playable scene.</summary>
	[McpTool( "bake_tree_impostor" )]
	public static async Task<object> Bake( string specimen, int elevationRow )
	{
		if ( string.IsNullOrEmpty( specimen ) || specimen.Length > 100 || elevationRow is < 0 or > 3 )
			throw new ArgumentOutOfRangeException( "Expected an installed specimen and elevation row 0ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ3." );
		foreach ( var character in specimen )
			if ( !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_') )
				throw new ArgumentException( "Specimen must be a single installed specimen key." );
		var root = Path.GetFullPath( Path.Combine( Project.Current.GetAssetsPath(), ".." ) );
		var folder = Path.Combine( root, "Assets/models/tree_lab", specimen );
		var manifestPath = Path.Combine( folder, "manifest.json" );
		using var manifest = JsonDocument.Parse( File.ReadAllText( manifestPath ) );
		if ( !manifest.RootElement.GetProperty( "complete" ).GetBoolean() )
			throw new InvalidOperationException( "Tree source export is incomplete." );
		var hashes = new Dictionary<string, string>();
		foreach ( var entry in manifest.RootElement.GetProperty( "files" ).EnumerateObject() )
		{
			if ( Path.GetFileName( entry.Name ) != entry.Name ) throw new InvalidOperationException( "Invalid source dependency." );
			var hash = Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( Path.Combine( folder, entry.Name ) ) ) ).ToLowerInvariant();
			if ( hash != entry.Value.GetProperty( "sha256" ).GetString() ) throw new InvalidOperationException( $"Stale tree dependency: {entry.Name}" );
			hashes[$"Assets/models/tree_lab/{specimen}/{entry.Name}"] = hash;
		}
		foreach ( var path in new[] { "Editor/TreeImpostorBaker.cs", "Assets/shaders/trees/tree_lab_bark.shader",
			"Assets/shaders/trees/tree_lab_foliage.shader", "Assets/shaders/trees/tree_lab_fine.shader",
			"Assets/shaders/trees/tree_baked_foliage.shader",
			"Assets/shaders/trees/tree_wind.hlsl", "Assets/shaders/trees/tree_wood_motion.hlsl",
			"Assets/shaders/trees/tree_lod_fade.hlsl", "Assets/shaders/trees/tree_bake_depth.hlsl" } )
			hashes[path] = Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( Path.Combine( root, path ) ) ) ).ToLowerInvariant();
		hashes[$"Assets/models/tree_lab/{specimen}/manifest.json"] =
			Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( manifestPath ) ) ).ToLowerInvariant();
		var modelFiles = new List<string>();
		if ( manifest.RootElement.TryGetProperty( "render_models", out var renderModels ) )
		{
			foreach ( var entry in renderModels.EnumerateArray() )
			{
				var filename = entry.GetProperty( "filename" ).GetString();
				if ( string.IsNullOrEmpty( filename ) || Path.GetFileName( filename ) != filename ||
					!filename.EndsWith( ".vmdl", StringComparison.Ordinal ) || modelFiles.Contains( filename ) ||
					!manifest.RootElement.GetProperty( "files" ).TryGetProperty( filename, out _ ) )
					throw new InvalidOperationException( "Invalid render-model dependency." );
				modelFiles.Add( filename );
			}
		}
		else modelFiles.Add( $"{specimen}.vmdl" );
		if ( modelFiles.Count == 0 ) throw new InvalidOperationException( "No render models in source." );
		// Compilation and resource loading have separate completion points. Wait
		// before copying materials, or their motion textures can still be fallbacks.
		foreach ( var entry in manifest.RootElement.GetProperty( "files" ).EnumerateObject() )
		{
			if ( !entry.Name.EndsWith( ".vmat", StringComparison.Ordinal ) ) continue;
			var material = await Material.LoadAsync( $"models/tree_lab/{specimen}/{entry.Name}" );
			if ( material is null ) throw new InvalidOperationException( $"Imported material did not load: {entry.Name}" );
			var expectedMotion = manifest.RootElement.GetProperty( "motion" ).GetProperty( "texture_size" );
			var bakedMaterial = entry.Name == $"{specimen}_baked.vmat"
				&& manifest.RootElement.TryGetProperty( "baked_foliage", out _ );
			var atlasSize = bakedMaterial ? manifest.RootElement.GetProperty( "baked_foliage" ).GetProperty( "atlas_pixels" ) : default;
			var deadline = DateTime.UtcNow.AddSeconds( 30 );
			while ( true )
			{
				var motion = material.GetTexture( "g_tTreeMotion" );
				var color = bakedMaterial ? material.GetTexture( "g_tColor" ) : null;
				var normal = bakedMaterial ? material.GetTexture( "g_tNormal" ) : null;
				var atlasReady = !bakedMaterial || (color is not null && color.IsLoaded
					&& normal is not null && normal.IsLoaded && color.Width == atlasSize[0].GetInt32()
					&& color.Height == atlasSize[1].GetInt32() && normal.Width == color.Width && normal.Height == color.Height);
				if ( motion is not null && motion.IsLoaded && motion.Width == expectedMotion[0].GetInt32()
					&& motion.Height == expectedMotion[1].GetInt32() && atlasReady ) break;
				if ( DateTime.UtcNow >= deadline )
					throw new InvalidOperationException( $"Tree material textures are not ready: {entry.Name}" );
				await Task.Delay( 16 );
			}
		}
		var models = new List<Model>();
		var minimum = new Vector3( float.MaxValue );
		var maximum = new Vector3( float.MinValue );
		foreach ( var filename in modelFiles )
		{
			var model = await Model.LoadAsync( $"models/tree_lab/{specimen}/{filename}" );
			if ( model is null || model.IsError ) throw new InvalidOperationException( $"Imported model did not load: {filename}" );
			models.Add( model );
			minimum = Vector3.Min( minimum, model.Bounds.Mins );
			maximum = Vector3.Max( maximum, model.Bounds.Maxs );
		}
		var center = (minimum + maximum) * 0.5f;
		var diameter = (maximum - minimum).Length * 1.04f;
		if ( !float.IsFinite( diameter ) || diameter <= 0f ) throw new InvalidOperationException( "Invalid tree bounds." );
		var outputPixels = manifest.RootElement.GetProperty( "stage" ).GetString() == "Juvenile" ? 256 : 1024;
		var azimuths = modelFiles.Count > 1 ? 16 : 8;
		var directory = Path.Combine( root, ".codex/tree-build/imported-impostors", specimen );
		Directory.CreateDirectory( directory );
		var rowPath = Path.Combine( directory, $"row-{elevationRow}.json" );
		File.Delete( rowPath );
		var scene = Scene.CreateEditorScene();
		try
		{
			var renderers = new List<ModelRenderer>();
			var originalSets = new List<Material[]>();
			var depthSets = new List<Material[]>();
			foreach ( var model in models )
			{
				ModelRenderer renderer;
				using ( scene.Push() )
				{
					var tree = new GameObject( true, "Imported tree asset bake" );
					renderer = tree.Components.Create<ModelRenderer>();
					renderer.Model = model;
					renderer.LodOverride = modelFiles.Count > 1 ? 2 : 0;
					scene.EditorTick( 0f, 0f );
				}
				if ( !renderer.SceneObject.IsValid() ) throw new InvalidOperationException( "Tree renderer was not created." );
				renderer.SceneObject.Flags.CastShadows = false;
				renderer.SceneObject.Attributes.Set( "TreeWind", Vector4.Zero );
				var originals = new Material[model.Materials.Length];
				var depths = new Material[originals.Length];
				for ( var index = 0; index < originals.Length; index++ )
				{
					originals[index] = model.Materials[index].CreateCopy();
					originals[index].SetFeature( "F_TREE_BAKE_DEPTH", 0 );
					originals[index].Set( "g_flSwayStrength", 0f );
					originals[index].Set( "g_flEdgeAmplitude", 0f );
					originals[index].Set( "g_flBranchAmplitude", 0f );
					originals[index].Set( "g_flAlphaDistanceStart", diameter * 4f );
					originals[index].Set( "g_flAlphaDistanceEnd", diameter * 5f );
					depths[index] = originals[index].CreateCopy();
					depths[index].SetFeature( "F_TREE_BAKE_DEPTH", 1 );
				}
				renderers.Add( renderer );
				originalSets.Add( originals );
				depthSets.Add( depths );
			}
			using var camera = new SceneCamera
			{
				World = scene.SceneWorld, Size = new Vector2( 1024, 1024 ), Ortho = true,
				OrthoHeight = diameter, ZNear = 1f, ZFar = diameter * 4f,
				BackgroundColor = Color.Transparent, EnablePostProcessing = false, AntiAliasing = true
			};
			var bitmap = new Pixmap( 1024, 1024 );
			for ( var view = 0; view < azimuths; view++ )
			{
				camera.Rotation = Rotation.From( new Angles( -15f + elevationRow * 30f, view * (360f / azimuths) + 180f, 0f ) );
				camera.Position = center - camera.Rotation.Forward * diameter * 2f;
				// Camera-facing source leaves and baked patches must use this
				// capture's frame in every pass. The camera is already beyond the
				// detailed handoff, so legacy leaf retention reaches its minimum.
				foreach ( var renderer in renderers )
				{
					renderer.SceneObject.Attributes.Set( "TreeLeafView", new Vector4( camera.Position, 1f ) );
					renderer.SceneObject.Attributes.Set( "TreeLeafViewForward", camera.Rotation.Forward );
					renderer.SceneObject.Attributes.Set( "TreeLeafViewUp", camera.Rotation.Up );
				}
				camera.Attributes.Set( "TreeBakeCenter", center );
				camera.Attributes.Set( "TreeBakeDirection", -camera.Rotation.Forward );
				camera.Attributes.Set( "TreeBakeDiameter", diameter );
				for ( var pass = 0; pass < 4; pass++ )
				{
					for ( var piece = 0; piece < renderers.Count; piece++ )
						for ( var index = 0; index < originalSets[piece].Length; index++ )
							renderers[piece].Materials.SetOverride( index,
								pass == 2 ? depthSets[piece][index] : originalSets[piece][index] );
					camera.DebugMode = pass == 1 ? SceneCameraDebugMode.NormalMap
						: pass == 3 ? SceneCameraDebugMode.AmbientOcclusion : SceneCameraDebugMode.Albedo;
					if ( !camera.RenderToPixmap( bitmap ) ) throw new InvalidOperationException( "Tree capture failed." );
					bitmap.SavePng( Path.Combine( directory, $"{elevationRow}-{view}-{(pass == 0 ? "Albedo" : pass == 1 ? "NormalMap" : pass == 2 ? "Depth" : "AmbientOcclusion")}.png" ) );
				}
				await Task.Delay( 1 );
			}
			var metadata = new
			{
				Version = 2, Specimen = specimen, ElevationRow = elevationRow, RenderModels = modelFiles,
				LeafFacing = "source material feature preserved for all passes",
				Center = new[] { center.x, center.y, center.z }, Diameter = diameter,
				CapturePixels = 1024, OutputPixels = outputPixels, Azimuths = azimuths,
				Elevations = new[] { -15, 15, 45, 75 }, Lod = modelFiles.Count > 1 ? 2 : 0,
				LeafRetention = modelFiles.Count > 1 ? "runtime minimum" : "full",
				Wind = "disabled on object and material copies", SourceHashes = hashes
			};
			File.WriteAllText( rowPath, JsonSerializer.Serialize( metadata, new JsonSerializerOptions { WriteIndented = true } ) );
			return new { Directory = directory, Metadata = metadata };
		}
		finally
		{
			scene.Destroy();
		}
	}
}
