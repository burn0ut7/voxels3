using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Bounded reconstruction of a seeded drainage graph. A patch owns immutable derived
/// reaches, never terrain edits or engine resources. Neighboring patches reconstruct
/// the same graph from world-space node identities.
/// </summary>
internal static class RiverNetwork
{
	public const int CurrentVersion = 13;
	public const int NodesPerPatch = 8;
	public const int RiverCatchmentNodes = 8;
	public const int MinimumCourseEdges = 16;
	public const float NodeSpacing = 2048f;
	public const float PatchSize = NodesPerPatch * NodeSpacing;
	public const float HalfWidth = 192f;
	// Local half-width deepens the bed gently; bounds use the capped maximum.
	public const float MinimumDepth = 24f;
	public const float BaseDepth = 48f;
	public const float MaximumDepth = 216f;
	public const float DepthPerRadius = 0.125f;
	public const float BankHeight = 0f;
	public const float BankWidth = 6144f;
	public const float MinimumBankWidth = 1536f;
	public const float ValleySlope = 1.5f;
	public const int SegmentsPerReach = 16;
	// Neighbor edge, bounded Bezier handles and lateral displacement, node jitter,
	// variable width and bank support all fit within seven source cells.
	private const int SourceHalo = 7;

	internal readonly record struct NodeId( int X, int Y );
	internal readonly record struct Node( NodeId Id, Vector2 Position, float Height );

	/// <summary>Endpoints are water-surface positions. Only connected, nonzero-length reaches are emitted.</summary>
	internal readonly record struct Segment( Vector3 Start, Vector3 End, float StartRadius, float EndRadius,
		float StartDepth, float EndDepth )
	{
		public float Radius => MathF.Max( StartRadius, EndRadius );
		public SdfWorldAabb Bounds => new(
			new Vector3( MathF.Min( Start.x, End.x ) - Radius - BankWidth,
				MathF.Min( Start.y, End.y ) - Radius - BankWidth, MathF.Min( Start.z, End.z ) - MaximumDepth ),
			new Vector3( MathF.Max( Start.x, End.x ) + Radius + BankWidth,
				MathF.Max( Start.y, End.y ) + Radius + BankWidth, MathF.Max( Start.z, End.z ) + BankHeight ) );
	}

	internal readonly record struct Sample( float Height, float WaterHeight, Vector2 Direction );

	internal sealed class Patch
	{
		private readonly RiverSpatialIndex _spatial;
		public NodeId Coordinate { get; }
		public IReadOnlyList<Segment> Segments { get; }
		public int NodeEvaluations { get; }
		public int RiverNodes { get; }
		public long PayloadBytes { get; }

		internal Patch( NodeId coordinate, Segment[] segments,
			int nodeEvaluations, int riverNodes )
		{
			Coordinate = coordinate;
			_spatial = new RiverSpatialIndex( segments, CancellationToken.None );
			Segments = Array.AsReadOnly( _spatial.Segments );
			NodeEvaluations = nodeEvaluations;
			RiverNodes = riverNodes;
			PayloadBytes = (long)segments.Length * 40;
			PayloadBytes += (long)_spatial.Nodes.Length * 28;
		}


		public float MinimumWetRadius( Vector3 minimum, Vector3 maximum ) => _spatial.MinimumWetRadius( minimum, maximum );

		public Sample SampleWorld( Vector3 position, float naturalHeight, float seaLevel )
		{
			var x = (int)MathF.Floor( position.x / NodeSpacing ) - Coordinate.X * NodesPerPatch;
			var y = (int)MathF.Floor( position.y / NodeSpacing ) - Coordinate.Y * NodesPerPatch;
			if ( x < 0 || x >= NodesPerPatch || y < 0 || y >= NodesPerPatch )
				throw new ArgumentOutOfRangeException( nameof( position ), "River sample is outside its acquired patch." );
			return _spatial.SampleWorld( position, naturalHeight, seaLevel );
		}
	}

	public static NodeId PatchAt( Vector3 position ) => new(
		(int)MathF.Floor( position.x / PatchSize ), (int)MathF.Floor( position.y / PatchSize ) );

	/// <summary>Pure numerical preparation; may execute on a terrain preparation worker.</summary>
	public static Patch Build( NodeId coordinate, ProceduralTerrainSettings settings, CancellationToken cancellation )
	{
		var builder = new Builder( settings, cancellation );
		return builder.Build( coordinate );
	}

	private sealed class Builder
	{
		private readonly ProceduralTerrainSettings _settings;
		private readonly CancellationToken _cancellation;
		private readonly RiverWorld _world;
		private readonly Dictionary<NodeId, RiverDrainageBasin> _basins = new();

		public Builder( ProceduralTerrainSettings settings, CancellationToken cancellation )
		{
			_settings = settings;
			_cancellation = cancellation;
			_world = RiverWorld.For( settings );
		}

		private RiverDrainageBasin Basin( NodeId id )
		{
			var coordinate = RiverDrainageBasin.Coordinate( id );
			if ( _basins.TryGetValue( coordinate, out var basin ) ) return basin;
			_cancellation.ThrowIfCancellationRequested();
			basin = _world.GetBasin( id );
			_basins.Add( coordinate, basin );
			return basin;
		}

		private Node GetNode( NodeId id ) => Basin( id ).GetNode( id );
		private NodeId Downstream( NodeId id ) => Basin( id ).Downstream( id );
		private int Catchment( NodeId id ) => Basin( id ).Catchment( id );

		private float NodeWidth( NodeId id )
		{
			var node = GetNode( id );
			var variation = WidthVariation( new Vector3( node.Position.x, node.Position.y, 0f ) );
			var width = HalfWidth * variation * MathF.Sqrt( Math.Clamp( Catchment( id ) / 48f, 0.16f, 9f ) );
			return Basin( id ).UpstreamLength( id ) > 0 ? width : width * 0.4f;
		}

		private float WidthVariation( Vector3 position ) => 1f + 0.22f * TerrainNoise.SimplexNoise3D(
			new Vector3( position.x, position.y, 0f ), NodeSpacing * 4f,
			unchecked((uint)_settings.WorldSeed) ^ 0xA24BAED5u );

		private float BedDepth( Vector3 position, float radius )
		{
			// Independent, slowly varying pools and shoals; evaluated once per endpoint.
			var variation = 1f + 0.65f * TerrainNoise.SimplexNoise3D(
				new Vector3( position.x, position.y, 0f ), NodeSpacing * 2f,
				unchecked((uint)_settings.WorldSeed) ^ 0xD1B54A35u );
			return Math.Clamp( (BaseDepth + radius * DepthPerRadius) * variation, MinimumDepth, MaximumDepth );
		}

		private bool HasCourse( NodeId id )
		{
			var basin = Basin( id );
			var distance = basin.Distance( id );
			return distance != int.MaxValue && distance > 0 &&
				distance + basin.UpstreamLength( id ) >= MinimumCourseEdges;
		}


		private void AddReach( List<Segment> segments, Node node, Node target, float startWidth, float endWidth, uint salt )
		{
			var id = node.Id;
			var startHeight = _settings.SeaLevel;
			var endHeight = _settings.SeaLevel;
			var start = new Vector3( node.Position.x, node.Position.y, startHeight );
			var end = new Vector3( target.Position.x, target.Position.y, endHeight );
			var delta = end - start;
			delta.z = 0f;
			var length = delta.Length;
			var nextNode = GetNode( Downstream( target.Id ) );
			var outgoing = new Vector3( nextNode.Position.x - target.Position.x, nextNode.Position.y - target.Position.y, 0f );
			if ( outgoing.LengthSquared < 1f ) outgoing = delta;
			var controlA = start + delta / 3f;
			var controlB = end - outgoing.Normal * (length / 3f);
			var side = new Vector3( -delta.y, delta.x, 0f ) / length;
			var shape = TerrainNoise.Hash( id.X, id.Y, unchecked((uint)_settings.WorldSeed) ^ salt );
			var phase = (shape & 65535u) / 65535f * (2f * MathF.PI);
			var amplitude = length * (0.06f + ((shape >> 16) & 255u) / 255f * 0.08f);
			// A broad trunk cannot follow stream-sized wiggles without its round
			// outer banks overlapping into lobes. Preserve endpoints and tangents.
			amplitude *= length / (length + 6f * MathF.Max( startWidth, endWidth ));
			// Width changes over several reaches instead of inflating every reach
			// into a separate pool. Normalize endpoint modulation to keep joins exact.
			var startVariation = WidthVariation( start );
			var endVariation = WidthVariation( end );
			// Endpoint widths and heights agree. Lateral variation vanishes with
			// its derivative at endpoints, preserving the shared tangent joins.
			var previous = start;
			var previousWidth = startWidth;
			var previousDepth = BedDepth( start, startWidth );
			for ( var step = 1; step <= SegmentsPerReach; step++ )
			{
				var t = step / (float)SegmentsPerReach;
				var u = 1f - t;
				var envelope = MathF.Sin( MathF.PI * t );
				envelope *= envelope;
				var next = start * (u * u * u) + controlA * (3f * u * u * t) +
					controlB * (3f * u * t * t) + end * (t * t * t) +
					side * (amplitude * envelope * MathF.Sin( 2f * MathF.PI * t + phase ));
				next.z = _settings.SeaLevel;
				var width = (startWidth + (endWidth - startWidth) * t) *
					WidthVariation( next ) / (startVariation + (endVariation - startVariation) * t);
				if ( step == SegmentsPerReach ) { next = end; width = endWidth; }
				var depth = BedDepth( next, width );
				segments.Add( new Segment( previous, next, previousWidth, width, previousDepth, depth ) );
				previous = next;
				previousWidth = width;
				previousDepth = depth;
			}
		}

		private void AddDelta( List<Segment> segments, Node source, Node mouth )
		{
			var forward = mouth.Position - source.Position;
			var side = new Vector2( -forward.y, forward.x ).Normal;
			// Split only at a retained large mouth. Each branch ends at a naturally
			// submerged node, never a random inland point or a disconnected pool.
			for ( var sign = -1; sign <= 1; sign += 2 )
			{
				var best = mouth;
				var bestScore = NodeSpacing * 0.3f;
				for ( var y = -1; y <= 1; y++ )
				{
					for ( var x = -1; x <= 1; x++ )
					{
						var candidate = GetNode( new NodeId( mouth.Id.X + x, mouth.Id.Y + y ) );
						if ( candidate.Id == mouth.Id || candidate.Height > _settings.SeaLevel ) continue;
						var offset = candidate.Position - source.Position;
						if ( Vector2.Dot( offset, forward ) <= 0f ) continue;
						var score = sign * Vector2.Dot( candidate.Position - mouth.Position, side );
						if ( score <= bestScore ) continue;
						best = candidate;
						bestScore = score;
					}
				}
				if ( best.Id == mouth.Id ) continue;
				var width = NodeWidth( source.Id );
				AddReach( segments, source, best, width * 0.7f, width * 0.5f,
					unchecked(0xC2B2AE35u + (uint)sign) );
			}
		}

		public Patch Build( NodeId coordinate )
		{
			var minimumX = coordinate.X * NodesPerPatch;
			var minimumY = coordinate.Y * NodesPerPatch;
			var segments = new List<Segment>();
			var riverNodes = 0;
			for ( var y = minimumY - SourceHalo; y < minimumY + NodesPerPatch + SourceHalo; y++ )
			{
				for ( var x = minimumX - SourceHalo; x < minimumX + NodesPerPatch + SourceHalo; x++ )
				{
					_cancellation.ThrowIfCancellationRequested();
					var id = new NodeId( x, y );
					var node = GetNode( id );
					if ( !Basin( id ).RiverEnabled( id ) || node.Height <= _settings.SeaLevel || Catchment( id ) < RiverCatchmentNodes ||
						!HasCourse( id ) ) continue;
					var target = GetNode( Downstream( id ) );
					riverNodes++;
					AddReach( segments, node, target, NodeWidth( id ), NodeWidth( target.Id ), 0x9FB21C65u );
					if ( target.Height <= _settings.SeaLevel && Catchment( id ) >= 96 )
						AddDelta( segments, node, target );
				}
			}

			return new Patch( coordinate, segments.ToArray(), _basins.Count * RiverDrainageBasin.NodeCount, riverNodes );
		}
	}
}
