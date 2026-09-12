using System;

/// <summary>One palette and draw contract for every material; owned by the terrain mesher.</summary>
internal sealed class GpuVoxelMaterials : IDisposable
{
	private readonly GpuBuffer<Vector4> _palette;
	public static void BindGeneration( RenderAttributes attributes )
	{
		attributes.Set( "VoxelGeneratedLayers", new Vector2( ProceduralVoxelMaterials.LayerSize, ProceduralVoxelMaterials.SoilDepth ) );
		attributes.Set( "VoxelGeneratedMountain", new Vector4( ProceduralVoxelMaterials.SnowPeakFraction,
			ProceduralVoxelMaterials.SnowMountainWeight, ProceduralVoxelMaterials.MountainStoneWeight,
			ProceduralVoxelMaterials.MountainGrassMaxSlopeSquared ) );
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
	}
}
