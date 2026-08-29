MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	// Classify LOD0 cells against the canonical surface into the active stream.
	#include "system.fxc"
	#include "shaders/voxels/voxel_sdf_v2.hlsl"
	#include "shaders/voxels/voxel_regular_cell.hlsl"
	#include "shaders/voxels/transvoxel_regular_metadata.hlsl"
}

CS
{
	// One invocation owns one regular cell and appends at most one record.
	#include "common.fxc"

	AppendStructuredBuffer<uint> ActiveCells < Attribute( "ActiveCells" ); >;
	StructuredBuffer<float4> SdfParameters < Attribute( "SdfParameters" ); >;

	[numthreads( 4, 4, 4 )]
	void MainCs( uint3 dispatchId : SV_DispatchThreadID )
	{
		float4 spatial = SdfParameters[0];
		float4 terrain = SdfParameters[1];
		int cellsPerAxis = (int)SdfParameters[2].x;
		if ( any( dispatchId >= (uint)cellsPerAxis ) )
		{
			return;
		}

		int3 localCell = int3( dispatchId );
		float cellSize = spatial.w;
		int3 globalSampleOrigin = (int3)round( spatial.xyz / cellSize );
		int3 globalCell = globalSampleOrigin + localCell;
		uint caseIndex = ClassifyVoxelRegularCell(
			globalCell,
			cellSize,
			(int)terrain.x,
			terrain.y,
			terrain.z,
			terrain.w );

		if ( caseIndex == 0 || caseIndex == 255 )
		{
			return;
		}

		uint cellClass = RegularCellClass[caseIndex];
		uint geometryCounts = RegularCellGeometryCounts[cellClass];
		uint triangleCount = geometryCounts & 15;
		uint packedCell = dispatchId.x |
			(dispatchId.y << 6) |
			(dispatchId.z << 12) |
			(triangleCount << 18) |
			(caseIndex << 24);
		ActiveCells.Append( packedCell );
	}
}
