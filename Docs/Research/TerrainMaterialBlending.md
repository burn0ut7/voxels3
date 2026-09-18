# Natural terrain material transitions

Research date: 2026-09-18. The initial design below preserves small patches.
The user subsequently authorized a prototype and explicitly accepted losing tiny
patches visually. Candidate F now exists in the workspace: quadratic reconstruction
of neighboring columns, local noise-shaped mixing and matching grass acceptance.
B retained stepped outlines; C/D explored wider blending; E reduced sample count.
F skips invisible noise while retaining E's appearance. The final prototype is
accepted after fixed-view/detail-change review and a passing matched repeat pair.
The first standing p99 failure and the observed run-to-run variation remain
disclosed in the validation ledger and evidence index.
The coverage cache and texture-height blending are deferred for this slice.
See [prototype contract](../Architecture/VoxelMaterials.md#material-transition-prototype-2026-09-18)
and [validation history](../ValidationResults.md#material-transition-001v1--local-blending-prototype-2026-09-18).
The original recommendation and source observations below describe the research
baseline, not the candidate's implemented reconstruction.
Current ownership remains in [Voxel materials](../Architecture/VoxelMaterials.md)
and [procedural sand](../Architecture/ProceduralSand.md).

## Recommendation

Keep the existing discrete material identity and generation recipe. Derive a
continuous, spatially detailed coverage field from neighboring material samples,
and use texture relief to shape the mixture within its boundaries. Material
coverage must survive independently of render-triangle size. Grass population
should consume the same coverage so blades thin out along the ground transition.

This separates three scales: the generated deposit's footprint, the local
transition between neighboring cells, and the grain/clump detail within that
transition. Broad smoothing would destroy the small deposits the user wants to
retain. Adding noise to today's vertex weights cannot recover deposits that
were never represented by those vertices.

## What the current world establishes

The live basic_example camera on engine 26.09.15 showed pale sand patches with
repeated straight segments and sharp corners, including submerged patches. This
was a read-only visual observation, not a benchmark or an isolated LOD diagnosis.
No camera, world, material, or runtime setting was changed for this research.

Source evidence:

- [ProceduralVoxelMaterials](../../Code/Voxels/Materials/ProceduralVoxelMaterials.cs)
  resolves canonical density, medium, placed overrides and procedural solid IDs.
  [The inspector](../../Code/Voxels/VoxelManager.Materials.cs) queries the containing
  lattice node using floor, including negative coordinates. Preserve these
  semantics; do not silently redefine IDs around new cell centers.
- [GenerateVoxelMaterialWeights](../../Assets/shaders/voxels/voxel_generated_materials.hlsl)
  reconstructs four 16-unit XY columns and two vertical contributors per column,
  omits air and normalizes five solid weights. Four weights are stored explicitly;
  sand is the remainder. Packing preserves the rounded total.
- [Regular extraction](../../Assets/shaders/voxels/voxel_persistent_geometry.hlsl)
  and [transition extraction](../../Assets/shaders/voxels/voxel_transition_geometry.hlsl)
  evaluate this at refined edge intersections and apply placed-dirt contributions.
  The retained draw payload is vertex weights, not a pixel-addressable material
  volume. Documentation about generated material runs must not be interpreted as
  evidence that such a retained volume already exists.
- [The terrain shader](../../Assets/shaders/voxels/voxel_terrain.shader) interpolates
  those weights and linearly combines color, normals, roughness and occlusion.
  Consequently, material variation inside a triangle is limited by its vertices.
  A patch between vertices can disappear even if generation knows it exists.
  This is an architectural limitation; this observation did not measure the LOD
  responsible for each visible corner.
- [Grass generation](../../Assets/shaders/voxels/voxel_grass_cs.shader) rejects
  candidate roots below 0.75 interpolated grass weight. The threshold can create
  a separate population edge even after ground blending improves.
- Height source images exist in
  [the material](../../Assets/materials/voxels/voxel_terrain.vmat), but the current
  two compiled maps per surface pack color/roughness and normal/AO. They do not
  carry height into sampling. Grass's 4096-square baked pattern currently includes
  four maps and no height, as its
  [manifest](../../Assets/textures/terrain/grass_pattern/manifest.json) records.
  Sampling the original grass height at baked-pattern coordinates would misalign
  it with the visible grass. Height must use the same baking/transforms.

## Real-world comparison and art direction

The National Park Service describes scattered plants on beaches, sand trapped
by vegetation, and established grasses growing in clumps. Its Indiana Dunes
photos provide a useful reference for interlocking grass/sand coverage rather
than a constant-width, uniformly mixed ribbon.
[NPS plant succession](https://www.nps.gov/indu/learn/nature/plant-succession.htm)

[Open-dune photograph, Joe Gruzalski/NPS](https://www.nps.gov/indu/images/IMG_8001_1.JPEG?autorotate=false&maxwidth=650)
and [foredune photograph, Joe Gruzalski/NPS](https://www.nps.gov/indu/images/beachy-shore.jpeg?autorotate=false&maxwidth=650).
These illustrate coastal vegetation, not a universal recipe for every river bank
or submerged deposit. The following are proposed visual treatments, not claims
that one physical process describes all terrain:

| Boundary | Proposed treatment |
| --- | --- |
| Grass / sand | Irregular grass clumps with sand between them; fewer blades toward sand, preserving clear sand and grass interiors. |
| Sand / dirt | Fine granular mixing with gentle coverage changes; retain narrow sediment streaks and isolated deposits. |
| Sand / stone or dirt / stone | Fine material visible in low parts of the rock texture while exposed high parts remain recognizable. |
| Snow / stone | Uneven covering with protruding rock detail; tune independently from loose sediment. |

Natural boundaries can also be abrupt. The target is removal of grid/triangle
patterns, with transition character appropriate to the materials. Wetness is a
separate response; darkening everything beside water is not this task's solution.

Epic's height-blend documentation provides a concrete rendering analogue: relief
can make dirt show between rocks during a material transition. It also documents
zero-contribution/invalid-normal failure cases. Adopt the principle of relief-
guided mixing and explicit normalization, not Unreal-specific nodes or APIs.
[Epic landscape materials](https://dev.epicgames.com/documentation/unreal-engine/landscape-materials-in-unreal-engine)

## Proposed data and rendering contract

1. **Identity remains authoritative.** Mining, placement, saving, replication and
   material queries use the current material rules and stored overrides. A
   visually mixed boundary does not author a mixed gameplay cell or change the
   world-generation recipe.
2. **Generate coverage alongside terrain work.** Inputs are the immutable recipe,
   authoritative density/material snapshot, world coordinates and source revision.
   Derive normalized solid weights with a compact neighboring-sample footprint.
   Air/water are not solid texture layers. Preserve source materials at their
   canonical samples and retain isolated one-cell features; do not select only
   the two strongest types or introduce an automatic dirt strip.
3. **Begin with a narrow support radius.** One base spacing is 16 units / 0.4064 m.
   A first prototype should explore no more than one spacing of additional
   boundary influence, with fixed values declared before runs. This is a visual
   starting scale, not a measured natural width or an approved scenario. Broader
   smoothing requires evidence that narrow patches remain visible. Filter choice
   must satisfy nonnegative weights, normalized sum, pure-region preservation
   and canonical-sample retention; generic blur does not satisfy these by itself.
4. **Sample coverage independently of geometry LOD.** Use world XYZ to address
   retained derived data. Select material filtering from the projected footprint,
   preserving small patches while they remain resolvable and their aggregate
   coverage once subpixel. Literal full detail at every distance is neither
   possible nor desirable: unfiltered subpixel detail shimmers. Geometry LOD must
   not decide which material exists.
5. **Add bounded detail only where materials overlap.** Stable world-coordinate
   variation shapes local edge irregularity. Matched texture heights bias the
   relative coverage so individual clumps/rock details stay legible. A zero base
   contribution must stay zero; normalized output must remain valid at three-
   and five-material junctions. Fade unresolved detail with the pixel footprint,
   retaining the underlying coverage. No parallax, displacement or extra geology
   evaluation in the fragment shader is needed for this behavior.
6. **Use one resulting mixture for all surface properties.** Apply final weights
   to color, normal, roughness and AO, with valid normal normalization. Ground and
   grass roots share the macro coverage and stable breakup rule. Replace the
   hard grass population cutoff with deterministic coverage-dependent acceptance
   while retaining existing candidate/capacity limits. Any material-support bound
   used to reject a whole triangle must conservatively include interior patches.

Height source amplitudes are normalized scan values, not comparable geological
elevations. Calibrate per-material relative influence for appearance. A height
blend is a shading approximation; it does not simulate sediment transport,
vegetation succession or snow accumulation.

## Representation and lifecycle decision

The recommended first representation to evaluate is a **sparse 3D coverage cache
near rendered surfaces**, independent of mesh triangles. It naturally distinguishes
exterior, cave floor and ceiling sharing XY. Keep the current five-solid encoding
for this slice; additional material types require a separate encoding decision.

This is a proposed new derived responsibility, not a second world-state model:

- GpuVoxelMaterials should own coverage residency, addressing and resource lifetime;
  GpuVoxelMesher requests and publishes revision-matched geometry/coverage.
- Preparation reads immutable field snapshots. GPU resources are created,
  dispatched and retired through the existing engine-thread boundary; workers
  cannot mutate scene or GPU resources directly.
- Density/material edits invalidate intersecting bricks plus the actual sampling
  halo. World/recipe changes invalidate the corresponding generation. Neighboring
  bricks use identical global coordinates and halo values. Negative addressing
  uses floor. Stale work is rejected by source revision and allocation generation.
- Share coverage between regular and transition meshes and the grass consumer.
  Geometry LOD changes reuse the same spatial field; a recipe/edit revision does
  not reuse stale weights. Bricks referenced by active draws cannot be recycled.
- Preserve canonical surface correspondence when coarse geometry moves away from
  the original surface. Sampling raw world XYZ alone can put a coarse triangle
  under a thin grass layer and reveal dirt incorrectly. The prototype must bound
  this offset and carry/reconstruct the matching canonical surface location,
  including distinct cave sheets. Do not solve it by thickening gameplay layers
  or projecting every surface onto the exterior heightfield. This is an unresolved
  representation gate; surface tiles remain the alternative if reliable volume
  correspondence costs more than they do.
- Coarse coverage is filtered from the same field. While finer pages are pending,
  use a valid parent of the same revision or retain a previously valid published
  pair. New edits must not combine new geometry with invalid old coverage. Bound
  replacement latency and account for overlapping old/new storage.
- The cache is disposable and local. Save/network payloads continue to carry
  canonical state. Matching source rules do not establish multiplayer parity;
  actual joins and edits still need qualification.

This design retires edge-only material reconstruction as the canonical visual
coverage path once all consumers migrate. Do not leave vertex-driven grass and
cache-driven ground as independently evolving implementations. A retained
conservative summary for culling is derived from the same coverage.

| Alternative | Decision for the proposed first slice |
| --- | --- |
| Wider vertex blend or extra shader noise | Insufficient alone: cannot recover an interior patch missing from the vertices. |
| Finer geometry everywhere | Reject as the default material solution; increases geometry, generation and streaming to solve a shading-data problem. |
| One overhead splat map | Reject as the general solution; conflates stacked cave/exterior surfaces. |
| Regenerate terrain/material rules per fragment | Reject; repeats expensive generation every frame and abandons the existing generation-time boundary. |
| Surface-addressed texture tiles | Serious alternative if sparse-volume costs fail. Saves empty space but needs robust projections, tile gutters, multiple sheets, edits and LOD correspondence. |

Godot Voxel documents cell-addressed normal-detail atlases that work with overhangs.
That supports investigating surface tiles, but does not demonstrate material
coverage caching or its cost in s&box. Its generation, memory and edited-streaming
limitations are relevant. See the existing
[texturing comparison](TerrainTexturingComparison.md) and
[Voxel Tools detail rendering](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/#detail-rendering).

## Cost model and implementation gate

Do not allocate a dense material volume across the full visual radius. Example
payload arithmetic for comparison, not a chosen brick layout: eight samples per
axis plus one sample of halo on either side gives 10^3 samples. At four bytes of
normalized weights this is 4,000 bytes per brick. 10,000 bricks cost 38.15 MiB for base
payload alone; validity/occupancy metadata, page tables, coarser levels, staging,
residency overlap and driver overhead are additional. Invalid/air samples must
not decode as sand simply because all four explicit channels are zero. The
filter's actual halo and sampling convention must
determine the final layout.

Proposed screening envelope: at most 64 MiB for all additional coverage resources,
including pending replacements, with bounded admission and no synchronous GPU
readback. This is a discussion target, not permission to drop visible features
when full. Before implementation, count the real visible mixed/pure surface
working set, choose compression/uniform-brick handling, and verify the necessary
installed s&box resource/update APIs. Pool exhaustion must be observable.

Height maps add bandwidth and residency. A separate 4 bpp height map with full mips
would cost about 2.67 MiB per 2048-square source and 10.67 MiB for 4096-square baked
grass: about 21.33 MiB for all five materials, excluding overhead. This assumes a
supported 4 bpp format, which is not verified here. Height sampling adds one lookup
per contributing projection/patch, potentially many in a mixed pixel. Repacking
existing channels is an alternative only if normal/AO quality is retained.
Use heights in overlap zones and measure; do not claim the operation is free.

## First implementation sequence and acceptance

First establish coverage that preserves canonical material detail across triangles
and LOD, then add close-up relief mixing and matching grass thinning. These are
stages of one replacement path, not shipping alternatives. Preserve the sand
recipe throughout so before/after differences isolate presentation.

Before the first runtime run, declare exact scenario versions, world revision,
camera poses, resolution, kernel/support, cache limits, warmup, duration and
measurable criteria in [the validation ledger](../ValidationResults.md):

- Fixed shoreline reproductions: overhead and grazing views, dry and submerged
  sand, narrow streaks, isolated cells, three-material junctions, and negative
  chunk boundaries. Require unchanged logical IDs and retained resolvable patches;
  inspect native screenshots for geometry-aligned corners and uniform blur bands.
- Move through the same LOD boundaries and revisit them. Material footprint must
  remain stable; no disappearing interior patches, grid seams or camera-locked
  breakup. Quantify boundary movement and retained projected coverage against
  fixed before images with tolerances declared before runs.
- Dig/place through a material boundary and a cave surface; exercise neighbor
  invalidation and stale work. Check canonical IDs, grass agreement, correct
  restoration after reload and client agreement through real production paths.
- Run the unchanged canonical figure-eight with a fresh matched baseline if no
  accepted comparable baseline exists. Record FPS, p95/p99, GPU time, completion,
  streaming, allocations, peak memory and resident coverage. No acceptance based
  on changing the workload or removing detail. Require shader cold-start checks.

No runtime changes, performance run or implementation acceptance were performed
in this research pass. Existing uncommitted rendering/water/grass work is outside
this document's ownership and was preserved.
