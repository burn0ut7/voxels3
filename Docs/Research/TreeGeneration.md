# Tree generation: unique meshes or reusable variants

Research date: 2026-09-18. Recommendation only; no tree implementation or
performance acceptance. Source checkout: `3a3993404dc78863ff11cfd407ded352e6a9a184`
with concurrent terrain, water, grass and material changes. Those changes were
not modified or benchmarked by this research.

## Recommendation

Generate a curated library of distinct tree models and reuse them with
deterministic per-tree variation. Start by evaluating 16-32 visibly different
silhouettes per species, distributed across age/size classes. This count is an
experiment starting point, not evidence that repetition will be invisible.
Prefer producing and inspecting the library before shipping; generating a
bounded library once per world is an alternative if world-specific species
variation proves worthwhile. Avoid rebuilding identical library entries on
every region visit.

Unique per-tree geometry is technically plausible, particularly with bounded
branching and low geometry counts. No measured evidence yet establishes that
it fits Voxels3's streaming and rendering budgets. Generation time alone cannot
settle this choice: retained geometry, upload spikes, shadows, leaf coverage,
LOD and collision also matter. Reconsider unique trees if visible repetition
survives good library design, or unique branch structure becomes gameplay.

## What actually differs

| Approach | Generation and memory | Rendering and visual tradeoff |
| --- | --- | --- |
| Unique mesh per tree | Build and retain each resident tree's geometry; repeated visits require rebuilding or bounded caching. | Unique branch topology; loses ordinary same-model instancing across whole trees. Shared materials alone do not restore mesh reuse. |
| Generated library | Build each variant once; many placements share geometry. | Straightforward instancing and reusable LOD assets; distinctive repeated forks/crowns can reveal reuse. |
| Shared topology with per-tree deformation | Share base geometry and store compact parameters. | Different proportions/bends can retain instancing, but arbitrary new branch topology is unavailable without another technique. Custom shading, bounds and collision agreement need verification. |
| Unique arrangements of reusable branches/leaf clusters | Reuse parts but store many transforms per tree. | Potential middle ground; multiplying part instances, seams, culling and LOD complexity can erase the benefit. Not the first slice. |

A shared geometry buffer or indirect drawing could reduce submission overhead
for unique trees. It does not eliminate their geometry storage or shading cost.
Conversely, instancing does not make all those rendered leaves and shadows free.

Illustrative arithmetic, not an asset measurement: a tree with 10,000 vertices
at 32 bytes and 30,000 four-byte indices uses 440,000 bytes of geometry. At
10,000 simultaneously resident unique trees that is 4.4 GB decimal. Thirty-two
shared variants use 14.08 MB plus placement records. Both omit textures, extra
LODs, physics, engine overhead and staging copies. Streaming reduces resident
counts for either approach; rendering fewer triangles does not automatically
evict a retained high-detail mesh.

## Generation techniques

[Runions, Lane and Prusinkiewicz (2007)](https://algorithmicbotany.org/papers/colonization.egwnp2007.pdf)
grow branching skeletons toward attraction points inside a crown region.
This is a useful candidate for generating varied broadleaf crowns and library
assets. Iterative attraction/nearest-branch work needs explicit limits if used
during streaming; skeleton construction is only part of producing renderable
wood, leaves, normals and LODs. The paper is algorithm evidence, not a modern
s&box timing result.

For a runtime candidate, propose a simpler seeded recursive branch recipe:
bounded depth, branch count, segment count and leaf-cluster count, with
species-specific angle, taper and crown controls. This is an engineering
proposal, not a benchmark-backed algorithm selection. A hard output cap makes
the amount of geometry controllable; it does not guarantee a frame-time budget.
Growth simulation is unnecessary merely to obtain varied final shapes.

[Deussen et al. (1998)](https://graphics.stanford.edu/papers/ecosys/)
explicitly replace similar procedural plants or plant parts with representative
instances. This supports treating generated diversity and geometric reuse as
compatible goals. Its offline ecosystem rendering is not evidence of current
real-time performance or a perceptual minimum number of variants.

## Verified s&box evidence

[GPU instancing documentation](https://sbox.game/dev/doc/rendering/shaders/gpu-instancing)
states that compatible copies of the same model/material batch automatically.
Transforms and tint can vary; the documented standard extra-instance data does
not promise arbitrary branch parameters. Procedural instance IDs offer a path
for custom shaders, but a tree implementation must verify its data delivery,
bounds, lighting and shadow behavior.

Installed XML snapshot identifies build `26.09.15`, `35012526502`, `build-pr`.
`Sandbox.Engine.xml` documents
`Graphics.DrawModelInstanced(Model, Span<Transform>, RenderAttributes)` and
`DrawModelInstancedIndirect` overloads. This confirms installed documentation,
not a compiled project integration or throughput measurement. The documented
transform-slot ceiling is not a practical tree budget.

[Facepunch's January foliage update](https://sbox.game/news/update-26-01-28)
describes leaf transmission, improved normals, distance-dependent alpha cutoff,
trunk sway and leaf flutter. Evaluate the supplied foliage shader before
building another one. The announcement does not prove custom generated meshes
carry its required attributes or that it meets this project's forest budget.

## Making reuse difficult to notice

Prioritize different crown outlines, trunk forks, lean and branch distribution.
Rotation, restrained scaling and tint variation are secondary: recoloring an
identical conspicuous fork does not disguise it. Include saplings and mature
forms instead of stretching one model across every age. Keep variation within
species rules so randomness does not produce implausible trees.

Use stable neighborhood selection to avoid adjacent repeats; mix sizes and
species according to habitat, with gaps and clustered growth. Assess isolated
roadside trees and forest edges as well as dense canopy. A combinatorial count
of seeds/colors/angles does not measure how many distinct trees players perceive.

If later needed, reserve unique silhouettes for prominent landmarks. Distant
LODs must preserve enough crown shape to avoid visible swaps; unique near trees
cannot simply become unrelated distant presets without checking transitions.

## Project boundary

The existing [biome proposal](BiomeTerrainGeneration.md#5-later-forests-grass-and-surface-appearance)
owns placement: global candidates, stable identity, bounded neighboring checks,
terrain support and budgeted realization. This note owns the geometry choice.
Scoped searches found no tree-generation implementation in Code or Assets.
Current grass is separate derived rendering, not an existing tree subsystem.
Some foundation prose predates current water/grass changes; do not interpret
its historical absence statements as current source facts.

Propose separate tree objects supported by the terrain, rather than embedding
all wood/leaves in the terrain SDF. Arbitrary voxel carving of wood would change
this decision and needs its own design. Simple felling does not require unique
meshes or voxelized trees.

Base identity/shape derives from world seed, population version, species recipe,
stable candidate ID and explicit random channels. Geometry/LODs are disposable
derived data. A future authoritative tree-state owner records persistent
destruction by ID; client rendering quality must not change gameplay identity.
Share compact recipes and authoritative changes, not every generated vertex.
Matching seeds alone do not prove cross-client deterministic reconstruction.

Follow terrain revision/cancellation boundaries for placement jobs; reject stale
support results. Keep tree generation out of the terrain publication critical
path. Prepare plain geometry off-thread only where verified safe and apply engine
resources on supported threads. Use bounded resident caches and integration work.
Nearby trunk collision should derive from the same shape description; foliage
needs no collision unless gameplay requires it. Multiplayer player spread can
increase authoritative collision interests independently of one client's view.

## Evidence needed before choosing unique trees

No runtime benchmark was performed: this task changes documentation only.
Before implementation testing, freeze a scenario and pass criteria in
[ValidationResults](../ValidationResults.md). Preserve the canonical figure-eight
unchanged and compare against its latest comparable accepted baseline, or capture
one first. A supplementary forest scenario needs its own recorded parameters.

Freeze seed, placement density/counts, species recipe, geometry/LOD budgets,
visible distance, shadows, resolution, hardware/build, player count/spread,
camera path, warmup and run duration. Compare library and unique geometry in
sequential experiments through the actual playable world; retain one selected
production implementation, not alternate shipping or test-only paths.

Measure shape generation separately from mesh creation/upload, resident and peak
CPU/GPU memory, allocations, stationary CPU/GPU frame time, frame-time p95/p99,
cold streaming and revisit completion, and terrain streaming regression. Test
collision/support, negative coordinates, boundary ownership, stale jobs and
revisit identity. Inspect dense canopy, isolated silhouettes, shadows and LOD
transitions. Evaluate perceived repetition with fixed views and normal traversal.

Choose unique geometry only if its visible/gameplay benefit justifies its
measured incremental cost within the frozen budgets. If the library still
repeats visibly, first identify whether the issue is shape coverage, neighboring
selection or insufficient variants. There is currently no defensible trees-per-
second estimate, FPS claim, or guaranteed invisible-repetition library size.
