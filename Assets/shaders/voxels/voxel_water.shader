HEADER
{
	Description = "Static sea-level voxel water";
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
	#include "shaders/voxels/voxel_terrain_noise.hlsl"
	#include "shaders/voxels/voxel_regional_landforms.hlsl"
	float4 WaterTerrain < Attribute( "WaterTerrain" ); >;
	float4 WaterScales < Attribute( "WaterScales" ); >;
	float WaterRuggedness < Attribute( "WaterRuggedness" ); >;
	float WaterSeaLevel < Attribute( "WaterSeaLevel" ); >;
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
};
VS
{
	PixelInput MainVs( const VertexInput input )
	{
		PixelInput output;
		output.vPositionWs = input.Position - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( input.Position );
		output.vNormalWs = float3( 0.0, 0.0, 1.0 );
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
		Material material = Material::Init( input );
		float3 worldPosition = input.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float height = SampleVoxelLandformHeight( worldPosition.xy, WaterTerrain, WaterScales, WaterRuggedness );
		if ( height >= WaterSeaLevel ) discard;
		float2 coordinates = worldPosition.xy / WaterCheckerSize;
		float checker = frac( (floor( coordinates.x ) + floor( coordinates.y )) * 0.5 ) * 2.0;
		float2 footprint = fwidth( coordinates );
		checker = lerp( checker, 0.5, saturate( max( footprint.x, footprint.y ) ) );
		return float4( lerp( WaterDark, WaterLight, checker ), 1.0 );
	}
}
