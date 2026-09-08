# Profiler capture: 2026-09-08 06:31:47

Source: the user-supplied Firefox profiler JSON under Steam's profiler_captures.
[Extracted evidence](profiler-063147-analysis.json) records its SHA256, timing,
allocation ticks, collection events and sampled call stacks. This10.894second
capture starts after the earlier figure-eight completed; it cannot explain that
run's760ms maximum frame.

## Findings

- Four stop-the-world depth1 collections lasted10.65–12.36ms. These are concrete
  stutter candidates in this capture. Although the exporter calls the markers
  `GCMajor`, their depth is1, not2; no full-generation collection was recorded.
- Heap-stat events report about2.09GiB managed heap, including746MiB in generation3
  (the large-object heap). The old-generation and large-object sizes did not fall
  during the capture. This is not a root/retention graph and does not prove a leak.
- Recorded allocation ticks total179.94MiB. Prominent sampled types include render
  attributes, editor widget traversal, task scheduling, timers and strings. A tick
  names the triggering type; it is not exact object-by-object byte attribution.
- `SweepTerrainStorage` occurs in266 of28376 main-thread samples, versus1007 for
  the encompassing `VoxelManager.OnUpdate`. Its sampled inclusive CPU deltas sum
  to50.13ms over the capture. The counts overlap; do not add them or equate them
  with frame-time percentages. Editor scene rendering also occupies many samples.

## Changes selected from evidence

1. Checkpoint metadata validation now reuses one128KiB sample scratch buffer.
   Previously a2048-page validation pass allocated256MiB of dense arrays in total,
   released them, and waited for collection. This reduction is specific to that
   pass: replacement comparison, active reads and edits still have their own costs.
   The existing decoder validates both dense and sparse pages; saved formats and
   authoritative values are unchanged.
2. The eviction sweep skips interest/bounds checks for already nonresident pages.
   It still checks up to8 pages per frame, observes the same1ms soft work budget
   and5second grace, and uses the same authority and eviction guards.

Forced collection is not an available game-code remedy in the inspected s&box
access rules. Raising memory limits would hide pressure. A larger lifetime/pooling
redesign is not justified solely by this capture; first measure these bounded
changes and retain the separate unresolved read-capacity recovery observation.

## Validation scope

Full-capacity checkpoint validation covered1056 sparse and992 dense pages, with
all saved hashes and the sampled far-region fingerprint intact. In a changed-source
replacement/travel run, three old-epoch read completions were discarded and the
correct world settled with no authored or rejected mutations. The original user's
971/160-page world was restored with identical saved hashes. A later live edit in
private read-pressure-v1 was separately preserved as199.

The first performance run averaged803.36FPS but had a760.08ms maximum frame and a
code-comment edit during measurement. Its tail is confounded; the edit's precise
relationship to that frame is unproven. Keep that result, and use the separately
recorded final-source figure-eight for comparison. Final numbers are recorded in
[the validation ledger](../../ValidationResults.md).

Final comparison (same136/2048 fixture and canonical route):

| Metric | Earlier baseline | Final source |
| --- | ---: | ---: |
| Average FPS | 802.27 | 868.70 |
| Frame p99 | 3.84ms | 3.46ms |
| Worst frame | 20.50ms | 25.12ms |
| Allocated bytes/frame | 30,409 | 29,921 |
| Peak process bytes | 3,314,929,664 | 3,818,352,640 |
| Maximum GC pause | 14.75ms | 14.28ms |

Average throughput and p99 improved in this run; worst frame and total process
memory did not. The later editor session is an environment qualification, not a
proven explanation. Do not infer that all stuttering or memory retention is fixed.
The capture's allocation/GC findings remain relevant for subsequent work.
[Final run](cold-sweep-figure-eight.json) preserves full measurements.
