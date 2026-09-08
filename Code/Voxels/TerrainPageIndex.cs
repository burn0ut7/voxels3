using System;
using System.Collections.Generic;

/// <summary>Immutable derived bounds index. Contains coordinates, never page/sample references.</summary>
internal sealed class TerrainPageIndex
{
	private readonly Vector3Int[] _keys;
	private readonly Node[] _nodes;
	private readonly record struct Node( Vector3Int Minimum, Vector3Int Maximum, int End, int KeyIndex );
	private static readonly IComparer<Vector3Int>[] Comparers =
	{
		Comparer<Vector3Int>.Create( ( a, b ) => Compare( a, b, 0 ) ),
		Comparer<Vector3Int>.Create( ( a, b ) => Compare( a, b, 1 ) ),
		Comparer<Vector3Int>.Create( ( a, b ) => Compare( a, b, 2 ) )
	};
	internal static readonly TerrainPageIndex Empty = new( Array.Empty<Vector3Int>() );

	public TerrainPageIndex( IEnumerable<Vector3Int> keys )
	{
		_keys = keys.ToArray();
		_nodes = new Node[Math.Max( 0, _keys.Length * 2 - 1 )];
		var next = 0;
		if ( _keys.Length > 0 ) Build( 0, _keys.Length, ref next );
	}

	private static int Compare( Vector3Int a, Vector3Int b, int axis )
	{
		var order = (axis == 0 ? a.x : axis == 1 ? a.y : a.z).CompareTo( axis == 0 ? b.x : axis == 1 ? b.y : b.z );
		if ( order != 0 ) return order;
		order = a.x.CompareTo( b.x );
		if ( order != 0 ) return order;
		order = a.y.CompareTo( b.y );
		return order != 0 ? order : a.z.CompareTo( b.z );
	}

	private void Build( int start, int count, ref int next )
	{
		var node = next++;
		var minimum = _keys[start];
		var maximum = minimum;
		for ( var i = start + 1; i < start + count; i++ )
		{
			var key = _keys[i];
			minimum = new Vector3Int( Math.Min( minimum.x, key.x ), Math.Min( minimum.y, key.y ), Math.Min( minimum.z, key.z ) );
			maximum = new Vector3Int( Math.Max( maximum.x, key.x ), Math.Max( maximum.y, key.y ), Math.Max( maximum.z, key.z ) );
		}
		if ( count > 1 )
		{
			var extent = maximum - minimum;
			var axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
			Array.Sort( _keys, start, count, Comparers[axis] );
			var left = count / 2;
			Build( start, left, ref next );
			Build( start + left, count - left, ref next );
		}
		_nodes[node] = new Node( minimum, maximum, next, count == 1 ? start : -1 );
	}

	public Enumerator Query( Vector3Int minimum, Vector3Int maximum ) => new( this, minimum, maximum );

	// Preorder subtree end offsets avoid a per-query stack or iterator allocation.
	internal struct Enumerator
	{
		private readonly TerrainPageIndex _index;
		private readonly Vector3Int _minimum;
		private readonly Vector3Int _maximum;
		private int _next;
		public Vector3Int Current { get; private set; }

		internal Enumerator( TerrainPageIndex index, Vector3Int minimum, Vector3Int maximum )
		{
			_index = index;
			_minimum = minimum;
			_maximum = maximum;
			_next = 0;
			Current = default;
		}

		public Enumerator GetEnumerator() => this;

		public bool MoveNext()
		{
			while ( _next < _index._nodes.Length )
			{
				var node = _index._nodes[_next];
				if ( node.Maximum.x < _minimum.x || node.Minimum.x > _maximum.x ||
					node.Maximum.y < _minimum.y || node.Minimum.y > _maximum.y ||
					node.Maximum.z < _minimum.z || node.Minimum.z > _maximum.z )
				{
					_next = node.End;
					continue;
				}
				_next++;
				if ( node.KeyIndex < 0 ) continue;
				Current = _index._keys[node.KeyIndex];
				return true;
			}
			return false;
		}
	}
}
