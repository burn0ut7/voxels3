# Volumetric clouds: production research and first slice

Research date: 2026-09-19. This document preserves the research and original
proposal; [the cloud architecture](../Architecture/VolumetricClouds.md) owns the
implementation and current qualification status. User scope: attractive,
performant volumetric clouds now; weather
orchestration in a later slice. The starting assumption is ground-view clouds;
flight through clouds is a separate product requirement to confirm.

## Recommendation

Build a small, independent cloud renderer using a world-space density layer,
baked tileable 3D shape/erosion noise, sun transmittance, approximate multiple
scattering, reduced-resolution tracing and depth-aware composition. Keep broad
coverage and vertical shape separate from fine erosion. Preserve a narrow set of
continuous appearance inputs that a future weather controller can drive.

This is a ground-view application of the production techniques below, not a
promise of Horizon image quality or its published timings. Do not begin with a
fluid simulation, terrain chunks in the sky, an entire atmosphere replacement,
or the asset-authoring pipeline of Nubis Cubed. Those solve larger problems than
the first slice requires. Equally, a noisy transparent plane would not fulfill
the requested volumetric appearance.

## Primary-source findings

Page numbers below are one-based PDF pages, not the occasionally different
slide numbers printed in the footer. PDFs were downloaded and text extracted;
selected modeling, reconstruction, lighting and performance diagrams were also
rendered and inspected. Embedded presentation videos were not reviewed.

### Horizon Zero Dawn, 2015: the useful foundation

The [user-supplied Schneider/Vos presentation](https://d3d3g8mu99pzk9.cloudfront.net/AndrewSchneider/The-Real-time-Volumetric-Cloudscapes-of-Horizon-Zero-Dawn.pdf)
separates large-scale shape from small-scale erosion. Perlin-Worley supplies
connected billows, height profiles distinguish cloud types, and coverage controls
distribution. It uses 128-cubed base noise, 32-cubed detail noise and a 2D curl
texture (pp. 30-40). Type, coverage and precipitation are weather-system inputs,
not consequences of the ray marcher (pp. 41-45).

Empty regions receive cheaper samples; entering a cloud switches to finer
sampling with a backward step. The maximum work is described as 64-128 view
samples and six cone-light samples, with early exits (pp. 77-92).

The critical qualification: the presentation reports about 20 ms before its
reconstruction optimization. Updating one sample in each 4-by-4 block and
reprojecting history, with fallback for missing history and a further
reduced-resolution rendering/upscale arrangement, brings its target to about
2 ms (pp. 93-95). That is not a full-resolution shader timing we can inherit.

**Decision:** adopt the separation of shape, erosion, coverage and lighting.
Reject copying the sample counts or the 2 ms claim as a Voxels3 budget.

### Nubis Evolved, 2022: motion is part of reconstruction

[Nubis Evolved](https://advances.realtimerendering.com/s2022/SIGGRAPH2022-Advances-NubisEvolved-NoVideos.pdf)
extends the system to skies, cloud environments and superstorms. Its reprojection
examples account for cloud motion, including rotation around a storm and ordinary
scrolling, before finding the previous sample position (pp. 155-157). Camera-only
reprojection is insufficient for rapidly moving density.

The VFX scaling table distinguishes PS4/PS5 maximum resolutions of 960x540 and
1920x1080, six/ten light samples and 60-90/96-180 view samples (pp. 183-184).
These are context-specific settings, not universal recommendations for skies.

**Decision:** if temporal accumulation is added, its contract must include wind
motion, camera discontinuities and history rejection. Do not just average the
previous screen image. Storm rotation, lightning and internal emission remain
future weather/VFX work.

### Nubis Cubed, 2023: immersive clouds change the representation

[Guerrilla's overview](https://www.guerrilla-games.com/read/nubis-cubed) explains
the move from height-profile cloudscapes to authored voxel clouds for aerial
gameplay. The [technical PDF](https://d3d3g8mu99pzk9.cloudfront.net/AndrewSchneider/Nubis%20Cubed.pdf)
uses coarse modeling fields plus runtime detail, rather than storing all final
density at high resolution (pp. 81-101).

Two nearby lighting samples retain local detail; a separate 256x256x32 lighting
volume replaces farther light marches. The reported savings are about 40%, with
lighting updates amortized over eight frames (pp. 129-131). Compressed SDFs skip
empty regions; distance-dependent steps and noise mip levels reduce work farther
away. Compression errors that overestimate safe distances can skip real clouds
(pp. 163-174).

The deck's per-cloudscape representation comparison is 0.541/9.437/25.166 MB
for vertical-profile/envelope/voxel methods; this is not total renderer memory
(p. 170). One full-cloud example costs 4 ms at 960x540; splitting near/far work
between 480x270 and 960x540 reduces it to 2.1 ms for higher frame-rate modes
(pp. 182-183).

**Decision:** defer authored voxel fields, SDF compression and a lighting volume
until immersive flight or measurements justify their storage/update complexity.
Do not assume every modern implementation requires temporal upscaling.

### Frostbite: why clouds look like clouds rather than smoke

[Hillaire's technical notes](https://media.contentapi.ea.com/content/dam/eacom/frostbite/files/s2016-pbs-frostbite-sky-clouds-new.pdf)
describe density/type maps, height-dependent erosion, compact single-channel
noise, four progressively spaced sunlight samples and temporal integration
(pp. 33-36). Analytical integration over each step reduces lighting dependence
on step length. A dual-lobe phase function retains useful backlighting and
frontlighting; an approximate multiple-scattering sum brightens thick cloud
interiors (pp. 38-42).

Cloud depth also matters for atmospheric haze. The notes use an accumulated
representative cloud depth to apply aerial perspective, and explain that heavy
cloud cover should eventually affect atmosphere lighting too (pp. 43-44).

Reported Xbox One main-view cost is 1.60 ms for a 1080p output, with clouds traced
at half resolution, 16 samples per pass and two scattering orders (p. 45). That
configuration differs from Horizon's; the figures cannot rank the renderers.
The conclusion explicitly identifies the cloud technique as not yet shipped at
publication time, unlike other Frostbite atmosphere systems (p. 50). The linked
notes extend the SIGGRAPH 2016 material and include later game references.

**Decision:** use transmittance-based integration, a modest multiple-scattering
approximation and consistent horizon haze. Defer cloud shadows on terrain,
global illumination changes and a full physical atmosphere.

### Unreal Engine: expose independent cost controls

[Epic's UE 5.6 cloud documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-cloud-component-in-unreal-engine?application_version=5.6)
documents approximate scattering orders, ray-marched volume shadows versus
cheaper Beer shadow maps, and distinct reconstruction modes. Mode 0 traces at
quarter resolution and reconstructs at half resolution before the final upscale;
Mode 1 traces at half resolution and reconstructs at output resolution. Its
discussion also makes opaque-object intersection support a mode-dependent issue.

[Component controls](https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-cloud-component-properties-in-unreal-engine)
separate layer bounds, maximum tracing distances, view/reflection/shadow sample
scales and transmittance termination.

**Decision:** separate view quality from lighting quality. Limit trace distance,
sample count and transmittance explicitly. Reflections require their own budget;
do not quietly ray march clouds for every auxiliary view.

### Unity HDRP: a concrete reconstruction implementation

Unity's public
[low-resolution setup](https://github.com/Unity-Technologies/Graphics/blob/a7e4c051d256a781ab362c64316b125a1e104694/Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/VolumetricClouds/HDRenderPipeline.VolumetricCloudsLowResolution.cs)
assigns 0.25 to trace dimensions and 0.5 to intermediate dimensions. Thus quarter
width and quarter height mean one-sixteenth as many pixels, not one-quarter.

The [reconstruction shader](https://github.com/Unity-Technologies/Graphics/blob/a7e4c051d256a781ab362c64316b125a1e104694/Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/VolumetricClouds/VolumetricClouds.compute)
validates previous UVs, sample history and scene depth, compensates exposure and
optionally clips history to the current neighborhood. It falls back to spatial
reconstruction when history is invalid. The final upscale considers full-resolution
scene depth and distinguishes sky from geometry.

The [trace shader](https://github.com/Unity-Technologies/Graphics/blob/a7e4c051d256a781ab362c64316b125a1e104694/Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/VolumetricClouds/VolumetricCloudsTrace.compute)
clips rays to cloud bounds and opaque depth. Its
[density/lighting utility](https://github.com/Unity-Technologies/Graphics/blob/a7e4c051d256a781ab362c64316b125a1e104694/Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/VolumetricClouds/VolumetricCloudsUtilities.hlsl)
shows density/erosion controls, separate light steps, empty-space skipping and
transmittance termination. Source links are pinned to Unity Graphics commit
`a7e4c051d256a781ab362c64316b125a1e104694`, fetched on the research date.
This is not proof of an exact installed HDRP release or code we can copy into
s&box unchanged.

**Decision:** temporal reconstruction is a subsystem with ownership and
invalidation rules. First establish a correct bounded spatial result; introduce
history only if the measured quality/cost tradeoff warrants it.

## What the comparison establishes

There is no public, matched benchmark that makes one of these systems the
universal winner. Strong implementations allocate effort by viewing conditions:

| Requirement | Best fit for this project | Main tradeoff |
| --- | --- | --- |
| Attractive sky from ground/mountains | Procedural 3D layer with broad coverage and height profiles | Limited arbitrary vertical structure |
| Very cheap distant wisps | Optional later 2D high-cloud layer | Cannot substitute for nearby volumetric cumulus |
| Flight through authored cloud formations | Coarse voxel modeling fields, detail reconstruction, empty-space acceleration | Content pipeline, lighting caches, memory and transitions |
| High output resolution | Reduced-resolution trace plus careful spatial/temporal reconstruction | Edge artifacts, ghosting and history ownership |
| Stormy visual character | Coverage, vertical shape, density, erosion and illumination together | Dark tint alone does not provide storm structure |

Broad composition, readable billows, lit edges, shaded bases and stable motion
should be established before adding tiny noise detail. The final judgment needs
moving in-world evidence, not only a still screenshot.

Here, a height-profile or "2.5D" model still renders a real 3D participating
volume. The restricted part is its large-scale authoring representation. It does
not mean a flat cloud image or a billboard, and it does not automatically rule
out entering the layer. It does limit what shapes and immersive views can be
represented and reconstructed convincingly.

Rockstar's [2019 atmosphere presentation abstract](https://advances.realtimerendering.com/s2019/index.htm)
also describes an integrated cloud/fog, reflection and sky-irradiance solution.
Only the abstract was inspected; no particular RDR2 cache, sample budget or
algorithm is inferred from it. Searches of newer production material did not
establish a newer universally superior cloud architecture. This review therefore
compares the detailed, inspectable sources above, not a ranking of every current
game's visuals.

## Voxels3 integration constraints

Inspected source: `Code/DistanceFog.cs`, `Assets/shaders/distance_fog.shader`,
`Assets/scenes/basic_example.scene`; engine 26.09.15. The camera's authored far
plane is 100,000 engine units. Engine units are inches; cloud controls should be
authored in meters and converted in one owner. Do not duplicate the terrain's
coordinate definitions or store cloud density as authoritative terrain.

The current fog makes background depth completely opaque. Rendering clouds
before it would erase them. Rendering clouds afterward without checking scene
depth would put them over mountains, trees and the player. The first integration
must deliberately solve both ordering and depth composition. A background-only
mask can be a limited first sky view, but it is not evidence of correct near-cloud
intersections or fly-through support.

Installed/staging evidence exposes `BasePostProcess.InsertCommandList`, temporary
render targets with a viewport divisor, target push/pop, shader blits, and 3D
textures. Local XML confirms render-target lifetime/pooling contracts; shader
compilation and actual runtime behavior still require validation. Existing fog
proves only the simple fullscreen path. Do not infer temporal-history safety,
volume mip upload order or per-view resource isolation from an API name alone.

The editable workspace contains concurrent terrain, water, grass and tree work.
Historical performance results are not automatically comparable to this source
or world. Preserve their changes and establish the exact source/world identity
before a matched cloud-off/cloud-on comparison.

## Proposed first implementation contract

**Inputs:** coverage, layer base/thickness, horizontal shape scale, erosion,
extinction density, wind vector, scene sunlight and modest ambient fill. Start
with one attractive broken-cumulus configuration. All controls remain continuous;
no weather state machine, precipitation logic or network protocol in this slice.

**Ownership:** one local rendering component owns cloud material resources,
immutable noise textures and render attributes. GPU outputs are derived color
and transmittance. Cloud rendering must not schedule terrain work, mutate the
world, require collision, or add network traffic. Engine rendering owns execution;
the component must release its resources on disable/destruction and survive
hotload/re-enable. No per-frame CPU voxel generation or texture uploads.

**Density:** sample world-space coordinates so translation creates parallax and
the pattern does not follow the camera. Use a slowly varying coverage field,
height profile, connected base noise and finer erosion. Animate an offset, not
fresh random numbers per frame. A future weather system owns shared world
parameters and time; the initial local visual does not claim multiplayer weather
synchronization.

**Tracing/composition:** intersect the actual layer, cap distance, skip empty
regions, terminate opaque rays and integrate bounded sunlight samples. Keep
premultiplied scattering and transmittance consistent through filtering. Start
with spatial reconstruction at reduced resolution; retain full-resolution depth
for final edges. No mandatory history buffer in the first prototype. If quality
requires history, budget and implement per-camera double buffers, wind-aware
reprojection and resets for resolution/FOV/camera cuts before relying on it.

**Scale:** start with a 128-cubed R8 shape volume and 32-cubed R8 erosion volume,
approximately 2.03 MiB before mip levels/overhead. A 1920x1080 half-dimension
RGBA16F buffer is approximately 3.96 MiB; quarter-dimension is 0.99 MiB. These
are arithmetic resource estimates, not measured residency or approved settings.
Investigate 48-64 view steps and four light steps as initial bounded engineering
values; tune against quality and measured GPU cost before freezing acceptance.

**Budget proposal:** aim for <=1 ms added GPU time in a 1080p mixed terrain/sky
view and <=2 ms in a full-sky stress view, with negligible steady CPU/allocation
cost. These are proposed absolute goals, not measured results or exceptions to
the project's existing relative regression gates. At high baseline frame rates,
even 1 ms is a substantial slowdown. Any acceptance outside the existing gates
needs the evidence and explicit user approval required by the project rules.

## Qualification before calling it complete

Freeze exact scenarios in the [validation ledger](../ValidationResults.md)
before running them. Include source hashes, world revision, hardware, viewport,
FOV, camera transforms, cloud inputs, elapsed animation time and warmup/duration.

1. Matched cloudy/clear frames: sky coverage, coherent shapes, lit volume and
   horizon fade, with clouds behind terrain, trees and water silhouettes.
2. Look up, toward the horizon and away from/toward the sun. Observe stationary
   drift and camera translation/rotation for noise, banding and screen locking.
3. Exercise zero coverage, dense coverage, disable/re-enable and hotload. Inspect
   underground/occluded views for leaks and unnecessary cost. Test the layer
   boundary if that view is claimed; otherwise explicitly leave flight unqualified.
4. Run the unchanged canonical figure-eight with a fresh comparable cloud-off
   baseline if needed, then cloud-on. Record frame/GPU costs, p95/p99 pacing,
   completion/streaming, memory, allocations and correctness. Use matched fixed
   sky-heavy observations to expose worst-case rendering cost as well.
5. Preserve failures. A screenshot, successful build or reported console-era
   benchmark does not prove performance acceptance in this game.

## Scope left for weather

Weather later supplies transitions and spatial fields for coverage, type,
thickness, density and wind, together with appropriate lighting. Rain, lightning,
cloud shadows on the world, atmosphere feedback, day/night, synchronization and
save data are separate integration work. They should consume the same renderer
rather than introduce independent normal-cloud and storm-cloud implementations.
