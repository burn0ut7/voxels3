# Static terrain grass

## First slice (prototype accepted)

Grass is derived appearance, with no simulation, collision, network state or
persistent vegetation world. The GPU mesher owns it through each render camera's
existing command list. Inputs are published, active LOD0 terrain vertices,
indices, canonical interpolated material weights, and the current view.
The terrain field remains authoritative. Placement/material replacement and
unloading automatically change the consumed geometry; unpublished/stale regions
cannot generate blades. Grass waits for terrain presentation readiness.

A bounded compute pass samples published triangles by area and material weight,
rejecting non-grass and steep/downward faces. Roots use barycentric positions on
the actual rendered surface, not a heightfield or independent CPU terrain query.
Position-derived hashes provide fixed variation with no time input. Topology
replacement may change distribution. Grass is upright, with a fixed slight lean,
13–30 cm height and opaque two-sided single-triangle blades; no alpha cards,
wind, movement or grass shadow pass. Standard lighting receives scene shadows.

## Ownership and limits

RenderCameraState owns a fixed 65,536-root GPU buffer (32 bytes/root), counters and
indirect arguments, disposed after its recorded commands. Compute reads arena
buffers only after their normal publication, with explicit barriers between
clear, compute, argument finalization and draw. No geometry readback, worker,
CPU mesh generation or per-blade allocation is introduced. Re-evaluation per
render view is bounded by active nearby LOD0 triangles and 16 candidates/triangle.
Whole regions beyond 20 m or outside the expanded frustum are rejected before
triangle reads; roots are also distance tested. Other terrain LODs have no grass.
The normal 4-region LOD0 extent exceeds the grass reach. Detached cameras outside
loaded LOD0 therefore see terrain without grass until that terrain is available.

Density is one candidate per 36 square world units (~43/m²), full through 6 m,
reducing to 30% at 12 m and 8% at 16 m, then zero by 20 m. Stable hash thresholds with
short size fades avoid wholesale ring swaps. A hard global capacity bounds
memory and drawing; saturation is a qualification failure because atomic
arrival order could change retained blades. GRASS-001/v1 passes the recorded
one-player prototype gates; this is not a maximum-throughput or multiplayer-load
claim. Prototype thresholds live in the compute shader;
capacity and root layout are owned by the C# grass renderer.
The production `voxel_grass_info` command requests one 16-byte scalar readback
of view count, current blades, lifetime peak candidates and overflow views.
It does not read roots or geometry; callbacks ignore disposed camera resources.

## Alternatives

CPU placement duplicates expensive field/material queries and introduces edit
invalidation and uploads. Per-blade components add unneeded object and draw costs.
Geometry shaders are not a verified portable engine path. Persistent per-chunk
root caches could reduce repeated generation, but add allocation, publication
and eviction responsibilities before profiling identifies that cost. A bounded
GPU pass over existing geometry is the smallest slice with exact surface roots
and automatic material/edit integration. Distance uses camera position per view,
not player state. Terrain texture shading remains independent.

## Qualification

The accepted single-triangle, ~43 blades/m² version is GRASS-001/v1-reduced
(run93f6ac8d3ec24cc6b3ca810b30bc481e). The ledger and
[comparison evidence](../ValidationEvidence/StaticGrass/README.md) retain the
three-triangle failure and the full-density candidate. Standing still costs
4.60% FPS and +0.2703 ms whole-frame GPU time in the paired route; all recorded
FPS, frame-tail, memory, allocation, collision and capacity gates pass.
The moving run shows no regression; its observed speed increase is not evidence
that adding grass makes terrain faster. Performance is hardware/workload-specific.

Actual grass/dirt/stone boundaries and the 6/12/20 m range views were inspected.
Two fixed close screenshots have identical pixels, including blade silhouettes.
Fresh live digging and snow/sand/water examples were not separately exercised;
material filtering and invalidation consume the existing published terrain path.
No new authoritative mutation behavior is introduced. Other hardware and loaded
multiplayer sessions remain outside this prototype's measured qualification.
