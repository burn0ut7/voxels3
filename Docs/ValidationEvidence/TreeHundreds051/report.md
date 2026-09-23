# Hundreds of trees — 051

September 23, 2026. Tested the current baked oak, including the subsequent
distant-trunk repair, with 64, 256 and 512 real prefab instances. Runtime source,
assets and saved scene were not changed. The forest is unsaved Play content.

## Static population measurements

RTX 5090 / Ryzen 7 9800X3D, visible s&box 26.09.15, `basic_example`, seed 1337 /
generator 52, grass 64 m, physical 2769×1529, FOV 60. Prefixes of the immutable
048 placement list supply terrain-grounded trees, unit scale and zero rotation.
The existing original oak is included in each count. The same player/terrain
interest remains stationary throughout these observations; its position differs
from previous tasks, so historical FPS is not a strict comparison.

Every observation uses 20 seconds settling and ten samples one second apart.
The table reports the final rolling FPS and tails, consistent with prior forest
reports. Native images were captured after timing and inspected.

| Trees | View | FPS | p95 / p99 frame | Draws | Submitted triangles | Detailed / distant trees |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | Wide control | 408.9 | 3.27 / 5.13 ms | 65 | 68,236 | All distant |
| 64 | Wide | 375.8 | 3.55 / 5.37 ms | 107 | 104,146 | All distant |
| 256 | Wide | 308.1 | 4.39 / 6.36 ms | 107 | 213,586 | All distant |
| 512 | Wide | 241.9 | 5.91 / 7.83 ms | 107 | 359,506 | All distant |
| 256 | Nearby | 233.8 | 5.36 / 7.59 ms | 203 | 1,001,170 | 2 / 254 |
| 256 | Inside crown | 196.8 | 6.41 / 8.60 ms | 218 | 5,043,366 | 4 / 252 |
| 512 | Nearby | 168.9 | 7.54 / 9.55 ms | 331 | 3,411,768 | 7 / 505 |
| 512 | Inside crown | 164.1 | 7.82 / 10.55 ms | 328 | 6,784,382 | 6 / 506 |

All static observations meet the declared exploratory 120 FPS, p95 ≤12 ms and
p99 ≤20 ms targets. Near representation counts are actual component readbacks
for every instance after settling. Hysteresis explains why the detail count
depends on the approach: thresholds remain 12/16 m and transition time 0.35 s.
The wide camera lies beyond every instance's detailed range.

These results do not reach the earlier goal of negligible forest cost. Mean
combined depth/forward/opaque-shadow GPU scopes are 0.6522, 0.8419, 1.4790 and
2.4122 ms at 1/64/256/512 trees. Relative to the single-tree control, 512 adds
1.7600 ms to these passes, exceeding the ≤1 ms extra-GPU target. Combined engine
Render/Update elapsed scopes increase about 1.47 ms, exceeding the ≤0.5 ms target.
At 256, extra GPU is 0.8268 ms but Render/Update grows about 0.6560 ms.

At the 512-tree inside-crown pose, affected GPU scopes total 4.8660 ms: large
depth prepass 2.5595, opaque shadows 1.2730, forward 1.0036, remaining prepasses
0.0299. Nearby geometry and shadow work remain substantial. The distant renderer
keeps draw count bounded in the wide view, while detailed trees add draws and
geometry. These observations motivate further optimization; they are not a
claim that a new optimization was implemented in this task.

During wide observations, rolling process memory rises from 10,608.1 to
10,770.6 MiB and GPU memory from 3,864.2 to 3,868.2 MiB. This is one warm editor
session with shared assets already loaded, not a cold asset-residency study.

## Movement and limits

The populated traversal uses the existing figure-eight trigger, with 512 trees,
speed 2500, distance 50000, one loop and the normal final drain/standing capture.
It has its own `TREE-HUNDREDS-051/route-v1` ID because population differs from
the canonical single-tree test. It checks streaming and frame pacing with the
forest present; the long route also travels well beyond this forest's bounds.
Its average cannot be interpreted as walking continuously inside a dense grove.

The populated run completed at 376.22 FPS moving (p95/p99 4.7267/6.7723 ms)
and 250.29 FPS standing (5.2741/6.9895 ms). It passes the declared frame targets.
There were zero runtime exceptions and collision failures; all 4,913 desired
collision chunks were ready with none pending. Post-loop drain was 10.234 s.
Moving process memory averaged 5.280 GB (5.483 GB peak), GPU 4.103 GB
(4.110 GB peak). Memory is sensitive to this editor's accumulated loading/GC
history; the decrease from earlier static readbacks is not an optimization claim.
Moving/standing allocations were 185,804.58 / 154,198.73 bytes per frame.
A separately declared matching control disables the temporary forest parent,
retaining the original tree and already loaded assets, to attribute active-tree
cost without treating this as an asset-unloading experiment.

The matching control confirms a population-related allocation cost:

| Phase | One active tree | 512 active trees | Extra allocation per frame |
| --- | ---: | ---: | ---: |
| Moving | 68,880.36 B/frame | 185,804.58 B/frame | 116,924.22 B |
| Standing | 33,833.28 B/frame | 154,198.73 B/frame | 120,365.45 B |

The control measured 415.04 FPS moving and 418.99 standing, versus 376.22 / 250.29
with the forest active. Source revision, route parameters and ending target
position match. This is the cost of adding active trees, not a regression from
changing rendering code. Existing per-tree `OnPreRender` work includes child
renderer enumeration/validation and repeated render-attribute updates. Those
are concrete profiling targets; these aggregate measurements do not identify
the allocating call site. One-time load/creation stutters were not separately
timed. Shared asset caches and memory history remain present in the control.

Traversal results are recorded in [the result summary](populated-route-summary.json)
and [the full result](populated-route.json.gz).

Rolling static windows overlap and are not independent raw-frame samples.
The detached-camera diagnostic's GPU frame average is stale, so only live named
GPU scopes are used. These are full scene-pass costs, not isolated tree timings.
Engine Render/Update scopes include elapsed engine work and are not proof of
pure CPU execution cost. Results apply to this high-end machine and these fixed
views, not all camera positions, hardware, species or multiplayer loads.

## Evidence

[Measurements](summary.json), [initial source and state](before.json),
[wide forest](current-forest-512.png),
[nearby 512-tree view](nearby-forest-512-near.png),
[inside-crown 512-tree view](nearby-forest-512-close.png),
[raw observations and orchestration](observations.zip).

The existing source tree, shaders, materials, prefab, terrain settings and saved
scene are unchanged. This task adds validation evidence only. The temporary
forest uses the existing production tree components and can be removed by ending
the current Play session. No new population or streaming system was introduced.

Final state: 512 enabled trees remain in the current Play session, under
`Hundreds051 Forest` plus the original oak. The original player position/view,
normal game camera, free viewport and interactive controls are restored.
Streaming is settled. Source hashes and the 19,500-byte saved scene match the
initial state exactly. No new engine error lines appeared during this task;
the eight existing stock-resource errors remain. A wrong-tool screenshot call
was rejected during restoration, then the correct main-camera capture succeeded.
[Final state](final-state.json), [log check](final-errors.json),
[restored player view](final-forest.png).
