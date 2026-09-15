# Predictive terrain loading prototype

2026-09-15 integration: the user adopted the exact tested water-plus-prediction
source snapshot for main. See [the adoption record](../ValidationEvidence/IdleWork/Experiment.md)
for measurements and explicit allocation/drain exceptions. Historical prototype
results below remain unchanged; this is not a blanket pass of older scenarios.

Status: the prediction-v2 experiment below is the recorded before baseline for
the subsequent [complete seam cache prototype](CompleteSeamCache.md), which now
owns the current expanded coarse-face membership and service budgets. The skirt
implementation is reverted to the preserved exact-seam v26 source. Its failed
results remain in the ledger; no performance acceptance is implied. V1's moving
test exposed an invalid LOD0 classification call and was aborted; v2 uses the
canonical LOD0 classifier. The failed run remains preserved.

VoxelManager estimates streaming-target velocity from observed positions and
prepares a bounded corridor ahead. It never uses knowledge of the figure-eight
route or moves the actual streaming target. Three forecasts span 0.25 to0.75
seconds, capped at eight base chunks total. Each forecasts a 27-region neighborhood
at LOD0 and LOD1; the far forecast also prepares the exact LOD0/1 layout boundary.
Current render coverage, collision interests and publication checks are unchanged.

One manager-owned set retains these cache entries, at most162regular regions
and96transition faces for the canonical4/8 layout. Geometry uses existing
descriptors, classification, GPU queues, exact Transvoxel and water generation.
No alternative mesher or terrain truth is introduced. Requests are sliced within
a0.5ms soft budget and12inspections per frame. Existing immediate publication and
edit priorities remain higher than speculative work. Water speculation is last.

The forecast refreshes at most five times per second. Stationary targets clear
interest; large frame gaps and implausible jumps reset the velocity estimate.
Retargeting releases only cache interest and preserves actual/staged coverage.
Current field descriptors are checked on each service cycle, so old edited data
cannot satisfy a new request. Session reset clears prediction state. No prediction
is authority or a network input; scheduling is local derived work.

Measure unchanged LOCAL-COVERAGE-001/v2 fast and standard routes against exact
v26. Near LOD0 visible<=0.1s is the experimental near-instant target, with no
material pacing/memory regression, unchanged geometry/topology gates and10s
drain. Record hits when forecast LOD0 regions enter actual warm interest, queued
work, forecast size and CPU preparation time. A cache hit alone does not prove
visible terrain: exact seams, water and publication can still be pending.

Alternatives: moving the whole coverage ahead risks losing terrain behind the
player; enlarging every cache costs work in all directions; forecasting distant
levels immediately expands speculative work before the near-detail benefit is
measured. This prototype limits speculation to the two finest levels and their exact boundary.

Measured v2 status
------------------

[Before/after evidence](../ValidationEvidence/LodPrediction/Comparison.md) shows
normal-speed first visibility0.0217621s versus1.3017778s for exact v26, but fast
visibility1.3662896s versus0.5409848s. Frame pacing and drain gates prevent
acceptance. Both88-mesh safety audits and exact seam identity checks passed;
forecast cache interest cleared at rest. A fast arrival snapshot identifies22
missing local refinement seams after regular terrain/water were ready: forecasting
the final LOD0/1 layout is not the same as preparing that local patch's dependencies.
The source remains an unaccepted prototype. Fixed scenarios, failures and
environment differences remain in the validation ledger; no commit/push.

2026-09-14 correctness correction: prediction uses metadata-only descriptors for
cache identity checks, then captures the authoritative regional field before
scheduling nonuniform regular geometry, matching the transition path. Without
that capture, a loaded edited world can enqueue EditRevision>0 with Field=null.
SHADOW-FPS-003/v1 exposed the null dereference during the standard route; its
unchanged current-world rerun qualifies this prerequisite independently of shadows.
