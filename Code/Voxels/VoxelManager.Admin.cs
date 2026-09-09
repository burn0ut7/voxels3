public sealed partial class VoxelManager
{
	/// <summary>Conservative clearance check for returning a debug player to solid physics.</summary>
	public bool IsAdminDestinationClear( PlayerController player, Vector3 position )
	{
		if ( !Networking.IsHost || !player.IsValid() || player.IsProxy || _terrainField is null ) return false;
		var box = player.BodyBox();
		var bounds = new BBox( box.Mins + position, box.Maxs + position );
		if ( !IsTerrainCollisionReady( bounds ) ) return false;
		// A triangle mesh overlap alone cannot prove that a box is outside solid terrain.
		if ( CurrentField.GetDensityRange( new SdfWorldAabb( bounds.Mins, bounds.Maxs ), RequiredBaseCellSize )
			.Classification != ChunkDensityClassification.DefinitelyAir ) return false;
		var trace = Scene.Trace.Box( box, position, position ).IgnoreGameObjectHierarchy( player.GameObject ).Run();
		return !trace.StartedSolid && !trace.Hit;
	}
}
