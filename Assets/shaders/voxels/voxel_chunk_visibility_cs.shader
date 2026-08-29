MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	// Slab visibility preserves each record's vertex and instance offsets.
	#include "system.fxc"
}

CS
{
	#include "common.fxc"

	StructuredBuffer<float4> VisibilityBounds < Attribute( "VisibilityBounds" ); >;
	StructuredBuffer<uint4> SourceIndirectArguments < Attribute( "SourceIndirectArguments" ); >;
	RWStructuredBuffer<uint4> VisibleIndirectArguments < Attribute( "VisibleIndirectArguments" ); >;
	RWStructuredBuffer<uint> VisibilityFrameCounters < Attribute( "VisibilityFrameCounters" ); >;
	RWStructuredBuffer<uint> VisibilityAggregateCounters < Attribute( "VisibilityAggregateCounters" ); >;
	int VisibilitySlotCount < Attribute( "VisibilitySlotCount" ); >;
	int VisibilityPass < Attribute( "VisibilityPass" ); >;
	int MeasureVisibility < Attribute( "MeasureVisibility" ); >;
	int CaptureSettledDiagnostics < Attribute( "CaptureSettledDiagnostics" ); >;

	bool IsDefinitelyOutsideFrustum( float3 minimum, float3 maximum )
	{
		const float LateralGuardScale = 1.05;
		uint outsideCounts[6] = { 0, 0, 0, 0, 0, 0 };

		[unroll]
		for ( uint cornerIndex = 0; cornerIndex < 8; cornerIndex++ )
		{
			float3 corner = float3(
				(cornerIndex & 1) != 0 ? maximum.x : minimum.x,
				(cornerIndex & 2) != 0 ? maximum.y : minimum.y,
				(cornerIndex & 4) != 0 ? maximum.z : minimum.z );
			float4 clip = Position3WsToPs( corner );
			if ( any( isnan( clip ) ) || any( isinf( clip ) ) || abs( clip.w ) < 1e-6 )
			{
				return false;
			}

			float tolerance = max( 1.0, abs( clip.w ) ) * 1e-5;
			outsideCounts[0] += clip.x < -clip.w * LateralGuardScale - tolerance;
			outsideCounts[1] += clip.x > clip.w * LateralGuardScale + tolerance;
			outsideCounts[2] += clip.y < -clip.w * LateralGuardScale - tolerance;
			outsideCounts[3] += clip.y > clip.w * LateralGuardScale + tolerance;
			outsideCounts[4] += clip.z < -tolerance;
			outsideCounts[5] += clip.z > clip.w + tolerance;
		}

		[unroll]
		for ( uint planeIndex = 0; planeIndex < 6; planeIndex++ )
		{
			if ( outsideCounts[planeIndex] == 8 )
			{
				return true;
			}
		}

		return false;
	}

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
				}
				if ( CaptureSettledDiagnostics != 0 )
				{
					VisibilityAggregateCounters[6] = resident;
					VisibilityAggregateCounters[7] = VisibilityFrameCounters[2];
					VisibilityAggregateCounters[8] = VisibilityFrameCounters[3];
					VisibilityAggregateCounters[9] = VisibilityFrameCounters[4];
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
		float3 maximum = VisibilityBounds[slot * 2 + 1].xyz;
		uint4 sourceArguments = SourceIndirectArguments[slot];
		uint activeCellCount = sourceArguments.y;
		uint firstVertex = sourceArguments.z;
		bool active = minimumAndActive.w > 0.5;
		bool warm = minimumAndActive.w > 1.5;
		bool visible = active && activeCellCount > 0 &&
			!IsDefinitelyOutsideFrustum( minimumAndActive.xyz, maximum );

		VisibleIndirectArguments[slot] = uint4(
			15,
			visible ? activeCellCount : 0,
			firstVertex,
			sourceArguments.w );
		if ( active && activeCellCount > 0 )
		{
			InterlockedAdd( VisibilityFrameCounters[0], 1 );
			InterlockedAdd( VisibilityFrameCounters[3], activeCellCount );
			InterlockedMax( VisibilityFrameCounters[4], activeCellCount );
			if ( warm )
			{
				InterlockedAdd( VisibilityFrameCounters[2], 1 );
			}
			if ( visible )
			{
				InterlockedAdd( VisibilityFrameCounters[1], 1 );
			}
		}
	}
}
