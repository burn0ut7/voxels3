using System;
using System.Collections.Generic;

/// <summary>Batches exposed water surfaces from published terrain chunks; owns no world state.</summary>
internal sealed class SurfaceWaterRenderer : SceneCustomObject
{
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_water.shader" );
	private readonly RenderAttributes _attributes = new();
	private readonly List<SdfWorldAabb> _chunks = new();
	private readonly object _sync = new();
	private Vertex[] _vertices = Array.Empty<Vertex>();
	private ProceduralTerrainSettings? _settings;
	private bool _presentationReady;

	public int ChunkCount => _chunks.Count;
	public int VertexCount { get; private set; }

	public SurfaceWaterRenderer( SceneWorld world ) : base( world ) { }

	public void SetPresentationReady( bool ready )
	{
		lock ( _sync )
		{
			_presentationReady = ready;
		}
	}

	public void Update( IReadOnlyList<SdfWorldAabb> chunks, ProceduralTerrainSettings settings )
	{
		lock ( _sync )
		{
			var changed = _settings != settings || _chunks.Count != chunks.Count;
			for ( var index = 0; !changed && index < chunks.Count; index++ )
			{
				changed = _chunks[index] != chunks[index];
			}
			if ( !changed ) return;
			if ( _settings != settings )
			{
				_attributes.Set( "WaterTerrain", new Vector4( settings.WorldSeed, settings.LandAmount, settings.MountainAmount, settings.PlainsAmount ) );
				_attributes.Set( "WaterScales", new Vector4( settings.ContinentalScale, settings.MountainRegionScale, settings.LocalLandformScale, settings.ReliefHeight ) );
				_attributes.Set( "WaterRuggedness", settings.Ruggedness );
				_attributes.Set( "WaterSeaLevel", settings.SeaLevel );
				_attributes.Set( "WaterCheckerSize", TerrainField.SampleSpacing );
				var water = VoxelMaterials.Get( VoxelMaterials.Water );
				_attributes.Set( "WaterDark", water.DarkColor );
				_attributes.Set( "WaterLight", water.LightColor );
			}
			_settings = settings;
			VertexCount = chunks.Count * 6;
			if ( _vertices.Length < VertexCount )
			{
				Array.Resize( ref _vertices, Math.Max( VertexCount, _vertices.Length * 2 ) );
			}
			_chunks.Clear();
			var minimum = Vector3.Zero;
			var maximum = Vector3.Zero;
			for ( var index = 0; index < chunks.Count; index++ )
			{
				var chunk = chunks[index];
				_chunks.Add( chunk );
				var a = new Vector3( chunk.Minimum.x, chunk.Minimum.y, settings.SeaLevel );
				var b = new Vector3( chunk.Maximum.x, chunk.Minimum.y, settings.SeaLevel );
				var c = new Vector3( chunk.Maximum.x, chunk.Maximum.y, settings.SeaLevel );
				var d = new Vector3( chunk.Minimum.x, chunk.Maximum.y, settings.SeaLevel );
				var vertex = index * 6;
				_vertices[vertex] = new Vertex( a );
				_vertices[vertex + 1] = new Vertex( b );
				_vertices[vertex + 2] = new Vertex( c );
				_vertices[vertex + 3] = new Vertex( a );
				_vertices[vertex + 4] = new Vertex( c );
				_vertices[vertex + 5] = new Vertex( d );
				minimum = index == 0 ? a : new Vector3( MathF.Min( minimum.x, a.x ), MathF.Min( minimum.y, a.y ), settings.SeaLevel );
				maximum = index == 0 ? c : new Vector3( MathF.Max( maximum.x, c.x ), MathF.Max( maximum.y, c.y ), settings.SeaLevel );
			}
			Bounds = VertexCount == 0 ? default : new BBox( minimum - Vector3.Up, maximum + Vector3.Up );
		}
	}

	public override void RenderSceneObject()
	{
		lock ( _sync )
		{
			if ( _presentationReady && VertexCount > 0 )
			{
				Graphics.Draw( _vertices.AsSpan( 0, VertexCount ), VertexCount, _material, _attributes );
			}
		}
	}
}
