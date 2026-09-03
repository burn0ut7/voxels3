# Meshing Route

Use this route for surface extraction, CPU or GPU mesh generation, render and
collision geometry, normals, seams, LOD, and mesh scheduling.

## Decision Status

LOD0, LOD1, and LOD2 rendering use the same production regular-cell Transvoxel
GPU path. The LOD0-to-LOD1 and LOD1-to-LOD2 boundaries use the same level-aware
production Transvoxel transition-cell path documented in
`Docs/Architecture/GpuVoxelMeshing.md`; there is no LOD-specific fallback or
parallel mesher. Collision meshing remains unselected. Before implementing that
responsibility,
compare viable algorithms and execution locations against smoothness, topology,
edit latency, collision needs, s&box API limits, hardware targets, memory
movement, and measured throughput.

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

## s&box VFX Shader Parser Gotcha

s&box 26.08.19 can terminate in native `vfx_vulkan` code while reflecting a
compute shader instead of reporting an ordinary shader error. The observed
signature was `EXCEPTION_ACCESS_VIOLATION_READ / 0xffffffffffffffff` through
`HlslParserErrorCallback`, `recoverFromMismatchedToken`,
`hlslvariablesParser`, and `CHlslParser::Parse`.

Voxels3 reproduced this when persistent vertex and index output writes were
embedded in the large multi-stage terrain compute shader. The density,
classification, scan, digest, and count stages were cold-start safe. Adding
either final geometry-buffer write stage to that monolithic program caused the
native parser crash even when the shader compiled successfully during a live
editor session. Moving the two writes into the small dedicated
`voxel_emit_vertices_cs.shader` and `voxel_emit_indices_cs.shader` resources
removed the crash without changing geometry bytes or topology.

Treat the following as hard requirements for terrain compute shaders:

- Keep persistent vertex and index writes in their dedicated shader resources.
  Do not merge them into `voxel_persistent_geometry_cs.shader` merely to reduce
  the shader count.
- This rule remains absolute for the regular terrain pipeline. The level-aware
  transition pipeline serving both LOD boundaries is a measured engine-specific
  exception: on
  s&box 26.08.19, dispatching a second transition compute resource beside its
  topology resource terminates the editor natively even when the second shader
  is a freshly compiled zero-work kernel with no reflected resources. Its final
  vertex and index stages therefore share one transition resource capped at the
  engine's 16-storage-buffer limit. Do not apply this exception to regular
  terrain or other shaders without reproducing the same native failure.
- Give each dedicated output shader only the declarations, helpers, and tables
  it actually consumes. Do not create several wrappers that all include the
  complete multi-stage program.
- Use conventional multiline VFX/HLSL grammar: multiline `HEADER`, `MODES`,
  `FEATURES`, `COMMON`, `CS`, `VS`, and `PS` blocks; one structure member or
  declaration per line; and explicit braces around control flow. The engine's
  parser and error-recovery path are less tolerant than the live compiler.
- Do not treat a successful hot compile as parser-crash validation. After a
  shader resource or include change, require a clean editor restart, verify
  the editor remains alive, verify the s&box Sentry `last_crash` marker did not
  advance, and inspect the fresh log for HLSL/parser, failed shader load,
  missing compute pipeline, dispatch, and managed exception errors.
- A newly added shader may initially report a failed on-demand recompile and a
  missing `.shader_c`. Compile it successfully in the live editor first, then
  repeat the clean-start check. Never hide this failure by disabling scratch
  construction or leaving an empty kernel in production.

When diagnosing a similar crash, bisect valid shader programs by complete
stage boundaries. Keep every intermediate variant syntactically valid; an
invalid preprocessor guard can remove a function's closing brace, and cold
construction of that invalid asset can crash the native error-recovery path
before a useful diagnostic reaches the console.

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
