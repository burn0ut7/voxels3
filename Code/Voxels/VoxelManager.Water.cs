using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed partial class VoxelManager
{
	private SurfaceWaterRenderer _waterRenderer;
	private readonly Dictionary<GpuSdfDescriptor, SurfaceWaterGeometry.Chunk> _waterCells = new();
	private readonly Dictionary<GpuSdfDescriptor, SdfWorldAabb> _waterCellRequests = new();
	private Dictionary<GpuMeshRegionKey, SurfaceWaterGeometry.Chunk> _waterCoverage = new();
	private Dictionary<GpuMeshRegionKey, SurfaceWaterGeometry.Chunk> _nextWaterCoverage = new();
	private readonly HashSet<GpuMeshRegionKey> _waterCoverageBranches = new();
	private readonly Vector3Int[] _waterCacheMinimum = new Vector3Int[SupportedVisualLevelCount];
	private readonly Vector3Int[] _waterCacheMaximum = new Vector3Int[SupportedVisualLevelCount];
	private CancellationTokenSource _waterCellCancellation;
	private Task<SurfaceWaterGeometry.Chunk> _waterCellPreparation;
	private long _waterCellRevision;
	private long _waterRenderedCellRevision = -1;
	private int _waterRequestSerial;
	private int _waterObservedSerial = -1;
	private int _waterObservedFieldRevision = -1;
	private int _waterObservedFieldEpoch = -1;
	private long _waterCellsGenerated;
	private long _waterReadyToPublishTimestamp;
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

	private void UpdateSurfaceWater()
	{
		if ( !_clipboxPlacementTargetAvailable ) return;
		UpdateWaterCellGeneration();
		UpdateSurfaceWaterChunks();
	}

	private void RefreshWaterCellRequests()
	{
		var field = CurrentField;
		if ( _waterObservedSerial == _waterRequestSerial && _waterObservedFieldRevision == field.Revision &&
			_waterObservedFieldEpoch == field.Epoch ) return;
		_waterCellRequests.Clear();
		var configuration = _targetVisualConfiguration;
		for ( var level = configuration.MinimumVisualLod; level <= configuration.MaximumVisualLod; level++ )
		{
			var size = _appliedCellsPerAxis * CellSizeForLevel( level );
			var z = (int)MathF.Ceiling( field.Settings.SeaLevel / size ) - 1;
			var extent = level == 0 ? configuration.Lod0VisualHalfExtent : configuration.LodCacheHalfExtent;
			var anchor = TargetOuterAnchor( level, configuration );
			var minimum = anchor - new Vector3Int( extent );
			var maximum = anchor + new Vector3Int( extent );
			_waterCacheMinimum[level] = minimum;
			_waterCacheMaximum[level] = maximum;
			if ( z < minimum.z || z >= maximum.z ) continue;
			var hasHole = level > configuration.MinimumVisualLod;
			var childExtent = level == 1 ? configuration.Lod0VisualHalfExtent : configuration.LodCacheHalfExtent;
			var childAnchor = hasHole ? TargetOuterAnchor( level - 1, configuration ) : default;
			var holeMinimum = (childAnchor - new Vector3Int( childExtent )) / 2;
			var holeMaximum = (childAnchor + new Vector3Int( childExtent )) / 2;
			for ( var y = minimum.y; y < maximum.y; y++ )
			for ( var x = minimum.x; x < maximum.x; x++ )
			{
				var coordinate = new Vector3Int( x, y, z );
				if ( hasHole && IsInsideHalfOpenBox( coordinate, holeMinimum, holeMaximum ) ) continue;
				var descriptor = CreateRegularDescriptor( level, coordinate, captureRegion: false );
				var origin = new Vector3( x * size, y * size, z * size );
				_waterCellRequests[descriptor] = new SdfWorldAabb( origin, origin + Vector3.One * size );
			}
		}
		foreach ( var key in _waterCells.Keys.Where( key => !_waterCellRequests.ContainsKey( key ) &&
			(key != CreateRegularDescriptor( key.Key.Level, key.Key.Coordinate, captureRegion: false ) ||
				key.Key.Level < configuration.MinimumVisualLod || key.Key.Level > configuration.MaximumVisualLod ||
				!IsInsideHalfOpenBox( key.Key.Coordinate, _waterCacheMinimum[key.Key.Level], _waterCacheMaximum[key.Key.Level] )) ).ToArray() )
			_waterCells.Remove( key );
		_waterObservedSerial = _waterRequestSerial;
		_waterObservedFieldRevision = field.Revision;
		_waterObservedFieldEpoch = field.Epoch;
		_waterCellRevision++;
	}

	private void UpdateWaterCellGeneration()
	{
		RefreshWaterCellRequests();
		if ( _waterCellPreparation is not null )
		{
			if ( !_waterCellPreparation.IsCompleted ) return;
			var item = _waterCellPreparation.GetAwaiter().GetResult();
			if ( _waterCellRequests.ContainsKey( item.Descriptor ) )
			{
				_waterCells[item.Descriptor] = item;
				_waterReadyToPublishTimestamp = item.CompletedTimestamp;
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
		// Each immutable result can publish on the next update. No batch, neighbor
		// mesh, terrain placement, or other water result participates in readiness.
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
		if ( _waterRenderedCellRevision == _waterCellRevision ) return;
		_nextWaterCoverage.Clear();
		_waterCoverageBranches.Clear();
		// Index only ancestors of existing coverage. Missing branches never recurse.
		foreach ( var pair in _waterCoverage )
		{
			var key = pair.Key;
			while ( key.Level + 1 < SupportedVisualLevelCount )
			{
				key = new GpuMeshRegionKey( key.Level + 1,
					new Vector3Int( key.Coordinate.x >> 1, key.Coordinate.y >> 1, key.Coordinate.z >> 1 ) );
				if ( !_waterCoverageBranches.Add( key ) ) break;
			}
		}
		foreach ( var request in _waterCellRequests.Keys )
		{
			if ( _waterCells.TryGetValue( request, out var ready ) )
			{
				// Empty results also own coverage: dry replacements must hide old water.
				_nextWaterCoverage.Add( request.Key, ready );
				continue;
			}
			RetainWaterCoverage( request.Key );
		}
		(_waterCoverage, _nextWaterCoverage) = (_nextWaterCoverage, _waterCoverage);
		_waterRenderer.Update( _waterCoverage, _waterCells );
		_waterRenderedCellRevision = _waterCellRevision;
		if ( _waterReadyToPublishTimestamp != 0 )
		{
			_waterMaximumPublishDelayMilliseconds = Math.Max( _waterMaximumPublishDelayMilliseconds,
				System.Diagnostics.Stopwatch.GetElapsedTime( _waterReadyToPublishTimestamp ).TotalMilliseconds );
			_waterReadyToPublishTimestamp = 0;
		}
	}

	private void RetainWaterCoverage( GpuMeshRegionKey tile )
	{
		var ancestor = tile;
		while ( true )
		{
			if ( _waterCoverage.TryGetValue( ancestor, out var chunk ) )
			{
				var descriptor = chunk.Descriptor;
				var current = CreateRegularDescriptor( descriptor.Key.Level, descriptor.Key.Coordinate, captureRegion: false );
				// Local edits can retain their last presentation; different worlds cannot.
				if ( descriptor == (current with { EditRevision = descriptor.EditRevision }) )
					_nextWaterCoverage.Add( tile, chunk );
				return;
			}
			if ( ancestor.Level + 1 >= SupportedVisualLevelCount ) break;
			ancestor = new GpuMeshRegionKey( ancestor.Level + 1,
				new Vector3Int( ancestor.Coordinate.x >> 1, ancestor.Coordinate.y >> 1, ancestor.Coordinate.z >> 1 ) );
		}
		if ( tile.Level == 0 || !_waterCoverageBranches.Contains( tile ) ) return;
		// Coarsening retains independently ready child footprints until this parent
		// is ready. Refinement clips a retained parent to each unfinished child.
		var childLevel = tile.Level - 1;
		var childSize = _appliedCellsPerAxis * CellSizeForLevel( childLevel );
		var z = (int)MathF.Ceiling( CurrentField.Settings.SeaLevel / childSize ) - 1;
		for ( var y = 0; y < 2; y++ )
		for ( var x = 0; x < 2; x++ )
			RetainWaterCoverage( new GpuMeshRegionKey( childLevel,
				new Vector3Int( tile.Coordinate.x * 2 + x, tile.Coordinate.y * 2 + y, z ) ) );
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
		_waterCoverageBranches.Clear();
		_waterObservedSerial = -1;
		_waterObservedFieldRevision = -1;
		_waterObservedFieldEpoch = -1;
		_waterRenderedCellRevision = -1;
		_waterCellRevision++;
		_waterCellsGenerated = 0;
		_waterReadyToPublishTimestamp = 0;
		_waterMaximumPublishDelayMilliseconds = 0;
		_waterCellGenerationMilliseconds = 0;
	}
}
