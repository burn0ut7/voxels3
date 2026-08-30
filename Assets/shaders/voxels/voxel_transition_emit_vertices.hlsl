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

struct TerrainVertexWords
{
	uint4 First;
	uint2 Second;
};

StructuredBuffer<TerrainRequest> Requests < Attribute( "Requests" ); >;
StructuredBuffer<float> DensitySamples < Attribute( "DensitySamples" ); >;
StructuredBuffer<uint3> Cells < Attribute( "Cells" ); >;
StructuredBuffer<uint> VertexOffsets < Attribute( "EdgeVertexIds" ); >;
StructuredBuffer<uint> VertexGroupSums < Attribute( "EdgeGroupSums" ); >;
StructuredBuffer<AllocationDescriptor> Allocations < Attribute( "Allocations" ); >;
RWStructuredBuffer<TerrainVertexWords> OutputVertices < Attribute( "OutputVertices" ); >;

int ChunkSize < Attribute( "ChunkSize" ); >;
int TransitionSampleSize < Attribute( "TransitionSampleSize" ); >;
int TransitionSampleCount < Attribute( "TransitionSampleCount" ); >;
int TransitionSampleStride < Attribute( "TransitionSampleStride" ); >;
int TransitionCellCount < Attribute( "TransitionCellCount" ); >;
int TransitionGroupCount < Attribute( "TransitionGroupCount" ); >;
int BatchSize < Attribute( "BatchSize" ); >;

uint2 TransitionDecodeCell( uint index )
{
	return uint2( index % (uint)ChunkSize, index / (uint)ChunkSize );
}

uint2 TransitionSampleGrid( uint sample, uint2 cell )
{
	uint fullSample = sample;
	if ( sample == 9 ) fullSample = 0;
	else if ( sample == 10 ) fullSample = 2;
	else if ( sample == 11 ) fullSample = 6;
	else if ( sample == 12 ) fullSample = 8;
	return cell * 2 + uint2( fullSample % 3, fullSample / 3 );
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

float TransitionDensity( uint block, uint2 sample )
{
	float value = DensitySamples[
		block * (uint)TransitionSampleStride + (uint)TransitionSampleCount +
		sample.x + sample.y * (uint)TransitionSampleSize];
	return abs( value ) < 0.000001 ? (value < 0 ? -0.000001 : 0.000001) : value;
}

float TransitionLayerDensity( uint block, uint layer, uint2 sample )
{
	return DensitySamples[
		block * (uint)TransitionSampleStride + layer * (uint)TransitionSampleCount +
		sample.x + sample.y * (uint)TransitionSampleSize];
}

float3 TransitionGradient( uint block, uint face, uint2 sample )
{
	uint2 lowerU = uint2( sample.x > 0 ? sample.x - 1 : 0, sample.y );
	uint2 upperU = uint2( min( sample.x + 1, (uint)TransitionSampleSize - 1 ), sample.y );
	uint2 lowerV = uint2( sample.x, sample.y > 0 ? sample.y - 1 : 0 );
	uint2 upperV = uint2( sample.x, min( sample.y + 1, (uint)TransitionSampleSize - 1 ) );
	float du = TransitionLayerDensity( block, 1, upperU ) -
		TransitionLayerDensity( block, 1, lowerU );
	float dv = TransitionLayerDensity( block, 1, upperV ) -
		TransitionLayerDensity( block, 1, lowerV );
	float dn = TransitionLayerDensity( block, 2, sample ) -
		TransitionLayerDensity( block, 0, sample );
	float3 uBasis = face == 0 ? float3( 0, -1, 0 ) :
		face == 1 ? float3( 0, 1, 0 ) :
		face == 2 ? float3( 1, 0, 0 ) :
		face == 3 ? float3( -1, 0, 0 ) :
		float3( 1, 0, 0 );
	float3 vBasis = face == 4 ? float3( 0, -1, 0 ) :
		face == 5 ? float3( 0, 1, 0 ) :
		float3( 0, 0, 1 );
	float3 normalBasis = face == 0 ? float3( -1, 0, 0 ) :
		face == 1 ? float3( 1, 0, 0 ) :
		face == 2 ? float3( 0, -1, 0 ) :
		face == 3 ? float3( 0, 1, 0 ) :
		face == 4 ? float3( 0, 0, -1 ) :
		float3( 0, 0, 1 );
	return uBasis * du + vBasis * dv + normalBasis * dn;
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

	uint code = Cells[block * (uint)TransitionCellCount + localCell].x;
	uint cellClass = TransitionCellClass[code];
	uint vertexCount = TransitionCellGeometryCounts[cellClass & 0x7f] >> 4;
	uint groupOffset = VertexGroupSums[
		block * (uint)TransitionGroupCount + localCell / 256];
	uint cellOffset = VertexOffsets[block * (uint)TransitionCellCount + localCell];
	uint2 cell = TransitionDecodeCell( localCell );
	TerrainRequest request = Requests[block];
	uint face = (request.PackedIdentity >> 8) & 0xff;
	float fineSpacing = request.OriginAndCellSize.w * 0.5;
	for ( uint localVertex = 0; localVertex < vertexCount; localVertex++ )
	{
		uint outputVertex = groupOffset + cellOffset + localVertex;
		if ( outputVertex >= allocation.VertexCapacity ) break;
		uint edge = TransitionVertexData[code * 12 + localVertex] & 0xff;
		uint firstSample = (edge >> 4) & 0xf;
		uint secondSample = edge & 0xf;
		uint2 firstGrid = TransitionSampleGrid( firstSample, cell );
		uint2 secondGrid = TransitionSampleGrid( secondSample, cell );
		float3 firstPosition = TransitionFacePoint(
			request, face, firstGrid.x * fineSpacing, firstGrid.y * fineSpacing );
		float3 secondPosition = TransitionFacePoint(
			request, face, secondGrid.x * fineSpacing, secondGrid.y * fineSpacing );
		float firstDensity = TransitionDensity( block, firstGrid );
		float secondDensity = TransitionDensity( block, secondGrid );
		float denominator = firstDensity - secondDensity;
		float interpolation = saturate(
			abs( denominator ) > 0.000001 ? firstDensity / denominator : 0.5 );
		float3 firstGradient = TransitionGradient( block, face, firstGrid );
		float3 secondGradient = TransitionGradient( block, face, secondGrid );
		float3 outputNormal = TransitionSafeNormalize(
			lerp( firstGradient, secondGradient, interpolation ) );
		float3 outputPosition = lerp( firstPosition, secondPosition, interpolation );
		bool lowResolutionSide = firstSample >= 9 && secondSample >= 9;
		float2 encodedNormal = TransitionEncodeTerrainNormal( outputNormal );

		TerrainVertexWords output;
		output.First = uint4(
			asuint( outputPosition.x ), asuint( outputPosition.y ), asuint( outputPosition.z ),
			asuint( (float)allocation.Reserved + (lowResolutionSide ? 0.5 : 0.0) ) );
		output.Second = uint2( asuint( encodedNormal.x ), asuint( encodedNormal.y ) );
		OutputVertices[allocation.VertexOffset + outputVertex] = output;
	}
}
