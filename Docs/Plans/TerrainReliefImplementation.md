# Terrain relief implementation specification

Date: 2026-09-18. Status: proposed implementation, not shipped or validated.

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
fixed scenarios and outcomes. The earlier user-requested removal remains the
current runtime policy until a new implementation is undertaken.

## Verified starting point

- [voxel_terrain.shader](../../Assets/shaders/voxels/voxel_terrain.shader) samples
  color/roughness and normal/AO BC7 pairs. Height inputs are declared but unused.
  Texture detail is full through 64 m and fades to mip averages by 128 m.
- Dirt, rock, sand and snow use three stochastic patches per contributing
  triplanar axis. Grass uses a baked periodic pattern. Existing zero-contribution
  branches and explicit texture gradients avoid some unnecessary reads.
- [voxel_terrain.vmat](../../Assets/materials/voxels/voxel_terrain.vmat) binds
  original grass height alongside baked grass color/normal/roughness/AO.
  [The baker](../../Tools/bake_grass_pattern.py) currently emits no height map.
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

## Completion and documentation updates

After accepted implementation, update VoxelMaterials with the actual algorithm,
active materials, scales, packing, bounds, lighting/depth support and limits.
Update texture README files and manifests to match retained assets and distances.
Link results to this proposal and the ledger; remove superseded runtime paths.
Do not mark grass, self-shadowing, depth integration or engine parity complete
merely because dirt/rock view tracing works.
