using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>Immutable spatial derivative shared by CPU field queries and GPU transport.</summary>
internal sealed class RiverSpatialIndex
{
	public RiverNetwork.Segment[] Segments { get; }
	public SpatialNode[] Nodes { get; }

	public RiverSpatialIndex( RiverNetwork.Segment[] ownedSegments, CancellationToken cancellation )
	{
		Segments = ownedSegments;
		var nodes = new List<SpatialNode>();
		if ( ownedSegments.Length > 0 ) BuildNode( ownedSegments, 0, ownedSegments.Length, nodes, cancellation );
		Nodes = nodes.ToArray();
	}

	public RiverNetwork.Sample SampleWorld( Vector3 position, float naturalHeight, float seaLevel )
	{
		var px = position.x;
		var py = position.y;
		var height = naturalHeight;
		var direction = Vector2.Zero;
		var bestDistance = float.PositiveInfinity;
		var shoulder = Math.Clamp( RiverNetwork.MinimumBankWidth + MathF.Max( 0f, naturalHeight - seaLevel ) * RiverNetwork.ValleySlope,
			RiverNetwork.MinimumBankWidth, RiverNetwork.BankWidth );
		var nodeIndex = 0;
		while ( nodeIndex < Nodes.Length )
		{
			ref readonly var node = ref Nodes[nodeIndex];
			var awayX = MathF.Max( MathF.Max( node.MinimumX - px, px - node.MaximumX ), 0f );
			var awayY = MathF.Max( MathF.Max( node.MinimumY - py, py - node.MaximumY ), 0f );
			var separation = MathF.Sqrt( awayX * awayX + awayY * awayY );
			var skip = separation >= shoulder;
			if ( separation > 0f )
			{
				var bank = Math.Clamp( separation / shoulder, 0f, 1f );
				var blend = bank * bank * (3f - 2f * bank);
				var lowerHeight = seaLevel + (naturalHeight - seaLevel) * blend;
				skip |= height <= seaLevel || naturalHeight > seaLevel && lowerHeight > height + 0.0625f;
			}
			if ( skip ) { nodeIndex = node.Escape; continue; }
			if ( node.Count == 0 ) { nodeIndex++; continue; }
			for ( var index = node.First; index < node.First + node.Count; index++ )
			{
				ref readonly var segment = ref Segments[index];
				var start = segment.Start;
				var end = segment.End;
				var startX = start.x;
				var startY = start.y;
				var dx = end.x - startX;
				var dy = end.y - startY;
				var lengthSquared = dx * dx + dy * dy;
				var t = lengthSquared > 0f ? Math.Clamp( ((px - startX) * dx + (py - startY) * dy) / lengthSquared, 0f, 1f ) : 0f;
				var offsetX = px - (startX + t * dx);
				var offsetY = py - (startY + t * dy);
				var distance = MathF.Sqrt( offsetX * offsetX + offsetY * offsetY );
				var radius = segment.StartRadius + t * (segment.EndRadius - segment.StartRadius);
				if ( distance >= radius + shoulder ) continue;
				var surface = start.z + t * (end.z - start.z);
				var normalized = distance / radius;
				var depth = segment.StartDepth + t * (segment.EndDepth - segment.StartDepth);
				var bed = surface - depth * MathF.Max( 0f, 1f - normalized * normalized );
				var bank = Math.Clamp( (distance - radius) / shoulder, 0f, 1f );
				var blend = bank * bank * (3f - 2f * bank);
				height = MathF.Min( height, (1f - blend) * bed + blend * naturalHeight );
				if ( normalized < bestDistance )
				{
					bestDistance = normalized;
					direction = distance < radius && lengthSquared > 0f
						? new Vector2( dx, dy ) / MathF.Sqrt( lengthSquared ) : Vector2.Zero;
				}
			}
			nodeIndex = node.Escape;
		}
		return new RiverNetwork.Sample( height, seaLevel, direction );
	}

	// Conservative wet-support query for generation of cells whose corners can
	// all be dry while a narrow reach passes through their interior.
	public float MinimumWetRadius( Vector3 minimum, Vector3 maximum )
	{
		var minimumRadius = float.PositiveInfinity;
		var index = 0;
		while ( index < Nodes.Length )
		{
			ref readonly var node = ref Nodes[index];
			if ( node.MaximumX < minimum.x || node.MinimumX > maximum.x ||
				node.MaximumY < minimum.y || node.MinimumY > maximum.y )
			{
				index = node.Escape;
				continue;
			}
			if ( node.Count == 0 ) { index++; continue; }
			for ( var item = node.First; item < node.First + node.Count; item++ )
			{
				ref readonly var segment = ref Segments[item];
				var radius = segment.Radius;
				if ( MathF.Max( segment.Start.x, segment.End.x ) + radius >= minimum.x &&
					MathF.Min( segment.Start.x, segment.End.x ) - radius <= maximum.x &&
					MathF.Max( segment.Start.y, segment.End.y ) + radius >= minimum.y &&
					MathF.Min( segment.Start.y, segment.End.y ) - radius <= maximum.y )
					minimumRadius = MathF.Min( minimumRadius, MathF.Min( segment.StartRadius, segment.EndRadius ) );
			}
			index = node.Escape;
		}
		return minimumRadius;
	}

	internal readonly struct SpatialNode( Vector4 bounds, int escape, int first, int count )
	{
		public readonly float MinimumX = bounds.x;
		public readonly float MinimumY = bounds.y;
		public readonly float MaximumX = bounds.z;
		public readonly float MaximumY = bounds.w;
		public readonly int Escape = escape;
		public readonly int First = first;
		public readonly int Count = count;
		public Vector4 Bounds => new( MinimumX, MinimumY, MaximumX, MaximumY );
	}
	private static readonly SegmentComparer CompareX = new( false );
	private static readonly SegmentComparer CompareY = new( true );

	private sealed class SegmentComparer( bool yAxis ) : IComparer<RiverNetwork.Segment>
	{
		public int Compare( RiverNetwork.Segment a, RiverNetwork.Segment b )
		{
			var first = yAxis ? a.Start.y + a.End.y : a.Start.x + a.End.x;
			var second = yAxis ? b.Start.y + b.End.y : b.Start.x + b.End.x;
			var comparison = first.CompareTo( second );
			if ( comparison != 0 ) return comparison;
			comparison = a.Start.x.CompareTo( b.Start.x );
			if ( comparison != 0 ) return comparison;
			comparison = a.Start.y.CompareTo( b.Start.y );
			if ( comparison != 0 ) return comparison;
			comparison = a.End.x.CompareTo( b.End.x );
			if ( comparison != 0 ) return comparison;
			comparison = a.End.y.CompareTo( b.End.y );
			if ( comparison != 0 ) return comparison;
			comparison = a.StartRadius.CompareTo( b.StartRadius );
			return comparison != 0 ? comparison : a.EndRadius.CompareTo( b.EndRadius );
		}
	}

	private static void BuildNode( RiverNetwork.Segment[] segments, int first, int count,
		List<SpatialNode> nodes, CancellationToken cancellation )
	{
		cancellation.ThrowIfCancellationRequested();
		var minimumX = float.PositiveInfinity;
		var minimumY = float.PositiveInfinity;
		var maximumX = float.NegativeInfinity;
		var maximumY = float.NegativeInfinity;
		for ( var index = first; index < first + count; index++ )
		{
			var segment = segments[index];
			// Wet support, not the much wider valley. Distance to this box is a
			// conservative lower bound on bank distance. Padding covers float error.
			var radius = segment.Radius + 1f;
			minimumX = MathF.Min( minimumX, MathF.Min( segment.Start.x, segment.End.x ) - radius );
			minimumY = MathF.Min( minimumY, MathF.Min( segment.Start.y, segment.End.y ) - radius );
			maximumX = MathF.Max( maximumX, MathF.Max( segment.Start.x, segment.End.x ) + radius );
			maximumY = MathF.Max( maximumY, MathF.Max( segment.Start.y, segment.End.y ) + radius );
		}
		var slot = nodes.Count;
		nodes.Add( default );
		if ( count > 8 )
		{
			Array.Sort( segments, first, count, maximumY - minimumY > maximumX - minimumX ? CompareY : CompareX );
			var left = count / 2;
			BuildNode( segments, first, left, nodes, cancellation );
			BuildNode( segments, first + left, count - left, nodes, cancellation );
		}
		// Preorder plus a subtree escape index permits stackless shader traversal.
		nodes[slot] = new SpatialNode( new Vector4( minimumX, minimumY, maximumX, maximumY ),
			nodes.Count, first, count <= 8 ? count : 0 );
	}

}
