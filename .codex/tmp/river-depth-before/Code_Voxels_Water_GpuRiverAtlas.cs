using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Packs canonical CPU reaches into a sampled texture. This deliberately consumes
/// no storage-buffer binding in the already-full transition compute pipeline.
/// </summary>
internal sealed class GpuRiverAtlas : IDisposable
{
	public const int TextureWidth = 1024;
	// Integer offsets remain exactly representable in the float4 transport.
	public const int MaximumTexels = 8 * 1024 * 1024;
	private static readonly object SharedGate = new();
	private const int MaximumSharedEntries = 64;
	private const long MaximumSharedBytes = 64L * 1024 * 1024;
	private static readonly List<Data> SharedData = new();
	private static ProceduralTerrainSettings _sharedSettings;
	private static long _sharedBytes;
	private static int _sharedTerrainVersion;
	private static int _sharedRiverVersion;
	private static long _packCount;
	private static long _sharedReuseCount;
	private static double _packMilliseconds;
	private Texture _texture;
	private int _height;
	public long CapacityBytes => (long)TextureWidth * _height * 16;
	public Vector4 Grid { get; private set; }
	private int _nodeTexels;
	private ProceduralTerrainSettings _settings;
	public static Vector4 Rules => new( RiverNetwork.NodeSpacing, RiverNetwork.Depth,
		RiverNetwork.BankWidth, RiverNetwork.MinimumBankWidth );

	internal sealed record Data( ProceduralTerrainSettings Settings, Vector4 Grid, Vector4[] Texels, int Height, int NodeTexels );
	internal sealed record Metrics( long Packs, long SharedReuses, double PackMilliseconds, long RetainedBytes, int Entries );

	public static Metrics CaptureMetrics()
	{
		lock ( SharedGate ) return new Metrics( _packCount, _sharedReuseCount, _packMilliseconds,
			_sharedBytes, SharedData.Count );
	}

	private static Vector4 ToGrid( RiverNetwork.NodeId minimum, int width, int height ) =>
		new( minimum.X * RiverNetwork.NodesPerPatch, minimum.Y * RiverNetwork.NodesPerPatch,
			width * RiverNetwork.NodesPerPatch, height * RiverNetwork.NodesPerPatch );

	// SharedGate owns the recipe and completed-transport LRU. No GPU objects live here.
	private static void SetSharedRecipe( ProceduralTerrainSettings settings )
	{
		if ( _sharedSettings == settings && _sharedTerrainVersion == ProceduralTerrainSdf.CurrentVersion &&
			_sharedRiverVersion == RiverNetwork.CurrentVersion ) return;
		SharedData.Clear();
		_sharedBytes = 0;
		_sharedSettings = settings;
		_sharedTerrainVersion = ProceduralTerrainSdf.CurrentVersion;
		_sharedRiverVersion = RiverNetwork.CurrentVersion;
	}

	private static Data FindShared( ProceduralTerrainSettings settings, Vector4 grid )
	{
		lock ( SharedGate )
		{
			SetSharedRecipe( settings );
			for ( var index = SharedData.Count - 1; index >= 0; index-- )
			{
				var data = SharedData[index];
				if ( data.Grid != grid ) continue;
				SharedData.RemoveAt( index );
				SharedData.Add( data );
				_sharedReuseCount++;
				return data;
			}
			return null;
		}
	}

	/// <summary>Null means this lane already owns the matching complete texture.</summary>
	public System.Threading.Tasks.Task<Data> Prepare( ProceduralTerrainSettings settings,
		SdfWorldAabb bounds, CancellationToken cancellation )
	{
		cancellation.ThrowIfCancellationRequested();
		// The shader's upper grid edge is exclusive, while density bounds are closed.
		// A sample exactly on that edge must acquire the neighboring patch.
		if ( _texture is not null && _settings == settings &&
			bounds.Minimum.x >= Grid.x * RiverNetwork.NodeSpacing &&
			bounds.Minimum.y >= Grid.y * RiverNetwork.NodeSpacing &&
			bounds.Maximum.x < (Grid.x + Grid.z) * RiverNetwork.NodeSpacing &&
			bounds.Maximum.y < (Grid.y + Grid.w) * RiverNetwork.NodeSpacing )
		{
			return System.Threading.Tasks.Task.FromResult<Data>( null );
		}
		// Match the exact patch footprint that Capture would pack. A broad superset
		// saves CPU work but makes every GPU density query traverse a larger tree.
		var low = RiverNetwork.PatchAt( bounds.Minimum );
		var high = RiverNetwork.PatchAt( bounds.Maximum );
		var shared = FindShared( settings, ToGrid( low, high.X - low.X + 1, high.Y - low.Y + 1 ) );
		if ( shared is not null ) return System.Threading.Tasks.Task.FromResult( shared );
		return GameTask.RunInThreadAsync( () =>
		{
			var region = RiverWorld.For( settings ).Capture( bounds, cancellation );
			return Pack( region, cancellation );
		} );
	}

	/// <summary>Numerical packing only; run alongside terrain preparation, not in a draw callback.</summary>
	public static Data Pack( RiverWorld.Region region, CancellationToken cancellation = default )
	{
		cancellation.ThrowIfCancellationRequested();
		var grid = ToGrid( region.Minimum, region.Width, region.Height );
		// Another worker may have completed this footprint after Prepare's lookup.
		var shared = FindShared( region.Settings, grid );
		if ( shared is not null ) return shared;
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		var spatial = new RiverSpatialIndex( region.CopyUniqueSegments( cancellation ), cancellation );
		var segments = spatial.Segments;
		var nodes = spatial.Nodes;
		var nodeTexels = checked(nodes.Length * 2);
		var count = (long)nodeTexels + segments.Length * 2L;
		if ( count > MaximumTexels )
			throw new InvalidOperationException( "River atlas exceeds its 128 MiB numerical transport budget." );
		var height = Math.Max( 1, (int)((count + TextureWidth - 1) / TextureWidth) );
		var texels = new Vector4[checked(TextureWidth * height)];
		var cursor = 0;
		foreach ( var node in nodes )
		{
			texels[cursor++] = node.Bounds;
			texels[cursor++] = new Vector4( node.Escape * 2, nodeTexels + node.First * 2, node.Count, 0f );
		}
		foreach ( var segment in segments )
		{
			texels[cursor++] = new Vector4( segment.Start, segment.StartRadius );
			texels[cursor++] = new Vector4( segment.End, segment.EndRadius );
		}
		var data = new Data( region.Settings, grid, texels, height, nodeTexels );
		cancellation.ThrowIfCancellationRequested();
		lock ( SharedGate )
		{
			_packCount++;
			_packMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
			SetSharedRecipe( data.Settings );
			var bytes = (long)texels.Length * 16;
			if ( bytes <= MaximumSharedBytes )
			{
				for ( var index = SharedData.Count - 1; index >= 0; index-- )
				{
					if ( SharedData[index].Grid != grid ) continue;
					_sharedBytes -= (long)SharedData[index].Texels.Length * 16;
					SharedData.RemoveAt( index );
				}
				while ( SharedData.Count >= MaximumSharedEntries || _sharedBytes + bytes > MaximumSharedBytes )
				{
					_sharedBytes -= (long)SharedData[0].Texels.Length * 16;
					SharedData.RemoveAt( 0 );
				}
				SharedData.Add( data );
				_sharedBytes += bytes;
			}
		}
		return data;
	}


	/// <summary>Publish on the same engine/render boundary that owns the consuming resource.</summary>
	public void Update( Data data )
	{
		if ( data is null ) return;
		if ( _texture is null || _height != data.Height )
		{
			var replacement = Texture.Create( TextureWidth, data.Height, ImageFormat.RGBA32323232F )
				.WithData<Vector4>( data.Texels.AsSpan() ).WithName( "voxel_river_reaches" ).Finish();
			var previous = _texture;
			_texture = replacement;
			_height = data.Height;
			previous?.Dispose();
		}
		else
		{
			_texture.Update<Vector4>( data.Texels.AsSpan() );
		}
		_settings = data.Settings;
		Grid = data.Grid;
		_nodeTexels = data.NodeTexels;
	}

	public void Bind( RenderAttributes attributes )
	{
		attributes.Set( "RiverData", _texture );
		attributes.Set( "RiverGrid", Grid );
		attributes.Set( "RiverRules", Rules );
		attributes.Set( "RiverValleySlope", RiverNetwork.ValleySlope );
		attributes.Set( "RiverTextureWidth", TextureWidth );
		attributes.Set( "RiverNodeTexels", _nodeTexels );
	}

	public void Bind( Sandbox.Rendering.CommandList.AttributeAccess attributes )
	{
		attributes.Set( "RiverData", _texture );
		attributes.Set( "RiverGrid", Grid );
		attributes.Set( "RiverRules", Rules );
		attributes.Set( "RiverValleySlope", RiverNetwork.ValleySlope );
		attributes.Set( "RiverTextureWidth", TextureWidth );
		attributes.Set( "RiverNodeTexels", _nodeTexels );
	}

	public void Dispose()
	{
		_texture?.Dispose();
		_texture = null;
		_height = 0;
	}
}
