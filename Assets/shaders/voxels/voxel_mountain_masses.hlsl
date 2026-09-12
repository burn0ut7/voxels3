// GPU mirror of MountainMasses: continuous warped ridges and broad shoulders.
float MountainFlowNoise( float2 point, uint seed, out float2 gradient )
{
	int2 origin = (int2)floor( point );
	float2 t = point - (float2)origin;
	float2 s = t * t * (3.0 - 2.0 * t);
	float a = (VoxelHash2D( origin.x, origin.y, seed ) & 65535u) / 65535.0;
	float b = (VoxelHash2D( origin.x + 1, origin.y, seed ) & 65535u) / 65535.0;
	float c = (VoxelHash2D( origin.x, origin.y + 1, seed ) & 65535u) / 65535.0;
	float d = (VoxelHash2D( origin.x + 1, origin.y + 1, seed ) & 65535u) / 65535.0;
	gradient = float2( ((b - a) * (1.0 - s.y) + (d - c) * s.y) * (6.0 * t.x * (1.0 - t.x)),
		((c - a) * (1.0 - s.x) + (d - b) * s.x) * (6.0 * t.y * (1.0 - t.y)) );
	return clamp( (a * (1.0 - s.x) + b * s.x) * (1.0 - s.y) + (c * (1.0 - s.x) + d * s.x) * s.y, 0.0, 1.0 );
}

float SampleVoxelMountainMass( float2 position, float scale, uint seed, out float2 gradient, out float peakFraction, out float erosionStrength )
{
	float2 point = position / scale;
	float2 gx;
	float2 gy;
	float wx = MountainFlowNoise( point * 0.43, seed ^ 0x7F4A7C15u, gx );
	float wy = MountainFlowNoise( point * 0.43 + float2( 19.3, -7.1 ), seed ^ 0x94D049BBu, gy );
	gx *= 0.43;
	gy *= 0.43;
	float2 warped = point + float2( wx - 0.5, wy - 0.5 ) * 1.8;
	float2 ga;
	float2 gb;
	float a = MountainFlowNoise( float2( 0.8 * warped.x + 0.6 * warped.y, -0.6 * warped.x + 0.8 * warped.y ) * 0.72,
		seed ^ 0x369DEA0Fu, ga );
	float b = MountainFlowNoise( float2( 0.6 * warped.x - 0.8 * warped.y, 0.8 * warped.x + 0.6 * warped.y ) * 1.15 + float2( -11.7, 23.4 ),
		seed ^ 0xDB4F0B91u, gb );
	ga = float2( 0.8 * ga.x - 0.6 * ga.y, 0.6 * ga.x + 0.8 * ga.y ) * 0.72;
	gb = float2( 0.6 * gb.x + 0.8 * gb.y, -0.8 * gb.x + 0.6 * gb.y ) * 1.15;
	ga = ga + 1.8 * (ga.x * gx + ga.y * gy);
	gb = gb + 1.8 * (gb.x * gx + gb.y * gy);
	float ra = max( 0.0, 1.0 - abs( 4.0 * a - 2.0 ) );
	float rb = max( 0.0, 1.0 - abs( 4.0 * b - 2.0 ) );
	float ridges = 0.72 * ra * ra + 0.28 * rb * rb;
	float2 ridgeGradient = ga * (-5.76 * ra * sign( 4.0 * a - 2.0 )) + gb * (-2.24 * rb * sign( 4.0 * b - 2.0 ));
	float st = clamp( (a - 0.28) / 0.40, 0.0, 1.0 );
	float shelf = st * st * (3.0 - 2.0 * st);
	float2 shelfGradient = ga * (6.0 * st * (1.0 - st) / 0.40);
	float bt = clamp( (wx - 0.42) / 0.26, 0.0, 1.0 );
	float blend = bt * bt * (3.0 - 2.0 * bt);
	float2 blendGradient = gx * (6.0 * bt * (1.0 - bt) / 0.26);
	float height = 0.45 + 0.55 * wy;
	float shape = (1.0 - blend) * ridges + blend * shelf;
	float mass = height * shape;
	gradient = (height * ((1.0 - blend) * ridgeGradient + blend * shelfGradient + (shelf - ridges) * blendGradient) + shape * 0.55 * gy) / scale;
	// Match the CPU tip score; exclude broad elevated shelves from snow.
	peakFraction = height * (1.0 - blend) * ridges;
	erosionStrength = 0.25 + 0.75 * wy;
	return mass;
}
