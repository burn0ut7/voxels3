# Readiness short-circuit prototype

2026-09-09. User-accepted prototype retained. The GPU maximum limitation below remains unresolved; the later repeat was interrupted.

The supplied 29.36 s sampling capture attributed 37.6% of sampled main-thread CPU
time to CapturePendingClipboxReadiness, including descriptor construction and
terrain correction-bound queries. Percentages are inclusive sampled CPU time,
not measured FPS gains. The runtime needs only a readiness predicate, while
explicit diagnostics need full missing counts.

The candidate changes only the existing VoxelManager readiness method and its
automatic caller. Four failed predicates return immediately for the automatic
call. A ready result traverses every existing dependency with unchanged current
field/epoch/edit identity checks. Diagnostic calls retain full counts through
the same method. There is no cache or new mutable state, no worker or shader
change, no generation-budget reduction, and no change to scheduling priorities,
LOD geometry, water, collision, edits, persistence or network protocols.

A dependency cache was deferred: the short-circuit removes redundant work without
adding stale-readiness invalidation/lifetime rules. See [candidate.patch](candidate.patch) for the
exact measured change. The source manifests preserve pre-existing shared work.

| Moving metric | Baseline B1 | Candidate C1 | Change |
|---|---:|---:|---:|
| Average FPS |553.98|717.87|+29.6%|
| Frame p95, ms |3.335|2.262|-32.2%|
| Frame p99, ms |4.511|3.250|-28.0%|
| Worst CPU frame, ms |115.66|89.56|-22.6%|
| Regular published regions/s |438.91|508.88|+15.9%|
| Regular readiness p95, ms |702.50|553.91|-21.2%|
| Outer readiness p95, ms |14387.76|9072.62|-36.9%|
| Route lag p95, chunks |3.640|2.908|-20.1%|
| Post-loop visual drain, ms |20067.16|14513.66|-27.7%|
| GPU p99, ms |1.691|1.844|+9.1%|
| Worst GPU frame, ms |11.817|13.114|+11.0%|
| Allocated bytes/frame |32027|30252|-5.5%|
| Peak process bytes |2259996672|2371579904|+4.9%|
| Peak GPU bytes |1872505999|1922837647|+2.7%|

Both starts used identical worldrevision 65/pages 24, geometry fingerprints,
configuration and grounded position; all 4913 collision regions were ready.
Both completed with zero runtime exceptions, collision failures, unsafe
placement commits and reported transition mismatches, and visual queues drained.
The candidate spawn render was inspected; it does not qualify all seams or
multiplayer. Field samples and geometry generation were not changed.

The maximum GPU sample exceeded the predeclared 10% investigation screen.
The user subsequently accepted the reported prototype with this limitation.
The attempted repeat did not produce a complete candidate result, so it does
not establish that the GPU-maximum difference was random or resolved.
The 89.6 ms worst CPU frame and 13.6 ms maximum GC pause also show that the candidate
has not eliminated stutters. Total moving allocation increased 2.16 GB to 2.65 GB
while bytes/frame fell, with more frames and published work. GC total increased
493.3 ms to 614.7 ms. Further allocation work requires identifying ownership.

The unchanged figure-eight releases the player at Z 0 and finishes underground.
Its stationary window follows visual settlement, not collision settlement.
Final collision ready/pending was 3219/1692 versus 2225/2686, with different final
positions and earlier candidate visual completion. Stationary FPS and final
collision counts do not establish a comparable settled-surface regression or
improvement. Collision request-to-ready p95 improved 2487.6 to 2102.2 ms, but those
lifecycle counters also extend through post-route activity.

B2 was stopped as invalid: the saved world changed externally to revision 253/75
pages, so geometry differed. No user edits were reverted. Source was restored
to baseline for B2 at that point; the candidate patch was retained for the
subsequent approved follow-up below. See the validation ledger READINESS-001/v1 for
parameters, source identity, run history and acceptance status.


## Accepted state and follow-up

The exact candidate is active and retained following the user's acceptance.
It changes only VoxelManager readiness evaluation. The original v1 pair remains
the complete performance evidence: [baseline B1](b1.json) and
[candidate C1](c1.json).

The approved current-world follow-up is READINESS-001/v2, using the user's new
scene/recipe unchanged. Baseline [v2-b1.json](v2-b1.json) completed at 610.32 FPS, p95 2.85 ms,
p99 3.82 ms. Candidate moving status reported 788.5 FPS, p95 1.95 ms, p99 2.76 ms,
but play restarted before final result publication. Later observations include
the restarted session and are explicitly not part of a completed benchmark.
No repeat loading-rate, GPU maximum or stationary comparison is accepted from
those partial observations. Saved worlds and unrelated scene/source edits were
preserved. Native compilation and the original completed comparison validate
this narrow prototype; rare long frames and broader feature qualification remain.
