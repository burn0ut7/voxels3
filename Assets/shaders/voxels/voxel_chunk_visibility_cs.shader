MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	// Indexed visibility preserves each arena record's index and vertex offsets.
	#include "system.fxc"
}

CS
{
	#include "common.fxc"

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
	RWStructuredBuffer<IndexedArguments> VisibleIndirectArguments < Attribute( "VisibleIndirectArguments" ); >;
	RWStructuredBuffer<uint> VisibilityFrameCounters < Attribute( "VisibilityFrameCounters" ); >;
	RWStructuredBuffer<uint> VisibilityAggregateCounters < Attribute( "VisibilityAggregateCounters" ); >;
	int VisibilitySlotCount < Attribute( "VisibilitySlotCount" ); >;
	int VisibilityPass < Attribute( "VisibilityPass" ); >;
	int MeasureVisibility < Attribute( "MeasureVisibility" ); >;
	int CaptureSettledDiagnostics < Attribute( "CaptureSettledDiagnostics" ); >;

	#include "shaders/voxels/voxel_frustum.hlsl"

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 dispatchId : SV_DispatchThreadID )
	{
		if ( VisibilityPass != 0 )
		{
			if ( dispatchId.x == 0 )
			{
				uint resident = VisibilityFrameCounters[0];
				uint visible = VisibilityFrameCounters[1];
				if ( MeasureVisibility != 0 )
				{
					VisibilityAggregateCounters[0] += 1;
					VisibilityAggregateCounters[1] += resident;
					VisibilityAggregateCounters[2] += visible;
					VisibilityAggregateCounters[3] = min( VisibilityAggregateCounters[3], visible );
					VisibilityAggregateCounters[4] = max( VisibilityAggregateCounters[4], visible );
					VisibilityAggregateCounters[5] += VisibilityFrameCounters[2];
					[unroll]
					for ( uint level = 0; level < 7; level++ )
					{
						uint frameIndex = 5 + level * 2;
						uint aggregateIndex = 10 + level * 3;
						VisibilityAggregateCounters[aggregateIndex] += VisibilityFrameCounters[frameIndex];
						VisibilityAggregateCounters[aggregateIndex + 1] += VisibilityFrameCounters[frameIndex + 1];
					}
				}

				if ( CaptureSettledDiagnostics != 0 )
				{
					VisibilityAggregateCounters[6] = resident;
					VisibilityAggregateCounters[7] = VisibilityFrameCounters[2];
					VisibilityAggregateCounters[8] = VisibilityFrameCounters[3];
					VisibilityAggregateCounters[9] = VisibilityFrameCounters[4];
					[unroll]
					for ( uint level = 0; level < 7; level++ )
					{
						uint frameIndex = 5 + level * 2;
						uint aggregateIndex = 10 + level * 3;
						VisibilityAggregateCounters[aggregateIndex + 2] = VisibilityFrameCounters[frameIndex];
					}
				}
			}

			return;
		}

		uint slot = dispatchId.x;
		if ( slot >= (uint)VisibilitySlotCount )
		{
			return;
		}

		float4 minimumAndActive = VisibilityBounds[slot * 2];
		float4 maximumAndCellCount = VisibilityBounds[slot * 2 + 1];
		IndexedArguments source = SourceIndirectArguments[slot];
		float3 maximum = maximumAndCellCount.xyz;
		uint activeCellCount = (uint)round( maximumAndCellCount.w );
		uint category = (uint)round( minimumAndActive.w );
		bool active = category > 0;
		bool transition = category >= 100;
		uint level = transition ? category - 100 : (category - 1) / 2;
		bool warm = !transition && ((category - 1) & 1) != 0;
		bool visible = active && source.IndexCount > 0 &&
			!IsDefinitelyOutsideFrustum( minimumAndActive.xyz, maximum );

		source.InstanceCount = visible ? 1 : 0;
		VisibleIndirectArguments[slot] = source;
		if ( active && source.IndexCount > 0 )
		{
			InterlockedAdd( VisibilityFrameCounters[0], 1 );
			InterlockedAdd( VisibilityFrameCounters[3], activeCellCount );
			InterlockedMax( VisibilityFrameCounters[4], activeCellCount );
			if ( warm )
			{
				InterlockedAdd( VisibilityFrameCounters[2], 1 );
			}
			if ( !transition && level < 7 )
			{
				InterlockedAdd( VisibilityFrameCounters[5 + level * 2], 1 );
			}
			if ( visible )
			{
				InterlockedAdd( VisibilityFrameCounters[1], 1 );
				if ( !transition && level < 7 )
				{
					InterlockedAdd( VisibilityFrameCounters[6 + level * 2], 1 );
				}
			}
		}
	}
}
