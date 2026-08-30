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
StructuredBuffer<uint> VertexOffsets < Attribute( "EdgeVertexIds" ); >;
StructuredBuffer<uint> VertexGroupSums < Attribute( "EdgeGroupSums" ); >;
StructuredBuffer<uint> IndexGroupSums < Attribute( "CellGroupSums" ); >;
StructuredBuffer<AllocationDescriptor> Allocations < Attribute( "Allocations" ); >;
RWStructuredBuffer<uint> OutputIndices < Attribute( "OutputIndices" ); >;

int TransitionCellCount < Attribute( "TransitionCellCount" ); >;
int TransitionGroupCount < Attribute( "TransitionGroupCount" ); >;
int BatchSize < Attribute( "BatchSize" ); >;

[numthreads(256,1,1)]
void MainCs( uint3 dispatchId : SV_DispatchThreadID )
{
	uint index = dispatchId.x;
	uint total = (uint)TransitionCellCount * (uint)BatchSize;
	if ( index >= total ) return;
	uint block = index / (uint)TransitionCellCount;
	uint localCell = index - block * (uint)TransitionCellCount;
	AllocationDescriptor allocation = Allocations[block];
	if ( allocation.Enabled == 0 ) return;
	uint3 cell = Cells[index];
	uint code = cell.x;
	uint rawClass = TransitionCellClass[code];
	uint cellClass = rawClass & 0x7f;
	uint triangleCount = TransitionCellGeometryCounts[cellClass] & 0xf;
	if ( triangleCount == 0 ) return;
	uint groupOffset = VertexGroupSums[
		block * (uint)TransitionGroupCount + localCell / 256];
	uint vertexOffset = VertexOffsets[index];
	uint output = IndexGroupSums[
		block * (uint)TransitionGroupCount + localCell / 256] + cell.z;
	if ( output + triangleCount * 3 > allocation.IndexCapacity ) return;
	bool inverted = (rawClass & 0x80) != 0;
	for ( uint triangle = 0; triangle < triangleCount; triangle++ )
	{
		uint table = cellClass * 36 + triangle * 3;
		uint first = TransitionCellVertexIndices[table];
		uint second = TransitionCellVertexIndices[table + (inverted ? 2 : 1)];
		uint third = TransitionCellVertexIndices[table + (inverted ? 1 : 2)];
		uint target = allocation.IndexOffset + output + triangle * 3;
		OutputIndices[target] = groupOffset + vertexOffset + first;
		OutputIndices[target + 1] = groupOffset + vertexOffset + second;
		OutputIndices[target + 2] = groupOffset + vertexOffset + third;
	}
}
