public sealed partial class VoxelManager
{
	/// <summary>
	/// Resolves a world position to a logical base chunk using the applied layout.
	/// Chunk coordinates use floor division, including below zero.
	/// </summary>
	public bool TryGetChunkCoordinate( Vector3 worldPosition, out Vector3Int coordinate )
	{
		coordinate = default;
		if ( !_hasStreamingCenter )
		{
			return false;
		}

		coordinate = WorldToChunkCoordinate( worldPosition );
		return true;
	}
}
