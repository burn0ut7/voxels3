using System;

internal sealed class GpuTerrainScratch : IDisposable
{
	public const int MaximumBatchSize = GpuVoxelMesher.MaximumDispatchesPerUpdate;
	private const int TransitionSampleLayerCount = 3;
	private readonly object _stateLock = new();
	// Do not fold the two output writers into the multi-stage shader. On s&box 26.08.19,
	// that composition crashes the native VFX variable parser during a cold resource load.
	// See Docs/AgentRoutes/meshing.md, "s&box VFX Shader Parser Gotcha".
	private readonly ComputeShader _shader = new( "shaders/voxels/voxel_persistent_geometry_cs.shader" );
	private readonly ComputeShader _emitVertices = new( "shaders/voxels/voxel_emit_vertices_cs.shader" );
	private readonly ComputeShader _emitIndices = new( "shaders/voxels/voxel_emit_indices_cs.shader" );
	private readonly ComputeShader _transitionShader = new( "shaders/voxels/voxel_transition_count_cs.shader" );
	private readonly ComputeShader _transitionEmitVertices = new( "shaders/voxels/voxel_transition_emit_vertices_cs.shader" );
	private readonly ComputeShader _transitionEmitIndices = new( "shaders/voxels/voxel_transition_emit_indices_cs.shader" );
	private readonly GpuBuffer<GpuTerrainRequest> _requests;
	private readonly GpuBuffer<float> _densitySamples;
	private readonly GpuBuffer<GpuCellData> _cells;
	private readonly GpuBuffer<uint> _edgeFlags;
	private readonly GpuBuffer<uint> _edgeVertexIds;
	private readonly GpuBuffer<uint> _edgeGroupSums;
	private readonly GpuBuffer<uint> _cellGroupSums;
	private readonly GpuBuffer<uint> _blockCounts;
	private readonly GpuBuffer<uint> _activeCellCounts;
	private readonly GpuBuffer<GpuDigest> _digests;
	private readonly GpuBuffer<GpuTerrainCountResult> _countResults;
	private readonly GpuBuffer<GpuTerrainAllocationDescriptor> _allocations;
	private readonly GpuTerrainCountResult[] _completedBuffer = new GpuTerrainCountResult[MaximumBatchSize];
	private readonly int _chunkSize;
	private readonly int _sampleSize;
	private readonly int _haloSize;
	private readonly int _haloSampleCount;
	private readonly int _cellCount;
	private readonly int _edgeSlotCount;
	private readonly int _edgeGroupCount;
	private readonly int _cellGroupCount;
	private readonly int _transitionSampleSize;
	private readonly int _transitionSampleCount;
	private readonly int _transitionSampleStride;
	private readonly int _transitionCellCount;
	private readonly int _transitionGroupCount;
	private GpuTerrainCountResult[] _completedCounts;
	private int _completedCount;
	private int _batchSize;
	private long _readbackTimestamp;
	private long _readbackReadyTimestamp;
	private ScratchState _state;
	private VoxelRenderMeshKind _meshKind;
	private bool _disposed;

	public long CapacityBytes { get; }
	public bool IsIdle { get { lock ( _stateLock ) return _state == ScratchState.Idle; } }

	public GpuTerrainScratch( int chunkSize )
	{
		_chunkSize = chunkSize;
		_sampleSize = checked( chunkSize + 1 );
		_haloSize = checked( chunkSize + 3 );
		_haloSampleCount = checked( _haloSize * _haloSize * _haloSize );
		_cellCount = checked( chunkSize * chunkSize * chunkSize );
		_edgeSlotCount = checked( _sampleSize * _sampleSize * _sampleSize * 3 );
		_edgeGroupCount = (_edgeSlotCount + 255) / 256;
		_cellGroupCount = (_cellCount + 255) / 256;
		_transitionSampleSize = checked( chunkSize * 2 + 1 );
		_transitionSampleCount = checked( _transitionSampleSize * _transitionSampleSize );
		_transitionSampleStride = checked( _transitionSampleCount * TransitionSampleLayerCount );
		_transitionCellCount = checked( chunkSize * chunkSize );
		_transitionGroupCount = (_transitionCellCount + 255) / 256;
		_requests = new GpuBuffer<GpuTerrainRequest>( MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Requests" );
		var densitySamplesPerRequest = Math.Max( _haloSampleCount, _transitionSampleStride );
		_densitySamples = new GpuBuffer<float>( checked( densitySamplesPerRequest * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Density" );
		_cells = new GpuBuffer<GpuCellData>( checked( _cellCount * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Cells" );
		_edgeFlags = new GpuBuffer<uint>( checked( _edgeSlotCount * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Edge Flags" );
		_edgeVertexIds = new GpuBuffer<uint>( checked( _edgeSlotCount * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Edge IDs" );
		_edgeGroupSums = new GpuBuffer<uint>( checked( _edgeGroupCount * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Edge Groups" );
		_cellGroupSums = new GpuBuffer<uint>( checked( _cellGroupCount * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Cell Groups" );
		_blockCounts = new GpuBuffer<uint>( MaximumBatchSize * 2, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Counts" );
		_activeCellCounts = new GpuBuffer<uint>( MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Active Cells" );
		_digests = new GpuBuffer<GpuDigest>( MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Digests" );
		_countResults = new GpuBuffer<GpuTerrainCountResult>( MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Count Results" );
		_allocations = new GpuBuffer<GpuTerrainAllocationDescriptor>( MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Allocations" );
		BindCommonAttributes();
		_emitVertices.Attributes.Set( "Requests", _requests );
		_emitVertices.Attributes.Set( "DensitySamples", _densitySamples );
		_emitVertices.Attributes.Set( "EdgeFlags", _edgeFlags );
		_emitVertices.Attributes.Set( "EdgeVertexIds", _edgeVertexIds );
		_emitVertices.Attributes.Set( "EdgeGroupSums", _edgeGroupSums );
		_emitVertices.Attributes.Set( "Allocations", _allocations );
		_emitVertices.Attributes.Set( "SampleSize", _sampleSize );
		_emitVertices.Attributes.Set( "HaloSize", _haloSize );
		_emitVertices.Attributes.Set( "HaloSampleCount", _haloSampleCount );
		_emitVertices.Attributes.Set( "EdgeSlotCount", _edgeSlotCount );
		_emitVertices.Attributes.Set( "EdgeGroupCount", _edgeGroupCount );
		_emitIndices.Attributes.Set( "Cells", _cells );
		_emitIndices.Attributes.Set( "EdgeVertexIds", _edgeVertexIds );
		_emitIndices.Attributes.Set( "EdgeGroupSums", _edgeGroupSums );
		_emitIndices.Attributes.Set( "CellGroupSums", _cellGroupSums );
		_emitIndices.Attributes.Set( "Allocations", _allocations );
		_emitIndices.Attributes.Set( "ChunkSize", _chunkSize );
		_emitIndices.Attributes.Set( "SampleSize", _sampleSize );
		_emitIndices.Attributes.Set( "CellCount", _cellCount );
		_emitIndices.Attributes.Set( "EdgeSlotCount", _edgeSlotCount );
		_emitIndices.Attributes.Set( "EdgeGroupCount", _edgeGroupCount );
		_emitIndices.Attributes.Set( "CellGroupCount", _cellGroupCount );
		BindTransitionAttributes();
		CapacityBytes =
			(long)MaximumBatchSize * 64 +
			(long)densitySamplesPerRequest * MaximumBatchSize * sizeof( float ) +
			(long)_cellCount * MaximumBatchSize * 12 +
			(long)_edgeSlotCount * MaximumBatchSize * sizeof( uint ) * 2 +
			(long)(_edgeGroupCount + _cellGroupCount) * MaximumBatchSize * sizeof( uint ) +
			(long)MaximumBatchSize * (sizeof( uint ) * 8 + 64 + sizeof( uint ) * 5);
	}

	public bool TrySubmitCount( GpuTerrainRequest[] requests, int count, out double submissionMilliseconds )
	{
		lock ( _stateLock )
		{
			if ( _disposed || _state != ScratchState.Idle || requests is null || count is < 1 or > MaximumBatchSize )
			{
				submissionMilliseconds = 0;
				return false;
			}
			_state = ScratchState.CountSubmitted;
			_batchSize = count;
			_meshKind = (VoxelRenderMeshKind)(requests[0].PackedIdentity & 0x01u);
		}
		for ( var index = 1; index < count; index++ )
		{
			if ( (VoxelRenderMeshKind)(requests[index].PackedIdentity & 0x01u) != _meshKind )
				throw new InvalidOperationException( "A voxel terrain scratch batch mixed regular and transition requests." );
		}
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		_requests.SetData( new Span<GpuTerrainRequest>( requests, 0, count ) );
		SetBatchSize( count );
		if ( _meshKind == VoxelRenderMeshKind.Transition )
		{
			SubmitTransitionCount( count );
			submissionMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
			return true;
		}
		foreach ( var buffer in new GpuBuffer[] { _densitySamples, _cells, _edgeFlags, _edgeVertexIds, _edgeGroupSums, _cellGroupSums, _blockCounts, _activeCellCounts, _digests, _countResults } )
			Graphics.ResourceBarrierTransition( buffer, Sandbox.Rendering.ResourceState.UnorderedAccess );
		_shader.Attributes.Set( "PersistentStage", 0 );
		_shader.Dispatch( Math.Max( _cellCount, _edgeSlotCount ) * count, 1, 1 );
		Barrier( _cells, _edgeFlags, _edgeVertexIds, _activeCellCounts, _digests );
		_shader.Attributes.Set( "PersistentStage", 1 );
		_shader.Dispatch( _haloSampleCount * count, 1, 1 );
		Barrier( _densitySamples );
		_shader.Attributes.Set( "PersistentStage", 2 );
		_shader.Dispatch( _cellCount * count, 1, 1 );
		Barrier( _cells, _edgeFlags, _activeCellCounts, _digests );
		_shader.Attributes.Set( "PersistentStage", 3 );
		_shader.Dispatch( _edgeGroupCount * 256 * count, 1, 1 );
		Barrier( _edgeVertexIds, _edgeGroupSums );
		_shader.Attributes.Set( "PersistentStage", 4 );
		_shader.Dispatch( _cellGroupCount * 256 * count, 1, 1 );
		Barrier( _cells, _cellGroupSums );
		_shader.Attributes.Set( "PersistentStage", 5 );
		_shader.Dispatch( 256 * count, 1, 1 );
		Barrier( _edgeGroupSums, _cellGroupSums, _blockCounts );
		_shader.Attributes.Set( "PersistentStage", 6 );
		_shader.Dispatch( _edgeSlotCount * count, 1, 1 );
		Barrier( _digests );
		_shader.Attributes.Set( "PersistentStage", 7 );
		_shader.Dispatch( count, 1, 1 );
		Barrier( _countResults );
		_readbackTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
		_countResults.GetDataAsync<GpuTerrainCountResult>( OnCountsRead, 0, count );
		submissionMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		return true;
	}

	private void SubmitTransitionCount( int count )
	{
		foreach ( var buffer in new GpuBuffer[] { _densitySamples, _cells, _edgeFlags, _edgeVertexIds,
			_edgeGroupSums, _cellGroupSums, _blockCounts, _activeCellCounts, _digests, _countResults } )
			Graphics.ResourceBarrierTransition( buffer, Sandbox.Rendering.ResourceState.UnorderedAccess );
		_transitionShader.Attributes.Set( "TransitionStage", 0 );
		_transitionShader.Dispatch( _transitionCellCount * count, 1, 1 );
		Barrier( _cells, _edgeFlags, _edgeVertexIds, _activeCellCounts, _digests );
		_transitionShader.Attributes.Set( "TransitionStage", 1 );
		_transitionShader.Dispatch( _transitionSampleStride * count, 1, 1 );
		Barrier( _densitySamples );
		_transitionShader.Attributes.Set( "TransitionStage", 2 );
		_transitionShader.Dispatch( _transitionCellCount * count, 1, 1 );
		Barrier( _cells, _edgeFlags, _activeCellCounts, _digests );
		_transitionShader.Attributes.Set( "TransitionStage", 3 );
		_transitionShader.Dispatch( _transitionGroupCount * 256 * count, 1, 1 );
		Barrier( _edgeVertexIds, _edgeGroupSums );
		_transitionShader.Attributes.Set( "TransitionStage", 4 );
		_transitionShader.Dispatch( _transitionGroupCount * 256 * count, 1, 1 );
		Barrier( _cells, _cellGroupSums );
		_transitionShader.Attributes.Set( "TransitionStage", 5 );
		_transitionShader.Dispatch( 256 * count, 1, 1 );
		Barrier( _edgeGroupSums, _cellGroupSums, _blockCounts );
		_transitionShader.Attributes.Set( "TransitionStage", 6 );
		_transitionShader.Dispatch( count, 1, 1 );
		Barrier( _countResults );
		_readbackTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
		_countResults.GetDataAsync<GpuTerrainCountResult>( OnCountsRead, 0, count );
	}

	public bool TryTakeCounts( out GpuTerrainCountResult[] counts, out int count,
		out double readbackMilliseconds, out double callbackWaitMilliseconds )
	{
		lock ( _stateLock )
		{
			if ( _state != ScratchState.CountReady )
			{
				counts = null; count = 0; readbackMilliseconds = 0;
				callbackWaitMilliseconds = 0; return false;
			}
			counts = _completedCounts; count = _completedCount; _completedCounts = null; _completedCount = 0;
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
				throw new InvalidOperationException( "Voxel terrain scratch is not ready to emit." );
		}
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		_allocations.SetData( new Span<GpuTerrainAllocationDescriptor>( allocations, 0, count ) );
		Graphics.ResourceBarrierTransition( vertices, Sandbox.Rendering.ResourceState.UnorderedAccess );
		Graphics.ResourceBarrierTransition( indices, Sandbox.Rendering.ResourceState.UnorderedAccess );
		_emitVertices.Attributes.Set( "OutputVertices", vertices );
		_emitIndices.Attributes.Set( "OutputIndices", indices );
		if ( _meshKind == VoxelRenderMeshKind.Transition )
		{
			_transitionEmitVertices.Attributes.Set( "OutputVertices", vertices );
			_transitionEmitIndices.Attributes.Set( "OutputIndices", indices );
			_transitionEmitVertices.Dispatch( _transitionCellCount * count, 1, 1 );
			Barrier( vertices );
			_transitionEmitIndices.Dispatch( _transitionCellCount * count, 1, 1 );
		}
		else
		{
			_emitVertices.Dispatch( _edgeSlotCount * count, 1, 1 );
			Barrier( vertices );
			_emitIndices.Dispatch( _cellCount * count, 1, 1 );
		}
		Barrier( indices );
		return System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	public void CompleteEmit()
	{
		lock ( _stateLock )
		{
			if ( _state != ScratchState.EmitReady )
				throw new InvalidOperationException( "Voxel terrain scratch emit completion is out of sequence." );
			_state = ScratchState.Idle;
		}
	}

	private void OnCountsRead( ReadOnlySpan<GpuTerrainCountResult> counts )
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
		_shader.Attributes.Set( "Requests", _requests );
		_shader.Attributes.Set( "DensitySamples", _densitySamples );
		_shader.Attributes.Set( "Cells", _cells );
		_shader.Attributes.Set( "EdgeFlags", _edgeFlags );
		_shader.Attributes.Set( "EdgeVertexIds", _edgeVertexIds );
		_shader.Attributes.Set( "EdgeGroupSums", _edgeGroupSums );
		_shader.Attributes.Set( "CellGroupSums", _cellGroupSums );
		_shader.Attributes.Set( "BlockCounts", _blockCounts );
		_shader.Attributes.Set( "ActiveCellCounts", _activeCellCounts );
		_shader.Attributes.Set( "Digests", _digests );
		_shader.Attributes.Set( "CountResults", _countResults );
		_shader.Attributes.Set( "Allocations", _allocations );
		_shader.Attributes.Set( "ChunkSize", _chunkSize );
		_shader.Attributes.Set( "SampleSize", _sampleSize );
		_shader.Attributes.Set( "HaloSize", _haloSize );
		_shader.Attributes.Set( "HaloSampleCount", _haloSampleCount );
		_shader.Attributes.Set( "CellCount", _cellCount );
		_shader.Attributes.Set( "EdgeSlotCount", _edgeSlotCount );
		_shader.Attributes.Set( "EdgeGroupCount", _edgeGroupCount );
		_shader.Attributes.Set( "CellGroupCount", _cellGroupCount );
	}

	private void BindTransitionAttributes()
	{
		foreach ( var shader in new[] { _transitionShader, _transitionEmitVertices, _transitionEmitIndices } )
		{
			shader.Attributes.Set( "Requests", _requests );
			shader.Attributes.Set( "Cells", _cells );
			shader.Attributes.Set( "EdgeVertexIds", _edgeVertexIds );
			shader.Attributes.Set( "EdgeGroupSums", _edgeGroupSums );
			shader.Attributes.Set( "Allocations", _allocations );
			shader.Attributes.Set( "ChunkSize", _chunkSize );
			shader.Attributes.Set( "TransitionSampleSize", _transitionSampleSize );
			shader.Attributes.Set( "TransitionSampleCount", _transitionSampleCount );
			shader.Attributes.Set( "TransitionSampleStride", _transitionSampleStride );
			shader.Attributes.Set( "TransitionCellCount", _transitionCellCount );
			shader.Attributes.Set( "TransitionGroupCount", _transitionGroupCount );
		}
		_transitionShader.Attributes.Set( "DensitySamples", _densitySamples );
		_transitionShader.Attributes.Set( "EdgeFlags", _edgeFlags );
		_transitionShader.Attributes.Set( "CellGroupSums", _cellGroupSums );
		_transitionShader.Attributes.Set( "BlockCounts", _blockCounts );
		_transitionShader.Attributes.Set( "ActiveCellCounts", _activeCellCounts );
		_transitionShader.Attributes.Set( "Digests", _digests );
		_transitionShader.Attributes.Set( "CountResults", _countResults );
		_transitionEmitVertices.Attributes.Set( "DensitySamples", _densitySamples );
		_transitionEmitIndices.Attributes.Set( "CellGroupSums", _cellGroupSums );
	}

	private void SetBatchSize( int count )
	{
		_shader.Attributes.Set( "BatchSize", count );
		_emitVertices.Attributes.Set( "BatchSize", count );
		_emitIndices.Attributes.Set( "BatchSize", count );
		_transitionShader.Attributes.Set( "BatchSize", count );
		_transitionEmitVertices.Attributes.Set( "BatchSize", count );
		_transitionEmitIndices.Attributes.Set( "BatchSize", count );
	}

	private static void Barrier( params GpuBuffer[] buffers )
	{
		foreach ( var buffer in buffers ) Graphics.UavBarrier( buffer );
	}

	public void Dispose()
	{
		lock ( _stateLock ) { if ( _disposed ) return; _disposed = true; }
		_requests.Dispose(); _densitySamples.Dispose(); _cells.Dispose(); _edgeFlags.Dispose(); _edgeVertexIds.Dispose();
		_edgeGroupSums.Dispose(); _cellGroupSums.Dispose(); _blockCounts.Dispose(); _activeCellCounts.Dispose();
		_digests.Dispose(); _countResults.Dispose(); _allocations.Dispose();
	}

	private enum ScratchState { Idle, CountSubmitted, CountReady, EmitReady }
	#pragma warning disable CS0649 // GPU-written structured-buffer layouts.
	private struct GpuCellData { public uint Case; public uint IndexCount; public uint IndexOffset; }
	private struct GpuDigest { public uint Topology; public uint Position; }
	#pragma warning restore CS0649
}
