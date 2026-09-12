using System;

/// <summary>Seeded, bounded cliff excavations. Positive values remove solid terrain.</summary>
internal static class TerrainCliffs
{
	// Compact rotated cuts create steep walls and a wider recess below the lip.
	internal const float SpacingFraction = 1.3f;
	internal const float Undercut = 64f;
	private readonly record struct Cut( Vector2 Center, Vector2 Axis, float RadiusX, float RadiusY, float Z, float HalfHeight );

	private static Cut At( int x, int y, ProceduralTerrainSettings settings )
	{
		var seed = unchecked((uint)settings.WorldSeed);
		var a = TerrainNoise.Hash( x, y, seed ^ 0xE19B01AAu );
		var b = TerrainNoise.Hash( x, y, seed ^ 0xC734D891u );
		if ( (a >> 24) < 205u ) return default;
		var scale = settings.LocalLandformScale * SpacingFraction;
		var axis = new Vector2( (b & 255u) - 127.5f, ((b >> 8) & 255u) - 127.5f );
		axis /= MathF.Sqrt( axis.x * axis.x + axis.y * axis.y );
		return new Cut( new Vector2( x + 0.5f + ((a & 255u) / 255f - 0.5f) * 0.36f,
			y + 0.5f + (((a >> 8) & 255u) / 255f - 0.5f) * 0.36f ) * scale,
			axis, scale * (0.099f + 0.054f * ((a >> 16) & 255u) / 255f), scale * 0.063f,
			settings.SeaLevel + settings.ReliefHeight * (0.28f + 0.30f * ((b >> 16) & 255u) / 255f),
			settings.ReliefHeight * (0.078f + 0.052f * (b >> 24) / 255f) );
	}

	internal static float Sample( Vector3 position, ProceduralTerrainSettings settings, float mountainWeight, float density )
	{
		var limit = settings.ReliefHeight;
		var mask = (mountainWeight - 0.75f) * limit;
		var value = density;
		var verticalBound = MathF.Min( position.z - settings.SeaLevel - limit * 0.15f,
			settings.SeaLevel + limit * 0.71f - position.z );
		if ( MathF.Min( mask, verticalBound ) <= density ) return density;
		var scale = settings.LocalLandformScale * SpacingFraction;
		var cellX = (int)MathF.Floor( position.x / scale );
		var cellY = (int)MathF.Floor( position.y / scale );
		for ( var y = -1; y <= 1; y++ )
		{
			for ( var x = -1; x <= 1; x++ )
			{
				// Every possible center lies within .18 cells of its grid center.
				// Radius <= .153*scale, minor radius=.063*scale; include the full lip.
				var nearX = MathF.Max( 0f, MathF.Abs( position.x / scale - (cellX + x + 0.5f) ) - 0.18f );
				var nearY = MathF.Max( 0f, MathF.Abs( position.y / scale - (cellY + y + 0.5f) ) - 0.18f );
				var upper = (1f - MathF.Sqrt( nearX * nearX + nearY * nearY ) / 0.153f) * (0.063f * scale) + Undercut + 1f;
				if ( upper <= value ) continue;
				var cut = At( cellX + x, cellY + y, settings );
				if ( cut.HalfHeight <= 0f ) continue;
				var dx = position.x - cut.Center.x;
				var dy = position.y - cut.Center.y;
				var u = MathF.Abs( dx * cut.Axis.x + dy * cut.Axis.y );
				var v = MathF.Abs( -dx * cut.Axis.y + dy * cut.Axis.x );
				var vertical = cut.HalfHeight - MathF.Abs( position.z - cut.Z );
				var recess = Undercut * Math.Clamp( vertical / cut.HalfHeight, 0f, 1f );
				var horizontal = (1f - MathF.Sqrt( u * u / (cut.RadiusX * cut.RadiusX) + v * v / (cut.RadiusY * cut.RadiusY) )) * MathF.Min( cut.RadiusX, cut.RadiusY ) + recess;
				value = MathF.Max( value, MathF.Min( horizontal, vertical ) );
			}
		}
		return MathF.Max( density, MathF.Min( value, mask ) );
	}

	// Upper interval only: subtraction never lowers the original density.
	internal static float BoundMaximum( SdfWorldAabb bounds, ProceduralTerrainSettings settings )
	{
		var scale = settings.LocalLandformScale * SpacingFraction;
		var low = settings.SeaLevel + settings.ReliefHeight * 0.15f;
		var high = settings.SeaLevel + settings.ReliefHeight * 0.71f;
		var verticalBound = MathF.Min( bounds.Maximum.z - low, high - bounds.Minimum.z );
		if ( verticalBound < 0f ) return verticalBound + 1f;
		var cellX = (int)MathF.Floor( bounds.Minimum.x / scale );
		var cellY = (int)MathF.Floor( bounds.Minimum.y / scale );
		if ( cellX != (int)MathF.Floor( bounds.Maximum.x / scale ) ||
			cellY != (int)MathF.Floor( bounds.Maximum.y / scale ) ) return settings.ReliefHeight * 0.13f + 1f;
		var center = bounds.Minimum + (bounds.Maximum - bounds.Minimum) * 0.5f;
		var half = (bounds.Maximum - bounds.Minimum) * 0.5f;
		var maximum = -float.MaxValue;
		for ( var y = -1; y <= 1; y++ )
		{
			for ( var x = -1; x <= 1; x++ )
			{
				var cut = At( cellX + x, cellY + y, settings );
				if ( cut.HalfHeight <= 0f ) continue;
				var dx = center.x - cut.Center.x;
				var dy = center.y - cut.Center.y;
				var u = MathF.Max( 0f, MathF.Abs( dx * cut.Axis.x + dy * cut.Axis.y ) - half.x * MathF.Abs( cut.Axis.x ) - half.y * MathF.Abs( cut.Axis.y ) );
				var v = MathF.Max( 0f, MathF.Abs( -dx * cut.Axis.y + dy * cut.Axis.x ) - half.x * MathF.Abs( cut.Axis.y ) - half.y * MathF.Abs( cut.Axis.x ) );
				var vertical = cut.HalfHeight - MathF.Max( 0f, MathF.Abs( center.z - cut.Z ) - half.z );
				var horizontal = (1f - MathF.Sqrt( u * u / (cut.RadiusX * cut.RadiusX) + v * v / (cut.RadiusY * cut.RadiusY) )) * MathF.Min( cut.RadiusX, cut.RadiusY ) + Undercut;
				maximum = MathF.Max( maximum, MathF.Min( horizontal, vertical ) );
			}
		}
		return maximum + 1f;
	}
}


