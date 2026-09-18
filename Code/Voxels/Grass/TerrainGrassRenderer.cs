using System;
using Sandbox.Rendering;

/// <summary>Bounded, per-view grass derived only from published terrain geometry.</summary>
internal sealed class TerrainGrassRenderer : IDisposable
{
	public const int Capacity = 65536;
	private readonly ComputeShader _generate = new( "shaders/voxels/voxel_grass_cs.shader" );
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_grass.shader" );
	private readonly GpuBuffer<Vector4> _roots = new( Capacity * 2, GpuBuffer.UsageFlags.Structured, "Terrain Grass Roots" );
	private readonly GpuBuffer<uint> _arguments = new( 4,
		GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "Terrain Grass Arguments" );
	public RenderAttributes DrawAttributes { get; } = new();
	private readonly GpuBuffer<uint> _statistics = new( 4, GpuBuffer.UsageFlags.Structured, "Terrain Grass Statistics" );
	private bool _readbackRequested;
	private bool _readbackPending;
	private bool _disposed;

	public TerrainGrassRenderer()
	{
		_statistics.SetData( new uint[4] );
	}

	public void RequestStatistics() => _readbackRequested = true;

	public void ProcessReadback()
	{
		if ( !_readbackRequested || _readbackPending || _disposed ) return;
		_readbackRequested = false;
		_readbackPending = true;
		_statistics.GetDataAsync<uint>( data =>
		{
			_readbackPending = false;
			if ( _disposed || data.Length < 4 ) return;
			Log.Info( $"[TerrainGrass] views={data[0]} currentBlades={data[1]} peakCandidates={data[2]} overflowViews={data[3]} capacity={Capacity} rootBytes={Capacity * 32}" );
		}, 0, 4 );
	}

	public void Begin( CommandList commands, GpuBuffer<Vector4> bounds,
		GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> source )
	{
		commands.Attributes.Set( "GrassRoots", _roots );
		commands.Attributes.Set( "GrassArguments", _arguments );
		commands.Attributes.Set( "GrassCapacity", Capacity );
		commands.Attributes.Set( "GrassPass", 0 );
		commands.Attributes.Set( "GrassStatistics", _statistics );
		commands.ResourceBarrierTransition( _statistics, ResourceState.UnorderedAccess );
		commands.Attributes.Set( "VisibilityBounds", bounds );
		commands.Attributes.Set( "SourceIndirectArguments", source );
		commands.ResourceBarrierTransition( _roots, ResourceState.UnorderedAccess );
		commands.ResourceBarrierTransition( _arguments, ResourceState.UnorderedAccess );
		commands.Clear( _arguments, 0 );
		commands.UavBarrier( _arguments );
	}

	public void AddArena( CommandList commands, GpuBuffer<TerrainVertex> vertices,
		GpuBuffer<uint> indices, int firstSlot, int slotCount )
	{
		commands.Attributes.Set( "GrassVertices", vertices );
		commands.Attributes.Set( "GrassIndices", indices );
		commands.Attributes.Set( "GrassFirstSlot", firstSlot );
		commands.ResourceBarrierTransition( vertices, ResourceState.GenericRead );
		commands.ResourceBarrierTransition( indices, ResourceState.GenericRead );
		commands.DispatchCompute( _generate, slotCount * 64, 1, 1 );
		commands.UavBarrier( _arguments );
		commands.UavBarrier( _roots );
	}

	public void End( CommandList commands )
	{
		commands.Attributes.Set( "GrassPass", 1 );
		commands.DispatchCompute( _generate, 1, 1, 1 );
		commands.UavBarrier( _arguments );
		commands.UavBarrier( _statistics );
		commands.ResourceBarrierTransition( _arguments, ResourceState.IndirectArgument );
		commands.ResourceBarrierTransition( _roots, ResourceState.GenericRead );
		DrawAttributes.Set( "GrassRoots", _roots );
		commands.DrawInstancedIndirect( _material, _arguments, 0, DrawAttributes );
	}

	public void Dispose()
	{
		_disposed = true;
		_statistics.Dispose();
		_roots.Dispose();
		_arguments.Dispose();
	}
}
