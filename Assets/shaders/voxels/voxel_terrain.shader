HEADER
{
	Description = "GPU Voxel Terrain";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
}

COMMON
{
	#include "common/shared.hlsl"
	#include "shaders/voxels/voxel_sdf.hlsl"
	#include "shaders/voxels/transvoxel_regular_tables.hlsl"

	StructuredBuffer<uint> ActiveCells < Attribute( "ActiveCells" ); >;
	int3 ChunkCoordinate < Attribute( "ChunkCoordinate" ); >;
	float3 ChunkWorldOrigin < Attribute( "ChunkWorldOrigin" ); >;
	int CellsPerAxis < Attribute( "CellsPerAxis" ); >;
	float CellSize < Attribute( "CellSize" ); >;
	float SurfaceHeight < Attribute( "SurfaceHeight" ); >;
}

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

struct VS_INPUT
{
	float3 position : POSITION < Semantic( None ); >;
};

VS
{
	PixelInput MainVs( uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID )
	{
		uint activeCell = ActiveCells[instanceId];
		uint triangleCount = (activeCell >> 18) & 7;
		if ( vertexId >= triangleCount * 3 )
		{
			PixelInput paddedOutput = (PixelInput)0;
			paddedOutput.vPositionPs = float4( 2.0, 2.0, 2.0, 1.0 );
			paddedOutput.vNormalWs = float3( 0.0, 0.0, 1.0 );
			return paddedOutput;
		}

		uint caseIndex = activeCell >> 24;
		int3 localCell = int3(
			activeCell & 63,
			(activeCell >> 6) & 63,
			(activeCell >> 12) & 63 );

		uint cellClass = RegularCellClass[caseIndex];
		uint topologyVertex = RegularCellVertexIndices[cellClass * 15 + vertexId];
		uint edgeData = RegularVertexData[caseIndex * 12 + topologyVertex] & 255;
		uint corner0 = edgeData >> 4;
		uint corner1 = edgeData & 15;
		int3 globalCell = ChunkCoordinate * CellsPerAxis + localCell;
		int3 globalSample0 = globalCell + VoxelCornerOffset( corner0 );
		int3 globalSample1 = globalCell + VoxelCornerOffset( corner1 );
		float density0 = SampleVoxelSdf( globalSample0, CellSize, SurfaceHeight );
		float density1 = SampleVoxelSdf( globalSample1, CellSize, SurfaceHeight );
		float interpolation = saturate( density0 / (density0 - density1) );
		float3 localPosition = lerp(
			(float3)(localCell + VoxelCornerOffset( corner0 )) * CellSize,
			(float3)(localCell + VoxelCornerOffset( corner1 )) * CellSize,
			interpolation );
		float3 worldPosition = ChunkWorldOrigin + localPosition;
		float3 gradient = SampleVoxelSdfGradient( worldPosition, CellSize, SurfaceHeight );

		PixelInput output;
		output.vPositionWs = worldPosition - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( worldPosition );
		output.vNormalWs = normalize( gradient );
		return output;
	}
}

PS
{
	#include "common/pixel.hlsl"

	RenderState( CullMode, BACK );
	RenderState( DepthWriteEnable, true );

	float4 MainPs( PixelInput input ) : SV_Target0
	{
		Material material = Material::Init( input );
		material.Albedo = float3( 0.17, 0.42, 0.12 );
		material.Roughness = 0.9;
		material.Metalness = 0.0;
		return ShadingModelStandard::Shade( input, material );
	}
}
