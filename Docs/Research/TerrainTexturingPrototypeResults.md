# Terrain texturing prototype results

2026-09-17. User-authorized experiments following the
[Godot terrain research](TerrainTexturingComparison.md).

## Decision

Restore the original renderer. All four directions were prototyped, but none is
accepted as a production replacement. Preserve the candidate patches and evidence
for further work; no experimental cache, shader branch or runtime toggle remains.
The original shader and mesher are restored byte-for-byte and compile successfully.
A final fresh-process run completed; its late camera/world verification is
qualified below, so it is not used to claim a matched performance improvement.

| Direction | Observed outcome | Disposition |
| --- | --- | --- |
| Isolate three-patch anti-repetition cost | Single sampling saved 0.281–0.312 ms stationary GPU time; moving gain did not survive the original repeat | Diagnostic only; visible repetition |
| Cheaper anti-repetition | Two continuous projections, including a zero-weight guarded variant, gave no useful stationary gain | Rejected |
| Screen-footprint simplification | Conservative original-to-single blend regressed stationary FPS by 7.1–8.4% | Rejected |
| Bounded material-pattern cache | Corrected 2D cache improved moving FPS by 3.7–7.8% versus its paired dual bypass; stationary change ranged from −2.5% to +1.7%, with about 112 MB extra texture memory | Not retained; benefit is too variable for the added cost and unfinished acceptance gates |

These are outcomes for these concrete prototypes, not proof that every cheaper
anti-tiling method or a full world-surface cache would fail. No result establishes
500–600 FPS while preserving the current scene and appearance.

## Method and measurements

Canonical scenario `TERRAIN-PROTOTYPES-001/v1` is defined before runs in the
[validation ledger](../ValidationResults.md). Source baseline `16c6d3d`; engine
26.09.15, RTX 5090 / Ryzen 9800X3D; effective 2769 × 1529; saved world revision
3892; seed 1337 / generator 48; visual radius 256 / LOD 0–5; gameplay radius 8.
First-person camera rotation 0,0,0, horizontal FOV 75. Figure-eight speed 2500,
distance 50000, one loop; approximately 122 seconds moving, automatic settling,
then 10 seconds stationary. At least 30 seconds post-compile warmup and settled
queues before each run. Player input remained enabled.

The native editor connector was unavailable, so the same existing editor MCP
server was called through its local HTTP endpoint. No runtime test component,
alternate terrain implementation or headless client was introduced.

| Run | Moving FPS | Stationary FPS | Moving GPU ms | Stationary GPU ms | Moving p99 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Original A | 372.84 | 351.00 | 2.0576 | 2.5028 | 10.5533 |
| Single diagnostic | 378.90 | 392.66 | 2.0406 | 2.1908 | 10.5526 |
| Original A repeat | 383.76 | 356.06 | 2.0366 | 2.4719 | 10.6581 |
| Dual | 384.78 | 351.26 | 2.0313 | 2.5089 | 10.4299 |
| Footprint | 376.91 | 326.23 | 2.0374 | 2.6595 | 11.0697 |
| Guarded dual | 364.22 | 351.53 | 2.1259 | 2.4767 | 11.1486 |
| 2D cache, fresh process | 408.49 | 358.78 | 1.9102 | 2.4569 | 10.2312 |
| Cache-resident dual bypass | 378.81 | 352.92 | 2.0906 | 2.4999 | 10.2626 |
| 2D cache repeat | 392.79 | 344.11 | 1.9561 | 2.5733 | 10.4520 |
| Restored original, comparability unqualified | 383.96 | 350.68 | 1.9821 | 2.5104 | 10.5373 |

All completed timing runs above recorded zero exceptions, 4,913 ready collision
chunks and zero collision failures. Raw results retain allocations, GC, memory,
streaming/settling, camera and profiler details, not just these averages.

The first original repeat removed the apparent moving benefit of single sampling.
Its stationary benefit remained. Thus removing anti-repetition alone did not
recover the requested frame rate in this workload.

The cache bypass retained the initialized cache/resources but used the exact dual
draw shader, controlling cache residency while bypassing its sampling. The first
cache run used 14 geometry arenas; bypass used 15. Each arena reserves 48 MiB,
explaining most of the approximately 49.9 MB GPU-peak difference between those
runs. Allocation/packing differences and process history limit attribution of
all FPS changes to shading. Session process memory also rose after hot shader
changes, including the original repeat; do not describe that growth as a measured
cache leak. Cold cache peak was 5.424 GB process / 2.294 GB GPU versus initial
original 5.081 GB / 2.182 GB.

The final original run finished at 10:36:49 local time. Later verification found
a changed camera (18.086,11.903,0 degrees) and a distinct 2,143-sample edit, saved
at revision 3893 / checkpoint 108 at 10:37:48. Exact input time relative to the
measured phases is not established. Its matched-comparison status is unqualified;
do not use it to strengthen a performance claim. The newer live edit is preserved.
Original runtime source is unchanged, compilation reports zero errors, and all
4,913 collision chunks are ready with no pending visual work.

Route maximum frames ranged into roughly 88–217 ms. Several runs recorded one
approximately 46 MB allocation, without event-level attribution. GPU pass lists
were empty because detailed GPU profiling was off for comparison. The retained
200-frame CPU snapshots are not whole-route averages: original moving
VoxelManager.OnUpdate averaged 0.3973 ms (p95 1.0498), including collision integration
0.0816 ms (p95 0.5927); stationary update was 0.0295 ms. These identify separate
moving work but do not assign every long frame to a specific cause.

## What was implemented

The single diagnostic replaced only stochastic patch evaluation with one packed
color/roughness and one packed normal/AO read per contributing projection.
Material weights, projection guards, normal reconstruction, 16× filtering,
32–64 m distance fading, geometry and shadows were preserved. Parallax stayed off.

Dual blended two continuous UV projections, one rotated 90 degrees, with smooth
periodic value noise. It kept all packed channels and rotated the sampled normal
back into the original frame. Its eight-UV period is an appearance approximation.
The guarded follow-up skipped reads only for exactly zero-weight projections.

Footprint independently kept the original three-patch pattern below 1/32 tile per
pixel and blended toward a single sample up to 1/8 tile per pixel, skipping the
original body beyond that. The transition adds reads; the measured result was
slower. This does not establish that a differently designed footprint policy
would have the same outcome.

The cache pre-evaluated the dual material pattern, not final world-surface colors.
Ten 1024² RGBA16F textures with full mips cost 111,848,080 logical bytes (106.67 MiB).
Creation belonged to GpuVoxelMesher on the game thread. Its existing render
rendezvous evaluated one material pair per tick, generated 2D mips and published
readiness after five ticks. Rendering retained current 3D projections, material
weights, geometry normals and lighting. Cache use blended in at a minor-axis UV
footprint of 1/128–1/64 tile per pixel, protecting finer nearby and grazing detail.
The owner disposed the textures with the mesher; no world-sized cache existed.

Terrain edits cannot make a geometry-independent source pattern stale; current
geometry and weights still control the drawn surface. Source-texture or policy
changes required a new play session in this prototype. Automatic asset-authoring
invalidation was not implemented and is not claimed. A full surface cache would
need separate 3D page addressing, residency and edit-version invalidation; this
experiment does not measure its full potential saving.

## Failures and correctness evidence

The first cache implementation used an array. Resource creation initially failed
on the render thread because ComputeShader creation requires the main thread.
Moving creation fixed that integration error. Lower array mips then rendered black;
forcing mip 0 restored colors/normals, and the engine log reported a texture
array/2D dimension mismatch in mip generation. Direct per-mip array initialization
later caused a native editor exit at 2026-09-17T14:07:52.703737Z. Available event
metadata has no stack proving the precise crashing call. This path was rejected.
The ordinary 2D replacement survived a fresh editor start and completed its route
runs without another crash or dimension error. Incorrect images were never timed
as successful optimization results.

Corrected near, middle and horizon screenshots were inspected. Nearby grass,
soil and rock detail remained visible. The bounded edit probe used one canonical
brush at (256,256,256), radius 128, strength 64, on a separate saved working copy.
It committed 2,103 sample changes, then canonical loading restored original world
revision 3892 and checkpoint 106. The regional fingerprint changed after editing
and matched its original value after restore. All 858 original checkpoint files
remained byte-identical to the separately verified backup. Near and farther
changed/restored views showed updated geometry without a stale material patch.
The material cache initialized only once throughout. Full restore required a
large existing geometry rebuild, publishing after 40.174 seconds; this was not a
new cache-generation cost or a claim of fast restoration.

Negative-coordinate views exposed no gross seam in the inspected image. Static
images do not fully qualify temporal shimmer. The predeclared bounded cavity
search (32 vertical rays) did not find a usable air interval; ceiling rendering
therefore remains unqualified. These limits, plus source-authoring invalidation,
are explicitly incomplete acceptance gates for the rejected cache prototype.

## Evidence and next opportunities

[Raw results, patches, camera records and images](../ValidationEvidence/TerrainPrototypes/)
include every candidate and failed cache path. The source manifest identifies all
cache files, not just the draw shader. The ledger preserves predeclared scenarios,
failures and restoration checks.

The strongest established texture-only saving was about 0.3 ms stationary GPU
work when accepting repetition. The bounded cache produced a modest moving GPU
saving with a memory tradeoff. Further work should attribute terrain color,
lighting, depth/shadow passes and the long moving frames separately, and examine
geometry packing/streaming costs. A compressed pre-baked pattern cache is another
research option; it has not been implemented or measured here. None of these
future directions is an adopted architecture or a promised FPS gain.

## Follow-up priority: large gains

The user explicitly prioritizes high percentage gains over these small savings.
At 350 FPS the total frame budget is 2.857 ms; 500 FPS requires 2.000 ms and
600 FPS requires 1.667 ms. That is about 30% or 42% less total frame time,
respectively. GPU time and CPU time overlap, so individual savings cannot simply
be added or converted into guaranteed FPS.

Prioritize matched diagnostic isolation of entire terrain color, shadow/depth,
and distant-geometry costs before another texture micro-optimization. Temporary
feature removal would measure an upper bound, not constitute an acceptable
shipping visual compromise. If a subsystem cannot account for a substantial
part of the missing budget, deprioritize it. Keep near-player quality and evaluate
distance-dependent geometry, shading and shadow policies only where those
measurements justify them. These are proposed follow-up experiments, not results.
