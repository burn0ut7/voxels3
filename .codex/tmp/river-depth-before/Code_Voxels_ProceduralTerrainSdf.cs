using System;
using static TerrainCaves;

public readonly record struct SdfWorldAabb( Vector3 Minimum, Vector3 Maximum );

/// <summary>
/// Canonical deterministic volumetric terrain field. The GPU mirror
/// uses the same integer hash, simplex recipes, and constructive composition.
/// </summary>
internal static class ProceduralTerrainSdf
{
	// Saved worlds identify this backend revision; it is not a variation control.
	public const int CurrentVersion = 41;
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
		var landform = RegionalLandforms.SampleWorld( worldPosition, settings );
		return TerrainCliffs.Sample( worldPosition, settings, landform.Mountains,
			TerrainCaves.SampleWorld( worldPosition, settings, landform.Height ) );
	}

	// Build-local derived workspace. The generator owns its XY-only dependency;
	// consumers can only obtain full volumetric density values.
	internal sealed class LatticeSampler
	{
		private readonly int _stride;
		private readonly Vector2[] _landforms;
		private readonly bool[] _sampled;
		private Vector3Int _origin;
		private float _cellSize;
		private ProceduralTerrainSettings _settings;
		private RiverWorld.Region _rivers;

		public LatticeSampler( int samplesPerAxis )
		{
			_stride = samplesPerAxis;
			_landforms = new Vector2[samplesPerAxis * samplesPerAxis];
			_sampled = new bool[_landforms.Length];
		}

		public void Begin( Vector3Int origin, float cellSize, ProceduralTerrainSettings settings )
		{
			_origin = origin; _cellSize = cellSize; _settings = settings;
			var minimum = new Vector3( origin.x * cellSize, origin.y * cellSize, origin.z * cellSize );
			_rivers = RiverWorld.For( settings ).Capture( new SdfWorldAabb( minimum,
				minimum + new Vector3( (_stride - 1) * cellSize ) ) );
			Array.Clear( _sampled );
		}

		public float Sample( int x, int y, int z )
		{
			var coordinate = _origin + new Vector3Int( x, y, z );
			var position = new Vector3( coordinate.x * _cellSize, coordinate.y * _cellSize, coordinate.z * _cellSize );
			var column = x + _stride * y;
			if ( !_sampled[column] )
			{
				var sample = RegionalLandforms.SampleNatural( position, _settings );
				_landforms[column] = new Vector2( _rivers.SampleWorld( position, sample.Height ).Height, sample.Mountains );
				_sampled[column] = true;
			}
			var landform = _landforms[column];
			return TerrainCliffs.Sample( position, _settings, landform.y,
				TerrainCaves.SampleWorld( position, _settings, landform.x ) );
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
		var maximumDensity = MathF.Max( surface.Maximum, cave.Maximum );
		var cliffMaskMaximum = (surfaceRange.MountainMaximum - 0.75f) * settings.ReliefHeight;
		if ( cliffMaskMaximum > maximumDensity )
		{
			maximumDensity = MathF.Max( maximumDensity,
				MathF.Min( cliffMaskMaximum, TerrainCliffs.BoundMaximum( worldAabb, settings ) ) );
		}
		var density = new DensityInterval(
			MathF.BitDecrement( MathF.Max( surface.Minimum, cave.Minimum ) ),
			MathF.BitIncrement( maximumDensity ) );

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
		var minimumSurfaceHeight = (RegionalLandforms.MinimumHeightFraction - 0.0001f) * settings.ReliefHeight - 16f - RiverNetwork.Depth;
		var maximumSurfaceHeight = (RegionalLandforms.MaximumHeightFraction + 0.0001f) * settings.ReliefHeight + RiverNetwork.BankHeight;
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
