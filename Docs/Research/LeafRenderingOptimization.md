# Keeping full canopies affordable

The initial audit below predates same-day tasks 046 and 047. For the current
single-oak scene, two-triangle near leaves, changed shadows and large-world
recommendations, see [the later scaling audit](#large-world-scaling-follow-up).
Earlier measurements remain historical evidence for their recorded source state.

Research date: 2026-09-22. This is a source audit and recommendation, not an
implemented optimization or a new benchmark. The request is to retain the amount
of leaves while reducing rendering cost. Preserve the authored trees and their
nearby leaf population; evaluate cheaper representations by visible canopy
coverage, silhouette, depth, lighting and motion. Preserving every independent
geometric leaf at every distance is a stricter requirement than preserving its
appearance, and limits the available savings.

The recommended direction is **full nearby leaves, cheaper leaf geometry and
motion where indistinguishable, coverage-preserving branch groups in the middle
distance, and the existing whole-tree impostors farther away**. Keep the existing
cheap shadow representation. Do not solve this request by reducing the source
leaf density or blindly extending current runtime thinning.

## What this project actually renders

The current working tree contains substantial earlier uncommitted work. Git HEAD
was `d6e26108`; that commit alone does not identify the inspected implementation.
Native editor status reported s&box 26.09.15, project `voxels3`, scene
`basic_example`, successful compilation and **Edit mode**. No play session,
camera movement, shader modification or runtime benchmark was performed here.

### Dense authored grove, not the older spawn population

[The saved scene](../../Assets/scenes/basic_example.scene) references the dense
`oak_growth_18_open_grown_271828_dense` prefab 100 times. Its
[manifest](../../Assets/models/tree_lab/oak_growth_18_open_grown_271828_dense/manifest.json)
reports **544,606 leaves retained in every mesh LOD**:

| Asset level | Total vertices | Total triangles | Leaf triangles, calculated from exporter topology |
| --- | ---: | ---: | ---: |
| LOD0 | 5,375,763 | 5,300,996 | 4,356,848: eight per leaf |
| LOD1 | 2,865,870 | 2,461,668 | 2,178,424: four per leaf |
| LOD2 | 2,214,507 | 1,160,022 | 1,089,212: two per leaf |

These are exported asset counts, not a measured number of visible triangles or
GPU invocations. Leaves account for approximately 82%, 88% and 94% of those
triangle totals respectively. Merely simplifying branches cannot remove most of
this geometry. New installed seeds 271829 through 271839 range from 332,955 to
697,411 leaves; they are not the 100 prefab references in this saved scene.

[TreeModelLod.cs](../../Code/Voxels/Trees/TreeModelLod.cs) and the dense prefab
currently specify:

- Mesh LOD0 below 3 m, LOD1 below 6 m, then LOD2, unless explicitly overridden.
- Whole-tree distant representation beyond 16 m, detailed return below 12 m;
  distances are measured from tree origin and adjusted for instance scale.
- A 0.35-second time transition. Detailed meshes and far representations can
  both render during that interval.
- Detailed renderers have shadows disabled. A separate shared-model shadow
  object uses `D_TREE_SHADOW=1` at all distances, sampling captured crown alpha
  with an approximate plane rather than rerendering individual leaf geometry.
- Settled distant objects and shadow objects permit batching; detailed objects
  do not, because their render attributes are set per object.

The separate [SpawnTreePopulation](../../Code/Voxels/Trees/SpawnTreePopulation.cs)
uses the older catalog, 100/110 m hysteresis and its own 65 m shadow range.
Those values do **not** describe the authored dense grove. The older
[SpawnTrees architecture note](../Architecture/SpawnTrees.md) is useful for that
population, but is not a complete description of the current authored grove.

An additional distinction matters when reproducing a slowdown: TreeModelLod's
`OnPreRender` returns when `Game.IsPlaying` is false. The custom 12/16 m
representation policy therefore does not run in ordinary Edit mode. Native
model LOD may still operate. A slow editor overview is not evidence that the
playable-world impostor path is failing; compare mode, camera and resolution.

### Export retention is not runtime visibility

The current [leaf shader](../../Assets/shaders/trees/tree_lab_foliage.shader),
lines 91-104, changes a retention parameter from 1.0 to 0.30 between 3 and 8 m
from each leaf pivot when `TreeLeafView` is active. Stable leaf identities
determine which blades shrink or disappear, with a transition band. This does
not imply exactly 30% visible coverage or exactly 30% surviving leaves.

Thus, the asset retains every leaf but the authored gameplay path already thins
their rendering. The earlier statement that all exported LODs preserve leaves
must not be confused with preservation of the visible canopy. A new optimization
must measure its fullness against a clearly identified full-leaf reference.
Turning that thinning off would itself change the performance baseline.

The same shader executes attachment lookup, normal decoding, wind, branch/root
deformation and camera-facing frame work per submitted vertex. In the facing
path, both the blade frame and its attachment are deformed. A vertex-stage early
return saves subsequent work for removed leaves but does not avoid all vertex
input and initial lookup work. This is not equivalent to removing those leaves
before drawing. Exact compiler reuse and actual shader costs need a GPU capture.

The shader is two-sided, has an alpha-tested depth path and uses depth equality
without depth writes in the forward path. It is not ordinary blended transparent
foliage. Its flutter amplitude fades over approximately 40-100 m, largely beyond
the authored grove's detailed range. An amplitude fade alone is not proof that
the GPU skips the corresponding arithmetic.

## What existing measurements establish

The authoritative history remains in [ValidationResults](../ValidationResults.md),
TREE-PERFORMANCE-038 and 042. These are previous observations, not new results.
Hardware was an RTX 5090 and Ryzen 9800X3D; physical resolution was 2769x1529.

In 042's fixed-pose attribution, before its final shadow optimization, four trees
were detailed and 96 distant:

| Diagnostic state | Whole-scene GPU time | Depth prepass | First shadow cascade |
| --- | ---: | ---: | ---: |
| All 100 trees | 5.99 ms | 2.017 ms | 0.864 ms |
| Only four detailed trees | 5.17 ms | 1.636 ms | 0.645 ms |
| No trees | 3.42 ms | 0.924 ms | approximately 0 ms |
| All restored | 5.98 ms | 1.980 ms | 0.861 ms |

[Raw attribution](../ValidationEvidence/TreePerformance/perf042-ablation.json)
records profiling overhead, camera, groups and restored state. Removing trees
also exposed more terrain: terrain draw time increased. Differences therefore
are not isolated additive leaf costs. They do show why a few detailed trees
deserve investigation, not just distant tree count.

The retained 042F2 cheap-shadow candidate subsequently recorded **223.91 FPS
standing / 4.168 ms GPU**, and 408.86 FPS moving in the cold canonical run.
[The matched comparison](../ValidationEvidence/TreePerformance/perf042-f2-cold-comparison.json)
shows standing GPU time down from 4.911 ms for accepted X, approximately 15.1%.
This qualifies the recorded workload, not every camera, current additional tree
variant, all-leaves-visible rendering or a lower-end GPU.

Avoid repeating experiments already resolved in this ledger: adding near-leaf
`earlydepthstencil` produced no material gain; a rigid-facing motion shortcut
produced only a small gain and was reverted; unsafe resolved-depth clipping
caused holes. The approximate shadow plane was retained with documented visual
limits. The source audit supplies new questions, not proof these earlier results
are wrong.

## How other games approach the problem

Sources below are developer presentations, official engine/middleware docs or
research. Their counts and timings do not transfer directly to s&box.

| Source | Documented approach | What transfers here |
| --- | --- | --- |
| [Horizon Zero Dawn, Guerrilla, GDC 2018](https://media.gdcvault.com/gdc2018/presentations/gilbert_sanders_between_tech_and.pdf#page=69), physical pages 69-83 | Bake detailed vegetation into components, construct component LOD chains, assemble trees, use progressively simpler shaders and eventually billboards. Page 77's example ranges from approximately 10,000 triangles to 12. Page 83 separates visual and shadow assets. | Derive cheaper representations from the same tree. The old PS4 example is an illustration, not a triangle budget for our game. The cached original PDF was text-inspected because the web fetch exceeded its size limit. |
| [Crysis, Crytek / GPU Gems 3](https://developer.nvidia.com/gpugems/gpugems3/part-iii-rendering/chapter-16-vegetation-procedural-animation-and-shading-crysis) | Distinguishes whole-plant motion from leaf detail; some foliage planes depict several leaves. Uses authored motion control and precomputed occlusion. | Preserve several visual leaves in a lower-geometry branch representation; retain coherent motion. Historical shader arithmetic and hardware results are not current performance predictions. |
| [Fortnite Chapter 4, Epic](https://www.unrealengine.com/tech-blog/bringing-nanite-to-fortnite-battle-royale-in-chapter-4) | Its Nanite trees often favored opaque geometry over masked cards; canopy-area preservation counteracts simplification loss. Wind uses baked simulation data and distance controls. | Optimize the complete geometry/pixel/animation tradeoff. Opaque millions-of-triangles meshes are not automatically faster in our conventional renderer. Their 300-500k figure is vertices. |
| [Fortnite shadow rendering, Epic](https://www.unrealengine.com/tech-blog/virtual-shadow-maps-in-fortnite-battle-royale-chapter-4) | Separates tree shadow proxies; cautions that over-simplification damages canopy lighting. The authors also discuss why Nanite later reduced the need for those proxies. | Shadow fidelity and cost need their own measurement. Our cheap proxy already implements this broad idea; it is not an untouched opportunity. |
| [SpeedTree SDK wind performance](https://docs8.speedtree.com/sdk/doku.php?id=legacy_pages%3Aperformance) | Provides full, branch, global and no-wind levels and smooth transitions between them. | Spend complex leaf motion only where it remains visible. SDK-specific switches are not available just by importing our FBX models. |
| [Alan Wake 2 vegetation skinning, Remedy, GDC 2024](https://presentations.remedy.fi/large-scale-gpu-based-skinning-for-vegetation-in-alan.ppsx), physical slides 27-29 | Reuses skinned source meshes for large non-interactive trees. | Consider shared animation work only if repeated deformation is measured as a bottleneck. Its renderer's mesh variants are not a rule for how many art variants to make. Cached original slide text inspected; no embedded-video review. |
| [Horizon Forbidden West deferred texturing, Guerrilla](https://www.guerrilla-games.com/read/adventures-with-deferred-texturing-in-horizon-forbidden-west) | Uses visibility preprocessing and compute shading, including software variable-rate shading, to accelerate alpha-tested foliage. | Reducing repeated shading is a real engine-level strategy. Rebuilding s&box's rendering pipeline is not the first project-level change. |
| [Epic Impostor Baker documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/impostor-baker-plugin-in-unreal-engine) | Documents Fortnite's distant impostors and the geometry-versus-pixel cost of blending captured views. | Continue using our existing captured far trees. More views, reconstruction and overlapping cards can increase cost despite tiny triangle counts. |

Current frontier research reinforces the same principle. Epic's
[Nanite Foliage documentation](https://dev.epicgames.com/documentation/unreal-engine/nanite-foliage)
is explicitly Experimental and illustrates The Witcher 4 technical demo. It
combines reusable parts, skeletal animation and near-pixel aggregate voxels.
It is neither evidence of a shipped Witcher 4 renderer nor a feature supplied by
our SDF terrain. A custom equivalent would be a substantial engine project.

Two quality details matter when reducing representation cost. Small triangles
can shade inefficiently; AMD discusses this in its
[texel-shading research](https://gpuopen.com/learn/texel-shading/). Also, alpha
minification can make foliage disappear: NVIDIA's
[hashed alpha testing](https://research.nvidia.com/publication/2017-02_hashed-alpha-testing)
addresses it with a noise tradeoff, while
[Unity's importer documentation](https://docs.unity.cn/2020.1/Documentation/Manual/class-TextureImporter.html)
describes preserving alpha-test coverage in mipmaps. These support measuring
coverage and moving-edge stability; they do not establish a ready-made s&box
setting or justify introducing noisy dithering into our already-sensitive trees.

## Recommended order of work

### 1. Establish the actual slow view and fullness reference

First distinguish editor overview from Play mode. Reproduce the user's slow
camera with source identity, physical resolution, active tree count, representation
counts and GPU pass timings. Compare current rendering with the same authored
tree at full leaf visibility. Do not silently define today's thinned rendering
as the desired fullness. Preserve the established figure-eight workload unchanged;
any added close-canopy scenario needs its own predeclared ledger entry.

### 2. Test cheaper individual leaves while preserving every leaf

This is the smallest geometry experiment. Reuse the existing four-triangle fold
where an eight-triangle blade cannot be distinguished, keeping leaf centers,
size, count, texture, normals and attachment behavior. At 544,606 leaves,
eight-to-four triangles saves 2,178,424 leaf triangles per complete tree mesh.
That is arithmetic, not a predicted frame-rate improvement. Folds and close
silhouettes still need comparison; two triangles cannot preserve every fold.

Use projected blade size, not just distance to the trunk, to decide the quality
boundary. A player can stand near outer leaves while far from a wide tree's
origin. Avoid making all leaves rigid or reducing their number as a hidden part
of this trial. The existing mesh levels already supply simpler topology, so
measure their coverage before building another system.

### 3. Build a real middle-distance branch representation

This is the strongest structural candidate for preserving fullness at scale.
Bake small, spatially coherent branch/leaf groups from the full source tree into
trimmed, shaped meshes with aligned color/coverage, normal and occlusion data.
Keep full individual leaves for close inspection; transition groups only when
their leaf detail is no longer distinguishable. Preserve branch depth, gaps and
attachment. Farther out, use the existing whole-tree impostor.

A texture can depict many of the original leaves without processing an independent
folded mesh for each. This preserves the authored population and aims to preserve
its appearance; it does not preserve separate geometric leaves in the middle
representation. If that stricter requirement is mandatory, skip this option.

The failure mode is a pile of large overlapping rectangles with little occupied
area. That exchanges vertex cost for alpha/pixel cost and can flatten the tree.
Use bounded 3D groups and tight coverage, test underside/backlight views, and
compare both GPU passes and memory. Do not hide coverage loss with inflated
individual leaves; earlier project iterations already exposed that artifact.

Blender/export owns these derived assets and attachment metadata. The existing
authored-tree runtime owner should select representations; do not add a second
population, networked leaf state or per-leaf components. Regenerate dependent
captures when source geometry/materials change. Introduce one candidate on the
existing tree before proliferating the asset library.

### 4. Reduce repeated motion and shading only where profiling supports it

Our source contains repeated pivot/frame work. Inspect compiled shader cost and
pass timing before restructuring it. Share object/branch-uniform calculations or
introduce a cheaper motion level that truly skips work while keeping attachment,
phase and normals consistent. The earlier rigid-leaf attempt was small and
reverted, so a large gain is not established.

Compute-once deformation buffers could reuse results across passes, but add
dispatches, synchronization, buffer traffic and memory; camera-dependent facing
also limits sharing. With detailed leaf shadows already replaced, there are fewer
passes to amortize. Defer a compute animation subsystem until measured benefit
justifies these costs. Do not run CPU logic for each of half a million leaves.

### 5. Treat batching, cutouts and culling as conditional opportunities

[s&box documents automatic instancing](https://sbox.game/dev/doc/rendering/shaders/gpu-instancing)
for compatible shared models and materials. The project already reuses models,
and settled far/shadow objects allow batching. Near objects have per-object fade
and view data; flipping their batching flag can break correctness. Installed
`SceneObject.Batchable` documentation explicitly notes dynamic attributes as a
reason for preventing material batching. Measure draw submission before changing
the attribute contract. Instancing does not eliminate the shading and geometry
of all visible instances.

For alpha cost, measure empty texture coverage and fragment overdraw before adding
vertices to trim masks or replacing them with opaque geometry. Our nearby leaves
already have shaped geometry. Both overlapping surfaces and tiny triangles can
be expensive. Keep depth/forward coverage and MSAA behavior consistent; a new
depth-only shortcut must not repeat the previously rejected pinholes.

If vertex submission still dominates after middle LOD, spatial branch-group
culling could skip off-screen or safely occluded groups before leaf work. Current
multipart exports are not proof of efficient spatial clusters. Test the tradeoff
between finer bounds and extra draws/metadata. Leaves hidden from the camera may
still matter to shadows, and camera-facing/wind bounds must remain conservative.
Per-leaf CPU culling and a forest-wide renderer rewrite are deferred.

## Validation gate for a future implementation

Before a run, record exact scenarios and criteria in the existing ledger. Use the
unchanged canonical BIOME-FIGURE8-001/v2 plus existing 038/039/042 quality and
attribution views where comparable. Freeze seeds, assets, placements, light,
resolution, FOV, wind, warmup, duration and source hashes. Additional measurements
must use the playable production path and existing controls, not new test scenes
or hooks.

Compare GPU total, depth/forward/shadow time, draw/vertex/fragment metrics where
available, frame p95/p99, allocations, memory and streaming/correctness. Report
unsupported counters. Keep disabled-leaf/shadow or reduced-resolution observations
as attribution, never as acceptance of a changed workload. Capture a new baseline
if current source/environment no longer matches the recorded accepted run.

The visual gate must cover full crown, close folded leaves, under-canopy and
backlit views, oblique/overhead cameras, wind motion, approaches/retreats, stopped
transitions and overlapping crowns. Compare silhouette, average opaque coverage,
interior gaps and shadow transmission against the full source reference. Fixed
image metrics can assist, but cannot certify perceived depth or temporal shimmer.
Retain leaf identities/counts for any candidate claiming literal preservation.

No new performance gain or visual approval is claimed by this research. The
recommended first implementation is one measured same-count leaf-topology trial;
the recommended larger investment is the missing branch-group middle LOD.

## Source identity and inspection limits

SHA-256 of decision-critical files at inspection:

| File | SHA-256 |
| --- | --- |
| `Code/Voxels/Trees/TreeModelLod.cs` | `4edf17f73992ec817bae7774c2722b4f5bdfc83bf9000787e480ac7700e8ab41` |
| `Assets/shaders/trees/tree_lab_foliage.shader` | `b1acf1790ad738fb41fdf3025706fadb23b5b94ecf560e0cd504b7b92ad9e6a1` |
| `Assets/shaders/trees/tree_wind.hlsl` | `d1820bc48a5b5b0f84c6b82d8631b3332832b1a2247d1511649cfed43158b57d` |
| `Assets/shaders/trees/tree_impostor.shader` | `80a83fc0c1f049d63856bbdd47818ce7c00dd266cb20bbef23a526d1ea7c1365` |
| `Tools/BlenderTrees/export_sbox.py` | `6012131ab92a05a830dc5a1b8edfef0d822537cb31efc6031a7ace8400662b53` |
| Dense oak `manifest.json` linked above | `b0663a13a5ab5756b42960b8852e7cd033e964d6ac22eb52a570e688f9428864` |
| Dense oak `.prefab` | `ee7756263cd1241dcffc927e10f4c4c4d05ec5dfd04ef968ed57a53cdf7da17a` |
| `Assets/scenes/basic_example.scene` | `d74c28f0f87001e73e026de19b599d8078f7069122af10a44f6ad694880e6105` |

External pages were checked September 22, 2026; historical production dates are
stated separately. No external engine's public benchmark is used to forecast a
Voxels3 gain. Existing runtime evidence is linked, not rerun or reclassified.

## Large-world scaling follow-up

Later source inspection on September 22, 2026. Research only: no runtime changes,
editor control, new benchmark or performance acceptance. The request expands from
leaf cost to thousands of trees and other objects across a large world.

### Current source corrections

- The saved `basic_example.scene` now references exactly one dense oak, named
  `Sprite Leaf Test Oak`. Task 046 replaced the earlier 100-tree setup. Existing
  100-tree runs do not measure this scene or the subsequent shadow candidate.
- `TreeModelLod.OnPreRender` forces foliage-only pieces to LOD2, including near
  the player; wood still selects LOD0/1/2 at 3/6 m. There are five detailed
  renderers: one wood model and four foliage models. The manifest still retains
  544,606 leaves. Two triangles per leaf means 1,089,212 submitted leaf triangles
  per complete detailed tree before visibility/retention rejection. The earlier
  5,300,996 total describes the exported full LOD0 asset, not today's selected
  mixed wood/foliage levels. Eight-to-four leaf simplification is no longer the
  next experiment for this path: the cards are already two triangles.
- The shader still reduces retention from 1.0 to 0.30 over 3-8 m from each leaf
  pivot. This happens after vertex submission, not by compacting the input mesh.
  Leaf count in the export is not visible canopy coverage.
- The 12/16 m detailed/distant hysteresis and 0.35 s transition remain. Current
  source restores detailed renderers' shadow types and uses the separate proxy
  only for the distant contribution. Task 047 records an interrupted validation;
  near shadows must not be described as an accepted all-distance cheap proxy.
- Even a settled far tree retains its detailed renderer components and model
  references. Every tree's `OnPreRender` enumerates children, validates renderer
  membership/transforms, and updates attributes, including hidden detail. This
  is an observable scaling concern, not a measured CPU bottleneck. Disabling a
  renderer is not the same as streaming its detailed representation out.

Source owners: [TreeModelLod](../../Code/Voxels/Trees/TreeModelLod.cs),
[leaf shader](../../Assets/shaders/trees/tree_lab_foliage.shader),
[dense manifest](../../Assets/models/tree_lab/oak_growth_18_open_grown_271828_dense/manifest.json),
and [ledger](../ValidationResults.md), tasks 045 final acceptance, 046 and 047.

### Proposed representation and residency policy

Spend detail according to visible size and gameplay relevance. A large logical
world need not keep every tree loaded as a detailed, updating game object.

| Region or need | Proposed representation | Work to retain |
| --- | --- | --- |
| Close inspection | Detailed wood and individual leaves where resolvable | Convincing depth, attachment, light transmission, wind and shadows |
| Middle distance | Baked spatial branch/leaf groups with simplified wood | Full crown, internal gaps and parallax; much less independent leaf work |
| Distant individual trees | Existing multi-view impostors using shared assets | Stable silhouette, lighting and bounded shadow cost |
| Very distant forest | Evaluate regional proxies only if individual impostors become costly | Forest skyline and major canopy structure |
| Outside relevant cells | Persistent instance records or deterministic baseline | No resident detailed renderers, per-tree callback or unnecessary physics |

These are proposed roles, not new fixed distance settings. Use projected size,
FOV and canopy bounds to choose transitions: trunk-origin distance alone can
misjudge a nearby outer branch. Keep hysteresis and visually matched fades;
overlapping old/new representations temporarily increase work. Nearby quality
must be judged under and inside overlapping crowns, not only from an exterior.

Middle-distance groups are the strongest structural rendering candidate. Bake
several leaves together while preserving coverage, depth and lighting. Tight,
shaped cutouts matter: oversized overlapping rectangles exchange geometry cost
for empty-pixel and overdraw cost. Keep the source tree unchanged and derive its
representations offline. Tiny per-leaf geometry throughout an entire canopy is
not necessary to preserve its perceived fullness.

### Regional population and gameplay ownership

For a future population slice, use one owner of stable tree identities and
authoritative changes. The inputs would be versioned world seed, placement rules,
species catalog, terrain support and tree edits; outputs would be regional
instance records and derived render/collision interests. Exact spatial scale
and budgets require design and measurement before implementation.

Load nearby detail progressively, retain cheaper far records/instances, and
release detail references when their regional interest ends. Reuse a bounded
library of meshes, materials and textures with transform/tint variation. A tree
record can carry identity, variant, transform and state without owning five
permanent detailed renderers. Engine asset-cache reclamation must be measured;
dropping references does not guarantee immediate GPU memory reclamation.

Update region membership and transitions when needed instead of validating every
static tree hierarchy every frame. Apply bounded creation/upload work on the
supported engine thread; reject stale background results after region changes.
Use spatial culling before detailed work, with conservative wind bounds. Do not
assume custom terrain provides engine occlusion automatically. Batch by compatible
asset/material within useful spatial groups; one enormous combined forest damages
culling and makes local changes expensive.

Keep visual, shadow and gameplay ranges independent. Solid trunks must exist
where players, AI, vehicles or projectiles can interact, including server-side
interests that have no render camera. Promote a tree to active falling/damage
behavior when needed and persist its changed state. For multiplayer, deterministic
placement must share seed/config/version; replicate authoritative relevant tree
changes with a coherent late-join path, not leaf transforms. Chopping or terrain
support changes must invalidate the corresponding distant representation too.
These contracts are proposals, not a claim that tree networking is implemented.

The same residency principles apply to rocks, shrubs and props. Species and
object types still need their own quality/collision policies. Do not first build
a universal world-object framework or a replacement rendering engine.

### External evidence and choices

[Epic's World Partition HLOD documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/world-partition---hierarchical-level-of-detail-in-unreal-engine)
describes cell streaming, proxies for unloaded distant cells, and an instancing
layer suited to tree/foliage impostors. Adopt the separation of logical objects,
loaded detail and distant visibility as a design direction. Defer regional forest
proxies until profiling shows they are needed. Unreal's editor assets and runtime
are not available automatically in s&box, and procedural edits complicate proxy
invalidation.

[Crysis](https://developer.nvidia.com/gpugems/gpugems3/part-iii-rendering/chapter-16-vegetation-procedural-animation-and-shading-crysis)
documents several leaves on low-polygon planes and GPU main/detail bending.
[Fortnite's non-Nanite path](https://dev.epicgames.com/documentation/en-us/unreal-engine/impostor-baker-plugin-in-unreal-engine)
uses distant tree impostors. Its
[Nanite production account](https://www.unrealengine.com/en-US/tech-blog/bringing-nanite-to-fortnite-battle-royale-in-chapter-4)
instead explains opaque geometry, preserved canopy area, baked wind and disabling
distant deformation. Adopt preservation of visible coverage and distance-dependent
work; do not transfer Nanite's geometry budgets to our conventional model path.

[s&box automatic instancing](https://sbox.game/dev/doc/rendering/shaders/gpu-instancing)
already batches compatible shared models/materials. Settled far/shadow objects
permit batching in our source; detailed objects deliberately do not because of
their dynamic attributes. Verify draws before changing this contract. Instancing
reduces submission and asset duplication, not the visible geometry, alpha tests,
wind or shadow work of all instances. Defer a custom GPU-driven renderer until
measured CPU submission/culling cost justifies it.

### First implementation and acceptance boundary

1. Reproduce the reported slow close view on current source and complete the
   unresolved shadow attribution. Record CPU/GPU frame milliseconds, detailed
   versus distant counts and per-pass cost. FPS losses are not additive per tree.
2. Prototype one coverage-preserving middle representation derived from the
   existing oak, including a compatible shadow representation. Preserve close
   appearance and compare geometry, overdraw, motion and transition costs.
3. Introduce bounded regional residency through the real population owner, then
   replace permanent detailed objects outside gameplay/visual interests. Measure
   component work, asset residency, creation spikes and re-entry behavior.
4. Qualify progressively larger fixed populations in the playable world while
   keeping the canonical figure-eight scenario unchanged. Declare additional
   forest workloads before running; do not compare unlike single/100/1000-tree
   scenes as if they were an optimization of one workload.

Choose target hardware, resolution, view range, forest density and frame budget
before promising a count. At 120 FPS the entire frame has 8.33 ms; vegetation
shares that with terrain and everything else. Record frame p95/p99, CPU and GPU
passes, draws, allocations, process/GPU memory, streaming arrivals and correctness.
Include close overlapping crowns, a wide forest view, movement/re-entry and
relevant multiplayer interactions. No current result establishes thousands-tree
performance or a constant cost per tree.

Follow-up source SHA-256: `TreeModelLod.cs`
`12c3e4f47f5d83e3fff82eee2622a44c391ca7901f65d9c54a1811fd4cc215cd`;
leaf shader `aaac72be4e5abcea258e0313c9f5759dba7a9ca8543745e7a1766e1b6a6afd4b`;
saved scene `e3343843476d7593abe7f716cb60de8c4e81ec4d2290f5710f39be23546a995c`.
Dense manifest hash is unchanged from the initial audit. Documentation/source
inspection only; the interrupted runtime task remains unqualified.

### Measured scaling experiments, September 23

The follow-up [048 experiment report](../ValidationEvidence/TreeScale048/report.md)
records real playable-world populations of 1, 64, 256 and 1,024 dense oaks.
The original elevated distant view measured 323.0, 310.2, 264.8 and 135.9 FPS
at 2769×1529 on RTX 5090/9800X3D. Draw calls stayed at 83 from 64 through 1,024
trees; increasing distant depth/forward pixel work remains substantial despite
batching. These are supporting rolling observations, not a portable tree budget
or ground-level dense-forest acceptance.

Lower wood detail, rearranged leaf groups, hidden-renderer update suppression and
several distant-shader changes did not meet the predeclared performance and
appearance gates. Stronger leaf thinning improved close rendering but visibly
reduced canopy fullness; it was rejected. No 048 runtime optimization is retained.
This narrows the next substantial near-tree experiment to a coverage-preserving
representation of groups of leaves, followed by bounded regional residency.
The existing source leaves, wind, near shadows and saved single-tree scene remain.
