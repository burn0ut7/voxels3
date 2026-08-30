using System;

internal readonly record struct GpuSdfDescriptor(
	VoxelRenderRegionKey Key,
	int CellsPerAxis,
	float CellSize,
	ProceduralTerrainSettings TerrainSettings,
	int GeneratorVersion,
	int ContentRevision,
	int PlacementRevision )
{
	public Vector3Int RegionCoordinate => Key.Coordinate;
	public int Lod => Key.Lod;
	public VoxelRenderMeshKind MeshKind => Key.MeshKind;
	public VoxelTransitionFace Face => Key.Face;

	public static GpuSdfDescriptor FromChunk(
		VoxelChunk chunk,
		int contentRevision,
		int placementRevision )
	{
		return new GpuSdfDescriptor(
			VoxelRenderRegionKey.Regular( 0, chunk.Coordinate ),
			chunk.CellsPerAxis,
			chunk.CellSize,
			chunk.TerrainSettings,
			ProceduralTerrainSdf.CurrentVersion,
			contentRevision,
			placementRevision );
	}

	public static GpuSdfDescriptor ForRenderRegion(
		VoxelRenderRegionKey key,
		int cellsPerAxis,
		float lod0CellSize,
		ProceduralTerrainSettings terrainSettings,
		int contentRevision,
		int placementRevision )
	{
		if ( key.Lod < 0 || key.Lod > 30 )
			throw new ArgumentOutOfRangeException( nameof( key ) );
		return new GpuSdfDescriptor(
			key,
			cellsPerAxis,
			lod0CellSize * (1 << key.Lod),
			terrainSettings,
			ProceduralTerrainSdf.CurrentVersion,
			contentRevision,
			placementRevision );
	}
}
