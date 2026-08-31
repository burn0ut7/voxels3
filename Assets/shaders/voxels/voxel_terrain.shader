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
	StructuredBuffer<float4> TerrainRecordDescriptors < Attribute( "TerrainRecordDescriptors" ); >;
	int ClipPublicationBank < Attribute( "ClipPublicationBank" ); >;
	int ClipMinimumLod < Attribute( "ClipMinimumLod" ); >;

	static const uint BoundaryMask = 63u;

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

	float BoundaryDelta( float position, float extent, float cellSize )
	{
		float width = cellSize * 0.25;
		if ( position < cellSize )
		{
			return (1.0 - position / cellSize) * width;
		}
		if ( position > extent - cellSize )
		{
			return ((extent - cellSize - position) / cellSize) * width;
		}
		return 0.0;
	}

	uint BoundaryCellMask( float3 position, float3 extent, float cellSize )
	{
		uint mask = 0u;
		if ( position.x < cellSize ) mask |= 1u;
		if ( position.x > extent.x - cellSize ) mask |= 2u;
		if ( position.y < cellSize ) mask |= 4u;
		if ( position.y > extent.y - cellSize ) mask |= 8u;
		if ( position.z < cellSize ) mask |= 16u;
		if ( position.z > extent.z - cellSize ) mask |= 32u;
		return mask;
	}

	uint BoundaryVertexMask( float3 position, float3 extent, float cellSize )
	{
		float epsilon = max( cellSize * 0.00001, 0.0001 );
		uint mask = 0u;
		if ( abs( position.x ) <= epsilon ) mask |= 1u;
		if ( abs( position.x - extent.x ) <= epsilon ) mask |= 2u;
		if ( abs( position.y ) <= epsilon ) mask |= 4u;
		if ( abs( position.y - extent.y ) <= epsilon ) mask |= 8u;
		if ( abs( position.z ) <= epsilon ) mask |= 16u;
		if ( abs( position.z - extent.z ) <= epsilon ) mask |= 32u;
		return mask;
	}

	PixelInput MainVs( const VertexInput input )
	{
		PixelInput output;
		uint recordId = (uint)floor( input.Normal.x );
		bool transitionLowResolutionSide = frac( input.Normal.x ) >= 0.25;
		float3 normal = DecodeTerrainNormal( input.Normal.yz );
		float3 worldPosition = input.Position;
		uint descriptorOffset = recordId * 5u + 2u;
		float4 originAndCellSize = TerrainRecordDescriptors[descriptorOffset];
		float4 extentAndLod = TerrainRecordDescriptors[descriptorOffset + 1u];
		float4 identity = TerrainRecordDescriptors[descriptorOffset + 2u];
		uint publicationBank = (uint)clamp( ClipPublicationBank, 0, 1 );
		uint transitionMask = publicationBank == 0u ? asuint( identity.x ) : asuint( identity.y );
		uint meshKind = asuint( identity.w );
		if ( (uint)round( extentAndLod.w ) <= (uint)ClipMinimumLod ) transitionMask = 0u;
		bool canUseSecondaryPosition = meshKind == 0u ||
			(meshKind == 1u && transitionLowResolutionSide);
		if ( canUseSecondaryPosition && transitionMask != 0u )
		{
			float3 localPosition = worldPosition - originAndCellSize.xyz;
			uint cellBorderMask = BoundaryCellMask(
				localPosition, extentAndLod.xyz, originAndCellSize.w );
			uint vertexBorderMask = BoundaryVertexMask(
				localPosition, extentAndLod.xyz, originAndCellSize.w );
			bool useSecondaryPosition = (transitionMask & cellBorderMask) != 0u &&
				(vertexBorderMask & (~transitionMask & BoundaryMask)) == 0u;
			if ( useSecondaryPosition )
			{
				float3 delta = float3(
					BoundaryDelta( localPosition.x, extentAndLod.x, originAndCellSize.w ),
					BoundaryDelta( localPosition.y, extentAndLod.y, originAndCellSize.w ),
					BoundaryDelta( localPosition.z, extentAndLod.z, originAndCellSize.w ) );
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
