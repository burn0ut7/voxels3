# Current cave depth and density result

Generator9 extends caves to32,768units below the local surface (64 base chunks)
and reduces both noodle and cheese-cave coverage again. The existing smooth
regional cutoff rises from0 to0.36; no extra noise call, shader stage, streaming
radius change, or passage-width threshold change. CPU, GPU and conservative
bounds use the same cutoff. The failed0.25 trial is preserved. Old saved worlds
remain on disk; current worlds use the new generator identity.

## Measured additional reduction

At identical startup anchors[0,0,0], same seed/configuration and settled geometry:

| Geometry | Previous C4 | Current C6 | Reduction |
| --- | ---: | ---: | ---: |
| Vertices | 10,236,266 | 5,648,257 | 44.82% |
| Indices | 52,873,434 | 27,609,564 | 47.78% |

This meets the predefined45–55% geometry-reduction target for approximately
halving current coverage. It is a derived geometry measurement, not exact cave
counts or underground void volume. Both cave types share the stricter regional
mask; original passage recipe remains inside surviving regions. More areas
become solid and region borders can terminate passages.

## Visible production validation

CAVE-SPARSITY-002/v1 reuses the fixed CAVE-DEPTH-001/v1 route/configuration.
Fresh unedited world, seed1337,32cells/16units,gameplay8,visual512,levels0..6,
4/8 extents, surface0/0.0005/128, >=30s settled warmup, speed2500,
distance50000, one loop,Z0, normal drain and10s stationary, host/no peers.
Visible coldPID89460, engine26.09.01c, Ryzen7 9800X3D/RTX5090,fps_max1000.
C6run83cd96d488c94d978ebd9b15a91f5eef, [source manifest](c6-source.json).
C4 and B2 ran earlier the same day; this is not isolated causal benchmarking.

| Measurement | Original B2 | Previous C4 | Current C6 |
| --- | ---: | ---: | ---: |
| Average FPS | 821.31 | 886.06 | 880.64 |
| Frame p95 / p99 (ms) | 2.0227 / 3.8664 | 1.6072 / 2.9698 | 1.6397 / 3.1151 |
| Worst frame (ms) | 21.0068 | 20.8385 | 19.0777 |
| GPU p95 / p99 (ms) | 1.6606 / 2.2883 | 1.4391 / 1.9660 | 1.4653 / 1.8637 |
| Worst GPU frame (ms) | 9.4128 | 10.0412 | 10.6838 |
| Peak process bytes | 3959521280 | 4320104448 | 4084461568 |
| Peak GPU bytes | 2878626856 | 3081002024 | 2828295208 |
| Allocation bytes/frame | 29222.229 | 28860.033 | 28950.246 |
| Collision p95 / p99 (ms) | 3184.7117 / 8740.261 | 1846.9109 / 2279.0913 | 1736.8005 / 2153.468 |
| Publication p95 / p99 (ms) | 83.7021 / 104.1933 | 80.3702 / 108.2534 | 74.6114 / 100.7061 |
| Maximum placement lag | 4 | 4 | 4 |

Recorded percentile, allocation, memory and collision screens pass against
both C4 and original B2. C6 process memory is3.16% above original, within5%;
GPU memory is1.75% below original. Previous C4 memory failures remain history,
but do not describe C6. Versus C4, process memory falls5.45%, GPU memory8.20%.
FPS changes-0.61%; no claimed causal speedup. GPU maximum is0.64ms above C4
and1.27ms above B2, while overall frame maximum and GPU tail percentiles improve
against original; retained as an isolated maximum, not a material unexplained
frame-pacing regression. Maximum GC13.168ms and synchronous streaming15.7664ms
are also retained. No passing claim for unmeasured cases.

All4913collision regions ready, meshing/collision queues0, managed exceptions,
collision failures, unsafe commits and transition boundary mismatches0.
Cold load and full route finish without device loss. The crash marker baseline
is21:48:49, following the prior editor shutdown; it does not advance in C6.
The prior crash-producing simplex-mask candidate remains documented in
[the first sparse result](FirstSparse.md). No underlying compiler-cause proof.

Normal save/reload is checked separately below in the ledger. Old original,
v7 and calibration worlds are preserved; the intended current fresh world is
edbe23e645c142cdb9aca329e18b46d2. Deep traversal, exact cave occurrence,
multiplayer and every new-depth collision edge case remain unverified.

[Full comparison](c6-comparison.json), [raw result](c6.json),
[startup calibration](c6-startup.log), [run log](c6.log),
[validation ledger](../../ValidationResults.md).

## Final acceptance

Saved-world reload passed: same world/revision0/checkpoint1,4913ready,queues0,
no new errors or crash marker advance, matching startup/reload geometry.
See [reload log](c6-reload.log). Accept C6 under the recorded checks; prior
C4 memory HOLD is superseded by passing C6 memory screens. The game remains
visible and playable. Exact cave counts and deeper gameplay limits above remain.
