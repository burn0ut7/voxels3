using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Disposable numerical river derivatives for one immutable recipe. Eviction never
/// changes a reach: active regional readers retain immutable patches independently.
/// </summary>
internal sealed class RiverWorld
{
	public const int MaximumCachedPatches = 8192;
	public const int MaximumRegionPatches = 4356;
	private static readonly object RegistryGate = new();
	private readonly record struct RecipeKey( ProceduralTerrainSettings Settings, int TerrainVersion, int RiverVersion );
	private static readonly Dictionary<RecipeKey, WeakReference<RiverWorld>> Registry = new();
	private static RiverWorld _last;
	private readonly object _gate = new();
	private readonly Dictionary<RiverNetwork.NodeId, LinkedListNode<Entry>> _patches = new();
	private readonly LinkedList<Entry> _order = new();
	private readonly Dictionary<RiverNetwork.NodeId, Lazy<RiverDrainageBasin>> _basins = new();
	private readonly Queue<RiverNetwork.NodeId> _basinOrder = new();
	public ProceduralTerrainSettings Settings { get; }
	private readonly RecipeKey _recipe;
	private sealed record Entry( RiverNetwork.NodeId Key, Lazy<RiverNetwork.Patch> Value );

	private RiverWorld( RecipeKey recipe )
	{
		_recipe = recipe;
		Settings = recipe.Settings;
	}

	public static RiverWorld For( ProceduralTerrainSettings settings )
	{
		lock ( RegistryGate )
		{
			var recipe = new RecipeKey( settings, ProceduralTerrainSdf.CurrentVersion, RiverNetwork.CurrentVersion );
			var last = _last;
			if ( last is not null && last._recipe == recipe ) return last;
			if ( !Registry.TryGetValue( recipe, out var reference ) || !reference.TryGetTarget( out last ) )
			{
				// Entries are weak; a previous world and its preparation jobs determine
				// lifetime. Prune dead recipe keys when adding another world recipe.
				var dead = new List<RecipeKey>();
				foreach ( var pair in Registry )
				{
					if ( !pair.Value.TryGetTarget( out _ ) ) dead.Add( pair.Key );
				}
				foreach ( var key in dead ) Registry.Remove( key );
				last = new RiverWorld( recipe );
				Registry[recipe] = new WeakReference<RiverWorld>( last );
			}
			_last = last;
			return last;
		}
	}

	public RiverNetwork.Patch GetPatch( RiverNetwork.NodeId coordinate )
	{
		Lazy<RiverNetwork.Patch> value;
		lock ( _gate )
		{
			if ( _patches.TryGetValue( coordinate, out var existing ) )
			{
				_order.Remove( existing );
				_order.AddLast( existing );
				value = existing.Value.Value;
			}
			else
			{
				while ( _patches.Count >= MaximumCachedPatches )
				{
					_patches.Remove( _order.First.Value.Key );
					_order.RemoveFirst();
				}
				// A shared numerical build has no requesting job's cancellation token.
				// Each build is bounded; callers cancel between patches. Cancelling one
				// consumer must not poison another consumer's identical cached patch.
				value = new Lazy<RiverNetwork.Patch>(
					() => RiverNetwork.Build( coordinate, Settings, CancellationToken.None ), true );
				_patches.Add( coordinate, _order.AddLast( new Entry( coordinate, value ) ) );
			}
		}
		return value.Value;
	}

	public RiverDrainageBasin GetBasin( RiverNetwork.NodeId node )
	{
		var coordinate = RiverDrainageBasin.Coordinate( node );
		Lazy<RiverDrainageBasin> value;
		lock ( _gate )
		{
			if ( !_basins.TryGetValue( coordinate, out value ) )
			{
				if ( _basins.Count >= 64 ) _basins.Remove( _basinOrder.Dequeue() );
				value = new Lazy<RiverDrainageBasin>( () => new RiverDrainageBasin( coordinate, Settings ), true );
				_basins.Add( coordinate, value );
				_basinOrder.Enqueue( coordinate );
			}
		}
		return value.Value;
	}

	public Region Capture( SdfWorldAabb bounds, CancellationToken cancellation = default )
	{
		if ( !float.IsFinite( bounds.Minimum.x ) || !float.IsFinite( bounds.Minimum.y ) ||
			!float.IsFinite( bounds.Maximum.x ) || !float.IsFinite( bounds.Maximum.y ) ||
			bounds.Maximum.x < bounds.Minimum.x || bounds.Maximum.y < bounds.Minimum.y )
			throw new ArgumentException( "River region requires finite ordered XY bounds.", nameof( bounds ) );
		var low = RiverNetwork.PatchAt( bounds.Minimum );
		// Density and geometry bounds are closed: retain the patch on the positive
		// side of an exact boundary, even if the final query is only one sample there.
		var high = RiverNetwork.PatchAt( bounds.Maximum );
		var width = checked(high.X - low.X + 1);
		var height = checked(high.Y - low.Y + 1);
		if ( width <= 0 || height <= 0 || (long)width * height > MaximumRegionPatches )
			throw new ArgumentOutOfRangeException( nameof( bounds ), "River region exceeds the supported clipbox footprint." );
		var patches = new RiverNetwork.Patch[width * height];
		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				cancellation.ThrowIfCancellationRequested();
				patches[x + y * width] = GetPatch( new RiverNetwork.NodeId( low.X + x, low.Y + y ) );
			}
		}
		return new Region( Settings, low, width, height, patches );
	}

	internal sealed class Region
	{
		private readonly RiverNetwork.Patch[] _patches;
		public ProceduralTerrainSettings Settings { get; }
		public RiverNetwork.NodeId Minimum { get; }
		public int Width { get; }
		public int Height { get; }
		public int BinWidth => Width * RiverNetwork.NodesPerPatch;
		public int BinHeight => Height * RiverNetwork.NodesPerPatch;
		public long PayloadBytes { get; }

		internal Region( ProceduralTerrainSettings settings, RiverNetwork.NodeId minimum,
			int width, int height, RiverNetwork.Patch[] patches )
		{
			Settings = settings;
			Minimum = minimum;
			Width = width;
			Height = height;
			_patches = patches;
			foreach ( var patch in patches ) PayloadBytes += patch.PayloadBytes;
		}

		public RiverNetwork.Sample SampleWorld( Vector3 position, float naturalHeight )
		{
			var coordinate = RiverNetwork.PatchAt( position );
			var x = coordinate.X - Minimum.X;
			var y = coordinate.Y - Minimum.Y;
			if ( x < 0 || x >= Width || y < 0 || y >= Height )
				throw new ArgumentOutOfRangeException( nameof( position ), "River sample exceeds its captured region." );
			return _patches[x + y * Width].SampleWorld( position, naturalHeight, Settings.SeaLevel );
		}

		public float MinimumWetRadius( Vector3 minimum, Vector3 maximum )
		{
			var radius = float.PositiveInfinity;
			var low = RiverNetwork.PatchAt( minimum );
			var high = RiverNetwork.PatchAt( maximum );
			for ( var y = low.Y; y <= high.Y; y++ )
			for ( var x = low.X; x <= high.X; x++ )
			{
				radius = MathF.Min( radius, _patches[x - Minimum.X + (y - Minimum.Y) * Width].MinimumWetRadius( minimum, maximum ) );
			}
			return radius;
		}

		public RiverNetwork.Segment[] CopyUniqueSegments( CancellationToken cancellation )
		{
			var seen = new HashSet<RiverNetwork.Segment>();
			var segments = new List<RiverNetwork.Segment>();
			foreach ( var patch in _patches )
			{
				cancellation.ThrowIfCancellationRequested();
				foreach ( var segment in patch.Segments )
				{
					if ( seen.Add( segment ) ) segments.Add( segment );
				}
			}
			return segments.ToArray();
		}

	}
}
