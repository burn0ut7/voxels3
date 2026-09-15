# GPU Voxel Meshing

2026-09-15 integration: the user adopted the exact tested water-plus-prediction
source snapshot for main. See [the adoption record](../ValidationEvidence/IdleWork/Experiment.md)
for measurements and explicit allocation/drain exceptions. Historical prototype
results below remain unchanged; this is not a blanket pass of older scenarios.

## Production Slice

RIVERS-016 prototype (unaccepted): generation resolves material runs at refined cell
crossings in stage6 and stores packed weights in a second edge-buffer plane.
Regular XY density caches retain2floats per column; transition density stores no
material columns. No new storage-buffer binding is introduced. Existing stage6
UAV barriers publish position and material edge data together before emission.
Material generation uses the existing recipe/rules, including river bed and slope,
and is absent from both draw shaders. Dedicated regular emission and the existing
transition emission write28-byte vertices: the original24-byte position/identity/
normal plus4normalized material weights in Color32. Persistent arena byte budgets
stay32/16MiB; vertex capacity derives from the new shared stride. All stale-source,
publication and visibility contracts continue to apply. These generated material
runs are compressed cell strata, not a second mutable world. CPU logical queries
and correction persistence remain unchanged. Visual LOD/material fidelity and
figure-eight FPS/memory gates are pending; prior material-render acceptance does
not qualify the prototype. See SurfaceWater and VoxelMaterials for ownership.

One integer-indexed clipbox hierarchy owns the enabled terrain render levels.
The shipping default enables levels 0 through 2. Optional visual-distance tiers
enable ordinary levels 3 through 6 through the same records and queues. The
authoritative world combines the procedural SDF with the shared correction field
owned by [terrain deformation](TerrainDeformation.md);
indexed meshes are derived, GPU-resident, revisioned, disposable caches.
Generator version 10 owns regional exterior landforms and retains version-9 noodle tunnels and cheese
caverns. One adjacent-pair-aware transition cache closes every enabled 2:1
interface. The subsequent in-progress deformation slice composes the edited
field in both extractors and adds coherent local publication; its separate
contract and validation status are in the linked document. Morphing, generator
changes and allocator redesign remain outside that slice.

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

Identical saved-state restore correction: a replacement with zero
changed samples may retain already published geometry that matches the source
field. Rebase only its derived descriptor to the new local epoch and regional
revision; retain allocation, geometry counts and draw placement. Pending GPU
work keeps its old epoch and the existing stale-result rejection/rescheduling.
Transition reuse additionally requires the resident descriptor to match the
desired descriptor. Changed replacements retain the existing invalidation path.
The manager also skips activation of prepared empty LOD0 regions when no samples
changed. An unchanged field cannot create a new surface in those empty regions.
This avoids rebuilding unchanged geometry without weakening lifetime checks or
introducing another terrain representation. Qualification is tracked by
STORAGE-IDENTICAL-RESTORE-001/v2 in the validation ledger. The bounded unchanged
reload retained exact geometry digests, field fingerprint and collision contact,
with zero rebuild dependencies and readiness in the first one-second sample.
Equal-revision saves with different samples still rebuilt and restored their
distinct field/contact results. Full-capacity route performance remained within
the accepted range. Broader storage lifecycle gates remain open.

`VoxelManager` owns the analytic gameplay range and one `GpuVoxelMesher`.
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

## Canonical Level-Indexed Placement

`VoxelManager` owns an ordinary record for every supported integer level and an
ordinary record for every adjacent pair. Cell size is `BaseCellSize * 2^level`;
every regular region remains `32^3` cells. Each enabled level snaps with the
existing nearest-region rule, including at negative coordinates. LOD0 remains
centered on the level-1 cadence even when it is the only visual level. A
non-outermost coarse cache is centered at twice its next-coarser anchor; the
outermost cache uses its own anchor. Each coarse hole is exactly the finer
coverage bounds divided by two, so every boundary is complete and 2:1 aligned.
The lowest enabled level fills its center and has no hole.

The default applied snapshot is `MinimumVisualLod=0`, `MaximumVisualLod=2`,
`Lod0VisualHalfExtent=4`, and `LodCacheHalfExtent=8`. It produces 512 active
LOD0 coordinates; a 4096-coordinate level-1 cache with a 64-coordinate hole and
4032 active coordinates; and a 4096-coordinate level-2 cache with a
512-coordinate hole and 3584 active coordinates. Its adjacent transition pairs
contain 96 and 384 face identities. `GameplayRadius=4` independently owns 729
authoritative gameplay coordinates.

`VisualChunkRadius` is the terrain quality policy surface, expressed as nominal
reach in LOD0-sized chunks. It selects, rather than duplicates, the canonical
`MaximumVisualLod` state. At the default `4/8` half extents, tiers
`4/16/32/64/128/256/512` select maximum levels `0..6`; unsupported intermediate
values reject without changing the applied visual revision. Every tier above
LOD0 adds one ordinary 4096-coordinate cache, one 512-coordinate hole, 3584
active coordinates, and one normal 384-face adjacent transition pair. Cell and
region sizes double per level. Gameplay residency is still owned only by
`GameplayRadius`, and entity draw distance is not part of this terrain policy.

Gameplay membership and bounded CPU preparation are defined in the
[voxel foundation](VoxelChunkFoundation.md#canonical-ownership-and-data-flow).

`VoxelManager` computes changed regular boxes and adjacent transition boundaries
through one placement builder. The current unaccepted local-publication redesign
stages the destination directly and converges through parent/child replacements.
It supersedes the historical boundary-step movement policy below; initialization
and configuration replacement retain the atomic path. See
[Local terrain publication](LocalTerrainPublication.md). Each transition identity contains its fine
level, coarse level, coarse coordinate, and face. The same identity,
descriptor, queue, scratch pipeline, resident cache, allocator, visibility path,
and draw path serve every enabled pair. Transition work is ordered by coarse
level and then distance to that pair's hole center; it remains one queue rather
than one queue or mesher per level.

Regular and transition compute emit their final table-derived primary positions.
The terrain vertex shader only applies the engine's high-precision world offset
and decodes the emitted normal; it performs no LOD-dependent position change.
Each emitted 28-byte vertex stores a finite, bit-exact signature, its stable
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
the independent gameplay box, every represented level record, and every enabled
adjacent-pair record. Region and world bounds are half-open intervals. Enabling
the existing `VerboseLogging` property emits the same snapshot only when a
placement boundary changes; it does not add per-frame logging or a second
placement model.

### Atomic Clipbox Handoff

The earlier streaming study considered regional refinement. It remains an
[unimplemented alternative](../Research/ChunkPerformanceOptimizationFindings.md#publication-and-scheduling-alternatives),
not the publication contract below. `VoxelManager`
owns one committed drawable placement, the latest target anchors and validated
configuration revision, and at most one staged replacement. While the
replacement is incomplete, every committed level and pair active set remains
unchanged and continues to cover the world. A newer viewer or configuration
target is coalesced without cancelling the in-flight stage;
after commit, the manager immediately stages the latest target if it moved again.

The staged placement uses the same mesher queues and resident caches as ordinary
terrain. Its exact dependencies are newly required LOD0 coordinates, active
coarse descriptors, and transition descriptors for changed adjacent pairs. A
conservative known-empty
regular result is a resident descriptor with no arena allocation and therefore
satisfies the same readiness contract. Readiness is reconsidered only when the
mesher publishes a resident descriptor or the manager's prepared LOD0 set changes;
settled frames do not rescan the staged sets. Automatic readiness checks stop at
the first missing dependency; explicit diagnostics use the same method to count
all missing dependencies. A ready result still checks every dependency against
the current descriptor identity, without caching readiness across field changes.
The accepted short-circuit prototype and its GPU-maximum qualification limit are
recorded under READINESS-001 in the validation ledger. Once every dependency is resident,
the manager changes all changed active sets, releases leaving residents, and
records the new anchors and applied configuration revision in one main-thread
commit. Preparation records the exact entering, leaving, activation, and
readiness deltas only for levels and pairs whose bounds or enablement changed;
commit touches only those deltas and swaps the already-built placement sets
instead of scanning or copying the hierarchy. A request that returns to the
committed configuration cancels its uncommitted stage. The initial bootstrap has
no prior coverage to retain
and therefore activates its first placement immediately while that placement
fills through the same production queues.

An unchanged coarse cache skips preparation scanning only when the mesher reports
zero pending/in-flight work at that level and its resident count matches the
cache count. Changed, bootstrap, and incomplete caches retain the conservative
scan. This avoids repeatedly inspecting complete levels without maintaining a
second dependency index; it does not claim that changed boxes are built by slabs.

Regular and transition request arrays are allocated only after a valid request
has been dequeued. Canceled count results retain their batch identity and normal
completion lifecycle but receive no geometry allocation or enabled emit entry.
Finalization still rejects them; they never become known-empty residents.
Reporting distinguishes avoided empty batches from canceled geometry regions;
region counts are not counts of physical GPU dispatch calls.

Every coarse-level preparation uses the canonical authoritative density interval
for the entire region. The classifier first rejects regions outside global
vertical support, then bounds regional height and caves and includes saved
corrections. Provably uniform regions use the existing empty-region publication
path; uncertain regions enter the GPU mesher. This avoids mesh jobs without
sampling corners as an emptiness proof or changing the terrain function.
The additional CPU classification cost and measured startup/streaming gains are
recorded in [the experiment results](../Research/TerrainStreamingOptimizationResults.md).

This design retains the volumetric SDF, Transvoxel topology, and single GPU
mesher while removing fixed per-level ownership. A recursive tree, octree,
hierarchy-wide rebuild, second renderer, CPU mesher, per-level queue, or
independently mutable publication model remains rejected.

## Adjacent 2:1 Transitions

Clipbox placement owns where the boundary exists; Transvoxel owns its geometry.
For each coarse region on a hole face, the manager derives a separate
`(FineLevel, CoarseLevel, CoarseCoordinate, Face)` identity. The face direction points from
the owning coarse region toward the hole. Face counts follow the configured
hierarchy described above. The combined key set is diffed by the one placement
update. Retained faces keep their generation and allocation while
moving anchors replace only the identities that entered or left the boundary.

A transition is a face-local Transvoxel volume-cell mesh, not a heightfield trim
or a partial coarse block. Its `32x32` transition cells sample a compact
five-offset halo from the same canonical SDF at the selected fine spacing:
`69x69` on the
interface plane, `65x65` on each fine normal offset, and `33x33` on each coarse
normal offset. This retains every classification, interpolation, and fine/coarse
gradient sample while avoiding unused off-plane positions. The official 512
transition cases, 56 geometry classes, inversion bit, vertex reuse data, and
triangulations produce ordinary indexed triangle lists. Fine-layer intersections use the selected fine edge and gradients; coarse-layer
intersections use the selected coarse edge and gradients. Both now refine the
intersection against the procedural field as described below.

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
phases of the same single-face batch, preventing their costs from stacking
in one frame without changing transition identity or publication. Count readback
contains bounded scalar metadata only. Exact allocations use the unchanged shared-arena range allocator. Final
vertex and index stages in the same transition compute resource write the existing
28-byte vertex and 32-bit index formats. Packed audit counters keep that resource
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

## Regular Extraction and Render Lifecycle

A remesh evaluates the canonical `voxel_sdf_v13.hlsl` field into a haloed
`35^3` density lattice, classifies the `32^3` regular cells from cached corners,
counts compact region-local edge vertices and indices, and scans those counts.
The count stage returns only bounded metadata to the CPU. It never returns
density, vertices, indices, or other geometry.

After metadata readback, the existing CPU allocator reserves exact vertex and
index ranges. The emit stage writes 28-byte position/normal/material vertices, 32-bit
indices, and indexed-indirect arguments directly into persistent arena buffers.
Ordinary drawing reads persistent geometry, visibility, indirect arguments and
one shared material palette. It does not evaluate procedural density, edits, rivers or landforms;
the pixel shader reads interpolated generated material weights.
[Voxel materials](VoxelMaterials.md) owns that derived appearance contract.

The 16-unit checker projects onto the dominant surface plane and filters distant
checks. The previous 256-unit XY green checker is superseded. The earlier rejected
64-unit three-axis sum produced misleading Z-phase bands; do not reintroduce that
pattern. Geometry correctness still requires fixed digests, transition audits,
settled counts and direct camera inspection. Color patterns are supporting visual
evidence, not a substitute for those measurements.

The infinite-bounds custom scene object is only a render-thread rendezvous. Each
normal manager update publishes one monotonically increasing epoch after
placement, finalization, and the dispatch budget are current. Exactly one render
callback may claim that epoch and advance foreground regular, transition, outer, and
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
executing list or its indirect buffers. Main-camera membership is revalidated at
4 Hz, or immediately when the current camera becomes invalid, instead of
enumerating and diffing every camera component every frame. A newly selected
valid camera can therefore take at most 250 ms to acquire the existing command
list; no render resource, queue, or publication state is duplicated during that
handoff.

The scheduler rendezvous retains `SceneCustomObject`'s native infinite bounds.
Giving the scheduler a merely large finite box makes it eligible for per-view
culling and can stop GPU work when a detached editor camera moves beyond that
box. A transition-based health report emits one error after pending work has
been observed without an advancing claimed render epoch for `500 ms`, followed
by one recovery record when progress resumes or the pending work clears. The
observation interval starts at the first pending health check and resets on
progress; construction time and a slow frame whose preceding render claim
advanced are not evidence of a scheduler stall. This diagnostic measures callback
progress, not GPU hardware completion. Health is sampled before current command-
list work. A separate bounded warning records any command-list
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

Regular count batches contain at most eight regions. A render tick admits at most one
new count batch and consumes at most one count-ready batch for allocation and
emit. The batch size and dispatch shape are unchanged. Regular emission retains
the full count-batch domain for each destination arena, with disabled descriptors
for regions without output in that arena. The compact-request prototype was
removed after its fresh-control comparison missed a publication-tail gate; see
the [investigation](../Research/GpuMeshingOptimizationStudy.md#9-prototype-investigation-outcome).

Performance schema 24 records regular count batches/regions, emission arena
passes, actual emission region slots, enabled output regions and multi-arena
batches. The existing measurement lifecycle resets and gates these counters;
one `performance.gpu_work` record accompanies the saved result. Region slots
include disabled requests repeated across destination arenas. This measures
dispatch-domain work, not kernel duration or GPU bandwidth. The limited
`TransitionDeferredRenderTicks` counter observes normal foreground ticks with
queued transitions; it excludes early outer-service returns and transition-only
in-flight work, so it is not a starvation measurement.

Three independent lanes overlap unrelated batch chains. Each lane owns its
requests, count results, candidate allocations, timestamps, and lifecycle. A
lane may serve a foreground or outer regular batch, but never both at once:

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
Incoming residents may publish inactive in any completion order. In the current
unaccepted redesign, ordinary streaming changes actual activation through local
parent/child replacements, including required seams and water. Desired box metadata
is reconciled after convergence. Initialization and configuration changes retain
whole-placement publication. The existing mesher activation sets own actual
coverage; there is no second renderer masking incomplete placement.

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

The [manager performance lifecycle](VoxelChunkFoundation.md#performance-overview)
owns measurement and result capture. GPU results cover stage latency, queue and
batch utilization, schedule-to-renderable lag, placement readiness, memory,
visibility, and correctness, with moving and settled rendering measured separately.
Exact fields are defined by the production result types; scenario parameters,
thresholds, baseline runs, and acceptance decisions belong to the
[validation ledger](../ValidationResults.md).

## Alternatives Outside This Slice

- Enlarging batches or merely increasing the fixed admission count risks frame
  spikes without addressing serialization.
- GPU-owned variable-range allocation changes allocator ownership and requires
  a separate API-backed design and measurement.
- Changing cave complexity, movement speed, load radius, LOD, collision,
  networking, deformation, or authority would invalidate this throughput
  comparison.
- Feeding CPU density arrays/topology or geometry readback into rendering would
  create a second visual path. The independent [collision prototype](TerrainCollision.md)
  consumes the canonical SDF solely for physics; it does not replace GPU visuals.
- Rebuilding meshes every frame discards their persistent-cache value.
- A dedicated fourth regular scratch instance was rejected after its outer-only
  emit path reproducibly crashed native Vulkan resource access during editor
  camera ejection. Mixing foreground and outer requests in one batch, or allowing
  outer work to participate in foreground settlement, would still let stale
  outer work block newer movement and remains rejected.
- CPU density fields or coarse voxel buffers supplied to visual extraction would
  violate the canonical GPU SDF rendering contract. Additional visual levels must continue through the generic records,
  queues, publication handoff, and telemetry arrays; they cannot introduce
  per-level scratch, shaders, or publication paths.

## s&box VFX Shader Parser Gotcha

s&box 26.08.19 can terminate in native `vfx_vulkan` code while reflecting a
compute shader instead of reporting an ordinary shader error. The observed
signature was `EXCEPTION_ACCESS_VIOLATION_READ / 0xffffffffffffffff` through
`HlslParserErrorCallback`, `recoverFromMismatchedToken`,
`hlslvariablesParser`, and `CHlslParser::Parse`.

Voxels3 reproduced this when persistent vertex and index output writes were
embedded in the large multi-stage terrain compute shader. The density,
classification, scan, digest, and count stages were cold-start safe. Adding
either final geometry-buffer write stage to that monolithic program caused the
native parser crash even when the shader compiled successfully during a live
editor session. Moving the two writes into the small dedicated
`voxel_emit_vertices_cs.shader` and `voxel_emit_indices_cs.shader` resources
removed the crash without changing geometry bytes or topology.

Treat the following as hard requirements for terrain compute shaders:

- Keep persistent vertex and index writes in their dedicated shader resources.
  Do not merge them into `voxel_persistent_geometry_cs.shader` merely to reduce
  the shader count.
- This rule remains absolute for the regular terrain pipeline. The level-aware
  transition pipeline serving adjacent LOD boundaries is a measured engine-specific
  exception: on
  s&box 26.08.19, dispatching a second transition compute resource beside its
  topology resource terminates the editor natively even when the second shader
  is a freshly compiled zero-work kernel with no reflected resources. Its final
  vertex and index stages therefore share one transition resource capped at the
  engine's 16-storage-buffer limit. Do not apply this exception to regular
  terrain or other shaders without reproducing the same native failure.
- Give each dedicated output shader only the declarations, helpers, and tables
  it actually consumes. Do not create several wrappers that all include the
  complete multi-stage program.
- Use conventional multiline VFX/HLSL grammar: multiline `HEADER`, `MODES`,
  `FEATURES`, `COMMON`, `CS`, `VS`, and `PS` blocks; one structure member or
  declaration per line; and explicit braces around control flow. The engine's
  parser and error-recovery path are less tolerant than the live compiler.
- Do not treat a successful hot compile as parser-crash validation. After a
  shader resource or include change, require a clean editor restart, verify
  the editor remains alive, verify the s&box Sentry `last_crash` marker did not
  advance, and inspect the fresh log for HLSL/parser, failed shader load,
  missing compute pipeline, dispatch, and managed exception errors.
- A newly added shader may initially report a failed on-demand recompile and a
  missing `.shader_c`. Compile it successfully in the live editor first, then
  repeat the clean-start check. Never hide this failure by disabling scratch
  construction or leaving an empty kernel in production.

On engine26.09.08b, editing an included HLSL file does not automatically rebuild
its dependent shaders. The installed `ShaderHooks.OnShaderWasEdited` handles
`.shader` source changes only. Explicitly rebuild each dependent `.shader` with
the editor's `EditorUtility.CompileShader` and `ForceRecompile` before restarting
Play. The existing editor toolset exposes this as `compile_source_shader` for
mounted source paths. The general `asset_compile` route rejects these shader
assets as non-recompilable in this build. Verify successful compiler results and
loaded geometry identity; source hashes alone did not establish the running
variant in DISTANT-REFINEMENT-001/v1. This does not replace the clean-editor-start
requirement above.

When diagnosing a similar crash, bisect valid shader programs by complete
stage boundaries. Keep every intermediate variant syntactically valid; an
invalid preprocessor guard can remove a function's closing brace, and cold
construction of that invalid asset can crash the native error-recovery path
before a useful diagnostic reaches the console.

## Readiness lookup allocation constraint

S1 (2026-09-08, explicitly accepted by the user): the manager's three
`CapturePendingClipboxReadiness` descriptor factories use `captureRegion:false`.
The same descriptor construction still computes regional correction revision and
field epoch, but skips acquiring a regional field reader used only for equality.
`Contains`, resident lookup, cancellation precedence and descriptor equality are
unchanged. Every mesh scheduling caller retains the default regional capture;
this adds no readiness cache or independently mutable field state. The
`SIMPLIFICATION-S1-001/v1` ledger entry owns before/after results and human checks.

The current working-tree candidate uses direct array/List iteration in both
GpuVoxelMesher.Contains overloads. Repeated readiness checks must not allocate
captured predicates. Resident/pending precedence, descriptor equality, and the
descriptor overload's cancelled-in-flight exclusion remain unchanged; the key
lookup retains its existing semantics. This change addresses allocation stacks
observed during CapturePendingClipboxReadiness, without changing GPU geometry
or dispatch ownership. See candidate 8 in the [validation ledger](../ValidationResults.md)
for sampled attribution, the standard benchmark, and unresolved acceptance.

### Sparse cave mask load failure â€” 2026-09-08

Adding a fifth simplex query for regional cave coverage coincided with a
reproducible play/load invalid-write device loss on26.09.01c/RTX5090; Aftermath
named the transition compute shader. Replacing that mask with smooth trilinear
hashed values survived cold load, full figure-eight and saved-world reload.
This is observed repair evidence, not proof of compiler causality or a general
noise-call limit. Keep the smaller canonical mask; details and failed evidence
are in [the depth review](../ValidationEvidence/CaveDepth/Review.md).


## Regional generator request layout (qualification pending)

Regular requests now occupy96 bytes: origin/cell Vector4, seed/three amounts
Vector4, three scales/relief Vector4, ruggedness/reserved Vector4, four scalar
metadata words, and one reserved Vector4. Transition requests occupy128 bytes:
origin/fine-cell, the same three recipe Vector4 values, three basis/face Vector4
values, and four metadata words. CPU declarations in GpuTerrainContracts match
persistent-density, transition and dedicated vertex-emission shader declarations.
Scratch accounting includes the additional32 bytes per request. No extra storage
buffer binding or meshing stage is introduced.

On26.09.08 the initial landform visual run showed gaps after the vertex emitter's
HLSL request layout changed without a corresponding watcher recompile. Editing its
source layout comment triggered a successful normal shader compile; replay showed
continuous initial ground. Native asset_compile reported the shader not
recompilable despite its source path existing. Keep this failure and recheck in
LANDFORM-001/v1 R1/R2; a clean editor restart is still required before acceptance.


Recipe resets now recreate regular and transition scratch lanes together. Clearing
in-flight records while retaining a busy transition scratch left no record able
to consume its completion; repeated recipe application reproduced a permanent
transition queue. LANDFORM-001/v1 R5 records the repair and complete queue drain.

`voxel_density_audit` is an explicit read-only diagnostic for the next unedited
regular count block at each LOD. It reads the actual density scratch before reuse
and compares 125 halo samples with the canonical scalar/lattice CPU paths and
conservative bounds. It never schedules geometry or dispatches another sampler.
Readback is bounded to one171500-byte block per LOD; missing levels remain pending
until ordinary work supplies them or a reset cancels the request. Arming and
sampling are rejected/skipped during schedule-latency measurement. Do not arm it
for a timed run. Edited blocks are excluded.
The diagnostic also scans this existing readback for nonfinite values and
selects the closest negative/nonnegative stored densities. It compares at most
two additional samples per block against canonical CPU/lattice evaluation,
logging coordinates, raw densities, errors, sign agreement and bounds. Missing
signs are explicit; there is no invented crossing in a one-sided block. Sparse
near-zero sign disagreements have their own counter rather than disappearing
from the report. These operations run only for an explicitly armed diagnostic;
there is no additional GPU dispatch/readback or recurring sampling path.

Historical R6 covered875samples with max error0.01638031 and no near-zero samples.
Current LANDFORM-DENSITY-012/v2 covers875sparse plus11closest probes with maximum
error0.001953125, zero lattice differences and no sign/nonfinite/bounds failures.
Two LOD3closest probes are within the +/-0.1 band and agree in sign, with maximum
error0.0000076293945. This finite screen does not qualify all near-zero samples,
transition buffers, edited blocks or all coordinates. The ledger owns exact
coverage, source versions, acceptance and preserved earlier observations.


The same arm now includes one unedited transition count block for each of six
faces at each of six LOD pairs. A36-bit pending mask is cancelled alongside the
regular mask on reset or performance measurement. Only noncancelled completed
count results are inspected before scratch reuse. Each explicit readback is
15389floats/61556bytes;125uniform indices span the face plane and four normal
planes, with at most two additional nearest signed-density probes. All stored
GPU samples are checked for finite values. CPU comparisons use the canonical
field and the exact coordinate decoder shared with correction uploads. No new
shader, generation task or authoritative state is introduced. Missing faces
stay pending until normal work supplies them or the session resets; finite
observations cannot prove global parity. LANDFORM-DENSITY-012/v3 owns runtime
qualification of this extension. Its first run completed all36combinations
and4542comparisons, maximum CPU/GPU error0.015625, zero sign/nonfinite/bounds
failures, and no transition near-zero samples. This establishes a bounded
transition-field screen; it does not establish near-zero transition parity,
mesh continuity, edited-face behavior or performance acceptance.


Deep CAVEPLAY-012/v1-v2 observations fail the same0.05density limit despite
passing selected geometry: regular error0.16308594 at world(2048,4224,-32640)
and transition error0.63684464 at(-2064,496,-29104). Aggregate diagnostics now
include the maximum-error sample coordinate and raw CPU/GPU values. Sign
agreement at these samples does not establish density acceptance; root cause
and correction remain open. See the ledger for immutable reproduction inputs.

CAVEPLAY-012/v3 tried precise GPU simplex locals and explicit scalar dot
arithmetic matching the CPU. The visible cold start succeeded on26.09.08,
but both previously observed maximum errors and coordinates were unchanged.
The inspected compiled transition SPIR-V payload(266476bytes) contains zero
NoContraction decorations. This does not prove all compiler behavior; it does
reject that source-only candidate as a fix. The candidate is reverted, while
the exact-coordinate diagnostic remains. HLSL language guarantees must be
verified through this engine's compiled pipeline before relying on them.

## Bounded edge refinement (R7, qualification pending)

The landform review exposed a large distance between fine and coarse surfaces:
the same smooth exterior produced a287-unit ledge because cave composition
changed the magnitude of a solid coarse endpoint. A single linear interpolation
of those endpoints did not locate the actual zero crossing. This was visible
after all terrain queues drained, at the LOD5/6 boundary X131072.

The count/digest stage now refines each active axis-aligned edge with up to eight
bisections and one final secant interpolation. It evaluates the same procedural
field at interior points; no generator version, cave coefficient, density sign,
table case, edge admission rule or terrain setting changes. World-axis ordering
is normalized, and finite-coordinate resolution can terminate a stalled bracket.
This locates one root of an existing sign-changing edge. It cannot recover
multiple surfaces whose signs were missed by the coarse lattice.

After scans complete, the existing edge flag word changes phase: zero means
inactive; an active word is asuint(refined world-axis coordinate)+1. This encoding
preserves zero and negative coordinates without using zero as an active sentinel.
The emit stage decodes the same world coordinate used by the digest. A shared
position helper keeps fixed edge components exact. Explicit UAV barriers order
the rewritten words. There are no additional buffers, buffer bindings or dispatch
stages, and the dedicated regular vertex/index writers remain separate.

For edited edges, subtract the procedural endpoint values from captured total
densities and linearly reconstruct those endpoint corrections along the edge.
This retains the existing coarse lattice's edit approximation; it does not claim
to resolve fine edit detail absent from that lattice. Base-field evaluation stays
canonical. Normals still interpolate the existing gradient samples at the refined
position fraction. Correction/normal behavior requires the planned edited-world
and collision checks before acceptance.

Eight is an internal bounded numerical limit, not a designer control. Root work
occurs during mesh generation, not every rendered frame. Added GPU cost remains
unqualified until the unchanged workload is measured; retaining scheduler caps
alone does not prove acceptable frame pacing. R7 cold play removes the reported
wall at the original camera, with collision4913/4913 ready and no pending work.
The existing audit still flags degenerate transition triangles; this is not a
blanket mesh or performance acceptance.

## Material appearance

[Voxel materials](VoxelMaterials.md) owns the shared draw palette, layer selection
and world-space checker. Both regular and transition geometry use that same draw
shader without changing persistent vertex layout or extraction. Appearance
qualification and performance comparison are recorded under MATERIALS-001/v1.

## Transition stage specialization candidate â€” 2026-09-09

D5 narrows the reproduced startup GPU failure to outstanding transition count
work: batch104 submitted refine/count at01:37:20.5042 and has no callback before
fault; batch119 also submitted sample/classify work. Neither batch emitted geometry.
This does not identify one exact instruction or establish a driver defect.

The next candidate specializes the existing transition resource by its ten
stages with the engine's DynamicCombo/RenderAttributes.SetCombo mechanism.
Inputs, buffers, ownership, stage order, synchronization, counts and geometry
algorithms remain unchanged. Each compiled variant contains its stage's live
code rather than the full dynamic ten-way program. There is still one canonical
transition shader resource, not a second emitter resource or alternate mesher.
This addresses the demonstrated failure in a large combined compute program and
provides distinct compiled stage identities for future crash attribution.

Alternatives: further blind restarts add no useful evidence; removing refinement
would restore the known LOD wall; a second transition shader resource conflicts
with the recorded engine limitation. Stage specialization is supported by installed
core/shaders/postprocess/postprocess_bloom_cs.shader and RenderAttributes.SetCombo.
It is a candidate until clean compilation/startup, real meshing/parity, edit/network
and performance checks pass. There is no claim that program size is the proven
cause. No generator-version change: the canonical field and arithmetic are unchanged.

D6 outcome: rejected. The stage-specialized resource terminated editor36804 at
its first sample dispatch, earlier than the original failure. No count callback
or native root-cause attribution was captured. The specialization was removed;
the runtime-uniform stage selector remains canonical. This is additional evidence
that transition pipeline variants need engine-specific qualification, not proof
that the source arithmetic is wrong or that any fallback has been accepted.

### Version-12 transition batch qualification

Transition scratch owns a single-face batch limit; the mesher uses that same
limit for dequeue and request arrays. Regular/outer batch limits are unchanged.
On s&box26.09.08 / RTX5090, eight-face batches repeatedly faulted around coarse
edge refinement. D16 completed every transition pair with single-face batches,
without field, cave, refinement or requested-coverage changes. Both batch work
and scratch capacity changed, so the driver/indexing root cause remains unknown.
This is a retained correctness candidate, not full qualification: broad geometry
still reports degenerate triangles, multiplayer/performance remain open, and
D17 also passed startup after temporary logging was removed. Shutdown after D16
left an engine error window and remains unqualified. See the validation ledger.

### Rejected refined-triangle compaction proposal

This proposal is not implemented. GEOMETRY-012 candidates B through E failed
GPU or native startup before the audit. All compaction code and added source
resources were reverted to D17 at that point; the
geometry failures remain open. The following records the rejected design.

The version12 origin audit found3179 degenerate transition triangles and41
regular triangles. Topology tables count triangles before refined edge positions
can coincide. The candidate inserts a GPU per-cell validity pass after edge
refinement and before index prefix scans. It stores a triangle bitmask in the
high16bits of Cells.x, leaving the case code in low16bits; Cells.y is retained
index count and Cells.z its prefix. No extra storage buffer or readback.
Count and emit consume the same mask, so allocations remain exact; vertex slots
remain stable even if unused. The relative squared-area tolerance is owned by
GpuVoxelMesher and passed to both count shaders, matching the CPU geometry audit.
One shared HLSL area predicate defines regular/transition rejection. Field and
caves remain untouched. Emission-only suppression was rejected because it leaves
holes in allocated index ranges; moving vertices was rejected because it changes
refined geometry. Added bounded cell work requires cold startup, mesh audit,
visual seam checks and figure-eight qualification before acceptance.


### Transition compaction candidate F

GEOMETRY-012/v1 F failed with a GPU memory fault at02:54:31 before audit and
was fully reverted. The following is the rejected transition-only design;
D17 was restored after F; G2 below is the subsequent retained candidate. It preserves
all case codes, refined positions, edge vertex IDs and the existing16-buffer
resource. After refinement, stage10 stores a retained-triangle mask in Cells.w
(previously unused after classification) and its index count in Cells.y.
Cell prefix scans then allocate the retained index count; stage8 consumes that
same mask, without uninitialized gaps. Empty cells remain zero from stage0.
GpuVoxelMesher.MinimumTriangleAreaSquaredRelative names the existing1e-10
squared-area threshold shared with the audit; transition count receives it as
an attribute and scales by coarseCellSize^4. Regular emission is unchanged and
its degenerate triangles remain an unresolved acceptance gate. No new readback,
shader resource, buffer or state owner is introduced. Added bounded per-cell
work requires cold-start, geometry, seam and performance qualification. Earlier
B-E failures and the last qualified D17 startup remain recorded separately.


### Transition topology storage candidate G

Current D17 SPIR-V has repeated function-storage copies of large static topology
arrays. Driver memory allocation is not established, but removing those copies
is a concrete alternative to expanding the already faulting shader program.
Candidate G moves the exact MIT-licensed official transition table values into
GpuTransitionTables, one immutable CPU source. GpuTransitionScratch owns a
read-only-use structured buffer, uploads once during construction, and disposes
it with scratch. Table offsets have the same C# owner and are bound as integer
attributes. This derived lookup data is independent of world seed/edits; no
field invalidation or per-frame upload is needed. The buffer is35KB per scratch
(8728 uints). Cell/face audit counters share one uint4 buffer, preserving the
16-storage-buffer limit and every counter meaning. Count/emit algorithms and
all stage barriers remain equivalent. Field, caves and regular meshing are
unchanged. A buffer lookup avoids large function arrays; replacing tables with
large switches or adding shader resources is rejected because it expands code
or conflicts with prior native startup failures. Cold startup, unchanged audit,
visual seams and full performance qualification remain required.
Inspection used Source2 Viewer/ValveResourceFormat dictionary resources:
https://github.com/ValveResourceFormat/ValveResourceFormat/blob/master/ValveResourceFormat/CompiledShader/ZstdDictionary.cs
This is a diagnostic use of reverse-engineered resource knowledge, not an engine
contract. See the validation evidence for its limits.

G initially failed because two lookup expressions masked the added offset;
G2 fixes their parentheses. G2 cold startup, zero table/face/lateral mismatches,
104-region audit completion and original-world fingerprint checks pass. The
same3220degenerate triangles remain, so geometry acceptance is still open.
G2 is the current retained table-storage candidate. Its compiled SPIR-V removes
all large topology Function arrays (271172 to234848bytes); no frame-time or
driver-memory improvement is claimed. See GEOMETRY-012/v1 G/G2 for both outcomes.


### Transition triangle selection candidate H

H retries F's per-cell selection using G2's table buffer. It retains exact edge
positions and vertex slots, stores the mask in the unused Cells.w word, and
runs index scans after selection so allocations and emitted indices agree.
The existing relative squared-area threshold has one C# owner and is bound to
the shader. This adds bounded work per transition cell and no new buffer or
readback. It remains unqualified until the fixed startup/audit/seam/performance
gates pass; regular triangle removal is still outstanding.

H now passes cold startup and the48-transition-region origin audit: no transition
degenerates or table/face/lateral mismatches. It is the retained implementation,
with G2 table storage. The56regular-region sample still contains31degenerate
triangles; seam, multiplayer and figure-eight acceptance remain pending. Counts
and masks share the audit's unchanged area threshold. The field and saved edited
fingerprint are unchanged. This bounded result does not prove the earlier native
fault's root cause or qualify global topology/performance.


### Regular triangle selection candidate I

I extends the retained selection responsibility to regular meshes. The canonical
regular HLSL table data and generated VoxelCollisionTables remain unchanged;
GpuTerrainScratch concatenates that existing view once into a14KB buffer and
binds its computed offsets to count and the dedicated index writer. Scratch
owns upload/disposal and memory accounting. No recurring upload, world state,
or alternate mesher is added. GPU shaders no longer compile large regular
static arrays. After refinement, stage8 stores a triangle mask above the low16
case bits and its index count in Cells.y. Existing scans produce exact offsets;
the index writer consumes the same mask. Count/emit share the unchanged area
criterion with the CPU audit. The regular output resource split remains intact.
This bounded extra per-cell work requires cold startup, full geometry, seams
and figure-eight qualification. Field/caves/vertex positions are unchanged.

I now passes the fixed104-region origin audit, with zero degenerate triangles
in56regular and48transition regions, zero table/face/lateral mismatches and
unchanged saved-world fingerprint. Cold startup settles without GPU fault.
I with H transitions is the current retained implementation. Broader seam and
edited-site validation, multiplayer and figure-eight acceptance remain pending.
The regular canonical table source/generator is unchanged; only GPU storage and
triangle count/emission consume the new representation. Earlier failures remain
in the ledger and do not establish a general GPU driver root cause.


### Transition readback observations

Existing meshing diagnostics now expose TransitionCountReadbacks, total
TransitionCountReadbackMilliseconds and TransitionCountCallbackWaitMilliseconds,
and the maximum of each duration. These are cumulative observations since the
mesher was created (or these properties were introduced by hotload), not reset
per route. CountReadbacks counts batches, not faces. The readback interval begins
after count dispatch submission and ends in its callback; callback wait ends when
the render scheduler consumes it. Neither is isolated GPU execution time.
The counters observe existing callbacks and add no readbacks or scheduling work.
Cancelled results are included. Use counter deltas for bounded observations;
maximum values remain lifetime maxima. These fields do not supply percentiles.


### Rejected generated-density cache

The generated-density disk-cache prototype has been removed at the user's
request. Meshing again uses the preprototype queue and sampling path; existing
resident geometry reuse remains. The [experiment report](../Research/GeneratedTerrainCacheExperiment.md)
retains historical results. No generated-density cache setting or IO remains in
the runtime meshing pipeline.


### Accepted empty transition classification

Before submitting an unedited transition count, the mesher evaluates the existing
canonical density interval over the descriptor's complete sampling bounds. A
strictly positive minimum or strictly negative maximum proves uniform signs.
Edited or uncertain regions retain GPU sampling. A uniform region becomes an
empty CandidateTransition in the idle lane and goes through normal next-render
cancellation, desired-descriptor and edit-publication checks. It skips GPU
sampling/count/emission and readback without bypassing readiness metadata.
Transition batches, scratch resources and view distance are unchanged.

Empty-to-empty transition publication does not dirty draw commands. Replacing
or adding drawable geometry still does; edit publication retains its existing
atomic invalidation path. Skipped-region count and cumulative classification CPU
time are exposed in voxel_collision_info. They count attempted jobs, including
ones later made stale, and are not geometry-readback counters.

The user accepted candidate B's measured startup frame-rate tradeoff on2026-09-09.
See [the experiment](../Research/EmptySeamExperiment.md) for historical rejection,
acceptance, fixed-world results and incomplete moving qualification. This does
not accept unrelated terrain errors or alter the existing benchmark route.


## River field preparation

The current river slice derives exact CPU segments from the immutable recipe;
see [the drainage contract](../Plans/RiverDrainageBasins.md). Regular and transition
scratch lanes reuse their existing river texture when its recipe and XY coverage
contain the entire closed request bounds. An exact upper-edge sample is a miss,
matching the shader's exclusive edge. Misses asynchronously capture/pack the
required region before dispatch. Reuse skips both numerical packing and upload;
the bounded exact-footprint shared numerical cache is documented in SurfaceWater.
A sampled RGBA32323232F texture transports a stackless spatial hierarchy and unique endpoint/radius pairs,
avoiding another storage binding in the16-buffer transition pipeline. Each atlas
is capped at128MiB; generation owners release their resources on teardown.
The CPU patch sampler and atlas packer share RiverSpatialIndex: median splits,
up to eight segments per leaf, and preorder subtree escape indices. Wet-support
bounds permit conservative valley-height pruning without changing the river
profile. Regular density preparation evaluates the XY landform once per halo
column in stage9 and stores height/mountain weight in the existing density
buffer tail. Stage1 reuses the height/mountain pair for all Z samples through the
shared SDF composition; the two-float cache uses9800bytes per batch member and
adds no storage-buffer binding.

The regular and transition28-byte vertex format retains packed allocation identity
in normal.x and appends generated material weights. CPU audit retains its finite
identity and slot/generation checks. The former terrain fragment river cutout is
removed: canonical meshing now owns all visible terrain holes. Coarse narrow-river
fidelity, edited-solid visibility and the unchanged figure-eight are acceptance
gates. Do not reintroduce procedural pixel clipping to hide a mesh deficiency.

## Terrain Depth and Shadows

Forward arena submissions remain GPU-culled indexed indirect draws at the
camera AfterOpaque stage, with explicit scene-lighting attributes so terrain
receives shadows. One infinite-bounds SceneCustomObject participates only in
depth-prepass and shadow layers. Each view independently culls the canonical
published region bounds; off-camera shadow casters remain eligible.

The SHADOW-FPS-003 revised shared-dispatch candidate submits one compute launch per
view, with one 512-thread group per arena. Each group retains the same frustum
predicate and prefix scan, and writes disjoint compact block records plus one
indirect argument. A block represents up to64 canonical triangles. The192-index
driver model fetches the block's first index, base vertex and valid index count,
then reads the existing28-byte terrain vertex. Final partial-block padding is
clipped without reading terrain geometry. World positions and decoded normals
remain identical; no terrain copy, alternate mesher or GPU readback is added.

GpuVoxelMesher owns DepthTrianglesPerBlock, DepthIndicesPerBlock and
DepthBlockCapacity. Per-arena capacity is ceil(IndexArenaCapacity/192)+512,
covering the indexed allocation and each region's possible partial block.
The per-camera VisibilityBuffers snapshot owns shared block and argument buffers,
sized to its descriptor arena capacity. Each arena uses a fixed block-range
offset. Its20-byte argument is GPU-copied to the original private argument buffer
before the existing zero-offset model draw. All transfers precede all depth draws.
GeometryArena retains that argument buffer and canonical vertices/indices; its
former private block scratch is removed. The first shared-argument nonzero-offset
model-draw candidate lost part of the character shadow in a matching secondary
view and is rejected, despite higher FPS and passing forward geometry audits.

The existing camera-state lock protects descriptor/scratch lifetime and the
immutable arena snapshot. Every view rewrites its scratch before UAV/read/copy/indirect
barriers and the per-arena draws. Snapshot growth retires buffers under the same
visibility lifecycle; disposal releases both depth buffers. The power-of-two
descriptor capacity can reserve more scratch than currently occupied arenas.
Scratch is16*DepthBlockCapacity+20 bytes per reserved arena per snapshot; retired
snapshots remain subject to the existing visibility disposal policy. Forward
submissions restore vertex/index resource states after depth reads.

This replaces per-arena compute launches and barriers, while retaining one draw
per arena per view. It preserves cascades, contact shadows, published geometry,
field-readiness gating, light-view culling and terrain materials. Combining terrain
arenas or reducing shadow quality was unnecessary for this slice. Camera-only
occlusion cannot safely replace light-view visibility and remains outside scope.

Historical triangle-instance and per-arena block-dispatch measurements are kept
in the validation ledger. SHADOW-FPS-003 records the current edited-world
comparison, matching cold-start memory controls, screenshots and88-region audit.
The measured gain does not accept the unrelated existing10-second streaming
drain failure or editor shutdown errors. Full performance acceptance and commit
remain pending the failing streaming gate. The corrected cold comparison measured
403.88->425.46 moving FPS and472.81->533.66 stationary FPS, with GPU frame time
1.617->1.232ms. Relative criteria pass; drain remains12.826s. Secondary character
shadows and88-region audits pass; complete shadow/occlusion coverage is unverified.
The first candidate timing does not qualify the corrected per-arena argument path.

### Distant shadow receiver boundary investigation

RING-SHADOW-003/004 isolates a ring in the installed directional-shadow receiver:
its coverage early return precedes pixel derivatives for the receiver normal
offset. Moving that existing calculation before the return removed the ring;
restoring the original source brought it back. Sun shadows, distance and quality
remained enabled and unchanged. See the [source-level evidence and patch](../ValidationEvidence/ShadowRing/README.md).
The initial experiment changed the engine include and then restored its bytes.
RING-SHADOW-005 qualifies a project-owned replacement at
`Assets/shaders/Shadows/DirectionalLightShadow.hlsl`, pinned to the engine source
revision and carrying its MIT notice. It selects one implementation through the
existing include path; terrain geometry, render passes and shadow settings remain
unchanged. The project terrain binary matches the ring-free experiment exactly,
without modifying installed engine source. Terrain, depth and water entry shaders
must all be rebuilt after changes to this include.

Close cast/self-shadow comparisons and a cold editor start have passed. The
canonical figure-eight qualification and its acceptance decision are recorded in
the [integration report](../ValidationEvidence/ShadowRing/README.md#project-integration-qualification-ring-shadow-005v1).
This override depends on the engine's directional-light buffer layout and shader
API. Requalify it against upstream on engine updates and remove it when the
upstream correction passes the recorded scenario. Do not grow a separate lighting
stack around it; precompiled engine receivers are not retroactively rebuilt.

### Landform include hotload qualification

HILLS-001/v1 on26.09.08b reproduced stale regular/transition shader binaries
after an include-only landform edit, while C# collision used the new recipe.
The player appeared inside the rendered floor. Editing both existing shader
entry files triggered successful recompilation; managed compile status alone
does not validate GPU recipe freshness. After field-include changes, verify
both shader compiles, restart Play/editor per the shader contract, and check
CPU/GPU density and actual rendered support before accepting visual results.
No second field, collision offset or LOD workaround is introduced.

### Active handoff scheduling candidate

Placement preparation schedules missing active coarse readiness regions across
all enabled levels before remaining entering cache regions. Previously cache
Entering was queued first, so its inactive entries could delay visible handoff
dependencies; Contains then retained that queue position. Existing queued/in-flight
requests retain their identity/order, edit queues keep priority, and all cache
coverage/atomic handoff requirements remain unchanged. This is an ordering
change, not added dispatch concurrency or a relaxed seam gate. Runtime timing
and unchanged figure-eight acceptance are tracked in HILLS-STREAMING-001/v1.

Fast-flight retargeting: when the requested viewer anchor leaves the staged
finest-enabled outer box, cancel that obsolete pending placement through the
existing cancellation lifecycle and prepare the latest target. Visual-setting
changes also supersede the old pending configuration. Nearby target changes
retain the current step to avoid cancelling useful work each frame. Published
geometry stays intact until all replacement readiness/seam checks pass.
This addresses observed94-region target lag; timing acceptance remains pending.

When no near regular work submits, outer count admission and transition work
share render ticks according to their existing last-service timestamps. A
successful outer submission returns before transition processing. This retains
one outer batch in flight and the 250ms forced-service deadline under near
load, while avoiding transition-queue starvation of outer work on otherwise
available ticks. It does not repeat the rejected simultaneous-dispatch or
16ms forced-deadline experiments. Compile passed on26.09.08b; runtime
performance qualification remains pending.

Cancelled placements retain entering regular regions that overlap the latest
requested cache, when the visual configuration is unchanged. Both preparation
and cancellation use TargetOuterAnchor; cancellation translates the existing
staged bounds by that anchor delta. The next preparation adopts retained
requests/residents through the existing descriptor identity checks. Entering
seams are retained only when their fine outer box is unchanged. All other
entering work uses the existing removal/stale-rejection lifecycle. This avoids
reclassifying/remeshing overlapping work on every fast-flight retarget.

### Bounded placement preparation candidate

Measured overlap-retaining preparation still takes64.9203ms in one call.
VoxelManager will retain its canonical placement preparation, but advance
the coarse classification/scheduling and seam-scheduling loops through an
engine-thread iterator with a2ms per-update budget, checked every32regions.
Staged spatial sets are constructed first; the iterator owns no independent
world state and creates descriptors from the current authoritative field.
Only the manager advances/disposes it. Cancellation and recipe resets dispose
it before changing staged sets. Atomic publication cannot run while this
preparation is incomplete; bootstrap commits after completion. Edit changes
continue through canonical invalidation; already registered descriptors are
invalidated normally and later descriptors capture the current field.
The budget limits CPU preparation bursts, not terrain quality or GPU batches.
Alternatives: more cancellation repeats work; doing the whole plan on a worker
would access engine-owned mesher state; relaxing handoff exposes seam holes.
Spatial set construction and an individual batch remain non-preemptible and
must be measured. Existing preparation totals include all slices; maximum
reports the longest synchronous preparation slice. No new test path.

The attempted extension of the edit seam16ms minimum-service policy to all
placement seams was rejected and removed. HILLS-FAST-001/v1 did not show a
material drain improvement (10.535s versus10.483s), while allocation increased.
The16ms guard remains edit-only. Budgeted CPU preparation, overlapping-work
retention and idle-near outer/seam sharing remain candidates; fresh-world
regression acceptance and the remaining fast-flight arrival delay are open.

### Empty seam admission

The existing conservative unedited transition bound moves from the render
queue to ScheduleTransition. Uniform-sign seams have no geometry, so publish
the same empty CandidateTransition through the existing resident/publication
path immediately instead of spending a render-service turn plus integration
turn on each empty face. Edited seams retain GPU extraction. The canonical
classification formula and strict sign test remain unchanged. Desired
descriptor identity is installed first; superseded GPU results still fail the
existing equality check. If the empty face belongs to an edit group, stage
it in that group's candidate dictionary rather than publishing separately.
Most admission occurs inside the manager's2ms preparation iterator. This
removes the former duplicate render-queue classification branch entirely.
No GPU batch size, scratch-lane count or simultaneous dispatch changes.

### Seam throughput qualification, 2026-09-10

HILLS-SEAM-BATCH-001 tests two faces per existing transition scratch batch,
using the current structured topology-table implementation. Live traces show
handoffs blocked solely by pending seams after regular terrain and water finish.
The batch constant continues to own buffer capacities, request arrays and dispatch
counts; no extra shader, binding, scratch lane or concurrent render action is
introduced. Source identities, readback, arena allocation, stale rejection and
atomic handoff are unchanged. This supersedes the single-face value only if the
new runtime qualification succeeds. D16's eight-face GPU failure remains valid
historical evidence; later table-buffer changes are a reason to test a modest
increase, not proof of safety or permission to assume eight-face correctness.

Four-face batching was rejected for worse moving frame pacing; current candidate
retains two faces. Cancellation now preserves exact overlapping seam identities
when the fine box slides. After stopping the preparation iterator, the manager
reuses the canceled pair's NextDesired set to enumerate the latest target with
AddTransitionFaces, and removes only obsolete entering keys. The next preparation
rebuilds its own sets normally. Committed Desired/visibility remain untouched;
there is no second seam constructor, cache, or publication owner. Existing source
revision checks decide reuse, including edits during a pending placement. This
extends regular-region overlap retention to the matching boundary faces and
requires the same fixed figure-eight and seam audits before acceptance.

### Shared regular/seam service candidate

The render scheduler alternates service opportunities between regular meshes and
transition seams. If the preferred pipeline cannot progress, the other can use
the turn. Existing near/outer queues and edit-priority queues remain canonical;
no second job, GPU resource or simultaneous regular/transition dispatch is added.
This replaces the edit-only16ms seam guard, since streaming handoffs also require
seams while regular requests continue arriving. Numerical river preparation,
split count stages, readback, emission and main-thread publication keep their
existing ownership. Outer forced service retains its250ms request deadline and
can be delayed by an already selected transition turn, just as other GPU stages
can delay it. Frame pacing, allocations, service gaps and correctness need live
qualification; a144ms fast-run frame is currently unexplained, so this candidate
is not accepted for commit. See HILLS-SERVICE-001 and live arrival evidence.

The two-way candidate is rejected by the controlled standard comparison:
outer p95 increased from4.368s to17.362s despite faster far seams. The current
candidate rotates preferred service across regular continuation, transition
work, and outer emission/count admission. An unavailable preferred pipeline
falls through to the existing scheduler. One callback still chooses only one
pipeline, and existing scratch-lane ownership and the outer250ms fallback remain.
This gives outer work explicit service rather than only leftovers from near
work. HILLS-SERVICE-STANDARD-001/v1 qualification remains required.

### Progressive exterior publication, 2026-09-10

The user explicitly requires individual chunks to appear at the visible edge
before the rest of a placement finishes. Queue throughput alone does not satisfy
this: the global handoff withheld all completed incoming chunks. The manager now
admits coarsest-level chunks outside the committed outer box for immediate normal
resident publication, including chunks temporarily covering the staged inner hole.
They use the same SDF descriptors, queues, arenas and render-active set. They are
scheduled nearest the target first, before other placement requests. No second
mesher, density representation, GPU lane or worker is introduced.

This exterior cannot overlap the committed hierarchy: it is outside its enclosing
coarsest box. Its adjoining committed boundary uses the same LOD and lattice, so
no new cross-LOD seam is exposed. Interior replacement and its transitions remain
atomic. On full handoff, temporary coarse chunks in the new inner hole are hidden
in the same update that activates fine terrain and seams. Cancellation retains
only preview coordinates inside the latest target cache for the same configuration;
others are deactivated before normal cache removal. Configuration changes and
reset clear preview ownership. Preview intent is bounded by the existing coarsest
cache box; classification/scheduling uses the existing2ms preparation iterator.
Water/collision still follow their canonical readiness paths and are not made
ready by a visual preview. Diagnostics distinguish preview intent and drawable
resident previews while global handoff remains pending. This supersedes the strict
all-or-nothing rule only for nonoverlapping exterior coverage, not arbitrary LOD
replacement. Full interior regional replacement is not implemented by this slice.

Outer requests with current render-active intent are selected after edit requests
and before hidden outer cache work. This is one priority within the canonical
outer dispatcher, not another scheduler or resource. Activating an already queued
outer key requeues its current descriptor; stale duplicate queue entries continue
to fail the existing pending-dictionary check. Reset clears this priority queue.

### Placement-required request priority candidate, 2026-09-11

Residency and urgency are different: LOD0 Gameplay includes hidden visual cache
coordinates, so selecting all of it before LOD1 can delay a visible handoff.
The manager marks exactly the pending placement's LOD0 entering coordinates and
coarse Readiness keys as required. The mesher stores this bounded derived priority
set, selecting edit work first, then required near requests, then ordinary near
residency queues. Outer selection keeps visible exterior coverage ahead of required
outer requests, followed by hidden cache requests. GPU lanes and concurrency are
unchanged. Cancellation/commit clear priority; reset/removal clear ownership.
A queued request promoted to required is requeued with the same descriptor/time;
stale entries use the existing dictionary check. A demoted priority entry is
returned to its ordinary queue, never dropped. In-flight work is not restarted.
No visibility or collision readiness requirement is relaxed. Performance remains
subject to HILLS-PLACEMENT-PRIORITY-001/v1 comparison before acceptance.


Cancellation containment candidate: a moving target leaving the finest staged
box no longer cancels a placement that still provides coarse coverage there.
Only leaving the staged coarsest outer box, a configuration change, or returning
to the committed target cancels it. The existing full readiness/seam checks still
apply before committing; the newest requested target is staged immediately
thereafter. This avoids perpetual restarts when flight outpaces fine-detail
coverage. It does not claim the finest LOD follows instantly or relax boundaries.
The prior finest-box rule is superseded, not retained as another mode.


Handoff completion service candidate: the mesher tracks the subset of queued
regular keys currently required by the placement. Once it is empty and none of
the three existing regular lanes contains a required count/emit, transition work
gets first service ahead of hidden cache work. Unavailable transition work falls
through to the ordinary three-way scheduler. Required near/outer work therefore
still makes progress; no simultaneous dispatch or additional scratch resource is
introduced. Queue insertion/removal/dequeue/reset and placement priority clear
maintain the pending subset. Required in-flight checks inspect at most existing
lane batch slots. This is a priority refinement inside the canonical scheduler.


### Boundary-step placement candidate (2026-09-11)

The manager retains the canonical staged-set builder, readiness checks and atomic
publication. For movement within committed outer coverage at unchanged settings,
it selects the finest outer box that can advance toward its target while keeping
one coarse-region gap to both its committed parent and child. Anchors below the
outermost level remain even; each parent hole is exactly its staged child's box
halved. Only this outer box and its parent's hole change, requiring at most one
transition pair. Subsequent commits continue toward the latest target. Startup,
configuration changes and destinations outside committed outer coverage use the
same builder with all target boxes, retaining independent exterior publication.
This supersedes whole-hierarchy staging for ordinary movement; it does not add
mixed per-chunk LOD boundaries, another mesher or change authoritative terrain.

The manager owns selection and all staged state on the engine thread. New LOD0
intermediate interest outside the viewer's warm window marks that interest dirty;
the existing desired-chunk rebuild restarts the single canonical preparation
worker on the next update, even if the viewer has stopped. Resets clear the flag.
Edits retain existing source-revision invalidation. GPU lanes, two-face batches,
render publication and collision ownership are unchanged. Integer containment
and fixed finest-first ordering determine selection; the authoritative field is
independent of placement timing. One coarse-region gap prevents adjacent LOD
transition surfaces from sharing a boundary during intermediate placements.

This replaces the large shared completion barrier implicated by the matched
8/18/23-commit experiments. Raising GPU batch sizes was rejected by measured
frame regressions; arbitrary per-chunk refinement would require a new boundary
topology. Acceptance requires the unchanged standard figure-eight criteria,
including <=10 s full drain, frame/allocation comparison and boundary audits.

The boundary-step candidate is accepted for the2026-09-11 standard/fast scenarios in the validation ledger. Required active chunks precede hidden cache fill; near, outer and seam work share preferred GPU turns. Coarsest chunks outside committed coverage publish independently as each finishes, and are hidden wherever the subsequent finer placement replaces them. Preparation classification is sliced with a2ms target checked every32regions. No GPU lane count increases.


### Immediate outer-edge requests (2026-09-11 follow-up)

The earlier fine-first boundary staging admitted exterior requests only when the
coarsest staged box moved. Inner steps could therefore leave newly exposed
terrain entirely unrequested. The GPU publisher already publishes each completed
render-active region independently; the missing work was admission and ownership.

The manager now owns a separate exterior preparation iterator, refreshed when
the latest coarsest target changes. It requests coarsest regions outside committed
coverage nearest first, using canonical descriptors, classification, queues and
publication. Both iterators share the existing 2 ms preparation budget, checked
every 32 inspected coordinates. Pending LOD0 preparation precedes exterior
preparation; otherwise exterior preparation precedes further hierarchy discovery.

Exterior ownership survives inner-boundary commits and transfers to the hierarchy
when coverage adopts the region. Target/configuration changes prune obsolete
requests. Cancellation preserves independently owned requests; reset/disposal
closes both iterators. Exterior residents are disjoint from the unchanged committed
cache and are included separately in its complete-cache count check. Existing
source-revision invalidation applies. Terrain previews can publish without full
hierarchy or water readiness; this does not add independent water previews.

The canonical boundary selector prefers the finest legal step that advances its
largest target-offset axis, with deterministic X/Y/Z ties and a first-legal-step
fallback. This prevents small sideways fine adjustments from repeatedly delaying
the parent movement needed to reach the player. When committed LOD0 excludes the
player, a newly available finer step may preempt a pending coarser step through
existing overlap-preserving cancellation. Containment, seam gaps, field identity,
water readiness and replacement readiness remain mandatory for hierarchy commits.

The manager marks player-detail priority while committed LOD0 differs from its
current target. Required near regular work receives the foreground GPU turn;
required parent regions precede distant previews; after required regular geometry
finishes, its seams outrank distant previews. Edit dependencies retain precedence.
Stale outer queue entries are discarded before queue emptiness controls priority.
During player-detail catch-up, outer requests may occupy two of the three existing
regular scratch lanes, retaining one lane for near work. Otherwise the one-outer-
batch limit applies. Region batch sizes and existing callback scheduling remain
unchanged. These rules supersede the earlier unconditional one-outer-batch limit.

Validation and remaining tradeoffs are recorded in the ledger. Exact LOD0 box
recentering is distinct from LOD0 coverage at the player: the latter can occur
while the box is still offset and distant hierarchy work remains. This change
has not established a one-second bound for exact recentering or eliminated the
long full-refinement drain after the extended fast-flight scenario.


### Continuous exterior service (2026-09-12 candidate)

The player-detail rule could consume every outer batch with hidden handoff work,
even while independently drawable exterior requests waited. The seam completion
shortcut also ran before the existing 250 ms outer service guard. During catch-up,
one of every four outer batches now prefers visible work; the other three prefer
required parent work. Empty classes lend their whole batch to the other class,
and edit dependencies retain precedence. A batch stays in the class chosen by
its first admitted request: edits, visible regions, required parents or cache.
The same eight-region maximum and scratch lanes apply. A visible batch admits
only the first region level and its XY-neighboring coordinates; other requests
return to the pending queue. This bounds its shared atlas footprint to a 3x3
region area plus the normal sampling halo, with no Z restriction because river
inputs are two-dimensional. This avoids forming one shared river-atlas bounding
box across nearby parent work and distant previews or opposite visible edges.
The per-request mixed-batch candidate was rejected after measured allocation and
frame regressions; its failed results remain in the ledger.

The existing outer service guard runs before seam shortcuts. Seam completion
shortcuts cannot bypass queued/in-flight visible work. This bounds admission
fairness, not GPU completion time or full refinement time. Regular completed
render-active regions continue to publish independently through the same owner.

Visible outer work has one distance-priority queue containing region keys. The
pending dictionary owns the latest descriptor/revision; dequeue resolves that
canonical request and drops removed or inactive keys. Activation and deactivation
requeue pending work into its current class, preserving hidden requests when a
preview is demoted. In-flight revision rejection and geometry lifetime remain
unchanged. The queue rebuilds when the coarsest viewer anchor changes, matching
exterior discovery. World-space region-center distance includes height, with
explicit level/Z/Y/X scalar ties. Queue capacity is reused. The full-descriptor,
fine-anchor rebuild candidate is superseded, but changing queue storage alone
did not resolve the allocation regression; no such causal claim is established.

No worker, resource lane, field representation or mixed LOD boundary is added.
Manager ownership/pruning and atomic refinement/seam contracts remain in force.
Read-only queue diagnostics distinguish handoff work from render-active work;
those counts do not establish pixel visibility. Performance and acceptance belong
in the ledger. Large hierarchy replacements outside committed coverage remain a
separate refinement limitation; independent exterior service does not wait for
those replacements to finish.

### Direct near-detail recovery experiment (2026-09-12, rejected)

An experiment staged the smallest chain of final fine/parent target boxes when
the player left finest coverage, retaining the existing seam and readiness
contracts. It reduced transitions and scheduled requests, but increased actual
regular count submissions, allocations, frame tails and loading time in the
unchanged canonical route. It was reverted. Single-boundary advancement remains
the implementation; a visible intermediate LOD alone does not establish
redundant work. User prioritizes total work, loading speed and frames over
transition appearance. Exact measurements and failed source remain in the ledger.

### Exact cancellation ownership (2026-09-12 candidate)

The original single-boundary selection and preemption policy remains. Compact
recovery, direct chains and extra batch restrictions were rejected after measured
work, allocation or loading regressions. Their history remains in the ledger.

Cancellation records old entering keys in reused scratch buffers, stages the
actual replacement, then retires keys absent from its next/committed cache or
transition sets and independent fine/exterior owners. Buffers resolve directly
after staging or against committed sets when no next step is required, and clear
on reset. This replaces translated-box guesses that could leave orphan work or
discard useful overlapping requests. Existing field/version and atomic mesh, seam
and water readiness checks remain unchanged. This correction does not remove
the sequential hierarchy recovery limitation or guarantee instantaneous LOD0.

### Boundary-local readiness (2026-09-12 candidate)

A placement now waits for its newly active regular regions and entering transition
faces, not unfinished retained regions or unchanged levels. Existing visible
regions keep their existing jobs and visibility; the handoff does not remove
coverage there. Newly exposed regions still require current-field completion,
and each changed seam remains atomic with both sides of that boundary. The
manager submits entering seams before speculative cache fill and scans only
newly owned cache coordinates. Cancellation remains owned by actual sets. Water
readiness likewise covers newly active sea-level cells; unrelated water requests
continue independently. No geometry, shader, collision, or spatial layout changes.
This removes unrelated dependencies, but does not yet make a connected boundary
switch publish individual regions independently. Runtime acceptance is pending.

The first local-readiness run increased work and allocations and is not accepted.
The follow-on candidate defers hidden cache fill until the requested placement
is reached. A manager-owned bounded enumerator scans missing committed cache
cells using the existing classifier and GPU scheduler; target movement cancels
that scan. Active entering regions are still prepared for every handoff, so an
unbuilt hidden parent becomes required when exposed. Existing pending requests
and residents retain their canonical ownership and are not duplicated. The
background scan shares the placement CPU budget after visible work and counts
as unsettled work for the performance runner. Reset/destroy dispose it.

Status: the boundary-local readiness and deferred-cache experiments above were
rejected and reverted after both increased work and allocations. Current runtime
retains the original atomic boundary handoff with exact cancellation ownership
and bounded water reuse. See the validation ledger for preserved measurements.

### Shared chunk content publication (2026-09-12 prototype)

[Surface water](SurfaceWater.md#shared-chunk-lifecycle-prototype-2026-09-12)
owns the new terrain/water lifecycle contract. Terrain residency still releases
GPU scratch lanes immediately and satisfies terrain preparation. Draw activation
additionally requires the requested chunk's water output, when applicable. The
manager publishes water in the main-thread mesher completion phase before draw
commands are committed; the same phase refreshes blocked regular visibility.
This distinguishes prepared GPU resources from complete visible chunk content.
LOD handoff readiness includes those requested water owners, and edited candidate
groups retain their previous presentation until affected water is ready. There
is no second world state, alternate mesher or global water-loading barrier.
Validation remains in CHUNK-WATER-LIFECYCLE-001; prior independent-publication
water results do not establish performance for this prototype.


### Stationary LOD arrival reporting (2026-09-12)

The manager observes the real streaming target, existing preparation membership,
GPU request/residency state, committed draw eligibility and water ownership.
It never schedules work or changes publication. A stationary episode starts
from the last movement of at least 16 world units, confirmed after one quiet
second. Sampling runs at 1 Hz, with detailed existing handoff-readiness scans at
most every 5 seconds. Keep at most 256 samples and cap an episode at 240 seconds.
Episodes before the first complete settle are labelled `before-first-settle`;
subsequent movement arrivals are `stationary-arrival`. Movement, field
identity or visual-configuration changes end the old episode explicitly.

Measure first prepared and first presented nearby 27 LOD0 regions separately from
full backlog drain. Include bounds/anchors, the region containing the target at
each LOD, pending counts, request state/age, preparation and water blocking,
missing handoff regions/seams, commit/supersession counts, camera aim and a
collision-only view hit when available. A missing ray hit is unknown, and empty
regions or draw eligibility are not proof of pixel-visible ground detail.

Warn after 5 seconds of stationary missing near detail, then at 5 second intervals;
keep a readable inspector status and `voxel_lod_report` command. Save immutable
report snapshots asynchronously through the existing data filesystem worker
pattern; only copied values reach the worker. Keep one active write and one
coalesced pending snapshot, reporting coalescence/failure explicitly. Reports
are derived diagnostics, with no world data mutation or network authority.
Production figure-eight results reference the latest arrival report. Existing
external polling missed observation deadlines and cannot replace engine-owned
timestamps; do not add an alternate movement or terrain test path.

Reports live under `performance/lod-arrivals/` in the game's data directory;
the console prints the full saved path. A settled manual report is labelled
`snapshot-only-no-arrival-timing`, with null arrival milestones. Prepared means
GPU-resident or known uniform, not merely CPU preparation. The near-region
blocking categories are exclusive and sum with presented regions to 27.
They follow preparation, GPU residency, draw publication, then water ownership
in that order. Water can itself block draw publication, so a zero near-water
count does not rule water out; inspect the detailed missing-water count too.
Request age includes queue time; count/readback and emit/publication are combined
states, not independent GPU execution timings. Historical milestones retain one-second sampling resolution. V23 adds near-only
observations at a50ms requested cadence and records the actual maximum gap. A collision-only aim hit belongs to the actual
streaming player; it is not a rendered-depth measurement.

This retains the existing mesher and deterministic field. Alternatives rejected
for this slice: per-frame full readiness scans add avoidable observation cost;
external polling loses precise stop-time context; a second meshing path cannot
explain production scheduling. Validation is recorded in `LOD-ARRIVAL-REPORT-001/v1`
in the [ledger](../ValidationResults.md); the reporting change does not itself
reduce arrival time or establish seam correctness.

The first instrumented user reproduction confirmed ready nearby geometry held
behind the pending hierarchy: 27/27 prepared near regions remained unpublished
while required coarse regions and seams completed. The eventual commit changed
the aimed terrain region from LOD5 directly to LOD0 about 21 seconds after the
recorded stop. A field edit split that observation into two revision-specific
reports. See the ledger for timestamps, old staged destination, dependency counts
and qualifications. This identifies publication coupling as a design problem;
it does not measure the independent cost of seams or adopt an approximate seam
replacement. Rendering changes must retain coverage before releasing fine detail.

### Compact arrival publication experiment (2026-09-12; rejected)

The compact recovery, seam-priority, stable four-face batching and coherent
four-face batching experiments were removed after failing arrival acceptance.
Runtime placement, transition scheduling and scratch capacity remain at the
pre-experiment implementation. No approximate seam replacement was adopted.

Compact recovery retained the normal LOD0 box, reduced intermediate ring half
extents to four and retained configured coarsest reach. Exact transitions and
required water still gated the compact handoff; configured extents grew back
through existing boundary steps. This preserved independent deterministic mesh
inputs but did not remove the cross-level publication barrier.

On COMPACT-ARRIVAL-001/v3 fast, the same-world control drained in 18.760s and
observed nearby detail at 1.511s. Bare compact recovery drained in 41.236s and
observed detail at 8.287s, with 37.2% more total managed allocation. Stable
four-face batching reduced that candidate's first-detail time to 6.789s but
still failed the five-second target and drained in 42.281s. Seam priority and
spatial batch grouping did not improve the result. These comparisons reject
the tested implementation; they do not establish the cost of GPU seam
computation separately from scheduling, sampling or readback.

The canonical figure-eight returns to its starting area. Its final arrival is
not equivalent to landing at a new distant location. In compact samples all
27 nearby meshes were prepared while exact transition dependencies blocked
publication. This confirms that generation readiness alone cannot establish
visible arrival latency. A fresh manual arrival report is required to relate
the current complaint to target coalescing, old placement completion and
current-target dependency waits. Keep authoritative field determinism and
coverage before releasing coarse fallback as design constraints for a future
publication redesign. No local per-parent publication or skirt design is
implemented or validated by these experiments.

See COMPACT-ARRIVAL-001 in the validation ledger and
[retained raw evidence](../ValidationEvidence/CompactArrival/) for parameters,
failed candidates, invalid comparisons and limitations.


### Stationary destination recovery (2026-09-12; superseded prototype)

The manual arrival b9198d4a90a2435f968b6211621bdba7 completed nearby geometry
by2.776s but presented all27 near chunks only at21.795s. For roughly the
first11s it completed an earlier fine anchor, then advanced nested boundaries
through several intermediate anchors. This is a scheduling defect independently
of the separate full-hierarchy publication cost.

The recovery change uses the existing full placement builder when the target
anchor has remained stable for one second, lies outside committed fine coverage,
and the pending fine coverage also misses that target. VoxelManager owns
the stability timestamp and pending recovery flag. Existing cancellation retains
committed coverage, preserves requests resident or needed by the replacement,
and discards superseded-only requests. The replacement uses full configured
extents and exact transition/water readiness before the same atomic publication.
No field, mesh descriptor, shader, thread ownership or GPU buffer changes.
Timing affects scheduling only; canonical chunk and transition inputs remain
independent of loaded neighbors. The change does not authorize publishing an
unstitched fine chunk or removing coarse fallback early.

This bypasses obsolete-destination completion and serial boundary advancement
for a stopped far arrival. It does not reintroduce the rejected compact-ring
layout or its restoration workload. The existing moving-target policy remains
in effect while anchors change. `lod.retarget.staged` records previous/current
fine anchors and the abandoned placement age; `lod.retarget.committed` records
recovery duration. The five-second arrival objective and normal regression
criteria still require measurement. Local per-parent publication remains an
unimplemented alternative if the full hierarchy barrier remains too expensive.


The guarded recovery failed the next manual far-arrival check: report
68a3c431faf845f899854be42213c5fb prepared all27 near chunks by1.364s but
presented them at18.411s. Correct-target recovery started about0.699s after
the stop and took16.953s. Its initial dependencies included6525 coarse regular
regions and744 transitions; at15.400s,640 coarse regular regions and32
transitions still blocked ready nearby terrain. Thus the scheduling correction
does not resolve the full-hierarchy publication barrier. It remains unaccepted.

The next architectural slice must separate desired LOD layout from locally
committed spatial coverage. A replacement must wait only on geometry and
boundaries necessary for its own covered region, retaining coarse fallback
until that replacement is valid. Cache fill and unrelated far regions cannot
be dependencies of nearby publication. This requires one canonical owner of
actual coverage used by terrain activation, water activation, edits and arrival
diagnostics; adding a fine-only rendering bypass would make those consumers
inconsistent. Preserve immutable field/revision descriptors, exact stale-result
rejection and deterministic chunk generation. A balanced local refinement
scheme is a design candidate, not implemented behavior or a validated fix.
Its edge/corner transition compatibility and cancellation rules must be
qualified before replacing the existing placement implementation.


### Local terrain publication (2026-09-12; implemented, unaccepted)

The [local publication design](LocalTerrainPublication.md) now implements the
next slice described above. The first travel run presented nearby terrain within
1.46s of its recorded stop but did not converge; it is a failure, not acceptance.
The revised candidate tracks divergence between actual coverage and old layout
metadata, recovers stale destinations after100ms of actual motion quiet, and
retires out-of-range coverage ahead of refinement. Geometry generation and exact
transition shaders are unchanged. Tests and limitations are recorded under
LOCAL-COVERAGE-001/v1 in the validation ledger. Historical acceptance statements
above apply to their source revisions, not this redesign.


The current candidate replaces the single-level split described in the initial
local design with a balanced virtual refinement patch. It can retain LOD5 while
preparing LOD0 and its graded surroundings, then publish the replacement once.
No intermediate virtual parent is a publication prerequisite. Local additions
remain; v9 removes serial coarsening and prepares the final layout concurrently.
The existing atomic full-layout commit completes background work without making
nearby local refinement wait for it. The patch's exclusive topology ownership,
sliced readiness, field invalidation, bounds and diagnostics are documented in
[LocalTerrainPublication](LocalTerrainPublication.md). Runtime qualification is
pending; previous origin-return timing does not qualify this direct handoff.

V11 streaming qualification: generator46 fast background drain47.726s baseline
became16.212s; nearby LOD0 presented1.410s. Standard drain19.400s. Final seam
audits passed, but the10s drain target, allocation differences and final-swap
stalls remain open. The later two-service-per-frame experiment was reverted
after an editor startup crash; current code retains one GPU service per tick.
See LOCAL-COVERAGE-001/v2 and the local-publication design for evidence/limits.


### Exterior admission restoration (2026-09-13, unaccepted)

The local coverage redesign suppressed the earlier independent exterior
activation path. Exterior requests now enter the canonical local addition
queue with regular/water/seam readiness and overlap/balance checks. Preparation
alternates exterior and main slices within the existing2ms budget instead of
waiting for changed LOD0 placement preparation to finish. Immediate player
LOD0 remains first, exterior coverage precedes general layout convergence.
See [the local publication lifecycle](LocalTerrainPublication.md) and v13/v14
ledger runs for measured outcomes and outstanding correctness/performance.

V20 exterior service correction: exterior interest is now a scheduling input
independent of actual activation. It participates in the existing nearest-first
outer queue, lane eligibility and spatial batches before local publication.
Exterior publication may progress during distant cleanup while retaining the
existing overlap/balance/content/seam guards. See LocalTerrainPublication.md
and the v20 ledger entry; this is implemented but not performance accepted.

## Surface skirt experiment (unaccepted, 2026-09-13)

[TerrainSkirts.md](TerrainSkirts.md) owns the user-authorized approximate
boundary experiment on v26. Eligible regular X/Y edges emit lips in existing
count/output stages; marked transition records substitute for exact geometry
only where both sides qualify. Bounds include lip geometry and edit dependencies.
Exact fallback, authority and canonical field sampling remain. Before/after
results and failed preflight/timing deviations remain in the validation ledger;
this experiment does not supersede the prior exact-seam acceptance evidence.
The skirt runtime was reverted on2026-09-13 at user request. The current
PredictiveTerrainLoading.md experiment retains exact seams and preloads bounded
near-detail cache interest ahead of observed movement; it is not yet accepted.

## Complete nearby seam cache experiment (2026-09-13)

[CompleteSeamCache.md](CompleteSeamCache.md) owns the current unaccepted extension
of predictive loading. Existing exact transition keys retain all six directions
for bounded current/forecast coarse owners at each enabled adjacent LOD pair.
V2 derives this footprint from the actual near LOD0 ancestors plus one coarse
neighbor. Existing GPU stages, field identity checks and active-face publication
remain canonical; no shaders or approximate seam geometry changed. Independent
regular/seam service cursors use a 0.5ms soft budget and 48 inspections. This is
not a hard frame-time guarantee. Payload and cache-readiness diagnostics support
fixed before/after figure-eight evidence; additional cache work is not accepted
as a shipping improvement without the recorded performance gates.
