// Canonical GPU mirror of ProceduralTerrainSdf; version is owned by its descriptor.
#include "shaders/voxels/voxel_terrain_noise.hlsl"
#include "shaders/voxels/voxel_regional_landforms.hlsl"
#include "shaders/voxels/voxel_terrain_caves.hlsl"
#include "shaders/voxels/voxel_cliffs.hlsl"

float SampleVoxelSdfFromLandform( float3 position, float4 terrain, float4 scales, float4 shape, float2 landform )
{
	float surface = position.z - landform.x;
	return SampleVoxelCliffs( position, terrain, scales, shape.y, landform.y,
		SampleVoxelCaves( position, (uint)(int)terrain.x, surface ) );
}

float SampleVoxelSdfWorld( float3 position, float4 terrain, float4 scales, float4 shape )
{
	float3 landform = SampleVoxelLandform( position.xy, terrain, scales, shape.x, shape.y );
	return SampleVoxelSdfFromLandform( position, terrain, scales, shape, landform.xy );
}

float SampleVoxelSdf( int3 coordinate, float cellSize, float4 terrain, float4 scales, float4 shape )
{
	return SampleVoxelSdfWorld( (float3)coordinate * cellSize, terrain, scales, shape );
}

int3 VoxelCornerOffset( uint cornerIndex )
{
	return int3( cornerIndex & 1, (cornerIndex >> 1) & 1, (cornerIndex >> 2) & 1 );
}
