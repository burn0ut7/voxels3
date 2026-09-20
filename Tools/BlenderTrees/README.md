# Blender Tree Lab

Procedural tree authoring in Blender 5.2.2. Four species share one seeded
skeleton-to-mesh pipeline: English oak, common ash, Norway spruce (evergreen)
and silver birch. Juvenile, Mature and Large stages change branch development,
proportions, roots and bark maturity; Height remains independently adjustable.
This tool does not modify the playable world.

[View the species and growth-stage comparison](../../Docs/ValidationEvidence/BlenderTrees/species-final-comparison.png).

## Preserved oak

The accepted original `oak_studies.blend` is retained unchanged. Continue new
work in `tree_library.blend`. Its four original oak specimens are marked as
protected references: the generator and guide-rebuild operator refuse to
replace them. New specimen names include species, stage, habit and seed, so
an ash cannot overwrite an oak sharing its seed. Original oak materials retain
their original names; new species/stages have separate materials.

## Using the installed panel

Open `tree_library.blend`. In the 3D View sidebar choose **Tree Lab**.

1. Choose Species, Growth Stage, Growth Habit and Seed. These preset selectors
   load defaults. Then adjust height, girth, spread, growth direction, crooked
   growth, crown bias, branching/leaf density and root spread/depth.
2. **Generate from Seed** creates or replaces that species/stage/habit/seed.
   **Next Seed** creates a different specimen. Other specimens remain stored.
3. **Saved Specimen** selects an existing tree and restores its controls.
   **Current tree** focuses it. **All trees** displays the library together.
   Gallery translations are temporary and are restored before selecting,
   rebuilding or saving. Local mesh coordinates and guide controls stay intact.
4. Enable **Show Direction Guides**, move a major limb's Bezier controls in
   Edit Mode, then **Rebuild from Edited Guides**. Smaller growth, foliage and
   collision proxies follow that skeleton. The operator exits Edit Mode.
   Protected references cannot be rebuilt; generate a new oak specimen first.
5. **Save Tree Library** packs the working file and writes `recipes.json`.

Generating from seed replaces that specimen's edited guides. Guide rebuilds
preserve the specimen's seed/species/stage and its edited primary skeleton;
main scaffold shape controls apply when generating from seed. Save the file to
retain manual guide edits. Seed identity guarantees repeatability only with
identical inputs, assets and generator revision.

## Species and age

| Species | Structure and foliage |
| --- | --- |
| Oak | Broad structural limbs; existing lobed leaf artwork |
| Ash | Opposite branching tendencies; paired leaflets and terminal leaflet on a rachis |
| Norway spruce | Persistent central leader, layered branches, lateral sprays and individual needle geometry |
| Silver birch | Slender stem, descending fine tips, small serrated triangular blades, pale mature bark |

Juveniles use fewer primary limbs or whorls, fewer branching generations,
slender proportions, smaller roots and smoother young bark. Leaves retain
plausible organ size rather than shrinking with the whole tree. Large specimens
increase structural development, default height and girth. Stages are art
controls, not chronological age or a biological growth simulation.

## Ownership and derived parts

The canonical generator is `build_oak_studies.py` (its historical filename is
retained). `SPECIES` and `preset_settings` own species data and defaults.
Explicit seed/configuration creates the skeleton; edited Bezier guides become
the skeleton for a guide rebuild. Wood, fine branches, leaves, roots and all
proxies derive from it. Crown, leaf and root random streams use explicit seeds;
no frame time or process-dependent hash enters generation.

Blender owns the stored specimen collections, guide coordinates and metadata.
The installed `tree_lab_addon.py` panel executes the generator on Blender's main
thread. Rebuilding invalidates only the named generated specimen and its guides/
proxies. Materials are scoped by species and stage. This extends the existing
pipeline rather than maintaining a separate generator per species. Species
rules are required because recoloring/scaling an oak cannot produce compound
ash leaves or a conifer's central leader and needle sprays.

| Part | Purpose |
| --- | --- |
| Trunk / Branches | Separate visible surfaces of continuously welded structural wood |
| Roots / RootTips | Structural roots and continuous fine underground tips |
| Twigs | Fine branches, living tips and ash leaf stalks |
| Leaves | Oak cards, shaped ash/birch blades or points carrying instanced spruce needles |
| COL_SOLID_Trunk | Nine simplified closed convex trunk pieces, `blocking=true` |
| COL_SOFT_Branch | Three interaction pieces per major limb, `blocking=false` |

Root geometry is local to soil Z=0 and does not conform to game terrain. The
smallest juvenile roots may be represented entirely in RootTips. Fine branches
and needles have no collision. Branch proxies cover main limbs, not every twig;
juvenile proxy padding scales with tree size. **Show Collision Proxies** displays
only the current tree's proxies. Player slowdown requires engine implementation.

Hidden Wood and UV_Source objects retain construction/projection data. Bark
uses matched color, roughness and normal detail with actual geometric relief on
welded wood. Fine tips share the species bark with normal shading; birch bark
color follows local branch thickness on mature/large birches; juvenile birches
share a consistent young-wood surface. Juvenile wood
uses a finer welding grid and reduced bark relief. Leaf wind and camera-facing
representations belong in the engine renderer and are not implemented here.

## Validation and limits

Native installed-operator scenarios, source hashes, visual evidence and strict
independent review verdicts are recorded in `../../Docs/ValidationResults.md`
and `../../Docs/ValidationEvidence/BlenderTrees`. Reference preservation is
checked with mesh/UV/guide/material fingerprints. Repeated seed generation,
next-seed changes, selection, mixed gallery and edited-guide rebuilding are
checked through the same panel operators used for authoring.

The fixed visual matrix covers juvenile and mature forms of all four species,
plus Large oak. Large ash, spruce and birch presets are available but are not
part of that visual acceptance matrix. Reviews cover the recorded views and
seeds, not every possible combination of controls.

Spruce uses native Geometry Nodes with closed, three-dimensional needles.
Juvenile points instance individual 12-vertex needles. Mature/large points
instance seeded variants of short, volumetric 64-needle sprays (768 vertices),
following each fine shoot. Rotation, scale and variant are stored on the points;
prototypes and node groups belong to that species/stage. Adult previews show
one spray in 24 to keep Blender interactive; final renders show every spray.
These points are not an expanded export mesh. Realize instances for a mesh-only
export, or derive cheaper engine foliage. No export conversion is included.

These are detailed source meshes. Mature ash alone contains about 4.75 million
foliage vertices; the refined mature birch has about 8.38 million. Spruce point
counts substantially understate its realized geometry (12 vertices per needle).
Actual counts/timing are evidence, not a shipping
budget. Large/dense settings can cost substantially more. No runtime/export/LOD,
networking, collision behavior, or game performance acceptance is implied.

`tree_library.blend` and the original oak file are large generated files ignored
by Git. `recipes.json` schema 2 records species/stage, protected status, seed,
settings, source hash and full guide controls/handles; it is a manifest, not an
importer. The installed add-on SOURCE and generator ROOT point to this workspace.
Update them when relocating it. The existing project's oak leaf atlas remains
an input for oak regeneration; the packed Blender files contain their images.

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
