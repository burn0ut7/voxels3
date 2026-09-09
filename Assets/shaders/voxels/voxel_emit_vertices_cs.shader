MODES
{
	Default();
}

FEATURES
{
	// Decode the exact refined world-axis coordinate written by the count stage.
	// Dedicated 24-byte vertices; consumes the 96-byte regional terrain request.
}

COMMON
{
	#include "system.fxc"
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_edge_position.hlsl"
	#include "shaders/voxels/voxel_emit_vertices.hlsl"
}
