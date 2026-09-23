# Animated baked foliage — 049

Accepted September 23, 2026 for the installed dense oak. The user authorized
grouped leaves with continued wind. Close-view foliage geometry is reduced by
97.0%; the measured scene depth/forward/opaque-shadow passes are 39.7% faster.
Branch sway and smaller leaf-group flutter remain visible in the playable world.

## Implementation

The original 544,606 leaves are partitioned into 4,096 patches within their
primary branches. Each patch uses an eight-triangle subdivided card and one of
64 representative cluster images, each baked at 512 pixels. Two 4096-square
atlases contain color and normals, with separate coverage and numeric motion.
The active leaf triangle count is 32,768, previously 1,089,212. All four existing
canopy model resources retain their prefab roles.

The shader applies existing branch/root wind to patch centers, then adds small
ripples and angular flutter. Leaves within a patch move together. This is an
approximation with repeated cluster shapes, not exact preservation of every
source blade. Native distant captures were rebuilt from the new representation.
Wood, original leaf FBXs, original motion texture, collision, prefab membership,
saved scene, runtime C# and the distant shader are unchanged.

[Architecture and bake workflow](../../Architecture/BakedTreeFoliage.md).

## Fixed measurements

RTX 5090 / Ryzen 7 9800X3D, visible s&box editor 26.09.15, `basic_example`,
seed 1337/generator 52, grass range 64 m. TREE-BAKE-049/v1 reuses the 048 close,
near and far cameras, FOV 60, physical render size 2769×1529, 20 seconds settling
and ten observations one second apart. Camera movement aborts the observer.
Blender was idle during timing. These are scene costs, not isolated leaf timings.

| View | Original FPS | Baked FPS | Original GPU passes | Baked GPU passes | Reduction |
| --- | ---: | ---: | ---: | ---: | ---: |
| Close / inside crown | 153.7 | 239.0 | 5.1843 ms | 3.1266 ms | 39.7% |
| Nearby exterior | 241.1 | 344.4 | 2.6674 ms | 1.5254 ms | 42.8% |
| Distant | 313.6 | 356.0 | 1.2569 ms | 0.9419 ms | 25.1% |

FPS is the median of ten rolling observations. GPU values are sums of the mean
live smoothed scopes: depth normal prepass Overlay/Large/Small, dynamic opaque
forward and cascades 0/1 opaque shadow draws. The detached-camera diagnostic's
GPU frame average and `Sandbox.Screen` dimensions can be stale; neither is used
to establish this workload. Native camera readbacks consistently report
2769×1528.5 (rounded physical height 1529). `VoxelMcpTools.get_ejected_camera`
derives that size from the actual renderer and DPI. Canonical possessed-camera
measurements report 2769×1529 before and after. These are single-session
observations, not independent repeated frame datasets or portable hardware budgets.

Close submitted scene geometry falls from 8,156,032 triangles / 94 draws to
3,930,256 / 62. Much of the remaining geometry is the unchanged detailed wood.
The four passes containing that wood alone account for about 3.78 million
submitted triangles. Trees still have meaningful rendering cost.

The unchanged 048 forest workload uses the same immutable nested placements,
240-inch spacing, elevated camera and observation windows. Its final rolling
observations are shown below, matching the original report's statistic.

| Trees | Original FPS | Baked FPS | Final p95 / p99 | Draws / triangles |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 323.0 | 436.0 | 2.45 / 4.45 ms | 62 / 67,762 |
| 64 | 310.2 | 405.2 | 2.63 / 5.11 ms | 83 / 73,810 |
| 256 | 264.8 | 316.0 | 3.33 / 5.87 ms | 83 / 92,242 |
| 1,024 | 135.9 | 168.6 | 6.50 / 8.82 ms | 83 / 165,970 |

At 1,024 trees, mean affected GPU passes fall from 5.22049 to 4.48865 ms (14.0%).
The far shader and batching are unchanged; these measurements include the
rebaked appearance/coverage. This elevated distant view does not qualify 1,024
nearby trees, world population streaming, multiplayer or arbitrary tree species.
All temporary instances were removed, leaving the original enabled oak.

## Canonical regression and correctness

Unchanged TREE-PERFORMANCE-038/v2-single-tree: speed 2500, distance 50000, one
figure-eight loop, clearance 393.7008 and the existing ten-second standing sample.
Compare accepted original run `c661ec369c254793bd9599576a49b514` with baked run
`1d19c0e527da46cf8201270007f7ca3c`. Runtime source was stable during each run.

| Metric | Original | Baked |
| --- | ---: | ---: |
| Moving FPS | 383.30 | 426.51 |
| Moving p95 / p99 | 4.2378 / 6.1316 ms | 3.6194 / 5.4299 ms |
| Standing FPS | 299.05 | 331.51 |
| Standing p95 / p99 | 4.5300 / 6.3914 ms | 3.4580 / 5.9683 ms |
| Moving allocation | 69,565.87 B/frame | 67,275.23 B/frame |
| Standing allocation | 34,370.94 B/frame | 35,864.12 B/frame |
| Moving process mean / peak | 13.198 / 13.446 GB | 11.760 / 12.082 GB |
| Moving GPU mean / peak | 4.255 / 4.366 GB | 4.098 / 4.115 GB |
| Standing process mean / peak | 13.328 / 13.330 GB | 11.899 / 11.900 GB |
| Standing GPU mean / peak | 4.417 / 4.417 GB | 4.065 / 4.065 GB |
| Post-loop streaming drain | 9583.444 ms | 9036.749 ms |
| Total synchronous streaming | 438.10373 ms | 429.78766 ms |
| Peak gameplay backlog | 1,223 | 669 |

GB here is decimal. Every recorded FPS, frame pacing, allocation and memory gate
passes. Standing allocation rises 4.34%, within the predeclared 10% limit.
Both phases have zero exceptions; collision finishes with 4,913 ready, zero
pending and zero failures. Final visuals/transitions/placement are settled.
Memory p95/p99 are unavailable in the existing collector and remain unverified.

Twelve fixed-camera native images show moving foliage. Forward/backward optical
flow with less than 0.5-pixel disagreement tracks 1,462 green-canopy features;
1,212 move more than one pixel (required at least 50). Median maximum displacement
is 2.15 pixels and p95 is 4.83. This is rendered image motion, not direct GPU
vertex readback. [Wind recording](wind.gif).

Close, exterior, opposing and far images were inspected. The declared 10, 15,
17, 15, 11 m approach/retreat sequence reports Detailed, Detailed, Distant,
Distant, Detailed. Existing 12/16 m hysteresis and 0.35-second fade remain.
Original source/collision/scene hashes match; all 45 installed manifest files
match their byte counts and hashes.

## Rejected attempts and startup history

The first 512-patch crossed-card candidate was fast (258.4 close FPS) but exposed
flat strips and soft leaf masses. It was rejected. A second candidate with
4,096 unique 128-pixel facing images was also too soft and was rejected before
timing. The accepted shared 512-pixel templates restore readable leaf shapes.
All preliminary images and timings are retained in the raw archive.

Native shader/material/model compilation and a healthy cold editor startup
passed. One preceding startup stopped at the engine's ToolsStallMonitor before
measurements; its timeout/log are retained and not counted as a pass. Retrying
the unchanged source opened the visible project normally. The final log contains
the same eight stock-resource errors as the original startup, no new foliage
shader/parser failures. The Sentry marker remains `2026-09-23T00:57:44.591285Z`.

After timing, an accidental encoding change in an existing editor error message
was restored to its original bytes. All four native captures and packing were
repeated to refresh provenance. Every atlas PNG and the material are byte-exact
against the measured outputs; native compilation still passes. See the
[hash comparison](provenance-refresh.json). No runtime rendering code changed.

## Evidence and commit scope

[Measurements](observations-summary.json),
[canonical summary](final-canonical-summary.json),
[canonical raw result](final-canonical.json.gz),
[source integrity](source-integrity.json),
[final state](final-state.json),
[raw observations and failed attempts](experiment-evidence.zip).

The workspace already contained an uncommitted tree pipeline and unrelated game
changes. This task leaves those edits intact. Its new baker, shader and derived
assets are committed directly. Changes to the pre-existing untracked native
baker and import document are preserved as an exact incremental
[integration patch](integration.patch), already applied in this workspace.
That patch requires the recorded prior workspace versions; this task does not
claim to make the whole pre-existing tree pipeline reproducible from Git HEAD.
Only this task's additions to tracked shared documents are staged.
