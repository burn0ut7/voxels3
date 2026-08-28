MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	// Re-evaluate one chunk through the canonical SDF/case boundary on demand.
	#include "system.fxc"
	#include "shaders/voxels/voxel_sdf.hlsl"
	#include "shaders/voxels/voxel_regular_cell.hlsl"
	#include "shaders/voxels/transvoxel_regular_metadata.hlsl"
}

CS
{
	#include "common.fxc"

	RWStructuredBuffer<uint> MeshStatistics < Attribute( "MeshStatistics" ); >;
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
		uint triangleCount = RegularCellGeometryCounts[cellClass] & 15;
		InterlockedAdd( MeshStatistics[0], 1 );
		InterlockedAdd( MeshStatistics[1], triangleCount );

		float3 worldCellCenter = ((float3)globalCell + 0.5) * CellSize;
		float3 gradient = SampleVoxelSdfGradient( worldCellCenter, CellSize, SurfaceHeight );
		if ( any( isnan( gradient ) ) || any( isinf( gradient ) ) || dot( gradient, gradient ) <= 1.0e-12 )
		{
			InterlockedAdd( MeshStatistics[2], 1 );
		}
	}
}
