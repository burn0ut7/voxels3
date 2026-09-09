// Canonical GPU mirror of ProceduralTerrainSdf version 13.
#include "shaders/voxels/voxel_terrain_noise.hlsl"
#include "shaders/voxels/voxel_regional_landforms.hlsl"
#include "shaders/voxels/voxel_terrain_caves.hlsl"

float SampleVoxelSdfWorld( float3 position, float4 terrain, float4 scales, float4 shape )
{
	float surface = position.z - SampleVoxelLandformHeight( position.xy, terrain, scales, shape.x );
	return SampleVoxelCaves( position, (uint)(int)terrain.x, surface );
}

float SampleVoxelSdf( int3 coordinate, float cellSize, float4 terrain, float4 scales, float4 shape )
{
	return SampleVoxelSdfWorld( (float3)coordinate * cellSize, terrain, scales, shape );
}

int3 VoxelCornerOffset( uint cornerIndex )
{
	return int3( cornerIndex & 1, (cornerIndex >> 1) & 1, (cornerIndex >> 2) & 1 );
}
