# Blender tree model assets

Import qualification in progress. Protected Blender libraries and separately saved source revisions own authoring
geometry; `Tools/BlenderTrees/export_sbox.py` derives mesh assets in Blender.
It works in a temporary scene and stages its output outside the game asset tree.
It never saves over the library or changes its protected oak references.

Connected growth sources with `surface_method=solid_union` contain fine branches
and roots in their single closed wood mesh. The exporter preserves that wood
at LOD0 and simplifies copies for lower levels; it does not require or fabricate
the legacy RootTips/Twigs objects. Bark projection consumes the builder's yielded
work before baking. Every actual source semantic part remains required. The
individual-needle spruce format is explicitly rejected until its adapter is
qualified. Exporting an oak does not establish support for those spruce sources.

Growth exports have an explicit asset key separate from the Blender specimen
label and retain graph, foliage-generator and settings provenance. The addon
marks them `catalog_eligible=false` for initial evaluation. Installing only these
samples leaves the forest catalog untouched; sample installation rejects keys
already owned by that catalog or an installed library asset. Subsequent catalog
rebuilds exclude these samples. The earlier season18 oak sample is installed as
`oak_growth_18_open_grown_271828`, with all60,688 leaves in each LOD. Native game
import qualification of that earlier sample was deferred when the editor closed.
The current foliage shader implements camera-facing correction with exported leaf
pivots and axes. Native compilation now passes; the newer dense and young samples
retain visible leaf faces in the inspected opposing views. Continuous motion,
all viewing angles and shadow consistency remain separate validation gates.

The initial catalog contains juvenile and mature oak, ash, Norway spruce
and silver birch, plus large oak. `catalog_variations.json` adds nine oak profiles
to reach twelve distinct oaks and eighteen models overall. Each profile combines
a source seed with explicit growth-control overrides applied after its species,
stage and form preset. This prioritizes the user's request for recognizable oak
variety: young spreading, tall woodland, low fork, broad meadow, leaning,
asymmetric, compact and large crowns. The installed catalog is generated from verified
asset manifests; generation recipes alone do not establish installation. A stored specimen's seed, settings and edited
guides own its shape. Export is deterministic for a fixed source and exporter
revision. Each catalog entry records both identities and its mesh budgets.

The three LODs share the same source crown and root origin. Saplings with only
fine roots retain RootTips without an artificial thick-root mesh. Every authored
semantic part must remain present at every LOD. Simplified wood keeps
the source bark through baked color, roughness and tangent normals. The closed, fused construction wood is simplified as one continuous surface
before UV packing and baking; independently decimating the visible parts created
cracks at their shared boundaries. Face membership preserves the semantic parts,
and the final split retains shared boundary coordinates and corner normals. All three wood LODs have
distinct islands in a shared structural atlas; no post-bake decimation may
move a triangle across an atlas island. Fine branches and root tips are resampled
from their original closed source rings, retain major bends, and keep three to six
radial sides according to tube radius with end caps. Largest source sweeps are selected within the existing
per-LOD triangle budgets. They use cylindrical coordinates and a reusable
color/normal/roughness tile baked from a copy of their parent bark material in Blender.
The fixed brown twig tint was rejected because it created abrupt color changes on
gray limbs. Scan coordinates stay periodic and unshifted inside the tile; palette
operations remain those of the source material. Birch's procedural marks use a
periodic torus coordinate mapping above the dark root flare. Fine relief is gentler,
but the material no longer substitutes a separate brown palette.
Structural wood reuses the same color and normal tiles through branch-aligned UV1 charts, while UV0
retains the unique source bake. The tree_lab_bark shader modulates baked species color
with a neutral ratio of tiled luminance to the texture average and blends stronger
tiled normals with the bake. Final simplified faces are reprojected onto original
branch frames before baking; protected geometry and semantic membership must remain
unchanged. A shared per-vertex collar mask preserves its suppressed core while
expanding and smoothing the transition. At collars, continuous world-space
triplanar grain replaces the branch UV detail, retaining readable color and normal
detail without sharp chart patches or a blurred band. This uses the same bark tile
at32-inch horizontal/64-inch vertical periods. Its world-space collar detail is
an ambient-wind approximation; larger deformation may require rest-space sampling. U retains its integer circumference period; only V is
scaled to enlarge the bark plates. Height-derived occlusion makes recesses read
in ambient light. A matching2k height scan feeds the engine
ParallaxOcclusion_Grad routine below eight meters, fading from80 to315inches and
at fused collars. Degenerate tangent frames skip normal transformation and
parallax. Bark is explicitly opaque, nonmetallic and nontransmissive. The height
map adds a texture dependency; runtime cost requires measured qualification. This avoids collapsing thin tubes to
slivers and allocating only a few atlas pixels to each of thousands of branches.
Broadleaf LODs retain every authored leaf at its original size and attachment.
Oak LOD0 uses nine-vertex curved blades (eight triangles), LOD1 a
raised center fan (four), and LOD2 the same outer outline (two). Oversized LOD0
exports distribute the same four-triangle folded blade among the eight-triangle
blades, retaining every leaf and its attachment. The exporter owns a1,350,000
triangle limit perLOD, including wood. This keeps triangle corners below the
4,194,303-entry saturated internal table observed in native26.09.15 crash dumps.
The table's exact engine semantics are not symbolically identified. All three
bounded models now import successfully on26.09.15 with correct bounds, three
LODs and physics; full visual/performance qualification remains in the ledger.
This changes blade subdivisions, not leaf count, scale or placement. Ash/birch retain
tapered geometric blades with a center fold at LOD0. No exported mesh level thins or
inflates leaves. Authored trees with TreeModelLod apply the separate view-dependent
whole-leaf retention policy described below. Spruce derives short crossed foliage cards from the source
needle cells, with fixed centers and sizes across mesh LODs (0.045m juvenile,
0.18m mature). The native renderer selects these three mesh levels. The separate
distant representation and automatic fade belong to [SpawnTrees](SpawnTrees.md).
All representations still require in-game visual and performance qualification.

Blender coordinates are meters. The exporter owns conversion to engine inches
(`1 / 0.0254`). Render geometry and trunk collision use that same conversion.
Blender5.2's FBX_SCALE_NONE path adds a100x transform even when apply_unit_scale
is false. ModelDoc RenderMeshFile import_scale0.01 compensates it; import_rotation
`[0,-90,0]` (pitch,yaw,roll) corrects the importer's90-degree XY rotation.
Physics endpoints are already in inches and receive neither correction again.
Production loading compares compiled bounds with the union of exported LOD bounds
within2inches, and requires three LODs and valid physics before placing trees.
Nine simple trunk cylinders derive from the stored proxy rings and accompany
every model independently of render LOD. The current dense grove and new installer
output use trunk-only runtime collision. Branch proxies remain in authoring
metadata; the installer does not create branch or foliage collision objects. Older
installed library prefabs retain their previous contents until reinstalled. Neither
path implements branch slowdown.

The deliverables are reusable models and ordinary ModelRenderer/ModelCollider
prefabs. Terrain state, placement and population ownership remain with the
existing system described in [SpawnTrees](SpawnTrees.md). At the user's request,
that population now selects the installed compiled catalog instead of the old runtime
mesh generator. The imported prefabs also remain usable independently. The
population instantiates trunk collision, independently of its render representation.
High-detail source meshes are unsuitable for direct runtime loading; compiled
assets avoid per-player Blender generation and per-instance model construction.

Every full export rebuilds and bakes from the current stored specimen. The completion
manifest is invalidated before output changes and atomically replaced only after
all model, material and texture files have been hashed. The installer checks those
hashes and exact dependencies before creating the prefab or copying assets. It
invalidates the destination completion marker before replacing files. Each file
is copied to an unrecognized temporary suffix and atomically renamed, so asset
watchers cannot read a partial FBX or manifest. It verifies all installed
dependency hashes and publishes the catalog atomically. Stop Play before replacing
a loaded tree library; complete per-file publication does not make a whole
multi-resource hot reload atomic. It
does not accept partial exports or reuse a geometry/texture cache for full exports.
For palette corrections, `refresh_fine_materials(key)` explicitly rebakes only the
four fine-bark textures from the preserved same-species/stage reference. It first
validates the complete staged dependencies, invalidates completion, verifies every
other dependency is byte-identical, and atomically publishes new texture hashes.
Separate material-refresh provenance preserves the original geometry export identity.
The ordinary installer still validates every installed dependency. Actual Blender
material names map to fixed asset filenames, including when Blender adds suffixes.
Temporary part membership is isolated from authoring vertex groups; splitting
must conserve every simplified wood triangle.

`export_motion.py` derives primary-limb pivots/weights, leaf attachment positions
and normals from the same branch frames. UV1 selects a leaf entry from an
RGBA16 linear point-sampled texture; wood retains branch-aligned bark UV1. Vertex
color stores primary identity, wood weight or leaf normal, and collar/canopy
occlusion. Every LOD shares the same attachment identities. Numeric PNG writing
preserves channels without Blender display or alpha conversion; native readback
must agree within1.5/65535 before publishing the manifest. The candidate018
texture uses256/512/1024columns with at most1024rows; excess attachment counts
fail explicitly. Existing256-wide layouts retain their addressing.

Shared shader motion bends the tree above its fixed root, then adds primary-limb
response and faster rotation about each leaf base. Normals/tangents follow the
deformation, including color and depth passes. Close leaves are actual curved
geometry. New broadleaf exports additionally carry an authored long axis in UV2
(`LowPrecisionUv2`), with motion manifest version2 and `octahedral_uv2` encoding.
The opt-in leaf-facing feature rotates the whole folded blade around its posed
base using a camera frame, then adds bounded lean/flutter. It runs in world space
so the correction preserves already-scaled dimensions. Existing materials and
legacy conifer cards keep version1 behavior. This candidate is not game-qualified:
UV2 import orientation, compilation, visible motion, camera orbit and consistent
color/depth/shadow silhouettes are still required. Fixed ambient wind is a shader object
attribute; material file keys are not a qualified override route. Weather and
user-facing wind configuration remain a subsequent slice. Static trunk collision
is deliberately independent of render wind. See the [runtime contract](SpawnTrees.md)
and [research](../Research/TreeGeneration.md#wind-for-the-blender-library-september-20).

Realism iterations save immutable `Revisions/<revision>/<key>/source.blend` with
original-source, generator and revision hashes. They do not overwrite either
original library or `Variants/<key>/source.blend`. CandidateD's mature oak1701 has
independent static approval in its recorded views; full library, wind, automatic
LOD and performance acceptance remain tracked separately in the ledger.

The oak recipes retain the three original stage specimens (seed1701) and add:

| Profile | Seed | Stage / habit | Main shape controls |
| --- | --- | --- | --- |
| Young spreading oak |1712| Juvenile / open grown |4.2m height, wider crown, low fork |
| Tall woodland oak |1713| Mature / woodland |22m height, narrow crown, high fork |
| Weathered leaning oak |1714| Mature / weathered |8-degree lean, broad uneven crown |
| Low fork oak |1715| Mature / open grown |Fork at10% of stem, spreading limbs |
| Broad meadow oak |1716| Mature / open grown |Wider crown, fuller branch density |
| Upright irregular oak |1717| Mature / woodland |Higher crown, varied limb reach |
| Wide old oak |1718| Large / open grown |23m height, broad low crown, wider roots |
| One sided hillside oak |1719| Mature / weathered |Strong crown bias, modest trunk lean |
| Compact crooked oak |1720| Mature / open grown |12.5m height, uneven branching |

These are art profiles, not botanical age estimates. Height controls the generator;
exported bounds record the actual crown height. Names are retained in the separate
source libraries, manifests and prefab names. The earlier variation-only task had a user waiver for its performance test.
The current realism/wind/LOD work has no such waiver and requires the fixed
figure-eight before acceptance.

Validation, exact triangle counts, placement and performance scope belong to
`TREE-IMPORT-001/v1` and `TREE-POPULATION-002/v1` in [the ledger](../ValidationResults.md). Until those checks
pass, these outputs are candidates, not approved game assets.

UV semantics follow the [official vertex-input reference](https://sbox.game/dev/doc/rendering/shaders/reference/vertex-input-semantics): UV0 is LowPrecisionUv and UV1 is LowPrecisionUv1. Parallax uses the installed26.09.15 engine helper in core/shaders/common/classes/Decals.hlsl; it changes surface sampling, not collision or silhouette.


Dense motion payload candidate018 keeps the first256 linear texels for branch
pivots and places leaves after them. Texture width expands from256 to512 or1024;
both dimensions remain powers of two no larger than1024 so half-precision UV1
centers remain exactly addressable. The observed346,767-leaf unfinished age30 build requires a
512x1024 image. Each LOD uses the same stored dimensions and leaf entries; the
writer, UV encoder and shader branch lookup consume those dimensions. Wider
payloads report version3; existing256-wide payloads retain version1/2 behavior.
The maximum remains explicit:1,048,320 leaf pivots plus256 branch slots. Extra
leaves are never silently removed.

UV1 also seeds flutter phase and camera-facing roll. Resizing the atlas changes
those hashes across different exports; identities and phases remain consistent
between LODs within a single export. This is an authoring rebuild, not a promise
of temporal continuity while changing density. Native PNG/UV payload checks and
visible s&box validation are separate gates; the shader change remains pending
engine compilation, import orientation and visible wind/facing/shadow checks.


Candidate018 now passes native offline payload checks on every one of the
346767 leaf blades at all three temporary detail levels. Pivot IDs survive
half-precision UV1 exactly, UV2 direction error stays below0.115degrees, and
Blender decodes the512x1024 numeric PNG within the existing1.5/65535 bound.
Saved66059-leaf D data also matches the before018 writer byte for byte at the
same height input. These checks preserve source geometry and remove their
temporary meshes. They do not bypass the native game import limit: temporary
metadata-check meshes above that limit were never exported or installed.
Shader compilation, actual imported UVs, wind, facing, shadows, automatic LODs
and the figure-eight still require the visible game editor.


## Full-canopy evaluation packaging (026)

The user now prioritizes exporting oak samples with separate collision pieces
and three LODs. The current E18 source has544606 leaves; fitting its canopy into
one native model would exceed the existing import guard. The exporter keeps
the wood and every leaf, and package oversized evaluation trees as a single
prefab with one wood model plus deterministic contiguous leaf-range models. Each
model has three detail levels and stays below1.35million triangles per level.
Leaf range membership is fixed across LODs; all pieces preserve coordinates,
UVs, material indices, normals and the global wind/facing payload. Shared textures
and the source motion atlas remain owned once by the specimen export.

The completion manifest owns the render-model filenames and per-model LOD
measurements; the installer builds ordinary child ModelRenderers from that list.
The wood model owns the compound collision; foliage pieces add no blockers.
Smaller exports remain one model. Multipart evaluation exports must remain
catalog_eligible=false because population loading currently owns a single model
per catalog entry. Source wood exceeding the per-model guard is rejected before
baking; wood partitioning is outside this first E18 sample. No import limit is
raised, and leaves are not removed to fit it. Runtime appearance, LOD timing and
wind across model pieces remain unqualified until native editor checks run.

The first completed export is installed under
`Assets/models/tree_lab/oak_growth_18_open_grown_271828_dense/`. Its prefab
contains five renderers sharing the same origin and motion atlas. Aggregate
LOD triangle counts are 5,300,996 / 2,461,668 / 1,160,022; splitting the resource
preserves the requested full canopy and does not reduce its total runtime cost.
The nine trunk cylinders block movement; 186 separate branch capsules are
nonblocking interaction triggers. The catalog is unchanged. Export and installer
checks, installed dependency hashes, and independent package review passed.
Native compilation of all five models and the shared bark/foliage shaders now
passes in the visible 26.09.15 editor. Actual forced LOD0/1/2 captures retain the
crown, and independent review passes initial static import integrity. A ray hits
the solid trunk while a parallel ray beside it misses. Automatic LOD transitions,
continuous wind/facing/shadow behavior, player collision response and the
figure-eight remain incomplete; these narrower checks are not final acceptance.


A second evaluation prefab is installed under
`Assets/models/tree_lab/oak_growth_6_open_grown_271828_dense/`. It uses one model
with 64,696 / 28,612 / 12,906 triangles, retaining all 5,753 leaves, nine solid
trunk pieces and nine nonblocking branch capsules. Its 20 installed dependencies
and source preservation checks pass. Both samples are excluded from the catalog
and use source-season labels rather than calibrated botanical ages. The young
model also compiles natively; its collision ray hits its own trunk, and independent
review passes its displayed opposing views. The remaining game checks are shared
with the dense sample.


## Current 100-oak evaluation grove

At the user's request, `basic_example.scene` now disables the legacy automatic
tree population (`SpawnTreesEnabled=false`) and owns one saved
`Dense Oak Grove - 100` group. Its 100 children reference the dense mature oak
prefab, retaining all five renderers, automatic three-level LOD, shared motion
data, nine solid trunk pieces and 186 nonblocking branch triggers per tree.
The source assets and legacy catalog are preserved; this authored grove replaces
the old population in this scene without adding another spawning implementation.

The fixed 10-by-10 layout has 400-inch spacing, XY -1800 through1800, unit scale
and index-derived yaw; roots were traced against the actual current terrain
before authoring. These saved heights belong to that terrain snapshot and do not
reground themselves after terrain edits or recipe changes. All copies share one
source shape, so this evaluates instance cost rather than species/age variety.
The complete layout, source identities and empty/100 measurements belong to
TREE-100-029/v1 in the validation ledger. This is an evaluation workload, not
acceptance of a production forest density or final distant representation.

The first completed comparison is not performance-ready: the canonical route
averaged186.04FPS with100oaks versus396.33FPS with no trees; the ten-second
standing view averaged24.64 versus288.96FPS, with40.26ms versus3.12ms GPU time.
The settled wide-grove rolling observation was about9FPS. Terrain drain after
the route took148.0s versus10.2s. Both runs recorded zero runtime exceptions
and terrain-collision failures. These results fail the regression gate; they
do not qualify tree-player physics or continuous wind. Initial cache histories
differ and most of the route is outside the grove, as recorded with raw evidence.
Preserve close canopy quality while evaluating a cheaper distant representation
and shadow workload; no such reduction is implemented by this test.

Physics also averages8.61ms/frame in the dense stationary profiler and the route
records664terrain collision-hold steps versus zero in the empty run. Zero failures
does not establish unchanged readiness. Review detailed branch-trigger activation
by proximity alongside rendering; no collision optimization is implemented here.


## Complete-tree distant capture

The following paragraphs record the original32-view implementation. Current
authored-grove capture and continuity corrections are described in045 below.

The shared native impostor baker must consume every manifest render model at
the common source origin. Loading only the specimen-named wood model drops the
four dense-oak canopy resources. The baker owns a temporary editor scene, forces
LOD0, unions imported model bounds, disables motion on material copies and
captures the existing32 color/normal/depth/occlusion views. Manifest and source
file hashes invalidate stale captures. Original assets and the playable grove
remain unchanged. This extends the existing distant representation pipeline;
it does not yet select that representation for authored prefab instances.
Capturing all source leaves preserves projected crown coverage without changing
the nearby exported meshes. Runtime transitions, wind approximation and shadow
quality still require visual qualification before deployment.

The multipart capture is now implemented and natively exercised on the dense
oak: all128capture images completed, all32color/depth alpha silhouettes match
at the tested threshold, source hashes remain unchanged, and the packed distant
material compiles. Bake-only material copies disable leaf-facing correction as
well as wind so all passes share the same geometry. The packer accepts verified
installed evaluation keys without adding them to the forest catalog. These
assets are not yet connected to the authored100-tree grove.


## Authored tree representation ownership

The following records implementation and qualification history through037.
The038/039 current candidate contract follows at the end of this section.

Authored multipart prefabs need the same distant mesh loader as seeded trees.
Extract that existing loader into TreeDistantModel; source manifests and packed
metadata own its framing/provenance, while one shared model per key is derived
and cached on the engine thread. Re-read metadata on acquisition to invalidate
the cached model when authoring changes. No second distant mesh implementation.
TreeModelLod on each authored tree owns only its visual transition and a distant
SceneObject, deriving transforms from the prefab root. It retains the original
ModelRenderers and all physics objects. Disable/destroy removes its distant object
and restores detailed rendering. A missing/stale distant asset leaves near geometry
visible and reports the problem. Collision proximity work is separate.

Current diagnostic candidate switches by scaled camera distance with12m return/16m exit
and0.35s time-based crossfade, tightened from the initial32m/40m candidate in
response to the user's8FPS observation. The existing seeded population keeps its existing
100m/110m thresholds but consumes the same loader and transition calculation.
Near exported geometry/materials are unchanged. The distant representation uses
the existing depth-reprojected, wind-bent view mesh; shadow behavior must be
checked explicitly. An unconditional hard switch and density reduction were
rejected because they risk popping and the sparse-crown regression.

The runtime loader verifies exact model membership against the export manifest,
including missing, additional and repeated pieces, before accepting shared bounds.
The authored component invalidates its distant object if active renderer membership,
model references or relative transforms change. It then restores detailed rendering;
changing Specimen or re-enabling the component permits a new acquisition. Current
runtime hash checks compare declared export dependency hashes with bake metadata;
they do not independently verify packed textures, materials or shader source bytes.
Offline capture/packing provenance checks and native visual validation therefore
remain required deployment gates. The 031 candidate failed its visual gate and is
not attached to saved prefabs or the saved grove.

Packed distant format3 preserves signed object-space normalXYZ in RGB, with
depth in alpha and occlusion in a separate linear texture. It supersedes the
hemisphere encoding that reversed17.5% of opaque captured normals in the dense
sample. The loader rejects format2 packs rather than decoding them incorrectly;
the disabled old population requires repacking before use. Original old packs
remain preserved. The032 shading comparison still failed despite this correction.
The033 live100-tree diagnostic improved measured standing FPS from24.64 to68.57
without changing near assets, with4 detailed and96 distant trees after the run.
This is an experimental performance result, not appearance or deployment acceptance.

Capture now preserves each source material's leaf-facing feature in every pass.
A compile-time depth feature on the same three production shaders writes the
depth albedo through their existing vertex paths, with shared capture math in
tree_bake_depth.hlsl. Only temporary depth material copies enable this feature;
normal runtime materials compile it out. The separate rest-only depth shader
was removed. All32dense capture silhouettes match between color and depth.
The034player view improves, but the elevated view still fails appearance
continuity; disabling far shadow casting did not remove that mismatch.

The038/039performance work removes branch trigger objects from the active dense
oak prefab and future installer output: current gameplay requires only solid
trunks. Branch proxy authoring metadata remains available for a later feature.
TreeModelLod now bounds near tessellation using existing all-leaf meshlevels at
3m/6m while retaining12m/16m detailed/distant hysteresis. Original explicit
LodOverride values are honored and restored with other renderer state.
The actual viewing camera owns visual LOD: standalone/possessed play uses
Scene.Camera; the existing editor frame adapter supplies the detached viewport
camera, clearing that transient reference outsideejectedplay. Components reject
overrides belonging to another scene. This fixes detailed trees being retained
near the inactive player when viewing the entire grove from the editor camera.
No editor dependency enters runtime code; no player/streaming origin changes.
The shared far model uses a private materialcopy with sourcehalfextents to
trim rasterization to projectedsourcebounds plus32inches eachside; its capture
coordinates, cullingbounds and depth/light reconstruction remain unchanged.
These are active candidates pending fullquality/figureeight/coldstart gates.


For the038/039 candidate, the canonical installer includes TreeModelLod when
installed distant metadata exists for that specimen. Runtime validates membership,
source dependency hashes and bounds, and falls back to the original renderers on
failure. The dense prefab now carries this component; activation no longer depends
on a diagnostic script. Its collision child remains independent of visual LOD.

The component keeps all source leaves within3m of their authored pivots, then
smoothly reduces retained detailed leaves to30% by8m with an additional8-percentage
point scale band. Selection is stable per exported leaf, preserves blade size,
and skips retired vertices before wind work. The active view position is a transient
render attribute, cleared on restore; absent attributes leave the original source
and baker rendering unchanged. Mesh tessellation selection remains3m/6m by root
distance. This is an intentional runtime density reduction in addition to mesh LOD.

One shadow-only copy of the shared baked model retains full captured crown coverage
at all distances. Detailed and visible distant renderers avoid duplicate shadow
submission. Disable, destruction and failed acquisition delete both derived objects
and restore original shadow types, mesh overrides and batching state. Current
qualification concerns normal authored renderers; mixed ShadowsOnly/visible source
pieces have not been qualified.

The current shader blends color, coverage and depth near angular view boundaries
(fractional frame coordinates0.38..0.62); zero-weight views do no sampling work.
The041 flicker candidate filters signed-object normals and AO at the projected
mip and blends them continuously with the same alpha/angular weights, normalizing
once and shading once. This replaces screen-space stochastic selection of one
point-sampled finest-mip normal, which produced fine lighting grain. Shadow
cameras use the nearest capture in orthographic projection; perspective depth
and forward passes use the same blended surface. Restricting this by depth-pass
mode alone caused blue cutouts and was corrected. This supersedes the hard nearest-view candidate,
which failed the orbit visual check. Temporal coverage/lighting quality, cold-start
behavior and canonical performance remain acceptance gates in the ledger.

The bake's historical source hashes are retained: the capture predates the new
view-dependent thinning attribute, which defaults disabled, and the new transparent
pixel early rejection uses the native shader's existing1/255 threshold. Source
FBX, materials and model files are unchanged. Runtime source-hash checking does
not by itself prove shader compatibility; native visual validation remains required.

Fully distant visible objects and shadow proxies share batchable material state.
The private cached material owns TreeLodFade0; only transitioning visible objects
use independent fade attributes and disable batching. The cache layout revision
invalidates materials created before this uniform was initialized. Near source
renderers remain unbatchable because their fade and view attributes are dynamic.

The X candidate also rejects forward pixels occluded by a nearer scene surface
before reconstructing the captured views. The test uses the projected front of
the entire source box, expanded by32inches per axis, and another32scaled-inch
depth tolerance. It reads the engine's public mip0 depth API and leaves depth
and shadow passes unchanged. This is deliberately broader than testing an
individual leaf surface; the tighter reconstructed-surface experiment had no
measured benefit and was removed. X fetched normal and occlusion textures only for the selected angular contribution.
The041 candidate supersedes that sampling policy with filtered continuous lighting.
The existing three depth refinements remain in every projection.

The saved dense grove has100 independent solid trunk ModelColliders and no
branch interaction objects. Each trunk collider uses the exported nine solid
pieces. The manifest retains186 branch authoring records for future work; those
records are not runtime colliders. The installer no longer creates branch trigger
children. Older unused installed specimens acquire this policy on reinstall.

Validation038 retains the full2769x1529 playable viewport and existing figure-eight
workload. Validation039 additionally verifies the actual detached render camera,
all100 active trees and their LOD states at fixed overview poses. Camera captures
can resize the editor viewport, so subsequent measurements explicitly restore and
verify its dimensions. Supplemental rolling FPS observations cannot replace the
canonical movement and standing windows. Current measurements and remaining gates
are recorded in the validation ledger; see candidate X for the frozen final run.

The frozen X worktree passed the038 current-goal benchmark after a fresh visible
editor launch:192.8FPS standing and420.0FPS moving at2769x1529, with zero timed
runtime exceptions or terrain-collision failures. Both039 overview views exceeded
190FPS with all100trees active and all100inside the whole-view frustum. Sampled
close, opposing and angular-transition images retain the nearby authored detail.
This qualifies the current approximately200FPS request on the recorded hardware;
it does not establish a200FPS minimum at every pose, multiplayer performance,
or the earlier stricter empty-world overhead budget.

Lighting040 diagnostic state (2026-09-22): the current worktree temporarily removes
X's manual forward depth rejection. The installed depth resolve copies MSAA
sample0, so a pixel-wide rejection based on that texture does not establish
occlusion for the other samples at a foreground leaf edge. This is a concrete
correctness concern; correspondence with the user-reported cyan fringe still
requires visual confirmation. The sample-safe candidate measured160.6standing
FPS and fails the190FPS gate. A conservative hardware-depth experiment did not
recover the target and was reverted. The current diagnostic is not accepted,
cold-start qualified or published; X's historical benchmark remains historical.
See TREE-LIGHTING-040/v1 for source snapshots, failures and pending checks.

Flicker041 candidate A (2026-09-22) retains040's sample-safe depth behavior and
changes only distant normal/AO sampling. Native before/after frames show reduced
fine lighting grain; fifteen sampled approach/orbit poses retain full crowns and
close leaf/bark detail. The six-frame sequences are sparse samples, not live video
or proof of MSAA-edge stability. Hot canonical performance remains below target
at158.6FPS standing and369.0FPS moving; the original X result is not current.
A scalar inverse-wind experiment had no material gain and was reverted. Final
cold/whole-view results and temporal qualification status belong in the ledger.

The existing editor camera-mode operation also synchronizes the transient tree
camera override immediately when switching Game/GameEjected. Relying only on the
editor frame callback left the overview camera cached after returning to Game;
near trees could remain distant. Passive same-batch readback after the change
matches the player's actual camera distance. The fixed-pose repetition was
interrupted by player movement, so final041C validation remains incomplete.
The041A cold benchmark measured167.3FPS standing and362.6FPS moving; full100-tree
overview363.1FPS. No200FPS standing acceptance or complete temporal qualification
is claimed. Current details and unfinished checks are inTREE-FLICKER-041/v1.


042 shadow specialization candidate: TreeModelLod's existing shadow-only object
selects D_TREE_SHADOW on the shared distant shader. That compiled variant derives
one nearest capture direction from the light, expands the same wind-bent plane,
and outputs captured coverage with native raster depth. Perspective visible/depth
passes retain the reprojected filtered surface. Cached source model/textures and
root transforms remain authoritative inputs; all work remains on the render path,
with no extra allocation, readback, geometry or world state. Existing teardown owns
the object. This trades shadow depth precision for lower measured GPU cost; camera
stability, full crown coverage and near trunk/shadow attachment must pass native
review. Reprojection in every shadow pixel and lowering global shadow resolution
were considered; the former is the measured cost, the latter changes unrelated
scene quality. Candidate status/results are in TREE-PERFORMANCE-042/v1.


042F2 was the retained performance implementation for the authored100-tree grove;
its all-distance flat shadow is superseded by the047 candidate below.
The shadow-only object selects the silhouette variant; tile sampling uses projected
mips with tile-center clamps. Its light-directed captured plane retains root wind
and full crown coverage, with approximate shadow depth. Visible distant objects
keep the filtered041 normal/AO and three-step surface reconstruction. Their raster
depth bounds the padded source box, with conservative per-sample depth output;
forward depth writes are disabled because the matching prepass owns depth. This
uses native sample tests and never reads resolved sample0 for pixel-wide rejection.
Nearby leaf shader, density, geometry, motion, textures and trunk-only collision
remain byte-identical to the pre042 state. The temporary rigid-blade experiment
was reverted. Fresh-editor canonical results:223.9FPS standing/408.9FPS moving;
all100-tree overview415.8-420.2FPS. Recorded FPS, tail, memory, allocation and
correctness gates pass againstX; close/orbit native views retain detail. Two exact
overview/Game handoff cycles also pass. Sparse captures do not prove every live
MSAA edge or dynamic-light condition. See the [042 report](../ValidationEvidence/TreePerformance/perf042-report.md)
and immutable scenarios/results in the validation ledger.


The dense oak collection recipe is owned by Tools/BlenderTrees/oak_variations.json:
the original271828 remains immutable and eleven explicit seeds/settings produce
separate source recipes, Blender collections, exports and prefabs. The visible
build_growth_variants queue calls the existing modal growth/source operators and
export_specimen; it owns no second geometry implementation. Each source publishes
only after success and is saved independently. Cancellation stops subsequent
specimens; completed exports are hash-validated on resume. These authored multipart
assets remain outside the older single-model population catalog. New variants do
not automatically replace the saved100-tree grove or its fixed benchmark workload.

043 authoring correction: broadleaf and needle binding refines a failed native
float BVH candidate against actual rendered-triangle bounds, using the candidate
distance as an upper bound and double-precision point/triangle distances. A fine
twig may emerge through an expanded parent trunk/collar: an anchor inside the
closed union uses the larger of its existing twig limit and the surrounding
surface's authored bark radius. Exterior anchors retain the original twig limit,
so missing wood cannot attach foliage across an empty gap. This is a local-source
sanity check; final anchors still lie on the actual rendered bark. Source metadata
records candidate fallback and embedded-anchor counts. No leaves are discarded.
Generation and in-world results belong in TREE-OAK-DOZEN-043.

043 installation readiness: adding the distant component preserves installed files
whose size and hash match the export manifest; changed files remain atomically
replaced. This prevents a prefab-only reinstall from retriggering every model and
motion-texture import. Native compile Success alone does not establish readiness:
all source materials/models must report compiled/up-to-date before distant capture,
and the packed distant material must be full-compiled and verified before use.
The second variant exposed malformed initial capture data and a stale material;
its complete recapture and in-world recheck passed after this sequence correction.

043 native capture loading correction: asset compilation and resource loading have
separate completion points. The baker awaits Material.LoadAsync for each declared
material and Model.LoadAsync for each render piece before making material copies.
It also requires the bound g_tTreeMotion texture to be loaded and have the authored
motion.texture_size dimensions; it waits up to30s then fails without capturing.
This avoids baking foliage around fallback pivots during a first import. Geometry,
leaf-facing, wind settings, capture views/passes and runtime rendering are unchanged.
First-use visual qualification and recovered failures remain in the043 ledger.

044 growth-light input: Recipe owns overhead_light (defaultfalse for existing
recipes). When enabled, the canonical LightField uses exactly world+Z; otherwise
its established directional field is unchanged. Studio-light transforms are
render settings, not hidden simulation inputs. The overhead oak plan explicitly
saves true per specimen. Validated Recipe equality supplies defaults when reading
older graph/recipe data without mutating stored checksums or exported assets.
The collection runner now accepts an explicit plan/status path instead of a
hard-coded eleven-item limit; it still uses the same modal operators, per-specimen
publication, hash-checked resume and cancellation. No runtime generation or world
population responsibility is added.

043 closed wood cavities: curved branch unions may contain inward-oriented sealed
inner shells. Before junction cleanup, the canonical surface generator fills only
shells with negative signed volume, branch identities present on the outer shell,
and strict containment of every vertex/face center by the oriented closed exterior.
It retains all outer geometry. Positive detached surfaces, ambiguous containment,
open edges and final disconnection remain errors. Counts of filled cavities and
internal faces are retained in the saved Blender source statistics; leaf attachment uses the retained
outer bark. This implements solid wood rather than a hollow interior shell model.

044 reduced wood uses the existing native collapse decimator at unchanged .3/.075
ratios. A failed closed-surface collapse may retry once from immutable source,
protecting the bad junction's nearest source vertices and two face rings with
zero weights. Positive group factor enables native weight input. Remaining bad
edges/vertex fans, disconnected output or budget excess still fail; LOD0 and leaf
counts never change. Native behavior evidence is Blender's
[modifier](https://github.com/blender/blender/blob/main/source/blender/modifiers/intern/MOD_decimate.cc)
and [collapse](https://github.com/blender/blender/blob/main/source/blender/bmesh/tools/bmesh_decimate_collapse.cc)
sources: factor0 disables weights, while zero-weight endpoints are excluded from
collapse. Upstream code informs the guard; actual Blender5.2.2 source/export
measurements qualify the installed behavior in044, not a general version guarantee.


Leaf045 nearby foliage selection: TreeDistantModel reads the existing
render_models role metadata alongside source membership validation and returns
the foliage-only model set to TreeModelLod. Foliage-only pieces use meshLOD2
(four vertices/two triangles per camera-facing cutout oak leaf). Wood and mixed
legacy pieces keep the existing root-distance0/1/2 selection; explicit renderer
LOD overrides still win. Source meshes, leaf identities/counts, atlas UVs, motion
textures, wind/facing shader and material remain unchanged. This avoids spending
nine vertex animations/eight triangles on each closest oak leaf; the user chose
simple flat sprite cards over retaining the physical center fold. The controller caches role membership per initialized tree and
clears it with the existing restore/hotload lifecycle; there is no per-leaf CPU
state. This changes neither canonical world state nor trunk collision.
At544606 leaves the close foliage triangle count is4356848 ->1089212;
leaf vertices4901454 ->2178424. These are asset counts, not an FPS claim.
Existing runtime distance-based leaf retention is unchanged; this is not a claim
that every exported leaf is drawn at every camera distance. Runtime performance
and visual acceptance are recorded under TREE-LEAF-045 in the validation ledger.


## LOD silhouette continuity045

The current authored-tree implementation keeps the existing12m/16m hysteresis,
0.35s complementary fade and shared32-triangle distant mesh. The visible raster
plane now encloses the perspective projection of all eight padded source-box
corners. Directional shadow planes retain orthographic bounds. This prevents
front branches from being clipped before depth reconstruction.

The canonical packer propagates nearest covered surface depth across each entire
capture tile. Coverage is unchanged by padding. Previously depth returned to the
center plane beyond16pixels of dilation: the first reprojection sample could land
outside a foreground branch and never reach it. Albedo/normal/AO padding remains
16pixels, and the shader retains three reconstruction steps. No extra runtime
texture, draw, geometry, allocation or CPU readback is added by depth padding.

Multipart captures use the actual coarsest authoredLOD2 and the foliage shader's
existing minimum-retention path through TreeLeafView. They use16azimuths and the
existing four elevations at1024pixels per tile. This aligns the detailed handoff's
blade shape/density and reduces capture-angle occlusion differences. Legacy
single-model population captures retain8azimuths andLOD0/full retention because
that renderer does not use the authored tree's leaf-retention attribute.
View count is recorded in capture metadata, checked by the loader, bound explicitly
on its material copy and used by the shader and Mesh.UvDensity. Derived cache
version4 invalidates earlier material copies. The active271828dense specimen has
been rebuilt; the original100-tree validation grove and the current single-oak
scene share those resources. Unused installed
specimens retain their previous bakes until explicitly rebuilt through this same
pipeline. Stop/restart Play after rebaking: existing per-instance scene objects
retain material copies and a multi-resource hot reload is not atomic.

Only derived render data changes. Source leaves, branch membership, collider
shapes, procedural placement and network state remain authoritative and unchanged.
Increasing the handoff distance alone would retain expensive detailed meshes for
more trees; disabling depth correction shrank foreground branches, and reducing
its iteration count did not repair the silhouette. Those experiments were rejected.
The16-view atlas doubles this specimen's texture footprint. The matched cold
038/v1 run passes all recorded gates:393.84FPS moving/206.66standing, GPU memory
+6.54%/+5.24%, process memory+2.87%/+2.38%; all frame-tail/allocation changes within10%. Numerical provenance,
failed experiments, native visual/temporal evidence and final acceptance status
are in TREE-LOD-CONTINUITY-045 in the validation ledger.


2026-09-22 test-tree cleanup: the user replaced the100-tree test grove in
basic_example.scene with one fresh oak_growth_18_open_grown_271828_dense prefab
instance named Sprite Leaf Test Oak. It uses the current two-triangle leaf cards,
wind and64-view distant bake.18catalog specimens remain available to the existing
procedural population loader.13unreferenced experimental variants and their
exclusive impostor resources were removed from Assets into a local recovery
archive because automatic approval rejected permanent deletion. Shared tree
shaders, source-generation tools and catalog dependencies remain. The previous
grove scene is retained as compressed validation evidence; its100-tree performance
history is not comparable with the new one-tree baseline (TREE-SINGLE-SPRITE-046).

## Authored tree shadow correction (047 candidate, 2026-09-22)

TreeModelLod now restores each detailed renderer's authored shadow type while
it is visible. The separate shadow object is enabled only while the distant
representation contributes; complementary pixel fades prevent two full shadows
during the existing0.35s handoff. Preserve12/16m hysteresis and the same mesh
LODs, leaf selection, light settings and collision. No additional geometry or
scene objects are created. Original renderer settings are restored on teardown.

TreeLeafView, TreeLeafViewForward and TreeLeafViewUp are transient per-object
attributes owned by TreeModelLod's actual render camera. The leaf vertex shader
uses that camera for facing and distance-dependent flutter in color, depth and
shadow passes. Without it, shadow rendering can orient leaves toward the light
camera. Native bake/non-authored callers without this override retain the active
render camera. These are derived view state, not saved/networked world state.

The distant shadow still uses one nearest capture's coverage and is deliberately
coarser than near geometry. It now samples the existing atlas depth and projects
the reconstructed, wind-bent surface into the shadow map, instead of writing a
flat plane through the crown. No new texture, bake or buffer is required. The
perspective distant renderer is unchanged. Keeping the flat proxy nearby was
rejected after a proxy-off diagnostic exposed the resulting broad dark region;
global light/ambient changes would not correct shadow geometry. Real near shadows
add shadow-pass work; use047's fixed close observation and the unchanged single-
tree figure-eight to measure that cost. See TREE-SHADOW-047 in the validation
ledger for current qualification and retained failed/interrupted runs.

## Animated baked leaf clusters (049 accepted)

The active dense oak now derives smaller camera-facing leaf clusters through
`bake_foliage.py`; see [baked foliage](BakedTreeFoliage.md) for its source/derived
boundary, wind and shared texture ownership. Original FBXs and motion, wood,
colliders, prefab membership and saved world placement remain unchanged. The
native distant baker follows each capture camera for leaf-facing materials and
waits for the new atlas textures before capturing. Runtime acceptance and failed
representations are recorded under TREE-BAKE-049 in the validation ledger.


## Distant trunk visibility050 (accepted)

The reported low-angle view exposes trunk surfaces hidden behind foliage in the
whole-tree orthographic capture. More depth iterations cannot recover surfaces
that were not captured. Independent wood/foliage impostors repaired the holes
but failed forest GPU budgets. A full coarse wood mesh also cost too much at
1024trees. Retain those failures in050. The implementation therefore keeps the
original whole-tree impostor and adds a small opaque trunk mesh. Depth testing
fills disoccluded trunk pixels while retaining the captured crown and branches.

`bake_distant_wood.py` imports the installed wood LOD2 in a temporary scene in
the visible Blender session, preserves source UV/material/wind attributes and
decimates only Trunk to a160-triangle cap (158actual). Roots,branches and fine
wood remain represented by the original impostor. Original meshes,collision,
prefab and saved scene remain intact. The derived FBX,one-LOD model and report
publish only after source,tool and output hashes validate. The native distant
baker is unchanged. The packer validates the trunk provenance and publishes
dependencies before the version4 far manifest. Mixed single-model catalog trees
retain their existing whole capture without this separately authored trunk.

The shared runtime loader verifies source membership and trunk provenance,then
builds one cached model containing the32-triangle view and158-triangle trunk.
It preserves the compiled trunk vertices,indices,material and UV density. No
additional scene objects or per-frame CPU work are required. The shared bark
fade uses a far material flag to complement the detailed geometry in the
existing12/16m hysteresis and0.35s transition. The trunk contributes actual
geometry to color,depth and shadows. The impostor retains three perspective
refinement steps nearby and uses one beyond six source diameters (119.22m
for this oak); the shared algorithm and all capture selection remain intact.
This reduces the measured distant pixel cost;050/v3 checks the boundary.
These are two complementary pieces of one derived LOD,not collision/world truth.
The simplified trunk silhouette is a deliberate distant approximation.

Both authored and seeded trees use the same loader. Continuous exposed trunk,
valid wind/fade/shadows,and the unchanged canonical figure-eight and048 forest
budgets are required before acceptance; see050 in the validation ledger.

050 acceptance uses original and repaired in-world views,all eight azimuths,
12/16m handoff,unchanged048 forest GPU scopes and038/v2 single-tree timings.
The fresh original control reproduces the historical frame-rate loss; the
tested repair passes the current control. Hot-reload memory/timing failures,
one startup stall and the successful identical-source cold retry remain in
the ledger. Do not claim a37%memory improvement from compilation-history
differences. Final cold memory recovers to about10.7GiB in the settled scene.
This repair is qualified for the active multipart dense oak; the18legacy
catalog specimens retain their original representation and were not rebaked.
