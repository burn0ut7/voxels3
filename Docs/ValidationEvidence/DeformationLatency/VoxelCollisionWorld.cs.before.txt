using System;
using System.Diagnostics;
using System.Threading;

/// <summary>Owns bounded CPU work and native static terrain collision, never render resources.</summary>
internal sealed class VoxelCollisionWorld : IDisposable
{
	public const int MaximumRadius = 8;
	public const int MaximumPlayerInterests = 64;
	public const int MaximumActorRegions = 4096;
	private const int MaximumCompleted = 3;
	private const int MaximumWorkers = 3;
	private const double IntegrationBudgetMilliseconds = 0.5;
	private readonly VoxelManager _owner;
	private readonly int _cells;
	private readonly float _cellSize;
	private readonly object _gate = new();
	private readonly SemaphoreSlim _wake = new( 0, MaximumWorkers );
	private readonly Dictionary<Vector3Int, Region> _regions = new();
	private readonly Queue<Region> _pending = new();
	private readonly HashSet<Region> _waitingForField = new();
	private readonly Queue<Region> _completed = new();
	private readonly Queue<VoxelCollisionGeometry> _availableGeometry = new();
	private readonly Queue<Region> _retiring = new();
	private readonly List<Vector3Int> _leaving = new();
	private readonly List<Region> _ordered = new();
	private readonly System.Threading.Tasks.Task[] _workers;
	private readonly CollisionSamples _sampling = new();
	private readonly CollisionSamples _extraction = new();
	private readonly CollisionSamples _creation = new();
	private readonly CollisionSamples _meshCreation = new();
	private float _worstCreationMilliseconds;
	private float _worstSetupMilliseconds;
	private float _worstMeshMilliseconds;
	private float _worstPublicationMilliseconds;
	private Vector3Int _worstCreationCoordinate;
	private int _worstCreationVertices;
	private int _worstCreationIndices;
	private readonly CollisionSamples _retirement = new();
	private readonly CollisionSamples _readyLatency = new();
	private bool _stopping;
	private readonly HashSet<Region> _building = new();
	private readonly List<Vector3Int> _centers = new();
	private readonly List<Vector3Int> _actorCoordinates = new();
	private readonly HashSet<Vector3Int> _actorRegions = new();
	private int _radius = -1;
	private long _revision = -1;
	private int _ready;
	private int _bodies;
	private int _failures;
	private int _stale;
	private int _published;
	private int _peakCompleted;
	private long _peakCompletedBytes;
	private long _residentGeometryBytes;
	private long _peakResidentGeometryBytes;
	private long _degenerates;
	private long _weldedIntersections;
	private long _supportPatchRebuilds;
	private long _sampleCount;
	private long _rejectedBlocks;

	private sealed class Region
	{
		public Vector3Int Coordinate;
		public long InterestDistanceSquared;
		public TerrainFieldSnapshot Field;
		public int EditRevision;
		public int FieldEpoch;
		public long RequestedAt;
		public readonly CancellationTokenSource Cancellation = new();
		public bool Cancelled => Cancellation.IsCancellationRequested;
		public bool Ready;
		public bool HasPublishedCollision;
		public bool QueuedResult;
		public VoxelCollisionGeometry Geometry;
		public PhysicsBody Body;
		public long GeometryBytes;
		public string Error;
	}

	public VoxelCollisionWorld( VoxelManager owner, int cells, float cellSize )
	{
		_owner = owner;
		_cells = cells;
		_cellSize = cellSize;
		for ( var i = 0; i < MaximumCompleted; i++ ) _availableGeometry.Enqueue( new VoxelCollisionGeometry() );
		_workers = new System.Threading.Tasks.Task[MaximumWorkers];
		for ( var i = 0; i < MaximumWorkers; i++ ) _workers[i] = Work( new VoxelCollisionMesher( cells ) );
	}

	public bool EditRebuildPending
	{
		get
		{
			lock ( _gate ) return _regions.Values.Any( region => region.EditRevision > 0 && !region.Ready );
		}
	}

	public bool Settled
	{
		get
		{
			lock ( _gate )
			{
				return _radius >= 0 && _ready == _regions.Count && _building.Count == 0 &&
					_pending.Count == 0 && _waitingForField.Count == 0 && _completed.Count == 0 && _retiring.Count == 0;
			}
		}
	}

	public void SetInterest( IReadOnlyList<Vector3Int> centers, IReadOnlyList<Vector3Int> actorCoordinates, int radius, long revision, TerrainFieldSnapshot field )
	{
		if ( radius == _radius && revision == _revision && _centers.SequenceEqual( centers ) && _actorCoordinates.SequenceEqual( actorCoordinates ) ) return;
		using var profiler = global::Sandbox.Diagnostics.Performance.Scope( VoxelPerformanceProfiler.CollisionInterest );
		if ( radius < 0 || radius > MaximumRadius ) throw new ArgumentOutOfRangeException( nameof( radius ) );
		if ( centers.Count < 1 || centers.Count > MaximumPlayerInterests ) throw new ArgumentOutOfRangeException( nameof( centers ) );
		if ( actorCoordinates.Count > MaximumActorRegions ) throw new ArgumentOutOfRangeException( nameof( actorCoordinates ) );
		lock ( _gate )
		{
			_actorCoordinates.Clear();
			_actorCoordinates.AddRange( actorCoordinates );
			_actorRegions.Clear();
			_actorRegions.UnionWith( actorCoordinates );
			_centers.Clear();
			_centers.AddRange( centers );
			_radius = radius;
			_leaving.Clear();
			foreach ( var pair in _regions )
			{
				var retained = IsInterested( pair.Key );
				if ( revision != _revision || !retained )
				{
					pair.Value.Cancellation.Cancel();
					if ( pair.Value.Ready ) _ready--;
					if ( pair.Value.Body.IsValid() && !retained )
						_retiring.Enqueue( pair.Value );
					_leaving.Add( pair.Key );
				}
			}
			foreach ( var coordinate in _leaving )
			{
				var previous = _regions[coordinate];
				_regions.Remove( coordinate );
				if ( IsInterested( coordinate ) )
				{
					// The new immutable request owns existing support until replacement succeeds.
					_regions.Add( coordinate, CreateRegion( coordinate, field, previous.Body, previous.GeometryBytes ) );
					previous.Body = null;
					previous.GeometryBytes = 0;
				}
			}
			_revision = revision;
			foreach ( var center in _centers )
			{
				for ( var z = center.z - radius; z <= center.z + radius; z++ )
				{
					for ( var y = center.y - radius; y <= center.y + radius; y++ )
					{
						for ( var x = center.x - radius; x <= center.x + radius; x++ )
						{
							var coordinate = new Vector3Int( x, y, z );
							if ( !_regions.ContainsKey( coordinate ) )
							{
								_regions.Add( coordinate, CreateRegion( coordinate, field ) );
							}
						}
					}
				}
			}
			foreach ( var coordinate in _actorCoordinates )
				if ( !_regions.ContainsKey( coordinate ) ) _regions.Add( coordinate, CreateRegion( coordinate, field ) );
			RebuildPendingOrder();
		}
	}

	private bool IsInterested( Vector3Int coordinate )
	{
		if ( _actorRegions.Contains( coordinate ) ) return true;
		foreach ( var center in _centers )
		{
			var delta = coordinate - center;
			if ( Math.Abs( delta.x ) <= _radius && Math.Abs( delta.y ) <= _radius && Math.Abs( delta.z ) <= _radius ) return true;
		}
		return false;
	}

	/// <summary>Rebuild only resident dependencies; unloaded edits remain in the field.</summary>
	public int InvalidateField( TerrainFieldChange change )
	{
		var field = change.Result;
		var dirtyBounds = change.DependencyPageBounds;
		lock ( _gate )
		{
			_leaving.Clear();
			foreach ( var pair in _regions )
			{
				var bounds = SamplingBounds( pair.Key );
				if ( change.Source.Epoch == field.Epoch && !TerrainFieldChange.Intersects( bounds, dirtyBounds ) ) continue;
				var currentRevision = field.GetCorrectionRange( bounds, out _, out _ );
				// Revision blocks can overlap a region even when changed samples do not.
				// Rebase only completed geometry outside the actual field dependency;
				// pending work retains the ordinary revision/cancellation checks below.
				if ( pair.Value.Ready && (change.Source.Epoch == field.Epoch
					? !TerrainFieldChange.Intersects( bounds, change.AffectedBounds )
					: !change.ReplacementSampleBounds.Any( changed => TerrainFieldChange.Intersects( bounds, changed ) )) )
				{
					pair.Value.EditRevision = currentRevision;
					pair.Value.FieldEpoch = field.Epoch;
					continue;
				}
				if ( pair.Value.EditRevision != currentRevision || pair.Value.FieldEpoch != field.Epoch ) _leaving.Add( pair.Key );
				else if ( !_building.Contains( pair.Value ) && !pair.Value.Ready && !pair.Value.QueuedResult )
					pair.Value.Field = field.CaptureRegion( SamplingBounds( pair.Key ), pinSamples: false );
			}
			foreach ( var coordinate in _leaving )
			{
				var previous = _regions[coordinate];
				previous.Cancellation.Cancel();
				if ( previous.Ready ) _ready--;
				_regions[coordinate] = CreateRegion( coordinate, field, previous.Body, previous.GeometryBytes, previous.Ready || previous.HasPublishedCollision );
				previous.Body = null;
				previous.GeometryBytes = 0;
			}
			if ( _leaving.Count > 0 ) RebuildPendingOrder();
			return _leaving.Count;
		}
	}

	private SdfWorldAabb SamplingBounds( Vector3Int coordinate )
	{
		var origin = new Vector3( coordinate.x, coordinate.y, coordinate.z ) * (_cells * _cellSize);
		// Tiny native-collision support fragments sample one cell beyond the chunk.
		return new SdfWorldAabb( origin - Vector3.One * _cellSize,
			origin + Vector3.One * ((_cells + 1) * _cellSize) );
	}

	private Region CreateRegion( Vector3Int coordinate, TerrainFieldSnapshot field,
		PhysicsBody body = null, long geometryBytes = 0, bool hasPublishedCollision = false ) => new()
	{
		Coordinate = coordinate, Field = field.CaptureRegion( SamplingBounds( coordinate ), pinSamples: false ), RequestedAt = Stopwatch.GetTimestamp(),
		EditRevision = field.GetCorrectionRange( SamplingBounds( coordinate ), out _, out _ ),
		FieldEpoch = field.Epoch,
		Body = body, GeometryBytes = geometryBytes, HasPublishedCollision = hasPublishedCollision
	};

	// Caller holds _gate. Cancelled requests cannot remain in the dispatch queue.
	private void RebuildPendingOrder()
	{
		_waitingForField.Clear();
		_ordered.Clear();
		foreach ( var region in _regions.Values )
		{
			if ( !region.Ready && !region.QueuedResult && !_building.Contains( region ) && region.Error is null )
			{
				region.InterestDistanceSquared = _actorRegions.Contains( region.Coordinate ) ? 0 : long.MaxValue;
				foreach ( var center in _centers )
				{
					var delta = region.Coordinate - center;
					region.InterestDistanceSquared = Math.Min( region.InterestDistanceSquared,
						(long)delta.x * delta.x + (long)delta.y * delta.y + (long)delta.z * delta.z );
				}
				_ordered.Add( region );
			}
		}
		_ordered.Sort( ( a, b ) =>
		{
			var order = a.InterestDistanceSquared.CompareTo( b.InterestDistanceSquared );
			if ( order != 0 ) return order;
			order = a.Coordinate.z.CompareTo( b.Coordinate.z );
			if ( order != 0 ) return order;
			order = a.Coordinate.y.CompareTo( b.Coordinate.y );
			return order != 0 ? order : a.Coordinate.x.CompareTo( b.Coordinate.x );
		} );
		_pending.Clear();
		foreach ( var region in _ordered ) _pending.Enqueue( region );
		if ( _wake.CurrentCount < MaximumWorkers ) _wake.Release( MaximumWorkers - _wake.CurrentCount );
	}

	public void NotifyFieldPagesLoaded()
	{
		lock ( _gate )
		{
			if ( _stopping || _waitingForField.Count == 0 ) return;
			foreach ( var region in _waitingForField ) if ( !region.Cancelled ) _pending.Enqueue( region );
			_waitingForField.Clear();
			if ( _wake.CurrentCount < MaximumWorkers ) _wake.Release( MaximumWorkers - _wake.CurrentCount );
		}
	}

	private async System.Threading.Tasks.Task Work( VoxelCollisionMesher mesher )
	{
		while ( true )
		{
			Region region = null;
			lock ( _gate )
			{
				if ( _stopping ) return;
				while ( _pending.Count > 0 && _completed.Count + _building.Count < MaximumCompleted && _availableGeometry.Count > 0 )
				{
					region = _pending.Dequeue();
					if ( region.Cancelled ) { region = null; continue; }
					if ( !region.Field.TryCaptureRegion( SamplingBounds( region.Coordinate ), out var reader ) )
					{
						_waitingForField.Add( region ); region = null; continue;
					}
					region.Field = reader;
					_building.Add( region );
					region.Geometry = _availableGeometry.Dequeue();
					break;
				}
			}
			if ( region is null )
			{
				await _wake.WaitAsync();
				continue;
			}
			await GameTask.WorkerThread();
			try
			{
				region.Geometry = mesher.Build( region.Coordinate, _cellSize, region.Field, () => region.Cancelled, region.Geometry );
			}
			catch ( OperationCanceledException ) { }
			catch ( Exception exception )
			{
				region.Error = exception.ToString();
			}
			lock ( _gate )
			{
				_building.Remove( region );
				region.Field = null;
				if ( _stopping ) return;
				region.QueuedResult = true;
				_completed.Enqueue( region );
				_peakCompleted = Math.Max( _peakCompleted, _completed.Count );
				long bytes = 0;
				foreach ( var completed in _completed ) bytes += completed.Geometry?.Bytes ?? 0;
				_peakCompletedBytes = Math.Max( _peakCompletedBytes, bytes );
			}
		}
	}

	// Called by the manager on the engine thread, outside physics simulation.
	public void Integrate()
	{
		using var profiler = global::Sandbox.Diagnostics.Performance.Scope( VoxelPerformanceProfiler.CollisionIntegration );
		var frameStart = Stopwatch.GetTimestamp();
		while ( _retiring.TryDequeue( out var retired ) )
		{
			var start = Stopwatch.GetTimestamp();
			if ( retired.Body.IsValid() ) retired.Body.Remove();
			_residentGeometryBytes -= retired.GeometryBytes;
			_bodies--;
			_retirement.Add( (float)Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
			if ( Stopwatch.GetElapsedTime( frameStart ).TotalMilliseconds >= IntegrationBudgetMilliseconds ) return;
		}
		while ( Stopwatch.GetElapsedTime( frameStart ).TotalMilliseconds < IntegrationBudgetMilliseconds )
		{
			Region region;
			lock ( _gate )
			{
				if ( !_completed.TryDequeue( out region ) ) return;
				if ( _wake.CurrentCount < MaximumWorkers ) _wake.Release( MaximumWorkers - _wake.CurrentCount );
			}
			if ( region.Cancelled )
			{
				_stale++;
				RecycleGeometry( region );
				continue;
			}
			if ( region.Error is not null || region.Geometry is null )
			{
				_failures++;
				Log.Error( $"[VoxelCollision] build.failed coordinate={region.Coordinate} error={region.Error}" );
				RecycleGeometry( region );
				continue;
			}
			var geometry = region.Geometry;
			_sampling.Add( geometry.SamplingMilliseconds );
			_extraction.Add( geometry.ExtractionMilliseconds );
			_degenerates += geometry.DegenerateTriangles;
			_weldedIntersections += geometry.WeldedIntersections;
			_supportPatchRebuilds += geometry.SupportPatchRebuilds;
			_sampleCount += geometry.SampleCount;
			_rejectedBlocks += geometry.RejectedBlocks;
			var previousBody = region.Body;
			var previousBytes = region.GeometryBytes;
			var hasMesh = geometry.Indices.Count != 0;
			if ( hasMesh )
			{
				var start = Stopwatch.GetTimestamp();
				long meshStart = 0;
				long meshEnd = 0;
				PhysicsBody body = null;
				try
				{
					body = new PhysicsBody( _owner.Scene.PhysicsWorld );
					body.Enabled = false;
					body.BodyType = PhysicsBodyType.Static;
					body.Component = _owner;
					body.Position = new Vector3( region.Coordinate.x, region.Coordinate.y, region.Coordinate.z ) * (_cells * _cellSize);
					meshStart = Stopwatch.GetTimestamp();
					var shape = body.AddMeshShape( geometry.Vertices, geometry.Indices );
					meshEnd = Stopwatch.GetTimestamp();
					if ( !shape.IsValid() || !shape.IsMeshShape ) throw new InvalidOperationException( $"Native terrain mesh creation failed. valid={shape.IsValid()} mesh={shape?.IsMeshShape} hull={shape?.IsHullShape} sphere={shape?.IsSphereShape} vertices={geometry.Vertices.Count} indices={geometry.Indices.Count} positions={string.Join( ";", geometry.Vertices.Take( 12 ) )}" );
					shape.Tags.Add( "voxel_terrain" );
					body.Enabled = true;
					region.Body = body;
					region.GeometryBytes = geometry.Vertices.Count * 12L + geometry.Indices.Count * 4L;
					_residentGeometryBytes += region.GeometryBytes;
					_peakResidentGeometryBytes = Math.Max( _peakResidentGeometryBytes, _residentGeometryBytes );
					_bodies++;
				}
				catch ( Exception exception )
				{
					body?.Remove();
					region.Error = exception.ToString();
					_failures++;
					Log.Error( $"[VoxelCollision] publish.failed coordinate={region.Coordinate} error={region.Error}" );
				}
				var end = Stopwatch.GetTimestamp();
				var creationMilliseconds = (float)Stopwatch.GetElapsedTime( start, end ).TotalMilliseconds;
				_creation.Add( creationMilliseconds );
				// Only completed calls contribute to mesh timing; failures remain in Creation/Failures.
				if ( meshEnd != 0 ) _meshCreation.Add( (float)Stopwatch.GetElapsedTime( meshStart, meshEnd ).TotalMilliseconds );
				if ( creationMilliseconds > _worstCreationMilliseconds )
				{
					_worstCreationMilliseconds = creationMilliseconds;
					_worstSetupMilliseconds = meshStart == 0 ? creationMilliseconds : (float)Stopwatch.GetElapsedTime( start, meshStart ).TotalMilliseconds;
					_worstMeshMilliseconds = meshEnd == 0 ? 0 : (float)Stopwatch.GetElapsedTime( meshStart, meshEnd ).TotalMilliseconds;
					_worstPublicationMilliseconds = meshEnd == 0 ? 0 : (float)Stopwatch.GetElapsedTime( meshEnd, end ).TotalMilliseconds;
					_worstCreationCoordinate = region.Coordinate;
					_worstCreationVertices = geometry.Vertices.Count;
					_worstCreationIndices = geometry.Indices.Count;
				}
			}
			RecycleGeometry( region );
			if ( region.Error is null )
			{
				// OnUpdate does not interleave a physics step with this validated swap.
				if ( previousBody.IsValid() )
				{
					var retirementStart = Stopwatch.GetTimestamp();
					previousBody.Remove();
					_retirement.Add( (float)Stopwatch.GetElapsedTime( retirementStart ).TotalMilliseconds );
					_residentGeometryBytes -= previousBytes;
					_bodies--;
				}
				if ( !hasMesh )
				{
					region.Body = null;
					region.GeometryBytes = 0;
				}
				region.Ready = true;
				region.HasPublishedCollision = true;
				_ready++;
				_published++;
				_readyLatency.Add( (float)Stopwatch.GetElapsedTime( region.RequestedAt ).TotalMilliseconds );
			}
			if ( hasMesh ) return;
		}
	}

	private void RecycleGeometry( Region region )
	{
		lock ( _gate )
		{
			if ( region.Geometry is not null ) _availableGeometry.Enqueue( region.Geometry );
			region.Geometry = null;
			if ( _wake.CurrentCount < MaximumWorkers ) _wake.Release( MaximumWorkers - _wake.CurrentCount );
		}
	}

	public bool IsReady( Vector3Int minimum, Vector3Int maximum, bool allowRetained = false )
	{

		for ( var z = minimum.z; z <= maximum.z; z++ )
		{
			for ( var y = minimum.y; y <= maximum.y; y++ )
			{
				for ( var x = minimum.x; x <= maximum.x; x++ )
				{
					if ( !_regions.TryGetValue( new Vector3Int( x, y, z ), out var region ) || (!region.Ready && !(allowRetained && region.HasPublishedCollision)) ) return false;
				}
			}
		}
		return true;
	}

	public void BeginMeasurement()
	{
		_worstCreationMilliseconds = 0; _worstSetupMilliseconds = 0; _worstMeshMilliseconds = 0; _worstPublicationMilliseconds = 0;
		_worstCreationCoordinate = default; _worstCreationVertices = 0; _worstCreationIndices = 0;
		_sampling.Clear(); _extraction.Clear(); _creation.Clear(); _meshCreation.Clear(); _retirement.Clear(); _readyLatency.Clear();
		_published = 0; _stale = 0; _degenerates = 0; _weldedIntersections = 0; _supportPatchRebuilds = 0; _sampleCount = 0; _rejectedBlocks = 0;
		lock ( _gate ) { _peakCompleted = _completed.Count; _peakCompletedBytes = 0; }
		_peakResidentGeometryBytes = _residentGeometryBytes;
	}

	public PerformanceCollisionMetrics Capture()
	{
		lock ( _gate )
		{
			return new PerformanceCollisionMetrics
			{
				Desired = _regions.Count, Ready = _ready, Bodies = _bodies, Pending = _pending.Count + _waitingForField.Count,
				Building = _building.Count > 0, ActiveWorkers = _building.Count, WorkerLimit = MaximumWorkers, Completed = _completed.Count, Retiring = _retiring.Count,
				Failures = _failures, StaleDiscarded = _stale, Published = _published,
				PeakCompleted = _peakCompleted, PeakCompletedBytes = _peakCompletedBytes,
				ResidentGeometryBytes = _residentGeometryBytes, PeakResidentGeometryBytes = _peakResidentGeometryBytes,
				DegenerateTriangles = _degenerates, WeldedIntersections = _weldedIntersections, SupportPatchRebuilds = _supportPatchRebuilds, SampleCount = _sampleCount, RejectedBlocks = _rejectedBlocks, Sampling = _sampling.Capture(), Extraction = _extraction.Capture(),
				MeshCreation = _meshCreation.Capture(),
				WorstCreationSetupMilliseconds = _worstSetupMilliseconds, WorstCreationMeshMilliseconds = _worstMeshMilliseconds,
				WorstCreationPublicationMilliseconds = _worstPublicationMilliseconds,
				WorstCreationCoordinate = new PerformanceVector3Int { X = _worstCreationCoordinate.x, Y = _worstCreationCoordinate.y, Z = _worstCreationCoordinate.z },
				WorstCreationVertices = _worstCreationVertices, WorstCreationIndices = _worstCreationIndices,
				Creation = _creation.Capture(), Retirement = _retirement.Capture(), RequestToReady = _readyLatency.Capture()
			};
		}
	}

	public void Dispose()
	{
		var remainingBodies = 0;
		lock ( _gate )
		{
			_stopping = true;
			foreach ( var region in _regions.Values )
			{
				region.Cancellation.Cancel();
				if ( region.Body.IsValid() ) region.Body.Remove();
				if ( region.Body.IsValid() ) remainingBodies++;
			}
			while ( _retiring.TryDequeue( out var region ) )
			{
				if ( region.Body.IsValid() ) region.Body.Remove();
				if ( region.Body.IsValid() ) remainingBodies++;
			}
			_regions.Clear(); _pending.Clear(); _waitingForField.Clear(); _completed.Clear();
			_ready = 0; _bodies = remainingBodies; _residentGeometryBytes = 0;
			if ( _wake.CurrentCount < MaximumWorkers ) _wake.Release( MaximumWorkers - _wake.CurrentCount );
		}
		if ( remainingBodies > 0 )
		{
			// The native editor may already have destroyed its log UI.
			try { Log.Error( $"[VoxelCollision] dispose.failed remainingBodies={remainingBodies}" ); }
			catch ( Exception ) { }
		}
	}

	private sealed class CollisionSamples
	{
		private readonly List<float> _values = new();
		private int _truncated;
		public void Add( float value )
		{
			if ( _values.Count < 131072 ) _values.Add( value );
			else _truncated++;
		}
		public void Clear() { _values.Clear(); _truncated = 0; }
		public PerformanceDistributionMetrics Capture()
		{
			var values = _values.ToArray();
			Array.Sort( values );
			var tails = PerformanceSampleTails.FromSorted( values );
			double total = 0;
			foreach ( var value in values ) total += value;
			return new PerformanceDistributionMetrics
			{
				Samples = values.Length, TruncatedSamples = _truncated,
				Average = values.Length == 0 ? 0 : (float)(total / values.Length),
				P50 = tails.P50,
				P95 = tails.P95,
				P99 = tails.P99,
				Maximum = tails.Maximum
			};
		}
	}
}
