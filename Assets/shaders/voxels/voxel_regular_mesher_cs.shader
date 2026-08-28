MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	// Classify LOD0 cells into the compact GPU-resident active stream.
	#include "system.fxc"
	#include "shaders/voxels/voxel_sdf.hlsl"
	#include "shaders/voxels/voxel_regular_cell.hlsl"
	#include "shaders/voxels/transvoxel_regular_metadata.hlsl"
}

CS
{
	// One invocation owns one regular cell and appends at most one record.
	#include "common.fxc"

	AppendStructuredBuffer<uint> ActiveCells < Attribute( "ActiveCells" ); >;
	int3 ChunkCoordinate < Attribute( "ChunkCoordinate" ); >;
	int CellsPerAxis < Attribute( "CellsPerAxis" ); >;
	float CellSize < Attribute( "CellSize" ); >;
	float SurfaceHeight < Attribute( "SurfaceHeight" ); >;

	[numthreads( 4, 4, 4 )]
	void MainCs( uint3 dispatchId : SV_DispatchThreadID )
	{
		if ( any( dispatchId >= (uint)CellsPerAxis ) )
		{
			return;
		}

		int3 localCell = int3( dispatchId );
		int3 globalCell = ChunkCoordinate * CellsPerAxis + localCell;
		uint caseIndex = ClassifyVoxelRegularCell( globalCell, CellSize, SurfaceHeight );

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
