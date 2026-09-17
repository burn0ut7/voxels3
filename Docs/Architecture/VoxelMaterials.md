# Voxel Materials

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
nodes beneath that surface, with exposed gentle-top nodes Grass1 and eligible tip nodes Snow5.
Added solid above the original surface defaults to dirt. Covered grass becomes
dirt; covered mountain snow exposes the stone body. Caves below the soil strata
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

## Mountain snow

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
passes. Full detail through 32 m fades smoothly to each source texture's coarsest
mip by 64 m. Beyond 64 m two SampleLevel reads per contributing material retain mean
color and roughness/AO, with the geometric normal. The fade band samples both
representations; explicit branches avoid near-only work beyond 64 m and far-only
reads within 32 m. Existing per-material color/roughness tuning still applies.

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
