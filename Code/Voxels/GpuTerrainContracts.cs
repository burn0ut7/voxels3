using System.Runtime.InteropServices;

[StructLayout( LayoutKind.Sequential, Pack = 4, Size = 24 )]
internal struct TerrainVertex
{
	[VertexLayout.Position]
	public Vector3 Position;
	[VertexLayout.Normal]
	public Vector3 Normal;
}

[StructLayout( LayoutKind.Sequential, Pack = 4, Size = 64 )]
internal struct GpuTerrainRequest
{
	public Vector4 OriginAndCellSize;
	public Vector4 Terrain;
	public int CellsPerAxis;
	public uint Generation;
	public uint RequestIndex;
	public uint Reserved0;
	public Vector4 Reserved1;
}

[StructLayout( LayoutKind.Sequential, Pack = 4, Size = 32 )]
internal struct GpuTerrainCountResult
{
	public uint VertexCount;
	public uint IndexCount;
	public uint Generation;
	public uint RequestIndex;
	public uint ActiveCells;
	public uint TopologyDigest;
	public uint PositionDigest;
	public uint Reserved;
}

[StructLayout( LayoutKind.Sequential, Pack = 4, Size = 64 )]
internal struct GpuTerrainAllocationDescriptor
{
	public uint VertexOffset;
	public uint VertexCapacity;
	public uint IndexOffset;
	public uint IndexCapacity;
	public uint Generation;
	public uint RequestIndex;
	public uint Enabled;
	public uint Reserved;
	public Vector4 Reserved0;
	public Vector4 Reserved1;
}

internal readonly record struct GpuTerrainRange( int Offset, int Count )
{
	public bool IsEmpty => Count == 0;
	public int End => checked( Offset + Count );
}
