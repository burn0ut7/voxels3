// Retained version-9 caves.
static const uint VoxelNoodleASeedSalt = 0xA511E9B3u;
static const uint VoxelNoodleBSeedSalt = 0x63D83595u;
static const uint VoxelThicknessSeedSalt = 0xC2B2AE35u;
static const uint VoxelCheeseSeedSalt = 0x27D4EB2Fu;
static const uint VoxelCaveRegionSeedSalt = 0x9E3779B9u;

float SampleVoxelCaveRegion( float3 position, uint seed )
{
	int3 origin = (int3)floor( position );
	float3 fraction = position - (float3)origin;
	float3 blend = fraction * fraction * (3.0 - 2.0 * fraction);
	float value = 0.0;
	[unroll]
	for ( uint corner = 0; corner < 8; corner++ )
	{
		int dx = corner & 1u;
		int dy = (corner >> 1u) & 1u;
		int dz = (corner >> 2u) & 1u;
		float weight = (dx == 0 ? 1.0 - blend.x : blend.x) *
			(dy == 0 ? 1.0 - blend.y : blend.y) * (dz == 0 ? 1.0 - blend.z : blend.z);
		float sampleValue = (VoxelHash3D( origin.x + dx, origin.y + dy, origin.z + dz, seed ) & 65535u) / 32767.5 - 1.0;
		value += sampleValue * weight;
	}
	return clamp( value, -1.0, 1.0 );
}

float SampleVoxelCaves( float3 worldPosition, uint seed, float surfaceDensity )
{
	float depth = -surfaceDensity;
	float envelope = min( depth - 512.0, 32768.0 - depth );
	// Caves cannot affect the surface here. Mirror the CPU exact early exit.
	[branch] if ( envelope <= surfaceDensity ) return surfaceDensity;
	// Raw caves and the regional mask are both >= -1024 (two density scales).
	[branch] if ( envelope <= -1024.0 ) return max( surfaceDensity, envelope );
	float noodleA = SampleVoxelSimplex3D( worldPosition, 6144.0, seed ^ VoxelNoodleASeedSalt );
	float noodleB = SampleVoxelSimplex3D( worldPosition, 6912.0, seed ^ VoxelNoodleBSeedSalt );
	float thickness = SampleVoxelSimplex3D(
		worldPosition, 16384.0,
		seed ^ VoxelThicknessSeedSalt );
	float threshold = 0.056 + 0.016 * thickness;
	float tunnelDensity = 512.0 *
		(threshold - max( abs( noodleA ), abs( noodleB ) ));
	float cheese = SampleVoxelSimplex3D( worldPosition, 8192.0, seed ^ VoxelCheeseSeedSalt );
	float cheeseThreshold = 0.48 - 0.12 * thickness;
	float cheeseDensity = 512.0 * (cheese - cheeseThreshold);

	float regionDensity = 512.0 * (SampleVoxelCaveRegion(
		worldPosition / 16384.0, seed ^ VoxelCaveRegionSeedSalt ) - 0.36);
	envelope = min( envelope, regionDensity );
	float caveDensity = min( max( tunnelDensity, cheeseDensity ), envelope );
	return max( surfaceDensity, caveDensity );
}
