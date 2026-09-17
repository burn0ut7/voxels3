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
	// Uses project Shadows/DirectionalLightShadow.hlsl; rebuild this entry after override changes.
	#include "common/shared.hlsl"
	float4 VoxelMaterialIds < Attribute( "VoxelMaterialIds" ); >;
	float VoxelCheckerSize < Attribute( "VoxelCheckerSize" ); >;
	float VoxelSandMaterialId < Attribute( "VoxelSandMaterialId" ); >;
	StructuredBuffer<float4> VoxelMaterialPalette < Attribute( "VoxelMaterialPalette" ); >;
}

struct VertexInput
{
	float3 Position : POSITION < Semantic( None ); >;
	float3 Normal : NORMAL < Semantic( None ); >;
	float4 Materials : COLOR0 < Semantic( None ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vMaterialWeights : TEXCOORD8;
};

VS
{
	#include "shaders/voxels/voxel_terrain_normal.hlsl"

	PixelInput MainVs( const VertexInput input )
	{
		PixelInput output;
		float3 normal = DecodeTerrainNormal( input.Normal.yz );
		output.vPositionWs = input.Position - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( input.Position );
		output.vNormalWs = normal;
		output.vMaterialWeights = input.Materials;
		return output;
	}
}

PS
{
	#include "common/pixel.hlsl"
	CreateInputTexture2D( GrassColor, Srgb, 8, "", "_color", "Grass,10/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( GrassNormal, Linear, 8, "NormalizeNormals", "_normal", "Grass,10/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( GrassRoughness, Linear, 8, "", "_rough", "Grass,10/30", Default( 0.9 ) );
	CreateInputTexture2D( GrassOcclusion, Linear, 8, "", "_ao", "Grass,10/40", Default( 1.0 ) );
	Texture2D GrassColorMap < Channel( RGB, Box( GrassColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D GrassNormalMap < Channel( RGB, Box( GrassNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D GrassSurfaceMap < Channel( R, Box( GrassRoughness ), Linear ); Channel( G, Box( GrassOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	SamplerState GrassSampler < Filter( ANISOTROPIC ); MaxAniso( 8 ); AddressU( WRAP ); AddressV( WRAP ); >;

	RenderState( CullMode, BACK );
	RenderState( DepthWriteEnable, true );
	float4 MainPs( PixelInput input ) : SV_Target0
	{
		Material material = Material::Init( input );
		float3 worldPosition = input.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 normal = normalize( input.vNormalWs );
		float3 axis = abs( normal );
		float2 coordinates = worldPosition.xy;
		if ( axis.x > axis.y && axis.x > axis.z )
		{
			coordinates = worldPosition.yz;
		}
		else if ( axis.y > axis.z )
		{
			coordinates = worldPosition.xz;
		}
		coordinates /= VoxelCheckerSize;
		float checker = frac( (floor( coordinates.x ) + floor( coordinates.y )) * 0.5 ) * 2.0;
		float2 footprint = fwidth( coordinates );
		checker = lerp( checker, 0.5, saturate( max( footprint.x, footprint.y ) ) );
		float4 weights = max( input.vMaterialWeights, 0.0 );
		float sand = saturate( 1.0 - dot( weights, float4( 1.0, 1.0, 1.0, 1.0 ) ) );
		float3 albedo = float3( 0.0, 0.0, 0.0 );
		for ( uint index = 1u; index < 4u; index++ )
		{
			uint id = (uint)VoxelMaterialIds[index];
			albedo += weights[index] * lerp( VoxelMaterialPalette[id * 2u].rgb, VoxelMaterialPalette[id * 2u + 1u].rgb, checker );
		}
		uint sandId = (uint)VoxelSandMaterialId;
		albedo += sand * lerp( VoxelMaterialPalette[sandId * 2u].rgb, VoxelMaterialPalette[sandId * 2u + 1u].rgb, checker );
		material.Albedo = albedo;
		material.Roughness = 0.9;
		material.Metalness = 0.0;

		// Grass004 covers 1.4 metres; engine world units are inches. World anchoring
		// and smooth triplanar weights keep the same mapping across chunk/LOD seams.
		float3 grassPosition = worldPosition / (1.4 / 0.0254);
		float3 projection = pow( axis, 4.0 );
		projection /= max( dot( projection, float3( 1.0, 1.0, 1.0 ) ), 0.0001 );
		float3 grassColor = GrassColorMap.Sample( GrassSampler, grassPosition.yz ).rgb * projection.x
			+ GrassColorMap.Sample( GrassSampler, grassPosition.xz ).rgb * projection.y
			+ GrassColorMap.Sample( GrassSampler, grassPosition.xy ).rgb * projection.z;
		float2 grassSurface = GrassSurfaceMap.Sample( GrassSampler, grassPosition.yz ).rg * projection.x
			+ GrassSurfaceMap.Sample( GrassSampler, grassPosition.xz ).rg * projection.y
			+ GrassSurfaceMap.Sample( GrassSampler, grassPosition.xy ).rg * projection.z;
		float3 detailX = GrassNormalMap.Sample( GrassSampler, grassPosition.yz ).xyz * 2.0 - 1.0;
		float3 detailY = GrassNormalMap.Sample( GrassSampler, grassPosition.xz ).xyz * 2.0 - 1.0;
		float3 detailZ = GrassNormalMap.Sample( GrassSampler, grassPosition.xy ).xyz * 2.0 - 1.0;
		// OpenGL normals follow each projection's positive U/V axes. Whiteout
		// blending preserves the geometric normal when the detail map is flat.
		float3 grassNormal = normalize(
			float3( normal.x * detailX.z, normal.y + detailX.x, normal.z + detailX.y ) * projection.x
			+ float3( normal.x + detailY.x, normal.y * detailY.z, normal.z + detailY.y ) * projection.y
			+ float3( normal.x + detailZ.x, normal.y + detailZ.y, normal.z * detailZ.z ) * projection.z );
		float grassWeight = saturate( weights.x );
		material.Albedo += grassColor * grassWeight;
		material.Normal = normalize( lerp( normal, grassNormal, grassWeight ) );
		// The source has glossy blade values; this terrain represents dry grass.
		float grassRoughness = lerp( 0.85, 1.0, saturate( grassSurface.r ) );
		material.Roughness = lerp( material.Roughness, grassRoughness, grassWeight );
		material.AmbientOcclusion = lerp( 1.0, grassSurface.g, grassWeight );
		return ShadingModelStandard::Shade( input, material );
	}
}
