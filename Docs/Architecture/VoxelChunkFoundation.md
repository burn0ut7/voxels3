# Voxel Chunk Foundation

## Scope

This decision covers the first production chunk slice: integer chunk identity,
SDF sample storage, deterministic flat-terrain population, bounded streaming,
and runtime/editor diagnostics. Surface extraction, collision, live edits,
persistence, and network replication are later slices and are not implemented
here.

## Canonical Ownership and Data Flow

- `VoxelManager` owns the loaded-chunk dictionary, desired streaming set, load
  queue, configuration, diagnostics, and spatial conversions.
- Each `VoxelChunk` owns one authoritative `(CellsPerAxis + 1)^3` float density
  array for its coordinate. Negative density is solid, positive density is air,
  and zero is the surface.
- The current unedited base field is the world-space plane
  `density = worldZ - TerrainSurfaceHeight`. Sampling is based only on global
  coordinates, so shared chunk-boundary samples are identical.
- Chunk objects are ordinary managed data, not networked GameObjects. Multiplayer
  peers can reconstruct the same base field; a later edit slice must route
  authoritative mutations through this single chunk state rather than creating a
  second copy.
- Streaming target movement computes one desired coordinate set. Obsolete chunks
  are removed, missing chunks are queued nearest-first with deterministic tie
  breaks, and a fixed per-frame budget bounds generation work.
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
- Raw float-density memory per chunk: `143,748` bytes, about `140.38 KiB`
- Default horizontal stream radius: `4` chunks in X/Y
- Default vertical world range: chunk Z `-2` through `6` inclusive
  (`9x9x9 = 729` chunks)
- Default generation budget: `8` chunks per frame

At the default bounds the settled world contains `26,198,073` density samples
using `104,792,292` raw density bytes, about `99.94 MiB`, excluding managed-array
and dictionary overhead.

Vertical residency is deliberately not centered on the player. Every horizontal
column inside radius `4` loads the complete configured world range, so a mountain
does not disappear merely because its peak is farther than a player-centered Z
radius. The default range covers world Z `-1024` through `3584` units. “Everything”
means everything inside this explicit finite terrain envelope; expanding the
world requires changing these bounds and recording a new performance scenario.

A later generator may prove that particular vertical chunks are uniformly empty
and omit their density arrays, but that optimization must preserve the same
full-column visibility contract. It must not reintroduce player-centered vertical
clipping or a second streaming path.

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

`32^3` at `16` units is the best initial balance, not a permanent claim. A future
dimension change requires representative fixed-parameter in-world measurements
and a versioned validation scenario.

## Threading, Invalidation, and Scale

This first implementation runs bounded generation on the engine update thread.
It performs no background work, so there is no stale-result race yet. The
per-frame chunk budget is the scheduling boundary. When configuration changes,
all loaded and queued chunks are invalid because their coordinate-to-sample
meaning changed; the manager clears them and rebuilds from the current target.
When only the target coordinate or stream radius changes, still-desired chunks
remain loaded. Changing the per-frame generation budget does not invalidate data
or rebuild the desired set.

Before introducing jobs, capture the immutable generation inputs and configuration
revision, then reject results whose revision or desired membership changed. Do
not add job infrastructure until measurements identify synchronous generation as
the bottleneck.

## Debug Contract

- Human-facing inspector properties use descriptive names and summaries.
- Logs use stable machine-searchable fields plus the readable chunk name:
  `chunk=C[x,y,z]` and `cell=L[x,y,z]`.
- Chunk lifecycle detail is opt-in; stream completion and invalid configuration
  remain concise summary/warning events.
- The `voxel_stream_origin x y z` console command moves the real production
  streaming target to an exact world position for deterministic troubleshooting.
  It refuses to choose silently when more than one active manager exists.
- Stream completion reports both effective loaded chunks per second (including
  frame-budget scheduling) and pure SDF generation chunks per second. Inspector
  status exposes the same values with human-readable names.
- Runtime overlays can draw all loaded chunk bounds and labels. Cell debugging is
  deliberately limited to one selected chunk and one selected Z slice so the
  user can inspect every cell without drawing the entire volume every frame.
- Inspector buttons query the real loaded production data; there is no separate
  debug copy or test implementation.
