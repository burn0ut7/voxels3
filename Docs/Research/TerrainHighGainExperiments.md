# Terrain high-gain experiments

2026-09-17. Retained the earlier distant geometry LOD policy after matched
figure-eight runs, a return-control repeat and cold-editor qualification.
No diagnostic pass removal is retained.

## Outcome and ownership

The largest usable gain came from reducing distant geometry across color, depth,
and shadow rendering. The matched scene-default candidate measured **468.78 FPS
moving versus 365.70 (+28.2%)**, and **429.68 stationary versus 364.37 (+17.9%)**.
Nearby detail, materials, shadows and nominal viewing distance remain unchanged.
Distant silhouettes and riverbanks become coarser. This does not establish a
stable 500–600 FPS target or a guaranteed percentage in other scenes.

Source baseline is `cf5ec87`. The only retained runtime change is two values in
`Assets/scenes/basic_example.scene`: `LodCacheHalfExtent` 8 to 4 and
`MaximumVisualLod` 4 to 5. `Lod0VisualHalfExtent=4` and `VisualChunkRadius=128`
remain unchanged. The nominal reach is 65,536 world units, about 1.66 km;
coarsest cell spacing changes from 256 to 512 world units. Component defaults
are unchanged. Parallax remains disabled under the existing material policy.

One existing clipbox hierarchy, Transvoxel transition system, allocator and
visibility path own the derived geometry. No second renderer, mutable world
representation, extra occlusion depth pass, new runtime controls or cache is
introduced. The authoritative saved field remains revision 3893 throughout.
Checkpoint increments reflect orderly saves, not terrain mutations.

The rationale and current placement contract belong to
[GPU meshing](../Architecture/GpuVoxelMeshing.md). Exact scenario parameters,
all runs and acceptance decisions are in [the ledger](../ValidationResults.md).
Raw compressed results, captures, diagnostic patches and audits are retained in
[the evidence directory](../ValidationEvidence/TerrainHighGains/).

## Whole-pass diagnostic limits

Initial native GPU snapshots measured terrain color at 0.932 ms, large depth at
0.314 ms, and four terrain shadow scopes totaling 0.634 ms. These are smoothed
snapshots with profiling enabled outside timed routes. Overlapping parent and
child scopes must not be added, and instrumented FPS is not a route comparison.

The radius-256 investigation used the current saved field, fixed camera
Euler 18.086, 11.903, 0, resolution 2769 × 1529, and the existing figure-eight
at speed 2500, distance 50000, one loop. Full parameters are recorded as
`TERRAIN-HIGH-GAINS-001/v1`. All runs below reported zero runtime exceptions
and collision failures; visual validity is separate from those counters.

| Variant | Moving FPS | Stationary FPS | Moving p99 ms |
| --- | ---: | ---: | ---: |
| Original first baseline | 354.76 | 363.56 | 10.625 |
| No terrain shadows, diagnostic only | 405.99 | 446.46 | 10.540 |
| No depth submission, invalid all-sky image | 373.29 | 397.79 | 11.070 |
| Flat pixel shader, diagnostic only | 402.14 | 450.24 | 10.270 |
| Original adjacent control | 359.04 | 389.32 | 10.542 |
| Earlier distant geometry LOD | 437.82 | 444.49 | 6.665 |
| Original restored control | 326.92 | 341.89 | 11.741 |

Removing terrain shadows loses a required visual feature. Replacing pixel
shading with a flat gray output removes textures and lighting appearance.
Neither complete removal reached 500–600 FPS. Removing depth submission made
the inspected image all sky, so its timings are **not** a valid isolated depth
cost measurement. The dependency failure was preserved, not reclassified as a
successful optimization. All four temporarily modified shader/C# source files
were restored byte-for-byte and subsequently validated through a clean start.

A direct indexed depth submission could avoid the current triangle-corner
expansion. The inspected public API exposes indexed GPU buffer draws on
CommandList, but no public callback execution or Mesh GPU-buffer attachment was
verified. Installed XML/cached API and current upstream
[CommandList source](https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Systems/Render/CommandList/CommandList.cs)
show an internal render-thread execution method. No unsupported call or
reflection bypass was adopted. This evidence does not rule out every possible
future engine integration.

## Radius-256 geometry candidate

For this investigation only, near/cache extents 4/8 with maximum LOD 5 changed
to 4/4 with maximum LOD 6. Nominal reach remained 256 chunks, about 3.33 km.
Against the adjacent original control, moving FPS improved 21.9%, stationary
FPS 14.2%, p95 from 5.145 to 4.025 ms, p99 from 10.542 to 6.665 ms, and maximum
frame time from 95.30 to 36.95 ms. Arrival settled in 7.9 instead of 14.9 seconds.

Resident triangles fell from 7,334,960 to 2,159,719 (70.6%), vertices from
3,977,590 to 1,187,668, and persistent arenas from 14 to 6. Resident counts
are not the number of visible triangles rasterized every frame. Process peak
fell from 6.54 to 6.32 GB and reported GPU peak from 2.23 to 2.18 GB in that
pair. Resource retirement and session history prevent equating geometry bytes
saved with an immediate, equal reduction in GPU residency.

Separate GPU snapshots supported the direction: color 0.932 to 0.559 ms, large
depth 0.314 to 0.257 ms, and terrain shadow scopes totaling 0.634 to 0.413 ms.
These are diagnostic observations, not exact paired pass timings or additive
whole-frame savings. Original repeats drifted materially; selecting only the
slowest original would overstate the gain.

## Scene-default comparison

The saved scene uses radius 128, so `TERRAIN-HIGH-GAINS-SHIPPING-001/v1` was
predeclared separately. Cold-start camera Euler 0, 0, 0 differs from the earlier
view; input-space EyeAngles was not writable through the inspected native MCP
property route. Other recipe, route, resolution and safety parameters remain
fixed. Results from these two scenarios must not be combined as one series.

| Run | Moving FPS | Stationary FPS | Moving p95 / p99 ms |
| --- | ---: | ---: | ---: |
| Original valid baseline | 365.70 | 364.37 | 7.291 / 10.167 |
| Earlier distant geometry LOD | 468.78 | 429.68 | 3.893 / 6.221 |
| Original return, camera mismatch; excluded | 309.62 | 346.50 | 8.281 / 11.578 |
| Cold saved candidate | 469.71 | 402.38 | 3.876 / 6.107 |
| Original return, valid repeat | 317.34 | 338.74 | 7.570 / 10.665 |

The initial shipping-baseline attempt was disturbed by premature ejected
screenshots during the route. Its raw result (332.63 / 343.88 FPS) is preserved
and excluded from comparisons; the unchanged repeat above is the valid baseline.
Those early screenshots are also excluded from matched visual evidence.

The return-to-original run ended with camera Euler -0.231, 8.321, 0 instead
of 0, 0, 0. Its exact change time is unknown, so it is excluded from matched
comparisons. It also included a 2439.59 ms frame, while maximum GPU time was
11.657 ms and maximum GC pause 12.486 ms. No scoped warning identified the
stall's cause. These observations do not establish a terrain GPU regression or
session drift. Its final resident triangle count was 0.52% below the first
original despite matching configuration, source and end position. The valid
adjacent baseline/candidate pair preserved camera orientation. The unchanged
return-control repeat below completes the declared comparison.

In the adjacent candidate pair, resident triangles fell from 5,702,206 to
1,804,110 (68.4%), vertices from 3,059,024 to 981,477, and arenas from 12 to 6.
Post-loop drain fell from 14.09 to 6.93 seconds. Moving maximum frame time fell
from 75.89 to 30.84 ms. Stationary p95/p99 also improved, by 10.4% / 9.8%.
Process peak increased 2.3%, GPU peak decreased 2.4%, total managed allocations
increased 2.6%, and allocations per frame decreased 20.0%. GC total pause time
increased from 445.82 to 510.88 ms despite improved frame tails; this is retained
as a limitation of the single-run comparison, not hidden as an overall GC win.

## Visual and correctness scope

Valid matched near, middle and horizon views were captured only after the timed
routes. Near and middle views were effectively unchanged. The horizon showed
localized silhouette differences. Detached close views of far-resident terrain
showed coarser tributaries and banks. No new sky crack was observed in the two
recorded radius-256 boundary views. Those views do not cover every transition
or establish that temporal popping is absent.

The production mesh audit completed 104/104 selected regular/transition meshes
for radius 256 and 88/88 for radius 128. Both reported zero stale, failed,
mutated, invalid-index, out-of-bounds, nonfinite, identity, oversized, degenerate
or draw-argument findings. Readbacks ran outside measurement. This is sampled
coverage, not a standalone winding proof or qualification of every cave,
lighting configuration, edit/network operation or low-sun shadow case.

The cold saved candidate preserved camera orientation and field revision, with
zero exceptions, collision failures and unsafe commits. Its moving FPS repeated
within 0.2% of the first candidate, but stationary FPS was 6.4% lower. Against
the faster original baseline, the two candidate runs show about 28% moving
gains and 10–18% stationary gains. Cold-run allocations were 0.9% lower in
total and 22.9% lower per frame; GC total pause was 6.3% higher, with improved
frame tails. The valid return-control repeat preserved the camera and completed
with zero exceptions, collision failures and unsafe commits, but was slower
than the first original (317.34 / 338.74 FPS). Its resident triangles differed
by 0.34% and arena count was 14 versus 12. Session/configuration-history effects
are not fully isolated, so the report uses the faster original rather than
claiming the 48% moving increase suggested by the slower return.

Acceptance: retain the two scene settings. Both candidate runs exceed the
predeclared 20% substantial-gain target in moving FPS against the faster original;
stationary gains remain below 20%. Both runs have
improved moving/stationary tails, settled streaming, no safety-counter failures
and no process/GPU peak regression above 10%. Nearby matched images retain the
appearance; the documented distant-shape tradeoff is accepted within this
optimization scope. This acceptance does not certify untested lighting/cave/
multiplayer cases or treat the GC totals as an improvement. The original
source shaders and mesh implementation remain restored.

The existing
prefab component-destruction assertion reproduced during orderly editor exit;
play had stopped and the scene had saved before shutdown. It is separate from
zero-exception gameplay runs and is not claimed fixed by these settings.

## Remaining opportunities

The evidence supports reducing submitted geometry across multiple passes before
further minor texture tuning. Near detail and textures are now a larger share
of the remaining cost. Shadow/depth vertex expansion is a potential structural
opportunity, but needs a supported integration path and its own measurements.
Occlusion remains constrained by the user's decision against an additional
terrain geometry draw solely to obtain depth. No unimplemented redesign is
counted toward the measured gain.

## Standstill follow-up: current player view

The user's follow-up correctly identifies an unresolved cost. The moving gain
above does not imply a similar standstill gain. The current player view is a
different angle and saved field revision 3894, so its absolute FPS cannot be
compared directly with the earlier 0,0,0 camera and revision 3893.

`TERRAIN-STANDSTILL-001/v1` records read-only observations from the existing
production sampler at camera Euler 11.961, -108.111, 0 and the same player
position. Source is 6aefb5a, engine/hardware unchanged. Three samples per phase
preserved camera, field revision, material/lighting settings and settled queues.
No test component, shader variant or new benchmark path was added.

| Phase | FPS range | Mean GPU time range |
| --- | ---: | ---: |
| Current full resolution, 2769 x 1529 | 335.1-339.2 | 2.57-2.61 ms |
| Diagnostic quarter-pixel count, 1385 x 765 | 542.0-549.9 | 1.30-1.31 ms |
| Restored full resolution | 328.0-330.9 | 2.62-2.65 ms |

Mean FPS rose from 337.1 to 546.9 (+62.2%) in the resolution diagnostic and
returned to 329.2 after restoration. The combined full-resolution range was
within the predeclared 5% stability limit. The diagnostic changes all work
sensitive to pixel count and texture footprint, not just terrain textures;
it is evidence of strong screen-pixel cost, not an acceptable visual downgrade
or proof that a particular shader optimization will deliver 547 FPS.

Separate instrumented snapshots identified the largest named GPU scopes:

| Scope | Smoothed time range |
| --- | ---: |
| Terrain color draw, including vertex/material/lighting work | 1.17-1.42 ms |
| Four opaque cascade depth scopes combined | 0.514-0.522 ms |
| Large depth/normal prepass | 0.354-0.364 ms |
| Distance fog | 0.141-0.148 ms |

Do not add nested parent/child scopes or compare instrumented FPS with the
unprofiled phases. Color-draw timing is not an isolated texture-sampling timer.
The CPU terrain update was approximately 0.03-0.04 ms in the unprofiled
observations, with zero visual/transition backlog. All 4913 collision regions
were ready with zero failures. This points to rendering, particularly visible
surface shading, rather than ongoing terrain generation. CPU/GPU times overlap.

The inspected current screenshot contains a large foreground grass area,
water and distant terrain. Standing still stops movement-driven streaming,
but the view is still rendered every frame. Earlier distant geometry LOD
therefore does not remove the nearby material/lighting cost. The next targeted
experiment should isolate material sampling from lighting in this heavier view
before selecting another appearance-preserving optimization. Previously
rejected anti-repetition and material-cache prototypes remain rejected; no new
cache result or 500-600 FPS promise is implied by this diagnosis.

A secondary observation is allocation history: the current scene uses 14 arenas
(11 have allocations) versus 6 in the earlier cold candidate. Source inspection
shows empty trailing arena trimming is called at figure-eight completion, not
ordinary idle, and allocated ranges are not compacted. This deserves a separate
controlled investigation; no FPS share is attributed to fragmentation here.
Current configuration is applied automatically by OnUpdate, so the higher arena
count does not mean that inspector settings failed to apply.

[Standstill evidence](../ValidationEvidence/TerrainStandstill/) retains all 12
sampler/profiler observations and the inspected image. Full resolution is
restored, GPU profiling disabled, and no runtime source or visual setting change
is retained. These documentation-only findings do not require a new figure-eight
acceptance run. The standstill performance target remains unresolved.
