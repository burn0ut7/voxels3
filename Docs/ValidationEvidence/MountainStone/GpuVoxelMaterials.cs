using System;

/// <summary>One palette and draw contract for every material; owned by the terrain mesher.</summary>
internal sealed class GpuVoxelMaterials : IDisposable
{
	private readonly GpuBuffer<Vector4> _palette;
	private readonly GpuRiverAtlas _rivers = new();
	public bool RiversReady { get; private set; }

	public void UpdateRivers( GpuRiverAtlas.Data data )
	{
		_rivers.Update( data );
		RiversReady = true;
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

	public void Bind( Sandbox.Rendering.CommandList.AttributeAccess attributes, ProceduralTerrainSettings settings )
	{
		_rivers.Bind( attributes );
		attributes.Set( "VoxelMaterialPalette", _palette );
		attributes.Set( "VoxelMaterialIds", new Vector4( VoxelMaterials.Grass, VoxelMaterials.Dirt, VoxelMaterials.Stone, VoxelMaterials.Air ) );
		attributes.Set( "VoxelMaterialSnow", new Vector4( ProceduralVoxelMaterials.SnowPeakFraction,
			VoxelMaterials.Snow, ProceduralVoxelMaterials.SnowMountainWeight, 0f ) );
		attributes.Set( "VoxelMaterialRules", new Vector4( ProceduralVoxelMaterials.LayerSize,
			ProceduralVoxelMaterials.SoilDepth, 0f, TerrainField.SampleSpacing ) );
		attributes.Set( "VoxelMaterialTerrain", new Vector4( settings.WorldSeed, settings.LandAmount, settings.MountainAmount, settings.PlainsAmount ) );
		attributes.Set( "VoxelMaterialScales", new Vector4( settings.ContinentalScale, settings.MountainRegionScale, settings.LocalLandformScale, settings.ReliefHeight ) );
		attributes.Set( "VoxelMaterialRuggedness", settings.Ruggedness );
		attributes.Set( "VoxelSeaLevel", settings.SeaLevel );
	}

	public void Dispose()
	{
		_rivers.Dispose();
		_palette.Dispose();
	}
}
