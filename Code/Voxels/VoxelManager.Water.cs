using System;
using System.Collections.Generic;

public sealed partial class VoxelManager
{
	private SurfaceWaterRenderer _waterRenderer;
	private readonly List<SdfWorldAabb> _waterChunkBounds = new();
	private long _waterPlacementRevision = -1;
	private long _waterResidentRevision = -1;
	private bool _waterAwaitingResident;

	[Property, ReadOnly, Category( "World" )]
	public string WaterStatus => _waterRenderer is null ? "Waiting for terrain" :
		$"{_waterRenderer.ChunkCount:N0} published surface chunks; {_waterRenderer.VertexCount:N0} vertices";

	private void UpdateSurfaceWater()
	{
		var residentRevision = _gpuMesher.ResidentPublicationRevision;
		if ( _waterPlacementRevision == _clipboxPlacementCommits &&
			(!_waterAwaitingResident || _waterResidentRevision == residentRevision) ) return;

		_waterChunkBounds.Clear();
		_waterAwaitingResident = false;
		foreach ( var state in _levels )
		{
			if ( !state.HasPlacement || !state.VisualEnabled ) continue;
			var chunkSize = _appliedCellsPerAxis * CellSizeForLevel( state.Level );
			// The exposed top belongs to the water volume immediately below it.
			var z = (int)MathF.Ceiling( CurrentField.Settings.SeaLevel / chunkSize ) - 1;
			if ( z < state.OuterMinimum.z || z >= state.OuterMaximum.z ) continue;
			for ( var y = state.OuterMinimum.y; y < state.OuterMaximum.y; y++ )
			{
				for ( var x = state.OuterMinimum.x; x < state.OuterMaximum.x; x++ )
				{
					var coordinate = new Vector3Int( x, y, z );
					if ( !state.Active.Contains( coordinate ) ) continue;
					if ( !_gpuMesher.IsResident( new GpuMeshRegionKey( state.Level, coordinate ) ) )
					{
						_waterAwaitingResident = true;
						continue;
					}
					var minimum = new Vector3( x * chunkSize, y * chunkSize, z * chunkSize );
					_waterChunkBounds.Add( new SdfWorldAabb( minimum, minimum + Vector3.One * chunkSize ) );
				}
			}
		}
		_waterRenderer.Update( _waterChunkBounds, CurrentField.Settings );
		_waterPlacementRevision = _clipboxPlacementCommits;
		_waterResidentRevision = residentRevision;
	}

	private void ResetSurfaceWater()
	{
		_waterRenderer?.Delete();
		_waterRenderer = null;
		_waterChunkBounds.Clear();
		_waterPlacementRevision = -1;
		_waterResidentRevision = -1;
		_waterAwaitingResident = false;
	}
}
