using System;

/// <summary>Immutable world recipe. Amounts are eligibility biases, not area percentages.</summary>
public readonly record struct ProceduralTerrainSettings(
	int WorldSeed = ProceduralTerrainSdf.DefaultWorldSeed,
	float LandAmount = ProceduralTerrainSettings.DefaultLandAmount,
	float MountainAmount = ProceduralTerrainSettings.DefaultMountainAmount,
	float PlainsAmount = ProceduralTerrainSettings.DefaultPlainsAmount,
	float ContinentalScale = ProceduralTerrainSettings.DefaultContinentalScale,
	float MountainRegionScale = ProceduralTerrainSettings.DefaultMountainRegionScale,
	float LocalLandformScale = ProceduralTerrainSettings.DefaultLocalLandformScale,
	float ReliefHeight = ProceduralTerrainSettings.DefaultReliefHeight,
	float Ruggedness = ProceduralTerrainSettings.DefaultRuggedness,
	float SeaLevel = SurfaceWater.DefaultSeaLevel )
{
	public const float DefaultLandAmount = 0.60f;
	public const float DefaultMountainAmount = 0.35f;
	public const float DefaultPlainsAmount = 0.60f;
	public const float DefaultContinentalScale = 131072f;
	public const float DefaultMountainRegionScale = 32768f;
	public const float DefaultLocalLandformScale = 8192f;
	public const float DefaultReliefHeight = 3072f;
	public const float DefaultRuggedness = 0.45f;

	public static ProceduralTerrainSettings Default => new( WorldSeed: ProceduralTerrainSdf.DefaultWorldSeed );

	public bool IsValid => WorldSeed >= -16777216 && WorldSeed <= 16777216 &&
		LandAmount >= 0f && LandAmount <= 1f && MountainAmount >= 0f && MountainAmount <= 1f &&
		PlainsAmount >= 0f && PlainsAmount <= 1f && Ruggedness >= 0f && Ruggedness <= 1f &&
		ContinentalScale >= 32768f && ContinentalScale <= 524288f &&
		MountainRegionScale >= 8192f && MountainRegionScale <= 131072f &&
		LocalLandformScale >= 2048f && LocalLandformScale <= 32768f &&
		ReliefHeight >= 512f && ReliefHeight <= 8192f &&
		SeaLevel >= SurfaceWater.MinimumSeaLevel && SeaLevel <= SurfaceWater.MaximumSeaLevel &&
		ContinentalScale >= 2f * MountainRegionScale && MountainRegionScale >= 2f * LocalLandformScale;
}

/// <summary>Pure XY landforms and conservative interval evaluation. No world or cache state.</summary>
internal static class RegionalLandforms
{
	internal readonly record struct Sample( float Height, float Land, float Mountains, float Plains, float Hills, float ErosionExposure, float PeakFraction );
	private const uint ContinentalSalt = 0xB5297A4Du;
	private const uint MountainSalt = 0x68E31DA4u;
	private const uint PlainsSalt = 0x1B56C4E9u;
	private const uint ShapeSalt = 0x7F4A7C15u;
	private const uint HillShapeSalt = 0x94D049BBu;
	private const uint HillPatchSalt = 0xD1B54A35u;
	private const uint UplandSalt = 0xA24BAED5u;

	// Independent widths keep hill slopes broader than the former detail mounds.
	internal const float MountainWidthScale = 1.30f;
	private const float HillWidthScale = 0.75f;
	private const float HillPatchScale = 5f;
	internal const float MinimumHeightFraction = -0.70f - TerrainErosion.MaximumOffsetFraction;
	internal const float MaximumHeightFraction = 1.72f + TerrainErosion.MaximumOffsetFraction;

	public static Sample SampleWorld( Vector3 position, ProceduralTerrainSettings settings )
	{
		var natural = SampleNatural( position, settings );
		var river = RiverWorld.For( settings ).GetPatch( RiverNetwork.PatchAt( position ) )
			.SampleWorld( position, natural.Height, settings.SeaLevel );
		return natural with { Height = river.Height };
	}

	internal static Sample SampleNatural( Vector3 position, ProceduralTerrainSettings settings )
	{
		var sample = SampleBase( position, settings, out var gradient );
		var amplitude = TerrainErosion.MountainStrength *
			Smooth( (sample.Mountains - TerrainErosion.MinimumMountainWeight) / (1f - TerrainErosion.MinimumMountainWeight) ) * sample.ErosionExposure *
			Smooth( (sample.Land - 0.65f) / 0.35f ) * Smooth( (sample.Height - settings.SeaLevel - 64f) / 256f ) * settings.ReliefHeight;
		if ( amplitude <= 0f )
		{
			return sample;
		}
		var offset = TerrainErosion.Offset( new Vector2( position.x, position.y ), gradient,
			settings.LocalLandformScale * TerrainErosion.WavelengthFraction, amplitude, unchecked((uint)settings.WorldSeed) );
		return sample with { Height = sample.Height + offset };
	}

	private static Sample SampleBase( Vector3 position, ProceduralTerrainSettings settings, out Vector2 gradient )
	{
		var seed = unchecked((uint)settings.WorldSeed);
		var n = Noise( position, settings.ContinentalScale, seed ^ ContinentalSalt );
		var m = Noise( position, settings.MountainRegionScale, seed ^ MountainSalt );
		var land = Eligibility( n, settings.LandAmount );
		var mountainPreference = Eligibility( m, settings.MountainAmount * (2f - settings.MountainAmount) );
		var mountains = mountainPreference * mountainPreference * mountainPreference * Smooth( (land - 0.5f) * 2f );
		var p = Noise( position, settings.LocalLandformScale * 2f, seed ^ PlainsSalt );
		var q = Noise( position, settings.LocalLandformScale, seed ^ ShapeSalt );
		var hillShape = Noise( position, settings.LocalLandformScale * HillWidthScale, seed ^ HillShapeSalt );
		var hillPatch = Noise( position, settings.LocalLandformScale * HillPatchScale, seed ^ HillPatchSalt );
		var hillPreference = (1f - Eligibility( p, settings.PlainsAmount )) * Smooth( (hillPatch - 0.25f) * 2.5f );
		var plains = (1f - mountains) * (1f - hillPreference * hillPreference);
		var hills = 1f - mountains - plains;
		// Gentle transition terrain; folded value-noise contours produced straight ridges.
		var mountainHeight = 0.10f + 0.28f * q * q;
		gradient = default;
		var exposure = 0f;
		var peakFraction = 0f;
		if ( mountains > TerrainErosion.MinimumMountainWeight )
		{
			var shapeBlend = Smooth( (mountains - TerrainErosion.MinimumMountainWeight) / (1f - TerrainErosion.MinimumMountainWeight) );
			var mass = MountainMasses.Sample( position, settings.LocalLandformScale * MountainWidthScale, seed, out var massGradient, out peakFraction, out var erosionStrength );
			var broadMountainHeight = 0.10f + 0.83f * mass;
			exposure = Smooth( (mass - 0.10f) / 0.40f ) * Smooth( (1f - mass) / 0.1f ) * erosionStrength;
			gradient = massGradient * (0.83f * mountains * land * settings.ReliefHeight);
			mountainHeight = (1f - shapeBlend) * mountainHeight + shapeBlend * broadMountainHeight;
		}
		var landHeight = plains * (0.035f + 0.015f * q) +
			hills * (0.035f + 0.015f * q + 0.28f * (0.7f * hillShape + 0.3f * q)) + mountains * mountainHeight;
		// Regional support spans the gaps between individual mountain groups.
		// Its maximum .68 fraction is included in the global surface bound.
		var uplandWeight = Smooth( (mountainPreference - 0.1f) / 0.9f ) * Smooth( (land - 0.65f) / 0.35f );
		if ( uplandWeight > 0f )
		{
			var elevation = Noise( position, settings.MountainRegionScale * 1.35f, seed ^ UplandSalt );
			landHeight += uplandWeight * (0.12f + 0.56f * Smooth( (elevation - 0.15f) / 0.7f )) * (0.7f + 0.3f * p);
		}
		var oceanHeight = -0.06f - 0.64f * (1f - n) * (1f - n);
		return new Sample( ((1f - land) * oceanHeight + land * landHeight) * settings.ReliefHeight,
			land, mountains, plains, hills, exposure, peakFraction );
	}

	// Each partial of smooth bilinear [0,1] value noise is at most 1.5.
	// Center +/- 1.5*(halfX+halfY)/scale therefore encloses the entire rectangle,
	// including lattice crossings. Interval arithmetic propagates every dependency.
	public static (float Minimum, float Maximum, float MountainMaximum) BoundHeight( SdfWorldAabb bounds, ProceduralTerrainSettings settings )
	{
		var natural = BoundNaturalHeight( bounds, settings );
		// Fixed-level river carving only lowers the natural exterior. This bound
		// does not construct drainage on the classification thread.
		return (MathF.BitDecrement( MathF.Min( natural.Minimum, settings.SeaLevel - RiverNetwork.MaximumDepth ) ),
			natural.Maximum, natural.MountainMaximum);
	}

	private static (float Minimum, float Maximum, float MountainMaximum) BoundNaturalHeight( SdfWorldAabb bounds, ProceduralTerrainSettings settings )
	{
		var center = bounds.Minimum + (bounds.Maximum - bounds.Minimum) * 0.5f;
		var radius = 0.75d * ((double)bounds.Maximum.x - bounds.Minimum.x + (double)bounds.Maximum.y - bounds.Minimum.y);
		var seed = unchecked((uint)settings.WorldSeed);
		Interval Field( float scale, uint salt )
		{
			var value = Noise( center, scale, seed ^ salt );
			var variation = radius / scale + 0.0001d;
			return new Interval( Math.Max( 0d, value - variation ), Math.Min( 1d, value + variation ) );
		}
		var n = Field( settings.ContinentalScale, ContinentalSalt );
		var m = Field( settings.MountainRegionScale, MountainSalt );
		var p = Field( settings.LocalLandformScale * 2f, PlainsSalt );
		var q = Field( settings.LocalLandformScale, ShapeSalt );
		var hillShape = Field( settings.LocalLandformScale * HillWidthScale, HillShapeSalt );
		var hillPatch = Field( settings.LocalLandformScale * HillPatchScale, HillPatchSalt );
		var land = Eligibility( n, settings.LandAmount );
		var mountainPreference = Eligibility( m, settings.MountainAmount * (2f - settings.MountainAmount) );
		var mountains = mountainPreference * Square( mountainPreference ) * Smooth( (land - 0.5d) * 2d );
		var hillPreference = (1d - Eligibility( p, settings.PlainsAmount )) * Smooth( (hillPatch - 0.25d) * 2.5d );
		var preference = 1d - Square( hillPreference );
		var plains = (1d - mountains) * preference;
		var hills = (1d - mountains) * (1d - preference);
		var mountainHeight = 0.10d + 0.28d * Square( q );
		var erosionExposure = new Interval( 0d, 0d );
		if ( mountains.Maximum > TerrainErosion.MinimumMountainWeight )
		{
			var massBounds = MountainMasses.Bound( bounds, settings.LocalLandformScale * MountainWidthScale, seed );
			var mass = new Interval( massBounds.Minimum, massBounds.Maximum );
			var shapeBlend = Smooth( (mountains - TerrainErosion.MinimumMountainWeight) * (1d / (1d - TerrainErosion.MinimumMountainWeight)) );
			var broadMountainHeight = 0.10d + 0.83d * mass;
			mountainHeight = (1d - shapeBlend) * mountainHeight + shapeBlend * broadMountainHeight;
			erosionExposure = Smooth( (mass - 0.10d) * 2.5d ) * Smooth( (1d - mass) * 10d );
		}
		var landHeight = plains * (0.035d + 0.015d * q) +
			hills * (0.035d + 0.015d * q + 0.28d * (0.7d * hillShape + 0.3d * q)) + mountains * mountainHeight;
		var uplandWeight = Smooth( (mountainPreference - 0.1d) * (1d / 0.9d) ) * Smooth( (land - 0.65d) * (1d / 0.35d) );
		if ( uplandWeight.Maximum > 0d )
		{
			var elevation = Field( settings.MountainRegionScale * 1.35f, UplandSalt );
			landHeight += uplandWeight * (0.12d + 0.56d * Smooth( (elevation - 0.15d) * (1d / 0.7d) )) * (0.7d + 0.3d * p);
		}
		var oceanHeight = -0.06d - 0.64d * Square( 1d - n );
		var height = ((1d - land) * oceanHeight + land * landHeight) * settings.ReliefHeight;
		// Each faded octave stays in [-1,1]. Bound its amplitude before any direction sampling.
		var erosionWeight = TerrainErosion.MountainStrength *
			erosionExposure *
			Smooth( (mountains - TerrainErosion.MinimumMountainWeight) * (1d / (1d - TerrainErosion.MinimumMountainWeight)) ) *
			Smooth( (land - 0.65d) * (1d / 0.35d) ) *
			Smooth( (height - settings.SeaLevel - 64d) * (1d / 256d) );
		var erosion = Math.Clamp( erosionWeight.Maximum, 0d, TerrainErosion.MountainStrength ) *
			settings.ReliefHeight * TerrainErosion.AmplitudeSum;
		// Covers float expression rounding in the scalar CPU and shader recipes.
		var padding = settings.ReliefHeight * 0.0001d;
		return (MathF.BitDecrement( (float)(Math.Max( -0.70d * settings.ReliefHeight, height.Minimum ) - erosion - padding) ),
			MathF.BitIncrement( (float)(Math.Min( 1.72d * settings.ReliefHeight, height.Maximum ) + erosion + padding) ),
			(float)Math.Min( 1d, mountains.Maximum + 0.0001d ) );
	}

	private static float Noise( Vector3 position, float scale, uint seed )
	{
		var x = position.x / scale;
		var y = position.y / scale;
		var ix = (int)MathF.Floor( x );
		var iy = (int)MathF.Floor( y );
		var u = Smooth( x - ix );
		var v = Smooth( y - iy );
		var a = (TerrainNoise.Hash( ix, iy, seed ) & 65535u) / 65535f;
		var b = (TerrainNoise.Hash( ix + 1, iy, seed ) & 65535u) / 65535f;
		var c = (TerrainNoise.Hash( ix, iy + 1, seed ) & 65535u) / 65535f;
		var d = (TerrainNoise.Hash( ix + 1, iy + 1, seed ) & 65535u) / 65535f;
		return Math.Clamp( (a * (1f - u) + b * u) * (1f - v) + (c * (1f - u) + d * u) * v, 0f, 1f );
	}

	private static float Eligibility( float value, float amount ) =>
		amount <= 0f ? 0f : amount >= 1f ? 1f : Smooth( (value - (1.2f - 1.4f * amount)) / 0.2f );
	private static float Smooth( float value )
	{
		var t = Math.Clamp( value, 0f, 1f );
		return t * t * (3f - 2f * t);
	}
	private static Interval Eligibility( Interval value, float amount ) =>
		amount <= 0f ? new Interval( 0d, 0d ) : amount >= 1f ? new Interval( 1d, 1d ) : Smooth( (value - (1.2d - 1.4d * amount)) * 5d );
	private static Interval Smooth( Interval value )
	{
		var a = Math.Clamp( value.Minimum, 0d, 1d );
		var b = Math.Clamp( value.Maximum, 0d, 1d );
		return new Interval( a * a * (3d - 2d * a), b * b * (3d - 2d * b) );
	}
	private static Interval Absolute( Interval value ) => new(
		value.Minimum <= 0d && value.Maximum >= 0d ? 0d : Math.Min( Math.Abs( value.Minimum ), Math.Abs( value.Maximum ) ),
		Math.Max( Math.Abs( value.Minimum ), Math.Abs( value.Maximum ) ) );
	private static Interval Square( Interval value ) => new(
		value.Minimum <= 0d && value.Maximum >= 0d ? 0d : Math.Min( value.Minimum * value.Minimum, value.Maximum * value.Maximum ),
		Math.Max( value.Minimum * value.Minimum, value.Maximum * value.Maximum ) );

	private readonly record struct Interval( double Minimum, double Maximum )
	{
		public static implicit operator Interval( double value ) => new( value, value );
		public static Interval operator +( Interval a, Interval b ) => new( a.Minimum + b.Minimum, a.Maximum + b.Maximum );
		public static Interval operator -( Interval a, Interval b ) => new( a.Minimum - b.Maximum, a.Maximum - b.Minimum );
		public static Interval operator *( Interval a, Interval b ) => new(
			Math.Min( Math.Min( a.Minimum * b.Minimum, a.Minimum * b.Maximum ), Math.Min( a.Maximum * b.Minimum, a.Maximum * b.Maximum ) ),
			Math.Max( Math.Max( a.Minimum * b.Minimum, a.Minimum * b.Maximum ), Math.Max( a.Maximum * b.Minimum, a.Maximum * b.Maximum ) ) );
	}
}
