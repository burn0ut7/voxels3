# Terrain texture cost investigation

Parallax remains disabled. The tested implementation keeps full nearby texture
detail and reduces distant shading work. It also packs roughness and ambient
occlusion into the color and normal textures, reducing three reads to two.

## What was expensive

The original parallax-free shader sampled three maps through three stochastic
patches on each of three projection planes: up to 27 texture operations per
contributing material. Normal maps, anti-repetition blending, and 16x anisotropic
filtering were executed at all distances. Mesh LOD did not reduce this pixel work.
Small floating-point material remainders could also enter the expensive sampler.
These are source operation bounds, not measured hardware texture-tap counts.

The final candidate uses explicit skips for negligible contributions, full detail
within 32 m, and a smooth fade to the textures' average color/roughness/AO by 64 m.
Far surfaces use the geometric normal and two texture reads per material. Near
surfaces keep color, normal mapping, roughness, AO, anti-repetition and 16x filtering;
packing reduces their bound from 27 to 18 reads per material. Terrain geometry,
visibility distance, collision, world data and CPU scheduling are unchanged.

## Valid same-resolution results

All listed runs use 2769×1529 actual pixels, the same canonical figure-eight,
seed 1337 / generator 48, exact center (-1.6258175, 1.2225341), speed 2500, distance 50000,
one loop, 10 m clearance and 10 s stationary measurement after settlement.
Engine 26.09.15, RTX 5090. Existing project changes are retained.

| Variant | Moving FPS | Stationary FPS | Moving GPU ms | Stationary GPU ms |
|---|---:|---:|---:|---:|
| Parallax-free original, background game closed |299.0|245.4|2.745|3.699|
| Distance fade, same session |368.4|318.9|2.119|2.777|
| Distance fade, fresh editor |369.4|355.9|2.106|2.495|
| Distance fade + packing, correct resolution |413.7|371.6|1.958|2.388|
| Distance fade + packing, fresh editor |401.3|355.9|1.987|2.495|

Packing's incremental comparison is against the distance-only implementation,
not a packing-only shader. No matched pre-texture 600 FPS measurement exists.
The fresh editor changed stationary performance; do not attribute the entire
original-to-final FPS difference to one shader change without that limitation.

## Visual evidence

[Near original](baseline-near.png), [distance-only near](candidate-near.png),
[packed near](packed-near.png), [original horizon](baseline-far.png),
[distance-only horizon](candidate-far.png).

Near original versus distance-only mean absolute RGB difference was about
0.000012/255. Packing versus distance-only averaged 0.495/255, with maximum
channel differences 21/17/23. BC7 recompression is not lossless; no lost grass,
soil or rock detail was apparent in the inspected close-up. Far terrain is
intentionally smoother. No hard fade boundary was apparent in the fixed horizon
view; this is not exhaustive material, lighting or motion qualification.

## Failed and limited evidence

The initial comparisons were contaminated by a second running game and severe
memory pressure (under 1 GiB available, active paging). They remain in the ledger
and raw files. Their gains are not acceptance evidence.

Editor DPI scaling and camera-mode changes affected actual render size. Runs
at 1847×1021 and the aborted 1846×1019 attempt are not comparable to 2769×1529.
The packed 496.7/502.5 FPS result came from the lower resolution and is excluded.
Actual screen dimensions were verified before corrected runs and in their results.

The same-session distance candidate had a 223.84 ms CPU-side worst frame despite
improved p95/p99. The engine logged a QueuePresentAndWait warning during that
run. GC and streaming maxima do not explain the outlier, and exact attribution
remains unresolved. Fresh distance-only maximum was 93.66 ms; corrected packed
maximum 90.79 ms, versus original 99.91 ms. Preserve the earlier failure.

Two normal editor shutdowns raised an existing prefab cleanup assertion and
retained an Error window. Each saved/stopped process was terminated only after
Source2Shutdown, then a single visible editor was launched. These are failed
clean shutdowns. New sessions loaded the shader successfully; the Sentry crash
marker remained 2026-09-16T03:40:57.725399Z. Stock missing prop/clothing resources
are separate from terrain shader load. The project scene was restored byte-for-byte.

Hot compilation retains compiler/asset caches and the old session had experienced
paging. Process working-set comparisons across those states do not establish a
runtime leak; fresh-process measurements are recorded separately below.

## Reproduction and source

[Distance candidate patch](candidate.patch) and [combined packing patch](packed.patch)
apply to the parallax-free working shader8168B7D0. The combined shader hash is
17D02D4409B16BB0E8D8BD0B87B47DE25F47DA7290ADB087BCE8DE5177EF2A2E.
After changing the channel layout, compile the shader and fully compile
materials/voxels/voxel_terrain.vmat before measuring. The source images and
material input paths remain unchanged. The engine's blendable.shader line 126
provides the installed mixed sRGB-RGB/linear-alpha BC7 declaration pattern.

The initial evidence commit left the runtime edits in the shared working tree.
Following the explicit implementation request, the tested shader, source material,
referenced texture images and material-loading line are integrated together.
Unrelated terrain-generation and water changes remain outside this integration.
The patches preserve the exact optimization relative to the prior working shader.

## Final decision

Keep the combined shader active locally. Its fresh-process run matched all fixed
workload fields and completed without exceptions, collision failures or pending
collision work. Compared with fresh distance-only, moving FPS improved 8.6%, while
stationary performance was effectively identical. The corrected warm run showed
a 12% moving improvement; do not claim a repeatable stationary packing benefit.

Fresh packed moving p95/p99/max were 4.514/9.618/88.778 ms, compared with distance-only
5.334/10.453/93.661 ms. Stationary p95/p99/max were 3.915/4.386/9.905 ms. Peak process
memory was 4.885 GB versus 5.012 GB; peak GPU memory 2.180 GB versus 2.209 GB. Allocations
per frame increased 2.8%, within the unchanged 10% criterion. Hot-compilation memory
growth did not persist after restart. Final incremental FPS/tail/memory criteria
passed. This does not erase earlier outliers or qualify the engine shutdown bug.

The clean original-to-final comparison measured 299 to 401–414 moving FPS and 245
to 356–372 stationary FPS. Those runs include an editor restart and show session
variation; the entire stationary difference cannot be attributed to packing.
Nearby detail was preserved in the tested view. Broader material/motion checks
and minimum-hardware performance remain unqualified.

Raw results are retained as lossless JSON gzip files in this directory. Key runs:
[controlled baseline](controlled-baseline.json.gz),
[controlled distance candidate](controlled-candidate.json.gz),
[fresh distance candidate](fresh-candidate.json.gz),
[corrected packed run](packed-fixed.json.gz),
[fresh packed run](fresh-packed.json.gz).
