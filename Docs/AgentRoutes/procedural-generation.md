# Procedural Generation Route

Use this route for seeds, density functions, noise, terrain composition, biomes,
and initial world creation.

## Determinism

- Generation must be a deterministic function of explicit world seed,
  configuration/version, and world-space coordinates.
- Do not use client-local random state, frame time, traversal order, culture, or
  process-dependent hashes as generation inputs.
- Sampling the same coordinate through different region paths must produce the
  same result.
- Define how generator-version changes affect saved worlds and multiplayer
  compatibility before changing an established generator.

## System Boundary

- Generation owns the unedited base field, not live mutable world state.
- Apply player edits through the canonical terrain mutation/storage system.
- Avoid baking meshing concerns into generation. The generator supplies field
  values and required semantic data through one documented contract.
- Compose only the terrain operations needed by the current feature slice.
  Do not build a general graph, biome framework, or plugin system speculatively.

## Performance and Tests

Generation must support bounded spatial evaluation without generating unrelated
world areas. Measure sample throughput and region-generation latency. Validate
same-seed reproducibility, boundary continuity, coordinate stability, bounds,
and known feature samples through the production path. Follow the
[performance and testing route](performance-and-testing.md) for execution,
fixed scenarios, and reporting.
