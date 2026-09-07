# GPU meshing Figure Eight evidence

Scenario and acceptance decisions: [validation ledger](../../ValidationResults.md#gpu-meshing-512-001v1---gpu-simplification-investigation). Raw files are exports of production results, not separate tests. The original baseline remains the comparison target; post-restart controls are additional attribution evidence.

| Revision | Raw result | Moving CPU p95 / p99 ms | Moving GPU p95 / p99 ms | Outer p99 ms | Slots / enabled | Summary failures |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| bf02740-schema23 | [19a2ebfe](19a2ebfe8de946cba93cfe40f74d0dba.json) | 1.4416 / 2.8390 | 1.2844 / 1.7097 | 242.1118 | unavailable / unavailable | [0](19a2ebfe8de946cba93cfe40f74d0dba-comparison.json) |
| bf02740-schema24-control | [2c021c9b](2c021c9bf6264ddc808701502e18d5d0.json) | 1.2904 / 2.7112 | 1.2274 / 1.6270 | 210.5632 | 152211 / 78543 | [0](2c021c9bf6264ddc808701502e18d5d0-comparison.json) |
| bf02740-schema24-clear-removal | [704feb0e](704feb0eebca4a7abdac96ff86d6e7c9.json) | 1.4993 / 3.1311 | 1.2240 / 1.8454 | 225.3238 | 153146 / 78575 | [5](704feb0eebca4a7abdac96ff86d6e7c9-comparison.json) |
| bf02740-schema24-clear-removal-repeat | [a5a69560](a5a695601dfc407c9ec40e3a5f504292.json) | 1.5118 / 3.1021 | 1.3545 / 1.8234 | 257.7233 | 152799 / 78525 | [6](a5a695601dfc407c9ec40e3a5f504292-comparison.json) |
| bf02740-schema24-compact-emission | [332a05fc](332a05fcc39e41a7ac23e5e05a2009b1.json) | 1.4406 / 2.9841 | 1.2240 / 1.7035 | 256.5200 | 78538 / 78538 | [1](332a05fcc39e41a7ac23e5e05a2009b1-comparison.json) |
| bf02740-schema24-compact-emission-cold | [9bf46670](9bf466704be040ca89cf1dc42ee282c7.json) | 1.1234 / 1.8356 | 0.9067 / 1.2612 | 201.6925 | 78558 / 78558 | [0](9bf466704be040ca89cf1dc42ee282c7-comparison.json) |
| bf02740-schema24-restored-control | [b80cd7b7](b80cd7b7de3947c79c45d436b45b996d.json) | 1.1610 / 1.9171 | 0.9096 / 1.2650 | 203.2652 | 152267 / 78556 | [0](b80cd7b7de3947c79c45d436b45b996d-comparison.json) |
| bf02740-schema24-compact-final | [ad3738d8](ad3738d8704a485e8a57b0bdcb54cad1.json) | 1.1538 / 1.8836 | 0.9005 / 1.2558 | 203.1787 | 78545 / 78545 | [0](ad3738d8704a485e8a57b0bdcb54cad1-comparison.json) |

Each `-comparison.json` records 203 scalar summary comparisons. Each `-correctness.json` records 6203 additional checks and any failures: pending/error/truncation counts, absolute publication budgets, final level counts and digests, and transition-face geometry matched by spatial identity. Face generation, arena/slot offsets and timings are deliberately excluded from geometry equality. These checks do not replace direct emitted-buffer audits.

Relative frame tolerance: +max(5%, 0.25 ms); publication/drain tolerance: +max(5%, 10 ms). Allocation tolerance: +5%. Absolute budgets and memory interpretation remain those declared in the ledger. Whole-editor memory and GPU timing lack kernel attribution.

Emitted-buffer observation: [candidate B nearest-region audit](332a05fcc39e41a7ac23e5e05a2009b1-audit.md). Known transition degenerates are preserved separately from other geometry failures.

The final compact candidate also [failed its predeclared fresh-control comparison](ad3738d8704a485e8a57b0bdcb54cad1-fresh-control-comparison.json), despite passing the original-baseline comparisons shown above. Both optimization prototypes were removed. [Retained source identity](retained-source.json) records the reporting and dead-source cleanup state validated by the restored control.
