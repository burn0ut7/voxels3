HEADER
{
	Description = "Opaque curved terrain grass tufts";
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
		// Five leaves share one surface root. Two triangles join a narrow
		// root, a curved wide middle and a tip, identically in depth and color.
		uint leaf = input.VertexId / 6;
		uint corner = input.VertexId % 6;
		float height = corner == 0 ? 0.0 : (corner == 5 ? 1.0 : 0.45);
		float side = (corner == 1 || corner == 4) ? -1.0 : ((corner == 2 || corner == 3) ? 1.0 : 0.0);
		float variation = frac( shape.w * 7.13 + leaf * 0.618034 );
		float angle = shape.x + leaf * 2.3999632;
		float3 bendAxis = float3( cos( angle ), sin( angle ), 0.0 );
		float3 widthAxis = float3( -bendAxis.y, bendAxis.x, 0.0 );
		float length = shape.y * lerp( 0.65, 1.05, variation );
		float lean = lerp( 0.22, 0.5, frac( variation * 3.71 ) );
		float width = shape.z * lerp( 0.75, 1.15, frac( variation * 5.17 ) );
		float3 position = root.xyz + root.w * (widthAxis * side * width +
			float3( 0.0, 0.0, height * length ) + bendAxis * height * height * length * lean);
		PixelInput output = (PixelInput)0;
		output.vPositionWs = position - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( position );
		// A shared upward lighting bias approximates light scattering through
		// thin leaves without dark, alternating faces across the whole meadow.
		output.vNormalWs = normalize( bendAxis * 0.45 + float3( 0.0, 0.0, 1.0 ) );
		output.GrassTint = float2( height, frac( shape.w + variation * 0.22 ) );
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
		float3 baseColor = lerp( float3( 0.085, 0.19, 0.028 ), float3( 0.17, 0.27, 0.06 ), input.GrassTint.y );
		material.Albedo = baseColor * lerp( 0.55, 1.12, input.GrassTint.x );
		material.Normal = normal;
		material.Roughness = 0.95;
		material.AmbientOcclusion = lerp( 0.8, 1.0, input.GrassTint.x );
		return ShadingModelStandard::Shade( input, material );
		#endif
	}
}
