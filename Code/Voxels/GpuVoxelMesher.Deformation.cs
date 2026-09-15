using System;
using System.Diagnostics;

internal sealed partial class GpuVoxelMesher
{
	private TerrainFieldSnapshot _editedField;
	private SdfWorldAabb _editDependencyBounds;
	private bool _editEpochChanged;
	private readonly Queue<PendingMesh> _editNearDispatchQueue = new();
	private readonly Queue<PendingMesh> _editOuterDispatchQueue = new();
	private readonly Queue<PendingTransition> _editTransitionDispatchQueue = new();
	private bool _fieldPresentationReady = true;

	public void SetFieldPresentationReady( bool ready )
	{
		if ( _fieldPresentationReady == ready ) return;
		_fieldPresentationReady = ready;
		MarkDrawCommandsDirty();
	}
	private readonly Dictionary<GpuMeshRegionKey, PendingMesh> _editRegularRefresh = new();
	private readonly List<GpuTransitionDescriptor> _editTransitionRefresh = new();

	private bool _editPublicationOpen;
	private bool _editPublicationPending;
	private readonly HashSet<GpuMeshRegionKey> _editRegularDependencies = new();
	private readonly HashSet<GpuTransitionKey> _editTransitionDependencies = new();
	private readonly Dictionary<GpuMeshRegionKey, CandidateMesh> _editRegularCandidates = new();
	private readonly Dictionary<GpuTransitionKey, CandidateTransition> _editTransitionCandidates = new();

	public int LastFieldPublicationRevision { get; private set; }
	public long LastFieldPublicationTimestamp { get; private set; }
	public int LastFieldRegularReused { get; private set; }
	public int LastFieldTransitionsReused { get; private set; }

	public bool FieldPublicationPending => _editPublicationPending;
	public string FieldPublicationStatus => $"regular={_editRegularCandidates.Count}/{_editRegularDependencies.Count} " +
		$"transitions={_editTransitionCandidates.Count}/{_editTransitionDependencies.Count} " +
		$"contentWaiting={_editRegularCandidates.Values.Count( candidate => !_chunkContentPrepared( candidate.Descriptor ) )} open={_editPublicationOpen}";

	public bool EditRebuildPending => _editPublicationPending || _editedField is not null &&
		(_pending.Values.Any( request => request.Descriptor.EditRevision > 0 ) ||
		_scratchLanes.Any( lane => lane.CountInFlight.Any( request => request.Descriptor.EditRevision > 0 ) ||
			lane.EmitInFlight.Any( request => request.Descriptor.EditRevision > 0 ) ) ||
		_transitionPending.Values.Any( request => request.Descriptor.EditRevision > 0 ) ||
		_transitionScratchLanes.Any( lane => lane.CountInFlight.Any( request => request.Descriptor.EditRevision > 0 ) ||
			lane.EmitInFlight.Any( request => request.Descriptor.EditRevision > 0 ) ));

	/// <summary>Refresh every cached or queued dependency, retaining old geometry until replacement.</summary>
	public int InvalidateField( TerrainFieldChange change, float routeDistance )
	{
		if ( _editPublicationPending ) throw new InvalidOperationException( "Previous terrain publication has not completed." );
		var field = change.Result;
		var dirtyBounds = change.DependencyPageBounds;
		_editedField = field;
		_editDependencyBounds = dirtyBounds;
		_editEpochChanged = change.Source.Epoch != field.Epoch;
		var identicalRestore = _editEpochChanged && change.ChangedSamples == 0;
		LastFieldRegularReused = 0;
		LastFieldTransitionsReused = 0;
		_editPublicationOpen = true;
		_editPublicationPending = true;
		_editRegularRefresh.Clear();
		foreach ( var resident in _resident.Values )
		{
			if ( !_editEpochChanged && !TerrainFieldChange.Intersects( resident.Descriptor.SamplingBounds, dirtyBounds ) ) continue;
			// Revision blocks cover more than the changed samples. Published geometry
			// outside the actual sampling dependency is still exact; only rebase its
			// identity. Pending work keeps the ordinary stale-result checks below.
			if ( (identicalRestore || !_editEpochChanged &&
				!TerrainFieldChange.Intersects( resident.Descriptor.SamplingBounds, change.AffectedBounds )) &&
				resident.Descriptor.MatchesField( change.Source ) )
			{
				var replacement = resident.Descriptor.WithField( field, captureRegion: false );
				if ( replacement != resident.Descriptor ) LastFieldRegularReused++;
				resident.Descriptor = replacement with { Field = null };
				continue;
			}
			_editRegularRefresh[resident.Descriptor.Key] = new PendingMesh(
				resident.Descriptor, resident.Residency, 0, routeDistance );
		}
		foreach ( var lane in _scratchLanes )
		{
			foreach ( var request in lane.CountInFlight )
				_editRegularRefresh[request.Descriptor.Key] = new PendingMesh( request.Descriptor, request.Residency, 0, routeDistance );
			foreach ( var request in lane.EmitInFlight )
				_editRegularRefresh[request.Descriptor.Key] = new PendingMesh( request.Descriptor, request.Residency, 0, routeDistance );
		}
		foreach ( var request in _pending.Values ) _editRegularRefresh[request.Descriptor.Key] = request;
		var changed = 0;
		foreach ( var request in _editRegularRefresh.Values )
		{
			var descriptor = request.Descriptor.WithField( field );
			if ( descriptor == request.Descriptor ) continue;
			Schedule( descriptor, routeDistance, request.Residency );
			changed++;
		}
		_editRegularRefresh.Clear();
		_editTransitionRefresh.Clear();
		foreach ( var descriptor in _transitionDesiredDescriptors.Values )
		{
			if ( !_editEpochChanged && !TerrainFieldChange.Intersects( descriptor.SamplingBounds, dirtyBounds ) ) continue;
			var replacement = descriptor.WithField( field, captureRegion: false );
			if ( replacement != descriptor ) _editTransitionRefresh.Add( replacement );
		}
		foreach ( var descriptor in _editTransitionRefresh )
		{
			var desired = _transitionDesiredDescriptors[descriptor.Key];
			if ( (identicalRestore || !_editEpochChanged &&
				!TerrainFieldChange.Intersects( desired.SamplingBounds, change.AffectedBounds )) &&
				desired.MatchesField( change.Source ) &&
				_transitionResident.TryGetValue( descriptor.Key, out var resident ) && resident.Descriptor == desired )
			{
				resident.Descriptor = descriptor with { Field = null };
				_transitionDesiredDescriptors[descriptor.Key] = resident.Descriptor;
				LastFieldTransitionsReused++;
				continue;
			}
			ScheduleTransition( descriptor, routeDistance );
			changed++;
		}
		_editTransitionRefresh.Clear();
		return changed;
	}
	public void SealFieldPublication() => _editPublicationOpen = false;

	private void TryPublishEditedField()
	{
		if ( !_editPublicationPending || _editPublicationOpen ||
			_editRegularDependencies.Count != _editRegularCandidates.Count ||
			_editTransitionDependencies.Count != _editTransitionCandidates.Count ) return;
		foreach ( var candidate in _editRegularCandidates.Values )
			if ( !_chunkContentPrepared( candidate.Descriptor ) ) return;
		foreach ( var candidate in _editRegularCandidates.Values ) PublishCompletedRegular( candidate, candidate.Residency );
		foreach ( var candidate in _editTransitionCandidates.Values ) PublishCompletedTransition( candidate );
		_editRegularCandidates.Clear();
		_editTransitionCandidates.Clear();
		_editRegularDependencies.Clear();
		_editTransitionDependencies.Clear();
		_editPublicationPending = false;
		LastFieldPublicationRevision = _editedField.Revision;
		LastFieldPublicationTimestamp = Stopwatch.GetTimestamp();
		MarkDrawCommandsDirty();
	}

	private void ClearEditPublication()
	{
		foreach ( var candidate in _editRegularCandidates.Values ) Release( candidate.Handle );
		foreach ( var candidate in _editTransitionCandidates.Values ) Release( candidate.Handle );
		_editRegularCandidates.Clear();
		_editTransitionCandidates.Clear();
		_editRegularDependencies.Clear();
		_editTransitionDependencies.Clear();
		_editPublicationOpen = false;
		_editPublicationPending = false;
		LastFieldPublicationRevision = 0;
		LastFieldPublicationTimestamp = 0;
		LastFieldRegularReused = 0;
		LastFieldTransitionsReused = 0;
	}

}
