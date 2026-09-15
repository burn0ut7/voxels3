# Shadow dispatch optimization

The corrected implementation batches depth visibility into one compute launch
per view, with one group per geometry arena. Shared results are copied into the
original private indirect arguments before zero-offset draws. Four shadow
cascades, contact shadows, geometry, materials and light-view culling remain.

SHADOW-FPS-003/v1 ran the unchanged standard figure-eight in the current edited
world: speed2500, distance50000, one loop,1847x959, seed1337/generator46, LOD0..5,
30-second ready warmup and10-second stationary sample. Both compared runs used
fresh editor processes and the same predictive-field prerequisite fix.

| Measurement | Original | Corrected optimization | Change |
| --- | ---: | ---: | ---: |
| Moving FPS | 403.88 | 425.46 | +5.34% |
| Stationary FPS | 472.81 | 533.66 | +12.87% |
| Moving GPU frame time | 1.617 ms | 1.232 ms | -23.83% |
| Moving p99 frame time | 6.990 ms | 7.189 ms | +2.85% |
| Moving maximum frame time | 91.042 ms | 96.828 ms | +6.35% |
| Stationary p99 frame time | 3.368 ms | 3.159 ms | -6.20% |
| Managed bytes allocated | 4,684,765,328 | 5,024,774,832 | +7.26% |
| Peak process bytes | 4,742,803,456 | 4,783,255,552 | +0.85% |
| Peak GPU bytes | 1,881,171,695 | 1,889,008,559 | +0.42% |
| Post-route streaming drain | 13.616 s | 12.826 s | -5.80% |
| Maximum placement preparation | 9.592 ms | 10.297 ms | +7.35% |

Both runs finished with zero measured exceptions, unsafe commits and pending
queues. The corrected post-route audit passed88/88 regions, with no stale results
or geometry/draw failures. Matching secondary views retain the extended character
cast shadow. This audit checks forward geometry/draw data, not all shadow draws;
complete terrain self-shadow and behind-terrain occlusion coverage is unverified.
These are single bounded runs, not statistical confidence estimates.

The relative FPS/tail/allocation/memory criteria pass. The existing absolute
10-second streaming-drain limit still fails in both variants. Full acceptance,
commit and push remain pending; no gate was weakened. Editor shutdown also
repeatedly produced a prefab-destruction error dialog, including with the original
renderer. Corrected cold startup succeeded with no new Sentry marker, and the
post-start console check through cursor227 found no errors. The editor remains
in visible Play with the normal game camera.

The initial shared-argument candidate was rejected: nonzero-offset model draws
lost part of the character shadow in a matching secondary view, despite a passing
first image and geometry audit. Its higher FPS is not evidence for the corrected
implementation. All historical runs and failures remain in the ledger. An extra
cold original control resolved the initial unmatched process-memory comparison.

Before either comparison, predictive loading could submit an edited-region
descriptor without its field reference and stall. The prerequisite captures the
field on actual scheduling, matching transition loading. Both variants include
that fix; unrelated existing terrain-prototype changes were preserved.

- [Cold original result](cold-control-result.json), [corrected result](revised-result.json), [comparison](revised-comparison.json).
- [Corrected source manifest](revised-source.json), [setup](revised-setup.json), [audit](revised-audit.json).
- [Original secondary image](secondary-original.png), [corrected post-route image](revised-secondary.png), [rejected image](secondary-rejected-shared-args.png).
- [Isolated four-file source patch](optimization.patch).
- [Full validation ledger](../../ValidationResults.md).
