# CPU performance review: 2026-09-07

## Findings and recommended order

The best next **project-owned CPU work** is to reduce repeated placement checks,
remove allocation from terrain membership queries, and make free-range release
incremental. Full visibility-descriptor uploads are the next CPU/GPU crossover.
The largest **ordinary stationary costs visible here** sit in engine rendering,
animation, editor activity, and allocation/GC; the settled terrain manager alone
is too small to explain them. No implementation or speedup is claimed by this review.

| Priority | Opportunity | Evidence strength | Expected benefit and scope |
| --- | --- | --- | --- |
| 1 | Remove capturing predicates from hot terrain membership/removal checks. | Current source plus recurring allocation tick types. | Small, bounded change with allocation and GC potential; exact byte savings require attribution. |
| 2 | Maintain the existing allocator's sorted free ranges with neighbor merging. | 243.4 ms sampled inclusive CPU in the selected moving interval; full sort/merge on every release in source. | Reduce removal/commit spikes without changing GPU ownership or capacity. |
| 3 | Stop repeatedly checking already-satisfied placement dependencies; then consider incremental changed-level sets. | Readiness 221.0 ms and preparation 322.9 ms sampled CPU; 4.46 million preparation scans in the full run. | Largest combined terrain bookkeeping target, but greater correctness risk than the first two changes. |
| 4 | Upload changed visibility descriptors and distinguish descriptor dirtiness from command-layout dirtiness. | 149.3 ms sampled upload CPU; whole capacity arrays uploaded in source. | Reduce CPU copying/driver work and bandwidth; preserve camera/resource lifetime rules. |
| 5 | Attribute engine/editor steady-state allocations and animation work. | About 24.3 MiB/s allocated stationary; animation about 0.111 ms/frame in the final profiler window. | Potentially broader benefit than terrain-only fixes, but much of the ownership is outside this repository. |
| 6 | Avoid repeated test-start buffer allocation; investigate retained camera resources. | A 43.5 MiB test-start float-array burst; explicit retired-resource retention in source. | Diagnostic-startup memory and long-session resource hygiene, not a demonstrated ordinary-frame speedup. |

For the first implementation slice, choose allocation-free membership checks
plus independently measured allocator release work, with individual controls.
Do not combine those with a placement redesign or GPU capacity experiment before
their attribution is established. This ordering balances evidence, scope, and risk;
it is a proposed investigation sequence, not an approved implementation backlog.

## Evidence identity and limitations

- Audited runtime source: Git `aab4bc8` (schema 24), including the previously
  accepted CPU changes from `ab9a6e8`. The authored scene has an existing user
  modification to levels 0..6 / visual radius 512. No runtime or scene edits were
  made for this review. Other work began appending to the validation ledger during
  the review; that concurrent work is outside this fixed-source assessment.
- Actual capture:
  `C:\Program Files (x86)\Steam\steamapps\profiler_captures\sbox_2026-09-07_00_57_52.json\sbox_2026-09-07_00_57_52.json`.
  The supplied path had expanded underscores into directories; this matching
  timestamped file exists. It is 29,892,435 bytes; SHA-256
  `cae8d10fee80abafd73d32eec42cb3537cbf5d36a1c359800b251873eb8245c8`.
- Capture metadata: sbox-dev PID 41496, 47 threads, 16 logical CPUs, nominal
  sample interval 0.2 ms, 26.7132752 seconds metadata duration. Installed build
  is 26.09.01c; the local CPU is an AMD Ryzen 7 9800X3D. Capture metadata does
  not independently pin the running runtime assembly to a Git revision.
- The [session excerpt](Evidence/CpuProfile20260907/session-excerpt.txt) places
  test start at 00:57:56.0284 EDT, approximately 3.8174 seconds after the profile
  epoch (04:57:52.211 UTC). Stop is requested at 00:58:17.3474, followed by ETW
  shutdown, CLR rundown and conversion. Metadata duration includes the boundary
  region; the main thread's samples end earlier. Do not call all 26.7 seconds
  clean gameplay or interpret export metadata as process termination.
- Analysis windows, selected only for attribution: 0.7..3.5 seconds before
  movement, and 5..25 seconds during movement. The former contains inspector
  construction/selection activity: stationary player does **not** mean an idle
  editor. It is not a controlled stationary baseline. The latter excludes the
  initial test allocation burst and shutdown request.
- The correlated completed result is
  `aa45c1cda4e847aba3284cac926960e7`, saved at 05:00:08.081605 UTC in
  `C:\Program Files (x86)\Steam\steamapps\common\sbox\data\local\voxels3#local\performance\results-v1.jsonl`.
  Labels are `manual-figure-eight` / `manual-inspector`, not a source hash.
  Its full 121.93-second journey overlaps recording **and profile conversion**;
  the capture itself covers only its beginning. Its final ten stationary seconds
  are separate from the pre-movement trace window.
- The result records seed 1337, generator v5, surface 0/0.0005/128, 32 cells at
  size 16, gameplay radius 8, levels 0..6, half extents 4/8, radius 512,
  one loop at speed 2500 and distance 50000, center (0,0), Z=0. Viewport,
  camera history, frame cap and pre-run settlement are not fully pinned by this
  manual result. Sampling shows a frame-clamp path, but does not establish its
  numeric setting. This is observational evidence, **not a new accepted baseline**.

The [derived evidence](Evidence/CpuProfile20260907/summary.json) retains the
capture identity, window results, GC/heap markers, allocation tick distribution,
and selected sections of the completed result. Per-face geometry arrays were
not copied; this review does not perform a geometry acceptance comparison.
The [offline analyzer](Evidence/CpuProfile20260907/analyze.py) reproduces the
summary from the original capture and result log. It never runs the game.
The [source manifest](Evidence/CpuProfile20260907/source.json) pins audited Git
blob hashes independently of subsequent working-tree changes.

### Interpretation rules

The analyzer reconstructs stacks through prefix/frame/function tables and sums
`threadCPUDelta / 1,000,000` (metadata declares nanoseconds). Each function gets
one inclusive contribution per sample even if recursive. These are **sampled
CPU attribution estimates**, not instrumented call durations or frame latency.
Nested rows overlap; worker times overlap the main thread. Do not add inclusive
rows or convert their sum directly into a promised FPS gain. No frame p50/p99 is
inferred from sample spacing. Native address-only frames remain unresolved even
though the capture says `symbolicated: true`.

Allocation markers are threshold events. Their type names identify the object
that crossed a roughly 100 KB allocation threshold; the accompanying bytes can
include other types. This export supplies no allocation stack reference. The
type-weight table is useful for suspects, **not exact bytes owned by a type**.
This interpretation follows [Microsoft's GC event definitions](https://learn.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events#gcallocationtick_v2-event).
The [Firefox profile schema](https://github.com/firefox-devtools/profiler/blob/main/src/types/profile.ts)
and [data-source guide](https://github.com/firefox-devtools/profiler/blob/main/docs-developer/data-sources.md)
inform format interpretation; this is an imported ETW capture, not Firefox's own
sampler. Neither external source establishes s&box-specific timing correctness.

## What the performance logs say

| Metric | Full moving window | Final stationary window |
| --- | --- | --- |
| Duration / samples | 121.92957 s / 106,994 | 10.000377 s / 9,653 |
| Average FPS | 877.40 | 965.30 |
| Frame p95 / p99 | 1.6857 / 3.7231 ms | 1.3038 / 2.2739 ms |
| GPU p95 / p99, for context | 1.3189 / 1.7562 ms | 0.9394 / 1.2946 ms |
| Managed allocation per frame | 31,087.975 bytes | 26,350.875 bytes |
| Managed allocation total / rate | 3,326,226,640 bytes / 26.02 MiB/s | 254,364,992 bytes / 24.26 MiB/s |
| GC generation 0 / 1 / 2 counts | 70 / 58 / 0 | 5 / 5 / 0 |
| Frames reporting collections | 70 | 5 |
| Reported cumulative / maximum GC pause | 612.432 / 11.912 ms | 42.405 / 9.399 ms |
| Exceptions / truncated frame samples | 0 / 0 | 0 / 0 |

The project samples engine `PerformanceStats.FrameTime`; these frame percentiles
are not isolated CPU execution time. CPU/GPU percentiles cannot be subtracted
to calculate CPU-only cost. Allocation/GC counters are engine-wide telemetry,
not terrain-owned accounting. Gen0 and Gen1 counts overlap; do not add them as
independent pause counts.

The trace independently contains 14 blocking GC events (six depth-0, eight
depth-1), totaling 122.8423 ms, maximum 10.7542 ms. These are exported GC event
intervals; restart-execution markers are absent, so they do not reconstruct the
complete suspension-to-resumption interval. No depth-2 event appears.

The steady-state allocation is the more important recurring memory question than
the single startup burst. Collection frames are only about 0.065% of moving
frames: p99 alone can miss these pauses. Preserve maximum GC pause and counts;
future per-frame spike attribution should identify GC overlap rather than assume
that GC explains the elevated p99.

The last 200-frame profiler snapshots show moving/stationary mean Render
0.5564/0.5053 ms, Animation 0.1120/0.1107 ms, Editor 0.1010/0.1077 ms,
and terrain manager 0.02154/0.00807 ms. They are short rolling windows near
completion, not whole-route averages. In particular, zero preparation calls in
that window do not contradict 1,995.046 ms of explicit preparation time across
the journey. One exported sampling scope even has p99 0.0054 ms above its
reported maximum 0.004 ms; `CaptureTiming` combines engine `GetMetric(200)`
fields with separately copied history. Resolve their window/semantic mismatch
before using these snapshots as rigorous tail gates.

Historical context matters. Accepted retained control
`b80cd7b7de3947c79c45d436b45b996d` recorded moving p95/p99 1.161/1.9171 ms
and stationary 1.0297/1.411 ms, with 28,852/24,706 bytes per frame. The manual
result is worse, but different process/history, active profiling/conversion,
and unpinned setup prevent assigning a code regression. Earlier cold candidate
and restored-control runs both improved broadly after restart; the
[ledger](../ValidationResults.md#gpu-meshing-512-001v1---gpu-simplification-investigation)
explicitly rejects attributing that environment improvement to compact emission.
The raw log contained 68 completed results when first inspected; this review
uses the matching manual run and the relevant same-workload historical controls,
not a cross-schema/cross-radius aggregate.

## CPU attribution in the moving capture

The selected 20-second interval contains 69,567 main-thread samples and
11,040.7 ms of CPU weights. Percentages below use that main-thread weight as
denominator, not wall time, total process CPU, or frame time.

| Inclusive path | Sampled CPU ms | Main-thread share |
| --- | ---: | ---: |
| Scene.Render | 2,796.5 | 25.3% |
| SkinnedModelRenderer.AnimationUpdate | 1,515.7 | 13.7% |
| VoxelManager.OnUpdate | 1,380.6 | 12.5% |
| Editor.ManagedTools.RunFrame | 717.7 | 6.5% |
| TryCommitPendingClipboxPlacement | 628.7 | 5.7% |
| CommitPendingClipboxPlacement | 405.5 | 3.7% |
| PrepareLodPlacement | 322.9 | 2.9% |
| RebuildDesiredChunks | 264.3 | 2.4% |
| GpuTerrainRangeAllocator.Release | 243.4 | 2.2% |
| CapturePendingClipboxReadiness | 221.0 | 2.0% |
| UploadVisibilityDescriptors | 149.3 | 1.4% |
| SleepForFrameRateClamp | 436.2 | 4.0% |

The lower terrain rows are inside higher rows. The allocator alone is about
17.6% of sampled manager time; readiness plus preparation are about 39.4%.
These identify where to investigate, not how much can be eliminated. Frame-clamp
CPU is deliberate pacing activity, not useful game work to optimize by changing
the acceptance workload.

Across the whole trace, main-thread CPU weights total 13.58 seconds; the named
Vulkan render thread totals 1.38 seconds. Render workers also execute terrain
command lists/readbacks: CPU work is not confined to `OnUpdate`. Native/driver
symbols are incomplete and no cross-thread critical-path reconstruction exists
here. Low manager time cannot establish that the terrain renderer is free.

There is direct profiler perturbation evidence: the finalizer thread has 1.484
seconds of CPU weights, with 1.430 seconds inclusive under
`ETW::GCLog::SendFinalizeObjectEvent`. Do not label this as 1.43 seconds of normal
game finalizer work. The trace includes 1,365 JIT markers and editor interaction
as well. A profiler-off control is essential before making frame-time claims.

## Project-owned opportunities and falsifiable hypotheses

### Allocation-free membership checks

[`GpuVoxelMesher.Contains` and `Remove`](../../Code/Voxels/GpuVoxelMesher.cs)
use nested `Any` predicates capturing a descriptor/key. Similar code exists in
transition removal. The capture has 126 threshold events naming
`Func<InFlightMesh,bool>`, 120 naming `Func<ScratchLane,bool>`, and 104 naming
`Func<CandidateMesh,bool>`. Their combined threshold byte weight is about
35.6 MiB, but is **not** proof those delegates allocated 35.6 MiB. Anonymous
display-class numbers are not stable source identities.

Hypothesis: direct iteration over the three existing lanes and their bounded
lists removes avoidable delegates/closures without a new lookup index or cache.
Preserve descriptor equality, revision/cancellation checks and early exits.
Compiler closure allocation may occur before a method's fast return; measure
actual allocation stacks or same-source A/B bytes before claiming which branch
owns it. Existing `CountInFlight` methods already use direct loops: replacing
them again would not fix this issue. Isolated evidence should show lower moving
bytes/frame and fewer relevant allocation stacks; a stationary benefit is not
assumed because settled membership queries may not execute.

### Incremental free-range release

[`GpuTerrainRangeAllocator.Release`](../../Code/Voxels/GpuTerrainRangeAllocator.cs)
appends, sorts all free ranges, and scans backward to merge on each release.
The moving trace makes this a measured target, beyond the earlier P3 source-only
question. The full result ends with 5,486 free ranges across arenas, not in one
list. Releasing a geometry handle updates both vertex and index allocators.

Hypothesis: sorted insertion plus predecessor/successor merging lowers removal
cost while retaining first-fit allocation and the same ownership. List movement
remains O(n); this is not an O(1) allocator. Preserve bounds/overlap detection,
empty-range behavior, adjacent coalescing and exact accounting. Compare release
time per placement and worst commit frame at the same free-list pressure. Avoid
replacing the GPU allocator or changing arena sizes in this CPU experiment.

### Placement dependency polling and changed-set construction

[`CapturePendingClipboxReadiness`](../../Code/Voxels/VoxelManager.cs) walks
entering LOD0 coordinates and every stored regular/transition readiness entry
on pending updates. Satisfied entries remain until the placement lifecycle
clears the lists. It rebuilds descriptors and looks up resident identity again.
The existing scan skip applies to unchanged **complete** coarse caches during
preparation; it does not eliminate this pending-frame polling.

The full result reports 27,653 readiness blocks, 74,163 deferred updates,
707 commits, maximum anchor lag 4, 4,456,448 preparation cache scans and
3,154 skipped levels. Preparation totals 1,995.046 ms, with an 11.5228 ms
maximum invocation. Range rebuilding totals 1,774.406 ms, maximum 7.9322 ms;
these differently scoped totals overlap and must not be added.

Hypothesis A: prune satisfied entries in the existing dependency lists when
their identity is guaranteed valid until atomic commit, or gate rechecks on
the relevant preparation/publication changes. A publication revision alone
cannot detect completion of CPU preparation or invalidation/removal. Explicitly
cover content resets, cancellation, empty publication and supersession.
Never permit partial placement publication to hide the remaining dependency.

Hypothesis B, only after A is measured: derive entering/leaving box and hole
slabs for changed coarse levels instead of rebuilding all coordinate sets and
diffing them. Preserve one canonical set owner, exact negative-coordinate
behavior and transitions. A broad slab rewrite has a larger correctness surface
than eliminating needless checks; measure each separately.

### Visibility upload and command submission

[`UploadVisibilityDescriptors`](../../Code/Voxels/GpuVoxelMesher.cs) uploads
both complete bounds and source-argument arrays on any descriptor dirtiness.
At the observed 8,192-slot capacity, their payload is 8,192 * (32 + 20) =
425,984 bytes per dirty upload pair. Upload count over the journey is not in
the saved result, so total bandwidth is not inferred. The 149.3 ms sampled
main-thread cost includes API/driver work; it is not GPU execution time.

Hypothesis: coalesce changed contiguous ranges and leave unchanged descriptors
alone; ensure full initialization after growth or a new camera state. A single
dirty min/max span may still cover most slots, so measure actual bytes and calls.
Preserve GPU-only writes to visible arguments, locks, current commit boundary,
and safe resource lifetimes. Separately audit which removals truly require a
command-list rebuild: commands depend on buffers/active arenas/measurement mode,
whereas region identity updates live in descriptors. Do not rebuild an executing
list from the render rendezvous. Historical native crashes make that boundary
part of correctness, not optional overhead.

The final log reports about 14.1 terrain indirect API submissions and 7,205
argument records per frame, with about 936 nonzero visible draws. These are
different quantities; thousands of argument records do not mean thousands of
C# draw API calls. The explicit draw-commit total is 1,021.477 ms, but its
counter only adds elapsed time when commands rebuild; descriptor-only early
returns are omitted. Improve attribution before treating it as total upload cost.

## Memory findings

### Recurring allocation and engine work

The capture contains 6,020 allocation ticks with 685,108,168 weighted bytes.
Prominent threshold labels include RenderAttributes (291 ticks), timer/delay
objects (212/213), editor descendant iterators (210), task-replication slot
arrays (195), and the terrain delegates above. These motivate allocation-stack
profiling of engine rendering, animation task scheduling, editor controls and
terrain membership. They do not justify assigning all GC pressure to Voxels3.

Animation is a material stationary CPU item. The authored player includes the
citizen SkinnedModelRenderer; the capture also has task-replication allocation
types tied to SkinnedModelRenderer. Investigate the actual animated renderer
count, first-person/offscreen requirements and scheduling overhead. Do not
infer three characters from 610 calls over 200 frames or disable the player's
animation as an equivalent optimization. Engine-side changes need verified
engine ownership/API evidence and a separate measured proposal.

### Performance-test allocation burst

At 3.813..3.817 seconds the main thread reports 27 large `Single[]` ticks:
21 allocations of 2,097,176 bytes and six of 262,168 bytes, totaling
45,613,704 bytes. This matches source payloads exactly apart from headers:
21 * 524,288 floats + 6 * 65,536 floats = **43.5 MiB**. Owners are regular,
per-level, transition, outer and throughput measurement buffers. The manager
also permanently owns four 524,288-float frame/sort arrays, another **8 MiB**.
The moving log's maximum allocation frame is 45,726,464 bytes.

This is strong evidence of instrumentation startup allocation, not growing
terrain sample arrays. Measurement completion releases the mesher arrays;
the permanent manager arrays persist. Potential experiment: prepare/reuse
bounded measurement storage before the measured window, with exact capacity,
reset/truncation semantics and an explicit retained-memory budget. Reuse trades
allocation for retained memory; shrinking capacity silently or changing the
sampling/percentile definition would invalidate comparability. A measurement
contract change needs a ledger decision before execution.

### Heap, process memory and GPU memory are different

- GC heap markers: first 921.84 MiB, peak 941.23 MiB, last 937.49 MiB.
  Gen2 stays at 777,405,496 bytes throughout; no Gen2 collection is recorded.
  Without a full live-object/root census, neither retained garbage nor a leak
  can be attributed. The counter array stores deltas after its first sample;
  summing or reading individual deltas as heap sizes would be wrong.
- Whole-process approximate memory: moving 4,229.27 to 4,237.45 MiB,
  peak 4,238.63 MiB. This short run does not show extreme sustained growth.
  It also cannot establish that repeated sessions/camera switches are bounded.
- Terrain vertex/index GPU capacity: **672 MiB committed versus 375.82 MiB
  used**, about 55.9% utilization and 296.18 MiB free capacity. Scratch is
  another 36.43 MiB. These are GPU resource bytes, not managed CPU heap bytes.
  The engine-wide GPU total (about 1,945.56 MiB at moving end) includes other
  resources. Never add it to the process figure as independent physical RAM.
- The source retains camera states on handoff and superseded visibility buffers
  until cleanup/disposal. This is a concrete lifetime concern, not a measured
  leak in this capture. Establish an engine-supported completion boundary before
  reclaiming; an arbitrary frame delay is unsafe. Track retired counts/bytes
  separately from ordinary arena high-water capacity.

GPU arena utilization remains a worthwhile crossover, but CPU release cost and
GPU capacity are separate problems. Smaller arenas may increase CPU submissions,
metadata and searches. The aggregate fragmentation metric is not equivalent to
the 44.1% unoccupied committed capacity. Preserve live-replacement headroom and
bounded scratch; do not promise to recover the full free capacity.

## What not to prioritize from this evidence

The CPU SDF classifier is not the leading sampled main-thread cost, and warm
result integration totals only 62.09 ms across the full journey. More worker
threads, dense CPU density storage, a CPU mesher, or a new terrain algorithm
are not supported next steps. Existing completed-result retention, idle request
array removal and canceled-count rejection are already implemented; the manual
run records 1,430 retained results, 149,585 empty submissions avoided and 15
canceled regular outputs skipped.

Count readback totals 33.3 seconds across overlapping batches; that is not 33.3
seconds of CPU blocking. Regular CPU allocation averages 0.00753 ms per measured
batch, and count submission 0.12773 ms; neither measures release cost. Outer
work and transitions have different populations. Current normal batch occupancy
is 7.79/8 and foreground drain 27.43 ms. Scheduler critical-path work may help
coverage, but raising concurrency cannot be justified by summed waits alone.
The 17,798 transition-deferred counter is incomplete by design and does not
prove starvation. Do not revive rejected compact-emission results as a proven
frame-time win.

## Follow-up measurement plan and acceptance boundary

No new in-world run was performed for this documentation-only review. Existing
user evidence was analyzed after capture; no pass criteria were invented for it.
Future runtime work must first record its hypothesis and measurable benefit in
the ledger and reuse **GPU-MESHING-512-001/v1** unchanged, including stop/play,
settlement, route, player, frame cap, camera, diagnostics and existing gates.

1. Obtain a fresh unchanged-source profiler-off control with exact source and
   environment identity. Use the accepted comparable baseline plus the nearby
   control; keep every failure. Do not accept the profiled manual run as control.
2. Measure membership allocation and allocator release separately through the
   real figure-eight. Require an attributable bytes/frame or release-time
   reduction as well as all existing frame/publication/correctness gates.
3. Add bounded production attribution only where missing: release/commit tails,
   readiness entries checked, descriptor bytes/calls and command reset reasons.
   No per-coordinate logs or diagnostic-only execution paths. Version telemetry
   when its meaning changes; instrumentation has its own unchanged-source control.
4. For placement work, verify identical settled geometry, empty-region handling,
   all transition dependencies, cancellation/content revisions, negative crossings,
   drain and peak anchor lag. Faster readiness must not publish early.
5. Use a separately identified profiler capture for stack attribution, retaining
   its observer overhead. Editor-versus-packaged allocation, repeated camera
   handoffs and long-session retained memory need fixed supporting scenarios
   recorded before execution; they are not substitutes for canonical acceptance.

Remaining unknowns: allocation call stacks and exact terrain byte ownership,
packaged-game CPU cost, native render critical path, precise GC-to-frame spike
overlap, retained-object roots, camera-resource retirement safety, lower-tier
hardware, and multiplayer scaling. Collision, terrain edits and project-specific
replication are not implemented workloads here. Findings should be revisited
against the then-current source before implementation.
