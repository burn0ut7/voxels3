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
	#include "shaders/voxels/voxel_material_blending.hlsl"
	#include "voxels/voxel_grass_pattern.hlsl"
	CreateInputTexture2D( GrassColor, Srgb, 8, "", "_color", "Grass,10/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( GrassNormal, Linear, 8, "", "_normal", "Grass,10/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( GrassRoughness, Linear, 8, "", "_rough", "Grass,10/30", Default( 0.9 ) );
	CreateInputTexture2D( GrassOcclusion, Linear, 8, "", "_ao", "Grass,10/40", Default( 1.0 ) );
	CreateInputTexture2D( GrassHeight, Linear, 8, "", "_height", "Grass,10/50", Default( 1.0 ) );
	Texture2D GrassColorMap < Channel( RGB, Box( GrassColor ), Srgb ); Channel( A, Box( GrassRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D GrassNormalMap < Channel( RGB, Box( GrassNormal ), Linear ); Channel( A, Box( GrassOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateInputTexture2D( DirtColor, Srgb, 8, "", "_color", "Dirt,11/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( DirtNormal, Linear, 8, "NormalizeNormals", "_normal", "Dirt,11/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( DirtRoughness, Linear, 8, "", "_rough", "Dirt,11/30", Default( 0.9 ) );
	CreateInputTexture2D( DirtOcclusion, Linear, 8, "", "_ao", "Dirt,11/40", Default( 1.0 ) );
	CreateInputTexture2D( DirtHeight, Linear, 8, "", "_height", "Dirt,11/50", Default( 1.0 ) );
	Texture2D DirtColorMap < Channel( RGB, Box( DirtColor ), Srgb ); Channel( A, Box( DirtRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D DirtNormalMap < Channel( RGB, Box( DirtNormal ), Linear ); Channel( A, Box( DirtOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateInputTexture2D( StoneColor, Srgb, 8, "", "_color", "Stone,12/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( StoneNormal, Linear, 8, "NormalizeNormals", "_normal", "Stone,12/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( StoneRoughness, Linear, 8, "", "_rough", "Stone,12/30", Default( 0.9 ) );
	CreateInputTexture2D( StoneOcclusion, Linear, 8, "", "_ao", "Stone,12/40", Default( 1.0 ) );
	CreateInputTexture2D( StoneHeight, Linear, 8, "", "_height", "Stone,12/50", Default( 1.0 ) );
	Texture2D StoneColorMap < Channel( RGB, Box( StoneColor ), Srgb ); Channel( A, Box( StoneRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D StoneNormalMap < Channel( RGB, Box( StoneNormal ), Linear ); Channel( A, Box( StoneOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateInputTexture2D( SandColor, Srgb, 8, "", "_color", "Sand,13/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( SandNormal, Linear, 8, "NormalizeNormals", "_normal", "Sand,13/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SandRoughness, Linear, 8, "", "_rough", "Sand,13/30", Default( 0.9 ) );
	CreateInputTexture2D( SandOcclusion, Linear, 8, "", "_ao", "Sand,13/40", Default( 1.0 ) );
	CreateInputTexture2D( SandHeight, Linear, 8, "", "_height", "Sand,13/50", Default( 1.0 ) );
	Texture2D SandColorMap < Channel( RGB, Box( SandColor ), Srgb ); Channel( A, Box( SandRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D SandNormalMap < Channel( RGB, Box( SandNormal ), Linear ); Channel( A, Box( SandOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateInputTexture2D( SnowColor, Srgb, 8, "", "_color", "Snow,14/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( SnowNormal, Linear, 8, "NormalizeNormals", "_normal", "Snow,14/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SnowRoughness, Linear, 8, "", "_rough", "Snow,14/30", Default( 0.9 ) );
	CreateInputTexture2D( SnowOcclusion, Linear, 8, "", "_ao", "Snow,14/40", Default( 1.0 ) );
	CreateInputTexture2D( SnowHeight, Linear, 8, "", "_height", "Snow,14/50", Default( 1.0 ) );
	Texture2D SnowColorMap < Channel( RGB, Box( SnowColor ), Srgb ); Channel( A, Box( SnowRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D SnowNormalMap < Channel( RGB, Box( SnowNormal ), Linear ); Channel( A, Box( SnowOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	SamplerState TerrainSampler < Filter( ANISOTROPIC ); MaxAniso( 16 ); AddressU( WRAP ); AddressV( WRAP ); >;
	struct TerrainSample
	{
		float3 Color;
		float3 Normal;
		float2 Surface;
	};

	TerrainSample SampleTerrainProjection( Texture2D colorMap, Texture2D normalMap, float2 uv, float2 gradientX, float2 gradientY, float contribution, float patternPeriod )
	{
		TerrainSample result = (TerrainSample)0;
		[branch]
		if ( contribution <= 0.0 )
		{
			return result;
		}
		[branch]
		if ( patternPeriod > 0.0 )
		{
			float2 cacheUv = float2( uv.x - uv.y * 0.577350269, uv.y * 1.154700538 ) / patternPeriod;
			float2 cacheDx = float2( gradientX.x - gradientX.y * 0.577350269, gradientX.y * 1.154700538 ) / patternPeriod;
			float2 cacheDy = float2( gradientY.x - gradientY.y * 0.577350269, gradientY.y * 1.154700538 ) / patternPeriod;
			float4 colorRoughness = colorMap.SampleGrad( TerrainSampler, cacheUv, cacheDx, cacheDy );
			float4 normalOcclusion = normalMap.SampleGrad( TerrainSampler, cacheUv, cacheDx, cacheDy );
			result.Color = colorRoughness.rgb;
			result.Surface = float2( colorRoughness.a, normalOcclusion.a );
			result.Normal = normalOcclusion.xyz * 2.0 - 1.0;
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
			[branch]
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
			float2 patchUv = uv + offset;
			float4 colorRoughness = colorMap.SampleGrad( TerrainSampler, patchUv, gradientX, gradientY );
			float4 normalOcclusion = normalMap.SampleGrad( TerrainSampler, patchUv, gradientX, gradientY );
			result.Color += blend[patch] * colorRoughness.rgb;
			result.Surface += blend[patch] * float2( colorRoughness.a, normalOcclusion.a );
			result.Normal += blend[patch] * (normalOcclusion.xyz * 2.0 - 1.0);
		}
		return result;
	}


	// One surface path: maps and physical tile size are material inputs.
	TerrainSample SampleTerrain( Texture2D colorMap, Texture2D normalMap,
		float tileMetres, float weight, float3 position, float3 gradientX,
		float3 gradientY, float3 normal, float3 projection, float detail, float patternPeriod )
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
		position *= scale;
		gradientX *= scale;
		gradientY *= scale;
		TerrainSample sampleX = SampleTerrainProjection( colorMap, normalMap,
			position.yz, gradientX.yz, gradientY.yz, projection.x, patternPeriod );
		TerrainSample sampleY = SampleTerrainProjection( colorMap, normalMap,
			position.xz, gradientX.xz, gradientY.xz, projection.y, patternPeriod );
		TerrainSample sampleZ = SampleTerrainProjection( colorMap, normalMap,
			position.xy, gradientX.xy, gradientY.xy, projection.z, patternPeriod );
		result.Color = sampleX.Color * projection.x + sampleY.Color * projection.y + sampleZ.Color * projection.z;
		result.Surface = sampleX.Surface * projection.x + sampleY.Surface * projection.y + sampleZ.Surface * projection.z;
		// OpenGL U/V axes; whiteout preserves the geometry normal for flat maps.
		result.Normal = normalize(
			float3( normal.x * sampleX.Normal.z, normal.y + sampleX.Normal.x, normal.z + sampleX.Normal.y ) * projection.x
			+ float3( normal.x + sampleY.Normal.x, normal.y * sampleY.Normal.z, normal.z + sampleY.Normal.y ) * projection.y
			+ float3( normal.x + sampleZ.Normal.x, normal.y + sampleZ.Normal.y, normal.z * sampleZ.Normal.z ) * projection.z );
		[branch]
		if ( detail < 1.0 )
		{
			result.Color = lerp( distant.Color, result.Color, detail );
			result.Surface = lerp( distant.Surface, result.Surface, detail );
			result.Normal = normalize( lerp( distant.Normal, result.Normal, detail ) );
		}
		return result;
	}

	RenderState( CullMode, BACK );
	RenderState( DepthWriteEnable, true );
	float4 MainPs( PixelInput input ) : SV_Target0
	{
		Material material = Material::Init( input );
		float3 position = input.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 normal = normalize( input.vNormalWs );
		float3 projection = max( pow( abs( normal ), 4.0 ) - 0.001, 0.0 );
		projection /= dot( projection, float3( 1.0, 1.0, 1.0 ) );
		// Derivatives precede material/plane/patch branches.
		float3 gradientX = ddx( position );
		float3 gradientY = ddy( position );
		// Full nearby texture detail; distant terrain keeps material color and lighting.
		float distanceMetres = length( g_vCameraPositionWs - position ) * 0.0254;
		float detail = 1.0 - smoothstep( 32.0, 64.0, distanceMetres );
		float4 weights = BlendVoxelMaterials( input.vMaterialWeights, position, max( length( gradientX ), length( gradientY ) ) );
		float total = dot( weights, float4( 1.0, 1.0, 1.0, 1.0 ) );
		float sandWeight = saturate( 1.0 - total );
		weights /= max( total, 1.0 );
		material.Albedo = 0.0;
		material.Normal = 0.0;
		material.Roughness = 0.0;
		material.AmbientOcclusion = 0.0;
		material.Metalness = 0.0;
		TerrainSample grass = SampleTerrain( GrassColorMap, GrassNormalMap,
			1.4, weights.x, position, gradientX, gradientY, normal, projection, detail, TerrainGrassPatternPeriod );
		material.Albedo += grass.Color * weights.x;
		material.Normal += grass.Normal * weights.x;
		material.Roughness += lerp( 0.85, 1.0, saturate( grass.Surface.r ) ) * weights.x;
		material.AmbientOcclusion += grass.Surface.g * weights.x;
		TerrainSample dirt = SampleTerrain( DirtColorMap, DirtNormalMap,
			2, weights.y, position, gradientX, gradientY, normal, projection, detail, 0.0 );
		// Dark earthy soil: reduce the red cast while retaining source variation.
		dirt.Color *= float3( 0.55, 0.65, 0.60 );
		material.Albedo += dirt.Color * weights.y;
		material.Normal += dirt.Normal * weights.y;
		material.Roughness += clamp( dirt.Surface.r, 0.65, 1.0 ) * weights.y;
		material.AmbientOcclusion += dirt.Surface.g * weights.y;
		TerrainSample stone = SampleTerrain( StoneColorMap, StoneNormalMap,
			2.38, weights.z, position, gradientX, gradientY, normal, projection, detail, 0.0 );
		// Retain a little mineral color while removing the source rock's brown cast.
		float stoneLuminance = dot( stone.Color, float3( 0.2126, 0.7152, 0.0722 ) );
		stone.Color = lerp( stoneLuminance.xxx, stone.Color, 0.15 );
		material.Albedo += stone.Color * weights.z;
		material.Normal += stone.Normal * weights.z;
		material.Roughness += lerp( 0.65, 1.0, saturate( stone.Surface.r ) ) * weights.z;
		material.AmbientOcclusion += stone.Surface.g * weights.z;
		TerrainSample snow = SampleTerrain( SnowColorMap, SnowNormalMap,
			1.0, weights.w, position, gradientX, gradientY, normal, projection, detail, 0.0 );
		material.Albedo += snow.Color * weights.w;
		material.Normal += snow.Normal * weights.w;
		material.Roughness += lerp( 0.8, 1.0, saturate( snow.Surface.r ) ) * weights.w;
		material.AmbientOcclusion += snow.Surface.g * weights.w;
		TerrainSample sand = SampleTerrain( SandColorMap, SandNormalMap,
			1.0, sandWeight, position, gradientX, gradientY, normal, projection, detail, 0.0 );
		material.Albedo += sand.Color * sandWeight;
		material.Normal += sand.Normal * sandWeight;
		material.Roughness += lerp( 0.85, 1.0, saturate( sand.Surface.r ) ) * sandWeight;
		material.AmbientOcclusion += sand.Surface.g * sandWeight;
		material.Normal = normalize( material.Normal );
		return ShadingModelStandard::Shade( input, material );
	}
}
