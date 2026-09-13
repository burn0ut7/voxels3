# Terrain Performance Research

For the current comparison of shipped games, editable voxel implementations,
arrival timing, and ranked experiments, start at
[LOD arrival research](#lod-arrival-research---2026-09-13). Earlier sections retain
their historical source and acceptance context.

The [2026-09-07 CPU capture review](CpuPerformanceReview20260907.md) follows up
the implemented CPU work using the user's stationary/figure-eight trace and
matching radius-512 result. It owns that capture's attribution and remaining
CPU/memory hypotheses; the historical source-only priorities below are not all
still pending.

The [GPU meshing study](GpuMeshingOptimizationStudy.md) owns the 2026-09-07
GPU source audit and extraction/renderer comparisons. Its proposed changes are
not implemented; the accepted chunk-preparation outcome below remains distinct.

Research context: 2026-09-03 measurements, reviewed against the documentation
owners on 2026-09-06. This is a record of investigation questions and alternatives,
not a selected implementation, approved backlog, or performance-acceptance claim.

[GPU meshing](../Architecture/GpuVoxelMeshing.md) owns the current pipeline and
publication contract. [Voxel foundation](../Architecture/VoxelChunkFoundation.md)
owns terrain state and CPU preparation. The [validation ledger](../ValidationResults.md)
owns measurements and decisions. The [research catalog](../smooth_procedural_voxel_terrain_resources.md)
owns external source descriptions and transfer limits.

## Evidence to Start From

- [Visual stress and loading evidence](../ValidationResults.md#terrain-visual-stress-001v1---256-goal-and-512-stretch)
  traces the eager gameplay-allocation failure and the replacement's expansion,
  contraction, geometry, queue, and frame measurements. The
  [visual scaling decision](VisualClipboxScaling.md) retains its design rationale.
- [Settled-frame attribution](../ValidationResults.md#settled-frame-attribution-and-camera-binding-cadence---2026-09-03)
  separates terrain manager/mesher cost from engine/editor tails and records the
  remaining strict performance failures. Functional scaling evidence must not be
  described as full acceptance.
- The schema-20 evidence records asynchronous count-readback latency and stage
  timings. Summed overlapping batch waits are not main-thread blocking time and
  do not alone prove the critical path or predict an optimization's benefit.
- The earlier atomic-handoff results remain under
  [CLIPBOX-LOD-HANDOFF-001](../ValidationResults.md#clipbox-lod-handoff-001v1---atomic-moving-placement-coverage).
  Use the exact source state and subsequent decisions when interpreting them;
  a failed early candidate is not proof that the current design must be replaced.

## Allocation and Pipeline Utilization

The count-to-CPU-allocation dependency is worth investigating, but its benefit
must be measured against total request-to-renderable and request-to-coverage
latency. First identify idle lanes with eligible work, underfilled batches,
callback-to-consumption delay, foreground/outer fairness, and which dependencies
actually delay placement. Use production telemetry before adding concurrency or
raising dispatch limits.

If evidence supports changing allocation, compare these alternatives:

| Alternative | Potential benefit | Main unanswered questions |
| --- | --- | --- |
| Bounded GPU page allocation | Keeps sizing/allocation near extraction. | Indexed draw representation across pages, fragmentation, exhaustion, reclamation, and generation-safe reuse. |
| GPU append followed by bounded compaction/copy | May preserve contiguous draw ranges with fewer CPU rendezvous. | Supported engine operations, temporary capacity, synchronization, and prior Vulkan failure reproduction. |
| Coarser CPU batch reservations or size classes | Reduces allocation/readback frequency while retaining CPU ownership. | Wasted capacity, cancellation granularity, latency, and draw overhead. |
| Better utilization of existing lanes | May remove scheduling bubbles without allocator changes. | Whether measured bubbles are avoidable without hurting priority, fairness, or frame tails. |

Engine API availability is not proof that a proposed allocator is safe. Any
selected design must establish bounded memory, stale/canceled-work cleanup,
publication identity, shader startup stability, and supported draw operations.
The production allocator remains unchanged until a replacement passes its
measured design and validation gates.

## Publication and Scheduling Alternatives

The removed `GpuVoxelTerrainStreaming.md` study proposed regional
availability-driven refinement and deadline-based scheduling. Those proposals
were not the implemented whole-placement contract. Its useful open questions
are retained here; historical prose is available in Git.

- Could a dependency-complete region or slab publish earlier than a whole
  placement while retaining coarse coverage and every required Transvoxel face?
  Establish the exact ownership and transition dependencies before choosing a
  smaller publication unit. This would replace the current contract, not add a
  fallback renderer beside it.
- Could dependency, age, time-to-player, and directional lead improve the
  existing bounded scheduler? Measure target-to-first-coverage, full refinement,
  maximum lag, deadline misses, and obsolete work. Current distance/priority
  behavior must not be described as an implemented deadline scheduler.
- Relate lead distance to supported player speed and measured tail coverage
  latency, including a safety margin. Reversals and sudden destination changes
  need explicit workload and product semantics; arbitrary teleports are not
  evidence about ordinary movement.

Geometry Clipmaps and GPU-Based Geometry Clipmaps inform incremental cache
updates; Far Cry 5 informs requested-versus-resident refinement; Sunset Overdrive
informs movement budgets and teleport constraints. These sources are routed in
[the catalog](../smooth_procedural_voxel_terrain_resources.md#research-question-router).
Their heightfields or authored assets do not define volumetric transition or
publication correctness. Transvoxel supplies local topology, not scheduling.

The old study's fixed-three-level restriction is superseded by the implemented
level-indexed hierarchy. Conservative rejection and arena record sizing now have
production owners in the architecture document; they are not pending tasks just
because the earlier study proposed them.

## Other Bounded Questions

- **Conservative rejection:** can a cheaper full-3D proof reject enough additional
  solid/air work to outweigh its own cost? Preserve uncertainty and caves; avoid
  the rejected recursive per-cell proof and duplicate height/noise approximations.
- **Draw/visibility submission:** does reducing per-arena submission work improve
  measured rendering cost? Evaluate its buffer, allocation, and publication
  implications separately from mesh-availability changes.
- **Engine/editor allocation:** which allocation stacks own the measured bytes
  and pauses? The historical trace did not identify those payload types. A low
  settled-manager cost does not rule out every moving-terrain interaction.

## Choosing a Follow-Up

Choose one question from fresh comparable evidence and record a concrete design
before implementation. Preserve the requested terrain quality, full 3D field,
transitions, coverage, bounded queues, memory, and correctness. A lower workload
is not an equivalent optimization.

Use the existing [figure-eight acceptance rule](../../AGENTS.md#figure-eight-performance-acceptance)
and [performance route](../AgentRoutes/performance-and-testing.md). Put any
additional scenario criteria and measured decision in the ledger rather than
creating a second acceptance checklist here.

## Source Review - 2026-09-06

Reviewed source: `ca8d5c3`, initially clean working tree. This review follows
chunk preparation, placement, meshing, publication, allocation, rendering,
diagnostics, player control, editor integration, and shader entry points. It is
a prioritized review, not an implemented optimization or a benchmark result.
Generated files and imported topology tables were not independently re-derived.
Build and live observation evidence is recorded in the ledger under
`SOURCE-REVIEW-2026-09-06`.

The current scene authors gameplay radius 8 and maximum visual level 4 (nominal
visual radius 128), whereas source defaults are 4 and 2. The running editor is
26.09.01c. Historical default-radius measurements cannot be treated as a fresh
baseline for this scene. No runtime source, scene setting, or user feature was
changed during the review.

### Chunk loading: highest-priority work

1. **P1: bound the actual configurable workload before allocating it.**
   `VoxelManager.TryValidateConfiguration` bounds level count and gameplay radius,
   but accepts any positive even `Lod0VisualHalfExtent` and `LodCacheHalfExtent`
   satisfying containment. `RebuildDesiredChunks` and `PrepareLodPlacement` then
   synchronously enumerate cubes. For example, equal half extents of 1024 pass
   those extent checks and request more than 8.5 billion cache identities per
   enabled level. This is an unchecked workload, not a slow GPU problem.
   Validate checked region counts, coordinate arithmetic, and a supported memory/
   work budget before changing applied state. Preserve supported visual-distance
   tiers and reject oversized requests without freezing or destroying the old
   placement. Choose limits from supported scenarios rather than arbitrarily
   restricting existing end-user view distance.

2. **P2: retain useful preparation across movement.**
   `RebuildDesiredChunks` cancels the warm worker, increments its revision,
   clears completed results, and reconstructs the pending coordinate list on each
   streaming rebuild. `StartWarmGeneration` repeats cancellation, revision
   advancement, and completed-queue clearing. Coordinates still desired with
   unchanged terrain can consequently be classified again; completed but
   unintegrated results are lost. Worker batches contain up to 256 results, and
   the next request waits for the previous worker. This proves redundant work
   is possible, not its current percentage or latency impact.
   Replace the duplicated restart ownership with one lifecycle. Retain results
   by coordinate and terrain revision while separately testing current desired/
   staged/committed membership. Preserve nearest-first scheduling, one bounded
   worker, cancellation on content changes, and stale completion rejection.
   Measure canceled queries, integration backlog, LOD0 publication latency, and
   placement lag before choosing a more complicated incremental work queue.

3. **P2: placement preparation still scans entire coarse caches.**
   Although unchanged level set construction is skipped and commit applies
   deltas, `PrepareLodPlacement` subsequently walks `stagedCache` for every
   supported coarse level to rebuild readiness. Changed level sets themselves
   are rebuilt through `AddHalfOpenBox`, then diffed, rather than updated by
   slabs. The existing LOD0 warm-window slab update does not optimize these
   coarse sets. With five enabled levels, the readiness loop can inspect four
   4096-coordinate caches for a placement step, including unchanged levels.
   First measure this main-thread phase. If material, retain exact outstanding
   dependencies and derive changed box/hole slabs. Do not simply skip unchanged
   levels: unfinished bootstrap/cache work can still be required by a new
   placement. Keep atomic publication and the single canonical level records.

4. **P2: remove obsolete generation bookkeeping and repair attribution.**
   `_generatedThisStream`, generation-batch counters, and several generation/
   integration timings are reset and read but no longer populated by the
   implicit-SDF path. `CompleteStream` runs immediately after starting warm
   preparation; its duration measures range application, not render readiness.
   Some UI text already labels this correctly, but the retained counters and
   chunks-per-second presentation still invite incorrect loading conclusions.
   `VoxelPerformanceProfiler` also requests `RebuildDesiredChunks`,
   `IntegrateGameplayChunks`, and `IntegrateWarmChunks` timing names without
   matching scope producers anywhere in current code. A zero timing for those
   names is missing instrumentation, not evidence that the phase is free.
   Remove dead backing state and obsolete profiler names together; retain user
   diagnostics by expressing logical membership, preparation, meshing, and
   placement completion accurately. Instrument real phases only. Version result
   schema changes and preserve old ledger/results rather than keeping a legacy
   telemetry implementation alongside the replacement.

### Meshing: next priorities

5. **P2: avoid request allocations when no batch exists.**
   `GpuVoxelMesher.ProcessGpuRenderTick` allocates an eight-entry regular request
   array whenever it finds an idle lane, before discovering an empty queue.
   `ProcessTransitionGpuRenderTick` does the same for transitions. Settled frames
   execute both paths. The declared element sizes are 64 and 96 bytes: together
   these allocate 1280 payload bytes per eligible idle render tick, plus array
   headers. At 1000 eligible ticks/sec that is 1.28 MB/sec of payload alone;
   this is source arithmetic, not a measured allocation rate or an explanation
   for the whole historical 26-29 KB/frame allocation figure.
   Allocate lazily after the first successful dequeue, or use authoritative
   pending counts to avoid entering empty submission paths. Preserve stale-queue
   draining and priority. No pool, extra GPU lane, or allocator redesign is
   needed for this fix. Compare moving and stationary allocation bytes/frame
   and frame tails with identical workloads.

6. **P2: discard canceled count results before allocation and emit.**
   `Remove` marks in-flight regular work canceled; transition removal tracks
   canceled generations. Nevertheless `AllocateAndEmit` and
   `AllocateAndEmitTransitions` allocate arena ranges and submit geometry for
   these results. Finalization then releases the canceled geometry. Avoidable
   work spans CPU allocation, emit dispatches, range release, and possibly arena
   growth. Reject canceled/obsolete candidates at the count-consumption boundary,
   retaining disabled entries where batch indexing requires them. Preserve
   count-generation checks, render completion barriers, re-entry semantics,
   cancellation telemetry, and scratch lifecycle. Never publish rejected work
   as a known-empty region. Measure cancellation-heavy movement and backtracking
   through the real player path before accepting this change.

7. **P2 investigation: transitions have no explicit service deadline.**
   `ProcessGpuRenderTick` services transitions only when no regular work was
   submitted. That includes warm regular work. Outer work has an explicit
   service-delay mechanism, while transition count continuation and emission
   can be deferred repeatedly under continuous foreground activity. Atomic
   placement needs those transitions, so more regular throughput can coexist
   with worse coverage latency. This is a scheduling risk; the review did not
   reproduce starvation. Compare per-pair publication tails and placement
   dependencies before changing priority. Prefer bounded service for required
   transition dependencies over raising batch sizes or adding scratch lanes.

8. **P2 investigation: bounded lifetime for retired camera resources.**
   `RefreshRenderCameras` retains every leaving `RenderCameraState` in
   `_retiredRenderCameraStates`; returning cameras get a new state.
   `EnsureVisibilityBuffers` similarly retains superseded visibility buffers.
   Camera states are disposed at mesher disposal, and retired visibility is
   reclaimed by visibility disposal, not by an ordinary retirement drain.
   Repeated camera switching therefore retains additional resources for the
   session. This is distinct from bounded arena high-water caching. Resolve
   command-list ownership and an engine-supported safe retirement boundary
   before releasing anything: historical premature resource reuse caused
   native crashes. Do not replace retention with an arbitrary frame delay.
   Validate repeated camera changes, readback completion, peak/retained memory,
   and clean editor startup as well as the canonical journey.

9. **P3: reduce allocator work without replacing allocator ownership.**
   `GpuTerrainRangeAllocator.Release` appends, sorts the whole free list, then
   scans it to merge ranges. Maintain sorted insertion and merge immediate
   neighbors instead, preserving overlap checks and first-fit behavior. This
   changes release from a full sort plus scan to a search and list shift; it
   does not eliminate fragmentation. Measure free-range counts and allocation/
   release time first. `FreeCount` and `LargestFreeRange` also scan, but most
   telemetry reads are infrequent and do not independently justify cached
   bookkeeping. A GPU allocator is a much larger, currently unproven proposal.

10. **P3 investigation: visibility uploads and diagnostic atomics.**
    Any dirty descriptor causes `UploadVisibilityDescriptors` to upload both
    complete capacity-sized CPU arrays. A contiguous dirty-range upload could
    reduce movement traffic while keeping GPU visible arguments exclusively
    GPU-owned. Separately, the visibility shader performs its frame-counter
    atomics even when neither measurement nor settled capture is enabled.
    Gate only diagnostic counting, preserving visibility output and the explicit
    audit's consumers. Neither proposal has a measured benefit yet. Preserve
    independent per-camera descriptors, new-buffer full initialization, bounds,
    active flags, and readback reset semantics.

### Simplification and compatibility assessment

- Keep the implicit field, persistent indexed geometry, conservative bounds,
  and unified level/pair records. No duplicate CPU mesher, dense chunk storage,
  or old per-level renderer was found in the production entry points reviewed.
  Replacing them with an octree or new meshing algorithm is not justified by
  the evidence collected here.
- Keep dedicated regular emit shaders. Their separation has recorded engine
  crash evidence and is not accidental complexity. Keep transition topology,
  caves, normals, coverage, view-distance tiers, and player controls intact.
- Concrete dead-code candidates are the unreferenced `voxel_regular_cell.hlsl`
  classification include, `SampleVoxelSdfGradient`, and the now-unused
  `PersistentGradient` / `PersistentSafeNormalize` helpers. Verify references
  again during deletion; remove orphaned dependent helpers only as part of that
  same change. Shader source cleanup still requires the documented clean-start
  check even if its expected runtime benefit is zero.
- `Editor/MyEditorMenu.cs` is a leftover template dialog, not a terrain tool.
  Remove it in a cleanup slice. `CustomTopDownController` remains player-facing
  behavior and must not be removed as template residue. Its local-input and
  scene-camera callbacks have no proxy guard; review actual attachment and
  multiplayer ownership before claiming remote players are safe or changing it.
- Current runtime/editor projects target .NET 10 and C# 14 and compile cleanly
  against the installed engine. There are no package references in the sbproj
  to upgrade. This establishes installed-build compatibility, not proof that
  the installation is the latest available release.
- Multiplayer terrain edits, collision generation, persistence, and multi-origin
  interest remain absent, as the architecture states. The configured 64-player
  maximum is not evidence of validated multiplayer scaling.

### Recommended implementation order

First repair misleading measurement fields/scopes and establish a comparable
unchanged-source baseline. Historical zero scopes cannot select a bottleneck.
Use the existing default journey unchanged when reproducing its scene/settings;
record the current authored radius-128 workload as a distinct scenario if it is
also to become an acceptance target. Do not relabel it as the default workload
or erase historical failed gates.

Next take the small allocation and cancellation-boundary fixes as separately
reviewable candidates, then evaluate retaining chunk preparation and reducing
coarse placement scans. Workload validation can be a separate correctness slice.
Consider transition scheduling only when dependency and latency evidence points
to it; allocator and draw changes follow measured need. Remove the superseded
path within each accepted slice rather than retaining compatibility branches.

For interacting optimizations, retain the unchanged baseline, isolated candidate
results, and a combined candidate result under identical parameters. Accept a
combination only when its measured behavior and correctness justify every
retained part; remove a losing standalone implementation rather than parking it
behind a toggle. Frame tails, publication/coverage latency, drain, allocations,
memory, geometry, and feature preservation all remain acceptance requirements.

#### Investigation design - before candidate implementation

The existing ownership and pipeline remain canonical. No shader, meshing
algorithm, terrain recipe, arena allocator, or publication unit is being replaced.
Production measurements will live in the existing result schema and real method
scopes; verbose logs remain opt-in, while run summaries/rejections remain sparse.

- Measurement control: remove obsolete materialized-generation counters, retain
  logical membership and user-readable status, and add real preparation,
  integration, placement-scan, and canceled-count observations. Schema changes
  must be explicit. Compare this control with the original before interpreting
  optimization effects.
- Preparation: prefer preserving completed compatible results first; retaining an
  entire in-flight worker is warranted only if measured cancellations justify
  its extra scheduling ownership. Content revision and current spatial interest
  remain separate validity conditions. One serialized worker remains the limit.
- Placement: first skip unchanged coarse levels only when the production mesher
  reports no pending or in-flight work for that level. Preserve scans for
  bootstrap/unsettled levels. This is smaller than adding a second dependency
  index or rewriting all set operations; measure remaining scans before expanding.
- Meshing: lazy request-array allocation needs no pool. Canceled count results
  can retain their original batch slots with disabled allocations and normal
  completion bookkeeping, avoiding both geometry and false empty publication.
- Configuration: use a checked combined coordinate budget for the LOD0 warm cube
  and enabled coarse caches. The recorded maximum supported seven-level layout
  requires 1331 + 6*4096 = 25907 coordinates; the next power-of-two budget 32768
  preserves every existing default 4/8 visual tier through radius 512 and reduced
  2/6 settings. Reject extents exceeding the budget before cubic multiplication
  or enumeration. Retained/staged sets remain bounded multiples of that budget.
  This bounds CPU coordinate work; it does not claim a new worst-case GPU-memory
  guarantee. No end-user distance tier is removed.

#### Completed-result retention refinement

The measured 1936 dropped completed results justify retaining already-computed
work without replacing the worker. During reprioritization, reuse the existing
missing-coordinate set to select compatible completed results, keep them and
their pending entries in matching FIFO order, then append nearest-first missing
coordinates for the next serialized worker. Compact the existing coordinate
buffer to exclude retained results. No dictionary, second worker, cache service,
or new authoritative state is needed. Content reset already clears both queues.
`StartWarmGeneration` becomes the sole movement-cancellation/revision owner and
must not clear retained completed/pending entries, including when no new work is
needed. Existing integration budgeting and membership checks remain mandatory.

For coarse-level scan skipping, additionally require resident count to equal
current cache count as well as zero pending/in-flight work. A bootstrap or
incomplete cache therefore retains the original conservative scan. Changed
levels still use the existing set/diff implementation; its remaining cost will
be measured before considering a slab rewrite.

## Implemented investigation outcome - 2026-09-07

The six selected opportunities are implemented in the chunk manager and existing
GPU mesher. See [the validation ledger](../ValidationResults.md#chunk-optimization-128-001v1---authored-world-investigation)
for fixed inputs, controls, individual/combined candidates, failed comparisons,
and the final acceptance decision. Runtime source diff
`2d510d558ec182906f7ca01573174e83fac6dc4e` identifies the accepted schema-23 code
before its commit. No shader, terrain recipe, extraction algorithm, persistent
arena allocator, or player feature was replaced.

| Opportunity | Implemented change | Evidence and limit |
| --- | --- | --- |
| Preserve preparation | Reuse compatible completed FIFO results; one movement cancellation/revision owner. | Final run retained 1320 results and discarded 132 no-longer-needed results. Content reset still invalidates queued work. Unfinished workers still cancel and serialize. |
| Reduce placement scans | Skip unchanged coarse caches only when complete/resident and free of pending work. | 62.72% fewer scanned coordinates and 36.42% lower placement phase time than measurement control. Changed/incomplete caches retain the canonical scan and set/diff path. |
| Skip canceled meshing | Reject canceled count results before geometry allocation and emission; preserve batch bookkeeping. | Final run skipped geometry for 8 nonempty canceled regular regions. Transition cancellation did not occur, so its savings remain unmeasured. |
| Remove idle allocations | Allocate request arrays after the first successful dequeue. | 169114 empty allocations avoided in the final moving window. Isolated candidate A reduced moving/stationary allocation by 884.888/1419.239 bytes/frame versus its instrumentation control. No pool or new container. |
| Replace obsolete diagnostics | Delete dead generation fields/names, attach scopes to real work, report preparation/placement and cached geometry fingerprints. | Schema 23 adds peak anchor lag separately from publication route lag. Whole-editor memory is not attributed to terrain. Rolling engine profiler snapshots are not whole-route phase totals; use explicit streaming totals for that comparison. |
| Bound oversized settings | Validate a checked combined preparation-coordinate budget before enumeration or applied-state changes. | Fixed invalid requests reject without changing geometry/revision; reduced settings and every default view-distance tier through 512 settle and restore correctly. This is a CPU-work budget, not a worst-case GPU-memory guarantee. |

Final synchronous streaming work was 1781.836 ms over the fixed journey, 26.59%
lower than exact original source in the same editor session. Maximum placement
anchor lag stayed at 2 regions; settled counts and every level/pair geometry
fingerprint match. Frame, publication, queue, allocation, and terrain-owned
memory checks satisfy the recorded acceptance bounds. No publication-latency
speedup is claimed: the ledger preserves two secondary tail-latency failures
against the unusually low instrumentation-only control, along with the passing
predeclared original and exact unchanged-source session comparisons.

Editor working-set variation was reproduced with unchanged code. Per the user's
explicit direction, acceptance looks for extreme, sustained, attributable growth
rather than minor whole-editor fluctuations. Historical memory failures remain
in the ledger. The work does not fix the separately documented transition-table
degenerates or the engine's retained-task hotload diagnostics. Matching sampled
regions in candidate/control audits have identical validity and geometry counts;
all final world fingerprints match. Collision, networking, persistence, shader
cleanup, and deeper allocator/transition-scheduler work are outside this slice.

Do not retain old implementations behind flags. Larger scheduling, pooling, or
coarse-slab rewrites were not justified by this evidence; the current bounded
changes remove measured redundant work without adding competing state owners.

## LOD arrival research - 2026-09-13

### Decision

Investigate targeted streaming prototypes before replacing the surface extractor.
The strongest near-term candidates are prompt recovery of stale local targets,
priority for the complete dependency set of a nearby replacement, and bounded
preparation ahead of movement. Retaining useful resident geometry is a second
tier candidate. A GPU allocation redesign or adaptive Dual Marching Cubes should
advance only if measurements identify extraction or synchronization as the
remaining limiting stage.

This is a research recommendation, not implemented behavior or performance
acceptance. The current working tree contains unaccepted local-publication work.
No runtime change or new benchmark was performed for this report. Existing
architecture owners and the validation ledger remain authoritative for contracts
and acceptance. The comparison covers normal movement, revisits, cold distant
arrivals, and edits; those experiences must be measured separately.

The evidence does not establish that every comparison game generates equivalent
terrain faster. It establishes several ways to make detail available earlier,
keep useful coverage while work continues, and reduce the amount of work required.
There is no matched cross-game benchmark with this generator, editable field,
water, view distance, hardware, and travel path. Treat apparent instantaneous LOD
as an experience to reproduce, not an externally measured extraction time.

### What the current measurements actually establish

The recent LOCAL-COVERAGE-001/v2 ledger includes these observations:

| Candidate and workload | Nearby preparation | Nearby presentation | Background drain | Qualification |
| --- | ---: | ---: | ---: | --- |
| v11 standard | 1.302 s | 2.304 s | 19.400 s | Drain failed; separate from nearby arrival. |
| v13 fast | Not extracted here | 1.978 s | 16.542 s | Drain failed. |
| v14 fast | 1.033 s | 2.033 s | 14.550 s | Drain failed; pacing/allocation remain unaccepted. |
| v14 standard | Not extracted here | 1.689 s | 19.590 s | Final saved state moved again; do not claim settled final queues. |

These are source-specific observations in [the ledger](../ValidationResults.md),
not a continuous benchmark trend across identical implementations. v15 water
priority is predeclared in the inspected ledger but has no recorded result there.
Its source change must not be presented as a proven speedup.

The automatic reporter has an important resolution limit. In
`VoxelManager.LodArrival.cs`, `LodArrivalSampleSeconds` is one second; sampling
waits for at least one second of quiet. Movement of at least 16 units ends an
episode. First-ready values are the first samples satisfying readiness, not
timestamps emitted by the actual completion events. Consequently a first sample
at about one second can include terrain that was already ready much earlier.
Even after the initial quiet gate, consecutive one-second samples only bracket
completion. Subtracting 1.033 from 2.033 does not prove an exactly one-second
preparation-to-presentation stall.

The reporter measures 27 coordinates around the streaming center. Proven-empty
regions count toward readiness, and activation is distinct from a pixel reaching
the display. Target-coordinate LOD, all-27 readiness, visible surface quality,
collision readiness, and global queue drain are different metrics. A return to
an already visited origin is not a cold arrival in new terrain.

There is also a separate, real one-second policy in `VoxelManager.cs`:
`StoppedTargetRecoverySeconds = 1`. In `UpdateClipboxPlacement`, unchanged target
anchors can trigger recovery after that interval if the staged layout is stale
or committed coverage differs from the requested layout. This is conditional
anchor-stability recovery, not a universal sleep before generating LOD0. It can
be investigated independently of the diagnostic sampling interval. A runtime
trace must establish how often it lies on the actual arrival path.

### Current dependency and ownership audit

This audit refers to the inspected source hashes listed below. The shared
working tree continued changing during report preparation; later candidate
changes and results are outside this snapshot. Relevant entry points are in
[placement](../../Code/Voxels/VoxelManager.cs),
[arrival diagnostics](../../Code/Voxels/VoxelManager.LodArrival.cs),
[water readiness](../../Code/Voxels/VoxelManager.Water.cs), and
[GPU scheduling](../../Code/Voxels/GpuVoxelMesher.cs).

`BeginRefinementPatch` in `VoxelManager.Coverage.cs` seeds the proposed topology
with desired coordinates in the nearby 3x3x3 cube, then closes surrounding
26-neighbor 2:1 balance. The patch may therefore require considerably more than
27 meshes. Planning has a 4096-leaf bound and a 0.75 ms soft update budget; one
refinement patch owns topology publication while it prepares. These are project
policies, not externally demonstrated minimum requirements for all voxel engines.

`PrepareLocalReplacement` checks current terrain descriptors, required water,
and every new transition face before `CommitLocalReplacement` switches coverage.
`IsChunkContentPrepared` in `VoxelManager.Water.cs` makes requested but unfinished
water a content dependency. The water service has a single preparation task.
Current source prioritizes admitted local water dependencies; it should be
qualified rather than proposed again as an absent feature.

The v14 outward observation recorded eight pending LOD5 additions with terrain
and seams ready but water missing. That proves a blocked exterior case, not that
water accounts for every LOD0 delay. Conversely, an earlier direct-refinement
record replaced 27 LOD3 parents with 314 leaves and 96 seams after 1.681 s of
preparation age, with about 0.483 ms of publication work. This demonstrates a
cheap local switch after readiness in one case. It neither establishes the
current critical path nor predicts cold LOD5-to-LOD0 latency everywhere.

The GPU mesher already uses persistent shared arenas, indirect drawing, three
regular scratch lanes, and batches of up to eight regions. Its render-tick
service interleaves regular work, exterior work, and transitions. Count results
return to CPU allocation before emission. The existence of this rendezvous is
source evidence; its share of end-to-end delay is not known from summed wait
totals. Concurrent waits cannot be added as if they were serial stalls.

The earlier recommendation to add local publication has partly been implemented:
ordinary movement can already publish local coverage before global completion.
Another generic proposal to "make publication local" would miss the current
question: how large is the actual local dependency set, when does it start, and
which last dependency prevents it from becoming visible?

### Comparison of implementations

| Reference | Mechanism supported by the source | Useful transfer | Important limit |
| --- | --- | --- | --- |
| Far Cry 5, GDC 2018 | Requested and resident nodes are separate; refinement requires loaded children; coarse roots remain resident. [^lod-fc5] | Derive visible coverage from availability, preserve a valid fallback. | Heightfield quadtree; not editable volumetric extraction. |
| Sunset Overdrive, GDC 2015 | Streaming budgets derive from traversal; runtime initialization was a bottleneck; teleports are exceptional. [^lod-sunset] | Budget arrival against movement and measure integration costs. | Authored city assets; no matched voxel timings. |
| Voxel Plugin Legacy 1.2 | A predictive invoker generates LODs ahead of fast characters. [^lod-vp1] | Prepare likely future detail before it becomes urgent. | Historical Unreal implementation; no stated arrival guarantee. |
| Voxel Tools, pinned source | Clipbox loading and local sibling activation; explicit transition and dropped-task concerns. [^lod-godot-code] | Study exact dependency lifetimes and ready-region handoff. | Godot ownership, ongoing TODOs, no faster-than-Voxels3 benchmark. |
| GPU geometry clipmaps | Incrementally updated height samples with reusable grid topology. [^lod-clipmaps] | Reuse data and update only new spatial regions. | A fixed heightfield grid cannot represent arbitrary caves. |
| Nanite and Voxel Plugin 2 | Hierarchical mesh streaming; a separate plugin generates interactive terrain on demand for Nanite. [^lod-nanite] [^lod-vp2] | Separate generation, residency, and rendering responsibilities. | Unreal integration is not a callable s&box implementation. |
| No Man's Sky, Worlds Part I | Developer reports a Dual Marching Cubes rewrite improving generation, vertex count, memory, and frame rate. [^lod-nms] | Investigate geometry workload if it proves dominant. | No published matched timings or exact current scheduler. |
| Enshrouded | Hand-authored voxel world built using imported shapes and stamps. [^lod-enshrouded] | A useful presentation target for editable terrain. | Authoring description does not disclose its runtime LOD machinery. |

#### Availability-driven rendering: Far Cry 5

Jeremy Moore's presentation distinguishes the nodes requested for streaming from
the nodes actually available. Its traversal descends only when detail is wanted
and all children are loaded. The resulting selection covers the world without
overlap, and the coarsest nodes are kept resident. These rules are directly
shown in PDF pages 20-26, including the diagram on page 25. [^lod-fc5]

For Voxels3, the transferable principle is coherent coverage from available data.
It does not justify immediately exposing a fine mesh while its seams are missing.
Our existing activation sets and local patches already implement part of this
separation. The next improvement should reduce or prepare their dependencies,
not add a second visibility mechanism. An infinite procedural world also cannot
keep every coarse root resident; retain a bounded interested region instead.

#### Movement budgets: Sunset Overdrive

Elan Ruskin's presentation budgets complete zone streaming at 1.6 seconds in its
illustrative traversal calculation. It then identifies runtime initialization,
rather than disk I/O, as the actual limiting factor. Teleporting outside resident
zones requires all seven surrounding zones at once and can leave the player
without loaded ground. The relevant material is on PDF pages 125-128 and
150-153. [^lod-sunset]

This is evidence against assuming that seamless play requires zero generation
time. An implementation can spend appreciable time preparing data and still
finish before the player reaches it. It also explains why CPU integration and
publication deserve their own measurements even when GPU extraction is fast.
The historical zone sizes and loading budgets are not targets for Voxels3.

#### Editable voxel references: Voxel Plugin and Voxel Tools

Voxel Plugin 1.2 explicitly supplies a predictive invoker positioned ahead of a
fast-moving character to generate future LODs. Its LOD documentation also makes
clear that coarse regions query fewer samples rather than first generating all
fine voxels in their volume. This supports bounded preparation ahead of the
player, not adopting its old Unreal architecture wholesale. [^lod-vp1]

Voxel Tools is particularly useful because its current code is inspectable.
At commit `0869fdc925bf6e270892e09ac3803a399d21d11e`, clipbox code allows LODs to
load in parallel and locally considers siblings before replacing a parent. It
also records problems where dropped requests can leave holes or blocked LODs.
These are concrete analogues of request lifetime and handoff problems already
encountered here, not proof that the external implementation has solved every
case. [^lod-godot-code]

Its performance documentation separately discusses main-thread budgets,
generation range analysis, and costly mesh destruction during rapid movement.
CPU generation optimizations do not automatically apply to its GPU backend.
Its API exposes generated-block caching as a tradeoff. These sources reinforce
the need to distinguish generation, engine integration, and resource lifetime,
and to measure the cache actually being proposed. [^lod-godot-perf]
[^lod-godot-api]

#### Heightfields, Nanite, and authored worlds

GPU geometry clipmaps store elevation in textures and reuse constant vertex and
index buffers. New movement updates only the newly exposed portions of nested
windows. This eliminates a topology-generation problem that a volumetric
isosurface still has to solve. The 2005 chapter's GPU restrictions are historical;
the reusable topology principle remains the relevant evidence. [^lod-clipmaps]

Nanite's documented asset pipeline constructs hierarchical clusters during
import and selects/streams clusters at runtime. That is not equivalent to
evaluating a new edited density field. However, it would also be wrong to claim
Nanite can never participate in runtime voxel terrain: Voxel Plugin 2 describes
on-demand interactive terrain generation with Nanite as its main renderer.
Neither document establishes a portable s&box integration or a cold-arrival
latency guarantee. [^lod-nanite] [^lod-vp2]

Enshrouded's developers describe a world assembled from rough imported shapes,
subscenes, and hand-placed voxel stamps. This is a useful comparator for visual
quality and editability, but its authoring pipeline does not prove that its LODs
are prebaked or reveal its streaming scheduler. Do not fill that evidence gap
with an inferred implementation. [^lod-enshrouded]

#### Algorithmic alternatives: No Man's Sky and Dual Marching Cubes

Hello Games' 2024 release notes explicitly report adopting Dual Marching Cubes,
with improvements to generation speed and memory as well as vertex count. Its
2017 world-generation talk overview describes the broader staged pipeline but
does not disclose the present scheduler. Neither source supplies a timing ratio
that can be transferred to this project. [^lod-nms] [^lod-nms-talk]

Schaefer and Warren's 2005 Dual Marching Cubes paper describes adaptive extraction
on dual grids and sparse representations of thin/sharp features. It is a serious
alternative to investigate, but the name in release notes does not prove Hello
Games uses that paper unchanged. It is also distinct from simply swapping in
Dual Contouring. [^lod-dmc]

For Voxels3, such a change would need a new meshing/LOD boundary design,
incremental edit behavior, material sampling, GPU allocation, and geometry
qualification. It might reduce work per replacement; it would not automatically
remove a stale-target timer, water blocker, or queued dependency. Defer this
larger prototype until those contributions have been isolated.

### Latency model and capacity

Use one destination and one source revision to measure this chain:

```text
detail becomes needed
    -> request admitted / target recovered
    -> required terrain, water, and seams become ready
    -> coherent local coverage activates
    -> first render submission containing that coverage
```

Terrain, water, and seams can overlap. Record their start/finish events and
dependency edges rather than summing their total elapsed times. Separate useful
execution time from queued time, callback consumption, stale-result rejection,
and intentionally delayed admission. Measure which dependency finishes last.

Prediction moves preparation earlier; it does not make the work free. A useful
design estimate is `lead distance >= speed * p95 dependency-ready time + margin`,
with the margin covering snapping, turning, and uncertainty. Use a measured
dependency-ready distribution, not the coarse stopped-arrival samples.

The current 512-unit chunks and nominal four-chunk fine half-extent give 2048
units of reach from a perfectly centered anchor. At speeds 2500 and 10000, that
distance corresponds to about 0.819 and 0.205 seconds of straight travel. This is
an illustrative center-to-edge calculation, not available prefetch time: actual
snapping, warm coverage, direction, and terrain boundaries change the lead.
It explains why even a one-second generation pipeline can fall behind a small
fine-detail area during rapid flight.

A stable streamer also needs useful service throughput to exceed the arrival
rate of required work. Longer lookahead cannot cure an indefinitely growing
backlog. Track generated bytes, canceled work, useful publications, and queue age
along with latency. Increasing radius in three dimensions can substantially
increase preparation and memory; prefer a bounded directional region plus
current safety coverage over an unlimited full-resolution cube.

### Ranked prototype program

The order below is an analytical recommendation based on current evidence.
Benefits are hypotheses. Change one mechanism per candidate, retain failed
results, and remove rejected implementations rather than keeping mode switches.

| Order | Experiment | Advancement condition | Primary risk |
| --- | --- | --- | --- |
| 0 | Event-level arrival and blocker attribution | Same-source control distinguishes subsecond completion and last blockers. | Diagnostic work perturbs timing. |
| 1 | Prompt local recovery when the staged target is stale | Material observed time is spent behind the one-second policy. | Excessive retargeting cancels useful work. |
| 2 | Complete-dependency priority and smaller safe replacement | Nearby patches wait for nonlocal or poorly prioritized dependencies. | Too-small handoffs create cracks or extra seam churn. |
| 3 | Bounded directional preparation | Work can finish before predicted entry without sustained queue growth. | Turns waste speculative work and memory. |
| 4 | Retain useful completed geometry across revisits | Rebuilds of identical recently used meshes are material. | Memory growth, fragmentation, stale edit reuse. |
| 5 | Reduce GPU rendezvous or change extraction | Actual extraction/synchronization dominates after the above. | Engine lifetime failures and major topology requalification. |

#### Experiment 0: measurement prerequisite

Extend existing production diagnostics with bounded event records for one target
and its replacement identity. Capture need, plan admission, stale recovery,
terrain ready, water ready, seams ready, activation, and render submission.
Record first events even during motion and when readiness occurs before the
first one-second observer sample. Preserve the existing all-27 observation and
global drain for historical comparison; add new metric names rather than
silently redefining them.

Capture the same source with and without detailed tracing to quantify observer
cost. Store only bounded records and flush outside timed movement. Render
submission is still not display scanout; visual captures are needed to qualify
the actual experience. This fits the existing diagnostic path and does not
require a new scene, test mesher, synthetic generator, or separate test project.

#### Experiment 1: prompt stale-target recovery

When actual local coverage is too coarse and the staged target no longer serves
the current requested region, investigate recovery driven by that state instead
of waiting solely for anchor stability. Preserve useful compatible preparation,
old valid coverage, and existing revision checks. Do not indiscriminately
restart the full plan every frame or merely set every timeout to zero.

The fixed candidate should specify which mismatch qualifies, how often a new
plan may replace an existing one, and what useful work survives. Compare time
lost before correct-target admission, cancellation count, obsolete emitted
geometry, near deadlines, and moving frame tails. Reject it if it replaces a
small stopped delay with repeated cancellation or higher sustained backlog.

#### Experiment 2: finish the nearby replacement as a unit

Build on the existing local patch and placement-priority sets. The priority of
a nearby handoff should reach each necessary terrain region, transition face,
and water request. Keep background fairness so exterior coverage still grows.
Qualify the existing v15 water-priority change first; do not layer a duplicate
water queue or worker onto it without a measured need.

Then, if patch dependency counts are excessive, investigate the smallest
coherent volume that gives the target its required detail. A smaller patch may
need extra external seams; it cannot publish arbitrary individual leaves under
a coarse parent. Transvoxel closes adjacent 2:1 boundaries, not arbitrary LOD
differences. Avoid generating and displaying temporal intermediate parents
just to reach the requested fine leaves. [^lod-transvoxel]

Measure dependency count, last-blocker category, useful near publications,
temporary seams, peak retained geometry, and frame cost. Inspect transitions
during movement and edits, including edge/corner neighbors and water shores.
A final settled seam audit alone cannot qualify transient coverage.

#### Experiment 3: prepare ahead while preserving current coverage

Let the current manager derive a capped future interest region from observed
movement and a measured preparation horizon. Feed descriptors through the same
CPU preparation, GPU meshing, water, retention, and cancellation paths. Current
coverage remains the sole visible owner; a predicted region does not move the
player's collision interest or become a second authoritative world.

The forecast must include enough surrounding graded terrain and seams to permit
a safe future handoff. Preparing only LOD0 meshes can leave the actual blocker
untouched. Retain a symmetric safety area for abrupt turns and allow speculative
requests to lose priority without evicting currently needed data. Fix the
lookahead cap and policy before each candidate run.

Compare missed detail deadlines during continuous travel, first-arrival time,
prediction hit rate, discarded bytes, memory, and background drain. If the
current pipeline cannot sustain the required work rate, optimize that limiting
stage before expanding the horizon. Do not hide failure by reducing the
recorded movement speed or view distance.

#### Experiment 4: retain the right cache

Distinguish CPU density samples, prepared descriptors, GPU meshes, seams, and
water. Keeping CPU samples cannot eliminate extraction and publication by
itself. Retaining already generated meshes and their compatible dependencies
may help revisits, but cannot accelerate a truly uncached first arrival.

The removed generated-density persistence experiment already failed to improve
its measured full-drain median: 30.485 s disabled versus 30.578 s enabled. That
result rejects that implementation and workload, not every possible cache.
See [the preserved experiment](GeneratedTerrainCacheExperiment.md). Do not
repeat a general "cache terrain" proposal without identifying the exact reused
product and the work it avoids.

Any new retention policy needs fixed byte limits, stale-world/recipe/edit
invalidation, and eviction rules within the existing resource owner. Compare
fresh destinations with revisits separately. Advance only for measured avoided
work and improved arrival under an acceptable memory bound.

#### Experiment 5: GPU scheduling and extraction research

First measure eligible idle-lane time, batch occupancy, count-to-allocation
rendezvous, callback-to-consumption delay, and actual GPU execution. If the
dependency path is dominated by avoidable service gaps, consider bounded
scheduling changes using the existing resource model. If allocation dominates,
revisit reservations or GPU allocation under the existing
[GPU study](GpuMeshingOptimizationStudy.md).

Prior evidence includes a reverted two-service experiment that crashed during
startup and compact-emission work that failed a transition latency criterion.
Neither proves all alternatives impossible; both rule out casually increasing
lanes or service count without engine-lifetime and clean-start qualification.

Only if extraction remains dominant should an adaptive Dual Marching Cubes
prototype displace the current mesher. Require an explicit replacement design
and the real playable-world entry point. No parallel reference terrain or
permanent alternate production extractor is proposed. New shader, allocation,
or draw APIs require installed s&box evidence and compiler/runtime validation;
external engine source is not permission to use an API here.

### Evaluation and acceptance

Preserve the existing standard and fast figure-eight scenario parameters,
source/world identity, warmup, viewport, and comparison rules. The latest nearby
five-second gate is a historical acceptance check, not a definition of an
instantaneous experience. Do not silently replace it or mix new event-based
metrics with old one-second observations.

For a future responsiveness objective, propose separately: resident or correctly
prefetched detail activates within 100 ms of becoming required, and normal
travel has at least 99% of required-detail entries ready by their deadline.
These are engineering targets for discussion, not empirical human-perception
thresholds or newly accepted project criteria. Cold unseen arrivals need an
independently measured distribution before committing to a numerical promise.

Use the existing figure-eight for regression and define any additional
production-path scenario in the ledger before its first run. A reproducible
cold distant stop should fix the actual route/destination and initial residency;
an origin return cannot substitute. Abrupt reversal, parent faces/edges/corners,
negative coordinates, edited shores, and cave entry are relevant coverage cases.
Preserve every failure and source/environment difference. Additional scenarios
do not replace or retune the canonical route.

For each candidate, report target activation p50/p95/p99/max, preparation and
last-blocker intervals, moving missed deadlines, full drain, frame pacing,
allocation, terrain/GPU memory, and cancellation/reuse. Maintain zero unsafe
publication and current source-revision correctness. Check actual rendered
boundaries during movement as well as final geometry. Any accepted regression
or canonical workload change requires the project's explicit approval process.

Multiplayer adds separate readiness: a client must have the correct recipe and
authoritative regional edits before claiming current terrain. Prediction here
means preparing presentation ahead of movement, not inventing or authorizing
terrain edits. Preserve the canonical mutation and replication paths; measure
join/correction convergence if those paths change. A local single-player route
does not qualify multiplayer arrival.

### Research disposition

Adopt the distinction between requested detail, resident products, and visible
coverage as the basis for further investigation. Prioritize experiments 0-3.
Conditionally investigate bounded resident reuse. Defer wholesale renderer,
allocator, or mesher replacement until critical-path evidence supports it.
Retain current topology and field correctness requirements.

The unresolved question is how much of current arrival comes from conditional
target recovery, dependency service, actual generation, and final integration.
Public implementations explain useful mechanisms but cannot answer that local
timing question. The next concrete deliverable should be an event-resolved
arrival trace and one isolated candidate, not another broad architecture rewrite.

### Evidence identity and sources

Local source inspection used the working tree above HEAD `fd8b209`; that commit
alone does not identify the uncommitted prototype. SHA256 values at inspection:

| File under Code/Voxels | SHA256 |
| --- | --- |
| VoxelManager.cs | `54482D3FFBA44246DF0E41D4D2722CE73B821D16252CA86227CA55DC44B1F772` |
| VoxelManager.Coverage.cs | `AE41BFA97373C6A0148E10272286A57B71A7CAB059983135B2B8A45D62F46375` |
| VoxelManager.LodArrival.cs | `19503D94C1B55E4D37D5119FD4450A796C53B918A5174F28FC4DE94A29906603` |
| VoxelManager.Water.cs | `EAB32A5FDA2965424ADE1E591925BA77451DBBF5F993F2BAAEF0DD053B193026` |
| GpuVoxelMesher.cs | `EC29EAE9365C0CE8002C418CDE867D40B10BB5B63DE28D5564EE489C0724A780` |

Primary external sources were inspected on 2026-09-13. Historical presentations
describe their named implementations, not necessarily current versions. Only
the session overview was inspected for the 2017 No Man's Sky talk. Far Cry 5 and
Sunset Overdrive PDFs were downloaded and their relevant page text inspected;
the Far Cry traversal slide was also rendered to verify its diagram text.
No external benchmark was run. Public claim summaries below are intentionally
limited; analytical recommendations above are specific to Voxels3.

[^lod-fc5]: Jeremy Moore, Ubisoft, *Terrain Rendering on Zeta / Terrain Rendering in Far Cry 5*, GDC 2018. [Presentation](https://media.gdcvault.com/gdc2018/presentations/TerrainRenderingFarCry5.pdf), PDF pp. 20-26 (printed slide 24 is PDF p. 25). Requested/resident distinction, persistent coarse coverage, and child-ready traversal.
[^lod-sunset]: Elan Ruskin, Insomniac Games, *Streaming Tech in Sunset Overdrive*, GDC 2015. [Presentation](https://media.gdcvault.com/gdc2015/presentations/Ruskin_Elan_SunsetOverdriveStreaming.pdf), PDF pp. 125-128 and 150-153. Movement budgets, initialization cost, and teleport constraints.
[^lod-vp1]: Voxel Plugin, *World Size and Level Of Details*, legacy version 1.2. [Documentation](https://docs.voxelplugin.com/1.2/core-systems/voxelworld/world-size-and-level-of-details), especially "Invoker Component With Prediction". Historical predictive preparation and coarse sampling.
[^lod-godot-code]: Zylann and contributors, Voxel Tools, commit `0869fdc925bf6e270892e09ac3803a399d21d11e`. [Clipbox implementation](https://github.com/Zylann/godot_voxel/blob/0869fdc925bf6e270892e09ac3803a399d21d11e/terrain/variable_lod/voxel_lod_terrain_update_clipbox_streaming.cpp#L1449), `update_mesh_block_load`, plus opening comments on parallel loading and dropped tasks. Pinned implementation evidence, not a measured speed comparison.
[^lod-godot-perf]: Zylann and contributors, *Performance*, Voxel Tools latest documentation, accessed 2026-09-13. [Documentation](https://voxel-tools.readthedocs.io/en/latest/performance/). Main-thread budgets, Vulkan resource destruction, and generator optimization/backend limits; version-specific observations.
[^lod-godot-api]: Zylann and contributors, *VoxelLodTerrain*, Voxel Tools latest API documentation, accessed 2026-09-13. [API](https://voxel-tools.readthedocs.io/en/latest/api/VoxelLodTerrain/). Generated-block caching as a memory/performance tradeoff.
[^lod-clipmaps]: Arul Asirvatham and Hugues Hoppe, *Terrain Rendering Using GPU-Based Geometry Clipmaps*, GPU Gems 2, 2005. [Chapter 2](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry), sections 2.1-2.2. Heightfield windows, incremental updates, and constant geometry buffers.
[^lod-nanite]: Epic Games, *Nanite Virtualized Geometry in Unreal Engine*, documentation served as UE 5.8, accessed 2026-09-13. [Documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/nanite-virtualized-geometry-in-unreal-engine), "How does Nanite work?". Imported clusters and on-demand residency; no s&box compatibility claim.
[^lod-vp2]: Voxel Plugin, *An Overview of Voxel Plugin*, documentation labeled 2.0p8, accessed 2026-09-13. [Overview](https://docs.voxelplugin.com/getting-started/working-with-voxel-plugin/). On-demand interactive terrain and Nanite rendering; distinct from legacy 1.2 invoker evidence.
[^lod-nms]: Hello Games, *Worlds Part I*, 2024. [Official update](https://www.nomanssky.com/worlds-part-I-update/), "General Engine Improvements". Developer-reported Dual Marching Cubes changes; no absolute or relative benchmark supplied.
[^lod-nms-talk]: Innes McKendrick, Hello Games, *Continuous World Generation in No Man's Sky*, GDC 2017. [Session overview](https://www.gdcvault.com/play/1024265/Continuous_World_Generation_in__No_Man_s_Sky_). Pipeline scope only; full talk content not used as implementation evidence.
[^lod-dmc]: Scott Schaefer and Joe Warren, *Dual Marching Cubes: Primal Contouring of Dual Grids*, Computer Graphics Forum, 2005. [Paper](https://people.eecs.berkeley.edu/~jrs/meshpapers/SchaeferWarren.pdf); [publication record](https://doi.org/10.1111/j.1467-8659.2005.00843.x). Adaptive extraction and thin-feature representation; no Voxels3 timing prediction.
[^lod-enshrouded]: Keen Games, *Enshrouded World Design Team Reddit AMA Recap*, official developer article, accessed 2026-09-13. [Article](https://enshrouded.com/en-US/news/enshrouded-world-design-team-reddit-ama-recap), "How did you create the world of Embervale?". World-authoring evidence only.
[^lod-transvoxel]: Eric Lengyel, *The Transvoxel Algorithm*, original dissertation 2010; author-maintained page accessed 2026-09-13. [Algorithm and references](https://transvoxel.org/). Exact adjacent 2:1 transition problem; not a complete streaming scheduler.
