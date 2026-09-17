	bool IsDefinitelyOutsideFrustum( float3 minimum, float3 maximum )
	{
		const float LateralGuardScale = 1.05;
		uint outsideCounts[6] = { 0, 0, 0, 0, 0, 0 };

		[unroll]
		for ( uint cornerIndex = 0; cornerIndex < 8; cornerIndex++ )
		{
			float3 corner = float3(
				(cornerIndex & 1) != 0 ? maximum.x : minimum.x,
				(cornerIndex & 2) != 0 ? maximum.y : minimum.y,
				(cornerIndex & 4) != 0 ? maximum.z : minimum.z );
			float4 clip = Position3WsToPs( corner );
			if ( any( isnan( clip ) ) || any( isinf( clip ) ) || abs( clip.w ) < 1e-6 )
			{
				return false;
			}

			float tolerance = max( 1.0, abs( clip.w ) ) * 1e-5;
			outsideCounts[0] += clip.x < -clip.w * LateralGuardScale - tolerance;
			outsideCounts[1] += clip.x > clip.w * LateralGuardScale + tolerance;
			outsideCounts[2] += clip.y < -clip.w * LateralGuardScale - tolerance;
			outsideCounts[3] += clip.y > clip.w * LateralGuardScale + tolerance;
			outsideCounts[4] += clip.z < -tolerance;
			outsideCounts[5] += clip.z > clip.w + tolerance;
		}

		[unroll]
		for ( uint planeIndex = 0; planeIndex < 6; planeIndex++ )
		{
			if ( outsideCounts[planeIndex] == 8 )
			{
				return true;
			}
		}

		return false;
	}

