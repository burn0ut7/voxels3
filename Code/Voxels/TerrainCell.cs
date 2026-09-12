using System;

/// <summary>Logical solid occupancy does not remove the remaining visible SDF surface.</summary>
internal readonly record struct TerrainCellState( Vector3Int Coordinate, float SolidFraction )
{
	public const float EmptyFraction = 0.1f;
	public bool IsEmpty => SolidFraction <= EmptyFraction;
}

internal sealed partial class TerrainFieldSnapshot
{
	// Zero means inherit procedural material. This tool slice authors Dirt only.
	public ushort SamplePlacedMaterial( Vector3Int cell )
	{
		var key = new Vector3Int( cell.x >> TerrainField.PageShift, cell.y >> TerrainField.PageShift, cell.z >> TerrainField.PageShift );
		RequirePageRange( key, key );
		return Pages.TryGetValue( key, out var page ) ? page.Material( (cell.x & TerrainField.PageMask) +
			TerrainField.SamplesPerPageAxis * ((cell.y & TerrainField.PageMask) + TerrainField.SamplesPerPageAxis * (cell.z & TerrainField.PageMask)) ) : (ushort)0;
	}

	public float SamplePlacedDirt( Vector3 position )
	{
		var lattice = position / TerrainField.SampleSpacing;
		var origin = new Vector3Int( (int)MathF.Floor( lattice.x ), (int)MathF.Floor( lattice.y ), (int)MathF.Floor( lattice.z ) );
		var fraction = lattice - new Vector3( origin.x, origin.y, origin.z );
		var result = 0f;
		for ( var z = 0; z < 2; z++ )
		for ( var y = 0; y < 2; y++ )
		for ( var x = 0; x < 2; x++ )
		{
			var weight = (x == 0 ? 1f - fraction.x : fraction.x) * (y == 0 ? 1f - fraction.y : fraction.y) * (z == 0 ? 1f - fraction.z : fraction.z);
			if ( weight > 0f && SamplePlacedMaterial( origin + new Vector3Int( x, y, z ) ) == VoxelMaterials.Dirt ) result += weight;
		}
		return result;
	}

	public bool TrySampleCell( Vector3 position, out TerrainCellState cell )
	{
		var coordinate = new Vector3Int( (int)MathF.Floor( position.x / TerrainField.SampleSpacing ),
			(int)MathF.Floor( position.y / TerrainField.SampleSpacing ), (int)MathF.Floor( position.z / TerrainField.SampleSpacing ) );
		var origin = new Vector3( coordinate.x, coordinate.y, coordinate.z ) * TerrainField.SampleSpacing;
		cell = default;
		if ( !TryCaptureRegion( new SdfWorldAabb( origin, origin + Vector3.One * TerrainField.SampleSpacing ), out var reader ) ) return false;
		Span<float> density = stackalloc float[8];
		for ( var index = 0; index < 8; index++ )
			density[index] = reader.SampleWorld( origin + new Vector3( index & 1, (index >> 1) & 1, index >> 2 ) * TerrainField.SampleSpacing );
		// Six equal-volume tetrahedra around the shared 0--7 diagonal. Integrate
		// their linear density exactly; never infer volume from density magnitude.
		var fraction = (TetrahedronSolidFraction( density[0], density[1], density[3], density[7] ) +
			TetrahedronSolidFraction( density[0], density[3], density[2], density[7] ) +
			TetrahedronSolidFraction( density[0], density[2], density[6], density[7] ) +
			TetrahedronSolidFraction( density[0], density[6], density[4], density[7] ) +
			TetrahedronSolidFraction( density[0], density[4], density[5], density[7] ) +
			TetrahedronSolidFraction( density[0], density[5], density[1], density[7] )) / 6f;
		cell = new TerrainCellState( coordinate, Math.Clamp( fraction, 0f, 1f ) );
		return true;
	}

	private static float TetrahedronSolidFraction( float a, float b, float c, float d )
	{
		Span<float> values = stackalloc float[4] { a, b, c, d };
		Span<float> inside = stackalloc float[4];
		Span<float> outside = stackalloc float[4];
		var count = 0;
		var empty = 0;
		foreach ( var value in values )
		{
			if ( value < 0f ) inside[count++] = value;
			else outside[empty++] = value;
		}
		if ( count == 0 ) return 0f;
		if ( count == 4 ) return 1f;
		if ( count == 1 )
			return inside[0] / (inside[0] - outside[0]) * inside[0] / (inside[0] - outside[1]) * inside[0] / (inside[0] - outside[2]);
		if ( count == 3 )
			return 1f - outside[0] / (outside[0] - inside[0]) * outside[0] / (outside[0] - inside[1]) * outside[0] / (outside[0] - inside[2]);
		var ac = inside[0] / (inside[0] - outside[0]);
		var ad = inside[0] / (inside[0] - outside[1]);
		var bc = inside[1] / (inside[1] - outside[0]);
		var bd = inside[1] / (inside[1] - outside[1]);
		return ac * ad + ac * bd * (1f - ad) + bc * bd * (1f - ac);
	}
}
