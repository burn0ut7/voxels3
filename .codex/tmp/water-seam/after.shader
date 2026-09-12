HEADER
{
	Description = "Generated water cell surfaces";
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
	#include "common/shared.hlsl"
	float4 WaterCoverage < Attribute( "WaterCoverage" ); >;
	float WaterCheckerSize < Attribute( "WaterCheckerSize" ); >;
	float3 WaterDark < Attribute( "WaterDark" ); >;
	float3 WaterLight < Attribute( "WaterLight" ); >;
}
struct VertexInput
{
	float3 Position : POSITION < Semantic( None ); >;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float CoverageClip[4] : SV_ClipDistance0;
};
VS
{
	PixelInput MainVs( const VertexInput input )
	{
		PixelInput output;
		output.vPositionWs = input.Position - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( input.Position );
		output.vNormalWs = float3( 0.0, 0.0, 1.0 );
		// Clip geometry before rasterization so shared edges keep their MSAA samples.
		output.CoverageClip[0] = input.Position.x - WaterCoverage.x;
		output.CoverageClip[1] = input.Position.y - WaterCoverage.y;
		output.CoverageClip[2] = WaterCoverage.z - input.Position.x;
		output.CoverageClip[3] = WaterCoverage.w - input.Position.y;
		return output;
	}
}
PS
{
	#include "common/pixel.hlsl"
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, true );
	float4 MainPs( PixelInput input ) : SV_Target0
	{
		float3 worldPosition = input.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float2 coordinates = worldPosition.xy / WaterCheckerSize;
		float checker = frac( (floor( coordinates.x ) + floor( coordinates.y )) * 0.5 ) * 2.0;
		float2 footprint = fwidth( coordinates );
		checker = lerp( checker, 0.5, saturate( max( footprint.x, footprint.y ) ) );
		return float4( lerp( WaterDark, WaterLight, checker ), 1.0 );
	}
}
