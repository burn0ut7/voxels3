# CPU and memory enhancement directions

Research slice dated 2026-09-07. Status: proposals and decision gates only;
no runtime changes or new performance acceptance results.

## Recommendation

Yes: Voxels3 can gain capabilities that make performance more predictable,
reduce the cost of maintaining terrain state, and reuse work more deliberately.
The strongest directions are **compact region metadata**, **bounded placement
planning**, and **dependency-aware scheduling with explicit memory accounting**.
A bounded revisit cache is also plausible, but exchanges memory for regeneration
work rather than improving both automatically.

This document concerns changes to representation, scheduling or lifecycle. The
[CPU capture review](CpuPerformanceReview20260907.md) owns local fixes such as
removing capturing predicates, incremental free-range release, pruning repeated
checks and partial visibility uploads. Those fixes should be measured first;
they may remove enough cost that a larger enhancement is unnecessary.

| Direction | Main benefit | Memory effect | Recommendation |
| --- | --- | --- | --- |
| E0: terrain-owned memory census | Makes memory decisions attributable and exposes retention. | Small bounded diagnostic overhead; no direct saving. | Establish before a memory redesign. |
| E1: compact bounded region records | Fewer managed objects, hashes and duplicated membership entries. | Potential CPU metadata reduction; size unmeasured. | Best representation investigation. |
| E2: placement planning with bounded work per update | Lower main-thread spikes and controlled backlog. | One bounded plan in addition to committed state. | Best frame-pacing enhancement if local fixes leave spikes. |
| E3: dependency-aware admission with memory headroom | Spend resources on the work that completes coverage. | Bounds optional work and reservation growth; needs a fit policy. | Design after critical-path and peak-memory evidence. |
| E4: bounded reuse of recently retired regions | Less rebuilding on backtracking. | Retains more live bytes unless it fits existing spare capacity. | Conditional on observed reuse. |

No percentage FPS improvement or whole-process memory reduction is predicted.
The task is research, not authorization to change view distance, terrain detail,
simulation cadence, animation behavior or the canonical acceptance workload.

## Source and evidence boundary

The starting point is `f633de0`, whose runtime is unchanged from `aab4bc8`.
The earlier review's [source manifest](Evidence/CpuProfile20260907/source.json)
pins that runtime. Current source was inspected for region-state collections,
resident records, placement preparation, serialized background classification,
and scheduling. Concurrent working-tree GPU telemetry/schema work was observed;
it is not evidence that the proposals here are implemented or accepted.

Current ownership remains the
[voxel foundation](../Architecture/VoxelChunkFoundation.md) and
[GPU meshing contract](../Architecture/GpuVoxelMeshing.md). The authoritative
world is the implicit field/configuration; all preparation, residency, geometry
and proposed caches are revisioned derivatives. This research introduces no
second terrain truth, CPU mesher, extra renderer or alternative mutation path.

The [September 7 evidence](Evidence/CpuProfile20260907/summary.json) motivates
the questions: the manual 512-radius journey records 4.46 million preparation
cache scans, an 11.5228 ms maximum preparation invocation, and about 26 KiB
allocated per stationary frame. Sampled readiness plus preparation are roughly
39% of sampled moving manager CPU time. These are different metrics, not
additive budgets. The capture/manual run are observational and affected by the
editor/profiler; none is a fresh controlled baseline for these proposals.

The process footprint is about 4.2 GiB, but terrain-owned managed bytes were
not measured. Geometry's recorded GPU capacity and live-byte figures have a
different scope. This document does not assume CPU metadata is the dominant
memory consumer simply because collections are visible in source.

## E0: terrain-owned memory census

**Enhancement:** make retained memory an explicit part of the existing production
diagnostics. This is the enabling slice for E1/E3/E4, not a new profiling framework.

Inputs are capacities and lifetimes already owned by `VoxelManager` and
`GpuVoxelMesher`. Outputs should distinguish manager collections, resident and
pending records, measurement arrays, live geometry, committed arenas, scratch,
visibility buffers, and retired camera resources. Owner-reported logical bytes
must be labeled separately from actual managed-object overhead or driver memory.
Do not sum overlapping scopes into a misleading total.

Record active count, capacity, peak, retained-after-settle amount and the event
that permits release. Sample slowly and serialize after measurement, following
the existing result lifecycle. Avoid enumerating the entire heap, constructing
strings, or traversing every resident each frame merely to report a budget.
Where practical, derive counts at the existing allocation/publication boundary;
do not introduce a second resource registry with independent lifetime decisions.

Then compare a post-settle managed-object census with those counters. Microsoft's
[GC performance guidance](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/performance)
supports distinguishing retained objects from allocation and investigating roots.
[`dotnet-gcdump`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump)
can collect a managed heap graph, but induces a full Gen2 collection and can
suspend the process. Its availability/attachment to this engine is unverified;
use it only outside timed gameplay, if supported. A dump is evidence about one
instant, not proof that resources are bounded for a whole session.

**Decision gate:** attribute the largest retained categories before choosing
pooling, compression or resource reclamation. If terrain metadata is only a few
MiB, prefer E1 for demonstrated CPU locality/allocation benefit, not as the answer
to a multi-GiB process. If most growth is camera retirement, follow the existing
review's safe-lifetime investigation rather than redesigning chunk storage.

## E1: compact bounded region metadata

### What changes

`TerrainClipboxLevelState` currently owns four coordinate hash sets plus lists
for entering/leaving/active changes/readiness. `GpuVoxelMesher` separately owns
resident/pending maps and active membership. Those domains have legitimate
different responsibilities, but repeatedly store and hash regular spatial keys.
`ResidentMesh` is a managed class containing a full descriptor, residency,
geometry handle and counts; `PublishKnownEmpty` creates a resident record even
when the geometry handle is null. This is metadata, not a dense SDF allocation.

The enhancement would represent **bounded spatial membership with slots/flags**
and store only the payload each state needs. Manager placement remains owned by
the manager; mesher residency remains owned by the mesher. Shared key/epoch
contracts replace repeated descriptor materialization where equivalent, without
making either owner independently decide the other's state.

Inputs: level bounds, coordinate, content/configuration identity, preparation and
publication state. Outputs: the same membership/readiness answers and meshing
requests as today. A compact slot must retain its absolute coordinate or a
validated coordinate tag, content identity and slot generation. Known-empty,
unknown, pending, stale and geometry-resident must remain distinct states.
Keep the counters/digests needed for current correctness evidence; an empty flag
cannot silently discard generation or publication semantics.

### Serious alternatives

| Representation | Advantage | Cost or hazard |
| --- | --- | --- |
| Existing maps, with local fixes | Lowest migration risk; handles staged overlap naturally. | Hashing, object/collection overhead and repeated keys remain. |
| One canonical indexed record pool, maps store indices | Retains flexible spatial lookup while reducing object churn. | Hash lookup remains; stale index reuse needs generation checks. |
| Bounded 3D toroidal slots per level | Direct indexing and contiguous metadata, with flags for membership. | Wraparound aliases old and incoming coordinates; committed/staged lifetimes complicate reuse. |

The **indexed pool is the lower-risk first candidate** if census evidence shows
resident object churn matters. Toroidal storage is a larger candidate only if
hashing/set memory remains material. These are alternatives, not layers to add
on top of each other. Any chosen responsibility must replace its old storage.

For scale intuition, one 16^3 cache has 4,096 coordinates. A single membership
bitplane takes 512 bytes; four bitplanes take 2 KiB. A hypothetical 32-byte slot
array takes 128 KiB. These are layout arithmetic, **not proposed full layouts or
measured savings**: tags, revisions, payloads, transitions, buffers and staged
overlap add storage. A cache-sized ring alone cannot hold two conflicting
committed/staged coordinates in the same slot. Bound and account for that overlap
before sizing; never overwrite a coordinate still needed by committed coverage
or in-flight GPU work. Negative modulo/floor behavior must be explicit.

[Geometry Clipmaps](https://hhoppe.com/geomclipmap.pdf) informs bounded regular
storage and toroidal/incremental updates. Its 2D heightfield rendering and
transition scheme do not transfer to this full-3D SDF/Transvoxel pipeline.
Here the proposed regular storage holds region metadata, not densities or a
replacement mesh representation. No heightfield simplification is adopted.

**Decision gate:** measure managed live bytes/object counts, lookup cost,
allocations per traveled distance and frame tails. Require exact membership,
revision rejection, empty-state behavior and geometry equality through movement,
backtracking, negative crossings and configuration restoration. Reject a layout
that saves bytes but increases common lookup/commit cost or requires an unbounded
overflow map. Do not implement it solely on the bitplane arithmetic above.

## E2: bounded placement planning

### What changes

Local scan/pruning fixes make individual operations cheaper. This enhancement
adds a **planning lifecycle with a finite amount of unfinished work**, so one
movement update need not finish all preparation synchronously.

Input: an immutable committed placement snapshot, target anchors/configuration,
and content revision. Output: a bounded plan containing regular and transition
changes and unresolved dependencies. The manager owns one in-progress plan plus
one latest target; the existing mesher owns all resource operations. Old geometry
remains committed until the complete candidate is valid and ready.

Prefer a resumable main-thread planner first: advance a fixed number of region
operations per update, optionally yielding earlier on a measured time budget.
Spatial ordering/tie breaks remain deterministic; elapsed time may choose when
to yield, not which regions exist or what their field values are. Partial plans
must not leak into active sets. Use revision checks to discard invalid plans,
coalesce superseded targets and bound pending work. Repeated motion must not
cancel all progress indefinitely; retain a still-valid intermediate placement
when permitted by the existing placement contract, then pursue the latest target.

Background planning is a second alternative if arithmetic still causes spikes.
Offload only pure coordinate/transition-plan computation over immutable inputs.
Do not read mutable dictionaries, scene objects or GPU resources from a worker.
Count snapshot-copy and reconciliation costs, and include competition with the
existing serialized warm classifier before adding any concurrency.

The current classifier already uses `Task.RunInThreadAsync` and explicitly
returns through `Task.MainThread`. Installed 26.09.01c XML includes thread-switch
entries, while [official s&box async guidance](https://sbox.game/dev/doc/scene/components/async)
states that async normally runs on the main thread and warns about accumulating
tasks and object lifetime. Merely marking placement `async` does not offload it.
The existing path proves a usable project pattern, not thread safety for every
engine API. No new worker API is selected by this research.

### Limits and acceptance

Time slicing can lower maximum frame work while **increasing coverage latency**.
It does not reduce total CPU automatically. The final active-set/resource commit
still has work and must preserve atomic publication. Do not split that commit
across visible frames or claim the planner alone fixes allocator release spikes.

Define maximum plan entries/bytes, one-plan backlog, per-update work budget and
cancellation rules before implementation. Measure plan CPU, snapshot-copy CPU,
worker CPU if used, stale work, commit maximum, p95/p99 frames, placement lag and
publication tails. Keep the existing gates; lower spikes with worse accepted
coverage latency is not a pass. If local improvements already bound preparation
comfortably, retain the simpler synchronous owner.

## E3: scheduling by coverage dependencies and memory headroom

### What changes

The scheduler already has gameplay-first queues, three scratch lanes, bounded
batches, and an outer-work service-delay rule. The enhancement is **admission
based on the current placement's outstanding dependencies and memory headroom**,
rather than treating more submitted regions as the primary success metric.

Inputs: current staged dependencies, existing queue identity/revision/age,
published completion, measured stage latency, and resource accounting from E0.
Outputs: which existing request may enter the existing lane and which optional
work waits. Scheduling remains owned by the mesher; the manager supplies
placement requirements through a narrow immutable/versioned contract. There is
no second scheduler or parallel queue of independently authoritative requests.

Start with deterministic dependency/age priority and retain outer fairness.
Required transitions can deserve service before nonblocking regular work.
Only add velocity/deadline prediction after measuring actual deadline misses.
An estimate may reorder already-requested work; it must not remove desired
coverage behind the player. Bound bookkeeping so priority computation does not
become a new full-cache scan on every frame.

Memory needs separate categories: committed coverage, replacement candidates,
scratch, retirement, and optional reusable regions. Reserve completion headroom
before optional admission and reclaim eligible optional work first. Pending
count does not predict geometry bytes: exact counts arrive asynchronously.
Blocking every count-ready lane while waiting for memory can deadlock a handoff
whose old coverage cannot retire until replacement completes.

That is a **design blocker to resolve**, not a solved policy. Establish a safe
upper bound/reservation or a demonstrated completion-fit rule for the supported
configuration. If the required committed-plus-replacement working set cannot
fit, throttling cannot fix it. Keep the last valid state and report the budget
failure; silently lowering LOD/view distance is a separate user-visible quality
policy, not an equivalent optimization. An observed average mesh size is not a
safe hard reservation bound.

[World Partition](https://dev.epicgames.com/documentation/unreal-engine/world-partition-in-unreal-engine?lang=en-US)
provides useful examples of source priority and distinct loading/activation
states. It does not establish this game's geometry-memory fit or atomic
transition correctness. This proposal extends the earlier
[publication/scheduling questions](ChunkPerformanceOptimizationFindings.md#publication-and-scheduling-alternatives)
into an explicit admission contract; it does not claim deadline scheduling is
already implemented.

**Decision gate:** first show cases where work unrelated to completing the
current placement consumes scarce lanes/headroom. Measure missing dependencies,
their age, eligible service gaps, peak candidate/retired bytes and time-to-coverage.
The existing transition-deferred counter is incomplete and cannot prove that
case alone. Require better coverage tails or lower peak committed resource
growth, alongside unchanged frame and fairness gates. Do not bundle larger
batches, a fourth lane, allocator replacement or reduced residency.

## E4: bounded reuse on backtracking

### What changes

Current warm caches and retained preparation already reuse work. The new
capability would retain selected **recently departed, no-longer-required**
derived residents, to avoid reclassification/count/emit/allocation when the player
returns. It must offer measured reuse beyond those existing caches.

Prefer a retention state in the existing resident owner. Key it by complete
content identity, not coordinate alone. A retained mesh is inactive in drawing;
re-entry promotes that same record after identity checks. Keep no duplicate
geometry or separate terrain truth. Define a hard byte limit, minimum replacement
headroom, age/eviction policy and cancellation/configuration invalidation.
Pinned committed/staged/in-flight dependencies cannot be evicted as optional.

Under a fixed total capacity budget, retention may occupy already-free arena
ranges but can still prevent trimming, increase fragmentation and raise peak
capacity later. Measure both retained live bytes and committed bytes. Cache
hit ratio is insufficient: count avoided CPU work/dispatches, cold misses,
eviction cost and actual end-to-end latency. A straight journey may receive no
benefit and pay retention overhead.

First collect reuse distance and bytes for **evicted** identities during the real
route, with bounded telemetry. If return happens only after a cache larger than
the acceptable budget, do not implement the enhancement. Prioritize inexpensive
metadata retention only if it avoids material work; CPU classification currently
does not dominate, so keeping classifications alone may offer little benefit.

Disk-caching generated meshes is not the first form of this enhancement. It adds
versioned storage, I/O, serialization and upload costs; normal production currently
does not read geometry back. Do not add geometry readback to populate a disk
cache without evidence that repeated startup/revisit cost warrants a new contract.

## Frame pacing and chunk-loading throughput

Added 2026-09-07 in response to the explicit frame/loading follow-up. Source
reference remains the runtime pinned above; the current scheduler and telemetry
call sites were rechecked while concurrent GPU work was in progress. No new
runtime result is introduced. These directions elaborate E2/E3 rather than
creating another scheduler design owner.

### Define what becomes faster

There are three separate goals: less CPU work per frame, fewer long frames, and
less time until the player has complete usable terrain coverage. A change can
improve one and hurt another: submitting more batches in a frame can raise
throughput while causing a hitch. Completing regular regions faster can still
leave an entire placement waiting for a transition.

| Existing manual-run measurement | Value | Meaning and limitation |
| --- | --- | --- |
| CPU preparation integrated | 93,390 / 121.93 s, about 766/s | LOD0 preparation results, including proven-empty regions; not visible meshes. |
| Foreground regular submitted / published | About 859.9 / 859.4 regions/s | Existing non-outer regular throughput population; excludes outer and transition throughput. Empty results and inactive placement candidates can still publish. |
| Foreground regular batch occupancy | 7.789 of 8 | Batches are already nearly full; packing alone has at most about 2.7% headroom at a fixed batch rate. This is arithmetic, not a speedup prediction. |
| Regular schedule-to-renderable p50 / p95 / p99 | 18.778 / 74.636 / 102.353 ms | Existing foreground latency population; region publication is not complete placement coverage. |
| Foreground count callback wait, mean / p99 | 0.558 / 2.187 ms | Callback readiness to later consumption; one possible scheduling gap, not GPU kernel time. |
| Foreground emit-to-publication, mean / p99 | 1.833 / 4.043 ms | Includes the required render-sequence boundary and update scheduling. |
| Foreground drain | 27.43 ms | Short at route end; does not prove every moving placement was timely. |
| Maximum placement anchor lag | 4 level-local regions | Distinct from route-distance lag; must be assessed per the fixed scenario. |

The saved `totalQueue` is only gameplay plus warm queue counts: its sampler calls
`SampleQueueDepth(PendingGameplayCount, PendingWarmCount)`. It does not include
near-visual, outer and transition queues. Its median zero therefore does not
establish that the whole mesher was idle. Similarly, logical gameplay-region
counts are analytic membership, not chunks constructed per second.

Use **valid, still-required publications per second**, broken out by level and
regular/transition type, plus **request-to-complete-placement latency** and
time-to-first-required-coverage for loading. Keep raw submitted regions/s as a
work counter. Do not pool coarse and fine regions into an apparent equivalent
work rate: their world coverage and geometry cost differ.

### F1: remove avoidable dependence on very high frame rates

The current render rendezvous advances at most once per manager update epoch.
`ProcessGpuRenderTick` admits at most one new regular, outer or transition count
batch, each at most eight regions. Foreground count consumption can coexist with
a foreground submission, but that does not allow two new count batches. Outer
emission and transition continuations also consume service opportunities.

With U claimed epochs per second, **8U is an admission ceiling**, before lane
availability, transitions/continuations, CPU/GPU execution and publication waits:

| Hypothetical claimed epochs/s | Maximum newly count-submitted region identities/s |
| ---: | ---: |
| 60 | 480 |
| 120 | 960 |
| 144 | 1,152 |

This table is source arithmetic, not a benchmark at those frame rates. U is the
actual manager/render-epoch cadence, not an assumed display refresh rate. The
manual run's regular-count total including outer work was 133,775, or about
1,097 per moving second when normalized by its duration; those broader counters
span the measurement lifecycle and are not an exact steady-state demand rate.
Nevertheless, foreground alone at roughly 859/s leaves little theoretical room
at 120 epochs/s for outer/transition work. The high-FPS run cannot establish
ordinary-frame-rate loading capacity.

**Enhancement candidate:** allow a small, explicitly budgeted amount of additional
eligible work within the existing single claimed epoch, using the existing lanes
and eight-region batch size. Separate the CPU submission-time budget from count
batch admission and completion service. A bounded credit/deadline mechanism may
avoid tying throughput solely to frame count; cap accumulated credit so a slow
frame cannot trigger an unbounded catch-up burst. This cannot manufacture GPU
capacity, and no additional work is useful when lanes are occupied.

This changes the current one-new-batch policy and requires a new architecture
decision and telemetry capable of counting the actual work. It must not be
implemented by letting extra editor cameras advance the scheduler, by removing
the epoch guard, or by raising `MaximumDispatchesPerUpdate` indiscriminately.
Keep resource completion, camera ownership and outer fairness intact.

**Measurement gate:** first classify blocked admission by no pending work,
no idle lane, count pending, emit/publication pending, or policy budget. Record
how often eligible idle lanes coexist with denied work. Only then consider
multiple bounded admissions. Preserve the canonical test's frame cap; ordinary
60/120-epoch behavior needs a separately defined real-world supporting scenario,
recorded before execution rather than quietly changing the accepted baseline.

### F2: prioritize the last dependencies that unlock a placement

The current regular-work path can defer transition service even when a transition
is the last missing dependency. E3 should measure these cases directly and give
required transition continuation/completion bounded service. This can improve
visible loading latency without increasing total region throughput or memory.
It can also lower regular regions/s while producing better complete coverage.

Record the identity/type of the last dependency, its queued/ready/service times,
and the interval from all resources ready to placement commit. Never infer
starvation from the limited transition-deferred counter. Preserve regular
gameplay progress and outer service guarantees; prioritizing transitions forever
would simply move the starvation problem.

### F3: reduce pipeline gaps while preserving completion boundaries

Classify every occupied lane by lifecycle state and elapsed age. Examine whether
ready metadata waits for service while unrelated work is chosen, whether emitted
candidates wait an avoidable update after their required boundary, and whether
needed LOD0 results wait in the integration queue. Do not treat all wait time as
removable or force a synchronous readback to make the counters smaller.

The recorded foreground readback average is 1.891 ms; exact CPU allocation averages
0.00753 ms per batch. Those numbers do not support an allocator-ownership rewrite
just to remove CPU allocation time. The already-measured release spikes are a
different operation. Callback/emit waits, batch timings and schedule latency have
different populations and nested intervals; adding their p99 values is invalid.

For CPU preparation, the existing batch size is 256 and integration budget is
0.5 ms. If a critical result waits behind unrelated preparation, a smaller bounded
handoff batch or dependency-first ordering may reduce first-ready latency. More
task handoffs increase scheduling/allocation overhead. The low integrated CPU
total in the earlier review does not justify raising that budget or adding more
workers without a demonstrated starvation interval.

### F4: spend less CPU on publication bursts and steady-state frames

For movement hitches, E2 plus the local allocator/visibility improvements remain
the leading source-backed candidates. Instrument the full commit path, including
release, activation, descriptor upload and command recording. A bounded planner
does not bound the final commit automatically. Reducing or batching redundant
changes before the atomic commit is preferable to spreading visible activation
across frames and breaking coverage.

For stationary frames, the capture review's final short window attributes roughly
0.505 ms/frame to Render, 0.111 ms to Animation and 0.108 ms to Editor, versus
0.008 ms to the manager. These scopes are not an additive frame budget, but they
show why faster chunk planning alone is unlikely to transform stationary FPS.
Investigate CPU render submission/attribute churn and actual animated-renderer
work with engine evidence. Profile an equivalent packaged run separately to
isolate editor costs; do not count hiding editor UI as a shipped optimization.
Track GC-related spike frequency/maxima alongside frame tails: rare pauses can
fall outside p99 even when average FPS is high.

### Throughput acceptance must distinguish demand from capacity

The route schedules about 861 foreground regions/s and publishes about 859/s.
That is achieved throughput for the route, not a measured maximum loading speed.
An optimizer may leave that rate unchanged because the same player trajectory
requests the same work; the benefit may instead be lower queue age, less CPU,
lower latency and more headroom. Do not reject a valid improvement because it
cannot publish regions the route never requested.

Within the fixed route, compare intervals with real pending eligible work and
report arrival rate, useful completion rate, backlog slope, cancellation and
coverage latency together. A sustained completion deficit grows backlog; an
empty queue hides spare capacity. If saturation is never reached, label capacity
unmeasured. A fixed playable-world cold-loading observation may additionally
measure time from world entry to complete required terrain, but it requires
predeclared scene/seed/quality/spawn/camera/cache state and its own ledger record.
No synthetic terrain path or increased player speed is substituted for the
canonical figure-eight.

Recommended next frame/loading investigation: **classify lane stalls and final
placement dependencies first**, then compare bounded admission (F1) or dependency
service (F2) according to that evidence. Keep separate moving/stationary frame
p95/p99, rare-stall counts/maxima, useful publications by type/level, placement
latency, CPU time per useful publication, peak memory and correctness. Require
an attributable improvement in the selected metric plus all existing gates;
neither a larger submitted count nor smoother frames with delayed coverage is
sufficient by itself.

## Boundaries with other slices

The separate GPU work owns shader reductions and arena-size experiments. This
document owns CPU metadata representation, planning, admission and reuse ideas;
it makes no acceptance claim about concurrent GPU work. Arena compaction or
GPU-owned allocation also require new relocation/resource-lifetime contracts
and are not prerequisites for E1/E2.

Do not pursue broad object pooling, unsafe memory, a new ECS, an octree, dense
SDF pages or additional workers by default. Pooling can retain more live memory;
current runtime forbids unsafe blocks. The regular bounded workload may justify
specific storage changes, not a framework rewrite. Likewise, server-GC changes,
forced collections and no-GC regions are runtime-wide policies, not local fixes
for unidentified allocation ownership.

Animation/update-rate reduction and adaptive terrain quality could save CPU,
but change visible behavior or cadence and lack a measured supported API plan
here. They need a separately agreed quality contract. Collision, edits,
persistence and multi-player interest management remain future features;
performance estimates for them cannot be extrapolated from this one-player run.

## Choosing and validating the next slice

1. Finish and remeasure the small CPU optimizations from the capture review.
2. Add the smallest E0 accounting needed to identify the remaining owner. Record
   telemetry definitions and its unchanged-source overhead control first.
3. Choose **one** measured residual: object/hash pressure leads to E1; preparation
   spikes lead to E2; dependency delays/resource pressure lead to E3; demonstrated
   costly revisits lead to E4. Do not build all four speculatively.
4. Before implementation, move the chosen ownership/lifecycle decision into its
   architecture owner, define measurable benefit and budgets in the ledger, then
   exercise the production entry point. This research is not that final design.

Reuse `GPU-MESHING-512-001/v1` and the latest accepted comparable baseline with a
fresh unchanged-source control. Preserve exact workload, quality, frame cap,
diagnostics and correctness gates. Record every run/failure. Additional memory
or revisit observations need fixed parameters before execution and do not replace
the canonical figure-eight. A changed measurement/workload definition requires
the route's explicit scenario-versioning process; do not tune it to obtain a pass.

For every candidate report frame tails, allocation, peak and settled owned memory,
streaming/placement latency, stale work and geometry correctness. For E1 also
report objects/capacity and slot-reuse correctness; for E2 total CPU and backlog;
for E3 starvation/completion under pressure; for E4 net work saved per retained
byte. Retain an enhancement only when its measured benefit exceeds its added
complexity and all required non-regression gates pass.

No new runtime measurements, heap dumps or engine mutations were performed in
this research slice. Source, arithmetic, primary references and local document
links are the validation scope. Quantitative benefit, full retained-memory
ownership and installed heap-diagnostic attachment remain unverified.
