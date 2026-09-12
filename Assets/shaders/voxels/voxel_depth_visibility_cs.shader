MODES
{
	Default();
}
FEATURES
{
}
COMMON
{
	#include "system.fxc"
}
CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_frustum.hlsl"
	struct IndexedArguments
	{
		uint IndexCount;
		uint InstanceCount;
		uint FirstIndex;
		int BaseVertex;
		uint FirstInstance;
	};
	StructuredBuffer<float4> VisibilityBounds < Attribute( "VisibilityBounds" ); >;
	StructuredBuffer<IndexedArguments> SourceIndirectArguments < Attribute( "SourceIndirectArguments" ); >;
	RWStructuredBuffer<uint4> DepthBlocks < Attribute( "DepthBlocks" ); >;
	RWStructuredBuffer<IndexedArguments> DepthArguments < Attribute( "DepthArguments" ); >;
	uint DepthSlotOffset < Attribute( "DepthSlotOffset" ); >;
	uint DepthIndicesPerBlock < Attribute( "DepthIndicesPerBlock" ); >;
	// One group covers one 512-record geometry arena (GpuVoxelMesher.RegionsPerSlab).
	groupshared uint BlockEnds[512];
	[numthreads( 512, 1, 1 )]
	void MainCs( uint3 threadId : SV_GroupThreadID )
	{
		uint lane = threadId.x;
		uint slot = DepthSlotOffset + lane;
		IndexedArguments source = SourceIndirectArguments[slot];
		float4 minimum = VisibilityBounds[slot * 2];
		float3 maximum = VisibilityBounds[slot * 2 + 1].xyz;
		bool visible = minimum.w > 0 && source.IndexCount > 0 &&
			!IsDefinitelyOutsideFrustum( minimum.xyz, maximum );
		uint blockCount = visible ? (source.IndexCount + DepthIndicesPerBlock - 1) / DepthIndicesPerBlock : 0;
		BlockEnds[lane] = blockCount;
		GroupMemoryBarrierWithGroupSync();
		for ( uint offset = 1; offset < 512; offset <<= 1 )
		{
			uint addend = lane >= offset ? BlockEnds[lane - offset] : 0;
			GroupMemoryBarrierWithGroupSync();
			BlockEnds[lane] += addend;
			GroupMemoryBarrierWithGroupSync();
		}
		uint firstBlock = BlockEnds[lane] - blockCount;
		for ( uint block = 0; block < blockCount; block++ )
		{
			uint indexOffset = block * DepthIndicesPerBlock;
			DepthBlocks[firstBlock + block] = uint4( source.FirstIndex + indexOffset,
				source.BaseVertex, min( DepthIndicesPerBlock, source.IndexCount - indexOffset ), 0 );
		}
		if ( lane == 511 )
		{
			IndexedArguments draw;
			draw.IndexCount = DepthIndicesPerBlock;
			draw.InstanceCount = BlockEnds[lane];
			draw.FirstIndex = 0;
			draw.BaseVertex = 0;
			draw.FirstInstance = 0;
			DepthArguments[0] = draw;
		}
	}
}
