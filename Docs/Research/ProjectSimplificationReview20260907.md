# Project simplification and performance review

Date: 2026-09-07. Status: research and source review; no runtime change, new benchmark, or performance acceptance. Reviewed HEAD `1e97e0d` plus the existing uncommitted storage/paging integration. Those runtime changes belong to ongoing work and are not accepted by this document.

## Assessment

The project has progressed beyond procedural rendering into live authoritative deformation, CPU collision, regional multiplayer transfer, and an in-progress disk-backed page lifecycle. Its central ownership is sound: procedural generation plus committed correction pages define terrain; visual geometry and collision are derivatives. The main simplification opportunity is to stop reconstructing or observing the same information repeatedly. Replacing that ownership model, removing synchronization, or adopting a new mesher is not justified by the current evidence.

There is considerably more implemented behavior than fully qualified behavior. A good-looking world, successful save, or high average FPS does not close the existing frame-tail, memory, lifecycle, multiplayer, and contact gates. Finish comparable measurements before stacking structural optimizations onto the current storage candidate.

This document owns the cross-system deletion and simplification shortlist. Existing subsystem research continues to own deeper algorithm proposals; the [validation ledger](../ValidationResults.md) remains the only owner of executable workloads and acceptance decisions.

## Review coverage and current state

The review followed all six project routes and traced the playable-world entry points across the following owners. Source symbols below are navigation anchors; line numbers can move during the ongoing implementation. This is a broad source and recorded-evidence review, not a proof of every shader instruction, protocol interleaving, or runtime edge case.

| Area reviewed | Current source evidence and assessment |
| --- | --- |
| World orchestration and streaming | [VoxelManager.cs](../../Code/Voxels/VoxelManager.cs): `OnUpdate`, configuration validation, desired-window rebuilding, level/pair state, preparation, placement readiness and commit. One manager orchestrates many responsibilities; its roughly 4,000 lines alone do not establish a defect. |
| Field and generation | [TerrainField.cs](../../Code/Voxels/TerrainField.cs), [VoxelChunk.cs](../../Code/Voxels/VoxelChunk.cs), [ProceduralTerrainSdf.cs](../../Code/Voxels/ProceduralTerrainSdf.cs): immutable content versions, regional readers, conservative bounds, generator v5 and build-local XY reuse. Paging adds mutable residency without changing sample authority. |
| GPU lifecycle | [GpuVoxelMesher.cs](../../Code/Voxels/GpuVoxelMesher.cs), [descriptors](../../Code/Voxels/GpuSdfDescriptor.cs), [contracts](../../Code/Voxels/GpuTerrainContracts.cs), both scratch owners, [allocator](../../Code/Voxels/GpuTerrainRangeAllocator.cs), and authored shader stages/includes: count/readback/allocation/emission/publication, transitions, persistent drawing and visibility. Regular and transition pipelines have materially different engine constraints. |
| Collision and actors | [VoxelCollisionWorld.cs](../../Code/Voxels/VoxelCollisionWorld.cs), [VoxelCollisionMesher.cs](../../Code/Voxels/VoxelCollisionMesher.cs), generated table ownership and manager fixed-update readiness: bounded workers/results, native installation, retained support, interest union and actor holds. Full collision acceptance remains open. |
| Deformation | [Manager deformation](../../Code/Voxels/VoxelManager.Deformation.cs), [GPU deformation](../../Code/Voxels/GpuVoxelMesher.Deformation.cs), [benchmark](../../Code/Voxels/VoxelManager.DeformationBenchmark.cs): validated ordered edits, local invalidation and coherent visual groups. Gameplay responsiveness and complete queue drain are different measurements. |
| Multiplayer | [Networking](../../Code/Voxels/VoxelManager.Networking.cs), [replication](../../Code/Voxels/VoxelManager.Replication.cs), [manifest](../../Code/Voxels/TerrainReplicationManifest.cs): host tool admission, player lifecycle, regional absolute page transfer, fragmentation, acknowledgement, epochs and coverage. Host migration deliberately disconnects because replicas lack the full world. |
| Persistence | [Store](../../Code/Voxels/TerrainFieldStore.cs), [codec](../../Code/Voxels/TerrainFieldCodec.cs), [storage pump](../../Code/Voxels/VoxelManager.Storage.cs): immutable page files, checkpoint visibility, reader/file retention, asynchronous reads and eviction sweep. These were uncommitted during review; feature and performance qualification remains incomplete. |
| Profiler and controls | [Profiler](../../Code/Voxels/VoxelPerformanceProfiler.cs), [result schema](../../Code/Voxels/PerformanceTestResult.cs), manager sampling/completion, deformation observations, [editor tools](../../Editor/VoxelMcpTools.cs): real production entry points and structured evidence, but differing scopes and completion meanings need attention. |
| Authored integration | [Scene](../../Assets/scenes/basic_example.scene), [player prefab](../../Assets/prefabs/terrain_player.prefab), [project](../../voxels3.sbproj), input/collision/platform settings, custom controller and editor entry points. Scene authors gameplay radius 8 and visual radius 512; project advertises 64 players at 50 Hz. Those are configuration values, not demonstrated multiplayer capacity. |

Generated scene/shader binaries and generated collision tables are not deletion candidates based on an apparent lack of handwritten callers. Research-only water/gas/stability/storage proposals are not implemented feature claims. Architecture prose is also unevenly current: the foundation's future-mutation wording and incomplete-transport description lag the implemented deformation/replication owners.

## Performance evidence: what it actually establishes

| Evidence | Recorded result | Limit on interpretation |
| --- | --- | --- |
| Latest recorded canonical current-source figure-eight `b94bb84e65d34a72a21a8f67c78120c5` | 121.91774 s; 921.37 FPS; CPU p95/p99/max 1.4864/3.0523/18.2105 ms; GPU p95/p99/max 1.08552/1.41215/2.39944 ms; 112,356 samples, no frame truncation; collision 4913/4913 ready. | Ledger section “Canonical figure-eight current-source rerun preflight”: repeated hotloads confounded process peak 4,757,835,776 bytes; overall acceptance explicitly unqualified. This run predates the current paging integration. |
| Its paired `63ebf86a` comparison | FPS -1.80%; CPU p95 +6.91%; p99 -0.20%. | Numerical similarity cannot waive the unresolved memory and lifecycle findings. An older accepted CPU baseline is not automatically comparable to today's edited/storage workload. |
| [Collision profile](CollisionProfile20260907.md), candidate 14 | 813.733 FPS; CPU p95/p99 2.1459/3.8133 ms; 28,887 allocated bytes/frame; native creation max 13.6169 ms against a 10 ms criterion; 19 contact rays passed. | Readiness and rays passed while original performance acceptance failed. Native installation is not preemptible merely because integration has a soft budget. Later candidates did not establish overall acceptance. |
| [Deformation profile](TerrainDeformationProfile20260907.md) | Sampled inclusive CPU identifies collision build, regular scratch preparation, correction sampling and range queries as substantial work. | Inclusive costs overlap and are not GPU kernel timings. Recheck after current regional-reader changes. |
| [Multiplayer profile](TerrainDeformationMultiplayerProfile170918.md) | Main-thread sample shares: animation 17.46%, manager 11.19%, collision integration 4.26%, correction ranges 2.13%, replication dispatch 0.47%; GC intervals 8.46, 8.99 and 18.62 ms. | Editor-only capture starts about 51 seconds after join, with continuing edits. No matched pre-join capture; dispatch is not total networking cost; local second-client contention is not isolated. |
| [Idle investigation](TerrainDeformationIdleProfile171551.md) | Quiet-interval shares: scene rendering 33.93%, animation 24.78%, manager 2.10%. | Descriptively selected interval, overlapping samples. The later 910.9/909.5 FPS observations failed stationary criteria; 900+ FPS is not accepted. Terrain rendering is outside much of manager update. |
| Latest storage ledger entries | Checkpoint/reopen preserved 16 page records and regional hashes; bounded ownership probe achieved complete drain within its 30-second bound. Integrated paging history attempt had preflight recorded at review. | Earlier reopen observation had only 1243/4913 collision regions ready at four seconds despite edit-only pending being false. It did not establish the separate ten-second drain gate. Eviction, failed I/O, multiplayer and comparable performance still require their own results. |

The [GPU study](GpuMeshingOptimizationStudy.md) and [scan/arena study](GpuReductionsAndArenaEfficiency.md) already reject particular compact-emission and scan experiments. A 24 MiB vertex-arena experiment measured 16.67% lower reservation but lacked a completed matched final control; source was restored to 32 MiB. Do not present these as accepted optimizations or rerun them under new names without new evidence.

## Profiler review

### Keep the useful structure

The figure-eight uses the real local player and streaming pipeline, counts its own loops, separates movement from a later stationary window, records bounded scalar observations, and appends results after capture. CPU scopes name actual work. GPU queue/readback/publication metrics are useful even without kernel timestamps. Explicit geometry audits and fingerprints provide correctness evidence beyond FPS. These are worth preserving.

### Resolve measurement ambiguities before deleting instrumentation

1. **Whole route versus last 200 frames.** `VoxelPerformanceProfiler.Capture` retrieves a 200-frame history and computes percentiles from it. The manager's frame distributions cover a much longer window. A scope's p99 cannot explain the entire route's p99, and nested scope totals cannot be summed as exclusive work. Record actual history sample count, capture phase and timestamp, and label this as trailing profiler history. Zero samples should remain distinguishable from zero work.
2. **Visual settle versus world settle.** `TrySaveCompletedPerformanceTest` checks preparation, regular/transition work and placement. It does not explicitly require `_collision.Settled`, storage reads or replication completion. That is a visual settle definition, not proof of a quiescent world. Preserve the existing scenario meaning; first report subsystem backlog at both boundaries. If a new fully settled-world measurement is necessary, document why the old definition is insufficient and follow ledger versioning rather than silently changing comparable windows.
3. **Different distribution semantics.** GPU `MetricSamples` uses a fixed array, includes overflow in its mean, and computes tails/max over retained samples. Collision `CollisionSamples` grows a bounded list, copies on capture and computes its mean over retained samples. Manager frame sampling separately reports truncation and nulls a truncated maximum. GPU frame capture has another policy. Consolidation requires an explicit common contract for observed/stored counts, invalid values, overflow, mean, percentiles and maximum; otherwise a cleanup changes the metric while pretending to preserve it.
4. **Observer cost exists outside explicit tests.** `UpdatePerformanceOverview` samples during ordinary play and calls `CompletePerformanceWindow` periodically. Ordinary completion sorts frame/GPU arrays and constructs summaries; its profiler-history capture is correctly gated to an active figure-eight. Stationary/deformation capture explicitly retrieves profiler histories. Collision distributions also collect outside the benchmark. Measure these boundaries and their allocations before claiming profiling is free or removing useful status. Capture overhead may fall outside the window it finalizes and inside a subsequent engine frame.
5. **Attribution remains incomplete.** The fixed scope list does not separately expose storage pump/sweep, mutation preparation versus commit, replication preparation versus integration, or GPU correction upload. CPU stacks can help; add only scopes/counters answering a live decision, using the existing sampler. GPU readback latency includes queue, execution, transfer and callback scheduling, so it cannot rank kernels by itself.
6. **Scope of memory and exceptions.** Engine allocation, GC, exception, process-memory and GPU-memory readings are not terrain-owned accounting. Preserve resource-owned counters and retained correction-array accounting alongside them. Do not add inclusive memory categories together, infer a leak from a hotloaded-process peak, or infer exact per-type allocation from sampled allocation ticks.
7. **End-to-end deformation meaning.** The deformation result explicitly distinguishes field commit, coherent publication, edited drain and actor release; publication is not display scanout. Preserve this distinction. Its work-and-drain distribution is not equivalent to the figure-eight stationary window. Unsupported scenarios, missing observations, no-op edits, rejections and timeouts must remain visible.

## Ranked simplification investigations

These are candidates, not instructions to remove working code immediately. Each acceptance condition below is additional to the common gates in the next section.

### S1 — Stop constructing job inputs for readiness-only comparisons

2026-09-08 progress: user accepted S1 (implementation `b5a354b`), tested against `8a3138c`,
using saved world revision996/167pages. Storage now has a page index and
`WithField` already reuses matching readers; the remaining three repeated
readiness constructions are the scope of this candidate. See
`SIMPLIFICATION-S1-001/v1` in the [ledger](../ValidationResults.md).
Before/after performance and explicit human approval are required before
acceptance, commit/push, or advancing to S2. The user has now supplied that
approval; the disclosed outliers and unperformed checks remain in the ledger.

**Priority: high. Confidence: high in repeated work; benefit unmeasured on current source.** `CapturePendingClipboxReadiness` calls `CreateRegularDescriptor`/`CreateTransitionDescriptor`; `WithField` queries correction bounds and captures a regional reader. `TryCaptureRegion` can enumerate the page directory and allocate a dictionary/snapshot. The same descriptor equality deliberately ignores the reader reference and compares configuration, region revision and epoch.

Investigate keeping readiness on the existing descriptor identity, obtaining immutable page dependencies only when admitting actual work. Remove redundant reader construction, not reader ownership or source-revision validation. Avoid adding another persistent descriptor cache unless identity-only checks cannot solve the repetition. Preserve the existing resident-revision readiness cache and all of its invalidation triggers.

**Test/acceptance:** unchanged figure-eight plus fixed edited boundary and distant-history cases; same placement decisions, regional identities, geometry fingerprints and stale-result rejection. Attribute readiness allocations before/after; require elimination of the redundant reader allocations in that call path and no increased sampling, preparation, queue or publication cost. An unrelated distant edit must not invalidate an unchanged local mesh.

### S2 — Remove interface-enumerator allocation from canonical page queries

2026-09-08 update: the historical range/capture loops below have already been
replaced by TerrainPageIndex struct queries. Current S2 candidate narrows to
CopyLatticeCorrections: private concrete dictionary iteration, retaining the
read-only consumer view. Before/after completed; user accepted S2 (d4ce975), with disclosed
GPU maximum and final-placement comparison limits. See
[S2 review](../ValidationEvidence/SimplificationS2/Review.md) and
SIMPLIFICATION-S2-001/v1 in the ledger. Historical profile attribution below
must not be read as a fresh measurement of this narrowed candidate.

**Priority: high. Confidence: profile-supported allocation source.** `TerrainFieldSnapshot.Pages` is a read-only dictionary interface; `GetCorrectionRange` enumerates it in the sparse branch, and `TryCaptureRegion` scans it. The multiplayer capture attributes about 7.13 MiB of sampled ticks across threads to correction-page enumerators.

Investigate retaining concrete private dictionary ownership for internal iteration while preserving read-only access for consumers. Keep the current choice between bounded coordinate probes and sparse-page iteration. Do not replace it with a dense world array, spatial-index framework, or one cache per subsystem. Treat snapshot construction separately from enumerator boxing so the measured benefit is attributable.

**Test/acceptance:** identical density/range/revision results for negative coordinates, page faces/edges/corners, sparse distant pages and edited coarse regions; no interface-enumerator allocation attributed to the changed hot loop; no new mutable dictionary exposure or worse query tails. Include nonresident metadata queries: absence of resident samples must never mean zero correction.

### S3 — Consolidate bounded distribution reporting

2026-09-08 candidate: share only identical nearest-rank retained-sample tails
across GPU/collision/frame reporting. Preserve distinct collection, overflow,
mean and window policies; no schema migration. Before/after pair completed,
but stationary/collision timing screens failed against the original baseline.
A fresh exact-S2 control also runs slower; latest S2/S3 screens pass with
isolated maxima qualified. User accepted S3 on2026-09-08; see
[S3 checkpoint](../ValidationEvidence/SimplificationS3/Review.md). Broader
collector or measurement-semantic changes are not part of this candidate.

**Priority: high for clarity; medium implementation risk.** GPU and collision sample containers, manager frame percentiles and result mappings duplicate a responsibility but currently differ in semantics. First settle the profiler contract above. Then replace only genuinely identical collection/summary logic with one small bounded production owner; remove the superseded implementations in the same change.

Do not build a telemetry framework, reflection-based exporter or generic benchmark runner. Keep movement, deformation and GPU lifecycle ownership separate. Preserve historical JSON interpretation and declare schema changes when metric meaning changes. Moving methods to a partial file alone does not reduce runtime work or state complexity.

**Test/acceptance:** same valid-window values and field meanings under unchanged production runs, zero silent overflow, explicit missing data, bounded storage and no additional per-frame allocation. Measure ordinary-play capture-boundary cost as well as active tests. Do not lower diagnostic coverage to make the benchmark faster.

### S4 — Narrow the obsolete whole-snapshot codec interface

**Priority: medium; conditional on completing storage integration.** Save/load orchestration now uses `TerrainFieldStore`, but `TerrainFieldCodec.WriteSnapshot/ReadSnapshot` still support complete page collections. Current visible callers in fingerprinting, replication manifests and checkpoint indexes use the format for identity/header serialization. Thus the methods are not dead; the unused whole-collection capability is the candidate.

Trace every caller, then investigate a canonical identity encoder/decoder that preserves existing bytes and validation while deleting unused whole-world loops and unnecessary temporary empty snapshots. Keep page-block encoding shared by storage and transfer. Do not introduce an old/new save fallback or change existing checkpoint/network bytes merely to obtain cleaner names.

**Test/acceptance:** existing stored identities, manifests and regional fingerprints remain byte-for-byte compatible where required; reopened pages and world/revision identity match; corruption, truncation, trailing data and settings mismatch still reject. Late join and reconnect must pass through the same page protocol. No gain is claimed until the old surface is actually removed.

### S5 — Remove repeated discovery and branching only where measured

**Priority: medium.** Manager `OnUpdate` repeats target/configuration movement logic in valid and invalid configuration branches. `UpdatePlayerCollisionInterests` rediscovers players, deduplicates and sorts every update before collision can reject unchanged interest. Networking already owns session player records, but editor/offline players also exist.

First simplify the shared target/placement decision after resolving the effective configuration, retaining rejection behavior. Separately measure player-discovery cost with one and multiple players. Reuse existing ownership if it covers every lifecycle; do not create a second player registry or cache dynamic actor bounds at a slower cadence. The latter could miss a fast actor and alter collision safety.

**Test/acceptance:** invalid visual requests preserve applied placement; valid gameplay changes and target movement still work; explicit/local/fallback targets, spawn/disconnect, proxies and co-located players retain identical interest. Zero added readiness delay or actor hold frames; any caching proposal must demonstrate lower discovery work without extra authoritative membership state.

### S6 — Remove confirmed unused surfaces, not engine workarounds

**Priority: medium for tiny deletions, low for hotload cleanup.** [MyEditorMenu.cs](../../Editor/MyEditorMenu.cs) still exposes the template menu that only displays an "It worked!" dialog. This is the clearest template-only removal candidate; it has no terrain responsibility. `CustomTopDownController` has no reference in the reviewed authored scene/prefab or source callers. `TerrainFieldPage.CopyTo` likewise had no caller in the scoped source search. Verify resource/reflection/editor use before removing either. The controller is public and potentially attachable; an authored-reference miss is not proof that no user uses it. Older research calling it player-facing is not evidence of current attachment.

`TerrainFieldPage.GetRange` also has a conservative missing-metadata fallback for hotloaded objects. That is not safely obsolete just because constructors now initialize metadata. `WorldId` lazily repairs an empty identity. Investigate supported hotload/migration expectations before deleting these guards; required editor behavior is a feature too.

**Test/acceptance:** no authored/resource or dynamic dependency, successful clean compile and Play, unchanged controls/camera/tool behavior. For hotload-related deletion, prove supported reloads retain valid metadata/identity or explicitly reconstruct derived state through the existing lifecycle; never silently discard edited state. Keep this separate from performance optimization.

### S7 — Simplify storage observation after paging is qualified

**Priority: later; current work is still in progress.** `SweepTerrainStorage` rebuilds `field.Pages.ToArray()` when snapshot identity changes and scans up to eight entries per update under a soft one-millisecond budget. Requiredness considers visual, collision and edit interests; readers separately retain samples and file handles. These are different lifetimes, not redundant caches.

Measure directory-copy allocations and sweep coverage under sustained edits. Repeated snapshot resets are a source-backed fairness question, not a demonstrated starvation bug. Prefer reducing redundant directory copies or unnecessary sweep work over adding an LRU hierarchy or reference-counting framework. Preserve dirty-page protection, exact stored versions and weak retained-array accounting.

**Test/acceptance:** frozen history/eviction/return/reopen scenarios prove actual resident release and disk reads, not mesh disappearance; every eligible page is eventually examined under the fixed edit rate; stale reads never install; live readers survive eviction/save cleanup; failed save/read never becomes silent data loss. Stay within existing sample/file/disk budgets and meet comparable frame, memory and I/O integration gates.

## Complexity to retain

- **Canonical field plus CPU/GPU consumers.** GPU visual extraction and CPU collision have different engine outputs. Removing one by reading GPU geometry back or maintaining a second editable density representation is not simplification.
- **Conservative classification, build-local sample reuse and sub-page revisions.** These reduce real work or protect caves and local edit invalidation. Deleting them can make code shorter while increasing sampling and remeshing.
- **Pending/in-flight/resident/candidate identities, cancellation and epochs.** They protect asynchronous resource ownership and old-world rejection. Do not collapse them into a single ready flag.
- **Coherent visual publication and retained collision.** Removing these can expose mixed edit generations, seams or missing support. Any future narrower publication group needs a separately measured correctness design.
- **Regular/transition shader separation and render-epoch guard.** Recorded native failures justify these boundaries, dummy bindings and lifetime rules. They are engine-version-qualified evidence, not universal API claims. Revalidate on an engine upgrade before considering deletion.
- **Bounded edit/transfer queues, acknowledgements, coverage and host validation.** These preserve backpressure and coherent replicas. Sending every edit to every player or relying solely on reliable transport would remove required application-level state.
- **The simple exact allocator.** It is already only about 62 lines. Historical foreground CPU allocation p95 around 0.01 ms does not justify GPU allocation, compaction or per-LOD pools. Revisit arena size independently if memory evidence warrants it.
- **Collision endpoint welding/support patches and actor holds.** Native geometry failures and missing-support hazards motivated them. Static rays do not qualify dynamic overlap contact; retain until the existing contact/lifecycle gates are closed.
- **Profiler correctness evidence.** Keep warnings, truncation reporting, spatial fingerprints and explicit audits. Removing them would weaken the ability to prove that a simplification preserved behavior.

Animation/rendering remains a separate performance investigation: matched one/two-player captures should identify renderer counts, animation work, viewport conditions and local-client contention. Disabling clothing, animation, views or reducing draw distance would change features or workload, so it is not an acceptable simplification under this request.

## Testing and acceptance plan

Before the first implementation run, enter the candidate, exact source hashes, environment, fixed parameters, measurable benefit and pass criteria in the ledger. This document does not create executable scenario versions or amend their existing limits.

1. **Establish a comparable source state.** Finish or isolate the ongoing paging work. Use cold editor sessions for memory/lifecycle controls, matching engine, hardware, viewport, FOV, FPS cap, network topology, world/checkpoint state and run conditions. Preserve the hotload-confounded result rather than calling it a baseline pass.
2. **Keep the canonical figure-eight unchanged.** Reuse recorded speed 2500, distance 50000, one loop, basic_example, seed 1337/generator v5, 32 cells/16 units, gameplay 8, visual 512 and the scenario's remaining exact settings. Check FPS, CPU/GPU tails, streaming completion, placement lag, allocations, GC, memory and correctness against the latest accepted comparable baseline. If none exists for this state, capture a pre-change control and label its acceptance limits.
3. **Use the existing real entry points.** Run relevant frozen deformation scenarios for S1/S2/S3/S7; save/load/history/reconnect/convergence scenarios for S4/S7; target/configuration and actor-contact cases for S5/S6. Define any genuinely missing fixed case before running it. No alternate field, synthetic mesher, test-only component or direct streaming-origin mutation.
4. **Predeclare the improvement being bought.** For allocation removal, count allocations attributed to the exact call path; for structural deletion, record removed mutable state/branches/interfaces and retained responsibilities. Predeclare repeat count and noise treatment. Do not invent a percentage tolerance after observing a regression, average away a failing tail, or change inputs for a pass.
5. **Require complete preservation.** Same features, field and spatial identities, matching fixed-region geometry/audit results where applicable, zero new holes/stale publications/protocol or I/O errors, correct actor release and contact, bounded queues and memory. A result that improves mean FPS but loses edit responsiveness or increases unexplained tails fails.
6. **Separate completion from acceptance.** Log each failure and incomplete run. GPU-settled, collision-ready, edit-drained, persisted, replica-applied and fully quiescent are distinct boundaries. A soft admission budget cannot guarantee an individual native call or directory copy fits inside it.
7. **Accept only the smallest proven change.** Retain a candidate only after all applicable existing absolute and relative gates pass and there is no material unexplained regression. Changing a workload or accepting a regression requires documented evidence and explicit user approval. Revert rejected candidates rather than retaining fallback implementations.

Suggested order: establish the current control and clarify measurement meanings; investigate S1 and S2 separately; consolidate S3 only after its semantics are fixed; narrow S4 after paging qualification; then use measurements to choose between S5 and S7. Tiny verified S6 deletions can be independent. Do not combine these into a broad refactor whose result cannot be attributed.

## Review validation and remaining limits

This review checked source owners, cross-file callers, authored integration, previous investigations and explicit ledger decisions. No engine session was restarted or benchmark run for this documentation-only task. It makes no new performance, shader-equivalence, multiplayer-scale or storage-acceptance claim. The prior profile summaries are dated evidence, not fresh captures of the current paging implementation.

Before implementation, recheck changed symbols and the end of the ledger: another task is actively extending storage. Maintain current implementation facts in the architecture owners, preserve historical results, and link this shortlist from the documentation map rather than duplicating its candidate list elsewhere.
