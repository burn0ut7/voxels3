using System;
using System.Threading;

/// <summary>Immutable generated water coverage; medium queries remain owned by SurfaceWater.Resolve.</summary>
internal sealed class GeneratedWaterCells
{
	public const int CellsPerAxis = VoxelManager.RequiredCellsPerAxis;
	public const int SamplesPerAxis = CellsPerAxis + 1;
	public SdfWorldAabb Bounds { get; }
	public float CellSize { get; }
	public float SeaLevel { get; }
	// A wet sample covers its entire owner cell, allowing the bank to hide the edge.
	// Expansion stays inside that cell (16 units per axis at LOD0, scaling with LOD).
	public bool[] SurfaceCoverage { get; } = new bool[CellsPerAxis * CellsPerAxis];
	public int RefinementSamples { get; private set; }
	// RGBA8: signed downstream XY (128 = zero), marsh weight, unused.
	// Dry corners retain appearance for interior-only pools. Derived, not fluid state.
	public byte[] Flow { get; private set; }
	public long Bytes => SurfaceCoverage.Length + (Flow?.LongLength ?? 0);
	private bool _mayContainMarsh;

	private GeneratedWaterCells( SdfWorldAabb bounds, float cellSize, float seaLevel )
	{
		Bounds = bounds;
		CellSize = cellSize;
		SeaLevel = seaLevel;
	}

	public static GeneratedWaterCells Generate( SdfWorldAabb bounds, float cellSize,
		TerrainFieldSnapshot field, CancellationToken cancellation )
	{
		var rivers = RiverWorld.For( field.Settings ).Capture( bounds, cancellation );
		var edited = field.GetCorrectionRange( bounds, out _, out _ ) != 0;
		var result = new GeneratedWaterCells( bounds, cellSize, field.Settings.SeaLevel );
		var center = (bounds.Minimum + bounds.Maximum) * 0.5f;
		var climate = TerrainBiomes.SampleWorld( center, field.Settings, 0f );
		// Conservative climate partial bound, including rounding margin. Height
		// bounds below are the existing canonical interval implementation.
		var climateRadius = 0.00005585f * ((bounds.Maximum.x - bounds.Minimum.x + bounds.Maximum.y - bounds.Minimum.y) * 0.5f) + 0.0001f;
		if ( climate.Temperature + climateRadius > 0.30f && climate.Moisture + climateRadius > 0.60f )
		{
			var heights = RegionalLandforms.BoundNaturalHeight( bounds, field.Settings );
			result._mayContainMarsh = heights.Maximum > result.SeaLevel - 128f && heights.Minimum < result.SeaLevel + 192f;
		}
		var origin = bounds.Minimum;
		origin.z = result.SeaLevel;
		Span<bool> samples = stackalloc bool[SamplesPerAxis * SamplesPerAxis];
		Span<byte> appearance = stackalloc byte[SamplesPerAxis * SamplesPerAxis * 4];
		var hasAppearance = false;
		for ( var y = 0; y < SamplesPerAxis; y++ )
		{
			cancellation.ThrowIfCancellationRequested();
			for ( var x = 0; x < SamplesPerAxis; x++ )
			{
				var index = x + SamplesPerAxis * y;
				samples[index] = result.IsWet( origin + new Vector3( x * cellSize, y * cellSize, 0f ), rivers, field, edited, out var flow );
				appearance[index * 4] = (byte)Math.Clamp( (int)MathF.Round( flow.x * 127f ) + 128, 1, 255 );
				appearance[index * 4 + 1] = (byte)Math.Clamp( (int)MathF.Round( flow.y * 127f ) + 128, 1, 255 );
				appearance[index * 4 + 2] = (byte)Math.Clamp( (int)MathF.Round( flow.z * 255f ), 0, 255 );
				appearance[index * 4 + 3] = 0;
				hasAppearance |= appearance[index * 4] != 128 || appearance[index * 4 + 1] != 128 || appearance[index * 4 + 2] != 0;
			}
		}
		if ( hasAppearance ) result.Flow = appearance.ToArray();
		for ( var y = 0; y < CellsPerAxis; y++ )
		{
			cancellation.ThrowIfCancellationRequested();
			for ( var x = 0; x < CellsPerAxis; x++ )
			{
				var first = x + SamplesPerAxis * y;
				result.SurfaceCoverage[x + CellsPerAxis * y] =
					samples[first] || samples[first + 1] || samples[first + SamplesPerAxis + 1] || samples[first + SamplesPerAxis] ||
					result.ContainsHiddenWater( origin + new Vector3( x * cellSize, y * cellSize, 0f ), cellSize, rivers, field, edited, cancellation );
			}
		}
		return result;
	}

	private bool IsWet( Vector3 position, RiverWorld.Region rivers, TerrainFieldSnapshot field, bool edited, out Vector3 flow )
	{
		var settings = field.Settings;
		var natural = RegionalLandforms.SampleNatural( position, settings );
		var river = rivers.SampleWorld( position, natural.Height, blendSurfaceFlow: true );
		// Appearance samples are world-coordinate functions even on dry banks.
		// Never gate shared values on the current owner's conservative bounds.
		var habitat = TerrainBiomes.SampleWorld( position, settings, natural.Mountains );
		var marsh = habitat.MarshWeight( natural.Height, natural.Mountains, settings.SeaLevel );
		flow = new Vector3( river.Direction.x, river.Direction.y, marsh );
		var bottom = TerrainBiomes.RefineHeight( position, settings, natural.Height, river.Height, natural.Mountains );
		if ( bottom >= settings.SeaLevel ) return false;
		if ( !edited ) return true;
		var solid = TerrainCliffs.Sample( position, settings, natural.Mountains,
			TerrainCaves.SampleWorld( position, settings, bottom ) ) + field.SampleCorrection( position );
		return solid > 0f;
	}

	private bool ContainsHiddenWater( Vector3 origin, float size, RiverWorld.Region rivers,
		TerrainFieldSnapshot field, bool edited, CancellationToken cancellation )
	{
		cancellation.ThrowIfCancellationRequested();
		if ( size <= TerrainField.SampleSpacing ) return false;
		var radius = rivers.MinimumWetRadius( origin, origin + new Vector3( size, size, 0f ) );
		if ( _mayContainMarsh )
		{
			var heights = RegionalLandforms.BoundNaturalHeight( new SdfWorldAabb( origin, origin + new Vector3( size, size, 0f ) ), field.Settings );
			// Conditioning also permits river cuts smaller than16 units. Include
			// that allowance when bounding the pre-carve natural height.
			if ( heights.Maximum > SeaLevel - 128f && heights.Minimum < SeaLevel + 16f + field.Settings.ReliefHeight * TerrainBiomes.MaximumReliefFraction )
				radius = TerrainField.SampleSpacing;
		}
		if ( size <= MathF.Max( TerrainField.SampleSpacing, radius ) ) return false;
		// All four corners are known dry. Preserve the existing narrow-channel
		// detection lattice, but stop as soon as this owner cell has water.
		var half = size * 0.5f;
		for ( var index = 1; index < 8; index++ )
		{
			if ( index == 2 || index == 6 ) continue;
			RefinementSamples++;
			if ( IsWet( origin + new Vector3( (index % 3) * half, (index / 3) * half, 0f ), rivers, field, edited, out _ ) ) return true;
		}
		for ( var y = 0; y < 2; y++ )
		for ( var x = 0; x < 2; x++ )
			if ( ContainsHiddenWater( origin + new Vector3( x * half, y * half, 0f ), half, rivers, field, edited, cancellation ) ) return true;
		return false;
	}
}
