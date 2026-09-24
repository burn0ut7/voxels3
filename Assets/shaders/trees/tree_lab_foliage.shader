HEADER
{
	Description = "Folded Blender leaves with attached branch and leaf motion";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Rendering" );
	Feature( F_TREE_MOTION, 0..1, "Tree Motion" );
	Feature( F_TREE_LEAF_FACING, 0..1, "Leaf Facing" );
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
	float2 vLeafAxis : TEXCOORD4 < Semantic( LowPrecisionUv2 ); >;
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
	StaticCombo( S_TREE_LEAF_FACING, F_TREE_LEAF_FACING, Sys( ALL ) );
	float4 g_vTreeLeafView < Attribute( "TreeLeafView" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
	float3 g_vTreeLeafViewForward < Attribute( "TreeLeafViewForward" ); Default3( 1.0, 0.0, 0.0 ); >;
	float3 g_vTreeLeafViewUp < Attribute( "TreeLeafViewUp" ); Default3( 0.0, 0.0, 1.0 ); >;
	float g_flTreeLeafMinimumFacing < Default( 0.35 ); Range( 0.05, 0.75 ); >;

	float3 TreeLeafNormalToWorld( float3 normal, float3 x, float3 y, float3 z )
	{
		// Cofactors implement inverse transpose, including mirrored instances.
		float handedness = dot( x, cross( y, z ) ) < 0.0 ? -1.0 : 1.0;
		return normalize( (cross( y, z ) * normal.x + cross( z, x ) * normal.y
			+ cross( x, y ) * normal.z) * handedness );
	}

	void TreeFaceLeaf( inout float3 position, inout float3 normal, inout float3 tangent,
		float3 pivot, float3 bladeNormal, float3 bladeAxis, float3 view, float3 cameraUp, float3 cameraRight,
		float roll, float lean )
	{
		// Use the camera frame, not the projection of the midrib: that projection
		// reverses at axial views. A rigid frame change keeps the authored fold.
		bladeAxis = normalize( bladeAxis );
		bladeNormal = normalize( bladeNormal - bladeAxis * dot( bladeNormal, bladeAxis ) );
		float3 across = cross( bladeAxis, bladeNormal );
		float3 up = cameraUp - view * dot( cameraUp, view );
		if ( dot( up, up ) < 0.000001 )
		{
			up = cameraRight - view * dot( cameraRight, view );
		}
		up = normalize( up );
		float3 targetAxis = TreeRotate( up, view, roll );
		float3 targetAcross = cross( targetAxis, view );
		// Authored variation and flutter follow facing, so facing cannot cancel
		// leaf motion. The bounded lean keeps the blade plane visibly broad.
		float limit = acos( clamp( g_flTreeLeafMinimumFacing, 0.05, 0.75 ) );
		lean = clamp( lean, -limit, limit );
		targetAxis = TreeRotate( targetAxis, targetAcross, lean );
		float3 targetNormal = TreeRotate( view, targetAcross, lean );
		float3 offset = position - pivot;
		position = pivot + targetAcross * dot( offset, across )
			+ targetAxis * dot( offset, bladeAxis ) + targetNormal * dot( offset, bladeNormal );
		normal = targetAcross * dot( normal, across )
			+ targetAxis * dot( normal, bladeAxis ) + targetNormal * dot( normal, bladeNormal );
		tangent = targetAcross * dot( tangent, across )
			+ targetAxis * dot( tangent, bladeAxis ) + targetNormal * dot( tangent, bladeNormal );
	}

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		o.vLeafOcclusion = 1.0;
		#if S_TREE_MOTION || S_TREE_LEAF_FACING
			float3x4 transform = GetTransformMatrix( i.nInstanceTransformID );
			float4 leaf = TreeMotionEntry( i.vTexCoord2 );
			// The visible camera owns leaf pose in color, depth and shadow passes.
			// A shadow camera must not rotate the leaves toward the light.
			float3 leafCamera = g_vTreeLeafView.w > 0.0 ? g_vTreeLeafView.xyz : g_vCameraPositionWs;
			float leafScale = 1.0;
			if ( g_vTreeLeafView.w > 0.0 )
			{
				// Preserve every close leaf. Stable whole-leaf selection avoids
				// cracks between a blade's triangles and agrees across shadow passes.
				float meters = length( g_vTreeLeafView.xyz - mul( transform, float4( leaf.xyz, 1.0 ) ) ) * 0.0254;
				float retention = lerp( 1.0, 0.30, smoothstep( 3.0, 8.0, meters ) );
				float identity = frac( 52.9829189 * frac( dot( i.vTexCoord2, float2( 641.06711, 997.005837 ) ) ) );
				leafScale = saturate( (retention - identity + 0.08) / 0.08 );
				if ( leafScale <= 0.0 )
				{
					o.vPositionPs = float4( 2.0, 2.0, 2.0, 1.0 );
					return o;
				}
			}
			float3 normal;
			float4 tangentAndSign;
			VS_DecodeObjectSpaceNormalAndTangent( i, normal, tangentAndSign );
			float3 tangent = tangentAndSign.xyz;
			float3 position = i.vPositionOs.xyz;
			if ( leafScale < 1.0 ) position = leaf.xyz + (position - leaf.xyz) * leafScale;
			float3 origin = mul( transform, float4( 0.0, 0.0, 0.0, 1.0 ) );
			float3 wind = TreeLocalWind( transform );
			float branchPhase = i.vColor.r * 157.0 + dot( origin.xy, float2( 0.013, 0.021 ) );
			float gust = 0.0;
			#if S_TREE_MOTION
				gust = TreeGust( origin, dot( origin.xy, float2( 0.013, 0.021 ) ) );
			#endif
			float3 leafNormal = TreeDecodeOctahedron( i.vColor.gb );
			float3 flutterAxis = cross( leafNormal, wind );
			if ( dot( flutterAxis, flutterAxis ) < 0.001 )
			{
				flutterAxis = cross( leafNormal, abs( leafNormal.z ) < 0.9
					? float3( 0.0, 0.0, 1.0 ) : float3( 1.0, 0.0, 0.0 ) );
			}
			flutterAxis = normalize( flutterAxis );
			float phase = frac( dot( i.vTexCoord2, float2( 31.173, 79.731 ) ) ) * 6.2831853 + branchPhase;
			float distance = length( leafCamera - mul( transform, float4( leaf.xyz, 1.0 ) ) );
			float detail = 1.0 - smoothstep( 1575.0, 3937.0, distance );
			float flutter = gust * detail * (0.16 * sin( g_flTime * 6.1 + phase )
				+ 0.07 * sin( g_flTime * 10.7 + phase * 1.37 ));
			#if !S_TREE_LEAF_FACING
				position = leaf.xyz + TreeRotate( position - leaf.xyz, flutterAxis, flutter );
				normal = TreeRotate( normal, flutterAxis, flutter );
				tangent = TreeRotate( tangent, flutterAxis, flutter );
			#endif
			TreeBranchMotion( position, normal, tangent, TreeBranchPivot( i.vColor.r ), leaf.w, wind, gust, branchPhase );
			TreeMainMotion( position, normal, tangent, wind, gust );
			#if S_TREE_LEAF_FACING
				float3 bladeAxis = TreeDecodeOctahedron( i.vLeafAxis );
				float authoredLean = leafNormal.z * 0.6;
				float3 pivot = leaf.xyz;
				TreeBranchMotion( pivot, leafNormal, bladeAxis, TreeBranchPivot( i.vColor.r ), leaf.w, wind, gust, branchPhase );
				TreeMainMotion( pivot, leafNormal, bladeAxis, wind, gust );
				float3 x = float3( transform[0][0], transform[1][0], transform[2][0] );
				float3 y = float3( transform[0][1], transform[1][1], transform[2][1] );
				float3 z = float3( transform[0][2], transform[1][2], transform[2][2] );
				// Face in world space so unequal instance scales do not stretch a
				// leaf as the camera moves. The posed pivot remains on its branch.
				position = mul( transform, float4( position, 1.0 ) );
				pivot = mul( transform, float4( pivot, 1.0 ) );
				normal = TreeLeafNormalToWorld( normal, x, y, z );
				leafNormal = TreeLeafNormalToWorld( leafNormal, x, y, z );
				tangent = normalize( mul( (float3x3)transform, tangent ) );
				bladeAxis = normalize( mul( (float3x3)transform, bladeAxis ) );
				float3 cameraForward = g_vTreeLeafView.w > 0.0 ? g_vTreeLeafViewForward : g_vCameraDirWs;
				float3 cameraUp = g_vTreeLeafView.w > 0.0 ? g_vTreeLeafViewUp : g_vCameraUpDirWs;
				float3 delta = leafCamera - pivot;
				if ( dot( delta, delta ) < 0.000001 )
				{
					delta = -cameraForward;
				}
				float3 view = normalize( delta );
				float3 right = cross( cameraForward, cameraUp );
				float roll = frac( dot( i.vTexCoord2, float2( 31.173, 79.731 ) ) ) * 6.2831853;
				TreeFaceLeaf( position, normal, tangent, pivot, leafNormal, bladeAxis, view, cameraUp, right,
					roll, authoredLean + flutter );
				o.vPositionWs = position;
				o.vNormalWs = normalize( normal );
				o.vTangentUWs = normalize( tangent );
				tangentAndSign.w *= dot( x, cross( y, z ) ) < 0.0 ? -1.0 : 1.0;
			#else
				o.vPositionWs = mul( transform, float4( position, 1.0 ) );
				o.vNormalWs = normalize( mul( (float3x3)transform, normal ) );
				o.vTangentUWs = normalize( mul( (float3x3)transform, tangent ) );
			#endif
			o.vPositionPs = Position3WsToPs( o.vPositionWs );
			o.vTangentVWs = normalize( cross( o.vNormalWs, o.vTangentUWs ) * tangentAndSign.w );
			o.vLeafOcclusion = i.vColor.a;
		#endif
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
			float visibility = DirectionalLightShadow::GetVisibility( m.WorldPosition, m.ScreenPosition );
			m.Emission += m.Albedo * g_DirectionalLightColor.rgb * scatter * (0.15 + visibility * 0.85) * 0.22;
		}
		TreeCaptureDepth( m );
		return ShadingModelStandard::Shade( i, m );
	}
}
