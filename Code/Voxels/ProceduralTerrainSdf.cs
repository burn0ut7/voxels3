using System;

public readonly record struct ProceduralTerrainSettings(
	int WorldSeed,
	float SurfaceBaseHeight,
	float SurfaceFrequency,
	float SurfaceAmplitude );

public readonly record struct SdfWorldAabb( Vector3 Minimum, Vector3 Maximum );

/// <summary>
/// Canonical deterministic version-2 surface terrain field. The GPU mirror uses
/// the same integer hash and single-octave 2D simplex recipe.
/// </summary>
internal static class ProceduralTerrainSdf
{
	// Saved worlds identify this backend revision; it is not a variation control.
	public const int CurrentVersion = 2;
	public const int DefaultWorldSeed = 1337;
	public const float DefaultSurfaceBaseHeight = 0f;
	public const float DefaultSurfaceFrequency = 0.0005f;
	public const float DefaultSurfaceAmplitude = 128f;

	private const float SimplexF2 = 0.3660254037844386f;
	private const float SimplexG2 = 0.21132486540518713f;
	// Each of the three simplex contributions has gradient magnitude at most
	// 27/343 before the canonical scale of 70. The exact global bound is therefore
	// 3 * 70 * 27/343 = 16.530612... . Seventeen leaves deterministic margin.
	private const double SimplexLipschitzBound = 17d;
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
		return worldPosition.z - surfaceHeight;
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

		var surfaceRange = BoundSurfaceRectangle(
			minimum.x,
			maximum.x,
			minimum.y,
			maximum.y,
			minimum.z,
			maximum.z,
			minimumSubdivisionSize,
			settings );
		var minimumDensity = MathF.BitDecrement( minimum.z - surfaceRange.MaximumHeight );
		var maximumDensity = MathF.BitIncrement( maximum.z - surfaceRange.MinimumHeight );
		var classification = surfaceRange.Classification;
		return new ChunkDensityRange( minimumDensity, maximumDensity, classification );
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

	private static uint RotateLeft( uint value, int count )
	{
		return value << count | value >> (32 - count);
	}
}
