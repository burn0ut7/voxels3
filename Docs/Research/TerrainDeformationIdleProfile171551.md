# Idle performance investigation — 17:15:51

The user reports a little over700FPS while standing still before another
client joins, targeting900+FPS. At700FPS the frame budget is1.429ms;900FPS
requires1.111ms, a reduction of about0.317ms. This investigation does not claim
the target is consistently met or the regression is resolved.

## What the new profile establishes

Capture `sbox_2026-09-07_17_15_51.json` was found in the matching directory
under `C:/Program Files (x86)/Steam/steamapps/profiler_captures`. It samples
editorPID14584, main thread44684, with0.2ms nominal sampling interval.
The whole trace includes fresh brush preparation and terrain invalidation in
seconds8–13. It therefore cannot be treated as entirely idle.

The quiet interval1000–6000ms contains13,815 main-thread samples, with no
sampled brush work on any thread and no sampled terrain invalidation.
This interval was selected after inspection for descriptive diagnosis, not as
a frozen benchmark or proof of zero activity between samples.

| Inclusive work in quiet interval | Samples | Share |
| --- | ---: | ---: |
| Editor scene rendering (RenderAll) | 4,688 | 33.93% |
| Scene animation update | 3,424 | 24.78% |
| VoxelManager.OnUpdate | 290 | 2.10% |
| Network pre-frame tick | 25 | 0.18% |
| Collision integration | 13 | 0.09% |
| Terrain replication update | 11 | 0.08% |

Rows overlap. Voxel rendering is part of scene rendering, not fully captured by
VoxelManager.OnUpdate. Low idle manager CPU cost therefore does not exonerate
terrain rendering/GPU cost. Neither CPU samples nor percentage changes between
different captures directly establish an FPS improvement or regression.

## Current-session observations

Read-only inspection found fps_max1000. A normal10second performance window
reported910.9FPS,p95 1.52ms,p99 2.53ms,GPU0.99ms, with zero terrain backlog,
4913collision regions ready and revision56. A later window reported909.5FPS,
p95 1.61ms,p99 2.73ms,GPU0.84ms, but the player moved from
(410.99176,-268.353638,82.9429474) to
(1935.80054,-279.398102,74.9478989). The stationary observation criteria failed;
the remaining third window was not collected. These readings cannot refute
the user's reported700FPS or be called a successful stationary comparison.

The manager averages Sandbox.Diagnostics.PerformanceStats.FrameTime over a
normal10second window. Installed engine evidence shows FrameTime is elapsed
wall time between restarts of its frame stopwatch, not merely scoped terrain
CPU time. The GPU metric is the engine's latest available GPU frame time.

## Investigation direction

Prioritize a matched stationary comparison with identical camera, player
animation, edited state, viewport and profiler conditions. The strongest
quiet-window CPU targets are scene rendering and animation. Installed
SceneAnimationSystem processes registered root renderers each bone-update stage
through Parallel.ForEach and schedules decode-cache maintenance; a static player
can still incur animation work. This is existing engine behavior, not itself
evidence that deformation introduced it.

The authored player uses the same citizen model, animation graph setting and
Dresser source as baselinef1319ed. The baseline's authored FOV60 differs from
the user's current90; earlier controlled figure-eight comparisons explicitly
copied90 into the baseline. Any idle comparison must do likewise. No appearance,
animation or camera behavior has been disabled to obtain a higher FPS.

## Evidence

[Per-second sample breakdown and capture hash](../ValidationEvidence/TerrainDeformation/profile-idle-171551-summary.json).
[Current-session observations](../ValidationEvidence/TerrainDeformation/idle-observation-v1.json).
No source changes or accepted performance fix resulted from this analysis.
