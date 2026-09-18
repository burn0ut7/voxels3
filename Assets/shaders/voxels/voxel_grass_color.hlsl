#ifndef VOXEL_GRASS_COLOR_HLSL
#define VOXEL_GRASS_COLOR_HLSL

float EvaluateGrassColor( float2 worldPosition )
{
	// Smooth value noise on a 4.5 m grid gives irregular, stationary patches.
	float2 patchPosition = worldPosition * (0.0254 / 4.5);
	int2 cell = (int2)floor( patchPosition );
	float2 blend = frac( patchPosition );
	blend = blend * blend * (3.0 - 2.0 * blend);
	uint4 hashes = asuint( int4( cell.x, cell.x + 1, cell.x, cell.x + 1 ) ) * 0x8da6b343u ^
		asuint( int4( cell.y, cell.y, cell.y + 1, cell.y + 1 ) ) * 0xd8163841u;
	hashes ^= hashes >> 16;
	hashes *= 0x7feb352du;
	hashes ^= hashes >> 15;
	hashes *= 0x846ca68bu;
	hashes ^= hashes >> 16;
	float4 corners = (hashes & 65535u) / 65535.0;
	return lerp( lerp( corners.x, corners.y, blend.x ), lerp( corners.z, corners.w, blend.x ), blend.y );
}

#endif
