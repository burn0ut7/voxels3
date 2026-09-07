# Terrain Performance Research

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
