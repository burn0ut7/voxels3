MODES
{
	Default();
}

FEATURES
{
	// Dedicated indexed-topology emission.
}

COMMON
{
	#include "system.fxc"
	#include "shaders/voxels/transvoxel_regular_tables.hlsl"
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_emit_indices.hlsl"
}
