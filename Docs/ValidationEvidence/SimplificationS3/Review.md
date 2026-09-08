# S3: shared percentile reporting — human checkpoint

2026-09-08. **Fresh S2 control also runs slower; latest S2/S3 comparison is close. User accepted S3 on2026-09-08: "I accept. Move onto S4".**
Paused for human review as requested. S3 is uncommitted; S4 has not started.
S2 acceptance was recorded and pushed as6ed6ea1 (implementation d4ce975).

## Change and preserved contract

A small readonly value type, PerformanceSampleTails in PerformanceTestResult.cs,
computes nearest-rank p50/p95/p99 and maximum from already sorted retained
samples. GPU MetricSamples, CollisionSamples, ordinary/moving CPU frames,
stationary CPU frames and GPU frames use it. Superseded formulas/helper removed;
four runtime files,37lines added/44removed (net7removed). No extra sample buffers,
heap-allocated result, new collection framework or serialized fields.

Collection ownership remains local. GPU rejects negative/nonfinite inputs and
includes overflow in its mean; collision retains inputs without that filter and
averages retained samples only. Frame Samples still counts observed frames;
truncated CPU maximum remains null. Empty tails remain zero. Existing arrays,
lists, capacities, sorting, reset timing, profiler scopes and report windows
remain. Sharing collectors would silently alter these different contracts, so
that broader consolidation is not part of this candidate. Specialized schedule/
transition latency reporting also remains outside this slice.

For nonempty n, each fixed percentile p is unchanged: ceil(n*p)-1. Existing
clamps cannot change that index for0<p<=1 and valid positive retained counts.
Empty input returns default zero before indexing. Sorting/mean summation order
is unchanged; no schema migration. This establishes source equivalence, not
an empirical replay of identical raw samples. No synthetic test path was added.

## First before/after performance pair

SIMPLIFICATION-S3-001/v1, canonical figure-eight, cold editor processes, same
revision997/167-page world/settings. B1 source6ed6ea1/PID83108,
run8c0e93757f1647dda523cd5d40dace2b; C1 PID23136,
runa2ae574530d84fc7a108a495e9ed1e07. Exact candidate hashes in c1-source.json.

| Metric | Before | After |
| --- | ---: | ---: |
| Moving average FPS | 855.89 | 822.23 |
| Frame p95 / p99 | 1.761 / 3.743 ms | 1.902 / 3.857 ms |
| Maximum frame | 22.690 ms | 24.936 ms |
| GPU p95 / p99 | 1.485 / 1.947 ms | 1.657 / 2.127 ms |
| Maximum GPU reading | 11.493 ms | 9.621 ms |
| Allocated bytes/frame | 30,106 | 30,352 |
| Total allocated bytes | 3,142,205,840 | 3,043,339,896 |
| Peak process bytes | 4,023,349,248 | 4,017,405,952 |
| Peak GPU bytes | 2,828,992,848 | 2,879,324,496 |
| Stationary FPS | 955.01 | 888.87 |
| Stationary frame p95 / p99 | 1.453 / 2.263 ms | 1.730 / 2.618 ms |
| Stationary GPU p95 / p99 | 1.329 / 1.647 ms | 1.574 / 1.954 ms |
| Collision ready p95 / p99 | 3,493.735 / 9,133.345 ms | 3,948.694 / 9,565.081 ms |
| Publication p95 / p99 | 79.485 / 99.372 ms | 84.633 / 105.713 ms |
| Maximum publication | 169.545 ms | 183.351 ms |
| Maximum synchronous streaming | 14.753 ms | 17.009 ms |
| Total synchronous streaming | 2,188.434 ms | 2,119.935 ms |
| Maximum placement preparation | 13.981 ms | 16.192 ms |
| Maximum GC pause | 11.941 ms | 15.094 ms |
| Maximum placement level lag | 2 | 2 |

Moving FPS -3.93%, allocation/frame +0.82%, processpeak -0.15%, GPUpeak +1.78%.
Moving frame/GPU percentile differences remain within the declared0.25ms
allowance, but stationary CPU p95/p99 rose0.278/0.356ms and stationary GPU p99
rose0.308ms: failures. Collisionready p95 increased13.02%, exceeding5%/10ms.
Publication tails, frame/GC/streaming maxima also worsened. Lower total allocated
bytes coincides with fewer measured frames (104,370 ->100,267), not proof of
allocation savings. One pair cannot attribute these changes to this small refactor
or to environmental variation. Do not accept or dismiss the regression automatically.

Ordinary-play startup overview was986.9FPS/p95 1.05/p99 1.88ms before and
979.4FPS/1.09/2.06ms after. These include normal periodic overview updates;
the trailing200-frame profiler snapshots are retained in startup JSON. They do
not isolate completion/sort cost. No claim of reduced observer cost, identical
raw-sample replay, nonfinite-input runtime coverage or overflow saturation.

## Validation and limits

Runtime/editor builds pass0warnings/errors and live engine compile succeeds.
Initial build failed for missing System import in the result file; corrected
before candidate engine launch, failed build retained. Both timed routes report
zero exceptions; engine console has no errors. Existing prefab shutdown Error
window recurred before/after baseline; logs preserved and already-shutting-down
processes terminated for cold restart. Candidate shutdown untested.

Both results retain schema26 and identical exported field paths. All24 inspected
p50/p95/p99/maximum distributions are finite and ordered in both; no truncations
reported. This checks output shape/order, not equality of different live timings.
Collection and overflow behavior was checked against original source.

All4913collision regions ready after drain; no visual/transition/placement work,
peak backlog127both, maximumplacementlag2both, mismatches/unsafecommits0.
Startup regular digests match CFC3EADBA35BABC5/C84BDA6FD612E0EB; final regular
968D9276B87662B2/293292CF0AEC3519 and transition18B28FD82D91047C/
D45A74DD17279E1C match. Both finish in center(0,0,-1), grounded/released, but
player positions differ (2.957,15.512,-31.120) versus(43.854,19.425,-28.902);
this is not proof of identical dynamic contact. All167saved pages retain their
SHA256 hashes; revision997 preserved. No terrain edit or multiplayer test run.

## Human checks and decision

- Watch ordinary frame/memory/status displays during idle play and streaming.
  Check that values continue updating and no new stutter appears at refreshes.
- Inspect the saved figure-eight report: moving/stationary sections, sample and
  truncation counts, GPU/collision distributions and diagnostic fields remain.
- Travel across LOD boundaries, return to edited terrain, dig/build, and walk/jump
  on it. Look for cracks, stale meshes, holds or collision disagreement.
- Treat the failed stationary/collision timing screens above as unresolved.
  A fresh matched before/after pair is recommended before accepting S3; it has
  not been run because the requested checkpoint is immediately after C1.

No S3 commit/push or S4 until human approval. No assertion that these human
checks have been completed. Raw evidence, comparison.json and report-checks.json
are beside this document; the chronological record is in ValidationResults.md.

## C2 after-only recheck — 2026-09-08

User requested another after performance run. Candidate hashes unchanged; cold
PID60840, run aac18ba6903b4f9b9d2e8f943f237097, same v1 workload and criteria.
User world had advanced to999: saved through the production path and separately
as s3-user-999. Ran a private copy of the preserved997 fixture, then restored
latest999 and the original user save slot. No source changes or rebuilds.

| Metric | Before B1 | First after C1 | Recheck C2 |
| --- | ---: | ---: | ---: |
| Moving FPS | 855.89 | 822.23 | 812.58 |
| Moving frame p95 / p99 (ms) | 1.761 / 3.743 | 1.902 / 3.857 | 1.955 / 3.926 |
| Stationary FPS | 955.01 | 888.87 | 870.82 |
| Stationary frame p95 / p99 (ms) | 1.453 / 2.263 | 1.730 / 2.618 | 1.806 / 2.672 |
| Collision ready p95 (ms) | 3,493.73 | 3,948.69 | 3,926.50 |
| Allocated bytes/frame | 30,106 | 30,352 | 30,452 |
| Peak process bytes | 4,023,349,248 | 4,017,405,952 | 4,034,215,936 |
| Peak GPU bytes | 2,828,992,848 | 2,879,324,496 | 2,828,992,848 |

C2 movingFPS is5.06% below B1 and1.17% below C1. The failed stationary and
collision-p95 screens repeat. C2 CPUmaximum22.1265ms, GPUmaximum10.867596ms,
GCmaximum13.39ms; publicationp95/p99/max85.7384/104.2131/179.1841ms;
maximumsynchronous18.1737ms,placementlag2. All4913collision regions ready,
queues0, no exceptions/console errors; startup and final regular geometry
fingerprints match prior runs. All167fixture page payloads and tested source
hashes unchanged. Raw c2-route/startup/settled/editor and c2-comparison retained.

Conclusion: the first slow after result was not an isolated observation in this
recheck. This does NOT establish that the percentile refactor caused the loss:
there is still only one before baseline, and environment/order effects have not
been controlled by a new B2. Recommend a fresh before control before deciding
to retain or revert. S3 remains unaccepted/uncommitted; no S4. Human checks above
remain applicable. No automatic acceptance or discarded prior failure.

## B2: exact S2 checkout control — 2026-09-08

At the user's request, preserved35dirty/untracked files in stash
441347c41f8f850d0937a704c6c6ff2411e82942 and an external byte-verified backup,
checked out d4ce975 detached with a clean worktree, then ran one cold control.
Original B1's6ed6ea1 differs from d4ce975 only in acceptance documentation.
Same v1 settings,997fixture, startup geometry and settlement; engine26.09.01c,
PID27348,run0ffee769036b492887f89c792b1c7019. Evidence was recorded outside
checkout until the original branch and all35preserved files were restored
byte-for-byte. Stash retained as backup. Restored S3 live compile succeeds.

| Metric | Original S2 B1 | S3 C1 | S3 C2 | Fresh S2 B2 |
| --- | ---: | ---: | ---: | ---: |
| Moving FPS | 855.89 | 822.23 | 812.58 | 814.40 |
| Moving p95 / p99 (ms) | 1.761 / 3.743 | 1.902 / 3.857 | 1.955 / 3.926 | 1.935 / 3.958 |
| Stationary FPS | 955.01 | 888.87 | 870.82 | 889.58 |
| Stationary p95 / p99 (ms) | 1.453 / 2.263 | 1.730 / 2.618 | 1.806 / 2.672 | 1.739 / 2.670 |
| Collision ready p95 (ms) | 3,493.73 | 3,948.69 | 3,926.50 | 3,967.70 |
| Allocated bytes/frame | 30,106 | 30,352 | 30,452 | 30,435 |

Latest S3 C2 versus fresh S2 B2: movingFPS -0.22%; stationaryFPS -2.11%;
CPU/GPU p95/p99 differences all within0.25ms; collisionready p95 -1.04%,p99
+0.05%; allocation/frame +0.057%; processpeak +1.72%,GPUpeak -1.75%.
Thus the specific stationary/collision failures against original B1 do not
reproduce against the fresh S2 control. B2 also lacks S3 yet shows the slower
level of performance. This weakens attribution to S3; it does not identify the
underlying environmental/run-to-run cause or prove zero cost in every scenario.

Keep remaining worse C2 maxima visible: GPU10.868 versus10.116ms,
synchronous18.174 versus15.729ms,placementpreparation17.269 versus15.107ms,
publication179.184 versus171.708ms. Publicationp95/p99 is85.738/104.213ms
versus86.209/108.771ms; maxplacementlag2/backlog127both. No new errors or
exceptions; all4913ready, all queues drained, regular and transition hashes
match, mismatches/unsafecommits0. Source checks establish clean S2 at measurement.
Normal B2 editor close reached Source2Shutdown then an Error window; preserved
log and terminated that shutdown process before restoring S3.

Conclusion: original B1 remains a recorded faster run, not a representative
baseline established by repeats. Latest comparison passes the declared numerical
percentile/memory/allocation/collision screens, with isolated maxima still
qualified. S3 remains preserved and uncommitted, awaiting human feature review
and acceptance; no S4. Latest999user world restored in its normal save slot.
See b2-route.json, b2-comparison.json and b2-restoration.json for exact evidence.

## C3: matched-method S3 rerun after fresh S2 — 2026-09-08

User requested another S3 run in the same way. ColdPID43536,
run3eca70e31d2d4c97b993f2231cd8b7e7; candidate hashes and all167fixture page
hashes unchanged, same997world/settings/warmup/v1 scenario. No source edits.

| Metric | Fresh S2 B2 | New S3 C3 |
| --- | ---: | ---: |
| Moving FPS | 814.40 | 801.79 |
| Moving frame p95 / p99 (ms) | 1.935 / 3.958 | 1.996 / 3.935 |
| Stationary FPS | 889.58 | 905.01 |
| Stationary frame p95 / p99 (ms) | 1.739 / 2.670 | 1.726 / 2.650 |
| Collision ready p95 / p99 (ms) | 3,967.70 / 9,532.23 | 3,872.32 / 9,535.97 |
| Allocated bytes/frame | 30,435 | 30,547 |
| Peak process bytes | 3,965,829,120 | 4,018,135,040 |
| Peak GPU bytes | 2,879,324,496 | 2,879,324,496 |
| Maximum placement level lag | 2 | 4 |
| Maximum synchronous streaming (ms) | 15.729 | 17.738 |
| Maximum placement preparation (ms) | 15.107 | 17.006 |

MovingFPS -1.55%; stationaryFPS +1.73%. CPU/GPU percentile, memory,
allocation/frame and collisionready screens pass versus B2. C3 GPU p95/p99/max
1.7015934/2.195835/10.490179ms; frame maximum21.4319ms; GCmaximum14.59ms;
publicationp95/p99/max87.5409/108.6256/173.7582ms. Preserve increased placement
lag and synchronous/preparation maxima as unresolved observations. No causal
performance improvement/regression claim from these variable runs.

Startup regular fingerprints and route transition fingerprints match B2;
transition mismatches/unsafecommits0. All4913collision regions ready, queues0,
player grounded/released, no exceptions/console errors. Final player settled
(-125.466,29.151,-62.892),center(-1,0,-1), so final regular fingerprints
96C649212D0F006A/675F0234A262266B cover a different spatial set from B2's
center(0,0,-1). This limits final matching-view comparison; it is not evidence
of a geometry defect. Human terrain/collision and streaming checks still apply.

Restored original user world999/normal slot after run. Pre-run editor showed
shutdown Error window but exited before attempted termination (process already
absent); recorded without retry. S3 remains unchanged, uncommitted and awaiting
human acceptance; no S4. All prior runs retained. See c3-route/comparison and
startup/settled evidence beside this document.

## Human acceptance — 2026-09-08

User explicitly accepted S3 and authorized S4. Acceptance follows all recorded
runs and disclosed placement/final-view limits; no additional human-check
completion is inferred. Candidate source hashes remain those tested in C1–C3.
