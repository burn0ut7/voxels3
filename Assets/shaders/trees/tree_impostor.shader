HEADER
{
	Description = "Depth reprojected imported tree views";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Rendering" );
}
MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}
COMMON
{
	#include "common/shared.hlsl"
	#define TREE_VIEW_PADDING 32.0
}
struct VertexInput
{
	#include "common/vertexinput.hlsl"
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	nointerpolation float4 vFrameInfo : TEXCOORD12;
	float3 vBentPlane : TEXCOORD13;
	nointerpolation float3 vLocalCamera : TEXCOORD14;
	nointerpolation float4 vCenterDiameter : TEXCOORD15;
	noperspective float vPlaneDepth : TEXCOORD16;
};
VS
{
	DynamicCombo( D_TREE_SHADOW, 0..1, Sys( ALL ) );
	#include "common/vertex.hlsl"
	#include "trees/tree_wind.hlsl"
	float3 g_vTreeSourceHalfExtents;
	float g_flTreeAzimuths < Default( 8.0 ); >;
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		float3x4 transform = GetTransformMatrix( i.nInstanceTransformID );
		float3 axisX = float3( transform[0][0], transform[1][0], transform[2][0] );
		float3 axisY = float3( transform[0][1], transform[1][1], transform[2][1] );
		float3 axisZ = float3( transform[0][2], transform[1][2], transform[2][2] );
		float scale = length( axisX );
		axisX /= scale;
		axisY /= scale;
		axisZ /= scale;
		float3 center = mul( transform, float4( i.vPositionOs.xyz, 1.0 ) );
		float diameter = i.vTexCoord2.x;
		float3 cameraDelta = (g_vCameraPositionWs - center) / scale;
		float3 camera = float3( dot( cameraDelta, axisX ), dot( cameraDelta, axisY ), dot( cameraDelta, axisZ ) );
		float3 view = normalize( camera );
		#if D_TREE_SHADOW
			// Shadow projection follows the light, independent of cascade origin.
			view = -float3( dot( g_vCameraDirWs, axisX ), dot( g_vCameraDirWs, axisY ), dot( g_vCameraDirWs, axisZ ) );
		#endif
		float azimuth = frac( atan2( view.y, view.x ) / 6.2831853 + 1.0 ) * g_flTreeAzimuths;
		float elevation = clamp( (asin( clamp( view.z, -1.0, 1.0 ) ) * 57.2957795 + 15.0) / 30.0, 0.0, 3.0 );
		o.vFrameInfo = float4( floor( azimuth ), min( floor( elevation ), 2.0 ), frac( azimuth ), elevation - min( floor( elevation ), 2.0 ) );
		#if D_TREE_SHADOW
			float2 frame = float2( fmod( floor( azimuth + 0.5 ), g_flTreeAzimuths ), floor( elevation + 0.5 ) );
			o.vFrameInfo = float4( frame, 0.0, 0.0 );
			float yaw = frame.x * 6.2831853 / g_flTreeAzimuths;
			float pitch = (frame.y * 30.0 - 15.0) * 0.0174532925;
			view = float3( cos( pitch ) * cos( yaw ), cos( pitch ) * sin( yaw ), sin( pitch ) );
		#endif
		float3 right = cross( float3( 0.0, 0.0, 1.0 ), view );
		if ( dot( right, right ) < 0.0001 )
		{
			right = float3( 0.0, 1.0, 0.0 );
		}
		right = normalize( right );
		float3 up = normalize( cross( view, right ) );
		float3 halfSize = g_vTreeSourceHalfExtents;
		#if D_TREE_SHADOW
			// The directional light uses an orthographic silhouette.
			float2 planeSize = 2.0 * float2( dot( abs( right ), halfSize ), dot( abs( up ), halfSize ) ) + 2.0 * TREE_VIEW_PADDING;
			float2 planeUv = (i.vTexCoord.xy - 0.5) * planeSize / diameter;
		#else
			// Project the complete padded box onto the billboard's center plane.
			// Orthographic extents clip branches in front of that plane: perspective
			// enlarges them before the pixel shader reconstructs their depth.
			float distance = max( length( camera ), 1.0 );
			float2 minimum = float2( 1e10, 1e10 );
			float2 maximum = float2( -1e10, -1e10 );
			for ( int cornerIndex = 0; cornerIndex < 8; cornerIndex++ )
			{
				float3 corner = (halfSize + TREE_VIEW_PADDING) * float3(
					(cornerIndex & 1) == 0 ? -1.0 : 1.0,
					(cornerIndex & 2) == 0 ? -1.0 : 1.0,
					(cornerIndex & 4) == 0 ? -1.0 : 1.0 );
				float perspective = distance / max( distance - dot( corner, view ), 1.0 );
				float2 projected = float2( dot( corner, right ), -dot( corner, up ) ) * perspective;
				minimum = min( minimum, projected );
				maximum = max( maximum, projected );
			}
			float2 planeUv = lerp( minimum, maximum, i.vTexCoord.xy ) / diameter;
		#endif
		float3 plane = (right * planeUv.x - up * planeUv.y) * diameter;
		o.vTextureCoords.xy = planeUv + 0.5;
		o.vLocalCamera = camera / diameter;
		float3 position = i.vPositionOs.xyz + plane;
		float3 origin = mul( transform, float4( 0.0, 0.0, 0.0, 1.0 ) );
		float3 normal = view;
		float3 tangent = right;
		TreeMainMotion( position, normal, tangent, TreeLocalWind( transform ),
			TreeGust( origin, dot( origin.xy, float2( 0.013, 0.021 ) ) ) );
		o.vBentPlane = (position - i.vPositionOs.xyz) / diameter;
		o.vPositionWs = mul( transform, float4( position, 1.0 ) );
		o.vPositionPs = Position3WsToPs( o.vPositionWs );
		o.vCenterDiameter = float4( center, diameter * scale );
		o.vTangentUWs = axisX;
		o.vTangentVWs = axisY;
		o.vNormalWs = axisZ;
		o = FinalizeVertex( o );
		o.vPlaneDepth = o.vPositionPs.z / o.vPositionPs.w;
		#if !D_TREE_SHADOW
		// Native per-sample early rejection uses a bound in front of the
		// complete padded tree; reconstruction keeps the original view ray.
		float3 localForward = float3( dot( g_vCameraDirWs, axisX ),
			dot( g_vCameraDirWs, axisY ), dot( g_vCameraDirWs, axisZ ) );
		float radius = (dot( abs( localForward ), halfSize + TREE_VIEW_PADDING ) + TREE_VIEW_PADDING) * scale;
		float4 front = Position3WsToPs( center - g_vCameraDirWs * radius );
		float frontDepth = front.w > 0.0 ? saturate( front.z / front.w ) : 1.0;
		o.vPositionPs.z = max( o.vPositionPs.z, frontDepth * o.vPositionPs.w );
		#endif
		return o;
	}
}
PS
{
	DynamicCombo( D_TREE_SHADOW, 0..1, Sys( ALL ) );
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Tree", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureCoverage, Linear, 8, "", "_coverage", "Tree", Default( 1.0 ) );
	CreateInputTexture2D( TextureObjectNormal, Linear, 8, "", "_normal", "Tree", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( TextureDepth, Linear, 8, "", "_depth", "Tree", Default( 0.5 ) );
	CreateInputTexture2D( TextureOcclusion, Linear, 8, "", "_occlusion", "Tree", Default( 1.0 ) );
	Texture2D g_tColor < Channel( RGB, AlphaWeighted( TextureColor, TextureCoverage ), Srgb ); Channel( A, Box( TextureCoverage ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tObjectNormal < Channel( RGB, Box( TextureObjectNormal ), Linear ); Channel( A, Box( TextureDepth ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;
	Texture2D g_tTreeOcclusion < Channel( RGB, Box( TextureOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	#define MATERIAL_ALPHA_TEXTURE g_tColor
	#include "common/pixel.hlsl"
	#include "trees/tree_lod_fade.hlsl"
	#include "trees/tree_wind.hlsl"
	SamplerState TreeSampler < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	RenderState( CullMode, NONE );
	#if S_MODE_DEPTH == 0
		RenderState( DepthWriteEnable, false );
	#endif
	BoolAttribute( VertexNeedsPropOrigin, true );
	float g_flTreeTilePixels < Default( 512.0 ); >;
	float3 g_vTreeFrameCenter;
	float3 g_vTreeSourceHalfExtents;
	float g_flTreeAzimuths < Default( 8.0 ); >;
	float g_flTreeDiameter;
	struct TreePixelOutput
	{
		float4 Color : SV_Target0;
		#if D_TREE_SHADOW
			precise float Depth : SV_Depth;
		#else
			precise float Depth : SV_DepthLessEqual;
		#endif
	};

	void ReadTreeView( float2 frame, float3 camera, float3 plane, float lod, float3 wind, float gust,
		out float4 color, out float2 finalUv, out float3 surface )
	{
		float azimuth = frame.x * 6.2831853 / g_flTreeAzimuths;
		float elevation = (frame.y * 30.0 - 15.0) * 0.0174532925;
		float3 direction = float3( cos( elevation ) * cos( azimuth ), cos( elevation ) * sin( azimuth ), sin( elevation ) );
		float3 right = float3( -sin( azimuth ), cos( azimuth ), 0.0 );
		float3 up = cross( direction, right );
		float3 ray = normalize( plane - camera );
		float denominator = min( dot( ray, direction ), -0.05 );
		float travel = -dot( camera, direction ) / denominator;
		float2 uv = float2( 0.5, 0.5 );
		float2 atlas = float2( g_flTreeAzimuths, 4.0 );
		// Clamp to the coarser trilinear level's texel centers. Tile dimensions
		// are powers of two and lod is capped before mip chains share a texel.
		float border = 0.5 * exp2( ceil( lod ) ) / g_flTreeTilePixels;
		// Keep full refinement near the handoff; at six tree diameters
		// the coarse view needs only its first perspective depth correction.
		int iterations = dot( camera, camera ) >= 36.0 ? 1 : 3;
		for ( int iteration = 0; iteration < iterations; iteration++ )
		{
			float3 bent = g_vTreeFrameCenter + (camera + ray * travel) * g_flTreeDiameter;
			float3 point = (TreeInverseMainMotion( bent, wind, gust ) - g_vTreeFrameCenter) / g_flTreeDiameter;
			uv = float2( dot( point, right ), -dot( point, up ) ) + 0.5;
			float2 sampleUv = (clamp( uv, border, 1.0 - border ) + frame) / atlas;
			float height = g_tObjectNormal.SampleLevel( TreeSampler, sampleUv, lod ).a - 0.5;
			travel += (height - dot( point, direction )) / denominator;
		}
		surface = camera + ray * travel;
		float3 rest = (TreeInverseMainMotion( g_vTreeFrameCenter + surface * g_flTreeDiameter,
			wind, gust ) - g_vTreeFrameCenter) / g_flTreeDiameter;
		uv = float2( dot( rest, right ), -dot( rest, up ) ) + 0.5;
		finalUv = (clamp( uv, border, 1.0 - border ) + frame) / atlas;
		color = g_tColor.SampleLevel( TreeSampler, finalUv, lod );
		color.a *= step( 0.0, min( uv.x, uv.y ) ) * step( max( uv.x, uv.y ), 1.0 );

	}

	TreePixelOutput MainPs( PixelInput i )
	{
		#if D_TREE_SHADOW
			clip( TreePixelDither( i.vPositionSs.xy ) - g_flTreeLodFade );
			float2 dx = ddx( i.vTextureCoords.xy ) * g_flTreeTilePixels;
			float2 dy = ddy( i.vTextureCoords.xy ) * g_flTreeTilePixels;
			float lod = clamp( 0.5 * log2( max( max( dot( dx, dx ), dot( dy, dy ) ), 1.0 ) ),
				0.0, log2( g_flTreeTilePixels ) - 2.0 );
			float border = 0.5 * exp2( ceil( lod ) ) / g_flTreeTilePixels;
			float2 uv = (clamp( i.vTextureCoords.xy, border, 1.0 - border ) + i.vFrameInfo.xy) / float2( g_flTreeAzimuths, 4.0 );
			Material m = Material::Init( i );
			m.TextureCoords = i.vTextureCoords.xy / float2( g_flTreeAzimuths, 4.0 );
			m.Opacity = g_tColor.SampleLevel( TreeSampler, uv, lod ).a;
			m.Opacity *= step( 0.0, min( i.vTextureCoords.x, i.vTextureCoords.y ) )
				* step( max( i.vTextureCoords.x, i.vTextureCoords.y ), 1.0 );
			AdjustAlphaToCoverage( m );
			TreePixelOutput output;
			output.Color = float4( 0.0, 0.0, 0.0, m.Opacity );
			// Use the captured surface depth, not a flat plane through the crown.
			float yaw = i.vFrameInfo.x * 6.2831853 / g_flTreeAzimuths;
			float pitch = (i.vFrameInfo.y * 30.0 - 15.0) * 0.0174532925;
			float3 direction = float3( cos( pitch ) * cos( yaw ), cos( pitch ) * sin( yaw ), sin( pitch ) );
			float3 right = float3( -sin( yaw ), cos( yaw ), 0.0 );
			float3 up = cross( direction, right );
			float height = g_tObjectNormal.SampleLevel( TreeSampler, uv, lod ).a - 0.5;
			float3 local = g_vTreeFrameCenter + g_flTreeDiameter *
				(right * (i.vTextureCoords.x - 0.5) - up * (i.vTextureCoords.y - 0.5) + direction * height);
			float scale = i.vCenterDiameter.w / max( g_flTreeDiameter, 1.0 );
			float3 origin = i.vCenterDiameter.xyz - scale * (i.vTangentUWs * g_vTreeFrameCenter.x
				+ i.vTangentVWs * g_vTreeFrameCenter.y + i.vNormalWs * g_vTreeFrameCenter.z);
			float3 wind = g_vTreeWind.xyz / max( length( g_vTreeWind.xyz ), 0.0001 );
			wind = float3( dot( wind, i.vTangentUWs ), dot( wind, i.vTangentVWs ), dot( wind, i.vNormalWs ) );
			TreeMainMotion( local, direction, right, wind,
				TreeGust( origin, dot( origin.xy, float2( 0.013, 0.021 ) ) ) );
			float3 world = origin + scale * (i.vTangentUWs * local.x + i.vTangentVWs * local.y + i.vNormalWs * local.z);
			float4 shadowPosition = Position3WsToPs( world );
			output.Depth = lerp( g_flViewportMinZ, g_flViewportMaxZ, shadowPosition.z / shadowPosition.w );
			return output;
		#else
		float rasterDepth = i.vPositionSs.z;
		// Preserve the original depth input to material lighting.
		i.vPositionSs.z = lerp( g_flViewportMinZ, g_flViewportMaxZ, i.vPlaneDepth );
		// Complement the detailed model's exact screen mask through the fade.
		clip( TreePixelDither( i.vPositionSs.xy ) - g_flTreeLodFade );
		float2 dx = ddx( i.vTextureCoords.xy ) * g_flTreeTilePixels;
		float2 dy = ddy( i.vTextureCoords.xy ) * g_flTreeTilePixels;
		float lod = clamp( 0.5 * log2( max( max( dot( dx, dx ), dot( dy, dy ) ), 1.0 ) ),
			0.0, log2( g_flTreeTilePixels ) - 2.0 );
		float3 axisX = i.vTangentUWs;
		float3 axisY = i.vTangentVWs;
		float3 axisZ = i.vNormalWs;
		float scale = i.vCenterDiameter.w / max( g_flTreeDiameter, 1.0 );
		float3 origin = i.vCenterDiameter.xyz - (axisX * g_vTreeFrameCenter.x
			+ axisY * g_vTreeFrameCenter.y + axisZ * g_vTreeFrameCenter.z) * scale;
		float3 wind = g_vTreeWind.xyz / max( length( g_vTreeWind.xyz ), 0.0001 );
		wind = float3( dot( wind, axisX ), dot( wind, axisY ), dot( wind, axisZ ) );
		float gust = TreeGust( origin, dot( origin.xy, float2( 0.013, 0.021 ) ) );
		// Most angles need one capture; blend smoothly only near view boundaries.
		float2 blend = smoothstep( 0.38, 0.62, i.vFrameInfo.zw );
		// Keep depth and color reconstruction identical. Directional shadow
		// cameras are orthographic; the perspective depth prepass is not.
		if ( abs( g_matViewToProjection[3].w ) > 0.5 )
		{
			blend = step( 0.5, i.vFrameInfo.zw );
		}
		float4 combined = float4( 0.0, 0.0, 0.0, 0.0 );
		float3 surface = float3( 0.0, 0.0, 0.0 );
		float3 normal = float3( 0.0, 0.0, 0.0 );
		float occlusion = 0.0;
		for ( int row = 0; row < 2; row++ )
		{
			for ( int column = 0; column < 2; column++ )
			{
				float weight = (column == 0 ? 1.0 - blend.x : blend.x)
					* (row == 0 ? 1.0 - blend.y : blend.y);
				if ( weight <= 0.0 )
				{
					continue;
				}
				float2 frame = float2( fmod( i.vFrameInfo.x + column, g_flTreeAzimuths ), i.vFrameInfo.y + row );
				float4 sampleColor;
				float2 sampleUv;
				float3 sampleSurface;
				ReadTreeView( frame, i.vLocalCamera, i.vBentPlane, lod, wind, gust, sampleColor, sampleUv, sampleSurface );
				combined.rgb += sampleColor.rgb * sampleColor.a * weight;
				combined.a += sampleColor.a * weight;
				surface += sampleSurface * sampleColor.a * weight;
				// Filter subpixel leaves and blend views continuously. Choosing a
				// full-resolution normal per screen pixel made wind sparkle.
				float3 sampleNormal = g_tObjectNormal.SampleLevel( TreeSampler, sampleUv, lod ).rgb * 2.0 - 1.0;
				normal += sampleNormal * sampleColor.a * weight;
				occlusion += g_tTreeOcclusion.SampleLevel( TreeSampler, sampleUv, lod ).r * sampleColor.a * weight;
			}
		}
		#if S_ALPHA_TEST
			clip( combined.a - 1.0 / 255.0 );
		#endif
		float3 local = g_vTreeFrameCenter + surface / max( combined.a, 0.001 ) * g_flTreeDiameter;
		float3 world = origin + (axisX * local.x + axisY * local.y + axisZ * local.z) * scale;
		i.vPositionWithOffsetWs = world - g_vHighPrecisionLightingOffsetWs.xyz;
		Material m = Material::Init( i );
		m.TextureCoords = i.vTextureCoords.xy / float2( g_flTreeAzimuths, 4.0 );
		m.Opacity = combined.a;
		m.Roughness = 0.85;
		m.Metalness = 0.0;
		m.Transmission = 0.12;
		TreePixelOutput output;
		normal /= max( length( normal ), 0.001 );
		float3 tangent = float3( 0.0, 1.0, 0.0 );
		float3 rest = TreeInverseMainMotion( local, wind, gust );
		TreeMainMotion( rest, normal, tangent, wind, gust );
		m.Normal = normalize( axisX * normal.x + axisY * normal.y + axisZ * normal.z );
		m.Albedo = combined.rgb / max( combined.a, 0.001 ) * i.vVertexColor.rgb;
		m.AmbientOcclusion = occlusion / max( combined.a, 0.001 );
		i.vNormalWs = m.Normal;
		output.Color = ShadingModelStandard::Shade( i, m );
		float4 clipPosition = Position3WsToPs( world );
		output.Depth = lerp( g_flViewportMinZ, g_flViewportMaxZ, clipPosition.z / clipPosition.w );
		output.Depth = min( output.Depth, rasterDepth );
		return output;
		#endif
	}
}
