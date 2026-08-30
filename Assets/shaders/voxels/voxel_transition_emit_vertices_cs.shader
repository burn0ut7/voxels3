MODES
{
	Default();
}

FEATURES
{
	// Dedicated transition vertex emission; kept independent from regular output.
}

COMMON
{
	#include "system.fxc"
	#include "shaders/voxels/transvoxel_transition_tables.hlsl"
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_transition_emit_vertices.hlsl"
}
