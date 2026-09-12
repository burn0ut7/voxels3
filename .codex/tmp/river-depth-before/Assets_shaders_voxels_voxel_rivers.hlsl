// RiverSpatialIndex.SampleWorld mirror. Topology and endpoint elevations come from
// the CPU's canonical drainage graph; the shader never chooses a downstream node.
Texture2D<float4> RiverData < Attribute( "RiverData" ); SrgbRead( false ); >;
float4 RiverGrid < Attribute( "RiverGrid" ); >;
float4 RiverRules < Attribute( "RiverRules" ); >;
float RiverValleySlope < Attribute( "RiverValleySlope" ); >;
uint RiverTextureWidth < Attribute( "RiverTextureWidth" ); >;
uint RiverNodeTexels < Attribute( "RiverNodeTexels" ); >;

float4 LoadRiverTexel( uint index )
{
	return RiverData.Load( int3( index % RiverTextureWidth, index / RiverTextureWidth, 0 ) );
}

// x: conditioned terrain height, y: water surface, zw: downstream XY direction.
float4 SampleVoxelRiver( float2 position, float naturalHeight, float seaLevel )
{
	float4 result = float4( naturalHeight, seaLevel, 0.0, 0.0 );
	int2 bin = (int2)floor( position / RiverRules.x ) - (int2)RiverGrid.xy;
	if ( any( bin < int2( 0, 0 ) ) || any( bin >= (int2)RiverGrid.zw ) )
	{
		return result;
	}
	float bestDistance = 3.402823466e+38;
	float shoulder = clamp( RiverRules.w + max( 0.0, naturalHeight - seaLevel ) * RiverValleySlope,
		RiverRules.w, RiverRules.z );
	uint node = 0u;
	while ( node < RiverNodeTexels )
	{
		float4 bounds = LoadRiverTexel( node );
		float4 metadata = LoadRiverTexel( node + 1u );
		float2 away = max( max( bounds.xy - position, position - bounds.zw ), float2( 0.0, 0.0 ) );
		float separation = length( away );
		bool skip = separation >= shoulder;
		if ( separation > 0.0 )
		{
			// Outside wet support, every candidate is at or above the sea plane.
			// On land, monotonic bank blending gives a conservative height bound.
			float bank = saturate( separation / shoulder );
			float blend = bank * bank * (3.0 - 2.0 * bank);
			float lowerHeight = lerp( seaLevel, naturalHeight, blend );
			skip = skip || result.x <= seaLevel ||
				(naturalHeight > seaLevel && lowerHeight > result.x + 0.0625);
		}
		if ( skip ) { node = (uint)metadata.x; continue; }
		uint count = (uint)metadata.z;
		if ( count == 0u ) { node += 2u; continue; }
		uint offset = (uint)metadata.y;
		for ( uint index = 0u; index < count; index++ )
		{
			float4 start = LoadRiverTexel( offset + index * 2u );
			float4 end = LoadRiverTexel( offset + index * 2u + 1u );
			float2 delta = end.xy - start.xy;
			float lengthSquared = dot( delta, delta );
			float t = 0.0;
			if ( lengthSquared > 0.0 ) t = clamp( dot( position - start.xy, delta ) / lengthSquared, 0.0, 1.0 );
			float distance = length( position - (start.xy + t * delta) );
			float radius = start.w + t * (end.w - start.w);
			if ( distance >= radius + shoulder ) continue;
			float surface = start.z + t * (end.z - start.z);
			float normalized = distance / radius;
			float bed = surface - RiverRules.y * max( 0.0, 1.0 - normalized * normalized );
			float bank = clamp( (distance - radius) / shoulder, 0.0, 1.0 );
			float blend = bank * bank * (3.0 - 2.0 * bank);
			float channel = (1.0 - blend) * bed + blend * naturalHeight;
			result.x = min( result.x, channel );
			if ( normalized < bestDistance )
			{
				bestDistance = normalized;
				result.y = seaLevel;
				result.zw = float2( 0.0, 0.0 );
				if ( distance < radius && lengthSquared > 0.0 ) result.zw = delta / sqrt( lengthSquared );
			}
		}
		node = (uint)metadata.x;
	}
	return result;
}
