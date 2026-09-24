using System;
using System.Collections.Generic;

/// <summary>Owns derived draw resources for published water chunks, never world state.</summary>
internal sealed class SurfaceWaterRenderer : SceneCustomObject
{
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_water.shader" );
	private readonly RenderAttributes _attributes = new();
	private readonly object _sync = new();
	private readonly Dictionary<GpuSdfDescriptor, ChunkDraw> _chunks = new();
	private readonly List<GpuSdfDescriptor> _retiring = new();
	private readonly List<DrawPage> _pages = new();
	private bool _presentationReady;
	private Color _tint;
	private Color _skyTint;
	private Vector3 _appearance;

	public int ChunkCount { get; private set; }
	public int VertexCount { get; private set; }
	public long UploadedVertices { get; private set; }
	public int BatchCount { get; private set; }
	public long FlowTextureBytes { get; private set; }

	public SurfaceWaterRenderer( SceneWorld world ) : base( world )
	{
		Flags.IsOpaque = false;
		Flags.IsTranslucent = true;
		Flags.NeedsLightProbe = true;
		_attributes.Set( "WaterFlowSamples", (float)GeneratedWaterCells.SamplesPerAxis );
	}

	public void SetAppearance( Color tint, float visibilityMeters, float flowSpeed, float rippleStrength, Color skyTint )
	{
		var appearance = new Vector3( visibilityMeters, flowSpeed, rippleStrength );
		lock ( _sync )
		{
			if ( _tint == tint && _skyTint == skyTint && _appearance == appearance ) return;
			_tint = tint;
			_skyTint = skyTint;
			_appearance = appearance;
			_attributes.Set( "WaterTint", new Vector3( tint.r, tint.g, tint.b ) );
			_attributes.Set( "WaterSkyTint", new Vector3( skyTint.r, skyTint.g, skyTint.b ) );
			// Engine/world units are inches. Visibility is the half-transmission distance.
			_attributes.Set( "WaterVisibility", visibilityMeters * 39.37008f );
			_attributes.Set( "WaterFlowSpeed", flowSpeed );
			_attributes.Set( "WaterRippleStrength", rippleStrength );
		}
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
			// Reuse freed atlas slots before admitting the new publication. A visible
			// previous edit result can outlive its cache entry until its replacement.
			_retiring.Clear();
			foreach ( var pair in _chunks )
			{
				if ( retainedChunks.ContainsKey( pair.Key ) ) continue;
				if ( coverage.TryGetValue( pair.Key.Key, out var visible ) && visible.Descriptor == pair.Key ) continue;
				_retiring.Add( pair.Key );
			}
			foreach ( var key in _retiring )
			{
				var draw = _chunks[key];
				draw.Page.ReleaseSlot( draw.Slot );
				_chunks.Remove( key );
			}

			foreach ( var page in _pages ) page.BeginPublication();
			VertexCount = 0;
			ChunkCount = coverage.Count;
			foreach ( var pair in coverage )
			{
				var chunk = pair.Value;
				if ( chunk.Vertices.Length == 0 ) continue;
				if ( !_chunks.TryGetValue( chunk.Descriptor, out var draw ) )
				{
					var hasFlow = chunk.Cells.Flow is not null;
					DrawPage page = null;
					foreach ( var candidate in _pages )
					{
						if ( candidate.HasFlow == hasFlow && candidate.HasSpace )
						{
							page = candidate;
							break;
						}
					}
					if ( page is null )
					{
						page = new DrawPage( hasFlow );
						_pages.Add( page );
					}
					draw = new ChunkDraw( page, page.AddFlow( chunk.Cells.Flow ) );
					_chunks.Add( chunk.Descriptor, draw );
				}
				draw.Page.Append( chunk, draw.Slot );
				VertexCount += chunk.Vertices.Length;
			}
			BatchCount = 0;
			FlowTextureBytes = 0;
			for ( var i = _pages.Count - 1; i >= 0; i-- )
			{
				var page = _pages[i];
				if ( page.Owners == 0 )
				{
					page.Release();
					_pages.RemoveAt( i );
					continue;
				}
				UploadedVertices += page.Publish();
				if ( page.VertexCount > 0 ) BatchCount++;
				if ( page.HasFlow ) FlowTextureBytes += (long)DrawPage.TextureSize * DrawPage.TextureSize * 4;
			}
		}
	}

	public void Release()
	{
		lock ( _sync )
		{
			_presentationReady = false;
			foreach ( var page in _pages ) page.Release();
			_pages.Clear();
			_chunks.Clear();
			_retiring.Clear();
			ChunkCount = 0;
			VertexCount = 0;
			BatchCount = 0;
			FlowTextureBytes = 0;
			Delete();
		}
	}

	public override void RenderSceneObject()
	{
		if ( Graphics.LayerType != SceneLayerType.Translucent ) return;
		using var profiler = global::Sandbox.Diagnostics.Performance.Scope( VoxelPerformanceProfiler.WaterRender );
		lock ( _sync )
		{
			if ( !_presentationReady ) return;
			var frustum = Graphics.Frustum;
			var captured = false;
			foreach ( var page in _pages )
			{
				// Stop at the first visible owner. The GPU clips the few off-screen
				// hull vertices in this batch; opaque-scene capture keeps its exact gate.
				var visible = false;
				foreach ( var bounds in page.ActiveBounds )
				{
					if ( !frustum.IsInside( bounds, partially: true ) ) continue;
					visible = true;
					break;
				}
				if ( !visible ) continue;
				if ( !captured )
				{
					Graphics.GrabFrameTexture( "WaterSceneColor", _attributes, Graphics.DownsampleMethod.Box, 2 );
					Graphics.SetupLighting( this, _attributes );
					captured = true;
				}
				_attributes.Set( "WaterFlow", page.Flow ?? Texture.Black );
				_attributes.Set( "WaterFlowTextureSize", page.HasFlow ? (float)DrawPage.TextureSize : 1f );
				Graphics.Draw( page.Buffer, _material, 0, page.VertexCount, _attributes );
			}
		}
	}

	private sealed record ChunkDraw( DrawPage Page, int Slot );

	[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
	private struct DrawVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.TexCoord( 0 )] public Vector4 Coverage;
		[VertexLayout.TexCoord( 1 )] public Vector2 FlowOrigin;
	}

	private sealed class DrawPage
	{
		// One immutable 33x33 RGBA8 tile per retained river/marsh chunk. First-fit slot
		// reuse bounds page count by the peak retained owner count, not travel time.
		private const int TilesPerAxis = 16;
		private const int Slots = TilesPerAxis * TilesPerAxis;
		public const int TextureSize = TilesPerAxis * GeneratedWaterCells.SamplesPerAxis;
		private readonly Stack<int> _free = new();
		private DrawVertex[] _vertices = Array.Empty<DrawVertex>();
		private int _bufferCapacity;
		public List<BBox> ActiveBounds { get; } = new();
		public GpuBuffer<DrawVertex> Buffer { get; private set; }
		public Texture Flow { get; private set; }
		public bool HasFlow { get; }
		public bool HasSpace => !HasFlow || _free.Count > 0;
		public int Owners { get; private set; }
		public int VertexCount { get; private set; }

		public DrawPage( bool hasFlow )
		{
			HasFlow = hasFlow;
			if ( !hasFlow ) return;
			for ( var slot = Slots - 1; slot >= 0; slot-- ) _free.Push( slot );
			Flow = Texture.Create( TextureSize, TextureSize, ImageFormat.RGBA8888 )
				.WithName( "Water Flow Page" ).Finish();
		}

		public int AddFlow( byte[] samples )
		{
			Owners++;
			if ( !HasFlow ) return -1;
			var slot = _free.Pop();
			var size = GeneratedWaterCells.SamplesPerAxis;
			Flow.Update<byte>( samples.AsSpan(), slot % TilesPerAxis * size, slot / TilesPerAxis * size, size, size );
			return slot;
		}

		public void ReleaseSlot( int slot )
		{
			Owners--;
			if ( HasFlow ) _free.Push( slot );
		}

		public void BeginPublication()
		{
			VertexCount = 0;
			ActiveBounds.Clear();
		}

		public void Append( SurfaceWaterGeometry.Chunk chunk, int slot )
		{
			var required = VertexCount + chunk.Vertices.Length;
			if ( _vertices.Length < required ) Array.Resize( ref _vertices, Math.Max( required, Math.Max( 256, _vertices.Length * 2 ) ) );
			var minimum = chunk.Cells.Bounds.Minimum;
			var maximum = chunk.Cells.Bounds.Maximum;
			var coverage = new Vector4( minimum.x, minimum.y, maximum.x, maximum.y );
			var origin = HasFlow ? new Vector2( slot % TilesPerAxis, slot / TilesPerAxis ) * GeneratedWaterCells.SamplesPerAxis : Vector2.Zero;
			foreach ( var vertex in chunk.Vertices )
			{
				_vertices[VertexCount++] = new DrawVertex { Position = vertex.Position, Coverage = coverage, FlowOrigin = origin };
			}
			minimum.z = chunk.Cells.SeaLevel - 1f;
			maximum.z = chunk.Cells.SeaLevel + 1f;
			ActiveBounds.Add( new BBox( minimum, maximum ) );
		}

		public int Publish()
		{
			if ( VertexCount == 0 ) return 0;
			if ( _bufferCapacity < VertexCount )
			{
				Buffer?.Dispose();
				_bufferCapacity = _vertices.Length;
				Buffer = new GpuBuffer<DrawVertex>( _bufferCapacity, GpuBuffer.UsageFlags.Vertex, "Water Page Vertices" );
			}
			Buffer.SetData( _vertices.AsSpan( 0, VertexCount ) );
			return VertexCount;
		}

		public void Release()
		{
			Flow?.Dispose();
			Flow = null;
			Buffer?.Dispose();
			Buffer = null;
		}
	}
}
