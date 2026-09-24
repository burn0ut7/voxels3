HEADER
{
	Description = "Composite volumetric sky clouds behind full-resolution scene depth";
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
	RenderState( ColorWriteEnable0, RGB );
	RenderState( BlendEnable, true );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, INV_SRC_ALPHA );
	Texture2D CloudScattering < Attribute( "CloudScattering" ); SrgbRead( false ); >;
	SamplerState CloudUpsample < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	float4 MainPs( PixelInput input ) : SV_Target0
	{
		float depth = Depth::GetNormalized( input.vTexCoord * g_vViewportSize );
		if ( depth > 0.0000001 ) return 0.0;
		return CloudScattering.SampleLevel( CloudUpsample, input.vTexCoord, 0 );
	}
}
