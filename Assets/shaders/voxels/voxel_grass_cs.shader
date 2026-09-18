HEADER
{
	Description = "Static grass from published terrain triangles";
}
MODES
{
	Default();
}
FEATURES
{
}
COMMON
{
	#include "system.fxc"
}
CS
{
	#include "common.fxc"
	#include "shaders/voxels/voxel_terrain_normal.hlsl"
	#include "shaders/voxels/voxel_frustum.hlsl"

	struct TerrainVertex
	{
		uint4 First;
		uint3 Second;
	};
	struct IndexedArguments
	{
		uint IndexCount;
		uint InstanceCount;
		uint FirstIndex;
		int BaseVertex;
		uint FirstInstance;
	};
	StructuredBuffer<TerrainVertex> GrassVertices < Attribute( "GrassVertices" ); >;
	StructuredBuffer<uint> GrassIndices < Attribute( "GrassIndices" ); >;
	StructuredBuffer<float4> VisibilityBounds < Attribute( "VisibilityBounds" ); >;
	StructuredBuffer<IndexedArguments> SourceIndirectArguments < Attribute( "SourceIndirectArguments" ); >;
	RWStructuredBuffer<float4> GrassRoots < Attribute( "GrassRoots" ); >;
	RWStructuredBuffer<uint> GrassArguments < Attribute( "GrassArguments" ); >;
	RWStructuredBuffer<uint> GrassStatistics < Attribute( "GrassStatistics" ); >;
	uint GrassCapacity < Attribute( "GrassCapacity" ); >;
	uint GrassFirstSlot < Attribute( "GrassFirstSlot" ); >;
	uint GrassPass < Attribute( "GrassPass" ); >;

	uint GrassHash( uint value )
	{
		value ^= value >> 16;
		value *= 0x7feb352d;
		value ^= value >> 15;
		value *= 0x846ca68b;
		return value ^ (value >> 16);
	}
	float GrassRandom( uint value )
	{
		return (GrassHash( value ) & 0x00ffffff) / 16777216.0;
	}

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 group : SV_GroupID, uint lane : SV_GroupIndex )
	{
		if ( GrassPass == 1 )
		{
			if ( group.x == 0 && lane == 0 )
			{
				uint count = GrassArguments[1];
				GrassStatistics[0] += 1;
				GrassStatistics[1] = min( count, GrassCapacity );
				GrassStatistics[2] = max( GrassStatistics[2], count );
				GrassStatistics[3] += count > GrassCapacity ? 1 : 0;
				GrassArguments[0] = 3;
				GrassArguments[1] = min( GrassArguments[1], GrassCapacity );
			}
			return;
		}
		uint slot = GrassFirstSlot + group.x;
		float4 lower = VisibilityBounds[slot * 2];
		float3 upper = VisibilityBounds[slot * 2 + 1].xyz;
		// Only published regular LOD0 geometry. Bounds include blade height.
		if ( lower.w < 1.0 || lower.w > 2.0 )
		{
			return;
		}
		upper.z += 12.0;
		float3 nearest = clamp( g_vCameraPositionWs, lower.xyz, upper );
		if ( length( nearest - g_vCameraPositionWs ) * 0.0254 >= 20.0 ||
			IsDefinitelyOutsideFrustum( lower.xyz, upper ) )
		{
			return;
		}
		IndexedArguments source = SourceIndirectArguments[slot];
		for ( uint triangle = lane; triangle < source.IndexCount / 3; triangle += 64 )
		{
			uint first = source.FirstIndex + triangle * 3;
			TerrainVertex a = GrassVertices[source.BaseVertex + GrassIndices[first]];
			TerrainVertex b = GrassVertices[source.BaseVertex + GrassIndices[first + 1]];
			TerrainVertex c = GrassVertices[source.BaseVertex + GrassIndices[first + 2]];
			float3 weights = float3( a.Second.z & 255, b.Second.z & 255, c.Second.z & 255 ) / 255.0;
			if ( max( weights.x, max( weights.y, weights.z ) ) < 0.75 )
			{
				continue;
			}
			float3 normal = DecodeTerrainNormal( asfloat( a.Second.xy ) ) +
				DecodeTerrainNormal( asfloat( b.Second.xy ) ) + DecodeTerrainNormal( asfloat( c.Second.xy ) );
			if ( normalize( normal ).z < 0.7 )
			{
				continue;
			}
			float3 p0 = asfloat( a.First.xyz );
			float3 p1 = asfloat( b.First.xyz );
			float3 p2 = asfloat( c.First.xyz );
			float candidates = min( length( cross( p1 - p0, p2 - p0 ) ) / 72.0, 16.0 );
			uint seed = GrassHash( a.First.x ^ GrassHash( b.First.y ) ^ GrassHash( c.First.z ) );
			uint count = (uint)floor( candidates + GrassRandom( seed ) );
			for ( uint blade = 0; blade < count; blade++ )
			{
				uint key = GrassHash( seed + blade * 747796405u );
				float u = sqrt( GrassRandom( key + 1 ) );
				float v = GrassRandom( key + 2 );
				float3 barycentric = float3( 1.0 - u, u * (1.0 - v), u * v );
				if ( dot( barycentric, weights ) < 0.75 )
				{
					continue;
				}
				float3 root = p0 * barycentric.x + p1 * barycentric.y + p2 * barycentric.z;
				float metres = length( root - g_vCameraPositionWs ) * 0.0254;
				float density = lerp( 1.0, 0.3, smoothstep( 6.0, 12.0, metres ) );
				density = lerp( density, 0.08, smoothstep( 12.0, 16.0, metres ) );
				density *= 1.0 - smoothstep( 16.0, 20.0, metres );
				float scale = saturate( (density - GrassRandom( key + 3 )) * 10.0 );
				if ( scale <= 0.0 )
				{
					continue;
				}
				uint index;
				InterlockedAdd( GrassArguments[1], 1, index );
				if ( index < GrassCapacity )
				{
					GrassRoots[index * 2] = float4( root - float3( 0.0, 0.0, 0.3 ), scale );
					GrassRoots[index * 2 + 1] = float4( GrassRandom( key + 4 ) * 6.2831853,
						lerp( 5.0, 12.0, GrassRandom( key + 5 ) ), lerp( 0.35, 0.7, GrassRandom( key + 6 ) ),
						GrassRandom( key + 7 ) );
				}
			}
		}
	}
}
