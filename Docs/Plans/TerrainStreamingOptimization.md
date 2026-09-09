# Terrain loading and streaming experiments

2026-09-09. Status: C retained as prototype; moving and startup gains measured,
stationary/collision qualification incomplete. [Results](../Research/TerrainStreamingOptimizationResults.md).

User requests measured startup and moving-streaming improvements,
prioritizing frame pacing. Generated-density disk caching remains rejected.

## Scope and ownership

Keep one canonical terrain field, dirty-page persistence and mesher. Compare
bounded changes through existing startup and figure-eight entry points. Preserve
batch sizes, shader layouts, geometry and player settings. Runtime experiments
are candidates until correctness and comparable frame/streaming results pass.

A: shorten outer forced-service gap250ms to16ms. Rejected after two startup runs:
only about5% faster with worse frame windows. Original interval restored.

B: submit independent outer count work on a transition-service tick. Editor exited
during the first startup; cause not isolated. Rejected; baseline restored.

C: use the existing complete authoritative density bound for coarse region
classification instead of the global vertical-support-only broad phase. The
canonical classifier already accounts for regional height, caves and corrections;
its definite result can avoid scheduling an empty mesh. This reuses the current
empty-region publication path, not a new mesher or a cache. The potential cost is
more CPU classification during clipbox changes. Test unchanged geometry, bounds
cost, startup and moving frame tails. Do not touch transition scheduling/shaders.

The user explicitly chose to keep the figure-eight unchanged, including its
known underground end position. Do not apply the return proposal. Preserve its
existing moving and stationary output, and report grounding/settling limitations
without accepting them as normal surface traversal. No new test components or
alternate workload are introduced.

## Validation

Use SCHEDULER-START-001/v1 and existing GENERATED-CACHE-001/v1 startup parameters
from the validation ledger. Compare a fresh baseline in the same editor after
recovery. Three startup observations per viable candidate; stop on rejection or
fault, recording why repeat count changed. Require matching topology/positions,
recipe and world revision, ready collision4913/failures0 and complete drain.
Screen for at least10% loading gain without greater than10% deterioration in
comparable FPS/p99 windows. Those windows alone do not qualify travel behavior.

For travel, run the existing canonical figure-eight with speed2500,distance50000,
one loop,Z0, gameplay8,visual512,LOD0..6, extents4/8,32cells16units. Begin from a
fresh settled world at the same spawn, same saved revision64/recipe. Capture
moving throughput, scheduling latency, queues, frame distributions, memory,
allocations and correctness using the existing result. Keep normal drain and10s
stationary phase; do not change completion behavior. Observe at10s cadence with
360s external cap. Store source identities and raw output alongside the ledger.

No commit or push of a candidate that introduces an unexplained performance or
correctness regression. Outstanding unrelated terrain qualification is distinct
from improvement demonstrated by these experiments.
