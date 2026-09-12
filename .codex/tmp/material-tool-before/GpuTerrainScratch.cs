using System;

internal sealed class GpuTerrainScratch : IDisposable
{
	public const int MaximumBatchSize = GpuVoxelMesher.MaximumDispatchesPerUpdate;
	private readonly object _stateLock = new();
	// Do not fold the two output writers into the multi-stage shader. On s&box 26.08.19,
	// that composition crashes the native VFX variable parser during a cold resource load.
	// See Docs/AgentRoutes/meshing.md, "s&box VFX Shader Parser Gotcha".
	private readonly ComputeShader _shader = new( "shaders/voxels/voxel_persistent_geometry_cs.shader" );
	private readonly ComputeShader _emitVertices = new( "shaders/voxels/voxel_emit_vertices_cs.shader" );
	private readonly ComputeShader _emitIndices = new( "shaders/voxels/voxel_emit_indices_cs.shader" );
	private readonly GpuBuffer<GpuTerrainRequest> _requests;
	private readonly GpuBuffer<float> _densitySamples;
	private readonly GpuBuffer<int> _topology;
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
	private GpuTerrainCountResult[] _completedCounts;
	private int _completedCount;
	private int _batchSize;
	private long _readbackTimestamp;
	private long _readbackReadyTimestamp;
	private ScratchState _state;
	private bool _disposed;
	private readonly GpuRiverAtlas _riverAtlas = new();
	private readonly System.Threading.CancellationTokenSource _riverCancellation = new();
	private System.Threading.Tasks.Task<GpuRiverAtlas.Data> _riverPreparation;
	private GpuTerrainRequest[] _preparedRequests;
	private TerrainFieldSnapshot[] _preparedFields;

	private float[] _correctionUpload;
	public long CorrectionUploadBytes => (_correctionUpload?.LongLength ?? 0) * sizeof( float );
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
		_requests = new GpuBuffer<GpuTerrainRequest>( MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Requests" );
		_densitySamples = new GpuBuffer<float>( checked( (_haloSampleCount + _haloSize * _haloSize * 2) * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Density" );
		_cells = new GpuBuffer<GpuCellData>( checked( _cellCount * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Cells" );
		// Second plane stores generated material weights at the resolved cell edge.
		_edgeFlags = new GpuBuffer<uint>( checked( _edgeSlotCount * MaximumBatchSize * 2 ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Edge Flags" );
		_edgeVertexIds = new GpuBuffer<uint>( checked( _edgeSlotCount * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Edge IDs" );
		_edgeGroupSums = new GpuBuffer<uint>( checked( _edgeGroupCount * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Edge Groups" );
		_cellGroupSums = new GpuBuffer<uint>( checked( _cellGroupCount * MaximumBatchSize ), GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Cell Groups" );
		_blockCounts = new GpuBuffer<uint>( MaximumBatchSize * 2, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Counts" );
		_activeCellCounts = new GpuBuffer<uint>( MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Active Cells" );
		_digests = new GpuBuffer<GpuDigest>( MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Digests" );
		_countResults = new GpuBuffer<GpuTerrainCountResult>( MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Count Results" );
		_allocations = new GpuBuffer<GpuTerrainAllocationDescriptor>( MaximumBatchSize, GpuBuffer.UsageFlags.Structured, "Voxel Terrain Scratch Allocations" );
		// Upload the existing generated view of the canonical topology once per scratch.
		int[] topology = [ .. VoxelCollisionTables.RegularCellClass,
			.. VoxelCollisionTables.RegularCellGeometryCounts, .. VoxelCollisionTables.RegularCellVertexIndices,
			.. VoxelCollisionTables.RegularVertexData ];
		_topology = new GpuBuffer<int>( topology.Length, GpuBuffer.UsageFlags.Structured, "Voxel Regular Topology" );
		_topology.SetData<int>( topology );
		foreach ( var shader in new[] { _shader, _emitIndices } )
		{
			shader.Attributes.Set( "RegularTopology", _topology );
			var offset = 0;
			shader.Attributes.Set( "RegularCellClassOffset", offset );
			offset += VoxelCollisionTables.RegularCellClass.Length;
			shader.Attributes.Set( "RegularCellGeometryCountsOffset", offset );
			offset += VoxelCollisionTables.RegularCellGeometryCounts.Length;
			shader.Attributes.Set( "RegularCellVertexIndicesOffset", offset );
			offset += VoxelCollisionTables.RegularCellVertexIndices.Length;
			shader.Attributes.Set( "RegularVertexDataOffset", offset );
		}
		GpuVoxelMaterials.BindGeneration( _shader.Attributes );
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
		CapacityBytes =
			(long)MaximumBatchSize * 96 +
			(long)(_haloSampleCount + _haloSize * _haloSize * 2) * MaximumBatchSize * sizeof( float ) +
			(long)_cellCount * MaximumBatchSize * 12 +
			(long)_edgeSlotCount * MaximumBatchSize * sizeof( uint ) * 3 +
			(long)(_edgeGroupCount + _cellGroupCount) * MaximumBatchSize * sizeof( uint ) +
			(long)MaximumBatchSize * (sizeof( uint ) * 8 + 64 + sizeof( uint ) * 5) +
			(long)topology.Length * sizeof( int );
	}

	public bool TrySubmitCount( GpuTerrainRequest[] requests, int count, out double submissionMilliseconds, TerrainFieldSnapshot[] fields = null )
	{
		lock ( _stateLock )
		{
			if ( _disposed || _state != ScratchState.Idle || requests is null || count is < 1 or > MaximumBatchSize )
			{
				submissionMilliseconds = 0;
				return false;
			}
			_state = ScratchState.PreparingRivers;
			_batchSize = count;
		}
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		_preparedRequests = requests;
		_preparedFields = fields;
		var first = requests[0];
		var settings = new ProceduralTerrainSettings( (int)first.Terrain.x, first.Terrain.y, first.Terrain.z,
			first.Terrain.w, first.TerrainScales.x, first.TerrainScales.y, first.TerrainScales.z,
			first.TerrainScales.w, first.TerrainShape.x, first.TerrainShape.y );
		var minimum = new Vector3( float.PositiveInfinity );
		var maximum = new Vector3( float.NegativeInfinity );
		for ( var index = 0; index < count; index++ )
		{
			var origin = requests[index].OriginAndCellSize;
			var low = new Vector3( origin.x, origin.y, origin.z ) - Vector3.One * origin.w;
			var high = low + Vector3.One * ((_chunkSize + 2) * origin.w);
			minimum = new Vector3( MathF.Min( minimum.x, low.x ), MathF.Min( minimum.y, low.y ), MathF.Min( minimum.z, low.z ) );
			maximum = new Vector3( MathF.Max( maximum.x, high.x ), MathF.Max( maximum.y, high.y ), MathF.Max( maximum.z, high.z ) );
		}
		var bounds = new SdfWorldAabb( minimum, maximum );
		var cancellation = _riverCancellation.Token;
		_riverPreparation = _riverAtlas.Prepare( settings, bounds, cancellation );
		submissionMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		return true;
	}

	public bool TryContinueCount()
	{
		lock ( _stateLock )
		{
			if ( _disposed || _state != ScratchState.PreparingRivers || !_riverPreparation.IsCompleted ) return false;
			_state = ScratchState.CountSubmitted;
		}
		_riverAtlas.Update( _riverPreparation.GetAwaiter().GetResult() );
		_riverAtlas.Bind( _shader.Attributes );
		_riverPreparation = null;
		var requests = _preparedRequests;
		var fields = _preparedFields;
		var count = _batchSize;
		_preparedRequests = null;
		_preparedFields = null;
		_requests.SetData( new Span<GpuTerrainRequest>( requests, 0, count ) );
		if ( fields is not null )
		{
			_correctionUpload ??= new float[_haloSampleCount];
			for ( var block = 0; block < count; block++ )
			{
				var field = fields[block];
				if ( field is null ) continue;
				var origin = requests[block].OriginAndCellSize;
				var step = (int)(origin.w / TerrainField.SampleSpacing);
				var sampleOrigin = new Vector3Int( (int)(origin.x / TerrainField.SampleSpacing) - step,
					(int)(origin.y / TerrainField.SampleSpacing) - step, (int)(origin.z / TerrainField.SampleSpacing) - step );
				field.CopyLatticeCorrections( sampleOrigin, step, _haloSize, _correctionUpload );
				_densitySamples.SetData<float>( _correctionUpload.AsSpan(), block * _haloSampleCount );
			}
		}
		SetBatchSize( count );
		foreach ( var buffer in new GpuBuffer[] { _densitySamples, _cells, _edgeFlags, _edgeVertexIds, _edgeGroupSums, _cellGroupSums, _blockCounts, _activeCellCounts, _digests, _countResults } )
			Graphics.ResourceBarrierTransition( buffer, Sandbox.Rendering.ResourceState.UnorderedAccess );
		_shader.Attributes.Set( "PersistentStage", 0 );
		_shader.Dispatch( Math.Max( _cellCount, _edgeSlotCount ) * count, 1, 1 );
		Barrier( _cells, _edgeFlags, _edgeVertexIds, _activeCellCounts, _digests );
		// The exterior is XY-only. Evaluate it once per column, retaining the
		// existing density buffer binding and the canonical cave/cliff composition.
		_shader.Attributes.Set( "PersistentStage", 9 );
		_shader.Dispatch( _haloSize * _haloSize * count, 1, 1 );
		Barrier( _densitySamples );
		_shader.Attributes.Set( "PersistentStage", 1 );
		_shader.Dispatch( _haloSampleCount * count, 1, 1 );
		Barrier( _densitySamples );
		_shader.Attributes.Set( "PersistentStage", 2 );
		_shader.Dispatch( _cellCount * count, 1, 1 );
		Barrier( _cells, _edgeFlags, _activeCellCounts, _digests );
		_shader.Attributes.Set( "PersistentStage", 3 );
		_shader.Dispatch( _edgeGroupCount * 256 * count, 1, 1 );
		Barrier( _edgeVertexIds, _edgeGroupSums );
		_shader.Attributes.Set( "PersistentStage", 6 );
		_shader.Dispatch( _edgeSlotCount * count, 1, 1 );
		Barrier( _digests, _edgeFlags );
		_shader.Attributes.Set( "PersistentStage", 8 );
		_shader.Dispatch( _cellCount * count, 1, 1 );
		Barrier( _cells );
		_shader.Attributes.Set( "PersistentStage", 4 );
		_shader.Dispatch( _cellGroupCount * 256 * count, 1, 1 );
		Barrier( _cells, _cellGroupSums );
		_shader.Attributes.Set( "PersistentStage", 5 );
		_shader.Dispatch( 256 * count, 1, 1 );
		Barrier( _edgeGroupSums, _cellGroupSums, _blockCounts );
		_shader.Attributes.Set( "PersistentStage", 7 );
		_shader.Dispatch( count, 1, 1 );
		Barrier( _countResults );
		_readbackTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
		_countResults.GetDataAsync<GpuTerrainCountResult>( OnCountsRead, 0, count );
		return true;
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

	// Explicit diagnostic readback of the real count pass, before scratch reuse.
	public float[] ReadDensitySamples( int block )
	{
		lock ( _stateLock )
		{
			if ( _disposed || _state != ScratchState.EmitReady || block < 0 || block >= _batchSize )
				throw new InvalidOperationException( "Density inspection requires a completed live count block." );
		}
		var samples = new float[_haloSampleCount];
		_densitySamples.GetData<float>( samples.AsSpan(), block * _haloSampleCount, _haloSampleCount );
		return samples;
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
		_emitVertices.Dispatch( _edgeSlotCount * count, 1, 1 );
		Barrier( vertices );
		_emitIndices.Dispatch( _cellCount * count, 1, 1 );
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
		_shader.Attributes.Set( "MinimumAreaSquaredRelative", GpuVoxelMesher.MinimumTriangleAreaSquaredRelative );
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

	private void SetBatchSize( int count )
	{
		_shader.Attributes.Set( "BatchSize", count );
		_emitVertices.Attributes.Set( "BatchSize", count );
		_emitIndices.Attributes.Set( "BatchSize", count );
	}

	private static void Barrier( params GpuBuffer[] buffers )
	{
		foreach ( var buffer in buffers ) Graphics.UavBarrier( buffer );
	}

	public void Dispose()
	{
		lock ( _stateLock ) { if ( _disposed ) return; _disposed = true; }
		_riverCancellation.Cancel();
		_riverCancellation.Dispose();
		_riverAtlas.Dispose();
		_requests.Dispose(); _densitySamples.Dispose(); _cells.Dispose(); _edgeFlags.Dispose(); _edgeVertexIds.Dispose();
		_edgeGroupSums.Dispose(); _cellGroupSums.Dispose(); _blockCounts.Dispose(); _activeCellCounts.Dispose();
		_digests.Dispose(); _countResults.Dispose(); _allocations.Dispose(); _topology.Dispose();
	}

	private enum ScratchState { Idle, PreparingRivers, CountSubmitted, CountReady, EmitReady }
	#pragma warning disable CS0649 // GPU-written structured-buffer layouts.
	private struct GpuCellData { public uint Case; public uint IndexCount; public uint IndexOffset; }
	private struct GpuDigest { public uint Topology; public uint Position; }
	#pragma warning restore CS0649
}
