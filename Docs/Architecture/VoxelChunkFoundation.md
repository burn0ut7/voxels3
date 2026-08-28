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
- Logical float-density memory per chunk if materialized: `143,748` bytes, about
  `140.38 KiB`; the current implicit plane allocates `0` density-payload bytes
- Default load radius: `4` chunks in X/Y/Z around the player's current chunk
  (`9x9x9 = 729` chunks)
- Background generation concurrency: one serialized worker pipeline
- Main-thread integration budget: `0.500 ms` per update, independent of chunk
  count

At the default radius the settled world contains `26,198,073` logical density
samples. Materializing them would use `104,792,292` raw density bytes, about
`99.94 MiB`. The current plane is evaluated directly and therefore uses no
density arrays; chunk objects and dictionary storage still have managed overhead.

Memory diagnostics separate exact density-payload bytes from an estimated loaded
voxel footprint. The estimate uses `64` bytes per `VoxelChunk`: a `16`-byte x64
managed-object header plus `44` bytes of current instance fields, rounded to
8-byte alignment, then adds exact density-payload bytes. At `729` implicit chunks
this is approximately `46,656` bytes (`45.56 KiB`). Dictionary, hash-set, queue,
list, task, allocator, and component overhead is explicitly excluded because
their capacities and runtime layouts are not owned by the chunk data contract.

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
- `64^3` cells reduces chunk count but requires `65^3 = 274,625` density samples,
  about `1.05 MiB` raw density memory per chunk. It also makes one edit invalidate
  a much larger mesh/collision job.
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
  sample and create about `99.94 MiB` of payload at the default radius. They were
  removed rather than pooled or compressed.
- A single constant for sign-uniform solid or air chunks is smaller than an
  array but changes exact density queries because density still varies with local
  Z. The selected representation evaluates the canonical expression
  `chunkMinimumZ + localZ * CellSize - TerrainSurfaceHeight`, preserving the same
  float operation order and values for uniform and surface chunks alike.
- Keeping arrays only for surface chunks introduces two storage modes despite
  the current plane needing neither. General compression, profiles, and mutable
  payload promotion remain out of scope until an implemented feature requires
  stored non-planar values.
- A byte array of material IDs would add `26,198,073` redundant bytes at the
  default radius. Returning Grass or Air from the canonical density result is
  exact, deterministic, and requires no second storage path.
- A material registry, biome mapping, layered soil, and render-material resource
  references are not required for the first two IDs and are intentionally absent.
- Reporting only density-array bytes made the implicit representation appear to
  use no memory. The selected report keeps that exact payload metric and adds the
  documented chunk-object estimate. Global GC totals and guessed collection
  internals were rejected because they cannot attribute memory reliably to one
  manager.

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

- Human-facing inspector properties use descriptive names and summaries.
- The inspector derives `Player Chunk` and `Player Chunk Data` directly from the
  actual streaming target. There is no manually selected chunk coordinate,
  local sample, or cell-slice state to keep synchronized with the player.
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
  slowest integration update, maximum observed active-play frame, generated
  density-payload bytes, logical samples, loaded density-payload chunks and exact
  bytes, the estimated loaded voxel footprint, and discarded stale results.
  Inspector status exposes the same values with human-readable names and states
  what the estimate excludes.
- Runtime overlays can draw all loaded chunk bounds and labels; the chunk
  containing the actual player is highlighted. The inspector button and console
  commands query the real loaded production data; there is no separate debug
  copy or test implementation.
