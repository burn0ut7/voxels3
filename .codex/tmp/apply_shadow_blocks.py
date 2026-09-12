from pathlib import Path
def write(p,s):
    Path(p).write_bytes(s.replace('\r\n','\n').replace('\n','\r\n').encode('utf-8'))
p='Code/Voxels/GpuVoxelMesher.cs';s=Path(p).read_text(encoding='utf-8')
s=s.replace('private const int IndirectArgumentStride = sizeof( uint ) * 5;', '''private const int IndirectArgumentStride = sizeof( uint ) * 5;
\tprivate const int DepthTrianglesPerBlock = 64;
\tprivate const int DepthIndicesPerBlock = DepthTrianglesPerBlock * 3;
\tprivate const int DepthBlockCapacity = (IndexArenaCapacity + DepthIndicesPerBlock - 1) / DepthIndicesPerBlock + RegionsPerSlab;''')
s=s.replace('GpuBuffer<Vector4> DepthRanges { get; } = new( RegionsPerSlab, GpuBuffer.UsageFlags.Structured, "Voxel Depth Triangle Ranges" )','GpuBuffer<Vector4> DepthBlocks { get; } = new( DepthBlockCapacity, GpuBuffer.UsageFlags.Structured, "Voxel Depth Blocks" )')
s=s.replace('DepthRanges.Dispose()', 'DepthBlocks.Dispose()')
s=s.replace('private readonly Model _triangle;', 'private readonly Model _block;')
s=s.replace('''\t\t\t// The three driver vertices are replaced by a canonical terrain triangle in the shader.
\t\t\tvar mesh = new Mesh( Material.FromShader( "shaders/voxels/voxel_terrain_depth.shader" ) );
\t\t\tmesh.CreateVertexBuffer<TerrainVertex>( 3, new TerrainVertex[3].AsSpan() );
\t\t\tmesh.CreateIndexBuffer( 3, new int[] { 0, 1, 2 }.AsSpan() );
\t\t\t_triangle = Model.Builder.AddMesh( mesh ).Create();''','''\t\t\t// Each instance expands a contiguous block of canonical terrain triangle indices.
\t\t\tvar mesh = new Mesh( Material.FromShader( "shaders/voxels/voxel_terrain_depth.shader" ) );
\t\t\tmesh.CreateVertexBuffer<TerrainVertex>( DepthIndicesPerBlock, new TerrainVertex[DepthIndicesPerBlock].AsSpan() );
\t\t\tvar indices = new int[DepthIndicesPerBlock];
\t\t\tfor ( var index = 0; index < indices.Length; index++ ) indices[index] = index;
\t\t\tmesh.CreateIndexBuffer( indices.Length, indices.AsSpan() );
\t\t\t_block = Model.Builder.AddMesh( mesh ).Create();
\t\t\t_visibility.Attributes.Set( "DepthIndicesPerBlock", DepthIndicesPerBlock );''')
s=s.replace('"DepthRanges", arena.DepthRanges','"DepthBlocks", arena.DepthBlocks').replace('arena.DepthRanges','arena.DepthBlocks')
s=s.replace('Graphics.DrawModelInstancedIndirect( _triangle,','Graphics.DrawModelInstancedIndirect( _block,')
assert '_triangle' not in s and 'DepthRanges' not in s
write(p,s)
p='Assets/shaders/voxels/voxel_depth_visibility_cs.shader';s=Path(p).read_text(encoding='utf-8')
s=s.replace('DepthRanges','DepthBlocks').replace('TriangleEnds','BlockEnds')
s=s.replace('uint DepthSlotOffset < Attribute( "DepthSlotOffset" ); >;','uint DepthSlotOffset < Attribute( "DepthSlotOffset" ); >;\n\tuint DepthIndicesPerBlock < Attribute( "DepthIndicesPerBlock" ); >;')
s=s.replace('BlockEnds[lane] = visible ? source.IndexCount / 3 : 0;', 'uint blockCount = visible ? (source.IndexCount + DepthIndicesPerBlock - 1) / DepthIndicesPerBlock : 0;\n\t\tBlockEnds[lane] = blockCount;')
s=s.replace('DepthBlocks[lane] = uint4( BlockEnds[lane], source.FirstIndex, source.BaseVertex, 0 );','''uint firstBlock = BlockEnds[lane] - blockCount;
\t\tfor ( uint block = 0; block < blockCount; block++ )
\t\t{
\t\t\tuint indexOffset = block * DepthIndicesPerBlock;
\t\t\tDepthBlocks[firstBlock + block] = uint4( source.FirstIndex + indexOffset,
\t\t\t\tsource.BaseVertex, min( DepthIndicesPerBlock, source.IndexCount - indexOffset ), 0 );
\t\t}''')
s=s.replace('draw.IndexCount = 3;', 'draw.IndexCount = DepthIndicesPerBlock;')
write(p,s)
p='Assets/shaders/voxels/voxel_terrain_depth.shader';s=Path(p).read_text(encoding='utf-8').replace('DepthRanges','DepthBlocks')
start=s.index('\t\tuint low = 0;');end=s.index('\t\tfloat3 position = ',start)
s=s[:start]+'''\t\tuint4 block = DepthBlocks[input.InstanceId];
\t\tif ( input.VertexId >= block.z )
\t\t{
\t\t\t// Padding belongs to the final partial block, never to terrain geometry.
\t\t\tPixelInput clipped = (PixelInput)0;
\t\t\tclipped.vPositionPs = float4( 2.0, 2.0, 2.0, 1.0 );
\t\t\treturn clipped;
\t\t}
\t\tuint index = DepthIndices[block.x + input.VertexId];
\t\tTerrainVertex vertex = DepthVertices[block.y + index];
'''+s[end:]
write(p,s)
