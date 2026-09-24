// Generation resolves vertical material runs at each refined cell crossing and
// stores packed weights beside its edge data. Emission/drawing only consume data.
float VoxelMaterialBlendSpacing < Attribute( "VoxelMaterialBlendSpacing" ); >;
float2 VoxelGeneratedLayers < Attribute( "VoxelGeneratedLayers" ); >;
float4 VoxelSnowLayer < Attribute( "VoxelSnowLayer" ); >;
float2 VoxelGeneratedMountain < Attribute( "VoxelGeneratedMountain" ); >;
float4 VoxelSandDesert < Attribute( "VoxelSandDesert" ); >;
#include "shaders/voxels/voxel_material_regions.hlsl"

float VoxelBiomeLayerDepth( float2 position, float4 terrain, float4 recipe )
{
	float2 unusedGradient;
	float noise = LandformNoise( position, recipe.z, (uint)(int)terrain.x ^ (uint)recipe.w, unusedGradient );
	return recipe.y - (recipe.y - recipe.x) * noise * noise * noise;
}

float4 GenerateVoxelMaterialColumn( float2 position, float4 terrain, float4 scales, float4 shape,
	out float sandLayerDepth, out float desertSandDepth, out float snowLayerDepth, out float marshSoilDepth )
{
	float3 landform = SampleVoxelNaturalLandform( position, terrain, scales, shape.x, shape.y );
	float naturalHeight = landform.x;
	float riverHeight = SampleVoxelRiver( position, naturalHeight, shape.y ).x;
	landform.x = RefineVoxelBiomeHeight( position, terrain, scales, naturalHeight, riverHeight, landform.y, shape.y );
	float2 climate;
	float4 habitat = SampleVoxelBiome( position, terrain, landform.y, climate );
	float cover = VoxelBiomeCoverThreshold( position, terrain );
	float marsh = VoxelMarshWeight( climate, naturalHeight, landform.y, shape.y );
	desertSandDepth = 0.0;
	snowLayerDepth = 0.0;
	marshSoilDepth = 0.0;
	if ( naturalHeight >= shape.y && habitat.y > cover )
	{
		desertSandDepth = VoxelBiomeLayerDepth( position, terrain, VoxelSandDesert );
	}
	sandLayerDepth = GenerateVoxelSandLayerDepth( position, landform.x, naturalHeight, terrain, scales, shape, landform.y, marsh, cover );
	float baseMaterial = landform.y >= VoxelGeneratedMountain.x ? 2.0 : 1.0;
	float topMaterial = baseMaterial;
	if ( landform.x >= shape.y )
	{
		if ( habitat.x > cover )
		{
			topMaterial = 3.0;
			snowLayerDepth = VoxelBiomeLayerDepth( position, terrain, VoxelSnowLayer );
		}
		else if ( desertSandDepth > 0.0 )
		{
			topMaterial = 4.0;
		}
		else
		{
			float slopeSquared = 0.0;
			if ( landform.y >= VoxelGeneratedMountain.x )
			{
				float heightX = SampleVoxelLandformHeight( position + float2( VoxelGeneratedLayers.x, 0.0 ), terrain, scales, shape.x, shape.y );
				float heightY = SampleVoxelLandformHeight( position + float2( 0.0, VoxelGeneratedLayers.x ), terrain, scales, shape.x, shape.y );
				float2 slope = (float2( heightX, heightY ) - landform.x) / VoxelGeneratedLayers.x;
				slopeSquared = dot( slope, slope );
			}
			if ( landform.y < VoxelGeneratedMountain.x || slopeSquared <= VoxelGeneratedMountain.y ) topMaterial = 0.0;
		}
	}
	if ( marsh > cover )
	{
		marshSoilDepth = VoxelGeneratedLayers.y;
		topMaterial = landform.x > shape.y + 12.0 ? 0.0 : 1.0;
		baseMaterial = 1.0;
	}
	return float4( landform.xy, topMaterial, baseMaterial );
}

float3 VoxelMaterialFilter( float fraction )
{
	float left = 0.5 - fraction;
	float right = 0.5 + fraction;
	return float3( 0.5 * left * left, 0.75 - fraction * fraction, 0.5 * right * right );
}

float4 GenerateVoxelMaterialWeights( float3 position, float4 terrain, float4 scales, float4 shape, out float gravel )
{
	// Presentation-only quadratic reconstruction: small material islands may merge.
	// Fixed world-space support keeps regular and transition requests identical.
	float2 lattice = position.xy / VoxelMaterialBlendSpacing;
	float2 origin = floor( lattice + 0.5 );
	float2 fractionXY = lattice - origin;
	float3 filterX = VoxelMaterialFilter( fractionXY.x );
	float3 filterY = VoxelMaterialFilter( fractionXY.y );
	float surfaceDepth = SampleVoxelLandformHeight( position.xy, terrain, scales, shape.x, shape.y ) - position.z;
	float4 weights = float4( 0.0, 0.0, 0.0, 0.0 );
	float sand = 0.0;
	// Gravel remains a separate supported material, with no automatic placement.
	gravel = 0.0;
	for ( uint index = 0u; index < 9u; index++ )
	{
		uint x = index % 3u;
		uint y = index / 3u;
		float2 columnPosition = (origin + float2( x, y ) - 1.0) * VoxelMaterialBlendSpacing;
		float sandLayerDepth;
		float desertSandDepth;
		float snowLayerDepth;
		float marshSoilDepth;
		float4 column = GenerateVoxelMaterialColumn( columnPosition, terrain, scales, shape, sandLayerDepth, desertSandDepth, snowLayerDepth, marshSoilDepth );
		// Follow the same depth in neighboring columns; wider lateral filtering
		// must not turn an inclined grass covering into exposed subsoil.
		float coordinate = (column.x - surfaceDepth) / VoxelGeneratedLayers.x;
		float node = floor( coordinate );
		float fraction = coordinate - node;
		float4 columnWeights = float4( 0.0, 0.0, 0.0, 0.0 );
		float columnSand = 0.0;
		for ( uint z = 0u; z < 2u; z++ )
		{
			float depth = column.x - (node + (float)z) * VoxelGeneratedLayers.x;
			if ( depth < 0.0 )
			{
				continue;
			}
			float contribution = z == 0u ? 1.0 - fraction : fraction;
			float3 nodePosition = float3( columnPosition, (node + (float)z) * VoxelGeneratedLayers.x );
			if ( depth < marshSoilDepth )
			{
				uint marshMaterial = depth < VoxelGeneratedLayers.x ? (uint)column.z : 1u;
				columnWeights[marshMaterial] += contribution;
				continue;
			}
			if ( depth < snowLayerDepth )
			{
				columnWeights.w += contribution;
				continue;
			}
			if ( depth < desertSandDepth || GenerateVoxelSand( nodePosition, depth, sandLayerDepth, (uint)(int)terrain.x ) )
			{
				columnSand += contribution;
				continue;
			}
			uint material = depth < VoxelGeneratedLayers.x ? (uint)column.z :
				(depth < VoxelGeneratedLayers.y ? (uint)column.w : 2u);
			if ( material == 4u ) columnSand += contribution;
			else columnWeights[material] += contribution;
		}
		float columnTotal = dot( columnWeights, float4( 1.0, 1.0, 1.0, 1.0 ) ) + columnSand;
		float influence = filterX[x] * filterY[y];
		if ( columnTotal > 0.000001 )
		{
			weights += columnWeights * (influence / columnTotal);
			sand += columnSand * (influence / columnTotal);
		}
		else
		{
			weights.y += influence;
		}
	}
	float total = dot( weights, float4( 1.0, 1.0, 1.0, 1.0 ) ) + sand;
	return total > 0.000001 ? weights / total : float4( 0.0, 1.0, 0.0, 0.0 );
}

uint2 PackGeneratedVoxelWeights( float4 weights, float gravel )
{
	// Sand is the sixth weight (remainder). Round gravel before the other
	// channels so a sand-free mixture keeps an exact total of 255.
	int gravelByte = (int)round( saturate( gravel ) * 255.0 );
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
	int target = min( (int)round( dot( scaled, float4( 1.0, 1.0, 1.0, 1.0 ) ) ), 255 - gravelByte );
	rounded[largest] += target - (rounded.x + rounded.y + rounded.z + rounded.w);
	uint4 packed = (uint4)rounded;
	return uint2( packed.x | (packed.y << 8u) | (packed.z << 16u) | (packed.w << 24u),
		asuint( (float)gravelByte / 255.0 ) );
}
