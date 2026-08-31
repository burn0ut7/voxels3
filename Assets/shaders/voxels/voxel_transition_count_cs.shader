MODES
{
	Default();
}

FEATURES
{
	// Dedicated transition sampling, classification, scans, and counts.
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
	#include "shaders/voxels/voxel_transition_count.hlsl"
}
