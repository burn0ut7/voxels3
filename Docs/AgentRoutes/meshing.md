# Meshing Route

Use this route for surface extraction, CPU or GPU mesh generation, render and
collision geometry, normals, seams, LOD, and mesh scheduling.

## Design Ownership

Read [GPU meshing](../Architecture/GpuVoxelMeshing.md) for the current extraction,
LOD, publication, and rendering contracts. Keep implementation status there.
For a new responsibility such as collision, compare algorithms and execution
locations against topology, edit latency, engine limits, memory movement, and
measured throughput before selecting its canonical path.

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

## Engine Constraints

Before shader edits, read the subsystem's
[parser and resource constraints](../Architecture/GpuVoxelMeshing.md#sbox-vfx-shader-parser-gotcha),
including clean-start validation. Keep reproduction evidence and engine-version
qualifications there instead of duplicating them in this route.

## Correctness

- Sampling at region boundaries must be identical on both sides.
- Normals must use a documented field-gradient or geometry convention.
- Triangle winding, index limits, degenerate handling, and material assignment
  must be explicit.
- Reject stale asynchronous results when the source data version changes.
- Changes to LOD transitions require an explicit crack-prevention design and
  validation of the affected boundaries.

## Measurement

Measure representative flat, noisy, empty, solid, and edited regions as relevant
through the production path. Track build latency, throughput, allocations,
output size, upload/readback cost, and worst-case frame impact. Visual inspection
alone is insufficient; follow the [performance route](performance-and-testing.md).
