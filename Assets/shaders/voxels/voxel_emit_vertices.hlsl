#if !VOXEL_PACKED_RECORD_IDENTITY
	#error Regular terrain vertex emission requires the packed record-identity layout.
#endif

struct TerrainRequest
{
	float4 OriginAndCellSize;
	float4 Terrain;
	int CellsPerAxis;
	uint Generation;
	uint RequestIndex;
	uint Reserved0;
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
StructuredBuffer<uint> EdgeFlags < Attribute( "EdgeFlags" ); >;
StructuredBuffer<uint> EdgeVertexIds < Attribute( "EdgeVertexIds" ); >;
StructuredBuffer<uint> EdgeGroupSums < Attribute( "EdgeGroupSums" ); >;
StructuredBuffer<AllocationDescriptor> Allocations < Attribute( "Allocations" ); >;
RWStructuredBuffer<TerrainVertexWords> OutputVertices < Attribute( "OutputVertices" ); >;

int SampleSize < Attribute( "SampleSize" ); >;
int HaloSize < Attribute( "HaloSize" ); >;
int HaloSampleCount < Attribute( "HaloSampleCount" ); >;
int EdgeSlotCount < Attribute( "EdgeSlotCount" ); >;
int EdgeGroupCount < Attribute( "EdgeGroupCount" ); >;
int BatchSize < Attribute( "BatchSize" ); >;

uint3 DecodePoint( uint index, uint size )
{
	uint plane = size * size;
	uint z = index / plane;
	uint remainder = index - z * plane;
	uint y = remainder / size;
	return uint3( remainder - y * size, y, z );
}

uint HaloIndex( int3 point )
{
	int3 halo = point + 1;
	return halo.x + HaloSize * (halo.y + HaloSize * halo.z);
}

float RawDensity( uint block, int3 point )
{
	return DensitySamples[block * (uint)HaloSampleCount + HaloIndex( point )];
}

float Density( uint block, int3 point )
{
	float value = RawDensity( block, point );
	return abs( value ) < 0.000001 ? (value < 0 ? -0.000001 : 0.000001) : value;
}

float3 Gradient( uint block, int3 point )
{
	return float3(
		RawDensity( block, point + int3( 1, 0, 0 ) ) - RawDensity( block, point - int3( 1, 0, 0 ) ),
		RawDensity( block, point + int3( 0, 1, 0 ) ) - RawDensity( block, point - int3( 0, 1, 0 ) ),
		RawDensity( block, point + int3( 0, 0, 1 ) ) - RawDensity( block, point - int3( 0, 0, 1 ) ) );
}

float3 SafeNormalize( float3 value )
{
	float lengthSquared = dot( value, value );
	return lengthSquared > 1e-12 ? value * rsqrt( lengthSquared ) : float3( 0, 0, 1 );
}

float2 EncodeTerrainNormal( float3 normal )
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
	uint totalEdgeSlots = (uint)EdgeSlotCount * (uint)BatchSize;
	if ( index >= totalEdgeSlots )
	{
		return;
	}

	uint block = index / (uint)EdgeSlotCount;
	uint slot = index - block * (uint)EdgeSlotCount;
	AllocationDescriptor allocation = Allocations[block];
	if ( allocation.Enabled == 0 || EdgeFlags[index] == 0 )
	{
		return;
	}

	uint groupOffset = EdgeGroupSums[block * (uint)EdgeGroupCount + slot / 256];
	uint localVertex = groupOffset + EdgeVertexIds[index];
	if ( localVertex >= allocation.VertexCapacity )
	{
		return;
	}

	uint sample = slot / 3;
	uint axis = slot - sample * 3;
	uint3 firstPoint = DecodePoint( sample, SampleSize );
	uint3 secondPoint = firstPoint;
	if ( axis == 0 )
	{
		secondPoint.x++;
	}
	else if ( axis == 1 )
	{
		secondPoint.y++;
	}
	else
	{
		secondPoint.z++;
	}

	float firstDensity = Density( block, int3( firstPoint ) );
	float secondDensity = Density( block, int3( secondPoint ) );
	float denominator = firstDensity - secondDensity;
	float interpolation = saturate(
		abs( denominator ) > 0.000001 ? firstDensity / denominator : 0.5 );
	TerrainRequest request = Requests[block];
	float3 outputPosition = request.OriginAndCellSize.xyz +
		lerp( float3( firstPoint ), float3( secondPoint ), interpolation ) *
		request.OriginAndCellSize.w;
	float3 outputNormal = SafeNormalize( lerp(
		Gradient( block, int3( firstPoint ) ),
		Gradient( block, int3( secondPoint ) ),
		interpolation ) );
	float2 encodedNormal = EncodeTerrainNormal( outputNormal );

	TerrainVertexWords output;
	output.First = uint4(
		asuint( outputPosition.x ),
		asuint( outputPosition.y ),
		asuint( outputPosition.z ),
		asuint( (float)allocation.Reserved ) );
	output.Second = uint2( asuint( encodedNormal.x ), asuint( encodedNormal.y ) );
	OutputVertices[allocation.VertexOffset + localVertex] = output;
}
