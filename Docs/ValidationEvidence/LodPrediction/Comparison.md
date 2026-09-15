# Predictive loading: measured prototype

Skirt runtime changes are reverted. The current v2 prototype predicts observed
movement up to0.75seconds ahead, capped at eight base chunks, and preloads LOD0,
LOD1, water and exact LOD0/1 layout seams through existing production pipelines.
It preserves current render coverage and clears speculative interest after stopping.

The result is promising for normal-speed arrival, but unaccepted overall.

| Measurement | Exact v26 fast | Prediction fast | Exact v26 standard | Prediction standard |
| --- | ---: | ---: | ---: | ---: |
| Near27 ready, seconds | 0.541 | 0.458 | 0.000 | 0.022 |
| Near27 first visible, seconds | 0.541 | 1.366 | 1.302 | 0.022 |
| Ready-to-visible gap, seconds | 0.000 | 0.908 | 1.302 | 0.000 |
| Average FPS | 154.91 | 137.62 | 141.72 | 106.21 |
| Frame p95, ms | 10.28 | 12.22 | 11.19 | 17.72 |
| Frame p99, ms | 15.25 | 16.02 | 17.20 | 20.09 |
| Maximum frame, ms | 109.48 | 105.12 | 93.35 | 81.59 |
| Backlog drain, seconds | 27.95 | 32.71 | 26.75 | 20.45 |
| Maximum placement preparation, ms | 42.26 | 11.92 | 12.75 | 12.72 |

These are single runs of unchanged LOCAL-COVERAGE-001/v2, not repeated averages.
The earlier v26 comparison uses correctly timed30-second warmups; fresh skirt-before
runs with warmup deviations are not substituted. The editor was cold-restarted
for rollback verification and Wardogs remained running concurrently, so process
memory and GPU contention confound causal attribution. V26 itself was unaccepted.
Both prediction runs used exact automatic30-second settled warmups and30-second
moving checks, original1847x959 viewport, seed1337/gen46/field0 and route inputs.

Arrival is the first production near-only observation after the last significant
movement, not an exact GPU timestamp or continuous-route coverage guarantee.
Fast observation gap62.1ms; standard first observation21.8ms. The normal-speed
near27 remained presented in all ten subsequently saved stationary samples from
1.692 through10.754seconds. Fine observation stops after the first success, so
there is no per-frame continuity proof between those observations.

The fast report shows why preloading was insufficient: at1.084seconds, terrain
and water were ready for all27 near chunks, but all27 were publication-blocked.
Its local refinement lacked22seams; the forecast's future layout seams did not
cover every required local refinement seam. Predicting the complete refinement
dependencies is the next targeted design question, not simply adding more chunks.

Prediction counters are cumulative per Play session, including startup and
known-empty publications; they are not extra GPU jobs or avoided visible stalls:

| Counter | Fast | Standard |
| --- | ---: | ---: |
| Regular requests/publications | 36304 | 6139 |
| Exact seam requests | 49669 | 21794 |
| Forecast entries reaching normal warm interest | 16186 | 0 |
| Those entries already resident | 6535 | 0 |
| Maximum prediction update, ms, including startup | 7.8141 | 4.3285 |
| Final forecast regular/seam interest | 0/0 | 0/0 |

At normal speed the forecast fits within the existing warm area; zero entry
counter does not mean no useful prioritization or seam preparation. CPU service
uses a0.5ms soft limit and12inspection cap; building sets and a single expensive
operation can exceed that limit, as these maxima demonstrate.

Both final88-mesh audits passed without invalid indices, nonfinite/out-of-bounds
positions, identity, degenerate triangle or draw argument failures. All exact
fine/coarse/lateral/table mismatch counts were zero. Moving/final structural
coverage audits had no overlap, imbalance, missing or extra seams. Final queues
were empty, collision4913ready, water ready, exceptions0. The stationary player
view showed continuous nearby terrain ([capture](final-view.png)); abrupt reversals,
teleports, live edited worlds and multiplayer load remain unqualified.

V1's first moving run failed because it passed LOD0 to the coarse classifier;
it was stopped, recorded, and fixed before the v2 runs. The shutdown Error and
partial-integration compile failure are preserved in rollback-shutdown.log.

Decision: normal-speed first arrival meets the experimental<=0.1second target;
fast arrival fails it, both drains fail10seconds, and pacing is materially worse
than the earlier baseline. Keep v2 as an unaccepted working-tree prototype,
without commit/push or a production speed claim. No source drift: all82manifest
files matched after measurement. Three restored shader sources and the SDF
descriptor exactly match their pre-skirt copies.

Evidence: [fast result](v2-fast-result.json), [standard result](v2-standard-result.json),
[fast arrival](v2-fast-arrival.json), [standard arrival](v2-standard-arrival.json),
[fast observations](v2-fast-observations.json), [standard observations](v2-standard-observations.json),
[fast audit](v2-fast-audit.json), [standard audit](v2-standard-audit.json),
[source](v2-source.json), [failed v1](v1-failure.json),
[v26 fast](../LodPriority/v26-performance.json),
[v26 standard](../LodPriority/v26-standard-performance.json).
