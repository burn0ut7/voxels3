// Color, relief and normals use the same tile and filtered height field.
TerrainSample SampleHeightProjection( Texture2D colorMap, Texture2D heightMap,
	float tileMetres, float amplitudeMetres, float sourceTexels, float roughness, float cavityFloor, float2 uv, float2 gradientX, float2 gradientY, float lod, float contribution )
{
	TerrainSample result = (TerrainSample)0;
	[branch]
	if ( contribution <= 0.0 )
	{
		return result;
	}
	result.Color = colorMap.SampleGrad( TerrainSampler, uv, gradientX, gradientY ).rgb;
	float stepSize = exp2( lod ) / sourceTexels;
	float left = heightMap.SampleLevel( TerrainHeightSampler, uv - float2( stepSize, 0.0 ), lod ).r;
	float right = heightMap.SampleLevel( TerrainHeightSampler, uv + float2( stepSize, 0.0 ), lod ).r;
	float below = heightMap.SampleLevel( TerrainHeightSampler, uv - float2( 0.0, stepSize ), lod ).r;
	float above = heightMap.SampleLevel( TerrainHeightSampler, uv + float2( 0.0, stepSize ), lod ).r;
	result.Normal = float3( float2( left - right, below - above ) *
		(amplitudeMetres / (2.0 * stepSize * tileMetres)), 0.0 );
	float height = heightMap.SampleLevel( TerrainHeightSampler, uv, lod ).r;
	result.Surface = float2( roughness, lerp( cavityFloor, 1.0, smoothstep( 0.08, 0.65, height ) ) );
	return result;
}

TerrainSample SampleHeightTerrain( Texture2D colorMap, Texture2D heightMap,
	float tileMetres, float amplitudeMetres, float sourceTexels, float minimumLod,
	float roughness, float cavityFloor, float3 position, float3 dx, float3 dy, float3 normal,
	float3 projection, float3 baseLod, float detail )
{
	TerrainSample distant = (TerrainSample)0;
	distant.Normal = normal;
	[branch]
	if ( detail < 1.0 )
	{
		// Use the actual coarsest color/height mips for a continuous distance fade.
		distant.Color = colorMap.SampleLevel( TerrainSampler, float2( 0.5, 0.5 ), 20.0 ).rgb;
		float meanHeight = heightMap.SampleLevel( TerrainHeightSampler, float2( 0.5, 0.5 ), 20.0 ).r;
		distant.Surface = float2( roughness, lerp( cavityFloor, 1.0, smoothstep( 0.08, 0.65, meanHeight ) ) );
	}
	[branch]
	if ( detail <= 0.0 )
	{
		return distant;
	}
	float scale = 0.0254 / tileMetres;
	float3 uv = position * scale + frac( g_vHighPrecisionLightingOffsetWs.xyz * scale );
	dx *= scale;
	dy *= scale;
	float3 lod = TerrainReliefLod( baseLod, tileMetres, sourceTexels, minimumLod );
	TerrainSample x = SampleHeightProjection( colorMap, heightMap, tileMetres, amplitudeMetres, sourceTexels, roughness, cavityFloor, uv.yz, dx.yz, dy.yz, lod.x, projection.x );
	TerrainSample y = SampleHeightProjection( colorMap, heightMap, tileMetres, amplitudeMetres, sourceTexels, roughness, cavityFloor, uv.xz, dx.xz, dy.xz, lod.y, projection.y );
	TerrainSample z = SampleHeightProjection( colorMap, heightMap, tileMetres, amplitudeMetres, sourceTexels, roughness, cavityFloor, uv.xy, dx.xy, dy.xy, lod.z, projection.z );
	TerrainSample result;
	result.Color = lerp( distant.Color, x.Color * projection.x + y.Color * projection.y + z.Color * projection.z, detail );
	result.Surface = lerp( distant.Surface, x.Surface * projection.x + y.Surface * projection.y + z.Surface * projection.z, detail );
	result.Normal = lerp( normal, MapTerrainNormals( x.Normal, y.Normal, z.Normal, normal, projection ), detail );
	return result;
}
