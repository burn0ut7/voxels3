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
	RWStructuredBuffer<uint4> DepthRanges < Attribute( "DepthRanges" ); >;
	RWStructuredBuffer<IndexedArguments> DepthArguments < Attribute( "DepthArguments" ); >;
	uint DepthSlotOffset < Attribute( "DepthSlotOffset" ); >;
	// One group covers one 512-record geometry arena (GpuVoxelMesher.RegionsPerSlab).
	groupshared uint TriangleEnds[512];
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
		TriangleEnds[lane] = visible ? source.IndexCount / 3 : 0;
		GroupMemoryBarrierWithGroupSync();
		for ( uint offset = 1; offset < 512; offset <<= 1 )
		{
			uint addend = lane >= offset ? TriangleEnds[lane - offset] : 0;
			GroupMemoryBarrierWithGroupSync();
			TriangleEnds[lane] += addend;
			GroupMemoryBarrierWithGroupSync();
		}
		DepthRanges[lane] = uint4( TriangleEnds[lane], source.FirstIndex, source.BaseVertex, 0 );
		if ( lane == 511 )
		{
			IndexedArguments draw;
			draw.IndexCount = 3;
			draw.InstanceCount = TriangleEnds[lane];
			draw.FirstIndex = 0;
			draw.BaseVertex = 0;
			draw.FirstInstance = 0;
			DepthArguments[0] = draw;
		}
	}
}
