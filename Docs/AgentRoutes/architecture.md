# Architecture Route

Use this route for system boundaries, ownership, data flow, and any feature that
crosses voxel storage, terrain generation, meshing, rendering, collision,
networking, or persistence.

## Current State

The voxel-world foundation is established in `Code/Voxels/VoxelManager.cs` and
`Code/Voxels/VoxelChunk.cs`. `VoxelManager` owns one canonical loaded-chunk
dictionary, bounded desired/load queues, fixed LOD0/LOD1/LOD2 clipbox placement,
both 2:1 transition boundaries, and one `GpuVoxelMesher`. Each `VoxelChunk` owns
the immutable parameters for the deterministic volumetric generator-v5 SDF and
derives Grass/Air material IDs without sample arrays. The GPU mesher derives
persistent regular and transition render geometry from that same SDF contract;
table-derived primary positions are final, and one transition kernel closes both
LOD boundaries with cull-compatible indices. Read `Docs/Architecture/VoxelChunkFoundation.md` and
`Docs/Architecture/GpuVoxelMeshing.md` before changing these boundaries,
dimensions, placement, or rendering contracts.

There is no collision generation, live-edit pipeline, persistence,
multi-origin interest manager, or voxel network protocol yet. Do not infer
those systems from the implemented render-meshing path.

## Required Shape

Design around one authoritative world-state model and an explicit derived-data
pipeline:

```text
seed/config + authoritative edits
              |
              v
       canonical SDF world state
              |
      dirty-region notification
              |
     +--------+--------+
     v                 v
render mesh       collision data
     |
visibility/streaming
```

Networking supplies authoritative inputs or state to this pipeline; it must not
create a separate terrain implementation. Procedural generation defines the
unedited base field; edits modify the authoritative world state through the same
mutation boundary used by every caller.

## Ownership Rules

- Assign one owner for world coordinates, spatial partitioning, SDF convention,
  world mutation, dirty tracking, and mesh scheduling.
- Keep source data separate from derived artifacts. Derived artifacts may be
  discarded and rebuilt without changing the world.
- Components orchestrate engine lifecycle and dependencies; domain logic should
  not depend on per-frame component callbacks when it can be expressed as a
  deterministic operation.
- Make lifecycle and cancellation explicit for asynchronous or threaded work.
  Results must be rejected if their source region changed while they were built.
- Cross subsystem boundaries with narrow data contracts based on the current
  slice. Do not introduce a framework or service layer in anticipation of later
  features.

## Design Gate

Before implementing a new subsystem, document:

- its authoritative inputs and outputs;
- the exact owner of mutable state;
- how invalidation flows downstream;
- threading and engine-thread boundaries;
- determinism requirements;
- expected scale and measurable budget;
- the alternatives considered and why the chosen path is better long-term.

Resolve those decisions in the smallest relevant design note or source-adjacent
documentation. Do not build several candidates into production code.
