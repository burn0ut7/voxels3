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
	StructuredBuffer<uint4> DepthBlocks < Attribute( "DepthBlocks" ); >;
	PixelInput MainVs( const VertexInput input )
	{
		uint4 block = DepthBlocks[input.InstanceId];
		if ( input.VertexId >= block.z )
		{
			// Padding belongs to the final partial block, never to terrain geometry.
			PixelInput clipped = (PixelInput)0;
			clipped.vPositionPs = float4( 2.0, 2.0, 2.0, 1.0 );
			return clipped;
		}
		uint index = DepthIndices[block.x + input.VertexId];
		TerrainVertex vertex = DepthVertices[block.y + index];
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
