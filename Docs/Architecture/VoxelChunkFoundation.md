# Voxel Chunk Foundation

## Scope

This document owns terrain state, spatial conventions, procedural generation,
CPU render preparation, and manager diagnostics. The
[GPU meshing document](GpuVoxelMeshing.md) owns visual placement, transitions,
GPU scheduling, allocation, publication, and drawing. Exact validation workloads
and acceptance decisions belong to the [ledger](../ValidationResults.md).

A [terrain collision prototype](TerrainCollision.md) is implemented but not yet
fully accepted. [Terrain deformation](TerrainDeformation.md) now owns the
in-progress live edit contract: one shared correction field, local tool and
GPU/collision invalidation, multiple player collision interests, and versioned
disk commands are implemented. Networking transport, persistence validation and
full feature acceptance remain incomplete. The linked deformation document and
validation ledger distinguish current measured behavior from remaining work;
[research](../Research/TerrainDeformationSecondSlice.md) remains a proposal source.

## Canonical Ownership and Data Flow

- [`VoxelManager`](../../Code/Voxels/VoxelManager.cs) owns one analytic gameplay
  interest cube, bounded LOD0 render-preparation sets and queues, configuration,
  spatial conversions, diagnostics, and one GPU mesher. There is no populated
  dictionary of authoritative gameplay chunk objects.
- [`VoxelChunk`](../../Code/Voxels/VoxelChunk.cs) is an immutable coordinate,
  dimension, and terrain-settings view. It evaluates logical samples on demand,
  owns no density/material arrays, and has no engine resources or network identity.
- [`ProceduralTerrainSdf`](../../Code/Voxels/ProceduralTerrainSdf.cs) owns the
  unedited field and conservative bounds. GPU extraction evaluates the matching
  field from an immutable descriptor; geometry remains derived data.
- `GameplayRadius` defines an inclusive, viewer-centered cube in all three axes.
  Collision uses the union of independently centered player cubes as described
  in the deformation contract; visual streaming still has one target.
  Its membership and overlap are computed analytically. A chunk view is created
  on demand for a query or a render-preparation consumer. Mutable per-coordinate
  storage requires a feature that actually owns such state.
- LOD0 preparation is bounded by visual coverage and its warm shell, including
  committed/staged placement dependencies. Gameplay radius cannot expand this
  render set. Visual tier selection and placement are defined only in
  [level-indexed placement](GpuVoxelMeshing.md#canonical-level-indexed-placement).
- An explicit `StreamingTarget` takes precedence. Otherwise the manager resolves
  one non-proxy `PlayerController`; absent or ambiguous player resolution
  leaves the manager GameObject as the streaming origin. The performance runner
  separately requires a valid local player.

The flow is field/settings -> analytic gameplay membership and bounded visual
requests -> immutable SDF descriptors -> derived GPU geometry. A future mutation
system must update the authoritative field and invalidate affected derivatives;
meshes and render caches must not become a second world-state model.

## Spatial Contract

The manager validates the production layout as 32 cells per axis at 16 world
units per cell. Each chunk spans 512 units and has 33 logical samples per axis,
including shared boundary samples. These values are owned by the manager's
configuration validation; changing inspector values does not establish a new
supported layout.

Visual configuration also has a 32,768-coordinate preparation budget, owned by
the manager. It counts the LOD0 warm cube when enabled plus all enabled coarse
cache cubes. The supported seven-level 4/8 layout needs 25,907 coordinates, so
all default visual-radius tiers through 512 and the reduced 2/6 layout remain
available. Oversized operands are rejected before checked volume calculation
and enumeration; invalid requests retain the applied placement. This bounds CPU
coordinate work, not worst-case GPU geometry memory. Committed/staged sets are
bounded multiples of the same request budget.

`WorldToChunkCoordinate` uses floor division, including for negative positions.
A chunk's global sample origin is its integer coordinate multiplied by cells per
axis. Every consumer must query shared positions identically.

Negative density is solid, positive density is air, and zero is the surface.
`VoxelChunk` derives `Grass = 1` for density at or below zero and `Air = 0`
otherwise. Material IDs have no separate mutable payload or registry.

Gameplay interest has no fixed world-Z floor or ceiling. Its supported radius
and defaults are owned by `VoxelManager`; scene-authored settings belong to the
scene, and fixed test settings belong to the ledger. Logical loaded counts do
not mean that the same number of objects or meshes have been allocated.

## Procedural Generator Version 5

`ProceduralTerrainSdf` owns the generator version, constants, gradients, hashes,
seed salts, and default settings. The exposed settings are `WorldSeed`,
`SurfaceBaseHeight`, `SurfaceFrequency`, and `SurfaceAmplitude`. There is one
backend recipe, with no selectable generator variants.

The exterior is world Z minus a seeded 2D simplex surface height. Two absolute
3D simplex fields create noodle passages; a slower field varies their width and
also changes the threshold of a cheese-cavern field. A surface-relative depth
interval preserves overburden and bounds cave depth beneath the local surface.
The final field combines the surface and depth-limited cave terms. Exact
formulae and constants live in the source owner and its
[GPU field mirror](../../Assets/shaders/voxels/voxel_sdf_v5.hlsl).

CPU and GPU use the same integer hashes, gradient tables, seed salts, and
field recipe. The CPU skips cave noise where the existing depth envelope proves
it cannot affect the final density; evaluated field operations remain equivalent. Negative coordinates use floor operations. Simplex outputs
are clamped, and conservative classification accounts for floating-point
uncertainty; determinism does not imply unmeasured bitwise CPU/GPU equivalence.

The full classifier bounds the complete closed AABB. It propagates conservative
surface and 3D noise intervals through field composition; uncertain or
non-finite cases remain potentially surface-containing. The cheaper coarse
broad phase proves only regions outside the generator's global vertical support.
Both use the canonical generator's bounds. Neither may assume that underground
terrain is a heightfield or silently omit a possible cave.

Changing field settings increments content/preparation revisions, cancels
incompatible work, clears derived meshes, and rebuilds through the same manager.
Each chunk and GPU descriptor retains the settings that produced it, making
stale-result checks sensitive to every field-shaping input.

## CPU Preparation and Lifecycle

`OnLoad` creates the mesher, applies configuration, starts bounded render
preparation, and returns a completed task. It does not wait for all gameplay
coordinates to materialize or for GPU terrain to settle. `OnUpdate` drives
preparation integration, placement readiness, mesher processing, and diagnostics.

Adjacent target moves update the bounded render window by entering/leaving
slabs. Initialization, configuration changes, larger jumps, or an invalid
incremental precondition rebuild that same set. The manager retains coordinates
needed by committed/staged placement and sorts missing preparation nearest-first
with explicit coordinate tie breaks.

Movement retains completed, unintegrated preparation whose coordinate is still
required. The existing missing-coordinate set selects those results; completed
and pending queues retain matching FIFO order, followed by nearest-first missing
coordinates for the new worker. No second preparation cache is introduced.
Content reset clears both queues. `StartWarmGeneration` owns cancellation and
revision advancement for movement and preserves retained results even when no
new classification is required.

One serialized background preparation chain receives immutable coordinates,
dimensions, terrain settings, revision, and cancellation token. It classifies
LOD0 regions and creates transient chunk views only for potential surfaces.
It constructs managed data off-thread, then explicitly returns to the main
thread to enqueue results. A new request cancels the previous revision and
waits for the preceding task before continuing, preventing overlapping workers.

The manager integrates bounded result batches on its update thread under the
source-owned time budget. It rechecks desired/placement membership, marks
prepared coordinates, and schedules potential surfaces with gameplay or warm
priority. Proven empty regions need no geometry. Coarse preparation, GPU
publication, and whole-placement readiness belong to the meshing document.

Configuration reset and destruction cancel outstanding preparation. Revision
checks reject old completions. No worker mutates scene state, owns GPU resources,
or creates a separate terrain implementation.

## Design Rationale

- The current implicit field needs neither dense arrays nor separate solid/air
  storage modes. Both would add storage for values already derivable on demand.
- Cubic gameplay interest follows the region topology in all three axes. Fixed
  world-Z bounds were removed because they imposed an unrelated floor/ceiling.
- The current cell scale balances detail against region, boundary, and eventual
  edit costs. A different scale requires a measured design and scenario change.
- Surface-relative caves preserve overburden under hills and valleys. Extra
  octaves, warping, worm carvers, and a general noise graph were unnecessary for
  this recipe. Earlier cave experiments remain in the ledger.
- Recursive per-cell range proof was rejected after background batches took
  seconds without yielding. Uncertainty must remain conservative.
- One task per chunk and synchronous bulk generation create unbounded scheduling
  or frame work. More concurrency needs evidence that CPU preparation is the
  bottleneck; it cannot solve a GPU publication dependency by itself.
- [Visual scaling rationale](../Research/VisualClipboxScaling.md) explains the
  separate gameplay and rendering ranges and the removed eager-allocation path.

## Debug Contract

`World Status` presents frame performance, chunk status, streaming performance,
and process memory. Readable status refreshes at the source-owned cadence;
logical chunk counts are not GPU-resident mesh counts. Legacy stream-generation
bookkeeping is removed from schema 22. Status distinguishes logical membership,
prepared regions, pending preparation, and GPU residents. Range-application time
is explicitly named and is not a mesh-availability or streaming-settle metric.
Use the performance result's meshing, queue, and placement measurements for that
question.

Schema 23 additionally records maximum moving-window placement anchor lag using
the inspection command's shared calculation. This is a distance in each level's
own regions, distinct from publication route lag in LOD0-sized chunks. It resets
at test start and survives drain/stationary capture without per-frame logging.

`voxel_chunk_info x y z` checks analytic gameplay membership and constructs a
bounded query view to report generator identity, density bounds, sample/material,
and boundary samples. `voxel_lod_info` observes manager-owned level/pair state.
It includes cached per-level and combined geometry fingerprints and transition
mismatch counters, so configuration rejection and restoration can be inspected
without reading geometry back from the GPU.
Neither creates independent world state or changes the streaming origin.

Verbose logging is opt-in; per-chunk load/unload spam and runtime loaded-chunk
bounds/labels are absent. Warnings, errors, explicit read-only diagnostics, and
performance begin/save records remain sparse unconditional evidence. Process
working set and engine GPU memory are labeled by scope, not attributed to chunks.

## Performance Overview

The inspector's `Run Performance Test` button and editor MCP
`run_performance_test` call `VoxelManager.StartPerformanceTest`. They share the
manager's player-movement and measurement lifecycle. The former standalone
`player_figure_eight` tool and toggle-button instructions are obsolete.

The runner moves a valid local player along a lemniscate centered on its starting
X/Y, at fixed world Z zero. Speed uses the local tangent, distance defines X
reach, and Y reach is half that distance. It counts complete loops itself;
external polling or elapsed sleeps do not choose the measured boundary.

After movement ends, it records the moving window, waits for preparation,
regular/transition work, and placement to settle, captures settled visibility,
waits two further render-sequence advances, and measures a stationary window.
The final result is saved only after stationary visibility completes. Merely
finishing the moving loop does not mean a run was saved or accepted.

The per-frame sampler records bounded scalar frame, GPU, allocation, GC,
exception, queue, and work counters; memory uses a slower cadence. Percentiles
use frame duration so high tails describe stalls. Copying/sorting profiler
history occurs at completed windows. Capacity exhaustion is reported rather
than silently allocating. Timing and capacity constants are owned by the manager;
[PerformanceTestResult.cs](../../Code/Voxels/PerformanceTestResult.cs) owns the
serialized field layout. Do not maintain a second schema changelog here.

Real profiler scopes cover range rebuilding, placement preparation, result
integration, mesher processing, and draw-command commits. Scalar work reporting
counts integrated and retained preparation results, discarded completed results,
coarse-cache coordinates scanned, skipped complete levels, and placement time.
Retention counts are reuse events, not a second count of newly classified
coordinates. Each saved run emits one sparse `performance.work` summary;
per-coordinate diagnostic logging remains opt-in.

One completed result appends to `performance/results-v1.jsonl` in
`FileSystem.Data`. It includes a run ID, capture time, caller-supplied task and
revision, effective configuration, and separate moving/stationary measurements.
The inspector supplies default context when blank; callers must use labels that
identify the actual measured source. The runtime does not inspect Git or launch
processes. File I/O occurs after measurement and one local manager writes at a
time. JSON Lines keeps appends independent of historical file size.

The [validation ledger](../ValidationResults.md) alone owns fixed scenario
parameters, baseline selection, thresholds, and acceptance decisions. The
[project instructions](../../AGENTS.md#figure-eight-performance-acceptance) own
when this primary test must run. Runtime result files are raw evidence; the
ledger records the reproducible comparison and decision.

## Retained-sample percentile contract

S3 (2026-09-08, explicitly accepted by the user): PerformanceSampleTails shares only
nearest-rank p50/p95/p99 and retained maximum for sorted spans; empty tails are
zero. It owns no storage. GPU, collision and manager keep their collection,
copy/sort, invalid-value, overflow, mean and reset policies. GPU averages all
valid observed values including overflow; collision averages retained values.
CPU frame maximum remains null when truncated. Serialized result fields and
moving/stationary/trailing-profiler window meanings do not change. See
SIMPLIFICATION-S3-001/v1 in the ledger for performance failures and human gate.
