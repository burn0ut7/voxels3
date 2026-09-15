# Complete seam cache performance catalog

User-authorized prototype, 2026-09-13. **Not accepted for shipping:** the corrected cache does not meet the fast near-instant target and increases background settlement time.

## Decision and correctness

Both corrected routes fail the ≤0.1s target and the ≤10s drain gate. Standard
allocations increased about 71%, and GPU peak rose from 1599 to 1744 MiB.
The prototype remains in the workspace, uncommitted and unaccepted; no push.

Both corrected final audits completed 88/88 meshes, stale/errors zero, maximum
edge 1.71 cells. Exact fine/coarse/lateral/table mismatch counts and unsafe
placement commits were zero. Moving and final coverage overlap, imbalance,
missing and extra active seam counters were zero. Final terrain/transition queues
were empty, water ready, collision 4913 ready, and exceptions zero. All stationary
packages were complete. The native final player screenshot showed continuous
checkered terrain without an obvious opening in that view.

This validates the sampled unedited routes and active meshes. It does not validate
every inactive cached direction's geometry, edits, multiplayer, teleports or
arbitrary movement. The before baseline was itself unaccepted; this comparison
does not promote either version to an accepted baseline.

At 30 seconds, corrected fast had 53/356 packages complete and 462/2136 faces
ready; standard had 55/288 packages and 498/1728 faces. Across the entire session,
fast logged 371553 local seam checks, 155221 misses, 109279 outside current cache
membership; standard logged 50838 checks, 27715 misses, 19233 outside membership.
These are repeated checks of local replacements, not unique jobs or exclusively
the near 27 chunks. A bounded box does not prebuild every intermediate refinement
dependency, and enlarging it added work without meeting the goal. Further cache
expansion is not supported by this experiment.

## Matched arrival and streaming comparison

Arrival is the fine-grained first observation that all 27 immediate chunks are prepared or presented after the route stops. It is not a per-frame guarantee throughout movement. The older one-second snapshot fields are not used for these rows.

| Run | Ready (s) | Visible (s) | Ready-to-visible (s) | Drain (s) | Max preparation (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| [Before fast](../LodPrediction/v2-fast-summary.json) | 0.4584 | 1.3663 | 0.9079 | 32.707 | 11.921 |
| [Before standard](../LodPrediction/v2-standard-summary.json) | 0.0218 | 0.0218 | 0.0000 | 20.453 | 12.717 |
| [Cache v1 fast](../LodSeamCache/v1-fast-summary.json) | 0.2434 | 2.6071 | 2.3637 | 41.286 | 12.755 |
| [Cache v2 fast](../LodSeamCache/v2-fast-summary.json) | 0.2293 | 1.0284 | 0.7991 | 54.988 | 14.030 |
| [Cache v2 standard](../LodSeamCache/v2-standard-summary.json) | 0.0000 | 0.3408 | 0.3408 | 41.034 | 13.053 |

## Frame pacing and allocations

| Run | Average FPS | p95 (ms) | p99 (ms) | Worst frame (ms) | Worst GC pause (ms) | Allocated (MiB) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Before fast | 137.62 | 12.216 | 16.021 | 105.117 | 16.161 | 4033.20 |
| Before standard | 106.21 | 17.716 | 20.088 | 81.590 | 14.660 | 1174.30 |
| Cache v1 fast | 135.26 | 12.018 | 17.160 | 1020.363 | 1015.547 | 4069.04 |
| Cache v2 fast | 140.88 | 11.117 | 14.779 | 76.600 | 20.560 | 4148.87 |
| Cache v2 standard | 123.04 | 13.515 | 18.165 | 82.410 | 19.086 | 2007.63 |

## Memory

| Run | Process average / peak (MiB) | GPU average / peak (MiB) |
| --- | ---: | ---: |
| Before fast | 4767.35 / 4858.97 | 1551.62 / 1572.12 |
| Before standard | 4864.92 / 4891.19 | 1580.89 / 1599.12 |
| Cache v1 fast | 2558.47 / 2618.50 | 1552.13 / 1571.94 |
| Cache v2 fast | 2629.90 / 2826.88 | 1556.52 / 1572.20 |
| Cache v2 standard | 2767.47 / 2830.12 | 1658.79 / 1744.45 |

Process working-set reduction is not attributed to this cache: runs share a long-lived editor process and have different collection histories. Wardogs remained running throughout. These are single-run observations under contention, not statistically established speedups.

## Protocol and acceptance

Unchanged LOCAL-COVERAGE-001/v2: basic_example, seed 1337, generator 46, field revision 0, LOD0–5, 32 cells at base size 16, near/cache/gameplay extents 4/8/8, viewport 1847×959, RTX 5090, engine 26.09.08b, editor PID 10584, background Wardogs PID 10580. Land/mountain/plains 0.75/0.3/0.6; scales 77724.09/18681.756/5232.39; relief 3072, ruggedness 0.45, sea 0.

Each run restarts normal visible Play, settles startup (including all cache packages for cache runs), then waits an automatic 30 seconds. Fast: speed 10000, distance 200000, one loop. Standard: speed 2500, distance 50000, one loop. Both take about 122 seconds. Moving diagnostics are captured at 30 seconds; native post-drain capture waits 10 seconds. Route safety cap: 240 seconds. The player settles naturally at slightly different positions within the same origin chunk; raw reports retain exact positions.

Experimental arrival gate: ≤0.1s; canonical gate: ≤5s. Drain ≤10s, preparation ≤16.67ms, zero geometry/seam/coverage errors, settled queues empty, collision 4913 ready, water ready, no exceptions or unsafe commits, no unexplained pacing/allocation/memory regression. Retained seam geometry payload ≤192 MiB; this is an acceptance budget, not a hard arena allocation cap.

## Implementation and evidence

V1 retained six exact faces around rounded nearby coarse centers. Its 27-owner cube omitted a possible negative-side parent neighbor; the failed fast run is retained and no v1 standard run was taken. V2 derives bounds from the actual immediate LOD0 parents plus coarse neighbors. It uses existing GPU meshing, revision identities and active-face publication, with independent regular/seam service cursors. No shader changes or approximate lips were introduced.

Stationary v2 cache: 320/320 owners and 1920/1920 faces ready, 2,570,204 bytes (2.45 MiB) geometry payload. Cache counts include known-empty faces. Payload excludes allocator slack and regular owner meshes; whole GPU/process memory is reported separately. Peak payload is sampled at membership refreshes, not an allocator high-water mark.

V1 fast recorded a 1020ms frame, 1016ms GC pause and 1005ms GPU maximum. These are preserved without assigning a cause. Corrected v2 fast improved first visibility about 25%, but drain worsened about 68% and allocations rose about 2.9% relative to before fast.

Source identities: [before](../LodPrediction/v2-source.json), [v1](v1-source.json), [v2](v2-source.json). Current v2 hashes are checked against all 82 listed files. [Prototype-only patch](prototype-v2.patch) isolates this task from earlier workspace changes. [Machine-readable catalog](catalog.json) preserves exact numbers.

Each row links its summary; matching `-result.json`, `-arrival.json`, `-observations.json` and `-audit.json` files preserve raw evidence in the same directory. [Design](../../Architecture/CompleteSeamCache.md) and [validation ledger](../../ValidationResults.md) record scope, fixed criteria and all failed trials.
