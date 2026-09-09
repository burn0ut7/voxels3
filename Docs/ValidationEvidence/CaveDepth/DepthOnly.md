# Cave depth comparison — 2026-09-08

The candidate extends cave support from 8,192 to 32,768 units below the local
surface (16 to 64 base chunks). CPU and GPU recipes advance to generator6;
wavelengths, surface settings, overburden, streaming radii and collision budgets
are unchanged. Old saves retain their version check; user selected fresh worlds.

## Comparable visible runs

CAVE-DEPTH-001/v1, exact workload in the validation ledger. B2: PID82636,
run c18aea25d2754e75b4b1fd53bd1f8cf8, baseline runtime source794b14fc.
C2: PID29296, run0fad953c1e1f45dca03789d0785cdb98, c1-source.json manifest.
Both cold visible editor, engine26.09.01c, unedited fresh worlds, same scene,
seed/configuration, >=30s settled warmup, canonical one-loop figure-eight.
Concurrent commits changed documentation/skill instructions, not candidate
runtime hashes. B1 hidden-window measurement is retained but excluded.

| Measurement | B2: 16 chunks | C2: 64 chunks |
| --- | ---: | ---: |
| Average FPS | 821.31 | 695.29 |
| Frame p95 / p99 (ms) | 2.0227 / 3.8664 | 2.2884 / 3.4828 |
| Worst frame (ms) | 21.0068 | 41.7518 |
| GPU p95 / p99 (ms) | 1.6606 / 2.2883 | 1.9255 / 2.5349 |
| Peak process memory (bytes) | 3959521280 | 3926749184 |
| Peak GPU memory (bytes) | 2878626856 | 3735193448 |
| Allocated bytes/frame | 29222.229 | 30623.422 |
| Collision ready p95 / p99 (ms) | 3184.7117 / 8740.261 | 3279.658 / 8582.035 |
| Render publication p95 / p99 (ms) | 83.7021 / 104.1933 | 155.4021 / 194.4966 |
| Maximum placement level lag | 4 | 10 |
| Maximum GC pause (ms) | 12.433 | 12.915 |
| Maximum synchronous streaming (ms) | 17.7033 | 12.8211 |

Average FPS decreased15.34%; peak GPU memory increased29.76% (2.68 to3.48GiB).
Frame p95 and GPU p95 exceed the predefined0.25ms/5% allowance; GPU p99
increase0.2465ms stays just within its0.25ms allowance. GPU memory exceeds5%.
Process memory, allocation/frame and collision tails pass their screens.
Worse worst frame and placement lag are retained without dismissing them as
noise. Publication p99 increased86.67%. No causal attribution of individual
stalls from one pair of runs.

Both ended with4913collision regions ready, zero pending collision/meshing,
zero collision failures, managed exceptions, unsafe placement commits and
transition boundary mismatches. Candidate startup coarse rejection count fell
from17920 to14336, consistent with wider support. Final geometry totals were
15.26M versus34.61M triangles, but final centers differ by one X chunk
(B2[-1,0,0], C2[0,0,0]); these are not equal-set geometry measurements.

Managed compilation and production shader execution passed after cold startup;
Sentry last_crash stayed2026-09-08 10:47:25 local. MCP asset_compile refused
shader source lookup; automatic engine recompilation produced the tracked
shader outputs and production run. Startup missing unrelated package assets,
shutdown Error-window interruptions, control-port conflict and externally
stopped warmup are retained in setup history, outside timing. No terrain
warnings/errors in the candidate timed console window.

Old world f58322c9b8374c0bb5556199124a290d remains on disk, with its selector
backed up as terrain/depth-original-selector.backup. Candidate fresh world
4b5de831753641e3ae930fa16f202079 was saved by normal play-stop and selected.
Editor remains visible and stopped. No migration or old-world deletion.

## Decision

HOLD commit/push pending explicit user acceptance of the measured performance
cost, per AGENTS.md. The requested depth is implemented; no renderer rewrite
or workload reduction was added to disguise the cost. Deep-player traversal,
new-depth collision edge cases, multiplayer and exhaustive CPU/GPU equivalence
remain unverified. The surface figure-eight proves production streaming and
meshing at the configured long range, not all underground gameplay.

Raw evidence: [B2](b2.json), [C2](c2.json), [comparison](comparison.json),
[source hashes](c1-source.json), [B2 log](b2.log), [C2 log](c2.log).
