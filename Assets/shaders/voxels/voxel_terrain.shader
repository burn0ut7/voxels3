HEADER
{
	Description = "Persistent GPU Voxel Terrain";
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
	// Persistent geometry supplies final world-space positions and normals.
	#include "common/shared.hlsl"
	StructuredBuffer<float4> TerrainRecordDescriptors < Attribute( "TerrainRecordDescriptors" ); >;
	int VisibilitySlotCount < Attribute( "VisibilitySlotCount" ); >;
}

struct VertexInput
{
	float3 Position : POSITION < Semantic( None ); >;
	float3 Normal : NORMAL < Semantic( None ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	static const uint BoundaryMask = 63u;
	static const uint GenerationTokenMask = 3u;
	static const uint RecordSlotMask = 0x001fffffu;
	static const uint RecordGenerationTokenShift = 21u;
	static const uint RecordIdentitySignatureMask = 0xff800000u;
	static const uint RecordIdentitySignature = 0x3f800000u;

	float3 DecodeTerrainNormal( float2 encoded )
	{
		float3 normal = float3(
			encoded.x,
			encoded.y,
			1.0 - abs( encoded.x ) - abs( encoded.y ) );
		if ( normal.z < 0.0 )
		{
			float2 signValue = float2(
				normal.x >= 0.0 ? 1.0 : -1.0,
				normal.y >= 0.0 ? 1.0 : -1.0 );
			normal.xy = (1.0 - abs( normal.yx )) * signValue;
		}
		return normalize( normal );
	}

	float BoundaryDelta( float position, float extent, float cellSize, bool minimumFace )
	{
		float width = cellSize * 0.25;
		if ( minimumFace && position < cellSize ) return saturate( 1.0 - position / cellSize ) * width;
		if ( !minimumFace && position > extent - cellSize )
			return -saturate( (position - (extent - cellSize)) / cellSize ) * width;
		return 0.0;
	}

	uint BoundaryCellMask( float3 localPosition, float3 extent, float cellSize )
	{
		uint mask = 0u;
		if ( localPosition.x < cellSize ) mask |= 1u;
		if ( localPosition.x > extent.x - cellSize ) mask |= 2u;
		if ( localPosition.y < cellSize ) mask |= 4u;
		if ( localPosition.y > extent.y - cellSize ) mask |= 8u;
		if ( localPosition.z < cellSize ) mask |= 16u;
		if ( localPosition.z > extent.z - cellSize ) mask |= 32u;
		return mask;
	}

	uint BoundaryVertexMask( float3 localPosition, float3 extent, float cellSize )
	{
		float epsilon = max( cellSize * 0.00001, 0.0001 );
		uint mask = 0u;
		if ( abs( localPosition.x ) <= epsilon ) mask |= 1u;
		if ( abs( localPosition.x - extent.x ) <= epsilon ) mask |= 2u;
		if ( abs( localPosition.y ) <= epsilon ) mask |= 4u;
		if ( abs( localPosition.y - extent.y ) <= epsilon ) mask |= 8u;
		if ( abs( localPosition.z ) <= epsilon ) mask |= 16u;
		if ( abs( localPosition.z - extent.z ) <= epsilon ) mask |= 32u;
		return mask;
	}

	PixelInput MainVs( const VertexInput input )
	{
		PixelInput output;
		uint encodedRecordIdentity = asuint( input.Normal.x );
		uint recordId = encodedRecordIdentity & RecordSlotMask;
		uint vertexGenerationToken =
			(encodedRecordIdentity >> RecordGenerationTokenShift) & GenerationTokenMask;
		bool vertexIdentityValid =
			(encodedRecordIdentity & RecordIdentitySignatureMask) == RecordIdentitySignature &&
			recordId < (uint)VisibilitySlotCount;
		float3 normal = DecodeTerrainNormal( input.Normal.yz );
		float4 originAndCellSize = 0.0;
		float4 extentAndState = 0.0;
		if ( vertexIdentityValid )
		{
			originAndCellSize = TerrainRecordDescriptors[recordId * 2u];
			extentAndState = TerrainRecordDescriptors[recordId * 2u + 1u];
		}
		float3 worldPosition = input.Position;
		uint descriptorState = (uint)round( extentAndState.w );
		uint descriptorGenerationToken = (descriptorState >> 6u) & GenerationTokenMask;
		uint transitionMask = descriptorState & BoundaryMask;
		float3 localPosition = worldPosition - originAndCellSize.xyz;
		float extentTolerance = max( originAndCellSize.w * 0.001, 0.001 );
		bool descriptorOwnsVertex = vertexIdentityValid &&
			descriptorGenerationToken == vertexGenerationToken &&
			originAndCellSize.w > 0.0 && all( extentAndState.xyz > 0.0 ) &&
			all( localPosition >= -extentTolerance ) &&
			all( localPosition <= extentAndState.xyz + extentTolerance );
		if ( transitionMask != 0u && descriptorOwnsVertex )
		{
			uint cellBorderMask = BoundaryCellMask( localPosition, extentAndState.xyz, originAndCellSize.w );
			uint vertexBorderMask = BoundaryVertexMask( localPosition, extentAndState.xyz, originAndCellSize.w );
			if ( (transitionMask & cellBorderMask) != 0u &&
				(vertexBorderMask & (~transitionMask & BoundaryMask)) == 0u )
			{
				float3 delta = float3(
					(transitionMask & 1u) != 0u ? BoundaryDelta( localPosition.x, extentAndState.x, originAndCellSize.w, true ) :
						((transitionMask & 2u) != 0u ? BoundaryDelta( localPosition.x, extentAndState.x, originAndCellSize.w, false ) : 0.0),
					(transitionMask & 4u) != 0u ? BoundaryDelta( localPosition.y, extentAndState.y, originAndCellSize.w, true ) :
						((transitionMask & 8u) != 0u ? BoundaryDelta( localPosition.y, extentAndState.y, originAndCellSize.w, false ) : 0.0),
					(transitionMask & 16u) != 0u ? BoundaryDelta( localPosition.z, extentAndState.z, originAndCellSize.w, true ) :
						((transitionMask & 32u) != 0u ? BoundaryDelta( localPosition.z, extentAndState.z, originAndCellSize.w, false ) : 0.0) );
				worldPosition += delta - normal * dot( normal, delta );
			}
		}
		output.vPositionWs = worldPosition - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( worldPosition );
		output.vNormalWs = normal;
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
		float3 worldPosition = input.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float checker = frac( (floor( worldPosition.x / 256.0 ) + floor( worldPosition.y / 256.0 )) * 0.5 ) * 2.0;
		material.Albedo = lerp( float3( 0.14, 0.32, 0.08 ), float3( 0.26, 0.52, 0.14 ), checker );
		material.Roughness = 0.9;
		material.Metalness = 0.0;
		return ShadingModelStandard::Shade( input, material );
	}
}
