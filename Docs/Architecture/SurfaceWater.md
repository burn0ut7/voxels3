# Static surface water

Status, 2026-09-10: connected river appearance approved by the user; hillside bank
refinement and full runtime acceptance remain in progress. Current generation is42,
river recipe13, water recipe7. [The drainage design](../Plans/RiverDrainageBasins.md)
records the branching method that the user explicitly asked to preserve.

## Canonical terrain and medium

River13 adds width-biased, seeded bed depth. At each curve endpoint depth is
clamp((48 + halfWidth*0.125)*(1 + 0.65*seededSimplexXY),24,216), with independent
depth noise at4096-unit scale. Depth interpolates along segments and retains the
existing parabolic cross-section. Broad rivers tend deeper but may have shallow
stretches. The graph, widths and water elevation are unchanged. CPU endpoints
store two extra floats; GPU endpoint texels reuse their former uniform elevation
component for depth, keeping texture size unchanged. CPU and GPU interpolate the
same depths; no additional noise evaluation occurs per terrain query. All terrain
lower bounds use the216-unit maximum. Generation42 saves are separate from41;
existing edits are not silently applied to the changed base. Runtime qualification
is pending.

SeaLevel defaults to0 and supports[-8192,8192]. Rivers and oceans share this exact
surface elevation. RiverNetwork carves a varying-width bed and valley into the
base exterior before caves, cliffs, meshing and collision. This is part of the
procedural field, not a later visual cut. TerrainField remains the single authority
for player corrections. RiverWorld owns bounded immutable numerical derivatives
of the seed/settings/version; GPU textures and render geometry are disposable.

SurfaceWater.Resolve gives solid density<=0 priority. Otherwise, points above the
carved pre-cave bed and strictly below the shared water surface are Water; remaining
space is Air. Exact water height is a boundary. Caves below the original carved
bed stay dry. Static water does not reroute or propagate after excavation.
ProceduralVoxelMaterials uses this same carved height and water level for medium
and submerged soil. InspectTerrainColumn reports both natural and carved heights.

## Rendering and publication

Local-edit handoff (2026-09-10, visual validation pending): invalidating a water
generation request no longer retires its published surface. The manager retains
the previous immutable draw result while that spatial chunk remains active and
terrain-resident, until current water data and matching terrain are both ready.
Ready replacements take precedence by region key, including dry replacements;
there is never a second published water version for the same region. Unloading,
changing the recipe/layout or field epoch still retires the previous result.
This retains at most one previous result per active region and uses a reused key
set only during publication changes. It changes presentation lifetime, not cell
generation, authority, or water behavior after digging.

Chunk upload revision (2026-09-10, validation pending): each nonempty published
water chunk owns one derived GPU vertex buffer and one bounded scene draw object.
VoxelManager selects immutable generated results using committed terrain residency;
the renderer retains matching descriptors, uploads new descriptors once, and
disposes retired objects. Empty chunks have no draw object. Generation and the
cell extractor are unchanged. GPU allocation/upload/deletion happen on the engine
thread; drawing and presentation readiness share the renderer's lock. Normal
scene bounds provide per-chunk culling without changing the active partition.
This replaces the aggregate vertex buffer and full-world CPU concatenation, which
copied millions of unchanged vertices on publication. The measured candidate had
a122.7MB maximum frame allocation and160.8ms maximum frame stall; these motivate
removing the known large allocation path but do not alone attribute every stall.
The tradeoff is more draw objects/submissions (bounded by nonempty active chunks).
Validate unchanged emitted geometry, one-time upload counters, retirement, steady
memory and the same figure-eight. A shared arena would reduce objects but require
a new allocator; the prototype first measures direct chunk ownership. No old
aggregate fallback remains.

Water occupies canonical chunk cells through SurfaceWater.Resolve and the Water
material, with solid density taking priority. SurfaceWaterGeometry merges their
coplanar exposed tops within each committed, active, resident chunk. A chunk
intersecting SeaLevel owns its exposed clipped cell faces; a face exactly on
a Z boundary belongs to the volume below it. There is no world-sized visible
rectangle or fallback surface for unloaded chunks. The same active LOD partition
as terrain prevents overlapping chunk faces. Empty-solid resident chunks may
still contain water cells and therefore can own a water surface.

The cell-data prototype below replaces the full-chunk fragment-clipped water plane.
The water shader draws generated geometry and palette/checker colors only. It has
no procedural includes, river texture, world recipe, or wet/dry clipping query.
Resident data and geometry follow the committed chunk partition; bootstrap admits
completed chunks incrementally and staged handoffs wait for matching water data.

GpuRiverAtlas shares completed numerical transports across generation scratch lanes. A lane first reuses its own covering texture; otherwise
reuse requires the exact patch footprint that canonical Capture would pack, with
matching settings and generator/river versions. This preserves each GPU request's
search-tree size. A broader shared tree was rejected after a controlled moving-FPS
regression. The completed-data LRU retains at most64entries and64MiB; individual
transports remain subject to the128MiB transport cap, with larger entries uncached.
Recipe changes clear the LRU. Cache access/publication uses a short lock; numerical
packing runs outside it and rechecks cancellation before publication. No cancelled
tasks or GPU resources are shared. A miss uses the same canonical preparation path.
Diagnostics expose packs, reuse count, packing time, entries and retained bytes.
This addresses repeated sorting/packing measured in the user's215942CPU capture;
it does not change visible water ownership.
The exact-footprint cache is currently a performance candidate: compilation and
density audits pass, but clean figure-eight FPS acceptance remains pending. See
RIVERS-015 in the validation ledger for rejected and noncomparable runs.

## Identity and limits

The remaining coarse solid/water mismatch and the required shared-partition
changes are scoped in [river terrain resolution](../Plans/RiverTerrainResolution.md).
That document is an investigation/design boundary, not implemented behavior.

### Cell-data prototype (in progress)

Inputs are the existing recipe, immutable regional edit reader, chunk coordinate,
32-cell layout and LOD spacing. GeneratedWaterCells fills water-density columns
(negative inside liquid), separate from solid density, and a33-squared
free-surface clearance array. Unedited columns use exact bottom/sea-level runs;
DensityAt decodes max(bottom-z,z-sea) without any procedural query. Edited chunks
retain33-cubed corrected density samples. The clearance array retains a shallow liquid crossing when
coarse Z lattice samples straddle its entire depth. SeaLevel, generation39 and
River10 were unchanged for the measured storage prototype. The user-requested
River11/12 width and bend corrections are documented in the drainage design. The prototype materializes chunks owning the visible
free surface; it does not yet replace all solid/material storage or disk format.

The unedited compression relies on the current composition contract: caves and
cliffs only subtract solid (both return a density no lower than the original
surface density). A point above the pre-cave bed cannot become solid through
these operations. This proves that the unedited bed/sea run obeys solid priority;
no second cliff/cave query is needed for water there. Additive edits break that
property and therefore use the corrected density/clearance path. If a future
generator adds solid above the bed, this compression contract must be revisited.

VoxelManager owns bounded committed/staged water-chunk requests and immutable
results. One worker generates and meshes at most8chunks per batch. Requests use
the existing regular descriptor's regional revision and epoch; edit readers pin
only required pages. Stale completions are discarded; reset cancels work. Leaving
the committed/staged union releases cells and meshes. Handoffs wait for their
water data, while bootstrap publishes completed cells only with resident terrain.
The maximum current/staged request count is bounded by existing visual coverage;
an unedited payload uses8712 bytes, and a dense edited payload152460 bytes (exact
accounting comes from Bytes). WaterStatus reports retained sample storage. Water
geometry uses12-byte position-only vertices; all emitted vertices and clipping
remain the same as the initial dense prototype.

SurfaceWaterGeometry reads generated free-surface samples, linearly clips shore
triangles and merges fully wet cell runs within an owner chunk. It never calls
generation. SurfaceWaterRenderer uploads cached geometry; voxel_water.shader has
no river, noise or landform includes/queries, just water palette/checker shading.
GPU river transport is now used only during solid density/material generation.
The terrain draw shader also consumes generated material weights; its procedural
material lookup and river cutout have been removed.
The earlier full-chunk fragment-clipped water implementation is removed.

Prototype risks requiring actual validation: shoreline interpolation/LOD detail,
memory cost of retained density samples, CPU generation throughput, edited-water
publication and coarse shallow rivers. These are not accepted tradeoffs. No
frequency, width, view-distance or workload reduction is part of the prototype.

The current adaptive-water slice additionally stores smaller surface cells inside
coarse owner cells. Generation uses the existing river spatial index's conservative
wet-support bounds to find channels even when all four coarse corners are dry.
Potential hidden reaches subdivide until the cell size is at most half the
narrowest overlapping reach radius, with16-unit base spacing as the lower bound.
This supplies at least four cells across a reach's full width except features
below the base lattice limit. Ocean coasts outside river support keep their normal
LOD. Fully wet cells retain coarse coplanar coverage. Each retained leaf stores
four bed heights and free-surface clearances; meshing reads these stored leaves,
without procedural queries. The existing coarse volume samples remain available;
these leaves refine the free-surface representation, not the terrain's solid mesh.
This is an incomplete stage toward shared fine cell extraction: rock bridges from
coarse solid topology remain a separate known failure and must not be hidden.
Refinement has at most6 levels with the current16..1024 spacings; storage is48
payload bytes per leaf plus dictionary/array overhead. WaterStatus reports leaf
count and payload bytes. Worker cancellation is checked at every refinement node.
Dry refinement leaves have implicit empty payloads. The earlier attempt to refine
all mixed coastlines to16units was rejected for740MiB payload and53s generation;
the ledger preserves that failure. The worker yields between chunks and explicitly
resumes on a worker thread before generation. This follows the engine's
[hotload concurrency requirement](https://sbox.game/dev/doc/code/advanced-topics/hotloading);
the rejected candidate demonstrated that cancellation checks alone did not let
hotload pause a long synchronous batch.

### Required cell-data direction (user clarification, 2026-09-09)

The user requires generated features, including rivers, to become chunk cell
data during generation. Meshing must consume the generated density and medium /
material data; drawing a completed chunk must not rerun river or terrain generation.
Water must load, invalidate, and unload with its owning chunk. River graph layout,
branching, width variation, and carving belong exclusively to generation.

The prototype above addresses generation-owned water samples and generated solid
material runs. VoxelChunk logical queries still evaluate the canonical field, and
TerrainFieldPage still persists corrections rather than generated cell payloads.
Those storage/query responsibilities have not silently changed with rendering.

The data contract must distinguish solid density from medium/material: water
cannot be encoded as negative solid density, which would make collision treat it
as rock. Generation supplies both through the canonical field. Generated chunks
need bounded resident storage, uniform-region compression, shared boundary sample
rules, revisioned invalidation and a water surface extractor consuming that data.
LOD must retain thin water volumes even when their depth is below sample spacing.
Existing edits and saves must remain valid or require an explicitly versioned
migration; no silent reinterpretation of correction pages as full cell payloads.
Removing per-fragment generation is a measurable acceptance requirement, alongside
the unchanged river shape, streaming, edit correctness and figure-eight criteria.
Further cache tuning alone is not completion of this requirement.

TerrainField identity includes generator and water versions plus the immutable
settings, including SeaLevel. Existing save selectors/manifests use that identity;
incompatible recipes are rejected rather than silently migrated. No independent
river save format or mutable river network exists.

This is static terrain generation, not a fluid simulation. No swimming, volume
conservation, dynamic flooding, waterfall physics or stacked cave rivers are
implemented. A submerged drainage seed may be an inland lake, and finite basin
boundaries are watershed divides. [The validation ledger](../ValidationResults.md)
retains rejected earlier shapes, visual approvals, actual measurements and all
remaining acceptance gaps; historical sea-only behavior is not the current contract.

2026-09-12 cache reuse candidate: generated water cells remain cached while their revision-matching owner lies in the committed or staged terrain cache, including temporarily hidden cells. Active requests alone determine readiness and publication. Cells outside both owner caches or with stale field identity are pruned. This reuses generated cells across changing LOD holes without generating water for speculative hidden owners.

### Boundary-local water readiness (2026-09-12 candidate)

Placement handoff checks only sea-level cells in ActiveEntering against current
descriptor identity. Retained visible water keeps its normal generation and
publication path and cannot block an unrelated boundary switch. The global
SurfaceWaterPrepared diagnostic still requires all requested cells and remains
the complete-settle predicate. Geometry generation and rendering ownership are
unchanged; this narrows a placement dependency rather than changing water data.

Status: the boundary-local readiness and deferred-cache experiments above were
rejected and reverted after both increased work and allocations. Current runtime
retains the original atomic boundary handoff with exact cancellation ownership
and bounded water reuse. See the validation ledger for preserved measurements.

## Independent target publication (2026-09-12 candidate)

Supersedes terrain-resident water publication above, following the explicit user
request. Water requests use the canonical final target anchors/extents directly,
with the same half-open child holes and sea-level Z owner, without waiting for
terrain placement. The target partition owns exactly one water LOD per location.
Each completed requested chunk can draw immediately. The coverage handoff below
retains existing water in unfinished footprints. Terrain no longer waits for water either. The
network field-presentation barrier remains authoritative and unchanged.

One worker generates one immutable chunk at a time, selected by XY distance to
the player (fine LOD breaks equal-distance ties). Main-thread integration rejects
results no longer requested at that exact descriptor identity and immediately
updates the existing per-chunk renderer. Reset cancels the worker. No thread
reads mutable placement or creates GPU resources. Seed, field revision, bounds
and sample spacing determine data; scheduling order never changes a chunk.

Generation removes the unused dense interior water samples and DensityAt method;
SurfaceWater.Resolve remains the medium authority. All surface and bank-refinement
samples remain unchanged. Mesh extraction merges fully wet unrefined cells in
two dimensions, never crossing a refined cell. Unambiguous shore cells use one
clipped polygon, joining boundary crossings directly without a diagonal kink.
Opposite-corner ambiguity keeps the existing diagonal connectivity. Shared edge
interpolation uses canonical endpoint order. No world/river/save recipe changes.
Previously unseen areas can remain absent during loading; existing coverage is
retained as described below. Terrain LOD publication is not redesigned.

Cache correction: CPU water results and existing GPU draws now share the water
final-target cache bounds, including hidden child holes, rather than lagging
terrain bounds. Requested descriptors and locally clipped fallback coverage render. An inactive draw retains its
buffer while its exact descriptor remains in that bounded cache; no hidden mesh
is generated speculatively. Layout changes and local field invalidation retire
unowned descriptors. This avoids re-uploading a previously visible parent when
a moving fine hole exposes it again. Source sampling and publication independence
are unchanged. Runtime comparison remains in the ledger.

Requested simplification also reduces adaptive water-bank refinement to two or
more samples across the intersecting river width (cell size <= local minimum
wet radius), retaining the16-unit minimum spacing. Near LOD0 sampling is unchanged.
This deliberately approximates distant shore contours with fewer straight
segments. Region coordinates, field samples at shared positions, and refinement
criteria remain deterministic; medium queries and saved terrain are unchanged.

`voxel_water_info level x y` inspects the actual generated sea-level chunk without
generating it: requested/cache state, vertex count, deterministic position hash,
bounds/finiteness, winding and zero-area triangle counts. Water emission drops
exactly zero-area triangles, including duplicate clipped endpoints at zero
clearance. This changes no finite-area surface.

## Continuous local water coverage (2026-09-12 candidate)

A target-layout change previously retired an old water LOD before its local
replacement existed, producing movement-related gaps. The manager now owns a
separate derived presentation partition: disjoint quadtree tiles reference the
immutable generated chunk that supplies their water. Each requested ready chunk,
including an empty/dry result, replaces exactly its own footprint immediately.
Missing requests retain intersecting old coverage; a coarse source can cover
unfinished fine tiles, and fine tiles can remain until a coarse result exists.
Ancestor lookup and an indexed branch set bound traversal to existing coverage.
Negative coordinates use arithmetic shifts for floor-divided parents. Different
recipe/epoch/layout identities cannot be retained; local edits may retain their
last presentation until their replacement arrives. Only final requested areas
retain tiles; fallback references keep their CPU/GPU source alive until replaced.

The renderer uploads one buffer per source descriptor and draws its coverage
tiles with half-open world-XY shader clipping. Empty replacements remove fallback
coverage too, avoiding overlapping coplanar water. Buffer reuse avoids remeshing
or re-uploading a retained source. Temporary subdivided fallback may need several
draw calls from one buffer; this cost requires the fixed runtime comparison.
A four-child atomic handoff was not chosen because it would delay an independently
ready child. Generation and terrain publication remain independent. This contract
is source behavior; visual and performance acceptance remain in the ledger.
