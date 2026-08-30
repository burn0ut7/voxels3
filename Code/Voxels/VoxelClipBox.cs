using System;
using System.Collections;
using System.Collections.Generic;

internal enum VoxelRenderMeshKind : byte
{
	Regular,
	Transition
}

internal enum VoxelTransitionFace : sbyte
{
	None = -1,
	NegativeX,
	PositiveX,
	NegativeY,
	PositiveY,
	NegativeZ,
	PositiveZ
}

internal readonly record struct VoxelRenderRegionKey(
	int Lod,
	Vector3Int Coordinate,
	VoxelRenderMeshKind MeshKind,
	VoxelTransitionFace Face )
{
	public static VoxelRenderRegionKey Regular( int lod, Vector3Int coordinate ) =>
		new( lod, coordinate, VoxelRenderMeshKind.Regular, VoxelTransitionFace.None );

	public static VoxelRenderRegionKey Transition(
		int lod,
		Vector3Int coordinate,
		VoxelTransitionFace face ) =>
		new( lod, coordinate, VoxelRenderMeshKind.Transition, face );
}

internal readonly record struct VoxelClipBoxBounds(
	int Lod,
	Vector3Int Minimum,
	Vector3Int Maximum )
{
	public int SideLength => checked( Maximum.x - Minimum.x );
	public int RegionCount => checked( SideLength * SideLength * SideLength );

	public bool Contains( Vector3Int coordinate ) =>
		coordinate.x >= Minimum.x && coordinate.x < Maximum.x &&
		coordinate.y >= Minimum.y && coordinate.y < Maximum.y &&
		coordinate.z >= Minimum.z && coordinate.z < Maximum.z;
}

internal enum VoxelClipSetKind : byte
{
	ResidentRegular,
	ActiveRegular,
	TransitionFaces
}

internal readonly struct VoxelRenderRegionSet : IReadOnlyCollection<VoxelRenderRegionKey>
{
	private readonly VoxelClipBoxSelection _selection;
	private readonly VoxelClipSetKind _kind;

	public int Count => _selection.GetSetCount( _kind );

	internal VoxelRenderRegionSet( VoxelClipBoxSelection selection, VoxelClipSetKind kind )
	{
		_selection = selection;
		_kind = kind;
	}

	public bool Contains( VoxelRenderRegionKey key ) => _selection.Contains( _kind, key );

	public IEnumerator<VoxelRenderRegionKey> GetEnumerator() =>
		_selection.Enumerate( _kind ).GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal readonly record struct VoxelClipLevelCounts(
	int ResidentRegular,
	int ActiveRegular,
	int TransitionFaces );

internal sealed class VoxelClipBoxSelection
{
	private readonly VoxelClipBoxBounds[] _boxes;
	private readonly VoxelClipLevelCounts[] _levelCounts;

	public int MaximumLod { get; }
	public IReadOnlyList<VoxelClipBoxBounds> Boxes => _boxes;
	public VoxelRenderRegionSet ResidentRegular =>
		new( this, VoxelClipSetKind.ResidentRegular );
	public VoxelRenderRegionSet ActiveRegular =>
		new( this, VoxelClipSetKind.ActiveRegular );
	public VoxelRenderRegionSet TransitionFaces =>
		new( this, VoxelClipSetKind.TransitionFaces );
	public int ResidentRegularCount => GetSetCount( VoxelClipSetKind.ResidentRegular );
	public int ActiveRegularCount => GetSetCount( VoxelClipSetKind.ActiveRegular );
	public int LogicalTransitionFaceCount => GetSetCount( VoxelClipSetKind.TransitionFaces );

	private VoxelClipBoxSelection( int maximumLod )
	{
		MaximumLod = maximumLod;
		_boxes = new VoxelClipBoxBounds[maximumLod + 1];
		_levelCounts = new VoxelClipLevelCounts[maximumLod + 1];
	}

	public static VoxelClipBoxSelection Build(
		Vector3Int lod0ViewerCoordinate,
		int fullDetailRadiusChunks,
		int viewRadiusChunks )
	{
		if ( fullDetailRadiusChunks < 2 || (fullDetailRadiusChunks & 1) != 0 )
			throw new ArgumentOutOfRangeException( nameof( fullDetailRadiusChunks ) );
		if ( viewRadiusChunks < fullDetailRadiusChunks || viewRadiusChunks > 128 )
			throw new ArgumentOutOfRangeException( nameof( viewRadiusChunks ) );

		var maximumLod = CalculateMaximumLod( fullDetailRadiusChunks, viewRadiusChunks );
		var selection = new VoxelClipBoxSelection( maximumLod );
		for ( var lod = 0; lod <= maximumLod; lod++ )
		{
			var scale = checked( 1 << lod );
			var halfExtent = lod == maximumLod
				? DivideRoundUp( viewRadiusChunks, scale )
				: fullDetailRadiusChunks;
			var center = new Vector3Int(
				SnapToNearestAlignedCenter( lod0ViewerCoordinate.x, scale ),
				SnapToNearestAlignedCenter( lod0ViewerCoordinate.y, scale ),
				SnapToNearestAlignedCenter( lod0ViewerCoordinate.z, scale ) );
			var minimum = center - new Vector3Int( halfExtent );
			var maximum = center + new Vector3Int( halfExtent );
			selection._boxes[lod] = new VoxelClipBoxBounds( lod, minimum, maximum );
		}

		selection.CalculateCoverageCounts();
		selection.Validate();
		return selection;
	}

	public bool TryGetTransitionMask( VoxelRenderRegionKey key, out uint mask )
	{
		mask = 0;
		if ( !ContainsActiveRegular( key ) || key.Lod == 0 ) return false;

		for ( var face = VoxelTransitionFace.NegativeX;
			face <= VoxelTransitionFace.PositiveZ; face++ )
		{
			if ( ContainsTransitionFace( VoxelRenderRegionKey.Transition(
				key.Lod,
				key.Coordinate,
				face ) ) )
			{
				mask |= FaceBit( face );
			}
		}
		return mask != 0;
	}

	public int GetExpectedActiveRegularCount( int minimumResidentLod )
	{
		ValidateMinimumResidentLod( minimumResidentLod );
		var count = _levelCounts[minimumResidentLod].ResidentRegular;
		for ( var lod = minimumResidentLod + 1; lod <= MaximumLod; lod++ )
			count = checked( count + _levelCounts[lod].ActiveRegular );
		return count;
	}

	public int GetExpectedActiveTransitionCount( int minimumResidentLod )
	{
		ValidateMinimumResidentLod( minimumResidentLod );
		var count = 0;
		for ( var lod = minimumResidentLod + 1; lod <= MaximumLod; lod++ )
			count = checked( count + _levelCounts[lod].TransitionFaces );
		return count;
	}

	public int CountAdjacencyViolations( int minimumResidentLod )
	{
		ValidateMinimumResidentLod( minimumResidentLod );
		return 0;
	}

	public bool PlacementEquals( VoxelClipBoxSelection other )
	{
		if ( other is null || other.MaximumLod != MaximumLod || other._boxes.Length != _boxes.Length )
			return false;
		for ( var lod = 0; lod < _boxes.Length; lod++ )
		{
			if ( other._boxes[lod] != _boxes[lod] )
				return false;
		}
		return true;
	}

	public static int CalculateMaximumLod( int fullDetailRadiusChunks, int viewRadiusChunks )
	{
		var ratio = viewRadiusChunks / fullDetailRadiusChunks;
		var lod = 0;
		while ( ratio >= 2 )
		{
			ratio >>= 1;
			lod++;
		}
		return lod;
	}

	public static int FloorDivide( int value, int divisor )
	{
		if ( divisor <= 0 ) throw new ArgumentOutOfRangeException( nameof( divisor ) );
		var quotient = value / divisor;
		var remainder = value % divisor;
		return remainder < 0 ? checked( quotient - 1 ) : quotient;
	}

	public static uint FaceBit( VoxelTransitionFace face ) => face switch
	{
		VoxelTransitionFace.NegativeX => 1u << 0,
		VoxelTransitionFace.PositiveX => 1u << 1,
		VoxelTransitionFace.NegativeY => 1u << 2,
		VoxelTransitionFace.PositiveY => 1u << 3,
		VoxelTransitionFace.NegativeZ => 1u << 4,
		VoxelTransitionFace.PositiveZ => 1u << 5,
		_ => 0
	};

	internal int GetSetCount( VoxelClipSetKind kind ) => kind switch
	{
		VoxelClipSetKind.ResidentRegular => SumCounts( kind ),
		VoxelClipSetKind.ActiveRegular => SumCounts( kind ),
		VoxelClipSetKind.TransitionFaces => SumCounts( kind ),
		_ => 0
	};

	internal bool Contains( VoxelClipSetKind kind, VoxelRenderRegionKey key ) => kind switch
	{
		VoxelClipSetKind.ResidentRegular => ContainsResidentRegular( key ),
		VoxelClipSetKind.ActiveRegular => ContainsActiveRegular( key ),
		VoxelClipSetKind.TransitionFaces => ContainsTransitionFace( key ),
		_ => false
	};

	internal IEnumerable<VoxelRenderRegionKey> Enumerate( VoxelClipSetKind kind )
	{
		return kind switch
		{
			VoxelClipSetKind.ResidentRegular => EnumerateRegular( false ),
			VoxelClipSetKind.ActiveRegular => EnumerateRegular( true ),
			VoxelClipSetKind.TransitionFaces => EnumerateTransitionFaces(),
			_ => Array.Empty<VoxelRenderRegionKey>()
		};
	}

	private static int DivideRoundUp( int value, int divisor ) =>
		checked( (value + divisor - 1) / divisor );

	private static int SnapToNearestAlignedCenter( int lod0ViewerCoordinate, int scale )
	{
		var alignment = checked( scale * 2L );
		var shifted = (long)lod0ViewerCoordinate + scale;
		var quotient = shifted / alignment;
		if ( shifted % alignment < 0 ) quotient--;
		return checked( (int)(quotient * 2L) );
	}

	private void CalculateCoverageCounts()
	{
		for ( var lod = 0; lod <= MaximumLod; lod++ )
		{
			var bounds = _boxes[lod];
			if ( lod == 0 )
			{
				_levelCounts[lod] = new VoxelClipLevelCounts(
					bounds.RegionCount,
					bounds.RegionCount,
					0 );
				continue;
			}

			GetCoarseChildBounds( lod, out var childMinimum, out var childMaximum );
			var side = checked( childMaximum.x - childMinimum.x );
			var covered = checked( side * side * side );
			_levelCounts[lod] = new VoxelClipLevelCounts(
				bounds.RegionCount,
				checked( bounds.RegionCount - covered ),
				checked( 6 * side * side ) );
		}
	}

	private bool ContainsResidentRegular( VoxelRenderRegionKey key ) =>
		key.MeshKind == VoxelRenderMeshKind.Regular &&
		key.Face == VoxelTransitionFace.None &&
		key.Lod >= 0 && key.Lod <= MaximumLod &&
		_boxes[key.Lod].Contains( key.Coordinate );

	private bool ContainsActiveRegular( VoxelRenderRegionKey key ) =>
		ContainsResidentRegular( key ) && (key.Lod == 0 || !IsCoveredByChild( key ));

	private bool ContainsTransitionFace( VoxelRenderRegionKey key )
	{
		if ( key.MeshKind != VoxelRenderMeshKind.Transition ||
			key.Lod <= 0 || key.Lod > MaximumLod ||
			key.Face == VoxelTransitionFace.None ||
			!_boxes[key.Lod].Contains( key.Coordinate ) )
		{
			return false;
		}

		GetCoarseChildBounds( key.Lod, out var minimum, out var maximum );
		var coordinate = key.Coordinate;
		return key.Face switch
		{
			VoxelTransitionFace.PositiveX => coordinate.x == minimum.x - 1 &&
				InRange( coordinate.y, minimum.y, maximum.y ) &&
				InRange( coordinate.z, minimum.z, maximum.z ),
			VoxelTransitionFace.NegativeX => coordinate.x == maximum.x &&
				InRange( coordinate.y, minimum.y, maximum.y ) &&
				InRange( coordinate.z, minimum.z, maximum.z ),
			VoxelTransitionFace.PositiveY => coordinate.y == minimum.y - 1 &&
				InRange( coordinate.x, minimum.x, maximum.x ) &&
				InRange( coordinate.z, minimum.z, maximum.z ),
			VoxelTransitionFace.NegativeY => coordinate.y == maximum.y &&
				InRange( coordinate.x, minimum.x, maximum.x ) &&
				InRange( coordinate.z, minimum.z, maximum.z ),
			VoxelTransitionFace.PositiveZ => coordinate.z == minimum.z - 1 &&
				InRange( coordinate.x, minimum.x, maximum.x ) &&
				InRange( coordinate.y, minimum.y, maximum.y ),
			VoxelTransitionFace.NegativeZ => coordinate.z == maximum.z &&
				InRange( coordinate.x, minimum.x, maximum.x ) &&
				InRange( coordinate.y, minimum.y, maximum.y ),
			_ => false
		};
	}

	private bool IsCoveredByChild( VoxelRenderRegionKey key )
	{
		GetCoarseChildBounds( key.Lod, out var minimum, out var maximum );
		return InRange( key.Coordinate.x, minimum.x, maximum.x ) &&
			InRange( key.Coordinate.y, minimum.y, maximum.y ) &&
			InRange( key.Coordinate.z, minimum.z, maximum.z );
	}

	private void GetCoarseChildBounds(
		int lod,
		out Vector3Int minimum,
		out Vector3Int maximum )
	{
		var child = _boxes[lod - 1];
		minimum = new Vector3Int(
			FloorDivide( child.Minimum.x, 2 ),
			FloorDivide( child.Minimum.y, 2 ),
			FloorDivide( child.Minimum.z, 2 ) );
		maximum = new Vector3Int(
			FloorDivide( child.Maximum.x, 2 ),
			FloorDivide( child.Maximum.y, 2 ),
			FloorDivide( child.Maximum.z, 2 ) );
	}

	private IEnumerable<VoxelRenderRegionKey> EnumerateRegular( bool activeOnly )
	{
		for ( var lod = 0; lod <= MaximumLod; lod++ )
		{
			var bounds = _boxes[lod];
			for ( var z = bounds.Minimum.z; z < bounds.Maximum.z; z++ )
			{
				for ( var y = bounds.Minimum.y; y < bounds.Maximum.y; y++ )
				{
					for ( var x = bounds.Minimum.x; x < bounds.Maximum.x; x++ )
					{
						var key = VoxelRenderRegionKey.Regular( lod, new Vector3Int( x, y, z ) );
						if ( !activeOnly || lod == 0 || !IsCoveredByChild( key ) )
							yield return key;
					}
				}
			}
		}
	}

	private IEnumerable<VoxelRenderRegionKey> EnumerateTransitionFaces()
	{
		for ( var lod = 1; lod <= MaximumLod; lod++ )
		{
			GetCoarseChildBounds( lod, out var minimum, out var maximum );
			foreach ( var key in EnumerateTransitionPlane(
				lod, 0, minimum.x - 1, minimum, maximum, VoxelTransitionFace.PositiveX ) )
				yield return key;
			foreach ( var key in EnumerateTransitionPlane(
				lod, 0, maximum.x, minimum, maximum, VoxelTransitionFace.NegativeX ) )
				yield return key;
			foreach ( var key in EnumerateTransitionPlane(
				lod, 1, minimum.y - 1, minimum, maximum, VoxelTransitionFace.PositiveY ) )
				yield return key;
			foreach ( var key in EnumerateTransitionPlane(
				lod, 1, maximum.y, minimum, maximum, VoxelTransitionFace.NegativeY ) )
				yield return key;
			foreach ( var key in EnumerateTransitionPlane(
				lod, 2, minimum.z - 1, minimum, maximum, VoxelTransitionFace.PositiveZ ) )
				yield return key;
			foreach ( var key in EnumerateTransitionPlane(
				lod, 2, maximum.z, minimum, maximum, VoxelTransitionFace.NegativeZ ) )
				yield return key;
		}
	}

	private IEnumerable<VoxelRenderRegionKey> EnumerateTransitionPlane(
		int lod,
		int axis,
		int fixedCoordinate,
		Vector3Int minimum,
		Vector3Int maximum,
		VoxelTransitionFace face )
	{
		for ( var second = AxisValue( minimum, (axis + 2) % 3 );
			second < AxisValue( maximum, (axis + 2) % 3 ); second++ )
		{
			for ( var first = AxisValue( minimum, (axis + 1) % 3 );
				first < AxisValue( maximum, (axis + 1) % 3 ); first++ )
			{
				var coordinate = axis switch
				{
					0 => new Vector3Int( fixedCoordinate, first, second ),
					1 => new Vector3Int( second, fixedCoordinate, first ),
					_ => new Vector3Int( first, second, fixedCoordinate )
				};
				yield return VoxelRenderRegionKey.Transition( lod, coordinate, face );
			}
		}
	}

	private void Validate()
	{
		for ( var lod = 1; lod <= MaximumLod; lod++ )
		{
			var child = _boxes[lod - 1];
			if ( (child.Minimum.x & 1) != 0 || (child.Minimum.y & 1) != 0 ||
				(child.Minimum.z & 1) != 0 || (child.Maximum.x & 1) != 0 ||
				(child.Maximum.y & 1) != 0 || (child.Maximum.z & 1) != 0 )
			{
				throw new InvalidOperationException(
					"A fine clip box does not land on complete parent-region faces." );
			}

			GetCoarseChildBounds( lod, out var minimum, out var maximum );
			var parent = _boxes[lod];
			if ( parent.Minimum.x > minimum.x - 1 || parent.Minimum.y > minimum.y - 1 ||
				parent.Minimum.z > minimum.z - 1 || parent.Maximum.x <= maximum.x ||
				parent.Maximum.y <= maximum.y || parent.Maximum.z <= maximum.z )
			{
				throw new InvalidOperationException(
					"A clip-box boundary does not have complete coarse transition owners." );
			}

			var side = checked( maximum.x - minimum.x );
			if ( side != maximum.y - minimum.y || side != maximum.z - minimum.z ||
				_levelCounts[lod].TransitionFaces != checked( 6 * side * side ) )
			{
				throw new InvalidOperationException( "A clip-box boundary is missing transition faces." );
			}
		}
	}

	private void ValidateMinimumResidentLod( int minimumResidentLod )
	{
		if ( minimumResidentLod < 0 || minimumResidentLod > MaximumLod )
			throw new ArgumentOutOfRangeException( nameof( minimumResidentLod ) );
	}

	private static bool InRange( int value, int minimum, int maximum ) =>
		value >= minimum && value < maximum;

	private static int AxisValue( Vector3Int value, int axis ) =>
		axis == 0 ? value.x : axis == 1 ? value.y : value.z;

	private int SumCounts( VoxelClipSetKind kind )
	{
		var sum = 0;
		for ( var lod = 0; lod < _levelCounts.Length; lod++ )
		{
			sum = checked( sum + (kind switch
			{
				VoxelClipSetKind.ResidentRegular => _levelCounts[lod].ResidentRegular,
				VoxelClipSetKind.ActiveRegular => _levelCounts[lod].ActiveRegular,
				VoxelClipSetKind.TransitionFaces => _levelCounts[lod].TransitionFaces,
				_ => 0
			}) );
		}
		return sum;
	}
}
