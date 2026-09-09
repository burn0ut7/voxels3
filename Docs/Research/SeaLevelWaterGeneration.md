# Sea-level water generation and cave interaction

Date: 2026-09-09. Research and proposed first slice; no water generation,
simulation, engine integration or performance acceptance is claimed.

## Recommended first slice

Generate static surface water up to **Z=0**, occupying the space above the
existing seabed. Preserve the solid seabed and the retained caves beneath it.
Use the existing Water material ID4 for appearance and logical identification,
but represent water separately from solid terrain density.

This answers the requested initial ocean/basin filling. It does not yet implement
flow, buckets, drainage, aquifers, rivers, swimming or waves. Those are separate
capabilities with different state and performance requirements.

The earlier [water/gas study](WaterAndGasSimulation.md) recommended finite,
editable water first. This request changes the proposed implementation sequence:
surface-water generation first, finite transport later. Its conservation,
boundary and networking requirements still apply when transport is implemented.

## What other systems establish

| System | Primary evidence | What we adopt or defer |
| --- | --- | --- |
| Minecraft Java 1.18 | Aquifers have local levels independent of sea level, can form underground lakes, and replace the former underwater cave carvers. | Adopt the separation between surface water and cave-water policy. Defer aquifer generation; sea level alone must not decide every cave's contents. |
| Minecraft Bedrock creator documentation | Describes multiple generation passes and distinct sea and seafloor materials. | Keep terrain shape, water occupancy and material appearance separate. This overview is not an exact Java implementation specification. |
| Luanti mapgen V7 | The inspected source assigns stone first, water to remaining positions below its water level, then runs cave generation in makeChunk. Its cave ordering includes explicit handling to avoid excessive cave liquid. | Terrain-before-water is a useful pattern. Copying pass order alone does not copy its liquid and cave rules. Its block-grid implementation is not our smooth-SDF mesher. |
| No Man's Sky | Hello Games documents mesh-based waves/foam in Worlds Part I and object-driven waves/wakes plus deeper oceans in Worlds Part II. | Treat large-water rendering and local interaction as distinct design concerns. These releases do not disclose a voxel flow solver, underground connectivity algorithm or exact generation order. |

Sources: [Mojang Java 1.18 release](https://www.minecraft.net/en-us/article/caves---cliffs--part-ii-out-today-java),
[Microsoft world generation overview](https://learn.microsoft.com/en-us/minecraft/creator/documents/world-generation?view=minecraft-bedrock-stable),
[Luanti V7 source, inspected master](https://github.com/luanti-org/luanti/blob/master/src/mapgen/mapgen_v7.cpp),
[Hello Games Worlds Part I](https://www.nomanssky.com/worlds-part-i-update/),
[Hello Games Worlds Part II](https://www.nomanssky.com/worlds-part-ii-update/).
Luanti's master link is mutable; the observations refer to the source retrieved
on this document's date.

Minecraft also distinguishes source water from flowing water; waterlogging is
another behavior beyond merely assigning a blue material. Its source/flow
distinction is documented in the [Bedrock Update Aquatic notes](https://feedback.minecraft.net/hc/en-us/articles/360004203351-Minecraft-1-4-0-Bedrock).
Luanti similarly exposes separate source/flowing liquid nodes and river-specific
source configuration in its [mapgen aliases](https://api.luanti.org/aliases/).
Neither establishes that a game needs Minecraft's exact renewal or flow rules.

There is no demonstrated universal best implementation in these sources.
For this project's infinite world and frame-rate priority, bounded procedural
initialization is the strongest first fit. A whole-world flood fill or a dense
ocean-sized fluid simulation would solve a much larger problem than requested.

## Current repository boundary

- [RegionalLandforms](../../Code/Voxels/Generation/RegionalLandforms.cs) supplies
  an unedited height H(x,y), already including below-zero basins.
- [TerrainCaves](../../Code/Voxels/Generation/TerrainCaves.cs) composes caves with
  that surface. Its envelope is min(depth-512,32768-depth). At depths strictly
  between zero and512, this envelope stays negative, so procedural caves cannot
  open the unedited seabed there. They can change density magnitudes without
  making those positions air. Edits can still cut through this protection.
- [TerrainField](../../Code/Voxels/TerrainField.cs) owns authoritative density
  corrections and snapshots; water must not bypass this ownership.
- [Materials](../Architecture/VoxelMaterials.md) register Water4 but do not
  generate it. Current material queries return Air for positive terrain density.
  The renderer shades the existing solid surface; adding a blue catalog entry
  does not create a water surface or volume.

These are current source observations, not proof that all terrain/material
integration is accepted. Their validation gates remain open independently.

## What “fill below zero” means

Keep the seabed: if its height is -800, water occupies the column from -800
to0. Rock below -800 stays rock. A cave at -1200 remains a cave.

Do not flatten all negative terrain to zero, delete all solid below zero, or
replace the seabed with solid blue voxels. None produces the intended ocean.

Proposed canonical point classification, using the final edited solid density D:

    if D(p) <= 0: Solid, with its existing material
    else if H(p.x,p.y) < p.z and p.z < SeaLevel: Water, material ID4
    else: Air

The geometric water surface is at SeaLevel; the exact surface point is a
boundary, not evidence of positive water depth. Freeze this half-open occupancy
convention in CPU/GPU consumers. Solid takes precedence over water.

H is the pre-cave landform height, not the highest air space found by scanning
the final caves. Thus the water domain has a finite seabed boundary and does not
accidentally extend into every underground cavity. This rule fills all qualifying
surface depressions, including isolated basins below sea level. It does not prove
ocean connectivity, model a water table, or generate elevated lakes.

One authoring setting is sufficient: SeaLevel, initially0. Existing LandAmount
and relief controls continue to shape the coastline and basin depth. Do not add
separate knobs for every bay, lake or wet chunk. Validate the supported sea-level
range with the terrain coordinate envelope before implementation.

## Generation order: dependencies, not destructive passes

Use this logical order:

1. Evaluate regional landform height and derive the surface-water domain.
2. Evaluate retained caves to obtain final solid terrain.
3. Apply canonical terrain corrections.
4. Resolve solid, water or air from the shared rules.
5. Derive solid geometry/collision and water presentation separately.

This preserves the user's intent to establish surface water before cave
decisions. In an on-demand procedural field, it does not require allocating a
world, filling blocks, then running another destructive pass over them.

Ordering alone cannot prevent future leaks: if a later cave recipe reaches the
seabed, it must explicitly preserve a seal or classify that opening as connected
water. When aquifers are added, choose local water domains and cave constraints
together. Do not let chunk arrival order determine which one wins.

## Deep modules and ownership

| Responsibility | Narrow inputs and output | State owner |
| --- | --- | --- |
| SurfaceWater generation | Immutable water recipe, H and position -> candidate wet domain/surface level | Pure generation module; no caches or engine objects |
| World medium query | Captured final density plus candidate water -> Solid/Water/Air and material | Canonical world-query boundary; reuses TerrainField snapshots |
| Water surface builder | Bounded domain plus captured terrain -> visible water boundary geometry | Derived render resources, cancellation and revision checks |
| Future fluid transport | Reservoir definitions, finite amounts and terrain connectivity -> committed transfers | One host-owned fluid state when that later slice exists |

These are responsibilities, not a request for a plugin framework or a service
class per row. Keep manager code to configuration and lifecycle orchestration.

A water query may return Water with positive *solid* density. Callers must not
equate “positive solid density” with “no medium.” Conversely, Water4 must never
make the solid collision mesher treat water as rock. Preserve the terrain SDF
contract and add an explicit medium result at the world-query boundary.

## Presentation, edits and future simulation

Render the blue checker water through a dedicated derived water surface.
Prefer a bounded tiled sea-level surface with shoreline clipping for this flat
first slice; build/sample shoreline geometry from the same domain rules.
Qualify coarse shorelines, edited occlusion, underwater views and tile seams.
Do not render one cube per occupied water node or allocate deep water interiors
solely to draw their top. A surface plane alone is not the water-volume query.

Keep solid collision unchanged. Swimming/buoyancy must later consume the same
water domain; visual waves must not independently determine fluid occupancy.
Reverify any engine water-volume API before choosing its adapter.

Static generation has an explicit edit limitation: a dig below the original
seabed does not receive water automatically. Adding solid excludes water locally;
removing it restores the procedural water where that domain already existed.
This is nonconserved reservoir behavior, not finite-fluid displacement.
A breached floor can therefore expose a static water boundary above a dry hole.
Do not hide that limitation or claim realistic drainage. If flowing into freshly
dug openings is required for the first playable slice, implement bounded
connectivity/transport before calling water complete.

The later solver must distinguish infinite reservoir supply from finite stored
water, account for exchanges, preserve sleeping state and never treat unloaded
neighbors as drains. Terrain edits invalidate local boundary data through the
existing committed-edit notification. Avoid an unbounded search to “the ocean”
for each newly loaded chunk.

## Determinism, persistence and performance

World identity must include the water recipe version and sea level. Resolve the
save/manifest format change explicitly; do not silently add oceans to previously
saved worlds. Terrain generator identity need not change if its density formula
does not change, but the complete world recipe still changes. Joiners must agree
on both recipes before derived geometry is accepted.

Static water needs no per-voxel authoritative arrays or recurring simulation
ticks. Cache only bounded derived tiles. Use immutable worker inputs, existing
spatial ownership, stale-result rejection and engine-thread resource publication.
Stop scheduling at work/memory caps and let water arrive progressively.

Measure shoreline sampling, mesh publication, water draw cost, overdraw,
retained memory and frame tails separately. Do not evaluate cave noise for every
ocean interior sample when a conservative classification already proves the
answer. Any shortcut must preserve the canonical query and unknown-page behavior.
No claimed budget or speedup is established by this research.

## First implementation sequence and acceptance

1. Freeze static versus flowing edit behavior and the sea-level convention.
2. Add immutable water recipe identity and pure surface-domain evaluation.
3. Integrate explicit medium/material queries; preserve solid collision.
4. Add bounded blue water surfaces and shoreline/underwater visibility.
5. Wire local edit invalidation, recipe resets, persistence and multiplayer.
6. Define fixed production scenarios before running them, then qualify results.

Required cases: unchanged dry land; seabed/water/air at one column; isolated
below-zero basin; dry sealed cave under an ocean; exact Z0 and near-zero samples;
positive/negative chunk and LOD seams; dig/build around a shoreline and through
the seabed; reload order independence; save/reload and late-join agreement;
no stale tiles after edits or recipe replacement. Check actual rendered surfaces
and collision, not only material IDs.

Run the canonical figure-eight with an explicitly recorded water recipe and
compare a matching pre-water baseline. Preserve the existing terrain benchmark's
post-route failure and pending safe-return decision; this proposal does not
authorize changing its workload. Run additional frozen ocean-heavy views to
measure water overdraw. Accept only after correctness and performance are
reviewed. No runtime changes or tests were performed for this document.
