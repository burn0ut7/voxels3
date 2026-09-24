HEADER
{
	Description = "Bounded world-space volumetric cloud layer";
}

MODES
{
	Forward();
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float2 vTexCoord : TEXCOORD0;
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput input )
	{
		PixelInput output;
		output.vPositionPs = float4( input.vPositionOs.xy, 0.0, 1.0 );
		output.vTexCoord = input.vTexCoord;
		return output;
	}
}

PS
{
	#include "postprocess/common.hlsl"

	Texture3D CloudShape < Attribute( "CloudShape" ); SrgbRead( false ); >;
	Texture3D CloudErosion < Attribute( "CloudErosion" ); SrgbRead( false ); >;
	Texture2D CloudLayout < Attribute( "CloudLayout" ); SrgbRead( false ); >;
	SamplerState CloudSampler < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;
	float4 CloudLayer < Attribute( "CloudLayer" ); >;
	float CloudBaseVariation < Attribute( "CloudBaseVariation" ); >;
	float4 CloudShapeParameters < Attribute( "CloudShapeParameters" ); >;
	float2 CloudWind < Attribute( "CloudWind" ); >;
	float CloudMetersPerUnit < Attribute( "CloudMetersPerUnit" ); >;
	float3 CloudSunDirection < Attribute( "CloudSunDirection" ); >;
	float4 CloudSunColor < Attribute( "CloudSunColor" ); >;
	float4 CloudHazeColor < Attribute( "CloudHazeColor" ); >;

	static const float CloudErosionRepeats = 8.0;

	// Density and height within the locally shifted layer share one spatial definition.
	float2 CloudDensity( float3 position, float footprintMeters )
	{
		if ( position.z <= CloudLayer.x - CloudBaseVariation ||
			position.z >= CloudLayer.x + CloudLayer.y + CloudBaseVariation ) return 0.0;
		position.xy -= CloudWind;
		float3 coordinate = position / CloudLayer.z;
		// The layout repeats every five shape periods. Each group keeps a level
		// base; its separation mask conceals interpolation between different bases.
		float3 group = CloudLayout.SampleLevel( CloudSampler, coordinate.xy / 5.0, 0 ).rgb;
		if ( group.b <= 0.001 ) return 0.0;
		float baseOffset = (group.g * 2.0 - 1.0) * CloudBaseVariation;
		float height = (position.z - CloudLayer.x - baseOffset) / CloudLayer.y;
		if ( height <= 0.0 || height >= 1.0 ) return 0.0;
		coordinate.z -= baseOffset / CloudLayer.z;
		float coverage = saturate( CloudShapeParameters.x + (group.r - 0.7) * 0.7 );
		if ( coverage <= 0.0 ) return 0.0;
		// Perlin-Worley dilation biases the stored signal upward. Expand its useful
		// range before coverage thresholding, otherwise fair weather becomes a sheet.
		float shape = saturate( (CloudShape.SampleLevel( CloudSampler, coordinate, 0 ).r - 0.35) / 0.6 );
		// Threshold the height-shaped signal so the top follows each billow's density.
		// Multiplying after the threshold makes a broad, uniformly flat cloud ceiling.
		float profile = smoothstep( 0.0, 0.1, height ) * (1.0 - smoothstep( 0.2, 1.0, height ));
		float density = saturate( (shape * profile - (1.0 - coverage)) / max( coverage, 0.001 ) );
		if ( density > 0.0 )
		{
			// Remove unresolved frequency bands independently; broad erosion can remain
			// visible after the finest octaves are smaller than a traced pixel.
			float detail = 0.5;
			float finestFeatureMeters = CloudLayer.z / (CloudErosionRepeats * 16.0);
			if ( footprintMeters < finestFeatureMeters * 2.0 )
			{
				float4 bands = CloudErosion.SampleLevel( CloudSampler, coordinate * CloudErosionRepeats, 0 );
				float footprint = footprintMeters / finestFeatureMeters;
				detail = lerp( bands.r, bands.g, smoothstep( 0.25, 0.5, footprint ) );
				detail = lerp( detail, bands.b, smoothstep( 0.5, 1.0, footprint ) );
				detail = lerp( detail, 0.5, smoothstep( 1.0, 2.0, footprint ) );
			}
			float erosion = lerp( detail, 1.0 - detail, smoothstep( 0.0, 0.2, height ) );
			density = saturate( (density - erosion * CloudShapeParameters.z) / max( 1.0 - CloudShapeParameters.z, 0.01 ) );
		}
		return float2( density * group.b, height );
	}

	float Phase( float cosine, float eccentricity )
	{
		float denominator = max( 1.0 + eccentricity * eccentricity - 2.0 * eccentricity * cosine, 0.001 );
		return (1.0 - eccentricity * eccentricity) / (denominator * sqrt( denominator ));
	}

	float3 CloudLighting( float3 position, float phase, float footprintMeters, float height )
	{
		float opticalDepth = 0.0;
		float previousDistance = 0.0;
		[unroll]
		for ( int index = 0; index < 4; ++index )
		{
			float distance = 35.0 * exp2( index * 1.2 );
			opticalDepth += CloudDensity( position + CloudSunDirection * distance, index < 2 ? footprintMeters : 1000000.0 ).x
				* (distance - previousDistance) * CloudShapeParameters.y;
			previousDistance = distance;
		}
		float3 ambient = lerp( float3( 0.15, 0.20, 0.28 ), float3( 0.35, 0.41, 0.50 ), height );
		// A bounded angular response retains forward glow without a clipped white rim
		// in scenes that have no filmic exposure/atmosphere pass.
		float direct = exp( -opticalDepth ) * phase / (1.0 + phase);
		float scattered = 0.65 * exp( -opticalDepth * 0.08 );
		return ambient + SrgbGammaToLinear( CloudSunColor.rgb ) * (direct * 0.4 + scattered) * 0.75;
	}

	float4 MainPs( PixelInput input ) : SV_Target0
	{
		float2 ndc = input.vTexCoord * float2( 2.0, -2.0 ) + float2( -1.0, 1.0 );
		// ProjectionToWorld gives a camera-relative world vector in the engine shader contract.
		float4 projected = mul( g_matProjectionToWorld, float4( ndc, 0.5, 1.0 ) );
		float3 direction = normalize( projected.xyz / max( projected.w, 1e-8 ) );
		float rayFootprint = max( length( ddx( direction ) ), length( ddy( direction ) ) );
		float3 origin = g_vCameraPositionWs * CloudMetersPerUnit;
		if ( abs( direction.z ) < 0.0001 ) return 0.0;
		float bottom = (CloudLayer.x - CloudBaseVariation - origin.z) / direction.z;
		float top = (CloudLayer.x + CloudLayer.y + CloudBaseVariation - origin.z) / direction.z;
		float entry = max( min( bottom, top ), 0.0 );
		float exit = min( max( bottom, top ), CloudLayer.w );
		if ( exit <= entry ) return 0.0;

		// Long horizon rays need more samples than the short overhead interval.
		// Stop refining below a traced pixel footprint; keep the worst-case loop bounded.
		float targetSpacing = max( CloudLayer.z / 96.0, rayFootprint * entry );
		int steps = min( 256, max( (int)CloudShapeParameters.w, (int)ceil( (exit - entry) / targetSpacing ) ) );
		float stepLength = (exit - entry) / steps;
		float sampleDistance = entry + stepLength * 0.5;
		float phase = 0.8 * Phase( dot( direction, CloudSunDirection ), 0.6 )
			+ 0.2 * Phase( dot( direction, CloudSunDirection ), -0.25 );
		float3 color = 0.0;
		float transmittance = 1.0;
		float accumulatedDepth = 0.0;
		[loop]
		for ( int index = 0; index < steps && transmittance > 0.015; ++index )
		{
			float3 position = origin + direction * sampleDistance;
			float footprintMeters = rayFootprint * sampleDistance;
			float2 cloud = CloudDensity( position, footprintMeters );
			if ( cloud.x > 0.001 )
			{
				float stepOpacity = 1.0 - exp( -cloud.x * CloudShapeParameters.y * stepLength );
				float weight = transmittance * stepOpacity;
				color += weight * CloudLighting( position, phase, footprintMeters, cloud.y );
				accumulatedDepth += weight * sampleDistance;
				transmittance *= 1.0 - stepOpacity;
			}
			sampleDistance += stepLength;
		}
		float opacity = 1.0 - transmittance;
		float depth = accumulatedDepth / max( opacity, 0.0001 );
		float hazeDistance = depth / 9000.0;
		float haze = 1.0 - exp( -hazeDistance * hazeDistance );
		color = lerp( color, SrgbGammaToLinear( CloudHazeColor.rgb ) * opacity, haze );
		// Fade the bounded far end into the existing atmosphere, including horizon rays.
		float rangeFade = 1.0 - smoothstep( CloudLayer.w * 0.6, CloudLayer.w, depth );
		return float4( color, opacity ) * rangeFade;
	}
}
