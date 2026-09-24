# Voxel Materials

## Independent clay surface candidate (2026-09-20)

`materials/voxels/clay.vmat` is a separate clay material asset with its own
original gray-blue color and height maps under `textures/terrain/clay`.
It selects the terrain shader's `F_CLAY_SURFACE` static variant. The existing
world material keeps that feature disabled; no dirt, gravel or other source
texture is replaced. No Clay voxel ID or automatic deposit rule is assigned in
this asset-only slice. The asset expects the existing custom terrain vertex
layout; it is not a general model material or an editor preview mesh material.

The terrain shader owns a0.5m tile,0.008m full height interval,1254 source texels
and minimum height mip2. Clay uses the existing bounded triplanar relief search
and its8–16m/grazing/subpixel fades. Its normals derive from the same filtered
height at the ray hit, using the shared height-surface sampler also used by
gravel. Gravel retains its own maps and all its previous physical/surface inputs.
Clay roughness is0.88 with subtle cavity shading (floor0.92). Collision, geometry,
world generation, saved state and network material assignments are unchanged.

The separate static variant reuses the canonical ray and surface sampling
instead of copying a clay-only parallax implementation. It does not add a world
draw or change vertex stride. Actual compiled cost/resource elimination must
still be verified; no performance claim follows from the static variant alone.
The [asset manifest](../../Assets/textures/terrain/clay/manifest.json) preserves
both image-generation prompts and source hashes. Rendered seams/registration,
shader compilation, clean-start and figure-eight acceptance remain pending in
CLAY-SURFACE-001/v1; the editor was unavailable during preparation.



## Gravel candidate (2026-09-20)

Material ID7 is separate Gravel. The user rejected scattered natural patches;
CPU/GPU gravel placement rules and their bindings are removed. The generator
emits zero gravel weight, leaving existing material assignment intact. Its
texture, parallax and six-weight rendering support remain available, but gravel
is not currently placed by generation or a gameplay tool. See
[gravel design](../Plans/GravelTerrain.md) for the current scope and budgets.
Protocol7 reserves gravel material support; density/save formats are unchanged.
Managed build passes; shader/cold-start, in-world appearance and unchanged
figure-eight acceptance are pending. Historical five-weight results below do
not qualify this candidate.

## Marsh candidate (2026-09-20)

Generator50/protocol6 adds Marsh soil ahead of the climate/shoreline rules below:
the canonical marsh weight must exceed the same seeded coverage threshold.
Its first144units use Dirt, with Grass only on exposed tops above sea+12units.
Air/water and explicit placed materials keep precedence. CPU/HLSL match this
priority; the existing five material weights and vertex stride are unchanged.
Terrain presentation adds damp soil tint/roughness and olive grass tint from
climate and surface altitude. See [marsh design](../Plans/MarshFirstSlice.md).
Managed builds and independent source review pass; rendered quality, cold shader
load, production correctness and performance remain unverified.

## Climate layers (2026-09-20 candidate)

Snow and desert sand now occupy real procedural material layers extending5–10
base cells (80–160units) below the canonical exterior. Thickness varies smoothly
in XY using2048-unit seeded noise and `160-80*noise^3`, biased toward10cells.
ProceduralVoxelMaterials owns the shared CPU depth formula and snow salt62119;
ProceduralSand owns the desert recipe with independent salt57191. The GPU adapter
binds those recipes to the matching shared generation formula. Snow requires dry
cold-climate coverage; desert sand requires nonmarine desert coverage. Snow takes
priority, followed by desert sand and the independent existing shoreline/deposit
rules. Normal dirt/stone resumes below. Air, water and explicit placed material
retain precedence, and excavation exposes the same interior material.

These climate rules replace the historical pointed-peak snow rule below. Neither
layer changes height, density, collision or saved edits. Protocol5 requires matching
procedural material behavior between peers. GPU reconstruction retains its existing
48-unit XY filtering and16-unit vertical interpolation, so rendered boundaries are
softened relative to discrete material queries. No new vertex channels or draws.
Production distribution/boundary and placed-dirt precedence checks pass. Independent
review accepts the local snow/sand excavation appearance. The first paired run
passes frame/pacing/allocation gates but fails process memory; full biome visuals,
clean-start and performance acceptance remain tracked in BIOME-SNOW-DEPTH-001/v2 and
BIOME-DESERT-SAND-001/v1 in the validation ledger.

The [sand recipe and seeded material regions](ProceduralSand.md) describe the
current sand slice, its five-weight rendering contract, and pending qualification.

## Dirt tool slice (2026-09-10, qualification in progress)

Right-click/build now authors Dirt2 in the same immutable TerrainFieldPage as its
density correction. Digging retains that surface identity on residual geometry.
CPU material queries and regular/transition mesh generation consume the override;
the shader does not infer the placed material from depth or correction sign.
The existing smooth radial shape operation is retained. General material selection
and player-placed water remain outside this immediate tool slice.

Page modes2/3 append a ushort material plane to the shared lossless density codec;
legacy0/1 pages decode with inherited materials. Zero means procedural inheritance,
and Dirt2 is the only explicit value accepted in this slice. Format2 world identity
is retained so existing worlds load normally; unsupported new page modes fail in
older readers. Network protocol3 prevents transfer to density-only peers. Material
differences participate in restore comparison, page/block revisions and fingerprints,
including the case where every density correction has returned to zero.

Paired payloads share residency, immutable readers, eviction/reload and the existing
512MiB reservation gate. Accounting conservatively reserves196608bytes per page,
even for legacy pages with no material array. Meshing transports a dirt-weight
plane in the existing scratch density buffer binding, then stores the result in
the existing packed vertex weights. This adds scratch capacity and edited-region
upload work; it is measured rather than assumed free. No per-cell scene objects.

TerrainCellState defines logical empty as<=10% solid volume. Its metric integrates
six linear tetrahedra from the eight canonical16-unit cell corner samples. It is
a deterministic volume estimate of the cell, not an exact integral of nonlinear
generation. The dig ray skips such remnants through a bounded cell traversal;
the SDF mesh and collision retain their continuous remainder. Right-click can
target that remainder and refill it. The material inspector exposes solidFraction
and logicalEmpty separately from the surface node material. Generation is not
snapped to binary occupancy. See MATERIAL-TOOL-001 in the validation ledger.

## First slice contract

Qualification is tracked by MATERIALS-001/v1, MATERIALS-BLEND-001/v1 and
MATERIALS-LOD-001/v1 in
[the ledger](../ValidationResults.md). Material identity, visual definitions and
procedural assignment are separate responsibilities behind a narrow module.
The canonical density/correction field still owns solid/air geometry; materials
do not introduce an occupancy grid.

The catalog owns stable unsigned 16-bit IDs, names and two checker colors per type:
Air 0, Grass 1, Dirt 2, Stone 3, Water 4, Snow 5, Sand 6. Definitions are immutable indexed data,
with constant-time lookup and no subclass, component or shader per type. Explicit
IDs match their append-only catalog slots; unknown IDs return a named magenta
fallback. The water candidate now assigns Water4 in the static surface-water
domain. See [surface water](SurfaceWater.md) for implementation and qualification.
No water collision or flow is implemented.

Procedural assignment consumes immutable field/settings and a world lattice node.
The sand recipe linked above precedes the retained strata rules below and adds
patchy beaches and sparse river sand among the normal grassy banks.
Negative/zero density is solid. Depth is measured vertically from the canonical
unedited landform height. Non-mountain columns have one16-unit grass layer,
eight dirt layers, then stone. Mountain-dominant columns assign Stone3 to solid
nodes beneath that surface, with exposed gentle-top nodes Grass1. The climate layers
above override those strata with Snow5 or Sand6 where eligible.
Added solid above the original surface defaults to dirt. Covered grass becomes
dirt; snow retains its climate-layer identity below a covered surface. Caves below the soil strata
are stone. Surface angle does not reclassify nodes; canonical mountain membership
selects rock. See the mountain stone contract below.

VoxelChunk forwards material queries and returns ushort IDs. It does not own IDs
or assignment rules. Queries acquire a small neighboring field region before
sampling; unavailable stored pages return failure. The production console command
`voxel_material_info x y z` reports the containing logical node through that same
chunk API, with negative coordinates using floor.

The dirt tool slice above persists explicit dirt overrides. Other materials retain
procedural assignment; the water candidate resolves Water from the captured world
recipe and density. Arbitrary painted/placed materials still require extending the
same field contract, not renderer-owned edits.

## Derived rendering and limits

One palette buffer contains two float4 colors per registered ID (224 logical bytes
for these seven entries). Its owner uploads it on the engine thread and disposes it
with the mesher. No per-frame palette allocations or per-type draws are introduced.
Rules, IDs and scale come from the CPU module. The RIVERS-016 prototype generates
material cell data beside solid density, replacing procedural draw-time lookups.
Regular and transition generation resolve top/subsoil material runs at the actual
refined cell crossing, reconstructing four16-unit XY lattice columns and two Z
nodes per column. Air contributors are omitted before normalizing across all
eight nodes. Runs describe the16-unit top layer,144-unit soil envelope,
then stone. Snow, slope eligibility, river bed and submerged classification are
evaluated during generation with the existing constants and recipe.

Generation packs four explicit weights (with Sand as their remainder to one)
beside the resolved edge position in the
existing edge buffer's second plane. Emission copies those weights to Color32 alongside position
and normal. The shared packed vertex layout is28bytes, owned by
GpuVoxelMesher.TerrainVertexBytes. Drawing interpolates weights and reads the
existing palette; it has no noise, terrain or river queries. The previous
voxel_materials.hlsl draw implementation and world-wide presentation river atlas
are removed. Collision and the authoritative edit/save field remain unchanged.

This is an unaccepted prototype. Material interpolation now follows mesh samples,
so coarse-LOD surface fidelity must be compared with the historical16-unit draw
lookup. Do not claim identical appearance or accept FPS gains that merely conceal
missing rivers or unacceptable material-detail loss. The terrain fragment cutout
for narrow rivers is removed: only the actual generated mesh owns terrain holes.
Earlier material qualification describes the former renderer, not proof for this
new storage/meshing path. Validation is recorded in RIVERS-016.

## Alternatives and budgets

A subclass, component or engine material per block type remains rejected. The
prototype uses one palette, compressed generated material runs and four vertex
weights; it introduces no per-type draw, mutable material world or save format.
The former pixel-field approach preserved16-unit material detail independently
of geometry but repeated generation every frame. User instruction now requires
that work during chunk generation. Larger vertices and edge material planes
are real costs that must appear in the same performance/memory comparison.

Historical vertex-height/normal material experiments changed excavated dirt to
stone and were rejected. The prototype therefore retains canonical material
membership and surface-relative depth; rendered normals do not classify material.
Coarse interpolation is still approximate and must be visually qualified. The
logical material query and existing procedural rules remain the canonical source.

## Historical mountain snow (superseded by climate layers)

SNOW-TIPS-001/v1 corrects the continuous-ridge generator's coverage rule.
MountainMasses exposes PeakFraction as `height * (1 - blend) * ridges`, the
height-weighted pointed-ridge contribution. Its broad shelf contribution is
excluded. Using the full mountain mass was rejected because high, flat plateaus
received large solid snow blankets. Fully shelf-dominated terrain now has score
zero; partially blended shelves attenuate the score, while pure pointed ridges
retain their previous score. The score cannot exceed the former full-mass value,
so this correction can only reduce the snow footprint. CPU and GPU formulas match.

RegionalLandforms carries the score beside mountain weight and height.
ProceduralVoxelMaterials owns SnowPeakFraction=.76 and SnowMountainWeight=.75.
Exposed top-layer nodes become Snow only when both thresholds pass and their
natural surface is at or above sea level. This is a pointed summit eligibility
score, not a fixed altitude snowline or the upper24% of every mountain. Terrain
mass, gradients, bounds and erosion are unchanged; density/save identity does not
change. The calculation reuses existing values without extra noise samples.

The GPU adapter binds both thresholds and Snow ID5. Generation returns height and peak semantics together and stores material runs.
The prototype mesh carries interpolated weights; its coarse edge softness is
not yet qualified against the former per-pixel base-voxel blending. CPU queries
retain actual coverage/air checks. Off-white checker colors are (0.78,0.80,0.80)
and (0.88,0.90,0.89), with the shared roughness and distance fade.

Snow replaces the top stratum without adding geometry or filling caves. This
derived material recipe applies to existing worlds without changing density save
identity; material payloads are still not persisted. Added storage is one catalog
row (32 GPU bytes), a bound Vector4 and scalar landform semantics. There are no
additional height/noise queries, render passes, queues or per-frame allocations.
Density/water consumers retain the height-only projection of the same GPU sampler.

## Mountain stone and grass nodes

MOUNTAIN-STONE-003/v1 refines MOUNTAIN-STONE-002 with grassy upward-facing tops; both replace the rejected patch-based candidate completely.
ProceduralVoxelMaterials owns MountainStoneWeight=.75. In a column whose canonical
mountain weight meets that threshold, solid nodes beneath the surface covering
have a Stone3 body, including the shallow interior. Existing exposed pointed-tip rules
select Snow5 first; exposed mild mountain tops instead select Grass1 as described below. Non-mountain columns retain grass/dirt/stone strata; additions
above the original surface retain dirt. Water and air classification still precede
solid material selection. No texture mask or rendered-triangle normal controls material identity.

The gameplay node query returns the discrete material ID. The current cell-data
prototype generates column material runs and encodes interpolated weights in mesh
vertices. Drawing reads catalog colors for those weights. The shared checker
visualizes the selected palette; it does not classify grass, stone or snow.
Coarse interpolation currently produces visible triangular patches; that visual
regression is recorded in RIVERS-016 and has not been accepted.

Removed: patch scales/salt/noise, coverage weights, tint blending, two extra GPU
parameter vectors and the unnecessary internal exposure of the generator's noise
helper. The stone threshold now travels in the generation-only
VoxelGeneratedMountain binding. The stone rule itself adds no noise queries.
The current prototype's material storage/vertex cost is recorded separately. Procedural
IDs remain derived through the canonical material query; density, collision shape,
world recipe/save identity and terrain geometry are unchanged by this material
rule. Future explicit painted/placed material persistence remains unimplemented.

The live node and appearance checks are recorded in the ledger. Full cold-start
and figure-eight acceptance are pending a stable combined source/world.

### Grass on mountain tops

The current user explicitly requested slope-based grass/rock node assignment,
superseding the earlier rejection of any slope-based surface rule. Exposed natural
surface nodes on mountains become Grass1 at slopes <=45degrees; steeper nodes
remain Stone3. Eligible snow tips still take priority. A grass covering is one
base-cell layer over the stone mountain body, so exposed deeper cliffs are rock.
This is procedural classification, not runtime grass growth.

ProceduralVoxelMaterials owns MountainGrassMaxSlopeSquared=1. Slope is evaluated
from the canonical composed landform/river height at the node's XY and its two
forward neighbors one16-unit base cell away. Squared slope is the sum of squared
height differences divided by the squared base spacing. CPU and GPU use this same
fixed world-lattice stencil. It is independent of camera, rendered normals and
visual LOD. Each GPU material column has its own discrete grass/stone result; only
neighbor interpolation blends the resulting palette colors.

The current GPU generation stage adds two forward height queries for each eligible
mountain column. Non-mountain and snow-tip columns skip slope work. CPU queries
add two heights only for exposed non-snow mountain nodes. There are no height
queries in the terrain draw shader. Generation/storage and visual fidelity remain
subject to the combined prototype's measured qualification.

Grass is restricted to the original exterior. Cave/overhang tops far below the
natural surface and material growth over player additions are not modeled. The
current renderer still approximates the procedural material field on edited
geometry as documented above; it does not sample the edited density in the pixel
shader. This limits claims about grass on newly sculpted or hidden surfaces.

## Grass PBR candidate (2026-09-15, human testing)

The terrain mesher now loads materials/voxels/voxel_terrain.vmat, using the same
voxel terrain shader, vertex weights, arena draws and canonical material IDs.
Grass weight (packed channel X) replaces its former checker palette contribution
with ambientCG Grass004. Other material weights retain their checker colors.
The material owns five source maps and compiles them into three BC7 textures:
sRGB color, linear OpenGL normal, and packed linear roughness/AO/height.
The 2048-square maps with full mip chains budget approximately16MiB in total
before driver overhead. No new per-frame CPU allocation or draw is introduced;
fragment work adds nine filtered samples and triplanar blending. Runtime costs
remain unmeasured and this is not performance acceptance.

The shader owns the single1.4-metre tile scale, converted to inches with0.0254.
World-coordinate triplanar projection uses fourth-power absolute normal weights;
whiteout detail-normal blending retains the geometric normal for flat texels.
Normal orientation and grass transitions need human review. There is no mesh
or SDF displacement, foliage geometry, material reclassification, save-format
change or collision change. A single top projection was rejected because steep
surfaces stretch; separate grass geometry remains a later scope. Source/license
notes are in Assets/textures/grass/README.md. GRASS-PBR-001/v1 records validation.

Human review rejected the source map's glossy response. Grass roughness now maps
to0.85..1.0 before blending with other materials, preserving texture variation
while representing dry ground cover. A same-composition screenshot confirmed
removal of the broad white glare.

## Grass parallax candidate (2026-09-15)

Requested extension to the current human-test grass material. Adopt bounded
height-field ray marching with linear intersection refinement and explicit
texture gradients. Existing source displacement occupies the linear B channel of the BC7 surface
texture. Source height amplitude is unspecified; the authored presentation depth
is2cm, not a measured scan depth. All color/normal/roughness/AO samples consume
the same displaced coordinates per triplanar projection. Keep the dry0.85..1.0
roughness range. Filtering increases to16x anisotropy.

Use12..24 height steps according to angle, one initial height sample, and fade
relief from full at2m to zero at8m and at projection grazing/back-facing angles.
Skip ray marching outside its contribution range. Worst case adds75 height
samples for three projections; far grass retains its nine material samples.
This is a bounded cost, not evidence of acceptable performance. Height uses the previously unused B channel, so the three-texture memory budget
remains approximately16MiB before driver overhead.

No extra CPU work, world state, draw calls, collision or pixel-depth writes.
POM approximates visual recesses; it does not create standing blades, silhouettes
or real self-shadowing. Single-sample parallax was rejected because it lacks
inter-blade view occlusion. Unbounded marching and full-distance POM were rejected
for cost. Geometry/virtual texturing are not part of this material slice.
Reference: Natalya Tatarchuk, Practical Parallax Occlusion Mapping (SIGGRAPH2006):
https://advances.realtimerendering.com/s2006/Tatarchuk-POM.pdf
Transfer limits: planar height-field technique applied per triplanar projection;
it does not establish s&box compatibility, measured speed or exact physical depth.

## Grass repetition correction candidate (2026-09-15)

The direct repeated mapping produced visible long rows in the user's grass
view. Replace it with one deterministic triangular patch sampler per projection:
three hashed fractional offsets, normalized fourth-power barycentric weights,
and explicit original UV gradients. Translation preserves the normal-map axes;
no per-patch rotation is used. Color, normal, surface and bounded parallax share
each patch's UV. Grass scale, dry roughness, height amplitude and source images
remain unchanged. Drop vanishing triplanar contributions continuously before
sampling to avoid paying for effectively invisible planes.

Inputs remain world position, geometric normal, canonical material weight and
three compiled PBR textures. Outputs remain derived shading only. No CPU state,
draw calls, texture allocations or terrain edits. Per contributing projection,
base sample count rises from3 to9 and worst-case height reads from25 to75.
Most flat surfaces have one contributing projection; worst-case3projection
cost remains27 base plus225 height reads. Costs need measured qualification.
Simple hue modulation was rejected because it preserves repeating blade shapes;
UV warping stretches blade detail. Full histogram-preserving transforms are not
adopted; sharpened weights limit blending blur without new LUT assets.
Reference: Mikkelsen, Practical Real-Time Hex-Tiling,
https://jcgt.org/published/0011/03/05/. This is a simplified translated-patch
adaptation, not a claim to implement the paper's full surface-gradient or
histogram algorithms. Runtime evidence belongs in GRASS-TILING-001/v1.

The final patch weights have a0.001 zero-support fringe before normalization.
This makes discarded contributions reach zero continuously and permits skipping
those texture/parallax reads. A matching0.001 fringe on fourth-power axis weights
skips vanishing triplanar projections. These are shading support thresholds,
not changes to canonical terrain material identity or mesh visibility.

## Five terrain surfaces (2026-09-15 candidate)

Terrain presentation now extends the grass path to Dirt2, Stone3, Snow5 and
implicit Sand6 weights. One shared sampler implements world-space triplanar
mapping, continuous triangular patch offsets, matched PBR maps and bounded POM.
No material classification, SDF, collision, replication or mesh changes are needed.
Source maps are owned by voxel_terrain.vmat; shader call sites own metre scale,
authored relief and dry roughness range. Gradients are evaluated before divergent
material branches. Zero-weight materials and projections skip texture work.
Texture2D helper parameters follow installed common/utils/triplanar.hlsl evidence.
The superseded terrain checker shading is removed. Existing CPU palette bindings
remain untouched in this shader-only slice. Grass mapping is preserved.

| Surface | Source | Tile metres | Authored relief metres | Roughness |
| --- | --- | --- | --- | --- |
| Grass | ambientCG Grass004 | 1.4 | 0.020 | 0.85..1 |
| Dirt | Poly Haven dirt | 2 | 0.033 | source, minimum0.65 |
| Stone | Poly Haven rock_face | 2.38 | 0.040 | 0.65..1 |
| Sand | ambientCG Ground101 | 1 | 0.002 | 0.85..1 |
| Snow | ambientCG Snow007A | 1 | 0.003 | 0.80..1 |

Relief amplitude is art direction, not measured scan depth. Each2K source set
compiles into three BC7 textures (sRGB color, linear OpenGL normal, linear packed
roughness/AO/height). Five sets budget approximately80MiB with mipmaps,64MiB more
than grass alone, before driver overhead. One shared16x anisotropic sampler.
POM retains12..24steps,2..8m distance fade and grazing fade. At most five materials
can contribute; each retains the existing three planes/three stochastic patches.
Only contributing materials execute samples. Blends cost more than pure regions;
TERRAIN-PBR-001/v1 must qualify the change before performance acceptance.
No mesh displacement, pixel-depth writes, parallax shadows or snow scattering are
claimed. Generalizing the existing path avoids five independent algorithms;
texture arrays and extra draw passes were unnecessary for this slice.

Stone human-review correction: source reads too brown. Preserve linear luminance
with Rec.709 weights and retain15% chroma in the Stone3 contribution only.
Normal, relief, roughness and other materials retain their settings. Source maps
remain unmodified; color correction belongs to the terrain shader.

## Fine sand correction

Sand6 now uses ambientCG Ground101 fine pale sand, replacing clumpy sand_01.
Provider physical dimensions are unspecified:1m tiling is authored, with2mm
visual relief. Five matched maps retain normal detail, dry0.85..1roughness,
stochastic tiling and existing POM fades/step bounds. Texture count and2K BC7
memory budget are unchanged. The previous sand_01 set is superseded.

## Clean snow correction

Snow5 now uses ambientCG Snow007A clean smooth snow, replacing debris-marked
Poly Haven snow_02. Tile1m and relief3mm are authored (provider dimensions absent),
versus former2m/25mm. Matched color/normal/roughness/AO/height, anti-tiling, dry
roughness and mip/filter budgets remain. Same-camera screenshot verifies mostly
white appearance with subtle grain and removal of embedded dark gray debris.

Dirt color human-review adjustment: multiply Dirt2 linear albedo by
(0.55,0.65,0.60) for darker, less reddish earthy brown. Source textures and all
surface-detail settings remain unchanged. This tint belongs only to Dirt2.


## Dirt relief calibration candidate (2026-09-15)

DIRT-RELIEF-001 independently disables dirt POM and normals through the production
shader; both affect the rendered image, and restoration is pixel-identical.
The original18mm height interval compressed the central98%of source heights into
7.33mm. The33mm candidate gives13.44mm for that interval and approximately matches
source normal slopes at2m tiling. This is an estimate, not measured scan depth.
An exaggerated45mm/1.35-normal candidate produced objectionable stretching and
was rejected. Original source normals and dark brown tint are retained.

Source roughness is already0.887..0.987. The old0.8..1 remap compressed it to
0.977..0.997, suppressing variation. Use the source with a0.65 safety floor;
this remains a dry, rough dielectric. Height, normal, AO and color still share
one transformed UV; linear data maps and sRGB color retain their proper reads.

Following Tatarchuk's POM treatment, a footprint-aware12..96step trace was
implemented and measured. It did not sufficiently improve the exaggerated scan
and coincided with higher GPU cost in the canonical route. It was rejected;
the shared12..24step trace is restored. Source maps, memory, draw calls, geometry,
gradients and existing fades are unchanged. The final shipping diff for this
correction changes only dirt amplitude and roughness. See failed evidence in
the ledger; no performance equivalence or full acceptance is claimed.

Poly Haven's texture standards and scan workflow motivate matched maps and
consistent scale. We retain the scanned embedded soil/debris appearance; normal
mapping controls lighting, POM controls apparent depth. POM self-shadowing is a
separate light-ray calculation and remains unimplemented. Actual protruding
stone silhouettes require geometry and are outside this material correction.
See the research library and validation ledger for evidence and pending acceptance.

## Surface-tangent parallax correction (2026-09-15)

The previous triplanar POM treated each world-axis projection as a separate
geometric surface: its denominator was signed view.x/y/z. On sloped ground, a
projection could approach grazing while the real surface remained front-facing.
The projection's denominator clamp and fade then stretched and collapsed detail
in a band tied to camera direction. Increasing sampling did not address this
coordinate mismatch.

SampleTerrain now derives normalView=dot(geometricNormal,view) and
worldTangentView=view-geometricNormal*normalView. Each texture plane receives the
corresponding two components of this tangent vector plus the same normalView.
The existing POM denominator, grazing fade and step selection therefore refer to
the real surface. This reduces to the old mapping on axis-aligned surfaces and
has zero lateral offset looking straight along the actual normal. The same UV
intersection still drives color, normal, roughness and AO in each patch.

Keep12..24steps,33mm dirt relief,existing material colors/roughness,stochastic
patch blending and2..8m distance fade. No new textures,draws,mesh/collision changes
or test hooks. This is a local planar approximation on curved terrain, not
geometric displacement or self-shadowing. TERRAIN-BLUR-FIX-001 owns visual and
performance evidence. Full acceptance still requires a comparable canonical run.
## Parallax disabled (2026-09-16)

User requested complete terrain parallax removal after the sampling investigation.
The shared shader now samples undisplaced stochastic UVs for all five surfaces.
Height ray marching, camera-dependent relief, and relief parameters are removed;
color, normal, roughness, occlusion, triplanar blending and 16x filtering remain.
Packed height channels are retained as asset data but are not read by shading.
This supersedes the earlier parallax candidates and surface-tangent correction.

## Distance shading and channel packing (2026-09-16)

The parallax-free shared surface sampler still performs up to 27 SampleGrad
operations per contributing material (three planes, three stochastic patches,
three maps). Geometry LOD does not reduce that pixel cost. Explicit branches
skip zero plane/patch weights and material weights at or below 0.00001 (below
stored 1/255 material precision), including floating-point sand remainders.

Runtime shading is owned by voxel_terrain.shader. Inputs are camera/world
position, canonical interpolated material weights, geometric normal and the
existing textures. No new state, buffers, world data, networking, geometry or
passes. Full detail through 64 m fades smoothly to each source texture's coarsest
mip by 128 m. Beyond 128 m two SampleLevel reads per contributing material retain mean
color and roughness/AO, with the geometric normal. The fade band samples both
representations; explicit branches avoid near-only work beyond 128 m and far-only
reads within 64 m. Existing per-material color/roughness tuning still applies.

Distance thresholds belong only to this shared shader, independent of clipbox
LOD boundaries. This avoids material changes at mesh swaps. Using source mip
averages avoids duplicate hand-authored palette constants; removing all normal
maps or lowering nearby filtering would sacrifice useful close detail. The fixed canonical route and visual comparisons are recorded below;
TERRAIN-DISTANCE-001 records measurements and decisions.

Channel packing: color RGB (sRGB) plus roughness alpha
(linear), normal RGB plus AO alpha (linear), both BC7. This replaces the separate
roughness/AO surface texture with existing unused alpha channels, reducing 15
compiled texture bindings to 10 and worst-case near sampling from 27 to 18 reads per material.
Full color/normal/roughness/AO behavior remains; compression may differ slightly.
Height inputs remain unused because parallax is disabled. Source images and
material input paths do not change. Shader rebuild must be followed by a full
material compile so generated channel packing matches the new shader layout.

The validated combined implementation is active: see TERRAIN-DISTANCE-001 and
../ValidationEvidence/TerrainDistance/Investigation.md for exact measurements,
failed attempts, source hashes and limits. Fresh packed versus fresh distance-only
improved moving FPS 8.6%, with unchanged stationary FPS and lower peak process/GPU
memory. The optimized shader, source material, referenced images and material-loading
line are integrated together. Unrelated generation/water work remains separate.

## Baked terrain patterns and relief prototype (2026-09-19)

Current source uses one camera-relative height ray across dirt, stone, grass,
sand and snow. This is an unaccepted prototype: the engine has not compiled or
qualified the latest source, and the final in-world stone appearance remains unverified.
The validation ledger owns results; file hashes do not prove rendered behavior.
No geometry, collision, world state, persistence or blade ownership changes.

`Tools/bake_terrain_patterns.py --material grass|sand|snow` owns the periodic
triangular lattice and those materials' physical settings. It replaces the
former grass-only baker. Grass stays4096px with1.4m chart tiles and20mm height
interval; sand uses4096px over8 lattice cells and snow2048px over2 cells,
with1m chart tiles and120/60mm intervals respectively. These are
art settings, not measured scan depths. The generated `voxel_terrain_pattern.hlsl`
is consumed by both ray and shading coordinates. Original source maps remain
authoritative art inputs; generated PNGs/manifests live in each material's
`Assets/textures/terrain/*_pattern` directory. Grass initially preserved the pre-height color, roughness and AO bytes.
CH subsequently sharpens grass overlap weights after the exponent4 support
cutoff, changing all channels coherently to reduce hazy overlaps. The cutoff
is applied before the extra square to retain support at triangle centers. Sand/snow now use periodic
bakes instead of the former unbounded stochastic shader lookups; their pattern
and effective source detail therefore change and need visual qualification.

Each bake samples color, normal detail, roughness and AO at the same patch
coordinates and weights. Grass/snow also sample source16-bit height there;
sand replaces the rejected scan height with the authored ripple field below.
Color blends in linear space. Macro normals
derive from the final quantized height at box mip2, with periodic differences
and the lattice chain rule. For texture T=(U-.577350269V,1.154700538V)/(tile*period),
physical gradients are dH/dU=H_Tx/(tile*period) and
 dH/dV=(-.577350269H_Tx+1.154700538H_Ty)/(tile*period).
GL normals encode(-amplitude*dH/dU,+amplitude*dH/dV,1), with bounded fine normal
residual added before normalization. Runtime flips GL green and projects the
resulting slopes onto the terrain tangent plane. Height LOD uses the same
lattice derivative transform as color; all participating materials share the
ray hit. Height-based triplanar weights agree between ray and final sampling.

Sand mapping3 uses TerrainSandPatternPeriod for ray, shading and derivatives;
mapping1 retains TerrainPatternPeriod for grass/snow, and mapping2 remains direct
UVs for dirt/stone. Sand has a separate base height LOD for its larger field.
The generated constants also own amplitude and minimum height mip. Material
compiler output remains two BC7 color/roughness and normal/AO maps, plus R16F
height. Estimated added height mip memory is42.7MiB each for grass/sand and10.7MiB
for snow. Stone now preserves the continuous RockFace scan coordinates at2048px/3m,
with120mm authored interval and height mip2. Its baker subdues broad scan
undulation to45% and adds35% crevice depth from the source AO; normals derive
from the resulting field. This is authored bedrock relief, not measured depth.
Its earlier rubble patch composition has been removed. Stone metadata is generated;
dirt's approved2048px/3m/120mm/mip2 inputs remain unchanged. Actual memory and
frame timing are unmeasured. Relief fades8-16m; ordinary texture detail still
fades64-128m. Shadows, silhouettes and depth output remain geometric.

Sand CS extends user-approved CR with local phase bends and a smooth amplitude
mask that tapers selected ridge segments into calm sand. This interrupts long
rows without changing color/grain or the authored height interval. CS is baked
but still awaiting a rendered beach comparison and independent visual review.

Sand CR replaces CK's rejected blurry pit pattern with18 connected curved ridges
per8-cell periodic field. Smooth phase modulation varies spacing and direction;
a slowly varying height envelope creates quieter stretches. Source grain,
color, roughness and near-white AO remain scan-derived across64 vertex hashes;
fine-normal residual gain is0.18 and there is no height Gaussian blur. Macro
normals derive from this exact authored height, not the original scan displacement.
This is authored art, not measured scan depth. CL's initial uniform-ripple version
passed independent sand identity review but was criticized for combed regularity
and dark continuous troughs. CM reduces those defects; CN adds bounded variation
between neighboring ridges to reduce synchronized bends. CO removes local-spacing
height modulation after native review identified pinched dark knuckles; crest height
now varies only through the broad envelope. CP reduces that envelope's strength
by15% after CO's native view showed stronger dark ribbons. CQ halves ridge count
to12 to broaden the remaining narrow striped banks, with envelope strength0.95
instead of0.85 to retain height while reducing slope. CQ looked too soft in its
native view, so CR uses18 bands and boosts only fine scanned albedo grain in linear
space (Gaussian sigma2.5 highpass gain1.5). Broad albedo variation is not amplified.
Finite repetition remains;
no claim of aperiodicity, motion parallax or final rendered acceptance follows.

Snow CJ filters its combined height with a periodic Gaussian of8 cache texels
before normal derivation; the fine source-normal residual remains separate.
Both canonical bakers share Tools/terrain_bake_output.py, which encodes all PNGs
and stages changed outputs before publishing them in a short burst. This avoids
long encoding gaps between live input updates; it does not provide multi-file
atomicity or establish engine load success. Unchanged outputs are not rewritten.

Source changes require regeneration, dependent shader and material compilation,
cold-start validation, matched in-world motion evidence, and canonical performance
qualification. These live checks are pending explicit user direction for computer
control. Preserve all user terrain edits when qualifying the candidate.

## Extended texture reach (2026-09-17)

TERRAIN-FADE-EXTEND-001 extends the shared fade from 32-64 m to 64-128 m.
The previous fade visibly flattened nearby mountain surfaces. This doubles both
full-detail reach and transition width, using the same textures and sampling
branches. More pixels execute detailed sampling; it is not free GPU work.
Fixed-view screening passes; canonical route qualification is pending because
the candidate return view changed during measurement. Distant surfaces
still become average material color beyond 128 m; this does not eliminate all
distant flatness or change geometry streaming/LOD.


## Material transition prototype (2026-09-18)

Candidate F is the accepted prototype. It retains E's visually screened quadratic
reconstruction and skips fully faded noise. The matched repeat pair passes all
performance gates, with about 2.1% moving FPS cost. The first F pair failed the
standing p99 gate; the bounded repeat found comparable variation in the unchanged
control and did not reproduce that regression. Both pairs remain in the ledger.
B retained stepped outlines, C was too hazy, and D was too expensive. The user
accepts tiny visual patches merging or disappearing; discrete cell identities,
procedural recipes, density, edits, collision and persistence remain unchanged.
This section supersedes the four-column reconstruction description for this
prototype. Historical measurements above do not qualify its added cost.

GenerateVoxelMaterialWeights owns presentation reconstruction at extracted edge
positions. It samples a separable quadratic B-spline over 3x3 XY columns at
48-unit presentation spacing (every third canonical column) and two vertical
nodes per column. Each column uses the same depth below
its own surface, avoiding subsoil bleeding onto inclined grass. Solid weights
are normalized per column before lateral filtering; air-only columns fall back
to dirt. The output remains four packed weights plus implicit sand in the existing
28-byte vertex. Regular and transition extraction use this one function with
world coordinates and fixed support, independently of mesh LOD.

GpuVoxelMaterials.GenerationHalo owns the river-query dependency: OceanReach plus
1.5 presentation spacings (712 units). BlendSpacing owns the 48-unit sampling
spacing and binds it as VoxelMaterialBlendSpacing to both generation paths;
vertical material-layer spacing remains 16 units. Both scratch paths use it alongside their
existing geometry/normal bounds. No retained coverage cache, extra mesh pass,
new texture or per-frame CPU calculation is introduced. Generation column work
rises from four to nine evaluations per weight request; the ledger records its
measured cost against the current saved world and unchanged surrounding sources.

voxel_material_blending.hlsl owns the shared local mixture. Continuous value
noise at 16- and 4-unit wavelengths varies relative weights inside mixed areas;
integer corner hashes are world-anchored, including negative coordinates.
Positive multipliers and squaring retain absent materials and renormalize all
five contributions. Pure interiors skip this work. Ground shading attenuates
subpixel variation using the pixel footprint, skipping noise octaves with zero
contribution and skipping the modulation entirely beyond the coarse cutoff. This is procedural edge breakup,
not texture-height blending. Existing color, normal and surface maps still apply.

Grass evaluates the same mixture at each candidate root, using full local noise,
and accepts roots probabilistically with smoothstep(0.1, 0.9, grassWeight).
Stable root hashes thin the population without animated randomness. Ground
pixel filtering and grass root sampling intentionally differ at a distance.
The prior hard 0.75 grass threshold is removed. Capacity and generation spacing
are unchanged; overflow checks remain required.

This is derived visual data only. Existing rebuild/publication owns invalidation;
there is no new mutable world state or network payload. Coarse triangles can
still omit small deposits and blend wider regions. The user accepted that tradeoff
for this experiment. A retained coverage cache would preserve those deposits at
higher memory and sampling cost; it remains a research alternative, not a second
runtime path. Fixed-view evidence and every runtime attempt are recorded under
MATERIAL-TRANSITION in the validation ledger.

Final near/far/near fixed-camera review retained coherent broad deposits with no
visible material cracks. Small details changed at the coarser placement, as
authorized. This is not a guarantee of patch preservation across every LOD or
proof of temporal smoothness from still images. See the [evidence index](../ValidationEvidence/MaterialBlending/README.md).

### Dirt tread removal (2026-09-20)

The five active Dry Mud Field001 maps now remove the scanned tractor chevrons
using Tools/remove_dirt_tracks.py. Shared clean-soil donor coordinates and weights
keep color, roughness, AO, height and fine normal detail aligned. Broad height is
continued from unmasked soil; macro normals are rebaked from the final16-bit height
at the existing mip2/3m/120mm scale. Runtime shader, bindings and sampling budgets
are unchanged. The original nor_gl file remains an offline source only. Exact
recipe and hashes live beside the maps; DIRT-TRACKS-001/v1 in the validation ledger
records native visual checks and the unavailable canonical performance baseline.

## Shared stone breakup candidate (2026-09-20)

Stone3 remains one material for exposed terrain, cliff edges and underground
walls. Tools/bake_stone_relief.py now samples the existing RockFace scan through
one continuous periodic coordinate warp. A4096px/12m field contains four source
repeats per axis with varied spacing; it replaces the exact2048px/3m repetitions.
Color, authored height, AO and roughness share those coordinates. Fine normal
slopes receive the mapping Jacobian; macro normals derive from the final16-bit
height at box mip1. No patch blending or runtime coordinate/noise path is added.
Broad source color contrast is reduced35% at96-source-texel Gaussian scale,
with broad mineral variation added to the larger field. Fine scan color remains.
The authored height interval increases120to180mm; broad height coefficient
increases.45to.65 and AO crevice contribution.35to.45. These are art settings,
not measured scan depths. Runtime POM still uses the shared ray and8–16m fade.
Generated metadata owns stone scale, height interval, resolution and minimum mip.
The height footprint remains170.7texels/metre; color sampling falls from682.7
to341.3texels/metre. Estimated compiled stone mip memory increases64MiB to85.3MiB.

Continuous warp was chosen to retain connected bedrock instead of cutting and
blending unrelated slabs. Increasing the source tile alone would enlarge the
same repeated features; per-ray stochastic patch sampling would add texture work.
The bake remains periodic at12m and cannot promise no recognizable features.
No geometry, collision, material IDs, assignment, persistence or networking change.
Native shader/material compilation and close/side/return visual checks succeeded;
exact return capture is identical. This is still a candidate: matched historical
performance and cold editor restart checks remain unqualified. See
STONE-BREAKUP-001/v1 in the validation ledger and its evidence directory.

The2026-09-20 shared stone breakup candidate above was REJECTED by the user as
unnatural. Its12m coordinate warp and180mm relief are reverted to the exact prior
3m/120mm continuous RockFace bake. The paragraph above describes a failed trial,
not current behavior. An independent rendered visual review is now required.
