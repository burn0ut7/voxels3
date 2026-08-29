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
/// Authoritative logical SDF samples for one spatial chunk. The current flat
/// field is evaluated directly without allocating a redundant density array.
/// Density below zero is solid, density above zero is air, and zero is the
/// terrain surface.
/// </summary>
public sealed class VoxelChunk
{
	public const byte AirMaterialId = 0;
	public const byte GrassMaterialId = 1;

	private readonly float _chunkMinimumZ;
	private readonly float _terrainSurfaceHeight;

	public Vector3Int Coordinate { get; }
	public int CellsPerAxis { get; }
	public int SamplesPerAxis { get; }
	public float CellSize { get; }
	public int SampleCount { get; }
	public float MinimumDensity { get; }
	public float MaximumDensity { get; }
	public ChunkDensityClassification DensityClassification { get; }
	public string HumanName => $"Chunk X {Coordinate.x}, Y {Coordinate.y}, Z {Coordinate.z}";
	public string LogId => $"C[{Coordinate.x},{Coordinate.y},{Coordinate.z}]";

	public VoxelChunk( Vector3Int coordinate, int cellsPerAxis, float cellSize, float terrainSurfaceHeight )
	{
		Coordinate = coordinate;
		CellsPerAxis = cellsPerAxis;
		SamplesPerAxis = cellsPerAxis + 1;
		CellSize = cellSize;

		var densityRange = ClassifyDensityRange( coordinate, cellsPerAxis, cellSize, terrainSurfaceHeight );
		var chunkWorldSize = cellsPerAxis * cellSize;
		_chunkMinimumZ = coordinate.z * chunkWorldSize;
		_terrainSurfaceHeight = terrainSurfaceHeight;
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
		float terrainSurfaceHeight )
	{
		var chunkWorldSize = cellsPerAxis * cellSize;
		var chunkMinimumZ = coordinate.z * chunkWorldSize;
		var minimumDensity = chunkMinimumZ - terrainSurfaceHeight;
		var maximumDensity = chunkMinimumZ + cellsPerAxis * cellSize - terrainSurfaceHeight;
		var classification = maximumDensity <= 0f
			? ChunkDensityClassification.DefinitelySolid
			: minimumDensity > 0f
				? ChunkDensityClassification.DefinitelyAir
				: ChunkDensityClassification.PotentiallySurfaceContaining;
		return new ChunkDensityRange( minimumDensity, maximumDensity, classification );
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

		density = _chunkMinimumZ + localSample.z * CellSize - _terrainSurfaceHeight;
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
