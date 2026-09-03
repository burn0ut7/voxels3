MODES
{
	Default();
}

FEATURES
{
	// One level-aware transition kernel emits canonical cull-compatible primary topology for both LOD boundaries.
}

COMMON
{
	#include "system.fxc"
	#include "shaders/voxels/voxel_sdf_v5.hlsl"
	#include "shaders/voxels/transvoxel_transition_tables.hlsl"
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_transition_geometry.hlsl"
}
