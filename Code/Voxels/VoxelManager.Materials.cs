public sealed partial class VoxelManager
{
	/// <summary>Inspect the logical material node containing a world position.</summary>
	[ConCmd( "voxel_material_info" )]
	public static void LogMaterialInfoCommand( float x, float y, float z )
	{
		if ( !TryGetActiveManager( "material.inspect", out var manager ) ) return;
		if ( !float.IsFinite( x ) || !float.IsFinite( y ) || !float.IsFinite( z ) ||
			System.MathF.Max( System.MathF.Abs( x ), System.MathF.Max( System.MathF.Abs( y ), System.MathF.Abs( z ) ) ) > TerrainField.MaximumWorldCoordinate )
		{
			Log.Warning( "[VoxelWorld] material.inspect rejected: position outside supported world bounds." );
			return;
		}
		var coordinate = manager.WorldToChunkCoordinate( new Vector3( x, y, z ) );
		var origin = new Vector3( coordinate.x, coordinate.y, coordinate.z ) * (manager._appliedCellsPerAxis * manager._appliedCellSize);
		var local = (new Vector3( x, y, z ) - origin) / manager._appliedCellSize;
		var sample = new Vector3Int( (int)System.MathF.Floor( local.x ), (int)System.MathF.Floor( local.y ), (int)System.MathF.Floor( local.z ) );
		var chunk = new VoxelChunk( coordinate, manager._appliedCellsPerAxis, manager._appliedCellSize, manager.CurrentField );
		if ( !chunk.TryGetSample( sample, out var density, out var materialId ) )
		{
			Log.Info( $"[VoxelWorld] material.inspect pending chunk={coordinate} sample={sample}" );
			return;
		}
		if ( !manager.CurrentField.TrySampleCell( new Vector3( x, y, z ), out var cell ) )
		{
			Log.Info( "[VoxelWorld] material.inspect cell data pending" );
			return;
		}
		Log.Info( $"[VoxelWorld] material.inspect chunk={coordinate} sample={sample} density={density} " +
			$"materialId={materialId} material=\"{VoxelMaterials.Get( materialId ).Name}\" " +
			$"solidFraction={cell.SolidFraction} logicalEmpty={cell.IsEmpty}" );
	}
}
