# River completion audit

Current implementation: generator39, river10, water7. The table below records the
generator37 foundation checks; river9 width refinement is tracked in RIVERS-010/v1
and width-survey-comparison.json. River10 frequency reduction is tracked in
RIVERS-011/v1. This audit is not overall acceptance.

| Requirement | Current evidence | Status |
| --- | --- | --- |
| Long connected rivers, tributaries and deltas; no isolated dots or short independent courses | RiverDrainageBasin and RiverNetwork; accepted flat-basin survey has 334 joins, 18 splits, no cycles or zero-length sections, longest complete course 181820.616 units | Verified for the recorded seed and survey |
| Varying width and course shapes | Seeded endpoint widths, curved sections and interior width modulation; approved generator35 geometry preserved in bank/spatial/wet-prune comparisons | Verified within recorded comparison |
| Terrain carved before meshing; water at shared sea level | Canonical RegionalLandforms composition, CPU/GPU density audits, all surveyed endpoints at SeaLevel0 | Verified for current fixed recipe; latest user direction supersedes elevated river ribbons |
| Blend neighboring terrain | Wider height-dependent valley shoulders; fixed bank images and height comparison | Visually verified in recorded hillside view |
| Preserve the branching method in a note | [Connected drainage basins](../../Plans/RiverDrainageBasins.md) documents the exact recipe and bounded-basin limits | Complete |
| Collision and render agreement | Seven regular and 26 transition density cases, max difference0.01171875 with zero explicit failures; 26 production mesh audit selections with zero failures | Verified within audit coverage |
| Solid edits remain visible; save/load retains field | RIVERS-008 before/build/restore column samples and native images | Passed; original field restored after validation |
| Host/client agreement | RIVERS-009 initial join, live edit and reconnect matching world/revision/hash | Passed for one visible localhost guest; no loss/latency injection claimed |
| Loading and frame-rate repair | Early water publication; unchanged-source repeat has all4913collision ready and queues0,117.12016moving/188.1189stationary FPS; moving allocations reduced72.248% against bootstrap | Repeat passes recorded thresholds; prior isolated23.2249ms stationary maximum retained and not reproduced (repeat8.7451ms) |
| Preserve other tasks and commit/push only river changes | RegionalLandforms, SDF and material files contain intertwined uncommitted erosion/cliff/stone/snow changes | Separation and commit remain incomplete |

Primary evidence and immutable scenario parameters remain in [ValidationResults](../../ValidationResults.md). River basin boundaries are drainage divides; submerged seeds may be inland lakes. Static water does not claim global ocean routing or fluid simulation.
