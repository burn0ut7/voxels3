using System;

public readonly record struct ProceduralTerrainSettings(
	int WorldSeed,
	float SurfaceBaseHeight,
	float SurfaceFrequency,
	float SurfaceAmplitude );

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
		var chunkMinimumZ = coordinate.z * chunkWorldSize;
		var chunkMaximumZ = chunkMinimumZ + chunkWorldSize;
		var heightBound = MathF.Abs( settings.SurfaceAmplitude );
		var minimumDensity =
			chunkMinimumZ - settings.SurfaceBaseHeight - heightBound;
		var maximumDensity =
			chunkMaximumZ - settings.SurfaceBaseHeight + heightBound;
		var classification = maximumDensity <= 0f
			? ChunkDensityClassification.DefinitelySolid
			: minimumDensity > 0f
				? ChunkDensityClassification.DefinitelyAir
				: ChunkDensityClassification.PotentiallySurfaceContaining;
		return new ChunkDensityRange( minimumDensity, maximumDensity, classification );
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

	private static uint RotateLeft( uint value, int count )
	{
		return value << count | value >> (32 - count);
	}
}
