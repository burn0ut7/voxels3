# Terrain streaming optimization results

2026-09-09. Candidate C retained as a measured prototype. Startup and moving results improve;
full stationary/collision acceptance remains incomplete.

## Experiments

| Candidate | Outcome |
| --- | --- |
| A: outer forced-service gap250ms to16ms | Rejected. Loads24.531/24.563s, only about5% better, with worse frame windows. |
| B: outer submission on transition-service ticks | Rejected. Editor exited during first measurement. Cause not isolated; no successful timing. |
| C: complete authoritative bound for coarse chunks | Startup passed19.797/19.797/19.672s with identical geometry. Moving FPS/tails and distant readiness improved; stationary qualification incomplete. |

A and B are removed. C reuses the existing field classifier; it does not simplify
terrain, restore generated-density caching, change batch sizes, or alter shaders.
The classifier includes regional height, caves and authoritative corrections.
Definite air/solid regions use the existing empty-region publication path.
Uncertain regions still use the normal GPU mesher. The tradeoff is CPU work per
classification in exchange for fewer GPU mesh jobs.

## Startup evidence

All runs use the unchanged saved world and fixed startup scenario described in
[the plan](../Plans/TerrainStreamingOptimization.md) and validation ledger.
After the B exit, the fresh editor baseline was25.140s. Candidate C median19.797s
is21.3% faster. Historical baseline25.797/25.938s is preserved separately because
of the editor restart. C final rolling frame windows584.8/594.6/596.1FPS and p99
5.22/5.06/5.07ms compare with baseline544.2FPS and p99 5.46ms. These are rolling
windows, not whole-run percentiles. All checks retained topology473FFDE4AD1E3FE1,
positions92FAEEE7BEE60656, saved revision64, collisionready4913/failures0 and
complete queue drain. Time starts after Play returns and includes all surrounding
terrain; it is not first-visible time or a physically cold disk test.

## Moving baseline

The canonical figure-eight was run unchanged at the user's explicit request:
speed2500,distance50000,one loop,Z0. Baseline run4c8f8cf73dbb41a68189663f4b25e41c
measured499.15FPS,p95 4.107ms,p99 5.659ms,max107.785ms. Outer queue averaged3713
regions with p95 5884; schedule-to-renderable median19.498s,p95 25.586s.
The baseline had zero runtime exceptions and zero unsafe placement commits.
It also allocated about1.97GB managed memory over the moving run, so the existing
zero-recurring-allocation target is not met by the baseline.

The unchanged benchmark releases the player at Z0 and subsequently settles deep
underground. Post-loop drain and final scene geometry are consequently qualified
as that existing workload, not normal stationary surface gameplay. Moving frame
statistics cover the moving window, but several cumulative lifecycle counters
and final fingerprints extend beyond it. Do not treat post-route results with
different final positions as an exact same-position comparison.

## Moving comparison

| Metric | Baseline | Candidate C |
| --- | ---: | ---: |
| Moving average FPS | 499.15 | 541.70 |
| Moving CPU frame p95 | 4.107 ms | 3.576 ms |
| Moving CPU frame p99 | 5.659 ms | 5.081 ms |
| Distant schedule-to-renderable p50 | 19.498 s | 14.236 s |
| Distant schedule-to-renderable p95 | 25.586 s | 21.259 s |
| Average distant queue | 3,713 regions | 3,047 regions |
| Route-lag p95 | 7.69 chunks | 4.45 chunks |

Distant median readiness improved27.0%, p95 improved16.9%, and moving frame p99
improved10.2%. These are one moving run per version, not a multi-run confidence
interval. The route-lag and distant lifecycle distributions include publication
activity through settlement, unlike the explicitly moving frame window.

Coarse classification CPU time increased from18.799 to198.233 ms accumulated,
with maximum query0.165 to0.341 ms. This is the intended CPU-for-avoided-GPU-work
tradeoff. Published GPU regions per moving second fell449.3 to415.5 because fewer
regions needed meshing; a raw jobs/second decrease is not a loading regression
when job requirements differ. All coarse LOD1..6 and transition final fingerprints
match. LOD0 differs with final retained gameplay positions after the unchanged
underground release. Startup geometry matches exactly at a fixed location.

Process peak increased4.136 to4.377 GB (+5.8%), with a higher starting process
size after repeated Play; GPU peak decreased2.122 to2.073 GB. Total managed
allocation increased1.972 to2.094 GB (+6.2%) while bytes/frame decreased32,392 to
31,696; more frames were processed. Existing recurring allocations remain.
Both had zero runtime exceptions, unsafe placement commits and transition
face/lateral/table mismatches. Maximum CPU frame increased107.8 to117.4 ms and
maximum GPU frame11.56 to13.08 ms, while CPU p95/p99 improved and GPU p99 was
nearly unchanged. Individual maxima are retained, not concealed by averages.

### Stationary qualification remains incomplete

The existing benchmark waits for visual settlement before its stationary window;
it does not wait for all collision work. Candidate C reached that phase sooner,
and its saved result still had3,392 of4,913 collision regions ready,1,519 pending
and zero failures. Baseline had4,913 ready. Candidate stationary FPS546.6 versus
baseline622.5 therefore includes different concurrent collision work, in addition
to different final positions. It is not a matched settled-stationary comparison.
The unmodified benchmark reports completed, but that does not establish full
project acceptance. This limitation is preserved; the user declined the return
correction, and no workload patch was applied. No commit/push or shipping
performance acceptance is claimed while this qualification gap remains.

## Evidence and limits

Raw startup snapshots and moving results are under
`Docs/ValidationEvidence/Water/` with scheduler- and classifier- prefixes.
The validation ledger preserves rejected experiments, the editor exit and all
performance observations. No frame regression, missing terrain or unsafe commit
is accepted by the prototype plan. Full multiplayer and unrelated terrain/cave
qualification are outside these bounded experiments.
