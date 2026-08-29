# Validation Results

This is the single canonical ledger for Voxels3 validation scenarios and their
measured results. It is documentation only; executable test logic, test-only
systems, and alternate feature implementations do not belong here or elsewhere.

Validation must run the shipping production code through the real in-world entry
point used by gameplay. Each run is append-only evidence. Never rewrite or
delete an earlier result because it failed or because a scenario was superseded.

## Rules

1. Define the scenario and its pass criteria before the first run.
2. Give it a stable ID and version, such as `TERRAIN-EDIT-001/v1`.
3. Record every relevant parameter explicitly. Avoid "default," "typical,"
   "random," or "same as before."
4. Reuse the exact parameter set for every baseline, comparison, and regression
   run of that scenario version.
5. Append raw measurements as well as calculated summaries. Link any durable
   capture or log by repository-relative path or stable external identifier.
6. Record failures and incomplete runs; do not cherry-pick.
7. Version a scenario only when an extraordinary, substantive issue makes the
   old definition invalid or impossible. Add the written justification before
   the new definition and preserve all old definitions and results.
8. The existing figure-eight is the primary automated journey and remains the
   sole project-owned test trigger. Read-only production-state observations may
   support it, but direct streaming-origin mutation and diagnostic-only terrain
   or mesh implementations are not valid substitute test paths. Historical
   results that used removed diagnostics remain immutable evidence, not current
   executable scenarios.

## Scenario Definition Template

Copy this section when a real feature is ready for validation. Replace every
placeholder before running it.

```markdown
### <SCENARIO-ID>/v1 — <behavior being validated>

- Production entry point:
- Actual world/scene:
- Behavior and complete expected outcome:
- Metrics and units:
- Pass criteria fixed before execution:
- Parameters:
  - Project/source revision:
  - Engine build:
  - World seed:
  - Coordinates/region:
  - Input values and operation order:
  - Operation count:
  - Warmup and measurement duration:
  - Player/client count:
  - Relevant engine/project settings:
  - Hardware/environment constraints:
  - Other feature-specific fixed values:

#### Run <YYYY-MM-DD HH:MM timezone>

- Executor:
- Project/source state:
- Engine build:
- Hardware/environment:
- Confirmation that scenario parameters were unchanged: yes/no
- Raw measurements:
- Derived measurements:
- Outcome: pass/fail/incomplete
- Evidence location:
- Remaining unmeasured risks:
- Notes:
```

## Scenario Parameter Changes

Record an approved extraordinary change here before adding the new version:

```markdown
### <SCENARIO-ID> v<OLD> to v<NEW>

- Date:
- Substantive issue making the old scenario invalid or impossible:
- Evidence:
- Exact parameter changes:
- Why no unchanged-parameter execution is possible:
- New baseline required: yes
- Comparability warning: results from these versions must not be treated as a
  continuous before/after comparison.
```

### VOXEL-CHUNK-PERF-001 v1 to v2

- Date: `2026-08-28`
- Substantive issue making the old scenario invalid or impossible: a symmetric
  player-centered 3D window does not guarantee that terrain above or below the
  player remains loaded. A mountain taller than `VerticalLoadRadius` disappears
  even when its X/Y column is inside the horizontal view range. Raising the
  vertical radius only moves the failure boundary and wastes chunks as the
  player changes elevation.
- Evidence: architectural review immediately after the v1 run; the v1 desired
  set is explicitly centered on player chunk Z and contains only five vertical
  layers.
- Exact parameter changes: replace `VerticalLoadRadius=2` with a world-anchored
  inclusive range `MinimumLoadedChunkZ=-2` through `MaximumLoadedChunkZ=6`.
  Initial loaded chunks change from `405` to `729`; a one-chunk X shift changes
  retained/unloaded/generated from `360/45/45` to `648/81/81`.
- Why no unchanged-parameter execution is possible: v1 can measure local-box
  throughput but cannot exercise or guarantee full vertical terrain-column
  residency, which is the required behavior.
- New baseline required: yes
- Comparability warning: v1 and v2 results measure different desired-set
  contracts and must not be treated as a continuous before/after comparison.

### VOXEL-CHUNK-PERF-001 v2 to v3

- Date: `2026-08-28`
- Substantive issue making the old scenario invalid or impossible: v2 limits
  production to `8` synchronous chunk constructions per update. Its initial
  `729` chunks therefore require at least `92` update frames and its one-column
  `81`-chunk shift requires at least `11`, even though measured pure generation
  consumed only `14.246 ms` and `1.101 ms`. The configured count is neither a
  frame-time guarantee nor a scalable worker policy.
- Evidence: `VOXEL-CHUNK-PERF-001/v2` recorded `476.177 ms` initial and `41.336
  ms` shift settle time while summed construction was `14.246 ms` and `1.101
  ms`; the delay is dominated by fixed per-frame admission.
- Exact parameter changes: remove `ChunkLoadsPerFrame=8`; generate every missing
  chunk through one serialized background worker pipeline; allow all completed
  results to become eligible immediately, with main-thread dictionary
  integration limited by a `0.500 ms` time budget rather than a chunk count.
  Add worker, integration, observed-frame, and stale-result metrics.
- Why no unchanged-parameter execution is possible: the removed per-frame count
  is the scheduling mechanism being replaced, so v2's settle-time contract no
  longer describes the shipping path.
- New baseline required: yes
- Comparability warning: v2 and v3 use the same spatial workload but different
  scheduling contracts; retain both as separate baselines rather than treating
  their timings as a continuous run series.

### VOXEL-CHUNK-PERF-001 v3 to v4

- Date: `2026-08-28`
- Substantive issue making the old scenario invalid or impossible: v3 starts
  initial population in `OnStart`, so its total-frame metric includes the s&box
  editor-to-play scene transition. The unchanged run measured only `0.198 ms` of
  manager integration but an `83.787 ms` transition frame, while the later live
  streaming shift measured `2.629 ms`. The initial metric cannot distinguish
  engine scene startup from chunk-streaming impact on active gameplay.
- Evidence: the retained `VOXEL-CHUNK-PERF-001/v3` failure at `06:35:01`; official
  s&box component lifecycle documentation specifies async `OnLoad` as the phase
  that keeps the loading screen active until procedural level work completes.
- Exact parameter changes: execute the same single-worker generation and
  time-budgeted integration pipeline from async `VoxelManager.OnLoad` for the
  initial 729 chunks, require that it completes before `OnStart`, and apply the
  `33.333 ms` playable-frame ceiling to the unchanged runtime +X shift. Initial
  loading-screen timing remains recorded but is not classified as a player frame.
- Why no unchanged-parameter execution is possible: v3's initial measurement
  boundary conflates two owners; moving the work to the engine's loading
  lifecycle changes the production entry point and requires a new version.
- New baseline required: yes
- Comparability warning: v3 and v4 share the spatial workload and worker policy,
  but initial settle occurs in different engine lifecycle phases.

### VOXEL-CHUNK-PERF-001 v4 to v5

- Date: `2026-08-28`
- Substantive issue making the old scenario invalid or impossible: v4 uses a
  player-centered horizontal radius but manually configured absolute chunk-Z
  bounds. Those bounds impose an arbitrary world ceiling/floor, do not follow a
  player who climbs or descends, and require a second spatial setting to describe
  what should be one 3D viewer interest volume.
- Evidence: v4 source constructs X/Y relative to the player but iterates absolute
  `MinimumLoadedChunkZ=-2` through `MaximumLoadedChunkZ=6`; player Z changes do
  not rebuild the desired set.
- Exact parameter changes: replace `HorizontalLoadRadius=4`,
  `MinimumLoadedChunkZ=-2`, and `MaximumLoadedChunkZ=6` with one
  `LoadRadius=4`; desired coordinates become the inclusive player-centered cube
  `[-4,+4]` in X, Y, and Z. Add an exact +Z shift after the existing +X shift.
  Each settled set remains `729`; either one-axis shift retains `648`, unloads
  `81`, and generates `81`.
- Why no unchanged-parameter execution is possible: v4 cannot demonstrate
  vertical following because its Z range is deliberately world-anchored and its
  desired set ignores player Z.
- New baseline required: yes
- Comparability warning: v4 and v5 have equal chunk counts at the origin but
  different vertical coordinates and residency semantics.

### VOXEL-STATUS-001 v1 to v2

- Date: `2026-08-28`
- Substantive issue making the old scenario invalid or impossible: v1 fixed
  `LoadRadius=4`, but the actual authored `basic_example` production scene fixes
  `LoadRadius=16`. Executing v1 would require changing the scene configuration
  instead of validating the shipping world as authored.
- Evidence: `Assets/scenes/basic_example.scene` contains `"LoadRadius": 16`; a
  clean production load logged range `C[-16,-16,-16]` through `C[16,16,16]` and
  `35,937` loaded chunks.
- Exact parameter changes: set `LoadRadius=16`; initial loaded/generated chunks
  change from `729/729` to `35,937/35,937`; the +X shift retained/unloaded/
  generated counts change from `648/81/81` to `34,848/1,089/1,089`.
- Why no unchanged-parameter execution is possible: v1 does not match the
  shipping scene, and changing authored configuration solely for validation
  would test a different production setup.
- New baseline required: yes
- Comparability warning: v1 and v2 use different residency volumes and must not
  be treated as a continuous before/after comparison.

## Recorded Scenarios and Runs

### VOXEL-STREAM-001/v1 — initial chunk population and one-chunk stream shift

- Production entry point: `VoxelManager.OnStart` and `VoxelManager.OnUpdate`
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Populate the canonical loaded set
  around chunk `(0,0,0)`, then move the production streaming origin exactly one
  chunk in +X. The world must settle at 75 chunks before and after the move; the
  shift must retain 60 chunks, unload 15, generate 15, and end at center chunk
  `(1,0,0)`. Chunk `(0,0,0)` local sample `(0,0,0)` must equal `0`, and local
  sample `(0,0,1)` must equal `16`, proving actual SDF population rather than
  world loading alone.
- Metrics and units: settled loaded chunks, pending chunks, retained/unloaded/
  generated chunks, loaded samples, raw density bytes, total settle time in
  milliseconds, maximum single chunk generation time in milliseconds, and the
  two fixed density sample values.
- Pass criteria fixed before execution: both settles contain exactly `75` loaded
  and `0` pending chunks; initial loaded samples equal `2,695,275`; raw density
  bytes equal `10,781,100`; the shift reports `60` retained, `15` unloaded, and
  `15` generated chunks; the two density samples are exactly `0` and `16`; no
  compile error, runtime exception, invalid-configuration event, or non-finite
  density occurs. Timing is recorded as a baseline with no pass ceiling in v1.
- Parameters:
  - Project/source revision: working source state at each recorded run
  - Engine build: `26.08.19`
  - World seed: not applicable; v1 generator is the fixed world-space plane
  - Coordinates/region: initial center `(0,0,0)`; shifted center `(1,0,0)`
  - Input values and operation order: start play at world origin; wait for zero
    pending chunks; query fixed samples; move the production streaming origin
    from `(0,0,0)` to `(512,0,0)`; wait for zero pending chunks; query status
  - Operation count: one initial population and one +X chunk-boundary shift
  - Warmup and measurement duration: no warmup; each settle begins when its
    desired set is rebuilt and ends when its pending queue reaches zero
  - Player/client count: `1`
  - Relevant engine/project settings: `32` cells/axis, `16` units/cell,
    horizontal radius `2`, vertical radius `1`, `2` loads/frame, terrain surface
    height `0`, chunk lifecycle detail logging disabled
  - Hardware/environment constraints: record the executing machine and runtime
    environment with the run
  - Other feature-specific fixed values: negative density is solid, iso-surface
    is `0`, X-fastest sample storage, selected sample coordinates `(0,0,0)` and
    `(0,0,1)` in chunk `(0,0,0)`

#### Run 2026-08-28 06:00:41 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `55E69E80DE3A17BFB5741145B65DB981FE4C20343103DEF9220129050ADE2117`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `5F271918B6BDD14FFCA8BFC911CA704E49AD9AE2229817E641263BC75D656E13`
  - Restored `Assets/scenes/basic_example.scene` SHA-256
    `B8D0BC22217437C0968079602A07E0B0B2ECE0B733710DA7A740288FC48B3EE4`
- Engine build: `26.08.19`
- Hardware/environment: Windows 11 Pro 64-bit; AMD Ryzen 7 9800X3D
  (8 cores/16 threads); 31.17 GB RAM; NVIDIA GeForce RTX 5090; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: started `basic_example` through s&box play mode, allowed
  `VoxelManager.OnStart`/`OnUpdate` to settle at the origin, then issued
  `voxel_stream_origin 512 0 0`, which moved the real production streaming origin
  and exercised the normal chunk-boundary streaming path
- Raw measurements:
  - Initial center: `C[0,0,0]`
  - Initial settle: loaded `75`, pending `0`, retained `0`, unloaded `0`,
    generated `75`
  - Initial samples: `2,695,275`; raw density bytes: `10,781,100`
  - Initial settle time: `246.657 ms`; slowest chunk generation: `0.268 ms`
  - Initial probes: `C[0,0,0]/L[0,0,0] = 0` and
    `C[0,0,0]/L[0,0,1] = 16`
  - Shifted center: `C[1,0,0]`
  - Shift settle: loaded `75`, pending `0`, retained `60`, unloaded `15`,
    generated `15`
  - Shift samples: `2,695,275`; raw density bytes: `10,781,100`
  - Shift settle time: `27.217 ms`; slowest chunk generation: `0.035 ms`
  - Shift probes: `C[1,0,0]/L[0,0,0] = 0` and
    `C[1,0,0]/L[0,0,1] = 16`
- Derived measurements:
  - Raw density memory: `10.28 MiB` for the settled 75-chunk set
  - Stream shift reused `80%` of loaded chunks and replaced `20%`
- Outcome: pass
- Evidence location:
  `C:/Program Files (x86)/Steam/steamapps/common/sbox/logs/sbox-dev.log`, entries
  timestamped `2026/08/28 06:00:41.9420` through `06:00:49.5899`
- Remaining unmeasured risks: no surface meshing, collision, live terrain edit,
  persistence, multi-client convergence, dedicated-server load, or overlapping
  multi-player interest-set behavior exists in this slice. Timing is a v1
  baseline and has no pass ceiling.
- Notes: The already-open editable scene contained a user `World` instance with
  noncanonical inspector values. Only the disposable validation setup used the
  fixed v1 parameters; the user's original scene values were restored after play
  before the restored scene hash above was recorded.

### VOXEL-CHUNK-PERF-001/v1 — final player-driven chunk throughput baseline

- Production entry point: `VoxelManager.OnStart`, `VoxelManager.OnUpdate`, the
  configured `Player Controller` streaming target, and the production
  `voxel_stream_origin` diagnostic command
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Start the actual world with the player
  at `(0,0,0)`, populate the full radius-4-horizontal/radius-2-vertical working
  set, then move the configured player target exactly one chunk to `(512,0,0)`.
  The initial world must settle at `405` chunks. The shift must retain `360`,
  unload `45`, generate `45`, settle again at `405`, and preserve the fixed SDF
  probes. The completion event must measure both effective stream throughput and
  pure generation throughput in chunks per second.
- Metrics and units: loaded/pending/retained/unloaded/generated chunks; loaded
  samples; raw density bytes; settle and summed generation milliseconds;
  effective loaded chunks/second; pure generation chunks/second; slowest chunk
  milliseconds; two density probes
- Pass criteria fixed before execution:
  - Initial: exactly `405` loaded, `0` pending, `405` generated,
    `14,554,485` samples, and `58,217,940` raw density bytes
  - Shift: exactly `405` loaded, `0` pending, `360` retained, `45` unloaded, and
    `45` generated
  - Both runs: effective throughput at least `300 chunks/second`, pure generation
    throughput at least `2,000 chunks/second`, and slowest single chunk no more
    than `1.000 ms`
  - Both probes: local `(0,0,0)` density `0` and local `(0,0,1)` density `16`
  - No compile error, runtime exception, invalid configuration, non-finite
    density, missing target, duplicate manager, or rejected diagnostic command
- Parameters:
  - Project/source revision: working source state recorded with the run
  - Engine build: `26.08.19`
  - World seed: not applicable; generator is the fixed world-space plane
  - Coordinates/region: player starts `(0,0,0)` in `C[0,0,0]` and moves once to
    `(512,0,0)` in `C[1,0,0]`
  - Input values and operation order: start play; wait for initial zero pending;
    issue `voxel_stream_origin 512 0 0`; wait for shifted zero pending
  - Operation count: one 405-chunk initial population and one exact +X shift
  - Warmup and measurement duration: no warmup; each measurement starts with the
    desired-set rebuild and ends when pending reaches zero
  - Player/client count: `1`
  - Relevant engine/project settings: `32` cells/axis, `16` units/cell,
    horizontal radius `4`, vertical radius `2`, `8` loads/frame, surface height
    `0`, `Player Controller` streaming target, all overlays and lifecycle detail
    logging disabled
  - Hardware/environment constraints: record with the run
  - Other feature-specific fixed values: negative density solid, iso-surface
    `0`, X-fastest storage, probes `L[0,0,0]` and `L[0,0,1]`

#### Run 2026-08-28 06:09:21 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `55E69E80DE3A17BFB5741145B65DB981FE4C20343103DEF9220129050ADE2117`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `DEE80C688D3A177B6C79DE5F06F00E6F270081137A9E5532C1DA9E72719DE5C9`
  - `Assets/scenes/basic_example.scene` SHA-256
    `60BF98B4D391DAF50B1511550C9027260F296C428AC8092A16774FC3F567149A`
- Engine build: `26.08.19`
- Hardware/environment: Windows 11 Pro build 26200; AMD Ryzen 7 9800X3D
  (8 cores/16 threads); 31.17 GiB RAM; NVIDIA GeForce RTX 5090 driver
  32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: the manager resolved the actual non-proxy
  `Player Controller`, populated around player position `(0,0,0)`, then
  `voxel_stream_origin 512 0 0` moved that same production target across one
  chunk boundary
- Raw measurements:
  - Initial: loaded `405`, pending `0`, generated `405`, samples `14,554,485`,
    density bytes `58,217,940`, settle `302.673 ms`, summed generation
    `8.870 ms`, effective `1,338.077 chunks/second`, pure generation
    `45,658.470 chunks/second`, slowest chunk `0.241 ms`
  - Shift: loaded `405`, pending `0`, retained `360`, unloaded `45`, generated
    `45`, settle `19.756 ms`, summed generation `0.839 ms`, effective
    `2,277.766 chunks/second`, pure generation `53,660.860 chunks/second`,
    slowest chunk `0.189 ms`
  - Both probes: local `L[0,0,0] = 0`; local `L[0,0,1] = 16`
- Derived measurements: `55.52 MiB` raw density memory; X shift reused `88.89%`
  and replaced `11.11%` of chunks
- Outcome: pass, subsequently superseded by v2 vertical-column contract
- Evidence location:
  `C:/Program Files (x86)/Steam/steamapps/common/sbox/logs/sbox-dev.log`, entries
  timestamped `2026/08/28 06:09:21.2068` through `06:09:30.0949`
- Remaining unmeasured risks: v1 does not guarantee terrain outside the
  player-centered five-layer vertical window; no meshing, collision, edits,
  multi-client interest union, or dedicated-server load is included
- Notes: This valid run is retained because every run is append-only evidence.
  The following v2 definition replaces its vertical residency contract.

### VOXEL-CHUNK-PERF-001/v2 — player-driven full terrain-column throughput

- Production entry point: `VoxelManager.OnStart`, `VoxelManager.OnUpdate`, the
  assigned or uniquely resolved local `Player Controller`, and
  `voxel_stream_origin`
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: For every X/Y coordinate inside
  horizontal radius `4`, keep every chunk from world chunk Z `-2` through `6`
  loaded regardless of player elevation. Start the player at `(0,0,0)`, settle
  the complete `9x9x9` column set, then move the actual player target exactly one
  chunk to `(512,0,0)`. The shift must retain `648`, unload `81`, generate `81`,
  and settle again at `729` chunks.
- Metrics and units: loaded/pending/retained/unloaded/generated chunks; loaded
  samples; raw density bytes; settle and summed generation milliseconds;
  effective and pure-generation chunks/second; slowest chunk milliseconds; fixed
  SDF probes
- Pass criteria fixed before execution:
  - Initial: exactly `729` loaded, `0` pending, `729` generated,
    `26,198,073` samples, and `104,792,292` raw density bytes
  - Shift: exactly `729` loaded, `0` pending, `648` retained, `81` unloaded, and
    `81` generated
  - Both runs: effective throughput at least `300 chunks/second`, pure generation
    throughput at least `2,000 chunks/second`, and slowest chunk no more than
    `1.000 ms`
  - Both probes: local `(0,0,0)` density `0` and `(0,0,1)` density `16`
  - No compile error, runtime exception, invalid configuration, non-finite
    density, target-resolution rejection, duplicate manager, or rejected command
- Parameters:
  - Project/source revision: working source state recorded with the run
  - Engine build: `26.08.19`
  - World seed: not applicable; fixed world-space plane
  - Coordinates/region: player `(0,0,0)` to `(512,0,0)`; inclusive world chunk Z
    range `-2..6`, covering world Z `-1024..3584` units
  - Input values and operation order: start play; wait for zero pending; issue
    `voxel_stream_origin 512 0 0`; wait for zero pending
  - Operation count: one 729-chunk population and one exact +X shift
  - Warmup and measurement duration: no warmup; desired-set rebuild through zero
    pending for each measurement
  - Player/client count: `1`
  - Relevant engine/project settings: `32` cells/axis, `16` units/cell,
    horizontal radius `4`, minimum chunk Z `-2`, maximum chunk Z `6`, `8`
    loads/frame, surface height `0`, real Player Controller target, overlays and
    lifecycle detail logging disabled
  - Hardware/environment constraints: record with the run
  - Other feature-specific fixed values: negative density solid, iso-surface
    `0`, X-fastest storage, probes `L[0,0,0]` and `L[0,0,1]`

#### Run 2026-08-28 06:13:02 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `55E69E80DE3A17BFB5741145B65DB981FE4C20343103DEF9220129050ADE2117`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `537452364F094E2B317F576B8EE5A0A29E870EDC0E717151844490E1703E50D9`
  - `Assets/scenes/basic_example.scene` SHA-256
    `3F81076B0973B3DDE6192C4EE9807869C7716BBEA7D32EB7DA86C38E82673585`
- Engine build: `26.08.19`
- Hardware/environment: Windows 11 Pro build 26200; AMD Ryzen 7 9800X3D
  (8 cores/16 threads); approximately 32 GiB RAM; NVIDIA GeForce RTX 5090
  driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: the manager resolved the actual non-proxy
  `Player Controller`, populated all configured world-Z chunks around player X/Y
  position `(0,0,0)`, then `voxel_stream_origin 512 0 0` moved that same
  production target across one X chunk boundary
- Raw measurements:
  - Initial: loaded `729`, pending `0`, generated `729`, samples `26,198,073`,
    density bytes `104,792,292`, settle `476.177 ms`, summed generation
    `14.246 ms`, effective `1,530.944 chunks/second`, pure generation
    `51,172.650 chunks/second`, slowest chunk `0.213 ms`
  - Shift: loaded `729`, pending `0`, retained `648`, unloaded `81`, generated
    `81`, settle `41.336 ms`, summed generation `1.101 ms`, effective
    `1,959.546 chunks/second`, pure generation `73,542.770 chunks/second`,
    slowest chunk `0.028 ms`
  - Both probes: local `L[0,0,0] = 0`; local `L[0,0,1] = 16`
- Derived measurements: `99.94 MiB` raw density memory; the X shift reused
  `88.89%` and replaced `11.11%` of chunks; replacing `81` chunks proves the
  move streamed one complete nine-chunk vertical slab rather than partial
  player-centered Z coverage
- Build verification: `dotnet build voxels3.slnx --nologo` succeeded with
  `0` warnings and `0` errors; scene JSON parsed successfully and contained
  exactly one `VoxelManager`
- Outcome: pass
- Evidence location:
  `C:/Program Files (x86)/Steam/steamapps/common/sbox/logs/sbox-dev.log`, entries
  timestamped `2026/08/28 06:13:02` through `06:13:15`
- Remaining unmeasured risks: the explicit vertical world envelope must be
  expanded if authored/procedural terrain exceeds chunk Z `-2..6`; no meshing,
  collision, live edits, multi-client interest union, or dedicated-server load
  is included in this chunk-storage throughput slice
- Notes: This is the canonical chunk-specific performance baseline. It measures
  the production world's actual chunk creation and streaming path without a
  separate harness, test scene, test system, or altered runtime parameters.

### VOXEL-DEBUG-001/v1 — player-centered chunk diagnostics

- Production entry point: `VoxelManager.OnStart`, `VoxelManager.OnUpdate`, the
  assigned `Player Controller`, `voxel_player_chunk`, and `voxel_chunk_info`
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Start the player at world position
  `(0,0,0)`, settle the production chunk stream, report the chunk containing the
  actual player, retrieve that loaded chunk's data directly, then request one
  known-unloaded chunk. Human-facing inspector state must identify the player's
  chunk without any manually selected chunk, local sample, or cell-slice input.
- Metrics and units: player world position and chunk identity; loaded/pending
  chunks; chunk cells/axis, samples/axis, sample count, density bytes, density
  minimum/maximum; missing-query result count
- Pass criteria fixed before execution:
  - Stream settles at exactly `729` loaded and `0` pending chunks
  - Player position `(0,0,0)` resolves to human-readable `Chunk X 0, Y 0, Z 0`
    and stable log identifier `C[0,0,0]`
  - Current chunk reports loaded, `32` cells/axis, `33` samples/axis, `35,937`
    samples, `143,748` density bytes, density minimum `0`, and maximum `512`
  - Direct lookup of `C[0,0,0]` returns the same production chunk data
  - Direct lookup of `C[99,99,0]` reports missing with loaded count `729`
  - No selected-chunk coordinate, selected-local-sample, selected-cell-slice,
    or selected-cell-overlay property remains in source or scene serialization
  - No compile error, runtime exception, duplicate manager, unresolved player,
    rejected command, or non-finite reported value
- Parameters:
  - Project/source revision: working source state recorded with the run
  - Engine build: `26.08.19`
  - World seed: not applicable; fixed world-space plane
  - Coordinates/region: player `(0,0,0)`; loaded lookup `C[0,0,0]`; missing
    lookup `C[99,99,0]`
  - Input values and operation order: start play; wait for `729` loaded and `0`
    pending; invoke `voxel_player_chunk`; invoke `voxel_chunk_info 0 0 0`; invoke
    `voxel_chunk_info 99 99 0`
  - Operation count: one player query and two explicit chunk queries
  - Warmup and measurement duration: initial stream through zero pending before
    all three queries; no additional warmup
  - Player/client count: `1`
  - Relevant engine/project settings: `32` cells/axis, `16` units/cell,
    horizontal radius `4`, chunk Z `-2..6`, `8` loads/frame, surface height `0`,
    assigned Player Controller target, overlays and lifecycle logging disabled
  - Hardware/environment constraints: record with the run
  - Other feature-specific fixed values: negative density solid, iso-surface
    `0`, X-fastest storage

#### Run 2026-08-28 06:24:12 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `55E69E80DE3A17BFB5741145B65DB981FE4C20343103DEF9220129050ADE2117`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `DF39B3F6FEBF393DD064E8E6080CEC302C3B8C0092B1744A96C14CDA6BD440F8`
  - `Assets/scenes/basic_example.scene` SHA-256
    `EBB810C81A2DD31D771FB84D87E8E960648E36C89636821A0A82C2693961180D`
- Engine build: `26.08.19`
- Hardware/environment: Windows 11 Pro build 26200; AMD Ryzen 7 9800X3D
  (8 cores/16 threads); approximately 32 GiB RAM; NVIDIA GeForce RTX 5090
  driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes. The player began
  at the fixed `(0,0,0)` scene position; ordinary production physics placed the
  live query at `(0,0,0.041249998)` without leaving `C[0,0,0]`.
- Exact execution path: start `basic_example`; wait for stream completion; run
  `voxel_player_chunk`; run `voxel_chunk_info 0 0 0`; run
  `voxel_chunk_info 99 99 0`; stop play
- Raw measurements:
  - Stream: loaded `729`, pending `0`, samples `26,198,073`, density bytes
    `104,792,292`, settle `537.404 ms`
  - Player query: target `Player Controller`, position `(0,0,0.041249998)`,
    chunk `C[0,0,0]`, readable name `Chunk X 0, Y 0, Z 0`
  - Current/direct loaded queries: `32` cells/axis, `33` samples/axis, `35,937`
    samples, `143,748` density bytes, density minimum `0`, maximum `512`
  - Missing query: `C[99,99,0]` reported missing with loaded count `729`
  - Deprecated debug-property occurrence count across manager source and scene:
    `0` for all six fixed names
- Build verification: `dotnet build voxels3.slnx --nologo` succeeded with
  `0` warnings and `0` errors; scene JSON parsed successfully and contained
  exactly one `VoxelManager`
- Outcome: pass
- Evidence location: live s&box console entries timestamped `2026/08/28
  06:24:12` through `06:24:20`; immutable source state and measurements are
  retained in this ledger entry
- Remaining unmeasured risks: chunk queries currently cover loaded-memory
  diagnostics only; no meshing, collision, live edits, persistence, remote
  client query, or dedicated-server interest-set diagnostics exist yet
- Notes: The validation invoked the shipping manager and its real loaded chunk
  dictionary in the playable world. No test file, test scene, test component,
  test-only hook, alternate implementation, or changed query coordinate was
  used.

### VOXEL-CHUNK-PERF-001/v3 — background full-column streaming

- Production entry point: `VoxelManager.OnStart`, `VoxelManager.OnUpdate`, the
  assigned `Player Controller`, the component-scoped worker pipeline, and
  `voxel_stream_origin`
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Start the actual player at `(0,0,0)`,
  generate and integrate the complete `9x9x9` working set without a per-frame
  chunk count, then move that player target exactly one chunk to `(512,0,0)`.
  Generation must remain off the main thread; loaded-world mutation must remain
  on the main thread; the shift must retain `648`, unload `81`, generate `81`,
  and settle at `729` chunks.
- Metrics and units: loaded/pending/retained/unloaded/generated/stale chunks;
  samples and density bytes; settle, background-worker, summed-generation,
  total-integration, and slowest-integration-frame milliseconds; maximum observed
  frame milliseconds; effective and generation chunks/second; fixed SDF probes
- Pass criteria fixed before execution:
  - Initial: `729` loaded, `0` pending, `729` generated, `26,198,073` samples,
    `104,792,292` density bytes, and `0` stale results
  - Shift: `729` loaded, `0` pending, `648` retained, `81` unloaded, `81`
    generated, and `0` stale results
  - Initial settles within `250.000 ms`; shift settles within `100.000 ms`
  - Both runs: effective throughput at least `300 chunks/second`, generation
    throughput at least `2,000 chunks/second`, slowest integration frame no more
    than `1.000 ms`, and maximum observed frame no more than `33.333 ms`
  - Both probes: local `(0,0,0)` density `0` and `(0,0,1)` density `16`
  - Exactly one background generation pipeline is active; no `ChunkLoadsPerFrame`
    property or serialized value remains
  - No compile error, runtime exception, invalid configuration, non-finite
    density, target-resolution rejection, duplicate manager, or rejected command
- Parameters:
  - Project/source revision: working source state recorded with the run
  - Engine build: `26.08.19`
  - World seed: not applicable; fixed world-space plane
  - Coordinates/region: player `(0,0,0)` to `(512,0,0)`; inclusive world chunk Z
    range `-2..6`, covering world Z `-1024..3584` units
  - Input values and operation order: start play; wait for zero pending; issue
    `voxel_stream_origin 512 0 0`; wait for zero pending
  - Operation count: one 729-chunk population and one exact +X shift
  - Warmup and measurement duration: no warmup; desired-set rebuild through zero
    pending for each measurement
  - Player/client count: `1`
  - Relevant engine/project settings: `32` cells/axis, `16` units/cell,
    horizontal radius `4`, chunk Z `-2..6`, surface height `0`, assigned Player
    Controller target, one serialized worker pipeline, `0.500 ms` main-thread
    integration budget, overlays and lifecycle detail logging disabled
  - Hardware/environment constraints: record with the run
  - Other feature-specific fixed values: negative density solid, iso-surface
    `0`, X-fastest storage, probes `L[0,0,0]` and `L[0,0,1]`

#### Run 2026-08-28 06:35:01 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `55E69E80DE3A17BFB5741145B65DB981FE4C20343103DEF9220129050ADE2117`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `ED095284D487F057AD7874ABC30719A2499220719DC05D2C95A879BCC5AD0D4E`
  - `Assets/scenes/basic_example.scene` SHA-256
    `40771E9087D752FD65AF673861924421674816D012B868DECA58C4CE3C3D31D2`
- Engine build: `26.08.19`
- Hardware/environment: Windows 11 Pro build 26200; AMD Ryzen 7 9800X3D
  (8 cores/16 threads); approximately 32 GiB RAM; NVIDIA GeForce RTX 5090
  driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: start `basic_example`; wait for the background initial
  population and zero pending; issue `voxel_stream_origin 512 0 0`; wait for the
  background shift and zero pending
- Raw measurements:
  - Initial: loaded `729`, pending `0`, generated `729`, stale `0`, samples
    `26,198,073`, density bytes `104,792,292`, settle `60.292 ms`, worker
    `13.830 ms`, summed generation `13.794 ms`, integration `0.198 ms`, slowest
    integration frame `0.198 ms`, maximum observed frame `83.787 ms`, effective
    `12,091.140 chunks/second`, generation `52,849.060 chunks/second`, slowest
    chunk `0.192 ms`
  - Shift: loaded `729`, pending `0`, retained `648`, unloaded `81`, generated
    `81`, stale `0`, settle `6.993 ms`, worker `1.447 ms`, summed generation
    `1.442 ms`, integration `0.015 ms`, slowest integration frame `0.015 ms`,
    maximum observed frame `2.629 ms`, effective `11,583.840 chunks/second`,
    generation `56,187.570 chunks/second`, slowest chunk `0.050 ms`
  - Both probes: local `L[0,0,0] = 0`; local `L[0,0,1] = 16`
- Derived measurements: compared with v2 as non-continuous context only, initial
  settle decreased from `476.177` to `60.292 ms` (`87.34%`) and runtime shift
  settle decreased from `41.336` to `6.993 ms` (`83.08%`)
- Outcome: fail. Every correctness, throughput, settle, integration, and runtime
  shift-frame criterion passed, but the initial editor-to-play transition frame
  was `83.787 ms`, exceeding the fixed `33.333 ms` maximum.
- Evidence location: live s&box console entries timestamped `2026/08/28
  06:35:01` through `06:35:08`
- Remaining unmeasured risks: the maximum-frame metric combines s&box's
  editor-to-play scene transition with initial population and cannot attribute
  that startup frame to chunk work; stale cancellation under rapid movement,
  procedural terrain, meshing, collision, and multi-client contention remain
  unmeasured
- Notes: The failure is retained. The runtime streaming shift—the playable case
  after scene startup—stayed within `2.629 ms`, while measured manager
  integration itself stayed within `0.198 ms` initially and `0.015 ms` during
  the shift.

### VOXEL-CHUNK-PERF-001/v4 — loading-phase population and live background shift

- Production entry point: async `VoxelManager.OnLoad`, `VoxelManager.OnStart`,
  `VoxelManager.OnUpdate`, the assigned `Player Controller`, the one serialized
  worker pipeline, and `voxel_stream_origin`
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Generate and integrate the initial
  `9x9x9` working set through the canonical worker while s&box holds the loading
  screen; enter active play with `729` chunks and zero pending; then move the
  actual player target exactly one chunk from `(0,0,0)` to `(512,0,0)`. The live
  shift must retain `648`, unload `81`, generate `81`, and settle at `729`.
- Metrics and units: lifecycle phase; loaded/pending/retained/unloaded/generated/
  stale chunks; samples and density bytes; loading/shift settle, worker,
  generation, integration, and slowest-integration milliseconds; runtime maximum
  observed frame milliseconds; throughput; fixed SDF probes
- Pass criteria fixed before execution:
  - `OnLoad` completes the initial `729` chunks with `0` pending, `729` generated,
    `0` stale, `26,198,073` samples, and `104,792,292` density bytes before
    `OnStart`; loading-phase settle remains within `250.000 ms`
  - Runtime shift finishes with `729` loaded, `0` pending, `648` retained, `81`
    unloaded, `81` generated, and `0` stale within `100.000 ms`
  - Runtime shift: effective throughput at least `300 chunks/second`, generation
    throughput at least `2,000 chunks/second`, slowest integration frame no more
    than `1.000 ms`, and maximum observed player frame no more than `33.333 ms`
  - Both phases: local `(0,0,0)` density `0` and `(0,0,1)` density `16`
  - Initial generation does not restart in `OnStart`; exactly one background
    pipeline is active; `ChunkLoadsPerFrame` remains absent
  - No compile error, runtime exception, invalid configuration, non-finite
    density, target-resolution rejection, duplicate manager, or rejected command
- Parameters:
  - Project/source revision: working source state recorded with the run
  - Engine build: `26.08.19`
  - World seed: not applicable; fixed world-space plane
  - Coordinates/region: player `(0,0,0)` to `(512,0,0)`; world chunk Z `-2..6`
  - Input values and operation order: start play and allow `OnLoad` to complete;
    confirm active play begins settled; issue `voxel_stream_origin 512 0 0`;
    wait for zero pending
  - Operation count: one 729-chunk loading-phase population and one exact +X
    live shift
  - Warmup and measurement duration: no warmup; `OnLoad` start through initial
    completion, then live shift rebuild through zero pending
  - Player/client count: `1`
  - Relevant engine/project settings: `32` cells/axis, `16` units/cell,
    horizontal radius `4`, chunk Z `-2..6`, surface height `0`, assigned Player
    Controller, one worker pipeline, `0.500 ms` integration budget, overlays and
    lifecycle logging disabled
  - Hardware/environment constraints: record with the run
  - Other feature-specific fixed values: negative density solid, iso-surface
    `0`, X-fastest storage, probes `L[0,0,0]` and `L[0,0,1]`

#### Run 2026-08-28 06:38:52 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `55E69E80DE3A17BFB5741145B65DB981FE4C20343103DEF9220129050ADE2117`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `1E0CD1B476A442A84B0B113B79DA2F755FAF02B2AD1EDB4CD05A93DF4D5B3338`
  - `Assets/scenes/basic_example.scene` SHA-256
    `40771E9087D752FD65AF673861924421674816D012B868DECA58C4CE3C3D31D2`
- Engine build: `26.08.19`
- Hardware/environment: Windows 11 Pro build 26200; AMD Ryzen 7 9800X3D
  (8 cores/16 threads); approximately 32 GiB RAM; NVIDIA GeForce RTX 5090
  driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: start `basic_example`; async `OnLoad` resolves the real
  Player Controller and completes the initial worker/integration pipeline; active
  play begins; issue `voxel_stream_origin 512 0 0`; wait for zero pending; stop
- Raw measurements:
  - Loading phase: ready `true`, loaded `729`, pending `0`, generated `729`, stale
    `0`, samples `26,198,073`, density bytes `104,792,292`, settle `15.848 ms`,
    worker `9.726 ms`, summed generation `9.690 ms`, integration `0.103 ms`,
    slowest integration update `0.103 ms`, effective `45,998.330 chunks/second`,
    generation `75,229.110 chunks/second`, slowest chunk `0.197 ms`
  - Live shift: loaded `729`, pending `0`, retained `648`, unloaded `81`,
    generated `81`, stale `0`, settle `7.970 ms`, worker `1.104 ms`, summed
    generation `1.100 ms`, integration `0.016 ms`, slowest integration frame
    `0.016 ms`, maximum observed player frame `4.167 ms`, effective
    `10,163.370 chunks/second`, generation `73,649.760 chunks/second`, slowest
    chunk `0.029 ms`
  - Both probes: local `L[0,0,0] = 0`; local `L[0,0,1] = 16`
- Derived measurements: as non-continuous context versus v2, loading-phase
  population settled `96.67%` faster and the live one-column shift settled
  `80.72%` faster; the live shift used `0.016 ms` of measured main-thread
  integration within a `4.167 ms` observed player frame
- Build verification: `dotnet build voxels3.slnx --nologo` succeeded with `0`
  warnings and `0` errors; scene JSON parsed successfully, contained exactly one
  `VoxelManager`, and production code/scene contained zero `ChunkLoadsPerFrame`
  occurrences
- Outcome: pass
- Evidence location: live s&box console entries timestamped `2026/08/28
  06:38:52` through `06:39:00`; immutable source state and raw results are
  retained in this ledger entry
- Remaining unmeasured risks: rapid-movement stale cancellation is implemented
  but not forced by this extremely fast flat-field workload; procedural terrain,
  meshing, collision, GC tail behavior over long traversal, multiple local
  players, and dedicated-server contention remain unmeasured
- Notes: Initial and runtime generation use the same production worker and
  integration queues. No test file, test scene, test component, test-only hook,
  fallback implementation, or altered scenario input was used.

### VOXEL-CHUNK-PERF-001/v5 — viewer-centered 3D load radius

- Production entry point: async `VoxelManager.OnLoad`, `VoxelManager.OnStart`,
  `VoxelManager.OnUpdate`, the assigned `Player Controller`, the serialized
  worker pipeline, and `voxel_stream_origin`
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: With one `LoadRadius=4`, populate the
  inclusive `9x9x9` chunk cube centered on player chunk `C[0,0,0]`, move the
  actual player exactly one chunk to `(512,0,0)`, then exactly one chunk upward
  to `(512,0,512)`. Every phase must settle at `729`; both one-axis shifts must
  retain `648`, unload `81`, and generate `81`. The loaded Z range must follow
  the player from `-4..4` to `-3..5` after the vertical shift.
- Metrics and units: player/streaming chunk; loaded X/Y/Z extents; loaded,
  pending, retained, unloaded, generated, and stale chunks; samples and density
  bytes; settle, worker, generation, integration, slowest-integration, and
  maximum-frame milliseconds; throughput; SDF probes
- Pass criteria fixed before execution:
  - Loading phase: center `C[0,0,0]`, loaded range X/Y/Z `-4..4`, exactly `729`
    loaded, `0` pending, `729` generated, `0` stale, `26,198,073` samples, and
    `104,792,292` density bytes within `250.000 ms`
  - +X shift: center `C[1,0,0]`, range X `-3..5`, Y/Z `-4..4`, `729` loaded,
    `0` pending, `648` retained, `81` unloaded, `81` generated, and `0` stale
  - +Z shift: center `C[1,0,1]`, range X `-3..5`, Y `-4..4`, Z `-3..5`, `729`
    loaded, `0` pending, `648` retained, `81` unloaded, `81` generated, and `0`
    stale; `C[1,0,5]` is loaded and `C[1,0,-4]` is missing
  - Both live shifts settle within `100.000 ms`, effective throughput is at least
    `300 chunks/second`, generation throughput is at least `2,000 chunks/second`,
    slowest integration frame is no more than `1.000 ms`, and maximum observed
    player frame is no more than `33.333 ms`
  - Initial and +X probes: local densities `L[0,0,0]=0` and `L[0,0,1]=16`; +Z
    probes: `L[0,0,0]=512` and `L[0,0,1]=528`
  - `HorizontalLoadRadius`, `MinimumLoadedChunkZ`, and `MaximumLoadedChunkZ` are
    absent from production source and scene; exactly one `LoadRadius` setting
    owns all three axes
  - No compile error, runtime exception, invalid configuration, non-finite
    density, target-resolution rejection, duplicate manager, rejected command,
    or background-pipeline overlap
- Parameters:
  - Project/source revision: working source state recorded with the run
  - Engine build: `26.08.19`
  - World seed: not applicable; fixed world-space plane
  - Coordinates/region: player `(0,0,0)` to `(512,0,0)` to `(512,0,512)`;
    `LoadRadius=4`
  - Input values and operation order: start play and allow `OnLoad` to complete;
    issue `voxel_stream_origin 512 0 0`; wait for zero pending; issue
    `voxel_stream_origin 512 0 512`; wait for zero pending; query
    `voxel_chunk_info 1 0 5` and `voxel_chunk_info 1 0 -4`
  - Operation count: one 729-chunk population, two one-axis shifts, two direct
    chunk queries
  - Warmup and measurement duration: no warmup; each desired-set rebuild through
    zero pending
  - Player/client count: `1`
  - Relevant engine/project settings: `32` cells/axis, `16` units/cell,
    `LoadRadius=4`, surface height `0`, assigned Player Controller, one worker
    pipeline, `0.500 ms` integration budget, overlays/lifecycle logging disabled
  - Hardware/environment constraints: record with the run
  - Other feature-specific fixed values: negative density solid, iso-surface
    `0`, X-fastest storage

#### Run 2026-08-28 06:47:39 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `55E69E80DE3A17BFB5741145B65DB981FE4C20343103DEF9220129050ADE2117`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `08442CBABEF87854F05DC34B96D56752B22170F153708C89A899EFE654B7AD90`
  - `Assets/scenes/basic_example.scene` SHA-256
    `2AAD63033329ED86AC57B0A287DC80CD8DD31BD2F238C54E3E24606993B6D4AD`
- Engine build: `26.08.19`
- Hardware/environment: Windows 11 Pro build 26200; AMD Ryzen 7 9800X3D
  (8 cores/16 threads); approximately 32 GiB RAM; NVIDIA GeForce RTX 5090
  driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: start `basic_example`; async `OnLoad` completes the
  initial production worker/integration pipeline; issue
  `voxel_stream_origin 512 0 0`; wait for zero pending; issue
  `voxel_stream_origin 512 0 512`; wait for zero pending; issue
  `voxel_chunk_info 1 0 5`; issue `voxel_chunk_info 1 0 -4`; stop play
- Raw measurements:
  - Loading phase: center `C[0,0,0]`, range minimum `C[-4,-4,-4]`, range maximum
    `C[4,4,4]`, ready `true`, loaded `729`, pending `0`, generated `729`, stale
    `0`, samples `26,198,073`, density bytes `104,792,292`, settle `15.608 ms`,
    worker `11.391 ms`, summed generation `11.350 ms`, integration `0.121 ms`,
    slowest integration update `0.121 ms`, effective `46,706.220 chunks/second`,
    generation `64,231.420 chunks/second`, slowest chunk `0.203 ms`; probes
    `L[0,0,0]=0` and `L[0,0,1]=16`
  - +X shift: center `C[1,0,0]`, range minimum `C[-3,-4,-4]`, range maximum
    `C[5,4,4]`, loaded `729`, pending `0`, retained `648`, unloaded `81`,
    generated `81`, stale `0`, settle `8.527 ms`, worker `1.246 ms`, summed
    generation `1.243 ms`, integration `0.015 ms`, slowest integration frame
    `0.015 ms`, maximum observed player frame `4.747 ms`, effective
    `9,498.792 chunks/second`, generation `65,175.410 chunks/second`, slowest
    chunk `0.046 ms`; probes `L[0,0,0]=0` and `L[0,0,1]=16`
  - +Z shift: center `C[1,0,1]`, range minimum `C[-3,-4,-3]`, range maximum
    `C[5,4,5]`, loaded `729`, pending `0`, retained `648`, unloaded `81`,
    generated `81`, stale `0`, settle `7.516 ms`, worker `1.208 ms`, summed
    generation `1.202 ms`, integration `0.010 ms`, slowest integration frame
    `0.010 ms`, maximum observed player frame `3.917 ms`, effective
    `10,776.720 chunks/second`, generation `67,365.260 chunks/second`, slowest
    chunk `0.037 ms`; probes `L[0,0,0]=512` and `L[0,0,1]=528`
  - Extent queries: `C[1,0,5]` loaded with `35,937` samples, `143,748` density
    bytes, density minimum `2560`, and density maximum `3072`; `C[1,0,-4]`
    reported missing with loaded count `729`
- Build verification: `dotnet build voxels3.slnx --nologo` succeeded in `1.14`
  seconds with `0` warnings and `0` errors; editor compiler reported success with
  `0` errors; scene JSON parsed successfully and contained exactly one
  `VoxelManager`; production source and scene contained zero occurrences of all
  three removed configuration names and scene `LoadRadius` was exactly `4`;
  `git diff --check` reported no whitespace errors
- Outcome: pass
- Evidence location: live s&box console entries timestamped `2026/08/28
  06:47:39` through `06:48:01`; immutable source state and raw measurements are
  retained in this ledger entry
- Remaining unmeasured risks: a cubic radius grows as `(2r+1)^3`, so larger
  configured radii need their own versioned memory and traversal scenario;
  procedural terrain, meshing, collision, rapid-movement cancellation,
  multi-origin server interest management, and multiplayer contention remain
  unmeasured
- Notes: The scenario ran only the shipping manager against its real playable
  world and loaded chunk dictionary. No test project, test file, test scene,
  test-only component, hook, mock, fallback, or altered parameter was used.

### VOXEL-CHUNK-PERF-001/v6 — implicit flat-SDF storage

- Production entry point: async `VoxelManager.OnLoad`, `VoxelManager.OnStart`,
  `VoxelManager.OnUpdate`, the assigned Player Controller, the serialized worker
  pipeline, `voxel_stream_origin`, and `voxel_chunk_info`
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Execute the same initial `9x9x9`
  population, exact +X chunk shift, exact +Z chunk shift, and extent queries as
  v5. Establish a dense-array baseline with added production allocation/storage
  telemetry, then replace redundant arrays with one implicit representation of
  the current flat SDF and rerun without changing inputs.
- Metrics and units: loaded logical chunks; chunks with density payloads; logical
  sample count; loaded and newly allocated density-payload bytes; settle,
  worker, generation, integration, slowest-integration, and maximum-frame
  milliseconds; effective and generation chunks/second; exact density probes
- Pass criteria fixed before execution:
  - Every phase settles with `729` logical chunks, `0` pending, `0` stale, and
    `26,198,073` logical samples; each shift retains `648`, unloads `81`, and
    generates `81`
  - Optimized storage has exactly `0` chunks with density payloads and `0`
    density-payload bytes in every phase
  - Initial and each live shift reduce newly allocated density-payload bytes by
    at least `95%` from the dense baseline for the same phase
  - Initial settles within `250.000 ms`; both live shifts settle within
    `100.000 ms`, achieve at least `300 effective chunks/second` and `2,000
    generation chunks/second`, keep slowest integration at or below `1.000 ms`,
    and keep maximum observed player frame at or below `33.333 ms`
  - Initial and +X probes remain exactly `L[0,0,0]=0` and `L[0,0,1]=16`; +Z
    probes remain exactly `L[0,0,0]=512` and `L[0,0,1]=528`
  - Extent query `C[1,0,5]` remains loaded with logical sample count `35,937`,
    density minimum `2560`, and density maximum `3072`; `C[1,0,-4]` remains
    missing
  - No compile error, runtime exception, invalid configuration, non-finite
    density, target-resolution rejection, duplicate manager, rejected command,
    or background-pipeline overlap
- Parameters:
  - Project/source revision: baseline and optimized source hashes recorded with
    their runs
  - Engine build: `26.08.19`
  - World seed: not applicable; fixed world-space plane
  - Coordinates/region: player `(0,0,0)` to `(512,0,0)` to `(512,0,512)`;
    `LoadRadius=4`
  - Input values and operation order: start play and allow `OnLoad` to complete;
    issue `voxel_stream_origin 512 0 0`; wait for zero pending; issue
    `voxel_stream_origin 512 0 512`; wait for zero pending; issue
    `voxel_chunk_info 1 0 5`; issue `voxel_chunk_info 1 0 -4`; stop play
  - Operation count: one 729-chunk population, two one-axis shifts, two direct
    chunk queries per run
  - Warmup and measurement duration: no warmup; each desired-set rebuild through
    zero pending
  - Player/client count: `1`
  - Relevant engine/project settings: `32` cells/axis, `16` units/cell,
    `LoadRadius=4`, surface height `0`, assigned Player Controller, one worker
    pipeline, `0.500 ms` integration budget, overlays/lifecycle logging disabled
  - Hardware/environment constraints: record with each run
  - Other fixed values: negative density solid, positive density air, zero
    surface, X-fastest logical indexing, exact probes above

#### Dense baseline 2026-08-28 06:55:43 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `15705A031C51186AD77FC3ACD68DAF3C01449B00B3D4DB7A21F0D5E084E24D71`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `E063288CA5AA58646EA384840A8CE9709813ACA310B726DE5092DE75471DFB87`
  - `Assets/scenes/basic_example.scene` SHA-256
    `2AAD63033329ED86AC57B0A287DC80CD8DD31BD2F238C54E3E24606993B6D4AD`
- Engine build and environment: `26.08.19`; Windows 11 Pro build 26200;
  AMD Ryzen 7 9800X3D (8 cores/16 threads); approximately 32 GiB RAM;
  NVIDIA GeForce RTX 5090 driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Raw measurements:
  - Initial: loaded `729`, payload chunks `729`, logical samples `26,198,073`,
    loaded density bytes `104,792,292`, newly allocated density bytes
    `104,792,292`, settle `17.969 ms`, worker `6.854 ms`, generation `6.824 ms`,
    integration `0.093 ms`, effective `40,568.960 chunks/second`, generation
    `106,828.700 chunks/second`; probes `0` and `16`
  - +X: loaded `729`, retained `648`, unloaded/generated `81`, payload chunks
    `729`, loaded density bytes `104,792,292`, newly allocated density bytes
    `11,643,588`, settle `7.755 ms`, worker `0.639 ms`, generation `0.635 ms`,
    integration `0.014 ms`, maximum frame `2.940 ms`, effective `10,445.410
    chunks/second`, generation `127,478.700 chunks/second`; probes `0` and `16`
  - +Z: loaded `729`, retained `648`, unloaded/generated `81`, payload chunks
    `729`, loaded density bytes `104,792,292`, newly allocated density bytes
    `11,643,588`, settle `6.726 ms`, worker `0.641 ms`, generation `0.637 ms`,
    integration `0.009 ms`, maximum frame `2.933 ms`, effective `12,043.000
    chunks/second`, generation `127,158.500 chunks/second`; probes `512` and
    `528`
  - Extent queries: `C[1,0,5]` loaded with `35,937` samples, `143,748` density
    bytes, minimum `2560`, maximum `3072`; `C[1,0,-4]` missing
- Outcome: baseline recorded; optimization criteria are evaluated by the next
  run, not by this dense representation
- Evidence location: live s&box console entries timestamped `2026/08/28
  06:55:43` through `06:56:04`
- Notes: `GC.GetAllocatedBytesForCurrentThread` was rejected by the s&box API
  whitelist before this scenario began and was removed. Newly allocated density
  bytes are instead measured exactly from the production chunks constructed by
  the worker. No unsupported GC API or synthetic profiler path remains.

#### Optimized run 2026-08-28 06:57:38 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `5618A9B13EF8EF20BF6A4FD2BC4CDEEB6103D6188C170584736C37B9ED3C1EE2`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `7B646E404AF438AE9564221C195433D97A5E88B29B0079BF7EF3D19A0110B9EF`
  - `Assets/scenes/basic_example.scene` SHA-256
    `2AAD63033329ED86AC57B0A287DC80CD8DD31BD2F238C54E3E24606993B6D4AD`
- Engine build and environment: `26.08.19`; Windows 11 Pro build 26200;
  AMD Ryzen 7 9800X3D (8 cores/16 threads); approximately 32 GiB RAM;
  NVIDIA GeForce RTX 5090 driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: start `basic_example`; allow production `OnLoad` to
  complete; issue `voxel_stream_origin 512 0 0`; wait for zero pending; issue
  `voxel_stream_origin 512 0 512`; wait for zero pending; issue
  `voxel_chunk_info 1 0 5`; issue `voxel_chunk_info 1 0 -4`; stop play
- Raw measurements:
  - Initial: loaded `729`, payload chunks `0`, logical samples `26,198,073`,
    loaded density bytes `0`, newly allocated density bytes `0`, settle
    `14.347 ms`, worker `0.231 ms`, generation `0.205 ms`, integration
    `0.134 ms`, effective `50,812.730 chunks/second`, generation `3,564,786.000
    chunks/second`; probes `0` and `16`
  - +X: loaded `729`, retained `648`, unloaded/generated `81`, payload chunks
    `0`, loaded density bytes `0`, newly allocated density bytes `0`, settle
    `7.235 ms`, worker `0.007 ms`, generation `0.002 ms`, integration `0.011 ms`,
    maximum frame `3.846 ms`, effective `11,196.040 chunks/second`, generation
    `35,217,400.000 chunks/second`; probes `0` and `16`
  - +Z: loaded `729`, retained `648`, unloaded/generated `81`, payload chunks
    `0`, loaded density bytes `0`, newly allocated density bytes `0`, settle
    `6.429 ms`, worker `0.008 ms`, generation `0.004 ms`, integration `0.010 ms`,
    maximum frame `1.744 ms`, effective `12,599.550 chunks/second`, generation
    `21,891,890.000 chunks/second`; probes `512` and `528`
  - Extent queries: `C[1,0,5]` loaded with `35,937` logical samples, no density
    payload, `0` payload samples, `0` density bytes, minimum `2560`, maximum
    `3072`; `C[1,0,-4]` missing
- Derived measurements versus the dense baseline in this scenario:
  - Loaded and newly allocated density-payload bytes decreased by `100%` in all
    phases; logical chunk and sample counts were unchanged
  - Initial settle decreased `20.16%`, +X settle decreased `6.71%`, and +Z
    settle decreased `4.42%`
  - Worker time decreased `96.63%` initially, `98.90%` on +X, and `98.75%` on
    +Z; measured generation time decreased `97.00%`, `99.69%`, and `99.37%`
- Build verification: `dotnet build voxels3.slnx --nologo` succeeded in `1.12`
  seconds with `0` warnings and `0` errors; the live s&box compiler succeeded
  with `0` errors; scene JSON parsed; `git diff --check` found no whitespace
  errors; production voxel source contained zero `new float[`, `Array.Fill`, or
  `_densitySamples` occurrences
- Outcome: pass
- Evidence location: live s&box console entries timestamped `2026/08/28
  06:57:38` through `06:57:59`; baseline and optimized raw data are retained in
  this scenario entry
- Remaining unmeasured risks: managed chunk-object, dictionary, queue, list, and
  task overhead remains; the reliable density allocation metric does not claim
  to measure those smaller allocations or global GC collections. Non-planar SDF
  storage is intentionally not implemented.
- Notes: The serialized worker was retained. After payload removal it consumed
  only `0.231 ms` initially and `0.007-0.008 ms` per shift, while preserving
  cancellation, stale-result rejection, and bounded main-thread integration.
  Those measurements do not justify a simultaneous scheduler rewrite. No test
  project, file, scene, component, hook, mock, or alternate path was added.

### VOXEL-GENERATION-001/v1 — flat grass and air material IDs

- Production entry point: async `VoxelManager.OnLoad`, the canonical
  `VoxelChunk` sample query, and `voxel_chunk_info`
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Populate the real `9x9x9` loaded
  world, then retrieve chunks immediately below and above the world-Z-zero
  boundary. Every valid logical sample returns density and its material ID from
  one production query. Negative and zero density return Grass ID `1`; positive
  density returns Air ID `0`.
- Pass criteria fixed before execution:
  - Initial stream retains the fixed `729` chunks, `26,198,073` logical samples,
    `0` payload chunks, and `0` density/material payload bytes; settle remains at
    or below `250.000 ms`
  - `C[0,0,-1]` minimum-Z sample is density `-512`, Grass ID `1`; its maximum-Z
    boundary sample is density `0`, Grass ID `1`
  - `C[0,0,0]` minimum-Z boundary sample is density `0`, Grass ID `1`; its
    maximum-Z sample is density `512`, Air ID `0`
  - The shared world-Z-zero sample agrees exactly across both chunks in density
    and material
  - Existing stream probes remain exactly `0` and `16`; no compile error,
    runtime exception, invalid configuration, non-finite value, missing queried
    chunk, or rejected command occurs
- Parameters:
  - Project/source revision: working source hashes recorded with the run
  - Engine build: `26.08.19`
  - World seed: not applicable; fixed world-space plane
  - World/coordinates: `basic_example`; player `(0,0,0)`; chunks `C[0,0,-1]`
    and `C[0,0,0]`; local minimum sample `L[0,0,0]` and maximum-Z sample
    `L[0,0,32]` in each chunk
  - Input order and operation count: start play; wait for production `OnLoad`;
    issue `voxel_chunk_info 0 0 -1`; issue `voxel_chunk_info 0 0 0`; stop play;
    one stream and two chunk retrievals
  - Warmup: none; player/client count: `1`
  - Settings: `32` cells/axis, `16` units/cell, `LoadRadius=4`, surface height
    `0`, assigned Player Controller, one worker, `0.500 ms` integration budget,
    overlays/lifecycle logging disabled
  - Material IDs: Air `0`; Grass `1`; zero density belongs to Grass
- Metrics and units: logical chunks and samples; payload chunks and bytes; stream
  settle/worker/generation/integration milliseconds; effective chunks/second;
  queried density values and byte material IDs

#### Run 2026-08-28 07:02:55 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `69DCE9F341A8EC0A2229FF10302F6C4B42B66C3CEE4A4CFFAF4655007E77545E`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `E91A1AC740EC7C881BA65548AD9C401FEF1053057CFA54943AF3529874E9E551`
  - `Assets/scenes/basic_example.scene` SHA-256
    `2AAD63033329ED86AC57B0A287DC80CD8DD31BD2F238C54E3E24606993B6D4AD`
- Engine build and environment: `26.08.19`; Windows 11 Pro build 26200;
  AMD Ryzen 7 9800X3D (8 cores/16 threads); approximately 32 GiB RAM;
  NVIDIA GeForce RTX 5090 driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: start `basic_example`; allow production `OnLoad` to
  complete; issue `voxel_chunk_info 0 0 -1`; issue
  `voxel_chunk_info 0 0 0`; stop play
- Raw measurements:
  - Initial stream: loaded `729`, pending `0`, generated `729`, stale `0`,
    logical samples `26,198,073`, payload chunks `0`, density/material payload
    bytes `0`, settle `14.662 ms`, worker `0.219 ms`, generation `0.192 ms`,
    integration `0.140 ms`, effective `49,720.370 chunks/second`, generation
    `3,790,945.000 chunks/second`
  - Existing stream samples: `L[0,0,0]` density `0`, material `Grass`, ID `1`;
    `L[0,0,1]` density `16`, material `Air`, ID `0`
  - `C[0,0,-1]`: minimum `L[0,0,0]` density `-512`, material `Grass`, ID `1`;
    maximum `L[0,0,32]` density `0`, material `Grass`, ID `1`
  - `C[0,0,0]`: minimum `L[0,0,0]` density `0`, material `Grass`, ID `1`;
    maximum `L[0,0,32]` density `512`, material `Air`, ID `0`
  - Shared-boundary comparison: both representations of world position
    `(0,0,0)` returned density `0`, Grass ID `1`; mismatch count `0`
- Build verification: `dotnet build voxels3.slnx --nologo` succeeded in `1.13`
  seconds with `0` warnings and `0` errors; live s&box compilation succeeded
  with `0` errors; scene JSON parsed; `git diff --check` found no whitespace
  errors; production source contained one `TryGetSample` declaration, four
  manager callers, zero `TryGetDensity` occurrences, and no material array or
  collection
- Outcome: pass
- Evidence location: live s&box console entries timestamped `2026/08/28
  07:02:55` through `07:03:05`; exact results are retained here
- Remaining unmeasured scope: no noise, seed, biome, soil layering, render
  material resource, meshing, collision, edits, persistence, or networking is
  implemented or claimed
- Notes: Density and material flow through one canonical production sample
  query. Material IDs are derived without allocation. The chunk retrieval
  command is an existing production diagnostic, not a test-only path; no test
  file, scene, component, hook, mock, or alternate generator was added.

### VOXEL-MEMORY-001/v1 — loaded voxel memory report

- Production entry point: async `VoxelManager.OnLoad`, production chunk
  integration, player-boundary streaming, inspector status, and stream summary
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Populate the real loaded world and
  move the player exactly one chunk in +X. Report exact density-payload bytes and
  a nonzero estimated loaded-voxel footprint derived from the settled production
  collection after both operations.
- Pass criteria fixed before execution:
  - Initial and +X phases each settle with `729` loaded chunks, `0` payload
    chunks, exact density-payload bytes `0`, and estimated loaded-voxel bytes
    `46,656` (`729 * 64 + 0`)
  - The human inspector value identifies the estimate as chunk objects plus
    density payload and explicitly states that managed collection overhead is
    excluded
  - The +X shift retains `648`, unloads `81`, generates `81`, finishes with `0`
    pending and stale chunks, and preserves the estimate at `46,656` bytes
  - Existing probes remain density/material `0/Grass 1` and `16/Air 0`
  - Initial settle remains at or below `250.000 ms`; +X settles at or below
    `100.000 ms`; no compile error, runtime exception, invalid configuration,
    non-finite result, target rejection, or duplicate manager occurs
  - The superseded `EstimatedDensityMemory` property and scene value are absent
- Parameters:
  - Project/source revision: working source hashes recorded with the run
  - Engine build: `26.08.19`; hardware/environment recorded with the run
  - World/scene: `basic_example`; fixed plane; no seed; one local player
  - Inputs and order: start at `(0,0,0)`; allow production `OnLoad` to complete;
    issue `voxel_stream_origin 512 0 0`; wait for zero pending; stop play
  - Operation count: one 729-chunk population and one exact +X shift
  - Warmup: none
  - Settings: `32` cells/axis, `16` units/cell, `LoadRadius=4`, surface height
    `0`, assigned Player Controller, one worker, `0.500 ms` integration budget,
    overlays/lifecycle logging disabled
  - Memory definition: estimated chunk object `64` bytes; exact loaded density
    payload added separately; managed collections/runtime overhead excluded
- Metrics and units: loaded/payload/pending/stale chunks; exact density-payload
  bytes; estimated loaded-voxel bytes and formatted MiB; settle milliseconds;
  effective chunks/second; exact density/material probes

#### Run 2026-08-28 11:14:22 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `69DCE9F341A8EC0A2229FF10302F6C4B42B66C3CEE4A4CFFAF4655007E77545E`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `33ECB6493E1582B9B373427BEC09CFB9388F9B07386D8BDBE3D1E0B2D2A48BF3`
  - `Assets/scenes/basic_example.scene` SHA-256
    `06698616A36B7D3F2D38E3697753922B86FA904BC5EA24C2F269B5B31F55D089`
- Engine build and environment: `26.08.19`; Windows 11 Pro build 26200;
  AMD Ryzen 7 9800X3D (8 cores/16 threads); approximately 32 GiB RAM;
  NVIDIA GeForce RTX 5090 driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: start `basic_example`; allow production `OnLoad` to
  complete; issue `voxel_stream_origin 512 0 0`; wait for zero pending; stop
- Raw measurements:
  - Initial: loaded `729`, pending `0`, generated `729`, stale `0`, payload
    chunks `0`, exact density-payload bytes `0`, estimated loaded-voxel bytes
    `46,656`, settle `19.974 ms`, worker `0.049 ms`, generation `0.023 ms`,
    integration `0.062 ms`, effective `36,496.900 chunks/second`; probes density/
    material `0/Grass 1` and `16/Air 0`
  - +X: loaded `729`, pending `0`, retained `648`, unloaded `81`, generated `81`,
    stale `0`, payload chunks `0`, exact density-payload bytes `0`, estimated
    loaded-voxel bytes `46,656`, settle `7.217 ms`, worker `0.008 ms`, generation
    `0.004 ms`, integration `0.014 ms`, maximum frame `3.159 ms`, effective
    `11,223.030 chunks/second`; probes density/material `0/Grass 1` and `16/Air 0`
  - Estimate calculation in both phases: `729 * 64 + 0 = 46,656` bytes, about
    `45.56 KiB` (`0.04 MiB` when formatted to two decimal places)
  - Human status definition: chunk objects plus exact density payload; managed
    collection overhead excluded
- Build verification: `dotnet build voxels3.slnx --nologo` succeeded with `0`
  warnings and `0` errors; live s&box compilation succeeded with `0` errors;
  scene JSON parsed; `git diff --check` found no whitespace errors; production
  source and scene contained zero `EstimatedDensityMemory` occurrences and both
  explicit replacement metrics
- Outcome: pass
- Evidence location: live s&box console entries timestamped `2026/08/28
  11:14:22` through `11:14:31`; exact results are retained here
- Remaining limitation: the estimate intentionally excludes managed dictionary,
  hash-set, queue, list, task, allocator, component, and runtime overhead. It is
  a stable project-owned estimate, not a claim of process-level heap precision.
- Notes: No GC API, profiler harness, test file, test scene, test component,
  compatibility property, fallback metric, or alternate memory path was added.

### VOXEL-DIAGNOSTICS-001/v1 — memory reporting removal

- Production entry point: async `VoxelManager.OnLoad`, production chunk
  integration, player-boundary streaming, inspector status, chunk inspection,
  and stream/world summary logging
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Populate the real loaded world and
  move the player exactly one chunk in +X without calculating, carrying, or
  reporting chunk-memory or density-payload-memory metrics. Terrain sampling,
  streaming state, and non-memory diagnostics remain unchanged.
- Pass criteria fixed before execution:
  - Initial and +X phases each settle with `729` loaded chunks and `0` pending or
    stale chunks
  - The +X shift retains `648`, unloads `81`, and generates `81`
  - Existing probes remain density/material `0/Grass 1` and `16/Air 0`
  - Inspector status, world summary, chunk inspection, lifecycle logging,
    stream-completion logging, and worker results expose no estimated object
    size, density-payload count, density-payload bytes, or aggregate memory value
  - Initial settle remains at or below `250.000 ms`; +X settles at or below
    `100.000 ms`; no compile error, runtime exception, invalid configuration,
    non-finite result, target rejection, or duplicate manager occurs
- Parameters:
  - Project/source revision: working source hashes recorded with the run
  - Engine build and hardware/environment: recorded with the run
  - World/scene: `basic_example`; fixed plane; no seed; one local player
  - Inputs and order: start at `(0,0,0)`; allow production `OnLoad` to complete;
    issue `voxel_stream_origin 512 0 0`; wait for zero pending; stop play
  - Operation count: one 729-chunk population and one exact +X shift
  - Warmup: none
  - Settings: `32` cells/axis, `16` units/cell, `LoadRadius=4`, surface height
    `0`, assigned Player Controller, one worker, `0.500 ms` integration budget,
    overlays/lifecycle logging disabled
- Metrics and units: loaded/pending/retained/unloaded/generated/stale chunks;
  settle milliseconds; density/material probes; memory-field occurrence count
  by each production diagnostic surface

#### Run 2026-08-28 12:42:23 EDT

- Executor: Codex (Sol); production execution not available through the active
  toolset
- Project/source state:
  - `Code/Voxels/VoxelChunk.cs` SHA-256
    `B247C17CA704201FFE234C051F477E7183BB278372C5EC176695C4845CFD4DB4`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `5FBDAC24150D8BA26F35517E14B9761EF38F764B7FC67C65C2988AE08F6F614F`
  - `Assets/scenes/basic_example.scene` SHA-256
    `31A4C5535BEB9E92C85900870B1425CCDAEFE8186E05065127633EC581B606E0`
- Engine/environment: `sbox-dev` was running from the Steam installation and
  reported file version `1.0.1.0`; no live Workbench or production-session
  control was available in the active toolset
- Confirmation that scenario parameters were unchanged: yes; the scenario was
  not executed
- Exact execution path: not run. Process detection did not establish an
  inspectable production play session, and no synthetic substitute was used.
- Static verification:
  - `dotnet build voxels3.slnx --nologo` succeeded with `0` warnings and `0`
    errors
  - Scene JSON parsing and `git diff --check` passed
  - Removed memory-reporting identifiers and log fields had `0` occurrences in
    `Code/Voxels/` and `Assets/scenes/basic_example.scene`
- Raw production measurements: none
- Outcome: incomplete; compile and source-shape checks pass, but the fixed
  in-world streaming scenario was not run
- Remaining unmeasured risks: live inspector serialization, initial population,
  +X streaming behavior, production logs, settle time, and density/material
  probes require a controllable s&box production session

### VOXEL-STATUS-001/v1 — concise inspector dashboard

- Production entry point: async `VoxelManager.OnLoad`, production chunk
  integration, player-boundary streaming, inspector status, and stream-completion
  logging
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Populate the real loaded world and
  move the player exactly one chunk in +X. The `World Status` inspector category
  exposes only three high-level values: chunk state, recent streaming
  performance, and process working-set memory. Detailed diagnostic values remain
  available in structured logs without appearing as individual inspector rows.
- Pass criteria fixed before execution:
  - The `World Status` category contains exactly `3` properties: `Chunk Status`,
    `Streaming Performance`, and `Process Memory Usage`
  - Initial and +X phases each report `729` loaded chunks and `0` queued chunks
  - The initial and +X performance summaries report finite positive effective
    chunks/second and settle milliseconds
  - Process memory reports a finite positive MiB value labeled as the approximate
    whole-process working set; it does not claim chunk or manager attribution
  - The +X stream log retains loaded, pending, retained, unloaded, generated,
    stale, worker, generation, integration, frame, throughput, and probe fields
  - No compile error, runtime exception, invalid configuration, non-finite
    result, target rejection, or duplicate manager occurs
- Parameters:
  - Project/source revision: working source hashes recorded with the run
  - Engine build and hardware/environment: recorded with the run
  - World/scene: `basic_example`; fixed plane; no seed; one local player
  - Inputs and order: start at `(0,0,0)`; allow production `OnLoad` to complete;
    issue `voxel_stream_origin 512 0 0`; wait for zero pending; inspect status and
    stream log after each phase; stop play
  - Operation count: one 729-chunk population and one exact +X shift
  - Warmup: none
  - Settings: `32` cells/axis, `16` units/cell, `LoadRadius=4`, surface height
    `0`, assigned Player Controller, one worker, `0.500 ms` integration budget,
    overlays/lifecycle logging disabled
- Metrics and units: inspector property count and exact labels; loaded/queued
  chunks; effective chunks/second; settle milliseconds; process working-set MiB;
  required structured-log field count

### VOXEL-STATUS-001/v2 — concise inspector dashboard

- Production entry point: async `VoxelManager.OnLoad`, production chunk
  integration, player-boundary streaming, inspector status, and stream-completion
  logging
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Populate the real loaded world and
  move the player exactly one chunk in +X. The `World Status` inspector category
  exposes only `Chunk Status`, `Streaming Performance`, and `Process Memory
  Usage`; detailed diagnostics remain in structured logs.
- Pass criteria fixed before execution:
  - The `World Status` category contains exactly the three named properties
  - Initial and +X phases each report `35,937` loaded chunks and `0` queued
  - Initial population generates `35,937`; the +X shift retains `34,848`,
    unloads `1,089`, generates `1,089`, and discards `0` stale chunks
  - Both performance summaries report finite positive effective chunks/second
    and settle milliseconds
  - Process memory reports a finite positive approximate whole-process working
    set; it does not claim chunk or manager attribution
  - The +X stream log retains loaded, pending, retained, unloaded, generated,
    stale, worker, generation, integration, frame, throughput, memory, and probe
    fields
  - No compile error, runtime exception, invalid configuration, non-finite
    result, target rejection, or duplicate manager occurs during the clean run
- Parameters:
  - Project/source revision: working source hashes recorded with the run
  - Engine build and hardware/environment: recorded with the run
  - World/scene: `basic_example`; fixed plane; no seed; one local player
  - Inputs and order: start at `(0,0,0)`; allow production `OnLoad` to complete;
    issue `voxel_stream_origin 512 0 0`; wait for zero pending; inspect the live
    component schema and stream log; stop play
  - Operation count: one 35,937-chunk population and one exact +X shift
  - Warmup: none
  - Settings: `32` cells/axis, `16` units/cell, `LoadRadius=16`, surface height
    `0`, assigned Player Controller, one worker, `0.500 ms` integration budget,
    overlays/lifecycle logging disabled
- Metrics and units: inspector property count and exact labels; loaded/queued/
  retained/unloaded/generated/stale chunks; effective chunks/second; settle,
  worker, generation, integration, slowest integration update, and maximum frame
  milliseconds; process working-set bytes/MiB; density/material probes

#### Run 2026-08-28 12:56:07 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `4FCE1218CF8F18912CE0AD4A5367E9CB07FED5A10FA03819A41BD6ECD17C4421`
  - `Assets/scenes/basic_example.scene` SHA-256
    `7EDDDDE1F31F12E880ED54CFC16DB8CCE0AEF4A4A347F2488954A76E5D88E667`
- Engine build and environment: `26.08.19`; Windows 11 Pro build 26200;
  AMD Ryzen 7 9800X3D (8 cores/16 threads); approximately 32 GiB RAM;
  NVIDIA GeForce RTX 5090 driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: clean start of `basic_example`; allow production
  `OnLoad` to settle; issue `voxel_stream_origin 512 0 0`; read the live
  component schema and structured completion logs; stop play
- Raw measurements:
  - Inspector schema: exactly `3` `World Status` properties — `Chunk Status`,
    `Streaming Performance`, and `Process Memory Usage`
  - Initial: loaded `35,937`, pending `0`, generated `35,937`, stale `0`, settle
    `24.164 ms`, worker `2.202 ms`, generation `1.059 ms`, integration `3.054
    ms`, slowest integration update `0.500 ms`, effective `1,487,194`
    chunks/second, process memory `4,095,877,120` bytes (`3,906.13 MiB`), probes
    density/material `0/Grass 1` and `16/Air 0`
  - +X: loaded `35,937`, pending `0`, retained `34,848`, unloaded `1,089`,
    generated `1,089`, stale `0`, settle `5.325 ms`, worker `0.084 ms`,
    generation `0.043 ms`, integration `0.106 ms`, slowest integration update
    `0.106 ms`, maximum frame `1.477 ms`, effective `204,510.9` chunks/second,
    process memory `4,098,568,192` bytes (`3,908.70 MiB`), probes density/
    material `0/Grass 1` and `16/Air 0`
  - The +X structured log retained every required detailed field
- Build verification: `dotnet build voxels3.slnx --nologo` succeeded with `0`
  warnings and `0` errors; live s&box compilation succeeded with `0` errors;
  scene JSON parsed; `git diff --check` found no whitespace errors
- Outcome: pass
- Evidence location: live s&box console entries timestamped `2026/08/28
  12:55:05` and `12:55:16`; exact results are retained here
- Remaining unmeasured risks: the engine metric is an approximate whole-process
  working set and cannot attribute memory to the voxel manager or individual
  chunks
- Notes: no guessed chunk-object size, GC API, compatibility property, test
  scene, test component, mock, or alternate streaming path was added.

### PLAYER-FIGURE-EIGHT-001/v1 — MCP movement smoke

- Production entry point: editor MCP tool `player_figure_eight`, the active
  `VoxelManager`, its assigned local `Player Controller`, and
  `VoxelManager.OnUpdate`
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: Start the real player on a continuous
  figure-eight centered at its initial X/Y, keep world Z exactly `0`, move at the
  configured horizontal speed and distance, then stop at the current position.
- Pass criteria fixed before execution:
  - The live MCP registry exposes one `player_figure_eight` tool with enable,
    speed, and distance inputs
  - Enabling with speed `512` units/second and distance `1024` units moves the
    assigned non-proxy player through both positive-X and negative-X lobes
  - Every sampled position has world Z `0`; X remains within `-1024..1024` and Y
    remains within `-512..512`, relative to the fixed start center
  - Across the moving samples, measured horizontal chord speed is within
    `460.8..563.2` units/second except any interval spanning the tool invocation
    or stop boundary
  - Disabling leaves the player within `1` unit of its stopped position for two
    seconds
  - No compile error, runtime exception, rejected target, non-finite position,
    duplicate manager, or invalid configuration occurs
- Parameters:
  - Project/source revision: working source hashes recorded with the run
  - Engine build and hardware/environment: recorded with the run
  - World/scene: `basic_example`; one local player; initial position `(0,0,0)`
  - Inputs and order: start play and wait for production `OnLoad`; call
    `player_figure_eight` with `enabled=true`, `speed=512`, `distance=1024`;
    sample the player every `0.5` seconds for `14` seconds; call the same tool
    with `enabled=false`; sample immediately and after `2` seconds; stop play
  - Operation count: one enable, `29` moving position samples including the
    immediate sample, one disable, and two stopped position samples
  - Warmup: initial production load only; player/client count: `1`
  - Height: fixed world Z `0`; no terrain query, trace, or collision-following
    behavior
- Metrics and units: MCP tool count/schema; player X/Y/Z in world units;
  horizontal displacement and chord speed; stopped-position drift; compiler and
  runtime diagnostics

#### Run 2026-08-28 12:46 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Source state: initial editor-tool implementation using an automatically
  discovered static `[EditorEvent.Frame]` callback; superseded after this run
- Confirmation that scenario parameters were unchanged: yes
- Raw measurements: tool registration and invocation succeeded; `29` samples
  over `14` seconds all remained `(0,0,0)`; horizontal speed was `0`
  units/second for all `28` intervals; stopped drift was `0`
- Outcome: fail; the newly added static frame callback was not discovered by
  editor hotload, so the player did not move
- Notes: The implementation was replaced with explicit registration of one
  in-memory editor driver. No scenario parameter or pass criterion changed.

#### Run 2026-08-28 12:51 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Source state: explicit editor-driver registration implementation
- Confirmation that scenario parameters were unchanged: yes
- Raw measurements: `29` samples obtained through `get_game_object` all reported
  `(0,0,0)`, but the production `VoxelManager` simultaneously logged boundary
  crossings through both positive- and negative-X chunks with Z chunk `0`
- Outcome: incomplete; the scene tool resolved the parallel edit-scene object
  sharing the runtime object's GUID, so those position samples did not measure
  the production player
- Notes: The unchanged scenario was rerun using the existing production
  `voxel_player_chunk` diagnostic to disambiguate the runtime object.

#### Run 2026-08-28 12:52 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Source state: explicit editor-driver registration implementation
- Confirmation that scenario parameters were unchanged: yes
- Raw measurements: the run stopped after its first attempted production
  position sample because the console read occurred before the diagnostic log
  was available
- Outcome: incomplete; no comparable movement metrics were produced
- Notes: A fixed `30 ms` diagnostic-log read delay was added to the observation
  procedure only. Movement inputs, sample cadence, duration, and pass criteria
  remained unchanged.

#### Run 2026-08-28 12:53 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Editor/VoxelMcpTools.cs` SHA-256
    `42457F5399ED85E29BD3B3158BDD19C5584F168F7D11164A76E3133414D85723`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `409C7BA960059A8C4A1CDB4B4606AAFEFA1AAB7424E4794E2A16100D8057D8AD`
  - `Assets/scenes/basic_example.scene` SHA-256
    `7EDDDDE1F31F12E880ED54CFC16DB8CCE0AEF4A4A347F2488954A76E5D88E667`
- Engine build and environment: `26.08.19`; Windows 11 Pro build 26200;
  AMD Ryzen 7 9800X3D (8 cores/16 threads); approximately 32 GiB RAM;
  NVIDIA GeForce RTX 5090 driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: start `basic_example`; wait for production `OnLoad`;
  invoke `player_figure_eight(enabled=true, speed=512, distance=1024)`; obtain
  each runtime position through `voxel_player_chunk` every `0.5` seconds for
  `14` seconds; invoke the same MCP tool with `enabled=false`; sample immediately
  and after `2` seconds; stop play
- Raw measurements:
  - MCP registry: one `player_figure_eight` tool in the `voxels3` toolset with
    optional boolean `enabled`, number `speed`, and number `distance` inputs
  - Moving samples: `29`; X minimum `-1022.750`, X maximum `1020.830`; Y minimum
    `-510.071`, Y maximum `503.535`; Z minimum and maximum both `0`
  - Horizontal chord speed across `28` intervals: minimum `482.269`, maximum
    `527.828`, mean `505.433` units/second; intervals outside
    `460.8..563.2`: `0`
  - First position `[5.801,5.801,0]`; last moving position
    `[1006.823,183.640,0]`
  - Stop position immediately and after two seconds:
    `[1012.015,154.381,0]`; drift `0` units
- Build verification: `dotnet build voxels3.slnx --nologo` succeeded in `1.16`
  seconds with `0` warnings and `0` errors; live s&box compilation succeeded
  with `0` errors; `git diff --check` passed before final commit checks
- Outcome: pass
- Evidence location: live s&box console production position and streaming logs
  timestamped during the run; exact aggregate measurements are retained here
- Remaining limitation: this slice intentionally fixes Z at `0` and provides no
  terrain following, multiplayer automation protocol, or movement report

### PLAYER-FIGURE-EIGHT-001/v2 — shared MCP and inspector control

- Production entry point: `VoxelManager` figure-eight configuration and update
  path, editor MCP tool `player_figure_eight`, and the manager inspector's
  `Toggle Player Figure Eight` button
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: The MCP tool and human inspector
  button configure the same manager-owned movement path. Either start surface
  moves the assigned local player around the same fixed-Z figure-eight; MCP
  disable or a second button press stops at the current position.
- Pass criteria fixed before execution:
  - The live MCP registry retains one `player_figure_eight` tool with enable,
    speed, and distance inputs
  - The live `VoxelManager` inspector schema exposes `Figure Eight Speed` and
    `Figure Eight Distance` plus one `Toggle Player Figure Eight` button
  - Source inspection confirms both entry points call one manager configuration
    method and one manager update method; no editor-frame driver or second curve
    implementation remains
  - With speed `512` units/second and distance `1024` units, the shared path
    reaches both positive-X and negative-X lobes
  - Every sampled runtime position has world Z `0`; relative X remains within
    `-1024..1024` and relative Y within `-512..512`
  - Horizontal chord speed is within `460.8..563.2` units/second for every
    moving interval outside entry/stop boundaries
  - Stopped drift remains at or below `1` unit over two seconds
  - No compile error, runtime exception, rejected target, non-finite position,
    duplicate manager, or invalid configuration occurs
- Parameters:
  - Project/source revision: working source hashes recorded with the run
  - Engine build and hardware/environment: recorded with the run
  - World/scene: `basic_example`; one local player; initial position `(0,0,0)`
  - Inputs and order: start play and wait for production `OnLoad`; configure
    speed `512` and distance `1024`; start through the shared manager path;
    sample the runtime player every `0.5` seconds for `14` seconds; stop through
    the shared manager path; sample immediately and after `2` seconds; stop play
  - Operation count: one start, `29` moving samples including the immediate
    sample, one stop, and two stopped samples
  - Warmup: initial production load only; player/client count: `1`
  - Height: fixed world Z `0`; no terrain query, trace, or collision-following
    behavior
- Metrics and units: MCP and inspector schema; implementation-path count;
  player X/Y/Z in world units; horizontal displacement and chord speed;
  stopped-position drift; compiler and runtime diagnostics

#### Run 2026-08-28 13:07 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - `Editor/VoxelMcpTools.cs` SHA-256
    `4F52DE10F1FE07DAEBDF09AA28B3B710CD570D4A66738CAC7FD991D651D61C1A`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `4B351861E703AA583FB94401DF627CDD41399626998564E65A4675EC46C1F0F1`
  - `Docs/Architecture/VoxelChunkFoundation.md` SHA-256
    `36E877EC89172B86F70E0B78435AC515391657852FB0A1AFF943E0F000374FA3`
  - `Assets/scenes/basic_example.scene` SHA-256
    `7EDDDDE1F31F12E880ED54CFC16DB8CCE0AEF4A4A347F2488954A76E5D88E667`
- Engine build and environment: `26.08.19`; Windows 11 Pro build 26200;
  AMD Ryzen 7 9800X3D (8 cores/16 threads); approximately 32 GiB RAM;
  NVIDIA GeForce RTX 5090 driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: restart the editor to load the changed runtime component
  shape; start `basic_example`; wait for production `OnLoad`; invoke
  `player_figure_eight(enabled=true, speed=512, distance=1024)`; obtain each
  runtime position through the production `voxel_player_chunk` diagnostic every
  `0.5` seconds for `14` seconds; invoke the same tool with `enabled=false`;
  sample immediately and after `2` seconds; stop play
- Surface and implementation evidence:
  - Live MCP registry contained one `player_figure_eight` tool in the `voxels3`
    toolset with optional `enabled`, `speed`, and `distance` inputs
  - Live `VoxelManager` component metadata exposed `Figure Eight Speed` and
    `Figure Eight Distance` in the `Smoke Test` inspector group
  - The successfully compiled runtime source contains exactly one
    `[Button("Toggle Player Figure Eight")]` method; it and the editor MCP tool
    both call `ConfigurePlayerFigureEight`; the only curve update is
    `VoxelManager.UpdatePlayerFigureEight`; no `EditorEvent.Frame` driver or
    second curve implementation remains
- Raw measurements:
  - Moving samples: `29`; X minimum `-1022.597`, X maximum `1020.610`; Y minimum
    `-510.777`, Y maximum `503.752`; Z minimum and maximum both `0`
  - Horizontal chord speed across `28` intervals: minimum `484.909`, maximum
    `516.593`, mean `505.467` units/second; intervals outside
    `460.8..563.2`: `0`
  - First position `[6.034,6.034,0]`; last moving position
    `[1005.219,191.640,0]`
  - Stop position immediately and after two seconds:
    `[1010.890,161.239,0]`; drift `0` units
- Build verification: `dotnet build voxels3.slnx --nologo` succeeded in `1.38`
  seconds with `0` warnings and `0` errors; live s&box compilation succeeded
  with `0` errors after a clean editor restart; `git diff --check` passed
- Outcome: pass
- Evidence location: live s&box component metadata, MCP registry, production
  position/streaming logs, and compiled source; exact aggregate measurements
  are retained here
- Remaining limitation: this slice intentionally fixes Z at `0`; terrain
  following, multiplayer automation protocol, and movement reporting remain out
  of scope

### PERFORMANCE-OVERVIEW-001/v1 — three-pillar runtime baseline

- Production entry point: `VoxelManager` production update, memory, and chunk
  integration paths; editor MCP tool `performance_overview`; manager inspector
  `Log Performance Overview` button
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: A completed fixed 10-second production
  window exposes finite frame, memory, and chunk metrics, and an explicit MCP or
  human request logs the same snapshot as one structured record with passive
  task/revision context and spatial identity.
- Pass criteria fixed before execution:
  - Exactly one completed window reports at least one frame sample and no sample
    truncation
  - Average FPS is finite and positive; p95 and p99 frame duration are finite,
    positive, and ordered `p95 <= p99`
  - Average GPU frame duration is finite and non-negative
  - Average and peak process working set and engine GPU memory are non-zero;
    each peak is at least its average; GPU budget is non-zero
  - Loaded, pending, window-integrated, and last-stream chunk counts are
    non-negative; window and last-stream chunk rates are finite and non-negative
  - One `performance.overview` record contains UTC time, scene, task, revision,
    stream center, target position, window/sample identity, and numeric fields
    for all three pillars
  - MCP and inspector entry points call the same manager logging method; runtime
    source performs no Git, process, shell, HTTP, or network lookup
  - Sampling performs no per-update managed allocation between window
    completions; memory reads occur at 1 Hz; sorting and logging do not occur in
    the per-frame accumulation path
  - Runtime and editor code compile without warnings or errors, and the live run
    emits no exception
- Parameters:
  - Project/source revision: working source hashes recorded with the run
  - Engine build and hardware/environment: recorded with the run
  - World/scene: `basic_example`; one local player; initial position `(0,0,0)`;
    `CellsPerAxis=32`; `CellSize=16`; `LoadRadius=16`;
    `TerrainSurfaceHeight=0`
  - Workload: wait for production `OnLoad` and `OnStart`; start the shared
    figure-eight path with `speed=2500`, `distance=50000`, fixed Z `0`; collect
    one complete `10`-second performance window; request one overview with
    `task=PERFORMANCE-OVERVIEW-001/v1` and the externally supplied working
    revision; stop movement; stop play
  - Frame sampling: every production update; capacity `32,768`; percentile
    definition: nearest-rank sorted frame duration at 95% and 99%
  - Memory sampling: once per second; process scope is s&box approximate process
    working set; GPU scope is engine-tracked render allocations and OS budget
  - Chunk sampling: count successful insertions into the canonical loaded-chunk
    dictionary during the same window; retain the existing complete-stream
    metrics unchanged
  - Warmup: initial production load only; measurement duration: `10` seconds;
    player/client count: `1`; overview log operation count: `1`
- Metrics and units: FPS; frame and GPU duration in milliseconds; CPU/GPU memory
  in bytes and MiB; loaded/pending/integrated chunk counts; chunks per second;
  last stream settle duration in milliseconds
- Baseline policy: this first run establishes observed values and verifies the
  measurement contract; it does not invent a product performance threshold
  before evidence exists

#### Run 2026-08-28 13:28 EDT

- Executor: Codex (Sol) through the live s&box editor and production game session
- Project/source state:
  - Base revision supplied to the runtime: `0cf0e21+working-tree`
  - `Code/Voxels/VoxelManager.cs` SHA-256
    `BB14AEA9240DEE58B946763C71E949115831CDF971CB0F2D09F3AC5D0C0E5129`
  - `Editor/VoxelMcpTools.cs` SHA-256
    `9D4986E181F2D5E3D641AB0FCCE256B5E87B64B4CF54D7A4D78A72FECB2F607D`
  - `Docs/Architecture/VoxelChunkFoundation.md` SHA-256
    `74044A88F506F52216292DBAF326E68DA73FE847E585AA755728DFAB03794CDF`
  - `Assets/scenes/basic_example.scene` SHA-256
    `1B0187D67B4FBAD6F9E0B2911F01737D2D7869C126F6D7C9CA8E460256CCED8C`
- Engine build and environment: `26.08.19`; Windows 11 Pro build 26200;
  AMD Ryzen 7 9800X3D (8 cores/16 threads); approximately 32 GiB RAM;
  NVIDIA GeForce RTX 5090 driver 32.0.16.1088; one local client
- Confirmation that scenario parameters were unchanged: yes
- Exact execution path: start project startup scene `basic_example`; wait for
  production initial load (`35,937` loaded, `0` pending); invoke
  `player_figure_eight(enabled=true, speed=2500, distance=50000)`; wait `11`
  wall-clock seconds so one 10-second window completes; invoke
  `performance_overview(task=PERFORMANCE-OVERVIEW-001/v1,
  revision=0cf0e21+working-tree)`; stop the shared movement path; stop play
- Raw structured record:
  - `capturedAtUtc=2026-08-28T17:28:36.3607972+00:00`
  - `scene=basic_example`; `center=C[80,45,0]`; target position
    `[41030.2,23448.64,0]`; task and revision matched the supplied values
  - Window `10.001` seconds; frame samples `2,390`; truncated samples `0`
  - Average `238.977` FPS; p95 frame `5.084` ms; p99 frame `6.451` ms;
    average GPU frame `0.964` ms
  - Average process working set `6,340,146,972` bytes (`6,046.435` MiB); peak
    `6,341,451,776` bytes (`6,047.680` MiB)
  - Average and peak engine GPU memory `1,229,791,370` bytes (`1,172.820`
    MiB); GPU memory budget `32,945,209,344` bytes (`31,419.000` MiB)
  - Current chunks: `35,937` loaded, `0` pending; window integrated `73,920`;
    window rate `7,391.247` chunks/second
  - Last complete stream: generated `1,089`; settle `5.097` ms; effective
    `213,642.5` chunks/second; generation `29,117,610` chunks/second
- Surface and implementation evidence:
  - Live MCP registry exposed one `performance_overview` tool with optional task
    and revision strings, and live component metadata exposed `Performance Task`,
    `Performance Revision`, and `Frame Performance`
  - MCP and `[Button("Log Performance Overview")]` call the same
    `WritePerformanceOverview` method
  - Source inspection found no Git, process, shell, HTTP, or network operation;
    task and revision enter only as passive strings
  - The per-frame sample path contains no collection growth, sorting, logging,
    or string construction; memory is gated by the one-second accumulator;
    fixed-array copy/sort and display strings occur only at window completion
- Build and runtime verification: `dotnet build voxels3.slnx --nologo`
  succeeded with `0` warnings and `0` errors; live s&box compilation succeeded
  with `0` errors; `git diff --check` passed; live error console contained no
  matching entries after the run
- Outcome: pass; all measurement-contract criteria passed and this run
  establishes the first observed baseline
- Remaining risks: this overview attributes process-wide RAM and engine-wide GPU
  memory, not voxel-only memory; it is a top-level indicator rather than a CPU,
  GPU-pass, allocator, or per-chunk profiler. Product pass/fail budgets remain to
  be set from representative hardware and workload evidence.

### PERFORMANCE-OVERVIEW-001/v2 — automated loop-boundary baseline

- Version justification recorded before execution: v1 depended on separately
  timed start, wait, report, and stop calls. That execution procedure is not a
  repeatable benchmark boundary even though its workload parameters were fixed.
  V2 preserves the scene, workload, and metrics while replacing manual timing
  and the arbitrary 10-second cutoff with one complete manager-owned loop.
- Production entry point: the manager inspector's `Toggle Player Figure Eight`
  button and editor MCP operation `player_figure_eight` invoke the same
  `ConfigurePlayerFigureEightTest` method. `VoxelManager` owns the exact workload
  start, window reset, loop-boundary completion, movement stop, and structured
  result.
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Behavior and complete expected outcome: One button press starts the suite,
  measures exactly one complete figure-eight loop, stops movement automatically,
  and emits one structured result. The MCP entry point is an automation adapter
  for the identical manager call. No AI timing, wall-clock sleep, Git discovery,
  remote lookup, or separately timed report call is involved.
- Pass criteria fixed before execution:
  - The button captures speed, distance, and loop count once at start; later
    inspector edits cannot change the active workload
  - One `ConfigurePlayerFigureEightTest` call resets the production window and
    starts movement on the same manager-thread boundary, then completes only at
    the configured exact full-curve crossing
  - Completion stops the shared figure-eight path before returning and emits
    exactly one `performance.overview` record marked `figureEightTest=True` with
    completed loops `1`, speed `2500`, and distance `50000`
  - The result meets the v1 metric-validity criteria: positive finite FPS and
    ordered frame tails, valid RAM/VRAM, non-negative chunk telemetry, at least
    one frame sample, and zero frame-sample truncation
  - Runtime/editor compilation, a live production-path run, structured-log
    readback, and `git diff --check` all succeed
- Canonical parameters:
  - Scene `basic_example`; one local player; initial position `(0,0,0)`;
    `CellsPerAxis=32`; `CellSize=16`; `LoadRadius=16`;
    `TerrainSurfaceHeight=0`
  - Figure-eight speed `2500`, distance `50000`, world Z `0`
  - One complete loop; configurable inspector range `1..8`; frame capacity
    `524,288`; frame sample every production update; process/GPU memory sample
    every one game-time second; canonical chunk integration counters
  - Nearest-rank p95 and p99 frame duration; the same three-pillar numeric fields
    and scopes defined by v1
- Metadata: task and revision are passive inspector or MCP inputs; the runtime
  does not discover either value
- Baseline policy: v2 replaces v1 as the canonical comparable execution method.
  V1 remains historical evidence and is not compared as an automated run.

#### Run 2026-08-28 — pass

- Entry: live `player_figure_eight` MCP adapter, which invokes the same
  `ConfigurePlayerFigureEightTest` method as the inspector button
- Task: `PERFORMANCE-OVERVIEW-001/v2`
- Revision: `2ef4cfa+working-tree`
- Begin record: `loops=1 speed=2500 distance=50000 center=[0,0,0]`
- Automatic result: `capturedAtUtc=2026-08-28T17:47:55.0473204+00:00`;
  `scene=basic_example`; `figureEightTest=True`; `completedLoops=1`;
  `testSpeed=2500`; `testDistance=50000`; `center=C[0,0,0]`;
  `targetX=0`; `targetY=0`; `targetZ=0`
- Frame result: `windowSeconds=121.944`; `frameSamples=26369`;
  `truncatedFrameSamples=0`; `averageFps=216.235`;
  `p95FrameMs=9.825`; `p99FrameMs=13.813`;
  `averageGpuFrameMs=0.482`
- Memory result: `averageProcessMemoryBytes=4527794243`;
  `peakProcessMemoryBytes=4808781824`;
  `averageGpuMemoryBytes=1065161886`;
  `peakGpuMemoryBytes=1065161886`; `gpuMemoryBudgetBytes=32945209344`
- Chunk result: `loadedChunks=33792`; `pendingChunks=2145`;
  `windowIntegratedChunks=842853`; `windowChunksPerSecond=6911.794`;
  `lastStreamGeneratedChunks=2145`; `lastStreamSettleMs=15.479`;
  `lastEffectiveChunksPerSecond=138578.4`;
  `lastGenerationChunksPerSecond=35869320`
- Automation evidence: no stop or report operation was called during the run;
  the sole overview record appeared when the manager counted the first complete
  loop. Play was stopped only after result readback.
- Validation: live runtime and editor compilers reported zero errors and zero
  warnings; both .NET projects built with zero errors and zero warnings; the MCP
  registry exposed all six expected test parameters; `git diff --check` passed.
- Outcome: pass. All fixed criteria passed. The inspector button binding was
  verified from source; live execution used its exact shared MCP method rather
  than a physical inspector click.

### DEBUG-SURFACE-REMOVAL-001/v1 — bounded diagnostics

- Definition recorded before execution: remove the manual world-summary,
  player-chunk, and performance-overview actions plus the per-loaded-chunk
  bounds, labels, and lifecycle logging paths. Preserve the automatic structured
  result emitted by the figure-eight suite.
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Production entry point: start the real scene, then invoke
  `player_figure_eight` once through its production MCP adapter.
- Fixed parameters: one local player; initial position `(0,0,0)`;
  `CellsPerAxis=32`; `CellSize=16`; `LoadRadius=16`;
  `TerrainSurfaceHeight=0`; speed `10000`; distance `1024`; loop count `1`;
  task `DEBUG-SURFACE-REMOVAL-001/v1`; revision supplied passively by the caller.
- Pass criteria fixed before execution:
  - `VoxelManager` exposes no `ShowLoadedChunkBounds`,
    `ShowLoadedChunkLabels`, or `LogChunkLifecycle` inspector properties and the
    active scene serializes none of them
  - Source contains no `Log World Summary`, `Log Player Chunk`, or
    `Log Performance Overview` inspector action; no `voxel_player_chunk`
    command; no loaded-chunk overlay traversal; and no `chunk.load` or
    `chunk.unload` lifecycle event
  - The live MCP registry exposes `player_figure_eight` but not the standalone
    `performance_overview` action
  - One live loop completes automatically and emits exactly one structured
    `performance.overview` record with `figureEightTest=True`,
    `completedLoops=1`, speed `10000`, distance `1024`, and zero truncated frame
    samples
  - The live run produces zero `chunk.load` and zero `chunk.unload` records
  - Runtime/editor compilation, both .NET builds, and `git diff --check` succeed

#### Run 2026-08-28 — pass

- Revision: `f2089ab+working-tree`; engine build `26.08.19`
- Static surface: live `VoxelManager` metadata exposed none of the three removed
  properties; the MCP registry exposed `player_figure_eight` and returned zero
  matches for `performance_overview`; source and scene searches returned no
  removed runtime symbol, command, event, or serialized field
- Begin record: `task=DEBUG-SURFACE-REMOVAL-001/v1`;
  `revision=f2089ab+working-tree`; `loops=1`; `speed=10000`;
  `distance=1024`; `center=[0,0,0]`
- Automatic result: `capturedAtUtc=2026-08-28T17:55:01.2221318+00:00`;
  `figureEightTest=True`; `completedLoops=1`; `testSpeed=10000`;
  `testDistance=1024`; `windowSeconds=0.629`; `frameSamples=132`;
  `truncatedFrameSamples=0`; `averageFps=209.828`;
  `p95FrameMs=9.431`; `p99FrameMs=14.934`;
  `averageGpuFrameMs=0.472`
- Streaming activity: `windowIntegratedChunks=8679`;
  `loadedChunks=33792`; `pendingChunks=2145`; buffered console contained zero
  `chunk.load` and zero `chunk.unload` lifecycle records before and after the run
- Validation: live runtime and editor compilers reported zero errors and zero
  warnings; runtime and editor .NET builds completed with zero errors and zero
  warnings; `git diff --check` passed
- Hotload note: removing serialized members produced expected unresolved-member
  migration messages at `13:53:56`; a fresh play session then started and the
  measured run at `13:55:00–13:55:01` produced no runtime error
- Outcome: pass. The unbounded debug surfaces are absent and the automated suite
  retains its single bounded structured result.

### PERFORMANCE-OVERVIEW-001/v3 — durable structured baseline

- Version justification recorded before execution: v2 established an automatic
  loop boundary but retained results only as general engine log text. V3 keeps
  the canonical scene and one-loop workload unchanged while replacing the
  result sink with a versioned append-only JSON Lines dataset.
- Actual world/scene: `Assets/scenes/basic_example.scene`
- Production entry point: the manager inspector's `Run Performance Test` button
  and editor MCP operation `run_performance_test` call the same manager start
  method; the live validation uses the MCP adapter.
- Fixed parameters unchanged from v2: one local player; initial position
  `(0,0,0)`; `CellsPerAxis=32`; `CellSize=16`; `LoadRadius=16`;
  `TerrainSurfaceHeight=0`; speed `2500`; distance `50000`; world Z `0`;
  loop count `1`; frame capacity `524,288`; per-update frame sampling;
  one-game-time-second memory sampling; nearest-rank p95 and p99 frame duration.
- Fixed identity for this run: task `PERFORMANCE-OVERVIEW-001/v3`; revision
  `70d878f+working-tree`.
- Canonical storage: append one compact JSON object per line to schema-versioned
  virtual path `performance/results-v1.jsonl` in `FileSystem.Data`, after the
  measured loop has completed.
- Pass criteria fixed before execution:
  - The inspector action is named `Run Performance Test`; the MCP registry
    exposes `run_performance_test` with required task and revision and no
    `player_figure_eight` operation
  - Blank, whitespace, or case-insensitive `unassigned` task/revision is rejected
    before movement and measurement start
  - One unchanged canonical loop stops automatically and appends exactly one
    newline-delimited JSON record after measurement, without rewriting earlier
    records
  - The appended object parses as JSON and contains schema version `1`, a unique
    run ID, UTC timestamp, required task/revision, scene/world/test parameters,
    and nested frame, memory, and chunk objects
  - The record reports `completedLoops=1`, speed `2500`, distance `50000`,
    positive finite frame measurements, ordered p95/p99 frame tails, valid
    memory/chunk metrics, and zero truncated frame samples
  - Runtime/editor compilation, both .NET builds, live production execution,
    durable-file readback, and `git diff --check` succeed

#### Run 2026-08-28 — pass

- Engine build `26.08.19`; revision `70d878f+working-tree`
- Rejection evidence: task `unassigned` was rejected by
  `StartPerformanceTest` before movement began
- Saved path:
  `C:\Program Files (x86)\Steam\steamapps\common\sbox\data\local\voxels3#local\performance\results-v1.jsonl`
- Record identity: schema `1`; run ID
  `40e2f1a60d3a48dc98299f0cb81d0b13`; captured UTC
  `2026-08-28T18:08:25.0701717+00:00`; outcome `completed`; task
  `PERFORMANCE-OVERVIEW-001/v3`; revision `70d878f+working-tree`
- Workload/world: test `player-figure-eight`; one completed loop; speed `2500`;
  distance `50000`; duration `121.943954` seconds; start/target `(0,0,0)`;
  scene `basic_example`; cells per axis `32`; cell size `16`; load radius `16`;
  terrain surface height `0`; streaming center `C[0,0,0]`
- Frame: `26457` samples; `0` truncated; average FPS `216.95671`; p95
  `9.6484` ms; p99 `13.4103` ms; average GPU `0.47542498` ms
- Memory bytes: process average `5308304132`, peak `5398777856`; GPU
  average/peak `1088171346`; GPU budget `32945209344`
- Chunks: loaded `33792`; pending `2145`; integrated `842886`; integrated per
  second `6912.077`; last generated `2145`; last settle `14.0532` ms; last
  effective per second `152634.28`; last generation per second `36232860`
- Durable readback: dataset contained one line and `1114` bytes; the line parsed
  directly as JSON with every required nested object and value above
- Outcome: pass. The unchanged baseline completed and the runtime created a
  durable structured record after measurement.

### PERFORMANCE-STORAGE-APPEND-001/v1 — append integrity

- Definition recorded before execution: prove a second completed production
  test appends one independently parseable record without changing the existing
  canonical v3 baseline record.
- Actual world/scene: the same live `basic_example` session after
  `PERFORMANCE-OVERVIEW-001/v3`.
- Fixed parameters: speed `10000`; distance `1024`; loop count `1`; world Z `0`;
  task `PERFORMANCE-STORAGE-APPEND-001/v1`; revision
  `70d878f+working-tree`.
- Pre-run dataset: one line, `1114` bytes; first-record SHA-256
  `021FB596DB81E6D02BF3D2586EE3981FE519F275C0AC9CA76BBCF2EBB4097D96`.
- Pass criteria fixed before execution:
  - The production `run_performance_test` operation completes one loop and saves
    a record with the fixed identity and workload
  - Dataset line count increases from one to two; both lines parse independently
    as JSON; run IDs differ; and the first-record SHA-256 is unchanged
  - The second record has schema version `1`, outcome `completed`, zero truncated
    samples, and positive finite frame measurements

#### Run 2026-08-28 — invalid validation

- The production run completed and appended run
  `99e0f14f3c0a49bb85400cb2d293c934`; the dataset increased to two independently
  parseable lines and distinct run IDs.
- The pre-run SHA-256 was invalid because PowerShell returned the one-line file
  as a scalar string and `[0]` selected its first character rather than its first
  line. The observed hash cannot establish the unchanged-record criterion.
- Outcome: invalid validation, not a product failure. The records are retained;
  the result is not used as append-integrity evidence.

### PERFORMANCE-STORAGE-APPEND-001/v2 — corrected append integrity

- Version justification recorded before execution: v1's workload and product
  behavior were valid, but its verification command hashed one character. V2
  preserves the same workload and uses an explicit array of full JSONL lines.
- Fixed parameters unchanged from v1 except task
  `PERFORMANCE-STORAGE-APPEND-001/v2`.
- Pre-run dataset: two lines, `2230` bytes; full-line SHA-256 values, in order:
  - `BD6D66D38F0DE68284E9809B0F975F0B551F329536757A8B63460A5BBF2E9540`
  - `15E049A91EAD0803D81229F93ECE87489ED0846FD02923769872AB8BA295F7D1`
- Pass criteria fixed before execution:
  - The production run appends one third independently parseable record with the
    v2 task, revision `70d878f+working-tree`, speed `10000`, distance `1024`, and
    one completed loop
  - Dataset line count increases from two to three, all run IDs differ, and both
    pre-existing full-line SHA-256 values remain unchanged
  - The third record has schema version `1`, outcome `completed`, zero truncated
    samples, and positive finite frame measurements

#### Run 2026-08-28 — pass

- Saved run ID `7298bfe501f14a748a99511a5644b7bf`; task
  `PERFORMANCE-STORAGE-APPEND-001/v2`; revision `70d878f+working-tree`
- Workload/result: one completed loop; speed `10000`; distance `1024`; duration
  `0.62613827` seconds; `118` frame samples; `0` truncated; average FPS
  `188.45854`; p95 `13.7385` ms; p99 `15.222` ms
- Dataset increased from two lines/`2230` bytes to three lines/`3347` bytes; all
  three JSON objects parsed and all three run IDs were distinct
- First and second hashes after append exactly matched both fixed pre-run hashes;
  third-record SHA-256 was
  `900D28C4C1C0A2C7752755358CC7B916940D2FA509B91412058719CC517468C2`
- Outcome: pass. Append preserved both prior records byte-for-byte.

### GPU-VOXEL-MESH-001/v1 - LOD0 GPU regular-cell meshing

- Definition recorded before implementation and before the first measurement.
- Actual world/scene: `Assets/scenes/basic_example.scene`; one local player;
  initial position `(0,0,0)`; game-camera output at `1280x720`.
- Production entry points: normal `VoxelManager` chunk integration and rendering,
  bounded `inspect_gpu_mesh` diagnostics, and the unchanged
  `run_performance_test` player journey.
- Fixed world and meshing parameters: `CellsPerAxis=32`; `CellSize=16`;
  `LoadRadius=16`; `TerrainSurfaceHeight=0`; LOD0; iso-surface `0`; density
  `<= 0` is solid; central-gradient step `8`; at most `8` GPU mesh dispatches
  per update; active-cell capacity `32^3`; maximum `5` regular-cell triangles.
- Fixed correctness probes:
  - Surface and negative-coordinate chunks `C[0,0,0]`, `C[-1,0,0]`,
    `C[0,-1,0]`, and `C[-1,-1,0]`
  - Completely solid chunk `C[0,0,-1]`
  - Completely air chunk `C[0,0,1]`
  - Shared X/Y boundaries through world `(0,0,0)` viewed from above and at an
    oblique game-camera angle
- Fixed performance journey: the unchanged `PERFORMANCE-OVERVIEW-001/v3`
  figure eight with speed `2500`, distance `50000`, fixed world Z `0`, one
  loop, frame capacity `524,288`, per-update frame sampling, one-second memory
  sampling, and nearest-rank p95/p99 frame duration.
- Pass criteria fixed before execution:
  - Each surface probe reports `1,024` active cells, `2,048` logical triangles,
    finite gradients, and no capacity overflow
  - Solid and air probes own no GPU mesh resource and report zero active cells
    and triangles
  - Shared-boundary screenshots show continuous coverage, consistent lighting,
    correct backface orientation, no background seam pixels, and no z-fighting
  - Normal production performs zero GPU-to-CPU geometry readbacks; the bounded
    diagnostic may asynchronously read only scalar statistics and indirect args
  - Voxel-owned logical GPU buffer capacity remains below `256 MiB`, mesh
    memory does not grow across the fixed loop, and the settled meshing path
    performs zero recurring game-thread allocations
  - Figure-eight p95 frame duration is at most `16.67 ms`, p99 is at most
    `25 ms`, required chunks remain available, and the mesh backlog returns to
    zero
  - GPU meshing duration, full-frame GPU impact, logical output size, engine GPU
    memory, pool allocation/reuse counts, and CPU allocations are recorded
  - Runtime/editor compilation, both .NET builds, shader compilation, live
    production execution, fresh console inspection, and `git diff --check`
    succeed

#### Pre-change run 2026-08-28 - invalid validation

- Engine build `26.08.19`; revision `723c3a2+pre-gpu-mesh`; run ID
  `d56a0a00f84f476db681bf10b241f1e3`.
- The unchanged loop completed and saved, but its captured start center was
  `(5946.25,-5913.322)` instead of the fixed `(0,0,0)` even though the player
  position readback immediately before invocation reported zero. The result is
  retained but cannot serve as the comparison baseline.
- Recorded workload: one loop; speed `2500`; distance `50000`; duration
  `121.94201` seconds; `26,158` frame samples; zero truncated samples.
- Frame: average FPS `214.50783`; p95 `10.4275` ms; p99 `14.0047` ms; average
  GPU `0.5459466` ms.
- Memory bytes: process average `3147320034`, peak `3177148416`; GPU
  average/peak `1088171346`; GPU budget `32945209344`.
- Chunks: loaded `35,937`; pending `0`; integrated `852,390`; integrated per
  second `6990.126`; last settle `15.1294` ms.
- Outcome: invalid validation due to the wrong initial position, not a product
  failure. No parameters were changed.

#### Pre-change baseline 2026-08-28 - pass

- Engine build `26.08.19`; revision `723c3a2+pre-gpu-mesh-rerun`; run ID
  `f15875d1a7f443acb91baede2b7eabb9`.
- Workload/world: one completed loop; speed `2500`; distance `50000`; duration
  `121.943344` seconds; start and target `(0,0,0)`; scene `basic_example`;
  `32` cells/axis; cell size `16`; load radius `16`; surface height `0`.
- Frame: `26,018` samples; zero truncated; average FPS `213.35796`; p95
  `11.1269` ms; p99 `14.7592` ms; average GPU `0.5764865` ms.
- Memory bytes: process average `3147685787`, peak `3167334400`; GPU
  average/peak `1088171346`; GPU budget `32945209344`.
- Chunks: loaded `33,792`; pending `2,145`; integrated `842,721`; integrated
  per second `6910.7583`; last generated `2,145`; last settle `14.7391` ms;
  last effective per second `145531.27`.
- Outcome: pass. The unchanged production journey completed from the exact
  fixed origin and provides the pre-GPU-mesh comparison baseline.

#### GPU correctness run 2026-08-28 - pass

- Engine build `26.08.19`; production scene `basic_example`; fixed world origin;
  LOD0; `32` cells/axis; cell size `16`; surface height `0`; normal step `8`;
  at most `8` dispatches/update.
- Bounded scalar diagnostics, after queue settlement:
  - `C[0,0,0]`: `1,024` active cells; `2,048` logical triangles; `0`
    invalid gradients; `0` overflow; `32,768` capacity records (`131,072`
    bytes).
  - `C[-1,0,0]`: `1,024` active cells; `2,048` logical triangles; `0`
    invalid gradients; `0` overflow.
  - `C[0,-1,0]`: `1,024` active cells; `2,048` logical triangles; `0`
    invalid gradients; `0` overflow.
  - `C[-1,-1,0]`: `1,024` active cells; `2,048` logical triangles; `0`
    invalid gradients; `0` overflow.
  - `C[0,0,-1]`: classified completely solid; no GPU mesh resource.
  - `C[0,0,1]`: classified completely air; no GPU mesh resource.
- The four surface inspections performed `8` scalar readbacks total (one
  indirect-argument record and one two-word statistics record per chunk).
  Geometry readbacks remained `0`.
- Two `1280x720` production game-camera captures were taken through
  `camera_screenshot`: one directly above the X/Y boundary intersection and
  one oblique. Both showed continuous grass coverage through world zero,
  consistent upward lighting, no background seam pixels, no z-fighting, and
  correct visibility with backface culling. Capture artifacts are retained in
  the task conversation.
- Outcome: pass.

#### Candidate run 2026-08-28 - invalid memory comparison

- Run ID `cbdecdc256814c8ab5f60ad7540aaf51`; revision
  `723c3a2+gpu-voxel-mesh-candidate`; exact locked figure-eight parameters.
- Frame: `25,553` samples; average FPS `209.53787`; p95 `12.3735` ms; p99
  `16.1783` ms; average full-frame GPU `1.0745332` ms.
- Meshing: `25,472` dispatches; `1,024` resident; `0` pending; logical buffers
  `134,217,728` bytes; `0` pool allocations; `25,482` pool reuses; `0`
  scalar and geometry readbacks.
- This run followed an earlier falling-player debug session in the same editor
  process, so its `5,610,180,809` average and `5,896,654,848` peak process
  bytes cannot be compared with the clean baseline. The record is retained but
  is not acceptance evidence.
- Outcome: invalid validation, not a product failure.

#### Pinned warmup run 2026-08-28 - warmup evidence

- Run ID `cbdb20a41a244a80bdbb5da43b831de2`; revision
  `723c3a2+gpu-voxel-mesh-candidate-pinned`; clean editor process; exact locked
  figure-eight parameters. The test-only controller preserved X/Y route motion,
  pinned world Z to `0`, and cleared vertical rigidbody velocity each update.
- Frame: `25,398` samples; average FPS `208.2731`; p95 `13.0691` ms; p99
  `16.5155` ms; average full-frame GPU `1.1493821` ms.
- Memory bytes: process start `4,580,339,712`, end `4,859,097,088`, average
  `4,812,215,950`, peak `4,925,837,312`; GPU start/end/average/peak
  `1,341,880,054`; GPU budget `32,945,209,344`.
- Meshing: `26,475` dispatches; `1,024` resident; `0` pending; logical buffers
  `134,217,728` bytes; `0` pool allocations; `26,493` pool reuses; `0`
  scalar and geometry readbacks.
- The one-time process-memory increase established the route/cache warmup and
  was not accepted as steady-state memory evidence. The next run reused the
  same locked workload without parameter changes.

#### Pinned steady-state run 2026-08-28 - pass

- Run ID `a065a068d5294625933cace487fcc90c`; revision
  `723c3a2+gpu-voxel-mesh-candidate-pinned-steady`; exact locked figure-eight:
  one loop, speed `2500`, distance `50000`, world Z `0`, duration `121.93529`
  seconds, origin start/target, `32` cells/axis, cell size `16`, radius `16`.
- Frame: `25,299` samples; `0` truncated; average FPS `207.46445`; p95
  `13.2854` ms; p99 `16.7176` ms; average full-frame GPU `1.1473838` ms.
  Both tail limits passed. Relative to the pre-change baseline, p95 increased
  `2.1585` ms, p99 increased `1.9584` ms, and average GPU increased about
  `0.571` ms while rendering the production terrain.
- Memory bytes: process start `4,853,780,480`, end `4,845,621,248`, average
  `4,836,273,840`, peak `4,858,867,712`; GPU start/end/average/peak
  `1,341,880,054`; GPU budget `32,945,209,344`. Process memory decreased
  `8,159,232` bytes and GPU memory was constant across the loop.
- Engine GPU-memory delta from the accepted pre-change baseline was
  `253,708,708` bytes. Voxel-owned logical active-cell capacity was
  `134,217,728` bytes (`128 MiB`) at capture, below the `256 MiB` limit.
- Chunks: `33,792` loaded; `2,145` pending at the route completion snapshot;
  `878,427` integrated; `7,204.0425` integrated/second; last stream generated
  `2,145` and settled in `21.0872` ms. This matches the established snapshot
  timing behavior of the accepted baseline, which also captured `2,145`
  pending chunks.
- Meshing: `26,474` dispatches; `1,024` resident; mesh backlog `0`; `65` pooled;
  `0` pool allocations after warmup; `26,486` pool reuses; `0` scalar
  readbacks during measurement; `0` geometry readbacks.
- `GpuProfilerStats` was enabled and the named command list was used for all
  compute dispatches. This engine snapshot did not expose the command-list path
  in `GpuProfilerStats.Entries`, so named smoothed/max timings were recorded as
  unavailable (`0`); full-frame GPU impact above remains valid.
- The sandbox whitelist does not expose a per-scope managed allocation byte
  counter. Direct mesher allocation bytes therefore remain unavailable; the
  measured production proxy is `0` mesh-resource pool allocations after warmup
  with bounded command-list and collection capacities. This limitation is not
  presented as a direct byte-allocation measurement.
- Runtime and editor builds passed with `0` warnings and `0` errors. The compute
  and terrain shader sources compiled; terrain reported `2` combos, compute had
  already produced its valid `1`-combo asset used by the live run. Fresh live
  compiler state passed, fresh console errors were `0`, and
  `git diff --check` passed.
- Outcome: pass for correctness, tail latency, GPU memory, steady process
  memory, streaming availability, backlog settlement, pooling, and zero geometry
  readback. Named compute timing and direct allocation-byte attribution were not
  available from the installed public diagnostics and are explicitly unverified.

### GPU-ACTIVE-CELL-PACK-001/v1 - Packed metadata vertex early-out

- Definition: optimize the production `GPU-VOXEL-MESH-001/v1` path without
  changing its fixed world, geometry, buffer capacity, or indirect draw shape.
  Active-cell records pack six-bit local X/Y/Z, a three-bit triangle count, and
  the existing eight-bit case index. Padded vertices must exit before topology,
  SDF interpolation, and gradient work while preserving rendered output.
- Correctness workload: unchanged `GPU-VOXEL-MESH-001/v1` scene and probes at
  `32` cells/axis, cell size `16`, radius `16`, surface height `0`, LOD0, normal
  step `8`, and at most `8` dispatches/update. Performance workload: unchanged
  `PERFORMANCE-OVERVIEW-001/v3`, one local player from `(0,0,0)`, speed `2500`,
  distance `50000`, fixed Z `0`, one loop, per-update sampling, nearest-rank
  percentiles, and engine build `26.08.19`.
- Pass criteria: every fixed mesh probe retains its canonical counts and finite
  gradients; solid/air classification, boundaries, lighting, and culling remain
  correct; p95 remains at most `16.67 ms`, p99 at most `25 ms`; backlog returns
  to zero; pool allocations and geometry readbacks remain zero; logical voxel
  buffers remain below `256 MiB`; consecutive runs show no unbounded memory
  growth; builds, shader compilation, live compiler state, console errors, and
  `git diff --check` pass.

#### Pre-change run 2026-08-28 - pass

- Run ID `6a925eb2c58c4e4892c11cb093181992`; revision
  `422a4f8-pre-active-cell-pack`; duration `121.93478` seconds; `25,072` samples;
  zero truncated samples.
- Frame: average FPS `205.60045`; p95 `13.8122 ms`; p99 `17.3015 ms`; average
  full-frame GPU `1.1994879 ms`.
- Memory bytes: process start `2,441,359,360`, end `2,443,816,960`, average
  `2,539,527,654`, peak `2,630,955,008`; GPU start/end/average/peak
  `1,365,095,974`; GPU budget `32,945,209,344`.
- Meshing: `25,462` dispatches; `1,024` resident; `0` pending; `65` pooled;
  `134,217,728` logical bytes; `0` pool allocations; `25,470` reuses; `0`
  scalar and geometry readbacks. Named meshing GPU timing and direct allocated
  bytes remained unavailable from the installed public diagnostics.

#### Candidate and steady-state runs 2026-08-28 - pass

- First candidate run ID `e3d6dd8e94d24a199ed8a5ccdcb7d24d`; revision
  `422a4f8+active-cell-pack-candidate`; duration `121.934525` seconds; `28,329`
  samples; zero truncated; average FPS `232.30719`; p95 `5.2992 ms`; p99
  `8.8741 ms`; average GPU `1.1678941 ms`.
- First-run memory bytes: process start `2,817,224,704`, end `2,847,035,392`,
  average `2,881,167,057`, peak `3,011,670,016`; GPU constant at
  `1,369,096,198`. This run includes post-shader warmup and is retained rather
  than used alone for steady-memory acceptance.
- Steady run ID `7e402734e60042bab79f9743065e784a`; revision
  `422a4f8+active-cell-pack-steady`; duration `121.935745` seconds; `26,797`
  samples; zero truncated; average FPS `219.74837`; p95 `6.2657 ms`; p99
  `16.3802 ms`; average GPU `1.1837764 ms`.
- Steady memory bytes: process start `2,820,841,472`, end `2,830,811,136`,
  average `2,927,531,561`, peak `3,020,550,144`; GPU start `1,369,096,198`, end
  `1,365,095,974`, average `1,367,003,385`, peak `1,369,096,198`. The steady
  final process value was `16,224,256` bytes below the preceding run's final
  value, and GPU memory decreased, demonstrating no continuing loop-over-loop
  growth.
- Steady meshing: `25,485` dispatches; `1,024` resident; `0` pending; `65`
  pooled; `134,217,728` logical bytes; `0` pool allocations; `25,491` reuses;
  `0` scalar and geometry readbacks. Streaming retained `33,792` loaded chunks
  with the established completion-snapshot `2,145` pending chunks; the mesh
  backlog returned to zero.
- Relative to baseline, steady average GPU time decreased `0.0157115 ms`
  (`1.31%`), while p95 decreased `7.5465 ms` and p99 decreased `0.9213 ms`.
  Named vertex-stage timing remains unavailable, so only the full-frame GPU
  change is claimed as measured benefit.
- Correctness probes after queue settlement: `C[0,0,0]`, `C[-1,0,0]`,
  `C[0,-1,0]`, and `C[-1,-1,0]` each reported `1,024` active cells, `2,048`
  logical triangles, `0` invalid gradients, and `0` overflow. `C[0,0,-1]` was
  solid and `C[0,0,1]` was air; neither owned a GPU mesh resource. Eight bounded
  scalar readbacks performed these inspections; geometry readbacks remained
  zero. A fresh `1280x720` production camera capture showed continuous terrain,
  consistent lighting, and correct culling without seams or z-fighting.
- The flat field emits two triangles per active cell, so `9` of each fixed `15`
  vertex slots (`60%`) now take the constant clipped early-out. Across `1,024`
  resident surface chunks, `9,437,184` padded vertex invocations per rendered
  frame avoid topology lookup, coordinate division, SDF interpolation, and six
  central-gradient samples.
- Runtime and editor .NET builds passed with `0` warnings and `0` errors. Both
  shader sources compiled on demand and executed in the live production path;
  fresh live compiler state reported success with `0` errors, fresh console
  errors were `0`, and `git diff --check` passed.
- Outcome: pass. Geometry and GPU ownership are unchanged, correctness is
  preserved, all fixed budgets pass, and full-frame GPU time improved without a
  material regression in any measured acceptance dimension.

### VOXEL-LOGGING-001/v1 - Opt-in verbose runtime logging

- Definition recorded before the candidate run. Use the unchanged
  `PERFORMANCE-OVERVIEW-001/v3` production figure eight at speed `2500`, distance
  `50000`, fixed Z `0`, one loop, and the existing `GPU-VOXEL-MESH-001/v1`
  world parameters on engine `26.08.19`.
- Baseline: run `7e402734e60042bab79f9743065e784a` from revision
  `422a4f8+active-cell-pack-steady`, with unconditional stream begin/completion
  logging. The live console separately demonstrated at least `20` begin and `20`
  large completion records within four seconds of production movement.
- Candidate behavior: `VerboseLogging=false` by default; normal movement emits
  zero stream begin, completion, or stale-detail records and performs none of
  their string formatting, process-memory query, or density/material probes.
  Enabling the setting in the inspector or through
  `voxel_verbose_logging true|false` restores those details. Warnings, errors,
  explicit diagnostics, and the sparse performance begin/save records remain
  available.
- Pass criteria: zero routine stream records with verbose logging disabled;
  verbose mode restores begin/completion records; performance results still save;
  p95 remains at most `16.67 ms`, p99 at most `25 ms`; mesh backlog, pooling,
  memory, readbacks, builds, live compilation, console errors, and
  `git diff --check` retain the existing production acceptance requirements.

#### Candidate run 2026-08-28 - pass

- Run ID `d72e003452f2483b8299919c65c05d73`; revision
  `87a0f06+quiet-logging-candidate`; exact locked figure eight from `(0,0,0)`;
  duration `121.93505` seconds; `28,846` samples; zero truncated samples.
- Frame: average FPS `236.54837`; p95 `5.1717 ms`; p99 `7.3602 ms`; average
  full-frame GPU `1.1492101 ms`. Relative to the logging-on baseline, p95
  decreased `1.094 ms`, p99 decreased `9.02 ms`, average GPU decreased
  `0.0345663 ms` (`2.92%`), and average FPS increased `7.64%`.
- Memory bytes: process start `3,451,805,696`, end `3,131,015,168`, average
  `3,289,091,290`, peak `3,462,180,864`; GPU start/end/average/peak
  `1,365,095,974`; GPU budget `32,945,209,344`. Process memory decreased across
  the loop and GPU memory remained constant.
- Streaming/meshing: `33,792` loaded chunks and the established completion
  snapshot of `2,145` pending chunks; `843,216` integrated; last settle
  `9.4243 ms`; `25,509` mesh dispatches; `1,024` resident meshes; mesh backlog
  `0`; `65` pooled; `134,217,728` logical bytes; `0` pool allocations; `25,521`
  reuses; `0` scalar and geometry readbacks.
- Console interval `16:40:43` through `16:42:45` contained the sparse
  performance begin/save records and exactly `0` `stream.begin`, `0`
  `stream.complete`, and `0` `stream.stale` records. A separate live boundary
  move with verbose logging enabled at `16:40:08` emitted one begin/completion
  pair, proving the troubleshooting path remains available; it was switched off
  before measurement.
- Runtime and editor builds passed with `0` warnings and `0` errors. Live
  compilation succeeded with `0` errors, fresh runtime console errors were `0`,
  and `git diff --check` passed.
- Outcome: pass. Routine production logging and its diagnostic-only work are
  absent by default, opt-in detail remains bounded and functional, the structured
  performance result still saves, and every fixed acceptance budget passes.

### GPU-MESH-DIAGNOSTICS-001/v1 - On-demand mesh diagnostics

- Definition recorded before source implementation. Remove logical-triangle
  atomics and center-gradient validation from the normal production meshing
  dispatch. The bounded `inspect_gpu_mesh` path must instead derive those
  statistics from the resident GPU active-cell stream in a separate on-demand
  compute pass. Normal meshing must not allocate, clear, bind, or write a
  statistics buffer.
- Correctness workload: unchanged `GPU-VOXEL-MESH-001/v1` world and probes at
  `32` cells/axis, cell size `16`, radius `16`, surface height `0`, LOD0, normal
  step `8`, and at most `8` mesh dispatches/update. Performance workload:
  unchanged `PERFORMANCE-OVERVIEW-001/v3`, one local player from `(0,0,0)`,
  speed `2500`, distance `50000`, fixed Z `0`, one loop, per-update sampling,
  nearest-rank percentiles, and engine build `26.08.19`.
- Pass criteria: four surface probes retain `1,024` active cells, `2,048`
  logical triangles, finite gradients, and zero overflow; solid and air chunks
  remain resource-free; normal production records zero scalar and geometry
  readbacks; p95 remains at most `16.67 ms`, p99 at most `25 ms`; mesh backlog
  returns to zero; pool allocations remain zero after warmup; logical voxel
  buffers remain below `256 MiB`; process and GPU memory show no unbounded
  growth; both .NET projects and both shader assets compile; live compiler and
  console state are clean; and `git diff --check` passes.

#### Pre-change run 2026-08-28 - pass

- Run ID `806b0f9f7fe14ae7b8bdc6a91de03993`; revision `c2704a5`; exact locked
  figure eight; duration `121.936935` seconds; `28,196` samples; zero truncated.
- Frame: average FPS `231.2179`; p95 `5.8408 ms`; p99 `8.1294 ms`; average
  full-frame GPU `1.1578912 ms`.
- Memory bytes: process start `3,113,435,136`, end `3,105,312,768`, average
  `3,105,332,207`, peak `3,115,651,072`; GPU start/end/average/peak
  `1,365,095,974`; GPU budget `32,945,209,344`.
- Streaming/meshing: `33,792` loaded chunks; completion snapshot `2,145`
  pending chunks; `843,183` integrated; `25,509` mesh dispatches; `1,024`
  resident meshes; mesh backlog `0`; `65` pooled; `134,217,728` logical bytes;
  `0` pool allocations; `25,514` reuses; `0` scalar and geometry readbacks.
- Outcome: accepted comparable baseline. The normal compute shader still
  performed one logical-triangle atomic and one center-gradient sample per
  active cell, plus invalid-gradient atomics when applicable.

#### Development probes 2026-08-28 - failed, corrected

- The first on-demand probe ran before the new diagnostic shader's generated
  asset existed and reported `1,024` indirect active cells but `0` triangles.
  The live console recorded the failed on-demand compile and missing compiled
  resource at `16:58:19`. Recompiling the source removed the resource error.
- A second attempt copied the append counter successfully but could not consume
  the append buffer as a compute SRV in this installed engine path. It reported
  `1,024` indirect and diagnostic active cells but `0` triangles. This approach
  was removed rather than retained as a fallback.
- The accepted diagnostic now shares one regular-cell classification function
  with production and re-evaluates only the explicitly inspected descriptor.
  These failed development observations are retained and are not acceptance
  evidence.

#### Candidate run 2026-08-28 - pass

- Run ID `2e6a7fd6771440c0ad30dab888004818`; revision label
  `c2704a5+on-demand-mesh-diagnostics-candidate`; exact locked figure eight;
  duration `121.94006` seconds; `28,948` samples; zero truncated.
- Frame: average FPS `237.37947`; p95 `5.0998 ms`; p99 `7.3115 ms`; average
  full-frame GPU `1.1580387 ms`. Relative to the pre-change run, p95 decreased
  `0.7410 ms`, p99 decreased `0.8179 ms`, average FPS increased `2.66%`, and
  average GPU increased `0.0001475 ms` (`0.013%`). The GPU delta is effectively
  flat and is not claimed as a measured stage-level speedup.
- Memory bytes: process start `4,511,604,736`, end `4,248,846,336`, average
  `4,332,726,473`, peak `4,511,604,736`; GPU start/end/average/peak
  `1,365,095,974`; GPU budget `32,945,209,344`. Process memory decreased
  `262,758,400` bytes and engine-tracked GPU memory remained constant.
- Streaming/meshing: `33,792` loaded chunks; completion snapshot `2,145`
  pending chunks; `843,315` integrated; last settle `6.2467 ms`; `25,510` mesh
  dispatches; `1,024` resident meshes; mesh backlog `0`; `65` pooled;
  `134,217,728` logical bytes; `0` pool allocations; `25,519` reuses; `0`
  scalar and geometry readbacks during the performance interval.
- In this flat workload, removing the normal diagnostic work avoided
  `26,122,240` logical-triangle atomics and the same number of center-gradient
  evaluations across the recorded dispatches. Each gradient previously issued
  six SDF samples, totaling `156,733,440` avoided diagnostic SDF evaluations.
  Named meshing compute timing remains unavailable in the installed profiler.
- Correctness probes after queue settlement: `C[0,0,0]`, `C[-1,0,0]`,
  `C[0,-1,0]`, and `C[-1,-1,0]` each reported `1,024` indirect active cells,
  `1,024` diagnostic active cells, `2,048` logical triangles, `0` invalid
  gradients, and `0` overflow. `C[0,0,-1]` was solid and `C[0,0,1]` was air;
  neither owned a GPU mesh resource. The four surface inspections performed
  eight scalar readbacks; geometry readbacks remained zero. A fresh `1280x720`
  game capture showed continuous terrain, consistent lighting, and no visible
  seam or z-fighting.
- Both modified shader sources compiled live at `17:07:08`; the diagnostic
  shader recompiled again at `17:07:15` after its final source update. Final
  runtime and editor builds passed with `0` warnings and `0` errors. Settled
  live compiler state reported success with `0` errors and `0` warnings for
  both project compilers. The only retained console warnings were the documented
  failed development compile at `16:58:19`; no fresh warning or error followed
  the successful shader compiles and accepted run. `git diff --check` passed.
- Outcome: pass. Normal meshing no longer allocates, clears, binds, or writes
  diagnostic statistics and performs no diagnostic gradient sampling. The
  bounded inspection path remains available on demand, correctness is
  unchanged, and every measured acceptance budget passes without a material
  regression.

### GPU-FRUSTUM-CULL-001/v1 - Conservative resident-mesh frustum culling

- Definition recorded before implementation and before the first measurement.
- Production world: `Assets/scenes/basic_example.scene`; one local player; the
  existing user-authored plane renderer/collider remains enabled and unchanged;
  main game camera at `1280x720`, horizontal FOV `60`; engine build `26.08.19`.
- Fixed world parameters: `CellsPerAxis=32`; `CellSize=16`; `LoadRadius=16`;
  `TerrainSurfaceHeight=0`; LOD0 regular-cell Transvoxel; at most `8` mesh
  dispatches per update. Logical residency, streaming, and mesh residency use
  their existing production paths.
- Fixed player journey: unchanged `PERFORMANCE-OVERVIEW-001/v3` figure eight
  from `(0,0,0)` at fixed world Z `0`, speed `2500`, distance `50000`, one loop,
  frame capacity `524,288`, per-update frame sampling, one-second memory
  sampling, and nearest-rank p95/p99 frame duration. Hardware, graphics settings,
  VSync/frame-cap state, warmup procedure, and initial camera orientation are
  recorded with the baseline and held unchanged for the candidate.
- Visibility definitions: resident means GPU-resident surface meshes, not all
  logically loaded chunks. A non-zero terrain draw is a resident indirect draw
  whose GPU-produced instance count is non-zero. The installed API has no
  GPU-produced draw-count entry point, so fixed zero-instance command entries do
  not count as geometry-bearing terrain draws.
- Fixed correctness views use the production main camera and the unobscured
  chunk-boundary intersection `(8192,8192,0)`: top-down from
  `(8192,8192,2048)`, grazing from `(6144,6144,128)`, inside/near a chunk bound,
  and a 180-degree turn with a slow extreme-angle frustum-edge sweep. Existing
  surface/solid/air GPU mesh probes remain unchanged.
- Pass criteria fixed before execution:
  - Candidate average non-zero terrain indirect draws decrease by at least 25%
    relative to average resident surface meshes; no visible boundary, seam,
    popping, or early-disappearance defect is observed.
  - Candidate average full-frame GPU duration improves by at least `0.05 ms`;
    p95 remains at most `16.67 ms`; p99 remains at most `25 ms`; no other
    correctness, responsiveness, streaming, or frame-pacing regression hides the
    improvement.
  - Logical visibility buffers remain below `1 MiB` at the production workload;
    engine GPU memory increases by no more than `16 MiB` and shows no continuing
    loop-over-loop growth.
  - Loaded chunks and resident meshes remain available, the mesh backlog returns
    to zero, stale revisions remain rejected, and post-warmup mesh pool
    allocations remain zero.
  - Normal production performs zero visibility and geometry readbacks. The
    opted-in completed performance run performs exactly one asynchronous scalar
    visibility readback after its measured boundary and never uses that data to
    decide rendering.
  - Full chunk-volume AABBs are expanded by one cell and rejected only when all
    eight corners are outside one homogeneous frustum plane. No density,
    heightfield, neighbor, occlusion, cave, or overhang assumption participates.
  - Runtime/editor builds, shader compilation, live production execution, fresh
    console inspection, and `git diff --check` pass.
- Current production terrain contains no cave or overhang geometry. This
  scenario validates cave/overhang readiness structurally through full 3D bounds
  and absence of terrain-shape heuristics; it does not claim unavailable visual
  cave coverage or introduce synthetic terrain.

#### Uncullled baseline 2026-08-28 - pass

- Run ID `63487a58582344d497d6b063581086bc`; revision label
  `0ad2a40+unculled-baseline`; engine build `26.08.19`; AMD Ryzen 7 9800X3D;
  NVIDIA GeForce RTX 5090 driver `32.0.16.1088`. The existing plane and editor
  graphics state remained unchanged.
- Exact locked figure eight: origin start, fixed Z `0`, speed `2500`, distance
  `50000`, one loop, duration `121.943184` seconds, `28,052` frame samples, and
  zero truncated samples.
- Frame: average FPS `230.03337`; p95 `5.4749 ms`; p99 `7.9662 ms`; average
  full-frame GPU `1.1383078 ms`.
- GPU memory: average `1,362,484,170` bytes; peak `1,365,132,546` bytes.
- Streaming/meshing: `33,792` loaded; completion snapshot `2,145` pending;
  `25,504` mesh dispatches; `1,024` resident surface meshes; mesh backlog `0`;
  `65` pooled resources; `0` pool allocations; `25,515` pool reuses; `0`
  scalar and geometry readbacks.
- The canonical uncullled path submits one non-zero indirect terrain draw for
  every resident surface mesh, so the baseline contains `1,024` resident,
  visible, and geometry-bearing terrain draws at the completion snapshot. No
  frustum visibility classification or visibility readback existed.
- Outcome: pass and accepted as the fixed pre-change comparison. The production
  player journey completed, streaming and meshing settled normally, and no
  terminal console error was observed.

#### GPU-written indirect development probes 2026-08-28 - fail

- Engine build `26.08.19`; Vulkan renderer; the fixed scenario candidate was
  not started because the implementation failed the earlier live safety gate.
- A compute classifier using conservative full-volume chunk bounds completed
  without device loss while terrain continued to draw from each mesh's original
  indirect buffer. Copying append counters into a shared source-count buffer was
  also stable with explicit transitions and UAV barriers.
- Every probe that changed terrain draws to consume the compute-written shared
  indirect-argument buffer ended in GPU device loss or the editor's GPU-crash
  handler. This reproduced with `ByteAddress | IndirectDrawArguments` and
  `Structured | IndirectDrawArguments`, with explicit UAV/indirect transitions,
  and with compute plus draws combined in one camera command list.
- No performance sample, correctness capture, or visibility scalar readback was
  accepted from these failed probes. They are not candidate results and cannot
  be compared with the baseline.
- The experimental buffers, shaders, instrumentation, and draw path were
  removed after the failure. Runtime and editor builds then passed with zero
  warnings and errors, `git diff --check` passed, and a clear-state editor launch
  compiled successfully on the restored per-resource indirect renderer.
- A read-only comparison with the neighboring `voxels2` project found no
  GPU-written visibility/indirect precedent: that project performs CPU frustum
  tests, compacts indirect commands on the CPU, uploads them with `SetData`, and
  only consumes the uploaded buffer on the GPU. Adopting that route would change
  this scenario's GPU-only visibility architecture and exactly-one-readback
  contract, so it requires an explicit design and validation decision rather
  than being treated as a like-for-like fix.

#### Visibility telemetry development runs 2026-08-28 - fail

- All runs used the locked `PERFORMANCE-OVERVIEW-001/v3` world and nominal
  `121.94`-second figure-eight workload. They are retained as failures rather
  than discarded. Engine build was `26.08.19` on the baseline hardware.
- Prototype 2: run `585a3441facc4dd6b275606b704ccc08`, revision
  `0ad2a40+gpu-frustum-prototype-2`; `20,777` frames; average GPU
  `0.72838247 ms`; p95/p99 `7.1405/9.3374 ms`; `1,032` resident and `57`
  pending mesh results at capture. The one scalar readback returned `0`
  visibility samples, so visible/resident draw metrics were invalid.
- Prototype 3: run `c93ef2ab44c141f9a018c0ecffa4260c`, revision
  `0ad2a40+gpu-frustum-prototype-3`; `21,115` frames; average GPU
  `0.7318834 ms`; p95/p99 `7.0765/9.4759 ms`; `1,032` resident and `57`
  pending. Visibility samples remained `0` after one scalar readback.
- Prototype 4: run `a02cc83b8ab64b8aa0fc15e1d90aceb7`, revision
  `0ad2a40+gpu-frustum-prototype-4`; `21,119` frames; average GPU
  `0.7327399 ms`; p95/p99 `7.0421/9.4694 ms`; `1,032` resident and `57`
  pending. Visibility samples remained `0` after one scalar readback.
- Prototype 5: run `d2ffa1441744489bb008dcdb3f426424`, revision
  `0ad2a40+gpu-frustum-prototype-5`; `23,129` frames; average GPU
  `0.73450196 ms`; p95/p99 `6.5038/8.8201 ms`; `1,040` resident and `49`
  pending. Visibility samples remained `0` after one scalar readback.
- Every failed run recorded `35,937` loaded chunks, `0` chunk backlog, `0`
  post-warmup pool allocations, `0` normal meshing scalar readbacks, `0`
  geometry readbacks, `131,100` logical visibility bytes, and exactly one
  visibility scalar readback. Failure was the absent GPU aggregate sample, not
  a favorable partial result.

#### GPU frustum prototype 6 2026-08-28 - measured pass, not final acceptance

- Run `7fe7c5b57d654320a758a39b416f60ad`; revision
  `0ad2a40+gpu-frustum-prototype-6`; captured
  `2026-08-28T22:48:14.6364964+00:00` on the locked baseline hardware and
  settings. The full player journey completed in `121.94015` seconds with
  `29,100` frame samples and no truncation.
- Frame results: average FPS `238.6298`; average full-frame GPU
  `0.70830303 ms`; p95 `5.0253 ms`; p99 `6.9579 ms`.
- GPU memory was constant at `1,342,113,110` start/end/average/peak bytes.
  Logical visibility storage was `131,100` bytes (`0.125 MiB`).
- Streaming/meshing capture: `35,937` loaded, `0` chunk pending; `25,513` mesh
  dispatches; `1,040` resident and `49` in-flight/pending mesh results; `41`
  pooled; `0` post-warmup allocations; `25,522` pool reuses; `0` normal meshing
  scalar readbacks; `0` geometry readbacks.
- GPU visibility: `29,099` samples; average resident surface meshes
  `1,085.5598`; average visible/non-zero indirect draws `253.75165`; minimum/
  maximum visible `238/269`; average culled draws `831.80817`; reduction
  `76.62482%`; exactly one asynchronous visibility scalar readback.
- Versus the accepted uncullled baseline, average GPU time improved
  `0.43000477 ms` (`37.78%`), p95 improved `0.4496 ms`, p99 improved
  `1.0083 ms`, and average GPU memory decreased `20,371,060` bytes. Draw
  reduction exceeded the required `25%`; GPU improvement exceeded `0.05 ms`;
  p95/p99, logical memory, engine memory, pool allocation, and readback budgets
  passed.
- Fixed production probes after the run reported `1,024` active cells,
  `2,048` triangles, `0` invalid gradients, and `0` overflow for
  `C[0,0,0]`, `C[-1,0,0]`, `C[0,-1,0]`, and `C[-1,-1,0]`. `C[0,0,-1]`
  remained completely solid and `C[0,0,1]` completely air with no mesh
  resource. Geometry readbacks remained zero.
- Production-camera captures at `(8192,8192,0)` covered top-down, grazing,
  inside/near-bound, a 180-degree turn, and incremental extreme-angle edge
  views. The flat production surface remained continuous without an observed
  seam, hole, early disappearance, or popping. No cave/overhang visual claim is
  made; readiness is structural through full 3D padded bounds and the absence
  of heightfield, density, neighbor, or enclosure heuristics.
- This run proves the GPU culling and telemetry route, but it is not the final
  repository acceptance result. The completion snapshot still contained `49`
  mesh results awaiting commit, and the scene-object bounds implementation was
  refined afterward to use the installed engine's native infinite custom-object
  bounds. The fixed figure eight must complete once more on the final source
  before commit/push.

#### Final-source rerun attempts 2026-08-28 - incomplete

- Prototype 7 began with the locked speed, distance, loop count, resolution, and
  world settings, but an earlier editor transform persisted and the begin record
  reported center `[512,0,0]`. This violates the immutable origin and no result
  was accepted.
- Prototypes 8 and 9 began from the correct origin with the same locked inputs.
  After the MCP-driven play restart, the game camera rendered the resident
  terrain on demand, but simulation updates did not advance: the production
  player remained exactly `(0,0,0)` and neither run completed or wrote a result
  record. Waiting longer, requesting camera renders, polling editor state, and
  toggling pause/resume did not advance the journey.
- Installed engine source at public cache commit
  `0f86783568728892a4ea318779cc3681aef8ae94` confirms that
  `SceneCustomObject` starts with native infinite bounds specifically so its
  callback renders when callers do not need spatial bounds. Final source now
  keeps that supported default and removes both the experimental billion-unit
  AABB and camera-relative helper bound.
- Outcome: incomplete, not accepted, not committed, and not pushed. The blocker
  is the restarted live session failing to advance the production player
  journey, not a substituted shorter workload. The unchanged canonical scenario
  remains required.

#### Clean-start lifecycle correction 2026-08-28 - pass

- A fully restarted editor exposed a circular startup dependency rather than a
  visibility-classification failure. `VoxelManager.OnLoad` waited for the GPU
  mesh queue, while that queue required camera rendering that cannot begin until
  component loading completes. The authored chunks became logically available,
  but update-driven movement and the measured journey could not start.
- The canonical ownership boundary now ends `OnLoad` when the authoritative
  logical chunk stream has integrated. Derived GPU mesh work drains afterward
  through the unchanged production limit of eight dispatches per update. No
  terrain state, streaming decision, mesh owner, revision rule, or render
  visibility decision moved to the CPU.
- Live compilation completed with zero errors and warnings. After a clean play
  restart, the outer production surface chunk `C[16,16,0]` became GPU resident
  and reported `1,024` active cells, `2,048` triangles, `0` invalid gradients,
  `0` overflow, and `0` geometry readbacks. A full figure eight then advanced
  and saved normally without a crash or device-loss entry.
- The opted-in result finalizer was also corrected to wait for the route's final
  derived mesh backlog to settle before saving residency/backlog fields. The
  measured loop and its GPU/frame samples end at the original locked boundary;
  only the post-measurement snapshot/save is deferred.

#### Final GPU frustum candidate 2026-08-28 - pass

- Run ID `3973db55b60f4c4e935232715e813e31`; revision label
  `0ad2a40+gpu-frustum-final-4`; engine build `26.08.19`; unchanged baseline
  hardware, driver, graphics state, existing plane, camera orientation, and
  warmup procedure.
- Exact locked figure eight: origin start, fixed Z `0`, speed `2500`, distance
  `50000`, one loop, duration `121.94021` seconds, `29,147` frame samples, and
  zero truncated samples.
- Frame results: average FPS `239.01791`; average full-frame GPU
  `0.5307214 ms`; p95 `5.0189 ms`; p99 `6.7573 ms`.
- GPU memory was constant at `1,242,977,954` start/end/average/peak bytes.
  Logical visibility storage was `131,100` bytes (`0.125 MiB`). Relative to the
  uncullled baseline, average GPU time improved `0.6075864 ms` (`53.38%`), p95
  improved `0.4560 ms`, p99 improved `1.2089 ms`, average GPU memory decreased
  `119,506,216` bytes, and peak GPU memory decreased `122,154,592` bytes.
- Streaming/meshing: `35,937` loaded chunks, `0` chunk pending; `25,516` mesh
  dispatches; `1,089` resident surface meshes, `0` mesh pending, `0` pooled at
  the settled origin; `0` post-warmup pool allocations; `25,525` pool reuses;
  `0` normal meshing scalar readbacks; `0` geometry readbacks. The candidate did
  not reduce logical or GPU-resident terrain availability.
- GPU visibility: `29,146` samples; average resident surface meshes
  `1,085.5763`; average visible/non-zero terrain indirect draws `253.74707`;
  minimum/maximum visible `238/269`; average culled draws `831.8292`; draw
  reduction `76.62559%`; exactly one asynchronous visibility scalar readback.
- Post-run probes reported `1,024` active cells, `2,048` triangles, `0` invalid
  gradients, and `0` overflow for `C[0,0,0]`, `C[-1,0,0]`, `C[0,-1,0]`, and
  `C[-1,-1,0]`. `C[0,0,-1]` remained completely solid and `C[0,0,1]`
  completely air with no mesh resource. Geometry readbacks remained zero.
- The unchanged final visibility shader and draw route retain the production
  camera evidence recorded with prototype 6: top-down, grazing, inside/near
  bound, 180-degree turn, and extreme-angle frustum-edge views showed a
  continuous surface without an observed seam, hole, early disappearance, or
  popping. The later lifecycle/result-finalization corrections do not modify
  bounds, classification, indirect arguments, draw attributes, or camera
  rendering. Cave/overhang readiness remains structural, not a visual claim.
- `dotnet build Code/voxels3.csproj --no-restore` and
  `dotnet build Editor/voxels3.editor.csproj --no-restore` both completed with
  zero warnings and errors. The live engine generated
  `voxel_chunk_visibility_cs.shader_c` from the new source with the same UTC
  timestamp and executed it throughout the measured run. The generic MCP asset
  recompile endpoint could not force a second compile because it classified the
  shader as compiled-only, but the live compiler remained successful with zero
  errors, the fresh console contained no runtime or shader error, and
  `git diff --check` passed. The remaining console warnings are unrelated engine
  startup resource/font warnings and predate the candidate run.
- Outcome: pass. Average non-zero draw reduction exceeds `25%`; average GPU
  improvement exceeds `0.05 ms`; p95/p99, logical buffer memory, engine GPU
  memory, settled backlog, pooling, dispatch, readback, and mesh-correctness
  gates all pass. The production session completed without a new runtime error,
  GPU device loss, or crash.
### VISIBILITY-EDGE-STRESS-001/v1 - Rapid terrain visibility and warm-residency stress

- Definition recorded on 2026-08-28 before implementation and before the first
  comparable run.
- Production world: `Assets/scenes/basic_example.scene`; one local player;
  existing user-authored plane renderer/collider unchanged; main game camera at
  `1280x720`, horizontal FOV `60`; engine build `26.08.19`.
- Fixed world parameters: `CellsPerAxis=32`; `CellSize=16`; `LoadRadius=16`;
  `TerrainSurfaceHeight=0`; LOD0 regular-cell Transvoxel; at most `8` total GPU
  mesh dispatches per update. Gameplay residency remains the inclusive
  player-centered radius-16 cube.
- Fixed movement workload: the production `PERFORMANCE-OVERVIEW-001/v3`
  figure-eight route from `(0,0,0)` at fixed Z `0`, speed `2500`, distance
  `50000`, and one loop. During the measured loop, apply twenty alternating
  180-degree main-camera turns reaching at least `900 degrees/second`, followed
  by slow frustum-edge sweeps at extreme positive and negative pitch. Hold the
  baseline hardware, graphics settings, VSync/frame-cap state, warmup, mouse
  input trace, and initial camera orientation unchanged for the candidate.
- Visual capture: record every presented frame at `120 FPS` or higher. Inspect
  the complete capture, including camera-inside/near-bound views and lateral
  streaming-boundary crossings, for terrain-colored continuity against the
  existing background/plane. A chunk-shaped hole, white edge flash, seam, early
  disappearance, or pop in any single presented frame fails.
- Metrics: average full-frame GPU time; nearest-rank p95/p99 frame time; total,
  gameplay, and render-warm resident surface meshes; gameplay and warm mesh
  backlog; mesh dispatches and pooling; average visible/non-zero terrain draws;
  culled count/percentage; logical mesh and visibility capacity; engine GPU
  memory; scalar and geometry readbacks.
- Candidate pass criteria: zero defective captured frames; average GPU time no
  more than `0.05 ms` above accepted run
  `3973db55b60f4c4e935232715e813e31`; p95 at most `16.67 ms`; p99 at most
  `25 ms`; at least `70%` average non-zero-draw culling; gameplay and warm mesh
  backlogs settle to zero; settled flat-world residency is `1,089` gameplay
  plus `136` warm surface meshes; logical mesh capacity grows by at most
  `20 MiB`; engine GPU memory grows by at most `24 MiB` without loop-over-loop
  growth; the total dispatch cap remains `8`; the opted-in run performs exactly
  one asynchronous visibility scalar readback and zero geometry readbacks.
- The current production SDF has no caves or overhangs. Readiness is validated
  structurally through full 3D bounds, canonical full-volume SDF generation,
  and absence of heightfield, enclosure, neighbor, or visibility-derived world
  ownership; this scenario makes no unavailable visual cave claim.

#### Pre-fix figure-eight baseline 2026-08-28 - invalid for comparison

- Run `6dece56ee2ce4f6596d815dc8f857abb`; revision label
  `b885b08+pre-fix-baseline`; engine build `26.08.19`; duration `121.94102`
  seconds; `28,933` samples; zero truncated.
- The production tool began at `(-2.3004785,0.9764664,0)` rather than the
  scenario's immutable `(0,0,0)` origin, and the live registry supplied no
  camera-input or per-present video-capture route for the twenty rapid turns.
  The run is retained as invalid rather than compared as acceptance evidence.
- Supporting telemetry only: average GPU `0.69462204 ms`; p95/p99
  `5.0757/7.3443 ms`; GPU memory constant at `1,365,068,726` bytes; `35,937`
  loaded chunks; `1,089` resident meshes; zero chunk/mesh backlog; `25,542`
  mesh dispatches; zero post-warmup allocations; `25,553` pool reuses; average
  visible/resident meshes `253.25536/1,085.5249`; `76.66978%` culled; exactly
  one visibility scalar readback and zero geometry readbacks.
- Outcome: invalid for comparison. It confirms the current production route
  still completes before implementation, but it does not satisfy the fixed
  origin or rapid-camera visual workload.

#### Candidate live validation 2026-08-29 - incomplete

- Source state: working tree based on `b885b08`; engine build `26.08.19`.
  Runtime and editor builds completed with zero warnings and zero errors;
  `local.voxels3` and `local.voxels3.editor` both reported settled successful
  live compiler state with zero diagnostics. The live asset compiler classified
  the compute shader as compiled-only and could not force a redundant compile;
  the engine-generated shader binary changed after the source save and the
  camera rendered the new command path without device loss.
- A hotloaded production-camera render showed continuous terrain and confirmed
  that the combined visibility-dispatch/barrier/indirect-draw command list could
  execute. This is a bounded safety observation, not the rapid-turn acceptance
  capture.
- A required clean play restart loaded all `35,937` authoritative chunks from
  the authored origin `(0,0,0)`, but the MCP-started play session then reproduced
  the previously documented editor condition where simulation updates do not
  advance. Derived mesh draining remained at `0` resident and `1,089` queued;
  pause/resume, camera renders, and a stop/wait/start retry did not resume update
  progression. No new runtime, shader, or GPU error accompanied the stall.
- Because the production figure-eight, render-warm settlement, scalar
  visibility result, rapid camera input, and per-present capture could not run,
  no candidate frame-time, memory, culling, backlog, residency, or visual-pop
  result is accepted. The four surface and solid/air probes were also not
  rerun because their queued GPU diagnostics require advancing updates.
- Outcome: incomplete. Static builds and one bounded render safety check pass;
  the immutable player journey, warm-residency counts, performance comparison,
  and zero-defect rapid-camera capture remain required before acceptance,
  commit, or push.

#### Lateral guard 1.10 tuning 2026-08-29 - incomplete

- Source state: working tree based on `a503193`; the sole runtime behavior
  change reduces the homogeneous lateral guard from `1.25w` to `1.10w`. Full
  3D padded bounds, exact depth planes, same-command GPU ordering, warm
  residency, dispatch limits, and readback behavior are unchanged.
- Runtime and editor builds completed with zero warnings and zero errors. The
  live compiler settled successfully and regenerated the visibility shader.
- The current MCP-started play session remained at `35,937` authoritative
  chunks, `0` resident meshes, and `1,089` queued meshes because simulation
  updates were still not advancing after the prior clean restart. It therefore
  cannot answer whether the smaller guard eliminates rapid-turn popping or
  preserves the required culling percentage.
- Outcome: incomplete. The 1.10 tuning compiles, but an advancing production
  play session, rapid camera sweep, and unchanged figure-eight comparison remain
  required before acceptance.
### VOXEL-STREAM-SLIDING-001/v1 - Adjacent sliding-window streaming

- Definition recorded on 2026-08-29 before instrumentation, baseline execution,
  or implementation.
- Production world: `Assets/scenes/basic_example.scene`; one local player;
  engine build `26.08.19`; main camera `1280x720`; existing user-authored plane
  renderer/collider unchanged.
- Fixed world parameters: `CellsPerAxis=32`; `CellSize=16`; `LoadRadius=16`;
  `TerrainSurfaceHeight=0`; one render-warm chunk; at most `8` total GPU mesh
  dispatches per update; `0.500 ms` main-thread integration budget.
- Fixed journey: begin fully settled at `C[0,0,0]`, then move the production
  streaming target through `C[1,0,0]`, `C[2,1,0]`, `C[2,1,1]`, `C[1,0,0]`,
  and `C[0,0,0]`. Gameplay generation, warm generation, and gameplay/warm mesh
  backlogs must settle to zero before each next move.
- Measurement boundary: synchronous timing starts before desired-set mutation
  and ends after unload/demotion, pending-work prioritization, generation-worker
  startup, draw-command commit, status refresh, and structured record creation.
  Record total, desired-update, prioritization, and draw-command milliseconds;
  gameplay/render entering, leaving, and unique touched coordinates; warm
  classification/construction totals; frame p95/p99; average GPU time; peak and
  settled mesh backlog; scalar and geometry readbacks.
- Fixed pass criteria:
  - Every settled desired set contains exactly `33^3 = 35,937` gameplay and
    `35^3 = 42,875` render coordinates.
  - One-, two-, and three-axis adjacent moves enter and leave respectively
    `1,089`, `2,145`, and `3,169` gameplay coordinates and `1,225`, `2,415`,
    and `3,571` render coordinates.
  - The candidate uses incremental mode for every listed move; its median and
    p95 synchronous time improve over the instrumented full-rebuild baseline,
    and desired/prioritization work scales with the recorded slabs rather than
    either complete cube.
  - Loaded membership, surface/solid/air probes, visible terrain, warm-shell
    anti-pop behavior, gameplay priority, and the dispatch limit remain
    unchanged. Gameplay and warm mesh backlogs settle to zero.
  - Frame p95 is at most `16.67 ms`; p99 is at most `25 ms`; average GPU time is
    no more than `0.05 ms` above the comparable baseline. Normal production
    performs zero scalar and geometry readbacks.

### VOXEL-STREAM-TELEPORT-001/v1 - Progressive large-teleport streaming

- Definition recorded on 2026-08-29 before instrumentation, baseline execution,
  or implementation.
- Production world, player, engine, camera, world configuration, integration
  budget, warm shell, and GPU dispatch limit are identical to
  `VOXEL-STREAM-SLIDING-001/v1`.
- Fixed journey: begin fully settled at `C[0,0,0]`; move the production
  streaming target directly to `C[64,64,0]`; settle; then begin a second
  non-overlapping teleport and move again immediately after its first generated
  gameplay batch is published to exercise stale-revision cancellation.
- Fixed measurements: full synchronous-path subdivisions; full gameplay/render
  coordinate counts; generation batch count and maximum size; first gameplay
  batch publication/integration latency; warm range classifications and
  transient constructions; stale publications/integrations; frame p95/p99;
  average GPU time; mesh backlog; scalar and geometry readbacks.
- Fixed pass criteria:
  - Teleports use full-rebuild mode and settle at `35,937` authoritative chunks.
  - The current flat field classifies all `6,938` warm-shell coordinates before
    construction, rejects `6,802` as provably solid or air, and constructs only
    the `136` potentially surface-containing coordinates.
  - Gameplay generation publishes deterministic nearest-first batches of at
    most `256`; the center batch becomes available before the complete
    `35,937`-chunk generation finishes.
  - A newer movement revision immediately invalidates remaining obsolete work;
    stale chunks never enter loaded state or GPU scheduling.
  - Terrain correctness, warm-shell quality, frame limits, GPU regression
    budget, settled backlogs, dispatch limit, and readback invariants are the
    same as `VOXEL-STREAM-SLIDING-001/v1`.
  - Cave/edit readiness is structural: any field whose complete chunk range is
    not proven returns `PotentiallySurfaceContaining`; this flat-world scenario
    makes no unavailable visual cave claim.

#### Instrumented full-rebuild baseline 2026-08-29

- Source state `9a51a2f+instrumented-full-baseline`; engine build `26.08.19`;
  AMD Ryzen 7 9800X3D; NVIDIA GeForce RTX 5090; production play session and
  authored scene unchanged. Instrumentation was added without changing the
  full-rebuild, monolithic-generation, warm-construction, integration, or GPU
  scheduling behavior.
- Settled adjacent transitions remained full rebuilds and visited all `35,937`
  gameplay plus `42,875` render coordinates each time:
  - `C[0,0,0]` to `C[1,0,0]`: total `18.9701 ms`; desired `0.8649 ms`;
    prioritization `7.2663 ms`; draw commit `1.2393 ms`; queued gameplay/warm
    `1,089/1,225`; all `1,225` warm candidates constructed.
  - `C[1,0,0]` to `C[2,1,0]`: total `9.3234 ms`; desired `0.9792 ms`;
    prioritization `3.6632 ms`; draw commit `0.6338 ms`; queued gameplay/warm
    `2,145/2,415`; all `2,415` warm candidates constructed.
  - `C[2,1,0]` to `C[2,1,1]`: total `7.3107 ms`; desired `0.9015 ms`;
    prioritization `2.9904 ms`; draw commit `0.0003 ms`; queued gameplay/warm
    `1,089/1,225`; all `1,225` warm candidates constructed.
  - `C[2,1,1]` to `C[1,0,0]`: total `10.6814 ms`; desired `0.9926 ms`;
    prioritization `5.0411 ms`; draw commit `0.6975 ms`; queued gameplay/warm
    `3,169/3,571`; all `3,571` warm candidates constructed.
- A deliberately immediate final move reproduced cancellation and is not used
  in settled adjacent timing: its new full rebuild queued `4,193` gameplay and
  `3,638` warm coordinates because prior work was still pending.
- The no-overlap teleport from `C[0,0,0]` to `C[64,64,0]` used full rebuild,
  visited `35,937/42,875` gameplay/render coordinates, queued `35,937` gameplay
  and `6,938` warm coordinates, and took `28.9618 ms` synchronously (`0.8643 ms`
  desired; `18.8722 ms` prioritization; `0.0366 ms` draw commit). The monolithic
  worker settled in `111.310 ms`; all `6,938` warm coordinates constructed
  transient chunks and none were rejected before construction.
- Canonical figure-eight run `dc1e4ac71256410f94e47c3d02875f82` completed the
  unchanged origin-centered one-loop workload in `121.93662 s`: `27,535` frame
  samples, zero truncation, average `225.79832 FPS`, p95 `5.5041 ms`, p99
  `12.3281 ms`, average GPU `0.7476039 ms`. It settled at `35,937` loaded chunks,
  `1,089` gameplay plus `136` warm resident meshes, zero gameplay/warm backlog,
  zero post-warmup pool allocations, zero normal scalar readbacks, zero geometry
  readbacks, and exactly one post-measurement visibility scalar readback.
- Baseline outcome: pass as comparison evidence. It establishes the avoidable
  full-volume synchronous work and transient construction cost; it is not the
  optimized acceptance result.

#### Incremental candidate 2026-08-29 - pass

- Source state `9a51a2f+incremental-candidate`; engine build `26.08.19`; baseline
  hardware, scene, player, camera, world settings, dispatch cap, integration
  budget, and movement coordinates unchanged. The baseline `desired` sub-timer
  ended after gameplay-cube construction and therefore excluded render-cube
  construction; baseline total synchronous and prioritization measurements are
  valid comparisons, while that isolated sub-timer is retained but not compared.
- Every locked adjacent transition selected incremental mode and retained exact
  desired cardinalities. Unique set differences and synchronous timings were:
  - `C[0,0,0]` to `C[1,0,0]`: gameplay `1,089` enter/leave; render `1,225`
    enter/leave; `2,178/2,450` total gameplay/render touched; total `8.1938 ms`;
    prioritization `1.2440 ms`; draw commit `0.8962 ms`.
  - `C[1,0,0]` to `C[2,1,0]`: gameplay `2,145` enter/leave; render `2,415`
    enter/leave; `4,290/4,830` touched; total `4.9028 ms`; prioritization
    `1.9594 ms`; draw commit `0.7321 ms`.
  - `C[2,1,0]` to `C[2,1,1]`: gameplay `1,089` enter/leave; render `1,225`
    enter/leave; `2,178/2,450` touched; total `2.3336 ms`; prioritization
    `0.8667 ms`; no draw rebuild was required.
  - `C[2,1,1]` to `C[1,0,0]`: gameplay `3,169` enter/leave; render `3,571`
    enter/leave; `6,338/7,142` touched; total `6.4085 ms`; prioritization
    `3.0999 ms`; draw commit `0.5725 ms`.
  - `C[1,0,0]` to `C[0,0,0]`: gameplay `1,089` enter/leave; render `1,225`
    enter/leave; `2,178/2,450` touched; total `2.8687 ms`; prioritization
    `0.8507 ms`; draw commit `0.6149 ms`.
- The candidate range was `2.3336-8.1938 ms` versus the settled baseline's
  `7.3107-18.9701 ms`. A one-axis warm slab classified `1,225` coordinates,
  rejected `595` solid plus `595` air, and constructed only `35` potential
  surface chunks rather than all `1,225`.
- Canonical figure-eight run `8d8b3115bccf46b6a9af9401d59afc44`
  completed in `121.931114 s` with schema `4`: `28,360` samples, zero
  truncation, average `232.57283 FPS`, p95 `5.1986 ms`, p99 `6.8883 ms`, and
  average GPU `0.7129534 ms`. Versus baseline, p95 improved `0.3055 ms`, p99
  improved `5.4398 ms`, and average GPU improved `0.0346505 ms`.
- Schema-v4 streaming aggregate: `742` incremental and `3` guarded full updates;
  `2,167.673 ms` total and `11.7184 ms` maximum synchronous work; `1,774,575`
  gameplay plus `2,003,645` render coordinates touched; `3,866` generation
  batches, maximum batch `256`, worst first-batch publication `11.8871 ms`;
  `926,973` warm coordinates classified, `900,720` proven solid/air rejections,
  and `26,253` potential/transient constructions. Peak gameplay/warm mesh
  backlogs were `226/79` and both settled to zero.
- Settled rendering remained `1,089` gameplay plus `136` warm resident meshes,
  zero mesh backlog, zero post-warmup pool allocations, zero normal scalar
  readbacks, zero geometry readbacks, and exactly one post-measurement visibility
  scalar readback. Four surface probes retained `1,024` active cells, `2,048`
  triangles, zero invalid gradients, and zero overflow; adjacent solid/air probes
  retained no GPU resource. A production-camera `1280x720` frame showed
  continuous terrain without a visible seam or hole.
- Outcome: pass for the fixed adjacent journey and canonical figure-eight
  comparison. The existing live registry still exposes no fixed rapid-camera
  input/capture route required to complete `VISIBILITY-EDGE-STRESS-001/v1`, so
  this run does not replace that scenario's previously recorded incomplete
  rapid-turn evidence.

#### Progressive teleport candidate 2026-08-29 - pass

- Source state, engine, hardware, scene, world, and budgets match the incremental
  candidate above. The no-overlap move to `C[64,64,0]` selected full mode,
  rebuilt `35,937/42,875` gameplay/render coordinates, and settled with all
  `35,937` authoritative chunks present.
- Gameplay generation published `141` deterministic batches: maximum `256`,
  final batch `97`. First publication occurred at `7.152 ms`; first gameplay
  integration occurred at `7.839 ms` while the worker was still incomplete.
  Total settle was `361.147 ms`; the longer complete settle is accepted because
  nearest terrain became available incrementally instead of waiting for the
  former monolithic batch.
- All `6,938` warm-shell coordinates were classified before construction:
  `3,401` definitely solid, `3,401` definitely air, and `136` potentially
  surface-containing. Exactly the `136` potential coordinates constructed
  transient chunks and reached GPU scheduling.
- A second no-overlap teleport followed immediately by another move produced
  two distinct full revisions; the obsolete worker emitted
  `stream.stale ... discarded=256`, and no stale coordinate integrated or
  scheduled a mesh under the newer revision.
- Outcome: pass. Full-rebuild fallback, progressive nearest-first availability,
  bounded batches, conservative warm rejection, latest-revision authority, and
  final desired-set correctness were observed through the production path.

### Testing-surface cleanup 2026-08-29

- Scope: remove executable artifacts belonging to superseded diagnostic test
  paths while preserving the existing figure-eight implementation, inspector
  action, MCP trigger, parameters, schema, and result persistence unchanged.
- Removed paths: the mutating `voxel_stream_origin` command, the redundant
  `voxel_verbose_logging` console wrapper, the `inspect_gpu_mesh` editor tool,
  its manager/mesher inspection queues and readbacks, and the diagnostic-only
  mesh compute shader.
- Retained evidence surfaces: the figure-eight performance suite; passive
  production frame, memory, chunk, streaming, meshing, visibility, dispatch,
  pool, backlog, and readback counters; inspector status; normal structured
  logs; production screenshots; and the read-only `voxel_chunk_info` query.
- Historical results and scenario definitions were not rewritten. References to
  removed paths describe immutable past runs and are not current instructions.

#### PERFORMANCE-OVERVIEW-001/v3 cleanup run 2026-08-29 01:44 EDT — pass

- Executor: Codex through the unchanged `run_performance_test` figure-eight.
- Project/source state: cleanup candidate based on `22bced7`, revision label
  `testing-cleanup-candidate`.
- Engine build: `26.08.19`.
- Hardware/environment: AMD Ryzen 7 9800X3D, NVIDIA GeForce RTX 5090,
  `basic_example`, one local player.
- Confirmation that scenario parameters were unchanged: yes; speed `2500`,
  distance `50000`, loop count `1`, world height `0`, and all streaming/mesh
  settings matched `PERFORMANCE-OVERVIEW-001/v3`.
- Raw measurements: schema `4`, run
  `fcbbebb2ff9640afb2c3810997efc16d`, one completed loop in `121.93755 s`,
  `28,748` samples, zero truncation, average `235.74437 FPS`, p95 `5.2687 ms`,
  p99 `6.9917 ms`, average GPU `0.7027053 ms`; `35,937` loaded chunks;
  `1,089` gameplay and `136` warm resident meshes; zero settled gameplay/warm
  backlog; zero normal scalar readbacks; zero geometry readbacks; one
  post-measurement visibility scalar readback.
- Streaming measurements: `752` incremental updates, zero full updates,
  maximum synchronous update `15.4422 ms`, maximum generation batch `256`, and
  peak gameplay/warm mesh backlogs `1,089/136`, both settled to zero.
- Derived measurements: p95 and p99 passed their `16.67/25 ms` limits; the
  result returned to streaming center `C[0,0,0]` and target `(0,0,0.04125)`;
  the production camera showed continuous flat terrain after settlement.
- Outcome: pass.
- Evidence location: `performance/results-v1.jsonl` in `FileSystem.Data`, run ID
  above, matching begin/save logs, and the live production-camera observation.
- Remaining unmeasured risks: this cleanup deliberately removes the former
  diagnostic-only per-cell GPU probe; future mesh correctness evidence must come
  from production rendering/residency and player-visible behavior.
