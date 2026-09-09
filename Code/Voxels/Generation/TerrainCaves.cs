using System;
using static TerrainNoise;

// Retained version-9 cave recipe and its conservative bounds.
internal static class TerrainCaves
{
	public const float NoodleAWavelength = 6144f;
	public const float NoodleBWavelength = 6912f;
	public const float ThicknessWavelength = 16384f;
	public const float CheeseWavelength = 8192f;
	public const float CaveRegionWavelength = 16384f;
	public const float CaveRegionThreshold = 0.36f;
	public const float CaveDensityScale = 512f;
	public const float CaveMinimumDepth = 512f;
	public const float CaveMaximumDepth = 32768f;
	public const float NoodleBaseThreshold = 0.056f;
	public const float NoodleThicknessVariation = 0.016f;
	public const float CheeseBaseThreshold = 0.48f;
	public const float CheeseThresholdVariation = 0.12f;
	internal const float MaximumRawCaveDensity = CaveDensityScale *
		(1f - CheeseBaseThreshold + CheeseThresholdVariation);

	private const uint NoodleASeedSalt = 0xA511E9B3u;
	private const uint NoodleBSeedSalt = 0x63D83595u;
	private const uint ThicknessSeedSalt = 0xC2B2AE35u;
	private const uint CheeseSeedSalt = 0x27D4EB2Fu;
	private const uint CaveRegionSeedSalt = 0x9E3779B9u;
	// For each fixed contribution, ||grad((0.6-r^2)^4 dot(g,x))|| < 0.164.
	// Four contributions scaled by32 stay below21;22 leaves rounding margin.
	private const double Simplex3DLipschitzBound = 22d;
	private const double SimplexFinitePrecisionPadding = 0.0001d;

	internal readonly record struct DensityInterval( float Minimum, float Maximum );

	internal static float SampleWorld( Vector3 worldPosition, ProceduralTerrainSettings settings, float surfaceHeight )
	{
		var surfaceDensity = worldPosition.z - surfaceHeight;
		var depth = -surfaceDensity;
		var envelope = MathF.Min( depth - CaveMinimumDepth, CaveMaximumDepth - depth );
		// min(caves, envelope) cannot beat the surface in this range. Keep the
		// canonical field value while avoiding irrelevant cave and region queries.
		if ( envelope <= surfaceDensity ) return surfaceDensity;
		// Raw caves are >= -scale; the regional mask is >= -(1+cutoff)*scale.
		// Two scales conservatively enclose both, before evaluating either noise.
		if ( envelope <= -2f * CaveDensityScale ) return MathF.Max( surfaceDensity, envelope );
		var seed = unchecked((uint)settings.WorldSeed);
		// Raising the regional cutoff removes cave-bearing areas without changing
		// the underlying passage recipe or adding another noise evaluation.
		var regionDensity = CaveDensityScale * (CaveRegionNoise(
			worldPosition / CaveRegionWavelength, seed ^ CaveRegionSeedSalt ) - CaveRegionThreshold);
		envelope = MathF.Min( envelope, regionDensity );
		if ( envelope <= surfaceDensity ) return surfaceDensity;
		// Raw caves are >= -scale; the regional mask is >= -(1+cutoff)*scale.
		// Two scales conservatively enclose both, before evaluating either noise.
		if ( envelope <= -2f * CaveDensityScale ) return MathF.Max( surfaceDensity, envelope );
		var thickness = SimplexNoise3D(
			worldPosition, ThicknessWavelength,
			seed ^ ThicknessSeedSalt );
		var threshold = NoodleBaseThreshold + NoodleThicknessVariation * thickness;
		var cheese = SimplexNoise3D( worldPosition, CheeseWavelength, seed ^ CheeseSeedSalt );
		var cheeseThreshold = CheeseBaseThreshold - CheeseThresholdVariation * thickness;
		var cheeseDensity = CaveDensityScale * (cheese - cheeseThreshold);
		var resolvedDensity = MathF.Max( surfaceDensity, MathF.Min( cheeseDensity, envelope ) );
		// A noodle can only reduce these upper bounds. Skip remaining noise only
		// when it cannot change the canonical max/min result; keep ties on the full path.
		if ( CaveDensityScale * threshold < resolvedDensity ) return resolvedDensity;
		var noodleA = SimplexNoise3D( worldPosition, NoodleAWavelength, seed ^ NoodleASeedSalt );
		if ( CaveDensityScale * (threshold - MathF.Abs( noodleA )) < resolvedDensity ) return resolvedDensity;
		var noodleB = SimplexNoise3D( worldPosition, NoodleBWavelength, seed ^ NoodleBSeedSalt );
		var tunnelDensity = CaveDensityScale *
			(threshold - MathF.Max( MathF.Abs( noodleA ), MathF.Abs( noodleB ) ));
		var caveDensity = MathF.Min( MathF.Max( tunnelDensity, cheeseDensity ), envelope );
		return MathF.Max( surfaceDensity, caveDensity );
	}

	internal static DensityInterval BoundCaveDensity(
		SdfWorldAabb worldAabb,
		DensityInterval surface,
		uint seed )
	{
		var minimumDepth = -surface.Maximum;
		var maximumDepth = -surface.Minimum;
		var envelopeAtMinimum = MathF.Min(
			minimumDepth - CaveMinimumDepth,
			CaveMaximumDepth - minimumDepth );
		var envelopeAtMaximum = MathF.Min(
			maximumDepth - CaveMinimumDepth,
			CaveMaximumDepth - maximumDepth );
		var envelopeMidpoint = (CaveMinimumDepth + CaveMaximumDepth) * 0.5f;
		var envelopeMaximum = minimumDepth <= envelopeMidpoint &&
			maximumDepth >= envelopeMidpoint
			? (CaveMaximumDepth - CaveMinimumDepth) * 0.5f
			: MathF.Max( envelopeAtMinimum, envelopeAtMaximum );
		var envelope = new DensityInterval(
			MathF.Min( envelopeAtMinimum, envelopeAtMaximum ),
			envelopeMaximum );
		if ( envelope.Maximum <= -2f * CaveDensityScale ) return envelope;
		var center = worldAabb.Minimum + (worldAabb.Maximum - worldAabb.Minimum) * 0.5f;
		var halfExtent = (worldAabb.Maximum - worldAabb.Minimum) * 0.5f;
		var regionCenter = CaveRegionNoise( center / CaveRegionWavelength, seed ^ CaveRegionSeedSalt );
		// Smoothed trilinear values in [-1,1] have each partial bounded by 3;
		// sqrt(27) < 6 bounds their gradient across shared lattice boundaries.
		var regionVariation = 6d * Math.Sqrt(
			(double)halfExtent.x * halfExtent.x +
			(double)halfExtent.y * halfExtent.y +
			(double)halfExtent.z * halfExtent.z ) / CaveRegionWavelength + SimplexFinitePrecisionPadding;
		var region = new DensityInterval(
			(float)Math.Clamp( regionCenter - regionVariation, -1d, 1d ),
			(float)Math.Clamp( regionCenter + regionVariation, -1d, 1d ) );
		envelope = new DensityInterval(
			MathF.Min( envelope.Minimum, CaveDensityScale * (region.Minimum - CaveRegionThreshold) ),
			MathF.Min( envelope.Maximum, CaveDensityScale * (region.Maximum - CaveRegionThreshold) ) );
		// The surface dominates the entire cave envelope; noise cannot affect
		// the final density interval, so avoid all four volumetric bounds.
		if ( envelope.Maximum < surface.Minimum ) return envelope;
		var noodleA = BoundSimplex3D( worldAabb, NoodleAWavelength, seed ^ NoodleASeedSalt );
		var noodleB = BoundSimplex3D( worldAabb, NoodleBWavelength, seed ^ NoodleBSeedSalt );
		var thickness = BoundSimplex3D( worldAabb, ThicknessWavelength, seed ^ ThicknessSeedSalt );
		var cheese = BoundSimplex3D( worldAabb, CheeseWavelength, seed ^ CheeseSeedSalt );
		var absoluteA = AbsoluteInterval( noodleA );
		var absoluteB = AbsoluteInterval( noodleB );
		var maximumAbsolute = new DensityInterval(
			MathF.Max( absoluteA.Minimum, absoluteB.Minimum ),
			MathF.Max( absoluteA.Maximum, absoluteB.Maximum ) );
		var threshold = new DensityInterval(
			NoodleBaseThreshold + NoodleThicknessVariation * thickness.Minimum,
			NoodleBaseThreshold + NoodleThicknessVariation * thickness.Maximum );
		var tunnel = new DensityInterval(
			CaveDensityScale * (threshold.Minimum - maximumAbsolute.Maximum),
			CaveDensityScale * (threshold.Maximum - maximumAbsolute.Minimum) );
		var cheeseThreshold = new DensityInterval(
			CheeseBaseThreshold - CheeseThresholdVariation * thickness.Maximum,
			CheeseBaseThreshold - CheeseThresholdVariation * thickness.Minimum );
		var cavern = new DensityInterval(
			CaveDensityScale * (cheese.Minimum - cheeseThreshold.Maximum),
			CaveDensityScale * (cheese.Maximum - cheeseThreshold.Minimum) );
		var union = new DensityInterval(
			MathF.Max( tunnel.Minimum, cavern.Minimum ),
			MathF.Max( tunnel.Maximum, cavern.Maximum ) );

		return new DensityInterval(
			MathF.Min( union.Minimum, envelope.Minimum ),
			MathF.Min( union.Maximum, envelope.Maximum ) );
	}

	private static DensityInterval BoundSimplex3D( SdfWorldAabb bounds, float wavelength, uint seed )
	{
		// A gradient bound cannot cross the retained kernel's cell/rank jumps.
		if ( !HasSingleSimplexCell( bounds, wavelength ) ) return new DensityInterval( -1f, 1f );
		var center = bounds.Minimum + (bounds.Maximum - bounds.Minimum) * 0.5f;
		// Use actual rounded center-to-endpoint distances, not half-size alone.
		var dx = Math.Max( (double)center.x - bounds.Minimum.x, (double)bounds.Maximum.x - center.x );
		var dy = Math.Max( (double)center.y - bounds.Minimum.y, (double)bounds.Maximum.y - center.y );
		var dz = Math.Max( (double)center.z - bounds.Minimum.z, (double)bounds.Maximum.z - center.z );
		// Both sample and center can move half a quantization step on each axis.
		var radius = (Math.Sqrt( dx * dx + dy * dy + dz * dz ) +
			Math.Sqrt( 3d ) / SimplexCoordinateScale) / wavelength;
		var variation = Simplex3DLipschitzBound * radius + SimplexFinitePrecisionPadding;
		var centerNoise = SimplexNoise3D( center, wavelength, seed );
		return new DensityInterval(
			MathF.BitDecrement( (float)Math.Clamp( centerNoise - variation, -1d, 1d ) ),
			MathF.BitIncrement( (float)Math.Clamp( centerNoise + variation, -1d, 1d ) ) );
	}

	private static DensityInterval AbsoluteInterval( DensityInterval interval )
	{
		var minimum = interval.Minimum <= 0f && interval.Maximum >= 0f
			? 0f
			: MathF.Min( MathF.Abs( interval.Minimum ), MathF.Abs( interval.Maximum ) );
		return new DensityInterval(
			minimum,
			MathF.Max( MathF.Abs( interval.Minimum ), MathF.Abs( interval.Maximum ) ) );
	}

	private static float CaveRegionNoise( Vector3 position, uint seed )
	{
		var x = (int)MathF.Floor( position.x );
		var y = (int)MathF.Floor( position.y );
		var z = (int)MathF.Floor( position.z );
		var fraction = position - new Vector3( x, y, z );
		var blend = fraction * fraction * (new Vector3( 3f ) - 2f * fraction);
		var value = 0f;
		for ( var corner = 0; corner < 8; corner++ )
		{
			var dx = corner & 1;
			var dy = (corner >> 1) & 1;
			var dz = (corner >> 2) & 1;
			var weight = (dx == 0 ? 1f - blend.x : blend.x) *
				(dy == 0 ? 1f - blend.y : blend.y) * (dz == 0 ? 1f - blend.z : blend.z);
			var sample = (Hash( x + dx, y + dy, z + dz, seed ) & 65535u) / 32767.5f - 1f;
			value += sample * weight;
		}
		return Math.Clamp( value, -1f, 1f );
	}

}
