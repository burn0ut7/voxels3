from pathlib import Path
base=Path('.codex/terrain-textures/before.shader').read_text()
# Reuse the proven sampling implementation for every material.
start=base.index('\tfloat2 GrassParallax')
end=base.index('\tRenderState',start)
helpers=base[start:end].replace('GrassSample','TerrainSample').replace('GrassParallax','TerrainParallax').replace('SampleGrassProjection','SampleTerrainProjection').replace('GrassSampler','TerrainSampler')
helpers=helpers.replace('float2 TerrainParallax( ', 'float2 TerrainParallax( Texture2D surfaceMap, float reliefRatio, ')
helpers=helpers.replace('(GrassReliefMetres / GrassTileMetres)','reliefRatio').replace('GrassSurfaceMap','surfaceMap')
helpers=helpers.replace('TerrainSample SampleTerrainProjection( ', 'TerrainSample SampleTerrainProjection( Texture2D colorMap, Texture2D normalMap, Texture2D surfaceMap, float reliefRatio, ')
helpers=helpers.replace('TerrainParallax( uv + offset','TerrainParallax( surfaceMap, reliefRatio, uv + offset').replace('GrassColorMap','colorMap').replace('GrassNormalMap','normalMap')
declarations=''
for i,name in enumerate(['Grass','Dirt','Stone','Sand','Snow']):
    a=base.index('\tCreateInputTexture2D( GrassColor')
    b=base.index('\tSamplerState',a)
    declarations+=base[a:b].replace('Grass',name).replace(',10/',f',{10+i}/')
prefix=base[:base.index('\tCreateInputTexture2D( GrassColor')]
for line in prefix.splitlines(True):
    if any(x in line for x in ['float4 VoxelMaterialIds','float VoxelCheckerSize','float VoxelSandMaterialId','StructuredBuffer<float4> VoxelMaterialPalette']):
        prefix=prefix.replace(line,'')
body='''
	// One surface path: maps, physical tile size and authored relief are material inputs.
	TerrainSample SampleTerrain( Texture2D colorMap, Texture2D normalMap, Texture2D surfaceMap,
		float tileMetres, float reliefMetres, float weight, float3 position, float3 gradientX,
		float3 gradientY, float3 normal, float3 projection, float3 view, float reliefFade )
	{
		TerrainSample result = (TerrainSample)0;
		if ( weight <= 0.0 )
		{
			return result;
		}
		float scale = 0.0254 / tileMetres;
		position *= scale;
		gradientX *= scale;
		gradientY *= scale;
		float relief = reliefFade * weight;
		TerrainSample sampleX = SampleTerrainProjection( colorMap, normalMap, surfaceMap, reliefMetres / tileMetres,
			position.yz, gradientX.yz, gradientY.yz, float3( view.yz, view.x * sign( normal.x ) ), relief, projection.x );
		TerrainSample sampleY = SampleTerrainProjection( colorMap, normalMap, surfaceMap, reliefMetres / tileMetres,
			position.xz, gradientX.xz, gradientY.xz, float3( view.xz, view.y * sign( normal.y ) ), relief, projection.y );
		TerrainSample sampleZ = SampleTerrainProjection( colorMap, normalMap, surfaceMap, reliefMetres / tileMetres,
			position.xy, gradientX.xy, gradientY.xy, float3( view.xy, view.z * sign( normal.z ) ), relief, projection.z );
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
'''
for name,tile,depth,minimum,weight in [('Grass',1.4,.02,.85,'weights.x'),('Dirt',2,.018,.8,'weights.y'),('Stone',2.38,.04,.65,'weights.z'),('Snow',2,.025,.8,'weights.w'),('Sand',1.5,.012,.85,'sandWeight')]:
    body+=f'''		TerrainSample {name.lower()} = SampleTerrain( {name}ColorMap, {name}NormalMap, {name}SurfaceMap,
			{tile}, {depth}, {weight}, position, gradientX, gradientY, normal, projection, view, reliefFade );
		material.Albedo += {name.lower()}.Color * {weight};
		material.Normal += {name.lower()}.Normal * {weight};
		material.Roughness += lerp( {minimum}, 1.0, saturate( {name.lower()}.Surface.r ) ) * {weight};
		material.AmbientOcclusion += {name.lower()}.Surface.g * {weight};
'''
body+='\t\tmaterial.Normal = normalize( material.Normal );\n\t\treturn ShadingModelStandard::Shade( input, material );\n\t}\n}\n'
Path('.codex/terrain-textures/candidate.shader').write_text(prefix+declarations+'\tSamplerState TerrainSampler < Filter( ANISOTROPIC ); MaxAniso( 16 ); AddressU( WRAP ); AddressV( WRAP ); >;\n'+helpers+body,newline='\r\n')
