# S6: unused surfaces — human review

2026-09-08. Candidate tested; NOT performance accepted. Awaiting human review.
S5 accepted and recorded in bec3178 (implementation3082fb6).

## Change and evidence

Remove Editor/MyEditorMenu.cs (8 lines: template menu whose only action displays
an It worked dialog) and TerrainFieldPage.CopyTo (2 lines: unused span copy).
Tracked source/authored resource search found no callers or dynamic type-name
references for these symbols. TerrainFieldPage is internal, CopyTo has no engine
attribute. SDK default compilation includes the editor template file; its Menu
attribute is its known registration and removing that template option is intentional.
No authored game feature depends on that dialog in the reviewed repository.

Retain CustomTopDownController: public attachable component with actual WASD,
rotation and overhead camera behavior. No authored attachment was found, but that
does not prove editor/external usage absent. Retain GetRange and WorldId hotload
repairs: no new evidence qualifies their removal. These are retained decisions,
not claims of completed hotload or optional-controller testing.

## Fixed comparison

SIMPLIFICATION-S6-001/v1. Fresh cold processes, same revision1404/235-page private
world, original authored spawn and canonical figure-eight settings. B1 bec3178,
PID41416, run57611464f8414feeab2a867f0d1fae0d. C1 bec3178 plus c1-source.json,
PID78880, runf87b22c47e414d74bfe4668f3b47d3f0. Both start exactly at
(0.07901251,-0.118453674), loop1, speed2500,distance50000,Z0; durations
121.93598/121.938866 seconds, normal 10s stationary tail. Original world changed
since S5, so S5 numbers are not the baseline. Raw route/observation files beside
this report preserve the full parameters and measurements.

| Metric | Before | After |
| --- | ---: | ---: |
| frame.averageFps | 823.8861 | 795.38696 |
| frame.p95Milliseconds | 1.9126 | 2.0143 |
| frame.p99Milliseconds | 3.8679 | 4.1249 |
| frame.maximumMilliseconds | 22.8115 | 23.038 |
| frame.p95GpuMilliseconds | 1.629591 | 1.7220974 |
| frame.p99GpuMilliseconds | 2.1083355 | 2.2027493 |
| frame.maximumGpuMilliseconds | 9.98807 | 9.21154 |
| runtime.averageManagedBytesAllocatedPerFrame | 30481.604 | 30755.215 |
| runtime.maximumGcPauseMilliseconds | 12.916 | 13 |
| runtime.exceptions | 0 | 0 |
| memory.peakProcessBytes | 4041596928 | 4019789824 |
| memory.peakGpuBytes | 2879324496 | 2879324496 |
| stationary.frame.averageFps | 896.54156 | 878.8917 |
| stationary.frame.p95Milliseconds | 1.7272 | 1.7947 |
| stationary.frame.p99Milliseconds | 2.5976 | 2.7385 |
| collision.requestToReady.p95 | 3927.1226 | 3874.911 |
| collision.requestToReady.p99 | 9572.255 | 9431.766 |
| meshing.scheduleToRenderable.p95Milliseconds | 85.2743 | 88.8902 |
| meshing.scheduleToRenderable.p99Milliseconds | 110.9334 | 115.2779 |
| meshing.scheduleToRenderable.maximumMilliseconds | 168.5259 | 172.9082 |
| streaming.maximumSynchronousMilliseconds | 15.0142 | 19.2357 |
| streaming.maximumPlacementPreparationMilliseconds | 14.4022 | 18.5536 |
| streaming.peakGameplayMeshBacklog | 127 | 127 |
| hierarchy.maximumPlacementLevelLag | 2 | 4 |
| hierarchy.placementUnsafeCommits | 0 | 0 |

Moving FPS fell3.46%. CPU p99 rose0.257ms (6.64%), exceeding the predeclared
max(5%,0.25ms) allowance by0.007ms. That screen FAILS; it is not rounded into a
pass. Other measured CPU/GPU percentile, allocation, memory and collision-tail
screens pass. Allocation/frame+0.90%,processpeak-0.54%,GPUpeak unchanged.
Maximum synchronous work15.0142->19.2357ms, preparation14.4022->18.5536ms and
placement lag2->4 also worsened. Preserve these unexplained observations.
One pair does not establish causation: removing code without a discovered caller
has no demonstrated hot-path mechanism for this slowdown, but that is not proof
that the difference is noise. Do not accept, commit or push this candidate as a
performance pass. Recommend a matched baseline/candidate repeat before acceptance;
no additional runs performed past the requested human checkpoint.

Both runs:4913collision regions ready, all queues settled, exceptions0,unsafe
commits0,transition mismatch counts0,peakbacklog127. Startup center/topology match
(0,-1,-1)/E501BB2624A993E7. Final centers differ B1(0,0,-1), C1(0,-1,-1), so
final whole-view geometry hashes are not directly comparable. No exhaustive
contact/camera/visual equivalence claim. All preserved page payloads and tested
source hashes unchanged. Normal world selector restored; no authored asset changes.

Runtime/editor builds0warnings/errors, fresh live compile succeeds. No timed
console errors. Existing shutdown Error window after Source2Shutdown occurred
before S6 and on B1; logs retained. Candidate full shutdown not tested. No new
hotload, multiplayer, terrain-edit or save/reopen operation coverage claimed by
the route; fixture reopen and original world restoration are verified separately.

## Human checks and acceptance

- Confirm normal movement, look/camera, run/jump and terrain-tool selection work.
- Edit terrain, travel across chunk/LOD boundaries, check seams and collision,
  then save/reopen and confirm the edit remains.
- Confirm only playercontrollertemplate/My Menu Option disappeared from editor
  menus; normal project tools remain available. Optional top-down controller stays.

Pause here for human review. S6 remains uncommitted. Performance qualification
is unresolved; S7 has not started. Approval must explicitly address the failed
screen if accepting these measurements without further qualification.

## C2 repeat — 2026-09-08

User explicitly requested another test. Unchanged source and scenario, cold
PID44840, run2f89695e0cae47108a3d21690068ecb9. Same start position and world;
121.93198s moving route. Raw c2-route.json and c2-comparison.json retained.

| Metric | Baseline B1 | First S6 C1 | Repeat S6 C2 |
| --- | ---: | ---: | ---: |
| frame.averageFps | 823.8861 | 795.38696 | 838.5864 |
| frame.p95Milliseconds | 1.9126 | 2.0143 | 1.8371 |
| frame.p99Milliseconds | 3.8679 | 4.1249 | 3.7422 |
| frame.maximumMilliseconds | 22.8115 | 23.038 | 35.601 |
| frame.p95GpuMilliseconds | 1.629591 | 1.7220974 | 1.5835762 |
| frame.p99GpuMilliseconds | 2.1083355 | 2.2027493 | 2.0604134 |
| frame.maximumGpuMilliseconds | 9.98807 | 9.21154 | 9.123802 |
| runtime.averageManagedBytesAllocatedPerFrame | 30481.604 | 30755.215 | 30328.943 |
| runtime.maximumGcPauseMilliseconds | 12.916 | 13 | 12.403 |
| runtime.exceptions | 0 | 0 | 0 |
| memory.peakProcessBytes | 4041596928 | 4019789824 | 4019159040 |
| memory.peakGpuBytes | 2879324496 | 2879324496 | 2879324496 |
| stationary.frame.averageFps | 896.54156 | 878.8917 | 932.05927 |
| stationary.frame.p95Milliseconds | 1.7272 | 1.7947 | 1.563 |
| stationary.frame.p99Milliseconds | 2.5976 | 2.7385 | 2.4348 |
| collision.requestToReady.p95 | 3927.1226 | 3874.911 | 3822.2856 |
| collision.requestToReady.p99 | 9572.255 | 9431.766 | 9344.4 |
| meshing.scheduleToRenderable.p95Milliseconds | 85.2743 | 88.8902 | 82.8275 |
| meshing.scheduleToRenderable.p99Milliseconds | 110.9334 | 115.2779 | 103.1673 |
| meshing.scheduleToRenderable.maximumMilliseconds | 168.5259 | 172.9082 | 169.7316 |
| streaming.maximumSynchronousMilliseconds | 15.0142 | 19.2357 | 18.0047 |
| streaming.maximumPlacementPreparationMilliseconds | 14.4022 | 18.5536 | 17.0813 |
| streaming.peakGameplayMeshBacklog | 127 | 127 | 127 |
| hierarchy.maximumPlacementLevelLag | 2 | 4 | 4 |
| hierarchy.placementUnsafeCommits | 0 | 0 | 0 |

C2 FPS is1.78% above B1 and5.43% above C1. CPU/GPU and stationary p95/p99,
allocation, memory and collision-tail screens pass against B1. The C1 FPS/p99
slowdown did not reproduce on identical candidate source; this is evidence of
run-to-run variation, not proof of no regression or an S6 speedup. Preserve C1's
failed screen. C2 worst frame35.601ms is worse than B1/C1; placement lag4 persists
versus B1's2, and sync/preparation maxima remain worse than baseline. These
unexplained maxima/lag prevent a clean overall performance qualification.
Recommend a fresh pre-S6 baseline run next to distinguish candidate-specific
lag/stalls from environment variation; do not infer causation from one baseline.
No baseline checkout or additional repeat performed in this turn.

All4913ready,queues0,exceptions0,unsafecommits0,mismatches0,backlog127. Startup
geometry matches; final center(-1,-1,-1) differs from B1 and C1, so whole-view
final hashes cannot establish equivalence. Source and original/fixture payload
hashes unchanged; original selector restored. Cold compile passed. Known shutdown
Error window recurred before C2; log preserved. Play was already stopped when
pre-run play_stop was requested, returning Not playing; no retry was issued and
this occurred outside the timed run. No timed errors. No new feature test coverage.

Human checks above remain pending. S6 uncommitted, not automatically accepted;
pause for the user's decision before any more tests, acceptance or S7.
