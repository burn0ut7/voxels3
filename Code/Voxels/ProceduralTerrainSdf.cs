using System;
using static TerrainCaves;

public readonly record struct SdfWorldAabb( Vector3 Minimum, Vector3 Maximum );

/// <summary>
/// Canonical deterministic version-13 volumetric terrain field. The GPU mirror
/// uses the same integer hash, simplex recipes, and constructive composition.
/// </summary>
internal static class ProceduralTerrainSdf
{
	// Saved worlds identify this backend revision; it is not a variation control.
	public const int CurrentVersion = 13;
	public const int DefaultWorldSeed = 1337;
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
		return TerrainCaves.SampleWorld( worldPosition, settings, SampleSurfaceHeight( worldPosition, settings ) );
	}

	private static float SampleSurfaceHeight( Vector3 position, ProceduralTerrainSettings settings ) =>
		RegionalLandforms.SampleWorld( position, settings ).Height;

	// Build-local derived workspace. The generator owns its XY-only dependency;
	// consumers can only obtain full volumetric density values.
	internal sealed class LatticeSampler
	{
		private readonly int _stride;
		private readonly float[] _heights;
		private readonly bool[] _sampled;
		private Vector3Int _origin;
		private float _cellSize;
		private ProceduralTerrainSettings _settings;

		public LatticeSampler( int samplesPerAxis )
		{
			_stride = samplesPerAxis;
			_heights = new float[samplesPerAxis * samplesPerAxis];
			_sampled = new bool[_heights.Length];
		}

		public void Begin( Vector3Int origin, float cellSize, ProceduralTerrainSettings settings )
		{
			_origin = origin; _cellSize = cellSize; _settings = settings;
			Array.Clear( _sampled );
		}

		public float Sample( int x, int y, int z )
		{
			var coordinate = _origin + new Vector3Int( x, y, z );
			var position = new Vector3( coordinate.x * _cellSize, coordinate.y * _cellSize, coordinate.z * _cellSize );
			var column = x + _stride * y;
			if ( !_sampled[column] )
			{
				_heights[column] = SampleSurfaceHeight( position, _settings );
				_sampled[column] = true;
			}
			return TerrainCaves.SampleWorld( position, _settings, _heights[column] );
		}
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
			!settings.IsValid ||
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
		if ( TryBoundOutsideVerticalSupport( worldAabb, settings, out var verticalBound ) )
			return verticalBound;

		var surfaceRange = RegionalLandforms.BoundHeight( worldAabb, settings );
		var surface = new DensityInterval(
			MathF.BitDecrement( minimum.z - surfaceRange.Maximum ),
			MathF.BitIncrement( maximum.z - surfaceRange.Minimum ) );
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
		return new DensityBound( density.Minimum, density.Maximum,
			ChunkDensityClassification.PotentiallySurfaceContaining );
	}

	private static bool TryBoundOutsideVerticalSupport(
		SdfWorldAabb worldAabb,
		ProceduralTerrainSettings settings,
		out DensityBound bound )
	{
		var minimum = worldAabb.Minimum;
		var maximum = worldAabb.Maximum;
		var minimumSurfaceHeight = -0.7001f * settings.ReliefHeight;
		var maximumSurfaceHeight = 1.0401f * settings.ReliefHeight;
		var minimumPotentialSurfaceHeight = minimumSurfaceHeight - CaveMaximumDepth;
		if ( float.IsFinite( minimumSurfaceHeight ) &&
			float.IsFinite( maximumSurfaceHeight ) &&
			float.IsFinite( minimumPotentialSurfaceHeight ) )
		{
			if ( minimum.z > maximumSurfaceHeight )
			{
				var minimumSurfaceDensity = minimum.z - maximumSurfaceHeight;
				var maximumSurfaceDensity = maximum.z - minimumSurfaceHeight;
				bound = new DensityBound(
					minimumSurfaceDensity,
					MathF.Max( maximumSurfaceDensity, MaximumRawCaveDensity ),
					ChunkDensityClassification.DefinitelyAir );
				return true;
			}

			if ( maximum.z < minimumPotentialSurfaceHeight )
			{
				var minimumSurfaceDensity = minimum.z - maximumSurfaceHeight;
				var maximumSurfaceDensity = maximum.z - minimumSurfaceHeight;
				var minimumDepth = minimumSurfaceHeight - maximum.z;
				var maximumEnvelope = CaveMaximumDepth - minimumDepth;
				bound = new DensityBound(
					minimumSurfaceDensity,
					MathF.Max( maximumSurfaceDensity, maximumEnvelope ),
					ChunkDensityClassification.DefinitelySolid );
				return true;
			}
		}
		bound = default;
		return false;
	}

	private readonly record struct DensityBound(
		float Minimum,
		float Maximum,
		ChunkDensityClassification Classification );

}
