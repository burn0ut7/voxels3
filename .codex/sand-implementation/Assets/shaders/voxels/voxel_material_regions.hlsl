// MaterialSpawnRegion.Contains mirror. Parameters and recipe salts are CPU-owned.
bool VoxelMaterialRegionContains( float3 position, float4 region, uint salt, bool column, uint seed )
{
	float3 coordinate = position / region.xyz;
	if ( column )
	{
		coordinate.z = 0.0;
	}
	int3 cell = (int3)floor( coordinate );
	uint hash = VoxelHash3D( cell.x, cell.y, cell.z, seed ^ salt );
	if ( (float)(hash >> 8u) * (1.0 / 16777216.0) >= region.w )
	{
		return false;
	}
	uint shape = VoxelHash3D( cell.x, cell.y, cell.z, hash ^ 0xA511E9B3u );
	float radius = 0.22 + (float)((shape >> 24u) & 255u) * (0.12 / 255.0);
	float3 center = 0.4 + float3( shape & 255u, (shape >> 8u) & 255u, (shape >> 16u) & 255u ) * (0.2 / 255.0);
	float3 offset = (coordinate - (float3)cell - center) / radius;
	if ( column )
	{
		offset.z = 0.0;
	}
	return dot( offset, offset ) <= 1.0;
}

float4 VoxelSandShore < Attribute( "VoxelSandShore" ); >;
float VoxelSandMaximumDepth < Attribute( "VoxelSandMaximumDepth" ); >;
float VoxelSandPatchMaximumHeight < Attribute( "VoxelSandPatchMaximumHeight" ); >;
float4 VoxelSandSurfaceRegion < Attribute( "VoxelSandSurfaceRegion" ); >;
float4 VoxelSandBuriedRegion < Attribute( "VoxelSandBuriedRegion" ); >;
float2 VoxelSandRegionSalts < Attribute( "VoxelSandRegionSalts" ); >;

bool GenerateVoxelSand( float3 position, float depth, float height, float mountains, float seaLevel, uint seed, float mountainThreshold )
{
	if ( depth < 0.0 || depth > VoxelSandMaximumDepth )
	{
		return false;
	}
	float relativeHeight = height - seaLevel;
	if ( relativeHeight < VoxelSandShore.x || relativeHeight > VoxelSandPatchMaximumHeight )
	{
		return false;
	}
	if ( depth < VoxelSandShore.z )
	{
		if ( relativeHeight <= VoxelSandShore.y )
		{
			return true;
		}
		return mountains < mountainThreshold && VoxelMaterialRegionContains( position,
			VoxelSandSurfaceRegion, (uint)VoxelSandRegionSalts.x, true, seed );
	}
	return relativeHeight <= VoxelSandShore.y && depth >= VoxelSandShore.w && VoxelMaterialRegionContains( position,
		VoxelSandBuriedRegion, (uint)VoxelSandRegionSalts.y, false, seed );
}
