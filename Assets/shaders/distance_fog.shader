HEADER
{
	Description = "Two-color fog relative to the camera far plane";
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
	#include "common/classes/Depth.hlsl"

	Texture2D ColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;
	float4 FogNearColor < Attribute( "FogNearColor" ); >;
	float4 FogFarColor < Attribute( "FogFarColor" ); >;
	float FogStartFraction < Attribute( "FogStartFraction" ); >;

	float4 MainPs( PixelInput input ) : SV_Target0
	{
		float4 color = ColorBuffer.SampleLevel( g_sBilinearMirror, input.vTexCoord, 0 );
		float2 screenPosition = input.vTexCoord * g_vViewportSize;
		float depth = Depth::GetNormalized( screenPosition );
		// Reverse depth is zero at the far plane, including the cleared background.
		float farDistance = Depth::Linearize( 0.0, screenPosition );
		float distance = Depth::Linearize( depth, screenPosition );
		float fade = smoothstep( FogStartFraction, 1.0, saturate( distance / max( farDistance, 0.001 ) ) );
		float3 fogColor = lerp( SrgbGammaToLinear( FogNearColor.rgb ), SrgbGammaToLinear( FogFarColor.rgb ), fade );
		return float4( lerp( color.rgb, fogColor, fade ), color.a );
	}
}
