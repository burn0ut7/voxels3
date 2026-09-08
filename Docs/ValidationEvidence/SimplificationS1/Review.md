# S1: readiness reader removal — human review

2026-09-08. **Accepted by the user:** "I accept let's move on to s2".
Implementation is committed as `b5a354b`. Acceptance follows the disclosed
first-pair results and outliers below; no repeat pair or exhaustive completion
of the human checklist is claimed. S2 may now proceed.

## Change

Three placement-readiness checks retain their existing descriptor/version/epoch
comparisons but skip constructing a regional field reader. Actual mesh requests
still acquire the same immutable inputs. No sampler, shader, collision, profiler
definition, queue policy or saved-world format changed. This removes repeated
runtime work rather than substantially reducing source-line count.

## First performance comparison

Separate cold editor processes, same current saved world (revision996,167pages),
same canonical figure-eight, authored spawn, startup settlement and settings.
The [ledger](../../ValidationResults.md) records SIMPLIFICATION-S1-001/v1.

| Metric | Before B1 | After C1 |
| --- | ---: | ---: |
| Moving average FPS | 781.63 | 802.67 |
| Moving frame p95 / p99 | 2.054 / 4.160 ms | 1.989 / 3.942 ms |
| Moving maximum frame | 137.512 ms | 131.693 ms |
| Moving GPU p95 / p99 | 1.734 / 2.233 ms | 1.676 / 2.164 ms |
| Moving maximum GPU reading | 3.936 ms | 8.800 ms |
| Allocated bytes/frame | 31,697 | 30,523 |
| Peak process memory | 3,961,495,552 bytes | 4,018,667,520 bytes |
| Peak GPU memory | 2,879,324,496 bytes | 2,828,992,848 bytes |
| Stationary FPS | 880.99 | 869.98 |
| Stationary frame p95 / p99 | 1.802 / 2.767 ms | 1.886 / 2.866 ms |
| Maximum placement level lag | 4 | 6 |
| Maximum schedule-to-renderable | 192.835 ms | 235.024 ms |
| Maximum synchronous streaming | 11.359 ms | 17.009 ms |

Moving FPS increased2.69%, allocations/frame decreased3.70%, and frame/GPU
p95/p99 improved in this pair. Process peak increased1.44%; stationary FPS
decreased1.25%. These are observations, not a proven repeatable speedup.

**Performance acceptance remains open.** The predeclared percentile/memory/
allocation screens are within tolerance, but maximum GPU time, placement lag,
publication maximum and synchronous-streaming maximum worsened. The source
change alone does not explain those observations. A fresh unchanged B2/C2 pair
is warranted before treating the optimization as performance-qualified; it has
not run because the requested checkpoint is human review after the first pair.
Do not accept an unexplained regression implicitly.

## Verified and pending

- Runtime/editor compilation: zero warnings/errors; candidate live compile
  succeeded. No reported runtime exceptions or collision failures in either run.
- Both end with4913collision regions ready, zero visual/transition/collision
  backlog and no pending placement. Peak gameplay backlog is127 in both.
- Startup regular geometry fingerprints match. Final regular and transition
  fingerprints match, with zero transition mismatches and unsafe commits.
  Startup/end collision body counts and payload bytes match.
- All167 saved page records, revisions and payload hashes match the preserved
  world. Normal teardown advanced checkpoints81to83; five unreferenced older
  payload files were cleaned up. Those remain in the original backup. This was
  not a terrain reset or loss of current saved pages.
- Final player positions differ within the same chunk, consistent with the
  existing route's free settling. Exact dynamic-contact equivalence is not
  established. The inspected production screenshot shows rendered edited terrain
  in that view; it does not qualify every seam or tool behavior.
- Baseline editor shutdown reported a prefab-destruction assertion and left an
  Error window after engine shutdown. Its log is preserved. Candidate shutdown
  is untested; the candidate remains running for human inspection.
- Removal of the readiness reader-allocation path is established by source
  inspection. Engine-wide allocation changes are not exact per-call attribution.

## Please test before approving

1. Walk/sprint across chunk boundaries, turn around quickly, then return. Look
   for newly missing terrain, delayed patches, unexpected freezes or flicker.
2. Inspect where nearby detailed terrain meets distant terrain while moving
   and looking around. Look for new cracks, holes or a boundary left behind.
3. Dig and build across chunk edges, including on the negative-coordinate side
   of the origin. Both sides should update together; collision should follow
   the visible change without new invisible walls or falling through support.
4. Leave an edited area and return. Save, stop Play and reopen. Previously saved
   edits should remain and new edits should still work. These ordinary tests
   modify the world through its normal autosave; the pre-test backup is recorded.
5. If testing with a second player, compare the same edited area on both clients,
   including joining after edits. Watch for new disagreement or stuck loading.

Report what you tested, any differences, and whether you approve the feature
behavior. Timing outliers still require follow-up before overall acceptance.
No subsequent finding or commit should proceed on the basis of silence.

Evidence: [before](b1-route.json), [after](c1-route.json),
[comparison](comparison.json), [checkpoint verification](checkpoint-comparison.json),
[baseline shutdown log](b1-editor.log), [candidate build](c1-build.txt).
