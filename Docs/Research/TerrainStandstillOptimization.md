# Standstill terrain optimization

2026-09-17. Retained: offline compressed grass pattern, direct fog blending,
and two directional shadow cascades with close coverage preserved in the
qualified views. Results and limits are below; earlier screening notes remain
historical evidence.

The user prioritizes recovering the approximately 600 FPS seen before terrain
texturing. Current fixed-view observations are approximately 330 FPS at
2769 x 1529. Quarter-pixel rendering reached about 547 FPS without changing
geometry, strongly implicating pixel-sensitive rendering. The color draw costs
approximately 1.2-1.4 ms, but includes vertex work, sampling and standard shading;
it is not an isolated texture timer. See the prior high-gain investigation.

## Ownership and experiment sequence

The existing terrain draw shader owns material evaluation. Canonical generated
vertex weights own material membership; the authoritative field, meshing,
collision, shadows and scene distance policy remain unchanged. No new mutable
terrain data, extra renderer, test component or runtime toggle is proposed.
The current visible interactive world and existing figure-eight/sampler are the
only runtime entry points. Fixed scenarios and every result live in the ledger.

First measure the original full route and stationary view. Then temporarily
substitute a lit constant material, and a composite output retaining albedo,
normal, roughness and occlusion evaluation but omitting standard shading.
The composite deliberately depends on all sampled channels so dead-code removal
does not silently turn this into an albedo-only test. Both change appearance and
are diagnostic ceilings only. Restore the original and measure again before
choosing an implementation.

Potential choices depend on those costs. Material simplification must preserve
close texture detail, normal mapping and material blends. Lighting changes must
preserve terrain shadow reception and useful environmental response. Previous
single-sample/dual-projection/footprint/pattern-cache failures remain evidence;
they are not automatically candidates again. A new cache or rendering system
requires its own ownership, invalidation, memory and supported API design first.

Target a repeatable at least 20% stationary gain with no material route,
correctness or visual regression; 600 FPS is a direction, not a promised result.
Retained changes require a canonical route, matched near/slope/blend views and
clean editor startup. Shader resource/parser constraints still apply.

## Evidence so far

Original canonical route: 425.57 moving / 371.02 stationary FPS, zero
exceptions/collision failures, settled geometry in five arenas. Original idle
windows were 372.3, 374.7 and 356.3 FPS (marginally beyond the 5% spread flag).
This differs from the earlier fourteen-arena observation, so shader variants
must use the new control rather than taking credit for allocation repacking.

The lit constant-material trial completed, but eye angles changed by approximately
0.35 degrees pitch and 0.52 degrees yaw. Its 453-459 FPS is an invalid matched
comparison. Original appearance is restored; no gain is accepted from that run.

Installed engine evidence: ShadingModelStandard::Shade applies decals, converts
to the standard combiner, evaluates direct/indirect lighting, applies diffuse
and specular AO and composites atmospherics. The named color draw therefore
includes more than texture lookup. Material::Init(PixelInput) conditionally reads
tangents and lightmap/texture coordinates; MainVs does not explicitly write
those fields. The CUSTOM_MATERIAL_INPUTS compile condition may bypass the reads,
so this is an inspection question, not an established runtime bug or saving.
No unsupported simple-lighting API or feature flag will be assumed.


## Source checks while live comparisons await readiness

The installed engine's `core/shaders/common/material.hlsl:90-110` copies
world tangents, lightmap UV and texture coordinates in `Init(PixelInput)` unless
`CUSTOM_MATERIAL_INPUTS` is defined. The voxel vertex shader writes none of
those fields. A search of the installed core shader tree finds that define only
in individual terrain, sprite and gizmo shaders, not in shared includes; the
voxel shader has no such define. `vr_common.fxc:100-102` defaults the tangent
basis flag to one. This establishes an input-initialization concern from source;
actual compiled dead-code elimination and its timing remain unmeasured.

The candidate `defined-inputs` uses the supported
`Material::Init(input.vPositionWithOffsetWs.xyz, input.vPositionSs)` overload and
`ShadingModelStandard::Shade(material)`. Current code then explicitly writes its
normal and every surface channel. This avoids reading unwritten vertex fields
without adding a custom lighting model. The candidate is prepared outside the
project in the experiment driver; it has not been installed, compiled or timed.
It must pass matched appearance, route and cold-start checks before retention.
This concern also exists in the pre-texture shader at commit 3627528, so it does
not by itself explain the reported texture-related FPS drop.

The installed standard shading path has no verified one-switch specular disable.
It handles direct lights, indirect ambient/probe/lightmap/DDGI branches,
reflection maps, AO, decals and atmospherics. Presence in source does not mean
every branch executes in this scene. A custom cheap-lighting replacement would
need explicit feature and appearance acceptance; none has been adopted.

Read-only inspection of the compiled shader resource's HLSL block recovered
source programs with includes, not preprocessed output or GPU register counts.
No compiled artifact was edited. The original full-view screenshot was inspected:
near grass detail and distant smooth material colors are present; it is only a
baseline, not visual validation of a candidate. The shader remains restored to
SHA-256 `17d02d4409b16bb0e8d8bd0b87b47de25f47da7290adb087bce8de5177ef2a2e`.


## Matched diagnostic recovery (v2)

The actual view remained fixed after the interruption. Controlled experiments
continue under the user's existing authorization, with automatic checks of
camera, world revision/checkpoint, queues, collision readiness and resolution.
The earlier readiness question no longer gates work. v2 has a separately declared
baseline at the changed view; no gain is calculated against v1.

| Variant | FPS windows | GPU ms windows | Interpretation |
| --- | --- | --- | --- |
| Original A | 369.0 / 367.5 / 367.7 | 2.36 / 2.36 / 2.35 | Stable control |
| Lit constant | 457.8 / 457.2 / 457.0 | 1.70 / 1.70 / 1.70 | Removes texture evaluation; keeps lighting |
| Unlit composite | 438.6 / 437.8 / 439.3 | 1.80 / 1.81 / 1.80 | Keeps sampled channels; removes lighting |
| Original B | 347.4 / 367.3 / 366.4 | 2.36 / 2.36 / 2.36 | GPU control matches; FPS spread 5.52% flagged |

Texture evaluation removal saves about 0.66 ms of whole-frame GPU time in
this view. Lighting removal saves about 0.55 ms. These are diagnostic deltas,
not additive isolated timers or appearance-preserving optimizations. Both
variants' inspected screenshots show the intended appearance changes; neither
is a shipping candidate. Even removing textures entirely does not reach
600 FPS here. Reflections, shadow reception, common geometry/depth/shadow costs
and CPU/frame scheduling still need attribution. Original-B's low first FPS
window is retained, despite unchanged GPU time; no stable-FPS claim uses it as
a favorable baseline. Readiness, parsing and source evidence above remain history.


## Compressed pattern experiment design

Prototype one material, grass, before expanding the asset cost. The existing
source grass maps remain authoritative art inputs. An offline deterministic
bake evaluates the three-patch stochastic pattern on a periodic four-by-four
triangular lattice. Each vertex hashes its wrapped lattice coordinates and
samples source UV relative to that lattice vertex, making opposite cache edges
continuous. The shader maps physical tile UV into lattice coordinates; normal
vectors remain expressed in the original projection axes. Existing triplanar
weights, physical grass tile size, material weights, 32-64 m fade, shadows and
standard lighting remain. No final world-space surface colors are cached.

Use 8192-square source images over four lattice cells: approximately the current
2048 samples per physical source tile. The two packed BC7 runtime maps with mips
cost approximately170.67MiB, versus10.67MiB for the original grass pair: about
160MiB incremental logical texture memory if the original pair is released.
Measure actual residency; hot-reload retention is not a cold memory result.
Other materials remain original during this bounded experiment.

Ownership: source art plus bake parameters produce ordinary derived texture
assets; the material compiler owns BC7 packing/mips and normal texture lifetime.
The existing terrain shader remains the only surface renderer. No runtime cache,
extra draw, compute dispatch, field representation or mutation path is added.
World edits and geometry publication still consume current material weights and
normals, so they require no baked-pattern invalidation. Source-art changes require
regeneration before material compilation; a retained bake needs a reproducible
production build utility and source-hash manifest. All generation is outside
measured windows. Reject missing channels, broken wrapping, obvious near detail
loss/repetition, or gains below the large-gain target after matched comparison.
This changes the particular anti-tiling pattern and introduces a finite repeat;
it is not pixel-identical and cannot be accepted by FPS alone.

Alternatives: the prior runtime1024 RGBA16F dual-pattern cache was rejected and
is not reinstated. This prototype uses higher spatial density and compressed
ordinary assets, with all visible grass sampling cached. If this does not recover
a substantial part of the measured cost, do not generate the other four materials.


Additional v2 diagnostics: defined-inputs366.3/368.7/368.5FPS,GPU2.37/2.35/2.36ms;
no measured optimization. Its inspected view retained normal terrain appearance.
Diffuse-only384.5/384.0/383.8FPS,GPU2.25/2.24/2.25ms; removing specular terms saves
about0.11ms, below the large-gain target, so it is not adopted. Flat-output
511.8/510.5/504.1FPS,GPU1.48ms all: even deleting all terrain material/lighting
work did not reach600FPS at this full-resolution view. Lighting and texture
removal deltas are not additive: compiler optimization/register lifetime and
shared work can change when either half is removed; no particular mechanism
has been isolated with hardware counters.

The first compressed grass bake completed in53.5s using bundled NumPy2.3.5 and
Pillow12.3.0. Generated PNG source sizes: color128,075,067 bytes,
normal162,234,802,roughness48,082,353,AO51,487,431. These large source artifacts
are experimental and not yet retained; production storage/build policy remains
a gate if the runtime result is useful. Reproducible baker and source/output
hash manifest are saved with the evidence. Fresh original after generation:
361.4/360.6/361.6FPS,GPU2.39ms all, stable camera/field/queues/resolution.

## Combined candidate screening

The8192/period4 grass bake recovered about11%FPS but added160MiB of GPU
texture residency and produced source images over100MB each. A4096/period2
bake retains the same approximate source density and reduces incremental
residency to32MiB, at the cost of a shorter finite repeat. It measured
428.0/428.6/427.5FPS against382.6/381.8/382.8FPS controls. No other material
bakes are proposed: the earlier all-cached-path diagnostic provided no
incremental benefit in this grass-dominated view.

Fog has an independent avoidable color copy/read. Direct RGB alpha blending
with BlitMode.Simple preserves the depth/fade/color formulas and destination
alpha. Matched full-resolution images passed the declared difference gate
(mean0.001712/255,p99zero); timing improved about5.5%. The shader remains one
postprocess owner for terrain, water, future objects and background.

Reducing shadow-map resolution alone saved little. Two cascades at4096 with
split ratio0.9704 saved more. The ratio approximately preserves the first
cascade's reach for this camera while giving up distant shadow precision.
It is not universal equivalence across all camera/FOV/shadow-distance settings.
Official upstream split-source evidence is pinned to880def129dc3900d830233cf62678671033f1267;
the installed build is26.09.15, so runtime checks remain decisive.

The three changes together screened at494.43FPS versus382.58FPS pooled
fully warmed controls (+29.23%); whole-frame GPU2.29->1.57ms (-31.44%).
GPU residency1601.5->1473.2MiB: shadow/fog savings exceed the32MiB texture
increase. The265.6FPS first restored sample lacked the required warmup and
is preserved, flagged and excluded from the matched gain. Repeated warmed
controls return382-383FPS. Hot process memory is contaminated by many
image imports and is not a cold memory qualification. Close visuals, cold
startup and canonical route acceptance are still pending.

The native compile tool intermittently reports that a mounted source path is
absent immediately after reload, despite the file existing on disk. Failed
responses and restoration failures are preserved. The external orchestration
now reads editor status and retries only this exact pre-compilation failure
(up to three attempts, unchanged source,8seconds apart); actual shader compiler
errors are not retried. Both source restorations are attempted independently.
No runtime project hooks or tooling changes were added for the experiments.

Integration ownership: Tools/bake_grass_pattern.py owns the offline period2
and2048 source-texels-per-tile configuration. It generates four4096 PNGs,
a source/output hash and dependency-version manifest, and a scalar HLSL
period include consumed by the one terrain shader. Outputs live in
Assets/textures/terrain/grass_pattern. Original Grass004 art is unchanged.
Each image is replaced atomically; this tool is an asset build step, never
runtime terrain work. Rebuilding should reproduce the screened2x2 bake
byte-for-byte on the recorded NumPy/Pillow versions before integration.
The material compiler still owns BC7 packing/mips; no runtime invalidation
or cache jobs are introduced. World edits continue through the existing field.

## Retained result

The final implementation keeps nearby color, normal, roughness and AO channels,
material blending, 16x filtering, shadows and the existing draw distance.
Grass's periodic patch blending is evaluated during asset creation. Fog blends
directly into scene color without a copied backbuffer. The scene spends fewer
shadow passes on distant geometry. Other terrain materials retain their existing
sampling; parallax remains disabled as previously requested.

| Matched comparison | Original | Optimized | Change |
| --- | --- | --- | --- |
| v2 original-facing standstill screening | 382.58 FPS | 494.43 FPS | +29.23% |
| v3 fresh-session standstill | 471.10 FPS | 563.97 FPS | +19.71% |
| v3 canonical figure-eight moving | 519.18 FPS | 589.80 FPS | +13.60% |
| v3 moving p99 frame time | 5.56 ms | 5.36 ms | -3.57% |
| v3 peak GPU memory | 1639.14 MiB | 1464.10 MiB | -10.68% |
| v3 peak process memory | 4659.47 MiB | 4693.93 MiB | +0.74% |

These views have separate baselines. Cold editor startup resets the first-person
look direction; the public native setter cannot restore it. v3 therefore uses
a separately declared orientation, identical in its baseline/candidate runs.
It is not evidence that the optimization alone turned the old300FPS observation
into564FPS. v2 also follows an earlier route that repacked geometry into fewer
arenas. No gain is calculated against that incomparable300-330FPS observation.

The v3 nominal20%standstill target is narrowly missed; it is not rounded into a
pass. The original-facing v2 gain exceeds that target, while all unchanged
route regression/correctness gates pass. This is a justified retained improvement,
not a claim of consistent600FPS or a universal percentage across views.

Both fresh routes completed with zero gameplay exceptions/collision failures.
Observed full settling improved5.9->4.8seconds. Managed allocation per frame
fell4.56%; more frames made total allocated bytes rise8.42%, within the existing
10%gate. Total GC pause rose6.00%. Exact inputs and all other measurements are
in the [ledger](../ValidationResults.md) and
[evidence index](../ValidationEvidence/TerrainStandstillOptimization/README.md).

Paired images preserve close texture/normal detail, dirt/stone transitions,
character shadows, distant terrain and fog. Grass's particular pattern changes
and has a finite repeat. Far shadows have lower precision; first-cascade
coverage is approximately preserved for the tested FOV/shadow distance, not all
possible settings. The view named grazing-grass actually faces the existing dirt
cut, so it does not establish an additional pure-grass grazing-angle test.

The final source and generated assets loaded successfully in a fresh visible
editor. The previous crash marker did not advance, and fresh logs have no project
shader/parser/pipeline failures or managed exceptions. Both control and candidate
logs contain the same stock missing-content and blue-noise resource errors.
Normal editor shutdown also reproduced a native mimalloc double-free Error dialog
in an original-only session; that teardown defect remains unresolved and is not
presented as fixed by these rendering changes.

Rebuild grass with `python Tools/bake_grass_pattern.py` using NumPy2.3.5 and
Pillow12.3.0, then compile the terrain shader and full material. The retained
production baker reproduced every screened4096PNG hash. No runtime baking,
second terrain renderer, extra depth draw, experimental toggle or diagnostic
feature removal remains.
