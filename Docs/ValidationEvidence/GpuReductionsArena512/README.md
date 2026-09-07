# GPU reductions and arena evidence

Fixed workload and decisions: [validation ledger](../../ValidationResults.md#gpu-meshing-512-001v1---gpu-simplification-investigation). See the [research report](../../Research/GpuReductionsAndArenaEfficiency.md) for interpretation. These are exports of the production Figure Eight, not separate tests.

| Run / revision | Moving CPU p95 / p99 ms | Moving GPU p95 / p99 ms | Peak / final arenas | Peak / final capacity MiB |
| --- | ---: | ---: | ---: | ---: |
| [50961ab6](50961ab6f6954b1a9154cb2129a5a31e.json) aab4bc8-reductions-arena-baseline | 1.3764 / 2.7697 | 1.2298 / 1.6403 | 15 / 14 | 720 / 672 |
| [61332885](613328854f374cc4960f31ce9be85850.json) aab4bc8-arena-instrument-control | 1.3625 / 2.6727 | 1.2751 / 1.6954 | 15 / 14 | 720 / 672 |
| [0e70e19a](0e70e19a68c0415e80a8099667a44e26.json) aab4bc8-arena-instrument-repeat | 1.3551 / 2.6219 | 1.2848 / 1.7047 | 15 / 14 | 720 / 672 |
| [08a4d0a8](08a4d0a8358548bdb61d1ad1bafcec2a.json) aab4bc8-arena-lean-control | 1.4048 / 2.5670 | 1.3411 / 1.7905 | 15 / 14 | 720 / 672 |
| [194a4c17](194a4c1736eb403c92849f2d39f52529.json) aab4bc8-s1-cooperative-scan | 1.4776 / 2.8620 | 1.3404 / 1.8172 | 15 / 14 | 720 / 672 |
| [e2aba980](e2aba9806419460298d16b92ec59c264.json) aab4bc8-m1-arena24 | 1.4653 / 2.7752 | 1.3497 / 1.8146 | 15 / 14 | 600 / 560 |
| [72c9fd96](72c9fd961b124d26a5449c6b61eef9d2.json) aab4bc8-m1-arena24-cold | 1.0662 / 1.7362 | 0.8676 / 1.1404 | 15 / 14 | 600 / 560 |

## Comparisons

| Comparison | Baseline | Failed / checked gates |
| --- | --- | ---: |
| [08a4d0a8358548bdb61d1ad1bafcec2a-fresh-comparison](08a4d0a8358548bdb61d1ad1bafcec2a-fresh-comparison.json) | 50961ab6 | 4 / 206 |
| [0e70e19a68c0415e80a8099667a44e26-fresh-comparison](0e70e19a68c0415e80a8099667a44e26-fresh-comparison.json) | 50961ab6 | 4 / 206 |
| [194a4c1736eb403c92849f2d39f52529-comparison](194a4c1736eb403c92849f2d39f52529-comparison.json) | 08a4d0a8 | 6 / 210 |
| [194a4c1736eb403c92849f2d39f52529-fresh-comparison](194a4c1736eb403c92849f2d39f52529-fresh-comparison.json) | 50961ab6 | 4 / 208 |
| [50961ab6f6954b1a9154cb2129a5a31e-accepted-comparison](50961ab6f6954b1a9154cb2129a5a31e-accepted-comparison.json) | b80cd7b7 | 18 / 203 |
| [613328854f374cc4960f31ce9be85850-comparison](613328854f374cc4960f31ce9be85850-comparison.json) | 50961ab6 | 1 / 203 |
| [613328854f374cc4960f31ce9be85850-fresh-comparison](613328854f374cc4960f31ce9be85850-fresh-comparison.json) | 50961ab6 | 1 / 203 |
| [72c9fd961b124d26a5449c6b61eef9d2-accepted-comparison](72c9fd961b124d26a5449c6b61eef9d2-accepted-comparison.json) | b80cd7b7 | 0 / 206 |
| [72c9fd961b124d26a5449c6b61eef9d2-comparison](72c9fd961b124d26a5449c6b61eef9d2-comparison.json) | 08a4d0a8 | 0 / 210 |
| [72c9fd961b124d26a5449c6b61eef9d2-fresh-comparison](72c9fd961b124d26a5449c6b61eef9d2-fresh-comparison.json) | 50961ab6 | 0 / 206 |
| [e2aba9806419460298d16b92ec59c264-comparison](e2aba9806419460298d16b92ec59c264-comparison.json) | 08a4d0a8 | 0 / 210 |
| [e2aba9806419460298d16b92ec59c264-fresh-comparison](e2aba9806419460298d16b92ec59c264-fresh-comparison.json) | 50961ab6 | 1 / 206 |

Frame tolerance: +max(5%, 0.25 ms); publication/drain: +max(5%, 10 ms); allocation: +5%. Absolute budgets, geometry equality and zero error/pending/truncation gates remain fixed. Added observations check peak arenas and maximum submissions. Reduced committed bytes are allowed; geometry equality remains exact. S1 requires 5% lower moving GPU p95 and p99; M1 requires 10% lower peak and settled arena capacity. Different numbers of checks reflect schema availability and hypothesis-specific benefit gates, not relaxed thresholds.

Each `-correctness.json` preserves the outcome of 6,203 additional recorded-data checks, including every transition face matched by spatial identity. Allocator offsets/generation and timing are excluded from geometry equality. Direct emitted-buffer observations are separate; known transition degenerates are not hidden.

All raw production values are preserved, including failed runs. Capacity is project buffer sizing, not driver/process GPU usage. Schema 24 peak arena count is inferred from maximum buffer groups; schema 25 records it directly. Early schema 25 probes include growth rejection fields removed from the final lean reporting implementation.

Source evidence: [S1 patch](S1-prototype.patch), [S1 hashes](S1-source.json), [M1 hashes](M1-source.json). The S1 patch is research evidence only, not an alternate shipping implementation.
