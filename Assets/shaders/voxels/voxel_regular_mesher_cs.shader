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
	#include "shaders/voxels/voxel_sdf_v1.hlsl"
	#include "shaders/voxels/voxel_regular_cell.hlsl"
	#include "shaders/voxels/transvoxel_regular_metadata.hlsl"
}

CS
{
	// One invocation owns one regular cell and appends at most one record.
	#include "common.fxc"

	AppendStructuredBuffer<uint> ActiveCells < Attribute( "ActiveCells" ); >;
	float3 ChunkWorldOrigin < Attribute( "ChunkWorldOrigin" ); >;
	int CellsPerAxis < Attribute( "CellsPerAxis" ); >;
	float CellSize < Attribute( "CellSize" ); >;
	// Numeric floats preserve the validated seed and version exactly.
	float2 GeneratorIdentity < Attribute( "GeneratorIdentity" ); >;

	[numthreads( 4, 4, 4 )]
	void MainCs( uint3 dispatchId : SV_DispatchThreadID )
	{
		if ( any( dispatchId >= (uint)CellsPerAxis ) )
		{
			return;
		}

		int3 localCell = int3( dispatchId );
		int3 globalSampleOrigin = (int3)round( ChunkWorldOrigin / CellSize );
		int3 globalCell = globalSampleOrigin + localCell;
		uint caseIndex = ClassifyVoxelRegularCell(
			globalCell,
			CellSize,
			(int)GeneratorIdentity.x,
			(int)GeneratorIdentity.y );

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
