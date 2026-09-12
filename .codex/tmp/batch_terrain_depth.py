from pathlib import Path
p=Path('Code/Voxels/GpuVoxelMesher.cs')
s=p.read_text()
s=s.replace('private readonly ReadbackSceneObject _readbackObject;', 'private readonly ReadbackSceneObject _readbackObject;\n\tprivate readonly TerrainDepthObject _depthObject;')
s=s.replace('_readbackObject = new ReadbackSceneObject( scene.SceneWorld, this );', '_readbackObject = new ReadbackSceneObject( scene.SceneWorld, this );\n\t\t_depthObject = new TerrainDepthObject( scene.SceneWorld, this );')
s=s.replace('_readbackObject?.Delete();', '_depthObject?.Delete();\n\t\t_readbackObject?.Delete();')
import re
s=re.sub(r'\t\tSetDepthVisibility\( resident.Handle, active,\n\t\t\tnew BBox\(.*?\) \);\n', '', s)
start=s.index('\tprivate void SetDepthVisibility(')
end=s.index('\tprivate void UploadVisibilityDescriptors',start)
s=s[:start]+s[end:]
s=s.replace('\t\tpublic TerrainDepthObject DepthObject { get; set; }\n','')
s=s.replace('\t\t\tlock ( handle )\n\t\t\t{\n\t\t\t\thandle.DepthObject?.Delete();\n\t\t\t\thandle.DepthObject = null;\n\t\t\t}\n','')
s=s.replace('\t\t\t_voxelMaterials.Bind( commands.Attributes );\n','')
s=re.sub(r'^.*(?:drawAttributes|commands.Attributes).Set\( "VoxelDepth(?:Vertices|VertexOffset)".*\n', '', s, flags=re.M)
s=s.replace('commands.ResourceBarrierTransition( arena.Vertices, ResourceState.GenericRead );','commands.ResourceBarrierTransition( arena.Vertices, ResourceState.VertexOrIndexBuffer );')
# Per-arena scratch is tiny, independent of the mesh triangle count and reused between views.
s=s.replace('\t\tpublic GpuBuffer<uint> Indices { get; }','\t\tpublic GpuBuffer<uint> Indices { get; }\n\t\tpublic GpuBuffer<Vector4Int> DepthRanges { get; } = new( RegionsPerSlab, GpuBuffer.UsageFlags.Structured, "Voxel Depth Triangle Ranges" );\n\t\tpublic GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> DepthArguments { get; } = new( 1,\n\t\t\tGpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "Voxel Depth Arguments" );')
s=s.replace('public void Dispose() { Vertices.Dispose(); Indices.Dispose(); }','public void Dispose() { Vertices.Dispose(); Indices.Dispose(); DepthRanges.Dispose(); DepthArguments.Dispose(); }')
start=s.index('\tprivate sealed class TerrainDepthObject : SceneCustomObject')
end=s.index('\tprivate sealed class ReadbackSceneObject',start)
s=s[:start]+'''\tprivate sealed class TerrainDepthObject : SceneCustomObject
\t{
\t\tprivate readonly GpuVoxelMesher _owner;
\t\tprivate readonly ComputeShader _visibility = new( "shaders/voxels/voxel_depth_visibility_cs.shader" );
\t\tprivate readonly Model _triangle;

\t\tpublic TerrainDepthObject( SceneWorld world, GpuVoxelMesher owner ) : base( world )
\t\t{
\t\t\t_owner = owner;
\t\t\tFlags.IsOpaque = true;
\t\t\tFlags.IsTranslucent = false;
\t\t\tFlags.CastShadows = true;
\t\t\t// The three driver vertices are replaced by a canonical terrain triangle in the shader.
\t\t\tvar mesh = new Mesh( Material.FromShader( "shaders/voxels/voxel_terrain_depth.shader" ) );
\t\t\tmesh.CreateVertexBuffer<TerrainVertex>( 3, new TerrainVertex[3].AsSpan() );
\t\t\tmesh.CreateIndexBuffer( 3, new int[] { 0, 1, 2 }.AsSpan() );
\t\t\t_triangle = Model.Builder.AddMesh( mesh ).Create();
\t\t}

\t\tpublic override void RenderSceneObject()
\t\t{
\t\t\tif ( Graphics.LayerType != SceneLayerType.DepthPrepass && Graphics.LayerType != SceneLayerType.Shadow ) return;
\t\t\tlock ( _owner._renderCameraLock )
\t\t\t{
\t\t\t\tvar visibility = _owner._visibilityReadbackState?.Visibility;
\t\t\t\tif ( visibility is null || !_owner._fieldPresentationReady ) return;
\t\t\t\t_visibility.Attributes.Set( "VisibilityBounds", visibility.Bounds );
\t\t\t\t_visibility.Attributes.Set( "SourceIndirectArguments", visibility.SourceArguments );
\t\t\t\tGraphics.ResourceBarrierTransition( visibility.Bounds, ResourceState.GenericRead );
\t\t\t\tGraphics.ResourceBarrierTransition( visibility.SourceArguments, ResourceState.GenericRead );
\t\t\t\tforeach ( var arena in _owner._arenas )
\t\t\t\t{
\t\t\t\t\tif ( arena.ActiveResidentCount == 0 ) continue;
\t\t\t\t\t_visibility.Attributes.Set( "DepthSlotOffset", arena.Index * RegionsPerSlab );
\t\t\t\t\t_visibility.Attributes.Set( "DepthRanges", arena.DepthRanges );
\t\t\t\t\t_visibility.Attributes.Set( "DepthArguments", arena.DepthArguments );
\t\t\t\t\tGraphics.ResourceBarrierTransition( arena.DepthRanges, ResourceState.UnorderedAccess );
\t\t\t\t\tGraphics.ResourceBarrierTransition( arena.DepthArguments, ResourceState.UnorderedAccess );
\t\t\t\t\t_visibility.Dispatch( RegionsPerSlab, 1, 1 );
\t\t\t\t\tGraphics.UavBarrier( arena.DepthRanges );
\t\t\t\t\tGraphics.UavBarrier( arena.DepthArguments );
\t\t\t\t\tGraphics.ResourceBarrierTransition( arena.DepthRanges, ResourceState.GenericRead );
\t\t\t\t\tGraphics.ResourceBarrierTransition( arena.DepthArguments, ResourceState.IndirectArgument );
\t\t\t\t\tGraphics.ResourceBarrierTransition( arena.Vertices, ResourceState.GenericRead );
\t\t\t\t\tGraphics.ResourceBarrierTransition( arena.Indices, ResourceState.GenericRead );
\t\t\t\t\tAttributes.Set( "DepthRanges", arena.DepthRanges );
\t\t\t\t\tAttributes.Set( "DepthVertices", arena.Vertices );
\t\t\t\t\tAttributes.Set( "DepthIndices", arena.Indices );
\t\t\t\t\tGraphics.DrawModelInstancedIndirect( _triangle, arena.DepthArguments, 0, Attributes );
\t\t\t\t}
\t\t\t}
\t\t}
\t}

'''+s[end:]
# Use existing four-uint storage layout; no vector integer API assumption.
s=s.replace('GpuBuffer<Vector4Int> DepthRanges','GpuBuffer<Vector4> DepthRanges')
p.write_text(s)
# Restore the user's original forward shader and share only its canonical normal decode.
p=Path('Assets/shaders/voxels/voxel_terrain.shader')
s=Path('.codex/tmp/terrain-shadows-original/voxel_terrain.shader').read_text()
start=s.index('\tfloat3 DecodeTerrainNormal(')
end=s.index('\n\tPixelInput MainVs',start)
normal=s[start:end]
Path('Assets/shaders/voxels/voxel_terrain_normal.hlsl').write_text(normal+'\n')
s=s[:start]+'\t#include "voxel_terrain_normal.hlsl"\n'+s[end:]
p.write_text(s)
p=Path('Assets/shaders/voxels/voxel_chunk_visibility_cs.shader')
s=p.read_text(); start=s.index('\tbool IsDefinitelyOutsideFrustum('); end=s.index('\n\t[numthreads',start)
Path('Assets/shaders/voxels/voxel_frustum.hlsl').write_text(s[start:end]+'\n')
p.write_text(s[:start]+'\t#include "voxel_frustum.hlsl"\n'+s[end:])
p=Path('Code/Voxels/Materials/GpuVoxelMaterials.cs'); s=p.read_text().replace('\tpublic GpuBuffer<Vector4> Palette => _palette;\n','')
start=s.index('\tpublic void Bind( Sandbox.Rendering.CommandList.AttributeAccess'); end=s.index('\tpublic void Dispose()',start)
p.write_text(s[:start]+s[end:])
