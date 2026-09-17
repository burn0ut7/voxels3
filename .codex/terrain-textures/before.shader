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
	CreateInputTexture2D( GrassHeight, Linear, 8, "", "_height", "Grass,10/50", Default( 1.0 ) );
	Texture2D GrassColorMap < Channel( RGB, Box( GrassColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D GrassNormalMap < Channel( RGB, Box( GrassNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D GrassSurfaceMap < Channel( R, Box( GrassRoughness ), Linear ); Channel( G, Box( GrassOcclusion ), Linear ); Channel( B, Box( GrassHeight ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	SamplerState GrassSampler < Filter( ANISOTROPIC ); MaxAniso( 16 ); AddressU( WRAP ); AddressV( WRAP ); >;
	// Metres; authored short-grass relief, not measured scan displacement.
	static const float GrassTileMetres = 1.4;
	static const float GrassReliefMetres = 0.02;

	float2 GrassParallax( float2 uv, float2 gradientX, float2 gradientY, float3 view, float strength )
	{
		strength *= smoothstep( 0.08, 0.25, view.z );
		if ( strength <= 0.0001 )
		{
			return uv;
		}
		int steps = (int)ceil( lerp( 24.0, 12.0, saturate( view.z ) ) );
		float depthStep = 1.0 / steps;
		float2 uvStep = view.xy / max( view.z, 0.15 )
			* (GrassReliefMetres / GrassTileMetres) * strength * depthStep;
		float2 previousUv = uv;
		float previousGap = 1.0 - GrassSurfaceMap.SampleGrad( GrassSampler, uv, gradientX, gradientY ).b;
		if ( previousGap <= 0.0 )
		{
			return uv;
		}
		// Height white is the top of the layer. Trace inward and refine the first
		// crossing rather than quantizing the visible depth to the march steps.
		for ( int step = 1; step <= 24; step++ )
		{
			float2 currentUv = uv - uvStep * step;
			float surfaceDepth = 1.0 - GrassSurfaceMap.SampleGrad( GrassSampler, currentUv, gradientX, gradientY ).b;
			float gap = surfaceDepth - step * depthStep;
			if ( gap <= 0.0 )
			{
				return lerp( previousUv, currentUv, saturate( previousGap / max( previousGap - gap, 0.00001 ) ) );
			}
			previousUv = currentUv;
			previousGap = gap;
			if ( step >= steps )
			{
				break;
			}
		}
		return previousUv;
	}

	struct GrassSample
	{
		float3 Color;
		float3 Normal;
		float2 Surface;
	};

	GrassSample SampleGrassProjection( float2 uv, float2 gradientX, float2 gradientY, float3 view, float relief, float contribution )
	{
		GrassSample result = (GrassSample)0;
		if ( contribution <= 0.0 )
		{
			return result;
		}
		// Shared lattice vertices have identical offsets on both sides of an edge.
		// Smooth weights vanish at the edge, so no hard per-tile UV reset is visible.
		float2 lattice = float2( uv.x - uv.y * 0.577350269, uv.y * 1.154700538 );
		float2 cell = floor( lattice );
		float2 fraction = frac( lattice );
		float3 blend;
		float2 vertex0;
		float2 vertex1;
		float2 vertex2;
		if ( fraction.x + fraction.y <= 1.0 )
		{
			blend = float3( 1.0 - fraction.x - fraction.y, fraction.x, fraction.y );
			vertex0 = cell;
			vertex1 = cell + float2( 1.0, 0.0 );
			vertex2 = cell + float2( 0.0, 1.0 );
		}
		else
		{
			blend = float3( fraction.x + fraction.y - 1.0, 1.0 - fraction.x, 1.0 - fraction.y );
			vertex0 = cell + 1.0;
			vertex1 = cell + float2( 0.0, 1.0 );
			vertex2 = cell + float2( 1.0, 0.0 );
		}
		blend *= blend;
		blend *= blend;
		// A continuous zero-support fringe avoids sampling invisible patches.
		blend = max( blend - 0.001, 0.0 );
		blend /= dot( blend, float3( 1.0, 1.0, 1.0 ) );
		for ( int patch = 0; patch < 3; patch++ )
		{
			if ( blend[patch] <= 0.0 )
			{
				continue;
			}
			float2 vertex = patch == 0 ? vertex0 : (patch == 1 ? vertex1 : vertex2);
			uint2 key = asuint( (int2)vertex );
			uint hash = (key.x * 0x8DA6B343u) ^ (key.y * 0xD8163841u);
			hash ^= hash >> 16;
			hash *= 0x7FEB352Du;
			hash ^= hash >> 15;
			float2 offset = float2( hash & 65535u, hash >> 16 ) / 65536.0;
			float2 patchUv = GrassParallax( uv + offset, gradientX, gradientY, view,
				relief * smoothstep( 0.0, 0.01, contribution ) );
			result.Color += blend[patch] * GrassColorMap.SampleGrad( GrassSampler, patchUv, gradientX, gradientY ).rgb;
			result.Surface += blend[patch] * GrassSurfaceMap.SampleGrad( GrassSampler, patchUv, gradientX, gradientY ).rg;
			result.Normal += blend[patch] * (GrassNormalMap.SampleGrad( GrassSampler, patchUv, gradientX, gradientY ).xyz * 2.0 - 1.0);
		}
		return result;
	}

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
		float3 grassPosition = worldPosition / (GrassTileMetres / 0.0254);
		float3 projection = max( pow( axis, 4.0 ) - 0.001, 0.0 );
		projection /= max( dot( projection, float3( 1.0, 1.0, 1.0 ) ), 0.0001 );
		// Compute gradients before divergent marching. All maps use the same ray
		// intersection and footprint, avoiding mip discontinuities between steps.
		float3 gradientX = ddx( grassPosition );
		float3 gradientY = ddy( grassPosition );
		float3 toCamera = g_vCameraPositionWs - worldPosition;
		float distanceMetres = length( toCamera ) * 0.0254;
		float3 view = normalize( toCamera );
		float grassWeight = saturate( weights.x );
		float relief = (1.0 - smoothstep( 2.0, 8.0, distanceMetres )) * grassWeight;
		GrassSample grassX = SampleGrassProjection( grassPosition.yz, gradientX.yz, gradientY.yz,
			float3( view.yz, view.x * sign( normal.x ) ), relief, projection.x );
		GrassSample grassY = SampleGrassProjection( grassPosition.xz, gradientX.xz, gradientY.xz,
			float3( view.xz, view.y * sign( normal.y ) ), relief, projection.y );
		GrassSample grassZ = SampleGrassProjection( grassPosition.xy, gradientX.xy, gradientY.xy,
			float3( view.xy, view.z * sign( normal.z ) ), relief, projection.z );
		float3 grassColor = grassX.Color * projection.x + grassY.Color * projection.y + grassZ.Color * projection.z;
		float2 grassSurface = grassX.Surface * projection.x + grassY.Surface * projection.y + grassZ.Surface * projection.z;
		float3 detailX = grassX.Normal;
		float3 detailY = grassY.Normal;
		float3 detailZ = grassZ.Normal;
		// OpenGL normals follow each projection's positive U/V axes. Whiteout
		// blending preserves the geometric normal when the detail map is flat.
		float3 grassNormal = normalize(
			float3( normal.x * detailX.z, normal.y + detailX.x, normal.z + detailX.y ) * projection.x
			+ float3( normal.x + detailY.x, normal.y * detailY.z, normal.z + detailY.y ) * projection.y
			+ float3( normal.x + detailZ.x, normal.y + detailZ.y, normal.z * detailZ.z ) * projection.z );
		material.Albedo += grassColor * grassWeight;
		material.Normal = normalize( lerp( normal, grassNormal, grassWeight ) );
		// The source has glossy blade values; this terrain represents dry grass.
		float grassRoughness = lerp( 0.85, 1.0, saturate( grassSurface.r ) );
		material.Roughness = lerp( material.Roughness, grassRoughness, grassWeight );
		material.AmbientOcclusion = lerp( 1.0, grassSurface.g, grassWeight );
		return ShadingModelStandard::Shade( input, material );
	}
}
