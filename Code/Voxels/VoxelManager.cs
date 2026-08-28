using System;
using System.Diagnostics;

/// <summary>
/// Owns the canonical loaded voxel chunks and streams them around one world-space
/// target. Chunks are deterministic managed data rather than networked objects.
/// </summary>
public sealed class VoxelManager : Component
{
	private readonly Dictionary<Vector3Int, VoxelChunk> _loadedChunks = new();
	private readonly HashSet<Vector3Int> _desiredChunks = new();
	private readonly Queue<Vector3Int> _pendingChunks = new();
	private readonly List<Vector3Int> _coordinateBuffer = new();

	private bool _hasStreamingCenter;
	private bool _streamInProgress;
	private Vector3Int _streamingCenterCoordinate;
	private long _streamStartedTimestamp;
	private int _generatedThisStream;
	private int _retainedThisStream;
	private int _unloadedThisStream;
	private float _generationMillisecondsThisStream;
	private int _appliedCellsPerAxis;
	private float _appliedCellSize;
	private int _appliedHorizontalRadius;
	private int _appliedMinimumChunkZ;
	private int _appliedMaximumChunkZ;
	private int _appliedChunksPerFrame;
	private float _appliedTerrainSurfaceHeight;
	private string _lastConfigurationError = string.Empty;
	private GameObject _resolvedStreamingTarget;
	private GameObject ActiveStreamingTarget => StreamingTarget ?? _resolvedStreamingTarget ?? GameObject;

	[Property, Category( "Chunk Configuration" ), Range( 4, 64 )]
	public int CellsPerAxis { get; set; } = 32;

	[Property, Category( "Chunk Configuration" ), Range( 1f, 128f )]
	public float CellSize { get; set; } = 16f;

	[Property, Category( "Chunk Configuration" ), Range( 0, 16 )]
	public int HorizontalLoadRadius { get; set; } = 4;

	[Property, Category( "Chunk Configuration" ), Range( -64, 64 )]
	public int MinimumLoadedChunkZ { get; set; } = -2;

	[Property, Category( "Chunk Configuration" ), Range( -64, 64 )]
	public int MaximumLoadedChunkZ { get; set; } = 6;

	[Property, Category( "Chunk Configuration" ), Range( 1, 128 )]
	public int ChunkLoadsPerFrame { get; set; } = 8;

	[Property, Category( "Chunk Configuration" )]
	public float TerrainSurfaceHeight { get; set; } = 0f;

	[Property, Category( "Chunk Configuration" )]
	public GameObject StreamingTarget { get; set; }

	[Property, Category( "Debug Visualization" )]
	public bool ShowLoadedChunkBounds { get; set; } = false;

	[Property, Category( "Debug Visualization" )]
	public bool ShowLoadedChunkLabels { get; set; } = false;

	[Property, Category( "Debug Visualization" )]
	public bool ShowSelectedCellSlice { get; set; } = false;

	[Property, Category( "Debug Visualization" )]
	public bool LogChunkLifecycle { get; set; } = false;

	[Property, Category( "Debug Selection" )]
	public Vector3Int SelectedChunkCoordinate { get; set; } = Vector3Int.Zero;

	[Property, Category( "Debug Selection" )]
	public Vector3Int SelectedLocalSample { get; set; } = Vector3Int.Zero;

	[Property, Category( "Debug Selection" ), Range( 0, 63 )]
	public int SelectedCellSliceZ { get; set; } = 0;

	[Property, ReadOnly, Category( "World Status" )]
	public int LoadedChunkCount { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public int PendingChunkCount { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public long LoadedDensitySampleCount { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public string EstimatedDensityMemory { get; private set; } = "0 bytes";

	[Property, ReadOnly, Category( "World Status" )]
	public string StreamingCenter { get; private set; } = "Not initialized";

	[Property, ReadOnly, Category( "World Status" )]
	public string LastStreamSummary { get; private set; } = "No stream completed";

	[Property, ReadOnly, Category( "World Status" )]
	public float LastStreamSettleMilliseconds { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public float LastChunkGenerationMilliseconds { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public float SlowestChunkGenerationMilliseconds { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public float LastStreamGenerationMilliseconds { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public float LastEffectiveChunksPerSecond { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public float LastGenerationChunksPerSecond { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public string StreamingTargetStatus { get; private set; } = "Manager object (no target assigned)";

	[Property, ReadOnly, Category( "World Status" )]
	public int LastRetainedChunkCount { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public int LastUnloadedChunkCount { get; private set; }

	[Property, ReadOnly, Category( "World Status" )]
	public int LastGeneratedChunkCount { get; private set; }

	[Property, ReadOnly, Category( "Debug Selection" )]
	public string SelectedChunkStatus { get; private set; } = "Selected chunk is not loaded";

	[Property, ReadOnly, Category( "Debug Selection" )]
	public string SelectedSampleStatus { get; private set; } = "Selected chunk is not loaded";

	protected override void OnStart()
	{
		ResolveStreamingTarget();
		ApplyConfigurationAndRebuild();
	}

	protected override void OnUpdate()
	{
		if ( !TryValidateConfiguration( out var configurationError ) )
		{
			if ( configurationError != _lastConfigurationError )
			{
				_loadedChunks.Clear();
				_desiredChunks.Clear();
				_pendingChunks.Clear();
				_hasStreamingCenter = false;
				_streamInProgress = false;
				_lastConfigurationError = configurationError;
				Log.Warning( $"[VoxelWorld] configuration.invalid reason=\"{configurationError}\"" );
				RefreshReadableStatus();
			}

			DrawDebugOverlay();
			return;
		}

		_lastConfigurationError = string.Empty;

		if ( DataConfigurationChanged() )
		{
			ApplyConfigurationAndRebuild();
		}
		else
		{
			_appliedChunksPerFrame = ChunkLoadsPerFrame;
			if ( HorizontalLoadRadius != _appliedHorizontalRadius ||
				MinimumLoadedChunkZ != _appliedMinimumChunkZ ||
				MaximumLoadedChunkZ != _appliedMaximumChunkZ )
			{
				_appliedHorizontalRadius = HorizontalLoadRadius;
				_appliedMinimumChunkZ = MinimumLoadedChunkZ;
				_appliedMaximumChunkZ = MaximumLoadedChunkZ;
				var targetPosition = ActiveStreamingTarget.WorldPosition;
				RebuildDesiredChunks( WorldToChunkCoordinate( targetPosition ), "streaming bounds changed" );
			}
			else
			{
				var targetPosition = ActiveStreamingTarget.WorldPosition;
				var targetCoordinate = WorldToChunkCoordinate( targetPosition );
				if ( !_hasStreamingCenter || targetCoordinate.x != _streamingCenterCoordinate.x ||
					targetCoordinate.y != _streamingCenterCoordinate.y )
				{
					RebuildDesiredChunks( targetCoordinate, "streaming target crossed a chunk boundary" );
				}
			}
		}

		if ( GeneratePendingChunks() )
		{
			RefreshReadableStatus();
		}
		DrawDebugOverlay();
	}

	protected override void OnValidate()
	{
		RefreshReadableStatus();
	}

	[Button( "Log World Summary" )]
	public void LogWorldSummary()
	{
		Log.Info(
			$"[VoxelWorld] summary center=C[{_streamingCenterCoordinate.x},{_streamingCenterCoordinate.y},{_streamingCenterCoordinate.z}] " +
			$"loaded={_loadedChunks.Count} pending={_pendingChunks.Count} samples={LoadedDensitySampleCount} " +
			$"densityBytes={CalculateLoadedDensityBytes()} cellSize={CellSize} cellsPerAxis={CellsPerAxis}" );
	}

	[Button( "Log Selected Chunk And Cell" )]
	public void LogSelectedChunkAndCell()
	{
		if ( !_loadedChunks.TryGetValue( SelectedChunkCoordinate, out var chunk ) )
		{
			Log.Warning(
				$"[VoxelWorld] chunk.missing chunk=C[{SelectedChunkCoordinate.x},{SelectedChunkCoordinate.y},{SelectedChunkCoordinate.z}] " +
				$"loaded={_loadedChunks.Count}" );
			return;
		}

		Log.Info(
			$"[VoxelWorld] chunk.inspect chunk={chunk.LogId} name=\"{chunk.HumanName}\" cellsPerAxis={chunk.CellsPerAxis} " +
			$"samplesPerAxis={chunk.SamplesPerAxis} sampleCount={chunk.SampleCount} densityBytes={chunk.DensityBytes} " +
			$"densityMin={chunk.MinimumDensity} densityMax={chunk.MaximumDensity}" );

		if ( chunk.TryGetDensity( SelectedLocalSample, out var density ) )
		{
			Log.Info(
				$"[VoxelWorld] cell.inspect chunk={chunk.LogId} cell=L[{SelectedLocalSample.x},{SelectedLocalSample.y},{SelectedLocalSample.z}] " +
				$"density={density} classification={(density < 0f ? "solid" : density > 0f ? "air" : "surface")}" );
		}
		else
		{
			Log.Warning(
				$"[VoxelWorld] cell.invalid chunk={chunk.LogId} cell=L[{SelectedLocalSample.x},{SelectedLocalSample.y},{SelectedLocalSample.z}] " +
				$"validRange=0..{chunk.SamplesPerAxis - 1}" );
		}
	}

	[ConCmd( "voxel_stream_origin" )]
	public static void SetDebugStreamingOrigin( float x, float y, float z )
	{
		VoxelManager manager = null;
		foreach ( var candidate in Game.ActiveScene.GetAllComponents<VoxelManager>() )
		{
			if ( manager is not null )
			{
				Log.Warning( "[VoxelWorld] debug.origin.rejected reason=\"multiple active VoxelManager components\"" );
				return;
			}

			manager = candidate;
		}

		if ( manager is null )
		{
			Log.Warning( "[VoxelWorld] debug.origin.rejected reason=\"no active VoxelManager component\"" );
			return;
		}

		var requestedPosition = new Vector3( x, y, z );
		manager.ActiveStreamingTarget.WorldPosition = requestedPosition;

		Log.Info(
			$"[VoxelWorld] debug.origin.applied position=[{x},{y},{z}] " +
			$"target=\"{manager.ActiveStreamingTarget.Name}\"" );
	}

	private void ResolveStreamingTarget()
	{
		if ( StreamingTarget is not null )
		{
			_resolvedStreamingTarget = StreamingTarget;
			Log.Info( $"[VoxelWorld] target.resolve mode=assigned name=\"{StreamingTarget.Name}\"" );
			return;
		}

		GameObject localPlayer = null;
		foreach ( var controller in Scene.GetAllComponents<PlayerController>() )
		{
			if ( controller.IsProxy )
			{
				continue;
			}

			if ( localPlayer is not null && localPlayer != controller.GameObject )
			{
				_resolvedStreamingTarget = GameObject;
				Log.Warning(
					"[VoxelWorld] target.resolve.rejected reason=\"multiple locally controlled PlayerController components\" " +
					$"fallback=\"{GameObject.Name}\"" );
				return;
			}

			localPlayer = controller.GameObject;
		}

		_resolvedStreamingTarget = localPlayer ?? GameObject;
		Log.Info(
			$"[VoxelWorld] target.resolve mode={(localPlayer is null ? "manager-fallback" : "local-player")} " +
			$"name=\"{_resolvedStreamingTarget.Name}\"" );
	}

	private bool TryValidateConfiguration( out string error )
	{
		if ( CellsPerAxis < 4 || CellsPerAxis > 64 )
		{
			error = "Cells Per Axis must be between 4 and 64.";
			return false;
		}

		if ( !float.IsFinite( CellSize ) || CellSize < 1f || CellSize > 128f )
		{
			error = "Cell Size must be finite and between 1 and 128 world units.";
			return false;
		}

		if ( HorizontalLoadRadius < 0 || HorizontalLoadRadius > 16 )
		{
			error = "Horizontal Load Radius must be between 0 and 16.";
			return false;
		}

		if ( MinimumLoadedChunkZ < -64 || MinimumLoadedChunkZ > 64 ||
			MaximumLoadedChunkZ < -64 || MaximumLoadedChunkZ > 64 ||
			MinimumLoadedChunkZ > MaximumLoadedChunkZ )
		{
			error = "Loaded Chunk Z bounds must be within -64 to 64 and minimum must not exceed maximum.";
			return false;
		}

		if ( ChunkLoadsPerFrame < 1 || ChunkLoadsPerFrame > 128 )
		{
			error = "Chunk Loads Per Frame must be between 1 and 128.";
			return false;
		}

		if ( !float.IsFinite( TerrainSurfaceHeight ) )
		{
			error = "Terrain Surface Height must be finite.";
			return false;
		}

		error = string.Empty;
		return true;
	}

	private bool DataConfigurationChanged()
	{
		return CellsPerAxis != _appliedCellsPerAxis ||
			CellSize != _appliedCellSize ||
			TerrainSurfaceHeight != _appliedTerrainSurfaceHeight;
	}

	private void ApplyConfigurationAndRebuild()
	{
		if ( !TryValidateConfiguration( out var configurationError ) )
		{
			_lastConfigurationError = configurationError;
			Log.Warning( $"[VoxelWorld] configuration.invalid reason=\"{configurationError}\"" );
			return;
		}

		_appliedCellsPerAxis = CellsPerAxis;
		_appliedCellSize = CellSize;
		_appliedHorizontalRadius = HorizontalLoadRadius;
		_appliedMinimumChunkZ = MinimumLoadedChunkZ;
		_appliedMaximumChunkZ = MaximumLoadedChunkZ;
		_appliedChunksPerFrame = ChunkLoadsPerFrame;
		_appliedTerrainSurfaceHeight = TerrainSurfaceHeight;

		_loadedChunks.Clear();
		_desiredChunks.Clear();
		_pendingChunks.Clear();
		_hasStreamingCenter = false;
		_streamInProgress = false;

		var targetPosition = ActiveStreamingTarget.WorldPosition;
		RebuildDesiredChunks( WorldToChunkCoordinate( targetPosition ), "configuration applied" );
	}

	private Vector3Int WorldToChunkCoordinate( Vector3 worldPosition )
	{
		var chunkWorldSize = CellsPerAxis * CellSize;
		return new Vector3Int(
			(int)MathF.Floor( worldPosition.x / chunkWorldSize ),
			(int)MathF.Floor( worldPosition.y / chunkWorldSize ),
			(int)MathF.Floor( worldPosition.z / chunkWorldSize ) );
	}

	private void RebuildDesiredChunks( Vector3Int center, string reason )
	{
		_streamingCenterCoordinate = center;
		_hasStreamingCenter = true;
		_desiredChunks.Clear();

		for ( var z = MinimumLoadedChunkZ; z <= MaximumLoadedChunkZ; z++ )
		{
			for ( var y = -HorizontalLoadRadius; y <= HorizontalLoadRadius; y++ )
			{
				for ( var x = -HorizontalLoadRadius; x <= HorizontalLoadRadius; x++ )
				{
					_desiredChunks.Add( new Vector3Int( center.x + x, center.y + y, z ) );
				}
			}
		}

		_coordinateBuffer.Clear();
		foreach ( var coordinate in _loadedChunks.Keys )
		{
			if ( !_desiredChunks.Contains( coordinate ) )
			{
				_coordinateBuffer.Add( coordinate );
			}
		}

		var unloadedCount = _coordinateBuffer.Count;
		foreach ( var coordinate in _coordinateBuffer )
		{
			if ( LogChunkLifecycle && _loadedChunks.TryGetValue( coordinate, out var chunk ) )
			{
				Log.Info( $"[VoxelWorld] chunk.unload chunk={chunk.LogId} name=\"{chunk.HumanName}\"" );
			}

			_loadedChunks.Remove( coordinate );
		}

		_coordinateBuffer.Clear();
		foreach ( var coordinate in _desiredChunks )
		{
			if ( !_loadedChunks.ContainsKey( coordinate ) )
			{
				_coordinateBuffer.Add( coordinate );
			}
		}

		_coordinateBuffer.Sort( ( left, right ) =>
		{
			var leftDistance = Math.Abs( left.x - center.x ) + Math.Abs( left.y - center.y ) + Math.Abs( left.z - center.z );
			var rightDistance = Math.Abs( right.x - center.x ) + Math.Abs( right.y - center.y ) + Math.Abs( right.z - center.z );
			var distanceComparison = leftDistance.CompareTo( rightDistance );
			if ( distanceComparison != 0 )
			{
				return distanceComparison;
			}

			var zComparison = left.z.CompareTo( right.z );
			if ( zComparison != 0 )
			{
				return zComparison;
			}

			var yComparison = left.y.CompareTo( right.y );
			return yComparison != 0 ? yComparison : left.x.CompareTo( right.x );
		} );

		_pendingChunks.Clear();
		foreach ( var coordinate in _coordinateBuffer )
		{
			_pendingChunks.Enqueue( coordinate );
		}

		_generatedThisStream = 0;
		_retainedThisStream = _loadedChunks.Count;
		_unloadedThisStream = unloadedCount;
		_generationMillisecondsThisStream = 0f;
		SlowestChunkGenerationMilliseconds = 0f;
		_streamStartedTimestamp = Stopwatch.GetTimestamp();
		_streamInProgress = true;

		Log.Info(
			$"[VoxelWorld] stream.begin center=C[{center.x},{center.y},{center.z}] reason=\"{reason}\" " +
			$"verticalRange=[{MinimumLoadedChunkZ},{MaximumLoadedChunkZ}] retained={_loadedChunks.Count} " +
			$"unloaded={unloadedCount} queued={_pendingChunks.Count} desired={_desiredChunks.Count}" );
		RefreshReadableStatus();

		if ( _pendingChunks.Count == 0 )
		{
			CompleteStream();
		}
	}

	private bool GeneratePendingChunks()
	{
		var generatedThisFrame = 0;
		while ( generatedThisFrame < ChunkLoadsPerFrame && _pendingChunks.TryDequeue( out var coordinate ) )
		{
			if ( !_desiredChunks.Contains( coordinate ) || _loadedChunks.ContainsKey( coordinate ) )
			{
				continue;
			}

			var generationStart = Stopwatch.GetTimestamp();
			var chunk = new VoxelChunk( coordinate, CellsPerAxis, CellSize, TerrainSurfaceHeight );
			LastChunkGenerationMilliseconds = (float)Stopwatch.GetElapsedTime( generationStart ).TotalMilliseconds;
			_generationMillisecondsThisStream += LastChunkGenerationMilliseconds;
			SlowestChunkGenerationMilliseconds = Math.Max(
				SlowestChunkGenerationMilliseconds,
				LastChunkGenerationMilliseconds );

			_loadedChunks.Add( coordinate, chunk );
			generatedThisFrame++;
			_generatedThisStream++;

			if ( LogChunkLifecycle )
			{
				Log.Info(
					$"[VoxelWorld] chunk.load chunk={chunk.LogId} name=\"{chunk.HumanName}\" samples={chunk.SampleCount} " +
					$"densityBytes={chunk.DensityBytes} densityMin={chunk.MinimumDensity} densityMax={chunk.MaximumDensity} " +
					$"generationMs={LastChunkGenerationMilliseconds:0.###}" );
			}
		}

		if ( _streamInProgress && _pendingChunks.Count == 0 )
		{
			CompleteStream();
		}

		return generatedThisFrame > 0;
	}

	private void CompleteStream()
	{
		_streamInProgress = false;
		LastStreamSettleMilliseconds = (float)Stopwatch.GetElapsedTime( _streamStartedTimestamp ).TotalMilliseconds;
		LastRetainedChunkCount = _retainedThisStream;
		LastUnloadedChunkCount = _unloadedThisStream;
		LastGeneratedChunkCount = _generatedThisStream;
		LastStreamGenerationMilliseconds = _generationMillisecondsThisStream;
		LastEffectiveChunksPerSecond = LastStreamSettleMilliseconds > 0f
			? _generatedThisStream * 1000f / LastStreamSettleMilliseconds
			: 0f;
		LastGenerationChunksPerSecond = LastStreamGenerationMilliseconds > 0f
			? _generatedThisStream * 1000f / LastStreamGenerationMilliseconds
			: 0f;
		var densityBytes = CalculateLoadedDensityBytes();
		LastStreamSummary =
			$"Loaded {_loadedChunks.Count}; retained {_retainedThisStream}; unloaded {_unloadedThisStream}; " +
			$"generated {_generatedThisStream}; {LastEffectiveChunksPerSecond:0.0} chunks/sec effective; " +
			$"{LastGenerationChunksPerSecond:0.0} chunks/sec generation";
		var probeChunkId = "missing";
		var surfaceProbeDensity = float.NaN;
		var oneCellUpProbeDensity = float.NaN;
		if ( _loadedChunks.TryGetValue( _streamingCenterCoordinate, out var probeChunk ) )
		{
			probeChunkId = probeChunk.LogId;
			probeChunk.TryGetDensity( Vector3Int.Zero, out surfaceProbeDensity );
			probeChunk.TryGetDensity( Vector3Int.OneZ, out oneCellUpProbeDensity );
		}

		Log.Info(
			$"[VoxelWorld] stream.complete center=C[{_streamingCenterCoordinate.x},{_streamingCenterCoordinate.y},{_streamingCenterCoordinate.z}] " +
			$"loaded={_loadedChunks.Count} pending={_pendingChunks.Count} retained={_retainedThisStream} " +
			$"unloaded={_unloadedThisStream} generated={_generatedThisStream} " +
			$"samples={CalculateLoadedSampleCount()} densityBytes={densityBytes} " +
			$"settleMs={LastStreamSettleMilliseconds:0.###} generationMs={LastStreamGenerationMilliseconds:0.###} " +
			$"effectiveChunksPerSecond={LastEffectiveChunksPerSecond:0.###} " +
			$"generationChunksPerSecond={LastGenerationChunksPerSecond:0.###} " +
			$"slowestChunkMs={SlowestChunkGenerationMilliseconds:0.###} " +
			$"probeChunk={probeChunkId} probeCell0=L[0,0,0] probeDensity0={surfaceProbeDensity} " +
			$"probeCellUp=L[0,0,1] probeDensityUp={oneCellUpProbeDensity}" );
	}

	private long CalculateLoadedSampleCount()
	{
		long sampleCount = 0;
		foreach ( var chunk in _loadedChunks.Values )
		{
			sampleCount += chunk.SampleCount;
		}

		return sampleCount;
	}

	private long CalculateLoadedDensityBytes()
	{
		long densityBytes = 0;
		foreach ( var chunk in _loadedChunks.Values )
		{
			densityBytes += chunk.DensityBytes;
		}

		return densityBytes;
	}

	private void RefreshReadableStatus()
	{
		LoadedChunkCount = _loadedChunks.Count;
		PendingChunkCount = _pendingChunks.Count;
		LoadedDensitySampleCount = CalculateLoadedSampleCount();
		var densityBytes = CalculateLoadedDensityBytes();
		EstimatedDensityMemory = $"{densityBytes:N0} bytes ({densityBytes / (1024f * 1024f):0.00} MiB)";
		StreamingCenter = _hasStreamingCenter
			? $"Player chunk X {_streamingCenterCoordinate.x}, Y {_streamingCenterCoordinate.y}; " +
				$"loaded world Z {MinimumLoadedChunkZ} through {MaximumLoadedChunkZ}"
			: "Not initialized";
		var targetObject = ActiveStreamingTarget;
		StreamingTargetStatus =
			$"{targetObject.Name} at X {targetObject.WorldPosition.x:0.##}, " +
			$"Y {targetObject.WorldPosition.y:0.##}, Z {targetObject.WorldPosition.z:0.##}";

		if ( _loadedChunks.TryGetValue( SelectedChunkCoordinate, out var selectedChunk ) )
		{
			SelectedChunkStatus =
				$"{selectedChunk.HumanName}; {selectedChunk.CellsPerAxis} cells/axis; {selectedChunk.SampleCount:N0} samples; " +
				$"density {selectedChunk.MinimumDensity:0.###} to {selectedChunk.MaximumDensity:0.###}";

			if ( selectedChunk.TryGetDensity( SelectedLocalSample, out var density ) )
			{
				var classification = density < 0f ? "solid" : density > 0f ? "air" : "surface";
				SelectedSampleStatus =
					$"Local sample X {SelectedLocalSample.x}, Y {SelectedLocalSample.y}, Z {SelectedLocalSample.z}: " +
					$"density {density:0.###} ({classification})";
			}
			else
			{
				SelectedSampleStatus = $"Local sample is outside 0 to {selectedChunk.SamplesPerAxis - 1}.";
			}
		}
		else
		{
			SelectedChunkStatus = "Selected chunk is not loaded";
			SelectedSampleStatus = "Selected chunk is not loaded";
		}
	}

	private void DrawDebugOverlay()
	{
		if ( !ShowLoadedChunkBounds && !ShowLoadedChunkLabels && !ShowSelectedCellSlice )
		{
			return;
		}

		var chunkWorldSize = CellsPerAxis * CellSize;
		foreach ( var chunk in _loadedChunks.Values )
		{
			var minimum = new Vector3(
				chunk.Coordinate.x * chunkWorldSize,
				chunk.Coordinate.y * chunkWorldSize,
				chunk.Coordinate.z * chunkWorldSize );
			var maximum = minimum + new Vector3( chunkWorldSize );
			var isSelected = chunk.Coordinate.x == SelectedChunkCoordinate.x &&
				chunk.Coordinate.y == SelectedChunkCoordinate.y &&
				chunk.Coordinate.z == SelectedChunkCoordinate.z;
			var color = isSelected ? Color.Yellow : Color.Cyan;

			if ( ShowLoadedChunkBounds )
			{
				DebugOverlay.Box( new BBox( minimum, maximum ), color, 0f, global::Transform.Zero, true );
			}

			if ( ShowLoadedChunkLabels )
			{
				DebugOverlay.Text( (minimum + maximum) * 0.5f, chunk.HumanName, 18f, TextFlag.Center, color, 0f, true );
			}
		}

		if ( !ShowSelectedCellSlice || !_loadedChunks.TryGetValue( SelectedChunkCoordinate, out var selectedChunk ) )
		{
			return;
		}

		if ( SelectedCellSliceZ < 0 || SelectedCellSliceZ >= selectedChunk.CellsPerAxis )
		{
			return;
		}

		var selectedMinimum = new Vector3(
			selectedChunk.Coordinate.x * chunkWorldSize,
			selectedChunk.Coordinate.y * chunkWorldSize,
			selectedChunk.Coordinate.z * chunkWorldSize );
		var debugCellSize = new Vector3( CellSize * 0.9f );
		for ( var y = 0; y < selectedChunk.CellsPerAxis; y++ )
		{
			for ( var x = 0; x < selectedChunk.CellsPerAxis; x++ )
			{
				var localSample = new Vector3Int( x, y, SelectedCellSliceZ );
				selectedChunk.TryGetDensity( localSample, out var density );
				var cellCenter = selectedMinimum + new Vector3(
					(x + 0.5f) * CellSize,
					(y + 0.5f) * CellSize,
					(SelectedCellSliceZ + 0.5f) * CellSize );
				var cellColor = density < 0f ? Color.Red : density > 0f ? Color.Green : Color.Yellow;
				DebugOverlay.Box( cellCenter, debugCellSize, cellColor, 0f, global::Transform.Zero, true );
			}
		}
	}
}
