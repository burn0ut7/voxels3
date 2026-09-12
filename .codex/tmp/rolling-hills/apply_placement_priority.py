from pathlib import Path
p=Path('Code/Voxels/GpuVoxelMesher.cs');s=p.read_text();Path('.codex/tmp/rolling-hills/GpuVoxelMesher.before-placement-priority.txt').write_text(s)
s=s.replace('private readonly Queue<PendingMesh> _activeOuterDispatchQueue = new();','private readonly Queue<PendingMesh> _activeOuterDispatchQueue = new();\n\tprivate readonly Queue<PendingMesh> _placementNearDispatchQueue = new();\n\tprivate readonly Queue<PendingMesh> _placementOuterDispatchQueue = new();\n\tprivate readonly HashSet<GpuMeshRegionKey> _placementRequired = new();',1)
needle='\tpublic void SetRenderActive( GpuMeshRegionKey key, bool active )'
assert needle in s
s=s.replace(needle,"""	public void SetPlacementRequired( GpuMeshRegionKey key )
	{
		if ( _placementRequired.Add( key ) && _pending.TryGetValue( key, out var pending ) )
			QueuePending( pending );
	}

	public void ClearPlacementPriority() => _placementRequired.Clear();

"""+needle,1)
needle='\t\tif ( pending.Residency == GpuMeshResidency.Gameplay )\n\t\t{'
assert s.count(needle)==1
s=s.replace(needle,"""		if ( _placementRequired.Contains( pending.Descriptor.Key ) &&
			!(pending.Descriptor.Key.Level >= 2 && _renderActive.Contains( pending.Descriptor.Key )) )
		{
			if ( pending.Residency == GpuMeshResidency.Gameplay ) _pendingGameplayCount++;
			else if ( pending.Residency == GpuMeshResidency.Warm ) _pendingWarmCount++;
			var queue = pending.Descriptor.Key.Level >= 2 ? _placementOuterDispatchQueue : _placementNearDispatchQueue;
			queue.Enqueue( pending );
			return;
		}
"""+needle,1)
s=s.replace('if ( TryDequeuePendingFrom( _editNearDispatchQueue, out pending ) ) return true;','if ( TryDequeuePendingFrom( _editNearDispatchQueue, out pending ) ) return true;\n\t\tif ( TryDequeuePlacement( _placementNearDispatchQueue, out pending ) ) return true;',1)
needle='\tprivate bool TryDequeuePendingFrom( Queue<PendingMesh> queue, out PendingMesh pending )'
s=s.replace(needle,"""	private bool TryDequeuePlacement( Queue<PendingMesh> queue, out PendingMesh pending )
	{
		while ( TryDequeuePendingFrom( queue, out pending ) )
		{
			if ( _placementRequired.Contains( pending.Descriptor.Key ) ) return true;
			// A canceled placement demotes unfinished work without losing its request.
			QueuePending( pending );
		}
		pending = default;
		return false;
	}

"""+needle,1)
s=s.replace('TryDequeuePendingFrom( _activeOuterDispatchQueue, out pending ) ||','TryDequeuePendingFrom( _activeOuterDispatchQueue, out pending ) ||\n\t\t\tTryDequeuePlacement( _placementOuterDispatchQueue, out pending ) ||',1)
s=s.replace('\t\t_renderActive.Remove( key );','\t\t_renderActive.Remove( key );\n\t\t_placementRequired.Remove( key );',1)
s=s.replace('\t\t_activeOuterDispatchQueue.Clear();','\t\t_activeOuterDispatchQueue.Clear();\n\t\t_placementNearDispatchQueue.Clear();\n\t\t_placementOuterDispatchQueue.Clear();\n\t\t_placementRequired.Clear();',1)
with p.open('w',newline='') as f:f.write(s.replace('\n','\r\n'))
p=Path('Code/Voxels/VoxelManager.cs');s=p.read_text();Path('.codex/tmp/rolling-hills/VoxelManager.before-placement-priority.txt').write_text(s)
needle='\t\tvar exterior = _levels[_stagedVisualConfiguration.MaximumVisualLod];'
assert s.count(needle)==1
s=s.replace(needle,"""		foreach ( var coordinate in _levels[0].Entering )
		{
			if ( ++inspected % 32 == 0 ) yield return 0;
			_gpuMesher.SetPlacementRequired( new GpuMeshRegionKey( 0, coordinate ) );
		}
"""+needle,1)
needle='\t\t\tforeach ( var coordinate in state.Readiness )\n\t\t\t{\n\t\t\t\tif ( ++inspected % 32 == 0 ) yield return 0;\n\t\t\t\tvar descriptor = CreateRegularDescriptor( level, coordinate );'
assert s.count(needle)==1
s=s.replace(needle,needle+'\n\t\t\t\t_gpuMesher.SetPlacementRequired( descriptor.Key );',1)
s=s.replace('\t\t_clipboxPlacementPending = false;\n\t\t_clipboxPlacementCommits++;','\t\t_clipboxPlacementPending = false;\n\t\t_gpuMesher.ClearPlacementPriority();\n\t\t_clipboxPlacementCommits++;',1)
needle='\tprivate void CancelPendingClipboxPlacement()\n\t{\n\t\tif ( !_clipboxPlacementPending ) return;'
assert needle in s
s=s.replace(needle,needle+'\n\t\t_gpuMesher.ClearPlacementPriority();',1)
with p.open('w',newline='') as f:f.write(s.replace('\n','\r\n'))
