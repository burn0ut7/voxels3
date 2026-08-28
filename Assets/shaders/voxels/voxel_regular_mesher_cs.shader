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
	#include "shaders/voxels/transvoxel_regular_metadata.hlsl"
}

CS
{
	// One invocation owns one regular cell and appends at most one record.
	#include "common.fxc"

	AppendStructuredBuffer<uint> ActiveCells < Attribute( "ActiveCells" ); >;
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
		uint caseIndex = 0;
		float cornerDensity[8];
		for ( uint corner = 0; corner < 8; corner++ )
		{
			cornerDensity[corner] = SampleVoxelSdf(
				globalCell + VoxelCornerOffset( corner ),
				CellSize,
				SurfaceHeight );
			if ( cornerDensity[corner] <= 0.0 )
			{
				caseIndex |= 1u << corner;
			}
		}

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
		InterlockedAdd( MeshStatistics[0], triangleCount );
		float3 worldCellCenter = ((float3)globalCell + 0.5) * CellSize;
		float3 gradient = SampleVoxelSdfGradient( worldCellCenter, CellSize, SurfaceHeight );
		if ( any( isnan( gradient ) ) || any( isinf( gradient ) ) || dot( gradient, gradient ) <= 1.0e-12 )
		{
			InterlockedAdd( MeshStatistics[1], 1 );
		}
	}
}
