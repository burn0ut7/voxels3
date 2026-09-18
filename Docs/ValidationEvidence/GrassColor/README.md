# Grass color patches

The user requested different greens and yellows, then clarified that colors
should be concentrated in patches, with only small differences within a patch.
The first hot preview used 70% plant seed, 25% world patch and 5% leaf seed; the
user rejected its scattered appearance. `preview-meadow.png` preserves it.

The second preview, `patch-skyline.png`, made patches dominant but used a warped
sine field and a high-contrast palette. The user rejected its visible bands and
stark green/yellow difference. Neither rejected palette completed a timed run.

The final generation shader uses smooth 2D value noise from the root's world XY on a 4.5 m
grid. Four hashed corner values interpolate with cubic weights, producing soft,
irregular patches without a directional wave. Plant/leaf offsets contribute at
most +/-0.015 and +/-0.005. The albedo endpoints are green(0.115,0.225,0.043) and
olive-yellow(0.215,0.25,0.075); their brightness is closer than the rejected ramp.
The root uses 65% of the patch warmth, rising to 100% at the tip.
No time or camera position enters the color field. Geometry, density, wind,
range, root bytes and draw count remain unchanged. The patch is cached once per
tuft in shape.x alongside a 16-bit angle fraction; the color half float is biased
into [1,2] to avoid denormal/NaN packed records. Angle precision is 0.0055 degrees,
color approximately 0.001. Forward vertices decode the sample and add the same
tiny local variation, using the existing two-float interpolant. The earlier
version evaluated this identical field per forward vertex; it was replaced
after measurement to remove repeated work.

## Visual evidence

Rejected previews are retained above; final cold views use the fixed meadow/close/skyline cameras from
GRASS-COLOR-001/v1. These are image assessments, not a photorealism measurement.

The first draw-shader compile immediately after each file write reported that
the mounted source path was unavailable. Retrying after the asset refresh
succeeded, with only the existing profile-implicitly-upgraded warning.
The initial meadow screenshot request found that the camera had returned to
normal Game mode; its tool error is retained as `patch-meadow.error.json`.
No failed screenshot is presented as an image. A range property write returned
an editor NullReferenceException; readback confirmed the existing range was64m.
After stopping Play, a subsequent Play start lost project types and the player
prefab. The camera setter returned a null reference and `failed-play-blank.png`
was blank. This is not visual acceptance. A fresh visible restart is required.

## Performance qualification

Scenario GRASS-COLOR-001/v1 was defined before timing in the validation ledger.
It reuses the exact wind workload and latest accepted wind run
`14f1601221e14b85b0aefc00d40f6623` on world3991/pages873, at64m and2769x1529.
The same frame-tail, memory/allocation, +0.3ms standing GPU and zero-failure gates
apply. The compute shader's indentation was corrected without changing tokens.
Attempt1 passed shader compilation and cold startup but its performance start
was rejected because all visual LODs were not settled. `rejected-attempt1.json`
and `attempt1-*` retain the state; no timed result exists. The final revision
requires fresh startup, full visual/transition/placement settlement, a fixed
candidate run and inspected final captures.

Candidate2 completed at2769x1391 after camera preview/restoration changed the
viewport size. Its results are non-comparable and retained under `candidate2-*`.
Corrected run `7c435b44f6954e2a88b212a6210e3ca1` rendered2769x1529 and failed
moving gates:363.05FPS(-24.20%),p957.6395ms,p9918.1847ms. Its standing windows
passed:419.83FPS,p952.9092ms,p995.1487ms,GPU2.08273ms(+0.19740ms).
Memory/allocation and zero-exception/collision/overflow checks passed, but this
was an overall FAIL. Moving GPU time decreased; CPU/frame stalls are not
established as a result of the color calculation. Raw/comparison evidence remains.

The standing GPU delta motivated caching color once per tuft. This is a source
optimization with the same appearance, workload and gates, not acceptance of
the failed run. The final cached-color run is recorded below.

## Final cached-color result

Run `e0034107aafc4d3ebd1411d4093dfcc4` passed the unchanged v1 gates against the
accepted wind baseline. Both profiler windows record 2769x1529, range 64 m,
the same world revision 3991/pages 873 and the fixed canonical route.

| Metric | Moving | Standing |
| --- | ---: | ---: |
| Average FPS | 495.15 (+3.38%) | 451.71 (+4.95%) |
| Frame p95 | 3.6453 ms (-4.63%) | 2.5929 ms (-15.13%) |
| Frame p99 | 6.3055 ms (+0.21%) | 4.1921 ms (-11.66%) |
| Average GPU | 1.67191 ms (-0.01975 ms) | 1.91005 ms (+0.02472 ms) |
| Process peak | -0.92% | -1.30% |
| GPU memory peak | -3.17% | -3.17% |
| Allocation per frame | -4.36% | -1.06% |

There were no timed exceptions, collision failures or grass overflows. All 4913
collision regions were ready, and visual/transition/placement work had settled.
Grass reported 5946 current tufts and 8038 peak candidates against capacity 65536.
[Raw result](optimized.json.gz), [comparison](optimized-comparison.json), and
[final diagnostics](optimized-final.json) retain exact values. Higher FPS is not
attributed to coloring; the earlier moving stalls remain unexplained and retained.

Final source matches the explicitly compiled and cold-started shader hashes.
Visible editor PID 9656 started at 12:18:05 on engine 26.09.15. The Sentry marker
did not advance. Eight startup resource warnings/errors are retained in the log;
none are grass parser, pipeline, dispatch or timed exception failures.
The test ran without screenshots, editor queries or workspace writes during its
timed windows. This qualifies the recorded single-player RTX 5090 scenario,
not other hardware or multiplayer load.

The final `optimized-meadow.png`, `optimized-close.png` and `optimized-skyline.png`
were inspected at the fixed cameras after timing. They show restrained green and
olive-yellow areas with soft irregular boundaries, small local variation, intact
tips and ground attachment, and no earlier directional color bands. These stills
do not replace the unchanged wind path's earlier animation sequence evidence.
Normal first-person camera, free viewport, input/look controls and the previous
player location were restored; [restoration](optimized-restored.json) records
settled streaming and collision. No authored scene was saved.
