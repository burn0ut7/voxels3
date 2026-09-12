HEADER
{
	Description = "Persistent GPU Voxel Terrain";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
}

COMMON
{
	// Persistent geometry supplies final world-space positions and normals.
	#include "common/shared.hlsl"
	float4 VoxelMaterialIds < Attribute( "VoxelMaterialIds" ); >;
	float VoxelCheckerSize < Attribute( "VoxelCheckerSize" ); >;
	StructuredBuffer<float4> VoxelMaterialPalette < Attribute( "VoxelMaterialPalette" ); >;
}

struct VertexInput
{
	float3 Position : POSITION < Semantic( None ); >;
	float3 Normal : NORMAL < Semantic( None ); >;
	float4 Materials : COLOR0 < Semantic( None ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vMaterialWeights : TEXCOORD8;
};

VS
{
	#include "shaders/voxels/voxel_terrain_normal.hlsl"

	PixelInput MainVs( const VertexInput input )
	{
		PixelInput output;
		float3 normal = DecodeTerrainNormal( input.Normal.yz );
		output.vPositionWs = input.Position - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( input.Position );
		output.vNormalWs = normal;
		output.vMaterialWeights = input.Materials;
		return output;
	}
}

PS
{
	#include "common/pixel.hlsl"
	RenderState( CullMode, BACK );
	RenderState( DepthWriteEnable, true );
	float4 MainPs( PixelInput input ) : SV_Target0
	{
		Material material = Material::Init( input );
		float3 worldPosition = input.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 normal = normalize( input.vNormalWs );
		float3 axis = abs( normal );
		float2 coordinates = worldPosition.xy;
		if ( axis.x > axis.y && axis.x > axis.z )
		{
			coordinates = worldPosition.yz;
		}
		else if ( axis.y > axis.z )
		{
			coordinates = worldPosition.xz;
		}
		coordinates /= VoxelCheckerSize;
		float checker = frac( (floor( coordinates.x ) + floor( coordinates.y )) * 0.5 ) * 2.0;
		float2 footprint = fwidth( coordinates );
		checker = lerp( checker, 0.5, saturate( max( footprint.x, footprint.y ) ) );
		float4 weights = max( input.vMaterialWeights, 0.0 );
		weights /= max( dot( weights, float4( 1.0, 1.0, 1.0, 1.0 ) ), 0.000001 );
		float3 albedo = float3( 0.0, 0.0, 0.0 );
		for ( uint index = 0u; index < 4u; index++ )
		{
			uint id = (uint)VoxelMaterialIds[index];
			albedo += weights[index] * lerp( VoxelMaterialPalette[id * 2u].rgb, VoxelMaterialPalette[id * 2u + 1u].rgb, checker );
		}
		material.Albedo = albedo;
		material.Roughness = 0.9;
		material.Metalness = 0.0;
		return ShadingModelStandard::Shade( input, material );
	}
}
