# Volumetric cloud layer

Implemented in the playable scene. Drift, parallax, coverage controls and resource
recreation have been exercised. The user's unnatural-shape report prompted an
[independent visual review](../ValidationEvidence/VolumetricClouds/IndependentVisualReview.md).
Candidate AG passes its four fixed still-image criteria: coherent bodies without
long internal grooves, asymmetric secondary lobes, varied group spacing and gradual
distant fade. Short rims and broad smooth interiors remain visible polish limits.
Temporal qualification remains pending; the current matched performance pair fails
FPS, standing frame-tail/allocation and streaming-drain requirements;
this is an active development candidate, not a fully realistic atmosphere claim.
[Research](../Research/VolumetricClouds.md) owns external evidence and alternatives.
[Validation](../ValidationResults.md) owns fixed scenarios and measured results.
The [current sunward view](../ValidationEvidence/VolumetricClouds/shape-ag-sunward.png)
and [clear horizon view](../ValidationEvidence/VolumetricClouds/horizon-ag-clear.png)
record the accepted still-image appearance and its remaining limitations.

## First-slice contract

One `VolumetricClouds` camera postprocess owns a world-space cloud layer. It has
no dependency on terrain density, chunks, collision, saves or networking. Inputs
are layer base/thickness and base-height variation in meters, shape scale, coverage, density, erosion,
wind in meters/second, a scene directional light, fog color and bounded quality
settings. A later weather controller may drive these appearance inputs; no
weather transitions, precipitation, lightning or synchronized weather clock are
part of this implementation.

Clouds are a participating volume, integrated along view rays through a bounded
altitude interval. The supported first slice is viewing the sky from below the
layer, with the layer above terrain. Immersive flight, clouds intersecting
mountains, reflection cameras and transparent geometry without depth writes
are not acceptance claims. The component should not throw when moved above the
layer, but that is not a promise of a complete flight-cloud renderer.

## Density and lighting

`Tools/bake_cloud_noise.py` generates deterministic periodic 3D base and erosion
noise into byte assets. Texture data is linear, x-fastest, then y, then z. The
base volume is128 cubed R8; erosion is32 cubed RGBA8. At runtime each component
uploads three immutable textures once: these two3D volumes and a256-square RGBA8
layout.
No CPU noise generation or recurring upload.
`voxels3.sbproj` explicitly includes `textures/clouds/*.bin` as loose resources;
arbitrary binary files are otherwise absent from the recognized-asset packaging
path. The offline baker and manifest are development artifacts, not runtime inputs.

The offline visual seed41873 is independent of terrain generation. Six-by-six
periodic jittered cloud groups have elliptical coverage envelopes, constant
individual elevations, and a separation mask. The layout RGBA channels store
coverage/elevation/separation/unused. The shader owns its repeat distance: five
base-noise periods,12km at defaults. One layout sample determines coverage and
shifts both the local height profile and 3D shape by up to250m above or below the
nominal base. A gap between groups masks elevation interpolation. This replaces
the previous continuously warped base field, which produced folded undersides;
it is not an additional renderer or selectable compatibility path.

The density sampler combines this layout with a height profile, connected
Perlin/Worley base shape and higher-frequency erosion. Base noise contains
frequencies4/8/16 with weights0.40/0.40/0.20 for both Perlin and Worley signals. Its dilated signal is remapped from0.35..0.95 before coverage
thresholding; the height profile is applied before that threshold to give lobes
varied tops. The outer trace interval expands by the same base-variation bound;
height-dependent lighting uses the local base. The variation is clamped so the
lowest base remains at least100m above world zero. An independent fixed bake seed42175 gives each layout group a35% chance of being clear, reducing
the uniform population of overlapping cloud bodies. Shape and distribution pass the recorded still-image criteria.
Erosion repeats8 times per base period. Its RGBA channels contain full4/8/16
frequency fBm, the same field with frequency16 replaced by its mean, only
frequency4 plus the higher-band means, and a constant0.5. The fields are centered
around0.5 and clipped during the bake. One lookup is blended across these bands
using the actual feature wavelengths and traced pixel footprint, then converges
to0.5 when every band is unresolved. This preserves broader breakup after fine
detail becomes subpixel; temporal stability remains under qualification.
Coordinates are in meters; `VolumetricClouds` owns the inch-to-meter conversion
passed to its shader. Wind advects the sampling position. The pattern is fixed
in world space rather than centered on the player.

Light transmittance uses four bounded density samples toward the scene sun;
the nearest two include fine erosion and the other two approximate mean erosion.
Front-to-back integration uses exponential extinction and analytically
integrated step opacity. Dual-lobe angular scattering, a relaxed attenuation
term and height-dependent ambient fill approximate soft cloud lighting. The
angular response is bounded to avoid a clipped rim without filmic exposure; these
are artistic approximations, not a physical atmosphere simulation. Aerial
perspective blends toward the existing far-fog sky color with
1-exp(-(representativeDepth/9000m)^2); cloud contrast falls before the24km trace
boundary while nearby clouds retain definition. The existing final40% range
fade remains a bounded-end safeguard.

## Rendering and ownership

Cloud tracing writes premultiplied color and opacity into a reduced-resolution
RGBA16F temporary target. The engine resolves its size per render view and owns
the target pool. The command list returns to the scene target using the public
`ClearRenderTarget` before compositing and releases the temporary target after use. There is no backbuffer
copy, CPU readback or temporal history.

Installed XML includes internal `PushRenderTarget`/`PopRenderTarget` methods,
but ordinary game compilation rejects them. They are not available integration
APIs. The public target-reset path must be verified in the real scene and camera
capture; arbitrary nested custom render targets are outside this slice.

The composite runs after `DistanceFog` (BeforePostProcess order 10, fog order 0)
and before tone mapping/UI. Full-resolution scene depth masks geometry so sky
clouds cannot paint over terrain, trees or the player. Tracing is independent
of low-resolution foreground depth, preserving sky visible through thin leaf
gaps. This deliberately limits the first slice to a sky layer; it does not
provide correct partial integration against an object inside the layer.

The component owns its three runtime textures and material references. Disable
releases owned textures and re-enable rebuilds them. Rendering/texture creation
stay on supported engine callbacks; no jobs or asynchronous results exist.
Per-render command lists follow the engine postprocess lifecycle and prevent
mutable targets from being shared across simultaneously queued views.

Coverage zero returns before resource creation/draw recording. Rays outside the
layer return transparent. Rays stop at the layer exit, maximum trace distance,
sample limit or near-opaque transmittance. Midpoint integration uses at least
the configured view samples, refining long horizon rays toward one sample per
shape-scale/96 meters or traced pixel footprint, capped at 256. Spatial random
ray offsets were removed after they produced visible distant stippling without
temporal reconstruction. Shader inputs are finite and clamped.

## Scale and budget

Current defaults: base 1,000 m with up to +/-250 m variation, thickness 900 m,
base-noise period 2,400 m,
coverage 0.32, extinction multiplier 0.08/m, erosion 0.22, wind (8, 3) m/s,
maximum trace distance 24 km, at least 64 view steps (up to 256 for long rays),
four sun steps, quarter width and height. Ledger scenarios freeze their own
inputs; final visual and performance qualification may require further changes.

Noise and layout payload is 2,490,368 bytes before engine overhead. At 1920x1080, the
quarter-dimension RGBA16F target has 129,600 texels (1,036,800 bytes); at
2769x1529 its exact size follows engine rounding. CPU work is bounded parameter
setup and command recording. Proposed additional GPU goals are <=1 ms mixed
view and <=2 ms full sky at 1080p, without overriding the repository's existing
relative performance gates. Actual acceptance requires measurements.

## Alternatives and upgrade boundary

Full-resolution ray marching spends excessive work on a smooth sky signal.
Reduced-resolution tracing is the first cost control. Temporal reconstruction
could improve detail/cost, but needs per-camera history, cloud motion, depth
rejection and camera-cut handling; it is not silently approximated by blending
old screen images. Adopt it only if spatial reconstruction fails quality/cost.

Authored voxel cloud fields, SDF skipping and cached lighting support immersive
cloud environments but add content pipelines and mutable caches. They are
deferred. Ordinary clouds and future storm appearances must continue through
one density/lighting renderer rather than duplicate implementations.

## Engine integration evidence

The first slice uses the engine's normal postprocess build/command-list lifecycle.
[BasePostProcess source at caae4cf](https://github.com/Facepunch/sbox-public/blob/caae4cf07bc4a0e2ab6f1e0ab08bdfbb751353c6/engine/Sandbox.Engine/Scene/Components/PostProcessing/BasePostProcess.cs)
clears per-effect attributes on each build and attaches each command list to a
camera postprocess layer. That published source declares no OnDisabled override;
installed26.09.15 compilation and live rendering establish the used public API
surface locally. Source inspection is not a substitute for lifecycle validation.

The noise payload inclusion follows the publisher's asset-root wildcard path in
[PackageManifest at the same revision](https://github.com/Facepunch/sbox-public/blob/caae4cf07bc4a0e2ab6f1e0ab08bdfbb751353c6/engine/Sandbox.Tools/Utility/ProjectPublisher/PackageManifest.cs).
Its Resources wildcard is separate from recognized asset collection; .bin is an
allowed loose extension. Published-package execution remains unverified until
an actual packaged build is run.

