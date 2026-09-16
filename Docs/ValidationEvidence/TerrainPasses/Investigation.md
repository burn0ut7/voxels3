# Terrain rendering investigation — 2026-09-16

The largest measured GPU opportunity is terrain color shading, followed by
depth and shadow rendering. Two further experiments were measured through the
playable world's canonical figure-eight. See the experiment decision below.
Parallax remains disabled.

## Measurement boundaries

Scenario: `TERRAIN-PASS-001/v1`, defined in
[the validation ledger](../../ValidationResults.md#terrain-pass-001v1-2026-09-16).
RTX 5090, Ryzen 9800X3D, engine 26.09.15, source HEAD `a8d73fe` plus the
existing uncommitted project work recorded in the ledger. The one-player world
uses seed 1337, generator 48, visual radius 256, LOD 0–5, gameplay radius 8,
and the unchanged 2500 speed / 50000 distance / one-loop route.

Game measurements rendered at 2769 × 1529. The live third-person camera was
19.1256351, -96.2428894, 0 with horizontal FOV 75. This differs from the earlier
session's camera: older 391–414 FPS results are not a valid direct comparison.
The first control's starting camera was not logged; its endpoint matched the
subsequent recorded camera. This limits small effect-size claims.

Native GPU timing was enabled with `overlay_gpu 1`, then disabled for all
candidate comparisons. The profiler-on route measured 301.42 FPS versus
344.60 FPS without it; these are instrumentation states, not an optimization.
Allocation sampling is likewise affected by diagnostic overhead.

Native GPU timing activation is verified in Facepunch's
[DebugOverlay source](https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Systems/Render/Debug/DebugOverlay.cs).
Pass snapshots below are smoothed values at the end of a window, not route-wide
averages. Parent and child timings are not summed together.

## Where the GPU time goes

The stationary endpoint of the profiled route contained these native scopes:

| Work | GPU time |
| --- | ---: |
| Terrain color drawing | 1.438 ms |
| Large depth prepass | 0.470 ms |
| Four opaque directional shadow cascades, summed | 0.621 ms |
| Distance fog | 0.150 ms |
| Managed work after depth prepass | 0.072 ms |
| Hierarchical depth preparation | 0.066 ms |
| Tone mapping | 0.064 ms |

Depth and shadow scopes include scene work; the capture does not isolate every
object's contribution. These costs cannot all be attributed to texture reads.
At the moving-route endpoint terrain color was 0.489 ms and translucency
0.302 ms. The latter's recorded decaying maximum was 7.37 ms, but this does
not establish that water caused a specific long frame.

Independent near/horizon camera snapshots are retained in
[near-profile.json](near-profile.json) and
[horizon-profile.json](horizon-profile.json). Their actual ejected viewport was
2769 × 1391, despite a requested game resolution; do not use them as canonical
FPS comparisons. Their aggregate GPU display stayed at 3.93 ms, so interpretation
uses the populated individual scopes rather than that aggregate.

## Frame pacing and CPU work

The unprofiled control measured 344.60 moving / 282.29 stationary FPS, with
moving GPU time 2.269 ms. Moving p95/p99 frame time was 6.25/11.03 ms and the
worst frame was 105.25 ms. That worst frame is not explained by the averages.

The run allocated 95,221 managed bytes per frame on average, with a 7.01 MB
maximum frame. GC pauses totalled 433.77 ms over the approximately 122-second
route; the longest individual GC pause was 12.41 ms. This is worth investigating
for smoothness, but does not explain the entire 105 ms frame.

Recorded CPU stage maxima included placement preparation 10.61 ms, synchronous
streaming work 9.54 ms, VoxelManager.OnUpdate 11.33 ms, pending-mesh processing
3.87 ms, and collision creation 2.36 ms. These maxima need not occur in the same
frame and must not be added together. Named script timings are a final 200-frame
snapshot, not whole-route averages or a timestamped explanation of the worst
frame (`Code/Voxels/VoxelPerformanceProfiler.cs`). No per-script allocation
attribution is present; allocation numbers cover the runtime as a whole.
GPU callback waiting reached 814.32 ms;
that is asynchronous completion latency, not evidence of an 814 ms CPU stall.

The control finished with all 4,913 desired collision chunks ready, no pending
collision work, no collision failures, and no recorded exceptions. Average
visible chunk/draw count was 480.76, versus 2,987.80 culled; maximum visible
count was 1,270. Process/GPU peak memory was 5.504/2.181 GB (decimal).

The system snapshot showed the GPU at 68–69 Â°C, P0, 2880 MHz graphics,
14001 MHz memory, and 426–435 W. A brief GPU-engine sample attributed 87.74%
to s&box, 4.13% to the compositor, 3.02% to ChatGPT, and 1.58% to wallpaper.
This does not show a large competing game; it is not a complete throttle audit.

## Experiments and decisions

| Variant | Moving FPS | Stationary FPS | Moving GPU ms | Decision |
| --- | ---: | ---: | ---: | --- |
| Current shader, native profiling off | 344.60 | 282.29 | 2.269 | Control |
| Analytic AABB/frustum support test | 336.50 | 279.82 | 2.318 | Reverted |
| Earlier distance fade, 24–48 m | 340.60 | 271.92 | 2.291 | Reverted |

The frustum experiment replaced eight corner transforms with a center and three
axis transforms plus conservative plane support tests. Both visibility shader
consumers compiled. The fixed near images were pixel-identical; horizon
differences were negligible. Nevertheless it was 2.35% slower on the moving
route and provided no measured gain. Fewer source operations did not justify
keeping it. The original shared helper and both consumers were restored.

The second experiment shortened the existing 32–64 m texture fade to 24–48 m.
It retained full texture detail within 24 m but visibly flattened exposed stone
in the middle-distance comparison. It showed no measured performance gain, so
the exact original shader was restored. Its moving p95/p99/max frame times were
6.15/11.38/103.43 ms; zero exceptions and collision failures, 4,913 chunks ready.
Process peak memory rose to 6.803 GB after repeated hot compilation, versus
5.504 GB in the initial control. This exceeds the acceptance memory threshold;
the measurements do not establish that a distance constant caused the increase.
No candidate runtime change is retained.

The exact discarded edits are preserved as
[frustum patch](rejected-frustum.diff) and [fade patch](rejected-fade.diff).

See [fade raw record](fade.json.gz),
[original middle-distance image](fade-control-mid.png), and
[earlier-fade image](fade-candidate-mid.png).

A [restored-control repeat](control-repeat.json.gz) reported 364.92 moving /
315.54 stationary FPS, but is **not accepted as a clean performance comparison**.
The native log places route start at 16:23:17.719 and automatic shader compilation
completion at 16:23:18.189. The run recorded a 1658.21 ms maximum frame and
93.78 ms maximum GPU frame. The overlap invalidates its tail-latency comparison;
the aggregate does not timestamp the worst frame, so the compiler is not proven
to be its sole cause. Its higher FPS is not credited to any optimization.
Future runs must wait for file-watcher recompilation to finish after the explicit
compile and complete a settled warmup before starting. This run is preserved
rather than silently discarded. No change is accepted on its basis.

Raw complete records: [control](control.json.gz),
[profiled control](profile-control.json.gz), [frustum](frustum.json.gz).
Visual evidence: [near control](control-near.png),
[near frustum](frustum-near.png), [horizon control](control-horizon.png),
[horizon frustum](frustum-horizon.png).

## Prioritized opportunities

1. **Reduce color shader work while preserving near detail.** The accepted shader
   already packs four material channels into two maps and skips negligible
   projection/patch contributions. Near pixels can still require three
   stochastic patches on each of three projections, with two reads per patch
   per participating material. Distance simplification already reduces distant
   materials to two coarsest-mip reads. The measured color scope is the largest
   target, but removing all its work is neither feasible nor a visual equivalent.
2. **Reduce depth/shadow submission and geometry cost.** Their combined endpoint
   scope was about 1.09 ms. The existing implementation already batches visibility
   dispatch per view. A previous shared indirect-argument-offset experiment lost
   character shadows and is not a safe optimization to reintroduce. The public
   API's immediate-render path constrains further batching; command-list indexed
   indirect drawing cannot simply be substituted into that callback.
3. **Trace long frames and allocation ownership.** Correlate frame spikes with
   streaming, GC, publication and native presentation events. Existing aggregate
   counters identify candidates, not a proven cause. Prioritize frame pacing
   alongside average FPS before adding heavy trees, simulation and weather.
4. **Attribute transient translucent costs before editing water.** The water
   renderer uses per-chunk custom objects and shared render state. Managed engine
   source does not establish native default render flags. Changing pass flags or
   claiming duplicate water draws without a measured invocation trace would be
   premature.
5. **Treat fog as a smaller bounded target.** Its approximately 0.15 ms endpoint
   scope offers less headroom than terrain shading or depth/shadows. Preserve
   the engine's projection handling, including oblique cameras, if simplifying it.

500–600 FPS means about 2.00–1.67 ms for the whole frame. These observations
identify where to work; they do not establish that this view can reach that
target while retaining every current visual feature.

## Log audit

A bounded search of the latest three editor logs found no presentation-wait,
device-lost, GPU-hang or out-of-memory warning in the current session log or the
preceding shutdown log after 15:30 local. The current log does contain a startup
`textures/dev/blue_noise_256.vtex` invalid-header error at 15:45:05 and several
missing stock resources at 15:45:22–23. No temporal association with a measured
long frame was established. The preceding session logged a terrain-player prefab
destruction assertion at 15:44:14, then `Source2Shutdown` at 15:44:16. An older
12:20:50 presentation-wait warning belongs to an earlier session and does not
explain this session's 105 ms frame.

Selected native events are preserved in [editor-events.log](editor-events.log),
including the compilation overlap. On the final normal shutdown the existing
prefab-destruction assertion recurred and the saved, stopped editor remained at
an error window after `Source2Shutdown`. Only that identified process was
terminated before relaunching a visible editor. This is a failed clean shutdown,
not evidence of a shader-parser crash.

The fresh editor remained alive with a successful managed compile and no matched
terrain shader/parser/pipeline/dispatch or managed exception failure in its fresh
log. The Sentry crash marker stayed `2026-09-16T16:21:58.915039Z`. The restored
color shader SHA-256 is
`17D02D4409B16BB0E8D8BD0B87B47DE25F47DA7290ADB087BCE8DE5177EF2A2E`.
The scene's original task-start bytes were preserved. Native profiling is off;
saved visual radius 128 / maximum LOD 4 and unconstrained viewport are restored.

[Fresh rendered terrain](restored-cold.png) was inspected. The
[initial startup snapshot](restored-cold.json) still had streaming work; the
[subsequent settled snapshot](restored-cold-settled.json) recorded zero visual
and transition queues, the player grounded, and all 4,913 collision chunks ready
with zero failures. This verifies restoration, not a new cold performance gain:
the saved camera and viewport differ from the earlier comparison. No new runtime
optimization was accepted in this investigation.
