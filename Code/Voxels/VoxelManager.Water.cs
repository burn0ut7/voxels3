using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed partial class VoxelManager
{
	private SurfaceWaterRenderer _waterRenderer;
	private readonly Dictionary<GpuSdfDescriptor, SurfaceWaterGeometry.Chunk> _waterCells = new();
	private readonly Dictionary<GpuSdfDescriptor, SdfWorldAabb> _waterCellRequests = new();
	private readonly HashSet<GpuSdfDescriptor> _waterUnpublished = new();
	private Dictionary<GpuMeshRegionKey, SurfaceWaterGeometry.Chunk> _waterCoverage = new();
	private Dictionary<GpuMeshRegionKey, SurfaceWaterGeometry.Chunk> _nextWaterCoverage = new();
	private CancellationTokenSource _waterCellCancellation;
	private Task<SurfaceWaterGeometry.Chunk> _waterCellPreparation;
	private long _waterCellRevision;
	private long _waterRenderedCellRevision = -1;
	private long _waterRenderedTerrainRevision = -1;
	private long _waterRenderedPreparedRevision = -1;
	private long _lastClipboxReadinessWaterRevision = -1;
	private int _waterRequestSerial;
	private int _waterObservedSerial = -1;
	private int _waterObservedFieldRevision = -1;
	private int _waterObservedFieldEpoch = -1;
	private long _waterCellsGenerated;
	private double _waterMaximumPublishDelayMilliseconds;
	private double _waterCellGenerationMilliseconds;

	[Property, ReadOnly, Category( "World" )]
	public string WaterStatus => _waterRenderer is null ? "Waiting for terrain" :
		$"{_waterRenderer.ChunkCount:N0} meshed water chunks; {_waterRenderer.VertexCount:N0} vertices; " +
		$"{_waterRenderer.UploadedVertices:N0} uploaded vertices; " +
		$"{_waterCells.Count}/{_waterCellRequests.Count} cell chunks; {_waterCellsGenerated} generated; " +
		$"{_waterCells.Values.Sum( item => (long)item.Cells.RefinedCellCount ):N0} refined cells; " +
		$"{_waterCells.Values.Sum( item => item.Cells.Bytes ) / 1048576.0:F1} MiB cells; {_waterCellGenerationMilliseconds:F1} ms generation; publishMaxMs={_waterMaximumPublishDelayMilliseconds:F3}";

	private bool SurfaceWaterPrepared
	{
		get
		{
			RefreshWaterCellRequests();
			return _waterCellRequests.Keys.All( descriptor => _waterCells.ContainsKey( descriptor ) );
		}
	}

	// This is a chunk-content dependency, not a GPU scheduling dependency.
	private bool IsChunkContentPrepared( GpuSdfDescriptor descriptor ) =>
		!_waterCellRequests.ContainsKey( descriptor ) || _waterCells.ContainsKey( descriptor );

	private bool IsWaterRegionActive( GpuMeshRegionKey key ) =>
		_levels[key.Level].Active.Contains( key.Coordinate ) ||
		key.Level == _partialOuterLevel && _partialOuterChunks.Contains( key.Coordinate );

	private void RefreshWaterCellRequests()
	{
		var field = CurrentField;
		if ( _waterObservedSerial == _waterRequestSerial && _waterObservedFieldRevision == field.Revision &&
			_waterObservedFieldEpoch == field.Epoch ) return;
		_waterCellRequests.Clear();
		foreach ( var state in _levels )
		{
			AddWaterCellRequests( state, staged: false );
			if ( _clipboxPlacementPending && state.PlacementChanged ) AddWaterCellRequests( state, staged: true );
		}
		if ( _partialOuterLevel >= 0 )
		{
			var size = _appliedCellsPerAxis * CellSizeForLevel( _partialOuterLevel );
			var z = (int)MathF.Ceiling( field.Settings.SeaLevel / size ) - 1;
			foreach ( var coordinate in _partialOuterChunks )
			{
				if ( coordinate.z != z ) continue;
				var descriptor = CreateRegularDescriptor( _partialOuterLevel, coordinate, captureRegion: false );
				var origin = new Vector3( coordinate.x, coordinate.y, coordinate.z ) * size;
				_waterCellRequests[descriptor] = new SdfWorldAabb( origin, origin + Vector3.One * size );
			}
		}
		foreach ( var key in _waterCells.Keys.Where( key =>
			key != CreateRegularDescriptor( key.Key.Level, key.Key.Coordinate, captureRegion: false ) ||
			!_levels[key.Key.Level].DesiredCache.Contains( key.Key.Coordinate ) &&
			!(_clipboxPlacementPending && _levels[key.Key.Level].PlacementChanged &&
				_levels[key.Key.Level].NextDesiredCache.Contains( key.Key.Coordinate )) &&
			!(key.Key.Level == _partialOuterLevel && _partialOuterChunks.Contains( key.Key.Coordinate )) ).ToArray() )
		{
			_waterCells.Remove( key );
			_waterUnpublished.Remove( key );
		}
		_waterObservedSerial = _waterRequestSerial;
		_waterObservedFieldRevision = field.Revision;
		_waterObservedFieldEpoch = field.Epoch;
		_waterCellRevision++;
	}

	private void AddWaterCellRequests( TerrainClipboxLevelState state, bool staged )
	{
		var minimum = staged ? state.StagedOuterMinimum : state.OuterMinimum;
		var maximum = staged ? state.StagedOuterMaximum : state.OuterMaximum;
		var active = staged ? state.NextActive : state.Active;
		var size = _appliedCellsPerAxis * CellSizeForLevel( state.Level );
		var z = (int)MathF.Ceiling( CurrentField.Settings.SeaLevel / size ) - 1;
		if ( z < minimum.z || z >= maximum.z ) return;
		// Visit only the sea-plane slice; membership comes from the terrain owner.
		for ( var y = minimum.y; y < maximum.y; y++ )
		for ( var x = minimum.x; x < maximum.x; x++ )
		{
			var coordinate = new Vector3Int( x, y, z );
			if ( !active.Contains( coordinate ) ) continue;
			var descriptor = CreateRegularDescriptor( state.Level, coordinate, captureRegion: false );
			var origin = new Vector3( x * size, y * size, z * size );
			_waterCellRequests[descriptor] = new SdfWorldAabb( origin, origin + Vector3.One * size );
		}
	}

	private void UpdateWaterCellGeneration()
	{
		if ( !_clipboxPlacementTargetAvailable ) return;
		RefreshWaterCellRequests();
		if ( _waterCellPreparation is not null )
		{
			if ( !_waterCellPreparation.IsCompleted ) return;
			var item = _waterCellPreparation.GetAwaiter().GetResult();
			if ( _waterCellRequests.ContainsKey( item.Descriptor ) )
			{
				_waterCells[item.Descriptor] = item;
				_waterUnpublished.Add( item.Descriptor );
			}
			_waterCellsGenerated++;
			_waterCellGenerationMilliseconds += item.Milliseconds;
			_waterCellPreparation = null;
			_waterCellRevision++;
		}
		var position = ActiveStreamingTarget.WorldPosition;
		var distance = float.PositiveInfinity;
		var found = false;
		var descriptor = default(GpuSdfDescriptor);
		var bounds = default(SdfWorldAabb);
		foreach ( var pair in _waterCellRequests )
		{
			if ( _waterCells.ContainsKey( pair.Key ) ) continue;
			var dx = MathF.Max( 0f, MathF.Max( pair.Value.Minimum.x - position.x, position.x - pair.Value.Maximum.x ) );
			var dy = MathF.Max( 0f, MathF.Max( pair.Value.Minimum.y - position.y, position.y - pair.Value.Maximum.y ) );
			var candidateDistance = dx * dx + dy * dy;
			if ( found && (candidateDistance > distance ||
				candidateDistance == distance && pair.Key.Key.Level >= descriptor.Key.Level) ) continue;
			found = true;
			distance = candidateDistance;
			descriptor = pair.Key;
			bounds = pair.Value;
		}
		if ( !found || !CurrentField.TryCaptureRegion( descriptor.SamplingBounds, out var field ) ) return;
		_waterCellCancellation ??= new CancellationTokenSource();
		var cancellation = _waterCellCancellation.Token;
		// Prepare independently; the owning chunk coordinates presentation with terrain.
		_waterCellPreparation = GameTask.RunInThreadAsync( () =>
		{
			cancellation.ThrowIfCancellationRequested();
			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			var cells = GeneratedWaterCells.Generate( bounds, descriptor.CellSize, field, cancellation );
			var vertices = SurfaceWaterGeometry.Build( cells );
			return new SurfaceWaterGeometry.Chunk( descriptor, cells, vertices, System.Diagnostics.Stopwatch.GetElapsedTime( started ).TotalMilliseconds );
		} );
	}

	private void UpdateSurfaceWaterChunks()
	{
		if ( !_clipboxPlacementTargetAvailable || _waterRenderer is null ) return;
		RefreshWaterCellRequests();
		var terrainRevision = _gpuMesher.ResidentPublicationRevision;
		if ( _waterRenderedCellRevision == _waterCellRevision &&
			_waterRenderedTerrainRevision == terrainRevision &&
			_waterRenderedPreparedRevision == _renderPreparedRevision ) return;
		_nextWaterCoverage.Clear();
		foreach ( var request in _waterCellRequests.Keys )
		{
			if ( !IsWaterRegionActive( request.Key ) ) continue;
			if ( _waterCells.TryGetValue( request, out var ready ) && IsTerrainRegionPrepared( request ) )
			{
				// Dry and empty-solid results participate in the same completion contract.
				_nextWaterCoverage.Add( request.Key, ready );
				if ( _waterUnpublished.Remove( request ) )
					_waterMaximumPublishDelayMilliseconds = Math.Max( _waterMaximumPublishDelayMilliseconds,
						System.Diagnostics.Stopwatch.GetElapsedTime( ready.CompletedTimestamp ).TotalMilliseconds );
			}
			else if ( _waterCoverage.TryGetValue( request.Key, out var previous ) &&
				previous.Descriptor == (request with { EditRevision = previous.Descriptor.EditRevision }) )
			{
				// The existing terrain edit group retains the corresponding old solid mesh.
				_nextWaterCoverage.Add( request.Key, previous );
			}
			_gpuMesher.RefreshChunkPresentation( request );
		}
		(_waterCoverage, _nextWaterCoverage) = (_nextWaterCoverage, _waterCoverage);
		_waterRenderer.Update( _waterCoverage, _waterCells );
		_waterRenderedCellRevision = _waterCellRevision;
		_waterRenderedTerrainRevision = terrainRevision;
		_waterRenderedPreparedRevision = _renderPreparedRevision;
	}

	private object CaptureWaterPresentation()
	{
		var waterWithoutTerrain = 0;
		var terrainWithoutWater = 0;
		var awaitingTerrain = 0;
		var awaitingWater = 0;
		foreach ( var pair in _waterCoverage )
		{
			if ( !IsWaterRegionActive( pair.Key ) || !IsTerrainRegionPrepared( pair.Value.Descriptor ) )
				waterWithoutTerrain++;
		}
		foreach ( var descriptor in _waterCellRequests.Keys )
		{
			if ( !IsWaterRegionActive( descriptor.Key ) ) continue;
			if ( !_waterCells.ContainsKey( descriptor ) ) awaitingWater++;
			if ( !IsTerrainRegionPrepared( descriptor ) ) awaitingTerrain++;
			if ( _gpuMesher.IsDrawable( descriptor.Key ) &&
				(!_waterCoverage.TryGetValue( descriptor.Key, out var water ) ||
					!_gpuMesher.IsResident( water.Descriptor )) ) terrainWithoutWater++;
		}
		return new { Published = _waterCoverage.Count, AwaitingTerrain = awaitingTerrain,
			AwaitingWater = awaitingWater, WaterWithoutTerrain = waterWithoutTerrain,
			TerrainWithoutWater = terrainWithoutWater };
	}

	[ConCmd( "voxel_water_info" )]
	public static void LogWaterInfoCommand( int level = 0, int x = 0, int y = 0 )
	{
		if ( !TryGetActiveManager( "water.inspect", out var manager ) || level < 0 || level >= SupportedVisualLevelCount ) return;
		var size = manager._appliedCellsPerAxis * manager.CellSizeForLevel( level );
		var z = (int)MathF.Ceiling( manager.CurrentField.Settings.SeaLevel / size ) - 1;
		var descriptor = manager.CreateRegularDescriptor( level, new Vector3Int( x, y, z ), captureRegion: false );
		if ( !manager._waterCells.TryGetValue( descriptor, out var chunk ) )
		{
			Log.Info( $"[VoxelWorld] water.inspect level={level} coordinate={x},{y},{z} ready=False" );
			return;
		}
		ulong digest = 14695981039346656037UL;
		var invalidVertices = 0;
		var degenerateTriangles = 0;
		var reversedTriangles = 0;
		foreach ( var vertex in chunk.Vertices )
		{
			var position = vertex.Position;
			digest = unchecked((digest ^ (uint)BitConverter.SingleToInt32Bits( position.x )) * 1099511628211UL);
			digest = unchecked((digest ^ (uint)BitConverter.SingleToInt32Bits( position.y )) * 1099511628211UL);
			digest = unchecked((digest ^ (uint)BitConverter.SingleToInt32Bits( position.z )) * 1099511628211UL);
			if ( !float.IsFinite( position.x ) || !float.IsFinite( position.y ) || !float.IsFinite( position.z ) ||
				position.x < chunk.Cells.Bounds.Minimum.x || position.x > chunk.Cells.Bounds.Maximum.x ||
				position.y < chunk.Cells.Bounds.Minimum.y || position.y > chunk.Cells.Bounds.Maximum.y || position.z != chunk.Cells.SeaLevel ) invalidVertices++;
		}
		for ( var index = 0; index + 2 < chunk.Vertices.Length; index += 3 )
		{
			var ab = chunk.Vertices[index + 1].Position - chunk.Vertices[index].Position;
			var ac = chunk.Vertices[index + 2].Position - chunk.Vertices[index].Position;
			var area = ab.x * ac.y - ab.y * ac.x;
			if ( area == 0f ) degenerateTriangles++;
			if ( area < 0f ) reversedTriangles++;
		}
		Log.Info( $"[VoxelWorld] water.inspect level={level} coordinate={x},{y},{z} ready=True " +
			$"requested={manager._waterCellRequests.ContainsKey( descriptor )} vertices={chunk.Vertices.Length} digest={digest:X16} " +
			$"invalidVertices={invalidVertices} degenerateTriangles={degenerateTriangles} reversedTriangles={reversedTriangles} " +
			$"generationMs={chunk.Milliseconds:F3}" );
	}

	private void ResetSurfaceWater()
	{
		_waterRenderer?.Release();
		_waterRenderer = null;
		_waterCellCancellation?.Cancel();
		_waterCellCancellation?.Dispose();
		_waterCellCancellation = null;
		_waterCellPreparation = null;
		_waterCells.Clear();
		_waterCellRequests.Clear();
		_waterCoverage.Clear();
		_nextWaterCoverage.Clear();
		_waterUnpublished.Clear();
		_waterObservedSerial = -1;
		_waterObservedFieldRevision = -1;
		_waterObservedFieldEpoch = -1;
		_waterRenderedCellRevision = -1;
		_waterCellRevision++;
		_waterCellsGenerated = 0;
		_waterRenderedTerrainRevision = -1;
		_waterRenderedPreparedRevision = -1;
		_lastClipboxReadinessWaterRevision = -1;
		_waterMaximumPublishDelayMilliseconds = 0;
		_waterCellGenerationMilliseconds = 0;
	}
}
