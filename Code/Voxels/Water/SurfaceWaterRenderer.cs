using System;
using System.Collections.Generic;

/// <summary>Owns derived draw resources for published water chunks, never world state.</summary>
internal sealed class SurfaceWaterRenderer
{
	private readonly SceneWorld _world;
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_water.shader" );
	private readonly RenderAttributes _attributes = new();
	private readonly object _sync = new();
	private readonly Dictionary<GpuSdfDescriptor, ChunkDraw> _chunks = new();
	private readonly List<GpuSdfDescriptor> _retiring = new();
	private long _publication;
	private bool _presentationReady;

	public int ChunkCount { get; private set; }
	public int VertexCount { get; private set; }
	public long UploadedVertices { get; private set; }

	public SurfaceWaterRenderer( SceneWorld world )
	{
		_world = world;
		_attributes.Set( "WaterCheckerSize", TerrainField.SampleSpacing );
		var water = VoxelMaterials.Get( VoxelMaterials.Water );
		_attributes.Set( "WaterDark", water.DarkColor );
		_attributes.Set( "WaterLight", water.LightColor );
	}

	public void SetPresentationReady( bool ready )
	{
		lock ( _sync ) _presentationReady = ready;
	}

	public void Update( IReadOnlyDictionary<GpuMeshRegionKey, SurfaceWaterGeometry.Chunk> coverage,
		IReadOnlyDictionary<GpuSdfDescriptor, SurfaceWaterGeometry.Chunk> retainedChunks )
	{
		lock ( _sync )
		{
			_publication++;
			VertexCount = 0;
			ChunkCount = coverage.Count;
			foreach ( var pair in coverage )
			{
				var chunk = pair.Value;
				if ( chunk.Vertices.Length == 0 ) continue;
				if ( !_chunks.TryGetValue( chunk.Descriptor, out var draw ) )
				{
					draw = new ChunkDraw( this, chunk );
					_chunks.Add( chunk.Descriptor, draw );
					UploadedVertices += chunk.Vertices.Length;
				}
				draw.Publication = _publication;
				VertexCount += chunk.Vertices.Length;
			}
			_retiring.Clear();
			foreach ( var pair in _chunks )
				if ( pair.Value.Publication != _publication && !retainedChunks.ContainsKey( pair.Key ) ) _retiring.Add( pair.Key );
			foreach ( var key in _retiring )
			{
				_chunks[key].Release();
				_chunks.Remove( key );
			}
		}
	}

	public void Release()
	{
		lock ( _sync )
		{
			_presentationReady = false;
			foreach ( var chunk in _chunks.Values ) chunk.Release();
			_chunks.Clear();
			_retiring.Clear();
			ChunkCount = 0;
			VertexCount = 0;
		}
	}

	private sealed class ChunkDraw : SceneCustomObject
	{
		private readonly SurfaceWaterRenderer _owner;
		private GpuBuffer<SurfaceWaterGeometry.WaterVertex> _vertices;
		private readonly int _count;
		public long Publication;
		private readonly Vector4 _coverage;

		public ChunkDraw( SurfaceWaterRenderer owner, SurfaceWaterGeometry.Chunk chunk ) : base( owner._world )
		{
			_owner = owner;
			_count = chunk.Vertices.Length;
			_vertices = new GpuBuffer<SurfaceWaterGeometry.WaterVertex>( _count, GpuBuffer.UsageFlags.Vertex, "Chunk Water Vertices" );
			_vertices.SetData( chunk.Vertices.AsSpan() );
			var minimum = chunk.Cells.Bounds.Minimum;
			var maximum = chunk.Cells.Bounds.Maximum;
			_coverage = new Vector4( minimum.x, minimum.y, maximum.x, maximum.y );
			minimum.z = chunk.Cells.SeaLevel - 1f;
			maximum.z = chunk.Cells.SeaLevel + 1f;
			Bounds = new BBox( minimum, maximum );
		}

		public void Release()
		{
			_vertices?.Dispose();
			_vertices = null;
			Delete();
		}

		public override void RenderSceneObject()
		{
			lock ( _owner._sync )
			{
				if ( _owner._presentationReady && Publication == _owner._publication && _vertices is not null )
				{
					_owner._attributes.Set( "WaterCoverage", _coverage );
					Graphics.Draw( _vertices, _owner._material, 0, _count, _owner._attributes );
				}
			}
		}
	}
}
