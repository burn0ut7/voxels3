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
	uint InstanceId : SV_InstanceID;
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

	float BoundaryDelta( float position, float extent, float cellSize, bool minimumActive, bool maximumActive )
	{
		float width = cellSize * 0.25;
		if ( minimumActive && position < cellSize )
		{
			return (1.0 - position / cellSize) * width;
		}
		if ( maximumActive && position > extent - cellSize )
		{
			return ((extent - cellSize - position) / cellSize) * width;
		}
		return 0.0;
	}

	PixelInput MainVs( const VertexInput input )
	{
		PixelInput output;
		float3 normal = normalize( input.Normal );
		float3 worldPosition = input.Position;
		uint descriptorOffset = input.InstanceId * 5u + 2u;
		float4 originAndCellSize = TerrainRecordDescriptors[descriptorOffset];
		float4 extentAndLod = TerrainRecordDescriptors[descriptorOffset + 1u];
		float4 identity = TerrainRecordDescriptors[descriptorOffset + 2u];
		uint publicationBank = (uint)clamp( ClipPublicationBank, 0, 1 );
		uint transitionMask = publicationBank == 0u ? asuint( identity.x ) : asuint( identity.y );
		uint meshKind = asuint( identity.w );
		if ( (uint)round( extentAndLod.w ) <= (uint)ClipMinimumLod ) transitionMask = 0u;
		if ( meshKind == 0u && transitionMask != 0u )
		{
			float3 localPosition = worldPosition - originAndCellSize.xyz;
			float3 delta;
			delta.x = BoundaryDelta(
				localPosition.x,
				extentAndLod.x,
				originAndCellSize.w,
				(transitionMask & 1u) != 0u,
				(transitionMask & 2u) != 0u );
			delta.y = BoundaryDelta(
				localPosition.y,
				extentAndLod.y,
				originAndCellSize.w,
				(transitionMask & 4u) != 0u,
				(transitionMask & 8u) != 0u );
			delta.z = BoundaryDelta(
				localPosition.z,
				extentAndLod.z,
				originAndCellSize.w,
				(transitionMask & 16u) != 0u,
				(transitionMask & 32u) != 0u );
			worldPosition += delta - normal * dot( normal, delta );
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
