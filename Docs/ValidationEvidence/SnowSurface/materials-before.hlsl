// Procedural material nodes are independent of the rendered surface angle and LOD.
#include "shaders/voxels/voxel_terrain_noise.hlsl"
#include "shaders/voxels/voxel_regional_landforms.hlsl"

float4 VoxelMaterialIds < Attribute( "VoxelMaterialIds" ); >;
float4 VoxelMaterialSnow < Attribute( "VoxelMaterialSnow" ); >;
float4 VoxelMaterialRules < Attribute( "VoxelMaterialRules" ); >;
float4 VoxelMaterialTerrain < Attribute( "VoxelMaterialTerrain" ); >;
float4 VoxelMaterialScales < Attribute( "VoxelMaterialScales" ); >;
float VoxelMaterialRuggedness < Attribute( "VoxelMaterialRuggedness" ); >;
float VoxelSeaLevel < Attribute( "VoxelSeaLevel" ); >;
StructuredBuffer<float4> VoxelMaterialPalette < Attribute( "VoxelMaterialPalette" ); >;

float VoxelMaterialSurfaceHeight( float2 position )
{
	return SampleVoxelLandformHeight( position, VoxelMaterialTerrain,
		VoxelMaterialScales, VoxelMaterialRuggedness, VoxelSeaLevel );
}

uint VoxelNodeMaterial( float depth, float surfaceHeight, float mountainWeight, float peakFraction, float waterHeight )
{
	if ( depth < 0.0 )
	{
		return (uint)VoxelMaterialIds.w;
	}
	if ( depth < VoxelMaterialRules.x && surfaceHeight >= waterHeight )
	{
		return mountainWeight >= VoxelMaterialSnow.z && peakFraction >= VoxelMaterialSnow.x
			? (uint)VoxelMaterialSnow.y : (uint)VoxelMaterialIds.x;
	}
	if ( depth < VoxelMaterialRules.y )
	{
		return (uint)VoxelMaterialIds.y;
	}
	return (uint)VoxelMaterialIds.z;
}

float3 SampleVoxelMaterialColor( float3 position, float checker, float surfaceDepth )
{
	float3 node = floor( position / VoxelMaterialRules.x );
	float3 fraction = position / VoxelMaterialRules.x - node;
	float4 heights;
	float4 mountainWeights;
	float4 peakFractions;
	float4 waterHeights;
	float4 columnWeights;
	float surfaceHeight = 0.0;
	[unroll]
	for ( uint column = 0; column < 4; column++ )
	{
		float2 offset = float2( column & 1u, (column >> 1u) & 1u );
		float2 xy = (node.xy + offset) * VoxelMaterialRules.x;
		float2 xyWeight = lerp( 1.0 - fraction.xy, fraction.xy, offset );
		float3 landform = SampleVoxelNaturalLandform( xy, VoxelMaterialTerrain,
			VoxelMaterialScales, VoxelMaterialRuggedness, VoxelSeaLevel );
		float4 river = SampleVoxelRiver( xy, landform.x, VoxelSeaLevel );
		heights[column] = river.x;
		waterHeights[column] = river.y;
		mountainWeights[column] = landform.y;
		peakFractions[column] = landform.z;
		columnWeights[column] = xyWeight.x * xyWeight.y;
		surfaceHeight += heights[column] * columnWeights[column];
	}
	// Attach to the base-lattice surface already sampled for the material nodes.
	// Excavation/cave depth survives; coarse triangle sag must not expose soil.
	float materialZ = (surfaceHeight - surfaceDepth) / VoxelMaterialRules.x;
	node.z = floor( materialZ );
	fraction.z = materialZ - node.z;
	float3 color = float3( 0.0, 0.0, 0.0 );
	float weightSum = 0.0;
	// Four columns, two nodes each. Blend never reaches beyond this base voxel.
	[unroll]
	for ( uint column = 0; column < 4; column++ )
	{
		[unroll]
		for ( uint z = 0; z < 2; z++ )
		{
			float depth = heights[column] - (node.z + (float)z) * VoxelMaterialRules.x;
			uint id = VoxelNodeMaterial( depth, heights[column], mountainWeights[column], peakFractions[column], waterHeights[column] );
			if ( id != (uint)VoxelMaterialIds.w )
			{
				float weight = columnWeights[column] * (z == 0 ? 1.0 - fraction.z : fraction.z);
				color += weight * lerp( VoxelMaterialPalette[id * 2].rgb,
					VoxelMaterialPalette[id * 2 + 1].rgb, checker );
				weightSum += weight;
			}
		}
	}
	if ( weightSum > 0.000001 )
	{
		return color / weightSum;
	}
	// Density additions above the original ground have no generated soil nodes.
	uint dirt = (uint)VoxelMaterialIds.y;
	return lerp( VoxelMaterialPalette[dirt * 2].rgb,
		VoxelMaterialPalette[dirt * 2 + 1].rgb, checker );
}
