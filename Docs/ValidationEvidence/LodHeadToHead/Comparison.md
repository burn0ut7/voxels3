# Current versus lighter prediction: fresh benchmark

Status: three matched runs completed. The lighter prediction standard run has
not run: its setup moved outside the fixed origin, then Play was stopped.
Testing is paused pending clarification about active play input. Original current
source is restored; 82/82 hashes match. No overall winner is established.

Fast comparison: current 212.9ms versus prediction 190.0ms first visibility;
backlog drain 13.7s versus 19.9s. The 23ms arrival difference is smaller than the
roughly 50ms maximum observation intervals, and one pair is not decisive.
Both fail the 100ms target and the 10s drain gate. All three completed matched
runs passed the 88-mesh audit with zero exact seam/structural/unsafe-commit errors,
zero exceptions, empty final queues, water ready and collision4913 ready.

Timing deviations are retained separately: [current fast r1](current-fast-r1-summary.json)
captured its moving check at40s instead of30s and was excluded/repeated before
comparing results. [Interrupted candidate setup](prediction-standard-interrupted-setup.json)
never triggered a benchmark. Do not substitute historical standard results for
the missing fresh run.

3 completed matched runs of unchanged LOCAL-COVERAGE-001/v2. Current means the seam-cache-v2 workspace at request time. The comparison candidate is prediction-v2, selected before testing because it had the best previous standard-speed arrival (21.8ms), not because it was an accepted overall winner. No new algorithm was introduced.

## Arrival and streaming

| Version / route | Near27 ready (s) | Near27 visible (s) | Ready-to-visible (s) | Drain (s) | Max preparation (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| [current / fast](current-fast-r2-summary.json) | 0.0492 | 0.2129 | 0.1637 | 13.716 | 11.136 |
| [current / standard](current-standard-r1-summary.json) | 0.0458 | 0.1010 | 0.0552 | 13.999 | 11.275 |
| [prediction / fast](prediction-fast-r1-summary.json) | 0.0360 | 0.1900 | 0.1540 | 19.897 | 10.413 |

Near27 times use firstPreparedObservedSeconds and firstPresentedObservedSeconds from the fine production observations after movement stops. They are not the older one-second snapshot fields or a continuous-route visibility guarantee. Zero means ready at the initial observation.

## Frame pacing and memory

| Version / route | FPS | p95 / p99 / max frame (ms) | Max GC (ms) | Allocated (MiB) | Process avg / peak (MiB) | GPU avg / peak (MiB) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| current / fast | 344.02 | 5.565 / 8.378 / 61.354 | 15.653 | 5956.76 | 2534.51 / 2660.35 | 1748.92 / 1813.45 |
| current / standard | 398.66 | 4.358 / 6.542 / 93.423 | 15.075 | 4214.14 | 2573.22 / 2650.27 | 1797.00 / 1893.31 |
| prediction / fast | 358.17 | 5.717 / 8.254 / 68.988 | 15.503 | 5865.16 | 2881.36 / 2974.64 | 1722.00 / 1764.73 |

## Meshing work

These native aggregate counters are preserved for mechanism inspection. Summed callback/readback durations can overlap and must not be treated as wall-clock delay. They do not count only newly requested near-LOD0 faces.

| Version / route | Dispatches | Regular count regions | Transition count readbacks | Cancelled transition count results |
| --- | ---: | ---: | ---: | ---: |
| current / fast | 63166 | 79903 | 5134 | 58 |
| current / standard | 89123 | 93445 | 6130 | 21 |
| prediction / fast | 69340 | 87127 | 4041 | 122 |

## Fixed protocol

Order was declared before the first run: current fast, current standard, prediction fast, prediction standard. Each run restarts normal visible Play, waits for settled terrain/water/collision (all cache packages where present), then uses an automatic 30-second warmup. The 30-second moving checkpoint captures coverage and collision state. The existing native benchmark captures 10 seconds after draining. Each final audit selects the nearest eight meshes per level, 88 total.

Fast: speed 10000, distance 200000, one loop. Standard: speed 2500, distance 50000, one loop. Scenario LOCAL-COVERAGE-001/v2: basic_example, seed1337/gen46/field0, LOD0–5, cells32/base16, near4/cache8/gameplay8, viewport1847×959. Land/mountain/plains .75/.3/.6; scales77724.09/18681.756/5232.39, relief3072, ruggedness.45, sea0. Natural settled player positions remain recorded in raw reports; no target repositioning was introduced. Route cap240s.

Same RTX5090, driver616.64, engine26.09.08b and long-lived editor PID10584. Previous Wardogs PID10580 was absent before these runs; the agent did not terminate it. Desktop GPU processes remained. Current source runs precede candidate runs, so order, process history and background load remain limitations. These four single runs cannot establish statistical significance. Prior contended runs are linked as historical context, not pooled into fresh averages.

Gates remain near27 first visible≤0.1s experimental (≤5s canonical), drain≤10s, preparation≤16.67ms, no unexplained material pacing/allocation/memory regression, zero exceptions/unsafe commits and exact seam/geometry/coverage errors, final queues0/waterready/collision4913. Cache package completeness and ≤192MiB transition payload apply where those diagnostics exist.

## Evidence

[Current source](current-source.json), [candidate source](prediction-source.json), [exact-number catalog](catalog.json), [previous all-face experiment](../LodSeamCache/PerformanceCatalog.md), [previous prediction experiment](../LodPrediction/Comparison.md), [validation ledger](../../ValidationResults.md). Matching files for every row use suffixes `-result.json`, `-arrival.json`, `-observations.json` and `-audit.json`.
