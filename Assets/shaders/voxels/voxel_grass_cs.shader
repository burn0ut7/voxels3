HEADER
{
	Description = "Grass roots and shared wind from published terrain triangles";
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
	#include "shaders/voxels/voxel_grass_wind.hlsl"
	#include "shaders/voxels/voxel_grass_color.hlsl"
	#include "shaders/voxels/voxel_terrain_normal.hlsl"
	#include "shaders/voxels/voxel_material_blending.hlsl"
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
	uint GrassVertexCount < Attribute( "GrassVertexCount" ); >;
	uint GrassPass < Attribute( "GrassPass" ); >;
	float GrassRangeMeters < Attribute( "GrassRangeMeters" ); >;

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
				GrassArguments[0] = GrassVertexCount;
				GrassArguments[1] = min( GrassArguments[1], GrassCapacity );
			}
			return;
		}
		// Include the entire wind sweep in both region and triangle culling.
		// Wind only lowers tips; derive the vertical envelope from the tallest leaf.
		const float minimumTuftLength = 25.0;
		const float maximumTuftLength = 35.0;
		const float patchHeightVariation = 0.15;
		// Include the VS's longest leaf, maximum lean and leaf half-width.
		const float maximumLeafLength = maximumTuftLength * (1.0 + patchHeightVariation) * 1.05;
		const float horizontalPadding = maximumLeafLength * (0.5 + GRASS_MAX_WIND_BEND) + 0.75;
		const float3 GrassLowerPadding = float3( horizontalPadding, horizontalPadding, 1.0 );
		const float3 GrassUpperPadding = float3( horizontalPadding, horizontalPadding, maximumLeafLength + 1.0 );
		uint slot = GrassFirstSlot + group.x;
		float4 lower = VisibilityBounds[slot * 2];
		float3 upper = VisibilityBounds[slot * 2 + 1].xyz;
		// Active regular terrain at every available LOD; transition filler is tagged >= 100.
		if ( lower.w < 1.0 || lower.w >= 100.0 || GrassRangeMeters <= 0.0 )
		{
			return;
		}
		lower.xyz -= GrassLowerPadding;
		upper += GrassUpperPadding;
		float3 nearest = clamp( g_vCameraPositionWs, lower.xyz, upper );
		if ( length( nearest - g_vCameraPositionWs ) * 0.0254 >= GrassRangeMeters ||
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
			if ( max( weights.x, max( weights.y, weights.z ) ) <= 0.00001 )
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
			float4 materialsA = (float4)((a.Second.z >> uint4( 0u, 8u, 16u, 24u )) & 255u) / 255.0;
			float4 materialsB = (float4)((b.Second.z >> uint4( 0u, 8u, 16u, 24u )) & 255u) / 255.0;
			float4 materialsC = (float4)((c.Second.z >> uint4( 0u, 8u, 16u, 24u )) & 255u) / 255.0;
			// Near regions straddle the view. Reject whole source triangles
			// before sampling roots, preserving every potentially visible leaf.
			float3 triangleLower = min( p0, min( p1, p2 ) ) - GrassLowerPadding;
			float3 triangleUpper = max( p0, max( p1, p2 ) ) + GrassUpperPadding;
			float3 triangleNearest = clamp( g_vCameraPositionWs, triangleLower, triangleUpper );
			if ( length( triangleNearest - g_vCameraPositionWs ) * 0.0254 >= GrassRangeMeters ||
				IsDefinitelyOutsideFrustum( triangleLower, triangleUpper ) )
			{
				continue;
			}
			float areaCandidates = length( cross( p1 - p0, p2 - p0 ) ) / 72.0;
			float candidates = min( areaCandidates, 16.0 );
			// Coarse triangles cover more ground. Reweight their bounded samples
			// instead of silently reducing density again at every LOD boundary.
			float candidateWeight = max( areaCandidates / 16.0, 1.0 );
			uint seed = GrassHash( a.First.x ^ GrassHash( b.First.y ) ^ GrassHash( c.First.z ) );
			uint count = (uint)floor( candidates + GrassRandom( seed ) );
			for ( uint blade = 0; blade < count; blade++ )
			{
				uint key = GrassHash( seed + blade * 747796405u );
				float u = sqrt( GrassRandom( key + 1 ) );
				float v = GrassRandom( key + 2 );
				float3 barycentric = float3( 1.0 - u, u * (1.0 - v), u * v );
				// Match the ground's continuous coverage with fewer full tufts,
				// instead of ending the population at one hard material threshold.
				float3 root = p0 * barycentric.x + p1 * barycentric.y + p2 * barycentric.z;
				float4 mixture = BlendVoxelMaterials( materialsA * barycentric.x + materialsB * barycentric.y +
					materialsC * barycentric.z, root, 0.0 );
				float coverage = smoothstep( 0.1, 0.9, mixture.x );
				if ( GrassRandom( key + 17 ) >= coverage )
				{
					continue;
				}
				float metres = length( root - g_vCameraPositionWs ) * 0.0254;
				float density = lerp( 1.0, 0.3, smoothstep( 6.0, 12.0, metres ) );
				density = lerp( density, 0.08, smoothstep( 12.0, 24.0, metres ) );
				float farDensity = min( 1.0, 1024.0 / max( metres * metres, 1.0 ) );
				density *= farDensity * (1.0 - smoothstep( GrassRangeMeters * 0.75, GrassRangeMeters, metres ));
				// Thin the distant population without shrinking every remaining tuft.
				float scale = saturate( (density - GrassRandom( key + 3 ) / candidateWeight) * 10.0 / farDensity );
				if ( scale <= 0.0 )
				{
					continue;
				}
				uint index;
				InterlockedAdd( GrassArguments[1], 1, index );
				if ( index < GrassCapacity )
				{
					// Evaluate the field once per tuft, shared by all 30 vertices in
					// both passes. Pack variation and wind into the existing record.
					uint variation = (GrassHash( key + 7 ) >> 8) & 65535u;
					float wind = EvaluateGrassWind( root.xy, g_flTime );
					uint packedVariationWind = variation | (f32tof16( wind ) << 16);
					// Color is shared by all leaves, without adding another root channel.
					uint angle = (GrassHash( key + 4 ) >> 8) & 65535u;
					// Bias color to [1,2] so the packed float is never denormal or NaN.
					float patchTone = EvaluateGrassColor( root.xy );
					uint packedAngleColor = angle | (f32tof16( 1.0 + patchTone ) << 16);
					// Greener patches grow slightly taller; warmer patches stay shorter.
					// Keep small, stable differences between neighboring tufts as well.
					float tuftLength = lerp( minimumTuftLength, maximumTuftLength, GrassRandom( key + 5 ) ) *
						lerp( 1.0 + patchHeightVariation, 1.0 - patchHeightVariation, patchTone );
					GrassRoots[index * 2] = float4( root - float3( 0.0, 0.0, 0.3 ), scale );
					GrassRoots[index * 2 + 1] = float4( asfloat( packedAngleColor ),
						tuftLength, lerp( 0.35, 0.65, GrassRandom( key + 6 ) ),
						asfloat( packedVariationWind ) );
				}
			}
		}
	}
}
