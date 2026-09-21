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
2. **Simulate Growth** advances the seasonal model and publishes a branch-only
   preview. **Next Seed** changes the seed and runs the same solver. Interactive
   simulation yields between seasons; **Cancel Build** or Esc keeps the previous
   completed specimen. The hard limits are 30,000 nodes and 80 seasons; dense or
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
   stems without structural side limbs can be meshed, but are not yet supported
   by the current motion exporter.
6. **Prepare for s&box** requires an exporter supporting the source format.
   The current local legacy exporter does not support the new connected surface
   and is rejected before writing export output. The graph/preview alone is not
   a game model. No game assets are installed by the growth workflow.

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

For supported legacy sources, the workspace's optional `export_sbox.py` adapter derives three
mesh LODs, bark/foliage materials, trunk collision and motion attributes in `.codex/tree-export/<specimen>/`. Source
ancestry is retained before simplification. Current wind data groups descendants
by primary limb; finer hierarchical branch wind and camera-facing leaf correction
remain separate renderer work. They are not implemented by a Blender preview.

In workspaces containing the optional export/install tools, completed exports can be checked with
`python Tools/BlenderTrees/install_exports.py <specimen_key>` from the project
root. Its explicit `--install` option copies verified assets into the game.
Changed installed assets require the canonical figure-eight plus in-world
wind/LOD/leaf-view validation. The local import contracts are documented in `Docs/Architecture/BlenderTreeImport.md`
when that downstream subsystem is present. The export button is disabled without
the adapter; Blender growth and meshing do not depend on it. A connected-surface
adapter must declare `shared_collars` in `SUPPORTED_SURFACE_METHODS` and consume
the unified wood without re-exporting hidden construction tubes. That game
adapter, fine wind and leaf-view correction remain unqualified downstream work.

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

Oak/ash bark uses Charlotte Baglioni's unchanged [Japanese Camphor Bark](https://polyhaven.com/a/japanese_camphor_bark)
scan as an art material. Spruce uses Dimitrios Savva's unchanged [Pine Bark](https://polyhaven.com/a/pine_bark).
Both are Poly Haven [CC0](https://polyhaven.com/license). Exact dimensions, URLs,
bytes and checksums are in the corresponding `Textures/*_source.json` files.
Birch's pale bark/lenticels and ash/birch leaf surfaces are native Blender
procedural materials. Ash/birch blades and spruce needles are authored geometry,
not recolored oak textures. These materials are visual approximations rather
than botanical specimen scans. Rejected Brown02/Willow candidates retain their
provenance. Original oak leaf artwork provenance is in `../TreeSources/README.md`
and `../../Assets/textures/trees/README.md`.
