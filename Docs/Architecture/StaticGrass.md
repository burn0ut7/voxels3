# Terrain meadow grass

## Blade variation (2026-09-19, qualification pending)

The current refinement widens stable parent length from 25–35 to 22–38 units
and leaf multipliers from 0.65–1.05 to 0.55–1.15. Both ranges retain their
arithmetic midpoint, while shorter and taller leaves separate the canopy more.
The existing +/-15% patch multiplier remains: final parent length is
18.7–43.7 units and full-size leaf length is 10.285–50.255 units (26–128 cm)
before shape and wind lowering. Distance fading can make leaves smaller.

Leaves vary their middle height from 38–52%, heading by +/-0.45 radians and
width multiplier from 0.65–1.15. Stable squared posture variation mixes upright
shoots with splayed leaves: basal horizontal tilt is 0.06–0.75, with vertical
rise sqrt(1-tilt squared). Independent quadratic tip curl is 0.12–0.45 and
side bend is +/-0.12; posture adds 0.04–0.44 downward tip bend. More splayed
leaves can droop below their middle, rather than all ending upright. Every
shape term vanishes at the shared root. The existing two triangles per leaf
remain a coarse bent silhouette, not a smooth multi-segment curve.

These use the existing stable leaf seed, with no time or camera input to shape.
Color and wind retain their existing owners. The shared shape include owns
maximum leaf multiplier, basal tilt, curl and side bend for both vertex shape
and compute culling. The conservative horizontal envelope is 92.2141 units;
vertical padding remains 51.255 above and 1 below. Depth and forward share the
same shape code; upward-biased lighting also varies with posture.
Geometry counts, root storage and density are unchanged, but taller leaves and
wider bounds may increase rendering work. Cold-start, rendered appearance and
canonical figure-eight qualification remain pending; previous height acceptance
below covers the earlier ranges only.

## Previous patch height qualification

Tuft length shares the existing smooth color patch: greener areas are up to 15%
taller and warmer areas up to 15% shorter, with intermediate areas retaining
the same mean height. Stable local tuft variation is 25–35 units before that
multiplier, reduced from the previous 22–38 units so neighboring plants read as
a patch while retaining varied tips. The existing per-leaf 0.65–1.05 multiplier
remains. Final tuft length is 21.25–40.25 units; maximum leaf length is 42.2625
units (about 1.07 m) before wind lowers it. Most patch samples are intermediate
and therefore vary less than these bounds.

The compute shader owns these tuft limits and derives its region/triangle
culling padding from maximum length, the vertex shader's 0.5 lean and 1.05 leaf
multiplier, the shared wind bound, and a conservative 0.75-unit half-width.
The existing shape.y stores the result, shared by depth and forward rendering.
There is no added noise evaluation, buffer, geometry, dispatch, CPU work or
network state. Using the existing patch couples greener growth to slightly
greater height; an independent noise field was unnecessary for this gentle
variation. GRASS-HEIGHT-001/v1 owns visual and performance qualification.
The height-only candidate passes all fixed gates against the accepted color
baseline, with zero timed exceptions, collision failures or capacity overflows.
Fixed cold-start views confirm subtle canopy variation and intact long coverage.
See [height evidence](../ValidationEvidence/GrassHeight/README.md). The earlier
combined water/height failure is retained separately and remains a failure.

## Patch color

The color include owns a stable world-space field evaluated once per retained
root by the existing generation dispatch. Smooth value noise interpolates hashed
corners on a 4.5 m grid to form irregular patches without directional waves. The patch
determines the palette. Tuft and leaf seeds contribute only +/-0.015 and +/-0.005
respectively, avoiding randomly interleaved yellow and green plants. Green and
olive-yellow endpoints stay close in brightness, with darker, greener bases and
gently warmer tips. It is cosmetic, not a new biome or terrain material.

Color has no time or camera input, so wind cannot slide it across a plant.
The forward vertex pass decodes the cached color; depth only decodes orientation.
The second root float4.x packs a 16-bit angle fraction and a half-float color tone
biased into [1,2], keeping the packed float normal and finite even at zero tone.
Angle quantization is at most 0.0055 degrees; color precision is approximately 0.001.
The existing two-component interpolant,
root records, textures, geometry, draw count and CPU responsibilities stay the
same. Caching replaces 30 forward-vertex noise evaluations with one per tuft
without a texture lookup or extra root channel.
GRASS-COLOR-001/v1 owns performance and visual qualification. The first preview
weighted random plant colors more strongly and was rejected by the user; the
first patch revision was also rejected for obvious sine bands and stark colors.
The smooth-noise palette replaces both. Final cached-color qualification passes
all fixed gates, with +0.02472 ms standing GPU time relative to the wind baseline,
no timed exceptions or capacity overflows and inspected soft, irregular patches.
The ledger retains the earlier failed per-vertex candidate; its moving stalls
are not established as color GPU cost. See [color evidence](../ValidationEvidence/GrassColor/README.md).

## Coherent wind animation

Grass roots and material eligibility remain derived from the published terrain.
The existing generation dispatch evaluates a world-space wind field once
per retained tuft: a 20 m sine wave travelling at 4 m/s along (0.8, 0.6), plus a
smaller 5 m crosswind ripple. Nearby tufts share phases; stable leaf variation
changes their response amplitude, rather than giving every blade unrelated time.
Roots remain pinned and quadratic height weighting concentrates bend at the tips.
Tip lowering and the existing lighting normal follow the bend. Depth and color
read the same cached wind sample, so their animated silhouettes agree.

Keep the existing 32-byte root record, capacity, dispatches and draw count.
The second float4.w packs a 16-bit variation fraction and a half-float wind bend.
This slightly quantizes the existing stable variation; it does not animate or
reseed placement. The shared wind include owns direction, maximum bend and
wave parameters; compute culling includes the maximum possible tip displacement.
No CPU plant simulation, extra texture, per-blade object, world state, network
replication or geometry readback is introduced. Render time drives a cosmetic
field; cross-client clock synchronization is not a gameplay requirement.

Alternatives: per-vertex wave evaluation repeats the same field across 30
vertices and both passes; a separate simulation/texture adds state and dispatches;
random per-blade phases cannot produce coherent travelling gusts. Reusing the
existing root generation pays for two sine evaluations only for retained tufts.
GRASS-WIND-001/v1 owns visual and performance acceptance. The fixed figure-eight
passes all recorded gates: moving GPU +0.0382 ms, standing GPU +0.1649 ms, with
unchanged root storage and no overflow or timed exceptions. The baseline contains
large CPU/GC outliers, so the candidate's higher FPS is not a wind speedup claim.
Fixed meadow, close and skyline sequences show shared bending, anchored roots
and intact tips. See [wind evidence](../ValidationEvidence/GrassWind/README.md).

## Adjustable range

VoxelManager owns `GrassRenderRangeMeters`, a local scene property with a 96 m
default and finite clamping to 0–256 m. The Q menu exposes the same value under
Graphics → Grass distance for every local player; 0 disables grass. Applying
the menu value calls `SaveGrassRenderRangePreference`, saving the local
`graphics.grass-range-metres` preference through `Game.Cookies`. Playable-scene
OnLoad reads it once, falling back to the authored property/default. Inspector
and native assignments remain temporary overrides and do not save a preference.
This is not Sync/RPC world state and does not rebuild terrain. The manager
publishes the scalar to its mesher; each depth view captures
one value, clears its old indirect arguments, and generates both draws from it.

The compute shader consumes only already-published active regular terrain at
all available LODs. Inactive records and transition filler remain excluded.
Roots stay barycentric on the rendered geometry with canonical material weights;
grass does not create its own terrain sampler or enlarge streaming interests.
Candidate count stays capped at 16 per triangle. For coarser triangles, probability
weights compensate for the capped area sampling before density thinning; counts
still cannot exceed that cap. Terrain LOD replacement can change root placement.

Nearby density keeps the existing 6/12/24 m knots. Beyond 32 m it decreases with
inverse squared distance, with final fade from 75% of the selected range to the
endpoint. The distant size fade compensates for both the inverse-square tail
and the 0.08 mid-distance density. Its normalization changes smoothly from 1
at 12 m to 0.08 at 24 m, retaining full meadow height for most distant survivors
while shrinking the last density ranks. Previously these samples averaged only
0.4 of full size at 24 m, contributing to the appearance of grass confined near
the player. Population thresholds and near behavior through 12 m are unchanged.
The view and triangle
bounds share the range uniform; they cannot retain a stale hard-coded cutoff.
Zero range skips generation dispatches and clears both draw counts. Root memory,
geometry and two draw modes stay bounded as below. Range changes are recorded in
production performance reports. GRASS-COVERAGE-001/v1 owns qualification of the
new 96 m default, saved preference and size correction; it is currently pending.
Larger visible plants can increase pixel cost despite unchanged tuft counts.
The 256 m maximum is an optional quality setting, not a performance guarantee.
See [coverage evidence](../ValidationEvidence/GrassCoverage/README.md).
GRASS-RANGE-001/v1 remains the historical 64 m, session-only qualification;
its interrupted menu checks are not evidence for the current saved setting.

Alternatives: enlarging LOD0 streaming increases terrain/collision-related work;
changing only the shader cutoff still stops grass at the LOD0 boundary; scaling
all near-density distances quadratically increases dense grass area. Reusing
published coarser triangles and thinning the tail extends appearance within the
existing ownership and storage budgets. Both 32 m and 64 m candidates pass the
unchanged performance gates against a contemporaneous accepted-source control;
the ledger preserves historical tail failures and a mismatched-resolution run.
See [range evidence](../ValidationEvidence/GrassRange/README.md).

## Current meadow tufts

Grass remains derived appearance from published active regular terrain triangles and
canonical interpolated grass weights. The existing GPU mesher and per-camera
grass renderer own generation, buffers and both draws. Terrain edits, material
replacement and unloading automatically replace the geometry consumed by grass.
There is no vegetation simulation, collision, network state or persistent world.

Five curved leaves now share each surface root, replacing isolated triangular
blades. Each leaf uses two triangles between a narrow root, bent middle and tip.
Deterministic
height, width, orientation, outward lean and color variation break up repeated
silhouettes. The user's meadow refinement replaces the short7..31cm tufts with
approximately 26–128 cm nominal leaf length and 1.2–3.8 cm full width at full size.
The five leaves overlap neighboring tufts while retaining varied silhouettes.
Patch-adjusted parent height 18.7–43.7 in is multiplied by 0.55–1.15 per leaf;
basal tilt, curl and droop follow the current blade variation above.
Conservative region bounds include 92.2141 in horizontal spread with wind,
51.255 in height and 1 in below terrain.
Every leaf starts at the actual published surface root.

Upward-biased two-sided normals and varied rich greens soften the previous
dark spike shading. This approximates leaf lighting, not physical transmission.
Standard scene lighting and received shadows remain. There are no alpha cards,
extra textures or grass shadow pass. Both depth and color use the same
vertex generation and roots; the canonical depth/fog integration below remains.

Density is one candidate tuft/36square units (~43tufts or215leaves/m²), full
through6m,30%at12m,8%at24m. Beyond32m, the relative population decreases by
(32/distance)². The final quarter of the selected range fades to zero. Stable
hash thresholds and short size fades retain progressive thinning. Material
eligibility, triangle sampling and the maximum16candidates/triangle retain their
owners; coarse candidates are area weighted as described above. Shape and
nearby density follow the current blade refinement and accepted meadow respectively.
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
remained isolated. Denser shorter tufts were measured under GRASS-NATURAL-001/v2
in the ledger. Its changed saved world required a fresh before/after baseline;
historical figures below are contextual. Existing terrain-texture fade changes
are held identical and excluded. The long meadow refinement uses the same fixed
workload, geometry count, density and range; only dimensions, shading and the
conservative bounds change. Taller coverage can increase pixel work, so unchanged
triangle count is not itself a performance claim. See the meadow qualification
below and [short-tuft evidence](../ValidationEvidence/NaturalGrass/README.md).

Alternatives: more isolated triangles retain the spike silhouette; alpha-cutout
cards add texture/overdraw and depth-cutout work; independent curved leaves
multiply placement and root storage. Shared-root opaque tufts address shape
within the existing renderer at a measured increase in vertex work. The previous
single-blade geometry is replaced, not retained as a second rendering path.

The sections below document the historical single-blade implementation and its
measurements. Current shape, density, draw counts and qualification are above.

## Meadow qualification

GRASS-MEADOW-001/v1 compares with the accepted short-tuft run below, with unchanged
scenario inputs and performance gates. Runf06cc54d7a21461ab5f94a05a8a865d2 passes
the comparison with that latest accepted baseline: moving510.70FPS(-2.40%),
standing443.29FPS(-4.90%), standingp953.2656ms(+4.26%) andGPU1.76267ms
(+0.12023ms). Memory, allocations, frame tails, streaming and collision pass;
zero exceptions and grass overflow. The post-route diagnostic records2602current
and2606peak tufts in117614views,2MiBroot storage,52120peak triangle submissions
across both draws. This is a session observation, not a worst-case capacity bound.

Against the older isolated-blade baseline, standingFPS is down8.80% andGPU rises
0.21302ms; standingp95 is12.22%higher and exceeds that older10%gate. This historical
comparison is not an all-gates pass. The governing comparison for this revision
is the latest accepted short-tuft baseline, defined before the run.
Cold startup on26.09.15, source identity and close/overview/range views were
verified. The original low skyline camera is inside the taller grass; a raised
supplemental view records the canopy. See [meadow evidence](../ValidationEvidence/MeadowGrass/README.md).
Qualification remains one player on the recordedRTX5090, with static grass and
the same32m range; other hardware, multiplayer load and fresh edits are untested.

## Previous short-tuft qualification

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
