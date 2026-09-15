# Repository simplification review — 2026-09-15

Scope: tracked runtime/editor code, UI, authored assets/configuration, tooling,
and documentation inventory. Ownership and reference searches cover terrain
fields, generation, meshing/publication, water, collision, storage, replication,
player/admin UI and editor tools. This review does not qualify every unfinished
feature or historical experiment.

## Decisions

| Area | Decision |
| --- | --- |
| HUD | Remove raw-key, binding and suppression tracing, its per-frame polling, and toggle logs. Keep the configured action, coordinates and visible-only refresh. |
| Streaming/render logs | Remove local/final publication dumps, render-view/callback logs, command-list/camera binding traces and state used solely by them. Keep publication timing, synchronization, recovery/failure messages and geometry audits. |
| Saved reports | Remove duplicate console summaries already present in saved JSON. Keep schema 29, the figure-eight runner, arrival measurements and explicit deformation/coverage reports. |
| Dead code | Deferred. Removing the unauthored template controller and unused accessors passed reference checks, but the measured candidate exceeded the preparation-time limit. The original code is restored; this report does not attribute the spike to those unused members. |
| Status/chunk simplification | **Rejected after two FPS regressions.** Restore the original status cache, refresh timer, chunk constructors and timing. Unused manager adjacency helpers also remain with the restored file. The failed cumulative candidate is preserved in `final.patch`; it is not the delivered implementation. |
| Generation/storage/networking | Retain conservative bounds, matching CPU/GPU recipes, regional snapshots, cancellation, stale-result checks, codec versions, save integrity and bounded replication. These have active consumers and correctness responsibilities. |
| Generated assets | Stop tracking 23 shader/scene derivatives; retain local compiled files. Twelve compiled shader names lack corresponding source/callers; the other compiled shaders have source owners. No shader source or compiled bytes were changed. |
| Historical evidence | Preserve the append-only ledger and supporting results, including failures and captures from distinct runs. The evidence directory contained 5,301 files / 845,269,737 bytes at review start. |
| Tooling/editor | Keep the table generator, shader rebuild tool and editor camera recovery; they serve current maintenance or documented engine workarounds. |

## Performance and correctness

[SIMPLIFY-001/v1](../../ValidationResults.md#simplify-001v1--repository-cleanup-before-baseline-2026-09-15)
fixes the workload and criteria before measurement. All runs use the same visible
playable figure-eight, engine 26.09.08b, Ryzen 7 9800X3D / RTX 5090, editor process
72028, and 1848×960 render size. Terrain revision was 1993 throughout; the ledger
corrects its initial stale 1990 description using all archived arrival reports.

Group 1 removes temporary diagnostics. The initial group 2 also removed status
work and simplified chunk construction; it failed twice and was withdrawn.
The revised group 2 added only dead controller/accessor removals to group 1, but
was also withdrawn after a preparation-time failure. **Only group 1 is delivered**,
with Git-only generated-file housekeeping.
Each variant starts through a normal Play restart and settled warmup. Raw results,
arrival reports and exact source patches accompany this review.

All completed runs have zero exceptions and pass the standard 88-mesh audit and
detailed overlap/balance/seam coverage checks. The preexisting 10-second streaming
drain target remains failed and is reported separately. No performance speedup
or complete multiplayer/digging qualification is claimed. HUD key interaction
was reviewed in source and compiled; it was not separately exercised by automation.

## Build and repository checks

Engine compilation passes. The rejected group 2 initially exposed one obsolete
status-refresh caller; that compile failure was corrected before its runtime runs
and remains recorded. Source hash checks identify each tested variant. Removed
logging-state searches cover code, Razor and authored assets. All 23 generated files
removed from Git remain locally available and ignored. No separate tests,
frameworks, scenes, components or test-only hooks were introduced.

## Recorded comparisons and delivery decision

| Source/run | FPS | Frame p99 ms | Allocated GiB | Process peak GiB | Drain s | Preparation max ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| [Original](before.json) | 475.27 | 8.2383 | 5.268 | 4.897 | 11.583 | 9.6399 |
| [Original repeat](control-repeat.json) | 488.72 | 8.6108 | 5.281 | 5.838 | 10.424 | 9.1653 |
| [Group 1](group1.json) | 471.90 | 8.3458 | 5.123 | 5.462 | 10.870 | 8.5552 |
| [Group 1 repeat — delivered source](group1-repeat.json) | 491.85 | 8.0750 | 5.469 | 4.870 | 10.022 | 8.6427 |
| [Group 2 — rejected](final.json) | 448.84 | 8.6361 | 5.184 | 6.370 | 11.646 | 12.9207 |
| [Group 2 repeat — rejected](final-repeat.json) | 457.19 | 8.5817 | 5.126 | 4.967 | 10.871 | 10.2039 |
| [Revised group 2 — withdrawn](revised.json) | 487.96 | 7.8328 | 5.395 | 4.957 | 12.041 | 20.2327 |

Group 1 was measured before and after its changes, then repeated after an original
source control exposed editor/session memory drift. Its repeat passes the recorded
relative FPS, frame/GPU tail, allocation and memory gates against both original
controls. Near arrival is 0.226 seconds; preparation is 8.643 ms. The existing
10-second drain target is still narrowly missed at 10.022 seconds. Failed first
comparisons remain in the ledger; no new regression exception is granted.

The delivered runtime diff exactly matches `group1.patch`, with all recorded
`group1-source.json` hashes verified after restoration. It removes a net 146
runtime lines. `final.patch` and `accepted-candidate.patch` are historical rejected
candidates (the latter filename was assigned before measurement), not patches to
apply to the delivered tree. Their source manifests and results are retained.
