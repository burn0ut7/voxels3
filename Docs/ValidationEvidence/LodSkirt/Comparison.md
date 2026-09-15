# Skirt prototype v1: before and after

The prototype is implemented but unaccepted. It improved one arrival measurement,
regressed the fast route and frame pacing, and did not meet the 10-second drain
budget. It was initially retained uncommitted for review, then reverted at the
user's request before the predictive-loading experiment. These historical
measurements and screenshots remain preserved.

Both routes use LOCAL-COVERAGE-001/v2, seed1337 / generator46, unedited field,
LOD0..5, viewport1847x959, engine26.09.08b and RTX5090. The correctly timed v26
source baseline matches the preserved pre-skirt source in all 12 manifest files.
Fresh before captures are retained but have 39-43-second warmup deviations;
they are diagnostic only. V1 after runs use automatic 30-second settled warmups.
The editor was cold-restarted for shader validation; process memory is therefore
not a clean paired comparison. Wardogs was concurrently running throughout;
these single runs cannot isolate skirt cost from environmental variation.

| Measurement | Fast v26 | Fast skirts | Standard v26 | Standard skirts |
| --- | ---: | ---: | ---: | ---: |
| Near LOD0 ready, seconds | 0.541 | 0.313 | 0.000 | 0.357 |
| Near LOD0 visible, seconds | 0.541 | 1.387 | 1.302 | 0.472 |
| Ready-to-visible gap, seconds | 0.000 | 1.073 | 1.302 | 0.115 |
| Average FPS | 154.91 | 130.37 | 141.72 | 106.14 |
| Frame p95, ms | 10.28 | 12.19 | 11.19 | 16.93 |
| Frame p99, ms | 15.25 | 17.92 | 17.20 | 20.12 |
| Maximum frame, ms | 109.48 | 89.05 | 93.35 | 83.74 |
| Backlog drain, seconds | 27.95 | 34.91 | 26.75 | 20.02 |
| Maximum preparation, ms | 42.26 | 13.06 | 12.75 | 12.37 |

Arrival values are first observations from the production fine arrival report,
not GPU completion timestamps. Maximum observation gap was 65.5ms fast / 60.2ms
standard; zero means ready at the first observation. Baseline v26 itself was
unaccepted; this comparison is not a new accepted baseline.

V1 emits downward lips in the existing regular count and output stages and skips
separate exact seam geometry only where conservative unedited surface bounds
qualify on both sides. At rest only two transitions used skirts, while 581 regular
regions carried lip capability after fast. Thus this experiment adds boundary
geometry broadly while removing few final exact seam dependencies. That explains
the limited applicability; it does not establish the cause of every timing change.
The standard session recorded 46 skirt substitutions including startup, two
resident substitutions and 581 regular regions with lip capability. All 82 runtime
source/shader files matched the after manifest after both runs; no source drift.

Both final 88-mesh audits passed: no invalid indices, nonfinite or out-of-bounds
positions, identity, degenerate triangle or draw argument errors. Final queues
were empty, collision4913 ready, exceptions0. Moving/final structural coverage
checks had no overlap, balance, missing or extra seam errors. One exact lateral
digest mismatch at LOD1->2 remains; approximate coverage is not exact seam identity.

Post-timing native ejected-camera inspection at x8192 viewed the substituted
LOD2 NegativeX boundary. Close view from (8350,1000,400), angles(40,180,0),
showed continuous checker terrain with no obvious open hole or exposed curtain.
A low oblique view from (8020,1000,210), angles(10,0,0), showed continuous nearby
terrain but distant dark dashed lines; their cause remains unresolved. Both use
60-degree FOV and1847x959 capture, with player streaming origin unchanged.
An earlier elevated water-facing view at (8050,-1000,700) was inconclusive for
underwater coverage. Camera control was restored to the player afterward.
See [close view](boundary-close.png) and [oblique view](boundary-oblique.png).
These views do not qualify caves, edited boundaries, corners or shadow behavior.

Evidence: [fast result](after-v1-fast-result.json),
[standard result](after-v1-standard-result.json),
[fast arrival](after-v1-fast-arrival.json),
[standard arrival](after-v1-standard-arrival.json),
[standard observations](after-v1-standard-observations.json),
[standard audit](after-v1-standard-audit.json),
[source hashes](after-v1-source.json),
[v26 fast](../LodPriority/v26-performance.json),
[v26 standard](../LodPriority/v26-standard-performance.json).

The next design issue is how to cover more actual transition boundaries without
creating visible curtains around caves or edits, while reducing the remaining
publication wait. Increasing lip depth alone is not established as a solution.
