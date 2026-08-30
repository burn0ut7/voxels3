MODES
{
	Default();
}

FEATURES
{
	// Dedicated Transvoxel transition index emission.
}

COMMON
{
	#include "system.fxc"
	#include "shaders/voxels/transvoxel_transition_tables.hlsl"
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_transition_emit_indices.hlsl"
}
