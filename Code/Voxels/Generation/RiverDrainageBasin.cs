using System;
using System.Collections.Generic;

/// <summary>Immutable, ocean-rooted priority flood. Parent discovery order is acyclic.</summary>
internal sealed class RiverDrainageBasin
{
	public const int NodesPerAxis = 128;
	public const int NodeCount = NodesPerAxis * NodesPerAxis;
	private readonly RiverNetwork.Node[] _nodes = new RiverNetwork.Node[NodeCount];
	private readonly int[] _parents = new int[NodeCount];
	private readonly int[] _catchments = new int[NodeCount];
	private readonly int[] _distances = new int[NodeCount];
	private readonly int[] _upstreamLengths = new int[NodeCount];
	private readonly bool[] _riverEnabled = new bool[NodeCount];
	private readonly RiverNetwork.NodeId _minimum;

	public static RiverNetwork.NodeId Coordinate( RiverNetwork.NodeId id ) => new(
		(int)MathF.Floor( (id.X + NodesPerAxis / 2) / (float)NodesPerAxis ),
		(int)MathF.Floor( (id.Y + NodesPerAxis / 2) / (float)NodesPerAxis ) );

	private int Index( RiverNetwork.NodeId id ) => id.X - _minimum.X + (id.Y - _minimum.Y) * NodesPerAxis;
	public RiverNetwork.Node GetNode( RiverNetwork.NodeId id ) => _nodes[Index( id )];
	public RiverNetwork.NodeId Downstream( RiverNetwork.NodeId id ) => _nodes[_parents[Index( id )]].Id;
	public int Catchment( RiverNetwork.NodeId id ) => _catchments[Index( id )];
	public int Distance( RiverNetwork.NodeId id ) => _distances[Index( id )];
	public int UpstreamLength( RiverNetwork.NodeId id ) => _upstreamLengths[Index( id )];
	public bool RiverEnabled( RiverNetwork.NodeId id ) => _riverEnabled[Index( id )];

	public RiverDrainageBasin( RiverNetwork.NodeId coordinate, ProceduralTerrainSettings settings )
	{
		_minimum = new RiverNetwork.NodeId( coordinate.X * NodesPerAxis - NodesPerAxis / 2,
			coordinate.Y * NodesPerAxis - NodesPerAxis / 2 );
		var visited = new bool[NodeCount];
		var order = new int[NodeCount];
		var count = 0;
		var queue = new PriorityQueue<int, (float Height, int Distance, uint Tie, int Index)>();
		for ( var index = 0; index < NodeCount; index++ )
		{
			var id = new RiverNetwork.NodeId( _minimum.X + index % NodesPerAxis, _minimum.Y + index / NodesPerAxis );
			var hash = TerrainNoise.Hash( id.X, id.Y, unchecked((uint)settings.WorldSeed) ^ 0xDB4F0B91u );
			var position = new Vector2(
				(id.X + 0.5f + ((hash & 255u) / 255f - 0.5f) * 0.25f) * RiverNetwork.NodeSpacing,
				(id.Y + 0.5f + (((hash >> 8) & 255u) / 255f - 0.5f) * 0.25f) * RiverNetwork.NodeSpacing );
			var height = RegionalLandforms.SampleNatural( new Vector3( position.x, position.y, 0f ), settings ).Height;
			height = MathF.Floor( height * 16f ) / 16f;
			_nodes[index] = new RiverNetwork.Node( id, position, height );
			_parents[index] = index;
			_distances[index] = int.MaxValue;
			_catchments[index] = 1;
			if ( height > settings.SeaLevel ) continue;
			visited[index] = true;
			_distances[index] = 0;
			queue.Enqueue( index, (settings.SeaLevel, 0, hash, index) );
		}
		while ( queue.TryDequeue( out var index, out var priority ) )
		{
			order[count++] = index;
			var x = index % NodesPerAxis;
			var y = index / NodesPerAxis;
			for ( var dy = -1; dy <= 1; dy++ )
			{
				for ( var dx = -1; dx <= 1; dx++ )
				{
					var nx = x + dx;
					var ny = y + dy;
					if ( nx < 0 || nx >= NodesPerAxis || ny < 0 || ny >= NodesPerAxis ) continue;
					var next = nx + ny * NodesPerAxis;
					if ( visited[next] ) continue;
					visited[next] = true;
					_parents[next] = index;
					_distances[next] = _distances[index] + 1;
					var node = _nodes[next];
					var height = MathF.Max( node.Height, priority.Height );
					_nodes[next] = node with { Height = height };
					var hash = TerrainNoise.Hash( node.Id.X, node.Id.Y, unchecked((uint)settings.WorldSeed) ^ 0xB5297A4Du );
					queue.Enqueue( next, (height, _distances[next], hash, next) );
				}
			}
		}
		// Children are always discovered after parents. Reverse removal order
		// accumulates the exact tree, including depressions and flat spill routes.
		for ( var i = count - 1; i >= 0; i-- )
		{
			var index = order[i];
			var parent = _parents[index];
			if ( parent != index ) _catchments[parent] += _catchments[index];
		}
		for ( var i = count - 1; i >= 0; i-- )
		{
			var index = order[i];
			var parent = _parents[index];
			if ( parent != index && _catchments[index] >= RiverNetwork.RiverCatchmentNodes )
				_upstreamLengths[parent] = Math.Max( _upstreamLengths[parent], _upstreamLengths[index] + 1 );
		}

		// Thin entire outlet trees, preserving every retained tributary and delta.
		// A per-basin quota gives 40% fewer systems, rounded to a whole system.
		var outlets = new List<(uint Hash, int Index)>();
		for ( var i = 0; i < count; i++ )
		{
			var index = order[i];
			if ( _parents[index] != index || _upstreamLengths[index] < RiverNetwork.MinimumCourseEdges ) continue;
			var id = _nodes[index].Id;
			outlets.Add( (TerrainNoise.Hash( id.X, id.Y, unchecked((uint)settings.WorldSeed) ^ 0x6C8E9CF5u ), index) );
		}
		outlets.Sort();
		var retainedCount = (outlets.Count * 3 + 2) / 5;
		for ( var i = 0; i < retainedCount; i++ ) _riverEnabled[outlets[i].Index] = true;
		for ( var i = 0; i < count; i++ )
		{
			var index = order[i];
			_riverEnabled[index] = _riverEnabled[_parents[index]];
		}
	}
}
