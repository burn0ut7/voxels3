using System;
using System.Collections.Generic;
using System.Diagnostics;

public sealed partial class VoxelManager
{
	private const int MaximumLocalReplacements = 8;
	private const double LocalCoverageBudgetMilliseconds = 0.75;
	private readonly HashSet<GpuMeshRegionKey> _coverageDesired = new();
	private readonly HashSet<GpuMeshRegionKey> _coverageSplitTargets = new();
	private readonly HashSet<GpuMeshRegionKey> _coverageRoots = new();
	private readonly HashSet<GpuMeshRegionKey> _coverageQueued = new();
	private readonly PriorityQueue<GpuMeshRegionKey, (int Service, float Distance, int Level, int Z, int Y, int X)> _coverageQueue = new();
	private readonly List<LocalReplacement> _coveragePending = new();
	private readonly Queue<GpuMeshRegionKey> _coverageRetireQueue = new();
	private readonly Queue<GpuTransitionKey> _coverageRetireSeams = new();
	private HashSet<GpuMeshRegionKey> _coverageRequired = new();
	private HashSet<GpuMeshRegionKey> _coverageNextRequired = new();
	private long _coverageLocalCommits;
	private bool _coverageAdmissionDeferred;
	private double _coverageMaximumPublicationMilliseconds;
	private long _coverageTopologyRetries;
	private long _coverageResumedRequests;
	private long _coverageExteriorPromotions;
	private long _coveragePlanTimestamp;
	private long _coverageUpdates;
	private double _coverageUpdateMilliseconds;
	private double _coverageMaximumUpdateMilliseconds;
	private bool _coverageConverged;
	private bool _coverageLocalWorkComplete;
	private bool _coverageLocalPlan;
	private bool _coverageDiffersFromLayout;
	private readonly List<GpuMeshRegionKey> _coverageRegularBuffer = new();
	private readonly List<GpuTransitionKey> _coverageSeamBuffer = new();

	private sealed class LocalReplacement
	{
		public GpuMeshRegionKey Parent;
		public readonly HashSet<GpuMeshRegionKey> Requests = new();
		public bool Split;
		public readonly List<GpuMeshRegionKey> Incoming = new( 8 );
		public readonly List<GpuMeshRegionKey> Dependencies = new( 14 );
		public readonly List<GpuTransitionKey> NewSeams = new( 6 );
		public readonly List<GpuTransitionKey> OldSeams = new( 6 );
		public long Started = Stopwatch.GetTimestamp();
		public readonly HashSet<GpuMeshRegionKey> Leaves = new();
		public readonly HashSet<GpuMeshRegionKey> Outgoing = new();
		public readonly HashSet<GpuMeshRegionKey> SplitNodes = new();
		public readonly HashSet<GpuTransitionKey> SeamKeys = new();
		public readonly Queue<GpuMeshRegionKey> Targets = new();
		public readonly Queue<GpuMeshRegionKey> Balance = new();
		public GpuMeshRegionKey Target;
		public int PreviousTargetLod;
		public GpuMeshRegionKey Focus;
		public int PreviousFocusLod;
		public int PlanningStage;
		public int SeamCursor;
		public bool PlanningFailed;
		public int PreparationCursor;
		public bool PreparationReady;
		public TerrainFieldSnapshot PreparationField;
	}

	// Arithmetic shifts implement floor division for negative region coordinates.
	private static GpuMeshRegionKey CoverageAncestor( GpuMeshRegionKey key, int level )
	{
		var shift = level - key.Level;
		return new( level, new Vector3Int( key.Coordinate.x >> shift,
			key.Coordinate.y >> shift, key.Coordinate.z >> shift ) );
	}

	private static GpuMeshRegionKey CoverageChild( GpuMeshRegionKey parent, int index ) =>
		new( parent.Level - 1, parent.Coordinate * 2 + new Vector3Int( index & 1, (index >> 1) & 1, (index >> 2) & 1 ) );

	private bool FindCoverageAncestor( GpuMeshRegionKey key, out GpuMeshRegionKey leaf )
	{
		for ( var level = key.Level; level < SupportedVisualLevelCount; level++ )
		{
			leaf = CoverageAncestor( key, level );
			if ( _gpuMesher.IsRenderActive( leaf ) ) return true;
		}
		leaf = default;
		return false;
	}

	private bool IsExteriorCoverageRequired( GpuMeshRegionKey key ) =>
		key.Level == _partialOuterLevel && _partialOuterChunks.Contains( key.Coordinate );

	private bool IsLocalCoverageRequired( GpuMeshRegionKey key ) =>
		_coverageDesired.Contains( key ) || IsExteriorCoverageRequired( key );

	private bool IsImmediateCoverageRequired( GpuMeshRegionKey key )
	{
		if ( key.Level != 0 ) return false;
		var center = WorldToChunkCoordinate( ActiveStreamingTarget.WorldPosition );
		return Math.Abs( key.Coordinate.x - center.x ) <= 1 &&
			Math.Abs( key.Coordinate.y - center.y ) <= 1 && Math.Abs( key.Coordinate.z - center.z ) <= 1;
	}

	private void QueueCoverageCandidate( GpuMeshRegionKey key, bool promoteExterior = false )
	{
		var added = _coverageQueued.Add( key );
		if ( !added && !promoteExterior ) return;
		if ( !added ) _coverageExteriorPromotions++;
		_coverageLocalWorkComplete = false;
		var scale = (float)(1 << key.Level);
		var position = ActiveStreamingTarget.WorldPosition / (RequiredCellsPerAxis * RequiredBaseCellSize);
		var minimum = new Vector3( key.Coordinate.x, key.Coordinate.y, key.Coordinate.z ) * scale;
		var maximum = minimum + new Vector3( scale );
		var dx = MathF.Max( 0, MathF.Max( minimum.x - position.x, position.x - maximum.x ) );
		var dy = MathF.Max( 0, MathF.Max( minimum.y - position.y, position.y - maximum.y ) );
		var dz = MathF.Max( 0, MathF.Max( minimum.z - position.z, position.z - maximum.z ) );
		// Immediate player detail first, then missing exterior, then layout convergence.
		var service = IsImmediateCoverageRequired( key ) ? 0 :
			IsExteriorCoverageRequired( key ) && !HasCoverageDescendant( key ) ? 1 : 2;
		_coverageQueue.Enqueue( key, (service, dx * dx + dy * dy + dz * dz, -key.Level,
			key.Coordinate.z, key.Coordinate.y, key.Coordinate.x) );
	}

	private void BeginLocalCoveragePlan()
	{
		_coveragePending.Clear();
		_coverageRetireQueue.Clear();
		_coverageRetireSeams.Clear();
		_coverageRequired.Clear();
		_coverageQueue.Clear();
		_coverageQueued.Clear();
		_coverageDesired.Clear();
		_coverageSplitTargets.Clear();
		_coverageRoots.Clear();
		_coverageConverged = false;
		_coverageLocalWorkComplete = false;
		_coveragePlanTimestamp = Stopwatch.GetTimestamp();
		var maximum = _stagedVisualConfiguration.MaximumVisualLod;
		foreach ( var state in _levels )
		{
			var desired = state.PlacementChanged ? state.NextActive : state.Active;
			foreach ( var coordinate in desired )
			{
				var key = new GpuMeshRegionKey( state.Level, coordinate );
				_coverageDesired.Add( key );
				_coverageRoots.Add( CoverageAncestor( key, maximum ) );
				for ( var level = key.Level + 1; level <= maximum; level++ )
					_coverageSplitTargets.Add( CoverageAncestor( key, level ) );
				QueueCoverageCandidate( key );
			}
		}
		foreach ( var key in _gpuMesher.ActiveRegionKeys )
		{
			if ( !IsExteriorCoverageRequired( key ) &&
				(key.Level > maximum || !_coverageRoots.Contains( CoverageAncestor( key, maximum ) )) ) _coverageRetireQueue.Enqueue( key );
		}
		foreach ( var coordinate in _partialOuterChunks )
		{
			var key = new GpuMeshRegionKey( _partialOuterLevel, coordinate );
			if ( !HasCoverageDescendant( key ) ) QueueCoverageCandidate( key );
		}
		foreach ( var key in _gpuMesher.CachedTransitionKeys )
		{
			var pair = _transitionPairs[key.CoarseLevel - 1];
			if ( !_gpuMesher.IsTransitionActive( key ) &&
				!(pair.PlacementChanged ? pair.NextDesired : pair.Desired).Contains( key ) ) _coverageRetireSeams.Enqueue( key );
		}
		foreach ( var key in _gpuMesher.ActiveTransitionKeys )
			if ( !IsExteriorCoverageRequired( new GpuMeshRegionKey( key.CoarseLevel, key.CoarseCoordinate ) ) &&
				(key.CoarseLevel > maximum || !_coverageRoots.Contains( CoverageAncestor( new GpuMeshRegionKey( key.CoarseLevel, key.CoarseCoordinate ), maximum ) )) )
				_coverageRetireSeams.Enqueue( key );
		_waterRequestSerial++;
	}

	private void RefreshLocalCoverageRequirements()
	{
		_coverageNextRequired.Clear();
		foreach ( var operation in _coveragePending )
			foreach ( var key in operation.Dependencies ) _coverageNextRequired.Add( key );
		if ( _coverageRequired.SetEquals( _coverageNextRequired ) ) return;
		(_coverageRequired, _coverageNextRequired) = (_coverageNextRequired, _coverageRequired);
		foreach ( var key in _coverageRequired )
			if ( key.Level == 0 && !_coverageNextRequired.Contains( key ) && !_renderPreparedChunks.Contains( key.Coordinate ) )
				_clipboxWarmInterestDirty = true;
		_waterRequestSerial++;
	}

	private void AdvanceLocalCoverage()
	{
		if ( _coverageLocalWorkComplete || !_clipboxPlacementPending ) return;
		var started = Stopwatch.GetTimestamp();
		try
		{
			// A refinement patch owns topology exclusively until atomic publication.
			if ( _coveragePending.Count == 1 && _coveragePending[0].Split )
			{
				var patch = _coveragePending[0];
				if ( AdvanceRefinementPatch( patch, started ) && PrepareLocalReplacement( patch ) )
				{
					CommitLocalReplacement( patch );
					_coveragePending.Clear();
					RefreshLocalCoverageRequirements();
				}
				return;
			}
			// Retiring out-of-range coverage cannot wait behind refinement admission.
			// Otherwise continuous travel retains every old visible subtree.
			for ( var count = 0; count < 16 && _coverageRetireQueue.TryDequeue( out var retiring ); count++ )
			{
				if ( !IsExteriorCoverageRequired( retiring ) && !_coverageDesired.Contains( retiring ) )
					RetireLocalCoverageRegion( retiring );
				if ( Stopwatch.GetElapsedTime( started ).TotalMilliseconds >= LocalCoverageBudgetMilliseconds ) return;
			}
			for ( var count = 0; count < 16 && _coverageRetireSeams.TryDequeue( out var seam ); count++ )
				// A local handoff may have activated a formerly unused queued seam.
				if ( !_predictionSeams.Contains( seam ) && !_gpuMesher.IsTransitionActive( seam ) &&
					!IsExteriorCoverageRequired( new GpuMeshRegionKey( seam.CoarseLevel, seam.CoarseCoordinate ) ) )
					_gpuMesher.RemoveTransition( seam );
			// The bounded retirement slice makes progress alongside admission.
			// Distant cleanup must not become a global barrier to ready player detail.
			for ( var index = _coveragePending.Count - 1; index >= 0; index-- )
			{
				var operation = _coveragePending[index];
				if ( !DescribeLocalReplacement( operation ) )
				{
					_coverageTopologyRetries++;
					_coveragePending.RemoveAt( index );
					foreach ( var request in operation.Requests )
					{
						QueueCoverageCandidate( request );
						_coverageResumedRequests++;
					}
					RefreshLocalCoverageRequirements();
					continue;
				}
				RefreshLocalCoverageRequirements();
				if ( PrepareLocalReplacement( operation ) )
				{
					CommitLocalReplacement( operation );
					_coveragePending.RemoveAt( index );
					RefreshLocalCoverageRequirements();
				}
				if ( Stopwatch.GetElapsedTime( started ).TotalMilliseconds >= LocalCoverageBudgetMilliseconds ) return;
			}

			var inspected = 0;
			while ( _coveragePending.Count < MaximumLocalReplacements && _coverageQueue.TryPeek( out var key, out var priority ) )
			{
				// Player detail and exterior coverage must progress during distant cleanup.
				// Every addition still rechecks actual overlap, balance and seams.
				if ( (_coverageRetireQueue.Count != 0 || _coverageRetireSeams.Count != 0) && priority.Service > 1 ) break;
				_coverageQueue.Dequeue();
				// A promoted candidate leaves its older priority entry behind.
				// Skip consumed entries, but charge inspection against the same budget.
				if ( _coverageQueued.Remove( key ) )
				{
					_coverageAdmissionDeferred = false;
					ConsiderLocalReplacement( key );
					if ( _coverageAdmissionDeferred || _coveragePending.Any( operation => operation.Split ) ) return;
				}
				if ( ++inspected >= 64 || Stopwatch.GetElapsedTime( started ).TotalMilliseconds >= LocalCoverageBudgetMilliseconds ) return;
			}
			if ( _coverageQueue.Count != 0 || _coveragePending.Count != 0 || _coverageRetireQueue.Count != 0 || _coverageRetireSeams.Count != 0 || _clipboxPreparation is not null ) return;
			// Coarsening is completed by the final-layout publication. Only revisit
			// missing coverage that can benefit from an independent local handoff.
			foreach ( var key in _coverageDesired )
				if ( !HasCoverageDescendant( key ) &&
					(IsImmediateCoverageRequired( key ) || !FindCoverageAncestor( key, out _ )) ) QueueCoverageCandidate( key );
			foreach ( var coordinate in _partialOuterChunks )
			{
				var key = new GpuMeshRegionKey( _partialOuterLevel, coordinate );
				if ( !HasCoverageDescendant( key ) ) QueueCoverageCandidate( key );
			}
			_coverageLocalWorkComplete = _coverageQueue.Count == 0;
		}
		finally
		{
			var elapsed = Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
			_coverageUpdates++;
			_coverageUpdateMilliseconds += elapsed;
			_coverageMaximumUpdateMilliseconds = Math.Max( _coverageMaximumUpdateMilliseconds, elapsed );
		}
	}

	private void RetireLocalCoverageRegion( GpuMeshRegionKey key, bool retainCache = false )
	{
		_gpuMesher.SetRenderActive( key, false );
		// Transition faces are owned by their coarse regular region.
		for ( var face = 0; key.Level > 0 && face < 6; face++ )
			_gpuMesher.SetTransitionActive( new GpuTransitionKey( key.Level - 1, key.Level,
				key.Coordinate, (GpuTransitionFace)face ), false );
		// Removing the last finer region also retires the adjacent coarse owner's seam.
		for ( var level = key.Level + 1; level < SupportedVisualLevelCount; level++ )
		{
			var ancestor = CoverageAncestor( key, level );
			if ( _gpuMesher.HasRenderDescendant( ancestor ) ) continue;
			for ( var face = 0; face < 6; face++ )
			{
				var direction = face switch
				{
					0 => new Vector3Int( -1, 0, 0 ), 1 => new Vector3Int( 1, 0, 0 ),
					2 => new Vector3Int( 0, -1, 0 ), 3 => new Vector3Int( 0, 1, 0 ),
					4 => new Vector3Int( 0, 0, -1 ), _ => new Vector3Int( 0, 0, 1 )
				};
				var owner = new GpuMeshRegionKey( level, ancestor.Coordinate + direction );
				if ( _gpuMesher.IsRenderActive( owner ) )
					_gpuMesher.SetTransitionActive( new GpuTransitionKey( level - 1, level,
						owner.Coordinate, (GpuTransitionFace)(face ^ 1) ), false );
			}
		}
		_coverageDiffersFromLayout = true;
		_waterRequestSerial++;
		var state = _levels[key.Level];
		if ( retainCache && (state.PlacementChanged ? state.NextDesiredCache : state.DesiredCache).Contains( key.Coordinate ) ) return;
		// Render coverage and gameplay preparation have different extents.
		if ( _predictionRegions.Contains( key ) ) return;
		if ( key.Level == 0 && _renderDesiredChunks.Contains( key.Coordinate ) ) return;
		_gpuMesher.Remove( key );
		// Prepared-without-a-GPU-record means proven empty, never evicted geometry.
		if ( key.Level == 0 && _renderPreparedChunks.Remove( key.Coordinate ) )
		{
			_renderPreparedRevision++;
			MarkWaterPresentationDirty( new GpuMeshRegionKey( 0, key.Coordinate ) );
		}
	}

	private void ConsiderLocalReplacement( GpuMeshRegionKey requested )
	{
		var maximum = _stagedVisualConfiguration.MaximumVisualLod;
		if ( !IsExteriorCoverageRequired( requested ) &&
			(requested.Level > maximum || !_coverageRoots.Contains( CoverageAncestor( requested, maximum ) )) )
		{
			// Outside the requested outer coverage. No replacement volume is required here.
			if ( _gpuMesher.IsRenderActive( requested ) )
				RetireLocalCoverageRegion( requested );
			return;
		}
		if ( FindCoverageAncestor( requested, out var current ) )
		{
			if ( _coverageDesired.Contains( current ) ) return;
			// Covered background regions retain their mesh until final publication.
			// Only immediate player detail needs a speculative refinement patch.
			if ( _coverageSplitTargets.Contains( current ) && IsImmediateCoverageRequired( requested ) )
			{
				TryAdmitLocalReplacement( current, requested, split: true );
			}
		}
		else
		{
			// An uncovered region needs its requested mesh, not a temporary coarse tree.
			if ( !IsLocalCoverageRequired( requested ) || HasCoverageDescendant( requested ) ) return;
			if ( FindCoarseCoverageBlocker( requested, requested.Level + 1, out var blocker ) )
			{
				TryAdmitLocalReplacement( blocker, requested, split: true );
				return;
			}
			TryAdmitLocalReplacement( requested, requested, split: false );
		}
	}

	private bool HasCoverageDescendant( GpuMeshRegionKey key ) =>
		_gpuMesher.IsRenderActive( key ) || _gpuMesher.HasRenderDescendant( key );

	private bool FindCoarseCoverageBlocker( GpuMeshRegionKey parent, int maximumNeighborLevel, out GpuMeshRegionKey blocker )
	{
		for ( var z = -1; z <= 1; z++ )
		for ( var y = -1; y <= 1; y++ )
		for ( var x = -1; x <= 1; x++ )
		{
			if ( x == 0 && y == 0 && z == 0 ) continue;
			var neighbor = new GpuMeshRegionKey( parent.Level, parent.Coordinate + new Vector3Int( x, y, z ) );
			if ( FindCoverageAncestor( neighbor, out blocker ) && blocker.Level > maximumNeighborLevel ) return true;
		}
		blocker = default;
		return false;
	}

	private void TryAdmitLocalReplacement( GpuMeshRegionKey parent, GpuMeshRegionKey resume, bool split )
	{
		if ( split )
		{
			if ( _coveragePending.Count != 0 )
			{
				QueueCoverageCandidate( resume );
				_coverageAdmissionDeferred = true;
				return;
			}
			var patch = new LocalReplacement { Parent = parent, Split = true };
			patch.Requests.Add( resume );
			BeginRefinementPatch( patch, resume );
			_coveragePending.Add( patch );
			return;
		}
		foreach ( var pending in _coveragePending )
		{
			if ( pending.Parent != parent ) continue;
			// Every blocked request needs a wakeup, not just the admission owner.
			pending.Requests.Add( resume );
			return;
		}
		var operation = new LocalReplacement { Parent = parent };
		operation.Requests.Add( resume );
		if ( !DescribeLocalReplacement( operation ) ) return;
		_coveragePending.Add( operation );
		RefreshLocalCoverageRequirements();
		// The pending limit bounds waiting work, not already-ready publication.
		if ( PrepareLocalReplacement( operation ) )
		{
			CommitLocalReplacement( operation );
			_coveragePending.RemoveAt( _coveragePending.Count - 1 );
			RefreshLocalCoverageRequirements();
		}
	}


	private const int MaximumRefinementLeaves = 4096;

	private void BeginRefinementPatch( LocalReplacement operation, GpuMeshRegionKey requested )
	{
		var center = WorldToChunkCoordinate( ActiveStreamingTarget.WorldPosition );
		operation.Focus = new GpuMeshRegionKey( 0, center );
		operation.PreviousFocusLod = FindCoverageAncestor( operation.Focus, out var previous ) ? previous.Level : -1;
		for ( var z = -1; z <= 1; z++ )
		for ( var y = -1; y <= 1; y++ )
		for ( var x = -1; x <= 1; x++ )
		{
			var near = new GpuMeshRegionKey( 0, center + new Vector3Int( x, y, z ) );
			if ( _coverageDesired.Contains( near ) ) operation.Targets.Enqueue( near );
		}
		// Select a desired leaf, never a temporal intermediate parent.
		var focus = requested.Level > 0 && !FindCoverageAncestor( requested, out _ ) ? operation.Parent : requested;
		while ( _coverageSplitTargets.Contains( focus ) )
		{
			var child = CoverageAncestor( new GpuMeshRegionKey( 0, center ), focus.Level - 1 );
			var minimum = focus.Coordinate * 2;
			focus = new GpuMeshRegionKey( child.Level, new Vector3Int(
				Math.Clamp( child.Coordinate.x, minimum.x, minimum.x + 1 ),
				Math.Clamp( child.Coordinate.y, minimum.y, minimum.y + 1 ),
				Math.Clamp( child.Coordinate.z, minimum.z, minimum.z + 1 ) ) );
		}
		operation.Target = focus;
		operation.PreviousTargetLod = FindCoverageAncestor( focus, out var targetPrevious ) ? targetPrevious.Level : -1;
		operation.Targets.Enqueue( focus );
	}

	private bool FindProposedAncestor( LocalReplacement operation, GpuMeshRegionKey key, out GpuMeshRegionKey leaf )
	{
		for ( var level = key.Level; level < SupportedVisualLevelCount; level++ )
		{
			leaf = CoverageAncestor( key, level );
			if ( operation.Leaves.Contains( leaf ) ||
				(!operation.Outgoing.Contains( leaf ) && _gpuMesher.IsRenderActive( leaf )) ) return true;
		}
		leaf = default;
		return false;
	}

	private void SplitProposedLeaf( LocalReplacement operation, GpuMeshRegionKey key )
	{
		if ( operation.Leaves.Count + 8 > MaximumRefinementLeaves )
		{
			operation.PlanningFailed = true;
			Log.Warning( "[VoxelWorld] coverage.refinement.limit retainedOldCoverage=true" );
			return;
		}
		if ( !operation.Leaves.Remove( key ) ) operation.Outgoing.Add( key );
		operation.SplitNodes.Add( key );
		for ( var child = 0; child < 8; child++ )
		{
			var incoming = CoverageChild( key, child );
			operation.Leaves.Add( incoming );
			operation.Balance.Enqueue( incoming );
		}
	}

	private bool AdvanceRefinementPatch( LocalReplacement operation, long started )
	{
		if ( operation.PlanningFailed ) return false;
		while ( operation.PlanningStage < 3 )
		{
			if ( operation.PlanningStage == 0 )
			{
				if ( operation.Targets.TryPeek( out var target ) )
				{
					if ( FindProposedAncestor( operation, target, out var leaf ) )
					{
						if ( leaf.Level > target.Level ) SplitProposedLeaf( operation, leaf );
						else operation.Targets.Dequeue();
					}
					else
					{
						operation.Targets.Dequeue();
						// Only uncovered finest seeds may be inserted by refinement.
						// Coarse additions use their existing finer-neighbor checks.
						if ( target.Level == 0 )
						{
							if ( operation.Leaves.Count >= MaximumRefinementLeaves )
							{
								operation.PlanningFailed = true;
								Log.Warning( "[VoxelWorld] coverage.refinement.limit retainedOldCoverage=true" );
								return false;
							}
							operation.Leaves.Add( target );
							operation.Balance.Enqueue( target );
							for ( var level = 1; level < SupportedVisualLevelCount; level++ )
								operation.SplitNodes.Add( CoverageAncestor( target, level ) );
						}
					}
				}
				else operation.PlanningStage = 1;
			}
			else if ( operation.PlanningStage == 1 )
			{
				if ( operation.Balance.TryDequeue( out var key ) )
				{
					if ( !operation.Leaves.Contains( key ) ) continue;
					var split = false;
					for ( var z = -1; z <= 1 && !split; z++ )
					for ( var y = -1; y <= 1 && !split; y++ )
					for ( var x = -1; x <= 1; x++ )
					{
						if ( x == 0 && y == 0 && z == 0 ) continue;
						var neighbor = new GpuMeshRegionKey( key.Level, key.Coordinate + new Vector3Int( x, y, z ) );
						if ( !FindProposedAncestor( operation, neighbor, out var coarse ) || coarse.Level <= key.Level + 1 ) continue;
						SplitProposedLeaf( operation, coarse );
						operation.Balance.Enqueue( key );
						split = true;
						break;
					}
				}
				else
				{
					operation.Incoming.AddRange( operation.Leaves );
					foreach ( var outgoing in operation.Outgoing )
						for ( var face = 0; face < 6; face++ )
							operation.OldSeams.Add( new GpuTransitionKey( outgoing.Level - 1, outgoing.Level, outgoing.Coordinate, (GpuTransitionFace)face ) );
					operation.PlanningStage = 2;
				}
			}
			else
			{
				if ( operation.SeamCursor < operation.Incoming.Count )
				{
					var key = operation.Incoming[operation.SeamCursor++];
					for ( var face = 0; face < 6; face++ )
					{
						var direction = face switch
						{
							0 => new Vector3Int( -1, 0, 0 ), 1 => new Vector3Int( 1, 0, 0 ),
							2 => new Vector3Int( 0, -1, 0 ), 3 => new Vector3Int( 0, 1, 0 ),
							4 => new Vector3Int( 0, 0, -1 ), _ => new Vector3Int( 0, 0, 1 )
						};
						var neighbor = new GpuMeshRegionKey( key.Level, key.Coordinate + direction );
						if ( FindProposedAncestor( operation, neighbor, out var coarse ) )
						{
							if ( coarse.Level == key.Level + 1 )
								operation.SeamKeys.Add( new GpuTransitionKey( key.Level, coarse.Level, coarse.Coordinate, (GpuTransitionFace)(face ^ 1) ) );
						}
						else if ( key.Level > 0 && (operation.SplitNodes.Contains( neighbor ) || _gpuMesher.HasRenderDescendant( neighbor )) )
							operation.SeamKeys.Add( new GpuTransitionKey( key.Level - 1, key.Level, key.Coordinate, (GpuTransitionFace)face ) );
					}
				}
				else
				{
					operation.NewSeams.AddRange( operation.SeamKeys );
					var dependencies = new HashSet<GpuMeshRegionKey>( operation.Incoming );
					foreach ( var seam in operation.NewSeams ) dependencies.Add( new GpuMeshRegionKey( seam.CoarseLevel, seam.CoarseCoordinate ) );
					operation.Dependencies.AddRange( dependencies );
					operation.PlanningStage = 3;
					RefreshLocalCoverageRequirements();
				}
			}
			if ( operation.PlanningFailed || Stopwatch.GetElapsedTime( started ).TotalMilliseconds >= LocalCoverageBudgetMilliseconds ) return false;
		}
		return true;
	}

	private bool DescribeLocalReplacement( LocalReplacement operation )
	{
		var parent = operation.Parent;
		operation.Incoming.Clear();
		operation.Dependencies.Clear();
		operation.NewSeams.Clear();
		operation.OldSeams.Clear();
		if ( !IsLocalCoverageRequired( parent ) || FindCoverageAncestor( parent, out _ ) ||
			HasCoverageDescendant( parent ) || FindCoarseCoverageBlocker( parent, parent.Level + 1, out _ ) ) return false;
		// Adding uncovered volume must also respect finer edge/corner neighbors.
		for ( var z = -1; parent.Level > _stagedVisualConfiguration.MinimumVisualLod && z <= 2; z++ )
		for ( var y = -1; y <= 2; y++ )
		for ( var x = -1; x <= 2; x++ )
		{
			if ( x >= 0 && x < 2 && y >= 0 && y < 2 && z >= 0 && z < 2 ) continue;
			var neighbor = new GpuMeshRegionKey( parent.Level - 1, parent.Coordinate * 2 + new Vector3Int( x, y, z ) );
			if ( !FindCoverageAncestor( neighbor, out _ ) && HasCoverageDescendant( neighbor ) ) return false;
		}
		operation.Incoming.Add( parent );
		for ( var face = 0; face < 6; face++ )
		{
			var direction = face switch
			{
				0 => new Vector3Int( -1, 0, 0 ), 1 => new Vector3Int( 1, 0, 0 ),
				2 => new Vector3Int( 0, -1, 0 ), 3 => new Vector3Int( 0, 1, 0 ),
				4 => new Vector3Int( 0, 0, -1 ), _ => new Vector3Int( 0, 0, 1 )
			};
			var neighbor = new GpuMeshRegionKey( parent.Level, parent.Coordinate + direction );
			var ownSeam = new GpuTransitionKey( parent.Level - 1, parent.Level, parent.Coordinate, (GpuTransitionFace)face );
			if ( FindCoverageAncestor( neighbor, out var coarse ) )
			{
				if ( coarse.Level == parent.Level + 1 )
					operation.NewSeams.Add( new GpuTransitionKey( parent.Level, coarse.Level,
						coarse.Coordinate, (GpuTransitionFace)(face ^ 1) ) );
			}
			else if ( parent.Level > _stagedVisualConfiguration.MinimumVisualLod && HasCoverageDescendant( neighbor ) )
				operation.NewSeams.Add( ownSeam );
		}
		operation.Dependencies.AddRange( operation.Incoming );
		foreach ( var seam in operation.NewSeams )
		{
			var coarse = new GpuMeshRegionKey( seam.CoarseLevel, seam.CoarseCoordinate );
			if ( !operation.Dependencies.Contains( coarse ) ) operation.Dependencies.Add( coarse );
		}
		return true;
	}


	private bool PrepareLocalReplacement( LocalReplacement operation )
	{
		var started = Stopwatch.GetTimestamp();
		if ( operation.PreparationCursor == 0 || !ReferenceEquals( operation.PreparationField, CurrentField ) )
		{
			operation.PreparationCursor = 0;
			operation.PreparationField = CurrentField;
			operation.PreparationReady = true;
		}
		var count = operation.Dependencies.Count + operation.NewSeams.Count;
		while ( operation.PreparationCursor < count )
		{
			var index = operation.PreparationCursor++;
			if ( index < operation.Dependencies.Count )
			{
				var key = operation.Dependencies[index];
				if ( AddCoverageWaterRequest( key ) ) _waterCellRevision++;
				var descriptor = CreateRegularDescriptor( key.Level, key.Coordinate, captureRegion: false );
				if ( !IsTerrainRegionPrepared( descriptor ) )
				{
					operation.PreparationReady = false;
					_gpuMesher.SetPlacementRequired( key );
					if ( key.Level > 0 && !_gpuMesher.Contains( descriptor ) )
					{
						descriptor = CreateRegularDescriptor( key.Level, key.Coordinate );
						if ( ClassifyClipboxRegion( key.Level, key.Coordinate ) != ChunkDensityClassification.PotentiallySurfaceContaining )
							_gpuMesher.PublishKnownEmpty( descriptor, GpuMeshResidency.Visual );
						else _gpuMesher.Schedule( descriptor, _playerFigureEightRouteDistance, GpuMeshResidency.Visual );
					}
				}
				if ( !IsChunkContentPrepared( descriptor ) ) operation.PreparationReady = false;
			}
			else
			{
				var key = operation.NewSeams[index - operation.Dependencies.Count];
				_predictionSeamReadinessChecks++;
				if ( !_gpuMesher.IsTransitionResident( CreateTransitionDescriptor( key, captureRegion: false ) ) )
				{
					_predictionSeamReadinessMisses++;
					if ( !_predictionSeams.Contains( key ) )
					{
						_predictionOutsidePackageMisses++;
						_predictionLastOutsideSeam = key;
					}
					operation.PreparationReady = false;
					_gpuMesher.SetPlacementTransitionRequired( key );
					_gpuMesher.ScheduleTransition( CreateTransitionDescriptor( key ), _playerFigureEightRouteDistance );
				}
			}
			if ( operation.Split && Stopwatch.GetElapsedTime( started ).TotalMilliseconds >= LocalCoverageBudgetMilliseconds ) return false;
		}
		operation.PreparationCursor = 0;
		return operation.PreparationReady && ReferenceEquals( operation.PreparationField, CurrentField );
	}

	private void CommitLocalReplacement( LocalReplacement operation )
	{
		var publicationStarted = Stopwatch.GetTimestamp();
		foreach ( var key in operation.OldSeams )
		{
			_gpuMesher.SetTransitionActive( key, false );
			var pair = _transitionPairs[key.CoarseLevel - 1];
			if ( !_predictionSeams.Contains( key ) && !(pair.PlacementChanged ? pair.NextDesired : pair.Desired).Contains( key ) ) _gpuMesher.RemoveTransition( key );
		}
		if ( operation.Split )
			foreach ( var outgoing in operation.Outgoing ) RetireLocalCoverageRegion( outgoing, retainCache: true );
		foreach ( var key in operation.Incoming ) _gpuMesher.SetRenderActive( key, true );
		foreach ( var key in operation.NewSeams ) _gpuMesher.SetTransitionActive( key, true );
		foreach ( var request in operation.Requests )
		{
			if ( IsLocalCoverageRequired( request ) && _gpuMesher.IsRenderActive( request ) ) continue;
			QueueCoverageCandidate( request );
			_coverageResumedRequests++;
		}
		var publicationMilliseconds = Stopwatch.GetElapsedTime( publicationStarted ).TotalMilliseconds;
		_coverageMaximumPublicationMilliseconds = Math.Max( _coverageMaximumPublicationMilliseconds, publicationMilliseconds );
		if ( operation.Split )
		{
			Log.Info( "[VoxelWorld] coverage.refinement.commit " + System.Text.Json.JsonSerializer.Serialize( new
			{
				Outgoing = operation.Outgoing.Count, Incoming = operation.Incoming.Count,
				operation.Focus, operation.PreviousFocusLod,
				operation.Target, operation.PreviousTargetLod,
				PublishedTargetLod = FindCoverageAncestor( operation.Target, out var targetPublished ) ? targetPublished.Level : -1,
				PublishedFocusLod = FindCoverageAncestor( operation.Focus, out var published ) ? published.Level : -1,
				RemovedLevels = operation.Outgoing.GroupBy( key => key.Level ).ToDictionary( group => group.Key, group => group.Count() ),
				PublishedLevels = operation.Incoming.GroupBy( key => key.Level ).ToDictionary( group => group.Key, group => group.Count() ),
				VirtualSplits = operation.SplitNodes.Count, Seams = operation.NewSeams.Count,
				AgeSeconds = Stopwatch.GetElapsedTime( operation.Started ).TotalSeconds,
				PublicationMilliseconds = publicationMilliseconds
			}, PerformanceJsonOptions ) );
		}
		_coverageLocalCommits++;
		_coverageDiffersFromLayout = true;
		_waterRequestSerial++;
	}
	private object CaptureLocalCoverage()
	{
		var exteriorMissingRegular = 0;
		var exteriorMissingContent = 0;
		var exteriorReadyInactive = 0;
		foreach ( var coordinate in _partialOuterChunks )
		{
			var key = new GpuMeshRegionKey( _partialOuterLevel, coordinate );
			if ( HasCoverageDescendant( key ) ) continue;
			var descriptor = CreateRegularDescriptor( key.Level, coordinate, captureRegion: false );
			if ( !IsTerrainRegionPrepared( descriptor ) ) exteriorMissingRegular++;
			else if ( !IsChunkContentPrepared( descriptor ) ) exteriorMissingContent++;
			else exteriorReadyInactive++;
		}
		var operations = _coveragePending.Select( operation => new
		{
			operation.Parent, Kind = operation.Split ? "refinement-patch" : "add-region",
			AgeSeconds = Stopwatch.GetElapsedTime( operation.Started ).TotalSeconds,
			Incoming = operation.Incoming.Count, Dependencies = operation.Dependencies.Count,
			WaitingRequests = operation.Requests.Count,
			PlanningStage = operation.Split ? operation.PlanningStage : (int?)null,
			operation.PlanningFailed, VirtualLeaves = operation.Leaves.Count,
			RemovedParents = operation.Outgoing.Count, VirtualSplits = operation.SplitNodes.Count,
			MissingRegular = operation.Dependencies.Count( key => !IsTerrainRegionPrepared( CreateRegularDescriptor( key.Level, key.Coordinate, captureRegion: false ) ) ),
			MissingWater = operation.Dependencies.Count( key => !IsChunkContentPrepared( CreateRegularDescriptor( key.Level, key.Coordinate, captureRegion: false ) ) ),
			MissingSeams = operation.NewSeams.Count( key => !_gpuMesher.IsTransitionResident( CreateTransitionDescriptor( key, captureRegion: false ) ) )
		} ).ToArray();
		return new
		{
			LocalPlan = _coverageLocalPlan, LocalWorkComplete = _coverageLocalWorkComplete, Converged = _coverageConverged, DiffersFromLayout = _coverageDiffersFromLayout,
			Commits = _coverageLocalCommits, TopologyRetries = _coverageTopologyRetries,
			ResumedRequests = _coverageResumedRequests,
			MaximumPublicationMilliseconds = _coverageMaximumPublicationMilliseconds,
			FinalReadinessChecks = _finalCoverageReadinessChecks,
			MaximumFinalReadinessMilliseconds = _maximumFinalCoverageReadinessMilliseconds,
			MaximumFinalPublicationMilliseconds = _maximumFinalCoveragePublicationMilliseconds,
			Updates = _coverageUpdates, UpdateMilliseconds = _coverageUpdateMilliseconds,
			MaximumUpdateMilliseconds = _coverageMaximumUpdateMilliseconds,
			Queued = _coverageQueue.Count, Retiring = _coverageRetireQueue.Count, RetiringSeams = _coverageRetireSeams.Count, Pending = operations,
			ExteriorPromotions = _coverageExteriorPromotions,
			PlayerCoverageLod = FindCoverageAncestor( new GpuMeshRegionKey( 0,
				WorldToChunkCoordinate( ActiveStreamingTarget.WorldPosition ) ), out var playerCoverage ) ? playerCoverage.Level : -1,
			ExteriorMissingRegular = exteriorMissingRegular,
			ExteriorMissingContent = exteriorMissingContent,
			ExteriorReadyInactive = exteriorReadyInactive,
			AdmissionBlockedByCleanup = (_coverageRetireQueue.Count != 0 || _coverageRetireSeams.Count != 0) &&
				_coverageQueue.TryPeek( out _, out var nextPriority ) && nextPriority.Service > 1,
			ExteriorRequested = _partialOuterChunks.Count,
			ExteriorActive = _partialOuterChunks.Count( coordinate =>
				_gpuMesher.IsRenderActive( new GpuMeshRegionKey( _partialOuterLevel, coordinate ) ) ),
			Desired = _coverageDesired.Count, Active = _gpuMesher.ActiveRegionKeys.Count(),
			PlanAgeSeconds = _coveragePlanTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime( _coveragePlanTimestamp ).TotalSeconds
		};
	}

	[ConCmd( "voxel_coverage_info" )]
	public static void LogCoverageInfo( bool detailed = false )
	{
		if ( !TryGetActiveManager( "coverage.inspect", out var manager ) || manager._gpuMesher is null ) return;
		var overlappingParents = manager._gpuMesher.ActiveRegionKeys.Count( key => manager._gpuMesher.HasRenderDescendant( key ) );
		var unbalanced = 0;
		var missingSeams = 0;
		var extraSeams = 0;
		GpuTransitionKey[] missingExamples = null, extraExamples = null;
		if ( detailed )
		{
			var expected = new HashSet<GpuTransitionKey>();
			foreach ( var key in manager._gpuMesher.ActiveRegionKeys )
			{
				if ( manager.FindCoarseCoverageBlocker( key, key.Level + 1, out _ ) ) unbalanced++;
				if ( key.Level == 0 ) continue;
				for ( var face = 0; face < 6; face++ )
				{
					var direction = face switch
					{
						0 => new Vector3Int( -1, 0, 0 ), 1 => new Vector3Int( 1, 0, 0 ),
						2 => new Vector3Int( 0, -1, 0 ), 3 => new Vector3Int( 0, 1, 0 ),
						4 => new Vector3Int( 0, 0, -1 ), _ => new Vector3Int( 0, 0, 1 )
					};
					var neighbor = new GpuMeshRegionKey( key.Level, key.Coordinate + direction );
					if ( manager._gpuMesher.HasRenderDescendant( neighbor ) )
						expected.Add( new GpuTransitionKey( key.Level - 1, key.Level, key.Coordinate, (GpuTransitionFace)face ) );
				}
			}
			missingSeams = expected.Count( key => !manager._gpuMesher.IsTransitionActive( key ) );
			extraSeams = manager._gpuMesher.ActiveTransitionKeys.Count( key => !expected.Contains( key ) );
			missingExamples = expected.Where( key => !manager._gpuMesher.IsTransitionActive( key ) ).Take( 8 ).ToArray();
			extraExamples = manager._gpuMesher.ActiveTransitionKeys.Where( key => !expected.Contains( key ) ).Take( 8 ).ToArray();
		}
		Log.Info( "[VoxelWorld] coverage.inspect " + System.Text.Json.JsonSerializer.Serialize( new
		{
			Coverage = manager.CaptureLocalCoverage(), OverlappingParents = overlappingParents,
			Detailed = detailed, UnbalancedRegions = detailed ? unbalanced : (int?)null,
			MissingActiveSeams = detailed ? missingSeams : (int?)null,
			UnexpectedActiveSeams = detailed ? extraSeams : (int?)null, MissingExamples = missingExamples, ExtraExamples = extraExamples
		}, PerformanceJsonOptions ) );
	}

}
