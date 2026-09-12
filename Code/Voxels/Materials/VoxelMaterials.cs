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
	public const ushort Snow = 5;

	// Keep explicit IDs in ascending order. One row adds a type without changing consumers.
	private static readonly VoxelMaterialDefinition[] Definitions =
	[
		new( Air, "Air", Vector3.Zero, Vector3.Zero ),
		new( Grass, "Grass", new( 0.028f, 0.075f, 0.023f ), new( 0.052f, 0.125f, 0.038f ) ),
		new( Dirt, "Dirt", new( 0.075f, 0.038f, 0.020f ), new( 0.125f, 0.068f, 0.035f ) ),
		new( Stone, "Stone", new( 0.070f, 0.075f, 0.080f ), new( 0.120f, 0.125f, 0.130f ) ),
		new( Water, "Water", new( 0.012f, 0.055f, 0.090f ), new( 0.025f, 0.100f, 0.155f ) ),
		new( Snow, "Snow", new( 0.78f, 0.80f, 0.80f ), new( 0.88f, 0.90f, 0.89f ) )
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
