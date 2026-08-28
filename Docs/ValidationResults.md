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
