using System;

// Shared deterministic primitives. Recipe salts and scales belong to their caller.
internal static class TerrainNoise
{
	// Binary world quantization owns cell/rank decisions; mirror in voxel_terrain_noise.hlsl.
	internal const float SimplexCoordinateScale = 256f;
	private const float SimplexG3 = 1f / 6f;

	private static long FloorDivide( long numerator, long denominator )
	{
		var quotient = numerator / denominator;
		return numerator < 0 && quotient * denominator != numerator ? quotient - 1 : quotient;
	}

	private static void SimplexIndices( long qx, long qy, long qz, long denominator,
		out long i, out long j, out long k )
	{
		i = FloorDivide( 4 * qx + qy + qz, 3 * denominator );
		j = FloorDivide( qx + 4 * qy + qz, 3 * denominator );
		k = FloorDivide( qx + qy + 4 * qz, 3 * denominator );
	}

	private readonly record struct SimplexCell(
		long X, long Y, long Z, int I1, int J1, int K1, int I2, int J2, int K2 );

	private static SimplexCell LocateSimplex( Vector3 worldPosition, float wavelength, out Vector3 offset )
	{
		var qx = (long)MathF.Round( worldPosition.x * SimplexCoordinateScale );
		var qy = (long)MathF.Round( worldPosition.y * SimplexCoordinateScale );
		var qz = (long)MathF.Round( worldPosition.z * SimplexCoordinateScale );
		var denominator = (long)(wavelength * SimplexCoordinateScale);
		SimplexIndices( qx, qy, qz, denominator, out var i, out var j, out var k );
		var rx = qx - i * denominator;
		var ry = qy - j * denominator;
		var rz = qz - k * denominator;
		var unskewNumerator = (i + j + k) * denominator;
		var offsetDenominator = (float)(6 * denominator);
		var x0 = (float)(6 * rx + unskewNumerator) / offsetDenominator;
		var y0 = (float)(6 * ry + unskewNumerator) / offsetDenominator;
		var z0 = (float)(6 * rz + unskewNumerator) / offsetDenominator;

		int i1;
		int j1;
		int k1;
		int i2;
		int j2;
		int k2;
		if ( rx >= ry )
		{
			if ( ry >= rz )
			{
				i1 = 1; j1 = 0; k1 = 0;
				i2 = 1; j2 = 1; k2 = 0;
			}
			else if ( rx >= rz )
			{
				i1 = 1; j1 = 0; k1 = 0;
				i2 = 1; j2 = 0; k2 = 1;
			}
			else
			{
				i1 = 0; j1 = 0; k1 = 1;
				i2 = 1; j2 = 0; k2 = 1;
			}
		}
		else
		{
			if ( ry < rz )
			{
				i1 = 0; j1 = 0; k1 = 1;
				i2 = 0; j2 = 1; k2 = 1;
			}
			else if ( rx < rz )
			{
				i1 = 0; j1 = 1; k1 = 0;
				i2 = 0; j2 = 1; k2 = 1;
			}
			else
			{
				i1 = 0; j1 = 1; k1 = 0;
				i2 = 1; j2 = 1; k2 = 0;
			}
		}

		offset = new Vector3( x0, y0, z0 );
		return new SimplexCell( i, j, k, i1, j1, k1, i2, j2, k2 );
	}

	// Bound the linear cell/rank inequalities over the whole quantized box.
	// Only its minimum needs cell division; positive skew coefficients make
	// the maximum the opposite extremum. Pairwise rank intervals retain ties.
	internal static bool HasSingleSimplexCell( SdfWorldAabb bounds, float wavelength )
	{
		var qx = (long)MathF.Round( bounds.Minimum.x * SimplexCoordinateScale );
		var qy = (long)MathF.Round( bounds.Minimum.y * SimplexCoordinateScale );
		var qz = (long)MathF.Round( bounds.Minimum.z * SimplexCoordinateScale );
		var upperX = (long)MathF.Round( bounds.Maximum.x * SimplexCoordinateScale );
		var upperY = (long)MathF.Round( bounds.Maximum.y * SimplexCoordinateScale );
		var upperZ = (long)MathF.Round( bounds.Maximum.z * SimplexCoordinateScale );
		var denominator = (long)(wavelength * SimplexCoordinateScale);
		SimplexIndices( qx, qy, qz, denominator, out var i, out var j, out var k );
		if ( 4 * upperX + upperY + upperZ >= (i + 1) * (3 * denominator) ||
			upperX + 4 * upperY + upperZ >= (j + 1) * (3 * denominator) ||
			upperX + upperY + 4 * upperZ >= (k + 1) * (3 * denominator) ) return false;
		var minimumX = qx - i * denominator;
		var minimumY = qy - j * denominator;
		var minimumZ = qz - k * denominator;
		var maximumX = upperX - i * denominator;
		var maximumY = upperY - j * denominator;
		var maximumZ = upperZ - k * denominator;
		return (minimumX >= minimumY ? minimumX >= maximumY : maximumX < minimumY) &&
			(minimumX >= minimumZ ? minimumX >= maximumZ : maximumX < minimumZ) &&
			(minimumY >= minimumZ ? minimumY >= maximumZ : maximumY < minimumZ);
	}

	internal static float SimplexNoise3D( Vector3 worldPosition, float wavelength, uint seed )
	{
		var cell = LocateSimplex( worldPosition, wavelength, out var offset );
		var (i, j, k, i1, j1, k1, i2, j2, k2) = cell;
		var (x0, y0, z0) = (offset.x, offset.y, offset.z);
		var x1 = x0 - i1 + SimplexG3;
		var y1 = y0 - j1 + SimplexG3;
		var z1 = z0 - k1 + SimplexG3;
		var x2 = x0 - i2 + 2f * SimplexG3;
		var y2 = y0 - j2 + 2f * SimplexG3;
		var z2 = z0 - k2 + 2f * SimplexG3;
		var x3 = x0 - 1f + 3f * SimplexG3;
		var y3 = y0 - 1f + 3f * SimplexG3;
		var z3 = z0 - 1f + 3f * SimplexG3;
		var value =
			SimplexContribution3D( i, j, k, x0, y0, z0, seed ) +
			SimplexContribution3D( i + i1, j + j1, k + k1, x1, y1, z1, seed ) +
			SimplexContribution3D( i + i2, j + j2, k + k2, x2, y2, z2, seed ) +
			SimplexContribution3D( i + 1, j + 1, k + 1, x3, y3, z3, seed );
		return Math.Clamp( value * 32f, -1f, 1f );
	}

	private static float SimplexContribution3D(
		long x,
		long y,
		long z,
		float offsetX,
		float offsetY,
		float offsetZ,
		uint seed )
	{
		var attenuation = 0.6f - offsetX * offsetX - offsetY * offsetY - offsetZ * offsetZ;
		if ( attenuation <= 0f )
		{
			return 0f;
		}

		var gradient = Gradient3D( Hash( unchecked((int)x), unchecked((int)y), unchecked((int)z), seed ) );
		attenuation *= attenuation;
		return attenuation * attenuation *
			(gradient.x * offsetX + gradient.y * offsetY + gradient.z * offsetZ);
	}

	private static Vector3 Gradient3D( uint hash )
	{
		const float diagonal = 0.70710677f;
		return (hash % 12u) switch
		{
			0u => new Vector3( diagonal, diagonal, 0f ),
			1u => new Vector3( -diagonal, diagonal, 0f ),
			2u => new Vector3( diagonal, -diagonal, 0f ),
			3u => new Vector3( -diagonal, -diagonal, 0f ),
			4u => new Vector3( diagonal, 0f, diagonal ),
			5u => new Vector3( -diagonal, 0f, diagonal ),
			6u => new Vector3( diagonal, 0f, -diagonal ),
			7u => new Vector3( -diagonal, 0f, -diagonal ),
			8u => new Vector3( 0f, diagonal, diagonal ),
			9u => new Vector3( 0f, -diagonal, diagonal ),
			10u => new Vector3( 0f, diagonal, -diagonal ),
			_ => new Vector3( 0f, -diagonal, -diagonal )
		};
	}

	internal static uint Hash( int x, int y, uint seed )
	{
		unchecked
		{
			var hash = seed;
			hash ^= (uint)x * 0x9E3779B1u;
			hash = RotateLeft( hash, 13 ) * 0x85EBCA77u;
			hash ^= (uint)y * 0xC2B2AE3Du;
			hash = RotateLeft( hash, 15 ) * 0x27D4EB2Fu;
			hash ^= hash >> 16;
			hash *= 0x7FEB352Du;
			hash ^= hash >> 15;
			hash *= 0x846CA68Bu;
			hash ^= hash >> 16;
			return hash;
		}
	}

	internal static uint Hash( int x, int y, int z, uint seed )
	{
		unchecked
		{
			var hash = seed;
			hash ^= (uint)x * 0x9E3779B1u;
			hash = RotateLeft( hash, 13 ) * 0x85EBCA77u;
			hash ^= (uint)y * 0xC2B2AE3Du;
			hash = RotateLeft( hash, 15 ) * 0x27D4EB2Fu;
			hash ^= (uint)z * 0x165667B1u;
			hash = RotateLeft( hash, 17 ) * 0xD3A2646Cu;
			hash ^= hash >> 16;
			hash *= 0x7FEB352Du;
			hash ^= hash >> 15;
			hash *= 0x846CA68Bu;
			hash ^= hash >> 16;
			return hash;
		}
	}

	private static uint RotateLeft( uint value, int count )
	{
		return value << count | value >> (32 - count);
	}
}
