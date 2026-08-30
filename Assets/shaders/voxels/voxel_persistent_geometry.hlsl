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

StructuredBuffer<TerrainRequest> Requests < Attribute( "Requests" ); >;
RWStructuredBuffer<float> DensitySamples < Attribute( "DensitySamples" ); >;
RWStructuredBuffer<uint3> Cells < Attribute( "Cells" ); >;
RWStructuredBuffer<uint> EdgeFlags < Attribute( "EdgeFlags" ); >;
RWStructuredBuffer<uint> EdgeVertexIds < Attribute( "EdgeVertexIds" ); >;
RWStructuredBuffer<uint> EdgeGroupSums < Attribute( "EdgeGroupSums" ); >;
RWStructuredBuffer<uint> CellGroupSums < Attribute( "CellGroupSums" ); >;
RWStructuredBuffer<uint> BlockCounts < Attribute( "BlockCounts" ); >;
RWStructuredBuffer<uint> ActiveCellCounts < Attribute( "ActiveCellCounts" ); >;
RWStructuredBuffer<uint2> Digests < Attribute( "Digests" ); >;
RWStructuredBuffer<CountResult> CountResults < Attribute( "CountResults" ); >;
StructuredBuffer<AllocationDescriptor> Allocations < Attribute( "Allocations" ); >;

int PersistentStage < Attribute( "PersistentStage" ); >;
int ChunkSize < Attribute( "ChunkSize" ); >;
int SampleSize < Attribute( "SampleSize" ); >;
int HaloSize < Attribute( "HaloSize" ); >;
int HaloSampleCount < Attribute( "HaloSampleCount" ); >;
int CellCount < Attribute( "CellCount" ); >;
int EdgeSlotCount < Attribute( "EdgeSlotCount" ); >;
int EdgeGroupCount < Attribute( "EdgeGroupCount" ); >;
int CellGroupCount < Attribute( "CellGroupCount" ); >;
int BatchSize < Attribute( "BatchSize" ); >;

static const uint3 PersistentCorners[8] = {
	uint3(0,0,0),uint3(1,0,0),uint3(0,1,0),uint3(1,1,0),
	uint3(0,0,1),uint3(1,0,1),uint3(0,1,1),uint3(1,1,1) };
groupshared uint PersistentScan[256];

uint3 PersistentDecode3D( uint index, uint size )
{
	uint plane=size*size,z=index/plane,remainder=index-z*plane,y=remainder/size;
	return uint3(remainder-y*size,y,z);
}
uint PersistentHaloIndex( int3 point )
{
	int3 halo=point+1;
	return halo.x+HaloSize*(halo.y+HaloSize*halo.z);
}
float PersistentRawDensity( uint block, int3 point )
{
	return DensitySamples[block*(uint)HaloSampleCount+PersistentHaloIndex(point)];
}
float PersistentDensity( uint block, int3 point )
{
	float value=PersistentRawDensity(block,point);
	return abs(value)<0.000001?(value<0?-0.000001:0.000001):value;
}
uint PersistentCase( uint block, int3 cell )
{
	uint code=0;
	[unroll] for(uint corner=0;corner<8;corner++)
		if(PersistentDensity(block,cell+int3(PersistentCorners[corner]))<0)code|=1u<<corner;
	return code;
}
uint PersistentSampleIndex( uint3 point )
{
	return point.x+SampleSize*(point.y+SampleSize*point.z);
}
uint PersistentEdgeSlot( uint3 cell, uint data )
{
	uint code=data&0xff,a=(code>>4)&0xf,b=code&0xf;
	uint3 first=cell+PersistentCorners[a],second=cell+PersistentCorners[b];
	uint3 delta=uint3(abs(int3(first)-int3(second)));
	uint axis=delta.x!=0?0:delta.y!=0?1:2;
	return PersistentSampleIndex(min(first,second))*3+axis;
}
uint PersistentHash( uint value )
{
	value^=value>>16;value*=0x7feb352d;value^=value>>15;value*=0x846ca68b;value^=value>>16;return value;
}
float3 PersistentGradient( uint block, int3 point )
{
	return float3(
		PersistentRawDensity(block,point+int3(1,0,0))-PersistentRawDensity(block,point-int3(1,0,0)),
		PersistentRawDensity(block,point+int3(0,1,0))-PersistentRawDensity(block,point-int3(0,1,0)),
		PersistentRawDensity(block,point+int3(0,0,1))-PersistentRawDensity(block,point-int3(0,0,1)) );
}
float3 PersistentSafeNormalize( float3 value )
{
	float lengthSquared=dot(value,value);
	return lengthSquared>1e-12?value*rsqrt(lengthSquared):float3(0,0,1);
}

void PersistentExclusiveScan( uint lane, uint value )
{
	PersistentScan[lane]=value;
	GroupMemoryBarrierWithGroupSync();
	for(uint step=1;step<256;step<<=1)
	{
		uint index=(lane+1)*step*2-1;
		if(index<256)PersistentScan[index]+=PersistentScan[index-step];
		GroupMemoryBarrierWithGroupSync();
	}
	if(lane==0)PersistentScan[255]=0;
	GroupMemoryBarrierWithGroupSync();
	for(uint step=128;step>0;step>>=1)
	{
		uint index=(lane+1)*step*2-1;
		if(index<256){uint saved=PersistentScan[index-step];PersistentScan[index-step]=PersistentScan[index];PersistentScan[index]+=saved;}
		GroupMemoryBarrierWithGroupSync();
	}
}

[numthreads(256,1,1)]
void MainCs( uint3 dispatchId : SV_DispatchThreadID, uint3 groupId : SV_GroupID, uint lane : SV_GroupIndex )
{
	uint index=dispatchId.x;
	if(PersistentStage==0)
	{
		if(index<(uint)CellCount*(uint)BatchSize)Cells[index]=uint3(0,0,0);
		if(index<(uint)EdgeSlotCount*(uint)BatchSize){EdgeFlags[index]=0;EdgeVertexIds[index]=0;}
		if(index<(uint)BatchSize){ActiveCellCounts[index]=0;Digests[index]=uint2(0,0);}
		return;
	}
	if(PersistentStage==1)
	{
		uint total=(uint)HaloSampleCount*(uint)BatchSize;if(index>=total)return;
		uint block=index/(uint)HaloSampleCount,local=index-block*(uint)HaloSampleCount;
		uint3 halo=PersistentDecode3D(local,HaloSize);TerrainRequest request=Requests[block];
		int3 origin=(int3)round(request.OriginAndCellSize.xyz/request.OriginAndCellSize.w);
		DensitySamples[index]=SampleVoxelSdf(origin+int3(halo)-1,request.OriginAndCellSize.w,(int)request.Terrain.x,request.Terrain.y,request.Terrain.z,request.Terrain.w);
		return;
	}
	if(PersistentStage==2)
	{
		if(index>=(uint)CellCount*(uint)BatchSize)return;uint block=index/(uint)CellCount,local=index-block*(uint)CellCount;uint3 cell=PersistentDecode3D(local,ChunkSize);uint code=PersistentCase(block,int3(cell));Cells[index].x=code;
		if(code==0||code==255)return;uint cellClass=RegularCellClass[code],counts=RegularCellGeometryCounts[cellClass],vertexCount=counts>>4,indexCount=(counts&0xf)*3;Cells[index].y=indexCount;InterlockedAdd(ActiveCellCounts[block],1);
		uint metadataDigest=0;for(uint vertex=0;vertex<vertexCount;vertex++){uint data=RegularVertexData[code*12+vertex];uint reuseDirection=data>>12,reusedVertexSlot=(data>>8)&0xf;metadataDigest^=PersistentHash((reuseDirection<<4)|reusedVertexSlot|(vertex<<8));InterlockedOr(EdgeFlags[block*(uint)EdgeSlotCount+PersistentEdgeSlot(cell,data)],1);}
		InterlockedXor(Digests[block].x,PersistentHash(local^(code<<16)^indexCount)^metadataDigest);return;
	}
	if(PersistentStage==3)
	{
		uint block=groupId.x/(uint)EdgeGroupCount,edgeGroup=groupId.x-block*(uint)EdgeGroupCount;if(block>=(uint)BatchSize)return;uint local=edgeGroup*256+lane,address=block*(uint)EdgeSlotCount+local;uint value=local<(uint)EdgeSlotCount?EdgeFlags[address]:0;PersistentExclusiveScan(lane,value);uint total=value+PersistentScan[lane];if(local<(uint)EdgeSlotCount)EdgeVertexIds[address]=PersistentScan[lane];if(lane==255)EdgeGroupSums[block*(uint)EdgeGroupCount+edgeGroup]=total;return;
	}
	if(PersistentStage==4)
	{
		uint block=groupId.x/(uint)CellGroupCount,cellGroup=groupId.x-block*(uint)CellGroupCount;if(block>=(uint)BatchSize)return;uint local=cellGroup*256+lane,address=block*(uint)CellCount+local;uint value=local<(uint)CellCount?Cells[address].y:0;PersistentExclusiveScan(lane,value);uint total=value+PersistentScan[lane];if(local<(uint)CellCount)Cells[address].z=PersistentScan[lane];if(lane==255)CellGroupSums[block*(uint)CellGroupCount+cellGroup]=total;return;
	}
	if(PersistentStage==5)
	{
		uint block=groupId.x;if(block>=(uint)BatchSize||lane!=0)return;uint vertices=0,indices=0;for(uint group=0;group<(uint)EdgeGroupCount;group++){uint address=block*(uint)EdgeGroupCount+group,count=EdgeGroupSums[address];EdgeGroupSums[address]=vertices;vertices+=count;}for(uint group=0;group<(uint)CellGroupCount;group++){uint address=block*(uint)CellGroupCount+group,count=CellGroupSums[address];CellGroupSums[address]=indices;indices+=count;}BlockCounts[block*2]=vertices;BlockCounts[block*2+1]=indices;return;
	}
	if(PersistentStage==6)
	{
		if(index>=(uint)EdgeSlotCount*(uint)BatchSize||EdgeFlags[index]==0)return;uint block=index/(uint)EdgeSlotCount,slot=index-block*(uint)EdgeSlotCount,sample=slot/3,axis=slot-sample*3;uint3 a=PersistentDecode3D(sample,SampleSize),b=a;if(axis==0)b.x++;else if(axis==1)b.y++;else b.z++;float da=PersistentDensity(block,int3(a)),db=PersistentDensity(block,int3(b)),denominator=da-db,t=saturate(abs(denominator)>0.000001?da/denominator:0.5);TerrainRequest request=Requests[block];float3 world=request.OriginAndCellSize.xyz+lerp(float3(a),float3(b),t)*request.OriginAndCellSize.w;InterlockedXor(Digests[block].y,PersistentHash(asuint(world.x)^PersistentHash(asuint(world.y))^PersistentHash(asuint(world.z))^slot));return;
	}
	if(PersistentStage==7)
	{
		if(index>=(uint)BatchSize)return;CountResult result;result.VertexCount=BlockCounts[index*2];result.IndexCount=BlockCounts[index*2+1];result.Generation=Requests[index].Generation;result.RequestIndex=index;result.ActiveCells=ActiveCellCounts[index];result.TopologyDigest=Digests[index].x;result.PositionDigest=Digests[index].y;result.Reserved=0;CountResults[index]=result;return;
	}
}
