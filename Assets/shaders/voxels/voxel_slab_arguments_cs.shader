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

	StructuredBuffer<uint> SlabActiveCellCounts < Attribute( "SlabActiveCellCounts" ); >;
	RWStructuredBuffer<uint4> SourceIndirectArguments < Attribute( "SourceIndirectArguments" ); >;
	int SlabGlobalSlotOffset < Attribute( "SlabGlobalSlotOffset" ); >;
	int SlabRegionCapacity < Attribute( "SlabRegionCapacity" ); >;

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 dispatchId : SV_DispatchThreadID )
	{
		uint localSlot = dispatchId.x;
		uint globalSlot = (uint)SlabGlobalSlotOffset + localSlot;
		uint firstVertex = localSlot * 15;
		SourceIndirectArguments[globalSlot] = uint4(
			15,
			SlabActiveCellCounts[localSlot],
			firstVertex,
			0 );
	}
}
