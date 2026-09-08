# Focused Collision Optimization Experiments

2026-09-08. Goal: find a measured collision performance or memory improvement
without material frame-pacing, geometry, contact, readiness or streaming regressions.
Current starting source: a6b20c8; current saved world revision971/160 pages.
This is a new experiment series, not acceptance of historical collision gates.

## Working agreement

The user's latest instruction supersedes the initial hold-without-revert request:
run short, targeted experiments; revert a failed candidate when appropriate;
preserve results and the user's original state. One changed mechanism at a time.
Use existing production diagnostics, stage timings and figure-eight entry point.
Do not add test-only paths, change terrain quality, expand worker count, or chase
small noisy differences with repeated tuning. Commit runtime changes only after
acceptance. If no high-value hypothesis remains, hold the last accepted state.

## Plan and stop rules

1. Preserve source identity, authored scene and saved terrain hashes. Record a
   fixed current-world scenario in the ledger before the first run.
2. Capture one normal-start baseline including startup collision/contact,
   canonical figure-eight, moving stage metrics, memory, allocations and tails.
3. Prototype one exact arithmetic shortcut in canonical cave bounds: evaluate
   cheese/thickness first; omit noodle bounds only when their maximum possible
   contribution is already dominated. Preserve unresolved arithmetic and all
   existing conservative rounding. No new field, cache, geometry or resolution.
4. Build/hotload, run the same scenario once. Reject immediately for correctness
   failure or no useful measured benefit. Record the patch and revert only this
   task's change. Do not tune the fixed workload to improve results.
5. Repeat only a promising candidate and its control as necessary to distinguish
   improvement from noise. A one-run difference is a screening result.
6. If rejected, inspect remaining measured costs briefly. Higher-value alternatives
   are edit rebuild amplification and bounded field reuse; smaller collision
   sections need evidence of native rebuild dominance before implementation.
   Do not repeat previously rejected block-size/gradient/cache experiments
   without new evidence. Cap this initial screen at two distinct prototypes.

## Evidence and ownership

Canonical field formulas remain owned by ProceduralTerrainSdf. The shortcut
would affect CPU field-bound consumers, so visual geometry/streaming equivalence
is also required. Collision retains two workers, reusable geometry, native
engine-thread publication and prior support until validated replacement.

The [Facepunch comparison](https://github.com/Facepunch/sbox-public/blob/a0b002cdbff9abfade9afd4cb205230a55120121/engine/Sandbox.Engine/Scene/Components/Terrain/Terrain.Collider.cs)
motivates reducing preparation and changed-region work. Heightfield rectangular
updates cannot directly replace our cave-capable mesh collision. No speedup is
inferred from C++ ownership or UpdateMesh naming.

The [collision profile](CollisionProfile20260907.md) supports prioritizing CPU
sampling/bounds; its historical timings are not this series' baseline.
The [ledger](../ValidationResults.md) owns immutable scenarios and run outcomes.
Record decisions here after each screen; no implementation is accepted yet.

## First screen: reject and revert

Bound-dominance prototype compiled and matched startup geometry/contact evidence.
Full-route construction improved2.08%, FPS3.65%, allocations/frame1.85%; process
peak increased4.37%. No selected primary metric crossed the predeclared5% gate.
Work populations and post-route physics positions differ, so these small changes
are not causal speedup claims. Reverted only ProceduralTerrainSdf.cs and retained
the patch, baseline, candidate and decision in the ledger. No repeat is warranted.

## Bounded follow-up and hold

Current code already has the exact-lattice SampleCorrection fast path and local
epoch/revision-aware collision invalidation. Do not spend another prototype on
adding those again. Bulk correction copying might reduce lookups in edited
regions, but would add copying/scratch work where conservative rejection avoids
most samples; the route does not yet attribute enough time to those lookups.
The older block-size, gradient-table and sparse-cache experiments already have
negative evidence. Body setup is a small fraction of measured native publication.

No second cheap code change has sufficient measured leverage for this screen.
Hold the original runtime implementation; performance-improvement goal remains
unmet. The strongest next investigation is a focused existing deformation capture
to quantify changed samples versus rebuilt regions/native triangles, followed
by a go/no-go decision on smaller collision sections. That is a new diagnostic
scope, not justification to start a sectioned-collider architecture speculatively.
No further repetitions of the rejected bounds candidate are warranted.

## Second screen: actual changed bounds

The continued goal's source check found128unit revision metadata can invalidate
neighbors outside an edit's true interpolation support. New fixed diagnostic
COLLISION-EDIT-FOCUSED-001/v1 confirmed20edits caused80collision rebuilds, about
413ms sampling/extraction and74ms native mesh creation, despite each edit's
changed support fitting one chunk. This evidence justifies a second narrow
prototype without introducing sectioned collision or extra caches.

Extend the existing completed-collision reuse branch: within one field epoch,
if a ready region's expanded sampling bounds do not intersect AffectedBounds,
retain its collider and rebase its derived revision. AffectedBounds is produced
by the canonical mutation path from changed lattice samples plus interpolation
support; collision sampling bounds already include its one-cell support patch.
Epoch replacement retains its existing separate changed-region proof. Never
reuse pending/in-flight geometry on this shortcut. Native replacement, readiness,
field authority, workers and visual invalidation remain unchanged. Measure
against private copies of the original user save and preserve it unmodified.

The screen confirmed four-to-one dependency reduction, but normal replacement
of the player's own collision chunk moved the resting player beyond the frozen
2unit limit. B1's movement was detected after all20edits; C1 added per-edit
inspection and stopped after16at2.09units. This is not an accepted matched timing
comparison. Keep the mechanism as a promising preserved patch, revert runtime,
and request approval for the following workload correction before further runs.

### Proposed COLLISION-EDIT-FOCUSED-001/v2 (awaiting approval)

Change only edit center from(384,384,-256) to(896,896,-256), and translate the
9contact probes by(+512,+512,0). The latter point has the same position within
its chunk/revision blocks, but is outside the player's supporting chunk. Keep
original971/160fixture,20requests,+64then-64,radius64,1secondminimum cadence,
10secondsettlement timeout,2unitmovement limit,private saves,all other parameters
and gates. Both B2 and C2 use per-edit player inspection. Preserve v1 failures;
v2 begins with a new baseline and cannot be compared as the same workload.

Reason: v1 coupled the intended invalidation-cost measurement to the existing
contact displacement caused by replacing the player's own support. This is a
scenario-validity issue, not permission to relax contact correctness. The
displacement remains a separate observed collision defect; avoiding it in this
cost diagnostic does not fix or accept it. After v2 screening, ordinary contact
and unchanged figure-eight regression qualification remain required. No runtime
candidate is accepted and no v2 run is authorized by this proposal itself.
