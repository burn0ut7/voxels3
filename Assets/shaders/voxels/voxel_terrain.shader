HEADER
{
	Description = "Persistent GPU Voxel Terrain";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_CLAY_SURFACE, 0..1, "Independent clay surface" );
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
}

struct VertexInput
{
	float3 Position : POSITION < Semantic( None ); >;
	float3 Normal : NORMAL < Semantic( None ); >;
	float4 Materials : COLOR0 < Semantic( None ); >;
	float Gravel : TEXCOORD0 < Semantic( None ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vMaterialWeights : TEXCOORD8;
	float3 vBiomeCover : TEXCOORD9;
	float vGravelWeight : TEXCOORD10;
};

VS
{
	#include "shaders/voxels/voxel_terrain_normal.hlsl"
	#include "shaders/voxels/voxel_terrain_noise.hlsl"
	#include "shaders/voxels/voxel_landform_noise.hlsl"
	#include "shaders/voxels/voxel_biomes.hlsl"
	float VoxelBiomeSeed < Attribute( "VoxelBiomeSeed" ); >;
	float VoxelBiomeSeaLevel < Attribute( "VoxelBiomeSeaLevel" ); >;

	PixelInput MainVs( const VertexInput input )
	{
		PixelInput output;
		float3 normal = DecodeTerrainNormal( input.Normal.yz );
		output.vPositionWs = input.Position - g_vHighPrecisionLightingOffsetWs.xyz;
		output.vPositionPs = Position3WsToPs( input.Position );
		output.vNormalWs = normal;
		output.vMaterialWeights = input.Materials;
		output.vGravelWeight = input.Gravel;
		float2 climate;
		// Surface colour follows regional climate on grass/soil, independently of
		// landform labels. Evaluate per vertex; no per-pixel climate sampling.
		output.vBiomeCover.xy = SampleVoxelBiome( input.Position.xy,
			float4( VoxelBiomeSeed, 0.0, 0.0, 0.0 ), 0.0, climate ).zw;
		// Presentation wetness follows surface altitude; classification uses natural height.
		output.vBiomeCover.z = VoxelMarshWeight( climate, input.Position.z, 0.0, VoxelBiomeSeaLevel );
		return output;
	}
}

PS
{
	StaticCombo( S_CLAY_SURFACE, F_CLAY_SURFACE, Sys( PC ) );
	Texture2D BiomeDebugMap < Attribute( "BiomeDebugMap" ); >;
	float4 BiomeDebugBounds < Attribute( "BiomeDebugBounds" ); >;
	int BiomeDebugEnabled < Attribute( "BiomeDebugEnabled" ); >;
	int BiomeDebugSmooth < Attribute( "BiomeDebugSmooth" ); >;
	SamplerState BiomeDebugPoint < Filter( POINT ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	SamplerState BiomeDebugLinear < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	#include "common/pixel.hlsl"
	#include "shaders/voxels/voxel_material_blending.hlsl"
	#include "voxels/voxel_terrain_pattern.hlsl"
	#include "voxels/voxel_stone_pattern.hlsl"
	CreateInputTexture2D( GrassColor, Srgb, 8, "", "_color", "Grass,10/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( GrassNormal, Linear, 8, "", "_normal", "Grass,10/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( GrassRoughness, Linear, 8, "", "_rough", "Grass,10/30", Default( 0.9 ) );
	CreateInputTexture2D( GrassOcclusion, Linear, 8, "", "_ao", "Grass,10/40", Default( 1.0 ) );
	CreateInputTexture2D( GrassHeight, Linear, 16, "", "_height", "Grass,10/50", Default( 1.0 ) );
	Texture2D GrassColorMap < Channel( RGB, Box( GrassColor ), Srgb ); Channel( A, Box( GrassRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D GrassNormalMap < Channel( RGB, Box( GrassNormal ), Linear ); Channel( A, Box( GrassOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D GrassHeightMap < Channel( R, Box( GrassHeight ), Linear ); OutputFormat( R16F ); SrgbRead( false ); >;
	CreateInputTexture2D( DirtColor, Srgb, 8, "", "_color", "Dirt,11/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( DirtNormal, Linear, 8, "", "_normal", "Dirt,11/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( DirtRoughness, Linear, 8, "", "_rough", "Dirt,11/30", Default( 0.9 ) );
	CreateInputTexture2D( DirtOcclusion, Linear, 8, "", "_ao", "Dirt,11/40", Default( 1.0 ) );
	CreateInputTexture2D( DirtHeight, Linear, 16, "", "_height", "Dirt,11/50", Default( 1.0 ) );
	Texture2D DirtColorMap < Channel( RGB, Box( DirtColor ), Srgb ); Channel( A, Box( DirtRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D DirtNormalMap < Channel( RGB, Box( DirtNormal ), Linear ); Channel( A, Box( DirtOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D DirtHeightMap < Channel( R, Box( DirtHeight ), Linear ); OutputFormat( R16F ); SrgbRead( false ); >;
	CreateInputTexture2D( StoneColor, Srgb, 8, "", "_color", "Stone,12/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( StoneNormal, Linear, 8, "", "_normal", "Stone,12/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( StoneRoughness, Linear, 8, "", "_rough", "Stone,12/30", Default( 0.9 ) );
	CreateInputTexture2D( StoneOcclusion, Linear, 8, "", "_ao", "Stone,12/40", Default( 1.0 ) );
	CreateInputTexture2D( StoneHeight, Linear, 16, "", "_height", "Stone,12/50", Default( 1.0 ) );
	Texture2D StoneColorMap < Channel( RGB, Box( StoneColor ), Srgb ); Channel( A, Box( StoneRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D StoneNormalMap < Channel( RGB, Box( StoneNormal ), Linear ); Channel( A, Box( StoneOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D StoneHeightMap < Channel( R, Box( StoneHeight ), Linear ); OutputFormat( R16F ); SrgbRead( false ); >;
	CreateInputTexture2D( SandColor, Srgb, 8, "", "_color", "Sand,13/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( SandNormal, Linear, 8, "", "_normal", "Sand,13/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SandRoughness, Linear, 8, "", "_rough", "Sand,13/30", Default( 0.9 ) );
	CreateInputTexture2D( SandOcclusion, Linear, 8, "", "_ao", "Sand,13/40", Default( 1.0 ) );
	CreateInputTexture2D( SandHeight, Linear, 16, "", "_height", "Sand,13/50", Default( 1.0 ) );
	Texture2D SandColorMap < Channel( RGB, Box( SandColor ), Srgb ); Channel( A, Box( SandRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D SandNormalMap < Channel( RGB, Box( SandNormal ), Linear ); Channel( A, Box( SandOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D SandHeightMap < Channel( R, Box( SandHeight ), Linear ); OutputFormat( R16F ); SrgbRead( false ); >;
	CreateInputTexture2D( SnowColor, Srgb, 8, "", "_color", "Snow,14/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( SnowNormal, Linear, 8, "", "_normal", "Snow,14/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SnowRoughness, Linear, 8, "", "_rough", "Snow,14/30", Default( 0.9 ) );
	CreateInputTexture2D( SnowOcclusion, Linear, 8, "", "_ao", "Snow,14/40", Default( 1.0 ) );
	CreateInputTexture2D( SnowHeight, Linear, 16, "", "_height", "Snow,14/50", Default( 1.0 ) );
	Texture2D SnowColorMap < Channel( RGB, Box( SnowColor ), Srgb ); Channel( A, Box( SnowRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D SnowNormalMap < Channel( RGB, Box( SnowNormal ), Linear ); Channel( A, Box( SnowOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D SnowHeightMap < Channel( R, Box( SnowHeight ), Linear ); OutputFormat( R16F ); SrgbRead( false ); >;
	SamplerState TerrainSampler < Filter( ANISOTROPIC ); MaxAniso( 16 ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState TerrainHeightSampler < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( GravelColor, Srgb, 8, "", "_color", "Gravel,15/10", Default3( 0.45, 0.45, 0.45 ) );
	CreateInputTexture2D( GravelHeight, Linear, 8, "", "_height", "Gravel,15/20", Default( 0.5 ) );
	Texture2D GravelColorMap < Channel( RGB, Box( GravelColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D GravelHeightMap < Channel( R, Box( GravelHeight ), Linear ); OutputFormat( R16F ); SrgbRead( false ); >;
	CreateInputTexture2D( ClayColor, Srgb, 8, "", "_color", "Clay,16/10", Default3( 0.46, 0.51, 0.56 ) );
	CreateInputTexture2D( ClayHeight, Linear, 8, "", "_height", "Clay,16/20", Default( 0.5 ) );
	Texture2D ClayColorMap < Channel( RGB, Box( ClayColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D ClayHeightMap < Channel( R, Box( ClayHeight ), Linear ); OutputFormat( R16F ); SrgbRead( false ); >;
	// Clay is independent art: half-metre tiling, eight-millimetre relief.
	#define TerrainClayTileMetres 0.5
	#define TerrainClayAmplitudeMetres 0.008
	#define TerrainClaySourceTexels 1254.0
	#define TerrainClayMinimumLod 2.0
	// Gravel height and derived normal slopes share these physical units.
	#define TerrainGravelTileMetres 0.8
	#define TerrainGravelAmplitudeMetres 0.035
	#define TerrainGravelSourceTexels 1254.0
	#define TerrainGravelMinimumLod 2.0
	// Physical source tiling and authored peak-to-trough relief, in metres.
	#define TerrainReliefTileMetres float2( 3.0, TerrainStoneTileMetres )
	// Authored amplitude owns ray extent and height-derived normal scale.
	#define TerrainReliefAuthoredAmplitudeMetres float2( 0.120, TerrainStoneAmplitudeMetres )
	#define TerrainReliefRayStrength 1.0
	#define TerrainReliefAmplitudeMetres (TerrainReliefAuthoredAmplitudeMetres * TerrainReliefRayStrength)
	#define TerrainReliefProjectionContrast 32.0
	#include "shaders/voxels/voxel_terrain_relief.hlsl"
	struct TerrainSample
	{
		float3 Color;
		float3 Normal;
		float2 Surface;
	};

	float3 TerrainNormalSlope( float3 encoded )
	{
		float3 tangentNormal = (encoded * 2.0 - 1.0) * float3( 1.0, -1.0, 1.0 );
		// A height field cannot represent back-facing scan normals. Bound the
		// slope for those texels and compression noise near the horizon.
		return float3( tangentNormal.xy / max( tangentNormal.z, 0.1 ), 0.0 );
	}

	float3 MapTerrainNormals( float3 x, float3 y, float3 z, float3 normal, float3 projection )
	{
		// Blend negative height gradients, not unit normal vectors. Keep the
		// result unnormalized through material blending; normalize only once.
		float3 slope = float3( 0.0, x.x, x.y ) * projection.x
			+ float3( y.x, 0.0, y.y ) * projection.y
			+ float3( z.x, z.y, 0.0 ) * projection.z;
		return normal + slope - normal * dot( normal, slope );
	}

	TerrainSample SampleTerrainProjection( Texture2D colorMap, Texture2D normalMap, float2 uv, float2 anchor, float2 gradientX, float2 gradientY, float contribution, int mapping )
	{
		TerrainSample result = (TerrainSample)0;
		[branch]
		if ( contribution <= 0.0 )
		{
			return result;
		}
		// Sand's larger periodic field shares its coordinates across every channel.
		float period = mapping == 3 ? TerrainSandPatternPeriod : TerrainPatternPeriod;
		float2 cacheUv = float2( uv.x - uv.y * 0.577350269, uv.y * 1.154700538 ) / period;
		cacheUv += frac( float2( anchor.x - anchor.y * 0.577350269, anchor.y * 1.154700538 ) / period );
		float2 cacheDx = float2( gradientX.x - gradientX.y * 0.577350269, gradientX.y * 1.154700538 ) / period;
		float2 cacheDy = float2( gradientY.x - gradientY.y * 0.577350269, gradientY.y * 1.154700538 ) / period;
		if ( mapping == 2 )
		{
			cacheUv = uv + frac( anchor );
			cacheDx = gradientX;
			cacheDy = gradientY;
		}
		float4 colorRoughness = colorMap.SampleGrad( TerrainSampler, cacheUv, cacheDx, cacheDy );
		float4 normalOcclusion = normalMap.SampleGrad( TerrainSampler, cacheUv, cacheDx, cacheDy );
		result.Color = colorRoughness.rgb;
		result.Surface = float2( colorRoughness.a, normalOcclusion.a );
		result.Normal = TerrainNormalSlope( normalOcclusion.xyz );
		return result;
	}


	// One surface path: maps and physical tile size are material inputs.
	TerrainSample SampleTerrain( Texture2D colorMap, Texture2D normalMap,
		float tileMetres, float weight, float3 position, float3 gradientX,
		float3 gradientY, float3 normal, float3 projection, float detail, int mapping )
	{
		TerrainSample result = (TerrainSample)0;
		[branch]
		if ( weight <= 0.00001 )
		{
			return result;
		}
		TerrainSample distant = (TerrainSample)0;
		[branch]
		if ( detail < 1.0 )
		{
			// The coarsest mip preserves each material's average color and surface.
			// Two uniform reads replace all projection/patch/normal reads at distance.
			float4 colorRoughness = colorMap.SampleLevel( TerrainSampler, float2( 0.5, 0.5 ), 20.0 );
			float occlusion = normalMap.SampleLevel( TerrainSampler, float2( 0.5, 0.5 ), 20.0 ).a;
			distant.Color = colorRoughness.rgb;
			distant.Surface = float2( colorRoughness.a, occlusion );
			distant.Normal = normal;
		}
		[branch]
		if ( detail <= 0.0 )
		{
			return distant;
		}
		float scale = 0.0254 / tileMetres;
		float3 anchor = g_vHighPrecisionLightingOffsetWs.xyz * scale;
		position *= scale;
		gradientX *= scale;
		gradientY *= scale;
		TerrainSample sampleX = SampleTerrainProjection( colorMap, normalMap,
			position.yz, anchor.yz, gradientX.yz, gradientY.yz, projection.x, mapping );
		TerrainSample sampleY = SampleTerrainProjection( colorMap, normalMap,
			position.xz, anchor.xz, gradientX.xz, gradientY.xz, projection.y, mapping );
		TerrainSample sampleZ = SampleTerrainProjection( colorMap, normalMap,
			position.xy, anchor.xy, gradientX.xy, gradientY.xy, projection.z, mapping );
		result.Color = sampleX.Color * projection.x + sampleY.Color * projection.y + sampleZ.Color * projection.z;
		result.Surface = sampleX.Surface * projection.x + sampleY.Surface * projection.y + sampleZ.Surface * projection.z;
		// Source GL green points against increasing image V. The projection sampler
		// flips it before mapping texture axes onto world axes; flat maps preserve N.
		result.Normal = MapTerrainNormals( sampleX.Normal, sampleY.Normal, sampleZ.Normal, normal, projection );
		[branch]
		if ( detail < 1.0 )
		{
			result.Color = lerp( distant.Color, result.Color, detail );
			result.Surface = lerp( distant.Surface, result.Surface, detail );
			result.Normal = lerp( distant.Normal, result.Normal, detail );
		}
		return result;
	}

	#include "shaders/voxels/voxel_gravel_surface.hlsl"

	RenderState( CullMode, BACK );
	RenderState( DepthWriteEnable, true );
	float4 MainPs( PixelInput input ) : SV_Target0
	{
		Material material = Material::Init( input );
		float3 position = input.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 normal = normalize( input.vNormalWs );
		float3 projection = max( pow( abs( normal ) / max( max( abs( normal.x ), abs( normal.y ) ), abs( normal.z ) ), 16.0 ) - 0.001, 0.0 );
		projection /= dot( projection, float3( 1.0, 1.0, 1.0 ) );
		// Derivatives precede material/plane/patch branches.
		float3 gradientX = ddx( input.vPositionWithOffsetWs );
		float3 gradientY = ddy( input.vPositionWithOffsetWs );
		// Full nearby texture detail; distant terrain keeps material color and lighting.
		float distanceMetres = length( g_vCameraPositionWs - position ) * 0.0254;
		float detail = 1.0 - smoothstep( 64.0, 128.0, distanceMetres );
		#if S_CLAY_SURFACE
			float3 clayBaseLod = TerrainReliefBaseLod( gradientX * 0.0254, gradientY * 0.0254, 2 );
			float clayHitDepth;
			position = TerrainReliefPosition( input.vPositionWithOffsetWs, gradientX, gradientY,
				normal, projection, float4( 0.0, 0.0, 0.0, 0.0 ), 0.0, 0.0, 1.0,
				clayBaseLod, clayBaseLod, clayBaseLod, clayHitDepth );
			float3 clayProjection = projection;
			if ( detail > 0.0 )
			{
				clayProjection = TerrainReliefProjection( ClayHeightMap, TerrainClayTileMetres,
					TerrainClaySourceTexels, TerrainClayMinimumLod, position, clayBaseLod, projection, 2 );
			}
			TerrainSample clay = SampleHeightTerrain( ClayColorMap, ClayHeightMap,
				TerrainClayTileMetres, TerrainClayAmplitudeMetres, TerrainClaySourceTexels,
				TerrainClayMinimumLod, 0.88, 0.92, position, gradientX, gradientY,
				normal, clayProjection, clayBaseLod, detail );
			material.Albedo = clay.Color;
			material.Normal = normalize( clay.Normal );
			material.Roughness = clay.Surface.r;
			material.AmbientOcclusion = clay.Surface.g;
			material.Metalness = 0.0;
			return ShadingModelStandard::Shade( input, material );
		#else
		float gravelWeight = input.vGravelWeight;
		float4 weights = BlendVoxelMaterials( input.vMaterialWeights, gravelWeight, position, max( length( gradientX ), length( gradientY ) ) );
		float total = dot( weights, float4( 1.0, 1.0, 1.0, 1.0 ) ) + gravelWeight;
		float sandWeight = saturate( 1.0 - total );
		weights /= max( total, 1.0 );
		gravelWeight /= max( total, 1.0 );
		float3 flatPosition = input.vPositionWithOffsetWs;
		float hitDepth;
		float3 reliefBaseLod = TerrainReliefBaseLod( gradientX * 0.0254, gradientY * 0.0254, 2 );
		float3 patternBaseLod = TerrainReliefBaseLod( gradientX * 0.0254, gradientY * 0.0254, 1 );
		float3 sandBaseLod = TerrainReliefBaseLod( gradientX * 0.0254, gradientY * 0.0254, 3 );
		position = TerrainReliefPosition( flatPosition, gradientX, gradientY, normal, projection, float4( weights.yz, weights.x, sandWeight ), weights.w, gravelWeight, 0.0, reliefBaseLod, patternBaseLod, sandBaseLod, hitDepth );
		float3 dirtProjection = projection;
		[branch]
		if ( weights.y > 0.00001 && detail > 0.0 )
		{
			dirtProjection = TerrainReliefProjection( DirtHeightMap, TerrainReliefTileMetres.x, 2048.0, 2.0, position, reliefBaseLod, projection, 2 );
		}
		float3 stoneProjection = projection;
		[branch]
		if ( weights.z > 0.00001 && detail > 0.0 )
		{
			stoneProjection = TerrainReliefProjection( StoneHeightMap, TerrainReliefTileMetres.y, TerrainStoneSourceTexels, TerrainStoneMinimumLod, position, reliefBaseLod, projection, 2 );
		}
		float3 grassProjection = projection;
		[branch]
		if ( weights.x > 0.00001 && detail > 0.0 )
		{
			grassProjection = TerrainReliefProjection( GrassHeightMap, TerrainGrassTileMetres, TerrainGrassSourceTexels, TerrainGrassMinimumLod, position, patternBaseLod, projection, 1 );
		}
		float3 sandProjection = projection;
		[branch]
		if ( sandWeight > 0.00001 && detail > 0.0 )
		{
			sandProjection = TerrainReliefProjection( SandHeightMap, TerrainSandTileMetres, TerrainSandSourceTexels, TerrainSandMinimumLod, position, sandBaseLod, projection, 3 );
		}
		float3 snowProjection = projection;
		[branch]
		if ( weights.w > 0.00001 && detail > 0.0 )
		{
			snowProjection = TerrainReliefProjection( SnowHeightMap, TerrainSnowTileMetres, TerrainSnowSourceTexels, TerrainSnowMinimumLod, position, patternBaseLod, projection, 1 );
		}
		material.Albedo = 0.0;
		material.Normal = 0.0;
		material.Roughness = 0.0;
		material.AmbientOcclusion = 0.0;
		material.Metalness = 0.0;
		TerrainSample grass = SampleTerrain( GrassColorMap, GrassNormalMap,
			TerrainGrassTileMetres, weights.x, position, gradientX, gradientY, normal, grassProjection, detail, 1 );
		float2 biomeCover = saturate( input.vBiomeCover.xy );
		float marsh = saturate( input.vBiomeCover.z );
		float3 grassClimateTint = (1.0 - biomeCover.x - biomeCover.y).xxx +
			biomeCover.x * float3( 0.80, 0.85, 0.65 ) + biomeCover.y * float3( 0.55, 0.85, 0.70 );
		grassClimateTint = lerp( grassClimateTint, float3( 0.65, 0.68, 0.39 ), marsh );
		material.Albedo += grass.Color * grassClimateTint * weights.x;
		material.Normal += grass.Normal * weights.x;
		material.Roughness += lerp( 0.85, 1.0, saturate( grass.Surface.r ) ) * weights.x;
		material.AmbientOcclusion += grass.Surface.g * weights.x;
		TerrainSample dirt = SampleTerrain( DirtColorMap, DirtNormalMap,
			TerrainReliefTileMetres.x, weights.y, position, gradientX, gradientY, normal, dirtProjection, detail, 2 );
		material.Albedo += dirt.Color * (1.0 - 0.1 * biomeCover.x - 0.25 * biomeCover.y) * lerp( float3( 1.0, 1.0, 1.0 ), float3( 0.43, 0.39, 0.28 ), marsh ) * weights.y;
		material.Normal += dirt.Normal * weights.y;
		material.Roughness += lerp( clamp( dirt.Surface.r, 0.65, 1.0 ), 0.38, marsh ) * weights.y;
		material.AmbientOcclusion += dirt.Surface.g * weights.y;
		TerrainSample stone = SampleTerrain( StoneColorMap, StoneNormalMap,
			TerrainReliefTileMetres.y, weights.z, position, gradientX, gradientY, normal, stoneProjection, detail, 2 );
		// Retain a little mineral color while removing the source rock's brown cast.
		float stoneLuminance = dot( stone.Color, float3( 0.2126, 0.7152, 0.0722 ) );
		stone.Color = lerp( stoneLuminance.xxx, stone.Color, 0.15 );
		material.Albedo += stone.Color * weights.z;
		material.Normal += stone.Normal * weights.z;
		material.Roughness += lerp( 0.65, 1.0, saturate( stone.Surface.r ) ) * weights.z;
		material.AmbientOcclusion += stone.Surface.g * weights.z;
		TerrainSample snow = SampleTerrain( SnowColorMap, SnowNormalMap,
			TerrainSnowTileMetres, weights.w, position, gradientX, gradientY, normal, snowProjection, detail, 1 );
		material.Albedo += snow.Color * weights.w;
		material.Normal += snow.Normal * weights.w;
		material.Roughness += lerp( 0.8, 1.0, saturate( snow.Surface.r ) ) * weights.w;
		material.AmbientOcclusion += snow.Surface.g * weights.w;
		TerrainSample sand = SampleTerrain( SandColorMap, SandNormalMap,
			TerrainSandTileMetres, sandWeight, position, gradientX, gradientY, normal, sandProjection, detail, 3 );
		material.Albedo += sand.Color * sandWeight;
		material.Normal += sand.Normal * sandWeight;
		material.Roughness += lerp( 0.85, 1.0, saturate( sand.Surface.r ) ) * sandWeight;
		material.AmbientOcclusion += sand.Surface.g * sandWeight;
		[branch]
		if ( gravelWeight > 0.00001 )
		{
			float3 gravelProjection = projection;
			if ( detail > 0.0 )
			{
				gravelProjection = TerrainReliefProjection( GravelHeightMap, TerrainGravelTileMetres,
					TerrainGravelSourceTexels, TerrainGravelMinimumLod, position, reliefBaseLod, projection, 2 );
			}
			TerrainSample gravel = SampleHeightTerrain( GravelColorMap, GravelHeightMap,
				TerrainGravelTileMetres, TerrainGravelAmplitudeMetres, TerrainGravelSourceTexels,
				TerrainGravelMinimumLod, 0.95, 0.65, position, gradientX, gradientY, normal, gravelProjection, reliefBaseLod, detail );
			material.Albedo += gravel.Color * gravelWeight;
			material.Normal += gravel.Normal * gravelWeight;
			material.Roughness += gravel.Surface.r * gravelWeight;
			material.AmbientOcclusion += gravel.Surface.g * gravelWeight;
		}
		material.Normal = normalize( material.Normal );
		if ( BiomeDebugEnabled != 0 )
		{
			float2 uv = float2( position.x - BiomeDebugBounds.x, BiomeDebugBounds.y - position.y ) / max( BiomeDebugBounds.zw, 1.0 );
			if ( all( uv >= 0.0 ) && all( uv <= 1.0 ) )
			{
				float3 color = BiomeDebugSmooth != 0 ? BiomeDebugMap.SampleLevel( BiomeDebugLinear, uv, 0 ).rgb : BiomeDebugMap.SampleLevel( BiomeDebugPoint, uv, 0 ).rgb;
				// Unlit diagnostic colour keeps the map legend meaningful on shaded slopes.
				return float4( SrgbGammaToLinear( color ), 1.0 );
			}
		}
		return ShadingModelStandard::Shade( input, material );
		#endif
	}
}
