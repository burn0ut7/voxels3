// TerrainBiomes mirror. Included after LandformNoise; no state or texture atlas.
// Returned weights: snow, desert, forest, jungle. Remaining weight is open land.
float4 SampleVoxelBiome( float2 position, float4 terrain, float mountains, out float2 climate )
{
	uint seed = (uint)(int)terrain.x;
	float2 unusedGradient;
	float temperature = LandformNoise( position, 65536.0, seed ^ 0x4F1BBCDCu, unusedGradient ) * 0.85 +
		LandformNoise( position, 32768.0, seed ^ 0x4F1BBCDCu ^ 0xA511E9B3u, unusedGradient ) * 0.15;
	float moisture = LandformNoise( position, 65536.0, seed ^ 0x6C8E9CF5u, unusedGradient ) * 0.85 +
		LandformNoise( position, 32768.0, seed ^ 0x6C8E9CF5u ^ 0xA511E9B3u, unusedGradient ) * 0.15;
	temperature = clamp( 1.5 * temperature - 0.25, 0.0, 1.0 );
	moisture = clamp( 1.5 * moisture - 0.25, 0.0, 1.0 );
	climate = float2( temperature, moisture );
	float snow = LandformSmooth( (0.30 - temperature) / 0.15 );
	float desert = (1.0 - snow) * LandformSmooth( (temperature - 0.70) / 0.15 ) * LandformSmooth( (0.40 - moisture) / 0.20 );
	float wooded = (1.0 - snow - desert) * (1.0 - LandformSmooth( (mountains - 0.55) / 0.25 ));
	float jungle = wooded * LandformSmooth( (temperature - 0.60) / 0.20 ) * LandformSmooth( (moisture - 0.55) / 0.20 );
	float forest = max( 0.0, wooded - jungle ) * LandformSmooth( (moisture - 0.38) / 0.20 );
	return float4( snow, desert, forest, jungle );
}

float VoxelMarshClimate( float2 climate )
{
	return LandformSmooth( (climate.x - 0.30) / 0.20 ) * LandformSmooth( (climate.y - 0.60) / 0.25 );
}

float VoxelMarshWeight( float2 climate, float naturalHeight, float mountains, float seaLevel )
{
	return VoxelMarshClimate( climate ) * LandformSmooth( (naturalHeight - seaLevel + 128.0) / 96.0 ) *
		(1.0 - LandformSmooth( (naturalHeight - seaLevel - 64.0) / 128.0 )) * (1.0 - LandformSmooth( mountains / 0.5 ));
}

float VoxelBiomeCoverThreshold( float2 position, float4 terrain )
{
	float2 unusedGradient;
	return 0.05 + 0.90 * LandformNoise( position, 512.0, (uint)(int)terrain.x ^ 0x63D83595u, unusedGradient );
}

float RefineVoxelBiomeHeight( float2 position, float4 terrain, float4 scales,
	float naturalHeight, float riverHeight, float mountains, float seaLevel )
{
	float marshAltitude = naturalHeight - seaLevel;
	if ( marshAltitude > -128.0 && marshAltitude < 192.0 && mountains < 0.5 )
	{
		float2 marshClimate;
		SampleVoxelBiome( position, terrain, mountains, marshClimate );
		float marsh = VoxelMarshWeight( marshClimate, naturalHeight, mountains, seaLevel ) *
			(1.0 - LandformSmooth( (naturalHeight - riverHeight) / 16.0 ));
		if ( marsh > 0.0 )
		{
			float2 marshGradient;
			float basin = LandformNoise( position, 512.0, (uint)(int)terrain.x ^ 0xB5297A4Du, marshGradient );
			basin *= 0.5 + 0.5 * basin;
			float target = seaLevel - 24.0 + 48.0 * basin;
			float maximum = scales.w * 0.02;
			return riverHeight + clamp( target - riverHeight, -maximum, maximum ) * marsh;
		}
	}
	float protection = LandformSmooth( (riverHeight - seaLevel - 64.0) / 256.0 ) *
		(1.0 - LandformSmooth( mountains / 0.5 )) * (1.0 - LandformSmooth( (naturalHeight - riverHeight) / 16.0 ));
	if ( protection <= 0.0 )
	{
		return riverHeight;
	}
	float2 climate;
	float4 habitat = SampleVoxelBiome( position, terrain, mountains, climate );
	if ( habitat.y <= 0.0 )
	{
		return riverHeight;
	}
	float2 unusedGradient;
	float relief = 2.0 * LandformNoise( position, 2048.0, (uint)(int)terrain.x ^ 0x9E3779B9u, unusedGradient ) - 1.0;
	return riverHeight + relief * habitat.y * protection * scales.w * 0.005;
}
