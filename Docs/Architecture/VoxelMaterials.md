# Voxel Materials

## First slice contract

Qualification is tracked by MATERIALS-001/v1, MATERIALS-BLEND-001/v1 and
MATERIALS-LOD-001/v1 in
[the ledger](../ValidationResults.md). Material identity, visual definitions and
procedural assignment are separate responsibilities behind a narrow module.
The canonical density/correction field still owns solid/air geometry; materials
do not introduce an occupancy grid.

The catalog owns stable unsigned 16-bit IDs, names and two checker colors per type:
Air 0, Grass 1, Dirt 2, Stone 3, Water 4. Definitions are immutable indexed data,
with constant-time lookup and no subclass, component or shader per type. Explicit
IDs match their append-only catalog slots; unknown IDs return a named magenta
fallback. The water candidate now assigns Water4 in the static surface-water
domain. See [surface water](SurfaceWater.md) for implementation and qualification.
No water collision or flow is implemented.

Procedural assignment consumes immutable field/settings and a world lattice node.
Negative/zero density is solid. Depth is measured vertically from the canonical
unedited landform height: one 16-unit grass layer, then eight dirt layers
(128 units), then stone. Added solid above the original surface defaults to dirt.
Logical grass candidates covered by solid one base cell above return dirt. Caves
below the soil strata are stone. Exposure does not turn dirt into stone based on
surface angle: steep cuts show whichever stratum they actually intersect.
The earlier slope-based candidate was rejected after user visual review.

VoxelChunk forwards material queries and returns ushort IDs. It does not own IDs
or assignment rules. Queries acquire a small neighboring field region before
sampling; unavailable stored pages return failure. The production console command
`voxel_material_info x y z` reports the containing logical node through that same
chunk API, with negative coordinates using floor.

No material payload is persisted. The water candidate expands world recipe
identity to include water version and sea level; density pages retain their meaning. Future painted/placed materials require authoritative
mutation, storage and transport through the field boundary, not renderer-owned
edits. The water candidate resolves Water from the captured world recipe and density.

## Derived rendering and limits

One palette buffer contains two float4 colors per registered ID (160 logical bytes
for these five entries). Its owner uploads it on the engine thread and disposes it
with the mesher. No per-frame palette allocations or per-type draws are introduced.
Rules, IDs and scale come from the CPU module; active recipe attributes derive
from canonical scheduled descriptors. Geometry layout, extraction, density,
correction buffers and collision remain unchanged.

The vertex shader records depth below the canonical natural surface:
`depth = height(vertex.xy) - vertex.z`. The rasterizer interpolates this scalar;
the pixel shader reconstructs the material lookup height as
`baseLatticeHeight(pixel.xy) - interpolatedDepth`. The reference height is the
bilinear interpolation of the same four 16-unit material columns sampled below,
avoiding a redundant fifth height query. This removes the chord error of coarse
triangles over curved hills. The original world position still owns geometry,
lighting and checker coordinates. Underground and excavated vertices retain
their depth; no normal-based or distance-based grass override is applied.

The pixel shader looks up the eight base-lattice nodes surrounding that attached
material position. Four canonical XY landform queries supply the two node heights
in each column. It resolves each node's procedural ID, blends palette colors by
trilinear weights, omits procedural air and normalizes the remaining weights.
Blend support is one 16-unit voxel regardless of triangle size or visual LOD.
Materials are neither selected by rendered normals nor interpolated across an
entire coarse mesh triangle. The interpolated quantity is surface-relative depth,
not layer identity, color or natural height. Density additions with no original solid contributors
use dirt. The shader does not upload or resample edited density: it presents the
procedural material field attached to the existing edited surface. CPU queries additionally
report actual air and buried grass from the current density snapshot. Explicit
material placement, grass growth and independent covering volumes are not present.

All colors share a world-anchored 16-unit checker projected onto the dominant
surface plane. Pixel derivatives fade distant checks to their mean. Negative
coordinates use floor, with no chunk-local phase. Palette capacity grows linearly
with definitions (32 bytes/ID), independently of chunk count. The fixed eight-node
blend does not grow with the total number of registered materials.

## Alternatives and budgets

A subclass, component or engine material per block type scatters behavior and
multiplies rendering submissions. Dense material arrays would spend storage on
derivable data and introduce persistence obligations. Embedding classification
or colors in each mesher couples material growth to geometry.

The first vertex-height/surface-angle candidate was rejected: excavated dirt
changed to stone and its color boundary depended on surface geometry. Sampling
actual material nodes in the pixel shader preserves local identity and bounded
blending. Sampling at the raw coarse triangle position was subsequently rejected:
it exposed underground dirt where a hillside triangle dipped below the fine
surface. Surface-relative depth removes this geometry error without thickening
the grass layer or painting all upward-facing terrain green.

Surface attachment costs one canonical height query per rendered vertex; its
pixel reference reuses the four existing node-column queries. This cost
requires the recorded figure-eight comparison; no performance acceptance is
claimed yet. A wider vertex format would cost memory for every resident vertex
and touch sensitive compute emitters. The current 24-byte format is retained.

Depth reconstruction uses a fixed base-lattice approximation to analytic height
between columns; that difference is independent of the visual LOD. It also
inherits the detail actually present in the mesh vertices.
It does not restore edits or cave topology omitted by a coarse lattice, and
interpolated depth on mixed cave/exterior triangles remains approximate. Steep
fine surfaces can still show locally blended neighboring strata; the correction
does not redefine the underlying material nodes. Both regular and transition
draws share the same shader and interpolation rule.

Catalog and query code are immutable/thread-safe. Only the render adapter owns
GPU resources. Recipe replacement follows the existing derived reset. Terrain
edits regenerate geometry, exposing the existing strata. There is no independent
material invalidation queue or world cache.
