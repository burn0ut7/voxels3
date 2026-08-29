using System;

/// <summary>
/// Canonical deterministic version-1 procedural terrain field. The GPU mirror
/// lives in voxel_sdf.hlsl; both use the same integer hash, constants, and
/// absolute-coordinate recipe.
/// </summary>
internal static class ProceduralTerrainSdf
{
	public const int CurrentVersion = 1;
	public const int DefaultWorldSeed = 1337;

	private const float HillAmplitude0 = 320f;
	private const float HillWavelength0 = 4096f;
	private const float HillAmplitude1 = 96f;
	private const float HillWavelength1 = 2048f;
	private const float HillAmplitudeBound = HillAmplitude0 + HillAmplitude1;
	private const float CaveWeight0 = 0.67f;
	private const float CaveWavelength0 = 1024f;
	private const float CaveWeight1 = 0.33f;
	private const float CaveWavelength1 = 512f;
	private const float CaveThreshold = 0.18f;
	private const float CaveScale = 192f;
	private const float CaveCenterZ = -128f;
	private const float CaveHalfExtent = 512f;
	private const uint HillSalt0 = 0xA511E9B3u;
	private const uint HillSalt1 = 0x63D83595u;
	private const uint CaveSalt0 = 0xB5297A4Du;
	private const uint CaveSalt1 = 0x1B56C4E9u;

	public static float SampleGlobal(
		Vector3Int globalSampleCoordinate,
		float cellSize,
		int worldSeed,
		int generatorVersion )
	{
		var worldPosition = new Vector3(
			globalSampleCoordinate.x * cellSize,
			globalSampleCoordinate.y * cellSize,
			globalSampleCoordinate.z * cellSize );
		return SampleWorld( worldPosition, worldSeed, generatorVersion );
	}

	public static float SampleWorld( Vector3 worldPosition, int worldSeed, int generatorVersion )
	{
		var versionedSeed = unchecked((uint)worldSeed ^ (uint)generatorVersion * 0x9E3779B9u);
		var hillHeight =
			HillAmplitude0 * ValueNoise2D( worldPosition.x, worldPosition.y, HillWavelength0, versionedSeed, HillSalt0 ) +
			HillAmplitude1 * ValueNoise2D( worldPosition.x, worldPosition.y, HillWavelength1, versionedSeed, HillSalt1 );
		var terrainDensity = worldPosition.z - hillHeight;

		var caveNoise =
			CaveWeight0 * ValueNoise3D( worldPosition, CaveWavelength0, versionedSeed, CaveSalt0 ) +
			CaveWeight1 * ValueNoise3D( worldPosition, CaveWavelength1, versionedSeed, CaveSalt1 );
		var caveNoiseBoundary = (MathF.Abs( caveNoise ) - CaveThreshold) * CaveScale;
		var caveVerticalEnvelope = MathF.Abs( worldPosition.z - CaveCenterZ ) - CaveHalfExtent;
		var caveBoundary = MathF.Max( caveNoiseBoundary, caveVerticalEnvelope );
		return MathF.Max( terrainDensity, -caveBoundary );
	}

	public static ChunkDensityRange ClassifyDensityRange(
		Vector3Int coordinate,
		int cellsPerAxis,
		float cellSize )
	{
		var chunkWorldSize = cellsPerAxis * cellSize;
		var chunkMinimumZ = coordinate.z * chunkWorldSize;
		var chunkMaximumZ = chunkMinimumZ + chunkWorldSize;
		var terrainMinimum = chunkMinimumZ - HillAmplitudeBound;
		var terrainMaximum = chunkMaximumZ + HillAmplitudeBound;

		var shiftedMinimumZ = chunkMinimumZ - CaveCenterZ;
		var shiftedMaximumZ = chunkMaximumZ - CaveCenterZ;
		var minimumAbsoluteZ = shiftedMinimumZ <= 0f && shiftedMaximumZ >= 0f
			? 0f
			: MathF.Min( MathF.Abs( shiftedMinimumZ ), MathF.Abs( shiftedMaximumZ ) );
		var maximumAbsoluteZ = MathF.Max( MathF.Abs( shiftedMinimumZ ), MathF.Abs( shiftedMaximumZ ) );
		var verticalEnvelopeMinimum = minimumAbsoluteZ - CaveHalfExtent;
		var verticalEnvelopeMaximum = maximumAbsoluteZ - CaveHalfExtent;
		var caveNoiseBoundaryMinimum = -CaveThreshold * CaveScale;
		var caveNoiseBoundaryMaximum = (1f - CaveThreshold) * CaveScale;
		var caveBoundaryMinimum = MathF.Max( caveNoiseBoundaryMinimum, verticalEnvelopeMinimum );
		var caveBoundaryMaximum = MathF.Max( caveNoiseBoundaryMaximum, verticalEnvelopeMaximum );
		var carveMinimum = -caveBoundaryMaximum;
		var carveMaximum = -caveBoundaryMinimum;
		var minimumDensity = MathF.Max( terrainMinimum, carveMinimum );
		var maximumDensity = MathF.Max( terrainMaximum, carveMaximum );
		var classification = maximumDensity <= 0f
			? ChunkDensityClassification.DefinitelySolid
			: minimumDensity > 0f
				? ChunkDensityClassification.DefinitelyAir
				: ChunkDensityClassification.PotentiallySurfaceContaining;
		return new ChunkDensityRange( minimumDensity, maximumDensity, classification );
	}

	private static float ValueNoise2D( float worldX, float worldY, float wavelength, uint seed, uint salt )
	{
		var sampleX = worldX / wavelength;
		var sampleY = worldY / wavelength;
		var minimumX = (int)MathF.Floor( sampleX );
		var minimumY = (int)MathF.Floor( sampleY );
		var blendX = Smooth( sampleX - minimumX );
		var blendY = Smooth( sampleY - minimumY );
		var lower = Lerp(
			HashValue( minimumX, minimumY, 0, seed, salt ),
			HashValue( minimumX + 1, minimumY, 0, seed, salt ),
			blendX );
		var upper = Lerp(
			HashValue( minimumX, minimumY + 1, 0, seed, salt ),
			HashValue( minimumX + 1, minimumY + 1, 0, seed, salt ),
			blendX );
		return Lerp( lower, upper, blendY );
	}

	private static float ValueNoise3D( Vector3 worldPosition, float wavelength, uint seed, uint salt )
	{
		var sample = worldPosition / wavelength;
		var minimumX = (int)MathF.Floor( sample.x );
		var minimumY = (int)MathF.Floor( sample.y );
		var minimumZ = (int)MathF.Floor( sample.z );
		var blendX = Smooth( sample.x - minimumX );
		var blendY = Smooth( sample.y - minimumY );
		var blendZ = Smooth( sample.z - minimumZ );
		var z0y0 = Lerp(
			HashValue( minimumX, minimumY, minimumZ, seed, salt ),
			HashValue( minimumX + 1, minimumY, minimumZ, seed, salt ),
			blendX );
		var z0y1 = Lerp(
			HashValue( minimumX, minimumY + 1, minimumZ, seed, salt ),
			HashValue( minimumX + 1, minimumY + 1, minimumZ, seed, salt ),
			blendX );
		var z1y0 = Lerp(
			HashValue( minimumX, minimumY, minimumZ + 1, seed, salt ),
			HashValue( minimumX + 1, minimumY, minimumZ + 1, seed, salt ),
			blendX );
		var z1y1 = Lerp(
			HashValue( minimumX, minimumY + 1, minimumZ + 1, seed, salt ),
			HashValue( minimumX + 1, minimumY + 1, minimumZ + 1, seed, salt ),
			blendX );
		return Lerp(
			Lerp( z0y0, z0y1, blendY ),
			Lerp( z1y0, z1y1, blendY ),
			blendZ );
	}

	private static float HashValue( int x, int y, int z, uint seed, uint salt )
	{
		unchecked
		{
			var hash = seed ^ salt;
			hash ^= (uint)x * 0x9E3779B1u;
			hash = RotateLeft( hash, 13 ) * 0x85EBCA77u;
			hash ^= (uint)y * 0xC2B2AE3Du;
			hash = RotateLeft( hash, 15 ) * 0x27D4EB2Fu;
			hash ^= (uint)z * 0x165667B1u;
			hash ^= hash >> 16;
			hash *= 0x7FEB352Du;
			hash ^= hash >> 15;
			hash *= 0x846CA68Bu;
			hash ^= hash >> 16;
			return (hash & 0x00FFFFFFu) * (2f / 16777215f) - 1f;
		}
	}

	private static uint RotateLeft( uint value, int count )
	{
		return value << count | value >> (32 - count);
	}

	private static float Smooth( float value )
	{
		return value * value * (3f - 2f * value);
	}

	private static float Lerp( float from, float to, float amount )
	{
		return from + (to - from) * amount;
	}
}
