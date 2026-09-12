using System;

// Adapted from Advanced Terrain Erosion Filter / Phacelle Noise,
// copyright (c) 2025 Rune Skovbo Johansen; C# reference by Luke Mitchell, 2026.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. https://mozilla.org/MPL/2.0/
// Voxels3 adaptation: integer cell hashes, neutral initial fade, bounded recipe.
internal static class TerrainErosion
{
	// World-space amplitude is a fraction of ReliefHeight. GPU mirror: voxel_erosion.hlsl.
	internal const float MountainStrength = 0.035f;
	internal const float MinimumMountainWeight = 0.5f;
	internal const int Octaves = 2;
	internal const float AmplitudeSum = 1.5f;
	internal const float MaximumOffsetFraction = MountainStrength * AmplitudeSum;
	internal const float WavelengthFraction = 0.20f;

	internal static float Offset( Vector2 position, Vector2 gradient, float wavelength, float amplitude, uint seed )
	{
		var slopeLength = MathF.Sqrt( gradient.x * gradient.x + gradient.y * gradient.y );
		if ( slopeLength <= 0.000001f || amplitude <= 0f )
		{
			return 0f;
		}
		var mask = Math.Clamp( slopeLength * 3f, 0f, 1f );
		mask = 1f - (1f - mask) * (1f - mask);
		var gullySlope = gradient / slopeLength * 0.7f;
		var frequency = 1f / (wavelength * 0.7f);
		var fadeTarget = 0f;
		var height = 0f;
		for ( var octave = 0; octave < Octaves; octave++ )
		{
			var length = MathF.Sqrt( gullySlope.x * gullySlope.x + gullySlope.y * gullySlope.y );
			var direction = gullySlope / MathF.Max( length, 0.000001f );
			var side = new Vector2( -direction.y, direction.x ) * (0.7f * MathF.Tau);
			var wave = Phacelle( position * frequency, side, seed );
			var faded = fadeTarget * (1f - mask) + wave.x * 0.7f * mask;
			height += faded * amplitude;
			gullySlope -= side * (MathF.Sign( wave.y ) * frequency * amplitude * 0.7f);
			fadeTarget = faded;
			var nextMask = Math.Clamp( MathF.Abs( wave.y ) * 1.5f, 0f, 1f );
			nextMask = 1f - (1f - nextMask) * (1f - nextMask);
			mask = (1f - (1f - mask) * (1f - mask)) * nextMask;
			amplitude *= 0.5f;
			frequency *= 2f;
		}
		return height;
	}

	private static Vector2 Phacelle( Vector2 position, Vector2 side, uint seed )
	{
		var ix = (int)MathF.Floor( position.x );
		var iy = (int)MathF.Floor( position.y );
		var fx = position.x - ix;
		var fy = position.y - iy;
		// Four global lattice pivots; smooth weights vanish at cell boundaries.
		var u = fx * fx * (3f - 2f * fx);
		var v = fy * fy * (3f - 2f * fy);
		var cosine = 0f;
		var sine = 0f;
		var weightSum = 0f;
		for ( var y = 0; y <= 1; y++ )
		{
			for ( var x = 0; x <= 1; x++ )
			{
				var hash = TerrainNoise.Hash( ix + x, iy + y, seed ^ 0xA24BAED5u );
				var dx = fx - x - ((hash & 65535u) / 65535f - 0.5f);
				var dy = fy - y - ((hash >> 16) / 65535f - 0.5f);
				var weight = (x == 0 ? 1f - u : u) * (y == 0 ? 1f - v : v);
				var phase = dx * side.x + dy * side.y + MathF.Tau * 0.25f;
				cosine += MathF.Cos( phase ) * weight;
				sine += MathF.Sin( phase ) * weight;
				weightSum += weight;
			}
		}
		cosine /= weightSum;
		sine /= weightSum;
		var magnitude = MathF.Max( 0.5f, MathF.Sqrt( cosine * cosine + sine * sine ) );
		return new Vector2( cosine / magnitude, sine / magnitude );
	}
}
