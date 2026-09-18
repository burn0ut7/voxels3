using System;

/// <summary>One palette and draw contract for every material; owned by the terrain mesher.</summary>
internal sealed class GpuVoxelMaterials : IDisposable
{
	// Presentation spacing is independent of canonical cell/layer identity.
	private const float BlendSpacing = 3f * ProceduralVoxelMaterials.LayerSize;
	// Quadratic support plus the existing scratch halo covers slope probes.
	public const float GenerationHalo = ProceduralSand.OceanReach + 1.5f * BlendSpacing;
	private readonly GpuBuffer<Vector4> _palette;
	public static void BindGeneration( RenderAttributes attributes )
	{
		attributes.Set( "VoxelMaterialBlendSpacing", BlendSpacing );
		attributes.Set( "VoxelGeneratedLayers", new Vector2( ProceduralVoxelMaterials.LayerSize, ProceduralVoxelMaterials.SoilDepth ) );
		attributes.Set( "VoxelGeneratedMountain", new Vector4( ProceduralVoxelMaterials.SnowPeakFraction,
			ProceduralVoxelMaterials.SnowMountainWeight, ProceduralVoxelMaterials.MountainStoneWeight,
			ProceduralVoxelMaterials.MountainGrassMaxSlopeSquared ) );
		attributes.Set( "VoxelSandOcean", ProceduralSand.Ocean );
		attributes.Set( "VoxelSandRiver", ProceduralSand.River );
		attributes.Set( "VoxelSandWater", new Vector3( ProceduralSand.MinimumRiverHeight, ProceduralSand.RiverNaturalHeight,
			ProceduralSand.RiverBlendHeight ) );
		attributes.Set( "VoxelSandDepths", new Vector3( ProceduralSand.BuriedMinimumDepth, ProceduralSand.BuriedMaximumDepth, ProceduralSand.MinimumLayerDepth ) );
		attributes.Set( "VoxelSandReach", new Vector2( ProceduralSand.OceanReach, ProceduralSand.RiverReach ) );
		attributes.Set( "VoxelSandBuriedRegion", new Vector4( ProceduralSand.BuriedDeposits.Size, ProceduralSand.BuriedDeposits.Probability ) );
		attributes.Set( "VoxelSandSalts", new Vector3( ProceduralSand.CoverageSalt, ProceduralSand.DetailSalt, ProceduralSand.BuriedDeposits.Salt ) );
	}

	public GpuVoxelMaterials()
	{
		var definitions = VoxelMaterials.All;
		var colors = new Vector4[definitions.Length * 2];
		foreach ( var definition in definitions )
		{
			colors[definition.Id * 2] = new Vector4( definition.DarkColor, 1f );
			colors[definition.Id * 2 + 1] = new Vector4( definition.LightColor, 1f );
		}
		_palette = new GpuBuffer<Vector4>( colors.Length, GpuBuffer.UsageFlags.Structured, "Voxel Material Palette" );
		_palette.SetData( colors.AsSpan() );
	}

	public void Dispose()
	{
		_palette.Dispose();
	}

	public void Bind( RenderAttributes attributes )
	{
		attributes.Set( "VoxelMaterialPalette", _palette );
		attributes.Set( "VoxelMaterialIds", new Vector4( VoxelMaterials.Grass, VoxelMaterials.Dirt, VoxelMaterials.Stone, VoxelMaterials.Snow ) );
		attributes.Set( "VoxelCheckerSize", TerrainField.SampleSpacing );
		attributes.Set( "VoxelSandMaterialId", (float)VoxelMaterials.Sand );
	}
}
