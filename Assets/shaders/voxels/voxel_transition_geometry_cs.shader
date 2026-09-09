// Transitions over version13 with exact cave-envelope early exits; startup candidate.
MODES
{
	Default();
}

FEATURES
{
	// Refined edge coordinates are shared by count digests and emission.
	// One level-aware transition kernel emits canonical cull-compatible primary topology for both LOD boundaries.
}

COMMON
{
	#include "system.fxc"
	// Match the regular field's cubic mountain eligibility at every transition sample.
	#include "shaders/voxels/voxel_sdf_v13.hlsl"
	#include "shaders/voxels/voxel_edge_intersection.hlsl"
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_transition_geometry.hlsl"
}
