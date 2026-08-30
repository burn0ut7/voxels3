MODES
{
	Default();
}

FEATURES
{
	// Dedicated 24-byte vertex emission with packed record identity.
}

COMMON
{
	#include "system.fxc"
}

CS
{
	#define VOXEL_PACKED_RECORD_IDENTITY 1
	#include "common.fxc"
	#include "shaders/voxels/voxel_emit_vertices.hlsl"
}
