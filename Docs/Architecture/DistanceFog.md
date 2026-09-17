# Two-color distance fog

`DistanceFog` owns a camera postprocess in `basic_example`. Its only authored
inputs are Near Color, Far Color and Start Fraction (0..0.95 of camera ZFar).
Defaults are blue (0.27, 0.49, 0.72), pale sky-blue (0.60, 0.74, 0.87), and 0.08.
Colors are opaque sRGB inspector colors, converted to linear RGB for blending.

The shader uses linear view depth divided by the rendered camera's far plane.
The far plane is reconstructed from the same projection as scene depth, so a
ZFar change is effective in that render, without a second distance setting or
per-frame camera lookup. This also avoids a spherical fade meeting a planar clip
boundary differently at the image corners. A smoothstep from Start Fraction to
1 drives opacity and interpolation between the two colors. Foreground remains
unchanged; depth at the far plane and background become exactly Far Color.

One fullscreen pass follows opaque/transparent scene rendering and precedes
tonemapping and the HUD. Terrain and current opaque, depth-writing water share
the same depth-driven fog. No terrain state, mesh, collision, networking or save
data changes. Transparent objects without depth writes are not individually
fogged by their own distance in this first iteration. No sky detail is preserved
at background depth; the existing scene has a flat camera background.

The engine postprocess system owns registration, command lists and enable/disable
lifecycle. The component owns its cached material and attributes; updates remain
on the engine rendering path, with no jobs or recurring collection allocations.
The pass uses `BlitMode.Simple` and hardware source-alpha RGB blending. It
outputs fog color and fade, reads scene depth once, and preserves destination
alpha with an RGB write mask. This removes the former backbuffer copy and color
sample while retaining the same depth reconstruction and two-color fade. There
is one fullscreen draw, no raymarching and no per-region work. Performance must meet the
existing figure-eight 5% FPS / 10% tail, allocation and memory comparison gates.

Built-in GradientFog exposes only one color. Per-material fog would duplicate
the responsibility across terrain, water and future materials and require a
separate background solution. A depth postprocess keeps this slice in one owner.
Physical volumetrics, height fog, light scattering and additional color stops
are outside this iteration.

API evidence: installed 26.09.08b `Sandbox.Engine.xml` BasePostProcess entries,
`core/shaders/common/classes/Depth.hlsl`, and the official
[custom postprocess example](https://sbox.game/dev/doc/rendering/post-processing/creating-postprocesses/).
Implementation and validation status are recorded in the
[validation ledger](../ValidationResults.md); source alone is not visual or
performance acceptance.

Qualification uses the terrain standstill optimization scenario. Its matched
image comparison, figure-eight results and
cold-start limitations belong to that ledger entry and
[investigation](../Research/TerrainStandstillOptimization.md).
