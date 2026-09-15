# Surface terrain skirt prototype

Status: v1 reverted at the user's request after measurement on 2026-09-13:
normal-speed arrival improved in one run, fast arrival and frame pacing regressed.
The v26 exact-seam source is the baseline. No performance acceptance is implied.

The reverted regular GPU mesher experiment emitted a downward lip at eligible X/Y boundaries in
its existing count, vertex and index stages. Its edge scan reserves one extra
vertex only for eligible boundary intersections. Each retained triangle edge on
such a boundary adds two skirt triangles. No new density samples, GPU readback,
scratch lane, scene object or per-frame regeneration is required. Lip depth is
two regular cells, owned by the CPU mesh contract and passed in the request.

Eligibility uses the canonical regional height bound to exclude possible caves
and cliff volumes, and the authoritative correction range to exclude edited
boundary samples or lip extent. Z faces retain exact Transvoxel. A transition can use skirts
only when its coarse face and all four neighboring fine faces independently
qualify under the same rule used by regular emission. Geometry approximation is
explicitly authorized for this prototype; this does not prove that a two-cell
lip covers every possible height mismatch. Steep boundaries, thin surfaces and
oblique views must be inspected before acceptance.

The transition cache keeps a marked skirt-coverage record with no separate
geometry. This is distinct from a proven-empty exact transition. Publication
still requires all current regular terrain and water, 2:1 balance, generation
identity and revision checks. Descriptor dependency bounds extend downward by
the lip depth in addition to the existing normal halo, so edits exposing a lip
also invalidate skirt eligibility and select the existing exact seam path. GPU
density sampling still uses the same original lattice and normal probes. Lip
geometry is visual derived data; collision and terrain edits use the unchanged
authoritative field. Regular bounds include the lip; diagnostics report skirt
coverage separately and audit its vertices/indices without claiming exact
Transvoxel digest equivalence on skirt-covered faces.

The regular mesh audit's declared spatial bound includes the lip depth; its
maximum triangle edge is the original1.75cells plus lip depth. Existing exact
transition audit limits remain unchanged. Exact lateral digest checks are kept;
a mismatch against a skirt-covered face is reported, not hidden as a pass.

Requests retain their96-byte layout: reserved vector X is world lip depth and Y
is the four-face mask. Counts retain32bytes and record lip depth in Reserved.
The existing cell metadata packs case8bits, triangle mask5bits, and boundary
edge mask15bits. Count and emission share that mask to prevent allocation drift.
Dedicated output shaders remain separate per the engine parser constraint.

Compare unchanged LOCAL-COVERAGE-001/v2 fast and standard routes, settled30s
warmup, viewport1847x959 and all recorded field parameters. Record first near
prepared/presented observations, their difference, frame/GPU tails, allocations,
resident memory, backlog drain, actual skirt coverage and mesh audits. The
old exact seam identity is an expected difference for substituted boundaries;
geometry safety and visual continuity remain requirements. Do not accept a
speedup by treating skipped geometry as proof of correct coverage.

Alternatives: retaining exact seams everywhere leaves their critical-path
dependency; duplicating every mesh vertex wastes memory; generating skirts in
a separate job retains another publication wait. This bounded experiment
extends the existing regular extractor only at its eligible boundaries.

Source evidence: [Cesium terrain edge/skirt inputs](https://cesium.com/learn/cesiumjs/ref-doc/QuantizedMeshTerrainData.html)
demonstrate independent edge curtains; this heightfield design does not prove
coverage of volumetric caves. [Transvoxel](https://transvoxel.org/) explains the
more general topology problem, which is why unsuitable faces retain exact seams.

Measured outcomes and acceptance limits
--------------------------------------

See [the comparison](../ValidationEvidence/LodSkirt/Comparison.md) for both
routes, evidence, environment differences and the decision. Only two settled
transition records were replaced; most exact seam dependencies remained.
Both post-run audits passed all 88 selected meshes, but these establish buffer
and geometry safety only. Existing PositionDigest hashes original intersection
vertices, not the extra lip vertices; it is not a complete skirt geometry
identity check. Topology metadata includes skirt masks.

The close boundary view showed continuous terrain; a wider oblique view showed
distant dark dashed seam lines with unresolved cause. Caves, newly edited lips,
mixed-boundary corners and shadow behavior are not qualified. The exact lateral
comparison reports one mismatch and remains visible in diagnostics. No claim
of global watertightness or production readiness follows from this experiment.
