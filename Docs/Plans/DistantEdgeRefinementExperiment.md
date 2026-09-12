# Distant edge refinement experiment

Status: experiment completed; candidate A rejected and original shader restored,
2026-09-12. Candidate B was not advanced.

The user accepts approximate distant surfaces but requires closed joins and
deterministic chunk generation. The first experiment isolates edge-position
accuracy from the existing placement scheduler and Transvoxel topology.

## Canonical responsibility

`voxel_edge_intersection.hlsl:RefineVoxelEdge` is shared by regular extraction
and transition extraction. Its explicit inputs are canonical world-space edge
endpoints, endpoint densities, terrain recipe and captured edit corrections.
It returns one world-axis coordinate for an already sign-changing edge.
No neighbor mesh, viewer state, queue order or batch identity is an input.
Both callers must choose the same iteration budget for the same edge length;
choosing a budget from the transition object's fine LOD would break its coarse
face agreement. Endpoints remain sorted in world-axis order.

Baseline uses eight bisections and a final secant interpolation on every edge.
Candidate A retains eight bisections for edges up to 32 world units (LOD0/1)
and uses four for longer edges. Candidate B, only if A remains viable, uses
two for those same longer edges. At the currently configured maximum LOD5,
the bracket width before secant is at most 32 units for A or 128 for B,
versus two for baseline. These are numerical bracket widths, not measured
screen-space error or guarantees of complete coarse topology.

This changes derived geometry only. World generation, authoritative edits,
collision, lookup tables, edge admission, rendering ownership, GPU lanes,
batch limits and publication gates remain unchanged. Each candidate replaces
the one helper in place; no alternate mesher or runtime quality toggle is added.
The existing area-based rejection uses refined triangle positions, so emitted
index counts can change even with identical lookup cases. Previously built
geometry must be discarded by a normal Play restart after explicitly rebuilding
both dependent source shaders; editing the include alone does not rebuild them
in this engine version.

## Alternatives and acceptance

Reducing repeated field evaluation is the smallest measurable slice. Removing
seams or adding skirts is deferred: simple downward skirts do not establish
closed volumetric cave/overhang boundaries from arbitrary flying viewpoints.
Local publication remains a separate architectural experiment; changing it in
this comparison would prevent attributing results to refinement precision.

Use unchanged HILLS-PLACEMENT-PRIORITY-001/v1 and, for viable candidates,
EDGE-PARTIAL-002/v1. Record source hashes, current saved world and exact runtime
parameters before running in Docs/ValidationResults.md. Preserve failed runs.
Compare moving/stationary frames, tail latency, allocation, memory, actual
regular/transition work, per-level request latency and full drain. Do not equate
drain or GPU readback callback intervals with isolated meshing execution time.

After each viable candidate, execute the production geometry coverage audit,
inspect the same fixed distant boundary views, and compare shared-edge mismatch
counters. The existing shader clean-editor-restart gate remains required.
Identical settled geometry digests on a repeated cold rebuild support repeat
determinism; they do not prove every batch order or cross-device bit equivalence.
Final claims must distinguish measured properties from those remaining untested.

## Measured decision

The four-step coarse candidate did not establish a standard-route speedup and
failed fast-flight recovery. All figures below use the world at revision 1585 with 328 pages and
2769x1436 rendering; the stale-shader and wrong-resolution runs are retained
in the ledger but excluded as controls.

| Measure | Eight-step control | Four-step coarse candidate |
| --- | ---: | ---: |
| Standard moving FPS | 443.33 | 434.67 |
| Standard p99 frame, ms | 5.35 | 5.39 |
| Standard full drain, s | 6.39 | 6.63 |
| Fast moving FPS | 345.56 | 342.25 |
| Fast p99 frame, ms | 8.12 | 8.22 |
| Fast full drain, s | 8.04 | 60.29 |
| Fast first nearby presentation after trigger, s | 123.60 | 177.10 |

The fast route itself lasts approximately 121.94s. Nearby presentation is an
existing 27-region draw-eligibility check sampled every 0.5s after movement;
it is separate from pixel visibility and full drain. Nearby CPU preparation
was complete at 123.10s in A, but presentation followed 54s later. At one sampled
blocked handoff, the finest staged anchor was [-34,-34,0], with 740 LOD1 readiness
failures and 218 missing transitions. Both regular/water readiness and seams were
involved. Similar moving FPS and slightly lower average GPU frame time did not
translate into fast LOD recovery. This pair cannot isolate which timing change
caused the different sequence of intermediate placements.

Repeated A rebuilds produced identical settled position and lookup-topology
fingerprints on this device; LOD0/1 positions matched the control. Coverage
audits passed, and two fixed distant views showed no new sky crack against the
baseline. These are bounded checks, not proof of arbitrary batch permutations,
cross-device determinism or universal volumetric closure. Existing area rejection
retained more seam triangles in A despite fewer edge-refinement iterations.

Reject A for its measured latency regression. B is not advanced. Runtime source
returns to the canonical eight-step helper, with no quality switch. Retain the
native editor shader-rebuild and render-resolution operations needed to perform
valid comparisons without desktop automation. The complete scenario, failures,
raw evidence and restoration checks are in [the validation ledger](../ValidationResults.md).

## Architectural scope of the result

Chunk computation and chunk presentation are separate contracts. The existing
regular descriptor supplies field inputs for an independently buildable region;
transition descriptors likewise sample the canonical field instead of requiring
neighbor mesh buffers. This experiment preserves that independence.

Presentation still advances nested clipbox boundaries in sequence. In
`VoxelManager.SelectClipboxBoundary`, a boundary must fit inside its committed
parent and contain its committed child with a coarse-region gap. In
`TryCommitPendingClipboxPlacement`, the next placement waits for the required
regular resources, preparation/water and transition resources before committing.
Reducing edge iterations does not remove either dependency. A per-region
schedule-to-renderable measurement starts after that region is scheduled; it
cannot measure time spent waiting for the hierarchy to request its refinement.

The next architectural alternative to evaluate is smaller spatial replacement
groups with retained coarse coverage. Each group would own a coarse fallback,
its replacement fine meshes and just the joins on that group's boundary. The
fallback must remain visible until the complete local replacement is ready;
completion elsewhere must not gate it. Keys must include coordinates, LOD,
field/generator revision and deterministic boundary configuration, with stale
results rejected by the existing revision discipline. World state and SDF
sampling stay canonical. This is a proposal, not implemented independent
publication. Arbitrary groups require a defined ownership rule for corners,
overhangs and intersecting transition faces; deleting the present readiness
checks would not implement it safely.

An ordinary downward skirt is a separate visual approximation, not a substitute
for that ownership rule. It can hide some terrain-height differences but cannot
guarantee closed volumetric cave or overhang boundaries from every flying view.
The present experiment changes edge positions without removing transition
surfaces or changing sign classification, so it cannot quantify a skirt speedup
or fix holes caused by terrain/water publication.
