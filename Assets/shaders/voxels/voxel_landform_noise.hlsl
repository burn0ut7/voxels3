#ifndef VOXEL_LANDFORM_NOISE_H
#define VOXEL_LANDFORM_NOISE_H

float LandformSmooth( float value )
{
	float t = clamp( value, 0.0, 1.0 );
	return t * t * (3.0 - 2.0 * t);
}

float LandformSmoothDerivative( float value )
{
	float t = clamp( value, 0.0, 1.0 );
	return 6.0 * t * (1.0 - t);
}

float LandformNoise( float2 position, float scale, uint seed, out float2 gradient )
{
	float2 point = position / scale;
	int2 origin = (int2)floor( point );
	float u = LandformSmooth( point.x - origin.x );
	float v = LandformSmooth( point.y - origin.y );
	float a = (VoxelHash2D( origin.x, origin.y, seed ) & 65535u) / 65535.0;
	float b = (VoxelHash2D( origin.x + 1, origin.y, seed ) & 65535u) / 65535.0;
	float c = (VoxelHash2D( origin.x, origin.y + 1, seed ) & 65535u) / 65535.0;
	float d = (VoxelHash2D( origin.x + 1, origin.y + 1, seed ) & 65535u) / 65535.0;
	gradient = float2( ((b - a) * (1.0 - v) + (d - c) * v) * LandformSmoothDerivative( point.x - origin.x ),
		((c - a) * (1.0 - u) + (d - b) * u) * LandformSmoothDerivative( point.y - origin.y ) ) / scale;
	return clamp( (a * (1.0 - u) + b * u) * (1.0 - v) + (c * (1.0 - u) + d * u) * v, 0.0, 1.0 );
}

#endif
