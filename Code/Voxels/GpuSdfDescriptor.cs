internal enum GpuMeshLevel
{
	Lod0,
	Lod1,
	Lod2
}

internal readonly record struct GpuMeshRegionKey( GpuMeshLevel Level, Vector3Int Coordinate );

internal enum Lod0Lod1TransitionFace
{
	NegativeX,
	PositiveX,
	NegativeY,
	PositiveY,
	NegativeZ,
	PositiveZ
}

internal readonly record struct Lod0Lod1TransitionKey(
	Vector3Int Lod1Coordinate,
	Lod0Lod1TransitionFace Face );

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
	Lod0Lod1TransitionKey Key,
	int CellsPerAxis,
	float FineCellSize,
	float CoarseCellSize,
	ProceduralTerrainSettings TerrainSettings,
	int GeneratorVersion,
	int SourceRevision );
