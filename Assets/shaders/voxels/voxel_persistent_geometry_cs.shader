MODES
{
	Default();
}

FEATURES
{
	// Persistent density, classification, scans, counts, and digest.
}

COMMON
{
	#include "system.fxc"
	#include "shaders/voxels/voxel_sdf_v4.hlsl"
	#include "shaders/voxels/transvoxel_regular_tables.hlsl"
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_persistent_geometry.hlsl"
}
