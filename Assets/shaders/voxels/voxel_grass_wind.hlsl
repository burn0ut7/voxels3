#ifndef VOXEL_GRASS_WIND_HLSL
#define VOXEL_GRASS_WIND_HLSL

// Unit direction and hard bend bound are shared by deformation and culling.
#define GRASS_WIND_DIRECTION float2( 0.8, 0.6 )
#define GRASS_MAX_WIND_BEND 0.5

float EvaluateGrassWind( float2 worldPosition, float seconds )
{
	float2 metres = worldPosition * 0.0254;
	float2 direction = GRASS_WIND_DIRECTION;
	// A broad front moves 4 m/s through the world, repeating every 20 m.
	// Squaring the smooth wave separates stronger gusts from gentle lulls.
	float gust = 0.5 + 0.5 * sin( (dot( metres, direction ) - seconds * 4.0) * (6.2831853 / 20.0) );
	float ripple = sin( (dot( metres, float2( -direction.y, direction.x ) ) - seconds * 1.5) * (6.2831853 / 5.0) );
	// Peak 0.475 leaves room for half-float rounding under the shared bound.
	return clamp( 0.08 + 0.36 * gust * gust + 0.035 * ripple, 0.0, GRASS_MAX_WIND_BEND );
}

#endif
