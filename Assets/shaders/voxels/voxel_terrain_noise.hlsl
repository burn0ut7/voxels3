// Deterministic shared hash and 3D simplex primitives.
// Must match TerrainNoise.SimplexCoordinateScale.
static const float VoxelSimplexCoordinateScale = 256.0;
static const float VoxelSimplexG3 = 1.0 / 6.0;

uint RotateVoxelHashLeft( uint value, uint count )
{
	return (value << count) | (value >> (32u - count));
}

uint VoxelHash2D( int x, int y, uint seed )
{
	uint hash = seed;
	hash ^= (uint)x * 0x9E3779B1u;
	hash = RotateVoxelHashLeft( hash, 13u ) * 0x85EBCA77u;
	hash ^= (uint)y * 0xC2B2AE3Du;
	hash = RotateVoxelHashLeft( hash, 15u ) * 0x27D4EB2Fu;
	hash ^= hash >> 16u;
	hash *= 0x7FEB352Du;
	hash ^= hash >> 15u;
	hash *= 0x846CA68Bu;
	hash ^= hash >> 16u;
	return hash;
}

uint VoxelHash3D( int x, int y, int z, uint seed )
{
	uint hash = seed;
	hash ^= (uint)x * 0x9E3779B1u;
	hash = RotateVoxelHashLeft( hash, 13u ) * 0x85EBCA77u;
	hash ^= (uint)y * 0xC2B2AE3Du;
	hash = RotateVoxelHashLeft( hash, 15u ) * 0x27D4EB2Fu;
	hash ^= (uint)z * 0x165667B1u;
	hash = RotateVoxelHashLeft( hash, 17u ) * 0xD3A2646Cu;
	hash ^= hash >> 16u;
	hash *= 0x7FEB352Du;
	hash ^= hash >> 15u;
	hash *= 0x846CA68Bu;
	hash ^= hash >> 16u;
	return hash;
}



float3 VoxelGradient3D( uint hash )
{
	const float diagonal = 0.70710677;
	switch ( hash % 12u )
	{
		case 0u: return float3( diagonal, diagonal, 0.0 );
		case 1u: return float3( -diagonal, diagonal, 0.0 );
		case 2u: return float3( diagonal, -diagonal, 0.0 );
		case 3u: return float3( -diagonal, -diagonal, 0.0 );
		case 4u: return float3( diagonal, 0.0, diagonal );
		case 5u: return float3( -diagonal, 0.0, diagonal );
		case 6u: return float3( diagonal, 0.0, -diagonal );
		case 7u: return float3( -diagonal, 0.0, -diagonal );
		case 8u: return float3( 0.0, diagonal, diagonal );
		case 9u: return float3( 0.0, -diagonal, diagonal );
		case 10u: return float3( 0.0, diagonal, -diagonal );
		default: return float3( 0.0, -diagonal, -diagonal );
	}
}


float VoxelSimplexContribution3D(
	int64_t x,
	int64_t y,
	int64_t z,
	float3 offset,
	uint seed )
{
	float attenuation = 0.6 - dot( offset, offset );
	if ( attenuation <= 0.0 )
	{
		return 0.0;
	}

	float3 gradient = VoxelGradient3D( VoxelHash3D( (int)x, (int)y, (int)z, seed ) );
	attenuation *= attenuation;
	return attenuation * attenuation * dot( gradient, offset );
}


int64_t QuantizeVoxelSimplexCoordinate( float coordinate )
{
	float scaled = coordinate * VoxelSimplexCoordinateScale;
	int64_t lower = (int64_t)floor( scaled );
	float fraction = scaled - (float)lower;
	return lower + (fraction > 0.5 || (fraction == 0.5 && (lower & 1) != 0) ? 1 : 0);
}

int64_t FloorDivideVoxelSimplex( int64_t numerator, int64_t denominator )
{
	int64_t quotient = numerator / denominator;
	return numerator < 0 && quotient * denominator != numerator ? quotient - 1 : quotient;
}

float SampleVoxelSimplex3D( float3 worldPosition, float wavelength, uint seed )
{
	int64_t qx = QuantizeVoxelSimplexCoordinate( worldPosition.x );
	int64_t qy = QuantizeVoxelSimplexCoordinate( worldPosition.y );
	int64_t qz = QuantizeVoxelSimplexCoordinate( worldPosition.z );
	int64_t denominator = (int64_t)(wavelength * VoxelSimplexCoordinateScale);
	int64_t i = FloorDivideVoxelSimplex( 4 * qx + qy + qz, 3 * denominator );
	int64_t j = FloorDivideVoxelSimplex( qx + 4 * qy + qz, 3 * denominator );
	int64_t k = FloorDivideVoxelSimplex( qx + qy + 4 * qz, 3 * denominator );
	int64_t rx = qx - i * denominator;
	int64_t ry = qy - j * denominator;
	int64_t rz = qz - k * denominator;
	int64_t unskewNumerator = (i + j + k) * denominator;
	float offsetDenominator = (float)(6 * denominator);
	float3 offset0 = float3( (float)(6 * rx + unskewNumerator),
		(float)(6 * ry + unskewNumerator), (float)(6 * rz + unskewNumerator) ) / offsetDenominator;
	int3 first;
	int3 second;
	if ( rx >= ry )
	{
		if ( ry >= rz )
		{
			first = int3( 1, 0, 0 ); second = int3( 1, 1, 0 );
		}
		else if ( rx >= rz )
		{
			first = int3( 1, 0, 0 ); second = int3( 1, 0, 1 );
		}
		else
		{
			first = int3( 0, 0, 1 ); second = int3( 1, 0, 1 );
		}
	}
	else
	{
		if ( ry < rz )
		{
			first = int3( 0, 0, 1 ); second = int3( 0, 1, 1 );
		}
		else if ( rx < rz )
		{
			first = int3( 0, 1, 0 ); second = int3( 0, 1, 1 );
		}
		else
		{
			first = int3( 0, 1, 0 ); second = int3( 1, 1, 0 );
		}
	}

	float3 offset1 = offset0 - (float3)first + VoxelSimplexG3;
	float3 offset2 = offset0 - (float3)second + 2.0 * VoxelSimplexG3;
	float3 offset3 = offset0 - 1.0 + 3.0 * VoxelSimplexG3;
	float value =
		VoxelSimplexContribution3D( i, j, k, offset0, seed ) +
		VoxelSimplexContribution3D( i + first.x, j + first.y, k + first.z, offset1, seed ) +
		VoxelSimplexContribution3D( i + second.x, j + second.y, k + second.z, offset2, seed ) +
		VoxelSimplexContribution3D( i + 1, j + 1, k + 1, offset3, seed );
	return clamp( value * 32.0, -1.0, 1.0 );
}
