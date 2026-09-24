# s&box Tree Growth

The Blender authoring module now grows a persistent branch graph season by
season. One solver serves the initial oak, ash, birch and spruce profiles.
Species are parameter data; adding another species should not create another
growth algorithm. These profiles are an initial visual approximation, not
calibrated botanical ages or a claim that every tree type is already supported.

## Authoring workflow

Load `tree_lab_addon.py` from this directory into visible Blender 5.2.2 with its
sibling files present. The live development session registers it as
`voxels_tree_lab`. Paths resolve relative to the module; copying the Python
panel file alone does not install its generator or asset dependencies.
In the 3D View sidebar choose **Tree Growth**.

1. Choose species, age preset, growth habit and seed, then adjust **Growth
   Seasons** and the growth/environment controls. Potential height describes
   growth potential; it does not uniformly scale a completed adult.
   New oak presets use leaf density34.2 with renewed foliage on living fine
   interior twigs; the density control supports values up to64.
   Ash, birch and spruce retain their1.35 presets. Saved recipes keep their
   recorded densities. **Update Foliage Only** applies a new density to an
   existing source while preserving its grown branch graph and wood.
2. **Simulate Growth** advances the seasonal model and publishes a branch-only
   preview. **Next Seed** changes the seed and runs the same solver. Interactive
   simulation yields between seasons; **Cancel Build** or Esc keeps the previous
   completed specimen. The hard limits are 100,000 nodes and 80 seasons; dense or
   old recipes can exceed the node budget and fail without publishing a partial tree.
3. **Saved Specimen** restores a completed specimen and its controls. **Current**
   focuses it; **Gallery** shows stored specimens together.
4. **Save Recipe** writes a `.tree.json` containing the controls, species profile,
   complete graph, source revision hash and graph checksum. **Load Recipe** restores
   that exact graph. Changing growth controls requires another simulation before
   source geometry can be built. Future solver changes may produce different
   graphs from the same recipe; loading preserves the saved graph.
5. When the form is useful, **Build Source Geometry** derives wood, foliage,
   roots and collision proxies from it. This is the expensive meshing step and
   stores a separate `_Source` collection. The build yields between stages;
   **Cancel Build** or Esc preserves the previous completed source. Publication
   happens only after all stages finish. Source builds do not create undo
   snapshots; save separate working files to retain past source revisions.
   Existing full source geometry is not replaced by a growth preview. Very young
   stems without structural side limbs can be meshed and use a single stiff
   trunk motion group. Offline surfaces have an explicit4million-face capacity;
   the native union is atomic and cannot be cancelled during that operation.
6. **Prepare for s&box** stages a completed supported source using the shared
   exporter. Connected broadleaf wood is supported; individual spruce needles
   remain unqualified. Every foliage LOD keeps the same blade identities. Large
   sources can exceed the separate native import guard; a finished Blender tree
   does not guarantee export readiness. No game assets are installed by the
   growth workflow. Wind, camera-facing leaves and LOD appearance still require
   testing in the visible game editor before accepting a new sample.

The original `tree_library.blend` and `oak_studies.blend` remain reference
libraries. Use Blender Save As for a separate working file; the former button
that overwrote the original library is removed. Existing manually edited guide
specimens remain stored geometry, but edited guides are no longer a second
source of growth. New guides are derived from the graph.

## Ownership and species

`growth.py` owns the engine-independent recipe, `Species` profiles, stable node
and axis identities, light competition, bud activation, resource allocation,
tip extension/loss and incremental radial growth. A larger age runs additional
seasons with the same seed. Existing node positions and ancestry persist.

`surface.py` owns connected collars and their shared branch rings.
`build_oak_studies.py` owns Blender previews and derived geometry. Its historical
filename remains; prescribed species scaffolds and recursive crown generation
are removed. Organ geometry/materials still differ by species: oak leaf artwork,
compound ash blades, birch blades and spruce needles. Roots are artist-directed
geometry at soil Z=0, not a root/soil growth simulation. Their random stream is
separate from the canopy. The panel owns main-thread jobs and persistence.

[Shared growth architecture](../../Docs/Architecture/TreeGrowth.md) records the
algorithm, alternatives, limits and invalidation contract. Initial species
traits are hypotheses for calibration: oak's weaker leader, ash's paired buds,
birch's slender/drooping growth and spruce's stronger leader with retained foliage.
They are not four separate skeleton generators.

## Game integration boundary

For legacy and connected `solid_union` sources, the workspace's optional `export_sbox.py` adapter derives three
mesh LODs, bark/foliage materials, trunk collision and motion attributes in `.codex/tree-export/<specimen>/`. Source
ancestry is retained before simplification. Current wind data groups descendants
by primary limb. The export includes leaf pivots/directions for the game's wind,
flutter and camera-facing shader; those effects still require native game
validation. They are not demonstrated by a Blender preview.

In workspaces containing the optional export/install tools, completed exports can be checked with
`python Tools/BlenderTrees/install_exports.py <specimen_key>` from the project
root. Its explicit `--install` option copies verified assets into the game.
Changed installed assets require the canonical figure-eight plus in-world
wind/LOD/leaf-view validation. The local import contracts are documented in `Docs/Architecture/BlenderTreeImport.md`
when that downstream subsystem is present. The export button is disabled without
the adapter; Blender growth and meshing do not depend on it. A connected-surface
adapter declares `solid_union` in `SUPPORTED_SURFACE_METHODS` and consumes
the unified wood without re-exporting hidden construction tubes. That game
adapter, fine wind and leaf-view correction remain unqualified downstream work.

Dense oak evaluation exports preserve every leaf and split an oversized canopy
into whole-leaf model pieces, each with three LODs, under one prefab. The wood
model owns nine solid trunk pieces. Runtime collision is trunk-only; branch
proxies remain authoring metadata for later gameplay work. All models share the original coordinates and global
motion atlas. The per-model triangle guard remains 1,350,000, owned by
`native_limits.py`. Multipart exports require `catalog_eligible=False`; they are
manual evaluation samples until population loading supports multipart trees.
Exporting or installing them does not establish native compilation or runtime
acceptance.

The existing `recipes.json`, catalog variations and `build_catalog.py` describe
legacy scaffold-generated sources. Their old controls are not replay-equivalent
under seasonal growth. Use the panel's explicit simulation/recipe workflow for
new growth specimens; the old catalog helper has not been migrated or qualified.
Stored legacy source meshes remain independently exportable.

## Validation and limits

`TREE-GROWTH-001/v1` in [the validation ledger](../../Docs/ValidationResults.md)
exercises the real Blender operators across four species and ages 6/18/30,
repeated seeds, competition changes, save/load, corrupted data and cancellation.
`TREE-GROWTH-002/v1` checks the connection to source geometry.
`TREE-GROWTH-003/v1` covers all twelve species/age meshes, graph preservation,
geometry/UV checks, current wind-data compatibility, memory, cancellation and
native two-angle renders for the earlier voxel mesher. Its mesh times were
5.0-27.9 seconds. `TREE-GROWTH-006/v1` validates the replacement shared collars,
closed connectivity, foliage attachment and independent review of the repaired
oak junctions. Failed attempts remain in the ledger. These authoring checks do not qualify
the existing production forest or establish botanical realism.

The model approximates canopy light and shoot competition; it does not model
soil chemistry, physiological carbon balance, disease, fracture, automatic
branch shedding, or guarantee that expanding mature wood never intersects.
Dead tips remain in the wood graph. Large populations are not simulated live
in s&box by this change. Species calibration, wider seed coverage and full
export validation remain required before replacing the production catalog.

## References and assets

Species observations come from Woodland Trust's [oak](https://www.woodlandtrust.org.uk/trees-woods-and-wildlife/british-trees/a-z-of-british-trees/english-oak/),
[ash](https://www.woodlandtrust.org.uk/trees-woods-and-wildlife/british-trees/a-z-of-british-trees/ash/),
[Norway spruce](https://www.woodlandtrust.org.uk/trees-woods-and-wildlife/british-trees/a-z-of-british-trees/norway-spruce/)
and [silver birch](https://www.woodlandtrust.org.uk/trees-woods-and-wildlife/british-trees/a-z-of-british-trees/silver-birch/)
pages. These establish visible leaf/crown/bark traits; juvenile topology and
stage dimensions are authoring choices, not verified biological age estimates.

The original oak references and ash use Charlotte Baglioni's unchanged [Japanese Camphor Bark](https://polyhaven.com/a/japanese_camphor_bark)
scan as an art material. Spruce uses Dimitrios Savva's unchanged [Pine Bark](https://polyhaven.com/a/pine_bark).
New oak revisions use Rob Tuytel's [Brown Bark 02](https://polyhaven.com/a/bark_brown_02)
for coarser furrows. These scans are Poly Haven [CC0](https://polyhaven.com/license). Exact dimensions, URLs,
bytes and checksums are in the corresponding `Textures/*_source.json` files.
Birch's pale bark/lenticels and ash/birch leaf surfaces are native Blender
procedural materials. Ash/birch blades and spruce needles are authored geometry,
not recolored oak textures. These materials are visual approximations rather
than botanical specimen scans. Earlier material candidates retain their provenance. Original oak leaf artwork provenance is in `../TreeSources/README.md`
and `../../Assets/textures/trees/oak_leaf_source.json`.

## Dense oak collection

`oak_variations.json` defines eleven seeded variants of the preserved dense
`oak_growth_18_open_grown_271828_dense` oak. Together they form a twelve-tree
collection. They use the current seasonal-growth generator, the same leaf density,
and modest differences in crown proportions, lean, branching and light direction.

In a visible Tree Growth Blender session, run `build_growth_variants.py` with
`runpy.run_path(...)['start']()`. It queues the existing modal growth and source
operators, saves each source and complete recipe under `Variants/<asset_key>/`,
and calls the canonical exporter. Cancel Build stops the queue. Completed exports
are hash-checked and skipped when resuming; incomplete saved sources are preserved
for review. The original reference libraries and original dense oak are untouched.

Install each completed key with `install_exports.py <key> --install`, compile its
source materials and render models in s&box and verify each asset is compiled
and up to date. Bake all four `bake_tree_impostor` elevation rows, then run
`Tools/pack_tree_impostors.py <key>` from the project root. Reinstall that key to
attach the existing `TreeModelLod` component to its prefab; identical dependencies
are preserved to avoid unnecessary live reimports. Full-compile the packed distant
material, compile the prefab, and verify both are up to date before using it.
The native baker waits for resource loading and checks the motion texture before
capturing. A readiness failure must be resolved before using the distant asset.
The packer requires NumPy, Pillow and SciPy. Multipart captures now use64views
(16azimuths/four elevations),LOD2 and the runtime minimum leaf retention. Full
nearest-surface depth padding preserves branch tips during perspective reprojection.
After repacking, restart Play to recreate copied distant materials; a live texture
reload alone cannot update the cached capture layout.
New keys use seeds
271829 through271839 and the form `oak_growth_18_open_grown_<seed>_dense`.
All use trunk-only game collision. These multipart prefabs are independent authored
assets; the older single-model procedural population catalog is unchanged.

`oak_overhead_variations.json` adds twelve separate overhead-lit growth recipes,
seeds271840..271851. In Tree Growth, **Overhead Growth Light** directs the simulated
canopy toward world+Z every season. The Blender studio Sun affects rendering only.
Use `start(plan_path=..., status_path=...)` on the same collection runner for this
plan; sources/export/install steps are identical. Existing recipes default to their
original directional growth light. The original dozen is not regenerated.

## Animated baked leaf clusters

For the installed dense oak, run `bake_foliage.py` inside the visible Blender
session with `runpy.run_path(path)['start']('oak_growth_18_open_grown_271828_dense')`.
It imports original canopy LOD2 sources, stages4096animated patch cards and64
shared512px color/normal cluster views under `.codex/tree-foliage-bake/<key>`.
Cancel Render cancels an active job; a staging `cancel` file requests cancellation
between stages and must be removed before restarting. The authoring scene is
restored automatically. No source FBX or source motion texture is overwritten.

After `status.json` reports complete, run
`python Tools/BlenderTrees/bake_foliage.py <key> --install` outside Blender.
Installation rejects changed inputs/tools or incomplete outputs. Compile the
new baked material and four canopy models, run all four native distant-bake
rows, pack with the existing packer, compile the far material and restart Play.
Regenerating source requires repeating these steps. See
[animated baked foliage](../../Docs/Architecture/BakedTreeFoliage.md) for ownership,
wind behavior, approximation limits and measured qualification.
