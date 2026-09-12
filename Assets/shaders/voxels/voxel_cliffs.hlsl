// GPU mirror of TerrainCliffs. Positive values remove solid terrain.
float SampleVoxelCliffs( float3 position, float4 terrain, float4 scales, float seaLevel, float mountainWeight, float density )
{
	float limit = scales.w;
	float mask = (mountainWeight - 0.75) * limit;
	float value = density;
	float verticalBound = min( position.z - seaLevel - limit * 0.15, seaLevel + limit * 0.71 - position.z );
	if ( min( mask, verticalBound ) <= density )
	{
		return value;
	}
	float scale = scales.z * 1.3;
	int2 origin = (int2)floor( position.xy / scale );
	uint seed = (uint)(int)terrain.x;
	[unroll]
	for ( int y = -1; y <= 1; y++ )
	{
		[unroll]
		for ( int x = -1; x <= 1; x++ )
		{
			int2 cell = origin + int2( x, y );
			float2 nearCenter = max( abs( position.xy / scale - ((float2)cell + 0.5) ) - 0.18, 0.0 );
			float upper = (1.0 - sqrt( dot( nearCenter, nearCenter ) ) / 0.153) * (0.063 * scale) + 65.0;
			if ( upper <= value )
			{
				continue;
			}
			uint a = VoxelHash2D( cell.x, cell.y, seed ^ 0xE19B01AAu );
			uint b = VoxelHash2D( cell.x, cell.y, seed ^ 0xC734D891u );
			if ( (a >> 24) >= 205u )
			{
				float2 axis = float2( (b & 255u) - 127.5, ((b >> 8) & 255u) - 127.5 );
				axis /= sqrt( dot( axis, axis ) );
				float2 center = ((float2)cell + 0.5 + (float2( a & 255u, (a >> 8) & 255u ) / 255.0 - 0.5) * 0.36) * scale;
				float radiusX = scale * (0.099 + 0.054 * ((a >> 16) & 255u) / 255.0);
				float radiusY = scale * 0.063;
				float zCenter = seaLevel + limit * (0.28 + 0.30 * ((b >> 16) & 255u) / 255.0);
				float halfHeight = limit * (0.078 + 0.052 * (b >> 24) / 255.0);
				float2 delta = position.xy - center;
				float u = abs( delta.x * axis.x + delta.y * axis.y );
				float v = abs( -delta.x * axis.y + delta.y * axis.x );
				float vertical = halfHeight - abs( position.z - zCenter );
				float recess = 64.0 * clamp( vertical / halfHeight, 0.0, 1.0 );
				float horizontal = (1.0 - sqrt( u * u / (radiusX * radiusX) + v * v / (radiusY * radiusY) )) * min( radiusX, radiusY ) + recess;
				value = max( value, min( horizontal, vertical ) );
			}
		}
	}
	return max( density, min( value, mask ) );
}


