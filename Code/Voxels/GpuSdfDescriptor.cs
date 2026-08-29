internal readonly record struct GpuSdfDescriptor(
	Vector3Int ChunkCoordinate,
	int CellsPerAxis,
	float CellSize,
	int WorldSeed,
	int GeneratorVersion,
	int SourceRevision )
{
	public static GpuSdfDescriptor FromChunk(
		VoxelChunk chunk,
		int worldSeed,
		int generatorVersion,
		int sourceRevision )
	{
		return new GpuSdfDescriptor(
			chunk.Coordinate,
			chunk.CellsPerAxis,
			chunk.CellSize,
			worldSeed,
			generatorVersion,
			sourceRevision );
	}
}
