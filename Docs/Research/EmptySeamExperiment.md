# Empty seam experiment

## Acceptance decision

2026-09-09: the user explicitly accepted candidate B after reviewing its measured
startup frame-rate tradeoff. Candidate B is restored: skip strictly uniform
unedited seams, and avoid empty-to-empty draw-command invalidation. Prior rejection
results below are retained as history. Acceptance covers the reported startup
tradeoff; moving behavior and integration are being verified with the unchanged
benchmark. No return correction or unrelated terrain change is authorized.


Initial screening: rejected; pre-experiment source was restored before the
subsequent user acceptance recorded above.

## Result

The prototype removed1,304 of2,064 seam count readbacks at the fixed spawn,
with matching terrain and seam fingerprints. It shortened loading, but did not
preserve the startup frame window. It was initially rejected under the frame-performance screen. The user then
explicitly accepted candidate B's measured tradeoff; B is now retained.

| Version | Full load | First reported10-second FPS | Frame p99 |
| --- | ---: | ---: | ---: |
| A: skip strictly uniform unedited seams | 15.641s | 465.2 | 8.65ms |
| B: also avoid empty-to-empty draw-command invalidation | 15.609s | 481.7 | 5.62ms |
| Exact previous code, consecutive source-control run | 20.328s | 609.4 | 4.75ms |

B shortened loading23.2% against the fresh control, but its average FPS was21.0%
lower and frame p99 was18.3% higher. The earlier retained regular-classifier
baseline19.797s /588.4FPS /5.10ms also fails to support accepting B.
These are individual startup observations, not confidence intervals. Repeats and
moving tests stopped when the declared frame criterion failed. Do not treat the
shorter startup as an accepted optimization or claim a measured streaming gain.

## What was tested

The normal transition queue evaluated the existing canonical density interval
for the entire descriptor SamplingBounds, including normal halos. Only strictly
positive or strictly negative unedited regions could bypass GPU work. Zero and
uncertain intervals and edited regions retained the normal GPU path.

A proven empty region used the existing CandidateTransition and next-render
finalization, retaining cancellation, desired-descriptor identity and edit
publication checks. No new mesher, cache, shader, batch size, memory capacity or
view distance. Uniform signs correspond to shader cases0/511, with zero geometry,
digests and audit counters. CPU bounding cost was about13ms summed in A; removing
GPU jobs does not by itself guarantee better frame pacing when remaining work
becomes eligible earlier. The exact cause of the FPS regression was not isolated.

B tested a concrete redundant operation: empty-to-empty publication marked draw
commands dirty despite changing no drawable geometry. Avoiding it improved the
observed p99 versus A but did not meet the baseline FPS criterion. Both changes
were removed during screening, then B was restored on acceptance. This report does not establish that draw invalidation alone caused
the regression or that an independent change would be beneficial.

## Correctness and restoration

Regular fingerprints matched473FFDE4AD1E3FE1 /92FAEEE7BEE60656. A seam check
matched5E38770EE4D1AC04 /99BBF279842ED4F6 with zero fine/coarse/lateral mismatches
and invalid tables. Final restored-world seam fingerprints also match. Every
completed startup had4,913 collision regions ready, zero pending/failures and all
visual queues drained. This is bounded spawn evidence, not exhaustive edited
seam or multiplayer qualification. The geometry audit and figure-eight were not
run on the rejected candidate. The previously retained regular-chunk classifier
improvement and dirty-page persistence remain unchanged.

During rejection, GpuVoxelMesher.cs and VoxelManager.cs were restored byte-for-byte
to their pre-experiment snapshots. Candidate B has since been reinstated as the
normal path, without a prototype toggle.
Source hashes and metrics are in [the summary](../ValidationEvidence/Water/empty-seam-summary.json).
[Historical prototype design](../Plans/EmptySeamPrototype.md).

## Timing limits and next useful measurement

Times begin after Play returns and end after all surrounding queues drain;
these are not per-chunk calculation times, first-visible times or physically
cold disk tests. The reported FPS/p99 windows describe the initial10seconds,
which are not complete-load frame distributions. All cases used the same saved
revision64, recipe, radius, view and polling workload in the same editor.

Before another scheduling change, measure regular count, refinement and emission
GPU execution alongside queue/readback latency during this early window. Current
CPU-submission and callback stopwatches cannot distinguish kernel execution from
waiting. That evidence is needed to target wasted work without merely moving more
expensive work into the initial frame window. No further speedup is claimed.
