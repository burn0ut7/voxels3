using System;
using Sandbox.Rendering;

/// <summary>Bounded, per-view grass derived only from published terrain geometry.</summary>
internal sealed class TerrainGrassRenderer : IDisposable
{
	public const int Capacity = 65536;
	private readonly ComputeShader _generate = new( "shaders/voxels/voxel_grass_cs.shader" );
	private readonly Material _material = Material.FromShader( "shaders/voxels/voxel_grass.shader" );
	private readonly GpuBuffer<Vector4> _roots = new( Capacity * 2, GpuBuffer.UsageFlags.Structured, "Terrain Grass Roots" );
	// Indexed depth and non-indexed forward share count/instance count; all offsets stay zero.
	private readonly GpuBuffer<uint> _arguments = new( 5,
		GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "Terrain Grass Arguments" );
	private readonly Model _blade;
	public RenderAttributes DrawAttributes { get; } = new();
	private readonly GpuBuffer<uint> _statistics = new( 4, GpuBuffer.UsageFlags.Structured, "Terrain Grass Statistics" );
	private bool _readbackRequested;
	private bool _readbackPending;
	private bool _disposed;

	public TerrainGrassRenderer()
	{
		_statistics.SetData( new uint[4] );
		_arguments.SetData( new uint[5] );
		DrawAttributes.Set( "GrassRoots", _roots );
		// A shared triangle lets the engine select the grass shader's depth mode.
		var mesh = new Mesh( _material );
		mesh.CreateVertexBuffer<TerrainVertex>( 3, new TerrainVertex[3].AsSpan() );
		mesh.CreateIndexBuffer( 3, new int[] { 0, 1, 2 }.AsSpan() );
		_blade = Model.Builder.AddMesh( mesh ).Create();
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

	public void Begin( GpuBuffer<Vector4> bounds,
		GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> source )
	{
		_generate.Attributes.Set( "GrassRoots", _roots );
		_generate.Attributes.Set( "GrassArguments", _arguments );
		_generate.Attributes.Set( "GrassCapacity", Capacity );
		_generate.Attributes.Set( "GrassPass", 0 );
		_generate.Attributes.Set( "GrassStatistics", _statistics );
		Graphics.ResourceBarrierTransition( _statistics, ResourceState.UnorderedAccess );
		_generate.Attributes.Set( "VisibilityBounds", bounds );
		_generate.Attributes.Set( "SourceIndirectArguments", source );
		Graphics.ResourceBarrierTransition( bounds, ResourceState.GenericRead );
		Graphics.ResourceBarrierTransition( source, ResourceState.GenericRead );
		Graphics.ResourceBarrierTransition( _roots, ResourceState.UnorderedAccess );
		Graphics.ResourceBarrierTransition( _arguments, ResourceState.UnorderedAccess );
		_arguments.Clear( 0 );
		Graphics.UavBarrier( _arguments );
	}

	public void AddArena( GpuBuffer<TerrainVertex> vertices,
		GpuBuffer<uint> indices, int firstSlot, int slotCount )
	{
		_generate.Attributes.Set( "GrassVertices", vertices );
		_generate.Attributes.Set( "GrassIndices", indices );
		_generate.Attributes.Set( "GrassFirstSlot", firstSlot );
		Graphics.ResourceBarrierTransition( vertices, ResourceState.GenericRead );
		Graphics.ResourceBarrierTransition( indices, ResourceState.GenericRead );
		_generate.Dispatch( slotCount * 64, 1, 1 );
		Graphics.UavBarrier( _arguments );
		Graphics.UavBarrier( _roots );
	}

	public void EndAndDrawDepth()
	{
		_generate.Attributes.Set( "GrassPass", 1 );
		_generate.Dispatch( 1, 1, 1 );
		Graphics.UavBarrier( _arguments );
		Graphics.UavBarrier( _statistics );
		Graphics.ResourceBarrierTransition( _arguments, ResourceState.IndirectArgument );
		Graphics.ResourceBarrierTransition( _roots, ResourceState.GenericRead );
		Graphics.DrawModelInstancedIndirect( _blade, _arguments, 0, DrawAttributes );
	}

	public void Draw( CommandList commands )
	{
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
