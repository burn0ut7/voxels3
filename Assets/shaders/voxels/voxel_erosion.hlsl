// Advanced Terrain Erosion Filter / Phacelle Noise, copyright (c) 2025 Rune Skovbo Johansen.
// C# reference by Luke Mitchell, 2026; Voxels3 adaptation mirrors TerrainErosion.cs.
// This Source Code Form is subject to MPL 2.0: https://mozilla.org/MPL/2.0/.
float2 VoxelErosionPhacelle( float2 position, float2 side, uint seed )
{
	int2 origin = (int2)floor( position );
	float2 fraction = position - origin;
	float2 blend = fraction * fraction * (3.0 - 2.0 * fraction);
	float cosine = 0.0;
	float sine = 0.0;
	float weightSum = 0.0;
	for ( int y = 0; y <= 1; y++ )
	{
		for ( int x = 0; x <= 1; x++ )
		{
			uint hash = VoxelHash2D( origin.x + x, origin.y + y, seed ^ 0xA24BAED5u );
			float dx = fraction.x - x - ((hash & 65535u) / 65535.0 - 0.5);
			float dy = fraction.y - y - ((hash >> 16) / 65535.0 - 0.5);
			float weight = (x == 0 ? 1.0 - blend.x : blend.x) * (y == 0 ? 1.0 - blend.y : blend.y);
			float phase = dx * side.x + dy * side.y + 6.28318530718 * 0.25;
			cosine += cos( phase ) * weight;
			sine += sin( phase ) * weight;
			weightSum += weight;
		}
	}
	cosine /= weightSum;
	sine /= weightSum;
	float magnitude = max( 0.5, sqrt( cosine * cosine + sine * sine ) );
	return float2( cosine / magnitude, sine / magnitude );
}

float VoxelErosionOffset( float2 position, float2 gradient, float wavelength, float amplitude, uint seed )
{
	float slopeLength = sqrt( gradient.x * gradient.x + gradient.y * gradient.y );
	if ( slopeLength <= 0.000001 || amplitude <= 0.0 )
	{
		return 0.0;
	}
	float mask = clamp( slopeLength * 3.0, 0.0, 1.0 );
	mask = 1.0 - (1.0 - mask) * (1.0 - mask);
	float2 gullySlope = gradient / slopeLength * 0.7;
	float frequency = 1.0 / (wavelength * 0.7);
	float fadeTarget = 0.0;
	float height = 0.0;
	for ( int octave = 0; octave < 2; octave++ )
	{
		float slopeMagnitude = sqrt( gullySlope.x * gullySlope.x + gullySlope.y * gullySlope.y );
		float2 direction = gullySlope / max( slopeMagnitude, 0.000001 );
		float2 side = float2( -direction.y, direction.x ) * (0.7 * 6.28318530718);
		float2 wave = VoxelErosionPhacelle( position * frequency, side, seed );
		float faded = fadeTarget * (1.0 - mask) + wave.x * 0.7 * mask;
		height += faded * amplitude;
		gullySlope -= side * (sign( wave.y ) * frequency * amplitude * 0.7);
		fadeTarget = faded;
		float nextMask = clamp( abs( wave.y ) * 1.5, 0.0, 1.0 );
		nextMask = 1.0 - (1.0 - nextMask) * (1.0 - nextMask);
		mask = (1.0 - (1.0 - mask) * (1.0 - mask)) * nextMask;
		amplitude *= 0.5;
		frequency *= 2.0;
	}
	return height;
}
