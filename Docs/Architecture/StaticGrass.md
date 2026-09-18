# Static terrain grass

## Current tufts (one-player qualification accepted)

Grass remains derived appearance from published LOD0 terrain triangles and
canonical interpolated grass weights. The existing GPU mesher and per-camera
grass renderer own generation, buffers and both draws. Terrain edits, material
replacement and unloading automatically replace the geometry consumed by grass.
There is no vegetation simulation, collision, network state or persistent world.

Five curved leaves now share each surface root, replacing isolated triangular
blades. Each leaf uses two triangles between a narrow root, bent middle and tip.
Deterministic
height, width, orientation, outward lean and color variation break up repeated
silhouettes. Individual leaves span approximately7..31cm height and0.8..2.6cm
full width. Conservative region bounds include14in horizontal spread,20in height
and1in below terrain. Every leaf starts at the actual published surface root.

Upward-biased two-sided normals and green-to-olive variation soften the previous
dark spike shading. This approximates leaf lighting, not physical transmission.
Standard scene lighting and received shadows remain. There are no alpha cards,
extra textures, wind or grass shadow pass. Both depth and color use the same
vertex generation and roots; the canonical depth/fog integration below remains.

Density is one candidate tuft/36square units (~43tufts or215leaves/m²), full
through6m,30%at12m,8%at24m,zero32m. Stable hash thresholds and short size fades
retain progressive thinning. Material eligibility, triangle sampling, maximum
16candidates/triangle, LOD0 restriction and32m range keep their previous owners.
The compute pass rejects padded source-triangle bounds by range and guarded
frustum before sampling, so offscreen parts of nearby regions do not generate
roots. Region and triangle checks share padding constants; visible density and
the canonical root hash are unchanged by this culling. No CPU placement,
per-leaf allocations, scene objects or geometry readback.

TerrainGrassRenderer owns30vertices/tuft and passes the count to compute argument
finalization. The shared depth model has30sequential indices; the forward draw
is non-indexed. The shader consumes six vertices/leaf. Both modes therefore
submit10triangles/tuft. Root capacity remains65,536 at32bytes/root (2MiB/view);
the hard bound is1,310,720triangle submissions/view across both passes. Actual
counts/cost must be measured. Overflow remains a qualification failure. The
existing16-byte diagnostic now reports currentTufts rather than currentBlades.

The sparse one-root/144square-unit preview was rejected visually because plants
remained isolated. Denser shorter tufts are measured under GRASS-NATURAL-001/v2
in the ledger. Its changed saved world requires a fresh before/after baseline;
historical figures below are contextual. Existing terrain-texture fade changes
are held identical and excluded. See [current evidence](../ValidationEvidence/NaturalGrass/README.md).

Alternatives: more isolated triangles retain the spike silhouette; alpha-cutout
cards add texture/overdraw and depth-cutout work; independent curved leaves
multiply placement and root storage. Shared-root opaque tufts address shape
within the existing renderer at a measured increase in vertex work. The previous
single-blade geometry is replaced, not retained as a second rendering path.

The sections below document the historical single-blade implementation and its
measurements. Current shape, density, draw counts and qualification are above.

## Current qualification

GRASS-NATURAL-001/v2, run4e7db047ad1c4c20a8f106204a825306, passes the unchanged
before/after gates on the recorded RTX5090: moving523.24FPS versus520.81,
stationary466.13FPS versus486.06 (-4.10%), stationaryGPU1.64244ms versus1.54965
(+0.09279ms). Frame tails, memory, allocations, streaming and collision pass.
The route peaked at3999generated tufts after triangle culling, zero overflow,
with2MiBroot storage and at most79980grass triangle submissions/view in that run.
This is one-player qualification, not a multiplayer or other-hardware claim.
Close, skyline, bare material boundaries and6/12/24/32m views were inspected.
Fixed-image comparisons retain small raster/shading variation; no static tuft
movement or missing edge silhouettes was observed. Fresh edits and individual
snow/sand/water examples were not re-exercised. Rejected candidates remain in
[current evidence](../ValidationEvidence/NaturalGrass/README.md) and the ledger.

## Historical single-blade implementation

### First slice (prototype accepted)

Grass is derived appearance, with no simulation, collision, network state or
persistent vegetation world. The GPU mesher owns it through each render camera's
state and the existing terrain depth object. Inputs are published, active LOD0 terrain vertices,
indices, canonical interpolated material weights, and the current view.
The terrain field remains authoritative. Placement/material replacement and
unloading automatically change the consumed geometry; unpublished/stale regions
cannot generate blades. Grass waits for terrain presentation readiness.

A bounded compute pass samples published triangles by area and material weight,
rejecting non-grass and steep/downward faces. Roots use barycentric positions on
the actual rendered surface, not a heightfield or independent CPU terrain query.
Position-derived hashes provide fixed variation with no time input. Topology
replacement may change distribution. Grass is upright, with a fixed slight lean,
23–51 cm height, 2.8–5.1 cm full width and opaque two-sided single-triangle blades; no alpha cards,
wind, movement or grass shadow pass. Standard lighting receives scene shadows.

### Ownership and limits

RenderCameraState owns a fixed 65,536-root GPU buffer (32 bytes/root), counters and
indirect arguments, disposed after its recorded commands. Compute reads arena
buffers only after their normal publication, with explicit barriers between
clear, compute, argument finalization and draw. No geometry readback, worker,
CPU mesh generation or per-blade allocation is introduced. Re-evaluation per
render view is bounded by active nearby LOD0 triangles and 16 candidates/triangle.
Whole regions beyond 32 m or outside the expanded frustum are rejected before
triangle reads; roots are also distance tested. Other terrain LODs have no grass.
The normal 4-region LOD0 extent exceeds the grass reach. Detached cameras outside
loaded LOD0 therefore see terrain without grass until that terrain is available.

Density is one candidate per 72 square world units (~21.5/m²), full through 6 m,
reducing to 30% at 12 m and 8% at 24 m, then zero by 32 m. Stable hash thresholds with
short size fades avoid wholesale ring swaps. A hard global capacity bounds
memory and drawing; saturation is a qualification failure because atomic
arrival order could change retained blades. GRASS-SILHOUETTE-001/v1 passes the
recorded one-player prototype gates; this is not a maximum-throughput or multiplayer-load
claim. Prototype thresholds live in the compute shader;
capacity and root layout are owned by the C# grass renderer.
The production `voxel_grass_info` command requests one 16-byte scalar readback
of view count, current blades, lifetime peak candidates and overflow views.
It does not read roots or geometry; callbacks ignore disposed camera resources.

### Alternatives

CPU placement duplicates expensive field/material queries and introduces edit
invalidation and uploads. Per-blade components add unneeded object and draw costs.
Geometry shaders are not a verified portable engine path. Persistent per-chunk
root caches could reduce repeated generation, but add allocation, publication
and eviction responsibilities before profiling identifies that cost. A bounded
GPU pass over existing geometry is the smallest slice with exact surface roots
and automatic material/edit integration. Distance uses camera position per view,
not player state. Terrain texture shading remains independent.

### Skyline depth correction

Grass generation runs once per view inside the existing terrain depth object's
DepthPrepass callback, after publication readiness. Public Graphics barriers,
GpuBuffer.Clear and ComputeShader.Dispatch follow the terrain depth integration.
A shared three-vertex model submits the same roots to the grass shader's Depth
mode. The existing AfterOpaque indirect draw consumes those roots for color.
Both modes share one vertex shader, so silhouettes match exactly. Shadow views
skip grass. No per-blade scene objects or extra root-generation pass are added.

The five-uint argument buffer supports indexed depth and non-indexed color:
count3 and instance count are shared; all offsets/base vertex/first instance
stay zero. It is initialized to zero and cleared before each generation. Depth
and color each submit at most65,536triangles; total geometry submissions are
bounded at131,072triangles/view. The roots still occupy2MiB/camera.

The original fog reads the engine depth chain. Previously the chain omitted
late grass draws, so sky fog erased their tips at the terrain skyline. Including
grass in the real depth pass fixes fog and other depth-based effects. A first
candidate copied the full framebuffer depth before fog; it corrected the image
but failed performance. That implementation was removed, and DistanceFog stays
on its original canonical path. Exact evidence and rejected runs are in the ledger.

### Qualification

The taller/wider32 m grass and depth integration pass GRASS-SILHOUETTE-001/v1,
run06722ee9576c47a8902d88d9c59195b6. Moving567.74FPS,stationary515.23FPS;
stationary GPU1.6057ms versus1.7415ms for the original grass and1.4712ms for
no grass. All fixed frame-tail, memory, allocation, correctness and capacity
gates pass; peak7312roots,0overflow. Skyline, close and24/32m captures were
inspected after cold start. This remains one-player qualification on the
recorded hardware. See [follow-up evidence](../ValidationEvidence/GrassSilhouettes/README.md).

The following historical measurements describe the original smaller20 m prototype.

The original accepted single-triangle, ~43 blades/m² version is GRASS-001/v1-reduced
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
