# Blender Tree Lab

Procedural tree authoring in Blender 5.2.2. The generator creates the structure
from a seed and growth settings. Editable Bezier guides control individual major
limbs. This is an authoring system; it does not modify the playable world.

## Using the installed panel

Open `oak_studies.blend`. In the 3D View sidebar, choose **Tree Lab**.

1. Choose Open-grown, Woodland, or Weathered and a seed.
2. Adjust height, spread, girth, lean, upward growth, droop and branch angle.
   Crooked Growth varies trunk and limb curvature. First Fork Height controls
   where the crown begins. Growth Direction and One-sided Crown bias growth
   toward a chosen horizontal direction.
   Branch Density and Leaf Density control crown fullness. Root Spread controls
   lateral reach relative to trunk size; Root Depth is measured in metres.
3. Select **Generate from Seed**. **Next Seed** generates another specimen.
   The same seed and settings reproduce the same geometry with the same
   generator version. A new seed can change branch count, attachment, direction,
   reach, bend, fine growth and leaf placement.
4. For deliberate branch shapes, enable **Show Direction Guides**. Select a
   guide curve and move its controls in Edit Mode. Choose **Rebuild from Edited
   Guides**; the operator exits Edit Mode for you. The guide controls the main limb;
   smaller branches, leaves and collision proxies regenerate around it.
   The limb stays attached to the trunk. Editing the trunk guide also updates
   primary attachment positions.

Generating from the seed replaces that specimen's edited guides. Rebuilding
from guides preserves them and uses the specimen's original seed. Global main
limb shape controls apply when generating from a seed; fine branching and leaf
density also apply when rebuilding guides. Save the Blender file to retain
manual guide edits.

Each seed grows five to seven main roots and two smaller offshoots per root.
They emerge at the trunk base and taper below local soil level (Z=0). Changing
root spread/depth preserves the crown's random stream. This is a local root
shape; it does not sample or conform to the game's terrain.

## Meshes and gameplay roles

| Part | Purpose |
| --- | --- |
| Trunk | Visible trunk surface |
| Branches | Visible welded major limbs |
| Roots / RootTips | Joined structural roots and continuous fine underground tips |
| Twigs | Fine branches and narrow living tips |
| Leaves | Bent, textured leaf geometry with no collision |
| COL_SOLID_Trunk | Nine simplified convex trunk segments; `blocking=true` |
| COL_SOFT_Branch | Three convex interaction segments per primary limb; `blocking=false` |

**Show Collision Proxies** displays the current tree's proxy meshes. Their
`collision_role` is `solid_trunk` or `branch_interaction`. The latter is intended
for nonblocking triggers such as player slowdown. These Blender properties
describe intended roles; the engine must import and implement that behavior.
Roots are visual geometry; no separate root collision is authored. Branch
proxies cover primary limbs, not every twig.

The hidden Wood and UV_Source objects retain construction and bark projection
data. They are not additional rendered parts. Primary guides, render meshes and
collision proxies all derive from the same skeleton.

## Rendering and game integration

Blender generates leaf placement and bent leaf geometry. Wind, camera-dependent
rotation and distance-based representations belong in the game renderer.
Near trees should preserve leaf attachment and shape; camera-facing clusters
can be evaluated for middle distance, with tree billboards for distant trees.
Camera-facing foliage has not been implemented by this authoring tool.

The current full-detail meshes are visual authoring assets. They are not a
measured game mesh budget, LOD set, runtime seed generator or finished export
pipeline. No game collision/slowdown behavior or runtime performance is claimed.

The bark generator uses matched colour, normal, roughness and scanned height.
Large welded wood receives actual geometric relief before its render parts are
split, preserving shared boundary positions and normals. Fine twig/root tips
use normal shading. This authoring detail must be reduced or baked into game
assets; it is not a runtime parallax shader. Candidate acceptance and failures
are recorded in the validation ledger.

## Source and local installation

`build_oak_studies.py` is the canonical generator. `tree_lab_addon.py` provides
the panel and operator and executes that generator. The installed add-on is
named `voxels_tree_lab.py` in Blender's user add-ons directory. Its SOURCE value
points to this workspace's generator; update it when relocating the workspace.
The generator's ROOT value likewise identifies this workspace's assets.

The packed local `oak_studies.blend` is ignored by Git because it is a large
generated working file. Keep the source and seed/settings with the Blender
file. Source changes may change a seed's result; seed identity alone does not
promise compatibility across generator revisions.
`recipes.json` records the saved specimens' seeds, settings, generator SHA256,
and complete editable guide coordinates/handles. It is a manifest, not an
automatic importer. The installed panel, the included bark maps and this
workspace's existing leaf atlas are required to regenerate the packed file.

Visual evidence and review history are recorded in
`../../Docs/ValidationEvidence/BlenderTrees` and
`../../Docs/ValidationResults.md`. Independent visual approval is separate from
functional control checks and game acceptance.

Current bark maps are unchanged downloads of Charlotte Baglioni's
[Japanese Camphor Bark](https://polyhaven.com/a/japanese_camphor_bark), shared by
Poly Haven under [CC0](https://polyhaven.com/license).
`Textures/japanese_camphor_bark_source.json` records source URLs, the 1.8m square
scan dimensions, file sizes, MD5 and SHA256 hashes. Its mostly intact, finely
fissured surface retains small worn areas without prominent repeated knots.
Seeded shifted patches vary the mapping; all four maps use matching coordinates.
The appearance is an art choice, not a verified botanical species identification.
Earlier rejected Bark Brown 02 and Bark Willow scans retain their provenance in
`Textures/sources.json` and `Textures/bark_willow_source.json`. Earlier candidates
also used Jolcham Oak Bark 01.
The leaf atlas reuses the project's existing original leaf artwork. Its
provenance is in `../TreeSources/README.md` and
`../../Assets/textures/trees/README.md`.
