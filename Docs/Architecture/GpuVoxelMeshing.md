# GPU Voxel Meshing

## Production Slice

LOD0 regular-cell Transvoxel is the sole terrain render path. The authoritative
world remains the implicit SDF represented by `VoxelChunk`; indexed meshes are
derived, GPU-resident, revisioned, disposable caches. Generator version 5 owns
the exterior surface, noodle tunnels, and cheese caverns. This slice excludes
transition cells, other LODs, collision, edits, networking, generator changes,
and allocator redesign.

Logical chunks are streaming, SDF-input, and revision units, not GPU allocation
or draw-call units. Persistent geometry lives in shared arenas. Each arena owns
a 32 MiB vertex buffer, a 16 MiB index buffer, and 256 indexed-indirect records.
The existing CPU range allocator owns exact contiguous ranges; released ranges
coalesce and live ranges do not move.

## Ownership and Data Flow

`VoxelManager` owns authoritative loaded chunks and one `GpuVoxelMesher`.
`VoxelChunk` owns no engine resource. The manager conservatively rejects chunks
whose exact density range proves them entirely solid or air. Potential surface
chunks enter the mesher as immutable coordinate, dimension, generator-setting,
and source-revision descriptors.

The mesher owns:

- gameplay-first pending queues and coordinate/revision validation;
- three independent transient `GpuTerrainScratch` lanes;
- persistent indexed geometry arenas and their CPU range allocators;
- coordinate-to-resident-geometry and candidate replacement state;
- shared compute shaders, indexed-indirect draw resources, and visibility data.

A remesh evaluates the canonical `voxel_sdf_v5.hlsl` field once into a haloed
`35^3` density lattice, classifies the `32^3` regular cells from cached corners,
counts compact region-local edge vertices and indices, and scans those counts.
The count stage returns only bounded metadata to the CPU. It never returns
density, vertices, indices, or other geometry.

After metadata readback, the existing CPU allocator reserves exact vertex and
index ranges. The emit stage writes 24-byte position/normal vertices, 32-bit
indices, and indexed-indirect arguments directly into persistent arena buffers.
Ordinary drawing reads only persistent position, normal, index, visibility, and
indirect-argument buffers. It never evaluates the procedural SDF.

The installed s&box 26.08.19 API exposes public indexed-indirect drawing,
indirect dispatch, append counters, asynchronous buffer readback, and multiple
independent GPU buffers. It does not expose a clean public GPU-owned
variable-range allocation path. Exact CPU range allocation after count metadata
therefore remains the canonical dependency; GPU-driven allocation is a separate
future decision.

## Scratch Pipeline

Count batches contain at most eight regions. A render tick admits at most one
new count batch and consumes at most one count-ready batch for allocation and
emit. The batch size and dispatch shape are unchanged.

Three independent lanes overlap unrelated batch chains. Each lane owns its
requests, count results, candidate allocations, timestamps, and lifecycle:

`Idle -> CountPending -> CountReady -> EmitSubmitted -> Published`

The count callback only records readiness. A later render tick validates the
requests, performs exact CPU allocation, and submits emit work. The main update
publishes a candidate only after emit has crossed its required render-sequence
boundary. A lane cannot reuse scratch resources before that boundary.

This overlap targets count/readback latency without creating a larger
monolithic dispatch. It does not raise the eight-region batch limit, change the
player route, enlarge terrain residency, reduce SDF work, or move allocation
ownership to the GPU.

## Priority, Revision, and Replacement

Gameplay requests always dequeue before warm requests when forming a batch.
Promotion and demotion retain resident geometry. Warm regions remain disposable
render derivatives and never enter authoritative loaded-chunk ownership.

Coordinate membership, stream revision, content revision, cancellation, and
supersession checks occur before count submission, before allocation/emit, and
before publication. Stale candidates release any reserved ranges and never
replace visible geometry. A replacement always targets new candidate ranges;
the previous revision remains visible until successful publication atomically
changes that coordinate's resident record. Only then are old ranges released.
Empty results follow the same revision lifecycle without consuming an arena.

Dimension or generator-configuration changes clear incompatible resident and
in-flight derived state. Terrain authority remains the canonical procedural SDF
and future edits invalidate only affected coordinate revisions.

## Persistent Drawing and Visibility

One persistent camera-attached terrain command list issues indexed-indirect
drawing per active shared arena. Visibility is a conservative GPU derivative of
resident region bounds. It may retain false positives but must not create false
negatives. Removal marks the resident record inactive; stale candidates never
enter visibility state.

Normal production performs zero geometry readbacks and zero ordinary-render SDF
evaluations. Performance diagnostics may perform bounded scalar readbacks for
visibility and settled aggregate counts; gameplay and rendering never consume
those diagnostics.

## Performance Contract

Schema 10 measures the unchanged moving figure-eight separately from settled
persistent rendering. The moving window records:

- regions scheduled, count-submitted, and published per second;
- batches submitted/completed per second and occupancy distribution;
- count submission, submit-to-readback callback, callback-to-consumption, CPU
  allocation, emit submission, and emit-to-publication distributions;
- gameplay, warm, and total queue distributions and post-loop drain time;
- schedule-to-renderable latency, cancellations, and supersessions;
- direct player-route distance travelled between schedule and publication in
  world units and 512-unit chunks;
- frame, GPU, memory, arena, readback, visibility, and correctness metrics.

After the moving loop, the player stops and the manager waits for all queues and
all scratch lanes to settle. After two further render-sequence advances it
measures a fixed 10-second stationary window using the same production terrain.
Stationary FPS, CPU tails, GPU distribution, memory, visibility, and settled
geometry are stored separately in the same result.

## Alternatives Outside This Slice

- Enlarging batches or merely increasing the fixed admission count risks frame
  spikes without addressing serialization.
- GPU-owned variable-range allocation changes allocator ownership and requires
  a separate API-backed design and measurement.
- Changing cave complexity, movement speed, load radius, LOD, collision,
  networking, deformation, or authority would invalidate this throughput
  comparison.
- CPU density arrays, CPU topology decoding, or geometry readback would create
  a second terrain/geometry path.
- Rebuilding meshes every frame discards their persistent-cache value.
