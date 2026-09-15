# Local terrain publication redesign

2026-09-15 integration: the user adopted the exact tested water-plus-prediction
source snapshot for main. See [the adoption record](../ValidationEvidence/IdleWork/Experiment.md)
for measurements and explicit allocation/drain exceptions. Historical prototype
results below remain unchanged; this is not a blanket pass of older scenarios.

Status: the surface skirt experiment was reverted. The [complete seam cache prototype](CompleteSeamCache.md)
extends the [prediction prototype](PredictiveTerrainLoading.md) on exact-seam v26.
Moving coverage and88-mesh audits passed on both v26 routes. Clean fast
and standard performance qualification is pending because another game was
using the GPU during recent runs. No performance acceptance or commit is implied. Fixed scenarios, every failed experiment and
raw measurements live in [the validation ledger](../ValidationResults.md).

## Ownership and deterministic inputs

The canonical world is the procedural recipe plus authoritative correction
field. Descriptors identify coordinates, LOD, generator version and regional
revision. Regular and transition geometry never require another chunk's loaded
mesh as a generation input. Exact Transvoxel covers all required boundaries.
Prediction only adds cache interest and does not change active coverage.

VoxelManager owns desired layout/cache membership and publication operations.
GpuVoxelMesher's regular and transition activation sets describe actual coverage.
Water, terrain edits, retention and arrival diagnostics consume that coverage;
a prepared cached mesh is not necessarily active. Transitions belong to their
coarse regular owner. All activation changes occur on the game thread before
render submission; GPU results still reject stale descriptor/generation identity.

## Preparation and early publication

Main layout and exterior preparation alternate slices within the existing2ms
soft budget, yielding every32 inspected coordinates. A changed LOD0 placement
must not suppress exterior discovery. Exterior interest has one owner:
_partialOuterChunks contains maximum-LOD fallback coordinates in the latest target
box: exterior coordinates and uncovered coordinates inside the historical committed
box. Actual coverage, rather than box metadata alone, decides whether preparation
can be skipped. Unadopted fallback remains requested until final layout publication. Configuration changes disable incompatible
exterior interest; final commits adopt intersecting exterior regions and refresh
preparation so clearing local queues cannot strand candidates.

Candidate service order is immediate27 LOD0, uncovered exterior, then other
uncovered desired regions. Distance and stable level/coordinate ties order work
within each class. A queued candidate that acquires exterior interest receives
its new priority; consumed old entries are skipped within the inspection budget.
New requests wake an otherwise idle local plan. Existing exterior coverage is
not treated as missing coverage just because the final layout wants finer detail.

Up to eight local additions can wait concurrently. Each checks actual ancestors,
descendants,26-neighbor2:1 balance, regular content, water and required seam
readiness. Additions re-describe their topology after other publications or
retirements. They activate only when their current dependencies are ready.

Refinement is reserved for immediate27 LOD0 and balance blockers of uncovered
additions. One virtual patch owns topology exclusively while it plans and
prepares. It prepares its desired leaves and changed seams, then swaps them
atomically. Intermediate virtual parents are never published. Old coverage
remains active until the replacement is ready. Existing covered background
regions retain their meshes until final-layout publication; they do not undergo
a second sequence of temporary local refinements. Patch planning is sliced
under a0.75ms soft budget and capped at4096 leaves. Descriptor snapshot changes
restart preparation; retargets cancel pending operations.

## Retirement and final completion

Out-of-range regular regions and inactive seams retire in bounded slices of16
entries each. Immediate player-detail work does not wait for those entire queues to drain.
Exterior additions also bypass cleanup; their existing topology checks still apply.
Other background additions wait, avoiding balancing work against retiring coverage.
A pending refinement still pauses other topology mutations until publication.
A queued seam can have become active since it was queued; retirement rechecks
activation before removing it. Retiring a regular owner deactivates its faces;
removing the last finer descendant also deactivates the adjacent coarse seam.
Current exterior interest is rechecked before retiring its owner.

Final requested meshes, seams and water prepare concurrently. After regular
work drains, final readiness checks every active final dependency against current
identity, including unchanged layout metadata whose actual coverage diverged.
The100ms readiness cadence and stop-at-first-missing predicate limit repeated
checks. Final publication activates the complete requested layout, deactivates
superseded coverage and reclaims inactive dependencies outside final caches.
Local near/exterior publication runs before this final readiness barrier.

LocalWorkComplete means no more useful early local work remains; it does not
mean the final layout has converged. The final publication and some complete
scans remain synchronous and must be measured, not assumed to fit a frame budget.

## Water and GPU service

The existing single water worker prioritizes admitted local publication
dependencies, then exterior water, then background layout water; distance, LOD and coordinates break
ties. Terrain and water retain their shared publication readiness contract.
Water cache retention follows the terrain owner's committed/staged/exterior
interest and current field identity. No independent water preview is introduced.

Exterior interest now enters the existing nearest-first outer GPU service queue
at discovery, independently of render activation or the eight publication slots.
Interest removal requeues pending work into its remaining service class; disposal
clears interest. This restores prompt preparation while publication still checks
terrain, water, overlap, balance and seams. The existing GPU lane count, batches
and one-service-per-render-tick rule remain.
Foreground local seams have priority over final background seams and behind edit
dependencies. Every seam retains its ordinary queue entry so clearing priority
cannot strand work. Idle near capacity can serve background regular work.
The exact-footprint river atlas cache remains64 entries/64MiB: the256-entry
experiment was reverted. No shader, mesher, LOD distance or field algorithm was
changed for the exterior restoration.

## Evidence and limits

Use voxel_chunk_readiness and the automatic LOD arrival report to distinguish
terrain preparation from presentation. voxel_coverage_info reports pending
operations and their missing regular/water/seam dependencies, retirement queues,
exterior requested/active counts and promotions. It also separates missing regular
terrain, missing content after terrain is ready, and ready-but-inactive exterior
regions, and reports whether cleanup blocks the next candidate. Detailed mode audits overlaps,
balance and missing/extra active seams, retaining example keys. These scans are
on demand; declared in-flight observations are instrumentation overhead.
Exterior counts include proven-empty regions and do not establish visual quality.

LOCAL-COVERAGE-001/v2 retains the fixed fast/standard figure-eight workloads,
near27 arrival <=5s, background drain <=10s and existing correctness, pacing,
allocation and memory comparison requirements. In-flight and final topology
checks complement actual geometry audits; they do not replace visual inspection
or edited-world qualification. See the ledger for measured failures and limits.

## Rejected alternatives

A global layout barrier as the only publication path delayed already-ready near
meshes. Serial background local refinement duplicated final preparation and
blocked uncovered exterior admission. Both are replaced by the early-local plus
final-layout ownership above. Compact rings, larger seam batches, approximate
skirts and a second mesher are not adopted. The two-service GPU experiment
crashed during startup and was reverted. Expanding the atlas entry cap modestly
reduced packing but did not solve drain/allocation; it was also reverted.

The v17-v19 timing comparisons are qualified by measured GPU contention during
v19 and the other game's process lifetime overlapping all three. This preserves
the results without claiming source changes alone caused their timing differences.
See the ledger and gpu-contention-v19.json; no other application was controlled.

V21 fast run: near27LOD0 presented2.448s, background drain27.956s (fails10s).
All eight recorded refinement focus points retained an active LOD; v20 had five
missing of nine. Moving/return and final topology audits passed. These are sampled
checks under GPU contention, not continuous flight or performance acceptance.

## Immediate admission prototype v22 (rejected and reverted)

V22 did not improve measured arrival/drain; its deferral path was not exercised.
Coverage.cs was restored exactly to v21. The following describes the rejected
experiment, not current behavior.

The candidate refreshes the existing local queue when the player crosses a LOD0
chunk boundary. It examines only the immediate27 desired keys, promotes queued
unpublished keys, and wakes a completed local plan when necessary. It does not
expand the staged layout or move the player/collision interest. Older queue
entries remain bounded by movement and are skipped after consumption.

If immediate detail is at the head, waiting distant additions relinquish their
admission slots and requeue their requests. These operations have not mutated
coverage: cached/in-flight terrain and water remain reusable. The existing sole
refinement patch still owns topology until its checked atomic swap. Near waiting
additions are retained. Priority changes do not bypass revision, overlap,
balance, terrain, water or transition readiness. Existing GPU service budgets
and queue ownership remain unchanged. ImmediatePromotions and DeferredAdditions
in the coverage diagnostic expose whether the candidate was exercised.

Alternative: increase the eight-operation limit. Rejected for this experiment
because it increases waiting work and does not guarantee near refinement can
acquire exclusive publication ownership. Rebuilding the full queue every frame
would add scans/allocations; the bounded movement-triggered promotion avoids it.
This slice does not address stale staged targets or the conditional one-second
recovery timer; those need separate measurement rather than conflating changes.
## Stationary target recovery prototype v23 (qualification pending)

The recovery decision observes actual target movement, refreshing its timestamp
after one base-cell displacement, and allows stale destination recovery after
100ms quiet. The prior check waited1s since a snapped anchor changed. Shortening
that anchor-only timer would cause churn during rapid movement; tracking actual
motion retains the coalescing behavior while promptly handling a stopped player.
The same canonical cancellation and destination builder perform the recovery;
no second layout, collision target or speculative terrain field is introduced.
Very slow movement below one cell per100ms may satisfy quiet; recovery still
requires stale staged anchors/coverage and uses the existing checked path.

A separate near-only observation runs at most every50ms until first readiness
in the existing motion episode. It saves FirstPreparedObservedSeconds,
FirstPresentedObservedSeconds and MaximumNearObservationGapSeconds alongside
unchanged historical one-second samples. These are first observations of the
existing terrain/water/draw-eligibility predicates, not proof of pixel scanout.
Actual frame gaps can exceed50ms; the report records the gap instead of asserting
a fixed error bound. No global topology scan or trace is added to this cadence.
## Foreground seam service v24 (qualification pending)

A local publication's missing seams are serviced once its required regular
terrain is prepared, even if unrelated exterior terrain remains queued or in
flight. The existing foreground queue is the sole owner of this interest. A
seam-only dependency set does not require any missing regular terrain. Edit
priority still precedes local work. The existing250ms outer-service deadline
runs before this shortcut, so exterior work retains service. Every selected
service still returns before another GPU service can run in that render tick.
No scratch lane, batch size, shader, allocator, mesh identity or topology rule
changes. V23's first arrival sample had no missing regular/water dependencies
but78missing seams; terrain was observed ready at0.247s and draw eligibility
at3.143s. That evidence motivated removing the unrelated exterior barrier.
## Transition empty-case work elimination v25 (qualification pending)

GpuTransitionDescriptor owns FaceBounds, the closed face rectangle containing all
transition case samples. SamplingBounds derives the existing coarse-cell halo
from that face and remains unchanged for normals, revisions, edits and field
capture. Unedited transition classification bounds FaceBounds; only a strictly
same-sign conservative density interval skips GPU extraction. Shader case bits
use nine face-plane samples per cell; normal probes affect shading of existing
vertices, never create triangles for a uniform case. Nonuniform/edited cases
retain the same GPU extractor. No mesher, density formula or seam approximation
is added. Final transition topology751A39EE7A9417E7/positionE6D948B4B9E69595 and
33696indices match across v21/v23/v24; v25 must preserve them in the fixed world.

The outer deadline now also allows a partly executed outer count to continue
when no count submission/emission can run. That dispatch returns immediately.
Existing normal count progression and submission/emission counter meanings are
preserved. This closes the starvation gap found while exercising v24 urgent
seams. Fixed tests and invalid/deviating observations remain in the ledger.

## Urgent transition count progression v26 (qualification pending)

A selected transition batch needed by local publication can finish its count
phases in one service. The bounded loop advances at most three existing phases
for the same two-face batch. Background work advances one phase as before.
GPU barriers, asynchronous count readback, stale-result checks and later geometry
emission remain unchanged. No additional batch or scratch lane is admitted.
This trades fewer inter-frame waits for more GPU work in the selected frame;
the fixed figure-eight measures whether that trade is acceptable. The earlier
three-phase pacing evidence used eight-face batches, whereas the current engine
requires two-face batches. Increasing batch size remains rejected.

Measured v26 arrival was0.541s on the fast route and1.302s on the standard
route. Fast v23 measured3.143s using the same near observation predicates.
These are contended single-run observations, not guaranteed latency bounds.
Both v26 runs preserved the baseline's final transition geometry identity and
passed88mesh audits. Fast placement preparation reached42.262ms and background
settlement remains slow; the candidate is unaccepted and uncommitted. Removing
seam counting waits improves the critical path but does not ensure nearby
replacement dependencies are prepared before the player needs them.
