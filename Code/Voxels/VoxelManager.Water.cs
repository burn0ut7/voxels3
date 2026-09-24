using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed partial class VoxelManager
{
	/// <summary>Read the generated reservoir level, including inland rivers, for dry-ground placement.</summary>
	public bool TryGetSurfaceWaterLevel( Vector3 position, out float level )
	{
		level = 0f;
		if ( _terrainField is null || !float.IsFinite( position.x ) || !float.IsFinite( position.y ) || !float.IsFinite( position.z ) ||
			MathF.Max( MathF.Abs( position.x ), MathF.Max( MathF.Abs( position.y ), MathF.Abs( position.z ) ) ) > TerrainField.MaximumWorldCoordinate ) return false;
		var settings = CurrentField.Settings;
		var landform = RegionalLandforms.SampleNatural( position, settings );
		level = RiverWorld.For( settings ).GetPatch( RiverNetwork.PatchAt( position ) ).SampleWorld( position, landform.Height, settings.SeaLevel ).WaterHeight;
		return true;
	}

	private float _waterVisibilityMeters = 3f;
	private float _waterFlowSpeed = 32f;
	private float _waterRippleStrength = 0.16f;

	/// <summary>Distance through water at which half the bed remains visible; smaller values are cloudier.</summary>
	[Property, Category( "Water" ), Range( 0.05f, 30f )]
	public float WaterVisibilityMeters
	{
		get => _waterVisibilityMeters;
		set => _waterVisibilityMeters = float.IsFinite( value ) ? Math.Clamp( value, 0.05f, 30f ) : 3f;
	}

	[Property, Category( "Water" )]
	public Color WaterTint { get; set; } = new( 0.12f, 0.4f, 0.34f );

	/// <summary>Shading advection in world units/second; does not change the generated water volume.</summary>
	[Property, Category( "Water" ), Range( 0f, 160f )]
	public float WaterFlowSpeed
	{
		get => _waterFlowSpeed;
		set => _waterFlowSpeed = float.IsFinite( value ) ? Math.Clamp( value, 0f, 160f ) : 32f;
	}

	[Property, Category( "Water" ), Range( 0f, 0.5f )]
	public float WaterRippleStrength
	{
		get => _waterRippleStrength;
		set => _waterRippleStrength = float.IsFinite( value ) ? Math.Clamp( value, 0f, 0.5f ) : 0.16f;
	}

	private SurfaceWaterRenderer _waterRenderer;
	private readonly Dictionary<GpuSdfDescriptor, SurfaceWaterGeometry.Chunk> _waterCells = new();
	private readonly Dictionary<GpuSdfDescriptor, SdfWorldAabb> _waterCellRequests = new();
	private readonly HashSet<GpuSdfDescriptor> _waterUnpublished = new();
	private Dictionary<GpuMeshRegionKey, SurfaceWaterGeometry.Chunk> _waterCoverage = new();
	private readonly HashSet<GpuSdfDescriptor> _waterPending = new();
	private readonly HashSet<GpuMeshRegionKey> _waterPresentationDirty = new();
	private bool _waterRendererDirty;
	private CancellationTokenSource _waterCellCancellation;
	private Task<SurfaceWaterGeometry.Chunk> _waterCellPreparation;
	private long _waterCellRevision;
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
		$"{_waterRenderer.BatchCount} draw batches; {_waterRenderer.FlowTextureBytes / 1048576.0:F1} MiB flow textures; " +
		$"{_waterCells.Count}/{_waterCellRequests.Count} cell chunks; {_waterCellsGenerated} generated; " +
		$"{_waterCells.Values.Sum( item => (long)item.Cells.RefinementSamples ):N0} refinement samples; " +
		$"{_waterCells.Values.Sum( item => item.Cells.Bytes ) / 1048576.0:F1} MiB cells; {_waterCellGenerationMilliseconds:F1} ms generation; publishMaxMs={_waterMaximumPublishDelayMilliseconds:F3}";

	private bool SurfaceWaterPrepared
	{
		get
		{
			RefreshWaterCellRequests();
			return _waterPending.Count == 0;
		}
	}

	// This is a chunk-content dependency, not a GPU scheduling dependency.
	private bool IsChunkContentPrepared( GpuSdfDescriptor descriptor ) =>
		!_waterCellRequests.ContainsKey( descriptor ) || _waterCells.ContainsKey( descriptor );

	private bool IsWaterRegionActive( GpuMeshRegionKey key ) =>
		_gpuMesher.IsRenderActive( key );

	private void RefreshWaterCellRequests()
	{
		var field = CurrentField;
		if ( _waterObservedSerial == _waterRequestSerial && _waterObservedFieldRevision == field.Revision &&
			_waterObservedFieldEpoch == field.Epoch ) return;
		foreach ( var descriptor in _waterCellRequests.Keys ) _waterPresentationDirty.Add( descriptor.Key );
		_waterCellRequests.Clear();
		foreach ( var key in _gpuMesher.ActiveRegionKeys ) AddCoverageWaterRequest( key );
		foreach ( var key in _coverageRequired ) AddCoverageWaterRequest( key );
		foreach ( var key in _predictionRegions ) AddCoverageWaterRequest( key );
		if ( _clipboxPlacementPending )
			foreach ( var state in _levels )
				AddWaterCellRequests( state, staged: state.PlacementChanged );
		foreach ( var key in _waterCells.Keys.Where( key =>
			key != CreateRegularDescriptor( key.Key.Level, key.Key.Coordinate, captureRegion: false ) ||
			!_predictionRegions.Contains( key.Key ) && !_gpuMesher.IsRenderActive( key.Key ) && !_coverageRequired.Contains( key.Key ) &&
			!_levels[key.Key.Level].DesiredCache.Contains( key.Key.Coordinate ) &&
			!(_clipboxPlacementPending && _levels[key.Key.Level].PlacementChanged &&
				_levels[key.Key.Level].NextDesiredCache.Contains( key.Key.Coordinate )) &&
			!(key.Key.Level == _partialOuterLevel && _partialOuterChunks.Contains( key.Key.Coordinate )) ).ToArray() )
		{
			_waterCells.Remove( key );
			_waterUnpublished.Remove( key );
			_waterRendererDirty = true;
		}
		_waterPending.Clear();
		foreach ( var descriptor in _waterCellRequests.Keys )
		{
			if ( !_waterCells.ContainsKey( descriptor ) ) _waterPending.Add( descriptor );
			_waterPresentationDirty.Add( descriptor.Key );
		}
		_waterObservedSerial = _waterRequestSerial;
		_waterObservedFieldRevision = field.Revision;
		_waterObservedFieldEpoch = field.Epoch;
		_waterCellRevision++;
	}

	private bool AddCoverageWaterRequest( GpuMeshRegionKey key )
	{
		var size = _appliedCellsPerAxis * CellSizeForLevel( key.Level );
		var z = (int)MathF.Ceiling( CurrentField.Settings.SeaLevel / size ) - 1;
		if ( key.Coordinate.z != z ) return false;
		var descriptor = CreateRegularDescriptor( key.Level, key.Coordinate, captureRegion: false );
		var origin = new Vector3( key.Coordinate.x, key.Coordinate.y, key.Coordinate.z ) * size;
		return _waterCellRequests.TryAdd( descriptor, new SdfWorldAabb( origin, origin + Vector3.One * size ) );
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
		_waterRenderer?.SetAppearance( WaterTint, WaterVisibilityMeters, WaterFlowSpeed, WaterRippleStrength,
			Scene.Camera.IsValid() ? Scene.Camera.BackgroundColor : Color.Black );
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
				_waterPending.Remove( item.Descriptor );
				_waterPresentationDirty.Add( item.Descriptor.Key );
			}
			_waterCellsGenerated++;
			_waterCellGenerationMilliseconds += item.Milliseconds;
			_waterCellPreparation = null;
			_waterCellRevision++;
		}
		if ( _waterPending.Count == 0 ) return;
		var position = ActiveStreamingTarget.WorldPosition;
		var priority = (Service: int.MaxValue, Distance: float.PositiveInfinity, Level: 0, Z: 0, Y: 0, X: 0);
		var found = false;
		var descriptor = default(GpuSdfDescriptor);
		var bounds = default(SdfWorldAabb);
		foreach ( var request in _waterPending )
		{
			var requestBounds = _waterCellRequests[request];
			var dx = MathF.Max( 0f, MathF.Max( requestBounds.Minimum.x - position.x, position.x - requestBounds.Maximum.x ) );
			var dy = MathF.Max( 0f, MathF.Max( requestBounds.Minimum.y - position.y, position.y - requestBounds.Maximum.y ) );
			var candidateDistance = dx * dx + dy * dy;
			var key = request.Key;
			// Match terrain/seam priority: finish admitted publication dependencies
			// before background layout water can occupy every local operation slot.
			var candidatePriority = (_coverageRequired.Contains( key ) ? 0 : IsExteriorCoverageRequired( key ) ? 1 :
				_predictionRegions.Contains( key ) && !IsWaterRegionActive( key ) &&
				!(_clipboxPlacementPending && (_levels[key.Level].PlacementChanged ? _levels[key.Level].NextActive : _levels[key.Level].Active).Contains( key.Coordinate )) ? 3 : 2,
				candidateDistance, key.Level, key.Coordinate.z, key.Coordinate.y, key.Coordinate.x);
			if ( found && candidatePriority.CompareTo( priority ) >= 0 ) continue;
			found = true;
			priority = candidatePriority;
			descriptor = request;
			bounds = requestBounds;
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

	private void MarkWaterPresentationDirty( GpuMeshRegionKey key )
	{
		var size = _appliedCellsPerAxis * CellSizeForLevel( key.Level );
		if ( size <= 0f || key.Coordinate.z != (int)MathF.Ceiling( CurrentTerrainSettings.SeaLevel / size ) - 1 ) return;
		_waterPresentationDirty.Add( key );
	}

	private void UpdateSurfaceWaterChunks()
	{
		if ( !_clipboxPlacementTargetAvailable || _waterRenderer is null ) return;
		RefreshWaterCellRequests();
		foreach ( var key in _waterPresentationDirty )
		{
			var request = CreateRegularDescriptor( key.Level, key.Coordinate, captureRegion: false );
			if ( !_waterCellRequests.ContainsKey( request ) || !IsWaterRegionActive( key ) )
			{
				_waterRendererDirty |= _waterCoverage.Remove( key );
				continue;
			}
			_waterCoverage.TryGetValue( key, out var previous );
			if ( _waterCells.TryGetValue( request, out var ready ) && IsTerrainRegionPrepared( request ) )
			{
				// Empty and dry results complete the same chunk-content dependency.
				if ( !ReferenceEquals( previous, ready ) )
				{
					_waterCoverage[key] = ready;
					_waterRendererDirty = true;
				}
				if ( _waterUnpublished.Remove( request ) )
					_waterMaximumPublishDelayMilliseconds = Math.Max( _waterMaximumPublishDelayMilliseconds,
						System.Diagnostics.Stopwatch.GetElapsedTime( ready.CompletedTimestamp ).TotalMilliseconds );
			}
			else if ( previous is not null &&
				previous.Descriptor != (request with { EditRevision = previous.Descriptor.EditRevision }) )
			{
				_waterCoverage.Remove( key );
				_waterRendererDirty = true;
			}
			// Retain matching old water while the existing edit group retains terrain.
			_gpuMesher.RefreshChunkPresentation( request );
		}
		_waterPresentationDirty.Clear();
		if ( !_waterRendererDirty ) return;
		_waterRenderer.Update( _waterCoverage, _waterCells );
		_waterRendererDirty = false;
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
		var flowSamples = chunk.Cells.Flow;
		var flowing = 0;
		var invalidFlow = 0;
		if ( flowSamples is not null )
		{
			for ( var index = 0; index + 3 < flowSamples.Length; index += 4 )
			{
				var flow = new Vector2( (flowSamples[index] - 128) / 127f, (flowSamples[index + 1] - 128) / 127f );
				if ( flow != Vector2.Zero ) flowing++;
				// Each signed component rounds by at most 1/254; the decoded unit
				// vector can therefore have squared length up to 1.01117.
				if ( flowSamples[index] == 0 || flowSamples[index + 1] == 0 || flow.LengthSquared > 1.012f ) invalidFlow++;
			}
		}
		Log.Info( $"[VoxelWorld] water.inspect level={level} coordinate={x},{y},{z} ready=True " +
			$"requested={manager._waterCellRequests.ContainsKey( descriptor )} vertices={chunk.Vertices.Length} digest={digest:X16} " +
			$"invalidVertices={invalidVertices} degenerateTriangles={degenerateTriangles} reversedTriangles={reversedTriangles} " +
			$"generationMs={chunk.Milliseconds:F3} flowSamples={(flowSamples?.Length ?? 0) / 4} flowingSamples={flowing} invalidFlow={invalidFlow}" );
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
		_waterPending.Clear();
		_waterPresentationDirty.Clear();
		_waterRendererDirty = false;
		_waterUnpublished.Clear();
		_waterObservedSerial = -1;
		_waterObservedFieldRevision = -1;
		_waterObservedFieldEpoch = -1;
		_waterCellRevision++;
		_waterCellsGenerated = 0;
		_lastClipboxReadinessWaterRevision = -1;
		_waterMaximumPublishDelayMilliseconds = 0;
		_waterCellGenerationMilliseconds = 0;
	}
}
