internal readonly record struct GpuSdfDescriptor(
	Vector3Int ChunkCoordinate,
	int CellsPerAxis,
	float CellSize,
	float SurfaceHeight,
	int SourceRevision )
{
	public static GpuSdfDescriptor FromChunk( VoxelChunk chunk, float surfaceHeight, int sourceRevision )
	{
		return new GpuSdfDescriptor(
			chunk.Coordinate,
			chunk.CellsPerAxis,
			chunk.CellSize,
			surfaceHeight,
			sourceRevision );
	}
}
