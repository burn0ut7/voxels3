using System;

/// <summary>Continuous mountain ridges and shoulders with analytic gradients and conservative bounds.</summary>
internal static class MountainMasses
{
	private const uint WarpXSalt = 0x7F4A7C15u;
	private const uint WarpYSalt = 0x94D049BBu;
	private const uint RidgeSalt = 0x369DEA0Fu;
	private const uint SpurSalt = 0xDB4F0B91u;
	// Half-width in folded noise coordinates, not altitude. GPU mirror: voxel_mountain_masses.hlsl.
	// A C1 crest lets the erosion slope mask fade through direction reversals.
	private const float CrestWidth = 0.08f;

	private static float Noise( Vector2 point, uint seed, out Vector2 gradient )
	{
		var x = (int)MathF.Floor( point.x );
		var y = (int)MathF.Floor( point.y );
		var tx = point.x - x;
		var ty = point.y - y;
		var u = tx * tx * (3f - 2f * tx);
		var v = ty * ty * (3f - 2f * ty);
		var a = (TerrainNoise.Hash( x, y, seed ) & 65535u) / 65535f;
		var b = (TerrainNoise.Hash( x + 1, y, seed ) & 65535u) / 65535f;
		var c = (TerrainNoise.Hash( x, y + 1, seed ) & 65535u) / 65535f;
		var d = (TerrainNoise.Hash( x + 1, y + 1, seed ) & 65535u) / 65535f;
		gradient = new Vector2( ((b - a) * (1f - v) + (d - c) * v) * (6f * tx * (1f - tx)),
			((c - a) * (1f - u) + (d - b) * u) * (6f * ty * (1f - ty)) );
		return Math.Clamp( (a * (1f - u) + b * u) * (1f - v) + (c * (1f - u) + d * u) * v, 0f, 1f );
	}

	public static float Sample( Vector3 position, float scale, uint seed, out Vector2 gradient, out float peakFraction, out float erosionStrength )
	{
		var point = new Vector2( position.x / scale, position.y / scale );
		var wx = Noise( point * 0.43f, seed ^ WarpXSalt, out var gx );
		var wy = Noise( point * 0.43f + new Vector2( 19.3f, -7.1f ), seed ^ WarpYSalt, out var gy );
		gx *= 0.43f;
		gy *= 0.43f;
		var warped = point + new Vector2( wx - 0.5f, wy - 0.5f ) * 1.8f;
		var a = Noise( new Vector2( 0.8f * warped.x + 0.6f * warped.y, -0.6f * warped.x + 0.8f * warped.y ) * 0.72f,
			seed ^ RidgeSalt, out var ga );
		var b = Noise( new Vector2( 0.6f * warped.x - 0.8f * warped.y, 0.8f * warped.x + 0.6f * warped.y ) * 1.15f + new Vector2( -11.7f, 23.4f ),
			seed ^ SpurSalt, out var gb );
		ga = new Vector2( 0.8f * ga.x - 0.6f * ga.y, 0.6f * ga.x + 0.8f * ga.y ) * 0.72f;
		gb = new Vector2( 0.6f * gb.x + 0.8f * gb.y, -0.8f * gb.x + 0.6f * gb.y ) * 1.15f;
		ga = ga + 1.8f * (ga.x * gx + ga.y * gy);
		gb = gb + 1.8f * (gb.x * gx + gb.y * gy);
		var da = 4f * a - 2f;
		var db = 4f * b - 2f;
		var aa = MathF.Abs( da );
		var ab = MathF.Abs( db );
		var ra = MathF.Max( 0f, 1f - (aa < CrestWidth ? da * da / (2f * CrestWidth) + CrestWidth * 0.5f : aa) );
		var rb = MathF.Max( 0f, 1f - (ab < CrestWidth ? db * db / (2f * CrestWidth) + CrestWidth * 0.5f : ab) );
		var ridges = 0.72f * ra * ra + 0.28f * rb * rb;
		var ridgeGradient = ga * (-5.76f * ra * Math.Clamp( da / CrestWidth, -1f, 1f )) +
			gb * (-2.24f * rb * Math.Clamp( db / CrestWidth, -1f, 1f ));
		var st = Math.Clamp( (a - 0.28f) / 0.40f, 0f, 1f );
		var shelf = st * st * (3f - 2f * st);
		var shelfGradient = ga * (6f * st * (1f - st) / 0.40f);
		var bt = Math.Clamp( (wx - 0.42f) / 0.26f, 0f, 1f );
		var blend = bt * bt * (3f - 2f * bt);
		var blendGradient = gx * (6f * bt * (1f - bt) / 0.26f);
		var height = 0.45f + 0.55f * wy;
		var shape = (1f - blend) * ridges + blend * shelf;
		var mass = height * shape;
		gradient = (height * ((1f - blend) * ridgeGradient + blend * shelfGradient + (shelf - ridges) * blendGradient) + shape * 0.55f * gy) / scale;
		// Only pointed ridge relief qualifies; broad elevated shelves are not tips.
		peakFraction = height * (1f - blend) * ridges;
		erosionStrength = 0.25f + 0.75f * wy;
		return mass;
	}

	public static (double Minimum, double Maximum) Bound( SdfWorldAabb bounds, float scale, uint seed )
	{
		var padding = 0.00001d + Math.Max( Math.Max( Math.Abs( bounds.Minimum.x ), Math.Abs( bounds.Maximum.x ) ),
			Math.Max( Math.Abs( bounds.Minimum.y ), Math.Abs( bounds.Maximum.y ) ) ) / scale * 0.0000002d;
		var x = new Interval( bounds.Minimum.x / (double)scale - padding, bounds.Maximum.x / (double)scale + padding );
		var y = new Interval( bounds.Minimum.y / (double)scale - padding, bounds.Maximum.y / (double)scale + padding );
		Interval Field( Interval px, Interval py, uint salt )
		{
			var cx = (px.Minimum + px.Maximum) * 0.5d;
			var cy = (py.Minimum + py.Maximum) * 0.5d;
			var value = Noise( new Vector2( (float)cx, (float)cy ), seed ^ salt, out _ );
			// Each partial of cubic value noise is bounded by1.5 in lattice units.
			var radius = 0.75d * (px.Maximum - px.Minimum + py.Maximum - py.Minimum) +
				0.0001d + (Math.Abs( cx ) + Math.Abs( cy )) * 0.0000003d;
			return new Interval( Math.Max( 0d, value - radius ), Math.Min( 1d, value + radius ) );
		}
		var wx = Field( x * 0.43d, y * 0.43d, WarpXSalt );
		var wy = Field( x * 0.43d + 19.3d, y * 0.43d - 7.1d, WarpYSalt );
		var u = x + (wx - 0.5d) * 1.8d;
		var v = y + (wy - 0.5d) * 1.8d;
		var a = Field( (0.8d * u + 0.6d * v) * 0.72d, (-0.6d * u + 0.8d * v) * 0.72d, RidgeSalt );
		var b = Field( (0.6d * u - 0.8d * v) * 1.15d - 11.7d, (0.8d * u + 0.6d * v) * 1.15d + 23.4d, SpurSalt );
		var ra = Ridge( a );
		var rb = Ridge( b );
		var ridges = 0.72d * ra * ra + 0.28d * rb * rb;
		var shelf = Smooth( (a - 0.28d) * 2.5d );
		var blend = Smooth( (wx - 0.42d) * (1d / 0.26d) );
		var mass = (0.45d + 0.55d * wy) * ((1d - blend) * ridges + blend * shelf);
		return (Math.Max( 0d, mass.Minimum - 0.0001d ), Math.Min( 1d, mass.Maximum + 0.0001d ));
	}

	private static Interval Ridge( Interval value )
	{
		var a = 4d * value.Minimum - 2d;
		var b = 4d * value.Maximum - 2d;
		var nearest = a <= 0d && b >= 0d ? 0d : Math.Min( Math.Abs( a ), Math.Abs( b ) );
		var farthest = Math.Max( Math.Abs( a ), Math.Abs( b ) );
		// The rounded absolute value is monotone in distance from the crest.
		// Cast the float owner so interval evaluation uses the exact same width.
		var width = (double)CrestWidth;
		if ( nearest < width )
		{
			nearest = nearest * nearest / (2d * width) + width * 0.5d;
		}
		if ( farthest < width )
		{
			farthest = farthest * farthest / (2d * width) + width * 0.5d;
		}
		return new Interval( Math.Max( 0d, 1d - farthest ), Math.Max( 0d, 1d - nearest ) );
	}

	private static Interval Smooth( Interval value )
	{
		var a = Math.Clamp( value.Minimum, 0d, 1d );
		var b = Math.Clamp( value.Maximum, 0d, 1d );
		return new Interval( a * a * (3d - 2d * a), b * b * (3d - 2d * b) );
	}

	private readonly record struct Interval( double Minimum, double Maximum )
	{
		public static implicit operator Interval( double value ) => new( value, value );
		public static Interval operator +( Interval a, Interval b ) => new( a.Minimum + b.Minimum, a.Maximum + b.Maximum );
		public static Interval operator -( Interval a, Interval b ) => new( a.Minimum - b.Maximum, a.Maximum - b.Minimum );
		public static Interval operator *( Interval a, Interval b ) => new(
			Math.Min( Math.Min( a.Minimum * b.Minimum, a.Minimum * b.Maximum ), Math.Min( a.Maximum * b.Minimum, a.Maximum * b.Maximum ) ),
			Math.Max( Math.Max( a.Minimum * b.Minimum, a.Minimum * b.Maximum ), Math.Max( a.Maximum * b.Minimum, a.Maximum * b.Maximum ) ) );
	}
}
