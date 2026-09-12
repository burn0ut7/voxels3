	float3 DecodeTerrainNormal( float2 encoded )
	{
		float3 normal = float3(
			encoded.x,
			encoded.y,
			1.0 - abs( encoded.x ) - abs( encoded.y ) );
		if ( normal.z < 0.0 )
		{
			float2 signValue = float2(
				normal.x >= 0.0 ? 1.0 : -1.0,
				normal.y >= 0.0 ? 1.0 : -1.0 );
			normal.xy = (1.0 - abs( normal.yx )) * signValue;
		}
		return normalize( normal );
	}

