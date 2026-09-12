HEADER
{
	Description = "Batched GPU Terrain Depth and Shadows";
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	Depth( S_MODE_DEPTH );
}
COMMON
{
	#include "common/shared.hlsl"
}
struct VertexInput
{
	uint VertexId : SV_VertexID;
	uint InstanceId : SV_InstanceID;
	float3 Position : POSITION < Semantic( None ); >;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
};
VS
{
	#include "shaders/voxels/voxel_terrain_normal.hlsl"
	struct TerrainVertex
	{
		uint4 First;
		uint3 Second;
	};
	StructuredBuffer<TerrainVertex> DepthVertices < Attribute( "DepthVertices" ); >;
	StructuredBuffer<uint> DepthIndices < Attribute( "DepthIndices" ); >;
	StructuredBuffer<uint4> DepthRanges < Attribute( "DepthRanges" ); >;
	PixelInput MainVs( const VertexInput input )
	{
		uint low = 0;
		uint high = 512;
		while ( low < high )
		{
			uint middle = (low + high) / 2;
			if ( DepthRanges[middle].x <= input.InstanceId )
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}
		uint4 range = DepthRanges[low];
		uint firstTriangle = low > 0 ? DepthRanges[low - 1].x : 0;
		uint index = DepthIndices[range.y + (input.InstanceId - firstTriangle) * 3 + input.VertexId];
		TerrainVertex vertex = DepthVertices[range.z + index];
		float3 position = asfloat( vertex.First.xyz );
		PixelInput output;
		output.vPositionWs = position - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( position );
		output.vNormalWs = DecodeTerrainNormal( asfloat( vertex.Second.xy ) );
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
		return DepthNormals::Output( normalize( input.vNormalWs ), 0.9, 1.0 );
	}
}
