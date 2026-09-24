// Presentation-only mixture shared by ground shading and grass roots.
// Integer corner identities keep the field continuous at large/negative positions.
float VoxelMaterialNoise( float3 position )
{
	int3 cell = (int3)floor( position );
	float3 fraction = position - (float3)cell;
	fraction = fraction * fraction * (3.0 - 2.0 * fraction);
	float value = 0.0;
	for ( uint index = 0u; index < 8u; index++ )
	{
		uint3 corner = uint3( index & 1u, (index >> 1u) & 1u, index >> 2u );
		uint3 coordinate = (uint3)(cell + (int3)corner);
		uint hash = coordinate.x * 1597334677u ^ coordinate.y * 3812015801u ^ coordinate.z * 2798796415u;
		hash ^= hash >> 16u;
		hash *= 0x7feb352du;
		hash ^= hash >> 15u;
		float3 weight = lerp( 1.0 - fraction, fraction, (float3)corner );
		value += weight.x * weight.y * weight.z * ((float)(hash & 0x00ffffffu) / 16777216.0);
	}
	return value * 2.0 - 1.0;
}

float4 BlendVoxelMaterials( float4 weights, inout float gravel, float3 position, float footprint )
{
	weights = max( weights, 0.0 );
	gravel = max( gravel, 0.0 );
	float total = dot( weights, float4( 1.0, 1.0, 1.0, 1.0 ) ) + gravel;
	float sand = saturate( 1.0 - total );
	weights /= max( total, 1.0 );
	gravel /= max( total, 1.0 );
	float largest = max( max( sand, gravel ), max( max( weights.x, weights.y ), max( weights.z, weights.w ) ) );
	[branch]
	if ( largest >= 0.99999 )
	{
		return weights;
	}
	// These are visual wavelengths in world units, independent of mesh LOD.
	float coarse = 1.0 - smoothstep( 4.0, 16.0, footprint );
	float fine = 1.0 - smoothstep( 1.0, 4.0, footprint );
	[branch]
	if ( coarse > 0.0 )
	{
		float variation = 0.7 * coarse * VoxelMaterialNoise( position / 16.0 );
		[branch]
		if ( fine > 0.0 )
		{
			variation += 0.3 * fine * VoxelMaterialNoise( position / 4.0 );
		}
		// Positive multipliers cannot invent an absent material or an empty junction.
		weights *= exp2( variation * float4( -1.5, -0.75, 0.75, 1.5 ) );
		gravel *= exp2( variation * 0.375 );
	}
	weights = weights * weights;
	sand = sand * sand;
	gravel *= gravel;
	float sum = dot( weights, float4( 1.0, 1.0, 1.0, 1.0 ) ) + sand + gravel;
	gravel /= sum;
	return weights / sum;
}
