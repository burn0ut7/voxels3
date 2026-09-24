#include "trees/tree_wind.hlsl"
StaticCombo( S_TREE_MOTION, F_TREE_MOTION, Sys( ALL ) );

void TreeWoodMotion( VertexInput i, inout PixelInput o )
{
	#if S_TREE_MOTION
		float3 normal;
		float4 tangentAndSign;
		VS_DecodeObjectSpaceNormalAndTangent( i, normal, tangentAndSign );
		float3 tangent = tangentAndSign.xyz;
		float3 position = i.vPositionOs.xyz;
		float3x4 transform = GetTransformMatrix( i.nInstanceTransformID );
		float3 origin = mul( transform, float4( 0.0, 0.0, 0.0, 1.0 ) );
		float3 wind = TreeLocalWind( transform );
		float phase = dot( origin.xy, float2( 0.013, 0.021 ) );
		float gust = TreeGust( origin, phase );
		TreeBranchMotion( position, normal, tangent, TreeBranchPivot( i.vColor.r ),
			i.vColor.g, wind, gust, i.vColor.r * 157.0 + phase );
		TreeMainMotion( position, normal, tangent, wind, gust );
		o.vPositionWs = mul( transform, float4( position, 1.0 ) );
		o.vPositionPs = Position3WsToPs( o.vPositionWs );
		o.vNormalWs = normalize( mul( (float3x3)transform, normal ) );
		o.vTangentUWs = normalize( mul( (float3x3)transform, tangent ) );
		o.vTangentVWs = normalize( cross( o.vNormalWs, o.vTangentUWs ) * tangentAndSign.w );
	#endif
}
