# GPU Voxel Meshing

## Production Slice

LOD0 regular-cell Transvoxel is the sole terrain render path. The authoritative
world remains the implicit SDF represented by `VoxelChunk`; mesh data is derived,
GPU-resident, and disposable. Deterministic surface generator version 2 now
supplies the field. This slice deliberately excludes transition cells, other
LODs, clipboxes, collision, edits, networking, generator optimization, and a
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
cell size, immutable procedural settings, backend generator version, and source revision. Compute evaluates the field via
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

The append counter is copied by GPU queue order into both the resource's bounded
inspection arguments and the resource's stable slot in a shared source-argument
buffer. `VertexCount` is fixed at 15 so each active-cell instance can represent
five triangles. Slots beyond the packed triangle count return the same constant
out-of-clip position before topology lookup, coordinate arithmetic, SDF
sampling, interpolation, or gradient work; each padded triangle is therefore
degenerate and clipped. The vertex shader decodes local XYZ directly and
interpolates chunk-local positions with `saturate(d0 / (d0 - d1))`. It evaluates
central-difference gradients at half a cell in world space, normalizes them from
solid toward air, and adds the chunk origin only for rendering.

Installed s&box 26.08.19 exposes indirect drawing publicly through a
camera-attached `Sandbox.Rendering.CommandList`; the corresponding immediate
`Graphics.DrawInstancedIndirect` entry point is internal and cannot be called by
project `SceneCustomObject` code. It also exposes no public GPU-produced
`DrawIndirectCount` path. The mesher therefore owns one mesh command list and
one persistent draw command list containing one indirect entry per resident
surface mesh. Entries whose GPU-written instance count is zero remain present
but emit no terrain geometry. `DrawInstancedIndirect` addresses an argument by
16-byte element index, while `CopyStructureCount` addresses the instance-count
field by byte offset; these units must not be interchanged.

Frustum visibility is a rendering-only derivative owned by the same mesher. A
pooled mesh resource has one stable slot for its entire lifetime. The slot stores
an active flag, a full three-dimensional world-space chunk AABB expanded by one
cell on every axis, its unculled active-cell count, and its GPU-written visible
arguments. A result enters the active set only after the existing in-flight
coordinate/revision checks accept it. Removal first marks the slot inactive;
logical chunks, SDF state, active-cell geometry, revisions, and pooled resources
are otherwise unchanged. Stale or cancelled results never activate a slot.

Each pooled mesh resource owns a tiny structured SDF-parameter buffer containing
its chunk origin, cell size, cells per axis, seed, surface base height, frequency,
and amplitude. Both compute classification and draw-time interpolation bind that
same immutable resource. This keeps batched dispatches from observing scalar
attributes written for a later chunk and prevents duplicated terrain planes.
The mesh command list runs at `AfterDepthPrepass/-100` and supplies source counts
without CPU readback. One persistent camera-attached terrain command list runs
at `AfterOpaque/0`: it binds visibility resources, clears counters,
dispatches visibility against that camera stage's `g_matWorldToProjection`,
applies UAV and indirect-argument barriers, optionally aggregates bounded
performance counters, and immediately issues the terrain indirect draws. The
compute and draws therefore consume one deterministic same-frame camera state.
The always-rendered `SceneCustomObject` owns only render-sequence tracking and
asynchronous diagnostics; visibility never passes through its callback.

One compute thread classifies one allocated slot by transforming all eight AABB
corners to homogeneous clip space. It culls only when every corner lies outside
the same frustum plane. The lateral clip planes are widened to `1.10w`, while
the depth planes remain exact. This static guard keeps near-edge terrain
drawable without temporal visibility history. A non-finite transform, near-zero
homogeneous W, or any other ambiguous case is visible. The shader never samples density or reasons
about height, neighboring chunks, enclosure, streaming distance, caves, cliffs,
floating terrain, or overhangs. This conservative contract may draw false
positives but must not create false negatives.

## Scheduling and Lifetime

The mesher owns a coordinate-to-resource map, nearest-first pending queue,
shared compute/visibility shaders and material, persistent mesh/draw command
lists, shared visibility buffers, and a pool of same-capacity resource sets.
Each resource owns:

- one append buffer with exactly `CellsPerAxis^3` four-byte records;
- one four-word indirect argument buffer;
- one stable slot in the mesher-owned visibility buffers;
- persistent draw attributes.

Visibility capacity grows geometrically with pool allocation. Replacing shared
buffers rebuilds the persistent draws and refreshes active descriptors and
source counts. Replaced buffers remain alive until mesher disposal because an
already queued camera command list can still reference them. This is bounded by
the small number of geometric growth events and avoids introducing a second
resource owner or a fixed terrain-shape limit. At the production capacity of
2,048 slots, visibility bounds, source/visible arguments, frame counters, and
aggregate counters total `131,100` logical bytes.

Mesh resources contain only production active-cell and indirect-argument data.
There is no diagnostic statistics buffer, inspection queue, or second compute
shader that reclassifies production cells.

At 32 cells per axis, each active-cell buffer is 131,072 bytes. The fixed
production radius contains 1,089 possible surface chunks, for 142,737,408 bytes
(136.125 MiB) of logical active-cell capacity before argument/stat buffers.
Capacity cannot overflow because one invocation can append at most one record
for each of the exactly `CellsPerAxis^3` invocations.

The main thread integrates authoritative chunks under its existing time budget
and records at most eight mesh dispatches per update. Initial component loading
waits only for the authoritative chunk stream to integrate; it does not wait for
derived GPU meshes. Camera render callbacks do not execute until component load
has completed, so making `OnLoad` wait for GPU mesh completion creates a circular
startup dependency. Once `OnStart` has accepted the authoritative load, the
normal update/render path drains the mesh queue at the same bounded rate.
Retained descriptors leave their buffers untouched. A fixed one-chunk render
warm shell surrounds the authoritative gameplay cube. `VoxelManager` generates
it on a separate cancellable worker queue. Before constructing a transient
chunk, the canonical authoritative SDF range contract classifies the complete
chunk as definitely solid, definitely air, or potentially surface-containing.
Only the potential result constructs a chunk and reaches GPU scheduling; the
other results are marked render-prepared without a mesh. Generator v2 bounds its
single-octave simplex surface using base height plus/minus configured amplitude;
any future contribution whose complete range is not proven must return
potentially surface-containing.
This contract is conservative full-volume reasoning, not a heightfield or
surface-band heuristic. Warm coordinates never enter the authoritative loaded
dictionary.
Gameplay mesh requests drain before warm requests under the same eight-dispatch
total limit. Promotion or demotion retains the mesh resource and visibility
slot. Leaving the combined render region returns the resource to the pool.
Stream-request revisions cancel jobs independently from the terrain-content
revision that determines whether geometry is stale. A content revision change replaces only a
stale coordinate; a dimension change clears resident state and disposes the
incompatible pool. Compute counter reset, dispatch, counter copy, and subsequent
draw depend only on GPU queue ordering, without fences or CPU synchronization.

Normal production never invokes `GetData` or `GetDataAsync` for active-cell
geometry, and no project diagnostic does so either. Geometry readbacks remain a
constant zero. Normal rendering performs no visibility readback. During an
opted-in performance run, the shader accumulates sample/resident/warm/visible/minimum/
maximum counters entirely on GPU. After the measured loop stops, saving waits
for the route's final derived mesh backlog to reach zero, captures settled
non-empty surface/warm counts plus total and maximum active cells from the same
indirect arguments, and completes one asynchronous ten-word scalar readback.
Saving also
waits for the route's final derived mesh backlog to reach zero so its residency
snapshot records settled availability without extending the measured frame
window. Gameplay and rendering never consume the readback. Performance records add mesh residency, backlog,
capacity, dispatch, pool, allocation, readback, visible/resident draw, and
cull-percentage metrics. CPU-side result assembly derives average active cells,
total allocated-buffer record capacity, and utilization from those scalars; no
per-chunk data is read back. The allocation field is null
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
- The former diagnostic-only mesh compute shader and scalar inspection readback
  were removed because they formed a second executable classification path.
  Mesh validation now observes production rendering and residency instead.
- Counter or geometry readback to determine draw size introduces a GPU/CPU
  round trip. GPU counter copy plus indirect drawing makes it unnecessary.
- Per-chunk CPU frustum tests and CPU-compacted indirect uploads would add
  recurring CPU work and make CPU visibility part of draw ownership. The nearby
  `voxels2` project uses that pattern, but it is deliberately not copied here.
- Enlarging authoritative gameplay residency would make render latency change
  terrain existence, edits, multiplayer, and later collision. The warm shell is
  independently disposable derived data and cannot become world truth.
- Temporal visibility history adds delayed camera state. Same-command
  sequencing plus a static conservative guard solves this slice without it.
- A global mesh arena or GPU variable-size allocator would replace the existing
  fixed-capacity resource pool and broaden this rendering-only slice into mesh
  ownership and allocation policy.
- Hi-Z occlusion, hierarchical bounds, clipboxes, and LOD selection answer
  separate questions and require their own measured designs. The visible
  argument buffer is the extension boundary for adding later predicates without
  changing mesh or terrain ownership.
- Accumulating triangle and gradient statistics in every production dispatch
  charged all remeshes for information consumed only by an explicit inspector.
  The selected lazy pass moves that work and allocation out of normal meshing.
- Reading the append buffer as a compute SRV was rejected on installed s&box
  26.08.19 because the counter copy succeeded but the compute pass did not
  expose appended records. Reclassification remains diagnostic-only and shares
  the production SDF/case function rather than duplicating that contract.
- Rebuilding meshes every frame discards their derived-cache value. Resident
  buffers persist until their coordinate unloads or source revision changes.
