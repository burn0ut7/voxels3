
## SAND-001/v1 — shoreline sand and seeded regions, 2026-09-15

Defined before sand runtime execution. Sand ID6 and the recipe in
[ProceduralSand](Architecture/ProceduralSand.md) are the candidate; unrestricted
inland/deep deposits were removed from the proposal before execution at the
user's request. No density, terrain/water recipe, or field edit changes.

Reuse WATER-FAST-001/v1's frozen1545/checkpoint26/world
f5ce10f3-6d75-428e-b3dd-63dee14891c6, generator48/River14/seed1337,
authored recipe and normal settled spawn. The restored hull control
8b48ba7281764eba9a9dd52e2695782e is the pre-sand measurement; its full source
manifest was checked against the workspace before applying sand (zero differences).
The later rejected water candidate is not a sand baseline. Engine26.09.08b,
Ryzen9800X3D/RTX5090; record fresh process identity after required shader cold start.

Unchanged route: visible basic_example, cells32/base16, LOD0..5, near4/cache8,
gameplay8, visual256, physical1848x960, speed2500, distance50000, one loop,
clearance393.7008, minimum48.86s settled warmup, 10s stationary,240s cap,
one local player, recorder disabled, no edits/manual movement during timing.
Readiness: queues0, waterready, collision4913, prediction320/320. Reassert
logical1232x640 after every camera mode switch and check actual size immediately
before the trigger and in the saved profiler/arrival report.

Criteria: FPS loss<=5%; p95/p99/max frame time, total allocation, process/GPU
memory peaks increase<=10%; preparation<=16.67ms; near arrival<=5s;
drain<=10s, with the baseline's10.746932s drain failure retained explicitly.
No new regression exemption is granted. Zero exceptions, terrain/water pairing
errors, missing/extra seams, overlap/imbalance, and88/88mesh audit success.
Sand adds32palette bytes, with no vertex/edge buffer stride change. Report
generation/streaming counters, fixed-workload comparability and unresolved gates.

Visual qualification after timing: reuse camera(-1800,-1700,800),angles35,47,0,
FOV60,1280x720 and wide Z8000/angles65,47,0. Expect warm pale-yellow banks at
the water contact, without yellow water, missing geometry, or changed edit shape.
Inspect at least one visible transition between sand and existing ground.

Read-only node qualification outside timing: at each XY pair from
x={-2048,-1024,0,1024,2048}, y={-3072,-2048,-1024,0,1024}, obtain the canonical
height using inspect_terrain_column with minimumZ=-512,spacing16,count65.
Use voxel_material_info at z=floor(height/16)*16 and that node minus64,160,400,
plus z=16. Repeat the same queries after a normal Play rebuild and compare IDs.
Require valid IDs, Sand6 at unedited solid shore nodes within the96-unit sand
layer, no Sand6 beyond the384-unit burial limit, and water/air/placed-dirt
precedence. Record missing coverage (including random patches/deposits not
encountered) rather than change the grid to force a pass. Existing edits can
exclude a node from the procedural expectation and must be reported.

Cold start: explicitly compile both generation shaders and the terrain draw
shader, then clean editor restart. Baseline crash marker at
engine/.source2/sentry/last_crash is2026-09-14T05:07:15.416540Z. Require unchanged
marker, live editor, successful compiler status and no fresh shader/parser/
pipeline/dispatch/managed exceptions. Matching source formulas alone do not
establish CPU/GPU parity or multiplayer convergence; report those limits.
