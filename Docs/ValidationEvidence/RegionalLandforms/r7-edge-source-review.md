# R7 edge placement source review

Reviewed 2026-09-09 on codex/terrain-biome-generation, uncommitted R7 sources.
This is source evidence, not a runtime acceptance result.

- Regular Stage 6 (voxel_persistent_geometry.hlsl) constructs axis-aligned world
  endpoints, refines active edges after the scans and stores the refined world
  coordinate. Its vertex writer decodes that coordinate without solving again.
- TransitionDecodeEdge uses unit fine edges and two-unit coarse edges in its
  face lattice. CreateTransitionRequest supplies signed axis-unit bases for all
  six faces. RefineVoxelEdge orders endpoints by increasing world-axis coordinate,
  including swapping endpoint densities and positions on reversed face bases.
- Both Stage 6 callers use the same immutable request settings and correction
  presence flag. Both scratch owners place a UAV barrier on the rewritten edge
  words before emit. The active coordinate encoding is consumed only after the
  binary flags have been scanned; scan counts do not consume float bit patterns.
- For an uninterrupted eight-bisection bracket, its mathematical width is the
  original edge length / 256 before the final clamped secant. At default spacing
  that is 0.0625 units for a 16-unit edge and 4 units for a 1024-unit edge.
  These are bracket widths, not measured surface error or global crack bounds.
  Early near-zero termination and float resolution are separate stopping rules.
- VoxelCollisionMesher.Build still linearly interpolates endpoint densities with
  da / (da - db), clamps the result and welds within 0.01 world units of endpoints.
  It also clamps near-zero density magnitudes to 1e-6 while preserving sign.
  GPU refinement therefore does not establish collision/render equality.
- Interior edited GPU values use procedural base plus linearly interpolated
  endpoint corrections. This is not a direct evaluation of every intervening
  stored edit sample on a coarse edge. Missing coarse sign changes and multiple
  roots remain limitations; no new topology recovery was added.

Required next evidence remains edited surface/collision alignment, negative and
near-zero cases, transition geometry and moving-frame/GPU measurements. Existing
degenerate audit failures are not waived by the above source checks.

Live preflight: editor 26.09.08, voxels3/basic_example playing, native compile
success with zero errors. r7-edit-preflight-state.json records zero pending visual,
transition and collision work and 4913 ready collision chunks. Player position
was approximately (-4984,7997,-28389), grounded; this is not the prescribed plains
edit scenario. The concurrently active "Add biome voxel materials" task owns an
ongoing live validation session. No player, camera, terrain or runtime code was
changed in this review, avoiding interference with that task's measurements.
