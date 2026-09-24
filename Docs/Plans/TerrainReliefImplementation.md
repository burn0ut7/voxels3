# Terrain relief implementation specification

Current candidate CS retains user-approved CR sand and adds local phase bends
and smoothly tapered ridge breaks to interrupt long horizontal banks. Height
amplitude, grain, color, roughness and AO settings are unchanged. Bake completed;
visual acceptance is pending because the current native views show grassy spawn
rather than the previously reviewed beach. Scoped native camera operations are
now authorized by current user instructions; older restrictions below are history.
Performance testing remains deferred.

Candidate CR replaces the rejected sand pit field with an8-cell4096px
authored ripple field, retaining fine scan grain. All sand ray/shading coordinates
and derivatives use its own period; other materials keep their previous mapping.
Native CL captures and two reviewers confirm recognizable flowing sand and clear
relief, but reject overly uniform ridge spacing and long dark trough ribbons.
CM varies spacing, crest strength and quiet stretches, with height scaled to local
spacing to limit steep narrow walls. Two CM reviews confirm lighter troughs and
more natural quiet areas but still flag parallel ridge banks; CN adds bounded
variation between neighboring ridges and fine-normal gain0.18. CN's local-spacing
height modulation introduced dark knuckles, so CO removes that modulation while
retaining the slowly varying height envelope. CP reduces CO's crest strength15%
after its native view showed stronger dark ribbons. CQ broadens ridges by halving
their count to12 per8-cell field, with envelope strength0.95 to retain height while
reducing steepness and dense striping. CQ looked too soft; CR uses18 bands and
fine albedo-grain highpass gain1.5, preserving broad color variation. Both native
CR reviewers report clearer sand identity, less crowded/harsh relief and better
grain, with a qualified current-view pass. Regular parallel banks and some soft
ridge definition remain; motion/other views are unverified. No performance
testing or camera/world manipulation. Older entries below preserve experiment history.

Native s&box MCP camera capture now shows CK's sand in-world. Root rejects its
current material appearance: dense rounded ridges and dark pockets dominate,
rather than broad flowing sand. The screenshot is evidence of appearance only,
not motion parallax or exact loaded-source identity. Future captures must use
the native camera_screenshot route; desktop captures returned unrelated windows.
Further height increases alone do not address the fragmented pattern.

Current candidate CK follows the user's request for smoother sand with higher
flowing peaks. Sand interval increases60->120mm, combined height is periodically
smoothed at sigma16 cache texels, and fine-normal residual gain decreases0.5->0.25.
Matching normals derive from that exact filtered height. Sand color/roughness/AO
stay unchanged; dirt, snow, grass and stone remain untouched. This targets taller
broad forms rather than amplifying every sharp narrow ridge. In-world acceptance
and independent rendered comparison are pending; performance remains deferred.

Current candidate CJ responds to the user's flat-sand report. Sand's authored
height interval increases from20 to60mm with matching regenerated normals;
color, height pattern, roughness and AO stay unchanged. Snow keeps60mm interval
and increases combined-height smoothing from sigma4 to8 cache texels to soften
the independently observed crease networks. Dirt, bedrock and grass are preserved.
Independent source review finds plausible sand forms but possible amplified
repetition, and credible snow accumulation with remaining crust-like shape risk.
Current-world comparisons against approved dirt are pending: the latest reviewer
capture returned an unrelated game, and no snow world view is available.
No performance testing; no camera movement or terrain editing is authorized.

Current candidate CI retains CH grass, CG bedrock, approved dirt and existing
sand. Snow combined height is periodically filtered at sigma4 cache texels
before quantization and matched normal derivation, targeting crisp rims around
soft interior forms. Snow color/roughness/AO remain identical; only its height
and normals change. Both bakers use Tools/terrain_bake_output.py to encode and
stage full image sets before publishing, addressing observed quiet-input timeout
failures. This is not a multi-file transaction or proof of live engine success.
Independent CI review finds softer rims with preserved broad forms and no
obvious new seams; overall softness still needs rendered review. Performance
remains deferred by user direction.

Current candidate CH keeps CG bedrock and sharpens grass overlap weights by
squaring them after the exponent4 support cutoff. All material channels share
the weights; normals derive from the resulting height. Independent offline
review finds modestly clearer blades in transition regions and no obvious new
seams, but broad hazy/sharp patchiness remains. Sand/snow pixels and dirt/stone
inputs are unchanged. Performance is deferred; rendered validation is pending.

Current candidate CG retains CF's connected source placement. The independent
CF reviewer accepted its offline bedrock identity but identified broad bowl-like
height depressions. CG authors H=clamp(0.5+0.45*(filteredScanHeight-0.5)
-0.35*(1-filteredAO),0,1), with AO sigma0.75 texel. This suppresses broad
undulation and deepens the scan's own crevices; it is an artistic approximation,
not measured displacement. Macro normals derive from the resulting height.
Independent CG map review finds less broad swelling, clearer fracture steps
and preserved coherent faces, without pervasive new grain pits. Remaining
rounded pockets and scalloped/flaking outlines need rendered review. Bake hashes
match; source color pixels are unchanged. Performance remains deferred.

Prior candidate CF preserves the RockFace scan's original connected fracture
network. CE's multi-patch composition was independently criticized for chopped
faces and recurring cap-shaped ledges, so that compositing path is removed from
the stone baker. Stone now uses native2048px,3m repeat,120mm height interval,
height sigma2, fine-normal gain0.25 and minimum mip2. This follows the approved
dirt approach with coherent source placement, and accepts a finite3m repeat
rather than fragmenting the bedrock to disguise it. No rendered acceptance yet.

Latest material correction (2026-09-19): the user explicitly wants continuous
underground bedrock: broad smooth stone faces with sharp jagged fractures. The
loose large-rock/gravel appearance is rejected as the wrong material. Candidate
CE replaces Rocks Ground02 with the existing RockFace source across all five
channels. It uses6m repeat/4 patches (1.5m chart scale),120mm height interval,
height sigma2 to retain fracture edges, and fine-normal gain0.25 for quieter
faces. Macro normals derive from this exact baked height at mip1. RockFace AO
and roughness are16-bit and must be normalized by65535 without8-bit conversion
clipping. Earlier RockFace tests lacked convincing depth; this source change
alone is not proof that the current ray achieves the user's appearance.
Performance remains deferred and computer control remains prohibited.

User steering (2026-09-19): continue visual development regardless of current
performance. Defer performance testing until later; it does not gate ongoing
prototype work. The no-computer-control rule remains in force. Stone candidate
CD sharpens height-based patch selection (contrast96) and varies source scale
from0.8 to1.2 per patch, with corresponding fine-normal slope scaling. This
targets cloudy boundaries and identical-size repeated landmarks. It does not
change dirt, terrain edits, or the shared runtime ray. Visual outcome unproven.
Snow CD also rotates its coherent scan patches, including source normal XY,
to disrupt repeated scalloped bands. Grass and sand retain their placement.
CD stone review finds clearer boundaries but still rejects obvious repeated
landmarks. All CD bakes completed with verified hashes; all grass and sand
image bytes remain unchanged. The unchanged-output write path now avoids
replacing identical images held by other processes. Performance remains deferred.

Current status (2026-09-19): the user approves BZ dirt's appearance and requests
a goal extending the same technique to stone, sand, snow and grass. Preserve
dirt's current maps and physical scale. Motion and performance remain unqualified.

User prohibits computer/live-application control without explicit direction.
Only file/code and non-interactive development work may continue. Do not run
camera, player, play/stop, restart or live editor automation. Visual and runtime
checks below remain pending until explicitly directed.

The latest canonical world observed is revision4438 in parallax-dirt-20260919,
including new user edits. DO NOT restore older4308/4309 saves. The older backups
remain recovery records, not a desired restoration target.

Stone currently repeats pale rock clusters in obvious diagonal rows, confirmed
in the user's live view before the control prohibition. CA's filtered heights
and simpler shading were prepared, but its fixed4309 preflight failed at4438;
no CA cold validation or comparison run occurred. The earlier file-only candidate CB
uses a coherent4m periodic arrangement of rotated/offset scan patches, baked
together for color, height, roughness, AO and normals. Height-aware patch weights
preserve raised features; normals derive from the final combined height.

`Tools/bake_stone_relief.py` owns this stone bake:4096px,4m physical repeat,
120mm height interval, minimum height mip1. The4m repeat replaces the1m repeat
without adding per-step stochastic texture reads to the ray. Approximate packed
texture memory grows from21.3MiB to85.3MiB including mip chains (two BC7 plus one
R16F), so measured whole-game memory and timing are still required.
Alternatives: live stochastic sampling multiplies ray fetches; changing only
color creates mismatched relief; larger tiling simply enlarges rocks. None is
adopted. This pattern remains periodic and must be judged in wide views.

CB binds all five stone maps from `stone_pattern`. The baker generates
`voxel_stone_pattern.hlsl`, which owns stone texture size, physical repeat,
height amplitude and minimum height mip. Ray search and final projection
blending consume the same metadata. Dirt retains2048px,3m,120mm,mip2.
No engine compile, cold start, motion comparison or performance run has been
performed for CB; file checks cannot establish rendered success.
The independent offline critic rejected the first CB albedo for recognizable
repeated slabs and cloudy patch boundaries. CB2 uses continuous deterministic
patch rotations and blend contrast32 instead of16. Independent offline review finds improved arrangement but still rejects
recognizable duplicate slabs and cloudy boundaries. The runtime candidate
remains unaccepted; rotation and sharper blending alone have not solved this.

Grass extension (file-only prototype): the existing periodic grass baker remains
the owner of patch placement. Add the source16-bit displacement as a ninth
channel with identical patch weights, producing grass_height.png. Keep color,
roughness and AO bytes unchanged. Generate macro normals from quantized height
at minimum mip2 with the inverse lattice Jacobian; retain bounded fine normal
residual. Physical art settings: tile1.4m, lattice period2, amplitude20mm,
4096px. This is shallow ground-cover relief beneath existing blade geometry,
not a substitute for blades or an assertion of measured source depth.
Height/ray/color use the same camera-relative coordinates. One shared ray must
include grass and displace all participating material lookups at transitions.
World density, collision, saves and blade geometry remain outside this change.
Additional grass height memory estimate42.7MiB including R16F mips; runtime
cost and visual acceptability remain unmeasured. Alternatives rejected: using
unrearranged source height mismatches features; a separate grass ray disagrees
at material boundaries. No new runtime scenario has been run.

Candidate CC file-only integration now includes all five material heights in
one ray. `Tools/bake_terrain_patterns.py` replaces the grass-only baker and
builds coherent grass/sand/snow maps with generated shared metadata. Sand/snow
use2048px period2 caches, tile1m and20/60mm intervals, minimum mip2. This changes
their former unbounded stochastic placement and halves samples per chart tile;
visual quality must be checked. Old shader stochastic sampling was removed.
All three bakes completed; manifest hashes match. Grass color/roughness/AO are
byte-identical to the prior pattern. Independent grass map critique sees matched
features but soft/sharp patchiness; it is not a rendered parallax pass.
Independent sand/snow map review supports broad color/height correspondence.
Sand has no obvious compositing joins; snow repeats recognizable scalloped
formations. These are concerns for the pending in-world review, not acceptance.
Engine compilation, cold start, visual motion and performance remain pending.

Everything below is retained experiment history and proposed qualification.
Current status above supersedes earlier active-candidate/restoration wording.


Date: 2026-09-18. Status: active prototype experiment, not accepted or shipped.

Current direction (2026-09-19): finish convincing dirt first, then stone,
sand, snow and grass. No material or candidate is accepted. User feedback
rejects BT as too flat, BU as exaggerated, and BV as unrealistic random bumps.

Candidate BX replaced dirt's compacted-grit source with the coherent
Brown Mud03 scan: color, roughness, occlusion and displacement share the same
features. Tile width is the documented1.3m. The100mm normalized height interval
is a prototype art choice, not a measured scan depth; its central90% spans44mm.
The EXR height is converted to16-bit PNG without a curve or smoothing. Normals
combine the actual mip2 height gradient with the scan's fine slope residual.
The source and bake manifest live in Assets/textures/terrain/brown_mud_03.
BX failed independent visual review for corrugated plates and grooved faces.
Candidate BY filters the height with a periodic Gaussian8 (5.08mm sigma),
uses an80mm interval, reduces the fine normal residual to25%, and removes the
old dirt color multiplier. Its central90% height spans34.60mm. Both keep the
BU/BV ray implementation and current stone unchanged. BY also fails: softened continuous crust and cavities remain. The matched ray-off
comparison shows clear added local thickness, but the critic could not establish
a named feature being hidden/revealed. Material realism and temporal proof remain
open. Active BZ changes to Dry Mud Field001 at documented3m scale with120mm
interval, Gaussian2 height filtering and0.5 fine-normal residual gain. It has
separate aggregates in the source; in-world qualification is pending.

BV smoothed and expanded the old dirt height. The user reported improved depth
but rejected the random bumps. Its first screenshot attempt failed because the
viewport left ejected mode; the unchanged retry captured17 views on revision4309.
Root inspection agrees: cones and broad swells do not read as realistic clods.
BU rock also fails independent review for stretched/ribbed sides. BW's filtered
rock recipe is prepared but unrun. Work proceeds on dirt before changing stone.

The prototype changes appearance only. Canonical density, collision and material
assignment remain authoritative. The temporary dirt mound exists in the task
save parallax-dirt-20260919 at revision4309. The user's original slot is preserved
at revision4308 and backed up; restore through the normal load/save path after
visual work, preserving any newer user edits first. Never restore old4274 data.

Performance remains unqualified for BX. BT passed the canonical gameplay numeric
gates at revision4274. Earlier close-view results were invalid because ejecting
reset the actual viewport size. Corrected-size v2 runs also proved invalid for
formal acceptance: they loaded4308 while the scenario specified4274. Their
observed BT close p99 regression was15.08%, so those results do not justify a pass.
Future runs must verify canonical source and actual renderer dimensions before
and after timing. Keep all failures and invalid results in the validation ledger.

Parallax acceptance requires recognizable realistic relief, a matched ray-off
control with named feature hide/reveal, and stable motion. Normal-map shading,
pixel differences, sparse stills or a successful compile alone do not qualify it.
The independent critic remains part of visual review. Sand/snow retain their
correct absolute mapping; grass retains its existing relative mapping until its
own material stage. Do not apply a common depth exaggeration to all materials.

| Candidate | Main change | Observed result |
| --- | --- | --- |
| AM / AN | Filtered slab height, side shading, 4 / 8 cm interval | Visible depth, but soft, stretched and scalloped sides. |
| AO / AP | Rock04 scan; then physical-footprint fade | Cleaner continuous surface, insufficient depth. |
| AQ | Filtered sunlight visibility on Rock04 | No convincing added recess structure. |
| AR / AS | Hit-surface normal and fine detail on Gray Rocks; then sunlight visibility | Ragged seams and artificial side transitions remain. |
| AT | Original RockFace scan with corrected shading | Modest crack displacement, insufficient structural depth. |
| AU | Surface-gradient normal composition | Visual failure; canonical gates pass, close frame-time gates fail. |
| AV / AW / AX | Grazing fade, remove sun trace, physical-footprint fade | All visually too shallow; removing sunlight tracing loses some crack contrast. |
| AY | 24 cm authored interval, 3x normal slopes, up to 64 search steps | Clearly thicker raised edges, but smeared/ribbed sides; visual failure. |
| AZ | Preserve fine grain; strengthen only low-pass normal slopes | Less harsh fine shading; smeared skirts remain. Visual failure. |
| BA | Reduce rock interval to 16 cm | Shorter skirts, weaker relief; visual failure. |
| BB | Bake normal macro shape from filtered height | Cleaner raking shading; ordinary-light skirts remain. Visual failure. |
| BC | Reproject fine detail on apparent steep sides | Blurred underlip replaces ribbing; visual failure. Removed. |
| BD | Rock04 at 16 cm with matched height normals | Corrugated boundary ridge; visual failure. |
| BE | Center height interval around the mesh | Boundary improvement; overall visual failure. |
| BF / BG | Distinct rocky-ground scan,120 mm, matching mip2 normals; restore snow/sand coordinates | More legible stones, but smeared exposed edges; visual failure. BG frame-time gates pass. |
| BH | Direct filtered-height side gradient and reprojected fine grain | Prepared, unrun. |
| BI | Height-aware stone projection weights shared by ray and shading | Visible raised edges; streaked sides and coverage proof still fail. |
| BJ | Height-aware blend plus filtered-height side orientation and fine grain | Less ribbing, but soft stretched sides remain; visual failure. |
| BK | Ray sample count follows projected height-map texels, capped256 | Visual failure unchanged; canonical passes, close frame-time gates fail. |
| BL | Narrower height blending at contrast32 | Prepared, unrun. |
| BM | Extend raised-rock side color uphill; retain fine normal grain | Local grain improvement; exposed-side continuity still fails. |
| BN | BM with prior8..64 step trace restored | Canonical/close performance gates pass; visually equivalent to BM, overall visual failure. |
| BO | Analytic footprint on the local virtual surface for fine grain | Visually equivalent to BN, no obvious new defect; overall visual failure. |
| BP | Narrower height blending using the affordable trace and analytic fine footprint | Better stone separation; exposed faces still fail, some outlines look jagged. |
| BQ | Broader physically reprojected side detail from the same rock source | Visually equivalent to BP; side-quality failure remains. Not retained in BR. |
| BR | Derivative-aware texture chart with separate physical hit and consistent gradients | Visually equivalent to BP; not retained in BS without demonstrated benefit. |
| BS | 80 mm stone interval with matching height-derived normals | Less inflated extrusion; soft directional sides remain. Overall visual failure. |
| BT | 60 mm stone interval with matching height-derived normals | Initial still review failed; higher-resolution matched recording revised material verdict to broadly convincing with modest added relief. Coverage/temporal proof pending. |

Fresh original baseline: run `1b36a84bc5514b04a4d6634efe0fe3e2`, moving
385.695 FPS, p95 4.4896 ms, p99 6.4344 ms; close-view median p95 3.76 ms,
p99 4.81 ms. AU failed close median p95 (+12.50%) and p99 (+29.31%).
AY run `ac4a0795eb2740b5803a744cbf230d43` passes canonical numeric gates;
close median p95 is 3.84 ms (+2.13%), p99 is 4.82 ms (+0.21%). Both close
window spreads exceed the 5% variability flag. Ejected GPU readings remain
unresponsive and cannot establish close GPU cost. These timings do not qualify
AZ or later changes. The independent visual failure still prevents acceptance.

BG run `8391d888ce55430c871a01966cc2f81c`: moving393.93 FPS,
p95 4.4808 ms, p99 6.315 ms; all canonical numeric gates pass. Close medians
p95 3.93 ms (+4.52%), p99 5.23 ms (+8.73%) pass, with p99 variability flagged.
Stock GPU profiling exposes a responsive terrain-draw scope: BG median0.7971 ms;
matched original median0.7617 ms gives +0.0355 ms, within the0.5 ms budget.
Original window spread10.26% is flagged. This is a component timing,
not the unavailable ejected whole-frame GPU counter. Neither BH nor BI is qualified by BG.

BK run `baf301f1a7c64a92bc929923a9e3768e` passes canonical numeric gates,
but close medians p95=4.47 ms (+18.88%) and p99=5.61 ms (+16.63%) fail.
Terrain GPU scope median1.0235 ms (+0.2619 ms) passes its separate budget,
with5.16% window variability flagged. This does not waive frame-time failures.
Any retained descendant must resolve the close-view regression.

The old dirt exposure is covered by grass in the current world; DIRT-002/v1 is
invalid. Temporal stability, mixed interfaces, other material normals, fade
behavior and corrected close-view performance remain open. The ledger preserves
exact settings, failures and evidence. Requirements below remain the acceptance
target; the starting-point section describes the original implementation.

## Decision and intended result

Implement near-camera parallax occlusion mapping (POM) for dirt and rock first,
using one intersection with a combined material height field. Qualify sun-facing
relief shadows next, then extend to other materials. Treat depth-buffer output as
a separate integration gate. Keep actual grass blades and use geometry for large
protruding stones: ordinary POM does not change silhouettes or collision.

The target is stable, recognizably three-dimensional ground at normal player
height while moving: clods overlap recesses, cracks have depth, and small relief
casts local shadows. A changed screenshot or a successful shader compile is not
acceptance. No claim of equivalence to another renderer is made before testing.

This document owns the proposal. [Voxel materials](../Architecture/VoxelMaterials.md)
owns implemented behavior; [the validation ledger](../ValidationResults.md) owns
fixed scenarios and outcomes. The earlier user-requested removal is the pre-prototype baseline. Current
working-tree behavior is experimental and has not replaced that accepted baseline.

## Historical pre-prototype starting point

The following describes the baseline before the active experiment. Current
working-tree dirt/stone height maps are sampled by the combined relief ray;
sand, snow and grass height bindings remain unused. Current dirt/stone use
direct tiling rather than the baseline stochastic patches.

- [voxel_terrain.shader](../../Assets/shaders/voxels/voxel_terrain.shader) samples
  color/roughness and normal/AO BC7 pairs. Height inputs are declared but unused.
  Texture detail is full through 64 m and fades to mip averages by 128 m.
- Dirt, rock, sand and snow use three stochastic patches per contributing
  triplanar axis. Grass uses a baked periodic pattern. Existing zero-contribution
  branches and explicit texture gradients avoid some unnecessary reads.
- [voxel_terrain.vmat](../../Assets/materials/voxels/voxel_terrain.vmat) binds
  original grass height alongside baked grass color/normal/roughness/AO.
  At this historical stage the grass baker emitted no height map; its current
  successor is [the pattern baker](../../Tools/bake_terrain_patterns.py).
- [voxel_terrain_depth.shader](../../Assets/shaders/voxels/voxel_terrain_depth.shader)
  is a separate depth/shadow path with geometric normals and no relief sampling.
- Prior POM had 12-24 angle-dependent steps, linear crossing refinement, a 2-8 m
  distance fade and grazing fade. A slope-coordinate correction was made before
  removal. No relief self-shadowing or pixel-depth output was implemented.
- TERRAIN-DEPTH-AUDIT-001 proved the old grass POM affected pixels, not that it
  looked convincing. CLOSE-001/v2 estimated roughly 0.72 ms/frame for old POM in
  one close mixed view; session drift and a stale GPU counter limit that result.

Some texture README files still describe the earlier 32-64 m fade. Current shader
source governs this plan. Prior results cannot qualify a new shader or asset bake.

## Ownership, inputs and boundaries

The forward terrain shader owns relief evaluation. Inputs are world position,
geometric normal, interpolated canonical material coverage, camera/light data,
matched texture maps and authored per-material physical scales. Outputs are a
visual hit position, common lookup coordinates, shaded normal and surface values;
later stages may output direct-light visibility and validated pixel depth.

All calculation is per fragment on the GPU. No per-frame CPU allocation, terrain
readback, new world cache, network message, density change or collision rebuild is
required. Terrain edits continue to rebuild their existing derived geometry;
relief follows that geometry and material coverage without another invalidation
system. Art changes require texture/material rebuilding, not world regeneration.

Put the shared height evaluation and ray search in one proposed include,
`Assets/shaders/voxels/voxel_terrain_relief.hlsl`. Keep material texture declarations
and per-material scale/amplitude values in `voxel_terrain.shader`. Reuse the include
from a depth pass only if that later integration passes its gate. Do not retain
the old independent-per-patch POM implementation as another production path.

## 1. Height assets and physical scale

Retain current source art for the first comparison. For every enabled material:

1. Inspect color, normal and height together; record dimensions, bit depth, value
   range, orientation and white/high convention. Confirm large stones/ridges line
   up. Preserve originals and provenance. Do not infer centimetres from grayscale.
2. Add a separate linear height texture, initially with the already demonstrated
   BC7 output format and height in R. Both existing packed maps have occupied alpha
   channels. Do not silently sacrifice AO or roughness. BC4 was rejected by an
   earlier engine compile; another format requires current compiler evidence.
3. Check compiled height and its mips for terracing, blocks and disappearing peaks.
   If BC7 fails the fixed visual checks, establish a supported higher-quality
   format and its measured memory cost before proceeding.
4. When grass is enabled, extend the existing baker to emit `grass_height.png`
   using precisely its current offsets, period, wrap and patch weights. Add its
   source/output hashes to the manifest and bind that output in the material.
   Keep normal/height correspondence under review: averaging normals and heights
   separately does not guarantee mathematically matching slopes at patch seams.

Starting values below are authored candidates, not measured scan relief:

| Material | Existing tile size | Candidate peak-to-trough relief | Initial scope |
| --- | --- | --- | --- |
| Dirt | 2.00 m | 0.033 m | Enabled |
| Rock | 2.38 m | 0.040 m | Enabled |
| Grass ground | 1.40 m | 0.010 m | Disabled until matched bake |
| Fine sand | 1.00 m | 0.002 m | Disabled until dirt/rock accepted |
| Snow | 1.00 m | 0.003 m | Disabled until dirt/rock accepted |

Use a recessed layer for the first slice: white lies at the geometric surface;
black lies one amplitude inward. This makes the search bounds explicit and avoids
pretending relief can extend the mesh outline. Raised debris belongs to geometry.
Snow dunes and sand ripples larger than these source features are separate art or
geometry work, not a reason to inflate fine-grain height amplitudes.

BC7 with a full mip chain costs approximately 5.33 MiB per 2048-square height map.
Dirt plus rock therefore adds about 10.67 MiB; adding the other two 2K maps and one
4K grass map totals about 42.67 MiB. These are texture storage estimates, excluding
driver overhead and source images; record actual residency during qualification.

## 2. One local surface and one view intersection

Use a local planar approximation anchored at the rasterized surface. In this
section all distances and positions are metres; convert engine inches once using
the existing 0.0254 convention. Never mix normalized UV depth and world distance.

Let P be the geometric surface position, N its normalized geometric normal, and
V the normalized direction from P toward the camera. Define nV = dot(N,V), and
T = V - N*nV. For positive inward depth d:

```text
U(d) = P - T * d / nV        tangent-plane point for texture evaluation
Q(d) = U(d) - N * d          apparent world hit on the camera ray
D(U) = sum_i w_i * A_i * (1 - h_i(U))
g(d) = d - D(U(d))
```

Here w_i are normalized current presentation weights, A_i are physical relief
amplitudes, and h_i is the normalized height sampled with the material's existing
tile scale, stochastic patch blend and triplanar weights. An inactive material
has A_i = 0. Freeze geometric normal, projection weights and macro material weights
at P for this short local search. Recompute stochastic patch coordinates/weights
at each U: holding those fixed across a crossed patch edge would define a different
height function. Use those same rules for final surface sampling.

Evaluate the combined height at every step; stop at the first sampled sign change
of g from negative to nonnegative. The search interval is [0, sum_i w_i*A_i]. It is
bounded because sampled heights and weights lie in [0,1]. If the interval is zero,
or the initial point is already at the top surface, return the original lookup.

Refine the bracket and sample all final color, normal, roughness and AO maps at
the common U hit. Q is reserved for actual apparent world position/depth, not for
silently changing the height-field parameterization. Preserve existing color and
roughness tuning so the first comparison isolates relief.

This is a coherent *chosen* height field, not a reconstruction of scanned 3D
geometry. Linear blending can flatten mixed relief. It is preferred initially
because it has explicit bounds, preserves existing coverage, and does not invent
material-specific sediment rules. A height-priority blend is a later design
change requiring the same height and surface selection rules throughout.

Do not independently trace each plane/material and blend the resulting colors.
That yields multiple hit depths instead of the single surface defined above.
Finite stepping can still miss narrow peaks: refinement improves a found bracket,
not the detection of features skipped by the coarse march.

## 3. Initial quality controls

These are fixed prototype starting settings. Register them with exact scenario
parameters before running; change them only as a separately identified candidate.

| Control | Initial value or rule |
| --- | --- |
| View steps | ceil(lerp(16, 32, 1 - saturate(nV))) |
| Intersection refinement | 4 bisections of the first bracket, then linear interpolation |
| Distance relief factor | 1 - smoothstep(8 m, 16 m, camera distance) |
| Grazing relief factor | smoothstep(0.05, 0.15, nV); zero for nV <= 0 |
| Height footprint factor | 1 - smoothstep(2, 4, estimated height texture mip level) |
| Height sampler | Separate anisotropic sampler, initial maximum anisotropy 4 |
| Final color/normal sampling | Retain current 16x anisotropy and explicit gradients |

Use per-material footprint factors on A_i, computed at P from undisplaced
derivatives and the actual texture transform, including the grass bake transform.
Apply distance and grazing factors to amplitudes before defining the search
interval; do not abruptly switch the final UV back at a distance boundary.
Skip the search entirely when the effective interval vanishes. Keep the existing
64-128 m surface-detail fade independent of this shorter relief range.

Compute derivatives before divergent branches or ray loops. Use explicit gradients
for ray samples; implicit derivatives inside divergent tracing are inappropriate.
The initial gradients approximate the undisplaced footprint: inspect movement and
grazing views for shimmer or excessive blur rather than assuming perfect filtering.

The prior 4x-height-sampler experiment did not establish a useful tradeoff. This
plan's separate sampler is a new candidate and must be compared visually to 16x;
it is not a previously validated optimization. More samples are not a default fix
for coordinate, map-alignment or lighting defects.

## 4. Relief shadows and depth integration

After the unshadowed view intersection passes, add one bounded visibility trace
toward the directional sun through the same D(U). Start at Q, advance toward the
light to the top of the layer, and compare the ray's inward depth against D at its
tangent-plane projection. A point deeper than the sampled surface is blocked.
Skip for dot(N,L) <= 0 and when geometric lighting already contributes no sunlight.

Start with 8 evenly spaced samples and a depth-comparison bias of the greater of
0.1 mm or 1% of the effective relief interval. Use the same fades as the view ray.
Treat this as a hard-visibility prototype; soft shadows need a separately measured
filter or light-cone model. Fine blockers may be missed at this budget.

Apply visibility only to the sun's direct diffuse/specular contribution. Do not
multiply albedo, all lighting, or baked AO to fake a sun shadow. First identify a
supported installed s&box shading hook that permits this. If it is unavailable,
record that blocker; do not copy an entire engine lighting implementation or
claim self-shadowing from darker AO.

Pixel depth is a subsequent gate, initially off. Verify installed shader output
support, actual depth comparison state, prepass order and every depth consumer
before writing depth derived from Q. The current depth/shadow shader does not
sample material height. Forward-only depth changes can conflict with prepass
depth and early rejection. Camera depth, shadow-map depth, water intersections,
grass contact, SSAO/contact shadows and visibility must be checked explicitly.
Do not feed visually recessed depth into conservative terrain occlusion without
proving it cannot falsely hide geometry. Pixel-depth output does not by itself
produce accurate relief shadows in shadow maps or change mesh silhouettes.

## 5. Work order and performance gates

1. Record current source hashes, engine/hardware/settings, current accepted
   comparable baseline and material assets. Capture fixed dirt/rock views before
   edits; no runtime baseline is asserted by this documentation task.
2. Integrate matched dirt/rock height inputs and the single-intersection shader.
   Keep current stochastic mapping for the first quality/cost measurement.
3. Measure pure dirt, pure rock and their mixed regions. Qualify mapping and
   motion before adding sun visibility. If cost fails, stop broad rollout.
4. Add and independently measure sun visibility. Resolve lighting integration
   before calling the result the complete relief candidate.
5. Extend to grass, sand and snow only after their asset/visual gates pass.
6. Evaluate pixel-depth output separately, then run clean-start and full canonical
   acceptance for the final retained implementation.

The combined field avoids incoherent hits but is not inherently cheap. Two active
materials with three planes and three patches require up to 18 height reads per
field evaluation. A 32-step march, initial sample and four refinements can require
666 height reads before final shading; eight light samples add up to 144 more.
Early hits and zero-contribution branches help, but this worst case is a real gate.

If height evaluation dominates, the preferred next bounded experiment is a matched
periodic bake for dirt/rock, following the existing grass approach and baking all
channels together. This trades memory and finite repetition for fewer height reads;
it needs its own art and performance qualification. Do not silently select one
dominant material/axis, lower quality, or increase fade aggressiveness to pass.
Do not build a runtime world cache or a second renderer for this slice.

Engineering screening target: added terrain GPU time <= 0.5 ms median and <= 1.0 ms
p95 in the registered close views at the registered resolution, using a verified
fresh timing source. These are proposed budgets, not observed results or permission
to weaken existing acceptance. If trustworthy pass timing is unavailable, report
the gap and use comparable frame timings without labeling them GPU timings.

Final acceptance uses the unchanged canonical figure-eight and its latest accepted
comparable baseline and recorded criteria. Include moving and standing frame time,
p95/p99, chunk completion/streaming, CPU allocations, process/GPU memory and
correctness. No added draws, per-frame CPU allocation or geometry rebuild work is
expected. Explain any measured change. A workload change or accepted regression
requires documented evidence and explicit user approval under project rules.

## 6. Required visual scenarios and evidence

Before implementation testing, define TERRAIN-RELIEF-001/v1 in the ledger. Select
real playable-world locations and record exact world/save identity, seed, revision,
camera positions/angles/FOV, resolution, lighting, movement endpoints/speed/duration,
material values, warmup and measurement windows. This plan deliberately does not
invent world coordinates or claim the scenario is already registered.

Capture matched control/candidate views and movement through the existing playable
entry point. Include dirt, rock, their junction, a steep face, a curved cut surface,
a chunk/LOD boundary, and later the grass/sand/snow contacts. Inspect at normal
player height and close range; include front-facing and grazing views and movement
across 8 m and 16 m relief fade boundaries. Use a recorded low-angle sun and a
recorded high-angle sun for the shadow candidate.

Pass conditions:

- Track at least three identified clods/ridges per dirt/rock view: their occlusion
  and motion must agree with the height map while moving, without texture swimming,
  reversed relief, duplicated peaks or patches sliding independently.
- No visible banding, black junctions, projection-axis seams, chunk seams or sudden
  fade pops at native playback resolution. Preserve the evidence for every failure.
- Sun visibility follows light direction and relief; shaded recesses must not
  incorrectly darken ambient illumination with the sun trace disabled.
- No obvious floating blade roots, water contact gaps or incorrect terrain
  visibility introduced by relief. Depth integration must separately pass its
  contact/occlusion cases before it is enabled.
- Source/shader/material compile succeeds, followed by a clean editor restart and
  fresh-log/crash-marker checks required by the meshing route. Hot compile alone
  does not establish clean-start safety.
- Canonical performance criteria pass. Do not substitute image-difference counts
  for depth quality or an average FPS for frame pacing.

Record each candidate's hashes, parameters, raw measurements, observed defects and
pass/fail decision. Keep failed runs. Use only the real renderer and existing
controls; no separate test scenes, synthetic renderers or test-only hooks.

## Alternatives and external evidence

- Restoring the deleted independent POM is smaller but retains multiple surface
  intersections and omits the present baked-grass height contract. Reject it as
  the implementation basis.
- Single-offset parallax is cheaper but lacks a searched occluding intersection.
  It does not meet this target.
- Tessellation/displacement or mesh detail changes silhouettes but adds geometry,
  LOD, shadow and contact responsibilities. Reserve it for features POM cannot
  represent rather than claiming POM replaces it.
- Cone/hierarchical tracing may skip empty intervals, but needs additional data,
  conservative bounds and preprocessing. Defer until measured tracing cost
  justifies it; do not adopt an unbounded research implementation first.

[Tatarchuk, Practical Parallax Occlusion Mapping](https://advances.realtimerendering.com/s2006/Tatarchuk-POM.pdf)
supports bounded height search, refinement, adaptive sampling, LOD and relief
shadows. Adopt those principles. The combined triplanar/material height field,
physical amplitudes, numerical budgets and fades above are project proposals,
not values established by that paper.

[Unity HDRP 17 displacement documentation](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Displacement-Mode.html)
provides a production reference for physical scale, min/max height, sample budgets,
mip fading and optional depth offset for depth-based effects. Adopt the explicit
contracts; it does not establish s&box support or equivalent performance.

Installed s&box `core/shaders/common/classes/Decals.hlsl` contains
`ParallaxOcclusion_Grad` with gradient sampling and crossing interpolation. It is
engine-version-local evidence that the technique exists, not a drop-in solution
for blended voxel terrain or proof of the required lighting/depth hooks.

### Normal composition (implemented in prototype, unaccepted)

[Mikkelsen's surface-gradient framework](https://jcgt.org/published/0009/03/04/paper-lowres.pdf),
sections 4.3 and 4.4, informed AU's replacement of modified-normal blending.
The prototype converts sampled normals to slopes, blends them across patches,
projections and materials, projects onto the geometric tangent plane, then
normalizes once. The experimental screen-derivative side-normal replacement was
removed. AU's new matched flat control confirms that shading changes alone do
not establish convincing parallax; both AU and AV failed visual acceptance.
Source-normal/height agreement and finite slope limits remain asset constraints.

## Completion and documentation updates

After accepted implementation, update VoxelMaterials with the actual algorithm,
active materials, scales, packing, bounds, lighting/depth support and limits.
Update texture README files and manifests to match retained assets and distances.
Link results to this proposal and the ledger; remove superseded runtime paths.
Do not mark grass, self-shadowing, depth integration or engine parity complete
merely because dirt/rock view tracing works.

## Measurement correction (2026-09-19)

BT canonical run `dc2c52e635d94faeb4662971a80dabaa` reports387.92FPS,
p95=4.4898ms,p99=6.2582ms and zero runtime exceptions/collision failures.
Its surprisingly fast close timing was traced to a smaller ejected viewport.
The installed editor calls `SetDefaultSize` on eject. The existing camera
inspection now reports actual renderer dimensions. CLOSEPERF-001/v2 and
GPUPASS-001/v2 preserve the intended2769x1529 workload and thresholds,
require actual size readback, and establish a new original-material baseline.
All earlier close measurements and visual failures remain in the ledger.

BT corrected-size v2 observations still exceed the close p99 budget (+15.08%).
A subsequent source audit also invalidated formal v2 qualification: both runs
loaded revision4308 after live edits, while their labels/preflight expected4274.
Future timings must verify actual canonical revision. Latest user state4308 is
backed up and preserved; dirt visual work uses a separate4309task slot.

## BU clump experiment (2026-09-19, unaccepted)

BU addresses the user's flat-soil feedback with an authored height range and
matching normals. Dirt/stone tile at1m, with full intervals80mm/120mm. Source
dirt's central90% height spans only0.267, so the old33mm setting gave that
range8.81mm. A derived16-bit dirt map remaps p1/p99 with a smoothstep curve;
its matching normal is rebuilt from boxmip2 of the same quantized height.
The original scan remains unchanged. This is an art choice, not calibration.

Dirt now shares the normalized smooth-maximum projection field and its shading
weights with stone (contrast32). Both traces and macro normal bakes use the
same mip2 floor. The8..64step view ray and existing rock side treatment remain.
No actual terrain geometry, collisions, pixel-depth or sun-shadow change is
claimed. A real built Dirt2 mound in a separate saved world provides readable
soil exposure. The user's latest4308 world is backed up; the4309task copy holds
exactly one additional build. DIRT-BUILD-001/v2 owns the fixed17views.

BU must still demonstrate clear clumps, believable surfaces and matched
ray-disabled visual differences. Bigger displacement values, more shading or
texture motion alone do not pass. Performance is measured only after visual
credibility, with actual viewport size and canonical revision checks.

## Remaining material sequence after dirt

Finish dirt's visible quality, matched ray-off coverage and motion checks before
retuning stone. Current stone fails the realistic exposed-face requirement;
the filtered-height BW preparation has not run and cannot count as progress
verified in the world. Retain both its ordinary and severe grazing scenarios.

Then qualify sand, snow and grass separately. Their height bindings currently
have no runtime sampling. Sand and snow use1m source tiles and absolute
stochastic mapping; the ray must evaluate the same patches as all other channels,
or all channels must move together to a shared mapping. Sand should retain
shallow grains/ripples; snow should preserve rounded accumulation without sharp
craters. Their actual source art and physical amplitudes need individual review.

Grass uses a1.4m tile and the periodic grass_pattern bake. Its currently assigned
original Grass004 displacement does not match that baked pattern. Extend the
existing canonical baker to include matching height if grass relief is pursued;
do not apply that original height directly to the cached color. Preserve actual
grass blades as geometry. None of these requirements is implemented by this note.

Each material requires fixed in-world parameters in the ledger before runtime
validation, independent visual critique and final combined figure-eight/close
performance checks against a comparable original-material baseline.
