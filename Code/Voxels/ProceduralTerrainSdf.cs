using System;

public readonly record struct ProceduralTerrainSettings(
	int WorldSeed,
	float SurfaceBaseHeight,
	float SurfaceFrequency,
	float SurfaceAmplitude );

public readonly record struct SdfWorldAabb( Vector3 Minimum, Vector3 Maximum );

/// <summary>
/// Canonical deterministic version-4 volumetric terrain field. The GPU mirror
/// uses the same integer hash, simplex recipes, and constructive composition.
/// </summary>
internal static class ProceduralTerrainSdf
{
	// Saved worlds identify this backend revision; it is not a variation control.
	public const int CurrentVersion = 4;
	public const int DefaultWorldSeed = 1337;
	public const float DefaultSurfaceBaseHeight = 0f;
	public const float DefaultSurfaceFrequency = 0.0005f;
	public const float DefaultSurfaceAmplitude = 128f;
	public const float NoodleAWavelength = 6144f;
	public const float NoodleBWavelength = 6912f;
	public const float ThicknessWavelength = 16384f;
	public const float CheeseWavelength = 8192f;
	public const float CaveDensityScale = 512f;
	public const float CaveMaximumDepth = 2048f;
	public const float NoodleBaseThreshold = 0.056f;
	public const float NoodleThicknessVariation = 0.016f;
	public const float CheeseBaseThreshold = 0.48f;
	public const float CheeseThresholdVariation = 0.12f;

	private const float SimplexF2 = 0.3660254037844386f;
	private const float SimplexG2 = 0.21132486540518713f;
	private const float SimplexF3 = 1f / 3f;
	private const float SimplexG3 = 1f / 6f;
	private const uint NoodleASeedSalt = 0xA511E9B3u;
	private const uint NoodleBSeedSalt = 0x63D83595u;
	private const uint ThicknessSeedSalt = 0xC2B2AE35u;
	private const uint CheeseSeedSalt = 0x27D4EB2Fu;
	// Each of the three simplex contributions has gradient magnitude at most
	// 27/343 before the canonical scale of 70. The exact global bound is therefore
	// 3 * 70 * 27/343 = 16.530612... . Seventeen leaves deterministic margin.
	private const double SimplexLipschitzBound = 17d;
	// A normalized 3D gradient has magnitude one. For one contribution
	// (0.6-r^2)^4 dot(g,x), the largest gradient magnitude is below 0.164;
	// four contributions scaled by 32 remain below 21. Twenty-two leaves margin.
	private const double Simplex3DLipschitzBound = 22d;
	private const double SimplexFinitePrecisionPadding = 0.0001d;

	public static float SampleGlobal(
		Vector3Int globalSampleCoordinate,
		float cellSize,
		ProceduralTerrainSettings settings )
	{
		var worldPosition = new Vector3(
			globalSampleCoordinate.x * cellSize,
			globalSampleCoordinate.y * cellSize,
			globalSampleCoordinate.z * cellSize );
		return SampleWorld( worldPosition, settings );
	}

	public static float SampleWorld( Vector3 worldPosition, ProceduralTerrainSettings settings )
	{
		var surfaceHeight = settings.SurfaceBaseHeight + SimplexNoise2D(
			worldPosition.x * settings.SurfaceFrequency,
			worldPosition.y * settings.SurfaceFrequency,
			unchecked((uint)settings.WorldSeed) ) * settings.SurfaceAmplitude;
		var surfaceDensity = worldPosition.z - surfaceHeight;
		var seed = unchecked((uint)settings.WorldSeed);
		var noodleA = SimplexNoise3D( worldPosition / NoodleAWavelength, seed ^ NoodleASeedSalt );
		var noodleB = SimplexNoise3D( worldPosition / NoodleBWavelength, seed ^ NoodleBSeedSalt );
		var thickness = SimplexNoise3D(
			worldPosition / ThicknessWavelength,
			seed ^ ThicknessSeedSalt );
		var threshold = NoodleBaseThreshold + NoodleThicknessVariation * thickness;
		var tunnelDensity = CaveDensityScale *
			(threshold - MathF.Max( MathF.Abs( noodleA ), MathF.Abs( noodleB ) ));
		var cheese = SimplexNoise3D( worldPosition / CheeseWavelength, seed ^ CheeseSeedSalt );
		var cheeseThreshold = CheeseBaseThreshold - CheeseThresholdVariation * thickness;
		var cheeseDensity = CaveDensityScale * (cheese - cheeseThreshold);
		var depth = -surfaceDensity;
		var envelope = MathF.Min( depth, CaveMaximumDepth - depth );
		var caveDensity = MathF.Min( MathF.Max( tunnelDensity, cheeseDensity ), envelope );
		return MathF.Max( surfaceDensity, caveDensity );
	}

	public static ChunkDensityRange ClassifyDensityRange(
		Vector3Int coordinate,
		int cellsPerAxis,
		float cellSize,
		ProceduralTerrainSettings settings )
	{
		var chunkWorldSize = cellsPerAxis * cellSize;
		var minimum = new Vector3(
			coordinate.x * chunkWorldSize,
			coordinate.y * chunkWorldSize,
			coordinate.z * chunkWorldSize );
		return GetConservativeDensityRange(
			new SdfWorldAabb( minimum, minimum + new Vector3( chunkWorldSize ) ),
			cellSize,
			settings );
	}

	/// <summary>
	/// Conservatively bounds the canonical density field over a closed world-space
	/// AABB. The current generator privately projects XY because its exact formula
	/// is separable; callers receive a full 3D density interval and never a height.
	/// </summary>
	public static ChunkDensityRange GetConservativeDensityRange(
		SdfWorldAabb worldAabb,
		float minimumSubdivisionSize,
		ProceduralTerrainSettings settings )
	{
		var minimum = worldAabb.Minimum;
		var maximum = worldAabb.Maximum;
		if ( !float.IsFinite( minimum.x ) || !float.IsFinite( minimum.y ) ||
			!float.IsFinite( minimum.z ) || !float.IsFinite( maximum.x ) ||
			!float.IsFinite( maximum.y ) || !float.IsFinite( maximum.z ) ||
			!float.IsFinite( minimumSubdivisionSize ) || minimumSubdivisionSize <= 0f ||
			!float.IsFinite( settings.SurfaceBaseHeight ) ||
			!float.IsFinite( settings.SurfaceFrequency ) ||
			!float.IsFinite( settings.SurfaceAmplitude ) ||
			minimum.x > maximum.x || minimum.y > maximum.y || minimum.z > maximum.z )
		{
			return new ChunkDensityRange(
				float.NegativeInfinity,
				float.PositiveInfinity,
				ChunkDensityClassification.PotentiallySurfaceContaining );
		}

		var bound = BoundDensityAabb( worldAabb, minimumSubdivisionSize, settings );
		return new ChunkDensityRange(
			MathF.BitDecrement( bound.Minimum ),
			MathF.BitIncrement( bound.Maximum ),
			bound.Classification );
	}

	private static DensityBound BoundDensityAabb(
		SdfWorldAabb worldAabb,
		float minimumSubdivisionSize,
		ProceduralTerrainSettings settings )
	{
		var minimum = worldAabb.Minimum;
		var maximum = worldAabb.Maximum;
		var surfaceRange = BoundSurfaceRectangle(
			minimum.x,
			maximum.x,
			minimum.y,
			maximum.y,
			minimum.z,
			maximum.z,
			minimumSubdivisionSize,
			settings );
		var surface = new DensityInterval(
			MathF.BitDecrement( minimum.z - surfaceRange.MaximumHeight ),
			MathF.BitIncrement( maximum.z - surfaceRange.MinimumHeight ) );
		var cave = BoundCaveDensity( worldAabb, surface, unchecked((uint)settings.WorldSeed) );
		var density = new DensityInterval(
			MathF.BitDecrement( MathF.Max( surface.Minimum, cave.Minimum ) ),
			MathF.BitIncrement( MathF.Max( surface.Maximum, cave.Maximum ) ) );

		if ( density.Maximum <= 0f )
		{
			return new DensityBound(
				density.Minimum, density.Maximum, ChunkDensityClassification.DefinitelySolid );
		}
		if ( density.Minimum > 0f )
		{
			return new DensityBound(
				density.Minimum, density.Maximum, ChunkDensityClassification.DefinitelyAir );
		}
		if ( surfaceRange.Classification == ChunkDensityClassification.DefinitelyAir )
		{
			return new DensityBound(
				density.Minimum, density.Maximum, ChunkDensityClassification.DefinitelyAir );
		}
		if ( surfaceRange.Classification == ChunkDensityClassification.PotentiallySurfaceContaining )
		{
			return new DensityBound(
				density.Minimum,
				density.Maximum,
				ChunkDensityClassification.PotentiallySurfaceContaining );
		}
		return new DensityBound(
			density.Minimum,
			density.Maximum,
			ChunkDensityClassification.PotentiallySurfaceContaining );
	}

	private static DensityInterval BoundCaveDensity(
		SdfWorldAabb worldAabb,
		DensityInterval surface,
		uint seed )
	{
		var noodleA = BoundSimplex3D( worldAabb, 1f / NoodleAWavelength, seed ^ NoodleASeedSalt );
		var noodleB = BoundSimplex3D( worldAabb, 1f / NoodleBWavelength, seed ^ NoodleBSeedSalt );
		var thickness = BoundSimplex3D(
			worldAabb,
			1f / ThicknessWavelength,
			seed ^ ThicknessSeedSalt );
		var cheese = BoundSimplex3D( worldAabb, 1f / CheeseWavelength, seed ^ CheeseSeedSalt );
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
		var minimumDepth = -surface.Maximum;
		var maximumDepth = -surface.Minimum;
		var envelopeAtMinimum = MathF.Min( minimumDepth, CaveMaximumDepth - minimumDepth );
		var envelopeAtMaximum = MathF.Min( maximumDepth, CaveMaximumDepth - maximumDepth );
		var envelopeMaximum = minimumDepth <= CaveMaximumDepth * 0.5f &&
			maximumDepth >= CaveMaximumDepth * 0.5f
			? CaveMaximumDepth * 0.5f
			: MathF.Max( envelopeAtMinimum, envelopeAtMaximum );
		var envelope = new DensityInterval(
			MathF.Min( envelopeAtMinimum, envelopeAtMaximum ),
			envelopeMaximum );
		return new DensityInterval(
			MathF.Min( union.Minimum, envelope.Minimum ),
			MathF.Min( union.Maximum, envelope.Maximum ) );
	}

	private static DensityInterval BoundSimplex3D(
		SdfWorldAabb worldAabb,
		float frequency,
		uint seed )
	{
		var center = worldAabb.Minimum + (worldAabb.Maximum - worldAabb.Minimum) * 0.5f;
		var halfExtent = (worldAabb.Maximum - worldAabb.Minimum) * 0.5f;
		var centerNoise = SimplexNoise3D( center * frequency, seed );
		var radius = Math.Sqrt(
			(double)halfExtent.x * halfExtent.x +
			(double)halfExtent.y * halfExtent.y +
			(double)halfExtent.z * halfExtent.z ) * Math.Abs( frequency );
		var variation = Simplex3DLipschitzBound * radius + SimplexFinitePrecisionPadding;
		return new DensityInterval(
			(float)Math.Clamp( centerNoise - variation, -1d, 1d ),
			(float)Math.Clamp( centerNoise + variation, -1d, 1d ) );
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

	private static SurfaceRange BoundSurfaceRectangle(
		float minimumX,
		float maximumX,
		float minimumY,
		float maximumY,
		float minimumZ,
		float maximumZ,
		float minimumSubdivisionSize,
		ProceduralTerrainSettings settings )
	{
		var centerX = minimumX + (maximumX - minimumX) * 0.5f;
		var centerY = minimumY + (maximumY - minimumY) * 0.5f;
		var centerNoise = SimplexNoise2D(
			centerX * settings.SurfaceFrequency,
			centerY * settings.SurfaceFrequency,
			unchecked((uint)settings.WorldSeed) );
		var centerHeight = settings.SurfaceBaseHeight + centerNoise * settings.SurfaceAmplitude;

		var halfX = (double)(maximumX - minimumX) * 0.5d;
		var halfY = (double)(maximumY - minimumY) * 0.5d;
		var noiseRadius = Math.Sqrt( halfX * halfX + halfY * halfY ) *
			Math.Abs( settings.SurfaceFrequency );
		var noiseVariation = SimplexLipschitzBound * noiseRadius +
			SimplexFinitePrecisionPadding;
		var minimumNoise = Math.Clamp( centerNoise - noiseVariation, -1d, 1d );
		var maximumNoise = Math.Clamp( centerNoise + noiseVariation, -1d, 1d );
		var height0 = settings.SurfaceBaseHeight + minimumNoise * settings.SurfaceAmplitude;
		var height1 = settings.SurfaceBaseHeight + maximumNoise * settings.SurfaceAmplitude;
		var minimumHeight = MathF.BitDecrement( (float)Math.Min( height0, height1 ) );
		var maximumHeight = MathF.BitIncrement( (float)Math.Max( height0, height1 ) );

		if ( centerHeight >= minimumZ && centerHeight <= maximumZ )
		{
			return new SurfaceRange(
				minimumHeight,
				maximumHeight,
				ChunkDensityClassification.PotentiallySurfaceContaining );
		}
		if ( maximumHeight < minimumZ )
		{
			return new SurfaceRange(
				minimumHeight,
				maximumHeight,
				ChunkDensityClassification.DefinitelyAir );
		}
		if ( minimumHeight >= maximumZ )
		{
			return new SurfaceRange(
				minimumHeight,
				maximumHeight,
				ChunkDensityClassification.DefinitelySolid );
		}

		var extentX = maximumX - minimumX;
		var extentY = maximumY - minimumY;
		if ( MathF.Max( extentX, extentY ) <= minimumSubdivisionSize )
		{
			return BoundSurfaceLeaf(
				minimumX,
				maximumX,
				minimumY,
				maximumY,
				minimumZ,
				maximumZ,
				settings );
		}

		SurfaceRange first;
		SurfaceRange second;
		if ( extentX >= extentY )
		{
			first = BoundSurfaceRectangle(
				minimumX, centerX, minimumY, maximumY,
				minimumZ, maximumZ, minimumSubdivisionSize, settings );
			second = BoundSurfaceRectangle(
				centerX, maximumX, minimumY, maximumY,
				minimumZ, maximumZ, minimumSubdivisionSize, settings );
		}
		else
		{
			first = BoundSurfaceRectangle(
				minimumX, maximumX, minimumY, centerY,
				minimumZ, maximumZ, minimumSubdivisionSize, settings );
			second = BoundSurfaceRectangle(
				minimumX, maximumX, centerY, maximumY,
				minimumZ, maximumZ, minimumSubdivisionSize, settings );
		}

		var classification = first.Classification == second.Classification
			? first.Classification
			: ChunkDensityClassification.PotentiallySurfaceContaining;
		return new SurfaceRange(
			MathF.BitDecrement( MathF.Min( first.MinimumHeight, second.MinimumHeight ) ),
			MathF.BitIncrement( MathF.Max( first.MaximumHeight, second.MaximumHeight ) ),
			classification );
	}

	private static SurfaceRange BoundSurfaceLeaf(
		float minimumX,
		float maximumX,
		float minimumY,
		float maximumY,
		float minimumZ,
		float maximumZ,
		ProceduralTerrainSettings settings )
	{
		var extentX = maximumX - minimumX;
		var extentY = maximumY - minimumY;
		var noiseRadius = Math.Sqrt(
			(double)extentX * extentX / 16d + (double)extentY * extentY / 16d ) *
			Math.Abs( settings.SurfaceFrequency );
		var noiseVariation = SimplexLipschitzBound * noiseRadius +
			SimplexFinitePrecisionPadding;
		var minimumNoise = 1d;
		var maximumNoise = -1d;
		var containsSurfaceSample = false;
		for ( var yIndex = 0; yIndex < 2; yIndex++ )
		{
			var y = minimumY + extentY * (yIndex == 0 ? 0.25f : 0.75f);
			for ( var xIndex = 0; xIndex < 2; xIndex++ )
			{
				var x = minimumX + extentX * (xIndex == 0 ? 0.25f : 0.75f);
				var noise = SimplexNoise2D(
					x * settings.SurfaceFrequency,
					y * settings.SurfaceFrequency,
					unchecked((uint)settings.WorldSeed) );
				minimumNoise = Math.Min( minimumNoise, noise - noiseVariation );
				maximumNoise = Math.Max( maximumNoise, noise + noiseVariation );
				var height = settings.SurfaceBaseHeight + noise * settings.SurfaceAmplitude;
				containsSurfaceSample |= height >= minimumZ && height <= maximumZ;
			}
		}

		minimumNoise = Math.Clamp( minimumNoise, -1d, 1d );
		maximumNoise = Math.Clamp( maximumNoise, -1d, 1d );
		var height0 = settings.SurfaceBaseHeight + minimumNoise * settings.SurfaceAmplitude;
		var height1 = settings.SurfaceBaseHeight + maximumNoise * settings.SurfaceAmplitude;
		var minimumHeight = MathF.BitDecrement( (float)Math.Min( height0, height1 ) );
		var maximumHeight = MathF.BitIncrement( (float)Math.Max( height0, height1 ) );
		var classification = containsSurfaceSample
			? ChunkDensityClassification.PotentiallySurfaceContaining
			: maximumHeight < minimumZ
				? ChunkDensityClassification.DefinitelyAir
				: minimumHeight >= maximumZ
					? ChunkDensityClassification.DefinitelySolid
					: ChunkDensityClassification.PotentiallySurfaceContaining;
		return new SurfaceRange( minimumHeight, maximumHeight, classification );
	}

	private readonly record struct SurfaceRange(
		float MinimumHeight,
		float MaximumHeight,
		ChunkDensityClassification Classification );

	private readonly record struct DensityInterval( float Minimum, float Maximum );

	private readonly record struct DensityBound(
		float Minimum,
		float Maximum,
		ChunkDensityClassification Classification );

	private static float SimplexNoise3D( Vector3 position, uint seed )
	{
		var skew = (position.x + position.y + position.z) * SimplexF3;
		var i = (int)MathF.Floor( position.x + skew );
		var j = (int)MathF.Floor( position.y + skew );
		var k = (int)MathF.Floor( position.z + skew );
		var unskew = (i + j + k) * SimplexG3;
		var x0 = position.x - (i - unskew);
		var y0 = position.y - (j - unskew);
		var z0 = position.z - (k - unskew);

		int i1;
		int j1;
		int k1;
		int i2;
		int j2;
		int k2;
		if ( x0 >= y0 )
		{
			if ( y0 >= z0 )
			{
				i1 = 1; j1 = 0; k1 = 0;
				i2 = 1; j2 = 1; k2 = 0;
			}
			else if ( x0 >= z0 )
			{
				i1 = 1; j1 = 0; k1 = 0;
				i2 = 1; j2 = 0; k2 = 1;
			}
			else
			{
				i1 = 0; j1 = 0; k1 = 1;
				i2 = 1; j2 = 0; k2 = 1;
			}
		}
		else
		{
			if ( y0 < z0 )
			{
				i1 = 0; j1 = 0; k1 = 1;
				i2 = 0; j2 = 1; k2 = 1;
			}
			else if ( x0 < z0 )
			{
				i1 = 0; j1 = 1; k1 = 0;
				i2 = 0; j2 = 1; k2 = 1;
			}
			else
			{
				i1 = 0; j1 = 1; k1 = 0;
				i2 = 1; j2 = 1; k2 = 0;
			}
		}

		var x1 = x0 - i1 + SimplexG3;
		var y1 = y0 - j1 + SimplexG3;
		var z1 = z0 - k1 + SimplexG3;
		var x2 = x0 - i2 + 2f * SimplexG3;
		var y2 = y0 - j2 + 2f * SimplexG3;
		var z2 = z0 - k2 + 2f * SimplexG3;
		var x3 = x0 - 1f + 3f * SimplexG3;
		var y3 = y0 - 1f + 3f * SimplexG3;
		var z3 = z0 - 1f + 3f * SimplexG3;
		var value =
			SimplexContribution3D( i, j, k, x0, y0, z0, seed ) +
			SimplexContribution3D( i + i1, j + j1, k + k1, x1, y1, z1, seed ) +
			SimplexContribution3D( i + i2, j + j2, k + k2, x2, y2, z2, seed ) +
			SimplexContribution3D( i + 1, j + 1, k + 1, x3, y3, z3, seed );
		return Math.Clamp( value * 32f, -1f, 1f );
	}

	private static float SimplexContribution3D(
		int x,
		int y,
		int z,
		float offsetX,
		float offsetY,
		float offsetZ,
		uint seed )
	{
		var attenuation = 0.6f - offsetX * offsetX - offsetY * offsetY - offsetZ * offsetZ;
		if ( attenuation <= 0f )
		{
			return 0f;
		}

		var gradient = Gradient3D( Hash( x, y, z, seed ) );
		attenuation *= attenuation;
		return attenuation * attenuation *
			(gradient.x * offsetX + gradient.y * offsetY + gradient.z * offsetZ);
	}

	private static Vector3 Gradient3D( uint hash )
	{
		const float diagonal = 0.70710677f;
		return (hash % 12u) switch
		{
			0u => new Vector3( diagonal, diagonal, 0f ),
			1u => new Vector3( -diagonal, diagonal, 0f ),
			2u => new Vector3( diagonal, -diagonal, 0f ),
			3u => new Vector3( -diagonal, -diagonal, 0f ),
			4u => new Vector3( diagonal, 0f, diagonal ),
			5u => new Vector3( -diagonal, 0f, diagonal ),
			6u => new Vector3( diagonal, 0f, -diagonal ),
			7u => new Vector3( -diagonal, 0f, -diagonal ),
			8u => new Vector3( 0f, diagonal, diagonal ),
			9u => new Vector3( 0f, -diagonal, diagonal ),
			10u => new Vector3( 0f, diagonal, -diagonal ),
			_ => new Vector3( 0f, -diagonal, -diagonal )
		};
	}

	private static float SimplexNoise2D( float x, float y, uint seed )
	{
		var skew = (x + y) * SimplexF2;
		var i = (int)MathF.Floor( x + skew );
		var j = (int)MathF.Floor( y + skew );
		var unskew = (i + j) * SimplexG2;
		var x0 = x - (i - unskew);
		var y0 = y - (j - unskew);
		var i1 = x0 > y0 ? 1 : 0;
		var j1 = x0 > y0 ? 0 : 1;
		var x1 = x0 - i1 + SimplexG2;
		var y1 = y0 - j1 + SimplexG2;
		var x2 = x0 - 1f + 2f * SimplexG2;
		var y2 = y0 - 1f + 2f * SimplexG2;
		var value =
			SimplexContribution2D( i, j, x0, y0, seed ) +
			SimplexContribution2D( i + i1, j + j1, x1, y1, seed ) +
			SimplexContribution2D( i + 1, j + 1, x2, y2, seed );
		return Math.Clamp( value * 70f, -1f, 1f );
	}

	private static float SimplexContribution2D(
		int x,
		int y,
		float offsetX,
		float offsetY,
		uint seed )
	{
		var attenuation = 0.5f - offsetX * offsetX - offsetY * offsetY;
		if ( attenuation <= 0f )
		{
			return 0f;
		}

		var gradient = Gradient2D( Hash( x, y, seed ) );
		attenuation *= attenuation;
		return attenuation * attenuation * (gradient.x * offsetX + gradient.y * offsetY);
	}

	private static Vector2 Gradient2D( uint hash )
	{
		return (hash & 7u) switch
		{
			0u => new Vector2( 1f, 0f ),
			1u => new Vector2( -1f, 0f ),
			2u => new Vector2( 0f, 1f ),
			3u => new Vector2( 0f, -1f ),
			4u => new Vector2( 0.70710677f, 0.70710677f ),
			5u => new Vector2( -0.70710677f, 0.70710677f ),
			6u => new Vector2( 0.70710677f, -0.70710677f ),
			_ => new Vector2( -0.70710677f, -0.70710677f )
		};
	}

	private static uint Hash( int x, int y, uint seed )
	{
		unchecked
		{
			var hash = seed;
			hash ^= (uint)x * 0x9E3779B1u;
			hash = RotateLeft( hash, 13 ) * 0x85EBCA77u;
			hash ^= (uint)y * 0xC2B2AE3Du;
			hash = RotateLeft( hash, 15 ) * 0x27D4EB2Fu;
			hash ^= hash >> 16;
			hash *= 0x7FEB352Du;
			hash ^= hash >> 15;
			hash *= 0x846CA68Bu;
			hash ^= hash >> 16;
			return hash;
		}
	}

	private static uint Hash( int x, int y, int z, uint seed )
	{
		unchecked
		{
			var hash = seed;
			hash ^= (uint)x * 0x9E3779B1u;
			hash = RotateLeft( hash, 13 ) * 0x85EBCA77u;
			hash ^= (uint)y * 0xC2B2AE3Du;
			hash = RotateLeft( hash, 15 ) * 0x27D4EB2Fu;
			hash ^= (uint)z * 0x165667B1u;
			hash = RotateLeft( hash, 17 ) * 0xD3A2646Cu;
			hash ^= hash >> 16;
			hash *= 0x7FEB352Du;
			hash ^= hash >> 15;
			hash *= 0x846CA68Bu;
			hash ^= hash >> 16;
			return hash;
		}
	}

	private static uint RotateLeft( uint value, int count )
	{
		return value << count | value >> (32 - count);
	}
}
