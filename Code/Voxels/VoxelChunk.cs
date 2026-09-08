public enum ChunkDensityClassification
{
	DefinitelySolid,
	DefinitelyAir,
	PotentiallySurfaceContaining
}

public readonly record struct ChunkDensityRange(
	float MinimumDensity,
	float MaximumDensity,
	ChunkDensityClassification Classification );

/// <summary>
/// Authoritative logical SDF samples for one spatial chunk. The deterministic
/// base field and immutable correction snapshot are sampled without a chunk density array.
/// Density below zero is solid, density above zero is air, and zero is the
/// terrain surface.
/// </summary>
internal sealed class VoxelChunk
{
	public const byte AirMaterialId = 0;
	public const byte GrassMaterialId = 1;

	private readonly Vector3Int _globalSampleOrigin;

	public Vector3Int Coordinate { get; }
	public int CellsPerAxis { get; }
	public int SamplesPerAxis { get; }
	public float CellSize { get; }
	public ProceduralTerrainSettings TerrainSettings => Field.Settings;
	public TerrainFieldSnapshot Field { get; }
	public int SampleCount { get; }
	public float MinimumDensity { get; }
	public float MaximumDensity { get; }
	public ChunkDensityClassification DensityClassification { get; }
	public float DensityRangeEvaluationMilliseconds { get; }
	public string HumanName => $"Chunk X {Coordinate.x}, Y {Coordinate.y}, Z {Coordinate.z}";
	public string LogId => $"C[{Coordinate.x},{Coordinate.y},{Coordinate.z}]";

	public VoxelChunk(
		Vector3Int coordinate,
		int cellsPerAxis,
		float cellSize,
		TerrainFieldSnapshot field )
		: this( coordinate, cellsPerAxis, cellSize, field, null )
	{
	}

	internal VoxelChunk(
		Vector3Int coordinate,
		int cellsPerAxis,
		float cellSize,
		TerrainFieldSnapshot field,
		ChunkDensityRange densityRange )
		: this( coordinate, cellsPerAxis, cellSize, field, (ChunkDensityRange?)densityRange )
	{
	}

	private VoxelChunk(
		Vector3Int coordinate,
		int cellsPerAxis,
		float cellSize,
		TerrainFieldSnapshot field,
		ChunkDensityRange? knownDensityRange )
	{
		Coordinate = coordinate;
		CellsPerAxis = cellsPerAxis;
		SamplesPerAxis = cellsPerAxis + 1;
		CellSize = cellSize;

		var boundsStart = System.Diagnostics.Stopwatch.GetTimestamp();
		var densityRange = knownDensityRange ?? ClassifyDensityRange(
			coordinate,
			cellsPerAxis,
			cellSize,
			field );
		DensityRangeEvaluationMilliseconds = knownDensityRange.HasValue
			? 0f
			: (float)System.Diagnostics.Stopwatch.GetElapsedTime( boundsStart ).TotalMilliseconds;
		_globalSampleOrigin = coordinate * cellsPerAxis;
		var size = cellsPerAxis * cellSize;
		var origin = new Vector3( coordinate.x, coordinate.y, coordinate.z ) * size;
		Field = field.CaptureRegion( new SdfWorldAabb( origin - Vector3.One * cellSize,
			origin + Vector3.One * (size + cellSize) ), pinSamples: false );
		SampleCount = checked( SamplesPerAxis * SamplesPerAxis * SamplesPerAxis );
		MinimumDensity = densityRange.MinimumDensity;
		MaximumDensity = densityRange.MaximumDensity;
		DensityClassification = densityRange.Classification;
	}

	/// <summary>
	/// Conservatively classifies the complete authoritative field range of one chunk.
	/// Future field contributions must participate in these bounds; when a complete
	/// range cannot be proven, return PotentiallySurfaceContaining.
	/// </summary>
	public static ChunkDensityRange ClassifyDensityRange(
		Vector3Int coordinate,
		int cellsPerAxis,
		float cellSize,
		TerrainFieldSnapshot field )
	{
		var size = cellsPerAxis * cellSize;
		var minimum = new Vector3( coordinate.x * size, coordinate.y * size, coordinate.z * size );
		return field.GetDensityRange( new SdfWorldAabb( minimum, minimum + new Vector3( size ) ), cellSize );
	}

	public static ChunkDensityClassification ClassifyDensityRangeBroadPhase(
		Vector3Int coordinate,
		int cellsPerAxis,
		float cellSize,
		TerrainFieldSnapshot field )
	{
		var size = cellsPerAxis * cellSize;
		var minimum = new Vector3( coordinate.x * size, coordinate.y * size, coordinate.z * size );
		field.GetCorrectionRange( new SdfWorldAabb( minimum, minimum + new Vector3( size ) ), out var low, out var high );
		if ( low != 0f || high != 0f ) return ChunkDensityClassification.PotentiallySurfaceContaining;
		return ProceduralTerrainSdf.ClassifyDensityRangeBroadPhase(
			coordinate,
			cellsPerAxis,
			cellSize,
			field.Settings );
	}

	public bool TryGetSample( Vector3Int localSample, out float density, out byte materialId )
	{
		if ( localSample.x < 0 || localSample.x >= SamplesPerAxis ||
			localSample.y < 0 || localSample.y >= SamplesPerAxis ||
			localSample.z < 0 || localSample.z >= SamplesPerAxis )
		{
			density = 0f;
			materialId = AirMaterialId;
			return false;
		}

		var global = _globalSampleOrigin + localSample;
		var position = new Vector3( global.x * CellSize, global.y * CellSize, global.z * CellSize );
		if ( !Field.TryCaptureRegion( new SdfWorldAabb( position, position ), out var reader ) )
		{
			density = 0f; materialId = AirMaterialId;
			return false;
		}
		density = reader.SampleWorld( position );
		materialId = density <= 0f ? GrassMaterialId : AirMaterialId;
		return true;
	}

	public static string GetMaterialName( byte materialId )
	{
		return materialId switch
		{
			AirMaterialId => "Air",
			GrassMaterialId => "Grass",
			_ => "Unknown"
		};
	}
}
