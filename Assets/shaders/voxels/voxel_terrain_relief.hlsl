float SampleReliefProjection( Texture2D heightMap, float2 uv, float2 anchor, float lod, float contribution, int mapping )
{
	[branch]
	if ( contribution <= 0.0 )
	{
		return 0.0;
	}
	if ( mapping != 2 )
	{
		float period = mapping == 3 ? TerrainSandPatternPeriod : TerrainPatternPeriod;
		uv = float2( uv.x - uv.y * 0.577350269, uv.y * 1.154700538 ) / period;
		anchor = float2( anchor.x - anchor.y * 0.577350269, anchor.y * 1.154700538 ) / period;
	}
	return heightMap.SampleLevel( TerrainHeightSampler, uv + frac( anchor ), lod ).r;
}

float3 TerrainReliefProjectionHeights( Texture2D heightMap, float tileMetres,
	float3 positionMetres, float3 lod, float3 projection, int mapping )
{
	float3 uv = positionMetres / tileMetres;
	float3 anchor = g_vHighPrecisionLightingOffsetWs.xyz * (0.0254 / tileMetres);
	return float3(
		SampleReliefProjection( heightMap, uv.yz, anchor.yz, lod.x, projection.x, mapping ),
		SampleReliefProjection( heightMap, uv.xz, anchor.xz, lod.y, projection.y, mapping ),
		SampleReliefProjection( heightMap, uv.xy, anchor.xy, lod.z, projection.z, mapping ) );
}

float SampleReliefHeight( Texture2D heightMap, float tileMetres, float3 positionMetres,
	float3 lod, float3 projection, float contrast, int mapping )
{
	float3 heights = TerrainReliefProjectionHeights( heightMap, tileMetres, positionMetres, lod, projection, mapping );
	if ( contrast <= 0.0 )
	{
		return saturate( dot( projection, heights ) );
	}
	// Normalized smooth maximum: constant heights survive unchanged. Its
	// height derivative gives the same projection weights used for shading.
	return saturate( log2( dot( projection, exp2( contrast * heights ) ) ) / contrast );
}

float3 TerrainReliefBaseLod( float3 dxMetres, float3 dyMetres, int mapping )
{
	float2 dxX = dxMetres.yz;
	float2 dxY = dxMetres.xz;
	float2 dxZ = dxMetres.xy;
	float2 dyX = dyMetres.yz;
	float2 dyY = dyMetres.xz;
	float2 dyZ = dyMetres.xy;
	if ( mapping != 2 )
	{
		float period = mapping == 3 ? TerrainSandPatternPeriod : TerrainPatternPeriod;
		dxX = float2( dxX.x - dxX.y * 0.577350269, dxX.y * 1.154700538 ) / period;
		dxY = float2( dxY.x - dxY.y * 0.577350269, dxY.y * 1.154700538 ) / period;
		dxZ = float2( dxZ.x - dxZ.y * 0.577350269, dxZ.y * 1.154700538 ) / period;
		dyX = float2( dyX.x - dyX.y * 0.577350269, dyX.y * 1.154700538 ) / period;
		dyY = float2( dyY.x - dyY.y * 0.577350269, dyY.y * 1.154700538 ) / period;
		dyZ = float2( dyZ.x - dyZ.y * 0.577350269, dyZ.y * 1.154700538 ) / period;
	}
	float3 footprintSquared;
	footprintSquared.x = max( dot( dxX, dxX ), dot( dyX, dyX ) );
	footprintSquared.y = max( dot( dxY, dxY ), dot( dyY, dyY ) );
	footprintSquared.z = max( dot( dxZ, dxZ ), dot( dyZ, dyZ ) );
	return 0.5 * log2( max( footprintSquared, 0.000000000001 ) );
}

float3 TerrainReliefLod( float3 baseLod, float tileMetres, float sourceTexels, float minimumLod )
{
	return max( baseLod + log2( sourceTexels / tileMetres ), minimumLod );
}

float3 TerrainReliefProjection( Texture2D heightMap, float tileMetres, float sourceTexels, float minimumLod,
	float3 position, float3 baseLod, float3 projection, int mapping )
{
	float3 heights = TerrainReliefProjectionHeights( heightMap, tileMetres,
		position * 0.0254, TerrainReliefLod( baseLod, tileMetres, sourceTexels, minimumLod ), projection, mapping );
	float3 weighted = projection * exp2( TerrainReliefProjectionContrast * heights );
	return weighted / dot( weighted, float3( 1.0, 1.0, 1.0 ) );
}

float TerrainReliefDepth( float3 positionMetres, float3 dirtLod, float3 stoneLod,
	float3 grassLod, float3 sandLod, float3 snowLod, float3 gravelLod, float3 clayLod, float3 projection, float4 amplitudes, float snowAmplitude, float gravelAmplitude, float clayAmplitude )
{
	float depth = 0.0;
	[branch]
	if ( amplitudes.x > 0.0 )
	{
		depth += amplitudes.x * (1.0 - SampleReliefHeight( DirtHeightMap,
			TerrainReliefTileMetres.x, positionMetres, dirtLod, projection, TerrainReliefProjectionContrast, 2 ));
	}
	[branch]
	if ( amplitudes.y > 0.0 )
	{
		depth += amplitudes.y * (1.0 - SampleReliefHeight( StoneHeightMap,
			TerrainReliefTileMetres.y, positionMetres, stoneLod, projection, TerrainReliefProjectionContrast, 2 ));
	}
	[branch]
	if ( amplitudes.z > 0.0 )
	{
		depth += amplitudes.z * (1.0 - SampleReliefHeight( GrassHeightMap,
			TerrainGrassTileMetres, positionMetres, grassLod, projection, TerrainReliefProjectionContrast, 1 ));
	}
	[branch]
	if ( amplitudes.w > 0.0 )
	{
		depth += amplitudes.w * (1.0 - SampleReliefHeight( SandHeightMap,
			TerrainSandTileMetres, positionMetres, sandLod, projection, TerrainReliefProjectionContrast, 3 ));
	}
	[branch]
	if ( snowAmplitude > 0.0 )
	{
		depth += snowAmplitude * (1.0 - SampleReliefHeight( SnowHeightMap,
			TerrainSnowTileMetres, positionMetres, snowLod, projection, TerrainReliefProjectionContrast, 1 ));
	}
	[branch]
	if ( gravelAmplitude > 0.0 )
	{
		depth += gravelAmplitude * (1.0 - SampleReliefHeight( GravelHeightMap,
			TerrainGravelTileMetres, positionMetres, gravelLod, projection, TerrainReliefProjectionContrast, 2 ));
	}
	[branch]
	if ( clayAmplitude > 0.0 )
	{
		depth += clayAmplitude * (1.0 - SampleReliefHeight( ClayHeightMap,
			TerrainClayTileMetres, positionMetres, clayLod, projection, TerrainReliefProjectionContrast, 2 ));
	}
	return depth;
}

float3 TerrainReliefPosition( float3 position, float3 dx, float3 dy,
	float3 normal, float3 projection, float4 weights, float snowWeight, float gravelWeight, float clayWeight, float3 baseLod, float3 patternBaseLod, float3 sandBaseLod, out float hitDepth )
{
	hitDepth = 0.0;
	// Keep sub-texel ray offsets local even many kilometres from the origin.
	float3 cameraDelta = (g_vCameraPositionWs - g_vHighPrecisionLightingOffsetWs.xyz) - position;
	float3 view = normalize( cameraDelta );
	float normalView = dot( normal, view );
	float distanceMetres = length( cameraDelta ) * 0.0254;
	float grazingFade = smoothstep( 0.05, 0.15, normalView );
	float fade = (1.0 - smoothstep( 8.0, 16.0, distanceMetres )) * grazingFade;
	// Stone's larger fractures retain depth across a readable cliff face.
	float stoneFade = (1.0 - smoothstep( 16.0, 40.0, distanceMetres )) * grazingFade;
	[branch]
	if ( (fade <= 0.0 && (stoneFade <= 0.0 || weights.y <= 0.00001))
		|| max( max( max( weights.x, weights.y ), max( weights.z, weights.w ) ), max( clayWeight, max( snowWeight, gravelWeight ) ) ) <= 0.00001 )
	{
		return position;
	}
	float3 dxMetres = dx * 0.0254;
	float3 dyMetres = dy * 0.0254;
	// Reuse the same explicit LOD for ray search and final height blending.
	float3 dirtLod = TerrainReliefLod( baseLod, TerrainReliefTileMetres.x, 2048.0, 2.0 );
	float3 stoneLod = TerrainReliefLod( baseLod, TerrainReliefTileMetres.y, TerrainStoneSourceTexels, TerrainStoneMinimumLod );
	float3 grassLod = TerrainReliefLod( patternBaseLod, TerrainGrassTileMetres, TerrainGrassSourceTexels, TerrainGrassMinimumLod );
	float3 sandLod = TerrainReliefLod( sandBaseLod, TerrainSandTileMetres, TerrainSandSourceTexels, TerrainSandMinimumLod );
	float3 snowLod = TerrainReliefLod( patternBaseLod, TerrainSnowTileMetres, TerrainSnowSourceTexels, TerrainSnowMinimumLod );
	float3 gravelLod = TerrainReliefLod( baseLod, TerrainGravelTileMetres, TerrainGravelSourceTexels, TerrainGravelMinimumLod );
	float3 clayLod = TerrainReliefLod( baseLod, TerrainClayTileMetres, TerrainClaySourceTexels, TerrainClayMinimumLod );
	// Height mip filtering must not also flatten a physically resolved layer.
	float footprint = max( max( length( dxMetres ), length( dyMetres ) ), 0.000001 );
	float4 authoredAmplitudes = float4( TerrainReliefAmplitudeMetres, TerrainGrassAmplitudeMetres * TerrainReliefRayStrength, TerrainSandAmplitudeMetres * TerrainReliefRayStrength );
	float4 depthPixels = authoredAmplitudes / footprint;
	float4 amplitudes = weights * authoredAmplitudes * float4( fade, stoneFade, fade, fade ) * smoothstep( 0.5, 2.0, depthPixels );
	amplitudes *= float4( weights.x > 0.00001 ? 1.0 : 0.0, weights.y > 0.00001 ? 1.0 : 0.0, weights.z > 0.00001 ? 1.0 : 0.0, weights.w > 0.00001 ? 1.0 : 0.0 );
	float snowAuthoredAmplitude = TerrainSnowAmplitudeMetres * TerrainReliefRayStrength;
	float snowAmplitude = snowWeight > 0.00001 ? snowWeight * snowAuthoredAmplitude * fade * smoothstep( 0.5, 2.0, snowAuthoredAmplitude / footprint ) : 0.0;
	float gravelAuthoredAmplitude = TerrainGravelAmplitudeMetres * TerrainReliefRayStrength;
	float gravelAmplitude = gravelWeight > 0.00001 ? gravelWeight * gravelAuthoredAmplitude * fade * smoothstep( 0.5, 2.0, gravelAuthoredAmplitude / footprint ) : 0.0;
	float clayAuthoredAmplitude = TerrainClayAmplitudeMetres * TerrainReliefRayStrength;
	float clayAmplitude = clayWeight > 0.00001 ? clayWeight * clayAuthoredAmplitude * fade * smoothstep( 0.5, 2.0, clayAuthoredAmplitude / footprint ) : 0.0;
	float maximumDepth = amplitudes.x + amplitudes.y + amplitudes.z + amplitudes.w + snowAmplitude + gravelAmplitude + clayAmplitude;
	[branch]
	if ( maximumDepth <= 0.000001 )
	{
		return position;
	}
	float3 tangentRay = (view - normal * normalView) / max( normalView, 0.05 );
	// H=0.5 coincides with the mesh; search from the top of the full interval.
	float referenceDepth = maximumDepth * 0.5;
	float3 origin = position * 0.0254 + tangentRay * referenceDepth;
	int steps = (int)ceil( lerp( 64.0, 8.0, saturate( normalView ) ) );
	[branch]
	if ( amplitudes.y > 0.0 )
	{
		// Deep stone relief can cross many height texels even near front-facing.
		// Resolve the actual projected ray length at the mip used by this trace.
		float3 stoneRayTexels = float3( length( tangentRay.yz ), length( tangentRay.xz ), length( tangentRay.xy ) )
			* maximumDepth * (TerrainStoneSourceTexels / TerrainReliefTileMetres.y) * exp2( -stoneLod );
		stoneRayTexels *= float3( projection.x > 0.0 ? 1.0 : 0.0, projection.y > 0.0 ? 1.0 : 0.0, projection.z > 0.0 ? 1.0 : 0.0 );
		float stoneSteps = max( max( stoneRayTexels.x, stoneRayTexels.y ), stoneRayTexels.z ) / 1.25;
		steps = max( steps, (int)ceil( min( stoneSteps, 128.0 ) ) );
	}
	float low = 0.0;
	// Depth is nonnegative, so g(0) <= 0 without an initial texture read.
	// Evaluate its exact value only if refinement leaves the bracket at zero.
	float lowGap = 0.0;
	[loop]
	for ( int step = 1; step <= steps; step++ )
	{
		float high = maximumDepth * ((float)step / steps);
		float highGap = high - TerrainReliefDepth( origin - tangentRay * high,
			dirtLod, stoneLod, grassLod, sandLod, snowLod, gravelLod, clayLod, projection, amplitudes, snowAmplitude, gravelAmplitude, clayAmplitude );
		if ( highGap >= 0.0 )
		{
			[unroll]
			for ( int refine = 0; refine < 4; refine++ )
			{
				float middle = (low + high) * 0.5;
				float gap = middle - TerrainReliefDepth( origin - tangentRay * middle,
					dirtLod, stoneLod, grassLod, sandLod, snowLod, gravelLod, clayLod, projection, amplitudes, snowAmplitude, gravelAmplitude, clayAmplitude );
				if ( gap >= 0.0 )
				{
					high = middle;
					highGap = gap;
				}
				else
				{
					low = middle;
					lowGap = gap;
				}
			}
			[branch]
			if ( low == 0.0 )
			{
				lowGap = -TerrainReliefDepth( origin, dirtLod, stoneLod, grassLod, sandLod, snowLod, gravelLod, clayLod, projection, amplitudes, snowAmplitude, gravelAmplitude, clayAmplitude );
			}
			float hit = lerp( low, high, saturate( -lowGap / max( highGap - lowGap, 0.0000001 ) ) );
			// Preserve world precision: add the small displacement in engine units.
			hitDepth = (hit - referenceDepth) / 0.0254;
			float3 hitPosition = position - tangentRay * hitDepth;
			return hitPosition;
		}
		low = high;
		lowGap = highGap;
	}
	hitDepth = (maximumDepth - referenceDepth) / 0.0254;
	return position - tangentRay * hitDepth;
}
