# GPU Voxel Meshing

## Production Slice

LOD0, LOD1, and one fixed regular LOD2 Transvoxel level are the terrain render levels. The authoritative
world remains the implicit SDF represented by `VoxelChunk`; indexed meshes are
derived, GPU-resident, revisioned, disposable caches. Generator version 5 owns
the exterior surface, noodle tunnels, and cheese caverns. A dedicated transition
cache closes only the fixed LOD0-to-LOD1 2:1 interface. The LOD1-to-LOD2 seam is
intentionally visible in this proof. This slice excludes LOD3, morphing,
generalized level hierarchies, collision, edits, networking, generator changes,
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
- the three existing transient `GpuTerrainScratch` lanes plus one isolated LOD2 lane;
- persistent indexed geometry arenas and their CPU range allocators;
- coordinate-to-resident-geometry and candidate replacement state;
- shared compute shaders, indexed-indirect draw resources, and visibility data.

## Fixed Independent LOD2 Cache

Placement uses immutable private proof inputs whose responsibilities remain
separate: authoritative gameplay radius, LOD0 visual extent and LOD cutovers,
fixed per-level cache extents, and maximum enabled visual level. They are not
runtime settings. Their defaults keep LOD0 at `32^3 @ 16` with gameplay and
visual half-extent `4`, LOD1 at `32^3 @ 32` with cache half-extent `8` and hole
half-extent `2`, and enable LOD2 at `32^3 @ 64` with cache half-extent `8` and
nominal hole half-extent `4`. Future view-distance presets can omit outer levels
without enlarging the authoritative LOD0 gameplay volume.

LOD2 snaps directly from the current streaming target to its own 2,048-unit
region grid. Its desired cache is the half-open cube `[anchor-8, anchor+8)`, or
4,096 identities. The current LOD1 outer world box, including the LOD0-filled
center, defines near-field coverage. An LOD2 region is inactive only when its
entire world AABB is contained by that box; partial intersections remain active
to prevent holes. Aligned anchors therefore exclude 512 regions and leave 3,584
active. Offset anchors retain accepted overlap.

The LOD0/LOD1/transition placement path runs only when the LOD1 anchor changes.
LOD2 placement runs independently when either its own anchor or the LOD1
coverage boundary changes. Each level owns its desired, active, entering,
leaving, resident, pending, and digest state. Leaving LOD2 identities are removed
immediately; retained residents and arena ranges remain unchanged. There is no
hierarchy-wide pending state, catch-up center, transaction, settlement barrier,
or cross-level publication gate.

LOD2 uses the canonical GPU SDF and unchanged regular-mesh compute contract. It
has one private at-most-eight-region scratch lane and queue, but publishes through
the same region-local candidate validation, shared CPU range allocator, arenas,
visibility path, and indexed-indirect drawing. Existing regular dequeue order
remains Gameplay, LOD1, Warm, followed by the existing transition decision.
LOD2 advances opportunistically only when neither foreground path submits work.
LOD2 count batches begin only on those foreground-idle ticks. After 250 ms
without eligible service, an already-started LOD2 count phase may advance after
that tick's foreground work; it never consumes or replaces a foreground lane or
stacks a second full count submission behind one. Level-aware descriptor
validation cancels stale or superseded work.

Foreground settlement, movement, publication, and performance-window progression
continue to depend only on existing gameplay, LOD1, warm, and transition work.
LOD2 converges independently and cannot hold that boundary. Schema 14 appends
LOD2 placement, residency, visibility, digest, queue, latency, cancellation, and
service-gap telemetry without changing existing field meanings.

Placement diagnostics read this owned state without affecting convergence.
`voxel_lod_info` emits an immediate structured snapshot of the streaming target,
LOD0 gameplay and visual boxes, the LOD1 outer box and hole, the LOD0-to-LOD1
transition state, and the LOD2 outer box, nominal hole, effective near-coverage
exclusion, and residency counts. Region and world bounds are reported as
half-open intervals. Enabling the existing `VerboseLogging` property emits the
same snapshot only when a placement boundary changes; it does not add per-frame
logging or a second placement model.

## Fixed LOD0/LOD1 Transition

Clipbox placement owns where the boundary exists; Transvoxel owns its geometry.
For each of the 16 LOD1 regions on each of the six faces of the LOD0 hole, the
manager derives a separate `(Lod1Coordinate, Face)` identity. The face direction
points from its owning LOD1 region toward the hole. The 96 desired identities are
diffed when the snapped LOD1 anchor changes, so retained faces keep their
generation and allocation while only entering and leaving pieces are scheduled
or removed.

A transition is a thin, zero-width face-local volume-cell mesh, not a heightfield
trim, a partial coarse block, or a modification of either regular mesh. Its
`32x32` transition cells sample a compact five-offset halo from the same canonical
SDF at LOD0 spacing: `69x69` on the interface plane, `65x65` on each fine normal
offset, and `33x33` on each coarse normal offset. This retains every classification,
interpolation, and fine/coarse gradient sample while avoiding unused off-plane
positions. The official 512 transition cases, 56 geometry classes, inversion
bit, vertex reuse data, and triangulations produce ordinary indexed triangle
lists. Fine-layer intersections use LOD0-equivalent interpolation and gradients;
coarse-layer intersections use LOD1-equivalent interpolation and gradients.

Transitions have three independent scratch lanes and isolated pending,
in-flight, cancellation, and resident state. Regular work always schedules and
consumes first. Transition GPU work uses ticks where regular meshing submitted no
GPU work and advances at most one existing batch phase per tick. Coherent SDF
sampling, classification/scan/audit plus count readback, and emission are separate
phases of the same at-most-eight-face batch, preventing their costs from stacking
in one frame without changing transition identity or publication. Count readback
contains bounded scalar metadata only. Exact allocations use the unchanged shared-arena range allocator. Final
vertex and index stages in the same transition compute resource write the existing
24-byte vertex and 32-bit index formats. Packed audit counters keep that resource
at s&box's 16-storage-buffer limit; valid dummy output descriptors remain bound
during count stages and are replaced with arena buffers only for emission.

Publication is face-local. A ready face becomes visible without waiting for the
other 95 faces and never participates in clipbox settlement or regular-mesh
publication. A dirty resident remains visible until its validated replacement
publishes. Removed or superseded work releases candidates and cannot publish a
stale callback; empty faces complete without arena ranges. Visibility uses a
conservative one-fine-cell-expanded face slab through the existing GPU path.

GPU diagnostics compare transition fine and coarse contour intersections against
the corresponding regular-grid sign-changing edges. Each face also reports four
world-position boundary digests; bounded CPU metadata comparison counts unmatched
edges between adjacent face pieces. Production results include a bounded record
for every resident face: key, generation, arena ranges, latency, counts, digests,
and audit counters. No geometry is read back for these audits.

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

After the moving loop, the player stops and the manager waits for the unchanged
foreground queues and lanes to settle; LOD2 does not hold this boundary. After two further render-sequence advances it
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
- Sharing an existing regular scratch lane would degrade accepted near-field
  responsiveness; coupling anchors or adding hierarchy publication would let
  stale outer work block newer movement.
- CPU density fields or coarse voxel buffers would violate the canonical GPU SDF
  contract. A generalized N-level hierarchy and an LOD1-to-LOD2 transition are
  premature for this bounded proof.
