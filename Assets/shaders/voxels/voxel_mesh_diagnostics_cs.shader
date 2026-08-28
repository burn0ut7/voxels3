MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	// Inspect the canonical active-cell stream without mutating production output.
	#include "system.fxc"
	#include "shaders/voxels/voxel_sdf.hlsl"
}

CS
{
	#include "common.fxc"

	StructuredBuffer<uint> DiagnosticActiveCells < Attribute( "DiagnosticActiveCells" ); >;
	RWStructuredBuffer<uint> MeshStatistics < Attribute( "MeshStatistics" ); >;
	int3 ChunkCoordinate < Attribute( "ChunkCoordinate" ); >;
	int CellsPerAxis < Attribute( "CellsPerAxis" ); >;
	float CellSize < Attribute( "CellSize" ); >;
	float SurfaceHeight < Attribute( "SurfaceHeight" ); >;

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 dispatchId : SV_DispatchThreadID )
	{
		uint activeCellCount = MeshStatistics[0];
		if ( dispatchId.x >= activeCellCount )
		{
			return;
		}

		uint activeCell = DiagnosticActiveCells[dispatchId.x];
		InterlockedAdd( MeshStatistics[1], (activeCell >> 18) & 7 );

		int3 localCell = int3(
			activeCell & 63,
			(activeCell >> 6) & 63,
			(activeCell >> 12) & 63 );
		int3 globalCell = ChunkCoordinate * CellsPerAxis + localCell;
		float3 worldCellCenter = ((float3)globalCell + 0.5) * CellSize;
		float3 gradient = SampleVoxelSdfGradient( worldCellCenter, CellSize, SurfaceHeight );
		if ( any( isnan( gradient ) ) || any( isinf( gradient ) ) || dot( gradient, gradient ) <= 1.0e-12 )
		{
			InterlockedAdd( MeshStatistics[2], 1 );
		}
	}
}
