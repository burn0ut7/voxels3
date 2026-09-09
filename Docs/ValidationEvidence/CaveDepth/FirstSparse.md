# Deeper, sparser caves — current result

Implemented generator7: caves extend32,768units below local surface (64 base
chunks), with a continuous independently seeded regional filter targeting
roughly half eligible underground volume. Existing passage width thresholds,
noise wavelengths,512-unit overburden and streaming distances are unchanged.
The filter uses smooth trilinear hashed values, mirrored on CPU/GPU with a
conservative gradient bound. It can end passages at region boundaries. Half
coverage is statistical, not an exact count guarantee.

## Crash repair and production checks

The first sparse mask used an additional simplex query. It failed during load
with a Vulkan invalid-write GPU fault; Aftermath named the transition compute
shader. User reproduced play/load crashing. It was replaced, not retained as a
fallback, by the smaller hashed-value regional filter. Shader complexity was
the working hypothesis; the dump does not establish the underlying compiler/
allocation cause. See [crash log](c3-crash.log) and [dump summary](c3-gpu-crash.json).

C4 cold visible editor PID42196, engine26.09.01c, source[c4 hashes](c4-source.json):
world loaded, settled, and completed CAVE-DEPTH-001/v1 full production route,
run f73f1c2d9b2447f38ddb234b9532521c. Managed compilation passed. No timed
warnings/errors, managed exceptions, collision failures, unsafe commits or
transition boundary mismatches.4913collision regions ready; all queues empty.
No crash marker advance from15:00:09 (the earlier user crash/restart interval).

CAVE-LOAD-001/v1 also passed: normal stop saved new world
bbd2bdadff1242908971ff52d7e46752 atrevision0/checkpoint1; normal play reloaded
that identity, settled with4913ready and queues0, no new errors/device loss.
Initial and reload geometry digests match: topology08C690DA66F93111,
positionsF0C8959B39DDD24A. Player grounded on VoxelManager, motion enabled,
support hit true. Editor remains visible, playing, and interactive.
This reproducer no longer crashes in these checks; it is not exhaustive proof
against all GPU faults. Deep-player traversal, exact volumetric coverage,
multiplayer and every new-depth collision edge case remain unverified.

## Fixed visible figure-eight comparison

Same fresh-world seed/configuration and workload for B2/C2/C4, as defined in
[the ledger](../../ValidationResults.md). Earlier hidden B1 is excluded.
B2 original16-chunk/v5; C2 dense64-chunk/v6; C4 sparse64-chunk/v7.

| Measurement | Original | Deep dense | Deep sparse |
| --- | ---: | ---: | ---: |
| Average FPS | 821.31 | 695.29 | 886.06 |
| Frame p95 / p99 (ms) | 2.0227 / 3.8664 | 2.2884 / 3.4828 | 1.6072 / 2.9698 |
| Worst frame (ms) | 21.0068 | 41.7518 | 20.8385 |
| GPU p95 / p99 (ms) | 1.6606 / 2.2883 | 1.9255 / 2.5349 | 1.4391 / 1.9660 |
| Worst GPU frame (ms) | 9.4128 | 4.3247 | 10.0412 |
| Peak process bytes | 3959521280 | 3926749184 | 4320104448 |
| Peak GPU bytes | 2878626856 | 3735193448 | 3081002024 |
| Allocated bytes/frame | 29222.229 | 30623.422 | 28860.033 |
| Collision p95 / p99 (ms) | 3184.7117 / 8740.261 | 3279.658 / 8582.035 | 1846.9109 / 2279.0913 |
| Publication p95 / p99 (ms) | 83.7021 / 104.1933 | 155.4021 / 194.4966 | 80.3702 / 108.2534 |
| Maximum placement lag | 4 | 10 | 4 |

C4 vs original: FPS+7.88%, CPU/GPU tails lower, allocations/frame-1.24%,
collision tails lower. Process memory+9.11% (+343.88MiB), GPU memory+7.03%
(+193.0MiB); both FAIL the unchanged5% screen. Peak GPU frame is also higher;
no isolated causal interpretation of a single maximum. Maximum GC pause11.557ms
and synchronous streaming15.2021ms are lower. Stationary991.68FPS,p991.752ms.
No universal speedup claim from one comparison; no overall performance pass.

At equal startup anchors[0,0,0] for all levels, C2 geometry was19,519,992vertices/
104,243,241indices; C4 was10,236,266/52,873,434:47.56% fewer vertices and49.28%
fewer indices. These are measured derived geometry totals, not cave counts.
Final route centers differ for B2/C4; do not use final totals as equal-set proof.

## Decision and retained worlds

Implementation and the reported startup-crash reproducer checks are complete.
HOLD commit/push pending explicit user acceptance of the remaining memory costs
under AGENTS.md. No automatic acceptance of failed memory criteria.
Old world f58322c9b8374c0bb5556199124a290d remains preserved with selector backup
terrain/depth-original-selector.backup. Dense v6 world also preserved; current
selector points to the saved v7 world. No migration or destructive reset.

[Full comparison](c4-comparison.json), [C4 raw](c4.json), [C4 log](c4.log),
[reload evidence](c4-reload.log), [earlier depth-only result](DepthOnly.md).
