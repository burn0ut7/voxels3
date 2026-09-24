using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Canonical depth-view mesh shared by authored and seeded trees.</summary>
internal static class TreeDistantModel
{
	private sealed record Cached( string Source, string Far, int LayoutVersion, Model Model );
	private static readonly Dictionary<string, Cached> Cache = new();
	private sealed class ExportManifest
	{
		[JsonPropertyName( "files" )] public Dictionary<string, ExportFile> Files { get; set; }
		[JsonPropertyName( "render_models" )] public RenderModel[] RenderModels { get; set; }
	}
	private sealed class RenderModel
	{
		[JsonPropertyName( "filename" )] public string Filename { get; set; }
		[JsonPropertyName( "role" )] public string Role { get; set; }
	}
	private sealed class ExportFile
	{
		[JsonPropertyName( "sha256" )] public string Hash { get; set; }
	}
	private sealed class FarManifest
	{
		public int Version { get; set; }
		public DistantWoodManifest DistantWood { get; set; }
		public int Azimuths { get; set; }
		public string NormalEncoding { get; set; }
		public string Specimen { get; set; }
		public float[] Center { get; set; }
		public float Diameter { get; set; }
		public Dictionary<string, string> SourceHashes { get; set; }
	}

	private sealed class DistantWoodManifest
	{
		public string Specimen { get; set; }
		public string SourceFile { get; set; }
		public string SourceSHA256 { get; set; }
		public Dictionary<string, int> Triangles { get; set; }
	}

	public static Model Load( string key, BBox sourceBounds, IEnumerable<Model> sourceModels, out HashSet<Model> foliageModels )
	{
		foliageModels = new HashSet<Model>();
		if ( string.IsNullOrEmpty( key ) || key.Contains( '/' ) || key.Contains( '\\' ) || key.Contains( ".." ) )
			throw new InvalidOperationException( "Invalid tree asset key." );
		var folder = $"models/tree_lab/{key}";
		var farPath = $"textures/trees/impostors/{key}.json";
		if ( !FileSystem.Mounted.FileExists( farPath ) ) return null;
		var sourceText = FileSystem.Mounted.ReadAllText( $"{folder}/manifest.json" );
		var farText = FileSystem.Mounted.ReadAllText( farPath );
		var manifest = JsonSerializer.Deserialize<ExportManifest>( sourceText );
		var farManifest = JsonSerializer.Deserialize<FarManifest>( farText );
		if ( farManifest?.Version is not (3 or 4) || farManifest.NormalEncoding != "signed-object-rgb" ||
			farManifest.Specimen != key || farManifest.Azimuths is not (8 or 16) || farManifest.Center?.Length != 3 ||
			!float.IsFinite( farManifest.Diameter ) || farManifest.Diameter <= 0f || farManifest.SourceHashes is null || manifest?.Files is null )
			throw new InvalidOperationException( $"Invalid distant tree metadata: {key}" );
		foreach ( var file in manifest.Files )
			if ( file.Value?.Hash is null || !farManifest.SourceHashes.TryGetValue( $"Assets/{folder}/{file.Key}", out var hash ) || hash != file.Value.Hash )
				throw new InvalidOperationException( $"Distant tree bake is stale: {key}/{file.Key}" );
		var expected = new HashSet<Model>();
		var woodModels = 0;
		foreach ( var entry in manifest.RenderModels ?? new[] { new RenderModel { Filename = $"{key}.vmdl" } } )
		{
			var filename = entry.Filename;
			if ( string.IsNullOrEmpty( filename ) || filename.Contains( '/' ) || filename.Contains( '\\' ) ||
				filename.Contains( ".." ) || !manifest.Files.ContainsKey( filename ) )
				throw new InvalidOperationException( $"Invalid tree render membership: {key}" );
			var sourceModel = Model.Load( $"{folder}/{filename}" );
			if ( !expected.Add( sourceModel ) )
				throw new InvalidOperationException( $"Invalid tree render membership: {key}" );
			if ( entry.Role == "foliage" ) foliageModels.Add( sourceModel );
			if ( entry.Role == "wood" ) woodModels++;
		}
		foreach ( var model in sourceModels )
			if ( !expected.Remove( model ) )
				throw new InvalidOperationException( $"Tree render pieces differ from distant bake: {key}" );
		if ( expected.Count != 0 )
			throw new InvalidOperationException( $"Tree render pieces are missing: {key}" );
		var center = new Vector3( farManifest.Center[0], farManifest.Center[1], farManifest.Center[2] );
		var diameter = farManifest.Diameter;
		if ( !float.IsFinite( center.x ) || !float.IsFinite( center.y ) || !float.IsFinite( center.z ) ||
			(center - sourceBounds.Center).Length > 0.05f || Math.Abs( diameter - sourceBounds.Size.Length * 1.04f ) > 0.05f )
			throw new InvalidOperationException( $"Distant tree framing differs from imported model: {key}" );
		var separateWood = woodModels > 0 && foliageModels.Count > 0;
		if ( separateWood && farManifest.DistantWood is null )
			throw new InvalidOperationException( $"Distant trunk geometry is missing: {key}" );
		if ( Cache.TryGetValue( key, out var cached ) && cached.Source == sourceText && cached.Far == farText && cached.LayoutVersion == 8 ) return cached.Model;
		// Vertical strips retain the root-anchored bend; all views share this mesh.
		const int rows = 16;
		var vertices = new Vertex[(rows + 1) * 2];
		var indices = new int[rows * 6];
		for ( var row = 0; row <= rows; row++ )
		{
			for ( var column = 0; column < 2; column++ )
				vertices[row * 2 + column] = new Vertex
				{
					Position = center, Normal = Vector3.Up, Tangent = new Vector4( Vector3.Forward, 1f ), Color = Color.White,
					TexCoord0 = new Vector4( column, row / (float)rows, 0f, 0f ), TexCoord1 = new Vector4( diameter, 0f, 0f, 0f )
				};
			if ( row == rows ) continue;
			var first = row * 2;
			indices[row * 6] = first; indices[row * 6 + 1] = first + 1; indices[row * 6 + 2] = first + 2;
			indices[row * 6 + 3] = first + 1; indices[row * 6 + 4] = first + 3; indices[row * 6 + 5] = first + 2;
		}
		var bounds = new BBox( center - new Vector3( diameter * 0.62f + 8f ), center + new Vector3( diameter * 0.62f + 8f ) );
		var materialKey = key;
		var material = Material.Load( $"materials/trees/impostors/{materialKey}.vmat" );
		if ( material is null ) throw new InvalidOperationException( $"Missing distant tree material: {materialKey}" );
		material = material.CreateCopy();
		material.Set( "g_vTreeSourceHalfExtents", sourceBounds.Size * 0.5f );
		material.Set( "g_flTreeAzimuths", (float)farManifest.Azimuths );
		material.Set( "g_flTreeLodFade", 0f );
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices.AsSpan() );
		mesh.CreateIndexBuffer( indices.Length, indices.AsSpan() );
		mesh.Bounds = bounds;
		// The vertex shader expands coincident centers into a diameter-wide view.
		// Streaming cannot infer world/UV scale from that collapsed mesh.
		mesh.UvDensity = diameter * farManifest.Azimuths;
		var builder = Model.Builder.AddMesh( mesh );
		if ( separateWood )
		{
			var woodMetadata = farManifest.DistantWood;
			if ( woodMetadata.Specimen != key || woodMetadata.SourceFile is null || woodMetadata.Triangles is null ||
				!farManifest.SourceHashes.TryGetValue( $"Assets/{folder}/{woodMetadata.SourceFile}", out var woodSourceHash ) ||
				woodSourceHash != woodMetadata.SourceSHA256 )
				throw new InvalidOperationException( $"Distant wood geometry is stale: {key}" );
			var wood = Model.Load( $"{folder}/distant_wood.vmdl" );
			if ( wood is null || wood.IsError || wood.Materials.Length != 1 )
				throw new InvalidOperationException( $"Invalid distant wood geometry: {key}" );
			var woodDraws = wood.MeshInfo.Meshes.SelectMany( part => part.DrawCalls ).ToArray();
			if ( woodDraws.Length != 1 )
				throw new InvalidOperationException( $"Distant wood requires one draw: {key}" );
			var woodVertices = wood.GetVertices();
			var woodIndices = Array.ConvertAll( wood.GetIndices(), index => checked((int)index + woodDraws[0].BaseVertex) );
			var expectedTriangles = 0;
			foreach ( var count in woodMetadata.Triangles.Values ) expectedTriangles += count;
			if ( woodIndices.Length != expectedTriangles * 3 )
				throw new InvalidOperationException( $"Distant wood triangle count differs from bake: {key}" );
			var woodMaterial = wood.Materials[0].CreateCopy();
			woodMaterial.Set( "g_flTreeLodFar", 1f );
			var woodMesh = new Mesh( woodMaterial );
			woodMesh.CreateVertexBuffer<Vertex>( woodVertices.Length, woodVertices.AsSpan() );
			woodMesh.CreateIndexBuffer( woodIndices.Length, woodIndices.AsSpan() );
			woodMesh.Bounds = bounds;
			woodMesh.UvDensity = woodDraws[0].UvDensity;
			builder = builder.AddMesh( woodMesh );
		}
		var result = builder.WithViewBounds( bounds ).Create();
		Cache[key] = new( sourceText, farText, 8, result );
		return result;
	}
}
