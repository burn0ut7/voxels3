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
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_regular_topology.hlsl"
	#include "shaders/voxels/voxel_emit_indices.hlsl"
}
