# Terrain startup timing investigation

Date: 2026-09-09. Read-only runtime investigation; no optimization implemented.

## Finding

The strongest measured optimization opportunity is GPU work scheduling and the
LOD transition pipeline. Distant regular terrain makes little progress while
transition seams are active, then drains roughly 32 times faster afterward.
This is a queue-throughput comparison, not a predicted 32-fold startup speedup.

The generated-density disk cache remains removed. Dirty-page persistence and
resident geometry reuse remain unchanged.

## Measured milestones

Same GENERATED-CACHE-001/v1 scenario, unchanged saved world revision64 and
recipe, visible editor31784 / engine26.09.08, grounded spawn, host without peers.
The removal check and one fresh repeat both matched the original geometry
fingerprints, with 4913 collision regions ready and no collision failures.

| Milestone after Play returns | Removal check | Repeat |
| --- | ---: | ---: |
| All collision regions ready | 8.188 s | 8.187 s |
| All transition seams ready | 18.407 s | 18.375 s |
| All visual work and placement complete | 25.938 s | 25.797 s |

These are overlapping completion milestones, not durations to add together.
Requested polling is 0.5 seconds; actual samples are approximately 0.7 seconds
apart. They measure surrounding-world completion, not first playable time or
application launch. Editor and OS caches remain warm across Play restarts.

From observations between 3 and 17 seconds, the regular pending queue drained
at 30.92 / 31.19 regions per second. After the first seam-complete observation,
it drained at 986.85 / 984.10 regions per second. In the removal check, 7,672
regular regions still remained at 17.047 seconds. The final visual tail lasted
7.531 / 7.422 seconds after the seam-complete observation.

## Why scheduling is the first target

`GpuVoxelMesher.ProcessGpuRenderTick` services ready outer counts first, then
forces an outer submission after a 250 ms service gap. Otherwise near regular
work precedes transition work, and outer submissions are attempted only when
neither submitted work. `TrySubmitOuterCount` permits one outer batch in flight,
with at most eight requests per batch.

Eight requests per 250 ms equals 32 requests per second, closely matching the
observed slow phase. The source and repeated timeline strongly support scheduler
throttling as a contributor. They do not isolate its exact share of the total,
or prove that increasing concurrency preserves frame pacing.

Source: [scheduler](../../Code/Voxels/GpuVoxelMesher.cs), especially
ProcessGpuRenderTick, TrySubmitOuterCount and OuterMaximumServiceDelayMilliseconds.

## Transition costs

Both runs consumed 2,064 transition count readbacks. Summed request-to-callback
latency was 9,109.995 / 8,983.820 ms, with another 1,656.903 / 1,681.504 ms from
callback readiness to scheduler consumption. Average readback latency was about
4.4 ms per request. These totals can overlap work on other lanes; they are not
an additive allocation of the 26 seconds or isolated GPU execution time.

Transition batches contain one face. Sampling, classification and later count
stages are continued on separate scheduler calls; counts return to the CPU for
allocation before geometry emission. This creates repeated scheduling and
readback dependencies even when the sampled data itself is cheap.

Source: [transition scratch](../../Code/Voxels/GpuTransitionScratch.cs),
MaximumBatchSize, TrySubmitCount, TryContinueCount, TryTakeCounts and OnCountsRead.
The prior larger-batch failures remain relevant: simply increasing batch size is
not a qualified fix.

## Collision costs

Existing distributions provide the following sums (average times sample count):

| Work across all collision regions | Removal check | Repeat |
| --- | ---: | ---: |
| Field sampling | 1,772 ms | 1,755 ms |
| Mesh extraction | 153 ms | 156 ms |
| Collision creation | 163 ms | 163 ms |

These are accumulated CPU stage times across workers, not wall-clock startup
phases. Collision completes around eight seconds, so accelerating it alone
cannot remove the later visual queue tail in these observations.

Final reported frame windows were 528.6 / 530.3 FPS with p99 frame times of
5.61 / 5.72 ms. Those are rolling windows, not full-run percentiles. No new
frame-rate improvement is claimed.

## Recommended next experiment

First, give outer regular work bounded progress alongside transition work using
the existing scratch resources. Keep nearby terrain and edit responsiveness
prioritized, preserve current geometry and batch limits, and measure frame-time
tails as well as load completion. This targets the observed queue starvation
without first changing the terrain function or disk format.

Second, investigate transition scheduling and count-readback dependencies:
measure per-stage GPU execution separately from queue and callback latency before
deciding whether to combine stages, reduce round trips, or change allocation.
Any larger redesign must respect the documented shader/resource crash history.

A hypothetical complete overlap of the roughly 7.4-second final tail with earlier
work illustrates the opportunity, but is not a forecast or measured speedup.
Do not raise budgets indiscriminately: the user's priority remains frame pacing.

Current evidence does not isolate GPU terrain sampling versus classification,
edge refinement, emission or upload execution. Existing submission stopwatches
measure CPU submission, and frame GPU averages include other rendering work.
This investigation identifies the strongest first target, not a complete GPU
kernel-time breakdown. Canonical travel qualification remains outstanding.

Raw observations: `Docs/ValidationEvidence/Water/cache-removed-check-*` and
`startup-breakdown-1-*`. Derived figures: `startup-breakdown-summary.json`.
Scenario and outcome: [validation ledger](../ValidationResults.md).
