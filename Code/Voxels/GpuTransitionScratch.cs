using System;

internal sealed class GpuTransitionScratch : IDisposable
{
	public const int MaximumBatchSize = GpuVoxelMesher.MaximumDispatchesPerUpdate;
	private const int DensitySize = 69;
	private const int FineNormalDensitySize = 65;
	private const int CoarseNormalDensitySize = 33;
	private const int DensityCount = DensitySize * DensitySize +
		FineNormalDensitySize * FineNormalDensitySize * 2 +
		CoarseNormalDensitySize * CoarseNormalDensitySize * 2;
	private const int CellCount = 32 * 32;
	private const int EdgeSlotCount = 10432;
	private const int EdgeGroupCount = (EdgeSlotCount + 255) / 256;
	private const int CellGroupCount = (CellCount + 255) / 256;
	private readonly object _stateLock = new();
	// s&box 26.08.19 crashes when a second transition ComputeShader is dispatched
	// beside this resource. Packed audit counters leave exactly 16 storage buffers,
	// so the two persistent output stages remain part of this single GPU pipeline.
	private readonly ComputeShader _shader;
	private readonly GpuBuffer<GpuTransitionRequest> _requests = new(
		MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Requests" );
	private readonly GpuBuffer<float> _densitySamples = new(
		DensityCount * MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Density" );
	private readonly GpuBuffer<GpuCellData> _cells = new(
		CellCount * MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Cells" );
	private readonly GpuBuffer<uint> _edgeFlags = new(
		EdgeSlotCount * MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Edge Flags" );
	private readonly GpuBuffer<uint> _edgeVertexIds = new(
		EdgeSlotCount * MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Edge IDs" );
	private readonly GpuBuffer<uint> _edgeGroupSums = new(
		EdgeGroupCount * MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Edge Groups" );
	private readonly GpuBuffer<uint> _cellGroupSums = new(
		CellGroupCount * MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Cell Groups" );
	private readonly GpuBuffer<uint> _blockCounts = new(
		MaximumBatchSize * 2, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Counts" );
	private readonly GpuBuffer<GpuDigest> _cellAuditCounts = new(
		MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Cell Audit" );
	private readonly GpuBuffer<GpuDigest> _digests = new(
		MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Digests" );
	private readonly GpuBuffer<GpuDigest> _faceMismatchCounts = new(
		MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Face Mismatches" );
	private readonly GpuBuffer<GpuLateralDigests> _lateralDigests = new(
		MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Lateral Digests" );
	private readonly GpuBuffer<GpuTransitionCountResult> _countResults = new(
		MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Count Results" );
	private readonly GpuBuffer<GpuTerrainAllocationDescriptor> _allocations = new(
		MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Allocations" );
	private readonly GpuBuffer<uint> _dummyOutputIndices = new(
		1, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Dummy Output" );
	private readonly GpuBuffer<TerrainVertex> _dummyOutputVertices = new(
		1, GpuBuffer.UsageFlags.Structured, "Voxel Transition Scratch Dummy Vertex Output" );
	private readonly GpuTransitionCountResult[] _completedBuffer = new GpuTransitionCountResult[MaximumBatchSize];
	private GpuTransitionCountResult[] _completedCounts;
	private int _completedCount;
	private int _batchSize;
	private long _readbackTimestamp;
	private long _readbackReadyTimestamp;
	private ScratchState _state;
	private bool _disposed;

	public long CapacityBytes { get; }
	public bool IsIdle { get { lock ( _stateLock ) return _state == ScratchState.Idle; } }

	public GpuTransitionScratch()
	{
		_shader = new ComputeShader( "shaders/voxels/voxel_transition_geometry_cs.shader" );
		BindCommonAttributes();
		CapacityBytes =
			(long)MaximumBatchSize * 112 +
			(long)DensityCount * MaximumBatchSize * sizeof( float ) +
			(long)CellCount * MaximumBatchSize * 16 +
			(long)EdgeSlotCount * MaximumBatchSize * sizeof( uint ) * 2 +
			(long)(EdgeGroupCount + CellGroupCount) * MaximumBatchSize * sizeof( uint ) +
			(long)MaximumBatchSize * (sizeof( uint ) * 4 + 24 + 64 + 64) +
			GpuVoxelMesher.TerrainVertexBytes + sizeof( uint );
	}

	public bool TrySubmitCount( GpuTransitionRequest[] requests, int count, out double submissionMilliseconds )
	{
		lock ( _stateLock )
		{
			if ( _disposed || _state != ScratchState.Idle || requests is null || count is < 1 or > MaximumBatchSize )
			{
				submissionMilliseconds = 0;
				return false;
			}
			_state = ScratchState.CountSamplingSubmitted;
			_batchSize = count;
		}
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		_requests.SetData( new Span<GpuTransitionRequest>( requests, 0, count ) );
		SetBatchSize( count );
		foreach ( var buffer in new GpuBuffer[] { _densitySamples, _cells, _edgeFlags, _edgeVertexIds,
			_edgeGroupSums, _cellGroupSums, _blockCounts, _cellAuditCounts, _digests,
			_faceMismatchCounts, _lateralDigests, _countResults } )
			Graphics.ResourceBarrierTransition( buffer, Sandbox.Rendering.ResourceState.UnorderedAccess );
		_shader.Attributes.Set( "TransitionStage", 0 );
		_shader.Dispatch( Math.Max( CellCount, EdgeSlotCount ) * count, 1, 1 );
		Barrier( _cells, _edgeFlags, _edgeVertexIds, _cellAuditCounts, _digests,
			_faceMismatchCounts, _lateralDigests );
		_shader.Attributes.Set( "TransitionStage", 1 );
		_shader.Dispatch( DensityCount * count, 1, 1 );
		Barrier( _densitySamples );
		submissionMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		return true;
	}

	public bool TryContinueCount()
	{
		ScratchState previousState;
		lock ( _stateLock )
		{
			if ( _disposed || _state is not (
				ScratchState.CountSamplingSubmitted or ScratchState.CountClassificationSubmitted) ) return false;
			previousState = _state;
			_state = previousState == ScratchState.CountSamplingSubmitted
				? ScratchState.CountClassificationSubmitted
				: ScratchState.CountSubmitted;
		}
		if ( previousState == ScratchState.CountSamplingSubmitted )
		{
			_shader.Attributes.Set( "TransitionStage", 2 );
			_shader.Dispatch( CellCount * _batchSize, 1, 1 );
			Barrier( _cells, _edgeFlags, _cellAuditCounts, _digests );
			_shader.Attributes.Set( "TransitionStage", 3 );
			_shader.Dispatch( EdgeGroupCount * 256 * _batchSize, 1, 1 );
			Barrier( _edgeVertexIds, _edgeGroupSums );
			_shader.Attributes.Set( "TransitionStage", 4 );
			_shader.Dispatch( CellGroupCount * 256 * _batchSize, 1, 1 );
			Barrier( _cells, _cellGroupSums );
			_shader.Attributes.Set( "TransitionStage", 5 );
			_shader.Dispatch( 256 * _batchSize, 1, 1 );
			Barrier( _edgeGroupSums, _cellGroupSums, _blockCounts );
			return true;
		}
		_shader.Attributes.Set( "TransitionStage", 6 );
		_shader.Dispatch( EdgeSlotCount * _batchSize, 1, 1 );
		Barrier( _digests, _faceMismatchCounts, _lateralDigests );
		_shader.Attributes.Set( "TransitionStage", 7 );
		_shader.Dispatch( _batchSize, 1, 1 );
		Barrier( _countResults );
		_readbackTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
		_countResults.GetDataAsync<GpuTransitionCountResult>( OnCountsRead, 0, _batchSize );
		return true;
	}

	public bool TryTakeCounts( out GpuTransitionCountResult[] counts, out int count,
		out double readbackMilliseconds, out double callbackWaitMilliseconds )
	{
		lock ( _stateLock )
		{
			if ( _state != ScratchState.CountReady )
			{
				counts = null; count = 0; readbackMilliseconds = 0; callbackWaitMilliseconds = 0;
				return false;
			}
			counts = _completedCounts;
			count = _completedCount;
			_completedCounts = null;
			_completedCount = 0;
			readbackMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(
				_readbackTimestamp, _readbackReadyTimestamp ).TotalMilliseconds;
			callbackWaitMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(
				_readbackReadyTimestamp ).TotalMilliseconds;
			_state = ScratchState.EmitReady;
			return true;
		}
	}

	public double SubmitEmitPass( GpuTerrainAllocationDescriptor[] allocations, int count,
		GpuBuffer<TerrainVertex> vertices, GpuBuffer<uint> indices )
	{
		lock ( _stateLock )
		{
			if ( _disposed || _state != ScratchState.EmitReady || count != _batchSize )
				throw new InvalidOperationException( "Voxel transition scratch is not ready to emit." );
		}
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		_allocations.SetData( new Span<GpuTerrainAllocationDescriptor>( allocations, 0, count ) );
		Graphics.ResourceBarrierTransition( vertices, Sandbox.Rendering.ResourceState.UnorderedAccess );
		Graphics.ResourceBarrierTransition( indices, Sandbox.Rendering.ResourceState.UnorderedAccess );
		_shader.Attributes.Set( "TransitionAllocations", _allocations );
		_shader.Attributes.Set( "TransitionOutputVertices", vertices );
		_shader.Attributes.Set( "TransitionOutputIndices", indices );
		_shader.Attributes.Set( "TransitionStage", 9 );
		_shader.Dispatch( EdgeSlotCount * count, 1, 1 );
		Barrier( vertices );
		_shader.Attributes.Set( "TransitionStage", 8 );
		_shader.Dispatch( CellCount * count, 1, 1 );
		Barrier( indices );
		return System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	public void CompleteEmit()
	{
		lock ( _stateLock )
		{
			if ( _state != ScratchState.EmitReady )
				throw new InvalidOperationException( "Voxel transition scratch emit completion is out of sequence." );
			_state = ScratchState.Idle;
		}
	}

	private void OnCountsRead( ReadOnlySpan<GpuTransitionCountResult> counts )
	{
		lock ( _stateLock )
		{
			if ( _disposed || _state != ScratchState.CountSubmitted ) return;
			counts.CopyTo( _completedBuffer );
			_completedCounts = _completedBuffer;
			_completedCount = counts.Length;
			_readbackReadyTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
			_state = ScratchState.CountReady;
		}
	}

	private void BindCommonAttributes()
	{
		_shader.Attributes.Set( "TransitionRequests", _requests );
		_shader.Attributes.Set( "TransitionDensitySamples", _densitySamples );
		_shader.Attributes.Set( "TransitionCells", _cells );
		_shader.Attributes.Set( "TransitionEdgeFlags", _edgeFlags );
		_shader.Attributes.Set( "TransitionEdgeVertexIds", _edgeVertexIds );
		_shader.Attributes.Set( "TransitionEdgeGroupSums", _edgeGroupSums );
		_shader.Attributes.Set( "TransitionCellGroupSums", _cellGroupSums );
		_shader.Attributes.Set( "TransitionBlockCounts", _blockCounts );
		_shader.Attributes.Set( "TransitionCellAuditCounts", _cellAuditCounts );
		_shader.Attributes.Set( "TransitionDigests", _digests );
		_shader.Attributes.Set( "TransitionFaceMismatchCounts", _faceMismatchCounts );
		_shader.Attributes.Set( "TransitionLateralDigests", _lateralDigests );
		_shader.Attributes.Set( "TransitionCountResults", _countResults );
		// The engine creates one Vulkan descriptor layout for every stage branch in
		// this shader. Keep the stage-8 descriptors valid during count dispatches;
		// SubmitEmitPass replaces the dummy output with the persistent arena range.
		_shader.Attributes.Set( "TransitionAllocations", _allocations );
		_shader.Attributes.Set( "TransitionOutputIndices", _dummyOutputIndices );
		_shader.Attributes.Set( "TransitionOutputVertices", _dummyOutputVertices );
		_shader.Attributes.Set( "TransitionDensitySize", DensitySize );
		_shader.Attributes.Set( "TransitionDensityCount", DensityCount );
		_shader.Attributes.Set( "TransitionCellCount", CellCount );
		_shader.Attributes.Set( "TransitionEdgeSlotCount", EdgeSlotCount );
		_shader.Attributes.Set( "TransitionEdgeGroupCount", EdgeGroupCount );
		_shader.Attributes.Set( "TransitionCellGroupCount", CellGroupCount );
	}

	private void SetBatchSize( int count )
	{
		_shader.Attributes.Set( "TransitionBatchSize", count );
	}

	private static void Barrier( params GpuBuffer[] buffers )
	{
		foreach ( var buffer in buffers ) Graphics.UavBarrier( buffer );
	}

	public void Dispose()
	{
		lock ( _stateLock ) { if ( _disposed ) return; _disposed = true; }
		_requests.Dispose(); _densitySamples.Dispose(); _cells.Dispose(); _edgeFlags.Dispose();
		_edgeVertexIds.Dispose(); _edgeGroupSums.Dispose(); _cellGroupSums.Dispose();
		_blockCounts.Dispose(); _cellAuditCounts.Dispose(); _digests.Dispose();
		_faceMismatchCounts.Dispose(); _lateralDigests.Dispose();
		_countResults.Dispose(); _allocations.Dispose(); _dummyOutputIndices.Dispose();
		_dummyOutputVertices.Dispose();
	}

	private enum ScratchState
	{
		Idle,
		CountSamplingSubmitted,
		CountClassificationSubmitted,
		CountSubmitted,
		CountReady,
		EmitReady
	}
	#pragma warning disable CS0649
	private struct GpuCellData { public uint Case; public uint IndexCount; public uint IndexOffset; public uint VertexCount; }
	private struct GpuDigest { public uint Topology; public uint Position; }
	private struct GpuLateralDigests { public uint MinimumU; public uint MaximumU; public uint MinimumV; public uint MaximumV; }
	#pragma warning restore CS0649
}
