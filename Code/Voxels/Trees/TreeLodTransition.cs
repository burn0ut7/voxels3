using System;

internal static class TreeLodTransition
{
	public static float Advance( float detail, ref bool farSelected, float distance,
		float returnDistance, float exitDistance, float step, float initialStep )
	{
		if ( !float.IsFinite( detail ) )
		{
			farSelected = distance >= (returnDistance + exitDistance) * 0.5f;
			return farSelected ? 0f : 1f;
		}
		var wasFar = farSelected;
		if ( distance >= exitDistance ) farSelected = true;
		else if ( distance <= returnDistance ) farSelected = false;
		var advance = wasFar == farSelected ? step : initialStep;
		return detail + Math.Clamp( (farSelected ? 0f : 1f) - detail, -advance, advance );
	}
}
