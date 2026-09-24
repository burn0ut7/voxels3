# Imported spawn trees

The production population uses the installed Blender catalog: juvenile
and mature oak, ash, Norway spruce and silver birch, plus large oak and generated
seed/habit variations. `Assets/models/tree_lab/catalog.json` is published by the
installer after validating completed asset manifests. The former
runtime mesh generator is no longer called by the playable population. Its old
editor baking utilities are not the source of the current forest.

`VoxelManager.Trees.cs` owns population lifetime, the authored spawn anchor and
world changes. `SpawnTreePopulation.cs` owns deterministic placement, shared model
references, derived render objects and trunk colliders. Blender authoring, export,
asset hashes and reusable prefabs belong to [Blender imports](BlenderTreeImport.md).
The game does not invoke Blender or generate a unique mesh for every placement.

## Inputs and placement

World seed, canonical terrain snapshot and horizontal spawn anchor determine the
candidate grid, jitter, species, age, yaw and scale. Capacity is512 within a240m
disk, with18m grid spacing and a14m central clearing. Species selection covers
four species; the age channel chooses juvenile below0.22, otherwise mature, with
large above0.88 where that species has a large specimen. A separate deterministic
hash selects a specimen within that species/stage bucket. Catalog entries are
sorted by key, and the immutable choices are captured by placement work. Adding
catalog entries can change chosen shapes; a fixed catalog preserves selection.
Placement scale ranges0.78-1.22. This is a fixed
catalog of source seeds, not arbitrary runtime branch generation or biome-specific
species distribution.

The existing placement path accepts grass material, excludes strong mountain
influence and low coast, brackets the canonical SDF surface, checks slope and four
support probes, and seats the root origin4inches into terrain. Roots from the
model extend below that origin. A worker evaluates at most eight candidates per
batch; at most two results integrate per update. Correction-range/epoch checks
reject stale dependencies without restarting for unrelated edits. Replica coverage
controls visibility and collision readiness. World/anchor changes, disable and
disposal release render objects, colliders and pending work.

## Rendering and collision

Catalog parsing requires unique safe keys, known species/stages, and juvenile and
mature coverage for all four species. The catalog loads one model per update and requires finite exported/compiled
bounds agreement within2inches, three LODs and valid physics. A missing model waits;
an invalid loaded model stops with an explicit console failure. There is no old
forest fallback. Shared models are reused by SceneObjects. ModelDoc switch values
are0/20/65. Installed engine clutter source describes the native LOD metric as
projected screen coverage with instance scale; these values are not20m/65m world
distances. View resolution/FOV and scale therefore matter to transition review.
Trees render within900m of the viewer; shadows are enabled within65m.

Each placement owns an ordinary static, nontrigger ModelCollider with the same
transform as its render object. Its nine cylinder shapes come from the exported
trunk proxies and remain independent of visual LOD. These objects are derived,
not saved or networked as a second world-state representation. Branches/foliage
are nonblocking. Separate branch trigger capsules exist in reusable prefabs, but
the population does not instantiate them and player slowdown is not implemented.

The fuller-canopy revision introduces shared shader wind and distant views as
described below. Exported source meshes, animated render positions and distant
textures are derived data. Collision remains static and independently selected;
weather ownership and configuration remain future work.

## Diagnostics and qualification

`voxel_trees_info` reports catalog/population counts, per-variant placement counts, bounds/LOD/physics import
checks, peak update/load duration and nearest specimens with collider object IDs.
`voxel_trees_reload` disposes the current population, enables trees and loads the
installed compiled catalog again. Use it after re-export/recompile, not as a
second placement implementation. Console reload does not save the scene.

All eighteen current models pass compiled bounds/three-LOD/physics checks on
26.09.15; the observed world produces294 placements. Version2 distant metadata
and dependency hashes are complete. Independent review accepts the corrected
stationary fade and transient blue-hole defects, but a near/far crown contour
and density mismatch remains. The latest matched figure-eight passes frame rate,
frame-time tails, allocations and correctness; process/GPU memory exceed the
recorded limits. Fresh-session memory qualification and complete visual approval
remain pending. Earlier failures, limited static/wind approvals and exact
measurements are preserved in [the validation ledger](../ValidationResults.md).

## Fuller canopy and wind/LOD iteration (2026-09-21, candidate design)

TREE-REALISM-001 is not accepted yet. Authoritative inputs remain the stored
Blender specimen/revision, branch guides and canonical terrain placement. Full exports
retain every broadleaf at authored scale (folded LOD0, a simpler fold for oak
LOD1, and an outline at LOD2), record
primary-branch identity/weight and shared leaf attachment data, and still rebuild
wood from source. No geometry-cache shortcut is introduced. Motion data is RGBA16
linear, point sampled without compression; UV1 identifies leaves, vertex color
stores primary identity, leaf normal and offline canopy occlusion. Bark retains
UV1 grain and vertex alpha collar mask. Shared root/primary/leaf motion executes
in the vertex shader for color and depth/shadows, at fixed ambient direction
(.8,.6,0),strength1. Roots belowz0 are fixed. Maximum strength is2; reserve bounds
for the complete bounded motion. Static blocking trunk remains a deliberate
ambient-wind approximation. Weather and player configuration remain later work.

The alternative of thinning/inflating leaves at every mesh LOD made holes and
oversized patches. The candidate instead derives far views from the same compiled
near model with native albedo/normal/depth/ambient-occlusion capture, eight azimuths and four
elevations(-15,15,45,75). Blend neighboring views and depth-reproject the camera
ray to retain perspective. Shared far models avoid per-tree texture/model copies.
The population owns near/far render object lifetime. Instance-scaled distance
selects far at110m and returns to detailed meshes at100m; new placements select
at105m. A0.35s time-based fade finishes even when the camera stops. The10m
hysteresis interval prevents repeated switching near a boundary. Selection checks
run every0.1s when settled and every frame while a transition is active; a newly
selected transition uses only the current frame duration. Creation stays two
trees/frame. The900m render and65m shadow ranges and SDF support/collision remain
unchanged; no network truth or terrain edits are added. Animated detailed bounds
and far bounds must contain motion; fixed performance criteria cover allocations,
memory and frame tails.

The Version2 bake format stores albedo/coverage in one BC7 texture and
view-facing hemisphere normalXY/occlusion/depth in another. Hemisphere encoding
avoids octahedral mip seams; explicit LOD and mip-aware tile clamps isolate views.
Depth lookup inverse-bends points along the actual rasterized camera ray, so
coverage and written depth refer to the same animated surface. Blending view
depths stays on that ray. These changes add pixel work and require measured
qualification. Runtime rejects other versions and stale source hashes. Publication
is performed outside Play; an incomplete library is not a gameplay qualification.

Native asset baking is editor-only, yields between views, never modifies the
playable scene and validates full source hashes. Runtime loads installed resources
only. The packer publishes each completed texture and material by atomic rename,
then publishes the completion manifest. Replace model dependencies with the editor
closed: live FBX files can remain locked even in Edit mode. These
per-file replacements do not make a whole multi-resource reload atomic.
Any change to imported dependencies invalidates far bakes. The unused procedural
tree geometry implementation has been removed, leaving one canonical imported
source. Candidate quality/performance must be measured through
the playable population; successful baking is not acceptance.


Runtime qualification found two native integration requirements: animated wood
shaders declare VertexNeedsPropOrigin, matching the stock bark shader, so the
renderer cannot merge origin-dependent trees. The shader-expanded far mesh
provides Mesh.UvDensity = diameter * metadata.Azimuths for its capture atlas; coincident
CPU vertices cannot describe the projected surface scale to texture streaming.
Without this value, the observed far trees used blurred low-detail images.
Explicit reconstructed fragment depth is marked precise to preserve arithmetic
across depth and forward shader variants. Matched evidence and the remaining
performance/visual gates are recorded in the validation ledger.

Near and far SceneObjects set Batchable=false because their TreeLodFade render
attributes differ per instance. Allowing material batching produced blue holes
during otherwise finite transitions; disabling it removed that defect in the
same production-camera sequence. This follows the installed SceneObject.Batchable
contract for dynamic render attributes. Shader origin preservation and per-object
attribute preservation are separate requirements.
