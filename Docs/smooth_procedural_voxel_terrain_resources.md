# Smooth Procedural Voxel Terrain Reference Library

Use this as a standing research index for large, smooth, procedural voxel/SDF terrain work. Before major architecture changes, compare the proposed design against the closest relevant references below and note intentional differences.

## Closest Open-Source Implementations

### [Zylann/godot_voxel](https://github.com/Zylann/godot_voxel)
Mature volumetric terrain system for Godot with infinite chunk paging, procedural generators, smooth SDF terrain, LOD, and Transvoxel transitions. **Strengths:** excellent reference for block LOD, transition meshes, streaming, editing, and practical engine integration. **Weaknesses:** CPU-heavy architecture in places and not a direct model for our GPU-first meshing path.

### [bw2012/UnrealSandboxTerrain](https://github.com/bw2012/UnrealSandboxTerrain)
Active Unreal smooth voxel terrain plugin with procedural landscapes/caves, runtime modification, LOD, materials, and multiplayer. **Strengths:** close to a game-oriented large procedural terrain system and useful for understanding production-style ownership and streaming. **Weaknesses:** Unreal-specific architecture and less documentation than Godot Voxel.

### [bw2012/UE4VoxelTerrain](https://github.com/bw2012/UE4VoxelTerrain)
Older but highly inspectable smooth procedural voxel terrain implementation using Transvoxel concepts, per-chunk LOD, caves, foliage, runtime editing, and significant CUDA code. **Strengths:** especially useful for studying CPU/GPU division and GPU terrain generation. **Weaknesses:** discontinued UE4-era implementation with architectural decisions tied to older Unreal/CUDA constraints.

### [bw2012/UE5VoxelTerrainDemo](https://github.com/bw2012/UE5VoxelTerrainDemo)
UE5 continuation/example for the UnrealSandbox terrain family. **Strengths:** useful for seeing how the older terrain concepts were adapted to a more current Unreal environment. **Weaknesses:** more of an example/demo than a deeply documented research implementation.

### [VoxelPlugin/VoxelPluginFreeLegacy](https://github.com/VoxelPlugin/VoxelPluginFreeLegacy)
Legacy open-source Unreal Voxel Plugin for fully volumetric, destructible, effectively infinite worlds. **Strengths:** large production-oriented codebase covering streaming, procedural generation, editing, rendering, and engine integration. **Weaknesses:** very large and complex, and the current commercial Voxel Plugin has evolved beyond this public legacy code.

### [EricLengyel/Transvoxel](https://github.com/EricLengyel/Transvoxel)
Official Transvoxel lookup-table repository. **Strengths:** authoritative source for transition-cell tables and case data; use this rather than copying third-party tables. **Weaknesses:** not a terrain engine or streaming architecture.

### [stoyannk/voxels](https://github.com/stoyannk/voxels)
Compact voxel terrain codebase with Transvoxel-related implementation details, compression, LOD, and dynamic editing. **Strengths:** easier to inspect than a full engine when debugging algorithm details. **Weaknesses:** smaller and less production-proven than Godot Voxel or Voxel Plugin.

## Core Algorithms and Papers

### [The Transvoxel Algorithm](https://transvoxel.org/)
Eric Lengyel's official reference for crack-free transitions between voxel meshes sampled at 2:1 resolutions. **Strengths:** essential for transition-cell topology, lookup tables, diagrams, and links to the dissertation. **Weaknesses:** solves LOD seams, not streaming, scheduling, allocation, or GPU architecture.

### [Voxel-Based Terrain for Real-Time Virtual Simulations](https://transvoxel.org/)
Lengyel's dissertation is the deepest theoretical and implementation reference behind Transvoxel and multiresolution voxel terrain. **Strengths:** foundational treatment of smooth volumetric terrain and multiresolution transitions. **Weaknesses:** older CPU-era assumptions must be translated carefully into modern GPU workflows.

### [Marching Cubes: A High Resolution 3D Surface Construction Algorithm](https://graphics.stanford.edu/courses/cs164-10-spring/Handouts/paper_p163-lorensen.pdf)
The foundational isosurface extraction paper by Lorensen and Cline. **Strengths:** canonical reference for regular-cell topology and interpolation. **Weaknesses:** no native adaptive LOD, streaming, or crack handling between resolutions.

### [Geometry Clipmaps: Terrain Rendering Using Nested Regular Grids](https://hhoppe.com/geomclipmap.pdf)
Losasso and Hoppe's foundational clipmap paper for persistent viewer-centered nested LOD caches. **Strengths:** critical for understanding incremental updates, bounded complexity, snapped levels, and why only exposed regions should change. **Weaknesses:** designed for 2D heightfields rather than arbitrary volumetric SDF surfaces.

### [GPU-Based Geometry Clipmaps — GPU Gems 2](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry)
NVIDIA's GPU-oriented evolution of geometry clipmaps. **Strengths:** excellent reference for minimizing CPU involvement, persistent GPU resources, reusable geometry, and incremental updates. **Weaknesses:** heightfield-specific ring/trim geometry should not be copied directly for caves or arbitrary SDF topology.

### [Dual Contouring of Hermite Data](https://www.cs.rice.edu/~jwarren/papers/dualcontour.pdf)
Alternative smooth surface-extraction method focused on preserving sharp features using Hermite samples and QEFs. **Strengths:** valuable comparison point if Marching Cubes/Transvoxel quality or feature preservation becomes limiting. **Weaknesses:** adaptive crack-free LOD and robust production implementation are more complex than our current path.

## Sparse Volume and Large-World References

### [AcademySoftwareFoundation/openvdb](https://github.com/AcademySoftwareFoundation/openvdb)
Industry-standard sparse hierarchical volume structure with effectively infinite 3D index space and compact storage. **Strengths:** excellent reference for sparse volume representation, hierarchy, and large datasets. **Weaknesses:** designed primarily for VFX/data processing rather than low-latency game terrain streaming.

### [Efficient Sparse Voxel Octrees — NVIDIA](https://research.nvidia.com/publication/2010-02_efficient-sparse-voxel-octrees)
GPU-oriented sparse voxel octree research focused on compact representation and efficient traversal. **Strengths:** useful for future work on enormous sparse worlds, GPU traversal, and hierarchical occupancy. **Weaknesses:** targets voxel representation/ray traversal more than editable polygonized smooth terrain.

### [Phyronnaz/HashDAG](https://github.com/Phyronnaz/HashDAG)
Research implementation for compressed sparse voxel DAGs with interactive modification. **Strengths:** useful if persistent edited-world storage becomes a dominant memory problem. **Weaknesses:** substantially different from our current procedural-SDF-plus-mesh-cache architecture.

## Smaller Reference Implementations

### [Eldemarkki/Marching-Cubes-Terrain](https://github.com/Eldemarkki/Marching-Cubes-Terrain)
Unity smooth infinite terrain example using Marching Cubes, Jobs, and Burst. **Strengths:** clean reference for chunk generation, parallel work, and straightforward procedural terrain organization. **Weaknesses:** lacks the mature multilevel LOD/transition architecture required for very large view distances.

### [Fobri/Fast-Unity-Marching-Cubes](https://github.com/Fobri/Fast-Unity-Marching-Cubes)
Performance-focused Unity Marching Cubes implementation emphasizing raw mesh buffers and threaded work. **Strengths:** useful for studying mesh-generation hot paths and practical optimization tradeoffs. **Weaknesses:** not a complete large-world LOD architecture.

### [Scrawk/Marching-Cubes](https://github.com/Scrawk/Marching-Cubes)
Straightforward Marching Cubes and Marching Tetrahedra reference implementation. **Strengths:** useful for algorithm sanity checks and simple topology comparison. **Weaknesses:** not intended as a production terrain streamer.

## Production / Closed-Source Systems Worth Studying

### [Planet Nomads](https://planet-nomads.com/)
Commercial game built around completely procedural smooth voxel terrain, caves, traversal, and dynamic LOD. **Strengths:** especially relevant because its developers documented terrain-streaming and LOD problems encountered while building a real game. **Weaknesses:** source is not public, so architecture must be inferred from development articles and demos.

### [No Man's Sky terrain reverse-engineering](https://github.com/gistya/nomansterrain)
Large procedural volumetric planetary terrain is a valuable production-scale conceptual reference. **Strengths:** demonstrates enormous procedural worlds with caves, density fields, and runtime generation. **Weaknesses:** implementation is closed and community reverse-engineering is not authoritative.

### Astroneer
Production example of smooth, editable, procedural planetary voxel terrain. **Strengths:** demonstrates that highly interactive smooth volumetric worlds can ship at game scale. **Weaknesses:** implementation details are closed, so use talks and postmortems for concepts rather than concrete code decisions.

## Recommended Recurring Review Set

For major terrain changes, always review at least:

1. [godot_voxel](https://github.com/Zylann/godot_voxel)
2. [UnrealSandboxTerrain](https://github.com/bw2012/UnrealSandboxTerrain)
3. [UE4VoxelTerrain](https://github.com/bw2012/UE4VoxelTerrain)
4. [VoxelPluginFreeLegacy](https://github.com/VoxelPlugin/VoxelPluginFreeLegacy)
5. [Transvoxel](https://transvoxel.org/)
6. [Geometry Clipmaps](https://hhoppe.com/geomclipmap.pdf)
7. [GPU-Based Geometry Clipmaps](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry)
8. [Marching Cubes](https://graphics.stanford.edu/courses/cs164-10-spring/Handouts/paper_p163-lorensen.pdf)

## Project Rule of Thumb

Before implementing a major terrain subsystem, identify the closest references above and explicitly document:
- what problem they solve;
- what architecture they use;
- what we are borrowing;
- what we are intentionally doing differently and why.

This is especially important for LOD placement, transition ownership, streaming granularity, CPU/GPU separation, mesh caching, scheduling, and large-world storage.
