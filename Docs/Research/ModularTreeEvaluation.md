# Modular Tree feasibility for Voxels3

2026-09-21. Research and a native Blender module check; no production generator,
asset, shader, population or saved Blender library has been replaced.

## Decision

Modular Tree is a viable **offline authoring dependency to evaluate**, and its
released native module works in our Blender 5.2.2/Python 3.13 session. Prefer
using its core to produce branch structure and wood in Blender, then adapting
our existing s&box export path. Do not replace the forest based on compatibility
alone: branch appearance has not yet been compared in rendered views.

The first priority is more natural finished shapes. Growth over time in the game
is a separate requirement; the user was asked to distinguish these and this
assessment does not assume a runtime growth system is needed.

Keep our existing catalog/placement, model reuse, collision, material baking and
native distant-bake path. A successful adoption should replace the corresponding
Tree Lab generation responsibility, not retain two production generators.
The module is useful directly in Blender; copying its algorithms into C# or
loading its CPython native binary in game code is not the proposed path.

## Version and evidence

- [Official release 5.5.2](https://github.com/GoodPie/modular_tree/releases/tag/5.5.2),
  tag `eca242d1b7208ea2b0834a1d8723f5732c43dd2a`.
- Inspected main `e1fb8273442655d3bd9cc8ba4474fc26cfbc6f67`; its `m_tree/source`,
  `m_tree/python_bindings` and `python_classes` have no diff from that tag.
- [Manifest](https://github.com/GoodPie/modular_tree/blob/eca242d1b7208ea2b0834a1d8723f5732c43dd2a/blender_manifest.toml)
  requires Blender 4.3.1 or later. The Windows release includes CPython 3.11 and
  3.13 wheels. We loaded the latter in the existing visible Blender session.
- The Blender extension page could not be fetched by the web tool (HTTP 402).
  Release/source claims here come from the maintainer's GitHub repository.
- [Core license](https://github.com/GoodPie/modular_tree/blob/eca242d1b7208ea2b0834a1d8723f5732c43dd2a/m_tree/LICENSE.md)
  is MIT; the addon manifest declares GPL-3.0-or-later. Keep those source
  boundaries and notices explicit when integrating; do not label the entire
  addon MIT.

The fixed recipe, parameters, checks and results are recorded as
`MTREE-FEASIBILITY-001/v1` in [the ledger](../ValidationResults.md#mtree-feasibility-001v1--blender-module-evaluation).
[Raw measurements](../ValidationEvidence/ModularTree/module-feasibility-v1.json)
are limited to module generation/meshing, not the full addon UI or game import.
[Workspace source fingerprints](../ValidationEvidence/ModularTree/workspace-source.json)
identify the existing uncommitted tree implementation inspected for comparison;
they are not a committed or accepted production baseline.

| Native path, seed 1701 | Vertices | Quads | Branch IDs | Maximum depth | Two observed generation/meshing times |
| --- | ---: | ---: | ---: | ---: | --- |
| Release oak preset | 118,614 | 110,656 | 1,393 | 12 | 170.1 / 71.7 ms |
| Same trunk, default GrowthFunction, five iterations | 1,198 | 1,105 | 17 | 2 | 0.915 / 0.902 ms |

Both repetitions matched their own geometry/attribute hash; values were finite,
indices were in range and the existing scene stayed at 59 objects. These are
different workloads, so their timings are not a comparative performance result.
The growth output's minimum Z was -0.388m; without viewing the geometry, this
does not establish a particular penetration defect, but it does require checking
ground clearance. No meshes were installed in s&box or added to the Blender scene.

## What its branching actually does

Our current [Tree Lab generator](../../Tools/BlenderTrees/build_oak_studies.py)
builds a prescribed scaffold, samples curved limbs, then recursively adds children
using parent tangents, angles, lift/droop and seeded variation. Juvenile/mature
stages alter the recipe. They are not elapsed biological time. Its sweeps already
store parent references and branch frames, which the current exporter consumes.

Modular Tree has two distinct relevant paths:

1. [Quick Generate](https://github.com/GoodPie/modular_tree/blob/eca242d1b7208ea2b0834a1d8723f5732c43dd2a/python_classes/operators.py#L155)
   uses TrunkFunction, BranchFunction and optional sub-branches. It does **not**
   invoke GrowthFunction. Branches extend incrementally, split probabilistically,
   use spiral attachment distributions and a crown envelope, and bend with
   accumulated branch weight and stiffness. This is the more direct candidate
   for finished oak shape authoring.
2. [GrowthFunction](https://github.com/GoodPie/modular_tree/blob/eca242d1b7208ea2b0834a1d8723f5732c43dd2a/m_tree/source/tree_functions/GrowthFunction.cpp)
   creates dormant lateral buds, distributes vigor with apical dominance,
   activates/extends/splits/cuts tips, thickens branches and applies gravity over
   iterations. Preview iteration regenerates the requested stage; it is not a
   persistent multiplayer growth simulation.

Its `light_flux` is a count/weight of growing tips and dormant buds; target vigor
is `1 + iteration^1.5`. The inspected growth path does not sample sunlight,
occlusion, nearby trees or branch collisions. Leaf-vein space colonization in
the README concerns veins **inside a leaf**, not light-seeking crown growth.
Consequently, improved forms are plausible, but realistic environmental
competition is not supplied by installing this addon.

An optional
[PipeRadiusFunction](https://github.com/GoodPie/modular_tree/blob/eca242d1b7208ea2b0834a1d8723f5732c43dd2a/m_tree/source/tree_functions/PipeRadiusFunction.cpp)
derives parent radius from child radii using a power sum. This is useful to
evaluate for coherent fork thickness; it is not called by Quick Generate.
GrowthFunction also computes vigor-scaled child lengths but constructs extension
and split nodes using `branch_length` instead. Treat its documented biological
interpretation as an approximation, not a validated growth model.

## What transfers, and what we must supply

| Need | What Modular Tree provides | Required s&box work |
| --- | --- | --- |
| Branch shape and junctions | Native branch graph and connected wood mesher | Compare naked scaffold/junctions; adapt rest frames, roots, semantic parts and bark UV/baking to our exporter |
| LODs | Leaf-card and crossed-card helpers; Blender leaf LOD switching | Produce our three full-tree mesh levels and compiled ModelDoc; preserve attachments and rebake distant views |
| Branch/leaf wind | Stem ID, depth, pivot, direction, extent; Unreal/Unity-oriented exporters | Export actual parents and leaf attachments; translate into our validated data format and shaders |
| Leaves visible from changing viewpoints | Blender OFF/AXIAL/CAMERA modes; curved procedural leaf geometry | Implement bounded view correction in the s&box leaf shader, with matching depth/shadow behavior |

### Wind: preserve ancestry before converting to a mesh

The [mesher](https://github.com/GoodPie/modular_tree/blob/eca242d1b7208ea2b0834a1d8723f5732c43dd2a/m_tree/source/meshers/manifold_mesher/ManifoldMesher.cpp#L425)
assigns a new stem ID and depth to each side branch. Its published mesh attributes
do not include an explicit parent ID. The released Python Tree binding does not
expose the source node graph either. A core/binding extension is therefore needed
to export the true parent relation and rest frames; avoid guessing parents from
nearest geometry after junction smoothing.

The [Unreal exporter](https://github.com/GoodPie/modular_tree/blob/eca242d1b7208ea2b0834a1d8723f5732c43dd2a/python_classes/pivot_painter/formats/unreal.py#L202)
explicitly uses hierarchy depth as a proxy for parent index. These cannot identify
the same relationship: two branches at depth 3 may have different parents.
[Epic's format](https://dev.epicgames.com/documentation/en-us/unreal-engine/pivot-painter-tool-2.0-in-unreal-engine)
distinguishes parent index from steps to root. Do not claim its textures provide
correct inherited motion in our renderer unchanged.

Our [motion exporter](../../Tools/BlenderTrees/export_motion.py) currently maps
descendants to primary limbs and encodes the primary identity in a byte, with a
256-primary limit. The measured 1,393 MTree branch IDs cannot simply be packed
into that channel. Retain full source ancestry, then explicitly map it to a
bounded runtime deformation hierarchy. Establish that mapping and its cost
before promising independently simulated motion for every twig. Leaves must
inherit their supporting branch motion, then rotate about their own attachment;
root anchoring, joint continuity and transformed normals remain mandatory.

### Leaf view correction

Our [current leaf shader](../../Assets/shaders/trees/tree_lab_foliage.shader)
already supplies leaf flutter, primary-branch motion and main sway. It has no
camera-facing correction. Its two-sided rendering prevents backface disappearance
but does not restore projected area when a blade becomes edge-on.

MTree's [geometry-node implementation](https://github.com/GoodPie/modular_tree/blob/eca242d1b7208ea2b0834a1d8723f5732c43dd2a/python_classes/resources/node_groups.py#L458)
uses a camera object for distance and orientation. These evaluated Blender nodes
do not become live s&box behavior through FBX export. Its axial mode preserves
one branch-aligned axis; full camera mode rotates the leaf toward the camera.
Neither is a ready-made s&box material.

Recommended design: retain small curved/folded leaves up close; introduce a
smooth, angle-limited rotation about the attached leaf base when its projected
area becomes too small. Define a stable rotation axis and fallback for nearly
parallel view/axis directions. Apply the same rigid correction to position,
normal and tangent after inherited branch motion, without moving the leaf base.
Allow natural variation rather than forcing every close leaf to face the viewer.
Axial rotation alone has degenerate viewpoints; folded geometry and differently
oriented leaves still matter.

Use identical geometry in color and camera-depth passes. For shadows, explicitly
choose and verify a stable view reference; blindly using a shadow camera can
produce a different leaf shape from the visible one. This project previously
exposed foliage pass mismatches, so depth/shadow equality is an acceptance gate.
Whole-tree distant impostors remain a separate representation with their own
view selection and baking; leaf billboarding does not solve crown LOD mismatch.

### LODs and source geometry

[LeafLODGenerator](https://github.com/GoodPie/modular_tree/blob/eca242d1b7208ea2b0834a1d8723f5732c43dd2a/m_tree/source/leaf/LeafLODGenerator.cpp)
builds a bounding quad, intersecting quads, and sample directions. It does not
provide our finished three-level wood/foliage models, baked atlas, native
impostor assets, material mapping, physics or catalog integration. Its node-based
leaf LOD and camera culling should be disabled/frozen appropriately for export,
so an authoring camera cannot silently remove leaves from a reusable model.

Our existing LOD exporter retains leaf attachments and changes blade subdivisions.
Keep that identity requirement, UV conventions, unit conversion, manifests and
current hard geometry caps. The 1,350,000-triangle import cap is a safety ceiling,
not a target or evidence of acceptable forest performance. A complete MTree
canopy and material set have not yet been measured.

## Smallest adoption slice and acceptance

First author one mature oak from a pinned MTree recipe using the actual module,
compare its exposed scaffold with the current mature oak from fixed viewpoints,
and select the branch recipe before growing the catalog. Evaluate forks,
taper, repetitive branching, self-intersections, ground clearance and overall
crown form. Compatibility measurements above do not satisfy this visual gate.

If the scaffold is accepted, extend the native source/bindings to export the
graph and adapt the existing exporter to consume it as the canonical source.
Inputs are pinned generator/version, seed and recipe; outputs are stored source
geometry/graph, three derived mesh LODs, materials, motion data, collision and
dependency hashes. Blender owns authoring state; the game owns placement and
time-varying wind. Run generation/export on Blender's main thread, one specimen
at a time; no per-player generation, new terrain ownership or per-leaf objects.
Regeneration invalidates all downstream meshes/motion/bakes for that specimen.
All LODs derive from that same rest graph and preserve attachment identities.

Then qualify one exported tree through the existing playable population before
regenerating the library. Define fixed visual/wind/LOD parameters in the ledger:
calm and gust motion, reversed wind, rotated instances, close orbit/above/below
views, silhouettes, roots/joints, leaf bases, depth/shadows, native LOD changes
and near/distant crown agreement. Preserve existing performance scenario inputs
and compare the canonical figure-eight with the latest comparable accepted
baseline, including tails, streaming, memory, allocations and correctness.
The existing forest has pending memory/visual qualification; that remains pending.

Alternatives: extending our current recursive scaffold is a smaller dependency
change but leaves us owning its branching rules. Porting MIT core algorithms to
C# is possible work, but adds meshing/determinism/maintenance obligations and
does not bring Blender node behavior into s&box. A true light/space competition
model addresses requirements absent from MTree, at a larger authoring cost.
For finished forms, evaluate the existing offline module before either rewrite.
