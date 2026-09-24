HEADER
{
	Description = "Tree bark with surface-projected branch junctions";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_BARK_ANIMATION, 0..1, "Bark Animation" );
}
MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "vr_tools_shading_complexity.shader" );
}
COMMON
{
	#include "common/shared.hlsl"
}
struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vParentBark : TEXCOORD12;
};
VS
{
	#include "common/vertex.hlsl"
	#include "common/trunk_bending.hlsl"
	StaticCombo( S_BARK_ANIMATION, F_BARK_ANIMATION, Sys( ALL ) );
#if S_BARK_ANIMATION
	float g_flSwayStrength < Default( 1.0 ); Range( 0.0, 25.0 ); UiGroup( "Bark Animation" ); >;
	float g_flSwaySpeed < Default( 1.0 ); Range( 0.0, 10.0 ); UiGroup( "Bark Animation" ); >;
#endif
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		o.vParentBark = float3( i.vTexCoord2, 1.0 - i.vColor.a );
#if S_BARK_ANIMATION
		float3 position = i.vPositionOs.xyz;
		float3 wind = g_vWindDirection.xyz * g_vWindStrengthFreqMulHighStrength.x;
		ApplyTrunkBending( position, g_flSwayStrength, g_flSwaySpeed, wind, g_flTime );
		float3x4 transform = GetTransformMatrix( i.nInstanceTransformID );
		o.vPositionWs = mul( transform, float4( position, 1.0 ) );
		o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );
#endif
		return FinalizeVertex( o );
	}
}
PS
{
	BoolAttribute( VertexNeedsPropOrigin, true );
	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"
	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );
	float g_flSpecularOcclusion < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Bark" ); >;
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );
		float2 uv = i.vParentBark.xy;
		float2 dxUv = ddx( uv );
		float2 dyUv = ddy( uv );
		float3 dxPosition = ddx( i.vPositionWithOffsetWs );
		float3 dyPosition = ddy( i.vPositionWithOffsetWs );
		float weight = saturate( i.vParentBark.z );
		if ( weight > 0.0 )
		{
			float4 color = g_tColor.SampleGrad( TextureFiltering, uv, dxUv, dyUv );
			float4 packed = g_tRma.SampleGrad( TextureFiltering, uv, dxUv, dyUv );
			float3 normalTs = DecodeNormal( g_tNormal.SampleGrad( TextureFiltering, uv, dxUv, dyUv ).xyz );
			float orientation = dxUv.x * dyUv.y - dxUv.y * dyUv.x < 0.0 ? -1.0 : 1.0;
			float3 normal = normalize( i.vNormalWs );
			float3 parentU = (dxPosition * dyUv.y - dyPosition * dxUv.y) * orientation;
			parentU = normalize( parentU - normal * dot( parentU, normal ) );
			float3 parentV = (dyPosition * dxUv.x - dxPosition * dyUv.x) * orientation;
			parentV = normalize( parentV - normal * dot( parentV, normal ) );
			float3 parentNormal = TransformNormal( normalTs, normal, parentU, parentV );
			m.Albedo = lerp( m.Albedo, color.rgb * g_flTintColor * i.vVertexColor.rgb, weight );
			m.Normal = normalize( lerp( m.Normal, parentNormal, weight ) );
			m.Roughness = lerp( m.Roughness, packed.r, weight );
			m.Metalness = lerp( m.Metalness, packed.g, weight );
			m.AmbientOcclusion = lerp( m.AmbientOcclusion, packed.b, weight );
		}
		float ndv = saturate( dot( m.Normal, normalize( g_vCameraPositionWs - m.WorldPosition ) ) );
		float specular = saturate( pow( ndv + m.AmbientOcclusion, exp2( -16.0 * m.Roughness - 1.0 ) ) - 1.0 + m.AmbientOcclusion );
		m.AmbientOcclusion *= lerp( 1.0, specular, g_flSpecularOcclusion );
		return ShadingModelStandard::Shade( i, m );
	}
}
