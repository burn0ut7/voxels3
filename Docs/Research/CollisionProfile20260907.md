# Collision profile review: September 7, 07:22:47

The largest collision cost in the supplied capture is CPU construction, not
actor readiness checks. The workload consumes substantial worker time even
though native physics integration remains on the engine thread. CPU/GPU
separation prevents readback dependencies; it does not make worker CPU free.

| Captured responsibility | Sampled inclusive CPU | Interpretation |
| --- | ---: | --- |
| Collision mesh construction | 16,133 ms | Largest project-owned worker workload across the capture. |
| Point sampling | 8,223 ms | Repeated procedural SDF evaluation. |
| Conservative density bounds | 6,093 ms | Now a substantial fraction of construction after finer hierarchical rejection. |
| 3D simplex noise | 8,766 ms | Shared by point sampling and bounds; overlaps both rows above. |
| Cave density bounds | 4,262 ms | Four volumetric noise bounds per unresolved query. |
| Main-thread terrain manager update | 1,213 ms | Includes native work and nested terrain tasks; not solely collision. |
| Main-thread editor rendering | 3,221 ms | Separate rendering contribution, not collision ownership. |
| Main-thread animation update | 2,251 ms | Separate engine workload. |

These are sampled inclusive CPU estimates, not independent elapsed durations,
frame percentiles or predicted FPS gains. Do not add rows. The trace spans
21.957415 seconds and has 16 logical CPUs; worker samples spread across pool
threads do not establish multiple concurrent collision workers. Current source
still allows one extraction worker. Native/inlined frames can obscure wrapper
attribution, so small sampled Integrate totals do not override instrumented
shape-creation timings.

## Strongest opportunities

1. Reduce repeated 3D noise in collision construction while preserving the
   canonical field. Gradient selection alone accounts for about 1,205 ms in
   this capture; investigate exact-value lookup/inlining or coherent batch
   evaluation before increasing worker concurrency. Any lookup must preserve
   constants, seed hashing, arithmetic order and boundary values.
2. Reduce bounds evaluation that cannot change classification. The new prototype
   checks the cave depth envelope before querying four noise bounds. More
   aggressive parent/child bound reuse would need bounded build-local ownership
   and evidence that it actually reduces total work; fewer point samples alone
   already proved insufficient in rejected candidate 11.
3. Address native publication tails separately. Custom candidate-14 timings show
   0.425 ms average creation, 1.7725 ms p99 and 13.6169 ms maximum. Earlier
   candidate13 reached14.6522 ms. A 0.5 ms admission budget cannot split a native
   call. Establish whether pauses overlap cooking before redesigning ownership,
   and verify engine threading guarantees before moving cooking off-thread.
The active optimization scope is collision construction, residency and native
publication, as clarified by the user. Rendering and animation rows above remain
attribution context only; they are not implementation targets. Preserve the
standard visual workload and player behavior for comparison.

## Prototype and test

Candidate14 moves existing cave-envelope bounds before noise bounds. When the
maximum possible cave envelope is strictly below the minimum surface density,
noise cannot change either endpoint of the final max-composed density interval.
The shortcut returns the existing envelope and skips four noise-bound queries.
All unresolved cases retain the existing formula. No grid, topology, radius,
worker count, mesh tolerance, point sampling, field version or GPU shader change.

The unchanged standard figure-eight completed as
ab09f87558574dc2b99a362e5d734f5d: 813.733 FPS, CPU p95/p99 2.1459/3.8133 ms,
GPU 2.7487278/3.2243729 ms, 28,887 allocated bytes/frame, maximum GC17.256 ms.
Stationary912.040 FPS, CPU1.8924/2.6515 ms. Native creation maximum13.6169 ms
fails the10 ms criterion. Startup totals match:20,514,554 samples,8,279,932
rejected child blocks,1060 bodies,39,546,000 payload bytes,12 support patches.
All19 prescribed terrain-contact rays pass. Zero collision failures; all4913
regions eventually ready, normal teardown verifies zero owned bodies.

Startup sampling3.0391 ms/chunk versus3.1284 in the preceding recorded startup
is only a modest observation. Moving sampling+extraction2.1578 ms/chunk versus
2.2214 in candidate12 is about2.9% lower, with different completed populations
and environment history. This is not a proven FPS improvement. The latest manual
benchmark before the capture averaged830.666 FPS; it was saved five minutes
before capture start and is not a matched baseline. Original acceptance gates
remain unsatisfied. No acceptance commit or push.

## Evidence identity and limits

The supplied path expanded underscores into directories. The matching file is
C:/Program Files (x86)/Steam/steamapps/profiler_captures/sbox_2026-09-07_07_22_47.json/sbox_2026-09-07_07_22_47.json.
Metadata start11:22:47.237UTC, main thread4472, sample interval0.2 ms,
threadCPUDelta in nanoseconds. The capture does not independently pin a Git
revision or distinguish every gameplay phase. The current engine reports26.09.01c.
No claim that the latest saved manual benchmark overlaps this capture.

[Capture identity, thread totals and ranked stacks](../ValidationEvidence/TerrainCollision/user-profile-072247-summary.json),
[current source hashes](../ValidationEvidence/TerrainCollision/prototype-14-source.json),
[raw standard result](../ValidationEvidence/TerrainCollision/prototype-14-figure-eight.json),
and [validation ledger](../ValidationResults.md) preserve evidence and failures.
Prefix/frame/function tables were decoded with the same method documented in
[the earlier CPU review](CpuPerformanceReview20260907.md); no offline terrain
implementation or replacement test path was introduced.


## Collision-only follow-up

Candidates 15 and 16 tested gradient lookup and sparse collision-cache
initialization respectively. Neither demonstrated lower combined sampling and
extraction time during the unchanged figure-eight, so both were reverted.
Candidate 14 remains the working prototype; the validation ledger retains both
failed experiments. Native creation maxima still include occasional 13–14 ms
stalls, while their cause within body setup, mesh creation and publication has
not been isolated. The current metric times the whole operation, so calling
these exclusively mesh-cooking stalls would overstate the evidence.

The [pinned public PhysicsBody implementation](https://github.com/Facepunch/sbox-public/blob/9de061bb0fe2dc73ff29a134a0041928f2a47166/engine/Sandbox.Engine/Systems/Physics/PhysicsBody.cs)
shows the List AddMeshShape overload forwarding spans without an extra managed
list copy. The span overload validates indices and calls native mesh creation
synchronously. AddCloneShape is obsolete and returns null in that revision;
it is not a usable cooked-shape reuse path. These are public source observations,
not a guarantee that installed engine 26.09.01c has identical native internals.
The [official PhysicsBody API](https://sbox.game/api/Sandbox.PhysicsBody) does not
establish an asynchronous mesh-cooking contract. Neither this evidence nor an
absence of a thread assertion establishes worker-thread safety. Keep native
publication on the engine thread until an explicit supported contract exists.

Next collision investigations should distinguish AddMeshShape duration from
surrounding body setup/publication, record geometry size for the worst calls,
and quantify repeated region builds before introducing a cache. A cache would
need bounded memory, field-version invalidation and retirement ownership; repeat
visits alone do not justify retaining every shape. These are proposed follow-ups,
not implemented optimizations or measured gains. This source review adds no
runtime pass and does not change the benchmark or acceptance thresholds.


Candidate17 added the proposed creation attribution and ran the unchanged
standard benchmark. Across12978completed calls, AddMeshShape averaged0.42571ms
of0.43389ms whole creation (about98.1%). The worst call was3.4396ms, comprising
0.0081ms setup,3.4258ms mesh creation and0.0057ms publication, with4179vertices
and8050triangles. Setup/publication simplification alone therefore offers little
measured saving in this run. The earlier13–14ms creation stalls did not recur;
their cause remains open. CPU construction remains the larger aggregate worker
workload.752.962FPS and failed original frame-time gates are recorded without an
optimization claim in the ledger and
[raw result](../ValidationEvidence/TerrainCollision/prototype-17-figure-eight.json).


Candidate18 reuses the canonical surface term within collision sample columns,
leaving cave evaluation fully volumetric. Combined construction2.0353ms/chunk
versus2.2050ms in candidate17 (7.7% lower),49917versus46504publications.
Startup count gates and19contact positions pass. The bounded generator-owned
workspace is retained experimentally.787.622FPS still fails original acceptance;
final residency differs, so these runs do not establish a causal FPS gain.
See the ledger and
[raw candidate18 result](../ValidationEvidence/TerrainCollision/prototype-18-figure-eight.json).
