HEADER
{
	Description = "Depth-tinted water with generated downstream flow";
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
	#define S_TRANSLUCENT 1
	#include "common/shared.hlsl"
}
struct VertexInput
{
	float3 Position : POSITION < Semantic( None ); >;
	float4 Coverage : TEXCOORD0 < Semantic( None ); >;
	float2 FlowOrigin : TEXCOORD1 < Semantic( None ); >;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float CoverageClip[4] : SV_ClipDistance0;
	nointerpolation float4 Coverage : TEXCOORD12;
	nointerpolation float2 FlowOrigin : TEXCOORD13;
};
VS
{
	PixelInput MainVs( const VertexInput input )
	{
		PixelInput output = (PixelInput)0;
		output.vPositionWs = input.Position - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( input.Position );
		output.vNormalWs = float3( 0.0, 0.0, 1.0 );
		output.CoverageClip[0] = input.Position.x - input.Coverage.x;
		output.CoverageClip[1] = input.Position.y - input.Coverage.y;
		output.CoverageClip[2] = input.Coverage.z - input.Position.x;
		output.CoverageClip[3] = input.Coverage.w - input.Position.y;
		output.Coverage = input.Coverage;
		output.FlowOrigin = input.FlowOrigin;
		return output;
	}
}
PS
{
	#define BLEND_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "common/classes/Depth.hlsl"
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, INV_SRC_ALPHA );
	Texture2D WaterSceneColor < Attribute( "WaterSceneColor" ); SrgbRead( false ); >;
	Texture2D WaterFlow < Attribute( "WaterFlow" ); SrgbRead( false ); >;
	float3 WaterTint < Attribute( "WaterTint" ); >;
	float3 WaterSkyTint < Attribute( "WaterSkyTint" ); >;
	float WaterVisibility < Attribute( "WaterVisibility" ); >;
	float WaterFlowSpeed < Attribute( "WaterFlowSpeed" ); >;
	float WaterRippleStrength < Attribute( "WaterRippleStrength" ); >;
	float WaterFlowSamples < Attribute( "WaterFlowSamples" ); >;
	float WaterFlowTextureSize < Attribute( "WaterFlowTextureSize" ); >;

	float RippleHash( int2 cell )
	{
		// Adjacent cells must hash their shared corner identically. Integer
		// mixing avoids floating multiply/add reassociation changing that corner
		// between the four inlined calls, especially far from the world origin.
		uint2 lattice = asuint( cell );
		uint value = lattice.x * 1597334677u ^ lattice.y * 3812015801u;
		value ^= value >> 16;
		value *= 2246822519u;
		value ^= value >> 13;
		value *= 3266489917u;
		value ^= value >> 16;
		return (value >> 8) * (1.0 / 16777216.0);
	}

	// Value and analytic slope: no normal texture or per-frame CPU updates.
	float3 RippleNoise( float2 position )
	{
		int2 cell = (int2)floor( position );
		float2 f = position - float2( cell );
		float2 u = f * f * (3.0 - 2.0 * f);
		float2 du = 6.0 * f * (1.0 - f);
		float a = RippleHash( cell );
		float b = RippleHash( cell + int2( 1, 0 ) );
		float c = RippleHash( cell + int2( 0, 1 ) );
		float d = RippleHash( cell + int2( 1, 1 ) );
		float mixed = a - b - c + d;
		return float3( a + (b - a) * u.x + (c - a) * u.y + mixed * u.x * u.y,
			du * (float2( b - a, c - a ) + mixed * u.yx) );
	}

	float2 RippleLayer( float2 position )
	{
		// Warped, rotated scales avoid a repeating grid or parallel wave bands.
		// Each normal octave fades once its world-inch scale is unresolved.
		float footprint = max( length( ddx( position ) ), length( ddy( position ) ) );
		float2 warped = position + RippleNoise( position / 160.0 ).yz * 18.0;
		float2 rotated = float2( warped.x * 0.8 - warped.y * 0.6, warped.x * 0.6 + warped.y * 0.8 );
		float2 small = RippleNoise( rotated / 11.0 + 37.1 ).yz;
		small = float2( small.x * 0.8 + small.y * 0.6, -small.x * 0.6 + small.y * 0.8 );
		return RippleNoise( warped / 27.0 ).yz * (1.0 - smoothstep( 6.0, 20.0, footprint ))
			+ small * 0.35 * (1.0 - smoothstep( 2.0, 8.0, footprint ))
			+ RippleNoise( (warped + 73.9) / 63.0 ).yz * 0.5;
	}

	struct WaterReflectionHit
	{
		float3 HitClipSpace;
		float Confidence;
		bool ValidHit;
	};

	WaterReflectionHit TraceWaterReflection( float3 position, float3 direction )
	{
		WaterReflectionHit result = (WaterReflectionHit)0;
		float4 clip = Position3WsToPs( position );
		float4 delta = Position4WsToPs( float4( direction, 0.0 ) );
		float3 origin = clip.xyz / clip.w;
		float3 ray = delta.xyz - origin * delta.w;
		origin.xy = origin.xy * float2( 0.5, -0.5 ) + 0.5;
		ray.xy *= float2( 0.5, -0.5 );
		ray /= max( max( abs( ray.x ), abs( ray.y ) ), 1e-10 );
		float2 axisSign = ray.xy < 0.0 ? -1.0 : 1.0;
		float2 inverseRay = axisSign / max( abs( ray.xy ), 1e-10 );
		float epsilon = 0.05 * min( g_vInvViewportSize.x, g_vInvViewportSize.y );
		float3 point = origin + ray * epsilon;
		int mip = 2;
		uint width, height, levels;
		g_tDepthChain.GetDimensions( 0, width, height, levels );
		float thickness = sqrt( length( position - g_vCameraPositionWs ) );
		// Descend before accepting any depth crossing, and sample the same cell
		// whose bounds we traverse. Subtract before dividing to retain precision.
		for ( uint step = 0; step < 64; step++ )
		{
			if ( any( point.xyz <= 0.0 ) || any( point.xyz >= 1.0 ) )
			{
				break;
			}
			float2 resolution = float2( max( uint2( width, height ) >> mip, 1u ) );
			int2 cell = int2( point.xy * resolution );
			float depth = Depth::Normalize( g_tDepthChain.Load( int3( cell, mip ) ).y );
			float2 boundary = (float2( cell ) + (ray.xy >= 0.0 ? 1.0 : 0.0)) / resolution;
			float2 crossing = (boundary - point.xy) * inverseRay;
			float advance = max( min( crossing.x, crossing.y ), epsilon );
			float nextDepth = point.z + ray.z * advance;
			bool empty = depth <= 0.0 || min( point.z, nextDepth ) > depth;
			if ( !empty && mip > 0 )
			{
				mip--;
				continue;
			}
			if ( !empty )
			{
				float intersection = abs( ray.z ) > 1e-12 ? (depth - point.z) / ray.z : 0.0;
				float3 hit = point + ray * clamp( intersection, 0.0, advance );
				float sceneDistance = Depth::Linearize( depth, hit.xy * g_vViewportSize );
				float hitDistance = Depth::Linearize( hit.z, hit.xy * g_vViewportSize );
				float confidence = 1.0 - smoothstep( 0.0, thickness, abs( sceneDistance - hitDistance ) );
				float3 surface = Depth::GetWorldPosition( hit.xy * g_vViewportSize );
				if ( confidence > 0.0 && surface.z > position.z - 2.0 )
				{
					float2 borderSize = 0.05 * float2( g_vViewportSize.y / g_vViewportSize.x, 1.0 );
					float2 border = smoothstep( 0.0, borderSize, hit.xy ) * (1.0 - smoothstep( 1.0 - borderSize, 1.0, hit.xy ));
					result.HitClipSpace = hit;
					result.Confidence = confidence * confidence * border.x * border.y;
					result.ValidHit = true;
					return result;
				}
			}
			point += ray * (advance + epsilon);
			mip = min( mip + 1, min( 8, (int)levels - 1 ) );
		}
		return result;
	}

	float4 MainPs( PixelInput input ) : SV_Target0
	{
		float3 worldPosition = input.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float2 uv = saturate( (worldPosition.xy - input.Coverage.xy) / (input.Coverage.zw - input.Coverage.xy) );
		float samples = max( WaterFlowSamples, 1.0 );
		// Clamp to this tile's boundary texel centers before atlas addressing.
		// This retains the standalone texture's bilinear clamp at shared edges.
		uv = (input.FlowOrigin + uv * (samples - 1.0) + 0.5) / WaterFlowTextureSize;
		float3 appearance = WaterFlow.SampleLevel( g_sBilinearClamp, uv, 0 ).rgb;
		float2 downstream = WaterFlowTextureSize > 1.0 ? (appearance.rg * 255.0 - 128.0) / 127.0 : float2( 0.0, 0.0 );
		float river = saturate( length( downstream ) );
		float3 bottom = Depth::GetWorldPosition( input.vPositionSs.xy );
		float thickness = length( bottom - worldPosition );
		float submerged = max( 0.0, worldPosition.z - bottom.z );
		// World-space habitat remains stable as the camera turns. Retain dry
		// bank samples around small pools and preserve strong river advection.
		float marsh = appearance.b * (1.0 - smoothstep( 0.0, 0.25, river ));
		float2 velocity = (downstream + float2( 0.12, -0.08 ) * (1.0 - river)) * WaterFlowSpeed * (1.0 - marsh);
		// Two half-cycle-offset phases fade out before resetting to avoid stretching
		// the ripple field indefinitely around bends, or a visible periodic snap.
		const float flowCycleSeconds = 6.0;
		float phase = frac( g_flTime / flowCycleSeconds );
		float secondPhase = frac( phase + 0.5 );
		float blend = abs( phase * 2.0 - 1.0 );
		float2 ripple = lerp( RippleLayer( worldPosition.xy - velocity * phase * flowCycleSeconds ),
			RippleLayer( worldPosition.xy - velocity * secondPhase * flowCycleSeconds ), blend );
		float pixelFootprint = max( length( ddx( worldPosition.xy ) ), length( ddy( worldPosition.xy ) ) );
		float detail = 1.0 - smoothstep( 24.0, 96.0, pixelFootprint );
		float3 normal = normalize( float3( -ripple * WaterRippleStrength * detail * lerp( 1.0, 0.15, marsh ), 1.0 ) );
		float3 view = normalize( g_vCameraPositionWs - worldPosition );
		if ( view.z < 0.0 )
		{
			normal = -normal;
		}
		float fresnel = 0.02 + 0.98 * pow( 1.0 - saturate( dot( normal, view ) ), 5.0 );
		float visibility = lerp( WaterVisibility, min( WaterVisibility, 18.0 ), marsh );
		float absorption = 1.0 - exp2( -thickness / max( visibility, 1.0 ) );
		float shore = view.z >= 0.0 ? saturate( submerged / 3.0 ) : 1.0;
		float2 screenUv = input.vPositionSs.xy * g_vInvViewportSize;
		// Project a short perturbed transmission ray. Reject samples crossing the
		// bank or foreground so refraction cannot pull dry terrain over the water.
		float3 refracted = worldPosition - view * min( thickness, 160.0 )
			+ float3( normal.xy, 0.0 ) * min( submerged, 80.0 ) * 0.5;
		float4 refractionClip = Position3WsToPs( refracted );
		float2 refractionUv = refractionClip.xy / refractionClip.w * float2( 0.5, -0.5 ) + 0.5;
		refractionUv = clamp( refractionUv, g_vInvViewportSize, 1.0 - g_vInvViewportSize );
		float3 refractedBottom = Depth::GetWorldPosition( refractionUv * g_vViewportSize );
		if ( refractedBottom.z >= worldPosition.z - 1.0 )
		{
			refractionUv = screenUv;
		}
		float3 transmitted = WaterSceneColor.SampleLevel( g_sBilinearClamp,
			refractionUv, 0 ).rgb;

		Material material = Material::Init( input.vPositionWithOffsetWs, input.vPositionSs );
		material.Normal = normal;
		material.Albedo = SrgbGammaToLinear( lerp( WaterTint, float3( 0.29, 0.30, 0.13 ), marsh ) ) * absorption * (1.0 - fresnel);
		material.Roughness = lerp( lerp( 0.09, 0.22, 1.0 - detail ), 0.16, marsh );
		material.Opacity = 1.0;
		material.Emission = transmitted * (1.0 - absorption) * (1.0 - fresnel);
		// The current scene has no reflection probes. Trace visible banks against
		// the opaque depth chain; missing/off-screen hits fade back to the sky.
		ClusterRange environment = Cluster::Query( ClusterItemType_EnvMap, input.vPositionSs );
		if ( environment.Count == 0 )
		{
			float3 reflectionDirection = reflect( -view, normal );
			float elevation = saturate( reflectionDirection.z );
			float3 reflection = SrgbGammaToLinear( WaterSkyTint ) * lerp( 1.35, 0.65, elevation );
			if ( view.z > 0.0 && fresnel > 0.025 )
			{
				WaterReflectionHit hit = TraceWaterReflection( worldPosition + float3( 0.0, 0.0, 2.0 ), reflectionDirection );
				if ( hit.ValidHit )
				{
					float3 reflected = WaterSceneColor.SampleLevel( g_sTrilinearClamp,
						hit.HitClipSpace.xy, 1.0 ).rgb;
					reflection = lerp( reflection, reflected, hit.Confidence );
				}
			}
			material.Emission += reflection * fresnel;
		}
		// Transmission is already composed from the opaque scene. Alpha only
		// softens the shallow intersection, without blending the riverbed twice.
		float4 shaded = ShadingModelStandard::Shade( input, material );
		shaded.rgb *= shore;
		return float4( shaded.rgb, shore );
	}
}
