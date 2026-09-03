# GPU Voxel Meshing

## Production Slice

LOD0, LOD1, and LOD2 are the terrain render levels. The authoritative
world remains the implicit SDF represented by `VoxelChunk`; indexed meshes are
derived, GPU-resident, revisioned, disposable caches. Generator version 5 owns
the exterior surface, noodle tunnels, and cheese caverns. One level-pair-aware
transition cache closes both fixed 2:1 interfaces. This slice excludes LOD3,
morphing,
generalized level hierarchies, collision, edits, networking, generator changes,
and allocator redesign.

Logical chunks are streaming, SDF-input, and revision units, not GPU allocation
or draw-call units. Persistent geometry lives in shared arenas. Each arena owns
a 32 MiB vertex buffer, a 16 MiB index buffer, and 512 indexed-indirect records.
The existing CPU range allocator owns exact contiguous ranges; released ranges
coalesce and live ranges do not move.

The record page is a project sizing choice rather than an engine limit. It was
raised from 256 to 512 after atomic handoff measurements showed arenas exhausting
record slots at only `16..36%` vertex and `30..72%` index utilization. The fixed
vertex/index byte capacities, packed record identity, dynamic visibility capacity,
and one indexed-indirect submission per active arena are unchanged. At the
production resident count this retains the same 4096-slot visibility capacity
while reducing geometry arenas and draw submissions.

## Ownership and Data Flow

`VoxelManager` owns authoritative loaded chunks and one `GpuVoxelMesher`.
`VoxelChunk` owns no engine resource. The manager conservatively rejects chunks
whose exact density range proves them entirely solid or air. Potential surface
chunks enter the mesher as immutable coordinate, dimension, generator-setting,
and source-revision descriptors.

The mesher owns:

- gameplay-first pending queues and coordinate/revision validation;
- three transient `GpuTerrainScratch` lanes for regular terrain batches;
- persistent indexed geometry arenas and their CPU range allocators;
- coordinate-to-resident-geometry and candidate replacement state;
- shared compute shaders, indexed-indirect draw resources, and visibility data.

## Canonical Fixed Three-Level Placement

The fixed three-level hierarchy keeps one near-field anchor and one aligned outer
anchor. The LOD0 visual box and LOD1 inner hole follow the nearest LOD1-region
anchor at the existing 1024-unit cadence. The LOD1 outer cache follows the LOD2
anchor expressed in LOD1 coordinates, so every outer LOD1 face lies on a complete
LOD2-region boundary. The LOD2 hole is exactly the LOD1 outer box converted to
LOD2 coordinates. It is neither an independently snapped approximation nor a
whole-region containment test. At the production dimensions this preserves the
existing 4096-region LOD1 cache and adds a 4096-region LOD2 cache with an exact
8x8x8 hole: 3584 LOD2 regions are active, with no cross-level overlap or gap.

`VoxelManager` computes both regular boxes and both transition boundaries in one
placement update. Each transition identity
contains its coarse level, coarse coordinate, and face. The same identity,
descriptor, queue, scratch pipeline, resident cache, allocator, visibility path,
and draw path serve LOD0-to-LOD1 and LOD1-to-LOD2. Near transitions dequeue
before outer transitions, but they are priority classes inside one scheduler
rather than separate meshers.

Regular and transition compute emit their final table-derived primary positions.
The terrain vertex shader only applies the engine's high-precision world offset
and decodes the emitted normal; it performs no LOD-dependent position change.
Each emitted 24-byte vertex stores a finite, bit-exact signature, its stable
arena-record slot, and a two-bit allocation-generation token in `Normal.x` for
explicit geometry audits; `Normal.yz` contain the octahedrally encoded unit
normal. Visibility therefore owns only conservative bounds and indirect draw
arguments. There are no boundary masks, render-space boundary descriptors,
secondary-position eligibility bits, or baked secondary-position paths.

The authoritative input is still the immutable procedural SDF descriptor plus
the manager-owned placement. Mutable placement, scheduling, and publication are
owned by `VoxelManager` and `GpuVoxelMesher`; shader buffers and meshes remain
disposable derivatives. GPU work uses the existing render-tick rendezvous. CPU
work only diffs integer identities, uploads visibility and indirect-draw data,
and consumes bounded count metadata. Every world position, digest, and
transition request is deterministic from the viewer anchor, source revision,
and terrain settings.

The rejected 2026-08-31 proof remains relevant evidence. It added regular
`32^3 @ 64` regions without LOD1-to-LOD2 transitions, retained partial overlap
for offset grids, and accepted a visible seam. It also created a dedicated fourth
regular scratch resource that reproduced native Vulkan page faults during editor
camera ejection. Production LOD2 reuses the established three regular lanes and
does not revive that resource lifetime, containment placement, or publication
path.

Placement diagnostics read owned state without affecting convergence.
`voxel_lod_info` emits an immediate structured snapshot of the streaming target,
the LOD0 gameplay and visual boxes, both LOD1 anchors, the LOD1 outer box and
hole, the exact LOD2 box and hole, and both transition-boundary states. Region
and world bounds are half-open intervals. Enabling the existing `VerboseLogging`
property emits the same snapshot only when a placement boundary changes; it does
not add per-frame logging or a second placement model.

### Atomic Clipbox Handoff

The 2026-09-03 [GPU voxel terrain streaming research](../Research/GpuVoxelTerrainStreaming.md)
informed the implemented requested-versus-resident separation. `VoxelManager`
owns one committed drawable placement, the latest target anchors, and at most one
staged replacement. While the replacement is incomplete, the committed LOD0,
LOD1, LOD2, and transition active sets remain unchanged and continue to cover the
world. A newer viewer target is coalesced without cancelling the in-flight stage;
after commit, the manager immediately stages the latest target if it moved again.

The staged placement uses the same mesher queues and resident caches as ordinary
terrain. Its exact dependencies are newly required LOD0 coordinates, regular
LOD1 and LOD2 descriptors, and transition descriptors. A conservative known-empty
regular result is a resident descriptor with no arena allocation and therefore
satisfies the same readiness contract. Readiness is reconsidered only when the
mesher publishes a resident descriptor or the manager's prepared LOD0 set changes;
settled frames do not rescan the staged sets. Once every dependency is resident,
the manager changes all four active sets, releases leaving residents, and records
the new anchors in one main-thread commit. Preparation records the exact entering,
leaving, activation, and readiness deltas; commit touches only those deltas and
swaps the already-built placement sets instead of scanning or copying the full
clipboxes a second time. The initial bootstrap has no prior coverage to retain
and therefore activates its first placement immediately while that placement
fills through the same production queues.

LOD1 and LOD2 preparation first applies a constant-time conservative vertical
support bound owned by the canonical generator. Regions wholly above the maximum
possible exterior surface are definitely air; regions wholly below both the
minimum exterior surface and maximum cave depth are definitely solid. All
uncertain regions remain potential and enter the GPU mesher. This broad phase
does not sample, approximate, or duplicate the SDF and cannot reject a possible
surface.

This design retains the fixed three-level volumetric SDF, Transvoxel topology,
and single GPU mesher. A second fallback terrain renderer, CPU mesher, generalized
LOD hierarchy, or independently mutable placement model remains rejected.

## Fixed 2:1 Transitions

Clipbox placement owns where the boundary exists; Transvoxel owns its geometry.
For each coarse region on a hole face, the manager derives a separate
`(CoarseLevel, CoarseCoordinate, Face)` identity. The face direction points from
the owning coarse region toward the hole. The inner boundary owns 96 identities;
the outer boundary owns 384. The combined 480-key set is diffed by the one
placement update. Retained faces keep their generation and allocation while
moving anchors replace only the identities that entered or left the boundary.

A transition is a face-local Transvoxel volume-cell mesh, not a heightfield trim
or a partial coarse block. Its `32x32` transition cells sample a compact
five-offset halo from the same canonical SDF at the selected fine spacing:
`69x69` on the
interface plane, `65x65` on each fine normal offset, and `33x33` on each coarse
normal offset. This retains every classification, interpolation, and fine/coarse
gradient sample while avoiding unused off-plane positions. The official 512
transition cases, 56 geometry classes, inversion bit, vertex reuse data, and
triangulations produce ordinary indexed triangle lists. Fine-layer intersections
use the selected fine-level interpolation and gradients; coarse-layer
intersections use the selected coarse-level interpolation and gradients.

Production keeps table-derived primary regular and transition geometry as the
sole final position path. Transition cases set bits for negative-density solid
samples, while the imported Transvoxel class inversion flag assumes the opposite
inside sign. The transition kernel therefore inverts that flag when ordering each
triangle, producing the engine-facing winding required by the material's back-face
culling. Winding is validated directly from both sides of each fixed boundary;
the face-plane transition triangles intentionally do not share the 3D gradient
normal of the surrounding terrain, so those vectors are not a valid winding
oracle.

Git history records two rejected alternatives. The baked fine-region experiment
regenerated mask-changing meshes and produced holes, pinched fans, and warped
sheets. The later coarse-side experiment displaced coarse regular and
half-resolution transition vertices by the coarse cell size; fixed-camera
inspection removed its largest strips but still exposed open boundary gaps. Both
introduced position ownership outside the table-derived topology. A subsequent
fine-side render-space experiment followed the documented Transvoxel
full-resolution ownership but still opened the LOD1-to-LOD2 boundary under the
current face-local zero-width construction. All three deformation paths, their
mesh-rebuild masks, and their render descriptors are absent from the canonical
primary-position design.

This still follows the official Transvoxel classification and topology tables.
The CPU owns clipbox placement, work identity, bounded count readback, and
allocation. The GPU owns SDF sampling, table classification, interpolation,
normals, scans, and final geometry emission. Moving extraction or vertex
generation to the CPU remains rejected because it would duplicate the canonical
GPU production path.

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

Mesher publication remains face-local: each completed face becomes an exact
resident independently and does not wait for the other faces to finish. A face
staged for a future clipbox remains inactive until the manager's whole-placement
readiness gate commits that clipbox; a replacement of an already active identity
retains the previous validated resident until its candidate publishes. Removed or
superseded work releases candidates and cannot publish a stale callback; empty
faces complete without arena ranges. Visibility uses a conservative
one-fine-cell-expanded face slab through the existing GPU path.

GPU diagnostics compare transition fine and coarse contour intersections against
the corresponding regular-grid sign-changing edges. Each face also reports four
world-position boundary digests; bounded CPU metadata comparison counts unmatched
edges between adjacent face pieces. Production results include a bounded record
for every resident face: key, generation, arena ranges, latency, counts, digests,
and audit counters. No geometry is read back for these audits.

The table output may contain zero-area transition triangles with repeated
positions. The maintained Godot implementation emits transition indices directly
as well; the official dissertation also calls out zero-area output as a possible
table consequence. Voxels3 currently preserves that table topology. Attempts to
reconstruct or compact transition triangles in an additional GPU phase caused
reproducible Vulkan invalid-write device loss on s&box `26.08.19`, so those paths
are rejected rather than retained as a fallback. Explicit `voxel_mesh_audit`
geometry readback remains diagnostic-only and never participates in rendering.

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

The production terrain material shades emitted normals with the historical
256-unit XY world-space green checker. The checker changes only albedo, not
material IDs, topology, positions, normals, visibility, or meshing work. A
recent 64-unit three-axis variant is rejected: its Z-dependent phase bands made
smooth curved and vertical surfaces resemble torn triangle fans in same-position
comparison. Geometry correctness is still decided by fixed digests, transition
audits, settled counts, and direct camera inspection; the checker is supporting
visual evidence rather than a substitute for those measurements.

The infinite-bounds custom scene object is only a render-thread rendezvous. Each
normal manager update publishes one monotonically increasing epoch after
placement, finalization, and the dispatch budget are current. Exactly one render
callback may claim that epoch and advance regular, transition, LOD2, and
visibility-readback state. Additional game, editor, or dependent views still
render the same `SceneWorld`, but their rendezvous callbacks cannot advance the
terrain GPU lifecycle again for that update epoch. A reentrancy guard also
rejects an overlapping callback while the claimed epoch is executing. This keeps
one canonical GPU scheduler without excluding cameras, pausing mesh updates, or
changing the persistent terrain draw path. Bounded diagnostics aggregate the
suppressed extra-view callbacks without logging every frame.

The rendezvous never records or resets a camera-attached command list. Command
recording and descriptor upload are manager-update responsibilities after emit
publication is finalized. The single list is attached to the selected main
camera; s&box propagates it to dependent editor views, so transient editor-camera
components do not acquire duplicate terrain state. This boundary is required by
the command-list lifecycle and prevents camera switching from replacing an
executing list or its indirect buffers.

The scheduler rendezvous retains `SceneCustomObject`'s native infinite bounds.
Giving the scheduler a merely large finite box makes it eligible for per-view
culling and can stop GPU work when a detached editor camera moves beyond that
box. A transition-based health report emits one error if pending regular or
transition work entered the current manager update without a GPU render tick for
`500 ms`, followed by one recovery record when ticks resume. Health is sampled
before current command-list work so a slow commit cannot mislabel its own elapsed
time as a missing render tick. A separate bounded warning records any command-list
commit over `500 ms` with its duration, arena count, visibility capacity, update
epoch, and render sequence.

Detached editor views depend on the camera component belonging to the live game
scene: the engine copies the main camera's post-processing and command-list
execution into that dependent view. The editor-only smoke controls validate this
relationship and recreate a stale ejected camera whose own scene no longer has a
main camera. This repairs the view dependency; it does not create a second
terrain command list, visibility buffer set, scheduler, or mesh owner.

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
requests, count results, candidate allocations, timestamps, and lifecycle. A
lane may serve a foreground or LOD2 regular batch, but never both at once:

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

Coordinate-local publication is separate from manager-visible clipbox placement.
Incoming residents may publish inactive in any completion order, but the committed
LOD0, LOD1, LOD2, and transition active sets change only through the atomic
whole-placement handoff described above. There is no partial active-set mutation
and no second renderer masking an incomplete placement.

Dimension or generator-configuration changes clear incompatible resident and
in-flight derived state. Terrain authority remains the canonical procedural SDF
and future edits invalidate only affected coordinate revisions.

## Persistent Drawing and Visibility

One persistent main-camera-attached terrain command list issues indexed-indirect
drawing per active shared arena and is inherited by dependent editor views. The
CPU owns bounds and source indirect arguments; the visibility compute pass is
the sole writer of visible indirect arguments after attachment. CPU descriptor
updates never write the visible buffer consumed by drawing. Visibility is a
conservative GPU derivative of resident region bounds. It may retain false
positives but must not create false negatives. Removal marks the resident record
inactive; stale candidates never enter visibility state.

Normal production performs zero geometry readbacks and zero ordinary-render SDF
evaluations. Performance diagnostics may perform bounded scalar readbacks for
visibility and settled aggregate counts; gameplay and rendering never consume
those diagnostics. Explicit `voxel_mesh_audit` additionally reads both indirect
argument buffers and verifies every GPU source record against the canonical CPU
descriptor plus every draw-enabled visible record against its source layout.

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

After the moving loop, the player stops and the manager waits for warm generation,
all regular and transition queues, and any pending whole-placement handoff to
settle. After two further render-sequence advances it measures a fixed 10-second
stationary window using the same production terrain.
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
- A dedicated fourth regular scratch instance was rejected after its LOD2-only
  emit path reproducibly crashed native Vulkan resource access during editor
  camera ejection. Mixing foreground and LOD2 requests in one batch, or allowing
  outer work to participate in foreground settlement, would still let stale
  outer work block newer movement and remains rejected.
- CPU density fields or coarse voxel buffers would violate the canonical GPU SDF
  contract. A generalized N-level hierarchy remains outside this fixed LOD2
  slice because it would reintroduce configuration, publication, and ownership
  machinery not required by the selected three-level product layout.
