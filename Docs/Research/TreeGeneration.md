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

## Wind for the Blender library, September 20

Research recommendation, not implemented animation. The local Blender import
candidate replaces the earlier runtime geometry recipes. Its foliage animation is currently
disabled, exported meshes have no authored wind colors, and wood uses the static
complex shader. Separate parts alone do not make a tree wind-ready.

Keep leaves in the Blender-generated tree, separated from wood by mesh/material
section. Export one reusable tree asset with its LODs. A leaf can have its own
logical attachment and pivot without becoming a separate game object, draw call,
rigidbody or CPU-updated transform. Seeds and branch guides should generate both
the visible structure and animation metadata; regenerating a tree regenerates
both, with no hand-painted dependency on one particular specimen.

### Evidence and transfer limits

- [Crytek's Crysis vegetation chapter](https://developer.nvidia.com/gpugems/gpugems3/part-iii-rendering/chapter-16-vegetation-procedural-animation-and-shading-crysis)
  describes GPU main bending plus leaf detail bending, controlled by wind and
  vertex colors. It constrains deformation to avoid implausible stretching.
  Adopt layered frequencies and stiffness. This is a historical shipped-game
  method, not a claim that every modern game uses the same algorithm.
- [Epic's Pivot Painter 2 documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/pivot-painter-tool-2.0-in-unreal-engine)
  stores pivots, directions and parent relationships for shader animation with
  inherited motion. Logical authoring elements can be combined into a static
  mesh. Adopt attachment-aware data when independent branch motion is required.
  Its Max script and Unreal material functions are not s&box integrations; our
  Blender exporter and shader would implement and validate their own format.
- [Epic's Fortnite Chapter 4 account](https://www.unrealengine.com/tech-blog/bringing-nanite-to-fortnite-battle-royale-in-chapter-4)
  describes offline branch simulation encoded into textures, indexed through
  mesh UVs at runtime, and disabling distant wind evaluation. This is an
  alternative if hierarchical shader evaluation becomes costly. Its rigid branch
  animation, Nanite renderer and content-specific performance cannot be assumed
  appropriate for our forest. Do not copy its mesh budgets or add simulation
  textures before measuring a concrete need.
- [Facepunch's foliage update](https://sbox.game/news/update-26-01-28) documents
  trunk sway and leaf flutter. Installed26.09.15 source gives the exact contract:
  `core/shaders/foliage.shader:99-102` uses red=edge attenuation,
  green=branch attenuation, blue=detail phase. These channel meanings differ
  from the Crysis article. `bark.shader` and `foliage.shader` call the same
  `common/trunk_bending.hlsl`; neither consumes a branch-parent/pivot hierarchy.
  The native helper displaces XY from object-space height rather than rotating
  connected branches around their attachments. Native support is a starting
  point, not proof of convincing motion for these trees.

Native source also exposes work required before adoption: the shared helper
squares signed height, including below-origin roots, and its sine term is not
zeroed by calm wind. Both materials pass engine wind directly into object-space
deformation and alter positions without corresponding normal/tangent deformation.
Root anchoring, calm behavior, world-to-local wind conversion and lighting-frame
updates therefore need implementation; merely enabling material flags does not
provide them. Native bark has no foliage detail-bending stage, so the leaf
shader's branch attenuation cannot independently flex the woody branches.

### Recommended implementation boundary

Blender owns rest shape, root origin, branch parent/attachment identity, leaf or
spray pivot/direction, and stiffness/phase derived from species, thickness and
seed. Preserve those identities during simplification; do not let interpolation
turn a discrete branch ID into another branch. Store the minimal validated
payload in vertex colors/extra UVs, using a compact data texture only where
attribute capacity requires it. Exact packing and importer precision remain a
feasibility gate. All pivots use the same inch/axis conversion as rendered wood.

The engine owns time-varying wind and GPU deformation. Start with a fixed root
and slow, restrained whole-tree sway; add branch-relative flex and faster leaf
rock/flutter. Mature trunks should move much less than juvenile tops and thin
twigs. Wood and leaves must evaluate identical ancestor motion before leaves
apply local movement. Weight attachment vertices to their parent and taper
additional bending along each branch; disconnected rotations would open joints.
Normals/tangents and shadow/depth passes must follow the same deformation.

First use native bark/foliage as a reference for shared sway and leaf detail on
one juvenile and one mature oak, with the corrections above. The requested
individually moving woody branches require an additional shared, attachment-aware
deformation stage; native foliage detail displacement cannot satisfy that part. Avoid
adding arbitrary noise to the whole mesh or treating the native 'branch' slider
as a skeletal hierarchy. Nearby folded leaves remain oriented to their twigs;
camera-facing leaves are not a wind technique. Distant cluster cards may be used
only with matching silhouette, shading and movement.

Use a common world-space wind direction and gust progression across vegetation,
with seeded local phase variation. Current grass hardcodes direction(0.8,0.6)
and travelling gusts in `voxel_grass_wind.hlsl`; clouds separately expose wind in
meters/second; native foliage uses engine wind globals. They are not connected.
Resolve one environmental input owner before integration, convert world wind
into each rotated tree's local frame, and clamp roots at/below soil to zero sway.
Cosmetic wind needs no per-leaf network state; weather inputs can be replicated
if gameplay/weather synchronization later requires it. Terrain remains unchanged.

Keep ordinary ambient-wind trunk collision static around the stiff lower trunk.
Leaf flutter needs no physics. Branch slowdown volumes are separate gameplay
approximations; shader motion does not move their colliders. Large branch motion,
climbing, breakage or precise contact would need explicit collider updates or a
different interaction design. Do not promise these from visual wind alone.

Reduce leaf detail before branch motion at distance, then retain only subtle
whole-tree sway where visible. LODs must share phase and parent motion, with
animated bounds covering the full permitted excursion. Skeletal trees remain
an option for a small number of interactive trees, not the first forest-wide
ambient-wind implementation. This choice is a proposal, not a measured cost claim.

### Acceptance before enabling the forest

Use the real playable population and define fixed conditions in the validation
ledger before running: calm, steady wind, gust ramp/recovery, opposite directions,
rotated instances, juvenile/mature forms and all LOD transitions. Require fixed
roots, attached leaf bases, continuous branch joins, no rubber-like elongation,
correct shared wind direction, plausible species stiffness, stable shading and
no animated-bounds disappearance. Review motion recordings independently; still
images cannot qualify wind. Compare wind off/on through the unchanged canonical
figure-eight and include depth/shadow, memory, tail latency and allocation costs.
No wind animation or performance result is claimed by this research update.

## Modular Tree evaluation, September 21

[Modular Tree feasibility](ModularTreeEvaluation.md) compares the released
5.5.2 native generator with the current Tree Lab source. It records a successful
Blender 5.2.2 module check, the distinction between preset branching and the
Growth node, missing exported parent identities, and required s&box LOD,
wind and leaf-view correction work. It proposes offline authoring reuse;
no replacement generator or runtime rendering change is implemented by that
evaluation. The earlier September 20 wind section above is historical research;
current implemented motion belongs to [Blender imports](../Architecture/BlenderTreeImport.md)
and [Spawn trees](../Architecture/SpawnTrees.md).
