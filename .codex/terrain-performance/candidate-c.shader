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
	CreateInputTexture2D( GrassColor, Srgb, 8, "", "_color", "Grass,10/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( GrassNormal, Linear, 8, "NormalizeNormals", "_normal", "Grass,10/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( GrassRoughness, Linear, 8, "", "_rough", "Grass,10/30", Default( 0.9 ) );
	CreateInputTexture2D( GrassOcclusion, Linear, 8, "", "_ao", "Grass,10/40", Default( 1.0 ) );
	CreateInputTexture2D( GrassHeight, Linear, 8, "", "_height", "Grass,10/50", Default( 1.0 ) );
	Texture2D GrassColorMap < Channel( RGB, Box( GrassColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D GrassNormalMap < Channel( RGB, Box( GrassNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D GrassSurfaceMap < Channel( R, Box( GrassRoughness ), Linear ); Channel( G, Box( GrassOcclusion ), Linear ); Channel( B, Box( GrassHeight ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateInputTexture2D( DirtColor, Srgb, 8, "", "_color", "Dirt,11/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( DirtNormal, Linear, 8, "NormalizeNormals", "_normal", "Dirt,11/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( DirtRoughness, Linear, 8, "", "_rough", "Dirt,11/30", Default( 0.9 ) );
	CreateInputTexture2D( DirtOcclusion, Linear, 8, "", "_ao", "Dirt,11/40", Default( 1.0 ) );
	CreateInputTexture2D( DirtHeight, Linear, 8, "", "_height", "Dirt,11/50", Default( 1.0 ) );
	Texture2D DirtColorMap < Channel( RGB, Box( DirtColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D DirtNormalMap < Channel( RGB, Box( DirtNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D DirtSurfaceMap < Channel( R, Box( DirtRoughness ), Linear ); Channel( G, Box( DirtOcclusion ), Linear ); Channel( B, Box( DirtHeight ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateInputTexture2D( StoneColor, Srgb, 8, "", "_color", "Stone,12/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( StoneNormal, Linear, 8, "NormalizeNormals", "_normal", "Stone,12/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( StoneRoughness, Linear, 8, "", "_rough", "Stone,12/30", Default( 0.9 ) );
	CreateInputTexture2D( StoneOcclusion, Linear, 8, "", "_ao", "Stone,12/40", Default( 1.0 ) );
	CreateInputTexture2D( StoneHeight, Linear, 8, "", "_height", "Stone,12/50", Default( 1.0 ) );
	Texture2D StoneColorMap < Channel( RGB, Box( StoneColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D StoneNormalMap < Channel( RGB, Box( StoneNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D StoneSurfaceMap < Channel( R, Box( StoneRoughness ), Linear ); Channel( G, Box( StoneOcclusion ), Linear ); Channel( B, Box( StoneHeight ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateInputTexture2D( SandColor, Srgb, 8, "", "_color", "Sand,13/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( SandNormal, Linear, 8, "NormalizeNormals", "_normal", "Sand,13/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SandRoughness, Linear, 8, "", "_rough", "Sand,13/30", Default( 0.9 ) );
	CreateInputTexture2D( SandOcclusion, Linear, 8, "", "_ao", "Sand,13/40", Default( 1.0 ) );
	CreateInputTexture2D( SandHeight, Linear, 8, "", "_height", "Sand,13/50", Default( 1.0 ) );
	Texture2D SandColorMap < Channel( RGB, Box( SandColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D SandNormalMap < Channel( RGB, Box( SandNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D SandSurfaceMap < Channel( R, Box( SandRoughness ), Linear ); Channel( G, Box( SandOcclusion ), Linear ); Channel( B, Box( SandHeight ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateInputTexture2D( SnowColor, Srgb, 8, "", "_color", "Snow,14/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( SnowNormal, Linear, 8, "NormalizeNormals", "_normal", "Snow,14/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( SnowRoughness, Linear, 8, "", "_rough", "Snow,14/30", Default( 0.9 ) );
	CreateInputTexture2D( SnowOcclusion, Linear, 8, "", "_ao", "Snow,14/40", Default( 1.0 ) );
	CreateInputTexture2D( SnowHeight, Linear, 8, "", "_height", "Snow,14/50", Default( 1.0 ) );
	Texture2D SnowColorMap < Channel( RGB, Box( SnowColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D SnowNormalMap < Channel( RGB, Box( SnowNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D SnowSurfaceMap < Channel( R, Box( SnowRoughness ), Linear ); Channel( G, Box( SnowOcclusion ), Linear ); Channel( B, Box( SnowHeight ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	SamplerState TerrainSampler < Filter( ANISOTROPIC ); MaxAniso( 16 ); AddressU( WRAP ); AddressV( WRAP ); >;
	float2 TerrainParallax( Texture2D surfaceMap, float reliefRatio, float2 uv, float2 gradientX, float2 gradientY, float3 view, float strength )
	{
		strength *= smoothstep( 0.08, 0.25, view.z );
		[branch]
		if ( strength <= 0.0001 )
		{
			return uv;
		}
		int steps = (int)ceil( lerp( 16.0, 8.0, saturate( view.z ) ) );
		float depthStep = 1.0 / steps;
		float2 uvStep = view.xy / max( view.z, 0.15 )
			* reliefRatio * strength * depthStep;
		float2 previousUv = uv;
		float previousGap = 1.0 - surfaceMap.SampleGrad( TerrainSampler, uv, gradientX, gradientY ).b;
		if ( previousGap <= 0.0 )
		{
			return uv;
		}
		// Height white is the top of the layer. Trace inward and refine the first
		// crossing rather than quantizing the visible depth to the march steps.
		[loop]
		for ( int step = 1; step <= 16; step++ )
		{
			float2 currentUv = uv - uvStep * step;
			float surfaceDepth = 1.0 - surfaceMap.SampleGrad( TerrainSampler, currentUv, gradientX, gradientY ).b;
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

	struct TerrainSample
	{
		float3 Color;
		float3 Normal;
		float2 Surface;
	};

	TerrainSample SampleTerrainProjection( Texture2D colorMap, Texture2D normalMap, Texture2D surfaceMap, float reliefRatio, float2 uv, float2 gradientX, float2 gradientY, float3 view, float relief, float contribution )
	{
		TerrainSample result = (TerrainSample)0;
		[branch]
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
			float2 patchUv = TerrainParallax( surfaceMap, reliefRatio, uv + offset, gradientX, gradientY, view,
				relief * smoothstep( 0.0, 0.01, contribution ) );
			result.Color += blend[patch] * colorMap.SampleGrad( TerrainSampler, patchUv, gradientX, gradientY ).rgb;
			result.Surface += blend[patch] * surfaceMap.SampleGrad( TerrainSampler, patchUv, gradientX, gradientY ).rg;
			result.Normal += blend[patch] * (normalMap.SampleGrad( TerrainSampler, patchUv, gradientX, gradientY ).xyz * 2.0 - 1.0);
		}
		return result;
	}


	// One surface path: maps, physical tile size and authored relief are material inputs.
	TerrainSample SampleTerrain( Texture2D colorMap, Texture2D normalMap, Texture2D surfaceMap,
		float tileMetres, float reliefMetres, float weight, float3 position, float3 gradientX,
		float3 gradientY, float3 normal, float3 projection, float3 view, float reliefFade )
	{
		TerrainSample result = (TerrainSample)0;
		[branch]
		if ( weight <= 0.00001 )
		{
			return result;
		}
		float scale = 0.0254 / tileMetres;
		position *= scale;
		gradientX *= scale;
		gradientY *= scale;
		float relief = reliefFade * weight;
		// Trace in the actual surface tangent plane. Axis-plane denominators
		// become grazing inside an otherwise front-facing surface and smear it.
		float normalView = dot( normal, view );
		float3 tangentView = view - normal * normalView;
		TerrainSample sampleX = SampleTerrainProjection( colorMap, normalMap, surfaceMap, reliefMetres / tileMetres,
			position.yz, gradientX.yz, gradientY.yz, float3( tangentView.yz, normalView ), relief, projection.x );
		TerrainSample sampleY = SampleTerrainProjection( colorMap, normalMap, surfaceMap, reliefMetres / tileMetres,
			position.xz, gradientX.xz, gradientY.xz, float3( tangentView.xz, normalView ), relief, projection.y );
		TerrainSample sampleZ = SampleTerrainProjection( colorMap, normalMap, surfaceMap, reliefMetres / tileMetres,
			position.xy, gradientX.xy, gradientY.xy, float3( tangentView.xy, normalView ), relief, projection.z );
		result.Color = sampleX.Color * projection.x + sampleY.Color * projection.y + sampleZ.Color * projection.z;
		result.Surface = sampleX.Surface * projection.x + sampleY.Surface * projection.y + sampleZ.Surface * projection.z;
		// OpenGL U/V axes; whiteout preserves the geometry normal for flat maps.
		result.Normal = normalize(
			float3( normal.x * sampleX.Normal.z, normal.y + sampleX.Normal.x, normal.z + sampleX.Normal.y ) * projection.x
			+ float3( normal.x + sampleY.Normal.x, normal.y * sampleY.Normal.z, normal.z + sampleY.Normal.y ) * projection.y
			+ float3( normal.x + sampleZ.Normal.x, normal.y + sampleZ.Normal.y, normal.z * sampleZ.Normal.z ) * projection.z );
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
		// Derivatives precede material/plane/patch branches and all ray marches.
		float3 gradientX = ddx( position );
		float3 gradientY = ddy( position );
		float3 toCamera = g_vCameraPositionWs - position;
		float3 view = normalize( toCamera );
		float reliefFade = 1.0 - smoothstep( 2.0, 8.0, length( toCamera ) * 0.0254 );
		float4 weights = max( input.vMaterialWeights, 0.0 );
		float total = dot( weights, float4( 1.0, 1.0, 1.0, 1.0 ) );
		float sandWeight = saturate( 1.0 - total );
		weights /= max( total, 1.0 );
		material.Albedo = 0.0;
		material.Normal = 0.0;
		material.Roughness = 0.0;
		material.AmbientOcclusion = 0.0;
		material.Metalness = 0.0;
		TerrainSample grass = SampleTerrain( GrassColorMap, GrassNormalMap, GrassSurfaceMap,
			1.4, 0.02, weights.x, position, gradientX, gradientY, normal, projection, view, reliefFade );
		material.Albedo += grass.Color * weights.x;
		material.Normal += grass.Normal * weights.x;
		material.Roughness += lerp( 0.85, 1.0, saturate( grass.Surface.r ) ) * weights.x;
		material.AmbientOcclusion += grass.Surface.g * weights.x;
		TerrainSample dirt = SampleTerrain( DirtColorMap, DirtNormalMap, DirtSurfaceMap,
			2, 0.033, weights.y, position, gradientX, gradientY, normal, projection, view, reliefFade );
		// Dark earthy soil: reduce the red cast while retaining source variation.
		dirt.Color *= float3( 0.55, 0.65, 0.60 );
		material.Albedo += dirt.Color * weights.y;
		material.Normal += dirt.Normal * weights.y;
		material.Roughness += clamp( dirt.Surface.r, 0.65, 1.0 ) * weights.y;
		material.AmbientOcclusion += dirt.Surface.g * weights.y;
		TerrainSample stone = SampleTerrain( StoneColorMap, StoneNormalMap, StoneSurfaceMap,
			2.38, 0.04, weights.z, position, gradientX, gradientY, normal, projection, view, reliefFade );
		// Retain a little mineral color while removing the source rock's brown cast.
		float stoneLuminance = dot( stone.Color, float3( 0.2126, 0.7152, 0.0722 ) );
		stone.Color = lerp( stoneLuminance.xxx, stone.Color, 0.15 );
		material.Albedo += stone.Color * weights.z;
		material.Normal += stone.Normal * weights.z;
		material.Roughness += lerp( 0.65, 1.0, saturate( stone.Surface.r ) ) * weights.z;
		material.AmbientOcclusion += stone.Surface.g * weights.z;
		TerrainSample snow = SampleTerrain( SnowColorMap, SnowNormalMap, SnowSurfaceMap,
			1.0, 0.003, weights.w, position, gradientX, gradientY, normal, projection, view, reliefFade );
		material.Albedo += snow.Color * weights.w;
		material.Normal += snow.Normal * weights.w;
		material.Roughness += lerp( 0.8, 1.0, saturate( snow.Surface.r ) ) * weights.w;
		material.AmbientOcclusion += snow.Surface.g * weights.w;
		TerrainSample sand = SampleTerrain( SandColorMap, SandNormalMap, SandSurfaceMap,
			1.0, 0.002, sandWeight, position, gradientX, gradientY, normal, projection, view, reliefFade );
		material.Albedo += sand.Color * sandWeight;
		material.Normal += sand.Normal * sandWeight;
		material.Roughness += lerp( 0.85, 1.0, saturate( sand.Surface.r ) ) * sandWeight;
		material.AmbientOcclusion += sand.Surface.g * sandWeight;
		material.Normal = normalize( material.Normal );
		return ShadingModelStandard::Shade( input, material );
	}
}
