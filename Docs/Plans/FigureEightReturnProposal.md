# Figure-eight return correction (not applied)

2026-09-09: the user explicitly chose to keep the existing benchmark unchanged.
This proposal remains historical and is not authorized for implementation.

The existing benchmark ends its forced Z=0 route inside the new terrain and
releases the player there. Falling then changes the stationary workload and
prevents a valid grounded final state. Prior failed runs remain in the ledger.

The proposed patch records the player position at benchmark start and restores
it only after the moving frame/throughput measurement has ended. It clears body
velocity, then uses the existing terrain/collision drain and stationary phase.
Speed2500, distance50000, one loop, Z0 route, seed/settings, radii, warmup,
measurement boundaries and acceptance thresholds remain unchanged.

This changes post-route placement, so a new scenario version and baseline are
required. Report the old/new generator moving workloads with their content and
engine differences; never join stationary results across versions as one
continuous comparison. Require grounded support and all queues settled before
and after the stationary window. The patch does not establish performance
acceptance or ordinary surface traversal of the forced underground route.

[Review the proposed source patch](FigureEightReturnProposal.patch).
It has not been applied. User approval is required by the plan's fixed-scenario
rule before changing this post-route workload. After approval, record the full
versioned scenario before running it through the existing playable benchmark.
