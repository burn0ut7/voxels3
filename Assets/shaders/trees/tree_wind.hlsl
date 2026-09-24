#ifndef TREE_LAB_WIND_HLSL
#define TREE_LAB_WIND_HLSL

// Coordinates and pivots are inches. TreeWind is a world-space direction and
// dimensionless ambient strength; weather ownership is a subsequent slice.
float4 g_vTreeWind < Attribute( "TreeWind" ); Default4( 0.8, 0.6, 0.0, 1.0 ); >;
float g_flTreeHeight < Default( 600.0 ); >;
float g_flTreeFlex < Default( 0.018 ); >;
float g_flTreeBranchFlex < Default( 0.028 ); >;
CreateInputTexture2D( TextureMotion, Linear, 16, "", "_motion", "Tree Motion", Default4( 0.5, 0.5, 0.5, 0.0 ) );
Texture2D g_tTreeMotion < Channel( RGBA, Box( TextureMotion ), Linear ); OutputFormat( RGBA16161616 ); SrgbRead( false ); >;
SamplerState TreeMotionSampler < Filter( POINT ); AddressU( CLAMP ); AddressV( CLAMP ); >;

float3 TreeRotate( float3 vector, float3 axis, float angle )
{
	float sine;
	float cosine;
	sincos( angle, sine, cosine );
	return vector * cosine + cross( axis, vector ) * sine
		+ axis * dot( axis, vector ) * (1.0 - cosine);
}

float3 TreeLocalWind( float3x4 transform )
{
	float3 x = normalize( float3( transform[0][0], transform[1][0], transform[2][0] ) );
	float3 y = normalize( float3( transform[0][1], transform[1][1], transform[2][1] ) );
	float3 z = normalize( float3( transform[0][2], transform[1][2], transform[2][2] ) );
	float3 wind = g_vTreeWind.xyz;
	wind /= max( length( wind ), 0.0001 );
	return float3( dot( wind, x ), dot( wind, y ), dot( wind, z ) );
}

float TreeGust( float3 origin, float phase )
{
	float2 direction = g_vTreeWind.xy / max( length( g_vTreeWind.xy ), 0.0001 );
	float front = 0.5 + 0.5 * sin( (dot( origin.xy * 0.0254, direction )
		- g_flTime * 4.0) * (6.2831853 / 20.0) );
	float sway = sin( g_flTime * 1.13 + phase ) * 0.12
		+ sin( g_flTime * 0.73 + phase * 0.61 ) * 0.08;
	return max( 0.0, 0.22 + 0.64 * front * front + sway )
		* clamp( g_vTreeWind.w, 0.0, 2.0 ) * step( 0.0001, length( g_vTreeWind.xy ) );
}

float4 TreeMotionEntry( float2 uv )
{
	float4 entry = g_tTreeMotion.SampleLevel( TreeMotionSampler, uv, 0.0 );
	entry.xyz = (entry.xyz - 0.5) * (g_flTreeHeight * 4.0);
	return entry;
}

float3 TreeBranchPivot( float identity )
{
	uint width;
	uint height;
	g_tTreeMotion.GetDimensions( width, height );
	return TreeMotionEntry( float2( (floor( identity * 255.0 + 0.5 ) + 0.5) / float( width ),
		1.0 - 0.5 / float( height ) ) ).xyz;
}

float3 TreeDecodeOctahedron( float2 packed )
{
	float2 xy = packed * 2.0 - 1.0;
	float3 normal = float3( xy, 1.0 - abs( xy.x ) - abs( xy.y ) );
	if ( normal.z < 0.0 )
	{
		normal.xy = (1.0 - abs( normal.yx )) * float2( normal.x >= 0.0 ? 1.0 : -1.0, normal.y >= 0.0 ? 1.0 : -1.0 );
	}
	return normalize( normal );
}

void TreeMainMotion( inout float3 position, inout float3 normal,
	inout float3 tangent, float3 wind, float gust )
{
	// Root and underground vertices remain exactly stationary, including calm.
	float height = max( g_flTreeHeight, 1.0 );
	float h = saturate( position.z / height );
	float angle = g_flTreeFlex * gust * h * h;
	float derivative = position.z > 0.0 && position.z < height
		? g_flTreeFlex * gust * 2.0 * h / height : 0.0;
	float3 axis = float3( -wind.y, wind.x, 0.0 );
	axis /= max( length( axis ), 0.0001 );
	// Inverse-transpose of the height-dependent bend, not just its rotation.
	float3 gradient = cross( axis, position ) * derivative;
	normal.z -= dot( gradient, normal ) / max( 1.0 + gradient.z, 0.5 );
	tangent += gradient * tangent.z;
	position = TreeRotate( position, axis, angle );
	normal = normalize( TreeRotate( normal, axis, angle ) );
	tangent = normalize( TreeRotate( tangent, axis, angle ) );
}

// Inverse of the bounded root bend for distant camera-ray reconstruction.
// Three fixed-point steps use a fourth-order rotation polynomial; the default
// flex and strength clamp keep the maximum angle below 0.04 radians.
float3 TreeInverseMainMotion( float3 position, float3 wind, float gust )
{
	float3 axis = float3( -wind.y, wind.x, 0.0 );
	axis /= max( length( axis ), 0.0001 );
	float3 rest = position;
	for ( int iteration = 0; iteration < 3; iteration++ )
	{
		float h = saturate( rest.z / max( g_flTreeHeight, 1.0 ) );
		float angle = -g_flTreeFlex * gust * h * h;
		float squared = angle * angle;
		float sine = angle * (1.0 - squared / 6.0);
		float cosine = 1.0 - squared * 0.5 + squared * squared / 24.0;
		rest = position * cosine + cross( axis, position ) * sine
			+ axis * dot( axis, position ) * (1.0 - cosine);
	}
	return rest;
}

void TreeBranchMotion( inout float3 position, inout float3 normal,
	inout float3 tangent, float3 pivot, float weight, float3 wind,
	float gust, float phase )
{
	float3 axis = float3( -wind.y, wind.x, 0.0 );
	axis /= max( length( axis ), 0.0001 );
	float response = 0.66 + 0.23 * sin( g_flTime * 1.71 + phase )
		+ 0.11 * sin( g_flTime * 2.37 + phase * 0.73 );
	float angle = g_flTreeBranchFlex * gust * response * weight * weight;
	position = pivot + TreeRotate( position - pivot, axis, angle );
	normal = normalize( TreeRotate( normal, axis, angle ) );
	tangent = normalize( TreeRotate( tangent, axis, angle ) );
}

#endif
