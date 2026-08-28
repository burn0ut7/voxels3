# Voxel Chunk Foundation

## Scope

This decision covers the first production chunk slice: integer chunk identity,
implicit SDF data, deterministic flat-terrain population, bounded streaming,
and runtime/editor diagnostics. Surface extraction, collision, live edits,
persistence, and network replication are later slices and are not implemented
here.

## Canonical Ownership and Data Flow

- `VoxelManager` owns the loaded-chunk dictionary, desired streaming set, load
  queue, configuration, diagnostics, and spatial conversions.
- Each `VoxelChunk` owns the parameters required to evaluate its authoritative
  `(CellsPerAxis + 1)^3` logical density samples. Negative density is solid,
  positive density is air, and zero is the surface.
- The current unedited base field is the world-space plane
  `density = worldZ - TerrainSurfaceHeight`. Sampling is based only on global
  coordinates, so shared chunk-boundary samples are identical.
- The same canonical sample query derives one semantic material ID from density:
  `Air = 0` when density is positive and `Grass = 1` when density is zero or
  negative. The zero-density surface therefore belongs to Grass on both chunks
  that share it. Material IDs are implicit and allocate no sample payload.
- Chunk objects are ordinary managed data, not networked GameObjects. Multiplayer
  peers can reconstruct the same base field; a later edit slice must route
  authoritative mutations through this single chunk state rather than creating a
  second copy.
- Streaming target movement computes one desired coordinate set. Obsolete chunks
  are removed and missing chunks are ordered nearest-first with deterministic tie
  breaks. One component-scoped worker pipeline generates the complete missing set
  off-thread; the main thread integrates ready results under a time budget.
- An explicitly assigned `StreamingTarget` is authoritative. Without one, the
  manager resolves exactly one active non-proxy `PlayerController` for the local
  client. It refuses to choose among multiple locally controlled players and
  logs that multi-origin server interest management is still required.

## Selected Dimensions

- Cells per axis: `32`
- SDF samples per axis: `33`
- Cell size: `16` s&box units (s&box uses Source-style inches)
- Chunk world extent: `512` units per axis
- Density samples per chunk: `35,937`
- Default load radius: `4` chunks in X/Y/Z around the player's current chunk
  (`9x9x9 = 729` chunks)
- Background generation concurrency: one serialized worker pipeline
- Main-thread integration budget: `0.500 ms` per update, independent of chunk
  count

At the default radius the settled world contains `26,198,073` logical density
samples. The current plane evaluates them directly and has no density arrays.
Runtime diagnostics do not estimate or report chunk-attributed memory; allocator
and managed-runtime layout are outside the chunk data contract. The concise
inspector reports s&box's approximate working-set measurement for the whole
process and labels it accordingly.

There is no authored minimum or maximum chunk Z. The one load radius defines a
viewer-centered cubic interest volume equally in all three axes. At radius `4`,
a player in `C[0,0,0]` loads coordinates `-4` through `4` in X, Y, and Z. Moving
to `C[0,0,1]` shifts the loaded Z range to `-3` through `5`. “Everything” means
every chunk inside this explicit 3D view extent; chunks beyond it are outside the
configured view distance rather than silently clipped by fixed world bounds.

The dimensions remain inspector-configurable because scale is a product setting,
but the validation scenario fixes them exactly. Runtime configuration changes
clear and rebuild the one canonical loaded set; they do not create parallel
layouts.

## Alternatives Considered

- `16^3` cells produces small, responsive chunks but quadruples chunk count per
  horizontal area relative to `32^3`, increasing dictionary, boundary, streaming,
  scheduling, and eventual draw/dispatch overhead.
- `64^3` cells reduces chunk count but requires `65^3 = 274,625` density samples
  and makes one edit invalidate a much larger mesh/collision job.
- An `8`-unit cell offers more detail but multiplies sample and eventual mesh work
  for the same world extent. A `32`-unit cell gives only about 2.25 cells across a
  72-unit-tall player. `16` units starts with about 4.5 cells across that scale.
- The selected cubic interest volume matches the cubic chunk topology, applies
  the same understandable distance in every axis, and preserves corner coverage
  as the camera rotates. A Euclidean sphere lowers residency but omits corner
  chunks that are inside the configured per-axis view extent.
- A generator-derived surface band could eventually reduce empty-space residency,
  but making it the canonical interest rule now would incorrectly assume
  heightfield terrain. General SDF terrain can contain caves, overhangs, floating
  terrain, and edits outside a predicted surface band.
- Fixed absolute Z bounds were removed because they impose an unrelated world
  floor and ceiling, fail to follow vertical player movement, and duplicate the
  meaning already owned by view distance.
- Full per-chunk arrays duplicate an exactly derivable value for every current
  sample. They were removed rather than pooled or compressed.
- A single constant for sign-uniform solid or air chunks is smaller than an
  array but changes exact density queries because density still varies with local
  Z. The selected representation evaluates the canonical expression
  `chunkMinimumZ + localZ * CellSize - TerrainSurfaceHeight`, preserving the same
  float operation order and values for uniform and surface chunks alike.
- Keeping arrays only for surface chunks introduces two storage modes despite
  the current plane needing neither. General compression, profiles, and mutable
  payload promotion remain out of scope until an implemented feature requires
  stored non-planar values.
- A material-ID array would duplicate values that are exactly derivable from the
  canonical density result. Returning Grass or Air directly is deterministic and
  requires no second storage path.
- A material registry, biome mapping, layered soil, and render-material resource
  references are not required for the first two IDs and are intentionally absent.
- Chunk-memory reporting was removed because object sizes and managed collection
  internals are not stable project-owned measurements, while the implicit field
  has no density payload to measure.

`32^3` at `16` units is the best initial balance, not a permanent claim. A future
dimension change requires representative fixed-parameter in-world measurements
and a versioned validation scenario.

## Threading, Invalidation, and Scale

`VoxelManager` is the only owner of loaded, desired, pending, and completed
collections. The worker receives an immutable snapshot of missing coordinates
plus cells/axis, cell size, surface height, and a stream revision. It constructs
only ordinary `VoxelChunk` managed data; it does not read scene state, mutate the
manager, log, or call engine resource APIs. Completion explicitly returns to the
main thread before any authoritative collection changes.

Initial population runs through this same pipeline from async `OnLoad`. s&box
keeps the loading screen active until the component finishes, so the first 729
chunks exist before `OnStart` admits active play. Player boundary crossings use
the identical generator and integration queue from `OnUpdate`; startup does not
maintain a second terrain or scheduling implementation.

Only one worker pipeline may execute for a manager. A newer desired set cancels
the prior revision, waits for that worker to yield, and then starts, preventing
task stacking and CPU oversubscription. Results whose revision or desired
membership changed are counted and discarded. Configuration changes invalidate
all loaded and queued chunks because their coordinate-to-sample meaning changed;
target movement retains chunks still in the desired set.

There is no chunks-per-frame setting. Every missing chunk is eligible for the
single worker immediately, while ready results are integrated until the current
update has spent `0.500 ms`. This time boundary protects frame integration work
without making throughput depend on an arbitrary chunk count. Stream telemetry
records background time, summed chunk construction time, total and slowest
integration time, stale results, and maximum observed frame duration.

Serious alternatives considered:

- Generating every chunk synchronously in one update minimizes settle latency
  but can consume the whole frame and create large allocation/GC stalls.
- Launching one task per chunk appears maximally parallel but creates unbounded
  scheduler pressure, memory-bandwidth contention, and stale task storms when
  the player moves.
- A configurable chunks-per-frame count bounds work indirectly but does not
  guarantee a frame-time budget and unnecessarily stretches cheap workloads
  across many frames.

The serialized worker plus time-budgeted integration is the canonical path. More
worker concurrency is not added speculatively; representative procedural terrain
and meshing measurements must show that the single worker is the bottleneck
before that policy changes.

## Debug Contract

- The human-facing `World Status` inspector category contains `Frame
  Performance`, `Chunk Status`, `Streaming Performance`, and `Process Memory
  Usage`. Configuration, performance-log context, and opt-in visualization
  controls remain separate editable categories.
- Frame performance reports the last complete 10-second frame window. Chunk
  status combines loaded and queued counts with the last window's integration
  rate. Streaming performance retains effective chunks per second and the last
  settle time. Process memory reports windowed process RAM and engine-tracked GPU
  memory; neither is memory attributed to chunks or one manager.
- Logs use the stable machine-searchable field `chunk=C[x,y,z]` plus the
  readable chunk name.
- Chunk lifecycle detail is opt-in; stream completion and invalid configuration
  remain concise summary/warning events.
- The `voxel_stream_origin x y z` console command moves the real production
  streaming target to an exact world position for deterministic troubleshooting.
  It refuses to choose silently when more than one active manager exists.
- `voxel_player_chunk` reports the target's current world position and chunk,
  then retrieves that chunk's production data. `voxel_chunk_info x y z`
  retrieves any currently loaded chunk by its log identifier coordinates and
  reports its minimum- and maximum-Z sample density and material, or a clear
  missing result otherwise.
- Stream completion reports effective loaded chunks per second, pure SDF
  generation chunks per second, worker time, time-budgeted integration work,
  slowest integration update, maximum observed active-play frame, loaded,
  retained, unloaded, generated, pending, and discarded stale chunk counts.
  These detailed fields remain in structured logs rather than becoming separate
  inspector rows.
- Runtime overlays can draw all loaded chunk bounds and labels; the chunk
  containing the actual player is highlighted. The inspector button and console
  commands query the real loaded production data; there is no separate debug
  copy or test implementation.

## Player Figure-Eight Smoke Movement

`VoxelManager` owns the optional figure-eight automation state and the one update
path that moves its local production streaming target. The
`player_figure_eight` editor MCP tool and the manager inspector's `Toggle Player
Figure Eight` button both call the same manager configuration method. The button
uses the manager's `Figure Eight Speed` and `Figure Eight Distance` properties;
pressing it again stops. No scene component, console command, editor-frame
driver, or second movement implementation exists.

While enabled, `VoxelManager.OnUpdate` moves the player around its start X/Y
using a lemniscate whose configured distance is the maximum X offset, whose Y
offset is half that value, and whose world Z is fixed at `0`. Speed is converted
to curve progress from the local tangent so it remains a world-units-per-second
input rather than a raw angular rate. Streaming observes the resulting real
player position through its existing target path.

The mutable enable flag, target, center, speed, distance, and curve parameter
belong only to the active manager. There is no worker access, replication
protocol, terrain query, collision trace, or report in this slice. Configuration
rejects missing or proxy player targets and non-finite or non-positive inputs.
The editor tool additionally rejects zero or multiple active managers. Movement
operates only on the locally controlled player; multiplayer automation requires
a later authority design.

Physics steering was rejected because collision and acceleration would make the
requested path indirect and difficult to repeat. Terrain tracing was rejected
because this slice explicitly fixes Z at zero and voxel collision does not yet
exist. Keeping the movement loop in the editor tool was superseded because a
runtime inspector button could not share that owner without an editor dependency.
A separate test component remains rejected because the existing manager already
owns the exact target and update point needed by this smoke behavior.

## Performance Overview

`VoxelManager` owns one top-level performance snapshot because it already owns
the production frame callback and canonical chunk-stream counters. A fixed
10-second window samples the engine's previous-frame duration every update and
process/GPU memory once per second. Chunk integrations are counted where they
enter the canonical loaded dictionary. The published snapshot contains three
pillars:

- frame performance: average frames per second, p95 and p99 frame duration, and
  average GPU frame duration;
- memory: average and peak approximate process working set plus average and peak
  engine-tracked GPU memory and the current GPU memory budget;
- chunks: current loaded and pending counts, chunks integrated during the
  window, window chunks per second, and the existing last-stream settle and
  generation throughput.

Upper-tail frame duration is the canonical stutter metric. Percentile FPS was
rejected because inversion makes a high percentile describe the fastest rather
than the slowest frames. Averages alone were rejected because they hide stalls.
Process working set and engine-tracked GPU allocations are deliberately labeled
as different scopes; neither is presented as memory owned only by voxel chunks.

The per-frame path writes scalar values into fixed arrays and performs no
sorting, logging, Git access, process launch, or network access. Memory is read
at 1 Hz. Copying and sorting happen once per completed window, and the fixed
32,768-frame capacity reports truncation rather than silently allocating. This
supports a complete 10-second window up to 3,276 frames per second. A separate
profiler component and an editor-owned sampler were rejected because either
would duplicate the production frame/chunk lifecycle or prevent the human and
MCP entry points from sharing one result.

The manager inspector's `Log Performance Overview` button and the editor MCP
tool `performance_overview` call the same manager method. That method emits one
machine-searchable `performance.overview` record only on request. UTC capture
time, scene, streaming center, target position, caller-supplied task, and
caller-supplied revision identify when, where, and what was measured. Task and
revision are passive strings: the runtime never queries Git, invokes another
process, or performs a network lookup. The external MCP caller may supply the
current commit, while a human may enter the same metadata in the inspector.
