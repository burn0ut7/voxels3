MODES
{
	Default();
}

FEATURES
{
	// One level-aware transition kernel owns topology, bounded seam deformation, bit-exact record identity, and audits.
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
