struct AllocationDescriptor
{
	uint VertexOffset;
	uint VertexCapacity;
	uint IndexOffset;
	uint IndexCapacity;
	uint Generation;
	uint RequestIndex;
	uint Enabled;
	uint Reserved;
	float4 Reserved0;
	float4 Reserved1;
};

StructuredBuffer<uint3> Cells < Attribute( "Cells" ); >;
StructuredBuffer<uint> EdgeVertexIds < Attribute( "EdgeVertexIds" ); >;
StructuredBuffer<uint> EdgeGroupSums < Attribute( "EdgeGroupSums" ); >;
StructuredBuffer<uint> CellGroupSums < Attribute( "CellGroupSums" ); >;
StructuredBuffer<AllocationDescriptor> Allocations < Attribute( "Allocations" ); >;
RWStructuredBuffer<uint> OutputIndices < Attribute( "OutputIndices" ); >;

int ChunkSize < Attribute( "ChunkSize" ); >;
int SampleSize < Attribute( "SampleSize" ); >;
int CellCount < Attribute( "CellCount" ); >;
int EdgeSlotCount < Attribute( "EdgeSlotCount" ); >;
int EdgeGroupCount < Attribute( "EdgeGroupCount" ); >;
int CellGroupCount < Attribute( "CellGroupCount" ); >;
int BatchSize < Attribute( "BatchSize" ); >;

static const uint3 Corners[8] = {
	uint3( 0, 0, 0 ), uint3( 1, 0, 0 ), uint3( 0, 1, 0 ), uint3( 1, 1, 0 ),
	uint3( 0, 0, 1 ), uint3( 1, 0, 1 ), uint3( 0, 1, 1 ), uint3( 1, 1, 1 ) };

uint3 DecodePoint( uint index, uint size )
{
	uint plane = size * size;
	uint z = index / plane;
	uint remainder = index - z * plane;
	uint y = remainder / size;
	return uint3( remainder - y * size, y, z );
}

uint SampleIndex( uint3 point )
{
	return point.x + SampleSize * (point.y + SampleSize * point.z);
}

uint EdgeSlot( uint3 cell, uint data )
{
	uint code = data & 0xff;
	uint firstCorner = (code >> 4) & 0xf;
	uint secondCorner = code & 0xf;
	uint3 firstPoint = cell + Corners[firstCorner];
	uint3 secondPoint = cell + Corners[secondCorner];
	uint3 delta = uint3( abs( int3( firstPoint ) - int3( secondPoint ) ) );
	uint axis = delta.x != 0 ? 0 : (delta.y != 0 ? 1 : 2);
	return SampleIndex( min( firstPoint, secondPoint ) ) * 3 + axis;
}

[numthreads(256,1,1)]
void MainCs( uint3 dispatchId : SV_DispatchThreadID )
{
	uint index = dispatchId.x;
	uint totalCells = (uint)CellCount * (uint)BatchSize;
	if ( index >= totalCells )
	{
		return;
	}

	uint block = index / (uint)CellCount;
	uint local = index - block * (uint)CellCount;
	uint code = Cells[index].x;
	AllocationDescriptor allocation = Allocations[block];
	if ( allocation.Enabled == 0 || code == 0 || code == 255 )
	{
		return;
	}

	uint3 cell = DecodePoint( local, ChunkSize );
	uint cellClass = RegularCellClass[code];
	uint counts = RegularCellGeometryCounts[cellClass];
	uint vertexCount = counts >> 4;
	uint triangleCount = counts & 0xf;
	uint vertices[12];
	for ( uint vertex = 0; vertex < vertexCount; vertex++ )
	{
		uint data = RegularVertexData[code * 12 + vertex];
		uint edge = EdgeSlot( cell, data );
		uint groupOffset = EdgeGroupSums[
			block * (uint)EdgeGroupCount + edge / 256];
		uint edgeVertex = EdgeVertexIds[block * (uint)EdgeSlotCount + edge];
		vertices[vertex] = groupOffset + edgeVertex;
		if ( vertices[vertex] >= allocation.VertexCapacity )
		{
			return;
		}
	}

	uint output = CellGroupSums[
		block * (uint)CellGroupCount + local / 256] + Cells[index].z;
	if ( output + triangleCount * 3 > allocation.IndexCapacity )
	{
		return;
	}

	uint topology = cellClass * 15;
	for ( uint triangle = 0; triangle < triangleCount; triangle++ )
	{
		uint table = topology + triangle * 3;
		uint target = allocation.IndexOffset + output + triangle * 3;
		OutputIndices[target] = vertices[RegularCellVertexIndices[table]];
		OutputIndices[target + 1] = vertices[RegularCellVertexIndices[table + 1]];
		OutputIndices[target + 2] = vertices[RegularCellVertexIndices[table + 2]];
	}
}
