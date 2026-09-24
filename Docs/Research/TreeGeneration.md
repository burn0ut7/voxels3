# Tree generation, materials and rendering

Initial research: 2026-09-18. Visual-production reset: 2026-09-19. The user
rejected the spawn-tree prototype's bark, trunks and leaves and requested deep
research before further iteration. This note records proposed direction, not
an accepted renderer or visual result. The current implementation is documented
in [SpawnTrees](../Architecture/SpawnTrees.md); its visual and performance goals
remain open. Biomes and persistent world objects remain outside this slice.

## September 19 decision

Build a small, curated library from convincing source assets, with procedural
variation and placement. Put the next effort into one excellent oak, then one
pine and one ash, before multiplying variants. The previous suggestion of
16-32 variants per species was an experiment, not an art target. Twenty-four
weak models do not solve the visual problem.

Treat the tree as a production pipeline: reference and material acquisition,
branch structure, leaf/twig construction, baking, lighting, wind, and multiple
representations of the same asset. Our recommendation is a conventional hybrid:
real wood geometry, carefully shaped leaf/twig meshes nearby, baked branch
clusters farther away, and a lit, multi-view whole-tree impostor in the distance.
An impostor is a small mesh showing views captured from the actual tree.

This keeps the original library/reuse decision, but replaces the assumption
that a tiny procedural drawing routine is sufficient to create finished art.
Authoring-time generation can still supply most of the shape variation. Every
variant need not be manually modeled, and every placed tree need not own unique
geometry. Population ownership and deterministic placement are separate from
asset quality.

## What documented high-quality productions actually do

These are selected primary production sources, not a ranking of every game's
trees. Game versions, hardware and rendering architectures differ. No published
number below is a Voxels3 budget or a prediction of s&box frame time. PDF page
references are physical pages starting at one.

| Production / source | Documented practice | Lesson and transfer limit |
| --- | --- | --- |
| [Horizon Zero Dawn, Guerrilla, GDC 2018](https://media.gdcvault.com/gdc2018/presentations/gilbert_sanders_between_tech_and.pdf#page=69) | Pages 69-76 show rough construction, high-detail work, UV-space baking, component LODs and assembly. Page 77 lists approximately 10,000 / 2,600 / 1,200 / 200+12 / 12 triangles, ending in a billboard. Pages 47-48 include normal, translucency and AO inputs. Page 83 separates visual and shadow meshes. | A carefully made source tree can support aggressive LODs. Preserve that asset's appearance through the chain. These historical asset counts and proprietary shadow settings are reference points, not current universal limits. |
| [Star Wars Battlefront, DICE, GDC 2016](https://media.gdcvault.com/gdc2016/Presentations/Brown_Kenneth_Hamilton_Andrew_PhotogrammetryStarWars.pdf#page=104) | Pages 104-106 describe hand-built vegetation based on location photographs: scanning foliage was difficult. They used distance-specific textures, controlled mipmaps, and broadly modeled leaves/branches for normals. A scanned tree base blends into tiling bark upward. | Photo-derived materials still need geometry and processing. Scanning an entire tree is not a prerequisite. This is the 2015 game's workflow, not evidence about every later Frostbite release. |
| [Horizon Forbidden West, Guerrilla, 2022](https://www.guerrilla-games.com/read/adventures-with-deferred-texturing-in-horizon-forbidden-west) | The developer describes visibility-buffer preprocessing, tiled compute shading and variable-rate shading to accelerate foliage and alpha-tested geometry on PS4/PS5. | Alpha-tested vegetation remains relevant in a visually ambitious shipped game. The overview does not establish its tree counts, exact leaf meshes or a portable s&box implementation. |
| [Alan Wake 2, Remedy, GDC 2024](https://presentations.remedy.fi/large-scale-gpu-based-skinning-for-vegetation-in-alan.ppsx) | Slides 27-29 describe instancing large non-interactive trees within the skinning pipeline. Slides 31/35 use GPU procedural skeletal animation. Slide 43 describes early skeleton-LOD work that reduced distant bone counts but had authoring/popping difficulties; its final shipped status is not established here. Slide 48 derives bounds from bones. | Detailed forests still reuse work; animation simplification needs visual review. Northlight's particular use of "variants" is not a recommended count of visually distinct tree shapes for our library. Its GPU skinning, compression and ray-tracing integration are engine-specific. |
| [Fortnite Chapter 4, Epic, 2023](https://www.unrealengine.com/tech-blog/bringing-nanite-to-fortnite-battle-royale-in-chapter-4) | For their Nanite content, opaque geometric foliage usually outperformed masked cards. Simplification needed canopy-area preservation. Wind was baked from branch hierarchy into textures; distant wind evaluation could stop. | More triangles can beat excessive alpha work in that renderer. This does not prove that replacing our cards with hundreds of thousands of triangles will be faster. Their reported 300-500k versus 10-20k figures are **vertices**, not triangle budgets. |
| [Fortnite shadow rendering, Epic, 2023](https://www.unrealengine.com/tech-blog/virtual-shadow-maps-in-fortnite-battle-royale-chapter-4) | Trees use simplified shadow proxies. Epic warns that excessive simplification changes light penetrating the canopy; geometry, masked pixels and animation all affect shadow cost. | Shadow quality and cost need their own LOD policy. Removing shadows at one arbitrary radius is not equivalent to preserving a believable forest cheaply. Virtual Shadow Maps are engine-specific. |
| [Fortnite without Nanite, Epic's Impostor Baker documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/impostor-baker-plugin-in-unreal-engine) | Epic explicitly documents upper-hemisphere impostors for these trees. Its implementation blends three nearby captured views; it can bake normal and depth information. A full-sphere mode covers additional directions. | Far trees can use sprites while retaining more appearance than a generic canopy blob. View blending, texture memory, pixel coverage and transitions still cost something. Hemisphere coverage must match our actual camera positions. |
| [Nanite Foliage, Epic documentation retrieved 2026-09-19](https://dev.epicgames.com/documentation/unreal-engine/nanite-foliage) | The page served as UE 5.8 documentation labels this **Experimental** and shows The Witcher 4 **technical demo**. It combines reusable branch assemblies, aggregate voxels for tiny disconnected details, and skeletal wind with better bounds. | This is a frontier reference, not evidence of a shipped Witcher 4 implementation. Reusing parts remains central even at extreme detail. s&box's SDF terrain does not provide this foliage renderer; adopting it would be an engine project. |

Two durable shading references add detail. [Crytek's Crysis chapter, 2007](https://developer.nvidia.com/gpugems/gpugems3/part-iii-rendering/chapter-16-vegetation-procedural-animation-and-shading-crysis)
describes separate whole-plant bending and leaf motion, with authored control
data, two-sided leaf shading, thickness-related transmission and occlusion.
These are useful principles; its hardware and shader timings are historical.

[NVIDIA's SpeedTree rendering chapter, 2007](https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-4-next-generation-speedtree-rendering)
distinguishes bark surface detail from silhouette detail, and shows why normal
maps, leaf self-shadowing, backlighting and antialiasing matter on cards. Its
examples include broad leaf folds in normals rather than only tiny vein detail.
This is a specific demonstration extending then-current SpeedTree, not a claim
about today's default SpeedTree renderer. Its silhouette-fin technique is not
selected for Voxels3.

## Why the rejected prototype missed the target

The user rejection is the visual result. Source inspection at the research reset
identified the following omissions; their individual contribution was not isolated
by controlled image comparisons. This table describes the rejected pre-reset
prototype. Subsequent oak candidates replace some of these paths; see the
[current implementation status](../Architecture/SpawnTrees.md#crown-volume-oak-replacement).
The source remains uncommitted, with concurrent unrelated work in the checkout.

| Current evidence | Implication for the redesign |
| --- | --- |
| [Texture baker](../../Tools/bake_tree_textures.py) draws 512-pixel sprays and cloud silhouettes using polygons/lines; bark uses procedural grooves/noise at 256x512. Bark normals are derived from the color image's brightness. | Higher resolution alone will not create plausible folds, wood relief or connected twigs. Replace the art inputs and baking method, not just the palette. Brightness is not a measured height field. |
| The generated foliage materials have opacity but no authored leaf normal or transmission texture; all tree materials share uniform roughness at 235/255. | Leaves lack authored surface orientation and differentiated response to light. A transmission feature flag cannot supply the missing leaf structure. Bark and leaves need material-specific roughness. |
| [Geometry generation](../../Code/Voxels/Trees/ProceduralTreeGeometry.cs) places four-vertex rectangles around crown volumes; the normals are biased from the crown shape. | Larger or more numerous rectangles still reveal sheets and arbitrary clumps. Controlled canopy normals can help broad lighting, but do not replace the leaf's own folded surface normals. |
| Wood is independent straight tube segments, with eight radial sides at the nearest level, simple root tubes and no branch collars. Its lengthwise texture coordinate uses local Z even on branches. | Coarse contours, joins and poorly aligned bark remain visible nearby. A better bark bitmap cannot repair branch attachment, grain direction or trunk shape. |
| Distant crowns use separately drawn canopy textures and crossed cards. LODs change at 28/75/180 m with hysteresis; there is no visual crossfade. | Hysteresis prevents rapid toggling but does not hide the swap. The far art does not encode the rendered near tree's actual branch/leaf appearance. Shape and density can change abruptly. |
| Leaf-card motion weights are uniform across each card; the existing inspection did not establish convincing wind or measured forest cost. | Movement needs anchored, coherent control data and observation in motion. A material compiling or a low triangle count is insufficient acceptance evidence. |

The shared library, bounded placement work and deterministic identities are
useful architectural foundations. They do not validate these asset choices.
The earlier independent review's acceptance of an initial stylized slice is
superseded by the user's higher visual requirement.

## Proposed production approach for s&box

This section is our engineering and art proposal. It has not been implemented
or performance-qualified. The first proof should improve one tree through the
real spawn population, not introduce another renderer or a special demo scene.

### 1. Approve source materials and a branch before a forest

Acquire or create coherent source assets with known physical scale: bark,
individual leaves, twigs, and a small set of branched sprays. Prefer properly
processed photographs/scans or high-detail modeled sources. Record origin,
license, scale, map conventions and the processing recipe beside the assets.
No purchase or new third-party asset dependency is selected by this research.

Base color should be prepared for the game's changing light, rather than
containing a strong photographed highlight or directional shadow. Produce
aligned opacity, normal, roughness and transmission inputs from the same source.
Use occlusion where the engine's material contract supports it. Keep material
properties distinct: an opacity mask says where the leaf exists; a transmission
map controls light passing through the existing surface. One cannot replace
the other, regardless of a shader property's historical name.

Do not rely on independently generated color/normal/height images to agree
automatically. Inspect alignment, normal handedness, scale, seams and mip levels
in the actual material. A plausible photograph, including an AI-generated one,
is not by itself a calibrated material set or a three-dimensional branch.

### 2. Make convincing wood and species structure

Use a continuous curved branch sweep with a stable frame along its length,
taper and deliberate irregularity. Add geometry where curvature, silhouette,
root flare or branch junctions need it. Give major forks believable transitions
and bark coordinates that follow branch length, not vertical world direction.
Avoid obvious repeated knots, abrupt UV scale changes and bark grain crossing
a fork unnaturally. More segments everywhere would spend geometry without
necessarily fixing those defects.

Treat the close trunk base as a distinct detail problem. An authored/scanned
base blended into repeatable upper bark is a candidate; a fully procedural
trunk must meet the same contour and material quality. Start with normal-mapped
surface relief plus actual silhouette geometry. Evaluate parallax only after
mapping and source quality are correct, and only where its measured benefit
justifies pixel cost. It is not a replacement for root shape or deep forks.

Choose specific real reference species before final art. The prototype labels
"pine", "oak" and "ash" cover many different forms. Our intended art profiles
are a conifer with needle-bearing branchlets and an irregular tapered crown;
a broad oak with strong forks; and a lighter, more open ash with compound leaf
sprays. These are direction-setting profiles, not a botanical claim about every
member of those groups. Preserve age, growth direction and branch hierarchy;
randomize within a profile rather than distributing leaves uniformly in a ball.

### 3. Give nearby foliage depth, then bake it down

Build leaf/twig source pieces with real attachment and physical scale. Compare
two small representations of the same branch: trimmed, bent spray meshes and
individual cutout leaf meshes. Fit geometry around occupied texture regions
instead of leaving large empty rectangles. Give visible leaf folds some actual
depth. Retain enough twig structure that foliage is supported when viewed from
below and from oblique angles.

Measure both candidates in s&box before choosing. Additional vertices can
reduce empty alpha coverage, but tiny triangles and more complex materials can
also be expensive. An entirely opaque leaf mesh is an experiment for selected
near detail, not an automatic consequence of Epic's Nanite results.

Bake normal information from the source branch/leaf shape into the simplified
sprays. Keep both scales of lighting: the broad canopy's volume and the local
fold of a leaf. Review front, back, side and underside illumination; avoid
turning all normals upward merely to make a dark canopy bright. Use restrained
color and roughness differences, not a single saturated green or random tint
large enough to change species identity.

At intermediate distances, replace fine geometry with larger baked branch
groups derived from the same structure. Preserve silhouette, gaps, average
coverage and lighting. Inspect opacity mip behavior and edge padding; lower
detail must not make the crown disappear or become a solid green mass.

### 4. Supply the data the native shaders expect

[Facepunch's foliage update](https://sbox.game/news/update-26-01-28) documents
transmission, normal controls, distance alpha treatment, trunk sway and leaf
flutter. Installed build 26.09.15 includes `core/shaders/foliage.shader`,
`bark.shader` and shared `common/trunk_bending.hlsl`. Source inspection confirms
separate opacity and transmissive-color inputs, vertex-color motion controls,
and an alpha depth pass followed by depth-equal shading. Start with these native
facilities and correct asset inputs. Inspecting shader source establishes its
contract, not this project's rendered correctness or performance.

The installed foliage shader's grazing fade has an MSAA coverage path and a
hard clip fallback without MSAA. Enabling it blindly is not a general cure for
edge-on cards. Verify the active antialiasing mode and inspect moving edges.
Similarly, a depth prepass can reduce repeated expensive surface shading;
alpha tests, geometry, shadow passes and memory traffic remain.

Author motion with a stable base and restrained leaf flutter, keeping leaf
attachment points with their twig. The verified native path supplies trunk
bending and vertex-color-driven detail displacement; it does not consume a
branch-parent/pivot hierarchy. Coherent hierarchical branch bending is a desired
quality option, not a verified native feature. First prove anchored flutter and
shared trunk motion. Escalate to another animation method only if that result
has a concrete visual deficiency. Reduce motion complexity as it becomes
subpixel; maintain bounds that enclose animation. Whole-tree spring simulation,
collision-driven bending and a custom wind compute system remain deferred.

### 5. Make distance representations of the approved tree

| Visual scale | Proposed representation | Appearance that must survive |
| --- | --- | --- |
| Close inspection | Detailed wood and shaped leaf/twig meshes; full native material inputs and local motion | Bark scale, folds, attachment, branch contours, believable backlighting |
| Ordinary nearby tree | Reduced branch geometry and baked sprays | Crown shape, major forks, depth and species identity |
| Middle distance | Coarse wood and larger baked clusters; simpler motion/material work where supported | Canopy coverage, interior darkness, silhouette and gaps |
| Far distance | Baked whole-tree impostor with multiple views and lighting information; detail motion disabled | Position, outline, color and lighting continuity |

Choose transitions using projected size as well as distance: tree size, FOV
and resolution affect visible error. The prototype's distances are provisional,
not a visual law. Keep a limited transition band; investigate compatible
dithering/crossfading and measure the temporary duplicate work. Do not assume
native materials expose a ready-made crossfade interface.

Baking/capture, view selection and blending, dynamic lighting, and shadow-pass
behavior require an explicit s&box feasibility gate. The installed foliage
shader is not a multi-view impostor renderer, and Epic's baker is not available
as an s&box integration. Prove this path with one approved tree before expanding
the library. If it cannot meet the quality/cost target, evaluate matched fixed
view billboards as the simpler fallback and record their viewing limitations.

Bake impostors from each approved variant with consistent pivot, scale and
materials. Select view coverage for hills and elevated cameras; upper-hemisphere
views alone may fail when the viewer is below a hillside tree. Start by proving
correct color, normals, coverage and view changes. Add depth/parallax only if
its observed improvement outweighs additional samples and artifacts. Atlas view
count and resolution trade quality for memory; use measured bytes, not the
number of billboard triangles, as the residency budget.

Visual LOD, shadows and wind are related but separate decisions. A simplified
shadow caster may preserve forest depth while cutting work; its support in the
current s&box path must be verified. Keep correct nearby root contact and canopy
self-shadowing. Inspect shadow boundaries while walking, not just a bright
isolated tree still. Far sprites reduce geometry substantially, but a screen
full of overlapping textured sprites is not free.

## What to retain, replace and defer

| Decision | Rationale |
| --- | --- |
| Retain bounded shared variants and deterministic spawn placement | Reuse and visual quality are compatible; placement does not need unique mesh ownership. Native model/material batching is a starting point, with actual batching still to verify. |
| Replace the current bark/foliage source art and near geometry recipe | These are concrete weaknesses identified in the prototype, not evidence that procedural trees inherently look poor. |
| Promote baked whole-tree impostors into the planned visual pipeline | The user explicitly wants very cheap distant trees. The source asset must be approved before its impostor can preserve the right appearance. |
| Generate and curate variants before shipping, unless a measured reason favors runtime construction | This moves quality inspection and asset baking out of gameplay. It does not require hand-authoring every tree. |
| Defer unique geometry for every placement | It does not fix the current materials, leaf shape or shading and adds retained geometry/upload work. |
| Defer virtualized foliage, per-branch runtime assembly and complex skeletal wind | These are substantial engine-dependent systems. They are not prerequisites for the next convincing tree. |

## Evidence required for the next implementation

First establish one approved oak through the shipping spawn-tree path, with
matched close, full-tree, underside, backlit and oblique views plus a wind clip.
Keep exposure, light, FOV and image size fixed when comparing revisions. Include
normal gameplay framing as well as deliberately revealing close inspections.
Review bark grain/scale, branch joins, card visibility, leaf support, canopy
depth and motion. An independent reviewer must use the user's high-visual bar;
being recognizable as a tree or better than the prototype is not sufficient.

Then prove the same tree through each LOD transition in both travel directions
and from elevated viewpoints. Inspect silhouette changes, crown density, trunk
alignment, shadow changes, flicker and ghosting in motion. Add pine and ash,
then variant and neighborhood diversity. A single flattering screenshot is
not approval of a species, an LOD chain or the forest.

Before any run, record exact scenario parameters and measurable gates in
[ValidationResults](../ValidationResults.md). Preserve the canonical figure-eight
and its baseline rules. The previously proposed replacement saved-world
performance workload is still unapproved; this research neither substitutes it
nor changes acceptance thresholds. Compare matched tree-disabled/enabled runs,
then measure close canopy, looking upward, distant forest and transition costs
outside that canonical route as supplementary cases.

Capture CPU/GPU frame time and tails, allocations, resident/peak geometry and
texture memory, construction/upload spikes, shadows, batching, streaming and
retirement correctness. Triangle count and a source-code review cannot replace
these measurements. If the budget fails, identify whether shading, alpha
coverage, shadow work, geometry, residency or integration is responsible before
lowering detail. Record unavailable measurements rather than guessing them.

This research performed no new in-world benchmark and makes no performance
acceptance claim. It deliberately leaves runtime code and current assets alone
while the production approach is reconsidered.

Independent review accepted this research as guidance for a staged redesign,
after identifying the native-wind and impostor-integration boundaries clarified
above. This is research-readiness review, not acceptance of the existing trees,
their performance, or the overall tree-generation goal.

Research access limits: the Guerrilla PDF was read through the GDC Vault mirror
after its older S3 URL failed. Both Guerrilla and DICE slide text and selected
asset/LOD figures were inspected. Remedy's vegetation deck was read as slide
XML/notes obtained through bounded HTTP ranges; embedded videos were not
reviewed. Its separate environment-art deck had little vegetation-specific text
and is not used to infer a leaf-production workflow. Some SpeedTree 8/9 pages
returned access errors, so their search snippets do not establish current
middleware behavior here. Downloaded reference material remains local research
scratch data, not game assets or redistributed repository content.

## Initial generation/reuse analysis (September 18)

The following analysis established the library-versus-unique tradeoff at source
checkout `3a3993404dc78863ff11cfd407ded352e6a9a184`. Its original implementation
absence finding was accurate at that time; the September 19 prototype now
exists. Proposed variant counts and the simple branch generator below are
superseded as the immediate visual-production plan by the reset above.

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
The September 18 searches found no tree-generation implementation in Code or
Assets. A spawn-tree prototype was added on September 19; its current owner is
[SpawnTrees](../Architecture/SpawnTrees.md). Grass remains separate derived rendering.
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

## Growth-form variation, September 19 follow-up

The user finds the population uniformly small with too few substantial limbs.
The source recipe's eight oak variants all used an approximately 11.5m height
and the same three leaders/nine lateral scaffolds. Small scale/yaw changes do
not cover the real range of juvenile, mature and broad old trees.

[Woodland Trust's English oak account](https://www.woodlandtrust.org.uk/trees-woods-and-wildlife/british-trees/a-z-of-british-trees/english-oak/)
describes 20-40m mature trees with broad crowns and sturdy branches, and notes
that old oaks can shorten. Its gallery includes summer and bare winter trees.
[The Wildlife Trusts' account](https://www.wildlifetrusts.org/wildlife-explorer/trees-and-shrubs/english-oak)
also describes a spreading crown and thick branches. Adopt broad lower scaffold
limbs and substantial trunks for open-grown old oaks, alongside taller, narrower
forms. Age is not a uniform scale factor, nor does oldest always mean tallest.
These accounts are proportion/identity references, not a procedural growth
algorithm. Their photographs are reference-only and are not imported as assets.

[Glen Eira's assessed English oak](https://www.gleneira.vic.gov.au/our-city/classified-trees/classified-tree-register/184ctr2021-springthorpe-gardens)
adds a measured example: approximately 60 years old, 14m high, 19m canopy width
and 83cm trunk diameter at breast height. Its crown is wider than its height.
Use that as evidence for a broad, lower mature form alongside tall trees, not
as a universal species proportion. Source photographs were located in search;
direct photo fetches failed, so no pixel-level botanical photo comparison is claimed.

[Ash](https://www.woodlandtrust.org.uk/trees-woods-and-wildlife/british-trees/a-z-of-british-trees/ash/)
can reach 35m and has an airy domed crown; [Scots pine](https://www.woodlandtrust.org.uk/trees-woods-and-wildlife/british-trees/a-z-of-british-trees/scots-pine/)
can reach 35m and has blue-green paired needles. These establish wider stature
targets for subsequent species variation, not a claim that the current generic
conifer recipe is a botanically exact Scots pine.

First implement eight distinct oak forms: small/young, intermediate, tall
woodland, broad mature, and two large spreading forms. Vary trunk height/girth,
leader count, lower-branch reach, crown asymmetry and branch density together.
Increase branching rather than leaf dimensions to fill larger crowns. Keep
placement identities, shared variant count and LOD thresholds unchanged. Baked
distant assets must wait for the source forms to stabilize; avoid baking the
superseded small shape library. Actual-world review and measured costs remain
required before accepting these forms.

### Close branch junction follow-up

[SpeedTree Branch Generator: Welding and Blend](https://docs8.speedtree.com/modeler/doku.php?id=branch_generator)
separates geometric attachment to the parent, spread of the weld, texture blending
and lighting normals. It also documents parent-surface ray casts and failed welds.
This supports addressing geometry and UV continuity together after shading-only
collars failed our review. Adopt parent-surface projection for large oak branch
bases; retain failed projections as known unresolved intersections. Product
controls are conceptual evidence, not a claim of equivalent proprietary geometry.

### Pine close-view rejection

The user's20:22 close screenshot and independent review reject the repeated flat
needle combs and unclear woody-twig hierarchy. A complete crown silhouette alone
does not qualify close foliage. The source Pine Tree01 atlas includes individual
needles as well as complete sprays, allowing a shared near geometric-needle
candidate without inventing new scan provenance. Its much larger mesh and lower
coverage are known costs; it is not accepted simply because individual needles
are geometric.

[Oregon State's Scots pine account](https://landscapeplants.oregonstate.edu/plants/pinus-sylvestris)
describes paired2.5-8cm stiff, twisted blue-green needles and a spreading mature
crown. [Its maritime pine account](https://landscapeplants.oregonstate.edu/plants/pinus-pinaster)
describes paired10-20cm needles clustered at branch ends. These are distinct
references; the generic source texture is not established as either species.
Use the latter length range for the current long-needle visual candidate, without
claiming botanical identification. [Austrian pine](https://landscapeplants.oregonstate.edu/plants/pinus-nigra)
provides another distinction: paired8-12cm needles,1-2mm wide, with dense young
pyramidal form becoming broader with age. Increasing needle width to hide missing
shoots would conflict with this fine-scale reference and must not be sold as realism.

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


## Shared growth authoring, September 21

Implementation and ownership have moved to [Shared tree growth](../Architecture/TreeGrowth.md).
A deterministic seasonal graph replaces the prescribed adult scaffold and recursive
crown generator. Oak, ash, birch and spruce use shared rules with data profiles.
This is an authoring foundation; biological calibration, broader species/seed
coverage and game qualification remain open. The existing primary-limb motion
payload is retained as an adapter boundary, not evidence of full hierarchical wind.
