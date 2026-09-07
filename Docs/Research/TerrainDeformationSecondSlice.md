# Terrain deformation: second-slice research

Date: 2026-09-07. Audience: Voxels3 gameplay and terrain implementers.
Status at research time: **proposal; no deformation implementation or benchmark run**.
For the current implementation and nine-section completion status, see
[terrain deformation implementation](../Architecture/TerrainDeformation.md).
Source reviewed: `0159e1d334cb992646a9d8020749a159fe36bf1b`, plus the working
source inspected that day. Scene settings changed during research; the values
below describe the inspected scene, not a newly established performance baseline.

## Recommendation

Build one host-owned, spatially indexed edited field, with a hold-to-dig tool
on left click and hold-to-build on right click. This interprets “form” as
removing terrain; smoothing/flattening is outside this first tool proposal.
Keep the existing GPU visual extractor and CPU collision extractor, but make
both consume the same revised field and invalidate only their actual spatial
dependencies. Store accumulated regional sample changes, rather than evaluating
an ever-growing history of brush operations during every mesh build.

Optimize **input-to-visible change and input-to-safe collision**, alongside
frame pacing. Fast field writes alone do not make deformation feel immediate.
Use bounded bulk edits, coalesced rebuild requests, fair scheduling, and explicit
overload handling. A remote explosion changes authoritative data even when
there is no loaded mesh at its location. Rendering, collision interest, and
network subscriptions must remain separate from edit ownership.

The proposed first representation is sparse float correction pages over the
unchanged procedural base. It preserves today's inexpensive unedited world and
bounds repeated sampling cost by nearby stored data rather than edit count.
It is a design choice to measure, **not a demonstrated fastest implementation**.
Keep the existing 32-cell mesh size initially; only change storage granularity,
precision, or mesh size after measuring the actual deformation bottleneck.

The existing collision prototype is unaccepted. Its contact and performance
limitations remain gates for a playable deformation slice; this research does
not accept them or establish a new baseline.

## 1. What the current implementation actually does

| Responsibility | Source evidence | Consequence for deformation |
| --- | --- | --- |
| Field and coordinates | [VoxelChunk](../../Code/Voxels/VoxelChunk.cs), [ProceduralTerrainSdf](../../Code/Voxels/ProceduralTerrainSdf.cs), [foundation](../Architecture/VoxelChunkFoundation.md) | Chunks are immutable query views, not populated density arrays. Generator v5 supplies terrain directly. Negative is solid, positive air, zero surface. Base spacing is 16 world units; 32 cells span 512 units with 33 shared samples. |
| Visual input | [GpuSdfDescriptor](../../Code/Voxels/GpuSdfDescriptor.cs), [regular shader](../../Assets/shaders/voxels/voxel_persistent_geometry.hlsl), [transition shader](../../Assets/shaders/voxels/voxel_transition_geometry.hlsl) | Descriptors contain settings and a source revision, no edit payload. GPU sampling evaluates the procedural mirror. Changing only CPU queries would leave visuals unchanged. |
| Visual extraction | [GpuVoxelMesher](../../Code/Voxels/GpuVoxelMesher.cs), [GPU architecture](../Architecture/GpuVoxelMeshing.md) | Regular extraction samples a 35-cubed halo; gradients read adjacent lattice samples. Transitions have separate fine/coarse halo dependencies. Count readback, allocation, emission and publication are multiple stages. Meshes are already reusable derived caches. |
| Classification | `VoxelChunk.ClassifyDensityRange`, `VoxelManager.ClassifyClipboxRegion`, `VoxelCollisionMesher.Build` | Full-region, coarse vertical, and collision parent/child bounds know only the procedural field. These must include edits before any consumer can safely reject solid/air regions. |
| Invalidation | [VoxelManager](../../Code/Voxels/VoxelManager.cs), configuration reset and descriptor creation | Content changes currently advance a global revision and reset visual preparation/meshes. Repeating this per brush tick would rebuild unrelated terrain. No region-local mutation API exists. |
| Collision | [VoxelCollisionWorld](../../Code/Voxels/VoxelCollisionWorld.cs), [VoxelCollisionMesher](../../Code/Voxels/VoxelCollisionMesher.cs), [collision contract](../Architecture/TerrainCollision.md) | CPU regular extraction has one worker, at most two completed results, and three mesh buffers. Native integration admits at most one construction per update with a 0.5 ms admission budget; the indivisible native call can exceed it. |
| Collision dependencies | Same collision sources | Small-fragment support patches can sample one cell beyond their owner chunk. Their owners also need invalidation when those samples change. Existing replacement preserves old support until successful replacement; readiness and actor holding need local-edit semantics. |
| Input and multiplayer | [input settings](../../ProjectSettings/Input.config), [controller](../../Code/CustomTopDownController.cs), [project](../../voxels3.sbproj) | `Attack1` and `Attack2` already bind mouse1/mouse2. The custom controller contains movement/camera behavior, no terrain tool. Project metadata allows 64 players at 50 Hz; this is configuration, not demonstrated capacity. No voxel edit replication, save system, or multi-origin collision interest exists. |

The inspected scene had seed 1337, base height 0, amplitude 128, frequency
0.0005, gameplay radius 8 and visual radius 512. Defaults in classes differ
from scene-authored values. Future runs must record effective configuration.

The ledger's candidate18 collision run reports mean moving construction
2.0353183 ms/chunk and maximum whole creation 3.0009 ms, but 1606 collision
requests remained pending in the result and original frame-time acceptance
remained failed. These are historical workload measurements, not deformation
latency or a capacity promise. See the final candidate18 entry in the
[validation ledger](../ValidationResults.md) and its linked raw evidence.

## 2. What working implementations teach us

These are inspectable implementations and first-party documentation, not a claim
that every system is shipped, equally mature, or multiplayer-proven at our scale.

| Evidence | Useful lesson | Transfer limit |
| --- | --- | --- |
| [Voxel Tools performance](https://voxel-tools.readthedocs.io/en/latest/performance/) | Bulk edits amortize spatial lookup, synchronization and dirty scheduling. Eager queued copies can consume memory faster than workers process them. Collider installation is a separate expensive stage. | Godot thread restrictions and reported collider cost ratios are not s&box measurements. |
| [Voxel Tools smooth terrain](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/) | Distance range and precision affect locality and distant normals; maintaining an exact global distance field makes apparently local edits nonlocal. | Do not copy its 16-bit encoding or narrow-band width without validating our gradients, LOD and queries. |
| [Voxel Tools VoxelLodTerrain](https://voxel-tools.readthedocs.io/en/latest/api/VoxelLodTerrain/) | Its full-load mode allows edits anywhere at a memory cost. Mesh size affects edit latency; collision delay and collision viewers are separate concerns. | Its storage/residency choice is not a requirement to keep all edited data in RAM forever. |
| [Voxel Plugin runtime sculpting](https://docs.voxelplugin.com/knowledgebase/blueprints/runtime-edits-and-sculpting) | It distinguishes stamps, which add later generation work, from sculpted state, recommended for many incremental edits. Async work delays visible completion. | Version 2.0p8 calls this experimental. Manual replication and late-join transfer remain explicit limitations; it is not proof of solved multiplayer. |
| [Voxel Plugin 1.2 profiling](https://docs.voxelplugin.com/1.2/technical-notes/performance-and-profiling) | Spatial locks and prioritized work separate editing, meshing and collider work. Larger merged meshes reduce rendering overhead but slow updates. | Legacy Unreal implementation; do not attribute its internals to 2.0 or copy its engine APIs. |
| [Voxel Plugin 1.2 multiplayer](https://docs.voxelplugin.com/1.2/core-systems/voxelworld/multiplayer) | Action replay needs deterministic tools; its voxel-state replication path avoids rerunning tool decisions on clients and supports joining. | Its custom TCP transport and bandwidth statements do not transfer to s&box networking. |
| [Transvoxel, Eric Lengyel](https://transvoxel.org/) | Transition cells depend on local voxel data and support local retriangulation across 2:1 resolutions. | Topology does not solve asynchronous mixed-revision publication or scheduling starvation. |

The evidence favors localized materialized state and bounded derivative work.
It does not establish a universal CPU-versus-GPU winner or a defensible edits
per second guarantee. Closed-source game demonstrations are insufficient to
infer their storage, networking, or latency budgets.

## 3. Proposed authoritative field and brush

### Ownership and representation

Introduce one world-owned field/store responsibility under the manager. Names
here describe responsibilities, not existing APIs. It owns world identity,
immutable generator settings/version, sparse correction pages, page revisions,
accepted mutation ordering and the canonical sample/range queries.

At any world point `p`, define the proposed field as:

```text
D(p) = BaseV5(p) + TrilinearCorrection(p / 16)
```

The correction is a float lattice at the existing base sample spacing. Missing
samples mean exactly zero correction. Base evaluation stays analytic: do not
silently replace the unedited generator with trilinear density interpolation.
Every CPU query, collision sample and GPU input follows this same definition.
This is a continuous scalar field with the existing sign convention, not a
guaranteed Euclidean signed-distance function. Do not use its magnitude as a
safe sphere-tracing step. Arbitrary-position queries need the same interpolation
and boundary rules as extraction.

Start by grouping 32-cubed **owned samples** in sparse pages, aligned with chunk
coordinates but distinct from mesh objects. Half-open integer ownership assigns
each global sample once; negative coordinates use floor division. Chunk-edge
samples and halos are reads of neighboring owners, never competing writable
copies. A page is allocated only on a nonzero change and survives mesh eviction.
Zero-page reclamation requires exact known-zero state, not a tolerance that
silently edits the terrain again. Use finite values and a source-owned legal
density range; its eventual bound is part of the versioned field format.

Payload arithmetic, excluding containers and temporary snapshots:

| Item | Calculated payload | Meaning |
| --- | --- | --- |
| One 32-cubed float correction page | 131072 bytes = 128 KiB | 1000 populated pages cost 125 MiB before overhead. |
| One regular 35-cubed correction upload | 171500 bytes, about 167.5 KiB | A complete derived halo for one edited regular request. |
| One current transition correction layout | `(69² + 2*65² + 2*33²)*4 = 61556` bytes | Covers the existing five-plane layout, about 60.1 KiB. |

Sparse per-sample maps reduce isolated-edit storage but add lookup overhead;
smaller pages reduce partial-page waste but increase metadata and lookup cost.
32-cubed pages are the first candidate, not a permanent size decision. Track
changed samples/page and revisit page size if small edits dominate memory.
Authoritative edited memory grows with modified area; bounded mesh caches do
not bound it. Initially impose an explicit world edit-memory limit and reject
new work before exceeding it. Saving/evicting dirty pages safely needs a real
storage path, not dropping changes on unload.

### Tool behavior and mutation boundary

The tool reads the existing actions on the local player, derives a camera aim
ray, and submits an intent. UI capture, tool enablement, misses, maximum reach,
permissions and both-buttons behavior must be explicit. Proposed both-buttons
policy: no terrain edit. Keep the cursor/brush preview responsive every frame;
it is presentation, not a predicted authoritative hole.

For the first progressive tool, use a compact radial density brush. For global
lattice sample `g`, brush center `c`, radius `r`, and strength `s`:

```text
t = length(16*g - c) / r
w = (1 - t*t)^2 when t < 1; otherwise 0
correction[g] += s*w for digging
correction[g] -= s*w for building
```

This is the proposed brush formula, not an imported engine primitive. It makes
holding a button deepen the effect even at an unchanged center. The actual
surface displacement depends on the existing field gradient; strength is in
density units, not a promise of world units removed. Quantize intent coordinates
and use fixed logical brush ticks so frame rate does not change strength.
Interpolate fast aim motion with a bounded spacing rule; count those samples
against the same rate/work budget. Never issue unbounded catch-up ticks after
a stalled frame. Radius below lattice resolution may have no effect and needs
an explicit minimum or a separately designed fine-detail feature.

Sphere CSG is a reasonable alternative for a crisp excavation stamp:
`min(D, sphere)` builds and `max(D, -sphere)` digs for our sign convention.
Repeated identical CSG stamps are idempotent, so they do not automatically
produce progressive holding behavior. Globally applying those expressions can
change scalar values outside the sphere even when occupancy is unchanged;
naively clipping writes to its AABB can damage continuity. Reject unrestricted
CSG as the first brush contract; a future bounded stamp must define its distance
band/blend and dependency support. Explosions can initially use the same compact
brush with different authorized parameters; exact spherical craters are a later
shape decision, not a reason for a separate mutation system.

All callers submit to one host mutation boundary: player tool, projectile,
explosion, administrative action, or authoritative state import. It validates
intent, reserves bounded work/memory, captures affected pages, computes changes
off-thread from immutable inputs where supported, and commits their new versions
as one ordered transaction. Only actual changes invalidate derivatives. Atomic
multi-page commit prevents readers from seeing half a boundary edit. Broad
explosions may compute in slices, but must not silently become partly accepted
transactions. Oversized operations require an explicit gameplay/work limit.

## 4. Local rebuilds without seams or hidden edits

### Dependency bounds, not just intersecting chunk boxes

The changed lattice samples affect trilinear correction support out to adjacent
base cells. Derive this support first; then intersect it with each consumer's
actual sampling footprint. Do not repeatedly apply a guessed universal padding.

1. Invalidate fine regular regions whose corner or gradient samples depend on
   changed corrections, including shared faces, edges, corners and negative
   coordinates. Existing regular gradients require their one-cell halo.
2. Invalidate affected coarse regular regions at every enabled level. Their
   world-space halo grows with cell spacing; rebuilding only LOD0 leaves stale
   distant terrain. Also invalidate cached/staged regions so revisiting cannot
   resurrect old meshes or known-empty entries.
3. Invalidate transition faces using the actual fine/coarse five-plane sampling
   footprint, including tangential gradient offsets. Do not restrict the test
   to the face's zero-thickness geometry plane.
4. Invalidate collision regions and owners of support patches whose sample
   footprint intersects the change. Keep the existing weld convention unless
   separate evidence justifies changing it.
5. Invalidate field-range and prepared-empty decisions. Until an edit-aware
   bound is available, return “potential surface” for affected bounds. Adding
   the conservative base interval to the correction interval is a safe starting
   bound; trilinear weights are nonnegative and sum to one. Include every
   supporting lattice sample, including zero-valued missing pages.

For unloaded regions, updating data and its revision is sufficient: do not
materialize render/collision meshes just to mark them dirty. Future preparation
queries the current field. Metadata lookup must scale with the affected page
range and active dependencies, not all edits ever made.

### GPU and CPU data flow

Keep GPU extraction and its bounded count readback. For an edited request,
prepare a revisioned correction array at the **exact sample positions already
used by that request**, then add it to procedural GPU density during lattice
generation. Unedited requests use zero corrections. This avoids a GPU-side
walk of every brush stroke and avoids reading geometry/density back for physics.
Coarse samples evaluate the same correction field at coarse positions; no
independently mutable LOD edit stores are introduced.

Correction upload arrays are disposable job inputs, bounded by admitted work,
not permanent duplicate terrain truth. Preserve them until the relevant GPU
commands finish. Thread supported buffer submission through the existing render
rendezvous and scratch ownership. Do not assume changing a bound buffer while
a dispatch is in flight is safe. The transition shader already reaches the
documented 16-storage-buffer limit: fitting a correction input requires a
verified resource layout change, not simply adding another storage binding.

Collision's lattice sampler and all three levels of its conservative bounds
must use the composed field. Keep generator-specific column reuse inside the
base generator, followed by canonical correction lookup. No worker creates
physics objects. Native create/validate/swap remains on the engine thread.

Each job carries world epoch, source-page dependency versions, region lifetime
identity, and a desired-request generation. Check these at completion and
publication. A bare global increment per brush tick would cancel unrelated
regions; a coordinate alone permits an old completion to overwrite a reloaded
region. Discard mismatched results and release all candidate resources.

### Scheduling and publication

Use one pending rebuild per derivative identity and replace its desired target
with the latest required snapshot. Preserve every accepted field mutation;
coalescing meshes does not mean dropping brush operations. Allocate job snapshots
at admission, with bounded completed-result queues and upload/candidate bytes.
Reserve capacity for interaction/near-actor collision, but age distant jobs and
reserve service for transitions and ordinary streaming. Existing regular-first
transition scheduling can starve transitions during continuous digging and must
be revisited for this slice.

Retain previous compatible visuals until affected regular regions and transition
faces are ready as a local publication group. Current face-local replacement
does not by itself ensure equal-revision seams between adjacent edited regions.
Reuse mesher candidates and manager ownership for the new dependency gate;
do not create a second renderer or reset the entire clipbox. Placement movement
and edit publication must validate the same dependencies before either commits.
Measure additional old-plus-candidate arena residency.

Cancellation alone is insufficient: if the hot region changes faster than it
can finish a build, every result may be stale forever. The first strict policy
should bound **mutation admission** for overlapping publication dependencies:
finish an admitted epoch while later intents wait in a capped queue, then admit
the next batch. Unrelated edits continue. Never hold a thread lock while waiting
for GPU/physics work. Report queue delay and rejection to callers; do not hide
it by starting latency clocks only after admission. Set an epoch timeout that
fails explicitly if required geometry cannot complete. This is a conservative
proposal to measure; publishing known-stale new candidates would require a
different, explicitly approved consistency contract.

Small edits should publish their whole affected group promptly. Very large
events may need regional progress, but splitting them safely requires verified
unchanged-boundary cuts; publishing arbitrary adjacent new/old regions can
crack. Start with bounded whole affected groups and measure before adding that
complexity. Subcell features can disappear at coarse visual LOD even when data
is correct. Do not promise distant bullet-sized holes are visible without an
explicit refinement policy and its cost.

## 5. Collision and remote gameplay

Track authoritative data revision, rendered revision and collision revision
separately. Visual completion does not mean safe traversal. Keep old collision
until replacement validates, but mark affected support unready and hold only
actors whose swept support intersects it. Preserve the existing unaffected
collision bodies. On replacement, remove the previous body in the same safe
integration boundary and wake affected resting bodies through verified engine
APIs. Digging under a player, building into a player and overlapping support
patches need real contact validation.

Initially reject build edits that would enclose relevant actors, using a
conservative actor-volume rule; do not rely on triangle shells to push actors
out of new solid terrain. Digging may release support only when the new collision
state is ready. Actor holds and their duration are user-visible latency and
must be measured, not treated as success merely because falling was prevented.

The current single-origin interest cube cannot serve multiple distant players
or active projectiles. Replace its membership responsibility with a bounded
union of gameplay interests when multiplayer simulation is implemented. Reuse
one collision owner and one region record per coordinate. A remote edit with
no simulated actor nearby needs no immediate collider; an active actor requires
collision readiness irrespective of any camera's visibility.

Authoritative aiming/projectiles must not depend solely on stale or absent
visuals. Fresh terrain colliders can accelerate queries. Where collision is
unavailable, use a canonical field query with a defined accuracy contract, or
defer the interaction until required collision is ready. A field-based ray
solver must preserve the production surface convention and handle grazing/thin
features; do not invent a second mesher as a query/test oracle. Never use naive
SDF-magnitude ray steps for this composed non-distance field. The smallest tool
slice may require local collision readiness, but remote explosions with known
authoritative world centers must not be rejected merely for being unloaded.

## 6. Multiplayer and persistence contract

Prefer **host-computed changed sample state plus regional snapshots** as the
initial replication design. Clients send intents, not trusted hit positions or
mesh data. The host validates caller, reach/trajectory, radius, strength, rate,
memory and permissions, assigns order and identity, then sends resulting sample
values. Carry absolute new correction values rather than additive deltas so
duplicate application cannot deepen a hole. CPU/GPU floating-point bitwise
equivalence is not established; do not make it a hidden requirement of replay.

Every transaction needs a world epoch, transaction ID, client request ID,
affected page manifest, expected base page versions and new page versions.
Use explicit ordered application, duplicate suppression and gap detection.
Concurrent overlapping requests follow host order, independent of frame and
dictionary iteration. State replacement does not require repeating tool raycasts
on other machines. Only the host mutates shared truth; clients' copies are
read-only replicas except for applying authoritative transactions.

Use one protocol for updates and recovery: changed page ranges during normal
play; bounded page snapshots when entering interest, joining, or repairing a
version gap. A snapshot identifies a coherent revision boundary; buffer/replay
only later relevant updates, verify completeness, then make the subscribed
region usable. Multi-page visible transactions need a completion manifest so
partial network delivery does not publish half an edit. Limit fragment size,
in-flight bytes, reassembly lifetime and transfer work per update. Do not put a
whole edited world into one RPC. Full page replacement and patch messages are
two encodings of the same state protocol, not competing world implementations.

Interest must cover a client's visual field dependencies as well as nearby
gameplay; a distant visible explosion matters beyond its collision radius.
Do not assume GameObject culling filters terrain RPCs: Facepunch documents that
RPCs still arrive on previously spawned culled objects. Explicitly select
subscribers, with a catch-up snapshot when interest returns.
[Network visibility](https://sbox.game/dev/doc/networking/network-visibility)
and [RPC messages](https://sbox.game/dev/doc/networking/rpc-messages) establish
the engine distinction and available filtering, not our terrain protocol.

Reliable RPC delivery is the documented default, but ordering/idempotency and
snapshot coherence remain application responsibilities. Installed XML snapshot
`26.09.01c` exposes `Rpc.FilterInclude` overloads; exact payload serialization,
fragment limits and host/dedicated behavior still need compilation and live
verification. `PhysicsShape.UpdateMesh` is documented locally as recreating a
mesh, not a cheap refit or async cooker. Existing `GpuTerrainScratch.SetData`
usage proves a current upload path; it does not establish the new layout's cost.

Persist world identity, base seed/settings/generator version, edit-format version
and committed pages. A bounded journal may support recovery, but must not become
the sample-time authority. First-session RAM retention is not disk persistence.
Generator changes require rejection/migration rather than applying old
corrections to a different base without consent. Save/load and late join must
reconstruct identical correction bytes; geometry may require separate tolerance
checks. Host migration, undo, materials and durable storage implementation can
be deferred, but must not be advertised as present.

## 7. Dedicated deformation benchmark

The user explicitly authorized a deformation-only benchmark on 2026-09-07.
It should run only when testing deformation, alongside the unchanged canonical
figure-eight for performance-affecting implementation changes. It does not
replace, retune, or forgive the figure-eight's existing gates.

Use the playable scene and production tool/mutation/network paths. Add the
trigger and bounded measurements to the existing manager/performance lifecycle;
no separate test project, mock field, scene, mesher or test-only mutation hook.
Automated hold input must call the same tool behavior used by the player;
explosion scenarios must exercise the real authorized explosion caller when it
exists. Do not claim a pure data write proves tool responsiveness.

The following is a **suite design**, not an executable scenario definition.
Before the first run, copy exact scenarios into the ledger template and freeze
all parameters. The ledger remains the sole owner of executable values/results.

| Scenario family | Fixed workload to define | Required evidence |
| --- | --- | --- |
| Single dig/build | Repeat isolated changes at interior, face, edge and corner sites, including negative coordinates and previously proven solid/air regions | Correct sign, changed samples, bounded dirty set, visible surface and native contact at expected revision. |
| Sustained hold | Separate stationary-aim and moving-aim runs, at several logical ticks per second; include a 10 Hz candidate workload and an overload workload | Progressive effect; input count versus accepted count; p50/p95/p99 latency; no ever-growing backlog or cancellation starvation. |
| Large deformation | A fixed radius spanning several chunks; repeated overlapping dig/build at a fixed cadence | Whole-event completion, peak staging bytes, collision tails, retained old geometry, cross-chunk/LOD correctness. |
| Streaming plus deformation | Fixed player route with fixed timed tool actions; cold/unloaded destination plus warm revisit | Edits survive unload/reload; no stale prepared-empty or cached geometry; no global remesh. |
| Multiplayer | Host plus clients at separate sites and overlapping sites; late join/reconnect during edits | Host order, exact correction convergence, coherent snapshots, per-client bytes and request-to-visible latency. |
| Remote events | Authoritative impact outside every client's residency, then approach; repeat near a remote simulated actor | World data changes without forced render allocation; later geometry includes the edit; actor collision is serviced independently. |
| Failure and lifetime | Duplicate/reordered/gapped transfer, scene teardown with jobs in flight, budget exhaustion, rejected edits | No double application, stale publication, leaked buffers/bodies, silent partial commit or lost acknowledged edit. |

Freeze scene/source/hardware/engine identity; seed and generator; camera/player
positions; all brush centers/radii/strengths/order/counts; aim trajectory; reach;
cadence; workload duration; warmup/settlement; clients and actor positions;
latency/jitter/loss configuration and how applied; visual/gameplay settings;
memory/queue caps; capture limits; and exact completion definitions. Preflight
surface sites before freezing them, and preserve failed runs afterward. Fresh
world identity/reset must prevent prior edits contaminating repetitions.

Record these times per request/transaction, using local monotonic clocks:

```text
input -> host admission -> field commit -> derivative queued
      -> worker/dispatch start -> mesh ready -> render submitted
      -> collision installed -> affected actor released
```

Input-to-first-visible and input-to-whole-edit-visible are different metrics.
Count a preview separately. Render submission is a latency proxy, not proof of
display scanout; correlate sampled rendered frames/video with transaction IDs
for visible claims. A transaction that changes no visible surface is a no-op
or nonvisible-data event, not a zero-millisecond visible success. Include pending
requests as incomplete/censored observations at the end; never compute favorable
percentiles from completed work alone. Cross-machine one-way subtraction needs
clock synchronization; client-local input-to-confirmed-visible does not.

Measure frame-time p50/p95/p99/max, CPU/GPU work, allocations/GC, page/snapshot/
GPU-candidate memory, upload and network bytes, invalidated/built/published
regions per level, queue age, stale work, transition delay, collision creation
and actor-hold time. Report first-touch page allocation separately from repeated
edits. A no-edit control uses the same camera/world/settings; compare deformation
versions against a frozen deformation baseline, not unlike scene captures.

Proposed product targets to ratify and freeze before implementation runs:

- Small local edits: input-to-visible p95 at most 100 ms and p99 at most 200 ms;
  collision-ready p95 at most 150 ms and p99 at most 250 ms. These are aspirations,
  not measured capacities. Remote tests need separately declared RTT-aware gates.
- Frame pacing: satisfy the existing figure-eight acceptance independently;
  for deformation, declare both an absolute hardware frame budget and a maximum
  regression versus its comparable control before running.
- Large edits: give first-visible and whole-event latency independent numeric
  gates after the radius and hardware are fixed. No acceptance run may start
  with these, queue-age limits, allocation limits or memory caps unspecified.
- Correctness: zero stale publications, missing shared-boundary dependencies,
  duplicated mutations, divergent authoritative page bytes or native failures;
  no lost edits on residency changes; bounded queues with explicit rejection.

Acceptance is incomplete until the frozen large-event, continuous-input,
multiplayer and distant-actor cases pass. Single-user responsiveness does not
demonstrate 64-player capacity. Scale actual connected players through fixed
scenario versions; do not extrapolate linearly from a single tool.

## 8. Smallest implementation sequence and decision gates

1. Establish the composed field, sparse pages and one bulk mutation boundary.
   Replace direct consumer assumptions with canonical sample/range inputs in
   the same change; no CPU-only edited terrain path left alongside old visuals.
2. Extend existing GPU/collision jobs with local revisions and correction
   snapshots, local publication dependencies, admission bounds and diagnostics.
   Verify transitions and support patches before presenting the tool as playable.
3. Add the real left/right tool and benchmark trigger, with explicit aiming and
   collision-readiness rules. Validate continuous input, not only isolated clicks.
4. Add host requests, state replication, catch-up and multiple gameplay interests
   through the same store and queues. Validate remote unloaded edits and actors.
   This can be a separately delivered step, but multiplayer remains unimplemented
   until it passes its real entry-point tests.
5. Optimize only measured stages. Consider smaller edit pages for sparse waste,
   reusable correction uploads for transfer pressure, native collider granularity
   for publication stalls, or bounded concurrency for worker bottlenecks. Do not
   add all of these preemptively.

Rejected for the initial slice: GPU-only authoritative edits (CPU physics,
server queries and replication still need truth); unbounded analytic stamp
replay (work grows with history); dense allocation for the whole streamed world;
per-voxel network messages; global content reset on every tick; shrink-all-chunks
or replace-the-mesher optimization without evidence; speculative client terrain
prediction before reconciliation is designed. Each alternative could be
reconsidered with a concrete measurement, not a general claim that it is faster.

## 9. Evidence confidence and remaining gaps

| Question | Confidence / evidence | Remaining work |
| --- | --- | --- |
| No current edit state; CPU/GPU procedural consumers | High: inspected source and architecture agree | Keep this inventory current during implementation. |
| Local writes must invalidate gradients, transitions and collision support | High: actual sampling footprints and Transvoxel primary description | In-world boundary and stale-job verification. |
| Accumulated state avoids history-dependent sample cost | High for mechanism; external engines support direction | Measure correction lookup, first-write allocation and upload cost here. |
| Float correction pages are the best first representation | Medium: project-specific inference | Compare measured memory/latency; evaluate shape quality and precision. |
| Current s&box supports this exact new GPU/network layout | Unverified | Compile resource/payload changes; clean-start shader test; live clients. |
| 10 Hz digging or large explosions meet targets | Unverified | Freeze workloads and run the real deformation path. |
| Current collision is safe and accepted for dynamic edits | Not established; ledger explicitly retains failures/limitations | Dynamic contact, replacement, tails and multiplayer readiness tests. |

Research used repository routes, current production sources, the collision
ledger, installed XML evidence, and first-party engine/plugin/algorithm pages.
Discovery covered mutable terrain storage, incremental sculpting, async and
collision costs, Transvoxel dependencies and s&box networking. Follow-up checked
the stamp/sculpt distinction, network visibility versus RPC filtering, actual
gradient footprints, and current collision acceptance. These checks bounded the
consequential contradictions. Further broad searches would not establish our
missing runtime numbers, so research stopped at the implementation/measurement
gates above. No deformation runtime validation was performed.

Source provenance: all web references were accessed 2026-09-07. Voxel Tools
pages are living `latest` documentation with no reliable page publication date;
its performance page includes a 2023-06-17 historical edit-cost description.
Voxel Plugin pages are explicitly separated as 2.0p8 experimental and legacy
1.2; no publication dates were inferred. Lengyel's linked dissertation is 2010.
Facepunch RPC documentation reports update 2024-11-29; network visibility reports
creation 2025-11-14. Installed API evidence identifies build `26.09.01c`, build
ID `33901499107`; XML comments are incomplete accessibility evidence. The source
tables above connect each external claim to its primary page and its transfer
limits. Recommendations and proposed formulas/budgets are this project's
research synthesis, not claims made by those sources.
