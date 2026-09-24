HEADER
{
	Description = "Blender tree bark with branch-aligned surface detail";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_TREE_MOTION, 0..1, "Tree Motion" );
	Feature( F_TREE_BAKE_DEPTH, 0..1, "Authoring Depth Capture" );
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
	float vTreeLocalHeight : TEXCOORD13;
	float3 vBarkDetail : TEXCOORD12;
};
VS
{
	#include "common/vertex.hlsl"
	#include "trees/tree_wood_motion.hlsl"
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		o.vTreeLocalHeight = i.vPositionOs.z;
		TreeWoodMotion( i, o );
		o.vBarkDetail = float3( i.vTexCoord2, i.vColor.a );
		return FinalizeVertex( o );
	}
}
PS
{
	BoolAttribute( VertexNeedsPropOrigin, true );
	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"
	#include "trees/tree_lod_fade.hlsl"
	#include "trees/tree_cut.hlsl"
	#include "trees/tree_bake_depth.hlsl"
	CreateInputTexture2D( TextureDetailColor, Srgb, 8, "", "_color", "Bark Detail", Default3( 0.5, 0.5, 0.5 ) );
	CreateInputTexture2D( TextureDetailNormal, Linear, 8, "NormalizeNormals", "_normal", "Bark Detail", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( TextureDetailHeight, Linear, 8, "", "_height", "Bark Detail", Default( 0.5 ) );
	Texture2D g_tDetailHeight < Channel( R, Box( TextureDetailHeight ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tDetailColor < Channel( RGB, Box( TextureDetailColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tDetailNormal < Channel( RGB, Box( TextureDetailNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	float g_flDetailStrength < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Bark Detail" ); >;
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		TreeDetailedFade( i.vPositionSs.xy );
		TreeCut( i.vTreeLocalHeight );
		Material m = Material::From( i );
		// U must retain an integer circumference period at each cylinder seam.
		float2 uv = i.vBarkDetail.xy * float2( 1.0, 0.5 );
		float2 dxUv = ddx( uv );
		float2 dyUv = ddy( uv );
		// Match the source material's smooth offset changes along each branch.
		// SampleGrad keeps mip selection continuous across the offset boundaries.
		float cell = floor( uv.y / 1.37 );
		float blend = smoothstep( 0.65, 1.0, frac( uv.y / 1.37 ) );
		float2 offset0 = frac( cell * float2( 0.61803399, 0.41421356 ) + float2( 0.123, 0.37 ) );
		float2 offset1 = frac( (cell + 1.0) * float2( 0.61803399, 0.41421356 ) + float2( 0.123, 0.37 ) );
		float3 normal = normalize( i.vNormalWs );
		float3 dxPosition = ddx( i.vPositionWithOffsetWs );
		float3 dyPosition = ddy( i.vPositionWithOffsetWs );
		float determinant = dxUv.x * dyUv.y - dxUv.y * dyUv.x;
		float orientation = determinant < 0.0 ? -1.0 : 1.0;
		float3 tangentU = (dxPosition * dyUv.y - dyPosition * dxUv.y) * orientation;
		float3 tangentV = (dyPosition * dxUv.x - dxPosition * dyUv.x) * orientation;
		tangentU -= normal * dot( tangentU, normal );
		tangentV -= normal * dot( tangentV, normal );
		float frameLength = min( dot( tangentU, tangentU ), dot( tangentV, tangentV ) );
		tangentU *= rsqrt( max( dot( tangentU, tangentU ), 1e-12 ) );
		tangentV *= rsqrt( max( dot( tangentV, tangentV ), 1e-12 ) );
		float weight = saturate( i.vBarkDetail.z ) * g_flDetailStrength;
		bool validFrame = frameLength > 1e-12 &&
			determinant * determinant > 1e-8 * dot( dxUv, dxUv ) * dot( dyUv, dyUv );
		float2 uv0 = uv + offset0;
		float2 uv1 = uv + offset1;
		float3 view = g_vCameraPositionWs - m.WorldPosition;
		// The native decal parallax routine uses explicit derivatives. Limit it
		// to nearby bark, fade at collars and disable it beyond eight meters.
		float relief = 0.03 * weight * (1.0 - smoothstep( 80.0, 315.0, length( view ) ));
		if ( validFrame && relief > 0.0001 )
		{
			float3x3 worldToTangent = float3x3( tangentU, tangentV, normal );
			float2 original0 = uv0;
			float2 original1 = uv1;
			ParallaxOcclusion_Grad( uv0, view, worldToTangent, relief, original0, dxUv, dyUv, g_tDetailHeight, TextureFiltering );
			ParallaxOcclusion_Grad( uv1, view, worldToTangent, relief, original1, dxUv, dyUv, g_tDetailHeight, TextureFiltering );
		}
		float3 detailColor = lerp( g_tDetailColor.SampleGrad( TextureFiltering, uv0, dxUv, dyUv ).rgb,
			g_tDetailColor.SampleGrad( TextureFiltering, uv1, dxUv, dyUv ).rgb, blend );
		float3 detailTs = normalize( lerp( DecodeNormal( g_tDetailNormal.SampleGrad( TextureFiltering, uv0, dxUv, dyUv ).xyz ),
			DecodeNormal( g_tDetailNormal.SampleGrad( TextureFiltering, uv1, dxUv, dyUv ).xyz ), blend ) );
		// A collar crosses branch UV charts. Continue its detail in world space
		// so the blend retains grain without exposing a triangle-shaped seam.
		float branchWeight = validFrame ? saturate( i.vBarkDetail.z ) : 0.0;
		float3 collarColor = detailColor;
		float3 collarNormal = normal;
		if ( branchWeight < 0.999 )
		{
			float3 tileScale = float3( 1.0 / 32.0, 1.0 / 32.0, 1.0 / 64.0 );
			float3 position = (i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz) * tileScale;
			float3 dx = dxPosition * tileScale;
			float3 dy = dyPosition * tileScale;
			float3 projection = pow( abs( normal ), 4.0 );
			projection /= max( dot( projection, float3( 1.0, 1.0, 1.0 ) ), 0.001 );
			collarColor = g_tDetailColor.SampleGrad( TextureFiltering, position.yz, dx.yz, dy.yz ).rgb * projection.x
				+ g_tDetailColor.SampleGrad( TextureFiltering, position.xz, dx.xz, dy.xz ).rgb * projection.y
				+ g_tDetailColor.SampleGrad( TextureFiltering, position.xy, dx.xy, dy.xy ).rgb * projection.z;
			float3 nx = DecodeNormal( g_tDetailNormal.SampleGrad( TextureFiltering, position.yz, dx.yz, dy.yz ).xyz );
			float3 ny = DecodeNormal( g_tDetailNormal.SampleGrad( TextureFiltering, position.xz, dx.xz, dy.xz ).xyz );
			float3 nz = DecodeNormal( g_tDetailNormal.SampleGrad( TextureFiltering, position.xy, dx.xy, dy.xy ).xyz );
			nx.y = -nx.y;
			ny.y = -ny.y;
			nz.y = -nz.y;
			float3 slope = float3( 0.0, nx.xy / max( nx.z, 0.1 ) ) * projection.x
				+ float3( ny.x, 0.0, ny.y ) / max( ny.z, 0.1 ) * projection.y
				+ float3( nz.xy / max( nz.z, 0.1 ), 0.0 ) * projection.z;
			collarNormal = normalize( normal + 4.5 * (slope - normal * dot( normal, slope )) );
		}
		// A neutral luminance ratio retains each species' baked color.
		float3 luminance = float3( 0.2126, 0.7152, 0.0722 );
		float average = max( dot( g_tDetailColor.SampleLevel( TextureFiltering, float2( 0.5, 0.5 ), 12.0 ).rgb, luminance ), 0.01 );
		float grain = clamp( dot( lerp( collarColor, detailColor, branchWeight ), luminance ) / average, 0.45, 1.8 );
		m.Albedo *= lerp( 1.0, grain, g_flDetailStrength );
		float height = lerp( g_tDetailHeight.SampleGrad( TextureFiltering, uv0, dxUv, dyUv ).r,
			g_tDetailHeight.SampleGrad( TextureFiltering, uv1, dxUv, dyUv ).r, blend );
		m.AmbientOcclusion *= lerp( 1.0, 0.55 + 0.45 * smoothstep( 0.2, 0.5, height ), weight );
		float3 surfaceDetail = collarNormal;
		if ( validFrame && branchWeight > 0.0 )
		{
			float3 detailWs = TransformNormal( normalize( float3( detailTs.xy * 4.5, detailTs.z ) ), normal, tangentU, tangentV );
			surfaceDetail = normalize( lerp( collarNormal, detailWs, branchWeight ) );
		}
		m.Normal = normalize( lerp( m.Normal, surfaceDetail, 0.9 * g_flDetailStrength ) );
		m.Metalness = 0.0;
		m.Transmission = 0.0;
		m.Opacity = 1.0;
		TreeCaptureDepth( m );
		return ShadingModelStandard::Shade( i, m );
	}
}
