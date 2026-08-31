// Canonical GPU mirror of ProceduralTerrainSdf version 5.
static const float VoxelSimplexF2 = 0.3660254037844386;
static const float VoxelSimplexG2 = 0.21132486540518713;
static const float VoxelSimplexF3 = 1.0 / 3.0;
static const float VoxelSimplexG3 = 1.0 / 6.0;
static const uint VoxelNoodleASeedSalt = 0xA511E9B3u;
static const uint VoxelNoodleBSeedSalt = 0x63D83595u;
static const uint VoxelThicknessSeedSalt = 0xC2B2AE35u;
static const uint VoxelCheeseSeedSalt = 0x27D4EB2Fu;

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

float VoxelSimplexContribution3D(
	int x,
	int y,
	int z,
	float3 offset,
	uint seed )
{
	float attenuation = 0.6 - dot( offset, offset );
	if ( attenuation <= 0.0 )
	{
		return 0.0;
	}

	float3 gradient = VoxelGradient3D( VoxelHash3D( x, y, z, seed ) );
	attenuation *= attenuation;
	return attenuation * attenuation * dot( gradient, offset );
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

float SampleVoxelSimplex3D( float3 position, uint seed )
{
	float skew = (position.x + position.y + position.z) * VoxelSimplexF3;
	int i = (int)floor( position.x + skew );
	int j = (int)floor( position.y + skew );
	int k = (int)floor( position.z + skew );
	float unskew = (i + j + k) * VoxelSimplexG3;
	float3 offset0 = position - (float3( i, j, k ) - unskew);
	int3 first;
	int3 second;
	if ( offset0.x >= offset0.y )
	{
		if ( offset0.y >= offset0.z )
		{
			first = int3( 1, 0, 0 ); second = int3( 1, 1, 0 );
		}
		else if ( offset0.x >= offset0.z )
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
		if ( offset0.y < offset0.z )
		{
			first = int3( 0, 0, 1 ); second = int3( 0, 1, 1 );
		}
		else if ( offset0.x < offset0.z )
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
	float surfaceDensity = worldPosition.z - SampleVoxelSurfaceHeight(
		worldPosition.xy,
		worldSeed,
		surfaceBaseHeight,
		surfaceFrequency,
		surfaceAmplitude );
	uint seed = (uint)worldSeed;
	float noodleA = SampleVoxelSimplex3D( worldPosition / 6144.0, seed ^ VoxelNoodleASeedSalt );
	float noodleB = SampleVoxelSimplex3D( worldPosition / 6912.0, seed ^ VoxelNoodleBSeedSalt );
	float thickness = SampleVoxelSimplex3D(
		worldPosition / 16384.0,
		seed ^ VoxelThicknessSeedSalt );
	float threshold = 0.056 + 0.016 * thickness;
	float tunnelDensity = 512.0 *
		(threshold - max( abs( noodleA ), abs( noodleB ) ));
	float cheese = SampleVoxelSimplex3D( worldPosition / 8192.0, seed ^ VoxelCheeseSeedSalt );
	float cheeseThreshold = 0.48 - 0.12 * thickness;
	float cheeseDensity = 512.0 * (cheese - cheeseThreshold);
	float depth = -surfaceDensity;
	float envelope = min( depth - 512.0, 8192.0 - depth );
	float caveDensity = min( max( tunnelDensity, cheeseDensity ), envelope );
	return max( surfaceDensity, caveDensity );
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
		SampleVoxelSdfWorld(
			worldPosition + float3( 0, 0, stepSize ),
			worldSeed, surfaceBaseHeight, surfaceFrequency, surfaceAmplitude ) -
		SampleVoxelSdfWorld(
			worldPosition - float3( 0, 0, stepSize ),
			worldSeed, surfaceBaseHeight, surfaceFrequency, surfaceAmplitude ) );
}
