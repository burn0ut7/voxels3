# Meshing Route

Use this route for surface extraction, CPU or GPU mesh generation, render and
collision geometry, normals, seams, LOD, and mesh scheduling.

## Decision Status

The repository has no selected meshing algorithm and no CPU or GPU mesher.
Before implementation, compare the viable algorithms and execution locations
against smoothness, topology, edit latency, collision needs, s&box API limits,
hardware targets, memory movement, and measured throughput.

Select one canonical production meshing path for a given responsibility. Do not
ship CPU and GPU implementations as mutual fallbacks or allow them to diverge.
If a later decision replaces the selected path, remove the superseded one in the
same change after equivalent behavior is verified.

## CPU and GPU Boundaries

- The authoritative SDF/world data remains authoritative regardless of where
  mesh extraction runs.
- A GPU implementation must account for buffer ownership, capacity, dispatch,
  synchronization, readback, error handling, and resource lifetime. Do not hide
  unpredictable readback behind a convenience abstraction.
- A CPU implementation must define job granularity, cancellation, concurrency,
  allocation behavior, and the engine-thread point where results are committed.
- If rendering and collision require different outputs, derive both from the
  same field and conventions. This is not permission to create two terrain
  systems.
- Do not create a CPU reference mesher, offline oracle, or validation-only
  implementation. Validate the selected production mesher by executing its real
  in-world path and measuring its actual geometry and downstream world effects.

## Correctness

- Sampling at region boundaries must be identical on both sides.
- Normals must use a documented field-gradient or geometry convention.
- Triangle winding, index limits, degenerate handling, and material assignment
  must be explicit.
- Reject stale asynchronous results when the source data version changes.
- LOD transitions require an explicit crack-prevention design before LOD is
  introduced. Do not add speculative LOD infrastructure to an initial mesher.

## Measurement

Measure representative flat, noisy, empty, solid, and heavily edited regions.
Track build latency, throughput, allocations, output size, upload/readback cost,
and worst-case frame impact. Exercise these through the production meshing path
in the actual playable world; visual inspection alone is not validation. Define
fixed scenario parameters and append every measured run to
`Docs/ValidationResults.md` as required by
`Docs/AgentRoutes/performance-and-testing.md`.
