# Empty seam job prototype

2026-09-09. **Rejected; runtime changes fully removed.**
See [measured results](../Research/EmptySeamExperiment.md). The following records
the historical prototype design, not retained behavior.

Extend the retained full-bound coarse classifier experiment by
avoiding provably uniform unedited transition jobs. This is not a terrain change,
disk cache, new mesher, altered view radius or larger GPU batch.

## Contract

Inputs are the existing transition descriptor, full SamplingBounds (face plus
coarse-cell halo), generator settings, epoch and edit revision. Use the canonical
full density interval, requiring minimum strictly positive or maximum strictly
negative. Never infer emptiness from corner samples. Edited regions retain the
GPU path; uncertain and zero-crossing intervals retain the GPU path.

GpuVoxelMesher owns the decision in its existing transition queue consumption.
A proven empty job produces a zero-geometry CandidateTransition in the existing
idle lane's EmitInFlight list, with the existing generation and descriptor.
Normal next-render finalization checks desired identity, cancellation and edit
publication dependencies before publishing. No direct residency shortcut or
second commit path. No GPU allocation, dispatch or readback for proven empty jobs.
The lane stays occupied until normal finalization, preserving lifetime ordering.

At most one request is processed under the current transition batch1 limit.
Only take the shortcut with no earlier batch entries or deferred reads, so it
remains correct if batching evolves. Bounds run on the render scheduling thread;
record total bound time and skipped-job count to expose the CPU tradeoff.
No additional scheduler concurrency, GPU buffers or shader bindings.

Uniform strict signs produce shader cases0 or511, which return before topology
and edge work; all count/digest/audit fields remain zero. Zero-result generation
metadata still identifies the job. Full halo bounds include the existing fine
and coarse normal samples. Do not skip edited descriptors in this first slice.

## Validation

Reuse the fixed startup and unchanged canonical figure-eight scenarios in the
validation ledger. Startup uses scene basic_example, saved revision64, seed1337,
Land.75 Mountains.35 Plains.6, scales131072/32768/8192, Relief3072 Ruggedness.45
Sea0, gameplay8,visual512,LOD0..6,extents4/8,32cells16units,spawnXY0. Poll0.5s
with60s cap, timed from Play return. Visible editor37320,engine26.09.08.
Baseline retained C startup median19.797s; moving result0356a14228484cf6be5e734a1b2cc985.
Spawn regular fingerprints473FFDE4AD1E3FE1/92FAEEE7BEE60656 and transition
5E38770EE4D1AC04/99BBF279842ED4F6 must match; all mismatch/table counters0.
Three startup runs if no failure; require4913collisionready, all queues0 and
zero failures. Seek >=10% loading improvement without >10% FPS/p99 regression.
Record readbacks, skipped jobs, CPU bound time, memory and correctness. Perform
the existing playable geometry audit after timing, not during measurement.
Then compare moving FPS/tails, outer readiness/queues and seam correctness using
the unchanged figure-eight speed2500,distance50000,one loop,Z0, same revision64
and recipe. No return correction. Retain known stationary/collision qualification
limits. Reject on geometry difference, native fault or material frame regression.


## Rejection

A loaded15.641s but worsened the first10-second FPS/p99 window. B additionally
avoided draw-command invalidation for empty-to-empty transition publication;
it loaded15.609s but still worsened FPS. Fresh exact pre-seam source control
loaded20.328s with609.4FPS,p99 4.75ms, versus B481.7FPS,p99 5.62ms. Neither meets
the frame criterion. Repeat series and moving tests were stopped at rejection;
no performance thresholds, route or features were relaxed to obtain acceptance.
Previous full-bound regular-chunk improvement remains. No seam skip is retained.
