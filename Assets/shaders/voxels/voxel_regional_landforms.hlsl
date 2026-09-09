float LandformSmooth( float value )
{
	float t = clamp( value, 0.0, 1.0 );
	return t * t * (3.0 - 2.0 * t);
}

float LandformEligibility( float value, float amount )
{
	if ( amount <= 0.0 )
	{
		return 0.0;
	}
	if ( amount >= 1.0 )
	{
		return 1.0;
	}
	return LandformSmooth( (value - (1.2 - 1.4 * amount)) / 0.2 );
}

float LandformNoise( float2 position, float scale, uint seed )
{
	float2 point = position / scale;
	int2 origin = (int2)floor( point );
	float u = LandformSmooth( point.x - origin.x );
	float v = LandformSmooth( point.y - origin.y );
	float a = (VoxelHash2D( origin.x, origin.y, seed ) & 65535u) / 65535.0;
	float b = (VoxelHash2D( origin.x + 1, origin.y, seed ) & 65535u) / 65535.0;
	float c = (VoxelHash2D( origin.x, origin.y + 1, seed ) & 65535u) / 65535.0;
	float d = (VoxelHash2D( origin.x + 1, origin.y + 1, seed ) & 65535u) / 65535.0;
	return clamp( (a * (1.0 - u) + b * u) * (1.0 - v) + (c * (1.0 - u) + d * u) * v, 0.0, 1.0 );
}

float SampleVoxelLandformHeight( float2 position, float4 terrain, float4 scales, float ruggedness )
{
	uint seed = (uint)(int)terrain.x;
	float n = LandformNoise( position, scales.x, seed ^ 0xB5297A4Du );
	float m = LandformNoise( position, scales.y, seed ^ 0x68E31DA4u );
	float p = LandformNoise( position, scales.z * 2.0, seed ^ 0x1B56C4E9u );
	float q = LandformNoise( position, scales.z, seed ^ 0x7F4A7C15u );
	float d = LandformNoise( position, scales.z * 0.5, seed ^ 0x94D049BBu );
	float f = LandformNoise( position, scales.z * 0.125, seed ^ 0xD1B54A35u );
	float land = LandformEligibility( n, terrain.y );
	float mountainPreference = LandformEligibility( m, terrain.z * (2.0 - terrain.z) );
	float mountains = mountainPreference * mountainPreference * mountainPreference * LandformSmooth( (land - 0.5) * 2.0 );
	float hillPreference = 1.0 - LandformEligibility( p, terrain.w );
	float plains = (1.0 - mountains) * (1.0 - hillPreference * hillPreference);
	float hills = 1.0 - mountains - plains;
	float ridge = 1.0 - abs( 2.0 * q - 1.0 );
	float cliff = LandformSmooth( (ridge - 0.55) / 0.12 ) * LandformSmooth( 2.0 * mountains );
	float mountainHeight = 0.10 + 0.55 * ridge * ridge * ridge + 0.28 * cliff +
		0.08 * ruggedness * (f - 0.5) * ridge;
	float mounds = LandformSmooth( (f - 0.45) * 4.0 );
	float landHeight = plains * (0.035 + 0.015 * q) +
		hills * (0.08 + 0.24 * (0.65 * q + 0.35 * d) +
			0.08 * ruggedness * mounds) + mountains * mountainHeight;
	float oceanHeight = -0.06 - 0.64 * (1.0 - n) * (1.0 - n);
	return ((1.0 - land) * oceanHeight + land * landHeight) * scales.w;
}
