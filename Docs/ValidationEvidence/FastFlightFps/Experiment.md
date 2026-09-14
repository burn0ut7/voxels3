# FPS-preserving fast-flight follow-up

2026-09-14. Follow-up to FastFlightStreamingExperiment.md. User prioritizes FPS.

The first bounded candidate changes only local seam request preparation. A seam
that is not resident may already be scheduled. The current local readiness loop
captures a full descriptor and calls ScheduleTransition again on each check;
the mesher refreshes its field before deduplicating that request. Reuse the exact
identity-only descriptor for both residency and requested-state checks. Preserve
foreground promotion on every missing-resident check; capture and schedule only
when the current descriptor has not been requested. Existing field-version
checks, queues, geometry, topology, publication and budgets remain the owners.

This avoids redundant caller-side field work without changing the amount of
terrain generated. No new cache or invalidation responsibility is introduced.
An edited or removed descriptor fails ContainsTransition and is scheduled through
the existing canonical path. The game thread owns these checks and admission.

The saved control profiler is a rolling200-frame snapshot, not a complete route
profile: OnUpdate1.296ms/frame, ProcessPendingMeshes0.684ms/frame. Route allocation
was7.98GB. These do not isolate duplicate seam preparation as the dominant cost;
this is a low-scope hypothesis to measure, not a promised optimization.

Use LOCAL-COVERAGE-001/v4 unchanged; raw runs and source identity live beside
this note. Require improved loading and no material unexplained FPS/p99/memory/
allocation regression. Failed candidates are restored and retained only as
archived patches. Results will be appended after the real playable route.

## First result and second hypothesis

The duplicate-request trial averaged521.97FPS versus504.72control with p99
8.00ms versus8.10ms, but nearby detail took1.813s versus1.492s and drain
15.993s versus12.813s. Both moving/final audits passed. Rejected and restored:
it did not improve the requested loading behavior. This does not establish a
causal slowdown from the tiny change; each candidate currently has one run.

The second candidate keeps all existing predicted regions and seams, but
reduces speculative seam inspections from24 to8 in a full service slice,
while retaining24regular inspections. A four-step cursor services three regular
entries then one seam, preserving progress across the0.5ms time limit. Only the
existing prediction owner changes; actual foreground seams retain their existing
service and all topology/publication/cache identity contracts are unchanged.
Empty-list fallback remains supported. Existing sorting and retention stay intact.
The smaller background admission rate may reduce interference or may leave
useful predicted seams late. The same fixed workload decides; neither result
is presumed. No new queues, buffers, threads or settings are introduced.

## Results: both changes rejected and restored

| Fast-flight run | Near detail after stopping | Total catch-up | Average FPS | p99 frame |
| --- | ---: | ---: | ---: | ---: |
| Fresh control | 1.492s | 12.813s | 504.72 | 8.10ms |
| Skip duplicate seam preparation | 1.813s | 15.993s | 521.97 | 8.00ms |
| Limit speculative seam admission | 1.957s | 16.024s | 527.61 | 8.06ms |

Both trials maintained FPS and p99 within the fixed limits, but nearby detail
and total catch-up were slower. Neither qualifies as a loading improvement.
All three moving30-second coverage audits and final88-mesh audits passed.
No measured exceptions; final queues settled and collision/water ready.
The final screenshot retained the original edited tunnel and terrain surface.
These checks do not establish continuous fine-detail coverage at every position.

The second candidate used10.7%more average process memory than the control;
process-age effects are not isolated. Full memory/allocation/GC and preparation
measurements are in Comparison.json and raw outputs. A single run per variant
cannot establish that either small change caused the observed differences.
Both miss the10-second drain criterion. No standard/repeat acceptance runs
were taken for these rejected candidates. The morning400FPS control differs
from this reopened editor's505FPS control; these comparisons use the fresh one.

All runtime source hashes match the starting manifest after restoration.
Patches remain archival only. The results do not support reducing speculative
work as a reliable route to faster visible detail. The next useful measurement
is the critical path from current-player request through terrain/water/seam
readiness to publication, including time waiting behind old layout work.
Changing that lifecycle needs targeted evidence and coherent seam ownership;
these experiments neither implement nor validate such a redesign.
