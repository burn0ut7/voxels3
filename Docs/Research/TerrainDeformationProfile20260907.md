# Terrain edit profile: September 7, 09:02:46

The supplied capture shows expensive CPU preparation on the rendering path,
substantial collision rebuilding, and broad main-thread invalidation. The brush
mutation itself is not the dominant captured work. This is an attribution
capture, not a fixed-workload benchmark or performance acceptance.

The resolved file is
`C:/Program Files (x86)/Steam/steamapps/profiler_captures/sbox_2026-09-07_09_02_46.json`
(17,346,332 bytes). It spans14,384.3565ms, reports16logical CPUs and a nominal
0.2ms sample interval. Estimates below sum `threadCPUDelta` in nanoseconds for
samples containing each function, deduplicating repeated functions within a
stack. They are sampled inclusive CPU time across the capture. Rows overlap;
do not add them or interpret them as individual frame durations. Native symbols
are partly unresolved, and this does not measure GPU execution time.

| Responsibility | Sampled inclusive CPU | Implication |
| --- | ---: | --- |
| CPU collision Build | 7,971.95ms | Collision extraction remains a substantial serial worker workload. |
| Regular GPU scratch TrySubmitCount | 3,928.85ms | CPU preparation for GPU work is itself costly on the rendering path. |
| Correction SampleCorrection, all threads | 3,647.16ms | Dominated by repeated correction preparation; overlaps collision and scratch rows. |
| Field GetDensityRange, all threads | 2,821.21ms | Conservative classification remains significant in collision building. |
| Main-thread collision Integrate | 504.32ms | 484.31ms appears under native AddMeshShape; integration budget cannot split a native cooking call. |
| Main-thread UpdateTerrainEdits | 328.16ms | 233.76ms appears under GPU invalidation, including repeated dependency-range scans. |
| Main-thread collision invalidation | 46.68ms | Additional broad dependency checks and queue ordering. |

The current regular scratch preparation visits every halo sample (35 cubed)
for every edited mesh request, including coarse regions in which most samples
cannot intersect an edited page. Every visit constructs positions, converts
them to lattice coordinates and looks up the sparse dictionary. Replace this
with a cleared scratch array plus direct copies from only page/lattice
intersections. Every regular LOD sample lies exactly on the base16-unit lattice,
so integer stride sampling preserves the same canonical values without
trilinear evaluation. No shader or field definition change is needed.

Invalidation currently builds a dictionary of the entire resident visual cache
and queries page revisions for every descriptor. Reject regions outside the
changed pages' conservative spatial envelope before those queries, and retain
only intersecting candidates. Apply the same early rejection to collision and
prepared-empty regions. This preserves the existing conservative dependencies;
it does not yet solve over-invalidation within a changed page.

Both changes are now implemented as an unaccepted candidate. Compilation and
runtime checks must verify the latest source. No speedup is claimed from source
inspection. The remaining work includes narrowing page-driven rebuilds,
measuring native collision tails, and instrumenting request-to-visible and
request-to-collision latency under the dedicated continuous/large/distant/
underground benchmark. Waiting for all previous edited collision work before
admitting another brush can directly delay feedback even if the game renders
smoothly; its latency must be measured and admission redesigned if necessary.

The user is interactively testing. Do not reset their session, run the scripted
benchmark, or move their camera implicitly. Normal mouse editing has no test or
camera-control entry point.

Raw derived attribution is retained in
[profile summary](../ValidationEvidence/TerrainDeformation/profile-090246-summary.json).
