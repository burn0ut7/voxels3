using System;
using System.Diagnostics;

/// <summary>Production physics geometry, independently extracted from the canonical field.</summary>
internal sealed class VoxelCollisionMesher
{
	private readonly int _cells;
	private readonly int _samples;
	private readonly float[] _density;
	private readonly ProceduralTerrainSdf.LatticeSampler _sampler;
	private readonly int[] _edges;
	private readonly int[] _corners;
	// Collision-only endpoint weld; shared lattice corners close adjacent cells/chunks.
	private const float VertexWeldTolerance = 0.01f;
	private readonly bool[] _sampled;
	private readonly bool[] _activeBlocks;
	private const int BlockCells = 2;
	private const int ParentBlockCells = 4;
	private readonly byte[] _parentBlocks;

	public VoxelCollisionMesher( int cells )
	{
		_cells = cells;
		_samples = cells + 1;
		_sampler = new ProceduralTerrainSdf.LatticeSampler( _samples );
		_density = new float[_samples * _samples * _samples];
		_edges = new int[_density.Length * 3];
		_corners = new int[_density.Length];
		_sampled = new bool[_density.Length];
		_parentBlocks = new byte[(cells / ParentBlockCells) * (cells / ParentBlockCells) * (cells / ParentBlockCells)];
		_activeBlocks = new bool[(cells / BlockCells) * (cells / BlockCells) * (cells / BlockCells)];
	}

	public VoxelCollisionGeometry Build( Vector3Int coordinate, float cellSize,
		TerrainFieldSnapshot field, Func<bool> cancelled, VoxelCollisionGeometry result,
		Vector3Int? supportMinimum = null, Vector3Int? supportMaximum = null )
	{
		var start = Stopwatch.GetTimestamp();
		result.SupportPatchRebuilds = 0;
		result.Vertices.Clear();
		result.Indices.Clear();
		result.SamplingMilliseconds = 0; result.ExtractionMilliseconds = 0;
		result.DegenerateTriangles = 0; result.WeldedIntersections = 0;
		result.SampleCount = 0; result.RejectedBlocks = 0;
		var localMinimum = supportMinimum ?? Vector3Int.Zero;
		var localMaximum = supportMaximum ?? new Vector3Int( _cells, _cells, _cells );
		var cells = localMaximum - localMinimum;
		var origin = coordinate * _cells + localMinimum;
		var worldMinimum = new Vector3( origin.x, origin.y, origin.z ) * cellSize;
		var worldMaximum = worldMinimum + new Vector3( cells.x, cells.y, cells.z ) * cellSize;
		var range = field.GetDensityRange( new SdfWorldAabb( worldMinimum, worldMaximum ), cellSize );
		if ( range.Classification != ChunkDensityClassification.PotentiallySurfaceContaining )
		{
			result.SamplingMilliseconds = (float)Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
			return result;
		}
		_sampler.Begin( origin, cellSize, field.Settings );
		Array.Clear( _sampled );
		Array.Clear( _activeBlocks );
		Array.Clear( _parentBlocks );
		var parents = new Vector3Int( (cells.x + ParentBlockCells - 1) / ParentBlockCells, (cells.y + ParentBlockCells - 1) / ParentBlockCells, (cells.z + ParentBlockCells - 1) / ParentBlockCells );
		var blocks = new Vector3Int( (cells.x + BlockCells - 1) / BlockCells, (cells.y + BlockCells - 1) / BlockCells, (cells.z + BlockCells - 1) / BlockCells );
		for ( var bz = 0; bz < blocks.z; bz++ )
		{
			for ( var by = 0; by < blocks.y; by++ )
			{
				for ( var bx = 0; bx < blocks.x; bx++ )
				{
					if ( cancelled() ) throw new OperationCanceledException();
					var px = bx * BlockCells / ParentBlockCells;
					var py = by * BlockCells / ParentBlockCells;
					var pz = bz * BlockCells / ParentBlockCells;
					var parentIndex = px + parents.x * (py + parents.y * pz);
					if ( _parentBlocks[parentIndex] == 0 )
					{
						var parentOrigin = origin + new Vector3Int( px, py, pz ) * ParentBlockCells;
						var parentMinimum = new Vector3( parentOrigin.x, parentOrigin.y, parentOrigin.z ) * cellSize;
						var parentMaximum = worldMinimum + new Vector3( Math.Min( (px + 1) * ParentBlockCells, cells.x ), Math.Min( (py + 1) * ParentBlockCells, cells.y ), Math.Min( (pz + 1) * ParentBlockCells, cells.z ) ) * cellSize;
						var parentRange = field.GetDensityRange( new SdfWorldAabb( parentMinimum, parentMaximum ), cellSize );
						_parentBlocks[parentIndex] = (byte)(parentRange.MaximumDensity < 0 || parentRange.MinimumDensity > 0 ? 2 : 1);
					}
					if ( _parentBlocks[parentIndex] == 2 )
					{
						result.RejectedBlocks++;
						continue;
					}
					var blockOrigin = origin + new Vector3Int( bx, by, bz ) * BlockCells;
					var minimum = new Vector3( blockOrigin.x, blockOrigin.y, blockOrigin.z ) * cellSize;
					var blockRange = field.GetDensityRange(
						new SdfWorldAabb( minimum, worldMinimum + new Vector3( Math.Min( (bx + 1) * BlockCells, cells.x ), Math.Min( (by + 1) * BlockCells, cells.y ), Math.Min( (bz + 1) * BlockCells, cells.z ) ) * cellSize ), cellSize );
					if ( blockRange.MaximumDensity < 0 || blockRange.MinimumDensity > 0 )
					{
						result.RejectedBlocks++;
						continue;
					}
					_activeBlocks[bx + blocks.x * (by + blocks.y * bz)] = true;
					for ( var z = bz * BlockCells; z <= Math.Min( (bz + 1) * BlockCells, cells.z ); z++ )
					{
						for ( var y = by * BlockCells; y <= Math.Min( (by + 1) * BlockCells, cells.y ); y++ )
						{
							for ( var x = bx * BlockCells; x <= Math.Min( (bx + 1) * BlockCells, cells.x ); x++ )
							{
								var index = x + _samples * (y + _samples * z);
								if ( _sampled[index] ) continue;
								var density = _sampler.Sample( x, y, z );
								if ( field.PageCount > 0 )
								{
									var sample = origin + new Vector3Int( x, y, z );
									density += field.SampleCorrection( new Vector3( sample.x * cellSize, sample.y * cellSize, sample.z * cellSize ) );
								}
								if ( !float.IsFinite( density ) ) throw new InvalidOperationException( "Non-finite collision density." );
								_density[index] = MathF.Abs( density ) < 1e-6f ? (density < 0 ? -1e-6f : 1e-6f) : density;
								_sampled[index] = true;
								result.SampleCount++;
							}
						}
					}
				}
			}
		}
		result.SamplingMilliseconds = (float)Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		start = Stopwatch.GetTimestamp();
		Array.Fill( _edges, -1 );
		Array.Fill( _corners, -1 );
		Span<int> vertices = stackalloc int[12];
		for ( var z = 0; z < cells.z; z++ )
		{
			if ( cancelled() ) throw new OperationCanceledException();
			for ( var y = 0; y < cells.y; y++ )
			{
				for ( var x = 0; x < cells.x; x++ )
				{
					if ( !_activeBlocks[x / BlockCells + blocks.x * (y / BlockCells + blocks.y * (z / BlockCells))] ) continue;
					var code = 0;
					for ( var corner = 0; corner < 8; corner++ )
					{
						var sample = x + (corner & 1) + _samples *
							(y + ((corner >> 1) & 1) + _samples * (z + (corner >> 2)));
						if ( _density[sample] < 0 ) code |= 1 << corner;
					}
					if ( code == 0 || code == 255 ) continue;
					var cellClass = VoxelCollisionTables.RegularCellClass[code];
					var counts = VoxelCollisionTables.RegularCellGeometryCounts[cellClass];
					for ( var vertex = 0; vertex < (counts >> 4); vertex++ )
					{
						var edge = VoxelCollisionTables.RegularVertexData[code * 12 + vertex] & 0xff;
						var a = edge >> 4;
						var b = edge & 15;
						var ax = x + (a & 1); var ay = y + ((a >> 1) & 1); var az = z + (a >> 2);
						var bx = x + (b & 1); var by = y + ((b >> 1) & 1); var bz = z + (b >> 2);
						var first = Math.Min( ax, bx ) + _samples * (Math.Min( ay, by ) + _samples * Math.Min( az, bz ));
						var axis = ax != bx ? 0 : ay != by ? 1 : 2;
						var slot = first * 3 + axis;
						if ( _edges[slot] < 0 )
						{
							var second = first + (axis == 0 ? 1 : axis == 1 ? _samples : _samples * _samples);
							var da = _density[first]; var db = _density[second];
							var t = Math.Clamp( MathF.Abs( da - db ) > 1e-6f ? da / (da - db) : 0.5f, 0f, 1f );
							var corner = -1;
							if ( t * cellSize <= VertexWeldTolerance ) { t = 0f; corner = first; }
							else if ( (1f - t) * cellSize <= VertexWeldTolerance ) { t = 1f; corner = second; }
							if ( corner >= 0 )
							{
								result.WeldedIntersections++;
								if ( _corners[corner] >= 0 )
								{
									_edges[slot] = _corners[corner];
									vertices[vertex] = _edges[slot];
									continue;
								}
							}
							var position = new Vector3( Math.Min( ax, bx ), Math.Min( ay, by ), Math.Min( az, bz ) );
							position += axis == 0 ? new Vector3( t, 0, 0 ) : axis == 1 ? new Vector3( 0, t, 0 ) : new Vector3( 0, 0, t );
							_edges[slot] = result.Vertices.Count;
							result.Vertices.Add( (position + new Vector3( localMinimum.x, localMinimum.y, localMinimum.z )) * cellSize );
							if ( corner >= 0 ) _corners[corner] = _edges[slot];
						}
						vertices[vertex] = _edges[slot];
					}
					for ( var triangle = 0; triangle < (counts & 15); triangle++ )
					{
						var table = cellClass * 15 + triangle * 3;
						var a = vertices[VoxelCollisionTables.RegularCellVertexIndices[table]];
						var b = vertices[VoxelCollisionTables.RegularCellVertexIndices[table + 1]];
						var c = vertices[VoxelCollisionTables.RegularCellVertexIndices[table + 2]];
						if ( a == b || a == c || b == c || Vector3.Cross( result.Vertices[b] - result.Vertices[a], result.Vertices[c] - result.Vertices[a] ).LengthSquared == 0f )
						{
							result.DegenerateTriangles++;
							continue;
						}
						result.Indices.Add( a ); result.Indices.Add( b ); result.Indices.Add( c );
					}
				}
			}
		}
		if ( result.Indices.Count > 0 )
		{
			var minimum = result.Vertices[result.Indices[0]];
			var maximum = minimum;
			foreach ( var index in result.Indices )
			{
				var position = result.Vertices[index];
				minimum = new Vector3( MathF.Min( minimum.x, position.x ), MathF.Min( minimum.y, position.y ), MathF.Min( minimum.z, position.z ) );
				maximum = new Vector3( MathF.Max( maximum.x, position.x ), MathF.Max( maximum.y, position.y ), MathF.Max( maximum.z, position.z ) );
			}
			if ( supportMinimum is null && maximum.x - minimum.x < cellSize &&
				maximum.y - minimum.y < cellSize && maximum.z - minimum.z < cellSize )
			{
				// A tiny chunk-boundary fragment is submitted with its connected field
				// neighborhood, rather than discarded or enlarged. At most 4^3 cells.
				var patchMinimum = new Vector3Int( (int)MathF.Floor( minimum.x / cellSize ) - 1,
					(int)MathF.Floor( minimum.y / cellSize ) - 1, (int)MathF.Floor( minimum.z / cellSize ) - 1 );
				var patchMaximum = new Vector3Int( (int)MathF.Ceiling( maximum.x / cellSize ) + 1,
					(int)MathF.Ceiling( maximum.y / cellSize ) + 1, (int)MathF.Ceiling( maximum.z / cellSize ) + 1 );
				var sampling = result.SamplingMilliseconds;
				var extraction = (float)Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
				var samples = result.SampleCount; var rejected = result.RejectedBlocks;
				Build( coordinate, cellSize, field, cancelled, result, patchMinimum, patchMaximum );
				result.SamplingMilliseconds += sampling;
				result.ExtractionMilliseconds += extraction;
				result.SampleCount += samples; result.RejectedBlocks += rejected;
				if ( result.Indices.Count == 0 ) throw new InvalidOperationException( "Connected collision patch lost the original surface." );
				result.SupportPatchRebuilds = 1;
				return result;
			}
		}
		result.ExtractionMilliseconds = (float)Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		return result;
	}
}

internal sealed class VoxelCollisionGeometry
{
	public List<Vector3> Vertices { get; } = new();
	public List<int> Indices { get; } = new();
	public int SupportPatchRebuilds;
	public float SamplingMilliseconds;
	public float ExtractionMilliseconds;
	public int DegenerateTriangles;
	public int WeldedIntersections;
	public int SampleCount;
	public int RejectedBlocks;
	public long Bytes => Vertices.Capacity * 12L + Indices.Capacity * 4L;
}
