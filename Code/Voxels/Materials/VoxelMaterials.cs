using System;

/// <summary>Immutable identity and appearance. IDs are append-only, independent of terrain rules.</summary>
internal readonly record struct VoxelMaterialDefinition(
	ushort Id, string Name, Vector3 DarkColor, Vector3 LightColor );

internal static class VoxelMaterials
{
	public const ushort Air = 0;
	public const ushort Grass = 1;
	public const ushort Dirt = 2;
	public const ushort Stone = 3;
	public const ushort Water = 4;

	// Keep explicit IDs in ascending order. One row adds a type without changing consumers.
	private static readonly VoxelMaterialDefinition[] Definitions =
	[
		new( Air, "Air", Vector3.Zero, Vector3.Zero ),
		new( Grass, "Grass", new( 0.14f, 0.32f, 0.08f ), new( 0.26f, 0.52f, 0.14f ) ),
		new( Dirt, "Dirt", new( 0.24f, 0.12f, 0.055f ), new( 0.40f, 0.23f, 0.11f ) ),
		new( Stone, "Stone", new( 0.25f, 0.27f, 0.29f ), new( 0.43f, 0.45f, 0.47f ) ),
		new( Water, "Water", new( 0.025f, 0.16f, 0.48f ), new( 0.05f, 0.36f, 0.78f ) )
	];
	private static readonly VoxelMaterialDefinition Unknown =
		new( ushort.MaxValue, "Unknown", new( 1f, 0f, 1f ), new( 0.3f, 0f, 0.3f ) );

	static VoxelMaterials()
	{
		if ( Definitions.Length >= ushort.MaxValue ) throw new InvalidOperationException( "Material ID capacity exceeded." );
		for ( var i = 0; i < Definitions.Length; i++ )
		{
			if ( Definitions[i].Id != i ) throw new InvalidOperationException( "Material IDs must match their stable catalog slots." );
		}
	}

	public static ReadOnlySpan<VoxelMaterialDefinition> All => Definitions;
	public static VoxelMaterialDefinition Get( ushort id ) => id < Definitions.Length ? Definitions[id] : Unknown;
}
