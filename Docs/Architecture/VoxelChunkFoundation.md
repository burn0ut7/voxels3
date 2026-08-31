# Voxel Chunk Foundation

## Scope

This decision covers the first production chunk slice: integer chunk identity,
implicit SDF data, deterministic volumetric terrain, bounded streaming,
and runtime/editor diagnostics. LOD0 surface extraction is now implemented by
the sole GPU path documented in `GpuVoxelMeshing.md`. Collision, live edits,
persistence, and network replication remain later slices.

## Canonical Ownership and Data Flow

- `VoxelManager` owns the loaded-chunk dictionary, desired streaming set, load
  queue, configuration, diagnostics, and spatial conversions.
- Each `VoxelChunk` owns the parameters required to evaluate its authoritative
  `(CellsPerAxis + 1)^3` logical density samples. Negative density is solid,
  positive density is air, and zero is the surface.
- The current unedited base field is deterministic volumetric generator version
  `5`, identified by explicit generation settings and evaluated only from
  absolute coordinates. Shared chunk-boundary samples therefore receive
  identical integer lattice inputs.
- The same canonical sample query derives one semantic material ID from density:
  `Air = 0` when density is positive and `Grass = 1` when density is zero or
  negative. The zero-density surface therefore belongs to Grass on both chunks
  that share it. Material IDs are implicit and allocate no sample payload.
- Chunk objects are ordinary managed data, not networked GameObjects. Multiplayer
  peers can reconstruct the same base field; a later edit slice must route
  authoritative mutations through this single chunk state rather than creating a
  second copy.
- `VoxelManager` owns the GPU mesher that derives transient extraction scratch
  and persistent indexed render geometry from an immutable chunk/SDF
  descriptor. `VoxelChunk` remains free of engine resources,
  GPU buffers, render objects, and mesh lifetime state.
- The renderer stores each completed regular or Transvoxel transition region as revisioned disposable indexed
  geometry. A regular remesh evaluates generator v5 once over a transient `35^3`
  density lattice, classifies the `32^3` regular cells from cached corners,
  reuses region-local edge vertices, and emits 24-byte position/normal vertices
  plus 32-bit indices. Central-difference endpoint gradients use the same
  one-sample halo; density and classification scratch are discarded after emit.
- Persistent geometry lives in shared arenas rather than chunk-owned resources.
  Each arena supplies a 32 MiB vertex buffer, a 16 MiB index buffer, and 256
  indexed-indirect records. Exact contiguous ranges are allocated only after a
  bounded count-metadata readback; released ranges coalesce, live ranges never
  move, and one indexed-indirect API submission draws each active arena.
- Candidate geometry never becomes authoritative state. The previous resident
  revision remains visible until the candidate has emitted and crossed a render
  sequence, after which publication atomically replaces only that coordinate.
  Empty results carry the same revision lifecycle without consuming an arena.
  Cancellation, supersession, unload, and configuration reset release derived
  ranges; no density or geometry is read back and no geometry is replicated.
- Ordinary terrain drawing consumes only persistent position, normal, index,
  visibility, and indirect-argument buffers. It does not include or evaluate
  the canonical SDF. Future edits therefore invalidate affected region
  revisions and pay field/extraction cost during remesh, not on every frame.
- Nested render-only clip boxes sample that same canonical SDF directly but
  never enter the loaded-chunk dictionary. Complete coarse boxes remain
  resident fallback coverage beneath finer levels; LOD0 authoritative chunks
  exist only in the full-detail box. The exact spatial and seam contract is
  documented in `TransvoxelClipBoxLod.md`.
- Streaming target movement owns one gameplay desired set and one clip-box
  render selection. Adjacent movement retains the previous published coverage
  while exact entering slabs and their seams are prepared; initialization and
  teleports publish the outer box first and refine coarse-to-fine. Missing
  gameplay coordinates are ordered nearest-first with deterministic tie breaks.
- An explicitly assigned `StreamingTarget` is authoritative. Without one, the
  manager resolves exactly one active non-proxy `PlayerController` for the local
  client. It refuses to choose among multiple locally controlled players and
  logs that multi-origin server interest management is still required.

## Procedural Generator Version 5

`ProceduralTerrainSdf` owns the single unedited base field. The serialized,
user-facing settings remain `WorldSeed`, `SurfaceBaseHeight`,
`SurfaceFrequency`, and `SurfaceAmplitude`. Defaults are respectively `1337`,
`0`, `0.0005`, and `128`; the authored stress scene uses those same settings.
Generator version `5`, the 2D/3D gradient tables, hashes, seed salts, and cave
recipe are backend-owned. There is no inspector-selectable generator variation
or alternate implementation.

Changing any exposed setting cancels gameplay and warm generation, increments
the existing stream and terrain-content revisions, clears derived GPU meshes,
and rebuilds through the canonical streaming path. Each immutable `VoxelChunk`
retains the complete settings value used to construct it. The GPU SDF descriptor
copies that value, so descriptor equality and stale-result rejection include
every field-shaping input.

The CPU owner and `voxel_sdf_v5.hlsl` use the same unchecked 32-bit integer
hashes, fixed gradient tables, seed salts, simplex skew/unskew constants, and
operation order. Negative coordinates use explicit floor operations. Simplex
outputs are clamped to `[-1,1]`, making their conservative range contractual
even where CPU and GPU floating-point implementations differ slightly.

The canonical field is:

```text
surfaceZ = surfaceBaseHeight
    + simplex2D(worldXY * surfaceFrequency, worldSeed) * surfaceAmplitude
surface = worldZ - surfaceZ
noodleThreshold = 0.056 + 0.016 * simplex3D(world / 16384, thicknessSeed)
tunnel = 512 * (noodleThreshold - max(
	abs(simplex3D(world / 6144, noodleASeed)),
	abs(simplex3D(world / 6912, noodleBSeed))))
cheeseThreshold = 0.48 - 0.12 * thicknessNoise
cheese = 512 * (simplex3D(world / 8192, cheeseSeed) - cheeseThreshold)
depth = -surface
cave = min(max(tunnel, cheese), min(depth - 512, 8192 - depth))
density = max(surface, cave)
```

The surface term remains the exterior terrain. Intersecting near-zero regions of
two independent 3D fields form long noodle passages; a slowly varying third
field changes their thickness. Version 5 uses `6144/6912` noodle wavelengths,
doubling physical passage scale and halving encounter frequency relative to the
rejected v6 recipe without changing normalized occupancy. That same slow field
lowers the cheese cutoff
where noodles are wider, producing cave-rich zones with broad chambers and
natural tunnel intersections. Its opposite phase leaves quieter regions with
thinner passages and a higher cavern cutoff. The effective cheese threshold
spans `0.36..0.60` without another noise sample. The relative-depth envelope
preserves one `512`-unit chunk of solid overburden and confines topology to the
next fifteen chunks, ending `8192` units below the local exterior surface. This
band spans more than one vertical period of each primary cave field, so tunnels
and chambers vary materially with Z instead of being clipped into a shallow
horizontal slice.

Conservative classification remains a full closed-AABB contract. The existing
tight 2D surface interval first proves exterior air or solid. Underground solid
regions propagate conservative 3D simplex intervals through `abs`, `max`,
`min`, thickness, the depth envelope, and final composition. Normalized 3D
simplex uses a documented Lipschitz bound of `22`. A complete-AABB interval may
prove a chunk solid or air; otherwise only the depth-banded cave region remains
`PotentiallySurfaceContaining`. Per-cell recursive proof was rejected after it
made background batches run for seconds without yielding. Non-finite input and
remaining uncertainty also stay potential; no heightfield assumption can reject
a cave.

Rejected alternatives are an absolute world-Z cave band, domain warping,
additional depth layers, explicit worm carvers, room graphs, fBm/octave
controls, a selectable generator menu, a general noise graph, exposing hashes
or salts, dense CPU sample arrays, and any allocator or meshing optimization
bundled with this generator change. The surface-relative band keeps consistent
overburden under hills and valleys. Existing unwarped 3D fields now have enough
vertical range to supply the requested topology without another field or
parallel generation path.

## Selected Dimensions

- Cells per axis: `32`
- SDF samples per axis: `33`
- Cell size: `16` s&box units (s&box uses Source-style inches)
- Chunk world extent: `512` units per axis
- Density samples per chunk: `35,937`
- Production load radius: `16` chunks in X/Y/Z around the player's current chunk
  (`33x33x33 = 35,937` chunks)
- Supported load-radius range: `0..128` chunks. The default and authored
  production scene remain `16`; the upper bound exists for intentionally
  expensive single-resolution baselines and is not a recommended production
  configuration. Radius `128` requests an inclusive `257^3 = 16,974,593`
  gameplay-chunk cube and a `259^3 = 17,373,979` render-warm cube.
- Background generation concurrency: one serialized worker pipeline
- Main-thread integration budget: `0.500 ms` per update, independent of chunk
  count

At the production radius the settled world contains `1,291,467,969` logical density
samples. The procedural field evaluates them directly and has no density arrays.
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
  array but changes exact density queries because the procedural field varies
  throughout the volume. Every chunk retains only the immutable parameters
  needed to evaluate the same canonical function on demand.
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
plus cells/axis, cell size, the immutable procedural settings, and a stream revision. It constructs
only ordinary `VoxelChunk` managed data; it does not read scene state, mutate the
manager, log, or call engine resource APIs. Completion explicitly returns to the
main thread before any authoritative collection changes.

Initial population runs through this same pipeline from async `OnLoad`. s&box
keeps the loading screen active until the component finishes, so the first 35,937
chunks exist before `OnStart` admits active play. Player boundary crossings use
the identical generator and integration queue from `OnUpdate`; startup does not
maintain a second terrain or scheduling implementation.

Only one worker pipeline may execute for a manager. A newer desired set cancels
the prior revision, waits for that worker to yield, and then starts, preventing
task stacking and CPU oversubscription. The sorted sequence publishes immutable
batches of at most 256 chunks to the main thread, so nearest results integrate
before a large teleport's complete coordinate set finishes generation. Revision
and cancellation checks run before construction, before publication, and during
integration. Results whose revision or desired membership changed are counted
and discarded. Configuration changes invalidate all loaded and queued chunks
because their coordinate-to-sample meaning changed; target movement retains
chunks and pending coordinates still in the desired set.

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
- One task per chunk would increase scheduler pressure and make cancellation
  storms more likely. GPU remesh throughput is controlled independently by
  bounded scratch batches and does not change this CPU streaming policy.
- Keeping full rebuild and incremental movement as separate streaming systems
  would duplicate ownership. Both modes instead feed the same queues, revisions,
  generation batches, integration budget, and GPU scheduler.

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
- Per-chunk load/unload logging is intentionally absent. Routine startup,
  streaming begin/completion, and stale-result detail is available only through
  the inspector's opt-in `Verbose Logging` setting, which defaults off and
  avoids constructing diagnostic strings and probes in normal production.
  Warnings, errors, bounded read-only diagnostic results, and performance-test
  begin/save records remain unconditional because they are sparse and actionable.
- `voxel_chunk_info x y z` retrieves any currently loaded chunk by its log
  identifier coordinates and reports generator identity, conservative density
  bounds, the origin sample/material, and the three positive-face boundary
  samples, or a clear missing result otherwise.
- Stream completion reports effective loaded chunks per second, pure SDF
  generation chunks per second, worker time, time-budgeted integration work,
  slowest integration update, maximum observed active-play frame, loaded,
  retained, unloaded, generated, pending, and discarded stale chunk counts.
  These detailed fields remain in structured logs rather than becoming separate
  inspector rows.
- Loaded-chunk bounds and labels are not drawn at runtime because their work and
  gizmo count scale with every loaded chunk. The read-only chunk query remains a
  bounded observation of canonical state. There is no diagnostic command that
  moves the streaming target and no separate debug copy or test implementation.

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
524,288-frame capacity reports truncation rather than silently allocating. The
capacity covers the bounded eight-loop inspector limit at the canonical speed
and distance with substantial headroom on the current baseline hardware. A
separate profiler component and an editor-owned sampler were rejected because
either would duplicate the production frame/chunk lifecycle or prevent the
human and MCP entry points from sharing one result.

The automated suite appends one versioned JSON object to
`performance/results-v1.jsonl` in `FileSystem.Data` when its configured loop
count completes. Each record contains a unique run ID; UTC capture time;
required task and revision; scene, world, generator, and workload parameters; and nested
frame, memory, chunk, meshing, visibility, and streaming metrics. Schema version
4 records full/incremental update counts, complete synchronous-path timing,
coordinate work, draw-command rebuild cost, generation batch/first-availability
timing, conservative warm classifications/constructions, and peak gameplay/warm
mesh backlog. Schema version 5 adds one settled GPU scalar snapshot containing
non-empty surface/warm mesh counts and total/average/maximum active-cell usage,
plus reserved capacity, utilization, and configured/observed dispatch limits.
It reuses the existing single end-of-run visibility scalar readback and performs
no geometry readback. Schema version 9 additionally records persistent unique
vertices, indices, triangles, used/committed geometry bytes, arena free ranges
and fragmentation, transient scratch bytes, bounded count-metadata readback
bytes and latency, count/emit submission CPU time, topology/position digests,
and the ordinary-render SDF evaluation count. Schema version 10 adds scratch
lane count; scheduled, count-submitted, and published throughput; batch rate
and occupancy; stage timing distributions; queue distributions; direct
player-route render lag; post-loop drain time; and a separate fixed 10-second
stationary frame/GPU/memory/visibility window after full settlement and two
render-sequence advances. The manager exposes the resolved
results path as inspector status.
Task and revision are passive caller-supplied strings: the runtime never queries
Git, invokes another process, or performs a network lookup. Blank or `unassigned`
context rejects the run before movement begins.

The inspector's `Run Performance Test` button is the canonical automated suite
entry point. Its task, revision, speed, distance, and loop-count attributes are
captured once when the test starts. The manager resets measurement on that same
update boundary, advances the shared production figure-eight movement path,
counts exact full-curve crossings, and stops automatically at the configured
loop count. Only then does it serialize and append the result, keeping file I/O
outside the measured interval. The canonical v3 baseline uses speed `2500`,
distance `50000`, and one loop; the inspector permits one through eight loops
for explicitly different workloads.

The editor MCP `run_performance_test` operation calls the same manager method as
the button and requires the same passive task and revision labels.
Neither entry point controls completion timing: no human, AI, external sleep,
polling cadence, Git discovery, process launch, or network lookup participates in
the measured boundary. Manual start/report/stop calls and an external script
clock were rejected because their scheduling variance would change the measured
interval.

JSON Lines is the canonical storage shape because each completed run is one
self-contained append, a partial final write cannot invalidate earlier runs,
and later scripts or dashboards can stream records without loading or rewriting
history. Rewriting one growing JSON array was rejected because save cost scales
with history. One file per run was rejected because it creates an unnecessary
file-enumeration and retention problem. General engine logs were rejected as the
data store because their rotation, formatting, and retention are not owned by
the project. The append occurs on the manager thread after measurement; only one
local manager/test may write at a time.
