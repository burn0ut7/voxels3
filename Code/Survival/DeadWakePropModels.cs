using System;

namespace Sandbox;

/// <summary>Small shared opening props with durable project materials, no transient editor content.</summary>
internal static class DeadWakePropModels
{
	public static Model[] CreateResources()
	{
		var wood = Material.Load( "materials/deadwake/timber.vmat" );
		var stone = Material.Load( "materials/deadwake/stone.vmat" );
		var sticks = new List<Vertex>();
		AddBranch( sticks, new Vector3( -15, -5, 3 ), new Vector3( 15, -2, 4 ), 2.4f );
		AddBranch( sticks, new Vector3( -12, 3, 3 ), new Vector3( 12, 6, 3 ), 2f );
		AddBranch( sticks, new Vector3( -8, -8, 6 ), new Vector3( 9, 7, 5 ), 1.7f );
		var rocks = new List<Vertex>();
		AddStone( rocks, new Vector3( -5, -3, 3 ), new Vector3( 7, 5, 4 ) );
		AddStone( rocks, new Vector3( 6, 4, 3 ), new Vector3( 5, 7, 3 ) );
		return [ Build( wood, sticks ), Build( stone, rocks ) ];
	}

	public static Model CreateStump( Vector3[] loop )
	{
		var vertices = new List<Vertex>();
		for ( var i = 0; i < loop.Length; i++ )
		{
			var a = loop[i];
			var b = loop[(i + 1) % loop.Length];
			var normal = Vector3.Cross( b - a, Vector3.Up ).Normal;
			Vertex Point( Vector3 point, float u ) => new Vertex( point, normal, (b - a).Normal,
				new Vector4( u, point.z / 24f, 0, 0 ) ) { Color = Color.White };
			var u0 = (float)i / loop.Length;
			var u1 = (float)(i + 1) / loop.Length;
			vertices.AddRange( [Point( a.WithZ( 0 ), u0 ), Point( b.WithZ( 0 ), u1 ), Point( b, u1 ),
				Point( a.WithZ( 0 ), u0 ), Point( b, u1 ), Point( a, u0 )] );
		}
		return Model.Builder.AddMesh( CreateMesh( Material.Load( "materials/deadwake/timber.vmat" ), vertices ) )
			.AddMesh( CutFace( [loop], true ) ).Create();
	}

	public static Model CreateFallenCut( Vector3[][] loops ) => Model.Builder.AddMesh( CutFace( loops, false ) ).Create();

	private static Mesh CutFace( Vector3[][] loops, bool up )
	{
		var vertices = new List<Vertex>();
		var normal = up ? Vector3.Up : Vector3.Down;
		foreach ( var loop in loops )
		{
			var center = loop.Aggregate( Vector3.Zero, (sum, p) => sum + p ) / loop.Length;
			var minimum = loop.Aggregate( (a, b) => Vector3.Min( a, b ) );
			var size = loop.Aggregate( (a, b) => Vector3.Max( a, b ) ) - minimum;
			Vertex Point( Vector3 point ) => new Vertex( point, normal, Vector3.Forward,
				new Vector4( (point.x - minimum.x) / size.x, (point.y - minimum.y) / size.y, 0, 0 ) ) { Color = Color.White };
			for ( var i = 0; i < loop.Length; i++ )
			{
				var a = loop[i];
				var b = loop[(i + 1) % loop.Length];
				vertices.AddRange( [Point( center ), Point( up ? a : b ), Point( up ? b : a )] );
			}
		}
		return CreateMesh( Material.Load( "materials/deadwake/endgrain.vmat" ), vertices );
	}

	public static Model CreateHatchet()
	{
		var handle = new List<Vertex>();
		AddBranch( handle, new Vector3( 0, 0, -12 ), new Vector3( 0, 0, 22 ), 1.15f );
		var head = new List<Vertex>();
		// Broad chipped faces taper to a thin cutting edge, rather than an oval pebble.
		Vector2[] outline = [ new( -2.5f, 16.7f ), new( 1.4f, 16f ), new( 6.8f, 16.4f ),
			new( 7.2f, 18f ), new( 6.9f, 21.8f ), new( 4f, 22.5f ), new( -1.8f, 21.8f ), new( -2.8f, 19.5f ) ];
		void HeadFace( Vector3 a, Vector3 b, Vector3 c )
		{
			var normal = Vector3.Cross( b - a, c - a ).Normal;
			var tangent = (Vector3.Forward - normal * normal.x).Normal;
			if ( tangent.LengthSquared < 0.01f ) tangent = Vector3.Up;
			foreach ( var point in new[] { a, b, c } )
			{
				// One continuous stone face; the albedo does not repeat across the head.
				var uv = MathF.Abs( normal.y ) > 0.4f
					? new Vector4( 0.05f + (point.x + 3f) * 0.085f, 0.05f + (point.z - 16f) * 0.135f, 0, 0 )
					: MathF.Abs( normal.z ) > MathF.Abs( normal.x )
						? new Vector4( 0.05f + (point.x + 3f) * 0.085f, 0.5f + point.y * 0.25f, 0, 0 )
						: new Vector4( 0.5f + point.y * 0.25f, 0.05f + (point.z - 16f) * 0.135f, 0, 0 );
				head.Add( new Vertex( point, normal, tangent, uv ) { Color = Color.White } );
			}
		}
		for ( var side = -1; side <= 1; side += 2 )
		{
			var center = new Vector3( 1.4f, side * 1.6f, 19.4f );
			for ( var i = 0; i < outline.Length; i++ )
			{
				var a = outline[i];
				var b = outline[(i + 1) % outline.Length];
				var va = new Vector3( a.x, side * (a.x > 6f ? 0.18f : 0.8f), a.y );
				var vb = new Vector3( b.x, side * (b.x > 6f ? 0.18f : 0.8f), b.y );
				HeadFace( center, side < 0 ? va : vb, side < 0 ? vb : va );
				if ( side > 0 )
				{
					HeadFace( va, vb.WithY( -vb.y ), va.WithY( -va.y ) );
					HeadFace( va, vb, vb.WithY( -vb.y ) );
				}
			}
		}
		var binding = new List<Vertex>();
		for ( var ring = 0; ring < 3; ring++ )
		for ( var segment = 0; segment < 24; segment++ )
		{
			Vector3 LoopPoint( int step )
			{
				var angle = step * MathF.Tau / 24;
				return new Vector3( MathF.Cos( angle ) * 1.7f, MathF.Sin( angle ) * 1.85f,
					17.1f + ring * 0.75f + MathF.Cos( angle ) * 0.35f );
			}
			AddBranch( binding, LoopPoint( segment ), LoopPoint( segment + 1 ), 0.2f, 8, false );
		}
		return Model.Builder.AddMesh( CreateMesh( Material.Load( "materials/deadwake/timber.vmat" ), handle ) )
			.AddMesh( CreateMesh( Material.Load( "materials/deadwake/flint.vmat" ), head ) )
			.AddMesh( CreateMesh( Material.Load( "materials/deadwake/binding.vmat" ), binding ) ).Create();
	}

	private static Model Build( Material material, List<Vertex> vertices ) => Model.Builder.AddMesh( CreateMesh( material, vertices ) ).Create();

	private static Mesh CreateMesh( Material material, List<Vertex> vertices )
	{
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		var indices = new List<int>( vertices.Count );
		var minimum = vertices[0].Position;
		var maximum = minimum;
		for ( var i = 0; i < vertices.Count; i++ )
		{
			indices.Add( i );
			minimum = Vector3.Min( minimum, vertices[i].Position );
			maximum = Vector3.Max( maximum, vertices[i].Position );
		}
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = new BBox( minimum, maximum );
		return mesh;
	}

	private static void AddBranch( List<Vertex> vertices, Vector3 start, Vector3 end, float radius, int sides = 16, bool capped = true )
	{
		var axis = (end - start).Normal;
		var side = Vector3.Cross( axis, MathF.Abs( axis.z ) < 0.9f ? Vector3.Up : Vector3.Forward ).Normal;
		var up = Vector3.Cross( axis, side ).Normal;
		var length = (end - start).Length;
		Vertex Point( int column, bool tip )
		{
			var angle = column * MathF.Tau / sides;
			var radial = side * MathF.Cos( angle ) + up * MathF.Sin( angle );
			var normal = (radial + axis * (capped ? radius * 0.22f / length : 0f)).Normal;
			var tangent = -side * MathF.Sin( angle ) + up * MathF.Cos( angle );
			return new Vertex( (tip ? end : start) + radial * radius * (tip && capped ? 0.78f : 1f), normal, tangent,
				new Vector4( (float)column / sides, tip ? length / 24f : 0f, 0f, 0f ) ) { Color = Color.White };
		}
		for ( var i = 0; i < sides; i++ )
		{
			var a = Point( i, false );
			var b = Point( i + 1, false );
			var c = Point( i + 1, true );
			var d = Point( i, true );
			vertices.AddRange( [a, b, c, a, c, d] );
			if ( capped )
			{
				AddTriangle( vertices, start, b.Position, a.Position );
				AddTriangle( vertices, end, d.Position, c.Position );
			}
		}
	}

	private static void AddStone( List<Vertex> vertices, Vector3 center, Vector3 size )
	{
		const int segments = 24;
		const int rings = 12;
		Vertex Point( int row, int column )
		{
			var theta = row * MathF.PI / rings;
			var phi = column * MathF.Tau / segments;
			var radial = new Vector3( MathF.Sin( theta ) * MathF.Cos( phi ), MathF.Sin( theta ) * MathF.Sin( phi ), MathF.Cos( theta ) );
			var roughness = 1f + 0.12f * MathF.Sin( phi * 3f + theta * 4f ) * MathF.Sin( theta );
			var normal = new Vector3( radial.x / size.x, radial.y / size.y, radial.z / size.z ).Normal;
			var tangent = new Vector3( -MathF.Sin( phi ) * size.x, MathF.Cos( phi ) * size.y, 0f ).Normal;
			return new Vertex( center + radial * size * roughness, normal, tangent,
				new Vector4( 0.04f + 0.14f * column / segments, 0.01f + 0.14f * row / rings, 0f, 0f ) ) { Color = Color.White };
		}
		for ( var row = 0; row < rings; row++ )
		for ( var column = 0; column < segments; column++ )
		{
			var a = Point( row, column );
			var b = Point( row + 1, column );
			var c = Point( row + 1, column + 1 );
			var d = Point( row, column + 1 );
			if ( row < rings - 1 ) vertices.AddRange( [a, b, c] );
			if ( row > 0 ) vertices.AddRange( [a, c, d] );
		}
	}

	private static void AddTriangle( List<Vertex> vertices, Vector3 a, Vector3 b, Vector3 c )
	{
		var normal = Vector3.Cross( b - a, c - a ).Normal;
		var tangent = (b - a).Normal;
		var bitangent = Vector3.Cross( normal, tangent );
		foreach ( var position in new[] { a, b, c } )
		{
			var uv = new Vector4( Vector3.Dot( position, tangent ) / 24f, Vector3.Dot( position, bitangent ) / 24f, 0, 0 );
			vertices.Add( new Vertex( position, normal, tangent, uv ) { Color = Color.White } );
		}
	}
}
