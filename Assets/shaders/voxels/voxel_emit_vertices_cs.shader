MODES
{
	Default();
}

FEATURES
{
	// Dedicated 24-byte vertex emission.
}

COMMON
{
	#include "system.fxc"
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_emit_vertices.hlsl"
}
