using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed partial class VoxelManager
{
	private SurfaceWaterRenderer _waterRenderer;
	private readonly Dictionary<GpuSdfDescriptor, SurfaceWaterGeometry.Chunk> _waterCells = new();
	private readonly Dictionary<GpuSdfDescriptor, SdfWorldAabb> _waterCellRequests = new();
	private List<SurfaceWaterGeometry.Chunk> _waterChunks = new();
	private List<SurfaceWaterGeometry.Chunk> _nextWaterChunks = new();
	private readonly HashSet<GpuMeshRegionKey> _waterReplacementKeys = new();
	private CancellationTokenSource _waterCellCancellation;
	private Task<SurfaceWaterGeometry.Chunk> _waterCellPreparation;
	private long _waterCellRevision;
	private long _waterRenderedCellRevision = -1;
	private int _waterRequestSerial;
	private int _waterObservedSerial = -1;
	private int _waterObservedFieldRevision = -1;
	private int _waterObservedFieldEpoch = -1;
	private long _waterCellsGenerated;
	private double _waterCellGenerationMilliseconds;

	[Property, ReadOnly, Category( "World" )]
	public string WaterStatus => _waterRenderer is null ? "Waiting for terrain" :
		$"{_waterRenderer.ChunkCount:N0} meshed water chunks; {_waterRenderer.VertexCount:N0} vertices; " +
		$"{_waterRenderer.UploadedVertices:N0} uploaded vertices; " +
		$"{_waterCells.Count}/{_waterCellRequests.Count} cell chunks; {_waterCellsGenerated} generated; " +
		$"{_waterCells.Values.Sum( item => (long)item.Cells.RefinedCellCount ):N0} refined cells; " +
		$"{_waterCells.Values.Sum( item => item.Cells.Bytes ) / 1048576.0:F1} MiB cells; {_waterCellGenerationMilliseconds:F1} ms generation";

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
				!_levels[key.Key.Level].DesiredCache.Contains( key.Key.Coordinate ) &&
				!(_clipboxPlacementPending && _levels[key.Key.Level].PlacementChanged &&
					_levels[key.Key.Level].NextDesiredCache.Contains( key.Key.Coordinate ))) ).ToArray() )
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
			if ( _waterCellRequests.ContainsKey( item.Descriptor ) ) _waterCells[item.Descriptor] = item;
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
		_nextWaterChunks.Clear();
		_waterReplacementKeys.Clear();
		foreach ( var pair in _waterCells )
		{
			var descriptor = pair.Key;
			if ( _waterCellRequests.ContainsKey( descriptor ) )
			{
				_nextWaterChunks.Add( pair.Value );
				_waterReplacementKeys.Add( descriptor.Key );
			}
		}
		// An edit invalidates generated data before its replacement can be published.
		// Keep the last draw for that resident cell until its replacement is ready.
		foreach ( var chunk in _waterChunks )
		{
			var descriptor = chunk.Descriptor;
			if ( _waterReplacementKeys.Contains( descriptor.Key ) ) continue;
			var current = CreateRegularDescriptor( descriptor.Key.Level, descriptor.Key.Coordinate, captureRegion: false );
			if ( !_waterCellRequests.ContainsKey( current ) ) continue;
			// Only a local edit may retain presentation; recipe, epoch and layout changes may not.
			if ( descriptor != (current with { EditRevision = descriptor.EditRevision }) ) continue;
			_nextWaterChunks.Add( chunk );
		}
		var changed = _waterRenderedCellRevision < 0 || _waterChunks.Count != _nextWaterChunks.Count;
		for ( var index = 0; !changed && index < _waterChunks.Count; index++ ) changed = _waterChunks[index] != _nextWaterChunks[index];
		if ( changed )
		{
			(_waterChunks, _nextWaterChunks) = (_nextWaterChunks, _waterChunks);
			_waterRenderer.Update( _waterChunks );
		}
		_waterRenderedCellRevision = _waterCellRevision;
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
		_waterChunks.Clear();
		_nextWaterChunks.Clear();
		_waterReplacementKeys.Clear();
		_waterObservedSerial = -1;
		_waterObservedFieldRevision = -1;
		_waterObservedFieldEpoch = -1;
		_waterRenderedCellRevision = -1;
		_waterCellRevision++;
		_waterCellsGenerated = 0;
		_waterCellGenerationMilliseconds = 0;
	}
}
