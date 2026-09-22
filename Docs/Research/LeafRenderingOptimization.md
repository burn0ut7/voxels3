# Keeping full canopies affordable

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
