struct TransitionRequest
{
	float4 OriginAndFineCellSize;
	float4 Terrain;
	float4 BasisUAndCoarseCellSize;
	float4 BasisVAndCellsPerAxis;
	float4 NormalAndFace;
	uint Generation;
	uint RequestIndex;
	float4 CoarseOriginAndMask;
	uint Reserved0;
	uint Reserved1;
};

struct TransitionCountResult
{
	uint VertexCount;
	uint IndexCount;
	uint Generation;
	uint RequestIndex;
	uint ActiveCells;
	uint TopologyDigest;
	uint PositionDigest;
	uint FineFaceMismatchCount;
	uint CoarseFaceMismatchCount;
	uint MinimumUDigest;
	uint MaximumUDigest;
	uint MinimumVDigest;
	uint MaximumVDigest;
	uint InvalidTableCount;
	uint Reserved0;
	uint Reserved1;
};

struct TransitionAllocationDescriptor
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

struct TransitionTerrainVertexWords
{
	uint4 First;
	uint2 Second;
};

StructuredBuffer<TransitionRequest> TransitionRequests < Attribute( "TransitionRequests" ); >;
RWStructuredBuffer<float> TransitionDensitySamples < Attribute( "TransitionDensitySamples" ); >;
RWStructuredBuffer<uint4> TransitionCells < Attribute( "TransitionCells" ); >;
RWStructuredBuffer<uint> TransitionEdgeFlags < Attribute( "TransitionEdgeFlags" ); >;
RWStructuredBuffer<uint> TransitionEdgeVertexIds < Attribute( "TransitionEdgeVertexIds" ); >;
RWStructuredBuffer<uint> TransitionEdgeGroupSums < Attribute( "TransitionEdgeGroupSums" ); >;
RWStructuredBuffer<uint> TransitionCellGroupSums < Attribute( "TransitionCellGroupSums" ); >;
RWStructuredBuffer<uint> TransitionBlockCounts < Attribute( "TransitionBlockCounts" ); >;
RWStructuredBuffer<uint2> TransitionCellAuditCounts < Attribute( "TransitionCellAuditCounts" ); >;
RWStructuredBuffer<uint2> TransitionDigests < Attribute( "TransitionDigests" ); >;
RWStructuredBuffer<uint2> TransitionFaceMismatchCounts < Attribute( "TransitionFaceMismatchCounts" ); >;
RWStructuredBuffer<uint4> TransitionLateralDigests < Attribute( "TransitionLateralDigests" ); >;
RWStructuredBuffer<TransitionCountResult> TransitionCountResults < Attribute( "TransitionCountResults" ); >;
StructuredBuffer<TransitionAllocationDescriptor> TransitionAllocations < Attribute( "TransitionAllocations" ); >;
RWStructuredBuffer<uint> TransitionOutputIndices < Attribute( "TransitionOutputIndices" ); >;
RWStructuredBuffer<TransitionTerrainVertexWords> TransitionOutputVertices < Attribute( "TransitionOutputVertices" ); >;

int TransitionStage < Attribute( "TransitionStage" ); >;
int TransitionBatchSize < Attribute( "TransitionBatchSize" ); >;
int TransitionDensitySize < Attribute( "TransitionDensitySize" ); >;
int TransitionDensityCount < Attribute( "TransitionDensityCount" ); >;
int TransitionCellCount < Attribute( "TransitionCellCount" ); >;
int TransitionEdgeSlotCount < Attribute( "TransitionEdgeSlotCount" ); >;
int TransitionEdgeGroupCount < Attribute( "TransitionEdgeGroupCount" ); >;
int TransitionCellGroupCount < Attribute( "TransitionCellGroupCount" ); >;

static const uint2 TransitionSamplePoints[13] =
{
	uint2( 0, 0 ), uint2( 1, 0 ), uint2( 2, 0 ),
	uint2( 0, 1 ), uint2( 1, 1 ), uint2( 2, 1 ),
	uint2( 0, 2 ), uint2( 1, 2 ), uint2( 2, 2 ),
	uint2( 0, 0 ), uint2( 2, 0 ), uint2( 0, 2 ), uint2( 2, 2 )
};

static const uint TransitionCaseSampleOrder[9] = { 0, 1, 2, 5, 8, 7, 6, 3, 4 };
static const uint TransitionPlaneDensityCount = 69 * 69;
static const uint TransitionFineNormalDensityCount = 65 * 65;
static const uint TransitionCoarseNormalDensityCount = 33 * 33;
groupshared uint TransitionScan[256];

uint TransitionHash( uint value )
{
	value ^= value >> 16;
	value *= 0x7feb352d;
	value ^= value >> 15;
	value *= 0x846ca68b;
	value ^= value >> 16;
	return value;
}

void TransitionDecodeDensity( uint index, out uint2 point, out int normalOffset )
{
	if ( index < TransitionPlaneDensityCount )
	{
		uint v = index / 69;
		point = uint2( index - v * 69, v );
		normalOffset = 0;
		return;
	}
	index -= TransitionPlaneDensityCount;
	if ( index < TransitionFineNormalDensityCount * 2 )
	{
		normalOffset = index < TransitionFineNormalDensityCount ? -1 : 1;
		index %= TransitionFineNormalDensityCount;
		uint v = index / 65;
		point = uint2( index - v * 65, v );
		return;
	}
	index -= TransitionFineNormalDensityCount * 2;
	normalOffset = index < TransitionCoarseNormalDensityCount ? -2 : 2;
	index %= TransitionCoarseNormalDensityCount;
	uint v = index / 33;
	point = uint2( index - v * 33, v ) * 2;
}

uint2 TransitionDecodeCell( uint index )
{
	uint cells = 32;
	return uint2( index % cells, index / cells );
}

uint TransitionDensityIndex( int2 point, int normalOffset )
{
	if ( normalOffset == 0 )
	{
		int2 halo = point + 2;
		return (uint)(halo.x + 69 * halo.y);
	}
	if ( abs( normalOffset ) == 1 )
	{
		uint normalBase = TransitionPlaneDensityCount +
			(normalOffset > 0 ? TransitionFineNormalDensityCount : 0);
		return normalBase + (uint)(point.x + 65 * point.y);
	}
	uint coarseBase = TransitionPlaneDensityCount + TransitionFineNormalDensityCount * 2 +
		(normalOffset > 0 ? TransitionCoarseNormalDensityCount : 0);
	return coarseBase + (uint)(point.x / 2 + 33 * (point.y / 2));
}

float TransitionRawDensity( uint block, int2 point, int normalOffset )
{
	return TransitionDensitySamples[block * (uint)TransitionDensityCount +
		TransitionDensityIndex( point, normalOffset )];
}

float TransitionDensity( uint block, int2 point )
{
	float value = TransitionRawDensity( block, point, 0 );
	return abs( value ) < 0.000001 ? (value < 0 ? -0.000001 : 0.000001) : value;
}

uint TransitionCase( uint block, uint2 cell )
{
	uint code = 0;
	int2 basePoint = int2( cell * 2 );
	[unroll]
	for ( uint bit = 0; bit < 9; bit++ )
	{
		uint sample = TransitionCaseSampleOrder[bit];
		if ( TransitionDensity( block, basePoint + int2( TransitionSamplePoints[sample] ) ) < 0 )
		{
			code |= 1u << bit;
		}
	}
	return code;
}

uint TransitionEdgeSlot( uint2 cell, uint data )
{
	uint edge = data & 0xff;
	uint first = (edge >> 4) & 0xf;
	uint second = edge & 0xf;
	if ( first >= 13 || second >= 13 )
	{
		return 0xffffffff;
	}
	uint2 a = TransitionSamplePoints[first];
	uint2 b = TransitionSamplePoints[second];
	if ( first >= 9 && second >= 9 )
	{
		uint2 coarseA = cell + a / 2;
		uint2 coarseB = cell + b / 2;
		uint2 minimum = min( coarseA, coarseB );
		if ( coarseA.y == coarseB.y )
		{
			return 8320 + minimum.y * 32 + minimum.x;
		}
		if ( coarseA.x == coarseB.x )
		{
			return 9376 + minimum.y * 33 + minimum.x;
		}
		return 0xffffffff;
	}
	if ( first >= 9 || second >= 9 )
	{
		return 0xffffffff;
	}
	uint2 fineA = cell * 2 + a;
	uint2 fineB = cell * 2 + b;
	uint2 minimum = min( fineA, fineB );
	if ( fineA.y == fineB.y && abs( int( fineA.x ) - int( fineB.x ) ) == 1 )
	{
		return minimum.y * 64 + minimum.x;
	}
	if ( fineA.x == fineB.x && abs( int( fineA.y ) - int( fineB.y ) ) == 1 )
	{
		return 4160 + minimum.y * 65 + minimum.x;
	}
	return 0xffffffff;
}

void TransitionDecodeEdge( uint slot, out uint2 first, out uint2 second )
{
	if ( slot < 4160 )
	{
		first = uint2( slot % 64, slot / 64 );
		second = first + uint2( 1, 0 );
		return;
	}
	if ( slot < 8320 )
	{
		uint local = slot - 4160;
		first = uint2( local % 65, local / 65 );
		second = first + uint2( 0, 1 );
		return;
	}
	if ( slot < 9376 )
	{
		uint local = slot - 8320;
		first = uint2( (local % 32) * 2, (local / 32) * 2 );
		second = first + uint2( 2, 0 );
		return;
	}
	uint local = slot - 9376;
	first = uint2( (local % 33) * 2, (local / 33) * 2 );
	second = first + uint2( 0, 2 );
}

float3 TransitionWorldPoint( TransitionRequest request, float2 point )
{
	return request.OriginAndFineCellSize.xyz +
		request.BasisUAndCoarseCellSize.xyz * point.x * request.OriginAndFineCellSize.w +
		request.BasisVAndCellsPerAxis.xyz * point.y * request.OriginAndFineCellSize.w;
}

float3 TransitionGradient( uint block, int2 point, int step, TransitionRequest request )
{
	float du = TransitionRawDensity( block, point + int2( step, 0 ), 0 ) -
		TransitionRawDensity( block, point - int2( step, 0 ), 0 );
	float dv = TransitionRawDensity( block, point + int2( 0, step ), 0 ) -
		TransitionRawDensity( block, point - int2( 0, step ), 0 );
	float dn = TransitionRawDensity( block, point, step ) -
		TransitionRawDensity( block, point, -step );
	return request.BasisUAndCoarseCellSize.xyz * du +
		request.BasisVAndCellsPerAxis.xyz * dv + request.NormalAndFace.xyz * dn;
}

float3 TransitionSafeNormalize( float3 value )
{
	float lengthSquared = dot( value, value );
	return lengthSquared > 1e-12 ? value * rsqrt( lengthSquared ) : float3( 0, 0, 1 );
}

float2 TransitionEncodeTerrainNormal( float3 normal )
{
	normal /= abs( normal.x ) + abs( normal.y ) + abs( normal.z );
	if ( normal.z < 0.0 )
	{
		float2 signValue = float2(
			normal.x >= 0.0 ? 1.0 : -1.0,
			normal.y >= 0.0 ? 1.0 : -1.0 );
		normal.xy = (1.0 - abs( normal.yx )) * signValue;
	}
	return normal.xy;
}

float TransitionBoundaryDelta( float position, float extent, float cellSize, bool minimumFace )
{
	float width = cellSize * 0.25;
	if ( minimumFace && position < cellSize ) return saturate( 1.0 - position / cellSize ) * width;
	if ( !minimumFace && position > extent - cellSize )
		return -saturate( (position - (extent - cellSize)) / cellSize ) * width;
	return 0.0;
}

uint TransitionBoundaryCellMask( float3 localPosition, float extent, float cellSize )
{
	uint mask = 0u;
	if ( localPosition.x < cellSize ) mask |= 1u;
	if ( localPosition.x > extent - cellSize ) mask |= 2u;
	if ( localPosition.y < cellSize ) mask |= 4u;
	if ( localPosition.y > extent - cellSize ) mask |= 8u;
	if ( localPosition.z < cellSize ) mask |= 16u;
	if ( localPosition.z > extent - cellSize ) mask |= 32u;
	return mask;
}

uint TransitionBoundaryVertexMask( float3 localPosition, float extent, float cellSize )
{
	float epsilon = max( cellSize * 0.00001, 0.0001 );
	uint mask = 0u;
	if ( abs( localPosition.x ) <= epsilon ) mask |= 1u;
	if ( abs( localPosition.x - extent ) <= epsilon ) mask |= 2u;
	if ( abs( localPosition.y ) <= epsilon ) mask |= 4u;
	if ( abs( localPosition.y - extent ) <= epsilon ) mask |= 8u;
	if ( abs( localPosition.z ) <= epsilon ) mask |= 16u;
	if ( abs( localPosition.z - extent ) <= epsilon ) mask |= 32u;
	return mask;
}

float3 TransitionSecondaryPosition( TransitionRequest request, float3 primary, float3 normal )
{
	float coarseCellSize = request.BasisUAndCoarseCellSize.w;
	float coarseRegionSize = request.BasisVAndCellsPerAxis.w * coarseCellSize;
	uint transitionMask = (uint)round( request.CoarseOriginAndMask.w ) & 63u;
	if ( transitionMask == 0u ) return primary;

	float3 localPosition = primary - request.CoarseOriginAndMask.xyz;
	float extentTolerance = max( coarseCellSize * 0.001, 0.001 );
	if ( any( localPosition < -extentTolerance ) ||
		any( localPosition > coarseRegionSize + extentTolerance ) ) return primary;
	uint cellBorderMask = TransitionBoundaryCellMask( localPosition, coarseRegionSize, coarseCellSize );
	uint vertexBorderMask = TransitionBoundaryVertexMask( localPosition, coarseRegionSize, coarseCellSize );
	if ( (transitionMask & cellBorderMask) == 0u ||
		(vertexBorderMask & (~transitionMask & 63u)) != 0u ) return primary;

	float3 delta = float3(
		(transitionMask & 1u) != 0u ? TransitionBoundaryDelta( localPosition.x, coarseRegionSize, coarseCellSize, true ) :
			((transitionMask & 2u) != 0u ? TransitionBoundaryDelta( localPosition.x, coarseRegionSize, coarseCellSize, false ) : 0.0),
		(transitionMask & 4u) != 0u ? TransitionBoundaryDelta( localPosition.y, coarseRegionSize, coarseCellSize, true ) :
			((transitionMask & 8u) != 0u ? TransitionBoundaryDelta( localPosition.y, coarseRegionSize, coarseCellSize, false ) : 0.0),
		(transitionMask & 16u) != 0u ? TransitionBoundaryDelta( localPosition.z, coarseRegionSize, coarseCellSize, true ) :
			((transitionMask & 32u) != 0u ? TransitionBoundaryDelta( localPosition.z, coarseRegionSize, coarseCellSize, false ) : 0.0) );
	return primary + delta - normal * dot( normal, delta );
}

void TransitionExclusiveScan( uint lane, uint value )
{
	TransitionScan[lane] = value;
	GroupMemoryBarrierWithGroupSync();
	for ( uint step = 1; step < 256; step <<= 1 )
	{
		uint index = (lane + 1) * step * 2 - 1;
		if ( index < 256 )
		{
			TransitionScan[index] += TransitionScan[index - step];
		}
		GroupMemoryBarrierWithGroupSync();
	}
	if ( lane == 0 )
	{
		TransitionScan[255] = 0;
	}
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

[numthreads( 256, 1, 1 )]
void MainCs( uint3 dispatchId : SV_DispatchThreadID, uint3 groupId : SV_GroupID, uint lane : SV_GroupIndex )
{
	uint index = dispatchId.x;
	if ( TransitionStage == 0 )
	{
		if ( index < (uint)TransitionCellCount * (uint)TransitionBatchSize )
		{
			TransitionCells[index] = uint4( 0, 0, 0, 0 );
		}
		if ( index < (uint)TransitionEdgeSlotCount * (uint)TransitionBatchSize )
		{
			TransitionEdgeFlags[index] = 0;
			TransitionEdgeVertexIds[index] = 0;
		}
		if ( index < (uint)TransitionBatchSize )
		{
			TransitionCellAuditCounts[index] = uint2( 0, 0 );
			TransitionDigests[index] = uint2( 0, 0 );
			TransitionFaceMismatchCounts[index] = uint2( 0, 0 );
			TransitionLateralDigests[index] = uint4( 0, 0, 0, 0 );
		}
		return;
	}
	if ( TransitionStage == 1 )
	{
		uint total = (uint)TransitionDensityCount * (uint)TransitionBatchSize;
		if ( index >= total )
		{
			return;
		}
		uint block = index / (uint)TransitionDensityCount;
		uint local = index - block * (uint)TransitionDensityCount;
		uint2 point;
		int normalOffset;
		TransitionDecodeDensity( local, point, normalOffset );
		TransitionRequest request = TransitionRequests[block];
		float2 facePoint = float2( point );
		if ( normalOffset == 0 )
		{
			facePoint -= 2;
		}
		float3 world = TransitionWorldPoint( request, facePoint ) +
			request.NormalAndFace.xyz * normalOffset * request.OriginAndFineCellSize.w;
		int3 sample = (int3)round( world / request.OriginAndFineCellSize.w );
		TransitionDensitySamples[index] = SampleVoxelSdf(
			sample,
			request.OriginAndFineCellSize.w,
			(int)request.Terrain.x,
			request.Terrain.y,
			request.Terrain.z,
			request.Terrain.w );
		return;
	}
	if ( TransitionStage == 2 )
	{
		if ( index >= (uint)TransitionCellCount * (uint)TransitionBatchSize )
		{
			return;
		}
		uint block = index / (uint)TransitionCellCount;
		uint local = index - block * (uint)TransitionCellCount;
		uint2 cell = TransitionDecodeCell( local );
		uint code = TransitionCase( block, cell );
		TransitionCells[index].x = code;
		if ( code == 0 || code == 511 )
		{
			return;
		}
		uint cellClass = TransitionCellClass[code];
		uint counts = TransitionCellGeometryCounts[cellClass & 0x7f];
		uint vertexCount = counts >> 4;
		uint indexCount = (counts & 0xf) * 3;
		TransitionCells[index].y = indexCount;
		TransitionCells[index].w = vertexCount;
		InterlockedAdd( TransitionCellAuditCounts[block].x, 1 );
		uint metadataDigest = 0;
		for ( uint vertex = 0; vertex < vertexCount; vertex++ )
		{
			uint data = TransitionVertexData[code * 12 + vertex];
			uint slot = TransitionEdgeSlot( cell, data );
			if ( slot >= (uint)TransitionEdgeSlotCount )
			{
				InterlockedAdd( TransitionCellAuditCounts[block].y, 1 );
				continue;
			}
			InterlockedOr( TransitionEdgeFlags[block * (uint)TransitionEdgeSlotCount + slot], 1 );
			metadataDigest ^= TransitionHash( data ^ (vertex << 20) );
		}
		InterlockedXor( TransitionDigests[block].x,
			TransitionHash( local ^ (code << 16) ^ indexCount ^ (cellClass << 8) ) ^ metadataDigest );
		return;
	}
	if ( TransitionStage == 3 )
	{
		uint block = groupId.x / (uint)TransitionEdgeGroupCount;
		uint edgeGroup = groupId.x - block * (uint)TransitionEdgeGroupCount;
		if ( block >= (uint)TransitionBatchSize )
		{
			return;
		}
		uint local = edgeGroup * 256 + lane;
		uint address = block * (uint)TransitionEdgeSlotCount + local;
		uint value = local < (uint)TransitionEdgeSlotCount ? TransitionEdgeFlags[address] : 0;
		TransitionExclusiveScan( lane, value );
		uint total = value + TransitionScan[lane];
		if ( local < (uint)TransitionEdgeSlotCount )
		{
			TransitionEdgeVertexIds[address] = TransitionScan[lane];
		}
		if ( lane == 255 )
		{
			TransitionEdgeGroupSums[block * (uint)TransitionEdgeGroupCount + edgeGroup] = total;
		}
		return;
	}
	if ( TransitionStage == 4 )
	{
		uint block = groupId.x / (uint)TransitionCellGroupCount;
		uint cellGroup = groupId.x - block * (uint)TransitionCellGroupCount;
		if ( block >= (uint)TransitionBatchSize )
		{
			return;
		}
		uint local = cellGroup * 256 + lane;
		uint address = block * (uint)TransitionCellCount + local;
		uint value = local < (uint)TransitionCellCount ? TransitionCells[address].y : 0;
		TransitionExclusiveScan( lane, value );
		uint total = value + TransitionScan[lane];
		if ( local < (uint)TransitionCellCount )
		{
			TransitionCells[address].z = TransitionScan[lane];
		}
		if ( lane == 255 )
		{
			TransitionCellGroupSums[block * (uint)TransitionCellGroupCount + cellGroup] = total;
		}
		return;
	}
	if ( TransitionStage == 5 )
	{
		uint block = groupId.x;
		if ( block >= (uint)TransitionBatchSize || lane != 0 )
		{
			return;
		}
		uint vertices = 0;
		uint indices = 0;
		for ( uint group = 0; group < (uint)TransitionEdgeGroupCount; group++ )
		{
			uint address = block * (uint)TransitionEdgeGroupCount + group;
			uint count = TransitionEdgeGroupSums[address];
			TransitionEdgeGroupSums[address] = vertices;
			vertices += count;
		}
		for ( uint group = 0; group < (uint)TransitionCellGroupCount; group++ )
		{
			uint address = block * (uint)TransitionCellGroupCount + group;
			uint count = TransitionCellGroupSums[address];
			TransitionCellGroupSums[address] = indices;
			indices += count;
		}
		TransitionBlockCounts[block * 2] = vertices;
		TransitionBlockCounts[block * 2 + 1] = indices;
		return;
	}
	if ( TransitionStage == 6 )
	{
		if ( index >= (uint)TransitionEdgeSlotCount * (uint)TransitionBatchSize )
		{
			return;
		}
		uint block = index / (uint)TransitionEdgeSlotCount;
		uint slot = index - block * (uint)TransitionEdgeSlotCount;
		uint2 first;
		uint2 second;
		TransitionDecodeEdge( slot, first, second );
		float firstDensity = TransitionDensity( block, int2( first ) );
		float secondDensity = TransitionDensity( block, int2( second ) );
		bool expected = (firstDensity < 0) != (secondDensity < 0);
		bool actual = TransitionEdgeFlags[index] != 0;
		if ( expected != actual )
		{
			if ( slot < 8320 )
			{
				InterlockedAdd( TransitionFaceMismatchCounts[block].x, 1 );
			}
			else
			{
				InterlockedAdd( TransitionFaceMismatchCounts[block].y, 1 );
			}
		}
		if ( !actual )
		{
			return;
		}
		float denominator = firstDensity - secondDensity;
		float interpolation = saturate( abs( denominator ) > 0.000001 ?
			firstDensity / denominator : 0.5 );
		TransitionRequest request = TransitionRequests[block];
		float3 world = TransitionWorldPoint( request,
			lerp( float2( first ), float2( second ), interpolation ) );
		uint worldHash = TransitionHash( asuint( world.x ) ^ TransitionHash( asuint( world.y ) ) ^
			TransitionHash( asuint( world.z ) ) );
		InterlockedXor( TransitionDigests[block].y, TransitionHash( worldHash ^ slot ) );
		if ( first.x == 0 && second.x == 0 )
		{
			InterlockedXor( TransitionLateralDigests[block].x, worldHash );
		}
		if ( first.x == 64 && second.x == 64 )
		{
			InterlockedXor( TransitionLateralDigests[block].y, worldHash );
		}
		if ( first.y == 0 && second.y == 0 )
		{
			InterlockedXor( TransitionLateralDigests[block].z, worldHash );
		}
		if ( first.y == 64 && second.y == 64 )
		{
			InterlockedXor( TransitionLateralDigests[block].w, worldHash );
		}
		return;
	}
	if ( TransitionStage == 7 )
	{
		if ( index >= (uint)TransitionBatchSize )
		{
			return;
		}
		TransitionCountResult result;
		result.VertexCount = TransitionBlockCounts[index * 2];
		result.IndexCount = TransitionBlockCounts[index * 2 + 1];
		result.Generation = TransitionRequests[index].Generation;
		result.RequestIndex = index;
		result.ActiveCells = TransitionCellAuditCounts[index].x;
		result.TopologyDigest = TransitionDigests[index].x;
		result.PositionDigest = TransitionDigests[index].y;
		result.FineFaceMismatchCount = TransitionFaceMismatchCounts[index].x;
		result.CoarseFaceMismatchCount = TransitionFaceMismatchCounts[index].y;
		result.MinimumUDigest = TransitionLateralDigests[index].x;
		result.MaximumUDigest = TransitionLateralDigests[index].y;
		result.MinimumVDigest = TransitionLateralDigests[index].z;
		result.MaximumVDigest = TransitionLateralDigests[index].w;
		result.InvalidTableCount = TransitionCellAuditCounts[index].y;
		result.Reserved0 = 0;
		result.Reserved1 = 0;
		TransitionCountResults[index] = result;
		return;
	}
	if ( TransitionStage == 8 )
	{
		if ( index >= (uint)TransitionCellCount * (uint)TransitionBatchSize )
		{
			return;
		}
		uint block = index / (uint)TransitionCellCount;
		uint local = index - block * (uint)TransitionCellCount;
		uint4 cellData = TransitionCells[index];
		uint code = cellData.x;
		TransitionAllocationDescriptor allocation = TransitionAllocations[block];
		if ( allocation.Enabled == 0 || code == 0 || code == 511 )
		{
			return;
		}
		uint cellClass = TransitionCellClass[code];
		uint counts = TransitionCellGeometryCounts[cellClass & 0x7f];
		uint vertexCount = counts >> 4;
		uint triangleCount = counts & 0xf;
		uint vertices[12];
		uint2 cell = TransitionDecodeCell( local );
		for ( uint vertex = 0; vertex < vertexCount; vertex++ )
		{
			uint slot = TransitionEdgeSlot( cell, TransitionVertexData[code * 12 + vertex] );
			if ( slot >= (uint)TransitionEdgeSlotCount )
			{
				return;
			}
			uint groupOffset = TransitionEdgeGroupSums[
				block * (uint)TransitionEdgeGroupCount + slot / 256];
			vertices[vertex] = groupOffset +
				TransitionEdgeVertexIds[block * (uint)TransitionEdgeSlotCount + slot];
			if ( vertices[vertex] >= allocation.VertexCapacity )
			{
				return;
			}
		}
		uint output = TransitionCellGroupSums[
			block * (uint)TransitionCellGroupCount + local / 256] + cellData.z;
		if ( output + triangleCount * 3 > allocation.IndexCapacity )
		{
			return;
		}
		uint topology = (cellClass & 0x7f) * 36;
		bool flip = (cellClass & 0x80) != 0;
		for ( uint triangle = 0; triangle < triangleCount; triangle++ )
		{
			uint table = topology + triangle * 3;
			uint first = TransitionCellVertexIndices[table];
			uint second = TransitionCellVertexIndices[table + 1];
			uint third = TransitionCellVertexIndices[table + 2];
			uint target = allocation.IndexOffset + output + triangle * 3;
			TransitionOutputIndices[target] = vertices[first];
			TransitionOutputIndices[target + 1] = vertices[flip ? third : second];
			TransitionOutputIndices[target + 2] = vertices[flip ? second : third];
		}
	}
	if ( TransitionStage == 9 )
	{
		if ( index >= (uint)TransitionEdgeSlotCount * (uint)TransitionBatchSize )
		{
			return;
		}
		uint block = index / (uint)TransitionEdgeSlotCount;
		uint slot = index - block * (uint)TransitionEdgeSlotCount;
		TransitionAllocationDescriptor allocation = TransitionAllocations[block];
		if ( allocation.Enabled == 0 || TransitionEdgeFlags[index] == 0 )
		{
			return;
		}
		uint groupOffset = TransitionEdgeGroupSums[
			block * (uint)TransitionEdgeGroupCount + slot / 256];
		uint localVertex = groupOffset + TransitionEdgeVertexIds[index];
		if ( localVertex >= allocation.VertexCapacity )
		{
			return;
		}
		uint2 first;
		uint2 second;
		TransitionDecodeEdge( slot, first, second );
		int gradientStep = slot < 8320 ? 1 : 2;
		float firstDensity = TransitionDensity( block, int2( first ) );
		float secondDensity = TransitionDensity( block, int2( second ) );
		float denominator = firstDensity - secondDensity;
		float interpolation = saturate( abs( denominator ) > 0.000001 ?
			firstDensity / denominator : 0.5 );
		TransitionRequest request = TransitionRequests[block];
		float2 point = lerp( float2( first ), float2( second ), interpolation );
		float3 position = TransitionWorldPoint( request, point );
		float3 normal = TransitionSafeNormalize( lerp(
			TransitionGradient( block, int2( first ), gradientStep, request ),
			TransitionGradient( block, int2( second ), gradientStep, request ),
			interpolation ) );
		if ( slot >= 8320 )
		{
			position = TransitionSecondaryPosition( request, position, normal );
		}
		float2 encodedNormal = TransitionEncodeTerrainNormal( normal );
		uint recordId = allocation.Reserved & 0x001fffffu;
		uint generationToken = (allocation.Reserved >> 30u) & 3u;
		uint encodedRecordIdentity = 0x3f800000u | (generationToken << 21u) | recordId;
		TransitionTerrainVertexWords output;
		output.First = uint4( asuint( position.x ), asuint( position.y ),
			asuint( position.z ), encodedRecordIdentity );
		output.Second = uint2( asuint( encodedNormal.x ), asuint( encodedNormal.y ) );
		TransitionOutputVertices[allocation.VertexOffset + localVertex] = output;
	}
}
