using System;

/// <summary>One seeded deposit per eligible world-space region. No mutable random state.</summary>
internal readonly record struct MaterialSpawnRegion( Vector3 Size, float Probability, uint Salt, bool Column )
{
	// Centers stay in [0.4,0.6], radii in [0.22,0.34]; deposits fit their region.
	// Floor owns negative-coordinate regions. Mirror in voxel_material_regions.hlsl.
	public bool Contains( Vector3 position, int worldSeed )
	{
		var coordinate = new Vector3( position.x / Size.x, position.y / Size.y, Column ? 0f : position.z / Size.z );
		var x = (int)MathF.Floor( coordinate.x );
		var y = (int)MathF.Floor( coordinate.y );
		var z = (int)MathF.Floor( coordinate.z );
		var hash = TerrainNoise.Hash( x, y, z, unchecked((uint)worldSeed) ^ Salt );
		if ( (hash >> 8) * (1f / 16777216f) >= Probability ) return false;
		var shape = TerrainNoise.Hash( x, y, z, hash ^ 0xA511E9B3u );
		var radius = 0.22f + ((shape >> 24) & 255u) * (0.12f / 255f);
		var dx = (coordinate.x - x - (0.4f + (shape & 255u) * (0.2f / 255f))) / radius;
		var dy = (coordinate.y - y - (0.4f + ((shape >> 8) & 255u) * (0.2f / 255f))) / radius;
		var dz = Column ? 0f : (coordinate.z - z - (0.4f + ((shape >> 16) & 255u) * (0.2f / 255f))) / radius;
		return dx * dx + dy * dy + dz * dz <= 1f;
	}
}
