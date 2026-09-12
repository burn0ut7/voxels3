// Regular terrain: cache canonical exterior once per XY column.
// River atlas: prune dry banks in sea-level footprint queries.
// River atlas: unique endpoints with packed bin references.
// River sampling: evaluate valley shoulder once per column.
// River revision 8: elevation-scaled valley shoulders.
// River revision 7: fixed-level terrain valleys and basin networks.
// River rendering revision 5: ocean banks and canonical channel visibility.
MODES
{
	Default();
}

FEATURES
{
	// Decode the exact refined world-axis coordinate written by the count stage.
	// Dedicated 28-byte vertices with generated material weights; consumes the 96-byte regional terrain request.
}

COMMON
{
	#include "system.fxc"
}

CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_edge_position.hlsl"
	#include "shaders/voxels/voxel_emit_vertices.hlsl"
}
