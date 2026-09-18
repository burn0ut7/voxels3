HEADER
{
	Description = "Opaque static terrain grass blades";
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}
COMMON
{
	#include "common/shared.hlsl"
}
struct VertexInput
{
	uint VertexId : SV_VertexID;
	uint InstanceId : SV_InstanceID;
	float3 Position : POSITION < Semantic( None ); >;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float2 GrassTint : TEXCOORD8;
};
VS
{
	StructuredBuffer<float4> GrassRoots < Attribute( "GrassRoots" ); >;
	PixelInput MainVs( const VertexInput input )
	{
		float4 root = GrassRoots[input.InstanceId * 2];
		float4 shape = GrassRoots[input.InstanceId * 2 + 1];
		// One opaque tapered triangle per blade; no alpha-card overdraw.
		float height = input.VertexId == 2 ? 1.0 : 0.0;
		float side = input.VertexId == 2 ? 0.0 : (input.VertexId == 0 ? -1.0 : 1.0);
		float3 widthAxis = float3( cos( shape.x ), sin( shape.x ), 0.0 );
		float3 bendAxis = float3( -widthAxis.y, widthAxis.x, 0.0 );
		float3 position = root.xyz + root.w * (widthAxis * side * shape.z * (1.0 - height * 0.65) +
			float3( 0.0, 0.0, height * shape.y ) + bendAxis * height * height * shape.y * 0.18);
		PixelInput output = (PixelInput)0;
		output.vPositionWs = position - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( position );
		output.vNormalWs = normalize( bendAxis + float3( 0.0, 0.0, 0.8 ) );
		output.GrassTint = float2( height, shape.w );
		return output;
	}
}
PS
{
	#include "common/pixel.hlsl"
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, true );
	float4 MainPs( PixelInput input, bool frontFace : SV_IsFrontFace ) : SV_Target0
	{
		float3 normal = normalize( float3( input.vNormalWs.xy * (frontFace ? 1.0 : -1.0), abs( input.vNormalWs.z ) ) );
		#if S_MODE_DEPTH
			return DepthNormals::Output( normal, 0.95, 1.0 );
		#else
		Material material = Material::Init( input );
		material.Albedo = lerp( float3( 0.065, 0.095, 0.018 ), float3( 0.15, 0.23, 0.045 ), input.GrassTint.x );
		material.Albedo *= lerp( 0.8, 1.2, input.GrassTint.y );
		material.Normal = normal;
		material.Roughness = 0.95;
		material.AmbientOcclusion = lerp( 0.65, 1.0, input.GrassTint.x );
		return ShadingModelStandard::Shade( input, material );
		#endif
	}
}
