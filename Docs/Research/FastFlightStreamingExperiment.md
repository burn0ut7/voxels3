# Fast-flight streaming experiment

2026-09-14. Two prototypes tested and rejected. All runtime source restored to
the captured starting version; patches and measurements remain as evidence.
The user requested research, implementation exploration and experiments. Current
source is generator 47 with other uncommitted work. Preserve that work and compare
against a captured worktree baseline, not an older Git revision.

## What the sources establish

| Primary source | Documented behavior | Transfer to Voxels3 and limits |
| --- | --- | --- |
| [Voxel Plugin 1.2 priorities](https://docs.voxelplugin.com/1.2/technical-notes/performance-and-profiling) | Task categories precede distance-based priority; priorities refresh as viewers move. Large priority updates can themselves be expensive. | Prioritize publication dependencies ahead of speculative cache work. Historical Unreal implementation; its thread pool is not an s&box API or a measured speed comparison. |
| [Voxel Plugin predictive invoker](https://docs.voxelplugin.com/1.2/core-systems/voxelworld/world-size-and-level-of-details) | A predictive invoker sits ahead of a fast-moving character. | Directional lead is established practice, but lead must cover actual preparation latency. Our existing predictor already implements a bounded lead. |
| [Godot Voxel streaming discussion](https://github.com/Zylann/godot_voxel/issues/107) | The developer discusses computing a target octree and requesting needed blocks in distance/LOD order. Border constraints complicate skipping intermediate levels. | A prepared regular mesh is not sufficient for publication. This is a design discussion, not proof that the proposed algorithm shipped unchanged. |
| [Godot Voxel terrain API](https://voxel-tools.readthedocs.io/en/latest/api/VoxelLodTerrain/) | Clipbox streaming loads concentric LOD boxes; mesh fading handles split/merge presentation. | Keep spatial streaming and visible replacement separate. Its CPU mesher and transition ownership differ from our GPU pipeline. |
| [Veloren chunk generator](https://veloren.gitlab.io/veloren/src/veloren_server/chunk_generator.rs.html) | Pending keys deduplicate requests; atomic flags cancel generation; results for removed requests are discarded. | Preserve useful jobs, cancel obsolete ones, reject late results. This source covers server generation, not smooth GPU LOD seams or velocity scheduling. |
| [Epic World Partition](https://dev.epicgames.com/documentation/en-us/unreal-engine/world-partition-in-unreal-engine) | Streaming sources have priorities and separate loaded/activated target states. A destination source can preload before a teleport. | Readiness and visibility are different contracts; arbitrary teleports need separate treatment. Actor streaming is not procedural SDF meshing. |
| [NVIDIA GPU terrain](https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-1-generating-complex-procedural-terrains-using-gpu) | The GPU terrain example prioritizes blocks and retains empty-block knowledge within bounded geometry storage. | Avoid generating irrelevant geometry and repeating proven-empty work. Historical extraction APIs and performance figures do not transfer. |
| [Hello Games Worlds Part I](https://www.nomanssky.com/worlds-part-i-update/) | Hello Games reports a dual-Marching-Cubes rewrite reducing vertices, generation time and memory. | Extraction changes can matter, but these release notes disclose no scheduler or benchmark details. They do not justify replacing our mesher. |

These are useful production references and public research, not an independently
verified ranking of the fastest engines. The [No Man's Sky GDC talk](https://www.gdcvault.com/play/1024265/Continuous_World_Generation_in__No_Man_s_Sky_)
identifies a relevant end-to-end pipeline; only its public session abstract was
retrieved here, so no detailed scheduling claim is attributed to the video.

## Current evidence and interpretation

The [complete seam-cache experiment](../ValidationResults.md)
added nearby all-face generation. Its fast post-stop near visibility improved
from 1.3663 to 1.0284 seconds, while total drain increased from 32.707 to 54.988
seconds; standard near visibility and allocations regressed. Those runs used
generator 46 and had background GPU contention. They are motivation for an
experiment, not a comparable baseline for the current generator 47 world.

Most recorded local seam misses were outside the speculative package membership.
These are repeated checks, not unique requests. They show incomplete prediction
coverage but do not prove that speculative work caused the wait. Final publication
has a separate barrier. Current source coalesces viewer movement while a layout
is pending; it does not cancel a patch on every base-chunk crossing. Cancellation
occurs at explicit configuration/coverage/return-to-committed or stopped-recovery
conditions. This distinction limits claims about wasted work.

There is another source-level question to measure separately: local candidate
membership and split targets come from the staged layout, while immediate-player
priority uses the current player position. A fast player can leave that staged
fine-detail box while the larger pending layout remains relevant. Cached forecast
geometry alone does not add a current-player refinement target. Changing this
requires explicit ownership for moving local detail, final-layout retirement and
water/seam retention; it is not bundled into the seam-generation experiment.

At 10,000 units/second a 512-unit base chunk is crossed every 51.2 milliseconds.
The predictor's eight-chunk cap provides at most 409.6 milliseconds of forward
lead, despite its nominal 0.75-second horizon. Its refresh interval is 200
milliseconds. These are source-derived limits, not measured deadlines or a proof
that increasing the corridor would help.

## First prototype: demand-driven seam generation

Question: does background all-face generation consume capacity better spent on
the exact seams required by current publication?

Retain the existing bounded regular forecast and the spatial seam-retention set.
Stop proactively scheduling every face in that retention set. Actual local and
final-layout dependencies continue to schedule exact transition descriptors
through the existing mesher. Previously generated nearby seams remain reusable.
No new mesher, fallback geometry, authority, collision policy or publication path
is introduced. Retention is an eviction guard, not a readiness requirement.

VoxelManager owns prediction membership and its cursor. Engine-thread scheduling
continues to capture current regional field identity; GPU lanes, cancellation,
stale-result rejection and exact active-seam checks remain unchanged. Regular
forecast service retains a 0.5 ms soft budget and a maximum of 24 inspections per
frame, matching its former alternating share of 48 regular/seam inspections.
The coarse membership cap is unchanged. The candidate should never interpret
missing speculative faces as missing required coverage.

Alternatives deferred: enlarging the predictor repeats an unsuccessful expansion;
increasing GPU batches has documented native failure history; a new meshing
algorithm has a much larger correctness surface. Preserving useful refinement
across retargets is a plausible follow-up, but requires a topology-lifetime design
and evidence that cancellation is the dominant remaining cost.

## Measurement and decision

Capture baseline source and current saved world before mutation. The old
generator-46 workload cannot be reproduced by the generator-47 implementation;
the user approved a new current-world baseline under the project workload
policy. Retain the canonical fast 10000/200000/one-loop and standard
2500/50000/one-loop routes, normal origin startup, fully settled plus 30-second
warmup, 10-second stationary capture and 240-second cap. Keep effective scene,
world revision, detail, viewport and quality fixed across comparisons.

Record moving FPS/p95/p99/max, allocations, process/GPU memory, regular and seam
work, post-stop near visibility, total drain, preparation time, final queues,
water/collision readiness, coverage and mesh/seam audits. Post-stop arrival does
not establish uninterrupted near-detail coverage throughout fast flight.

Keep the existing near <=5 s (experimental <=0.1 s), drain <=10 s, preparation
<=16.67 ms, zero correctness errors and no unexplained performance-regression
gates. Compare relative changes as well as absolute failures. All-face package
completion describes the former proactive policy; required visible dependencies
must still be complete, while retained optional faces may legitimately be absent.
Do not call that a topology failure or silently treat cache residency as visibility.

If the first fast trial is worse, preserve its evidence and restore the baseline
instead of carrying forward a known regression. A promising fast trial requires
standard-route and repeated comparable checks before acceptance or commit.

## Experiment 1 result and second prototype

The demand-only trial reduced post-stop near visibility from 1.4971 to 1.0431 s
and drain from 14.004 to 11.179 s. Moving FPS fell from 399.75 to 307.18 and p99
rose from 9.8376 to 11.6718 ms. The candidate also had more full collections and
a 596 ms maximum GC pause. Total allocations fell, but collection-history and
diagnostic asymmetries prevent attributing all differences to seam admission.
Both final 88-mesh audits passed. The unexplained pacing regression rejects this
candidate; both production files were restored exactly before experiment 2.

The second prototype changes only local request admission. VoxelManager's
existing local coverage planner receives up to 27 current-player LOD0 requests
when the player enters a different base chunk, even while final layout work is
pending. Keep requests inside the staged maximum-level roots and require enabled
LOD0. A containing coarse region may be refined for current-player detail even
when that coarse region belongs to the older staged final layout.

The planner owns one last-requested center and an initialized flag; beginning
a new local plan clears the flag. Membership for current immediate detail is
computed by the existing predicate, so old requests lose that interest when the
player moves. The existing queue deduplicates keys and admission rejects stale
requests. No accumulating second desired-layout set is introduced. Normal
out-of-range retirement retains current immediate detail; all other retirement
and the final atomic layout publication remain in their existing owners.

The same single refinement operation plans balanced leaves, captures immutable
regional fields and schedules exact regular/seam/water dependencies. New motion
does not mutate an admitted patch's topology or cancel it just to follow a new
center. Additions remain capped at eight, refinement at 4096 leaves, and local
service at its 0.75 ms soft budget. The extra admission scan is bounded at 27
keys per changed base chunk, inside that service scope. Foreground seams,
speculative cache behavior and GPU batch/lane limits return to baseline.

Known risks: more useful near-detail requests may increase total work; an old
final-layout swap can temporarily coarsen newer local detail before the next
layout catches up. The prototype does not claim to eliminate that final-layout
behavior or guarantee continuous LOD0 at arbitrary speed. Actual balance,
coverage and seams must pass moving/final checks; preserve field identity and
the existing collision/water publication contract. Treat failure as evidence
and restore the single coverage file. Reuse LOCAL-COVERAGE-001/v4 unchanged.

## Measured outcome

| Fast route | Near detail after stopping (s) | Total drain (s) | Average FPS | p99 frame (ms) |
| --- | ---: | ---: | ---: | ---: |
| Current loader control | 1.497 | 14.004 | 399.75 | 9.84 |
| Demand-only seams | 1.043 | 11.179 | 307.18 | 11.67 |
| Moving local detail | 1.375 | 34.779 | 226.94 | 18.38 |

Neither prototype passes acceptance. Demand-only seams improve post-stop arrival
but regress pacing. Moving local detail regresses drain/pacing and its moving
audits find one, then two missing active seams. Both candidates' final settled
88-mesh audits pass; that does not erase moving failures. Baseline and both
candidates have zero measured exceptions and unsafe publication counters.

The second candidate's session-scoped topology retries increase from 8 to 1930;
local planner update time increases from about 4.26 to 25.53 seconds. This is
evidence of additional planner work, not a moving-window function-level profile.
The current minimal admission patch is insufficient: current-player refinement,
old-layout final publication and retirement must agree on exact seam lifetime.

The next design question is how to maintain a small coherent current-player
replacement while the large final layout converges, with explicit seam closure
through retirement. Measure that lifecycle and its CPU cost before increasing
prediction distance, cache size, batch size, or replacing the mesher. The present
experiments establish no best-in-class implementation or arbitrary-speed guarantee.

These are single-run experiments in one long-lived editor. Collection histories
differ, moving diagnostics were not captured at identical times, and experiment2
used heavier detailed moving audits. The first control attempt failed to save
because monitoring held the results file; it is excluded and preserved. The
replacement control saved successfully. Performance differences cannot all be
assigned causally to a single code branch. No standard-route/repeat acceptance
runs were taken for the rejected candidates, and multiplayer/teleports remain
unverified.

Raw outputs, isolated patches, source identities and the compact numeric
comparison are in [the evidence directory](../ValidationEvidence/FastFlight/).
The append-only [validation ledger](../ValidationResults.md) retains parameters,
setup corrections, failed captures and decisions. The restored playable source
does not contain either experimental change.

## FPS-preserving follow-up

Two further small experiments retained FPS but worsened chunk arrival; both
were restored. See the [follow-up measurements and archived patches](../ValidationEvidence/FastFlightFps/Experiment.md)
for the fresh control, fixed workload, correctness checks and limitations.
