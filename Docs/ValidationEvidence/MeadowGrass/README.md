# Long meadow grass

The user requested long, lush meadow grass after the short-tuft version. This
revision increases full-size leaf height from approximately 7–31 cm to 36–101 cm,
widens leaves to approximately 1.3–3.8 cm, and uses richer greens with darker roots.
The middle bend sits at 45% of height; leaf tips lean outward by 22–50% of height.
Neighboring tufts overlap to form a fuller stand. Frustum bounds expand with the
geometry so offscreen roots cannot incorrectly hide visible tips.

Five leaves and ten opaque triangles per tuft, one depth and one color draw,
root capacity, placement density, material filtering and distance thinning are
unchanged. There is no new CPU placement, vegetation simulation or allocation.
Larger leaves can still cost more pixel work; measured results determine acceptance.

## Fixed qualification

GRASS-MEADOW-001/v1 in the validation ledger reuses the exact
GRASS-NATURAL-001/v2 canonical figure-eight parameters. The latest accepted
short-tuft run is `4e7db047ad1c4c20a8f106204a825306`, recorded in
[NaturalGrass](../NaturalGrass/README.md). An additional comparison retains the
original isolated-blade baseline `b518c1394fc248a9b2b20be516760aaf`.

The saved world remains revision 3989 with 873 pages. Existing uncommitted
terrain-texture fade changes are preserved identically and excluded. Player
input stays enabled and the game stays visible. All fixed FPS, frame-tail,
memory, allocation, collision, streaming and overflow gates remain unchanged.

`preview-close.png` uses the earlier matched close pose; `preview-meadow.png`
raises the camera to Z310 at the same XY and looks down 10 degrees. These were
inspected before the cold restart. `final-close.png` and `final-meadow.png`
repeat those views after the cold restart and performance run. They show long,
overlapping leaves rooted on grass, with exposed dirt/stone left bare.

This remains static grass on the existing 32 m budget. The change does not
qualify multiplayer load, other GPUs, fresh terrain editing or all material types.

## Result

Run `f06cc54d7a21461ab5f94a05a8a865d2` completed the fixed route and passes all
gates against the latest accepted short-tuft baseline. `candidate.json.gz`
preserves the full result; `comparison.json` preserves every metric and gate.

| Measurement | Short tufts | Meadow | Change |
| --- | ---: | ---: | ---: |
| Moving FPS | 523.24 | 510.70 | -2.40% |
| Standing FPS | 466.13 | 443.29 | -4.90% |
| Standing p95 | 3.1323 ms | 3.2656 ms | +4.26% |
| Standing p99 | 4.0104 ms | 4.0963 ms | +2.14% |
| Standing whole-frame GPU | 1.64244 ms | 1.76267 ms | +0.12023 ms |

Memory and allocations pass, with zero exceptions or collision failures and
4,913 collision regions ready. Streaming settled; nearby LOD0 was first observed
at 1.3 seconds and all streaming at 7.3 seconds. The post-route diagnostic shows
2,602 current and 2,606 peak tufts, zero overflow across 117,614 views, 2 MiB root
storage and 52,120 peak grass triangle submissions across the two draws. This
diagnostic includes the session and is not an isolated route maximum.

`comparison-original.json` also retains the cumulative cost relative to the
original spikes: standing FPS -8.80%, GPU +0.21302 ms, and p95 +12.22%.
That older p95 comparison exceeds its 10% limit; it is not an all-gates pass.
The baseline for this change was explicitly the latest accepted short-tuft run.

`candidate-preflight.json`, `candidate-final.json`, `source.patch.gz` and
`diagnostics.json` retain settings, source hashes and runtime evidence. The
initial final-snapshot console filters matched command names rather than logger
names and returned no matches; `diagnostics.json` records the corrected reads.
`cold-start.log.gz` and `cold-findings.json` preserve the fresh visible editor
launch (PID1804, engine26.09.15). The Sentry marker remained unchanged, C# compile
succeeded with zero errors, and there were no project shader/parser/dispatch or
managed-exception failures. Existing stock-resource errors and a Vulkan cache
rename failure are retained rather than hidden.

The 6/12/24/32 m overhead views show progressive thinning and no central geometry
at 32 m. The fixed low `final-skyline.png` is now inside the taller leaves;
`final-skyline-raised.png` uses the documented supplemental Z735 camera to show
the canopy and view edges. `final-player.png` and `final-player-settled.png` record
the first-person view while restoring normal play. Component-ID property writes
did not persist ThirdPerson=true on readback; setting the player game-object ID
with type PlayerController did. `final-third-person.png` and restored-session.json
record the verified final third-person view near the user's pre-task position,
with free viewport sizing and input enabled. The authored scene was not saved.
