using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>Immutable generated water surface samples; medium queries remain owned by SurfaceWater.Resolve.</summary>
internal sealed class GeneratedWaterCells
{
	public const int CellsPerAxis = VoxelManager.RequiredCellsPerAxis;
	public const int SamplesPerAxis = CellsPerAxis + 1;
	public SdfWorldAabb Bounds { get; }
	public float CellSize { get; }
	public float SeaLevel { get; }
	private readonly float[] _bottom;
	// Retain the free-surface crossing even when river depth is below the LOD spacing.
	public float[] SurfaceClearance { get; }
	public readonly record struct SurfaceCell( Vector3 Origin, float Size, Vector4 Bottom, Vector4 Clearance );
	public Dictionary<int, SurfaceCell[]> RefinedCells { get; } = new();
	public int RefinedCellCount { get; private set; }
	public long Bytes => (long)(_bottom.Length + SurfaceClearance.Length) * sizeof( float ) +
		(long)RefinedCellCount * (3 + 1 + 4 + 4) * sizeof( float );

	private GeneratedWaterCells( SdfWorldAabb bounds, float cellSize, float seaLevel )
	{
		Bounds = bounds;
		CellSize = cellSize;
		SeaLevel = seaLevel;
		_bottom = new float[SamplesPerAxis * SamplesPerAxis];
		SurfaceClearance = new float[SamplesPerAxis * SamplesPerAxis];
	}

	public static GeneratedWaterCells Generate( SdfWorldAabb bounds, float cellSize,
		TerrainFieldSnapshot field, CancellationToken cancellation )
	{
		var settings = field.Settings;
		var rivers = RiverWorld.For( settings ).Capture( bounds, cancellation );
		var edited = field.GetCorrectionRange( bounds, out _, out _ ) != 0;
		var result = new GeneratedWaterCells( bounds, cellSize, settings.SeaLevel );
		for ( var y = 0; y < SamplesPerAxis; y++ )
		{
			cancellation.ThrowIfCancellationRequested();
			for ( var x = 0; x < SamplesPerAxis; x++ )
			{
				var position = bounds.Minimum + new Vector3( x * cellSize, y * cellSize, 0f );
				var natural = RegionalLandforms.SampleNatural( position, settings );
				var bottom = rivers.SampleWorld( position, natural.Height ).Height;
				var column = x + SamplesPerAxis * y;
				result._bottom[column] = bottom;
				var clearance = settings.SeaLevel - bottom;
				if ( edited )
				{
					position.z = settings.SeaLevel;
					var solid = TerrainCliffs.Sample( position, settings, natural.Mountains,
						TerrainCaves.SampleWorld( position, settings, bottom ) ) + field.SampleCorrection( position );
					clearance = MathF.Min( clearance, solid );
				}
				result.SurfaceClearance[column] = clearance;

			}
		}
		if ( cellSize > TerrainField.SampleSpacing )
		{
			for ( var y = 0; y < CellsPerAxis; y++ )
			for ( var x = 0; x < CellsPerAxis; x++ )
			{
				var first = x + SamplesPerAxis * y;
				var bottom = new Vector4( result._bottom[first], result._bottom[first + 1], result._bottom[first + SamplesPerAxis + 1], result._bottom[first + SamplesPerAxis] );
				var clearance = new Vector4( result.SurfaceClearance[first], result.SurfaceClearance[first + 1], result.SurfaceClearance[first + SamplesPerAxis + 1], result.SurfaceClearance[first + SamplesPerAxis] );
				var origin = bounds.Minimum + new Vector3( x * cellSize, y * cellSize, 0f );
				origin.z = settings.SeaLevel;
				var cell = new SurfaceCell( origin, cellSize, bottom, clearance );
				if ( !NeedsRefinement( cell, rivers ) ) continue;
				var leaves = new List<SurfaceCell>();
				Refine( cell, leaves, rivers, field, edited, cancellation );
				result.RefinedCells.Add( x + CellsPerAxis * y, leaves.ToArray() );
				result.RefinedCellCount += leaves.Count;
			}
		}
		return result;
	}

	private static bool NeedsRefinement( SurfaceCell cell, RiverWorld.Region rivers )
	{
		if ( cell.Size <= TerrainField.SampleSpacing ) return false;
		var wet = cell.Clearance;
		if ( wet.x > 0f && wet.y > 0f && wet.z > 0f && wet.w > 0f ) return false;
		var radius = rivers.MinimumWetRadius( cell.Origin, cell.Origin + new Vector3( cell.Size, cell.Size, 0f ) );
		// Two or more cells across each intersecting reach's full width, without
		// changing the base lattice or oversampling unrelated ocean coastlines.
		return cell.Size > MathF.Max( TerrainField.SampleSpacing, radius );
	}

	private static void Refine( SurfaceCell cell, List<SurfaceCell> leaves, RiverWorld.Region rivers,
		TerrainFieldSnapshot field, bool edited, CancellationToken cancellation )
	{
		cancellation.ThrowIfCancellationRequested();
		if ( !NeedsRefinement( cell, rivers ) )
		{
			// Empty subcells have an implicit dry payload in this refinement root.
			if ( cell.Clearance.x > 0f || cell.Clearance.y > 0f || cell.Clearance.z > 0f || cell.Clearance.w > 0f ) leaves.Add( cell );
			return;
		}
		var half = cell.Size * 0.5f;
		Span<Vector2> samples = stackalloc Vector2[9];
		samples[0] = new Vector2( cell.Bottom.x, cell.Clearance.x );
		samples[2] = new Vector2( cell.Bottom.y, cell.Clearance.y );
		samples[8] = new Vector2( cell.Bottom.z, cell.Clearance.z );
		samples[6] = new Vector2( cell.Bottom.w, cell.Clearance.w );
		for ( var index = 1; index < 8; index++ )
		{
			if ( index == 2 || index == 6 ) continue;
			var position = cell.Origin + new Vector3( (index % 3) * half, (index / 3) * half, 0f );
			var natural = RegionalLandforms.SampleNatural( position, field.Settings );
			var bottom = rivers.SampleWorld( position, natural.Height ).Height;
			var clearance = field.Settings.SeaLevel - bottom;
			if ( edited ) clearance = MathF.Min( clearance, TerrainCliffs.Sample( position, field.Settings,
				natural.Mountains, TerrainCaves.SampleWorld( position, field.Settings, bottom ) ) + field.SampleCorrection( position ) );
			samples[index] = new Vector2( bottom, clearance );
		}
		for ( var y = 0; y < 2; y++ )
		for ( var x = 0; x < 2; x++ )
		{
			var a = samples[x + 3 * y];
			var b = samples[x + 3 * y + 1];
			var c = samples[x + 3 * (y + 1) + 1];
			var d = samples[x + 3 * (y + 1)];
			Refine( new SurfaceCell( cell.Origin + new Vector3( x * half, y * half, 0f ), half,
				new Vector4( a.x, b.x, c.x, d.x ), new Vector4( a.y, b.y, c.y, d.y ) ),
				leaves, rivers, field, edited, cancellation );
		}
	}
}
