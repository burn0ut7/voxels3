// Generation resolves vertical material runs at each refined cell crossing and
// stores packed weights beside its edge data. Emission/drawing only consume data.
float2 VoxelGeneratedLayers < Attribute( "VoxelGeneratedLayers" ); >;
float4 VoxelGeneratedMountain < Attribute( "VoxelGeneratedMountain" ); >;
#include "shaders/voxels/voxel_material_regions.hlsl"

float4 GenerateVoxelMaterialColumn( float2 position, float4 terrain, float4 scales, float4 shape )
{
	float3 landform = SampleVoxelLandform( position, terrain, scales, shape.x, shape.y );
	float baseMaterial = landform.y >= VoxelGeneratedMountain.z ? 2.0 : 1.0;
	float topMaterial = baseMaterial;
	if ( landform.x >= shape.y )
	{
		if ( landform.y >= VoxelGeneratedMountain.y && landform.z >= VoxelGeneratedMountain.x )
		{
			topMaterial = 3.0;
		}
		else
		{
			float slopeSquared = 0.0;
			if ( landform.y >= VoxelGeneratedMountain.z )
			{
				float heightX = SampleVoxelLandformHeight( position + float2( VoxelGeneratedLayers.x, 0.0 ), terrain, scales, shape.x, shape.y );
				float heightY = SampleVoxelLandformHeight( position + float2( 0.0, VoxelGeneratedLayers.x ), terrain, scales, shape.x, shape.y );
				float2 slope = (float2( heightX, heightY ) - landform.x) / VoxelGeneratedLayers.x;
				slopeSquared = dot( slope, slope );
			}
			if ( landform.y < VoxelGeneratedMountain.z || slopeSquared <= VoxelGeneratedMountain.w ) topMaterial = 0.0;
		}
	}
	return float4( landform.xy, topMaterial, baseMaterial );
}

float4 GenerateVoxelMaterialWeights( float3 position, float4 terrain, float4 scales, float4 shape )
{
	// Classify the same four base-lattice columns as the canonical material
	// reconstruction. One arbitrary XY column can have no solid contributor on
	// a sloping surface even though adjacent material nodes contain the surface.
	float2 lattice = position.xy / VoxelGeneratedLayers.x;
	float2 origin = floor( lattice );
	float2 fractionXY = lattice - origin;
	float4 columns[4];
	float4 columnWeights;
	float height = 0.0;
	for ( uint index = 0u; index < 4u; index++ )
	{
		float2 offset = float2( index & 1u, (index >> 1u) & 1u );
		columns[index] = GenerateVoxelMaterialColumn( (origin + offset) * VoxelGeneratedLayers.x, terrain, scales, shape );
		float2 contribution = lerp( 1.0 - fractionXY, fractionXY, offset );
		columnWeights[index] = contribution.x * contribution.y;
		height += columns[index].x * columnWeights[index];
	}
	float surfaceDepth = SampleVoxelLandformHeight( position.xy, terrain, scales, shape.x, shape.y ) - position.z;
	float coordinate = (height - surfaceDepth) / VoxelGeneratedLayers.x;
	float node = floor( coordinate );
	float fraction = coordinate - node;
	float4 weights = float4( 0.0, 0.0, 0.0, 0.0 );
	float sand = 0.0;
	for ( uint index = 0u; index < 4u; index++ )
	{
		float4 column = columns[index];
		for ( uint z = 0u; z < 2u; z++ )
		{
			float depth = column.x - (node + (float)z) * VoxelGeneratedLayers.x;
			if ( depth < 0.0 ) continue;
			float contribution = columnWeights[index] * (z == 0u ? 1.0 - fraction : fraction);
			float2 offset = float2( index & 1u, (index >> 1u) & 1u );
			float3 nodePosition = float3( (origin + offset) * VoxelGeneratedLayers.x, (node + (float)z) * VoxelGeneratedLayers.x );
			if ( GenerateVoxelSand( nodePosition, depth, column.x, column.y, shape.y, (uint)(int)terrain.x, VoxelGeneratedMountain.z ) )
			{
				sand += contribution;
				continue;
			}
			uint material = depth < VoxelGeneratedLayers.x ? (uint)column.z :
				(depth < VoxelGeneratedLayers.y ? (uint)column.w : 2u);
			weights[material] += contribution;
		}
	}
	float total = dot( weights, float4( 1.0, 1.0, 1.0, 1.0 ) ) + sand;
	return total > 0.000001 ? weights / total : float4( 0.0, 1.0, 0.0, 0.0 );
}

uint PackGeneratedVoxelWeights( float4 weights )
{
	// Fifth weight (sand) is the remainder. Preserve the rounded total so
	// quantization cannot introduce sand into a surface containing none.
	float4 scaled = saturate( weights ) * 255.0;
	int4 rounded = (int4)round( scaled );
	uint largest = 0u;
	for ( uint index = 1u; index < 4u; index++ )
	{
		if ( scaled[index] > scaled[largest] )
		{
			largest = index;
		}
	}
	int target = (int)round( min( dot( scaled, float4( 1.0, 1.0, 1.0, 1.0 ) ), 255.0 ) );
	rounded[largest] += target - (rounded.x + rounded.y + rounded.z + rounded.w);
	uint4 packed = (uint4)rounded;
	return packed.x | (packed.y << 8u) | (packed.z << 16u) | (packed.w << 24u);
}
