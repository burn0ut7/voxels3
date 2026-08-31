struct TerrainRequest
{
	float4 OriginAndCellSize;
	float4 Terrain;
	int CellsPerAxis;
	uint Generation;
	uint RequestIndex;
	uint PackedIdentity;
	float4 Reserved1;
};

struct CountResult
{
	uint VertexCount;
	uint IndexCount;
	uint Generation;
	uint RequestIndex;
	uint ActiveCells;
	uint TopologyDigest;
	uint PositionDigest;
	uint Reserved;
};

StructuredBuffer<TerrainRequest> Requests < Attribute( "Requests" ); >;
RWStructuredBuffer<float> DensitySamples < Attribute( "DensitySamples" ); >;
RWStructuredBuffer<uint3> Cells < Attribute( "Cells" ); >;
RWStructuredBuffer<uint> VertexCounts < Attribute( "EdgeFlags" ); >;
RWStructuredBuffer<uint> VertexOffsets < Attribute( "EdgeVertexIds" ); >;
RWStructuredBuffer<uint> VertexGroupSums < Attribute( "EdgeGroupSums" ); >;
RWStructuredBuffer<uint> IndexGroupSums < Attribute( "CellGroupSums" ); >;
RWStructuredBuffer<uint> BlockCounts < Attribute( "BlockCounts" ); >;
RWStructuredBuffer<uint> ActiveCellCounts < Attribute( "ActiveCellCounts" ); >;
RWStructuredBuffer<uint2> Digests < Attribute( "Digests" ); >;
RWStructuredBuffer<CountResult> CountResults < Attribute( "CountResults" ); >;

int TransitionStage < Attribute( "TransitionStage" ); >;
int ChunkSize < Attribute( "ChunkSize" ); >;
int TransitionSampleSize < Attribute( "TransitionSampleSize" ); >;
int TransitionSampleCount < Attribute( "TransitionSampleCount" ); >;
int TransitionSampleStride < Attribute( "TransitionSampleStride" ); >;
int TransitionCellCount < Attribute( "TransitionCellCount" ); >;
int TransitionGroupCount < Attribute( "TransitionGroupCount" ); >;
int BatchSize < Attribute( "BatchSize" ); >;

static const uint TransitionCaseWeights[9] =
{
	0x001, 0x002, 0x004, 0x080, 0x100, 0x008, 0x040, 0x020, 0x010
};

groupshared uint TransitionScan[256];

uint2 TransitionDecode2D( uint index, uint size )
{
	return uint2( index % size, index / size );
}

float3 TransitionFacePoint( TerrainRequest request, uint face, float u, float v )
{
	float extent = request.OriginAndCellSize.w * request.CellsPerAxis;
	float3 local = face == 0 ? float3( 0, extent - u, v ) :
		face == 1 ? float3( extent, u, v ) :
		face == 2 ? float3( u, 0, v ) :
		face == 3 ? float3( extent - u, extent, v ) :
		face == 4 ? float3( u, extent - v, 0 ) :
		float3( u, v, extent );
	return request.OriginAndCellSize.xyz + local;
}

float3 TransitionFaceNormal( uint face )
{
	return face == 0 ? float3( -1, 0, 0 ) :
		face == 1 ? float3( 1, 0, 0 ) :
		face == 2 ? float3( 0, -1, 0 ) :
		face == 3 ? float3( 0, 1, 0 ) :
		face == 4 ? float3( 0, 0, -1 ) :
		float3( 0, 0, 1 );
}

float TransitionSampleSdf( TerrainRequest request, float3 worldPosition )
{
	float fineCellSize = request.OriginAndCellSize.w * 0.5;
	int3 globalSample = (int3)round( worldPosition / fineCellSize );
	return SampleVoxelSdf(
		globalSample,
		fineCellSize,
		(int)request.Terrain.x,
		request.Terrain.y,
		request.Terrain.z,
		request.Terrain.w );
}

float TransitionDensity( uint block, uint x, uint y )
{
	float value = DensitySamples[
		block * (uint)TransitionSampleStride + (uint)TransitionSampleCount +
		x + y * (uint)TransitionSampleSize];
	return abs( value ) < 0.000001 ? (value < 0 ? -0.000001 : 0.000001) : value;
}

uint TransitionHash( uint value )
{
	value ^= value >> 16;
	value *= 0x7feb352d;
	value ^= value >> 15;
	value *= 0x846ca68b;
	value ^= value >> 16;
	return value;
}

void TransitionExclusiveScan( uint lane, uint value )
{
	TransitionScan[lane] = value;
	GroupMemoryBarrierWithGroupSync();
	for ( uint step = 1; step < 256; step <<= 1 )
	{
		uint index = (lane + 1) * step * 2 - 1;
		if ( index < 256 ) TransitionScan[index] += TransitionScan[index - step];
		GroupMemoryBarrierWithGroupSync();
	}
	if ( lane == 0 ) TransitionScan[255] = 0;
	GroupMemoryBarrierWithGroupSync();
	for ( uint step = 128; step > 0; step >>= 1 )
	{
		uint index = (lane + 1) * step * 2 - 1;
		if ( index < 256 )
		{
			uint saved = TransitionScan[index - step];
			TransitionScan[index - step] = TransitionScan[index];
			TransitionScan[index] += saved;
		}
		GroupMemoryBarrierWithGroupSync();
	}
}

[numthreads(256,1,1)]
void MainCs( uint3 dispatchId : SV_DispatchThreadID, uint3 groupId : SV_GroupID, uint lane : SV_GroupIndex )
{
	uint index = dispatchId.x;
	if ( TransitionStage == 0 )
	{
		if ( index < (uint)TransitionCellCount * (uint)BatchSize )
		{
			Cells[index] = uint3( 0, 0, 0 );
			VertexCounts[index] = 0;
			VertexOffsets[index] = 0;
		}
		if ( index < (uint)BatchSize )
		{
			ActiveCellCounts[index] = 0;
			Digests[index] = uint2( 0, 0 );
		}
		return;
	}

	if ( TransitionStage == 1 )
	{
		uint total = (uint)TransitionSampleStride * (uint)BatchSize;
		if ( index >= total ) return;
		uint block = index / (uint)TransitionSampleStride;
		uint local = index - block * (uint)TransitionSampleStride;
		uint layer = local / (uint)TransitionSampleCount;
		uint sampleIndex = local - layer * (uint)TransitionSampleCount;
		uint2 sample = TransitionDecode2D( sampleIndex, TransitionSampleSize );
		TerrainRequest request = Requests[block];
		uint face = (request.PackedIdentity >> 8) & 0xff;
		float spacing = request.OriginAndCellSize.w * 0.5;
		float3 worldPosition = TransitionFacePoint(
			request, face, sample.x * spacing, sample.y * spacing ) +
			TransitionFaceNormal( face ) * ((float)layer - 1.0) * spacing;
		DensitySamples[index] = TransitionSampleSdf( request, worldPosition );
		return;
	}

	if ( TransitionStage == 2 )
	{
		uint total = (uint)TransitionCellCount * (uint)BatchSize;
		if ( index >= total ) return;
		uint block = index / (uint)TransitionCellCount;
		uint local = index - block * (uint)TransitionCellCount;
		uint2 cell = TransitionDecode2D( local, ChunkSize );
		uint code = 0;
		for ( uint sample = 0; sample < 9; sample++ )
		{
			uint sx = cell.x * 2 + sample % 3;
			uint sy = cell.y * 2 + sample / 3;
			if ( TransitionDensity( block, sx, sy ) < 0 ) code |= TransitionCaseWeights[sample];
		}
		uint cellClass = TransitionCellClass[code];
		uint counts = TransitionCellGeometryCounts[cellClass & 0x7f];
		uint vertexCount = counts >> 4;
		uint indexCount = (counts & 0xf) * 3;
		Cells[index] = uint3( code, indexCount, 0 );
		VertexCounts[index] = vertexCount;
		if ( indexCount != 0 ) InterlockedAdd( ActiveCellCounts[block], 1 );
		uint topology = TransitionHash( local ^ (code << 16) ^ indexCount ^ Requests[block].PackedIdentity );
		InterlockedXor( Digests[block].x, topology );
		uint densityHash = 0;
		for ( uint sample = 0; sample < 9; sample++ )
		{
			uint sx = cell.x * 2 + sample % 3;
			uint sy = cell.y * 2 + sample / 3;
			densityHash ^= TransitionHash( asuint( TransitionDensity( block, sx, sy ) ) + sample );
		}
		InterlockedXor( Digests[block].y, TransitionHash( densityHash ^ local ) );
		return;
	}

	if ( TransitionStage == 3 || TransitionStage == 4 )
	{
		uint block = groupId.x / (uint)TransitionGroupCount;
		uint cellGroup = groupId.x - block * (uint)TransitionGroupCount;
		if ( block >= (uint)BatchSize ) return;
		uint local = cellGroup * 256 + lane;
		uint address = block * (uint)TransitionCellCount + local;
		uint value = local < (uint)TransitionCellCount
			? (TransitionStage == 3 ? VertexCounts[address] : Cells[address].y)
			: 0;
		TransitionExclusiveScan( lane, value );
		uint total = value + TransitionScan[lane];
		if ( local < (uint)TransitionCellCount )
		{
			if ( TransitionStage == 3 ) VertexOffsets[address] = TransitionScan[lane];
			else Cells[address].z = TransitionScan[lane];
		}
		if ( lane == 255 )
		{
			uint groupAddress = block * (uint)TransitionGroupCount + cellGroup;
			if ( TransitionStage == 3 ) VertexGroupSums[groupAddress] = total;
			else IndexGroupSums[groupAddress] = total;
		}
		return;
	}

	if ( TransitionStage == 5 )
	{
		uint block = groupId.x;
		if ( block >= (uint)BatchSize || lane != 0 ) return;
		uint vertices = 0;
		uint indices = 0;
		for ( uint group = 0; group < (uint)TransitionGroupCount; group++ )
		{
			uint address = block * (uint)TransitionGroupCount + group;
			uint vertexCount = VertexGroupSums[address];
			uint indexCount = IndexGroupSums[address];
			VertexGroupSums[address] = vertices;
			IndexGroupSums[address] = indices;
			vertices += vertexCount;
			indices += indexCount;
		}
		BlockCounts[block * 2] = vertices;
		BlockCounts[block * 2 + 1] = indices;
		return;
	}

	if ( TransitionStage == 6 )
	{
		if ( index >= (uint)BatchSize ) return;
		CountResult result;
		result.VertexCount = BlockCounts[index * 2];
		result.IndexCount = BlockCounts[index * 2 + 1];
		result.Generation = Requests[index].Generation;
		result.RequestIndex = index;
		result.ActiveCells = ActiveCellCounts[index];
		result.TopologyDigest = Digests[index].x;
		result.PositionDigest = Digests[index].y;
		result.Reserved = 0;
		CountResults[index] = result;
	}
}
