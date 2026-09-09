// After scans, active edge words contain asuint(world-axis coordinate)+1.
// Zero remains inactive; the stored position is shared by digest and emit stages.
float3 VoxelEdgePosition( float3 first, float3 second, float coordinate )
{
	return float3( first.x == second.x ? first.x : coordinate,
		first.y == second.y ? first.y : coordinate,
		first.z == second.z ? first.z : coordinate );
}
