internal enum GpuMeshLevel
{
	Lod0,
	Lod1,
	Lod2
}

internal readonly record struct GpuMeshRegionKey( GpuMeshLevel Level, Vector3Int Coordinate );

internal enum GpuTransitionFace
{
	NegativeX,
	PositiveX,
	NegativeY,
	PositiveY,
	NegativeZ,
	PositiveZ
}

internal readonly record struct GpuTransitionKey(
	GpuMeshLevel CoarseLevel,
	Vector3Int CoarseCoordinate,
	GpuTransitionFace Face );

internal readonly record struct GpuSdfDescriptor(
	GpuMeshRegionKey Key,
	int CellsPerAxis,
	float CellSize,
	ProceduralTerrainSettings TerrainSettings,
	int GeneratorVersion,
	int SourceRevision )
{
	public Vector3Int ChunkCoordinate => Key.Coordinate;

	public static GpuSdfDescriptor FromChunk(
		VoxelChunk chunk,
		int sourceRevision )
	{
		return new GpuSdfDescriptor(
			new GpuMeshRegionKey( GpuMeshLevel.Lod0, chunk.Coordinate ),
			chunk.CellsPerAxis,
			chunk.CellSize,
			chunk.TerrainSettings,
			ProceduralTerrainSdf.CurrentVersion,
			sourceRevision );
	}
}

internal readonly record struct GpuTransitionDescriptor(
	GpuTransitionKey Key,
	int CellsPerAxis,
	float FineCellSize,
	float CoarseCellSize,
	ProceduralTerrainSettings TerrainSettings,
	int GeneratorVersion,
	int SourceRevision,
	uint CoarseMask );
