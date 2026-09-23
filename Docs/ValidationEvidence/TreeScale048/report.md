# Tree scaling experiments 048

September 23, 2026. No tested optimization met both the declared performance and
appearance requirements. The original runtime source and assets are restored.

## What the measurements establish

The current dense oak is expensive both up close and in a forest. It contains
544,606 leaves. Its selected two-triangle cards still submit 1,089,212 leaf
triangles for each complete detailed tree, before vertex-shader rejection.
The closest wood level adds 944,148 triangles. Depth and shadow passes repeat
geometry work. Reducing the visible density in the shader does not compact the
submitted geometry.

The fixed close view measured 154–156 FPS with the tree and 421 FPS with its
entire root disabled. This removal is attribution only. The full scene counter
was 8,156,032 triangles with the tree and 22,592 without it. Reducing viewport
dimensions by half raised the original close view to 234.5 FPS, demonstrating
substantial pixel work as well as geometry cost. Lower resolution was restored.

The real playable-world stress test created nested populations of the same oak:

| Trees | Original FPS | Scene draw calls | Scene triangles |
| --- | ---: | ---: | ---: |
| 1 | 323.0 | 62 | 67,762 |
| 64 | 310.2 | 83 | 73,810 |
| 256 | 264.8 | 83 | 92,242 |
| 1,024 | 135.9 | 83 | 165,970 |

This is an elevated, entirely distant forest view, not a walk through overlapping
detailed crowns. All counts use the same immutable placements and camera.
The scene already batches distant trees effectively: increasing 64 to 1,024
did not increase draw calls. The small depth prepass rose from 0.0904 to 1.7619 ms,
and forward rendering from 0.1744 to 2.6977 ms. These are complete scene scopes,
not isolated additive costs per tree. CPU Render scope also includes waiting.

Hardware: RTX 5090, Ryzen 9800X3D; s&box 26.09.15; physical 2769×1529,
FOV 60 for supporting observations. GPU profiling is enabled for both sides.
Rolling diagnostics overlap and are not independent frame samples. Their detached
camera GPU-total text is stale; use the current named GPU scopes instead.

## Rejected experiments

| Candidate | Change | Observed result and decision |
| --- | --- | --- |
| A | Lower closest wood LOD | About 4% combined GPU gain; below the declared 10% gate |
| C | Move constant distant wind calculations to vertices | No demonstrated gain |
| D | Explicit distant depth-prepass flag | No demonstrated pass/draw change; creation failed before 1,024 |
| E | Skip hidden detailed attribute writes | 3.4% lower CPU Render scope; hotload also lost one tree's shadow |
| F | Spatially repartition the same leaves into four models | About 0.5% GPU gain in the close view |
| G/G2 | Stronger distant leaf thinning | G invalidated by stale compiled meshes; G2 gained 11.2% close GPU time but visibly thinned the exterior canopy |
| H | Tighten distant raster strips | No useful gain at 1,024; overall FPS fell |
| I | Two depth refinements at every distant-tree distance | 152.6 FPS at 1,024, but small branch patches changed in closer views |
| J | Fade the third refinement only at about 60–80 m | 142.8 FPS; 8.8% combined GPU improvement, below the declared gate |
| J2 | Explicitly unroll J's loop | 149.2 FPS; 9.4% combined GPU improvement, still below the gate |

Rejected source changes and temporary populations are restored. Failed native
prefab creation and invalid asset-cache observations remain in the ledger.
Restoring source hashes alone did not reload two compiled models: touching their
source dependencies, reimporting, and checking original compiled sizes plus
rendered counters was necessary.

The performance gate was fixed at a 10% reduction in affected GPU passes, with
preserved appearance and the canonical regression checks also required. It was
not relaxed after J/J2 fell short. Their additional visual qualification was not
run after the performance rejection. No permanent thousand-tree scene was saved.

## Structural work still needed

The next substantial near-tree improvement needs a cheaper representation of
groups of leaves, with individual leaves retained where they are visible close
to the player. Simply deleting more leaves makes this oak look thinner.
Tight, shaped branch-group cards or another coverage-preserving representation
must reduce both submitted geometry and overlapping empty pixels. It needs
inspection beneath and inside the crown, compatible shadows, wind and a matched
transition to the current distant representation.

For a large logical world, the population owner also needs regional residency:
instance records and cheap distant rendering outside nearby interests, with
detailed renderers and gameplay collision activated only where needed. The current
stress test instantiates complete prefabs and does not establish bounded residency,
streaming performance, multiplayer relevance or an acceptable thousand-tree
ground-level view.

[Crytek's production account](https://developer.nvidia.com/gpugems/gpugems3/part-iii-rendering/chapter-16-vegetation-procedural-animation-and-shading-crysis)
describes several leaves on low-polygon planes and GPU wind. This supports testing
grouped foliage; it does not predict a Voxels3 speedup.
[Epic's HLOD documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/world-partition---hierarchical-level-of-detail-in-unreal-engine)
separates distant proxies from loaded cells. Adopt that separation as the next
population design direction; Unreal's implementation is not an s&box API.

## Evidence

The immutable workload, declarations, failed runs and decisions are in
[the validation ledger](../../ValidationResults.md), TREE-SCALE-048.
[Observation summary](observations-summary.json) includes both depth-prepass sizes,
forward rendering and shadow passes. Raw JSON observations retain actual camera,
source identities, per-sample diagnostics and cleanup readback.
The [experiment archive](experiment-evidence.zip) preserves those observations,
external native-MCP drivers, failed candidate source and the canonical baseline.

Selected native images:

- [Original 1,024-tree forest](baseline-retry-forest-1024.png).
- Rejected leaf thinning: [original canopy](restored-control-near-on.png) and
  [G2 canopy](candidate-g2-near-on.png).
- Rejected global depth simplification: [original closer view](restored-original-225-700-far.png)
  and [I closer view](candidate-i-225-700-far.png).

The canonical single-tree baseline is
[recorded separately](../TreeShadows/scale048-baseline-retry-summary.json):
377.47 FPS moving and 293.18 standing, with zero timed exceptions.
That route is required regression coverage; it does not qualify the dense forest.

After exact source/asset restoration and a fresh visible editor restart, the
same route completed at 383.30 FPS moving and 299.05 standing. Frame p95/p99,
allocation and mean/peak memory checks stayed within the recorded limits;
terrain/collision work drained with zero timed exceptions or collision failures.
The existing collector does not expose memory-tail percentiles. See the
[restoration comparison](restoration-comparison.json). The source fingerprint
matches the baseline exactly, so this small FPS difference is not an optimization
gain. The [restored close image](restored-cold-close.png) and original rendered
triangle count were checked after restarting. The saved single-oak scene is
unchanged and the editor remains in interactive Play with a free viewport.
