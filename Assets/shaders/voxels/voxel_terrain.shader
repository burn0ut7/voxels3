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
}

struct VertexInput
{
	float3 Position : POSITION < Semantic( None ); >;
	float3 Normal : NORMAL < Semantic( None ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	PixelInput MainVs( const VertexInput input )
	{
		PixelInput output;
		output.vPositionWs = input.Position - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( input.Position );
		output.vNormalWs = normalize( input.Normal );
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
		float checker = frac( (floor( worldPosition.x / 256.0 ) + floor( worldPosition.y / 256.0 )) * 0.5 ) * 2.0;
		material.Albedo = lerp( float3( 0.14, 0.32, 0.08 ), float3( 0.26, 0.52, 0.14 ), checker );
		material.Roughness = 0.9;
		material.Metalness = 0.0;
		return ShadingModelStandard::Shade( input, material );
	}
}
