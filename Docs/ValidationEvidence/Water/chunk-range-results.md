# Water chunk coverage validation

2026-09-09, WATER-CHUNK-RANGE-001/v1. Engine26.09.08, Ryzen9800X3D/RTX5090,
visible editor host, fps_max1000, water-load-comparison-v2, generator13, seed1337.
Canonical figure-eight: speed2500, distance50000, one loop, Z0; fresh normal
Stop/Play and >=30s settled warmup. Full parameters are in the validation ledger.
Before source:8ec1920 plus existing working-tree water implementation. Candidate:
chunk-owned water surface correction in the same working tree. Source manifests
and complete runtime records are retained alongside this report.

| Moving measurement | Before B1 | After C1 |
| --- | ---: | ---: |
| Average FPS | 547.02 | 540.93 |
| Frame p95 ms | 3.4284 | 3.4547 |
| Frame p99 ms | 4.7021 | 4.6574 |
| Maximum frame ms | 128.721 | 109.748 |
| GPU p95 ms | 1.2789 | 1.4048 |
| GPU p99 ms | 1.9233 | 1.8287 |
| Peak process bytes | 2782081024 | 2538160128 |
| Peak GPU bytes | 1922850827 | 1872505999 |
| Allocated bytes/frame | 32017.322 | 31923.082 |
| Exceptions / unsafe commits | 0 / 0 | 0 / 0 |
| Final collision ready/pending/failures | 3223/1688/0 | 4913/0/0 |

B1 run e48ae7c45c4f41328185224b37dc0e7d; C1 run
77264791356b430c8d866de50f7ef1f8. Moving FPS decreased1.11%; GPU p95 increased
9.84% (0.126ms); other listed tail, memory and allocation measurements stayed
within the predefined10% investigation threshold or improved. All meshing queues
drained in both records. No material unexplained moving regression identified.

Stationary samples are not comparable: B1 final target(-1585.6394,2768.804,-31032.918)
with collision pending1688; C1(-4186.12,3988.2664,718.471) with pending0. Preserve
both full records; do not claim a stationary speedup.

R1 PASS: at the fixed detached camera and target, water ends at the published
terrain boundary. Candidate screenshot chunk-range-r1-after.png; original repro
visual-range-overrun.png. Settled resident surface count1216, vertices7296;
outer LOD6 bounds[-262144,-262144,-262144]..[262144,262144,262144], all regular
and transition queues0, geometry mismatches0, invalid tables0. Shoreline remains
visible within terrain coverage. User subsequently reported "Looks good".

R2 INTERRUPTED: after target0,0,400000 was requested, play externally restarted
at08:28:32; observed player near authored spawn and camera no longer ejected.
This is not a pass or evidence of a vertical-coverage defect. R3 NOT RUN.
These additional edge cases and multiplayer remain unverified. No further
runtime manipulation after user acceptance. Final native runtime/editor compile
status: success,0 errors,0 warnings.

Implementation remains in the shared working tree: it depends on pre-existing,
uncommitted water/generation/material work. This evidence commit intentionally
does not include those other task changes or imply a standalone code release.
