#ifndef VOXEL_VERTEX_IDENTITY_HLSL
#define VOXEL_VERTEX_IDENTITY_HLSL

// The finite packed normal.x retains slot/generation identity; its sign marks
// authoritative corrections. Normal.yz continue to own the octahedral normal.
uint EncodeVoxelVertexIdentity( uint allocationIdentity, uint hasCorrections )
{
	return 0x3f800000u | (((allocationIdentity >> 30u) & 3u) << 21u) |
		(allocationIdentity & 0x001fffffu) | (hasCorrections != 0u ? 0x80000000u : 0u);
}

#endif
