using System;
using System.Collections.Generic;

/// <summary>Extracts the exposed free surface from generated chunk cell data.</summary>
internal static class SurfaceWaterGeometry
{
	[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
	internal struct WaterVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		public WaterVertex( Vector3 position ) { Position = position; }
	}
	internal sealed record Chunk( GpuSdfDescriptor Descriptor, GeneratedWaterCells Cells, WaterVertex[] Vertices, double Milliseconds );

	public static WaterVertex[] Build( GeneratedWaterCells cells )
	{
		var vertices = new List<WaterVertex>();
		var size = GeneratedWaterCells.CellsPerAxis;
		var stride = GeneratedWaterCells.SamplesPerAxis;
		var origin = cells.Bounds.Minimum;
		origin.z = cells.SeaLevel;
		// 1 = completely wet and available; 2 = already covered by a rectangle.
		var coverage = new byte[size * size];
		for ( var y = 0; y < size; y++ )
		for ( var x = 0; x < size; x++ )
		{
			var index = x + stride * y;
			if ( !cells.RefinedCells.ContainsKey( x + size * y ) &&
				cells.SurfaceClearance[index] > 0f && cells.SurfaceClearance[index + 1] > 0f &&
				cells.SurfaceClearance[index + stride + 1] > 0f && cells.SurfaceClearance[index + stride] > 0f )
				coverage[x + size * y] = 1;
		}
		for ( var y = 0; y < size; y++ )
		for ( var x = 0; x < size; x++ )
		{
			if ( coverage[x + size * y] == 2 ) continue;
			if ( coverage[x + size * y] == 1 )
			{
				var endX = x + 1;
				while ( endX < size && coverage[endX + size * y] == 1 ) endX++;
				var endY = y + 1;
				while ( endY < size )
				{
					var complete = true;
					for ( var column = x; column < endX; column++ )
						if ( coverage[column + size * endY] != 1 ) { complete = false; break; }
					if ( !complete ) break;
					endY++;
				}
				for ( var row = y; row < endY; row++ )
				for ( var column = x; column < endX; column++ ) coverage[column + size * row] = 2;
				var first = origin + new Vector3( x * cells.CellSize, y * cells.CellSize, 0f );
				var right = origin + new Vector3( endX * cells.CellSize, y * cells.CellSize, 0f );
				var diagonal = origin + new Vector3( endX * cells.CellSize, endY * cells.CellSize, 0f );
				var up = origin + new Vector3( x * cells.CellSize, endY * cells.CellSize, 0f );
				vertices.Add( new WaterVertex( first ) ); vertices.Add( new WaterVertex( right ) ); vertices.Add( new WaterVertex( diagonal ) );
				vertices.Add( new WaterVertex( first ) ); vertices.Add( new WaterVertex( diagonal ) ); vertices.Add( new WaterVertex( up ) );
				continue;
			}
			if ( cells.RefinedCells.TryGetValue( x + size * y, out var refined ) )
			{
				foreach ( var cell in refined ) ClipCell( cell.Origin, cell.Size, cell.Clearance, vertices );
				continue;
			}
			var sample = x + stride * y;
			ClipCell( origin + new Vector3( x * cells.CellSize, y * cells.CellSize, 0f ), cells.CellSize,
				new Vector4( cells.SurfaceClearance[sample], cells.SurfaceClearance[sample + 1],
					cells.SurfaceClearance[sample + stride + 1], cells.SurfaceClearance[sample + stride] ), vertices );
		}
		return vertices.ToArray();
	}

	private static void ClipCell( Vector3 origin, float size, Vector4 wet, List<WaterVertex> vertices )
	{
		if ( wet.x <= 0f && wet.y <= 0f && wet.z <= 0f && wet.w <= 0f ) return;
		var right = origin + new Vector3( size, 0f, 0f );
		var diagonal = origin + new Vector3( size, size, 0f );
		var up = origin + new Vector3( 0f, size, 0f );
		// Opposite wet corners retain the existing diagonal's connectivity.
		if ( (wet.x > 0f) == (wet.z > 0f) && (wet.y > 0f) == (wet.w > 0f) && (wet.x > 0f) != (wet.y > 0f) )
		{
			ClipTriangle( origin, right, diagonal, wet.x, wet.y, wet.z, vertices );
			ClipTriangle( origin, diagonal, up, wet.x, wet.z, wet.w, vertices );
			return;
		}
		Span<Vector3> points = stackalloc Vector3[4] { origin, right, diagonal, up };
		Span<float> density = stackalloc float[4] { wet.x, wet.y, wet.z, wet.w };
		Span<Vector3> clipped = stackalloc Vector3[6];
		var count = 0;
		for ( var index = 0; index < 4; index++ )
		{
			var next = (index + 1) % 4;
			if ( density[index] > 0f ) clipped[count++] = points[index];
			if ( (density[index] > 0f) != (density[next] > 0f) )
				clipped[count++] = SurfaceCrossing( points[index], points[next], density[index], density[next] );
		}
		for ( var index = 1; index + 1 < count; index++ )
		{
			vertices.Add( new WaterVertex( clipped[0] ) );
			vertices.Add( new WaterVertex( clipped[index] ) );
			vertices.Add( new WaterVertex( clipped[index + 1] ) );
		}
	}

	private static Vector3 SurfaceCrossing( Vector3 a, Vector3 b, float da, float db )
	{
		// Shared edges use the same arithmetic direction in either owner chunk.
		if ( a.x > b.x || a.x == b.x && a.y > b.y )
		{
			(a, b) = (b, a);
			(da, db) = (db, da);
		}
		return a + (b - a) * (da / (da - db));
	}

	private static void ClipTriangle( Vector3 a, Vector3 b, Vector3 c, float da, float db, float dc, List<WaterVertex> vertices )
	{
		Span<Vector3> points = stackalloc Vector3[3] { a, b, c };
		Span<float> density = stackalloc float[3] { da, db, dc };
		Span<Vector3> clipped = stackalloc Vector3[4];
		var count = 0;
		for ( var index = 0; index < 3; index++ )
		{
			var next = (index + 1) % 3;
			if ( density[index] > 0f ) clipped[count++] = points[index];
			if ( (density[index] > 0f) != (density[next] > 0f) )
				clipped[count++] = SurfaceCrossing( points[index], points[next], density[index], density[next] );
		}
		for ( var index = 1; index + 1 < count; index++ )
		{
			vertices.Add( new WaterVertex( clipped[0] ) );
			vertices.Add( new WaterVertex( clipped[index] ) );
			vertices.Add( new WaterVertex( clipped[index + 1] ) );
		}
	}
}
