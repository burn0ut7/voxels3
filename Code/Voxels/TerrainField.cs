using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Canonical edited world state. Meshes and job snapshots never write these pages.
/// All coordinates and density limits in the edit format are owned here.
/// </summary>
internal sealed class TerrainField
{
	public const int FormatVersion = 2;
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
	public const long MaximumSampleBytes = 512L * 1024 * 1024;
	private readonly object _gate = new();
	private TerrainFieldSnapshot _current;
	private TerrainFieldStore.Checkpoint _checkpoint;
	private readonly Queue<ReadRequest> _reads = new();
	private readonly HashSet<TerrainFieldPage> _pendingReads = new();
	private readonly HashSet<TerrainFieldPage> _failedReads = new();
	public string ReadFailure { get; private set; }
	public bool ReadCapacityDeferred { get; private set; }
	public long LoadedPages { get; private set; }
	public long EvictedPages { get; private set; }
	public long StaleReadCompletions { get; private set; }
	public long ReadCapacityDeferrals { get; private set; }
	public int PendingReads { get { lock ( _gate ) return _pendingReads.Count; } }
	internal readonly record struct ReadRequest( Vector3Int Coordinate, TerrainFieldPage Page, int Epoch,
		string Root, TerrainFieldStore.Page Stored );

	internal void RequestPage( Vector3Int coordinate, TerrainFieldPage page, int epoch )
	{
		lock ( _gate )
		{
			if ( epoch != _current.Epoch || !_current.Pages.TryGetValue( coordinate, out var current ) ||
				!ReferenceEquals( current, page ) || page.IsResident || _failedReads.Contains( page ) || !_pendingReads.Add( page ) ) return;
			var stored = page.Stored;
			if ( stored.Page is null )
			{
				_pendingReads.Remove( page ); _failedReads.Add( page );
				ReadFailure = "Nonresident terrain page has no saved version.";
				return;
			}
			_reads.Enqueue( new ReadRequest( coordinate, page, epoch, stored.Root, stored.Page ) );
		}
	}

	public ReadRequest[] TakeReadBatch( out TerrainFieldPage.SampleReservation reservation )
	{
		lock ( _gate )
		{
			reservation = null;
			ReadCapacityDeferred = false;
			if ( _reads.Count == 0 ) return Array.Empty<ReadRequest>();
			if ( !TerrainFieldPage.TryReserveSamples( Math.Min( 8, _reads.Count ), out reservation ) )
			{
				ReadCapacityDeferred = true;
				ReadCapacityDeferrals++;
				return Array.Empty<ReadRequest>();
			}
			var batch = new List<ReadRequest>( 8 );
			while ( batch.Count < 8 && _reads.TryDequeue( out var request ) )
			{
				if ( request.Epoch == _current.Epoch && _current.Pages.TryGetValue( request.Coordinate, out var page ) &&
					ReferenceEquals( page, request.Page ) && !page.IsResident ) batch.Add( request );
				else _pendingReads.Remove( request.Page );
			}
			if ( batch.Count == 0 ) { reservation.Dispose(); reservation = null; }
			return batch.ToArray();
		}
	}

	public bool CompleteRead( ReadRequest request, TerrainFieldPage loaded, string error )
	{
		lock ( _gate )
		{
			_pendingReads.Remove( request.Page );
			if ( request.Epoch != _current.Epoch || !_current.Pages.TryGetValue( request.Coordinate, out var page ) ||
				!ReferenceEquals( page, request.Page ) )
			{
				StaleReadCompletions++;
				return false;
			}
			if ( error is not null ) { _failedReads.Add( page ); ReadFailure = error; return false; }
			if ( !page.InstallResidentSamples( loaded ) ) return false;
			LoadedPages++;
			return true;
		}
	}

	public void SweepPage( Vector3Int coordinate, TerrainFieldPage page, int epoch, bool required, long now )
	{
		lock ( _gate )
		{
			if ( epoch != _current.Epoch || !_current.Pages.TryGetValue( coordinate, out var current ) || !ReferenceEquals( page, current ) ) return;
			if ( page.TryExpireResidentSamples( required || _pendingReads.Contains( page ), now ) ) EvictedPages++;
		}
	}
	public TerrainFieldStore.Checkpoint Checkpoint { get { lock ( _gate ) return _checkpoint; } }

	public void MarkSaved( TerrainFieldStore.Checkpoint checkpoint, TerrainFieldSnapshot source )
	{
		lock ( _gate )
		{
			if ( checkpoint.Identity.WorldId != _current.WorldId || checkpoint.Identity.Epoch != _current.Epoch ) return;
			foreach ( var pair in checkpoint.Pages )
			{
				var savedPage = source.Pages[pair.Key];
				savedPage.AttachStored( checkpoint.Root, pair.Value );
				if ( !_current.Pages.TryGetValue( pair.Key, out var currentPage ) || !ReferenceEquals( currentPage, savedPage ) )
					savedPage.ReleaseResidentSamples();
			}
			_checkpoint = checkpoint;
		}
	}

	public TerrainField( ProceduralTerrainSettings settings )
	{
		_current = new TerrainFieldSnapshot( settings, 0, new Dictionary<Vector3Int, TerrainFieldPage>(), Guid.NewGuid(),
			owner: new WeakReference<TerrainField>( this ) );
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
		if ( change.Result.IsRegional ) throw new InvalidOperationException( "A regional reader cannot replace the authoritative field." );
		lock ( _gate )
		{
			if ( !ReferenceEquals( _current, change.Source ) ) return false;
			if ( _current.WorldId != change.Result.WorldId || _current.Epoch != change.Result.Epoch )
			{
				_checkpoint = null; _reads.Clear(); _pendingReads.Clear(); _failedReads.Clear(); ReadFailure = null;
			}
			// Old metadata views must not keep obsolete saved payloads resident.
			// Captured sample readers own separate references; unsaved versions stay
			// available until their save completes or their final owner releases them.
			var retiredKeys = _current.Epoch != change.Result.Epoch ? _current.Pages.Keys : change.ChangedPages;
			foreach ( var key in retiredKeys )
			{
				if ( _current.Pages.TryGetValue( key, out var previous ) &&
					(!change.Result.Pages.TryGetValue( key, out var next ) || !ReferenceEquals( previous, next )) )
					previous.ReleaseResidentSamples();
			}
			_current = change.Result;
			if ( change.Checkpoint is not null )
				_checkpoint = change.Checkpoint with { Identity = new TerrainFieldIdentity( _current.Settings, _current.Revision, _current.WorldId, _current.Epoch ) };
			return true;
		}
	}

	/// <summary>Stage absolute restored/received state through the same immutable mutation boundary.</summary>
	public static TerrainFieldChange PrepareReplacement( TerrainFieldSnapshot source, TerrainFieldSnapshot replacement,
		CancellationToken cancellation, bool resetEpoch = false, TerrainFieldStore.Checkpoint checkpoint = null )
	{
		if ( source.Settings != replacement.Settings ) throw new ArgumentException( "Terrain generator settings differ." );
		if ( source.IsRegional || replacement.IsRegional ) throw new ArgumentException( "A regional reader cannot author replacement state." );
		if ( replacement.PageCount > MaximumPages ) throw new InvalidOperationException( "Terrain world page budget exhausted." );
		var keys = new HashSet<Vector3Int>( source.Pages.Keys );
		keys.UnionWith( replacement.Pages.Keys );
		var pages = new Dictionary<Vector3Int, TerrainFieldPage>( replacement.Pages );
		var changed = new List<Vector3Int>();
		var changedRegions = new List<SdfWorldAabb>();
		var samples = 0;
		var epoch = resetEpoch || source.WorldId != replacement.WorldId ? checked( source.Epoch + 1 ) : source.Epoch;
		var low = new Vector3Int( int.MaxValue, int.MaxValue, int.MaxValue );
		var high = new Vector3Int( int.MinValue, int.MinValue, int.MinValue );
		foreach ( var key in keys )
		{
			cancellation.ThrowIfCancellationRequested();
			source.Pages.TryGetValue( key, out var previous );
			replacement.Pages.TryGetValue( key, out var restored );
			if ( ReferenceEquals( previous, restored ) ||
				(restored is null && previous.Minimum == 0f && previous.Maximum == 0f && !previous.HasMaterials) ) continue;
			var previousReader = previous is null ? null : TerrainFieldStore.PinForRead( previous );
			var restoredReader = restored is null ? null : TerrainFieldStore.PinForRead( restored );
			var pageChanged = false;
			var pageLow = new Vector3Int( int.MaxValue, int.MaxValue, int.MaxValue );
			var pageHigh = new Vector3Int( int.MinValue, int.MinValue, int.MinValue );
			for ( var index = 0; index < SamplesPerPage; index++ )
			{
				if ( (previousReader?.Sample( index ) ?? 0f) == (restoredReader?.Sample( index ) ?? 0f) &&
					(previousReader?.Material( index ) ?? 0) == (restoredReader?.Material( index ) ?? 0) ) continue;
				pageChanged = true; samples++;
				var point = key * SamplesPerPageAxis + new Vector3Int( index & PageMask,
					(index >> PageShift) & PageMask, index >> (2 * PageShift) );
				low = new Vector3Int( Math.Min( low.x, point.x ), Math.Min( low.y, point.y ), Math.Min( low.z, point.z ) );
				high = new Vector3Int( Math.Max( high.x, point.x ), Math.Max( high.y, point.y ), Math.Max( high.z, point.z ) );
				pageLow = new Vector3Int( Math.Min( pageLow.x, point.x ), Math.Min( pageLow.y, point.y ), Math.Min( pageLow.z, point.z ) );
				pageHigh = new Vector3Int( Math.Max( pageHigh.x, point.x ), Math.Max( pageHigh.y, point.y ), Math.Max( pageHigh.z, point.z ) );
			}
			if ( !pageChanged ) continue;
			changed.Add( key );
			changedRegions.Add( new SdfWorldAabb( new Vector3( pageLow.x - 1, pageLow.y - 1, pageLow.z - 1 ) * SampleSpacing,
				new Vector3( pageHigh.x + 1, pageHigh.y + 1, pageHigh.z + 1 ) * SampleSpacing ) );
		}
		var result = new TerrainFieldSnapshot( source.Settings, replacement.Revision, pages, replacement.WorldId, epoch: epoch, owner: source.Owner );
		var bounds = samples == 0 ? default : new SdfWorldAabb( new Vector3( low.x - 1, low.y - 1, low.z - 1 ) * SampleSpacing,
			new Vector3( high.x + 1, high.y + 1, high.z + 1 ) * SampleSpacing );
		return new TerrainFieldChange( source, result, bounds, samples, changed.ToArray() )
		{
			Checkpoint = checkpoint, ReplacementSampleBounds = changedRegions.ToArray()
		};
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
	public static TerrainFieldChange PrepareBrush( TerrainFieldSnapshot source, TerrainFieldSnapshot reader, Vector3 center,
		float radius, float strength, CancellationToken cancellation, TerrainFieldPage.SampleReservation reservation )
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
		var materials = new Dictionary<Vector3Int, ushort[]>();
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
					reader.Pages.TryGetValue( pageKey, out var previousPage );
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
						values = previousPage?.CopyValues( reservation ) ?? TerrainFieldPage.AllocateValues( reservation );
						edits.Add( pageKey, values );
						materials.Add( pageKey, previousPage?.CopyMaterials() ?? new ushort[SamplesPerPage] );
					}
					values[index] = next;
					// Building authors dirt, even when it restores the exact procedural density.
					// Digging retains the surface material on the smooth remainder.
					if ( strength < 0f ) materials[pageKey][index] = VoxelMaterials.Dirt;
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
			reader.Pages.TryGetValue( pair.Key, out var previous );
			pages[pair.Key] = new TerrainFieldPage( revision, pair.Value, previous, true, materials: materials[pair.Key] );
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
		return new TerrainFieldChange( source, new TerrainFieldSnapshot( source.Settings, revision, pages, source.WorldId,
			epoch: source.Epoch, owner: source.Owner ), bounds, changedSamples, changedKeys );
	}
}

internal sealed class TerrainFieldPage
{
	private float[] _values;
	private WeakReference<float[]> _releasedValues;
	private ushort[] _materials;
	private WeakReference<ushort[]> _releasedMaterials;
	public bool HasMaterials { get; }
	private static long _reusedSamplePages;
	public static long ReusedSamplePages => Interlocked.Read( ref _reusedSamplePages );
	private readonly object _residencyGate = new();
	private readonly bool _isReader;
	private long _lastRequiredAt = System.Diagnostics.Stopwatch.GetTimestamp();
	private string _storedRoot;
	private TerrainFieldStore.Page _storedPage;
	public (string Root, TerrainFieldStore.Page Page) Stored
	{
		get { lock ( _residencyGate ) return (_storedRoot, _storedPage); }
	}

	public void AttachStored( string root, TerrainFieldStore.Page page )
	{
		if ( page.Revision != Revision ) throw new InvalidOperationException( "Saved terrain revision does not match the page." );
		lock ( _residencyGate ) { _storedRoot = root; _storedPage = page; }
	}
	private static readonly object AllocationGate = new();
	private static readonly List<WeakReference<float[]>> Allocations = new();
	private static int _reservedPages;
	public static long ReservedSampleBytes { get { lock ( AllocationGate ) return _reservedPages * SampleBytes; } }

	internal sealed class SampleReservation : IDisposable
	{
		internal int Remaining;
		internal SampleReservation( int pages ) { Remaining = pages; }
		public void Dispose()
		{
			lock ( AllocationGate ) { _reservedPages -= Remaining; Remaining = 0; }
		}
	}

	public static bool TryReserveSamples( int pages, out SampleReservation reservation )
	{
		if ( pages < 1 || pages > TerrainField.MaximumTransactionPages ) throw new ArgumentOutOfRangeException( nameof( pages ) );
		lock ( AllocationGate )
		{
			reservation = null;
			if ( (Allocations.Count + _reservedPages + pages) * SampleBytes > TerrainField.MaximumSampleBytes )
				Allocations.RemoveAll( reference => !reference.TryGetTarget( out _ ) );
			if ( (Allocations.Count + _reservedPages + pages) * SampleBytes > TerrainField.MaximumSampleBytes ) return false;
			reservation = new SampleReservation( pages );
			_reservedPages += pages;
			return true;
		}
	}
	// Reserve the complete density/material payload even for legacy density-only pages.
	public const long SampleBytes = (long)TerrainField.SamplesPerPage * (sizeof( float ) + sizeof( ushort ));
	public bool IsResident { get { lock ( _residencyGate ) return _values is not null; } }

	// All dense correction arrays enter here, including codec and mutation staging.
	// Weak records count retained versions without prolonging their lifetime.
	public static float[] AllocateValues( SampleReservation reservation = null )
	{
		lock ( AllocationGate )
		{
			if ( reservation is not null )
			{
				if ( reservation.Remaining == 0 ) throw new InvalidOperationException( "Terrain sample reservation exhausted." );
				reservation.Remaining--; _reservedPages--;
			}
			else
			{
				if ( (Allocations.Count + _reservedPages + 1) * SampleBytes > TerrainField.MaximumSampleBytes )
					Allocations.RemoveAll( reference => !reference.TryGetTarget( out _ ) );
				if ( (Allocations.Count + _reservedPages + 1) * SampleBytes > TerrainField.MaximumSampleBytes )
					throw new InvalidOperationException( "Terrain sample memory budget exhausted." );
			}
			var values = new float[TerrainField.SamplesPerPage];
			Allocations.Add( new WeakReference<float[]>( values ) );
			return values;
		}
	}

	public static long RetainedSampleBytes
	{
		get
		{
			lock ( AllocationGate )
			{
				Allocations.RemoveAll( reference => !reference.TryGetTarget( out _ ) );
				return Allocations.Count * SampleBytes;
			}
		}
	}
	// 8-sample blocks separate mutation dependencies from 32-sample storage pages.
	private const int RevisionBlockShift = 3;
	private const int RevisionBlocksAxis = TerrainField.SamplesPerPageAxis >> RevisionBlockShift;
	private readonly int[] _blockRevisions;
	private readonly float[] _blockMinimums;
	private readonly float[] _blockMaximums;
	public int Revision { get; }
	public float Minimum { get; }
	public float Maximum { get; }

	// Resident pages take exclusive ownership of the array. Metadata-only
	// validation derives ranges without retaining its caller-owned scratch.
	public TerrainFieldPage( int revision, float[] values, TerrainFieldPage previous = null, bool trackChanges = false, bool retainSamples = true, ushort[] materials = null )
	{
		if ( values.Length != TerrainField.SamplesPerPage ) throw new ArgumentException( "Invalid terrain page size." );
		if ( materials is not null && materials.Length != values.Length ) throw new ArgumentException( "Invalid material page size." );
		Revision = revision;
		_values = retainSamples ? values : null;
		_materials = retainSamples ? materials : null;
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
			var material = materials?[index] ?? 0;
			if ( material != 0 && material != VoxelMaterials.Dirt ) throw new ArgumentException( "Unsupported placed material." );
			HasMaterials |= material != 0;
			var x = (index & TerrainField.PageMask) >> RevisionBlockShift;
			var y = ((index >> TerrainField.PageShift) & TerrainField.PageMask) >> RevisionBlockShift;
			var z = (index >> (2 * TerrainField.PageShift)) >> RevisionBlockShift;
			var block = x + RevisionBlocksAxis * (y + RevisionBlocksAxis * z);
			if ( trackChanges && (value != (previous?.Sample( index ) ?? 0f) || material != (previous?.Material( index ) ?? 0)) ) _blockRevisions[block] = revision;
			_blockMinimums[block] = MathF.Min( _blockMinimums[block], value );
			_blockMaximums[block] = MathF.Max( _blockMaximums[block], value );
			if ( !float.IsFinite( value ) || MathF.Abs( value ) > TerrainField.MaximumCorrection ) throw new ArgumentException( "Invalid terrain correction." );
			minimum = MathF.Min( minimum, value );
			maximum = MathF.Max( maximum, value );
		}
		Minimum = minimum;
		Maximum = maximum;
	}

	private TerrainFieldPage( TerrainFieldPage source, float[] values )
	{
		_values = values;
		_materials = source._materials;
		HasMaterials = source.HasMaterials;
		_isReader = true;
		Revision = source.Revision;
		Minimum = source.Minimum;
		Maximum = source.Maximum;
		_blockRevisions = source._blockRevisions;
		_blockMinimums = source._blockMinimums;
		_blockMaximums = source._blockMaximums;
		_storedRoot = source._storedRoot;
		_storedPage = source._storedPage;
	}

	public bool TryPin( out TerrainFieldPage reader )
	{
		lock ( _residencyGate )
		{
			_lastRequiredAt = System.Diagnostics.Stopwatch.GetTimestamp();
			// A saved immutable version may still be alive in readers or awaiting GC.
			// Reuse those exact samples without keeping an evicted array alive.
			ushort[] releasedMaterials = null;
			if ( _values is null && _releasedValues is not null && _releasedValues.TryGetTarget( out var released ) &&
				(!HasMaterials || (_releasedMaterials is not null && _releasedMaterials.TryGetTarget( out releasedMaterials ))) )
			{
				_values = released;
				_materials = releasedMaterials;
				_releasedMaterials = null;
				_releasedValues = null;
				Interlocked.Increment( ref _reusedSamplePages );
			}
			reader = _values is null ? null : _isReader ? this : new TerrainFieldPage( this, _values );
			return reader is not null;
		}
	}

	// Residency is the only mutable property of a canonical page version. Readers
	// own their captured array reference and cannot be evicted by the directory.
	public bool ReleaseResidentSamples()
	{
		if ( _isReader ) throw new InvalidOperationException( "Cannot evict a terrain reader." );
		lock ( _residencyGate )
		{
			if ( _storedPage is null ) return false;
			if ( _values is null ) return false;
			_releasedValues = new WeakReference<float[]>( _values );
			_releasedMaterials = _materials is null ? null : new WeakReference<ushort[]>( _materials );
			_materials = null;
			_values = null;
			return true;
		}
	}

	public bool InstallResidentSamples( TerrainFieldPage loaded )
	{
		if ( _isReader || loaded.Revision != Revision || loaded.Minimum != Minimum || loaded.Maximum != Maximum || loaded.HasMaterials != HasMaterials || !loaded.TryPin( out var reader ) )
			throw new InvalidOperationException( "Loaded terrain samples do not match the requested page version." );
		lock ( _residencyGate )
		{
			if ( _values is not null ) return false;
			_values = reader._values;
			_materials = reader._materials;
			_releasedMaterials = null;
			_releasedValues = null;
			_lastRequiredAt = System.Diagnostics.Stopwatch.GetTimestamp();
			return true;
		}
	}

	public bool TryExpireResidentSamples( bool required, long now )
	{
		lock ( _residencyGate )
		{
			if ( required ) { _lastRequiredAt = now; return false; }
			if ( _isReader || _values is null || _storedPage is null ||
				System.Diagnostics.Stopwatch.GetElapsedTime( _lastRequiredAt, now ).TotalSeconds < 5 ) return false;
			_releasedValues = new WeakReference<float[]>( _values );
			_releasedMaterials = _materials is null ? null : new WeakReference<ushort[]>( _materials );
			_materials = null;
			_values = null;
			return true;
		}
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

	public float Sample( int index ) => (_values ?? throw new InvalidOperationException( "Terrain page is nonresident; acquire it before sampling." ))[index];
	public ushort Material( int index )
	{
		if ( _values is null ) throw new InvalidOperationException( "Terrain page is nonresident; acquire it before sampling." );
		return _materials?[index] ?? 0;
	}
	public ushort[] CopyMaterials()
	{
		if ( _values is null ) throw new InvalidOperationException( "Terrain page is nonresident; acquire it before copying." );
		var result = new ushort[TerrainField.SamplesPerPage];
		if ( _materials is not null ) Array.Copy( _materials, result, result.Length );
		return result;
	}
	public float[] CopyValues( SampleReservation reservation = null )
	{
		var values = _values ?? throw new InvalidOperationException( "Terrain page is nonresident; acquire it before copying." );
		var result = AllocateValues( reservation );
		Array.Copy( values, result, values.Length );
		return result;
	}
}

internal sealed partial class TerrainFieldSnapshot
{
	// The dictionary is owned at construction and never mutated afterward.
	private readonly Dictionary<Vector3Int, TerrainFieldPage> _pages;
	internal IReadOnlyDictionary<Vector3Int, TerrainFieldPage> Pages => _pages;
	internal Dictionary<Vector3Int, TerrainFieldPage>.Enumerator GetPageEnumerator() => _pages.GetEnumerator();
	private readonly TerrainPageIndex _pageIndex;
	private readonly Vector3Int? _regionMinimum;
	private readonly Vector3Int? _regionMaximum;
	private readonly bool _pinsSamples;
	internal WeakReference<TerrainField> Owner { get; }
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
	internal bool IsRegional => _regionMinimum.HasValue;
	// Local cache/job lifetime, deliberately absent from stored/network history.
	public int Epoch { get; }
	public long PageBytes => (long)PageCount * TerrainFieldPage.SampleBytes;
	public long ResidentPageBytes => Pages.Values.Count( page => page.IsResident ) * TerrainFieldPage.SampleBytes;

	internal TerrainFieldSnapshot( ProceduralTerrainSettings settings, int revision, Dictionary<Vector3Int, TerrainFieldPage> pages,
		Guid worldId, Vector3Int? regionMinimum = null, Vector3Int? regionMaximum = null, int epoch = 0,
		WeakReference<TerrainField> owner = null, bool pinsSamples = false, TerrainPageIndex pageIndex = null )
	{
		_worldId = worldId;
		Settings = settings;
		Revision = revision;
		_pages = pages;
		_pageIndex = pageIndex ?? (pages.Count == 0 ? TerrainPageIndex.Empty : new TerrainPageIndex( pages.Keys ));
		_regionMinimum = regionMinimum;
		_regionMaximum = regionMaximum;
		Epoch = epoch;
		Owner = owner;
		_pinsSamples = pinsSamples;
	}

	/// <summary>
	/// Retain only this reader's immutable page dependencies. This does not author a
	/// new field revision; the owner keeps the complete world directory separately.
	/// Bounds include the consumer's normal/mesh halo; one lattice sample here
	/// accounts for the correction field's interpolation support.
	/// </summary>
	public TerrainFieldSnapshot CaptureRegion( SdfWorldAabb bounds, bool pinSamples = true )
	{
		if ( !TryCaptureRegion( bounds, out var reader, pinSamples ) ) throw new InvalidOperationException( "Terrain region is waiting for stored pages." );
		return reader;
	}

	public bool TryCaptureRegion( SdfWorldAabb bounds, out TerrainFieldSnapshot reader, bool pinSamples = true )
	{
		reader = this;
		if ( Pages.Count == 0 && !_regionMinimum.HasValue ) return true;
		var size = TerrainField.SampleSpacing * TerrainField.SamplesPerPageAxis;
		var minimum = bounds.Minimum - Vector3.One * TerrainField.SampleSpacing;
		var maximum = bounds.Maximum + Vector3.One * TerrainField.SampleSpacing;
		var low = new Vector3Int( (int)MathF.Floor( minimum.x / size ),
			(int)MathF.Floor( minimum.y / size ), (int)MathF.Floor( minimum.z / size ) );
		var high = new Vector3Int( (int)MathF.Floor( maximum.x / size ),
			(int)MathF.Floor( maximum.y / size ), (int)MathF.Floor( maximum.z / size ) );
		RequirePageRange( low, high );
		if ( _regionMinimum == low && _regionMaximum == high && (!pinSamples || _pinsSamples) ) return true;
		var pages = new Dictionary<Vector3Int, TerrainFieldPage>();
		var ready = true;
		foreach ( var key in _pageIndex.Query( low, high ) )
		{
			if ( !Pages.TryGetValue( key, out var page ) ) continue;
			if ( !pinSamples ) pages.Add( key, page );
			else if ( page.TryPin( out var pageReader ) ) pages.Add( key, pageReader );
			else
			{
				ready = false;
				if ( Owner is not null && Owner.TryGetTarget( out var owner ) ) owner.RequestPage( key, page, Epoch );
			}
		}
		reader = ready ? new TerrainFieldSnapshot( Settings, Revision, pages, WorldId, low, high, Epoch, Owner, pinSamples, _pageIndex ) : null;
		return ready;
	}

	private void RequirePageRange( Vector3Int low, Vector3Int high )
	{
		if ( !_regionMinimum.HasValue ) return;
		var minimum = _regionMinimum.Value;
		var maximum = _regionMaximum.Value;
		if ( low.x < minimum.x || low.y < minimum.y || low.z < minimum.z ||
			high.x > maximum.x || high.y > maximum.y || high.z > maximum.z )
			throw new InvalidOperationException( "Terrain reader exceeded its acquired page region." );
	}

	public float SampleGlobalCorrection( Vector3Int sample )
	{
		var key = new Vector3Int( sample.x >> TerrainField.PageShift, sample.y >> TerrainField.PageShift, sample.z >> TerrainField.PageShift );
		RequirePageRange( key, key );
		if ( !Pages.TryGetValue( key, out var page ) ) return 0f;
		return page.Sample( (sample.x & TerrainField.PageMask) + TerrainField.SamplesPerPageAxis * ((sample.y & TerrainField.PageMask) + TerrainField.SamplesPerPageAxis * (sample.z & TerrainField.PageMask)) );
	}

	/// <summary>Fill an aligned regular-mesher lattice by visiting only intersecting edit pages.</summary>
	public void CopyLatticeCorrections( Vector3Int origin, int step, int size, Span<float> destination )
	{
		if ( step < 1 || size < 1 || destination.Length != size * size * size ) throw new ArgumentException( "Invalid correction lattice." );
		var last = origin + new Vector3Int( (size - 1) * step, (size - 1) * step, (size - 1) * step );
		RequirePageRange( new Vector3Int( origin.x >> TerrainField.PageShift, origin.y >> TerrainField.PageShift, origin.z >> TerrainField.PageShift ),
			new Vector3Int( last.x >> TerrainField.PageShift, last.y >> TerrainField.PageShift, last.z >> TerrainField.PageShift ) );
		destination.Clear();
		foreach ( var pair in _pages )
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
		if ( Pages.Count == 0 && !_regionMinimum.HasValue ) return 0f;
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
		if ( Pages.Count == 0 && !_regionMinimum.HasValue ) return 0;
		var pageSize = TerrainField.SampleSpacing * TerrainField.SamplesPerPageAxis;
		var low = new Vector3Int(
			(int)MathF.Floor( (bounds.Minimum.x - TerrainField.SampleSpacing) / pageSize ),
			(int)MathF.Floor( (bounds.Minimum.y - TerrainField.SampleSpacing) / pageSize ),
			(int)MathF.Floor( (bounds.Minimum.z - TerrainField.SampleSpacing) / pageSize ) );
		var high = new Vector3Int(
			(int)MathF.Floor( (bounds.Maximum.x + TerrainField.SampleSpacing) / pageSize ),
			(int)MathF.Floor( (bounds.Maximum.y + TerrainField.SampleSpacing) / pageSize ),
			(int)MathF.Floor( (bounds.Maximum.z + TerrainField.SampleSpacing) / pageSize ) );
		RequirePageRange( low, high );
		var revision = 0;
		var volume = ((long)high.x - low.x + 1) * ((long)high.y - low.y + 1) * ((long)high.z - low.z + 1);
		if ( volume <= 8 && volume <= Pages.Count )
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
			foreach ( var key in _pageIndex.Query( low, high ) )
			{
				if ( !Pages.TryGetValue( key, out var page ) ) continue;
				revision = Math.Max( revision, page.GetRange( key, bounds, out var pageMinimum, out var pageMaximum, includeRevision ) );
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
	public TerrainFieldStore.Checkpoint Checkpoint { get; init; }
	// Restore safety must not treat the gap between distant changed pages as edited terrain.
	public SdfWorldAabb[] ReplacementSampleBounds { get; init; } = Array.Empty<SdfWorldAabb>();
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
