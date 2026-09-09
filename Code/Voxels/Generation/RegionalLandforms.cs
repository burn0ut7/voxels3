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
	internal readonly record struct Sample( float Height, float Land, float Mountains, float Plains, float Hills );
	private const uint ContinentalSalt = 0xB5297A4Du;
	private const uint MountainSalt = 0x68E31DA4u;
	private const uint PlainsSalt = 0x1B56C4E9u;
	private const uint ShapeSalt = 0x7F4A7C15u;
	private const uint DetailSalt = 0x94D049BBu;
	private const uint FineReliefSalt = 0xD1B54A35u;

	public static Sample SampleWorld( Vector3 position, ProceduralTerrainSettings settings )
	{
		var seed = unchecked((uint)settings.WorldSeed);
		var n = Noise( position, settings.ContinentalScale, seed ^ ContinentalSalt );
		var m = Noise( position, settings.MountainRegionScale, seed ^ MountainSalt );
		var p = Noise( position, settings.LocalLandformScale * 2f, seed ^ PlainsSalt );
		var q = Noise( position, settings.LocalLandformScale, seed ^ ShapeSalt );
		var d = Noise( position, settings.LocalLandformScale * 0.5f, seed ^ DetailSalt );
		var f = Noise( position, settings.LocalLandformScale * 0.125f, seed ^ FineReliefSalt );
		var land = Eligibility( n, settings.LandAmount );
		var mountainPreference = Eligibility( m, settings.MountainAmount * (2f - settings.MountainAmount) );
		var mountains = mountainPreference * mountainPreference * mountainPreference * Smooth( (land - 0.5f) * 2f );
		var hillPreference = 1f - Eligibility( p, settings.PlainsAmount );
		var plains = (1f - mountains) * (1f - hillPreference * hillPreference);
		var hills = 1f - mountains - plains;
		// Sharper crests and a bounded steep rise create valleys and cliff bands.
		var ridge = 1f - MathF.Abs( 2f * q - 1f );
		var cliff = Smooth( (ridge - 0.55f) / 0.12f ) * Smooth( 2f * mountains );
		var mountainHeight = 0.10f + 0.55f * ridge * ridge * ridge + 0.28f * cliff +
			0.08f * settings.Ruggedness * (f - 0.5f) * ridge;
		var mounds = Smooth( (f - 0.45f) * 4f );
		var landHeight = plains * (0.035f + 0.015f * q) +
			hills * (0.08f + 0.24f * (0.65f * q + 0.35f * d) +
				0.08f * settings.Ruggedness * mounds) + mountains * mountainHeight;
		var oceanHeight = -0.06f - 0.64f * (1f - n) * (1f - n);
		return new Sample( ((1f - land) * oceanHeight + land * landHeight) * settings.ReliefHeight,
			land, mountains, plains, hills );
	}

	// Each partial of smooth bilinear [0,1] value noise is at most 1.5.
	// Center +/- 1.5*(halfX+halfY)/scale therefore encloses the entire rectangle,
	// including lattice crossings. Interval arithmetic propagates every dependency.
	public static (float Minimum, float Maximum) BoundHeight( SdfWorldAabb bounds, ProceduralTerrainSettings settings )
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
		var d = Field( settings.LocalLandformScale * 0.5f, DetailSalt );
		var f = Field( settings.LocalLandformScale * 0.125f, FineReliefSalt );
		var land = Eligibility( n, settings.LandAmount );
		var mountainPreference = Eligibility( m, settings.MountainAmount * (2f - settings.MountainAmount) );
		var mountains = mountainPreference * Square( mountainPreference ) * Smooth( (land - 0.5d) * 2d );
		var preference = 1d - Square( 1d - Eligibility( p, settings.PlainsAmount ) );
		var plains = (1d - mountains) * preference;
		var hills = (1d - mountains) * (1d - preference);
		var ridge = 1d - Absolute( 2d * q - 1d );
		var cliff = Smooth( (ridge - 0.55d) * (1d / 0.12d) ) * Smooth( 2d * mountains );
		var mountainHeight = 0.10d + 0.55d * ridge * Square( ridge ) + 0.28d * cliff +
			0.08d * settings.Ruggedness * (f - 0.5d) * ridge;
		var mounds = Smooth( (f - 0.45d) * 4d );
		var landHeight = plains * (0.035d + 0.015d * q) +
			hills * (0.08d + 0.24d * (0.65d * q + 0.35d * d) +
				0.08d * settings.Ruggedness * mounds) + mountains * mountainHeight;
		var oceanHeight = -0.06d - 0.64d * Square( 1d - n );
		var height = ((1d - land) * oceanHeight + land * landHeight) * settings.ReliefHeight;
		// Covers float expression rounding in the scalar CPU and shader recipes.
		var padding = settings.ReliefHeight * 0.0001d;
		return (MathF.BitDecrement( (float)(Math.Max( -0.70d * settings.ReliefHeight, height.Minimum ) - padding) ),
			MathF.BitIncrement( (float)(Math.Min( 1.04d * settings.ReliefHeight, height.Maximum ) + padding) ));
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
