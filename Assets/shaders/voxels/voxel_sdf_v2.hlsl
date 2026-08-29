// Canonical GPU mirror of ProceduralTerrainSdf version 2.
static const float VoxelSimplexF2 = 0.3660254037844386;
static const float VoxelSimplexG2 = 0.21132486540518713;

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

float2 VoxelGradient2D( uint hash )
{
	switch ( hash & 7u )
	{
		case 0u: return float2( 1.0, 0.0 );
		case 1u: return float2( -1.0, 0.0 );
		case 2u: return float2( 0.0, 1.0 );
		case 3u: return float2( 0.0, -1.0 );
		case 4u: return float2( 0.70710677, 0.70710677 );
		case 5u: return float2( -0.70710677, 0.70710677 );
		case 6u: return float2( 0.70710677, -0.70710677 );
		default: return float2( -0.70710677, -0.70710677 );
	}
}

float VoxelSimplexContribution2D(
	int x,
	int y,
	float offsetX,
	float offsetY,
	uint seed )
{
	float attenuation = 0.5 - offsetX * offsetX - offsetY * offsetY;
	if ( attenuation <= 0.0 )
	{
		return 0.0;
	}

	float2 gradient = VoxelGradient2D( VoxelHash2D( x, y, seed ) );
	attenuation *= attenuation;
	return attenuation * attenuation * dot( gradient, float2( offsetX, offsetY ) );
}

float SampleVoxelSimplex2D( float2 position, uint seed )
{
	float skew = (position.x + position.y) * VoxelSimplexF2;
	int i = (int)floor( position.x + skew );
	int j = (int)floor( position.y + skew );
	float unskew = (i + j) * VoxelSimplexG2;
	float x0 = position.x - (i - unskew);
	float y0 = position.y - (j - unskew);
	int i1 = x0 > y0 ? 1 : 0;
	int j1 = x0 > y0 ? 0 : 1;
	float x1 = x0 - i1 + VoxelSimplexG2;
	float y1 = y0 - j1 + VoxelSimplexG2;
	float x2 = x0 - 1.0 + 2.0 * VoxelSimplexG2;
	float y2 = y0 - 1.0 + 2.0 * VoxelSimplexG2;
	float value =
		VoxelSimplexContribution2D( i, j, x0, y0, seed ) +
		VoxelSimplexContribution2D( i + i1, j + j1, x1, y1, seed ) +
		VoxelSimplexContribution2D( i + 1, j + 1, x2, y2, seed );
	return clamp( value * 70.0, -1.0, 1.0 );
}

float SampleVoxelSurfaceHeight(
	float2 worldPosition,
	int worldSeed,
	float surfaceBaseHeight,
	float surfaceFrequency,
	float surfaceAmplitude )
{
	return surfaceBaseHeight + SampleVoxelSimplex2D(
		worldPosition * surfaceFrequency,
		(uint)worldSeed ) * surfaceAmplitude;
}

float SampleVoxelSdfWorld(
	float3 worldPosition,
	int worldSeed,
	float surfaceBaseHeight,
	float surfaceFrequency,
	float surfaceAmplitude )
{
	return worldPosition.z - SampleVoxelSurfaceHeight(
		worldPosition.xy,
		worldSeed,
		surfaceBaseHeight,
		surfaceFrequency,
		surfaceAmplitude );
}

float SampleVoxelSdf(
	int3 globalSampleCoordinate,
	float cellSize,
	int worldSeed,
	float surfaceBaseHeight,
	float surfaceFrequency,
	float surfaceAmplitude )
{
	return SampleVoxelSdfWorld(
		(float3)globalSampleCoordinate * cellSize,
		worldSeed,
		surfaceBaseHeight,
		surfaceFrequency,
		surfaceAmplitude );
}

int3 VoxelCornerOffset( uint cornerIndex )
{
	return int3( cornerIndex & 1, (cornerIndex >> 1) & 1, (cornerIndex >> 2) & 1 );
}

float3 SampleVoxelSdfGradient(
	float3 worldPosition,
	float cellSize,
	int worldSeed,
	float surfaceBaseHeight,
	float surfaceFrequency,
	float surfaceAmplitude )
{
	float stepSize = cellSize * 0.5;
	return float3(
		SampleVoxelSdfWorld(
			worldPosition + float3( stepSize, 0, 0 ),
			worldSeed, surfaceBaseHeight, surfaceFrequency, surfaceAmplitude ) -
		SampleVoxelSdfWorld(
			worldPosition - float3( stepSize, 0, 0 ),
			worldSeed, surfaceBaseHeight, surfaceFrequency, surfaceAmplitude ),
		SampleVoxelSdfWorld(
			worldPosition + float3( 0, stepSize, 0 ),
			worldSeed, surfaceBaseHeight, surfaceFrequency, surfaceAmplitude ) -
		SampleVoxelSdfWorld(
			worldPosition - float3( 0, stepSize, 0 ),
			worldSeed, surfaceBaseHeight, surfaceFrequency, surfaceAmplitude ),
		2.0 * stepSize );
}
