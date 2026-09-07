using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Canonical edited world state. Meshes and job snapshots never write these pages.
/// All coordinates and density limits in the edit format are owned here.
/// </summary>
internal sealed class TerrainField
{
	public const int FormatVersion = 1;
	public const int PageShift = 5;
	public const int SamplesPerPageAxis = 1 << PageShift;
	public const int PageMask = SamplesPerPageAxis - 1;
	public const int SamplesPerPage = SamplesPerPageAxis * SamplesPerPageAxis * SamplesPerPageAxis;
	public const float SampleSpacing = 16f;
	public const float MaximumWorldCoordinate = 1048576f;
	public const float MinimumBrushRadius = 32f;
	public const float MaximumBrushRadius = 1024f;
	public const float MaximumBrushStrength = 4096f;
	public const float MaximumCorrection = 65536f;
	public const int MaximumPages = 2048;
	public const int MaximumTransactionPages = 216;
	private readonly object _gate = new();
	private TerrainFieldSnapshot _current;

	public TerrainField( ProceduralTerrainSettings settings )
	{
		_current = new TerrainFieldSnapshot( settings, 0, new Dictionary<Vector3Int, TerrainFieldPage>(), Guid.NewGuid() );
	}

	public TerrainFieldSnapshot Current
	{
		get
		{
			lock ( _gate ) return _current;
		}
	}

	/// <summary>Only the manager's ordered mutation boundary commits prepared work.</summary>
	public bool TryCommit( TerrainFieldChange change )
	{
		if ( change is null ) throw new ArgumentNullException( nameof( change ) );
		lock ( _gate )
		{
			if ( !ReferenceEquals( _current, change.Source ) ) return false;
			_current = change.Result;
			return true;
		}
	}

	/// <summary>Stage absolute restored/received state through the same immutable mutation boundary.</summary>
	public static TerrainFieldChange PrepareReplacement( TerrainFieldSnapshot source, TerrainFieldSnapshot replacement,
		CancellationToken cancellation )
	{
		if ( source.Settings != replacement.Settings ) throw new ArgumentException( "Terrain generator settings differ." );
		var keys = new HashSet<Vector3Int>( source.Pages.Keys );
		keys.UnionWith( replacement.Pages.Keys );
		var pages = new Dictionary<Vector3Int, TerrainFieldPage>( source.Pages );
		var changed = new List<Vector3Int>();
		var samples = 0;
		var revision = checked( source.Revision + 1 );
		var low = new Vector3Int( int.MaxValue, int.MaxValue, int.MaxValue );
		var high = new Vector3Int( int.MinValue, int.MinValue, int.MinValue );
		foreach ( var key in keys )
		{
			cancellation.ThrowIfCancellationRequested();
			source.Pages.TryGetValue( key, out var previous );
			replacement.Pages.TryGetValue( key, out var restored );
			if ( ReferenceEquals( previous, restored ) ||
				(restored is null && previous.Minimum == 0f && previous.Maximum == 0f) ) continue;
			var pageChanged = false;
			for ( var index = 0; index < SamplesPerPage; index++ )
			{
				if ( (previous?.Sample( index ) ?? 0f) == (restored?.Sample( index ) ?? 0f) ) continue;
				pageChanged = true; samples++;
				var point = key * SamplesPerPageAxis + new Vector3Int( index & PageMask,
					(index >> PageShift) & PageMask, index >> (2 * PageShift) );
				low = new Vector3Int( Math.Min( low.x, point.x ), Math.Min( low.y, point.y ), Math.Min( low.z, point.z ) );
				high = new Vector3Int( Math.Max( high.x, point.x ), Math.Max( high.y, point.y ), Math.Max( high.z, point.z ) );
			}
			if ( !pageChanged ) continue;
			// Zero tombstones ensure removal invalidates old derived geometry as well.
			pages[key] = new TerrainFieldPage( revision, restored?.CopyValues() ?? new float[SamplesPerPage], previous, true );
			if ( pages.Count > MaximumPages ) throw new InvalidOperationException( "Terrain replacement exceeds the page budget including removals." );
			changed.Add( key );
		}
		if ( samples == 0 ) return new TerrainFieldChange( source,
			source.WorldId == replacement.WorldId ? source : new TerrainFieldSnapshot( source.Settings, revision, pages, replacement.WorldId ),
			default, 0, Array.Empty<Vector3Int>() );
		var bounds = new SdfWorldAabb( new Vector3( low.x - 1, low.y - 1, low.z - 1 ) * SampleSpacing,
			new Vector3( high.x + 1, high.y + 1, high.z + 1 ) * SampleSpacing );
		return new TerrainFieldChange( source, new TerrainFieldSnapshot( source.Settings, revision, pages, replacement.WorldId ), bounds, samples, changed.ToArray() );
	}

	public static bool IsValidBrush( Vector3 center, float radius, float strength )
	{
		return float.IsFinite( center.x ) && float.IsFinite( center.y ) && float.IsFinite( center.z ) &&
			float.IsFinite( radius ) && radius >= MinimumBrushRadius && radius <= MaximumBrushRadius &&
			float.IsFinite( strength ) && strength != 0f && MathF.Abs( strength ) <= MaximumBrushStrength &&
			MathF.Abs( center.x ) + radius + SampleSpacing <= MaximumWorldCoordinate &&
			MathF.Abs( center.y ) + radius + SampleSpacing <= MaximumWorldCoordinate &&
			MathF.Abs( center.z ) + radius + SampleSpacing <= MaximumWorldCoordinate;
	}

	/// <summary>
	/// Pure bounded bulk operation, suitable for the single mutation worker. Positive
	/// strength digs; negative strength builds. A stale prepared result cannot commit.
	/// </summary>
	public static TerrainFieldChange PrepareBrush( TerrainFieldSnapshot source, Vector3 center,
		float radius, float strength, CancellationToken cancellation )
	{
		if ( source is null ) throw new ArgumentNullException( nameof( source ) );
		if ( !IsValidBrush( center, radius, strength ) ) throw new ArgumentOutOfRangeException( nameof( radius ), "Invalid terrain brush." );
		var minimum = new Vector3Int(
			(int)MathF.Ceiling( (center.x - radius) / SampleSpacing ),
			(int)MathF.Ceiling( (center.y - radius) / SampleSpacing ),
			(int)MathF.Ceiling( (center.z - radius) / SampleSpacing ) );
		var maximum = new Vector3Int(
			(int)MathF.Floor( (center.x + radius) / SampleSpacing ),
			(int)MathF.Floor( (center.y + radius) / SampleSpacing ),
			(int)MathF.Floor( (center.z + radius) / SampleSpacing ) );
		var edits = new Dictionary<Vector3Int, float[]>();
		var changedMinimum = maximum;
		var changedMaximum = minimum;
		var changedSamples = 0;
		var newPageCount = 0;
		var inverseRadiusSquared = 1f / (radius * radius);
		for ( var z = minimum.z; z <= maximum.z; z++ )
		{
			cancellation.ThrowIfCancellationRequested();
			for ( var y = minimum.y; y <= maximum.y; y++ )
			{
				for ( var x = minimum.x; x <= maximum.x; x++ )
				{
					var delta = new Vector3( x * SampleSpacing - center.x, y * SampleSpacing - center.y, z * SampleSpacing - center.z );
					var squaredFraction = delta.LengthSquared * inverseRadiusSquared;
					if ( squaredFraction >= 1f ) continue;
					var pageKey = new Vector3Int( x >> PageShift, y >> PageShift, z >> PageShift );
					var index = (x & PageMask) + SamplesPerPageAxis * ((y & PageMask) + SamplesPerPageAxis * (z & PageMask));
					source.Pages.TryGetValue( pageKey, out var previousPage );
					edits.TryGetValue( pageKey, out var values );
					var previous = values is null ? previousPage?.Sample( index ) ?? 0f : values[index];
					var weight = 1f - squaredFraction;
					var next = Math.Clamp( previous + strength * weight * weight, -MaximumCorrection, MaximumCorrection );
					if ( next == previous ) continue;
					if ( values is null )
					{
						if ( edits.Count >= MaximumTransactionPages ) throw new InvalidOperationException( "Terrain transaction page budget exhausted." );
						if ( previousPage is null && source.PageCount + ++newPageCount > MaximumPages )
							throw new InvalidOperationException( "Terrain world page budget exhausted." );
						values = previousPage?.CopyValues() ?? new float[SamplesPerPage];
						edits.Add( pageKey, values );
					}
					values[index] = next;
					changedSamples++;
					changedMinimum = new Vector3Int( Math.Min( changedMinimum.x, x ), Math.Min( changedMinimum.y, y ), Math.Min( changedMinimum.z, z ) );
					changedMaximum = new Vector3Int( Math.Max( changedMaximum.x, x ), Math.Max( changedMaximum.y, y ), Math.Max( changedMaximum.z, z ) );
				}
			}
		}
		if ( changedSamples == 0 ) return new TerrainFieldChange( source, source, default, 0, Array.Empty<Vector3Int>() );
		var revision = checked( source.Revision + 1 );
		var pages = new Dictionary<Vector3Int, TerrainFieldPage>( source.Pages );
		var changedKeys = new Vector3Int[edits.Count];
		var keyIndex = 0;
		foreach ( var pair in edits )
		{
			cancellation.ThrowIfCancellationRequested();
			source.Pages.TryGetValue( pair.Key, out var previous );
			pages[pair.Key] = new TerrainFieldPage( revision, pair.Value, previous, true );
			changedKeys[keyIndex++] = pair.Key;
		}
		Array.Sort( changedKeys, ( a, b ) =>
		{
			var order = a.z.CompareTo( b.z );
			if ( order != 0 ) return order;
			order = a.y.CompareTo( b.y );
			return order != 0 ? order : a.x.CompareTo( b.x );
		} );
		// A changed lattice sample contributes to adjacent interpolation cells.
		var bounds = new SdfWorldAabb(
			new Vector3( changedMinimum.x - 1, changedMinimum.y - 1, changedMinimum.z - 1 ) * SampleSpacing,
			new Vector3( changedMaximum.x + 1, changedMaximum.y + 1, changedMaximum.z + 1 ) * SampleSpacing );
		return new TerrainFieldChange( source, new TerrainFieldSnapshot( source.Settings, revision, pages, source.WorldId ), bounds, changedSamples, changedKeys );
	}
}

internal sealed class TerrainFieldPage
{
	private readonly float[] _values;
	// 8-sample blocks separate mutation dependencies from 32-sample storage pages.
	private const int RevisionBlockShift = 3;
	private const int RevisionBlocksAxis = TerrainField.SamplesPerPageAxis >> RevisionBlockShift;
	private readonly int[] _blockRevisions;
	private readonly float[] _blockMinimums;
	private readonly float[] _blockMaximums;
	public int Revision { get; }
	public float Minimum { get; }
	public float Maximum { get; }

	// Takes exclusive ownership of the array. It is never exposed for writing.
	public TerrainFieldPage( int revision, float[] values, TerrainFieldPage previous = null, bool trackChanges = false )
	{
		if ( values.Length != TerrainField.SamplesPerPage ) throw new ArgumentException( "Invalid terrain page size." );
		Revision = revision;
		_values = values;
		_blockRevisions = new int[RevisionBlocksAxis * RevisionBlocksAxis * RevisionBlocksAxis];
		_blockMinimums = new float[_blockRevisions.Length];
		_blockMaximums = new float[_blockRevisions.Length];
		if ( trackChanges && previous?._blockRevisions is not null )
			Array.Copy( previous._blockRevisions, _blockRevisions, _blockRevisions.Length );
		else Array.Fill( _blockRevisions, trackChanges ? previous?.Revision ?? 0 : revision );
		var minimum = 0f;
		var maximum = 0f;
		for ( var index = 0; index < values.Length; index++ )
		{
			var value = values[index];
			var x = (index & TerrainField.PageMask) >> RevisionBlockShift;
			var y = ((index >> TerrainField.PageShift) & TerrainField.PageMask) >> RevisionBlockShift;
			var z = (index >> (2 * TerrainField.PageShift)) >> RevisionBlockShift;
			var block = x + RevisionBlocksAxis * (y + RevisionBlocksAxis * z);
			if ( trackChanges && value != (previous?.Sample( index ) ?? 0f) ) _blockRevisions[block] = revision;
			_blockMinimums[block] = MathF.Min( _blockMinimums[block], value );
			_blockMaximums[block] = MathF.Max( _blockMaximums[block], value );
			if ( !float.IsFinite( value ) || MathF.Abs( value ) > TerrainField.MaximumCorrection ) throw new ArgumentException( "Invalid terrain correction." );
			minimum = MathF.Min( minimum, value );
			maximum = MathF.Max( maximum, value );
		}
		Minimum = minimum;
		Maximum = maximum;
	}

	public int GetRange( Vector3Int key, SdfWorldAabb bounds, out float minimum, out float maximum, bool includeRevision )
	{
		// Existing hotloaded pages can lack derived metadata; retain conservative bounds.
		minimum = Minimum; maximum = Maximum;
		if ( _blockRevisions is null || _blockMinimums is null || _blockMaximums is null ) return Revision;
		var origin = new Vector3( key.x, key.y, key.z ) * (TerrainField.SampleSpacing * TerrainField.SamplesPerPageAxis);
		var blockSize = TerrainField.SampleSpacing * (1 << RevisionBlockShift);
		var low = (bounds.Minimum - origin - Vector3.One * TerrainField.SampleSpacing) / blockSize;
		var high = (bounds.Maximum - origin + Vector3.One * TerrainField.SampleSpacing) / blockSize;
		var minX = Math.Max( 0, (int)MathF.Floor( low.x ) );
		var minY = Math.Max( 0, (int)MathF.Floor( low.y ) );
		var minZ = Math.Max( 0, (int)MathF.Floor( low.z ) );
		var maxX = Math.Min( RevisionBlocksAxis - 1, (int)MathF.Floor( high.x ) );
		var maxY = Math.Min( RevisionBlocksAxis - 1, (int)MathF.Floor( high.y ) );
		var maxZ = Math.Min( RevisionBlocksAxis - 1, (int)MathF.Floor( high.z ) );
		if ( minX == 0 && minY == 0 && minZ == 0 && maxX == RevisionBlocksAxis - 1 &&
			maxY == RevisionBlocksAxis - 1 && maxZ == RevisionBlocksAxis - 1 ) return Revision;
		var revision = 0;
		minimum = 0f; maximum = 0f;
		for ( var z = minZ; z <= maxZ; z++ )
			for ( var y = minY; y <= maxY; y++ )
				for ( var x = minX; x <= maxX; x++ )
				{
					var block = x + RevisionBlocksAxis * (y + RevisionBlocksAxis * z);
					if ( includeRevision ) revision = Math.Max( revision, _blockRevisions[block] );
					minimum = MathF.Min( minimum, _blockMinimums[block] );
					maximum = MathF.Max( maximum, _blockMaximums[block] );
				}
		return revision;
	}

	public float Sample( int index ) => _values[index];
	public float[] CopyValues()
	{
		var result = new float[_values.Length];
		Array.Copy( _values, result, _values.Length );
		return result;
	}
	public void CopyTo( Span<float> destination ) => _values.AsSpan().CopyTo( destination );
}

internal sealed class TerrainFieldSnapshot
{
	// The dictionary is owned at construction and never mutated afterward.
	internal readonly IReadOnlyDictionary<Vector3Int, TerrainFieldPage> Pages;
	private Guid _worldId;
	public Guid WorldId
	{
		get
		{
			lock ( Pages )
			{
				if ( _worldId == Guid.Empty ) _worldId = Guid.NewGuid();
				return _worldId;
			}
		}
	}
	public ProceduralTerrainSettings Settings { get; }
	public int Revision { get; }
	public int PageCount => Pages.Count;
	public long PageBytes => (long)PageCount * TerrainField.SamplesPerPage * sizeof( float );

	internal TerrainFieldSnapshot( ProceduralTerrainSettings settings, int revision, Dictionary<Vector3Int, TerrainFieldPage> pages, Guid worldId )
	{
		_worldId = worldId;
		Settings = settings;
		Revision = revision;
		Pages = pages;
	}

	public float SampleGlobalCorrection( Vector3Int sample )
	{
		var key = new Vector3Int( sample.x >> TerrainField.PageShift, sample.y >> TerrainField.PageShift, sample.z >> TerrainField.PageShift );
		if ( !Pages.TryGetValue( key, out var page ) ) return 0f;
		return page.Sample( (sample.x & TerrainField.PageMask) + TerrainField.SamplesPerPageAxis * ((sample.y & TerrainField.PageMask) + TerrainField.SamplesPerPageAxis * (sample.z & TerrainField.PageMask)) );
	}

	/// <summary>Fill an aligned regular-mesher lattice by visiting only intersecting edit pages.</summary>
	public void CopyLatticeCorrections( Vector3Int origin, int step, int size, Span<float> destination )
	{
		if ( step < 1 || size < 1 || destination.Length != size * size * size ) throw new ArgumentException( "Invalid correction lattice." );
		destination.Clear();
		foreach ( var pair in Pages )
		{
			var page = pair.Value;
			if ( page.Minimum == 0f && page.Maximum == 0f ) continue;
			var pageOrigin = pair.Key * TerrainField.SamplesPerPageAxis;
			var lowX = Math.Clamp( (int)Math.Ceiling( (pageOrigin.x - origin.x) / (double)step ), 0, size );
			var lowY = Math.Clamp( (int)Math.Ceiling( (pageOrigin.y - origin.y) / (double)step ), 0, size );
			var lowZ = Math.Clamp( (int)Math.Ceiling( (pageOrigin.z - origin.z) / (double)step ), 0, size );
			var highX = Math.Clamp( (int)Math.Floor( (pageOrigin.x + TerrainField.PageMask - origin.x) / (double)step ) + 1, 0, size );
			var highY = Math.Clamp( (int)Math.Floor( (pageOrigin.y + TerrainField.PageMask - origin.y) / (double)step ) + 1, 0, size );
			var highZ = Math.Clamp( (int)Math.Floor( (pageOrigin.z + TerrainField.PageMask - origin.z) / (double)step ) + 1, 0, size );
			if ( lowX >= highX || lowY >= highY || lowZ >= highZ ) continue;
			for ( var z = lowZ; z < highZ; z++ )
			{
				var localZ = origin.z + z * step - pageOrigin.z;
				for ( var y = lowY; y < highY; y++ )
				{
					var localY = origin.y + y * step - pageOrigin.y;
					var sourceIndex = origin.x + lowX * step - pageOrigin.x +
						TerrainField.SamplesPerPageAxis * (localY + TerrainField.SamplesPerPageAxis * localZ);
					var targetIndex = lowX + size * (y + size * z);
					for ( var x = lowX; x < highX; x++, sourceIndex += step ) destination[targetIndex++] = page.Sample( sourceIndex );
				}
			}
		}
	}

	public float SampleCorrection( Vector3 position )
	{
		if ( Pages.Count == 0 ) return 0f;
		var p = position / TerrainField.SampleSpacing;
		var x = (int)MathF.Floor( p.x );
		var y = (int)MathF.Floor( p.y );
		var z = (int)MathF.Floor( p.z );
		var fx = p.x - x;
		var fy = p.y - y;
		var fz = p.z - z;
		if ( fx == 0f && fy == 0f && fz == 0f ) return SampleGlobalCorrection( new Vector3Int( x, y, z ) );
		var result = 0f;
		for ( var dz = 0; dz < 2; dz++ )
		{
			for ( var dy = 0; dy < 2; dy++ )
			{
				for ( var dx = 0; dx < 2; dx++ )
				{
					var weight = (dx == 0 ? 1f - fx : fx) * (dy == 0 ? 1f - fy : fy) * (dz == 0 ? 1f - fz : fz);
					if ( weight != 0f ) result += weight * SampleGlobalCorrection( new Vector3Int( x + dx, y + dy, z + dz ) );
				}
			}
		}
		return result;
	}

	public float SampleWorld( Vector3 position )
	{
		return ProceduralTerrainSdf.SampleWorld( position, Settings ) + SampleCorrection( position );
	}

	/// <summary>Conservative page intervals include all interpolation-support samples.</summary>
	public int GetCorrectionRange( SdfWorldAabb bounds, out float minimum, out float maximum, bool includeRevision = true )
	{
		minimum = 0f;
		maximum = 0f;
		if ( Pages.Count == 0 ) return 0;
		var pageSize = TerrainField.SampleSpacing * TerrainField.SamplesPerPageAxis;
		var low = new Vector3Int(
			(int)MathF.Floor( (bounds.Minimum.x - TerrainField.SampleSpacing) / pageSize ),
			(int)MathF.Floor( (bounds.Minimum.y - TerrainField.SampleSpacing) / pageSize ),
			(int)MathF.Floor( (bounds.Minimum.z - TerrainField.SampleSpacing) / pageSize ) );
		var high = new Vector3Int(
			(int)MathF.Floor( (bounds.Maximum.x + TerrainField.SampleSpacing) / pageSize ),
			(int)MathF.Floor( (bounds.Maximum.y + TerrainField.SampleSpacing) / pageSize ),
			(int)MathF.Floor( (bounds.Maximum.z + TerrainField.SampleSpacing) / pageSize ) );
		var revision = 0;
		var volume = ((long)high.x - low.x + 1) * ((long)high.y - low.y + 1) * ((long)high.z - low.z + 1);
		if ( volume <= Pages.Count )
		{
			for ( var z = low.z; z <= high.z; z++ )
			{
				for ( var y = low.y; y <= high.y; y++ )
				{
					for ( var x = low.x; x <= high.x; x++ )
					{
						if ( !Pages.TryGetValue( new Vector3Int( x, y, z ), out var page ) ) continue;
						revision = Math.Max( revision, page.GetRange( new Vector3Int( x, y, z ), bounds, out var pageMinimum, out var pageMaximum, includeRevision ) );
						minimum = MathF.Min( minimum, pageMinimum );
						maximum = MathF.Max( maximum, pageMaximum );
					}
				}
			}
		}
		else
		{
			foreach ( var pair in Pages )
			{
				var key = pair.Key;
				if ( key.x < low.x || key.x > high.x || key.y < low.y || key.y > high.y || key.z < low.z || key.z > high.z ) continue;
				revision = Math.Max( revision, pair.Value.GetRange( key, bounds, out var pageMinimum, out var pageMaximum, includeRevision ) );
				minimum = MathF.Min( minimum, pageMinimum );
				maximum = MathF.Max( maximum, pageMaximum );
			}
		}
		return revision;
	}

	public ChunkDensityRange GetDensityRange( SdfWorldAabb bounds, float cellSize )
	{
		var range = ProceduralTerrainSdf.GetConservativeDensityRange( bounds, cellSize, Settings );
		GetCorrectionRange( bounds, out var correctionMinimum, out var correctionMaximum, includeRevision: false );
		if ( correctionMinimum == 0f && correctionMaximum == 0f ) return range;
		var minimum = range.MinimumDensity + correctionMinimum;
		var maximum = range.MaximumDensity + correctionMaximum;
		return new ChunkDensityRange( minimum, maximum,
			minimum > 0f ? ChunkDensityClassification.DefinitelyAir :
			maximum < 0f ? ChunkDensityClassification.DefinitelySolid : ChunkDensityClassification.PotentiallySurfaceContaining );
	}
}

internal sealed record TerrainFieldChange( TerrainFieldSnapshot Source, TerrainFieldSnapshot Result,
	SdfWorldAabb AffectedBounds, int ChangedSamples, Vector3Int[] ChangedPages )
{
	// Descriptor revisions conservatively cover whole page ownership plus interpolation support.
	public SdfWorldAabb DependencyPageBounds
	{
		get
		{
			if ( ChangedPages.Length == 0 ) return default;
			var low = ChangedPages[0]; var high = low;
			foreach ( var key in ChangedPages )
			{
				low = new Vector3Int( Math.Min( low.x, key.x ), Math.Min( low.y, key.y ), Math.Min( low.z, key.z ) );
				high = new Vector3Int( Math.Max( high.x, key.x ), Math.Max( high.y, key.y ), Math.Max( high.z, key.z ) );
			}
			var size = TerrainField.SampleSpacing * TerrainField.SamplesPerPageAxis;
			return new SdfWorldAabb( new Vector3( low.x, low.y, low.z ) * size - Vector3.One * TerrainField.SampleSpacing,
				new Vector3( high.x + 1, high.y + 1, high.z + 1 ) * size + Vector3.One * TerrainField.SampleSpacing );
		}
	}

	public static bool Intersects( SdfWorldAabb a, SdfWorldAabb b ) =>
		a.Minimum.x <= b.Maximum.x && a.Maximum.x >= b.Minimum.x &&
		a.Minimum.y <= b.Maximum.y && a.Maximum.y >= b.Minimum.y &&
		a.Minimum.z <= b.Maximum.z && a.Maximum.z >= b.Minimum.z;
}
