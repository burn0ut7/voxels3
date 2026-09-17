# Terrain texturing research: Godot and related renderers

Research date: 2026-09-17. Status: source comparison and proposed experiments;
no runtime implementation or performance gain is accepted by this document.

## Findings for Voxels3

The most promising near-term question is how much our three-patch anti-repetition
filter costs, and whether we can retain its appearance with less work where the
screen cannot resolve individual patches. Moving the entire texture fade closer
already failed our visual/performance comparison. The external sources support
more selective simplification and caching, not another arbitrary shorter fade.

Godot terrain systems do not all use the same renderer. Terrain3D and HTerrain
are heightfield systems; Godot Voxel supports volumetric surfaces and is the
closer geometry match. Their mechanisms are useful evidence, but their screenshots,
sample counts, and engine choices do not establish comparative FPS on our world.

The September 16 [investigation](../ValidationEvidence/TerrainPasses/Investigation.md)
measured terrain color at 1.438 ms in one stationary profiled view, with depth and
shadows about 1.09 ms. These are smoothed native pass snapshots with profiling
enabled, not timing predictions for the proposals below. Material optimization
does not automatically remove the depth/shadow work.

## Current implementation checked against source

The current [terrain shader](../../Assets/shaders/voxels/voxel_terrain.shader)
uses three axis projections, with up to three stochastic patches per projection.
Each participating patch reads two packed BC7 maps: color/roughness and normal/AO.
It skips zero-contribution projections and patches and negligible material weights.
Grass, dirt, stone, snow, and sand share that path. Vertex weights already carry
material selection; we do not read a many-layer splat map to discover materials.

Full detail extends to 32 m, then blends to a two-read material-average
representation by 64 m. The sampler is 16x anisotropic. Parallax is disabled.

The maximum detailed material cost is therefore 3 projections x 3 patches x
2 packed maps = 18 shader sampling instructions, before lighting. This is not
18 reads at every pixel: flat ground often skips two projections, patches can
vanish, and absent materials return immediately. Filtering may perform multiple
underlying texel accesses per instruction. Blending zones can also read the two
far values. Sample-count ratios are not GPU-time ratios.

## Source comparisons

### Godot's standard material

Godot's own documentation describes triplanar mapping as three projected texture
reads blended by orientation, and warns that it is more expensive than ordinary
UV mapping. Switching engines would not remove this multiplication.
[BaseMaterial3D documentation](https://docs.godotengine.org/en/stable/classes/class_basematerial3d.html#class-basematerial3d-property-uv1-triplanar)

### Terrain3D: selective work and heightfield-specific projection

The stable documentation describes up to 32 texture sets, with a base ID, overlay
ID and blend value at each painted vertex. Albedo/height and normal/roughness
are packed into two textures. Two IDs per vertex does not mean only two materials
are evaluated at every pixel: surrounding control points also contribute.
[Texturing documentation](https://terrain3d.readthedocs.io/en/stable/docs/texture_painting.html)

Pinned development source: `TokisanGames/Terrain3D`
`f9a216a72b917c8bef5126843e890268c755a82f`. This is a main-branch snapshot,
not a claim that all behavior matches the stable 1.0.2 documentation.

The main shader computes a footprint from coarse world-position derivatives and
vertex density. It skips three extra material-accumulation calls when bilinear
control-point blending is disabled. At this revision the executable test is
`region_mip < 4.0`; an adjacent comment describes a different threshold, so the
code controls this finding. Base and overlay reads are conditional, including
skipping duplicate IDs. This is footprint-aware simplification, not a fixed
distance-only test. Its threshold should not be copied into our different units
and material representation.
[Main shader](https://github.com/TokisanGames/Terrain3D/blob/f9a216a72b917c8bef5126843e890268c755a82f/src/shaders/main.glsl#L433)

Its optional cliff projection switches texture coordinates on sufficiently steep
surfaces and aligns normal-map coordinates. It does not blend three complete
axis-projected material samples like our shader. This is a meaningful structural
difference, but its heightfield assumptions do not cover our ceilings, overhangs
and arbitrary edited surfaces.
[Projection source](https://github.com/TokisanGames/Terrain3D/blob/f9a216a72b917c8bef5126843e890268c755a82f/src/shaders/projection.glsl)

The lightweight shader removes per-control-point coordinate variation and tracks
which material IDs have already been read, allowing reuse of a sample for a
repeated ID. That reuse is valid because the coordinates match. Our three
stochastic patches deliberately use different coordinates, so treating them as
duplicates would change the image. We already evaluate each of our five material
types once in the outer material path.
[Lightweight shader](https://github.com/TokisanGames/Terrain3D/blob/f9a216a72b917c8bef5126843e890268c755a82f/project/addons/terrain_3d/extras/shaders/lightweight.gdshader#L392)

Terrain3D also offers two-scale texturing. Its source reads an additional coarse
pattern during the transition and skips the corresponding fine read only once
the far factor reaches one. This can hide repetition; it is not a free speedup.
Its macro variation separately adds two noise lookups to modulate color.
[Dual scaling](https://github.com/TokisanGames/Terrain3D/blob/f9a216a72b917c8bef5126843e890268c755a82f/src/shaders/dual_scaling.glsl),
[macro variation](https://github.com/TokisanGames/Terrain3D/blob/f9a216a72b917c8bef5126843e890268c755a82f/src/shaders/macro_variation.glsl)

The maintainers explicitly provide a lightweight shader and identify projection,
detiling, optional background noise and dual scaling as features with costs.
This supports testing feature-specific work rather than assuming all terrain
texturing is intrinsically cheap.
[Performance guidance](https://terrain3d.readthedocs.io/en/stable/docs/tips_technical.html#performance)

### HTerrain: indexed layers and a spatial far-color cache

Pinned source: `Zylann/godot_heightmap_plugin`
`7f574eb47fbd74cb1a79adc2cc9fb7f0694fccc3`.

The inspected array shader uses three indexed terrain layers and two packed
texture arrays. Its detailed path reads six material samples, with additional
terrain/control inputs outside that count. It samples a global map and skips
the detailed block when the global-map blend is complete. The bake shader
evaluates three indexed albedo layers into that map.
[Array shader](https://github.com/Zylann/godot_heightmap_plugin/blob/7f574eb47fbd74cb1a79adc2cc9fb7f0694fccc3/addons/zylann.hterrain/shaders/array.gdshader#L101),
[bake shader](https://github.com/Zylann/godot_heightmap_plugin/blob/7f574eb47fbd74cb1a79adc2cc9fb7f0694fccc3/addons/zylann.hterrain/shaders/array_global.gdshader#L57)

The documentation describes a global color map for distant rendering and grass
tinting. It preserves location-dependent combined terrain color, rather than
replacing each material with one uniform average.
[HTerrain documentation](https://hterrain-plugin.readthedocs.io/en/latest/#global-map)

Its documented tiling-reduction option uses a second differently oriented/scaled
sample, selected through a warped procedural pattern and depth blending. It is
per-texture and available only in supporting shaders; the inspected array shader
is not evidence that every HTerrain variant includes it. This offers a concrete
two-sample alternative to investigate, with its own repetition and blending
tradeoffs. The docs also distinguish the array shader from the classic shader's
selective cliff triplanar option.
[Tiling reduction](https://hterrain-plugin.readthedocs.io/en/latest/#tiling-reduction)

Transfer limit: a single overhead map cannot distinguish a cave floor, ceiling
and land surface sharing the same horizontal position. A Voxels3 cache would
need surface/cell-aware addressing or an explicitly restricted domain. Digging,
material edits, streaming and LOD changes must invalidate affected cache entries.

### Godot Voxel: comparable topology, different detail tradeoffs

Voxel Tools documents triplanar mapping and indexed material mixtures for smooth
volumetric terrain. Its S4 vertex representation carries four material IDs and
weights; texture arrays provide indexed access. Its example does not demonstrate
that arrays eliminate the cost of evaluating multiple projections or materials.
[Smooth-terrain documentation](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/)

The pinned Godot demo (`Zylann/voxelgame`
`89cc0b17d4462ed9f9ad10996a79b81f4cc809a6`) samples three projections for both
top and side albedo textures, blending by topness. Its current helper has no
active biplanar implementation. This is a simpler material example, not a
feature-equivalent benchmark of our five-material, stochastic, packed PBR shader.
[Terrain demo shader](https://github.com/Zylann/voxelgame/blob/89cc0b17d4462ed9f9ad10996a79b81f4cc809a6/project/smooth_terrain/transvoxel_terrain.gdshader#L34),
[triplanar helper](https://github.com/Zylann/voxelgame/blob/89cc0b17d4462ed9f9ad10996a79b81f4cc809a6/project/addons/zylann.voxel/shaders/triplanar.gdshaderinc)

Voxel Tools also describes baking detailed geometry normals into cell-addressed
atlases for coarser meshes, including overhangs. It trades generation time and
memory for apparent geometric detail; documentation warns that streamed-out
edited data limits distant edit fidelity. This is geometry-normal detail, not
a demonstrated cache of the complete color/roughness/AO material shader.
[Detail-rendering documentation](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/#detail-rendering)

### Related references beyond Godot

Terrain3D's design notes identify Unity's IndexMapTerrain as an influence. That
project exposes indexed painting with per-sample projection and rotation. It is
useful lineage for the control-map approach, not a ready-made volumetric solution.
[Terrain3D design](https://github.com/TokisanGames/Terrain3D/blob/f9a216a72b917c8bef5126843e890268c755a82f/doc/docs/shader_design.md),
[IndexMapTerrain](https://github.com/cdxntchou/IndexMapTerrain)

Epic's runtime virtual texturing guide explicitly uses a cache to avoid repeatedly
evaluating complex landscape material results. This strengthens the longer-term
case for caching surface attributes. It does not supply an s&box implementation,
and a top-down landscape RVT is not automatically suitable for stacked cave
surfaces. Lighting and dynamic shadows would still require their own treatment.
[Epic's RVT guide](https://dev.epicgames.com/documentation/unreal-engine/runtimevirtual-texturing-quick-start-in-unreal-engine)

## Experiment priorities and decisions

These are project inferences from the sources, not measured optimizations.

| Priority | Proposed experiment | What it would establish | Main risk |
| --- | --- | --- | --- |
| 1 | Temporarily replace three-patch anti-repetition sampling with one continuous repeating sample, keeping all packed channels and current projection/fade rules | Upper-bound practical headroom attributable to patch sampling and associated math | Visible repetition; diagnostic only unless a separately verified visual solution follows |
| 2 | Retain current close appearance, simplify anti-repetition only when its pattern is insufficiently resolved on screen | Whether footprint-aware material detail can save work without erasing nearby normals or rock detail | Shimmer, branch divergence, costly crossfade, and repeated motifs |
| 3 | Compare a cheaper repeated base pattern plus controlled broad color variation against the present stochastic appearance | Whether less expensive anti-repetition is visually acceptable | Color variation does not hide all directional or high-contrast repeated features |
| 4 | Evaluate selective projection reduction on genuinely multi-axis surfaces | Whether slopes/cliffs contribute enough cost to justify a projection change | Orientation seams, normal-map flips, and cave/ceiling failures |
| 5 | Design a bounded surface-attribute cache if simpler shader changes leave substantial repeated work | Potential to amortize stable material evaluation across frames | Mapping, memory, invalidation, stale edits, filtering borders and update spikes |

A footprint test must describe the anti-repetition pattern being simplified,
not merely the source texture's selected mip level. Oblique views can retain
detail along one direction through anisotropic filtering. Randomly changing
patch selection with the camera could introduce temporal noise even if a still
image looks acceptable; stability during motion is part of the experiment.

Priorities 1–3 target work our implementation actually repeats. Priority 4 is
secondary because flat ground already skips negligible axes. Array conversion
alone is not selected as a speed optimization: it is valuable for expanding the
material catalog, but does not inherently reduce samples, and current materials
already share a draw/shader path. Repacking channels is also not a new opportunity;
we already use two maps.

We do not adopt heightfield-only world mapping, arbitrary two-material truncation,
earlier removal of all texture detail, parallax, or a second authoritative terrain
representation. A future cache must remain derived data, with one owner and
versioned invalidation from existing density/material state.

For a cache design, explicitly specify inputs (surface geometry, material weights,
texture versions, requested footprint), outputs (unlit material attributes),
residency ownership, per-frame update and memory budgets, edit/LOD invalidation,
stale-work rejection, and a correct fallback while pages are pending. Verify
required s&box resource/update APIs before implementation. This research has not
verified a drop-in virtual-texturing facility for our custom terrain renderer.

## Validation required before adopting anything

Record exact candidate parameters and pass criteria in the existing validation
ledger before running. Use the canonical figure-eight and matched stationary
near, slope/cliff, cave/ceiling, material-boundary and distant views. Preserve
camera, resolution, world revision and settings; include negative coordinates,
live digging and LOD transitions for visual correctness. Do not disable input.

Wait for both explicit and automatic file-watcher compilation to finish, then
settle and warm up. Use repeated A/B/A comparisons because prior session drift
was larger than some candidate changes. Capture native pass attribution in
separate diagnostic runs; judge final FPS with instrumentation off. Measure
moving/stationary GPU time, frame tails, allocations, peak memory, streaming,
collision, and shader/render errors. A retained shader change also requires a
fresh editor start. Sample-count reduction is insufficient for acceptance.

This research changes documentation only. No playable-world benchmark was run,
no runtime shader was edited, and no 500–600 FPS outcome is predicted.
