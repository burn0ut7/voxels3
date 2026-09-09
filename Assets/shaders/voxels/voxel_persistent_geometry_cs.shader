// Version13 field with exact cave-envelope early exits.
MODES
{
	Default();
}

FEATURES
{
	// Refined edge coordinates are shared by count digests and emission.
	// Persistent density, classification, scans, counts, and digest.
}

COMMON
{
	#include "system.fxc"
	// Regional landforms: cubic mountain and squared hill eligibility soften tails near plains.
	#include "shaders/voxels/voxel_sdf_v13.hlsl"
	#include "shaders/voxels/voxel_edge_intersection.hlsl"
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_regular_topology.hlsl"
	#include "shaders/voxels/voxel_persistent_geometry.hlsl"
}
