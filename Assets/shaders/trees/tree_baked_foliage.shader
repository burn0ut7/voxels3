HEADER
{
	Description = "Branch-local baked leaf patches with shared wind and flutter";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Rendering" );
	Feature( F_TREE_MOTION, 0..1, "Tree Motion" );
	Feature( F_TREE_BAKE_DEPTH, 0..1, "Authoring Depth Capture" );
}
MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}
COMMON
{
	#include "common/shared.hlsl"
}
struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
	float2 vPatchLocal : TEXCOORD4 < Semantic( LowPrecisionUv2 ); >;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float vLeafOcclusion : TEXCOORD12;
};
VS
{
	#include "common/vertex.hlsl"
	#include "trees/tree_wind.hlsl"
	StaticCombo( S_TREE_MOTION, F_TREE_MOTION, Sys( ALL ) );
	float4 g_vTreeLeafView < Attribute( "TreeLeafView" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
	float3 g_vTreeLeafViewForward < Attribute( "TreeLeafViewForward" ); Default3( 1.0, 0.0, 0.0 ); >;
	float3 g_vTreeLeafViewUp < Attribute( "TreeLeafViewUp" ); Default3( 0.0, 0.0, 1.0 ); >;
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		o.vLeafOcclusion = i.vColor.a;
		float3x4 transform = GetTransformMatrix( i.nInstanceTransformID );
		float4 anchor = TreeMotionEntry( i.vTexCoord2 );
		float3 normal;
		float4 tangentAndSign;
		VS_DecodeObjectSpaceNormalAndTangent( i, normal, tangentAndSign );
		float3 tangent = tangentAndSign.xyz;
		float3 position = i.vPositionOs.xyz;
		float3 origin = mul( transform, float4( 0.0, 0.0, 0.0, 1.0 ) );
		float3 wind = TreeLocalWind( transform );
		float phase = dot( origin.xy, float2( 0.013, 0.021 ) );
		float gust = 0.0;
		#if S_TREE_MOTION
			gust = TreeGust( origin, phase );
		#endif
		float branchPhase = i.vColor.r * 157.0 + phase;
		float3 branchPivot = TreeBranchPivot( i.vColor.r );
		float patchPhase = frac( dot( i.vTexCoord2, float2( 31.173, 79.731 ) ) ) * 6.2831853;
		// Subdivided cards ripple internally; their edges stay on the moving
		// branch patch. Gust zero is exactly the authored resting geometry.
		float flutter = gust * anchor.w * sin( g_flTime * 6.1 + patchPhase + branchPhase );
		float2 interior = sin( saturate( i.vPatchLocal ) * 3.14159265 );
		position += normal * (interior.x * interior.y * flutter * 0.65);
		TreeBranchMotion( position, normal, tangent, branchPivot, anchor.w, wind, gust, branchPhase );
		TreeMainMotion( position, normal, tangent, wind, gust );
		float3 pivot = anchor.xyz;
		float3 patchNormal = float3( 0.0, 0.0, 1.0 );
		float3 patchAcross = float3( 1.0, 0.0, 0.0 );
		TreeBranchMotion( pivot, patchNormal, patchAcross, branchPivot, anchor.w, wind, gust, branchPhase );
		TreeMainMotion( pivot, patchNormal, patchAcross, wind, gust );
		float3 x = float3( transform[0][0], transform[1][0], transform[2][0] );
		float3 y = float3( transform[0][1], transform[1][1], transform[2][1] );
		float3 z = float3( transform[0][2], transform[1][2], transform[2][2] );
		float handedness = dot( x, cross( y, z ) ) < 0.0 ? -1.0 : 1.0;
		patchNormal = normalize( (cross( y, z ) * patchNormal.x + cross( z, x ) * patchNormal.y
			+ cross( x, y ) * patchNormal.z) * handedness );
		patchAcross = normalize( mul( (float3x3)transform, patchAcross ) );
		float3 patchUp = normalize( cross( patchNormal, patchAcross ) );
		position = mul( transform, float4( position, 1.0 ) );
		pivot = mul( transform, float4( pivot, 1.0 ) );
		// The actual render camera owns the pose in color, depth and shadows.
		// Each small patch faces it without changing its moving 3D anchor.
		float3 camera = g_vTreeLeafView.w > 0.0 ? g_vTreeLeafView.xyz : g_vCameraPositionWs;
		float3 forward = g_vTreeLeafView.w > 0.0 ? g_vTreeLeafViewForward : g_vCameraDirWs;
		float3 up = g_vTreeLeafView.w > 0.0 ? g_vTreeLeafViewUp : g_vCameraUpDirWs;
		float3 delta = camera - pivot;
		float3 view = dot( delta, delta ) > 0.000001 ? normalize( delta ) : -forward;
		up -= view * dot( up, view );
		if ( dot( up, up ) < 0.000001 ) up = cross( forward, g_vCameraUpDirWs );
		up = TreeRotate( normalize( up ), view, flutter * 0.035 );
		float3 across = cross( up, view );
		float3 offset = position - pivot;
		o.vPositionWs = pivot + across * dot( offset, patchAcross ) + up * dot( offset, patchUp )
			+ view * dot( offset, patchNormal );
		o.vPositionPs = Position3WsToPs( o.vPositionWs );
		o.vNormalWs = view;
		o.vTangentUWs = across;
		o.vTangentVWs = up * tangentAndSign.w * handedness;
		return FinalizeVertex( o );
	}
}
PS
{
	BoolAttribute( VertexNeedsPropOrigin, true );
	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	RenderState( CullMode, NONE );
	#if S_MODE_DEPTH == 0
		RenderState( DepthFunc, EQUAL );
		RenderState( DepthWriteEnable, false );
	#endif
	#include "trees/tree_lod_fade.hlsl"
	#include "trees/tree_bake_depth.hlsl"
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 shadowReceiverNormal = ComputeShadowReceiverNormal( i.vPositionWithOffsetWs );
		TreeDetailedFade( i.vPositionSs.xy );
		Material m = Material::From( i );
		#if S_ALPHA_TEST
			clip( m.Opacity - 1.0 / 255.0 );
		#endif
		m.Roughness = max( m.Roughness, 0.65 );
		m.Metalness = 0.0;
		m.Transmission = 0.12;
		m.AmbientOcclusion *= i.vLeafOcclusion;
		m.Albedo *= 0.75 + 0.25 * i.vLeafOcclusion;
		if ( g_DirectionalLightEnabled )
		{
			float3 light = -normalize( g_DirectionalLightDirection.xyz );
			float3 view = normalize( g_vCameraPositionWs - m.WorldPosition );
			float scatter = pow( saturate( dot( view, -normalize( light + m.Normal * 0.2 ) ) ), 3.0 );
			float visibility = DirectionalLightShadow::GetVisibility( m.WorldPosition, shadowReceiverNormal, m.ScreenPosition );
			m.Emission += m.Albedo * g_DirectionalLightColor.rgb * scatter * (0.15 + visibility * 0.85) * 0.22;
		}
		TreeCaptureDepth( m );
		return ShadingModelStandard::Shade( i, m );
	}
}
