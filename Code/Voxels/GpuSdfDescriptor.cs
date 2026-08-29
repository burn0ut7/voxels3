internal readonly record struct GpuSdfDescriptor(
	Vector3Int ChunkCoordinate,
	int CellsPerAxis,
	float CellSize,
	ProceduralTerrainSettings TerrainSettings,
	int GeneratorVersion,
	int SourceRevision )
{
	public static GpuSdfDescriptor FromChunk(
		VoxelChunk chunk,
		int sourceRevision )
	{
		return new GpuSdfDescriptor(
			chunk.Coordinate,
			chunk.CellsPerAxis,
			chunk.CellSize,
			chunk.TerrainSettings,
			ProceduralTerrainSdf.CurrentVersion,
			sourceRevision );
	}
}
