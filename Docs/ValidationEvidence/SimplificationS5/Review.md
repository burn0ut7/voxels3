# S5: shared target/configuration decision — human review

2026-09-08. **Candidate tested; awaiting human approval.** S4 acceptance is
recorded/pushed96e530a (implementation898ebd9). S5 uncommitted; S6 not started.

## Change

VoxelManager.OnUpdate now resolves the effective visual configuration first,
then shares the gameplay/visual-change and target-movement decision. Invalid
requests retain _targetVisualConfiguration; valid data changes still take the
full ApplyConfigurationAndRebuild branch first. Warning suppression, rebuild
reason precedence, valid gameplay radius application during rejected visuals,
ActiveStreamingTarget selection, update cadence and downstream order remain.
One source file,24lines added/40removed,net16removed; no new helper/cache/state.

For valid configuration, the shared radius bounds are already guaranteed by
TryValidateConfiguration. For invalid configuration, the same bounds guard
remains; retained visual settings make visualChanged false. This preserves the
old branch conditions without duplicating target coordinate/rebuild logic.

Player discovery is unchanged. Its scene enumeration covers enabled offline
players and host proxies, deduplicates coordinates, applies a cap, sorts Z/Y/X,
falls back to streaming center and includes separate actor interests. The peer
registry is not shown to cover all these cases. There is no separate discovery
timing scope; this host-only run does not establish multi-player discovery cost.
That research subquestion remains unmeasured/deferred, not declared optimized.

## Before and after

SIMPLIFICATION-S5-001/v1, same1000/167-page world/private fixture/settings,
cold processes. B1 source96e530a/PID43688,run90d4110ce94c4627af395031c3da4fea.
C1 PID36032,run60ecf92f5fcd45799e274f5908afb560; hash in c1-source.json.

| Metric | Before | After |
| --- | ---: | ---: |
| Moving FPS | 817.85 | 825.41 |
| Frame p95 / p99 (ms) | 1.937 / 3.930 | 1.889 / 3.829 |
| Maximum frame (ms) | 25.193 | 22.367 |
| GPU p95 / p99 (ms) | 1.663 / 2.136 | 1.620 / 2.098 |
| Maximum GPU reading (ms) | 4.036 | 11.819 |
| Stationary FPS | 901.41 | 909.89 |
| Stationary frame p95 / p99 (ms) | 1.705 / 2.580 | 1.689 / 2.572 |
| Allocated bytes/frame | 30,397 | 30,337 |
| Peak process bytes | 4,004,098,048 | 4,011,249,664 |
| Peak GPU bytes | 2,879,204,496 | 2,828,992,848 |
| Collision ready p95 / p99 (ms) | 3,981 / 9,459 | 3,820 / 9,428 |
| Publication p95 / p99 (ms) | 85.438 / 104.937 | 84.331 / 103.965 |
| Maximum publication (ms) | 185.325 | 175.774 |
| Maximum synchronous streaming (ms) | 18.621 | 15.342 |
| Maximum placement preparation (ms) | 16.327 | 14.594 |
| Maximum placement level lag | 2 | 2 |

FPS +0.92%,allocation/frame -0.20%,processpeak +0.18%,GPUpeak -1.74%.
Declared percentile/memory/allocation/collision screens pass. GPU maximum rose
4.036 ->11.819ms; preserve this unexplained spike. One pair does not establish
speedup or exact refactor cost. GCmaximum13.020 ->13.583ms also retained.

No timed errors/exceptions;4913ready and all visual/transition/placement work
finished, peakbacklog127both, unsafecommits0. Startup regular topology matches
AAA8D471009389A6. Final B1 center(0,0,-1) versus C1(-1,-1,-1) differs through
physics settling, so whole-view final hashes are not directly comparable.
No claim of identical contact, exhaustive visual equivalence or improved holds.

## Configuration checks (both sources, outside timed windows)

| Normal property request | Observed before and after |
| --- | --- |
| Visual maxLOD7,gameplay8 | Rejected; applied maxLOD6,visual revision2 retained |
| Keep invalid maxLOD7,gameplay7 | Valid gameplay change applied;3375collision regions desired/ready |
| Keep invalid maxLOD7,gameplay-1 | Invalid radius rejected;3375desired/ready retained |
| Restore maxLOD6,gameplay8 |4913desired/ready recovered |

Expected warnings were emitted only when error reason changed. Source scene
was not edited. Timed route exercises ordinary target movement; invalid-config
movement, explicit/local/fallback target changes and multi-player spawn/proxy/
disconnect cases were not runtime-tested. Data-reset precedence was source
reviewed, not exercised by destroying/reseeding the saved world.

Runtime/editor builds and cold live compile pass0warnings/errors. Tested hash
and all167fixture page hashes unchanged. Existing baseline shutdown Error window
recurred after Source2Shutdown; log retained. Candidate full editor shutdown
untested. Original1000user world/settings restored afterward.

## Human checks before acceptance

- Travel across chunk/LOD boundaries in both directions; look for missing terrain,
  new stutters, incorrect detail changes or collision holds.
- In Play, try an invalid visual request while moving, then restore valid settings.
  Existing terrain should keep streaming with the previously applied visuals.
- Check valid gameplay radius changes and recovery after invalid requests.
- Check your normal target/player setup, editing and collision. If you use target
  overrides or multiple players, verify spawn/disconnect and per-player coverage.

Pause here. S5 remains uncommitted pending approval with the GPU/final-view
limits and unmeasured discovery subquestion disclosed. No S6. Raw before/after,
configuration observations and comparison.json are beside this document.
