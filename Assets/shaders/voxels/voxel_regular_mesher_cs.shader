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

	RWStructuredBuffer<uint> SlabActiveCells < Attribute( "SlabActiveCells" ); >;
	RWStructuredBuffer<uint> SlabActiveCellCounts < Attribute( "SlabActiveCellCounts" ); >;
	StructuredBuffer<float4> SlabSdfParameters < Attribute( "SlabSdfParameters" ); >;
	StructuredBuffer<uint> MeshingSlots < Attribute( "MeshingSlots" ); >;
	int SlabRegionCapacity < Attribute( "SlabRegionCapacity" ); >;
	int MeshingJobCount < Attribute( "MeshingJobCount" ); >;

	[numthreads( 4, 4, 4 )]
	void MainCs( uint3 dispatchId : SV_DispatchThreadID )
	{
		int cellsPerAxis = (int)SlabSdfParameters[MeshingSlots[0] * 3 + 2].x;
		uint jobIndex = dispatchId.z / (uint)cellsPerAxis;
		if ( jobIndex >= (uint)MeshingJobCount )
		{
			return;
		}

		uint slot = MeshingSlots[jobIndex];
		uint localZ = dispatchId.z - jobIndex * (uint)cellsPerAxis;
		float4 spatial = SlabSdfParameters[slot * 3];
		float4 terrain = SlabSdfParameters[slot * 3 + 1];
		cellsPerAxis = (int)SlabSdfParameters[slot * 3 + 2].x;
		if ( dispatchId.x >= (uint)cellsPerAxis || dispatchId.y >= (uint)cellsPerAxis ||
			localZ >= (uint)cellsPerAxis )
		{
			return;
		}

		int3 localCell = int3( dispatchId.xy, localZ );
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
			(localZ << 12) |
			(triangleCount << 18) |
			(caseIndex << 24);
		uint outputIndex;
		InterlockedAdd( SlabActiveCellCounts[slot], 1, outputIndex );
		SlabActiveCells[slot * (uint)SlabRegionCapacity + outputIndex] = packedCell;
	}
}
