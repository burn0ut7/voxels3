# GPU meshing: simplify first, replace deliberately

Research for Voxels3 | 7 September 2026 | Decision report

## 1. Recommendation and evidence boundary

**Keep the indexed, persistent GPU mesher and clipbox ownership for the next optimization slice. Remove verified dead shader source and unnecessary visibility diagnostics, then measure cooperative reductions and scheduling delays. Treat dual marching cubes as the leading algorithm-replacement investigation; persistent meshlets are the leading renderer-replacement research direction. Neither replacement is justified as an implementation decision yet.**

Sections 1–8 record the original audit and proposals. The subsequent [prototype investigation](#9-prototype-investigation-outcome) reports eight fresh Figure Eight runs: both performance candidates were removed, while production reporting and verified dead-source cleanup were retained. The goal is smooth, volumetric terrain with caves, predictable streaming, modest retained memory, and a system that can later support authoritative edits. No universal best-in-class implementation exists across those constraints. Strong references solve different portions of the problem.

The audited runtime is commit `ab9a6e8e64fa29f979e84da3b9ce3cafe7cf57c6`. The installed engine reports `26.09.01c`; its raw build fields include `33901499107` and `build-pr`. The existing uncommitted scene requests levels 0..6 / visual radius 512. Its SHA-256 is `2B9C3E74156C3057E296A1AADE93F719F8CAD274BE07FCC9D163F755A4D206FD`. That scene change was preserved.

The latest accepted ledger run, `f1bcf41aed124cc491b66262c1f30087`, used visual radius 128, levels 0..4, Ryzen 7 9800X3D and RTX 5090. Moving GPU p95/p99 were 1.1635/1.6122 ms; stationary GPU p95/p99 were 0.8795/0.9365 ms. Moving CPU p95/p99 were 1.2937/2.6133 ms. These are whole-frame measurements on high-end hardware, not individual compute-kernel timings or evidence that radius 512 is accepted. [Accepted run and limitations](../ValidationResults.md#final-candidate---2026-09-07t03494836300310000).

The main unknown is attribution: the code establishes redundant work and serialization, but the ledger does not establish which GPU kernel dominates today. Submission stopwatch timings measure CPU submission; count readback time includes queue/execution/transfer/callback effects. They must not be relabeled as GPU execution time.

The strongest immediate findings are:

- Visibility accumulates diagnostic counters even when its measurement flags are off.
- Regular extraction uses eight count-side dispatches, followed by two emission dispatches for each destination arena represented in the batch.
- Position fingerprints repeat edge interpolation in a separate pass before the actual vertex writer.
- A single lane serially scans group totals; regular batches have 422 edge groups and 128 cell groups per region.
- Transitions depend on idle regular-work ticks, while whole-placement publication waits for every required dependency.
- Several authored shader helpers/tables have no current consumers; old compiled assets must not be confused with active source implementations.

These findings support targeted removal and measurement. They do not support promising an FPS percentage or calling the existing extractor obsolete.

## 2. What the current pipeline actually does

The authoritative input is the procedural field/settings, exposed by immutable chunk views and descriptors. There is no populated mutable density world, collision mesher, edit protocol, persistence system, or multi-origin terrain interest system in this slice. CPU and GPU evaluate corresponding field recipes; their existence does not imply measured bitwise cross-device equivalence. [Foundation ownership](../Architecture/VoxelChunkFoundation.md#canonical-ownership-and-data-flow).

The regular chain is: descriptor -> 35^3 halo density lattice -> classify 32^3 cells -> mark used grid edges -> scan vertex and index counts -> audit positions -> read back bounded counts -> reserve exact CPU-owned ranges -> emit persistent indexed geometry -> publish after the render-sequence boundary. [Scratch implementation](../../Code/Voxels/GpuTerrainScratch.cs), [regular compute](../../Assets/shaders/voxels/voxel_persistent_geometry.hlsl).

There are 42,875 density samples, 32,768 cells, and 107,811 edge slots per regular region. Three regular lanes hold at most eight regions each. Calculated from the declared capacity formula, their combined logical scratch is 34,310,016 bytes, approximately 32.72 MiB. This is logical buffer capacity, not an independent measurement of driver commitment. It is bounded and much smaller than hundreds of MiB of persistent arena capacity.

Each arena reserves 32 MiB for 24-byte vertices, 16 MiB for 32-bit indices, and 512 record identities. A batch spanning A destination arenas dispatches the full batch emission domain A times; slots belonging elsewhere return early. Thus ordinary regular count-plus-emit submission is 8 + 2A dispatches, excluding visibility and empty-output cases. The useful geometry is not duplicated, but dispatches and early-return invocations are. [Allocation/emission owner](../../Code/Voxels/GpuVoxelMesher.cs).

Transitions use three separate scratch lanes, eight faces per batch, 1,024 face cells and 10,432 edge slots. Their compact five-offset sampling layout contains 15,389 density values per face. Count processing spans sampling, classification/scans, and audits/readback across eligible ticks; emission shares one transition shader resource. Regular and transition products occupy the same arenas and renderer. These are different topology responsibilities, not competing terrain systems. [Transition scratch](../../Code/Voxels/GpuTransitionScratch.cs).

Meshes persist until invalidation or eviction; drawing does not reevaluate procedural density. Visibility writes indirect instance counts per camera, and drawing submits 512 indirect records per active arena. This is one API submission per arena, not one draw per logical chunk and not necessarily one hardware primitive batch. [Draw/visibility implementation](../../Code/Voxels/GpuVoxelMesher.cs), [visibility kernel](../../Assets/shaders/voxels/voxel_chunk_visibility_cs.shader).

Whole-placement handoff keeps committed coverage while its replacement finishes. Individual meshes may become resident before they become active. Request-to-mesh latency and request-to-visible-coverage latency therefore differ. Removing this gate without another complete transition/coverage design would simplify code by removing correctness.

## 3. Near-term simplifications worth testing

**First: stop collecting unused visibility statistics.** The visibility kernel always performs frame-counter atomics for active geometry. `MeasureVisibility` and `CaptureSettledDiagnostics` gate aggregation, but do not gate these updates. Command recording also always clears and barriers the frame-counter buffer. Guard the diagnostic work with the existing measurement state while leaving visible indirect arguments untouched. This is a high-confidence redundant-work finding; speedup is unmeasured. [Kernel](../../Assets/shaders/voxels/voxel_chunk_visibility_cs.shader), `CommitDrawCommandsLocked` in [mesher](../../Code/Voxels/GpuVoxelMesher.cs).

The canonical figure-eight enables diagnostics, so this change may principally benefit ordinary play. Do not claim a figure-eight improvement from disabling its observations. Preserve the diagnostic-on comparison, and predeclare bounded ordinary-play observations if evaluating diagnostic-off savings. Use the same production entry points and geometry path.

**Second: reduce diagnostic contention without losing evidence.** Regular classification atomically updates one active-cell counter and one topology fingerprint per region; the position pass XORs into one regional value. Cooperative group reductions can preserve integer sums and XOR fingerprints while reducing contended global operations. A group reduction must include inactive lanes safely, preserve the existing hashes, and avoid divergent barrier exits. Transition face/lateral checks remain correctness evidence; do not simply delete them. [Regular stages 2/6](../../Assets/shaders/voxels/voxel_persistent_geometry.hlsl), [transition stages 2/6](../../Assets/shaders/voxels/voxel_transition_geometry.hlsl).

**Third: replace only the serial totals scan if it matters.** Stage 5 currently lets one thread loop through 550 regular group totals per region; the transition version loops through only 45. Parallelizing the regular totals within one group is a bounded candidate. The existing local scans also use multiple shared-memory barriers. Wave-assisted scans are worth checking, but supported wave operations and subgroup sizes need installed-engine evidence. Do not add device-wide look-back machinery for a 550-value problem by default. [Merrill and Garland, NVIDIA, March 2016](https://research.nvidia.com/sites/default/files/pubs/2016-03_Single-pass-Parallel-Prefix/nvr-2016-002.pdf); [portable scan implementations, accessed 2026-09-07](https://github.com/b0nes164/GPUPrefixSums).

**Fourth: remove redundant clearing only after proving every read is initialized.** `Cells` and `EdgeVertexIds` are cleared, then substantially overwritten. A modified classifier could initialize each complete cell record, including empty cells; scans already write valid edge-ID entries. Edge flags and regional accumulators still require initialization. Removing all clearing indiscriminately would introduce stale scratch data. The likely benefit is bandwidth reduction, not necessarily removal of the entire clear dispatch.

**Do not immediately fuse fingerprinting into emission.** That would remove repeated interpolation, but fingerprints currently return before allocation. Moving them changes readback, empty-result handling, and publication timing. Cache extra edge data only if saved arithmetic exceeds added memory traffic. Prefer exact cooperative reductions as the smaller first step.

## 4. Extraction alternatives and memory tradeoffs

**Indexed extraction is already implemented.** Geiss's GPU terrain chapter describes unique grid-edge vertices and indexed output; Voxels3 already captures that principal benefit. Replacing this with a simple append-triangle sample would make source shorter while restoring duplicated vertices and normals. Its historical GeForce 8800 timings are not modern targets. [Ryan Geiss, NVIDIA GPU Gems 3, 2007](https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-1-generating-complex-procedural-terrains-using-gpu).

**Cooperative marching blocks are a credible compute-side alternative.** Parallel Marching Blocks uses thread-block cooperation, local edge reuse, compact active blocks and local prefix sums to reduce large global intermediates. Investigate those mechanisms if classification, atomics or scratch traffic dominate. Our regions are already small and density is cached; extra sub-block classification can cost more than it saves. The paper's scientific-volume comparisons do not predict terrain speedups. [Liu et al., Computer Graphics Forum, 2016, author manuscript](https://pure.strath.ac.uk/ws/portalfiles/portal/92247319/Liu_etal_CGF22016_Parallel_marching_blocks_a_practical_isosurfacing_algorithm_large_data_many_core_architectures.pdf).

A directly owned grid-edge classifier could replace repeated cell-driven `InterlockedOr` marking. That requires proving exactly which edges imported regular tables use, retaining boundary ownership, and preserving the current density-zero convention. It should replace the marking path, not add a second classification system. Compact active-cell or active-edge lists help only when saved emission work exceeds list creation and dispatch overhead; stage 3/4 already provide some offsets that might support this without another general-purpose scan framework.

**Vertex packing is a separate opportunity.** Current vertices contain 12-byte positions, a 4-byte audit identity, and two 32-bit octahedral-normal components. A hypothetical 16-byte position-plus-packed-normal format would reduce vertex bytes by one third, not total geometry memory by one third. It would relocate identity diagnostics and change the GPU input contract; quantized positions would additionally threaten exact seams at large coordinates. Verify a supported vertex layout and normal error bounds before choosing it. [Current layout](../../Code/Voxels/GpuTerrainContracts.cs), [writer](../../Assets/shaders/voxels/voxel_emit_vertices.hlsl), [decoder](../../Assets/shaders/voxels/voxel_terrain.shader).

Blindly switching to 16-bit indices is unsafe: a 32-cell cubic lattice has up to 3*32*33^2 = 104,544 geometric edges, exceeding 65,535 possible vertex identities. Real regions may be smaller, but worst-case capacity cannot be assumed away. Splitting output or using mixed formats adds ownership and draw complexity.

**Keep density precision and spatial layout fixed initially.** Half precision, smaller chunks, reduced cave detail, and coarser LOD are different experiments. Each changes either field interpretation, boundary behavior, or workload and cannot be bundled into an equivalent-throughput optimization.

## 5. Allocation, scheduling and coverage

**The count readback is a synchronization dependency, not a bandwidth problem.** A full regular batch returns only 256 bytes; a transition batch returns 512 bytes. The cost to investigate is the wait before CPU allocation and the later callback-consumption opportunity. Removing count bytes alone will not make terrain ready sooner. The current first-fit/coalescing allocator is small and exact; no measured fragmentation crisis is established here. [Allocator](../../Code/Voxels/GpuTerrainRangeAllocator.cs), [scratch callbacks](../../Code/Voxels/GpuTerrainScratch.cs).

Consider three alternatives in order of evidence:

- **Better use of the existing lifecycle:** record eligible idle lanes, batch occupancy, callback-ready wait, destination arenas per batch, and which dependency delays placement. Prefer allocation locality or compact arena-specific emission request maps if repeated full-domain emission is material. Keep original scratch request identity explicit.
- **CPU pre-reservation:** size classes or conservative reservations allow extraction without waiting for exact counts. They trade space for latency and require overflow handling. Worst-case regular indexed geometry alone is approximately 4.27 MiB per region using 104,544 vertices and 491,520 indices; reserving this everywhere is unsuitable. Retry must preserve old visible geometry and use the same extraction path.
- **GPU-owned pages:** GPU counters/free lists can allocate within precreated buffers. No special engine allocator API is inherently necessary. The hard parts are contiguous indexed output, page fragmentation, capacity failure, visibility publication, reclamation and safe reuse while old views are in flight. A paged mesh might require several draw records or a different vertex-fetch design. This is a new allocator contract, not a small patch.

Installed XML confirms indirect dispatch, asynchronous buffer readback, append counters and buffer range-copy members. Current production proves indexed-indirect drawing. Online staging corroborates indirect dispatch; direct dispatch takes thread counts whereas indirect arguments contain group counts. None of this proves arbitrary bindless writable geometry buffers or a safe new GPU allocation lifecycle. The published bindless examples expose textures and samplers. [Facepunch ComputeShader API](https://sbox.game/api/Sandbox.ComputeShader/); [Bindless API, updated 2026-07-30](https://sbox.game/dev/doc/rendering/shaders/classes/bindless-api).

**Transitions are a scheduling hypothesis, not a demonstrated current failure.** `ProcessGpuRenderTick` services transitions only when no regular work was submitted; outer work additionally has a 250 ms service target. An endless foreground workload can delay transitions, while a placement needs them to commit. Measure oldest ready transition age and placement-critical dependencies before replacing these branches with a bounded age/dependency-aware policy. Do not simply add lanes or admit more work per tick.

Partial regional publication is a larger alternative. It needs exact coarse replacement, face/corner ownership and transition readiness for every published region. Keep whole-placement handoff until that design demonstrably improves coverage latency without cracks or a parallel fallback renderer.

## 6. Which larger systems deserve investigation?

**Retain clipboxes as the current default.** Godot Voxel's maintainer documentation says its clipbox system updates moving differences and addresses multiplayer/regular-octree-traversal shortcomings. It also notes less precise box-shaped loading. This supports evaluating our bounded hierarchy on its merits, not replacing it with an octree for modernity. Godot's CPU-oriented extraction and secondary-position seams are not drop-in GPU designs. [Voxel Tools, Smooth terrains, accessed 2026-09-07](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/#streaming-systems).

**Dual marching cubes is the strongest algorithm-replacement research candidate.** Hello Games reports reduced vertex count, faster generation, better frame rate and lower memory after its terrain rewrite. The academic DMC method builds a feature-aligned dual grid; it is distinct from dual contouring. The public game report does not disclose GPU work division, LOD topology, timings or an exact match to that paper. Treat it as credible motivation, not a transplant recipe. [Hello Games, Worlds Part I, 2024](https://www.nomanssky.com/worlds-part-i-update/); [Schaefer and Warren, Dual Marching Cubes, author paper](https://www.cs.rice.edu/~jwarren/papers/dmc.pdf).

A DMC candidate must define adaptive sampling, geometry error, deterministic vertex placement, cross-region topology, LOD boundaries and incremental invalidation. It would replace the current extraction/transition solution where incompatible, not sit beside it as another production mesher. Exact old fingerprints would cease to be an appropriate geometry criterion: before testing, define fixed surface-error, topology, seam and feature-preservation criteria with explicit approval for changed geometry.

**Dual contouring is strongest when sharp features become a requirement.** Hermite intersections/normals and quadratic-error fitting support feature preservation and adaptive simplification. Stable fitting and topology-preserving simplification remain real implementation responsibilities. The present procedural terrain does not establish that this extra machinery is worth its cost. Surface nets can simplify vertex placement, but do not automatically solve our multilevel boundary ownership. [Ju, Losasso, Schaefer and Warren, Dual Contouring of Hermite Data, 2002](https://www.cs.rice.edu/~jwarren/papers/dualcontour.pdf).

**Persistent meshlets deserve a future engine-backed investigation.** A 2026 Eurographics paper describes adaptive occupied-block analysis and persistent isosurface meshlets for task/mesh shaders. Thus mesh shaders do not inherently require rebuilding unchanged terrain each frame. Primary publisher abstract/metadata were accessible through search; full-paper retrieval failed, so no quantitative speedup is adopted. No callable s&box task/mesh-shader route was established in this session. [Kreskowski, Rendle and Froehlich, Eurographics EGPGV 2026](https://diglib.eg.org/items/c9a3ca98-a091-44c7-8401-b38d74a58d3d).

Voxel Plugin 2 targets Nanite for on-demand interactive terrain, so dismissing Nanite as exclusively imported/static geometry is inaccurate. Its Unreal integration is not a verified s&box capability. AMD's work-graph mesh-node guide also requires specific preview SDK, hardware and driver support. Neither is currently a project-ready replacement. [Voxel Plugin overview, accessed 2026-09-07](https://docs.voxelplugin.com/getting-started/working-with-voxel-plugin/); [AMD getting started, updated November 2025](https://gpuopen.com/learn/work_graphs_mesh_nodes/work_graphs_mesh_nodes-getting_started/).

## 7. What to delete, preserve and decline

**Deletion shortlist, based on current source references:**

- `voxel_regular_cell.hlsl` has no authored include/call consumer. Its old direct-SDF classifier uses `<= 0`, while current extraction clamps near-zero density and uses `< 0`. Remove the unused alternate helper; do not reintroduce it as a reference mesher.
- `transvoxel_regular_metadata.hlsl` has no authored include consumer. Keep the actively included `transvoxel_regular_tables.hlsl` and the Transvoxel license. Avoid deleting similarly named live tables.
- `PersistentGradient` and `PersistentSafeNormalize` in the regular count include have no callers. Emission has its actual normal implementation. Remove dead definitions and unused bindings/declarations only after checking their shader contracts.
- Old `.shader_c` names remain tracked, including resources whose authored shader no longer exists. They are generated artifacts, not proof of active duplicate pipelines. Handle tracking/package cleanup through the supported resource workflow; never hand-edit binaries or treat their removal as measured runtime optimization.

Reference searches covered authored Code, Editor, Assets and ProjectSettings inputs, excluding generated output. This establishes a cleanup shortlist, not proof of all external package consumers. No source or generated asset was deleted during research.

**Preserve useful complexity:** exact range ownership, source revisions, cancellation checks, generation-safe publication, render-sequence resource lifetime, old geometry until replacement, three-lane overlap, and complete transition coverage. The large mesher contains scheduling, arena ownership, drawing and diagnostics; file size alone does not justify a new service framework. Extract responsibilities only when an implemented change gives them a concrete independent owner.

**Preserve engine workarounds until reproduced on the current engine.** The regular emitter split and single-resource transition writer have documented native parser/device-loss history on 26.08.19. The installed 26.09.01c build is different, but that does not establish a fix. Cold-start shader loading and real camera-view lifecycle validation are required before consolidation. Do not remove barriers, dummy descriptors, or add scratch lanes merely to reduce line count. [Parser/resource constraints](../Architecture/GpuVoxelMeshing.md#sbox-vfx-shader-parser-gotcha).

**Decline wholesale ray rendering for this slice.** Direct sphere tracing needs conservative distance steps or derivative bounds; the project's composed density field is not established as an exact Euclidean distance field. Sparse voxel ray systems additionally change representation, traversal, depth, materials and renderer integration. They may avoid mesh storage, but replace a cached cost with view-dependent work. [John C. Hart, Sphere tracing, 1996](https://experts.illinois.edu/en/publications/sphere-tracing-a-geometric-method-for-the-antialiased-ray-tracing/).

Similarly, blocky greedy meshing solves a different surface model; heightfield clipmap topology cannot preserve caves. Hi-Z occlusion could reduce hidden terrain rendering, but needs trustworthy depth timing and conservative behavior under camera motion. Frustum simplification or draw compaction should wait for measured visibility/render cost. None warrants a second renderer today.

## 8. Proposed next slice and acceptance

The next implementation should stay narrow: remove verified authored dead code, guard unused visibility diagnostics, and measure/reduce regular diagnostic atomics. Keep extraction topology, field settings, vertex layout, allocator, shader resource boundaries and publication unchanged. If GPU attribution shows scans or arena-spanning emission dominate instead, choose that single mechanism first. Do not bundle speculative rewrites.

Before running, record the exact source, scene hash, engine, viewport/environment and comparison in the existing validation ledger. Reuse `CHUNK-OPTIMIZATION-128-001/v1` unchanged for comparable radius-128 evidence; the newer radius-512 scene is not a silent substitution. A 512 experiment must use the existing applicable stress scenario or a separately justified, predeclared scenario with its own baseline. No scene setting was changed in this research.

Measure through the actual playable world's canonical figure-eight. Retain moving/stationary CPU and GPU p95/p99, per-level/pair publication tails, queue drain, maximum anchor and route lag, active/resident counts, arena utilization, scratch and allocated bytes, allocations/frame, and geometry/correctness evidence. Add bounded production observations only for the specific hypothesis: physical dispatches, arenas touched per batch, idle eligible lanes, callback-ready delay or oldest transition wait. Measurement schema changes need a control run and explicit metric definitions.

Where supported, use real GPU timestamps/profiler markers for density, classification, scans, audits, emission and visibility. If unavailable, report end-to-end timings and limit kernel-attribution claims. A render-sequence advance is an existing publication convention, not an independently verified GPU fence API.

For topology-preserving changes, retain exact final fingerprints, finite vertices, winding, indices, sampled normals, negative-coordinate and LOD-face/corner checks. Preserve known transition-table degenerates as a separate existing limitation, not a reason to disable auditing. Recheck cold start and game/editor camera behavior for every shader/resource change. The sole automated test trigger remains the figure-eight; no alternate test scenes, CPU oracle or synthetic mesher.

Accept only an attributable benefit with all locked gates satisfied. Remove a losing candidate instead of retaining a legacy switch. A later full replacement needs its own documented input/output contract, memory cap, exhaustion/recovery behavior, boundary topology and feature-preservation criteria before implementation. Once accepted, delete the superseded allocator/extractor/renderer in that same change.

Research confidence is high for source-identified work and the current ownership graph, medium for the candidate ranking, and deliberately unset for speedup. Remaining gaps are kernel timings, current 512 baseline, lower-tier GPU behavior, callable advanced shader support and edited/multiplayer workloads that do not yet exist. Further broad searching is unlikely to change the immediate shortlist; those gaps require targeted engine evidence and production measurements.

Discovery covered GPU indexed extraction, block cooperation, scans, allocation, clipboxes/Transvoxel, DMC/DC, meshlets, work graphs, Nanite and ray rendering. Follow-up checked the highest-impact claims against primary papers, maintainer documentation, installed XML and shipping source. No runtime performance claim is added by this document.

## 9. Prototype investigation outcome

**Retain the measurement improvements and remove the two unused shader sources.
Reject both performance prototypes for now.** Eight production Figure Eight runs
established a radius-512 baseline, checked instrumentation, investigated two
candidates, and separated a large editor-restart effect from actual optimization.
No alternate mesher, runtime switch, allocator, test scene or CPU oracle remains.
The current rendering algorithm, scratch initialization, emission domain and
transition scheduling are unchanged.

The durable [evidence index](../ValidationEvidence/GpuMeshing512/README.md) links
all raw production results and comparisons. The [validation ledger](../ValidationResults.md#gpu-meshing-512-001v1---gpu-simplification-investigation)
owns the immutable scenario, thresholds, individual failures and acceptance.
Tests used the authored radius-512 scene unchanged: seed 1337/generator 5, gameplay
radius 8, levels 0–6, 32 cells per region, 16-unit base cells, half extents 4/8,
one local player, speed 2500, distance 50000 and one Figure Eight loop. Each run
started at the authored origin after stop/play and settlement, then included normal
queue drain and ten stationary seconds. Hardware was Ryzen 7 9800X3D / RTX 5090,
s&box 26.09.01c. This is not lower-tier GPU or multiplayer-load evidence.

### What stays

Schema 24 adds bounded production counters for regular count batches/regions,
emission arena passes, actual dispatched region slots, enabled output regions,
multi-arena batches and limited foreground transition deferrals. The existing
measurement lifecycle owns reset and collection. One `performance.gpu_work` log
accompanies each persisted result; there is no per-frame log spam. Existing
publication tails, readback/submission timings, memory observations, exact geometry
fingerprints and `voxel_mesh_audit` remain the debugging/reporting paths.

The retained state matches the successful restored-control run
`b80cd7b7de3947c79c45d436b45b996d`. It passes all 203 original-baseline summary
comparisons and 6203 additional recorded correctness/absolute-budget checks.
The reporting-only control also passed before the restart. These observations
support retaining the counters, not claiming that logging speeds up the GPU.

Deleted `voxel_regular_cell.hlsl` and `transvoxel_regular_metadata.hlsl` had no
authored consumer in Code, Editor, Assets or ProjectSettings. This removes the
obsolete alternate density-zero classifier and unused metadata declaration. The
live regular lookup tables and their license remain. Generated shader binaries
were not hand-edited or treated as source cleanup.

### Candidate A: remove overwritten scratch clears — rejected

This candidate removed cell/edge-ID clears and their corresponding post-clear
barriers, initialized empty-cell metadata in classification, and removed unused
count-shader helpers and allocation declarations/binding. Scan and classification
stages already overwrite the affected entries before their intended consumers.
At 32 cells per axis, the source-level saving was 693388 logical store bytes per
regular count region; this is not a measured DRAM-bandwidth reduction.

Both runs preserved recorded geometry but failed performance gates. The first
missed moving CPU p99 and several regular/transition publication tails. The
unchanged repeat again missed CPU/streaming tails and reached placement-level lag
4 against a permitted 3. All runtime/shader changes from this candidate were
removed. Without kernel attribution, the reason that this apparently redundant
work removal did not pass is unresolved. Correct geometry alone was insufficient.

### Candidate B: compact emission by destination arena — rejected

The control dispatched 152211 regular region slots for 78543 enabled output
regions. Each destination arena received the full original count-batch domain,
including disabled descriptors. The prototype packed enabled descriptors into the
existing buffer and used each descriptor's `RequestIndex` to address its original
density, cell, edge and prefix-sum data. It added no GPU buffer, topology path or
allocation policy. Per-arena vertex/index dispatch counts stayed the same; their
thread domains shrank. The final variant also removed redundant emitter batch-size
assignments from count setup.

The reduction was real: the final candidate dispatched 78545 slots for 78545
regions, versus the nearby restored control's 152267 slots for 78556 regions,
about 48.4% fewer slots with near-identical workload completion. This is a reduction
in dispatched region slots, not 48.4% less GPU time. Threads within enabled regions
still scan complete cell/edge domains.

| Measurement | Fresh restored control | Final compact candidate |
| --- | ---: | ---: |
| Moving CPU p95 / p99, ms | 1.1610 / 1.9171 | 1.1538 / 1.8836 |
| Moving GPU p95 / p99, ms | 0.9096 / 1.2650 | 0.9005 / 1.2558 |
| Stationary GPU p95 / p99, ms | 0.5388 / 0.7503 | 0.5386 / 0.7634 |
| Moving allocations/frame, bytes | 28852.453 | 28816.855 |
| Post-loop drain, ms | 25.9653 | 25.4373 |
| Fine1/coarse2 publication p99, ms | 211.6967 | 223.4091 |

The first candidate missed original-baseline outer p99. Its unchanged cold run
passed. However, the restored unoptimized control reproduced almost all of the
broad post-restart frame improvement. A meaningful frame-time gain from compaction
was therefore not established. The final candidate passed original-baseline gates
but missed the additionally predeclared nearby-control transition p99 limit:
222.281535 ms. The failure was small, but it was preserved rather than weakening
the criterion or repeating until a favorable run appeared. All compact-emission
code, including its wrapper comments and batch-size cleanup, was removed.
This is a conservative rejection with unresolved timing attribution, not proof
that compaction intrinsically harms transitions.

### Correctness, engine behavior and limits

All eight runs retained identical recorded regular/transition geometry, including
per-level/pair fingerprints and every recorded transition face matched by spatial
identity. All pending/error/truncation and absolute-budget checks passed; the
separate relative performance comparisons identify the rejected runs. Persistent
terrain capacity stayed at 14 arenas with the same 34310016 regular-scratch and
3884628 transition-scratch bytes. Whole-editor memory varied with process/hotload
history, without a terrain-owned capacity increase from these candidates.

The explicit emitted-buffer audit sampled 104 regions after candidate B's timed
run. It found no invalid indices, nonfinite/out-of-bounds positions, abnormal
normals, allocation-identity mismatch, oversized triangles or draw-argument defect.
Its 47 flagged regions were transitions with existing degenerates. The total was
1838 versus the baseline sample's 1666; equal-distance selection can choose different
region identities, so these totals are not a fixed-subset comparison. The full
[audit](../ValidationEvidence/GpuMeshing512/332a05fcc39e41a7ac23e5e05a2009b1-audit.md)
and known limitation remain visible. Main-camera inspection supported continuity
in that view; exhaustive seams and edited terrain were not verified.

HLSL include edits did not trigger the installed editor's shader recompiler.
Re-saving the owning `.shader` wrapper did, and generated output was verified
before timing. Native `asset_compile` rejected these resources as compiled-only.
The candidate survived a clean editor restart and actual Figure Eight execution,
with the crash marker unchanged. These findings reinforce the existing dedicated
regular-emitter resource boundary; they do not establish that merging shaders is
safe on this engine.

### Next investigation

The next useful step is reliable GPU stage attribution and stronger publication
wait attribution in the same production workload. Current CPU allocation p95 is
roughly 0.01 ms in the measured foreground batches; count readback includes queue,
execution, transfer and callback delay. Those observations do not justify replacing
the allocator. The deferral counter establishes opportunities to observe waiting,
but does not measure starvation or identify the dependency blocking publication.

Compact emission remains a small, understood future experiment if workload or
hardware evidence justifies revisiting it. Cooperative scans/atomics and visibility
diagnostic gating remain unimplemented proposals. Do not stack them onto an
unaccepted candidate. Dual marching cubes, GPU-owned allocation and meshlets still
require their own measured bottleneck and engine-integration evidence before a
replacement is warranted.

## 10. Scan and arena follow-up

The [GPU scan and arena investigation](GpuReductionsAndArenaEfficiency.md)
continues the regular totals-scan and capacity-sizing questions with isolated
prototypes and Figure Eight controls. Its dated results distinguish rejected
experiments from retained implementation. The source audit above describes the
original baseline; current contracts remain in the architecture document.
