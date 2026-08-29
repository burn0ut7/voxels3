// Canonical GPU mirror of ProceduralTerrainSdf version 1. Integer global
// lattice coordinates keep shared chunk-corner inputs identical.
static const float VoxelHillAmplitude0 = 320.0;
static const float VoxelHillWavelength0 = 4096.0;
static const float VoxelHillAmplitude1 = 96.0;
static const float VoxelHillWavelength1 = 2048.0;
static const float VoxelCaveWeight0 = 0.67;
static const float VoxelCaveWavelength0 = 1024.0;
static const float VoxelCaveWeight1 = 0.33;
static const float VoxelCaveWavelength1 = 512.0;
static const float VoxelCaveThreshold = 0.18;
static const float VoxelCaveScale = 192.0;
static const float VoxelCaveCenterZ = -128.0;
static const float VoxelCaveHalfExtent = 512.0;

uint RotateVoxelHashLeft( uint value, uint count )
{
	return (value << count) | (value >> (32u - count));
}

float VoxelHashValue( int x, int y, int z, uint seed, uint salt )
{
	uint hash = seed ^ salt;
	hash ^= (uint)x * 0x9E3779B1u;
	hash = RotateVoxelHashLeft( hash, 13u ) * 0x85EBCA77u;
	hash ^= (uint)y * 0xC2B2AE3Du;
	hash = RotateVoxelHashLeft( hash, 15u ) * 0x27D4EB2Fu;
	hash ^= (uint)z * 0x165667B1u;
	hash ^= hash >> 16u;
	hash *= 0x7FEB352Du;
	hash ^= hash >> 15u;
	hash *= 0x846CA68Bu;
	hash ^= hash >> 16u;
	return (float)(hash & 0x00FFFFFFu) * (2.0 / 16777215.0) - 1.0;
}

float SmoothVoxelNoise( float value )
{
	return value * value * (3.0 - 2.0 * value);
}

float SampleVoxelValueNoise2D( float2 worldPosition, float wavelength, uint seed, uint salt )
{
	float2 samplePosition = worldPosition / wavelength;
	int2 minimum = int2( floor( samplePosition ) );
	float2 blend = float2(
		SmoothVoxelNoise( samplePosition.x - minimum.x ),
		SmoothVoxelNoise( samplePosition.y - minimum.y ) );
	float lower = lerp(
		VoxelHashValue( minimum.x, minimum.y, 0, seed, salt ),
		VoxelHashValue( minimum.x + 1, minimum.y, 0, seed, salt ),
		blend.x );
	float upper = lerp(
		VoxelHashValue( minimum.x, minimum.y + 1, 0, seed, salt ),
		VoxelHashValue( minimum.x + 1, minimum.y + 1, 0, seed, salt ),
		blend.x );
	return lerp( lower, upper, blend.y );
}

float SampleVoxelValueNoise3D( float3 worldPosition, float wavelength, uint seed, uint salt )
{
	float3 samplePosition = worldPosition / wavelength;
	int3 minimum = int3( floor( samplePosition ) );
	float3 blend = float3(
		SmoothVoxelNoise( samplePosition.x - minimum.x ),
		SmoothVoxelNoise( samplePosition.y - minimum.y ),
		SmoothVoxelNoise( samplePosition.z - minimum.z ) );
	float z0y0 = lerp(
		VoxelHashValue( minimum.x, minimum.y, minimum.z, seed, salt ),
		VoxelHashValue( minimum.x + 1, minimum.y, minimum.z, seed, salt ),
		blend.x );
	float z0y1 = lerp(
		VoxelHashValue( minimum.x, minimum.y + 1, minimum.z, seed, salt ),
		VoxelHashValue( minimum.x + 1, minimum.y + 1, minimum.z, seed, salt ),
		blend.x );
	float z1y0 = lerp(
		VoxelHashValue( minimum.x, minimum.y, minimum.z + 1, seed, salt ),
		VoxelHashValue( minimum.x + 1, minimum.y, minimum.z + 1, seed, salt ),
		blend.x );
	float z1y1 = lerp(
		VoxelHashValue( minimum.x, minimum.y + 1, minimum.z + 1, seed, salt ),
		VoxelHashValue( minimum.x + 1, minimum.y + 1, minimum.z + 1, seed, salt ),
		blend.x );
	return lerp( lerp( z0y0, z0y1, blend.y ), lerp( z1y0, z1y1, blend.y ), blend.z );
}

float SampleVoxelSdfWorld( float3 worldPosition, int worldSeed, int generatorVersion )
{
	uint versionedSeed = (uint)worldSeed ^ (uint)generatorVersion * 0x9E3779B9u;
	float hillHeight =
		VoxelHillAmplitude0 * SampleVoxelValueNoise2D(
			worldPosition.xy, VoxelHillWavelength0, versionedSeed, 0xA511E9B3u ) +
		VoxelHillAmplitude1 * SampleVoxelValueNoise2D(
			worldPosition.xy, VoxelHillWavelength1, versionedSeed, 0x63D83595u );
	float terrainDensity = worldPosition.z - hillHeight;
	float caveNoise =
		VoxelCaveWeight0 * SampleVoxelValueNoise3D(
			worldPosition, VoxelCaveWavelength0, versionedSeed, 0xB5297A4Du ) +
		VoxelCaveWeight1 * SampleVoxelValueNoise3D(
			worldPosition, VoxelCaveWavelength1, versionedSeed, 0x1B56C4E9u );
	float caveNoiseBoundary = (abs( caveNoise ) - VoxelCaveThreshold) * VoxelCaveScale;
	float caveVerticalEnvelope = abs( worldPosition.z - VoxelCaveCenterZ ) - VoxelCaveHalfExtent;
	float caveBoundary = max( caveNoiseBoundary, caveVerticalEnvelope );
	return max( terrainDensity, -caveBoundary );
}

float SampleVoxelSdf(
	int3 globalSampleCoordinate,
	float cellSize,
	int worldSeed,
	int generatorVersion )
{
	return SampleVoxelSdfWorld(
		(float3)globalSampleCoordinate * cellSize,
		worldSeed,
		generatorVersion );
}

int3 VoxelCornerOffset( uint cornerIndex )
{
	return int3( cornerIndex & 1, (cornerIndex >> 1) & 1, (cornerIndex >> 2) & 1 );
}

float3 SampleVoxelSdfGradient(
	float3 worldPosition,
	float cellSize,
	int worldSeed,
	int generatorVersion )
{
	float stepSize = cellSize * 0.5;
	return float3(
		SampleVoxelSdfWorld( worldPosition + float3( stepSize, 0, 0 ), worldSeed, generatorVersion ) -
			SampleVoxelSdfWorld( worldPosition - float3( stepSize, 0, 0 ), worldSeed, generatorVersion ),
		SampleVoxelSdfWorld( worldPosition + float3( 0, stepSize, 0 ), worldSeed, generatorVersion ) -
			SampleVoxelSdfWorld( worldPosition - float3( 0, stepSize, 0 ), worldSeed, generatorVersion ),
		SampleVoxelSdfWorld( worldPosition + float3( 0, 0, stepSize ), worldSeed, generatorVersion ) -
			SampleVoxelSdfWorld( worldPosition - float3( 0, 0, stepSize ), worldSeed, generatorVersion ) );
}
