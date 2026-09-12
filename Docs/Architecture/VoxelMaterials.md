# Voxel Materials

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
Air 0, Grass 1, Dirt 2, Stone 3, Water 4, Snow 5. Definitions are immutable indexed data,
with constant-time lookup and no subclass, component or shader per type. Explicit
IDs match their append-only catalog slots; unknown IDs return a named magenta
fallback. The water candidate now assigns Water4 in the static surface-water
domain. See [surface water](SurfaceWater.md) for implementation and qualification.
No water collision or flow is implemented.

Procedural assignment consumes immutable field/settings and a world lattice node.
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

One palette buffer contains two float4 colors per registered ID (192 logical bytes
for these six entries). Its owner uploads it on the engine thread and disposes it
with the mesher. No per-frame palette allocations or per-type draws are introduced.
Rules, IDs and scale come from the CPU module. The RIVERS-016 prototype generates
material cell data beside solid density, replacing procedural draw-time lookups.
Regular and transition generation resolve top/subsoil material runs at the actual
refined cell crossing, reconstructing four16-unit XY lattice columns and two Z
nodes per column. Air contributors are omitted before normalizing across all
eight nodes. Runs describe the16-unit top layer,144-unit soil envelope,
then stone. Snow, slope eligibility, river bed and submerged classification are
evaluated during generation with the existing constants and recipe.

Generation packs four normalized weights beside the resolved edge position in the
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
