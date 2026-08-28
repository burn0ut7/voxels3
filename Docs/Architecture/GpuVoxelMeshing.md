# GPU Voxel Meshing

## Production Slice

LOD0 regular-cell Transvoxel is the sole terrain render path. The authoritative
world remains the implicit SDF represented by `VoxelChunk`; mesh data is derived,
GPU-resident, and disposable. This slice deliberately excludes transition cells,
other LODs, clipboxes, collision, edits, networking, procedural noise, and a
general mesh allocator.

The implementation uses Eric Lengyel's official regular-cell tables from the
Transvoxel repository at commit
`51a494f03c5b024cd153b596bcc7152eb3cc93a6`. Only `regularCellClass`,
`regularCellData`, and `regularVertexData` are included. Their MIT license and
attribution are stored beside the shader tables.

## Ownership and Data Flow

`VoxelManager` owns authoritative loaded chunks and constructs one
`GpuVoxelMesher`. A `VoxelChunk` never owns an engine resource. On the main
thread, the manager classifies each integrated chunk from its exact density
bounds. `MaximumDensity <= 0` is completely solid and `MinimumDensity > 0` is
completely air; neither state allocates a mesh resource or dispatches compute.
All other descriptors enter the GPU path conservatively.

The immutable GPU descriptor contains only chunk coordinate, cells per axis,
cell size, surface height, and source revision. Compute evaluates the field via
the shared `voxel_sdf.hlsl` boundary. It derives every cell corner from an
integer global sample coordinate, and sets a case bit for `density <= 0`. This
preserves the authoritative negative-solid, positive-air, zero-solid convention
and makes samples on adjacent chunk faces bit-identical.

One compute thread classifies one regular cell. Cases 0 and 255 emit nothing.
Every other cell appends one `uint`: bits 0 through 5 store local X, bits 6
through 11 store local Y, bits 12 through 17 store local Z, bits 18 through 20
store the triangle count, bits 21 through 23 are unused, and bits 24 through 31
store the case index. Six bits per coordinate preserve the supported maximum of
64 cells per axis. Official topology data supplies logical triangle counts and
draw-time vertex expansion; no CPU mesher, CPU SDF array, geometry upload, or
geometry readback exists.

The append counter is copied by GPU queue order into
`IndirectDrawArguments.InstanceCount`. `VertexCount` is fixed at 15 so each
active-cell instance can represent five triangles. Slots beyond the packed
triangle count return the same constant out-of-clip position before topology
lookup, coordinate arithmetic, SDF sampling, interpolation, or gradient work;
each padded triangle is therefore degenerate and clipped. The vertex shader
decodes local XYZ directly and interpolates chunk-local positions with
`saturate(d0 / (d0 - d1))`. It evaluates central-difference gradients at half a
cell in world space, normalizes them from solid toward air, and adds the chunk
origin only for rendering.

Installed s&box 26.08.19 exposes indirect drawing publicly through a
camera-attached `Sandbox.Rendering.CommandList`; the corresponding
`Graphics.DrawInstancedIndirect` entry point is internal and cannot be called by
project `SceneCustomObject` code. The manager therefore owns one named compute
command list and one persistent draw command list containing one indirect draw
per resident surface chunk. The compute list executes first in the same camera
stage, preserving reset, dispatch, counter-copy, and draw queue order. This is
the canonical public engine path and does not change buffer or chunk ownership.

## Scheduling and Lifetime

The mesher owns a coordinate-to-resource map, nearest-first pending queue,
shared compute shader and material, persistent compute/draw command lists, and a
pool of same-capacity resource sets. Each resource owns:

- one append buffer with exactly `CellsPerAxis^3` four-byte records;
- one four-word indirect argument buffer;
- persistent draw attributes.

Normal meshing allocates, clears, binds, and writes no statistics buffer. A
resource inspected through `inspect_gpu_mesh` lazily acquires one three-word
diagnostic buffer from its own lifetime. The first word receives the append
counter, while the diagnostic pass accumulates logical triangles and invalid
gradients into the other two words. Uninspected resources therefore carry no
diagnostic allocation.

At 32 cells per axis, each active-cell buffer is 131,072 bytes. The fixed
production radius contains 1,089 possible surface chunks, for 142,737,408 bytes
(136.125 MiB) of logical active-cell capacity before argument/stat buffers.
Capacity cannot overflow because one invocation can append at most one record
for each of the exactly `CellsPerAxis^3` invocations.

The main thread integrates authoritative chunks under its existing time budget
and records at most eight mesh dispatches per update or async load yield.
Retained descriptors leave their buffers untouched. Unload removes their draw
and returns resources to the pool. A source revision change replaces only a
stale coordinate; a dimension change clears resident state and disposes the
incompatible pool. Compute counter reset, dispatch, counter copy, and subsequent
draw depend only on GPU queue ordering, without fences or CPU synchronization.

Normal production never invokes `GetData` or `GetDataAsync` for active-cell
geometry. The bounded `inspect_gpu_mesh(x,y,z)` editor diagnostic schedules a
separate compute pass over the actual resident active-cell stream. That pass
decodes the packed triangle count and samples the center gradient only on
request; it does not reclassify cells or mutate the append stream. After GPU
queue ordering makes the results available, the diagnostic may asynchronously
read the indirect arguments and three-word scalar buffer.
Scalar and geometry readbacks are counted separately; the geometry count is a
constant zero. Performance records add mesh residency, backlog, capacity,
dispatch, pool, allocation, and readback metrics. The allocation field is null
when s&box's runtime whitelist prevents a scoped managed-byte counter; production
allocation validation then uses the engine allocation profiler. Dedicated GPU
meshing timing is recorded as unavailable when the installed profiler does not
expose a stable named compute entry rather than being inferred from CPU time.

## Alternatives Rejected

- Explicit GPU vertex buffers multiply output bandwidth and storage for data
  already recoverable from the active cell and official topology. Draw-time
  expansion is smaller and sufficient for the first measured slice.
- Prefix scans and indexed compaction add dispatches, scratch buffers, and
  synchronization without evidence that append output is insufficient.
- A global arena or variable-size allocator adds fragmentation and lifetime
  policy before varied output types exist. Equal fixed chunk capacity makes a
  simple pool exact and bounded.
- CPU topology decoding, a reference mesher, or CPU density arrays would create
  a second geometry/data path and violate the authoritative boundary.
- Counter or geometry readback to determine draw size introduces a GPU/CPU
  round trip. GPU counter copy plus indirect drawing makes it unnecessary.
- Rebuilding meshes every frame discards their derived-cache value. Resident
  buffers persist until their coordinate unloads or source revision changes.
