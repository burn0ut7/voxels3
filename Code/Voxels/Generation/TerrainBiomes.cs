using System;

/// <summary>Stable labels for the initial environment palette. Landforms remain independent.</summary>
public enum TerrainBiome
{
	Ocean,
	Plains,
	Hills,
	Mountains,
	Desert,
	Snow,
	Forest,
	Jungle,
	Marsh,
	Coastline
}

/// <summary>Pure, versioned climate and habitat rules. No mutable world or render state.</summary>
internal static class TerrainBiomes
{
	// Cubic bilinear noise has each partial <=1.5/spacing. With no elevation or
	// domain warp, the complete temperature gradient is <=0.00005584 per unit.
	// Snow <=.30 and hot desert >=.70 therefore have a >7100-unit gap before
	// presentation filtering. See BiomesFirstSlice.md for the 128m acceptance gate.
	public const float ClimateScale = 65536f;
	public const int Count = 10;
	public static readonly Vector4 CoastBand = new( -96f, -32f, 32f, 128f );
	public const float CoastReach = 640f;
	public const float CoastCrossingFade = 16f;
	public const float MaximumReliefFraction = 0.02f;
	public const float ReliefScale = 2048f;
	private const uint TemperatureSalt = 0x4F1BBCDCu;
	private const uint MoistureSalt = 0x6C8E9CF5u;
	private const uint DetailSalt = 0xA511E9B3u;
	private const uint CoverSalt = 0x63D83595u;
	private const uint ReliefSalt = 0x9E3779B9u;
	private const uint MarshSalt = 0xB5297A4Du;

	internal readonly record struct Sample( float Temperature, float Moisture, float Snow,
		float Desert, float Forest, float Jungle )
	{
		public float Open => MathF.Max( 0f, 1f - Snow - Desert - Forest - Jungle );
		public float MarshClimate => Smooth( (Temperature - 0.30f) / 0.20f ) * Smooth( (Moisture - 0.60f) / 0.25f );
		public float MarshWeight( float naturalHeight, float mountains, float seaLevel ) =>
			MarshClimate * Smooth( (naturalHeight - seaLevel + 128f) / 96f ) *
			(1f - Smooth( (naturalHeight - seaLevel - 64f) / 128f )) * (1f - Smooth( mountains / 0.5f ));
		/// <summary>Pass SampleNatural: marine basins exclude inland river carving and biome relief.</summary>
		public float Weight( TerrainBiome biome, RegionalLandforms.Sample naturalLandform, float seaLevel, float coastAffinity )
		{
			var marsh = MarshWeight( naturalLandform.Height, naturalLandform.Mountains, seaLevel );
			var coast = (1f - marsh) * coastAffinity;
			if ( biome == TerrainBiome.Marsh ) return marsh;
			if ( biome == TerrainBiome.Coastline ) return coast;
			var remaining = 1f - marsh - coast;
			if ( naturalLandform.Height < seaLevel ) return biome == TerrainBiome.Ocean ? remaining : 0f;
			var mountains = Smooth( (naturalLandform.Mountains - 0.35f) / 0.35f );
			var hills = Math.Clamp( naturalLandform.Hills / MathF.Max( 0.00001f, naturalLandform.Hills + naturalLandform.Plains ), 0f, 1f );
			return remaining * (biome switch
			{
				TerrainBiome.Snow => Snow,
				TerrainBiome.Desert => Desert,
				TerrainBiome.Forest => Forest,
				TerrainBiome.Jungle => Jungle,
				TerrainBiome.Mountains => Open * mountains,
				TerrainBiome.Hills => Open * (1f - mountains) * hills,
				TerrainBiome.Plains => Open * (1f - mountains) * (1f - hills),
				_ => 0f
			});
		}

		/// <summary>Uses the same unconditioned landform contract as Weight.</summary>
		public TerrainBiome Dominant( RegionalLandforms.Sample naturalLandform, float seaLevel, float coastAffinity )
		{
			var selected = TerrainBiome.Ocean;
			var highest = -1f;
			for ( var index = 0; index < Count; index++ )
			{
				var biome = (TerrainBiome)index;
				var weight = Weight( biome, naturalLandform, seaLevel, coastAffinity );
				if ( weight > highest ) { selected = biome; highest = weight; }
			}
			return selected;
		}
	}

	/// <summary>Bounded local natural shoreline affinity, excluding river/biome carving.</summary>
	public static float SampleCoastline( Vector3 position, ProceduralTerrainSettings settings, float naturalHeight )
	{
		var height = naturalHeight - settings.SeaLevel;
		if ( height <= CoastBand.x || height >= CoastBand.w ) return 0f;
		var band = Smooth( (height - CoastBand.x) / (CoastBand.y - CoastBand.x) ) *
			(1f - Smooth( (height - CoastBand.z) / (CoastBand.w - CoastBand.z) ));
		var minimum = naturalHeight;
		var maximum = naturalHeight;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var offset = direction switch
			{
				0 => new Vector3( CoastReach, 0f, 0f ),
				1 => new Vector3( -CoastReach, 0f, 0f ),
				2 => new Vector3( 0f, CoastReach, 0f ),
				_ => new Vector3( 0f, -CoastReach, 0f )
			};
			var neighbor = RegionalLandforms.SampleNatural( position + offset, settings ).Height;
			minimum = MathF.Min( minimum, neighbor );
			maximum = MathF.Max( maximum, neighbor );
			if ( minimum <= settings.SeaLevel - CoastCrossingFade && maximum >= settings.SeaLevel + CoastCrossingFade ) break;
		}
		return band * Smooth( (settings.SeaLevel - minimum) / CoastCrossingFade ) *
			Smooth( (maximum - settings.SeaLevel) / CoastCrossingFade );
	}

	public static Sample SampleWorld( Vector3 position, ProceduralTerrainSettings settings, float mountains )
	{
		var seed = unchecked((uint)settings.WorldSeed);
		var temperature = RegionalLandforms.Noise( position, ClimateScale, seed ^ TemperatureSalt, out _ ) * 0.85f +
			RegionalLandforms.Noise( position, ClimateScale * 0.5f, seed ^ TemperatureSalt ^ DetailSalt, out _ ) * 0.15f;
		var moisture = RegionalLandforms.Noise( position, ClimateScale, seed ^ MoistureSalt, out _ ) * 0.85f +
			RegionalLandforms.Noise( position, ClimateScale * 0.5f, seed ^ MoistureSalt ^ DetailSalt, out _ ) * 0.15f;
		temperature = Math.Clamp( 1.5f * temperature - 0.25f, 0f, 1f );
		moisture = Math.Clamp( 1.5f * moisture - 0.25f, 0f, 1f );
		var snow = Smooth( (0.30f - temperature) / 0.15f );
		var desert = (1f - snow) * Smooth( (temperature - 0.70f) / 0.15f ) * Smooth( (0.40f - moisture) / 0.20f );
		var wooded = (1f - snow - desert) * (1f - Smooth( (mountains - 0.55f) / 0.25f ));
		var jungle = wooded * Smooth( (temperature - 0.60f) / 0.20f ) * Smooth( (moisture - 0.55f) / 0.20f );
		var forest = MathF.Max( 0f, wooded - jungle ) * Smooth( (moisture - 0.38f) / 0.20f );
		return new( temperature, moisture, snow, desert, forest, jungle );
	}

	// A coherent coverage threshold makes compatible patches through the climate
	// transition. The threshold never leaves (0,1), so zero eligibility stays zero.
	public static float CoverThreshold( Vector3 position, ProceduralTerrainSettings settings ) =>
		0.05f + 0.90f * RegionalLandforms.Noise( position, 512f,
			unchecked((uint)settings.WorldSeed) ^ CoverSalt, out _ );

	public static float RefineHeight( Vector3 position, ProceduralTerrainSettings settings,
		float naturalHeight, float riverHeight, float mountains )
	{
		var marshAltitude = naturalHeight - settings.SeaLevel;
		if ( marshAltitude > -128f && marshAltitude < 192f && mountains < 0.5f )
		{
			var marsh = SampleWorld( position, settings, mountains ).MarshWeight( naturalHeight, mountains, settings.SeaLevel ) *
				(1f - Smooth( (naturalHeight - riverHeight) / 16f ));
			if ( marsh > 0f )
			{
				var basin = RegionalLandforms.Noise( position, 512f,
					unchecked((uint)settings.WorldSeed) ^ MarshSalt, out _ );
				// Expand existing basins without relocating their seeded centers.
				// This profile never raises the old target or changes its depth range.
				basin *= 0.5f + 0.5f * basin;
				var target = settings.SeaLevel - 24f + 48f * basin;
				var maximum = settings.ReliefHeight * MaximumReliefFraction;
				return riverHeight + Math.Clamp( target - riverHeight, -maximum, maximum ) * marsh;
			}
		}
		var protection = Smooth( (riverHeight - settings.SeaLevel - 64f) / 256f ) *
			(1f - Smooth( mountains / 0.5f )) * (1f - Smooth( (naturalHeight - riverHeight) / 16f ));
		if ( protection <= 0f ) return riverHeight;
		var habitat = SampleWorld( position, settings, mountains );
		if ( habitat.Desert <= 0f ) return riverHeight;
		var relief = 2f * RegionalLandforms.Noise( position, ReliefScale,
			unchecked((uint)settings.WorldSeed) ^ ReliefSalt, out _ ) - 1f;
		return riverHeight + relief * habitat.Desert * protection * settings.ReliefHeight * 0.005f;
	}

	private static float Smooth( float value )
	{
		var t = Math.Clamp( value, 0f, 1f );
		return t * t * (3f - 2f * t);
	}
}
