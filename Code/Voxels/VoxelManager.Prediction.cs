using System;
using System.Collections.Generic;
using System.Diagnostics;

public sealed partial class VoxelManager
{
	private readonly HashSet<GpuMeshRegionKey> _predictionRegions = new();
	private readonly HashSet<GpuTransitionKey> _predictionSeams = new();
	private readonly HashSet<GpuMeshRegionKey> _nextPredictionRegions = new();
	private readonly HashSet<GpuTransitionKey> _nextPredictionSeams = new();
	private readonly List<GpuMeshRegionKey> _predictionRegionOrder = new();
	private readonly List<GpuTransitionKey> _predictionSeamOrder = new();
	private Vector3 _predictionPreviousPosition;
	private Vector3 _predictionVelocity;
	private bool _predictionHasPosition;
	private float _predictionRefreshElapsed;
	private int _predictionRegionCursor;
	private int _predictionSeamCursor;
	private bool _predictionServiceSeam;
	private readonly HashSet<GpuMeshRegionKey> _predictionReadyRegions = new();
	private readonly HashSet<GpuTransitionKey> _predictionReadySeams = new();
	private (int Epoch, int Revision, int Content) _predictionReadyField;
	private long _predictionSeamReadinessChecks;
	private long _predictionSeamReadinessMisses;
	private long _predictionOutsidePackageMisses;
	private GpuTransitionKey? _predictionLastOutsideSeam;
	private long _predictionPeakSeamBytes;
	private long _predictionScheduled;
	private long _predictionSeamsScheduled;
	private long _predictionEntered;
	private long _predictionReadyOnEntry;
	private double _predictionMaximumMilliseconds;

	private object CapturePredictionPackages()
	{
		var owners = 0;
		var complete = 0;
		var facesReady = 0;
		foreach ( var key in _predictionRegions )
		{
			if ( key.Level == 0 ) continue;
			owners++;
			var ready = _gpuMesher.IsResident( CreateRegularDescriptor( key.Level, key.Coordinate, captureRegion: false ) );
			for ( var face = 0; face < 6; face++ )
			{
				var seam = new GpuTransitionKey( key.Level - 1, key.Level, key.Coordinate, (GpuTransitionFace)face );
				if ( _gpuMesher.IsTransitionResident( CreateTransitionDescriptor( seam, captureRegion: false ) ) ) facesReady++;
				else ready = false;
			}
			if ( ready ) complete++;
		}
		return new { Owners = owners, Complete = complete, FacesReady = facesReady,
			GeometryBytes = _gpuMesher.TransitionGeometryBytes( _predictionSeams ),
			PeakGeometryBytes = _predictionPeakSeamBytes,
			LocalReadinessChecks = _predictionSeamReadinessChecks, LocalReadinessMisses = _predictionSeamReadinessMisses,
			OutsidePackageMisses = _predictionOutsidePackageMisses, LastOutsideSeam = _predictionLastOutsideSeam };
	}

	private void UpdateTerrainPrediction()
	{
		var started = Stopwatch.GetTimestamp();
		var field = CurrentField;
		var identity = (field.Epoch, field.Revision, _terrainContentRevision);
		if ( _predictionReadyField != identity )
		{
			_predictionReadyRegions.Clear();
			_predictionReadySeams.Clear();
			_predictionReadyField = identity;
		}
		var position = ActiveStreamingTarget.WorldPosition;
		var delta = Time.Delta;
		var movement = position - _predictionPreviousPosition;
		var validMotion = _predictionHasPosition && delta > 0f && delta < 0.25f &&
			movement.Length < _appliedCellsPerAxis * _appliedCellSize * 8f;
		_predictionPreviousPosition = position;
		_predictionHasPosition = true;
		var velocity = validMotion ? movement / delta : Vector3.Zero;
		_predictionVelocity = validMotion
			? Vector3.Lerp( _predictionVelocity, velocity, Math.Clamp( delta / 0.15f, 0f, 1f ) )
			: Vector3.Zero;
		_predictionRefreshElapsed += delta;
		if ( _predictionRefreshElapsed >= 0.2f || !validMotion )
		{
			_predictionRefreshElapsed = 0f;
			_nextPredictionRegions.Clear();
			_nextPredictionSeams.Clear();
			var forecastPosition = position;
			if ( validMotion && _predictionVelocity.Length > 64f && _levels[0].HasPlacement &&
				_targetVisualConfiguration.MinimumVisualLod == 0 && _targetVisualConfiguration.MaximumVisualLod >= 1 )
			{
				var lead = _predictionVelocity * 0.75f;
				var maximumLead = _appliedCellsPerAxis * _appliedCellSize * 8f;
				if ( lead.Length > maximumLead ) lead = lead.Normal * maximumLead;
				forecastPosition = position + lead;
				for ( var step = 1; step <= 3; step++ )
				{
					var forecast = position + lead * (step / 3f);
					var center = WorldToLevelAnchor( forecast, 0 );
					for ( var z = -1; z <= 1; z++ )
					for ( var y = -1; y <= 1; y++ )
					for ( var x = -1; x <= 1; x++ )
						_nextPredictionRegions.Add( new GpuMeshRegionKey( 0, center + new Vector3Int( x, y, z ) ) );
				}
			}
			if ( _levels[0].HasPlacement )
			{
				for ( var level = _targetVisualConfiguration.MinimumVisualLod + 1;
					level <= _targetVisualConfiguration.MaximumVisualLod; level++ )
				for ( var sample = 0; sample < 2; sample++ )
				{
					var nearCenter = WorldToChunkCoordinate( sample == 0 ? position : forecastPosition );
					// Include ancestors of every immediate LOD0 target and the coarse
					// neighbors that can own its faces. Rounded anchors miss negative parents.
					var minimum = CoverageAncestor( new GpuMeshRegionKey( 0, nearCenter - new Vector3Int( 1 ) ), level ).Coordinate - new Vector3Int( 1 );
					var maximum = CoverageAncestor( new GpuMeshRegionKey( 0, nearCenter + new Vector3Int( 1 ) ), level ).Coordinate + new Vector3Int( 1 );
					for ( var z = minimum.z; z <= maximum.z; z++ )
					for ( var y = minimum.y; y <= maximum.y; y++ )
					for ( var x = minimum.x; x <= maximum.x; x++ )
					{
						var owner = new Vector3Int( x, y, z );
						if ( !_nextPredictionRegions.Add( new GpuMeshRegionKey( level, owner ) ) ) continue;
						for ( var face = 0; face < 6; face++ )
							_nextPredictionSeams.Add( new GpuTransitionKey( level - 1, level, owner, (GpuTransitionFace)face ) );
					}
				}
			}
			if ( !_predictionRegions.SetEquals( _nextPredictionRegions ) || !_predictionSeams.SetEquals( _nextPredictionSeams ) )
			{
				foreach ( var key in _predictionRegions )
					if ( !_nextPredictionRegions.Contains( key ) ) _supersededPlacementRegions.Add( key );
				foreach ( var key in _predictionSeams )
					if ( !_nextPredictionSeams.Contains( key ) ) _supersededPlacementTransitions.Add( key );
				_predictionRegions.Clear();
				_predictionRegions.UnionWith( _nextPredictionRegions );
				_predictionSeams.Clear();
				_predictionSeams.UnionWith( _nextPredictionSeams );
				_predictionReadyRegions.IntersectWith( _predictionRegions );
				_predictionReadySeams.IntersectWith( _predictionSeams );
				_predictionRegionOrder.Clear();
				_predictionRegionOrder.AddRange( _predictionRegions );
				_predictionRegionOrder.Sort( (a, b) =>
				{
					var order = a.Level.CompareTo( b.Level );
					if ( order != 0 ) return order;
					order = a.Coordinate.z.CompareTo( b.Coordinate.z );
					if ( order != 0 ) return order;
					order = a.Coordinate.y.CompareTo( b.Coordinate.y );
					return order != 0 ? order : a.Coordinate.x.CompareTo( b.Coordinate.x );
				} );
				_predictionSeamOrder.Clear();
				_predictionSeamOrder.AddRange( _predictionSeams );
				SortTransitionsNearestFirst( _predictionSeamOrder );
				// Preserve progress across forecast changes; each cursor wraps its new list.
				_waterRequestSerial++;
				RetireSupersededPlacementWork();
			}
			_predictionPeakSeamBytes = Math.Max( _predictionPeakSeamBytes,
				_gpuMesher.TransitionGeometryBytes( _predictionSeams ) );
		}
		// Completed entries remain retained; only their scheduling checks sleep.
		// Publication/removal and field changes invalidate the corresponding proof.
		var total = _predictionReadyRegions.Count == _predictionRegionOrder.Count &&
			_predictionReadySeams.Count == _predictionSeamOrder.Count
			? 0 : _predictionRegionOrder.Count + _predictionSeamOrder.Count;
		for ( var inspected = 0; inspected < Math.Min( 48, total ) &&
			Stopwatch.GetElapsedTime( started ).TotalMilliseconds < 0.5; inspected++ )
		{
			_predictionServiceSeam = !_predictionServiceSeam;
			if ( _predictionRegionOrder.Count > 0 && (!_predictionServiceSeam || _predictionSeamOrder.Count == 0) )
			{
				var key = _predictionRegionOrder[_predictionRegionCursor++ % _predictionRegionOrder.Count];
				if ( _predictionRegionCursor >= _predictionRegionOrder.Count ) _predictionRegionCursor = 0;
				if ( _predictionReadyRegions.Contains( key ) ) continue;
				var descriptor = CreateRegularDescriptor( key.Level, key.Coordinate, captureRegion: false );
				if ( _gpuMesher.IsResident( descriptor ) )
				{
					_predictionReadyRegions.Add( key );
					continue;
				}
				if ( _gpuMesher.Contains( descriptor ) ) continue;
				var classification = key.Level == 0
					? VoxelChunk.ClassifyDensityRange( key.Coordinate, _appliedCellsPerAxis, _appliedCellSize, CurrentField ).Classification
					: ClassifyClipboxRegion( key.Level, key.Coordinate );
				var residency = key.Level == 0 ? GpuMeshResidency.Warm : GpuMeshResidency.Visual;
				if ( classification != ChunkDensityClassification.PotentiallySurfaceContaining )
					_gpuMesher.PublishKnownEmpty( descriptor, residency );
				else _gpuMesher.Schedule( CreateRegularDescriptor( key.Level, key.Coordinate ), _playerFigureEightRouteDistance, residency );
				if ( key.Level == 0 && _renderPreparedChunks.Add( key.Coordinate ) )
				{
					_renderPreparedRevision++;
					MarkWaterPresentationDirty( new GpuMeshRegionKey( 0, key.Coordinate ) );
				}
				_predictionScheduled++;
			}
			else
			{
				var key = _predictionSeamOrder[_predictionSeamCursor++ % _predictionSeamOrder.Count];
				if ( _predictionSeamCursor >= _predictionSeamOrder.Count ) _predictionSeamCursor = 0;
				if ( _predictionReadySeams.Contains( key ) ) continue;
				var descriptor = CreateTransitionDescriptor( key, captureRegion: false );
				if ( _gpuMesher.IsTransitionResident( descriptor ) )
				{
					_predictionReadySeams.Add( key );
					continue;
				}
				if ( _gpuMesher.ContainsTransition( descriptor ) ) continue;
				_gpuMesher.ScheduleTransition( CreateTransitionDescriptor( key ), _playerFigureEightRouteDistance );
				_predictionSeamsScheduled++;
			}
		}
		_predictionMaximumMilliseconds = Math.Max( _predictionMaximumMilliseconds,
			Stopwatch.GetElapsedTime( started ).TotalMilliseconds );
	}
}
