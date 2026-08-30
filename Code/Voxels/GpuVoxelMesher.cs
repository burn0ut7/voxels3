using System;
using System.Diagnostics;
using Sandbox.Rendering;

internal sealed class GpuVoxelMesher : IDisposable
{
	// Persistent geometry is disposable revisioned cache state; the SDF remains canonical.
	public const int MaximumDispatchesPerUpdate = 8;
	public const int RegionsPerSlab = 256;
	public const int TerrainVertexBytes = 24;
	private const int VertexArenaBytes = 32 * 1024 * 1024;
	private const int IndexArenaBytes = 16 * 1024 * 1024;
	private const int VertexArenaCapacity = VertexArenaBytes / TerrainVertexBytes;
	private const int IndexArenaCapacity = IndexArenaBytes / sizeof( uint );
	private const int IndirectArgumentStride = sizeof( uint ) * 5;
	private const int MaximumScheduleLatencySamples = 524288;

	private readonly Scene _scene;
	private readonly ComputeShader _visibilityShader = new( "shaders/voxels/voxel_chunk_visibility_cs.shader" );
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_terrain.shader" );
	private readonly Sandbox.Rendering.CommandList _drawCommands = new( "Voxel Terrain Indexed Indirect Draws" );
	private readonly Dictionary<Vector3Int, ResidentMesh> _resident = new();
	private readonly Dictionary<Vector3Int, PendingMesh> _pending = new();
	private readonly Queue<PendingMesh> _gameplayDispatchQueue = new();
	private readonly Queue<PendingMesh> _warmDispatchQueue = new();
	private readonly List<GeometryArena> _arenas = new();
	private readonly List<InFlightMesh> _countInFlight = new( MaximumDispatchesPerUpdate );
	private readonly List<CandidateMesh> _emitInFlight = new( MaximumDispatchesPerUpdate );
	private readonly HashSet<Vector3Int> _cancelledInFlight = new();
	private readonly List<VisibilityBuffers> _retiredVisibilityBuffers = new();
	private readonly object _visibilityLock = new();
	private readonly ReadbackSceneObject _readbackObject;
	private readonly GpuBuffer<uint> _visibilityAggregateCounters = new( 10, GpuBuffer.UsageFlags.Structured, "Voxel Visibility Aggregate Counters" );
	private GpuTerrainScratch _scratch;
	private CameraComponent _camera;
	private int _cellsPerAxis;
	private int _visibilityCapacity;
	private int _pendingGameplayCount;
	private int _pendingWarmCount;
	private int _warmResidentCount;
	private uint _nextGeneration;
	private Vector4[] _visibilityBoundsData = Array.Empty<Vector4>();
	private GpuBuffer.IndirectDrawIndexedArguments[] _sourceArgumentData = Array.Empty<GpuBuffer.IndirectDrawIndexedArguments>();
	private VisibilityBuffers _visibilityBuffers;
	private bool _drawCommandsDirty;
	private bool _visibilityDescriptorsDirty;
	private bool _visibilityMeasurementActive;
	private bool _visibilitySettledCaptureActive;
	private bool _visibilityReadbackPending;
	private bool _visibilityReadbackInFlight;
	private long _visibilityReadbackRequestedRenderSequence;
	private GpuVisibilityMeasurement? _completedVisibilityMeasurement;
	private long _dispatchCount;
	private long _poolAllocationCount;
	private long _poolReuseCount;
	private long _scalarReadbackCount;
	private long _countReadbackCount;
	private long _countReadbackBytes;
	private double _countReadbackMilliseconds;
	private double _countSubmissionMilliseconds;
	private double _emitSubmissionMilliseconds;
	private long _visibilityScalarReadbackCount;
	private long _renderSequence;
	private long _submittedRenderSequence;
	private int _maximumDispatchesRequested = MaximumDispatchesPerUpdate;
	private int _processedRenderDispatches;
	private ulong _topologyDigest;
	private ulong _positionDigest;
	private float[] _scheduleLatencyMilliseconds = Array.Empty<float>();
	private int _scheduleLatencySampleCount;
	private int _scheduleLatencyTruncatedCount;
	private int _scheduleLatencyCancelledCount;
	private int _scheduleLatencySupersededCount;
	private bool _scheduleLatencyMeasurementActive;
	private bool _disposed;

	public int ResidentCount => _resident.Count;
	public int PendingCount => PendingGameplayCount + PendingWarmCount;
	public int PendingGameplayCount => _pendingGameplayCount + CountInFlight( GpuMeshResidency.Gameplay );
	public int PendingWarmCount => _pendingWarmCount + CountInFlight( GpuMeshResidency.Warm );
	public int WarmResidentCount => _warmResidentCount;
	public int PoolCount => _arenas.Sum( arena => arena.FreeSlotCount );
	public int AllocatedResourceCount => _arenas.Count * RegionsPerSlab;
	public long DispatchCount => _dispatchCount;
	public long PoolAllocationCount => _poolAllocationCount;
	public long PoolReuseCount => _poolReuseCount;
	public long ScalarReadbackCount => _scalarReadbackCount;
	public long CountReadbackCount => _countReadbackCount;
	public long CountReadbackBytes => _countReadbackBytes;
	public double CountReadbackMilliseconds => _countReadbackMilliseconds;
	public double CountSubmissionMilliseconds => _countSubmissionMilliseconds;
	public double EmitSubmissionMilliseconds => _emitSubmissionMilliseconds;
	public long VisibilityScalarReadbackCount => _visibilityScalarReadbackCount;
	public const long GeometryReadbackCount = 0;
	public const long OrdinaryRenderSdfEvaluationCount = 0;
	public int TerrainIndirectApiSubmissionCount => _arenas.Count( arena => arena.ActiveResidentCount > 0 );
	public int IndirectArgumentRecordCount => TerrainIndirectApiSubmissionCount * RegionsPerSlab;
	public int TerrainBufferGroupCount => _arenas.Count;
	public long LogicalCapacityBytes => UsedVertexBytes + UsedIndexBytes;
	public long ReservedActiveCellCapacity => 0;
	public long ReservedActiveCellCapacityBytes => 0;
	public long UniqueVertexCount => _arenas.Sum( arena => (long)arena.VertexUsed );
	public long IndexCount => _arenas.Sum( arena => (long)arena.IndexUsed );
	public long TriangleCount => IndexCount / 3;
	public long UsedVertexBytes => UniqueVertexCount * TerrainVertexBytes;
	public long UsedIndexBytes => IndexCount * sizeof( uint );
	public long CommittedVertexBytes => (long)_arenas.Count * VertexArenaBytes;
	public long CommittedIndexBytes => (long)_arenas.Count * IndexArenaBytes;
	public long TransientScratchBytes => _scratch?.CapacityBytes ?? 0;
	public int ArenaCount => _arenas.Count;
	public int FreeRangeCount => _arenas.Sum( arena => arena.VertexFreeRangeCount + arena.IndexFreeRangeCount );
	public int LargestFreeVertexRange => _arenas.Count == 0 ? 0 : _arenas.Max( arena => arena.VertexLargestFree );
	public int LargestFreeIndexRange => _arenas.Count == 0 ? 0 : _arenas.Max( arena => arena.IndexLargestFree );
	public float FragmentationPercent
	{
		get
		{
			var free = _arenas.Sum( arena =>
				(long)arena.VertexFree * TerrainVertexBytes + (long)arena.IndexFree * sizeof( uint ) );
			var largest = _arenas.Sum( arena =>
				(long)arena.VertexLargestFree * TerrainVertexBytes + (long)arena.IndexLargestFree * sizeof( uint ) );
			return free > 0 ? (float)(free - largest) * 100f / free : 0f;
		}
	}
	public string TopologyDigest => _topologyDigest.ToString( "X16" );
	public string PositionDigest => _positionDigest.ToString( "X16" );
	public long LogicalVisibilityBytes => _visibilityCapacity == 0 ? 0 :
		(long)_visibilityCapacity * (sizeof( float ) * 8 + IndirectArgumentStride * 2) + sizeof( uint ) * 15;

	public GpuVoxelMesher( Scene scene, int cellsPerAxis )
	{
		_scene = scene;
		_cellsPerAxis = cellsPerAxis;
		_scratch = new GpuTerrainScratch( cellsPerAxis );
		_readbackObject = new ReadbackSceneObject( scene.SceneWorld, this );
		Sandbox.Diagnostics.GpuProfilerStats.Enabled = true;
		AttachToMainCamera();
	}

	public void Schedule( VoxelChunk chunk, int sourceRevision, GpuMeshResidency residency = GpuMeshResidency.Gameplay )
	{
		if ( chunk.DensityClassification != ChunkDensityClassification.PotentiallySurfaceContaining )
		{
			Remove( chunk.Coordinate );
			return;
		}
		var descriptor = GpuSdfDescriptor.FromChunk( chunk, sourceRevision );
		if ( _resident.TryGetValue( chunk.Coordinate, out var resident ) && resident.Descriptor == descriptor )
		{
			if ( residency == GpuMeshResidency.Gameplay ) SetResidency( resident, residency );
			return;
		}
		QueuePending( new PendingMesh( descriptor, residency, Stopwatch.GetTimestamp() ) );
	}

	public void BeginScheduleLatencyMeasurement()
	{
		_scheduleLatencyMilliseconds = new float[MaximumScheduleLatencySamples];
		_scheduleLatencySampleCount = 0;
		_scheduleLatencyTruncatedCount = 0;
		_scheduleLatencyCancelledCount = 0;
		_scheduleLatencySupersededCount = 0;
		_scheduleLatencyMeasurementActive = true;
	}

	public GpuMeshScheduleLatencyMeasurement CompleteScheduleLatencyMeasurement()
	{
		_scheduleLatencyMeasurementActive = false;
		Array.Sort( _scheduleLatencyMilliseconds, 0, _scheduleLatencySampleCount );
		var result = new GpuMeshScheduleLatencyMeasurement(
			_scheduleLatencySampleCount,
			_scheduleLatencyTruncatedCount,
			GetScheduleLatencyPercentile( 0.50 ),
			GetScheduleLatencyPercentile( 0.95 ),
			GetScheduleLatencyPercentile( 0.99 ),
			_scheduleLatencySampleCount > 0 ? _scheduleLatencyMilliseconds[_scheduleLatencySampleCount - 1] : 0,
			_scheduleLatencyCancelledCount,
			_scheduleLatencySupersededCount );
		_scheduleLatencyMilliseconds = Array.Empty<float>();
		return result;
	}

	private float GetScheduleLatencyPercentile( double percentile )
	{
		if ( _scheduleLatencySampleCount == 0 ) return 0;
		var index = Math.Clamp( (int)Math.Ceiling( _scheduleLatencySampleCount * percentile ) - 1, 0, _scheduleLatencySampleCount - 1 );
		return _scheduleLatencyMilliseconds[index];
	}

	public void SetResidency( Vector3Int coordinate, GpuMeshResidency residency )
	{
		if ( _resident.TryGetValue( coordinate, out var resident ) )
		{
			SetResidency( resident, residency );
			return;
		}
		if ( _pending.TryGetValue( coordinate, out var pending ) && pending.Residency != residency )
		{
			QueuePending( pending with { Residency = residency } );
			return;
		}
		for ( var index = 0; index < _countInFlight.Count; index++ )
		{
			if ( _countInFlight[index].Descriptor.ChunkCoordinate == coordinate )
			{
				_countInFlight[index] = _countInFlight[index] with { Residency = residency };
				return;
			}
		}
		for ( var index = 0; index < _emitInFlight.Count; index++ )
		{
			if ( _emitInFlight[index].Descriptor.ChunkCoordinate == coordinate )
			{
				_emitInFlight[index] = _emitInFlight[index] with { Residency = residency };
				return;
			}
		}
	}

	public void Remove( Vector3Int coordinate )
	{
		RemovePending( coordinate );
		if ( _countInFlight.Any( value => value.Descriptor.ChunkCoordinate == coordinate ) ||
			_emitInFlight.Any( value => value.Descriptor.ChunkCoordinate == coordinate ) )
		{
			_cancelledInFlight.Add( coordinate );
		}
		if ( !_resident.Remove( coordinate, out var resident ) ) return;
		ReleaseResident( resident );
		_drawCommandsDirty = true;
	}

	public void Reset( int cellsPerAxis )
	{
		Clear();
		if ( cellsPerAxis == _cellsPerAxis ) return;
		DisposeArenas();
		DisposeVisibilityBuffers();
		_scratch?.Dispose();
		_cellsPerAxis = cellsPerAxis;
		_scratch = new GpuTerrainScratch( cellsPerAxis );
	}

	public int ProcessPending( int maximumDispatches )
	{
		AttachToMainCamera();
		FinalizeEmits();
		_maximumDispatchesRequested = Math.Clamp( maximumDispatches, 0, MaximumDispatchesPerUpdate );
		var processed = System.Threading.Interlocked.Exchange( ref _processedRenderDispatches, 0 );
		CommitDrawCommands();
		return processed;
	}

	private void ProcessGpuRenderTick()
	{
		if ( _disposed || _scratch is null ) return;
		if ( _emitInFlight.Count > 0 ) return;
		if ( _countInFlight.Count > 0 )
		{
			if ( _scratch.TryTakeCounts( out var counts, out var count, out var readbackMilliseconds ) )
			{
				AllocateAndEmit( counts, count, readbackMilliseconds );
			}
			return;
		}

		var requests = new GpuTerrainRequest[MaximumDispatchesPerUpdate];
		var processed = 0;
		while ( processed < _maximumDispatchesRequested && TryDequeuePending( out var pending ) )
		{
			var generation = ++_nextGeneration;
			var inFlight = new InFlightMesh( pending.Descriptor, pending.Residency, generation, pending.ScheduledTimestamp );
			_countInFlight.Add( inFlight );
			requests[processed] = CreateRequest( inFlight, processed );
			processed++;
		}
		if ( processed > 0 )
		{
			if ( !_scratch.TrySubmitCount( requests, processed, out var submissionMilliseconds ) )
				throw new InvalidOperationException( "Voxel terrain scratch rejected an idle count batch." );
			_countSubmissionMilliseconds += submissionMilliseconds;
			System.Threading.Interlocked.Add( ref _processedRenderDispatches, processed );
		}
	}

	private static GpuTerrainRequest CreateRequest( InFlightMesh inFlight, int requestIndex )
	{
		var descriptor = inFlight.Descriptor;
		var size = descriptor.CellsPerAxis * descriptor.CellSize;
		var origin = new Vector3(
			descriptor.ChunkCoordinate.x * size,
			descriptor.ChunkCoordinate.y * size,
			descriptor.ChunkCoordinate.z * size );
		return new GpuTerrainRequest
		{
			OriginAndCellSize = new Vector4( origin, descriptor.CellSize ),
			Terrain = new Vector4(
				descriptor.TerrainSettings.WorldSeed,
				descriptor.TerrainSettings.SurfaceBaseHeight,
				descriptor.TerrainSettings.SurfaceFrequency,
				descriptor.TerrainSettings.SurfaceAmplitude ),
			CellsPerAxis = descriptor.CellsPerAxis,
			Generation = inFlight.Generation,
			RequestIndex = (uint)requestIndex
		};
	}

	private void AllocateAndEmit( GpuTerrainCountResult[] counts, int count, double readbackMilliseconds )
	{
		if ( count != _countInFlight.Count ) throw new InvalidOperationException( "Voxel terrain count batch length changed." );
		_countReadbackCount++;
		_scalarReadbackCount++;
		_countReadbackBytes += count * 32;
		_countReadbackMilliseconds += readbackMilliseconds;
		var arenas = new HashSet<GeometryArena>();
		for ( var index = 0; index < count; index++ )
		{
			var source = _countInFlight[index];
			var result = counts[index];
			if ( result.Generation != source.Generation || result.RequestIndex != (uint)index )
				throw new InvalidOperationException( "Stale voxel terrain count metadata." );
			GeometryHandle handle = null;
			if ( result.IndexCount > 0 )
			{
				handle = Acquire( checked( (int)result.VertexCount ), checked( (int)result.IndexCount ), source.Generation );
				arenas.Add( handle.Arena );
			}
			_emitInFlight.Add( new CandidateMesh(
				source.Descriptor, source.Residency, source.Generation, source.ScheduledTimestamp, handle, result ) );
		}
		_countInFlight.Clear();
		foreach ( var arena in arenas )
		{
			var allocations = new GpuTerrainAllocationDescriptor[count];
			for ( var index = 0; index < count; index++ )
			{
				var candidate = _emitInFlight[index];
				if ( candidate.Handle?.Arena != arena ) continue;
				allocations[index] = new GpuTerrainAllocationDescriptor
				{
					VertexOffset = (uint)candidate.Handle.Vertices.Offset,
					VertexCapacity = (uint)candidate.Handle.Vertices.Count,
					IndexOffset = (uint)candidate.Handle.Indices.Offset,
					IndexCapacity = (uint)candidate.Handle.Indices.Count,
					Generation = candidate.Generation,
					RequestIndex = (uint)index,
					Enabled = 1
				};
			}
			_emitSubmissionMilliseconds += _scratch.SubmitEmitPass( allocations, count, arena.Vertices, arena.Indices );
		}
		_scratch.CompleteEmit();
		_submittedRenderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
	}

	private void FinalizeEmits()
	{
		if ( _emitInFlight.Count == 0 ||
			System.Threading.Interlocked.Read( ref _renderSequence ) <= _submittedRenderSequence ) return;
		foreach ( var completed in _emitInFlight )
		{
			var coordinate = completed.Descriptor.ChunkCoordinate;
			if ( _cancelledInFlight.Remove( coordinate ) )
			{
				if ( _scheduleLatencyMeasurementActive ) _scheduleLatencyCancelledCount++;
				Release( completed.Handle );
				continue;
			}
			var residency = completed.Residency;
			if ( _pending.TryGetValue( coordinate, out var replacement ) )
			{
				if ( replacement.Descriptor != completed.Descriptor )
				{
					if ( _scheduleLatencyMeasurementActive ) _scheduleLatencySupersededCount++;
					Release( completed.Handle );
					continue;
				}
				residency = replacement.Residency;
				RemovePending( coordinate );
			}
			if ( _resident.Remove( coordinate, out var previous ) ) ReleaseResident( previous );
			var resident = new ResidentMesh( completed.Descriptor, residency, completed.Handle, completed.Counts );
			_resident.Add( coordinate, resident );
			if ( residency == GpuMeshResidency.Warm ) _warmResidentCount++;
			if ( completed.Handle is not null )
			{
				completed.Handle.Arena.ActiveResidentCount++;
				SetVisibilityActive( resident, true );
			}
			_topologyDigest ^= CoordinateDigest( coordinate, completed.Counts.TopologyDigest );
			_positionDigest ^= CoordinateDigest( coordinate, completed.Counts.PositionDigest );
			RecordScheduleLatency( completed.ScheduledTimestamp );
			_dispatchCount++;
			_drawCommandsDirty = true;
		}
		_emitInFlight.Clear();
		CommitDrawCommands();
	}

	private static ulong CoordinateDigest( Vector3Int coordinate, uint digest )
	{
		ulong value = digest ^ (uint)coordinate.x * 0x9E3779B1u ^
			(uint)coordinate.y * 0x85EBCA77u ^ (uint)coordinate.z * 0xC2B2AE3Du;
		value ^= value >> 30;
		value *= 0xBF58476D1CE4E5B9ul;
		value ^= value >> 27;
		value *= 0x94D049BB133111EBul;
		return value ^ (value >> 31);
	}

	private void RecordScheduleLatency( long scheduledTimestamp )
	{
		if ( !_scheduleLatencyMeasurementActive ) return;
		var milliseconds = (float)Stopwatch.GetElapsedTime( scheduledTimestamp ).TotalMilliseconds;
		if ( _scheduleLatencySampleCount < _scheduleLatencyMilliseconds.Length )
			_scheduleLatencyMilliseconds[_scheduleLatencySampleCount++] = milliseconds;
		else
			_scheduleLatencyTruncatedCount++;
	}

	private GeometryHandle Acquire( int vertexCount, int indexCount, uint generation )
	{
		foreach ( var arena in _arenas )
		{
			if ( arena.TryAcquire( vertexCount, indexCount, generation, out var reused ) )
			{
				_poolReuseCount++;
				return reused;
			}
		}
		if ( vertexCount > VertexArenaCapacity || indexCount > IndexArenaCapacity )
			throw new InvalidOperationException( $"Terrain region geometry ({vertexCount} vertices, {indexCount} indices) exceeds an arena." );
		var created = new GeometryArena( _arenas.Count );
		_arenas.Add( created );
		_poolAllocationCount++;
		EnsureVisibilityCapacity( _arenas.Count * RegionsPerSlab );
		if ( !created.TryAcquire( vertexCount, indexCount, generation, out var allocation ) )
			throw new InvalidOperationException( "A new terrain geometry arena rejected a valid allocation." );
		_drawCommandsDirty = true;
		return allocation;
	}

	private void ReleaseResident( ResidentMesh resident )
	{
		if ( resident.Residency == GpuMeshResidency.Warm ) _warmResidentCount--;
		_topologyDigest ^= CoordinateDigest( resident.Descriptor.ChunkCoordinate, resident.Counts.TopologyDigest );
		_positionDigest ^= CoordinateDigest( resident.Descriptor.ChunkCoordinate, resident.Counts.PositionDigest );
		if ( resident.Handle is null ) return;
		SetVisibilityActive( resident, false );
		resident.Handle.Arena.ActiveResidentCount--;
		Release( resident.Handle );
	}

	private static void Release( GeometryHandle handle ) => handle?.Arena.Release( handle );

	private void QueuePending( PendingMesh pending )
	{
		if ( _scheduleLatencyMeasurementActive && _pending.ContainsKey( pending.Descriptor.ChunkCoordinate ) )
			_scheduleLatencySupersededCount++;
		RemovePending( pending.Descriptor.ChunkCoordinate );
		_pending[pending.Descriptor.ChunkCoordinate] = pending;
		if ( pending.Residency == GpuMeshResidency.Gameplay )
		{
			_pendingGameplayCount++;
			_gameplayDispatchQueue.Enqueue( pending );
		}
		else
		{
			_pendingWarmCount++;
			_warmDispatchQueue.Enqueue( pending );
		}
	}

	private bool TryDequeuePending( out PendingMesh pending )
	{
		while ( _gameplayDispatchQueue.TryDequeue( out var gameplay ) )
		{
			if ( _pending.TryGetValue( gameplay.Descriptor.ChunkCoordinate, out var current ) && current == gameplay )
			{
				_pending.Remove( gameplay.Descriptor.ChunkCoordinate );
				_pendingGameplayCount--;
				pending = gameplay;
				return true;
			}
		}
		while ( _warmDispatchQueue.TryDequeue( out var warm ) )
		{
			if ( _pending.TryGetValue( warm.Descriptor.ChunkCoordinate, out var current ) && current == warm )
			{
				_pending.Remove( warm.Descriptor.ChunkCoordinate );
				_pendingWarmCount--;
				pending = warm;
				return true;
			}
		}
		pending = default;
		return false;
	}

	private void RemovePending( Vector3Int coordinate )
	{
		if ( !_pending.Remove( coordinate, out var pending ) ) return;
		if ( pending.Residency == GpuMeshResidency.Gameplay ) _pendingGameplayCount--;
		else _pendingWarmCount--;
	}

	private int CountInFlight( GpuMeshResidency residency ) =>
		_countInFlight.Count( value => value.Residency == residency ) +
		_emitInFlight.Count( value => value.Residency == residency );

	private void SetResidency( ResidentMesh resident, GpuMeshResidency residency )
	{
		if ( resident.Residency == residency ) return;
		if ( resident.Residency == GpuMeshResidency.Warm ) _warmResidentCount--;
		if ( residency == GpuMeshResidency.Warm ) _warmResidentCount++;
		resident.Residency = residency;
		if ( resident.Handle is not null ) SetVisibilityActive( resident, true );
	}

	public DrawCommandCommitResult CommitDrawCommands()
	{
		var start = Stopwatch.GetTimestamp();
		UploadVisibilityDescriptors();
		if ( !_drawCommandsDirty )
			return new DrawCommandCommitResult( false, (float)Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
		_drawCommands.Reset();
		if ( _visibilityCapacity > 0 )
		{
			_drawCommands.Attributes.Set( "VisibilityBounds", _visibilityBuffers.Bounds );
			_drawCommands.Attributes.Set( "SourceIndirectArguments", _visibilityBuffers.SourceArguments );
			_drawCommands.Attributes.Set( "VisibleIndirectArguments", _visibilityBuffers.VisibleArguments );
			_drawCommands.Attributes.Set( "VisibilityFrameCounters", _visibilityBuffers.FrameCounters );
			_drawCommands.Attributes.Set( "VisibilityAggregateCounters", _visibilityAggregateCounters );
			_drawCommands.Attributes.Set( "VisibilitySlotCount", _visibilityCapacity );
			_drawCommands.Attributes.Set( "VisibilityPass", 0 );
			_drawCommands.Attributes.Set( "MeasureVisibility", _visibilityMeasurementActive ? 1 : 0 );
			_drawCommands.Attributes.Set( "CaptureSettledDiagnostics", _visibilitySettledCaptureActive ? 1 : 0 );
			_drawCommands.ResourceBarrierTransition( _visibilityBuffers.Bounds, ResourceState.GenericRead );
			_drawCommands.ResourceBarrierTransition( _visibilityBuffers.SourceArguments, ResourceState.GenericRead );
			_drawCommands.ResourceBarrierTransition( _visibilityBuffers.VisibleArguments, ResourceState.UnorderedAccess );
			_drawCommands.ResourceBarrierTransition( _visibilityBuffers.FrameCounters, ResourceState.UnorderedAccess );
			_drawCommands.Clear( _visibilityBuffers.FrameCounters, 0 );
			_drawCommands.DispatchCompute( _visibilityShader, _visibilityCapacity, 1, 1 );
			_drawCommands.UavBarrier( _visibilityBuffers.VisibleArguments );
			_drawCommands.UavBarrier( _visibilityBuffers.FrameCounters );
			if ( _visibilityMeasurementActive || _visibilitySettledCaptureActive )
			{
				_drawCommands.ResourceBarrierTransition( _visibilityBuffers.FrameCounters, ResourceState.GenericRead );
				_drawCommands.ResourceBarrierTransition( _visibilityAggregateCounters, ResourceState.UnorderedAccess );
				_drawCommands.Attributes.Set( "VisibilityPass", 1 );
				_drawCommands.DispatchCompute( _visibilityShader, 1, 1, 1 );
				_drawCommands.UavBarrier( _visibilityAggregateCounters );
			}
			_drawCommands.ResourceBarrierTransition( _visibilityBuffers.VisibleArguments, ResourceState.IndirectArgument );
			foreach ( var arena in _arenas )
			{
				if ( arena.ActiveResidentCount == 0 ) continue;
				_drawCommands.ResourceBarrierTransition( arena.Vertices, ResourceState.VertexOrIndexBuffer );
				_drawCommands.ResourceBarrierTransition( arena.Indices, ResourceState.VertexOrIndexBuffer );
				_drawCommands.DrawIndexedInstancedIndirect(
					arena.Vertices,
					arena.Indices,
					_material,
					_visibilityBuffers.VisibleArguments,
					(uint)(arena.Index * RegionsPerSlab),
					null,
					Graphics.PrimitiveType.Triangles,
					RegionsPerSlab,
					IndirectArgumentStride );
			}
		}
		_drawCommandsDirty = false;
		return new DrawCommandCommitResult( true, (float)Stopwatch.GetElapsedTime( start ).TotalMilliseconds );
	}

	public void BeginVisibilityMeasurement()
	{
		lock ( _visibilityLock )
		{
			_completedVisibilityMeasurement = null;
			_visibilityReadbackPending = false;
			_visibilityReadbackInFlight = false;
		}
		Span<uint> counters = stackalloc uint[10];
		counters[3] = uint.MaxValue;
		_visibilityAggregateCounters.SetData( counters );
		_visibilityMeasurementActive = true;
		_visibilitySettledCaptureActive = false;
		_drawCommandsDirty = true;
		CommitDrawCommands();
	}

	public void StopVisibilityMeasurement()
	{
		_visibilityMeasurementActive = false;
		_drawCommandsDirty = true;
		CommitDrawCommands();
	}

	public void CaptureSettledVisibilityMeasurement()
	{
		_visibilitySettledCaptureActive = true;
		_drawCommandsDirty = true;
		CommitDrawCommands();
		lock ( _visibilityLock )
		{
			_visibilityReadbackPending = true;
			_visibilityReadbackRequestedRenderSequence = System.Threading.Interlocked.Read( ref _renderSequence );
		}
	}

	public bool TryTakeVisibilityMeasurement( out GpuVisibilityMeasurement measurement )
	{
		lock ( _visibilityLock )
		{
			if ( _completedVisibilityMeasurement is not { } completed )
			{
				measurement = default;
				return false;
			}
			measurement = completed;
			_completedVisibilityMeasurement = null;
			_visibilitySettledCaptureActive = false;
			_drawCommandsDirty = true;
			CommitDrawCommands();
			return true;
		}
	}

	private void ProcessVisibilityReadback()
	{
		GpuBuffer<uint> counters;
		long logicalBytes;
		lock ( _visibilityLock )
		{
			if ( !_visibilityReadbackPending || _visibilityReadbackInFlight ||
				System.Threading.Interlocked.Read( ref _renderSequence ) <= _visibilityReadbackRequestedRenderSequence + 1 ) return;
			_visibilityReadbackPending = false;
			_visibilityReadbackInFlight = true;
			counters = _visibilityAggregateCounters;
			logicalBytes = LogicalVisibilityBytes;
			_visibilityScalarReadbackCount++;
			_scalarReadbackCount++;
		}
		counters.GetDataAsync<uint>( data =>
		{
			var frames = data.Length >= 10 ? data[0] : 0;
			var minimum = data.Length >= 10 && frames > 0 && data[3] != uint.MaxValue ? data[3] : 0;
			lock ( _visibilityLock )
			{
				_completedVisibilityMeasurement = new GpuVisibilityMeasurement(
					frames,
					data.Length >= 10 ? data[1] : 0,
					data.Length >= 10 ? data[2] : 0,
					minimum,
					data.Length >= 10 ? data[4] : 0,
					data.Length >= 10 ? data[5] : 0,
					data.Length >= 10 ? data[6] : 0,
					data.Length >= 10 ? data[7] : 0,
					data.Length >= 10 ? data[8] : 0,
					data.Length >= 10 ? data[9] : 0,
					logicalBytes,
					1 );
				_visibilityReadbackInFlight = false;
			}
		} );
	}

	private void EnsureVisibilityCapacity( int requiredCapacity )
	{
		if ( requiredCapacity <= _visibilityCapacity ) return;
		var newCapacity = Math.Max( RegionsPerSlab, _visibilityCapacity );
		while ( newCapacity < requiredCapacity ) newCapacity = checked( newCapacity * 2 );
		if ( _visibilityBuffers is not null ) _retiredVisibilityBuffers.Add( _visibilityBuffers );
		var oldBounds = _visibilityBoundsData;
		var oldArguments = _sourceArgumentData;
		_visibilityCapacity = newCapacity;
		_visibilityBoundsData = new Vector4[newCapacity * 2];
		_sourceArgumentData = new GpuBuffer.IndirectDrawIndexedArguments[newCapacity];
		oldBounds.CopyTo( _visibilityBoundsData, 0 );
		oldArguments.CopyTo( _sourceArgumentData, 0 );
		var bounds = new GpuBuffer<Vector4>( newCapacity * 2, GpuBuffer.UsageFlags.Structured, "Voxel Visibility Bounds" );
		var source = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>( newCapacity,
			GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "Voxel Source Indexed Arguments" );
		var visible = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>( newCapacity,
			GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "Voxel Visible Indexed Arguments" );
		var frame = new GpuBuffer<uint>( 5, GpuBuffer.UsageFlags.Structured, "Voxel Visibility Frame Counters" );
		source.SetData( _sourceArgumentData );
		visible.SetData( _sourceArgumentData );
		_visibilityBuffers = new VisibilityBuffers( bounds, source, visible, frame );
		_visibilityDescriptorsDirty = true;
		_drawCommandsDirty = true;
	}

	private void SetVisibilityActive( ResidentMesh resident, bool active )
	{
		if ( resident.Handle is null ) return;
		var descriptor = resident.Descriptor;
		var size = descriptor.CellsPerAxis * descriptor.CellSize;
		var origin = new Vector3(
			descriptor.ChunkCoordinate.x * size,
			descriptor.ChunkCoordinate.y * size,
			descriptor.ChunkCoordinate.z * size );
		var padding = new Vector3( descriptor.CellSize );
		var slot = resident.Handle.GlobalSlot;
		var index = slot * 2;
		var activeResidency = resident.Residency == GpuMeshResidency.Warm ? 2f : 1f;
		_visibilityBoundsData[index] = new Vector4( origin - padding, active ? activeResidency : 0 );
		_visibilityBoundsData[index + 1] = new Vector4(
			origin + new Vector3( size ) + padding,
			active ? resident.Counts.ActiveCells : 0 );
		_sourceArgumentData[slot] = active
			? new GpuBuffer.IndirectDrawIndexedArguments
			{
				IndexCount = (uint)resident.Handle.Indices.Count,
				InstanceCount = 1,
				FirstIndex = (uint)resident.Handle.Indices.Offset,
				BaseVertex = resident.Handle.Vertices.Offset,
				FirstInstance = 0
			}
			: default;
		_visibilityDescriptorsDirty = true;
	}

	private void UploadVisibilityDescriptors()
	{
		if ( !_visibilityDescriptorsDirty || _visibilityBuffers is null ) return;
		_visibilityBuffers.Bounds.SetData( _visibilityBoundsData );
		_visibilityBuffers.SourceArguments.SetData( _sourceArgumentData );
		_visibilityDescriptorsDirty = false;
	}

	public void Clear()
	{
		if ( _scheduleLatencyMeasurementActive )
			_scheduleLatencyCancelledCount += _pending.Count + _countInFlight.Count + _emitInFlight.Count;
		_pending.Clear();
		_gameplayDispatchQueue.Clear();
		_warmDispatchQueue.Clear();
		_pendingGameplayCount = 0;
		_pendingWarmCount = 0;
		_cancelledInFlight.Clear();
		foreach ( var candidate in _emitInFlight ) Release( candidate.Handle );
		_emitInFlight.Clear();
		_countInFlight.Clear();
		foreach ( var resident in _resident.Values ) ReleaseResident( resident );
		_resident.Clear();
		_warmResidentCount = 0;
		_drawCommandsDirty = true;
		CommitDrawCommands();
	}

	public void Dispose()
	{
		if ( _disposed ) return;
		_disposed = true;
		Clear();
		if ( _camera is not null )
		{
			_camera.RemoveCommandList( _drawCommands );
			_camera = null;
		}
		DisposeArenas();
		DisposeVisibilityBuffers();
		_scratch?.Dispose();
		_visibilityAggregateCounters.Dispose();
		_readbackObject?.Delete();
	}

	private void AttachToMainCamera()
	{
		CameraComponent selected = null;
		foreach ( var candidate in _scene.GetAllComponents<CameraComponent>() )
		{
			selected ??= candidate;
			if ( candidate.IsMainCamera ) { selected = candidate; break; }
		}
		if ( selected == _camera ) return;
		if ( _camera is not null )
		{
			_camera.RemoveCommandList( _drawCommands );
		}
		_camera = selected;
		if ( _camera is not null )
		{
			_camera.AddCommandList( _drawCommands, Sandbox.Rendering.Stage.AfterOpaque, 0 );
		}
	}

	private void DisposeArenas()
	{
		foreach ( var arena in _arenas ) arena.Dispose();
		_arenas.Clear();
	}

	private void DisposeVisibilityBuffers()
	{
		_visibilityBuffers?.Dispose();
		_visibilityBuffers = null;
		foreach ( var retired in _retiredVisibilityBuffers ) retired.Dispose();
		_retiredVisibilityBuffers.Clear();
		_visibilityCapacity = 0;
		_visibilityBoundsData = Array.Empty<Vector4>();
		_sourceArgumentData = Array.Empty<GpuBuffer.IndirectDrawIndexedArguments>();
		_visibilityDescriptorsDirty = false;
	}

	private sealed class ResidentMesh
	{
		public GpuSdfDescriptor Descriptor { get; }
		public GpuMeshResidency Residency { get; set; }
		public GeometryHandle Handle { get; }
		public GpuTerrainCountResult Counts { get; }
		public ResidentMesh( GpuSdfDescriptor descriptor, GpuMeshResidency residency, GeometryHandle handle, GpuTerrainCountResult counts )
		{
			Descriptor = descriptor; Residency = residency; Handle = handle; Counts = counts;
		}
	}

	private readonly record struct PendingMesh( GpuSdfDescriptor Descriptor, GpuMeshResidency Residency, long ScheduledTimestamp );
	private readonly record struct InFlightMesh( GpuSdfDescriptor Descriptor, GpuMeshResidency Residency, uint Generation, long ScheduledTimestamp );
	private readonly record struct CandidateMesh( GpuSdfDescriptor Descriptor, GpuMeshResidency Residency, uint Generation, long ScheduledTimestamp, GeometryHandle Handle, GpuTerrainCountResult Counts );

	private sealed class GeometryHandle
	{
		public GeometryArena Arena { get; }
		public int Slot { get; }
		public uint Generation { get; }
		public GpuTerrainRange Vertices { get; }
		public GpuTerrainRange Indices { get; }
		public int GlobalSlot => Arena.Index * RegionsPerSlab + Slot;
		public GeometryHandle( GeometryArena arena, int slot, uint generation, GpuTerrainRange vertices, GpuTerrainRange indices )
		{
			Arena = arena; Slot = slot; Generation = generation; Vertices = vertices; Indices = indices;
		}
	}

	private sealed class GeometryArena : IDisposable
	{
		private readonly bool[] _occupied = new bool[RegionsPerSlab];
		private readonly Stack<int> _freeSlots = new( RegionsPerSlab );
		private readonly GpuTerrainRangeAllocator _vertices = new( VertexArenaCapacity );
		private readonly GpuTerrainRangeAllocator _indices = new( IndexArenaCapacity );
		public int Index { get; }
		public int ActiveResidentCount { get; set; }
		public int FreeSlotCount => _freeSlots.Count;
		public int VertexUsed => _vertices.UsedCount;
		public int IndexUsed => _indices.UsedCount;
		public int VertexFree => _vertices.FreeCount;
		public int IndexFree => _indices.FreeCount;
		public int VertexLargestFree => _vertices.LargestFreeRange;
		public int IndexLargestFree => _indices.LargestFreeRange;
		public int VertexFreeRangeCount => _vertices.FreeRangeCount;
		public int IndexFreeRangeCount => _indices.FreeRangeCount;
		public GpuBuffer<TerrainVertex> Vertices { get; }
		public GpuBuffer<uint> Indices { get; }

		public GeometryArena( int index )
		{
			Index = index;
			Vertices = new GpuBuffer<TerrainVertex>( VertexArenaCapacity,
				GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Vertex, $"Voxel Terrain Arena Vertices {index}" );
			Indices = new GpuBuffer<uint>( IndexArenaCapacity,
				GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Index, $"Voxel Terrain Arena Indices {index}" );
			for ( var slot = RegionsPerSlab - 1; slot >= 0; slot-- ) _freeSlots.Push( slot );
		}

		public bool TryAcquire( int vertexCount, int indexCount, uint generation, out GeometryHandle handle )
		{
			if ( _freeSlots.Count == 0 || !_vertices.TryAllocate( vertexCount, out var vertices ) )
			{
				handle = null;
				return false;
			}
			if ( !_indices.TryAllocate( indexCount, out var indices ) )
			{
				_vertices.Release( vertices );
				handle = null;
				return false;
			}
			var slot = _freeSlots.Pop();
			_occupied[slot] = true;
			handle = new GeometryHandle( this, slot, generation, vertices, indices );
			return true;
		}

		public void Release( GeometryHandle handle )
		{
			if ( handle.Arena != this || handle.Slot < 0 || handle.Slot >= RegionsPerSlab || !_occupied[handle.Slot] )
				throw new InvalidOperationException( "Invalid terrain geometry handle release." );
			_occupied[handle.Slot] = false;
			_vertices.Release( handle.Vertices );
			_indices.Release( handle.Indices );
			_freeSlots.Push( handle.Slot );
		}

		public void Dispose() { Vertices.Dispose(); Indices.Dispose(); }
	}

	private sealed class VisibilityBuffers : IDisposable
	{
		public GpuBuffer<Vector4> Bounds { get; }
		public GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> SourceArguments { get; }
		public GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> VisibleArguments { get; }
		public GpuBuffer<uint> FrameCounters { get; }
		public VisibilityBuffers( GpuBuffer<Vector4> bounds, GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> source,
			GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> visible, GpuBuffer<uint> frame )
		{
			Bounds = bounds; SourceArguments = source; VisibleArguments = visible; FrameCounters = frame;
		}
		public void Dispose() { Bounds.Dispose(); SourceArguments.Dispose(); VisibleArguments.Dispose(); FrameCounters.Dispose(); }
	}

	private sealed class ReadbackSceneObject : SceneCustomObject
	{
		private readonly GpuVoxelMesher _owner;
		public ReadbackSceneObject( SceneWorld world, GpuVoxelMesher owner ) : base( world )
		{
			_owner = owner;
			Bounds = BBox.FromPositionAndSize( Vector3.Zero, Vector3.One * 1_000_000_000f );
		}
		public override void RenderSceneObject()
		{
			System.Threading.Interlocked.Increment( ref _owner._renderSequence );
			_owner.ProcessGpuRenderTick();
			_owner.ProcessVisibilityReadback();
		}
	}
}

internal readonly record struct DrawCommandCommitResult( bool Rebuilt, float Milliseconds );
internal readonly record struct GpuMeshScheduleLatencyMeasurement(
	int Samples, int TruncatedSamples, float P50Milliseconds, float P95Milliseconds,
	float P99Milliseconds, float MaximumMilliseconds, int Cancelled, int Superseded );
internal readonly record struct GpuVisibilityMeasurement(
	uint FrameCount, uint ResidentTotal, uint VisibleTotal, uint MinimumVisible, uint MaximumVisible,
	uint WarmTotal, uint SettledSurfaceMeshes, uint SettledWarmSurfaceMeshes, uint SettledActiveCells,
	uint SettledMaximumActiveCells, long LogicalBufferBytes, long ScalarReadbacks )
{
	public float AverageResident => FrameCount > 0 ? (float)ResidentTotal / FrameCount : 0;
	public float AverageVisible => FrameCount > 0 ? (float)VisibleTotal / FrameCount : 0;
	public float AverageWarm => FrameCount > 0 ? (float)WarmTotal / FrameCount : 0;
	public float AverageCulled => MathF.Max( 0, AverageResident - AverageVisible );
	public float CulledPercent => AverageResident > 0 ? AverageCulled * 100 / AverageResident : 0;
}
internal enum GpuMeshResidency { Gameplay, Warm }
