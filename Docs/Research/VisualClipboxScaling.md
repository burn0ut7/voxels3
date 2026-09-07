# Visual Clipbox Scaling Decision

Decision context: 2026-09-03 gameplay-independent visual loading work. This
record owns the rationale and serious alternatives. Current configuration and
ownership belong to [voxel foundation](../Architecture/VoxelChunkFoundation.md)
and [GPU meshing](../Architecture/GpuVoxelMeshing.md#canonical-level-indexed-placement).
Test outcomes belong to [the ledger](../ValidationResults.md#terrain-visual-stress-001v1---256-goal-and-512-stretch).

## Problem and Chosen Direction

The failed implementation expanded gameplay interest into an eager coordinate
set, constructed one immutable wrapper per coordinate, and sent potential
surfaces into LOD0 meshing. Increasing gameplay radius therefore created cubic
allocation and mesh work even though the visual clipbox had not expanded.
The ledger's loading diagnosis records the exact session and measurements.

The implemented direction separates analytic gameplay membership from bounded
visual preparation. Additional view distance enables another ordinary fixed-cache
level and adjacent transition pair through the existing GPU pipeline. The
implicit gameplay payload does not justify materializing every coordinate;
future mutable gameplay state will need its own explicit residency decision.

## Evidence and Transfer

The [research catalog](../smooth_procedural_voxel_terrain_resources.md) supplies
source links and their compatibility limits:

- Geometry Clipmaps and GPU-Based Geometry Clipmaps support nested reusable
  caches and updates to exposed regions. Voxels3 adapts that cache discipline;
  their heightfield rings and trims cannot represent caves.
- GPU Gems 3 procedural terrain supports bounded geometry pools and remembered
  empty blocks. Its hardware, extraction pipeline, and capacities are not targets.
- Godot Voxel and Voxel Plugin provide LOD, backlog, and priority failure cases.
  Their engine ownership and CPU scheduling are not templates for Voxels3.
- Far Cry 5 and GigaVoxels inform requested/resident separation and bounded
  detail. Neither supplies Voxels3's volumetric mesh publication implementation.

## Alternatives and Disposition

| Alternative | Decision and reason |
| --- | --- |
| Expand the gameplay or LOD0 cube for view distance | Rejected: reproduces cubic work and confuses simulation interest with rendering quality. |
| Separate distant heightfield renderer | Rejected: loses arbitrary volumetric surfaces and introduces another terrain representation. |
| Replace the renderer with an octree or sparse ray caster | Rejected for this slice: changes ownership, topology, and publication beyond the scaling problem. |
| Fixed-cache level-indexed clipbox | Implemented: extends reach using the same volumetric extraction, transition, allocator, and publication paths. |
| Analytic implicit-SDF gameplay cube | Implemented for the current immutable payload; chunk views are created on demand. |
| Underground occlusion hierarchy | Deferred: separate measured visibility problem, not a prerequisite for distance tiers. |

## Limits

Outer caches remain full 3D. Conservative field bounds skip provably empty or
solid extraction, but are not occlusion culling of hidden cave surfaces. A
future visibility change must preserve volumetric geometry and transition
coverage.

This design record does not grant performance acceptance. Expansion/contraction
checks, default figure-eight comparisons, unresolved regressions, and exact
run data are recorded only in the ledger. Do not infer a passing baseline from
an implementation being present in the source.
