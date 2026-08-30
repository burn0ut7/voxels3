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
	public int RegionCount => checked(
		(Maximum.x - Minimum.x) *
		(Maximum.y - Minimum.y) *
		(Maximum.z - Minimum.z) );

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

internal readonly record struct VoxelClipLevelDeltaCounts(
	int EnteringRegular,
	int LeavingRegular,
	int EnteringTransitions,
	int LeavingTransitions );

internal sealed class VoxelClipBoxDelta
{
	private readonly List<VoxelClipBoxBounds>[] _enteringRegularSlabs;
	private readonly List<VoxelClipBoxBounds>[] _leavingRegularSlabs;
	private readonly List<VoxelRenderRegionKey>[] _enteringTransitions;
	private readonly List<VoxelRenderRegionKey>[] _leavingTransitions;
	private readonly List<VoxelRenderRegionKey> _coverageChanges = new();

	public VoxelClipBoxSelection Previous { get; }
	public VoxelClipBoxSelection Current { get; }
	public int MaximumLod { get; }
	public int CoverageChangeCount => _coverageChanges.Count;

	private VoxelClipBoxDelta(
		VoxelClipBoxSelection previous,
		VoxelClipBoxSelection current )
	{
		Previous = previous;
		Current = current;
		MaximumLod = Math.Max( previous?.MaximumLod ?? -1, current?.MaximumLod ?? -1 );
		_enteringRegularSlabs = CreateLevelLists<VoxelClipBoxBounds>( MaximumLod + 1 );
		_leavingRegularSlabs = CreateLevelLists<VoxelClipBoxBounds>( MaximumLod + 1 );
		_enteringTransitions = CreateLevelLists<VoxelRenderRegionKey>( MaximumLod + 1 );
		_leavingTransitions = CreateLevelLists<VoxelRenderRegionKey>( MaximumLod + 1 );
	}

	public static VoxelClipBoxDelta Build(
		VoxelClipBoxSelection previous,
		VoxelClipBoxSelection current )
	{
		if ( previous is null && current is null )
			throw new ArgumentException( "A clip-box delta requires an old or new selection." );

		var delta = new VoxelClipBoxDelta( previous, current );
		for ( var lod = 0; lod <= delta.MaximumLod; lod++ )
		{
			var oldBounds = previous is not null && lod <= previous.MaximumLod
				? previous.Boxes[lod]
				: (VoxelClipBoxBounds?)null;
			var newBounds = current is not null && lod <= current.MaximumLod
				? current.Boxes[lod]
				: (VoxelClipBoxBounds?)null;
			if ( newBounds.HasValue )
				AddDifferenceSlabs( newBounds.Value, oldBounds, delta._enteringRegularSlabs[lod] );
			if ( oldBounds.HasValue )
				AddDifferenceSlabs( oldBounds.Value, newBounds, delta._leavingRegularSlabs[lod] );
		}

		if ( current is not null )
		{
			foreach ( var key in current.TransitionFaces )
			{
				if ( previous is null || !previous.TransitionFaces.Contains( key ) )
					delta._enteringTransitions[key.Lod].Add( key );
			}
		}
		if ( previous is not null )
		{
			foreach ( var key in previous.TransitionFaces )
			{
				if ( current is null || !current.TransitionFaces.Contains( key ) )
					delta._leavingTransitions[key.Lod].Add( key );
			}
		}

		delta.BuildCoverageChanges();
		return delta;
	}

	public IReadOnlyList<VoxelClipBoxBounds> GetEnteringRegularSlabs( int lod ) =>
		IsValidLod( lod ) ? _enteringRegularSlabs[lod] : Array.Empty<VoxelClipBoxBounds>();

	public IReadOnlyList<VoxelClipBoxBounds> GetLeavingRegularSlabs( int lod ) =>
		IsValidLod( lod ) ? _leavingRegularSlabs[lod] : Array.Empty<VoxelClipBoxBounds>();

	public IReadOnlyList<VoxelRenderRegionKey> GetEnteringTransitions( int lod ) =>
		IsValidLod( lod ) ? _enteringTransitions[lod] : Array.Empty<VoxelRenderRegionKey>();

	public IReadOnlyList<VoxelRenderRegionKey> GetLeavingTransitions( int lod ) =>
		IsValidLod( lod ) ? _leavingTransitions[lod] : Array.Empty<VoxelRenderRegionKey>();

	public IEnumerable<VoxelRenderRegionKey> EnumerateEnteringRegular( int lod ) =>
		EnumerateRegular( lod, GetEnteringRegularSlabs( lod ) );

	public IEnumerable<VoxelRenderRegionKey> EnumerateLeavingRegular( int lod ) =>
		EnumerateRegular( lod, GetLeavingRegularSlabs( lod ) );

	public IEnumerable<VoxelRenderRegionKey> EnumerateCoverageChanges() => _coverageChanges;

	public VoxelClipLevelDeltaCounts GetLevelCounts( int lod )
	{
		if ( !IsValidLod( lod ) ) return default;
		return new VoxelClipLevelDeltaCounts(
			CountRegions( _enteringRegularSlabs[lod] ),
			CountRegions( _leavingRegularSlabs[lod] ),
			_enteringTransitions[lod].Count,
			_leavingTransitions[lod].Count );
	}

	private void BuildCoverageChanges()
	{
		var changes = new HashSet<VoxelRenderRegionKey>();
		for ( var lod = 0; lod <= MaximumLod; lod++ )
		{
			foreach ( var key in EnumerateEnteringRegular( lod ) ) changes.Add( key );
			foreach ( var key in EnumerateLeavingRegular( lod ) ) changes.Add( key );

			if ( lod > 0 )
			{
				var oldCovered = Previous is not null && lod <= Previous.MaximumLod
					? Previous.GetCoveredChildBounds( lod )
					: (VoxelClipBoxBounds?)null;
				var newCovered = Current is not null && lod <= Current.MaximumLod
					? Current.GetCoveredChildBounds( lod )
					: (VoxelClipBoxBounds?)null;
				var slabs = new List<VoxelClipBoxBounds>( 6 );
				if ( oldCovered.HasValue )
					AddDifferenceSlabs( oldCovered.Value, newCovered, slabs );
				foreach ( var key in EnumerateRegular( lod, slabs ) ) changes.Add( key );
				slabs.Clear();
				if ( newCovered.HasValue )
					AddDifferenceSlabs( newCovered.Value, oldCovered, slabs );
				foreach ( var key in EnumerateRegular( lod, slabs ) ) changes.Add( key );
			}

			foreach ( var key in _enteringTransitions[lod] )
			{
				changes.Add( key );
				changes.Add( VoxelRenderRegionKey.Regular( key.Lod, key.Coordinate ) );
			}
			foreach ( var key in _leavingTransitions[lod] )
			{
				changes.Add( key );
				changes.Add( VoxelRenderRegionKey.Regular( key.Lod, key.Coordinate ) );
			}
		}

		_coverageChanges.AddRange( changes );
		_coverageChanges.Sort( CompareKeys );
	}

	private bool IsValidLod( int lod ) => lod >= 0 && lod <= MaximumLod;

	private static List<T>[] CreateLevelLists<T>( int count )
	{
		var result = new List<T>[count];
		for ( var index = 0; index < count; index++ ) result[index] = new List<T>();
		return result;
	}

	private static void AddDifferenceSlabs(
		VoxelClipBoxBounds source,
		VoxelClipBoxBounds? excluded,
		List<VoxelClipBoxBounds> destination )
	{
		if ( !excluded.HasValue )
		{
			destination.Add( source );
			return;
		}

		var other = excluded.Value;
		var minimum = new Vector3Int(
			Math.Max( source.Minimum.x, other.Minimum.x ),
			Math.Max( source.Minimum.y, other.Minimum.y ),
			Math.Max( source.Minimum.z, other.Minimum.z ) );
		var maximum = new Vector3Int(
			Math.Min( source.Maximum.x, other.Maximum.x ),
			Math.Min( source.Maximum.y, other.Maximum.y ),
			Math.Min( source.Maximum.z, other.Maximum.z ) );
		if ( minimum.x >= maximum.x || minimum.y >= maximum.y || minimum.z >= maximum.z )
		{
			destination.Add( source );
			return;
		}

		AddSlab( source.Lod, source.Minimum.x, minimum.x,
			source.Minimum.y, source.Maximum.y, source.Minimum.z, source.Maximum.z, destination );
		AddSlab( source.Lod, maximum.x, source.Maximum.x,
			source.Minimum.y, source.Maximum.y, source.Minimum.z, source.Maximum.z, destination );
		AddSlab( source.Lod, minimum.x, maximum.x,
			source.Minimum.y, minimum.y, source.Minimum.z, source.Maximum.z, destination );
		AddSlab( source.Lod, minimum.x, maximum.x,
			maximum.y, source.Maximum.y, source.Minimum.z, source.Maximum.z, destination );
		AddSlab( source.Lod, minimum.x, maximum.x,
			minimum.y, maximum.y, source.Minimum.z, minimum.z, destination );
		AddSlab( source.Lod, minimum.x, maximum.x,
			minimum.y, maximum.y, maximum.z, source.Maximum.z, destination );
	}

	private static void AddSlab(
		int lod,
		int minimumX,
		int maximumX,
		int minimumY,
		int maximumY,
		int minimumZ,
		int maximumZ,
		List<VoxelClipBoxBounds> destination )
	{
		if ( minimumX >= maximumX || minimumY >= maximumY || minimumZ >= maximumZ ) return;
		destination.Add( new VoxelClipBoxBounds(
			lod,
			new Vector3Int( minimumX, minimumY, minimumZ ),
			new Vector3Int( maximumX, maximumY, maximumZ ) ) );
	}

	private static IEnumerable<VoxelRenderRegionKey> EnumerateRegular(
		int lod,
		IReadOnlyList<VoxelClipBoxBounds> slabs )
	{
		for ( var slabIndex = 0; slabIndex < slabs.Count; slabIndex++ )
		{
			var slab = slabs[slabIndex];
			for ( var z = slab.Minimum.z; z < slab.Maximum.z; z++ )
			{
				for ( var y = slab.Minimum.y; y < slab.Maximum.y; y++ )
				{
					for ( var x = slab.Minimum.x; x < slab.Maximum.x; x++ )
						yield return VoxelRenderRegionKey.Regular( lod, new Vector3Int( x, y, z ) );
				}
			}
		}
	}

	private static int CountRegions( IReadOnlyList<VoxelClipBoxBounds> slabs )
	{
		var count = 0;
		for ( var index = 0; index < slabs.Count; index++ )
			count = checked( count + slabs[index].RegionCount );
		return count;
	}

	private static int CompareKeys( VoxelRenderRegionKey left, VoxelRenderRegionKey right )
	{
		var comparison = left.Lod.CompareTo( right.Lod );
		if ( comparison != 0 ) return comparison;
		comparison = left.MeshKind.CompareTo( right.MeshKind );
		if ( comparison != 0 ) return comparison;
		comparison = left.Face.CompareTo( right.Face );
		if ( comparison != 0 ) return comparison;
		comparison = left.Coordinate.z.CompareTo( right.Coordinate.z );
		if ( comparison != 0 ) return comparison;
		comparison = left.Coordinate.y.CompareTo( right.Coordinate.y );
		return comparison != 0 ? comparison : left.Coordinate.x.CompareTo( right.Coordinate.x );
	}
}

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

	public int CountAdjacencyViolations()
	{
		var violations = 0;
		var observedFaces = new int[MaximumLod + 1];
		foreach ( var transition in TransitionFaces )
		{
			observedFaces[transition.Lod]++;
			var owner = VoxelRenderRegionKey.Regular( transition.Lod, transition.Coordinate );
			if ( !ContainsActiveRegular( owner ) ) violations++;
			if ( !TryGetTransitionMask( owner, out var mask ) ||
				(mask & FaceBit( transition.Face )) == 0 ) violations++;

			for ( var second = 0; second < 2; second++ )
			{
				for ( var first = 0; first < 2; first++ )
				{
					var fineCoordinate = GetFineNeighborCoordinate(
						transition.Coordinate,
						transition.Face,
						first,
						second );
					var fine = VoxelRenderRegionKey.Regular( transition.Lod - 1, fineCoordinate );
					if ( !ContainsActiveRegular( fine ) ) violations++;
				}
			}
		}
		for ( var lod = 1; lod <= MaximumLod; lod++ )
			violations += Math.Abs( observedFaces[lod] - _levelCounts[lod].TransitionFaces );
		return violations;
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

	public bool OverlapsAtEveryLevel( VoxelClipBoxSelection other )
	{
		if ( other is null || other.MaximumLod != MaximumLod ) return false;
		for ( var lod = 0; lod <= MaximumLod; lod++ )
		{
			var left = _boxes[lod];
			var right = other._boxes[lod];
			if ( left.Minimum.x >= right.Maximum.x || left.Maximum.x <= right.Minimum.x ||
				left.Minimum.y >= right.Maximum.y || left.Maximum.y <= right.Minimum.y ||
				left.Minimum.z >= right.Maximum.z || left.Maximum.z <= right.Minimum.z )
			{
				return false;
			}
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

	internal VoxelClipBoxBounds GetCoveredChildBounds( int lod )
	{
		if ( lod <= 0 || lod > MaximumLod ) throw new ArgumentOutOfRangeException( nameof( lod ) );
		GetCoarseChildBounds( lod, out var minimum, out var maximum );
		return new VoxelClipBoxBounds( lod, minimum, maximum );
	}

	private static Vector3Int GetFineNeighborCoordinate(
		Vector3Int coarse,
		VoxelTransitionFace face,
		int first,
		int second )
	{
		return face switch
		{
			VoxelTransitionFace.PositiveX => new Vector3Int(
				checked( (coarse.x + 1) * 2 ), checked( coarse.y * 2 + first ), checked( coarse.z * 2 + second ) ),
			VoxelTransitionFace.NegativeX => new Vector3Int(
				checked( coarse.x * 2 - 1 ), checked( coarse.y * 2 + first ), checked( coarse.z * 2 + second ) ),
			VoxelTransitionFace.PositiveY => new Vector3Int(
				checked( coarse.x * 2 + second ), checked( (coarse.y + 1) * 2 ), checked( coarse.z * 2 + first ) ),
			VoxelTransitionFace.NegativeY => new Vector3Int(
				checked( coarse.x * 2 + second ), checked( coarse.y * 2 - 1 ), checked( coarse.z * 2 + first ) ),
			VoxelTransitionFace.PositiveZ => new Vector3Int(
				checked( coarse.x * 2 + first ), checked( coarse.y * 2 + second ), checked( (coarse.z + 1) * 2 ) ),
			VoxelTransitionFace.NegativeZ => new Vector3Int(
				checked( coarse.x * 2 + first ), checked( coarse.y * 2 + second ), checked( coarse.z * 2 - 1 ) ),
			_ => throw new ArgumentOutOfRangeException( nameof( face ) )
		};
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
