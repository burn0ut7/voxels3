	bool IsDefinitelyOutsideFrustum( float3 minimum, float3 maximum )
	{
		const float LateralGuardScale = 1.05;
		float3 center = (minimum + maximum) * 0.5;
		float3 extent = (maximum - minimum) * 0.5;
		float4 clipCenter = Position3WsToPs( center );
		// Homogeneous directions omit the camera translation. Each scaled matrix
		// column is the contribution of one box axis to every clip-space plane.
		float4 axisX = Position4WsToPs( float4( extent.x, 0.0, 0.0, 0.0 ) );
		float4 axisY = Position4WsToPs( float4( 0.0, extent.y, 0.0, 0.0 ) );
		float4 axisZ = Position4WsToPs( float4( 0.0, 0.0, extent.z, 0.0 ) );
		float4 radius = abs( axisX ) + abs( axisY ) + abs( axisZ );
		if ( any( isnan( clipCenter ) ) || any( isinf( clipCenter ) ) ||
			any( isnan( radius ) ) || any( isinf( radius ) ) )
		{
			return false;
		}
		// Retain uncertain boxes crossing the eye plane. The old corner test
		// also retained corners with effectively zero homogeneous W.
		if ( clipCenter.w - radius.w <= 1e-6 && clipCenter.w + radius.w >= -1e-6 )
		{
			return false;
		}
		float tolerance = max( 1.0, abs( clipCenter.w ) + radius.w ) * 1e-5;
		float4 sides = float4( clipCenter.x, -clipCenter.x, clipCenter.y, -clipCenter.y )
			+ LateralGuardScale * clipCenter.w;
		float4 sideRadius =
			abs( float4( axisX.x, -axisX.x, axisX.y, -axisX.y ) + LateralGuardScale * axisX.w )
			+ abs( float4( axisY.x, -axisY.x, axisY.y, -axisY.y ) + LateralGuardScale * axisY.w )
			+ abs( float4( axisZ.x, -axisZ.x, axisZ.y, -axisZ.y ) + LateralGuardScale * axisZ.w );
		float farMaximum = clipCenter.w - clipCenter.z
			+ abs( axisX.w - axisX.z ) + abs( axisY.w - axisY.z ) + abs( axisZ.w - axisZ.z );
		return any( sides + sideRadius < -tolerance ) ||
			clipCenter.z + radius.z < -tolerance || farMaximum < -tolerance;
	}
