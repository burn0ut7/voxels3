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

float4 VoxelSandOcean < Attribute( "VoxelSandOcean" ); >;
float4 VoxelSandRiver < Attribute( "VoxelSandRiver" ); >;
float3 VoxelSandWater < Attribute( "VoxelSandWater" ); >;
float3 VoxelSandDepths < Attribute( "VoxelSandDepths" ); >;
float2 VoxelSandReach < Attribute( "VoxelSandReach" ); >;
float4 VoxelSandBuriedRegion < Attribute( "VoxelSandBuriedRegion" ); >;
float3 VoxelSandSalts < Attribute( "VoxelSandSalts" ); >;

float GenerateVoxelSandLayerDepth( float2 position, float height, float naturalHeight, float4 terrain, float4 scales, float4 shape )
{
	float seaLevel = shape.y;
	uint seed = (uint)(int)terrain.x;
	float relativeHeight = height - seaLevel;
	if ( relativeHeight > VoxelSandOcean.z || (relativeHeight < VoxelSandWater.x && naturalHeight > seaLevel) )
	{
		return 0.0;
	}
	float river = saturate( (naturalHeight - seaLevel - VoxelSandWater.y) / VoxelSandWater.z );
	float4 recipe = VoxelSandOcean + (VoxelSandRiver - VoxelSandOcean) * river;
	if ( relativeHeight > recipe.z )
	{
		return 0.0;
	}
	float coverage = 0.75 * SampleVoxelSimplex3D( float3( position, 0.0 ), recipe.x, seed ^ (uint)VoxelSandSalts.x ) +
		0.25 * SampleVoxelSimplex3D( float3( position, 0.0 ), recipe.x * 0.25, seed ^ (uint)VoxelSandSalts.y );
	if ( coverage <= recipe.y )
	{
		return 0.0;
	}
	float strength = saturate( (coverage - recipe.y) * 2.0 );
	float bankHeight = recipe.z * (0.15 + 0.85 * strength);
	if ( relativeHeight > bankHeight )
	{
		return 0.0;
	}
	if ( relativeHeight >= 0.0 )
	{
		float reach = (VoxelSandReach.x + (VoxelSandReach.y - VoxelSandReach.x) * river) * (0.25 + 0.75 * strength);
		bool nearWater = false;
		for ( uint direction = 0u; direction < 4u; direction++ )
		{
			float2 offset = float2( 0.0, -reach );
			if ( direction == 0u ) { offset = float2( reach, 0.0 ); }
			if ( direction == 1u ) { offset = float2( -reach, 0.0 ); }
			if ( direction == 2u ) { offset = float2( 0.0, reach ); }
			if ( SampleVoxelLandformHeight( position + offset, terrain, scales, shape.x, seaLevel ) < seaLevel )
			{
				nearWater = true;
				break;
			}
		}
		if ( !nearWater )
		{
			return 0.0;
		}
	}
	return VoxelSandDepths.z + (recipe.w - VoxelSandDepths.z) * strength;
}

bool GenerateVoxelSand( float3 position, float depth, float layerDepth, uint seed )
{
	if ( depth < 0.0 || depth > VoxelSandDepths.y || layerDepth <= 0.0 )
	{
		return false;
	}
	return depth < layerDepth || (depth >= VoxelSandDepths.x && VoxelMaterialRegionContains( position,
		VoxelSandBuriedRegion, (uint)VoxelSandSalts.z, false, seed ));
}
