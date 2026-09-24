using System;

public sealed partial class VoxelManager
{
	/// <summary>Applied authoritative generation identity for local biome inspection.</summary>
	internal bool TryGetBiomeContext( out Guid world, out ProceduralTerrainSettings settings )
	{
		world = _terrainField is null ? Guid.Empty : CurrentField.WorldId;
		settings = _terrainField is null ? default : CurrentField.Settings;
		return _terrainField is not null && !Scene.IsEditor;
	}

	// Texture is owned by the debug panel. Unbind before it releases the resource.
	internal void SetBiomeDebugMap( Texture texture, Vector4 bounds, bool smooth ) =>
		_gpuMesher?.SetBiomeDebugMap( texture, bounds, smooth );
}
