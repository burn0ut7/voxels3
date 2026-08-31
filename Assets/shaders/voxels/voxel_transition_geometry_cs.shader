MODES
{
	Default();
}

FEATURES
{
	// Transition density, classification, scans, count and output emission, contour audits, and diagnostics.
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
