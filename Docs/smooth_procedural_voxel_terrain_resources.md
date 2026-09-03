# Smooth Procedural Voxel Terrain Research Index

This document routes Voxels3 research to durable external sources. It is not a
list of approved designs. Inclusion means that a source can help answer a
specific question; it does not mean its architecture, tradeoffs, or code should
be copied.

Voxels3 has a GPU-first terrain and meshing direction. Many valuable references,
including Godot Voxel, are primarily CPU-oriented or are built around another
engine's ownership and threading model. Use those sources to study algorithms,
data flow, scheduling problems, production failure modes, or alternative
tradeoffs. Translate every finding through the current Voxels3 architecture,
the applicable agent routes, verified s&box behavior, and measured project data.

## How to Use and Maintain This Index

1. Start with the research-question router below, then open only the catalog
   entries relevant to the current decision.
2. Read each entry's transfer limits before treating its implementation as
   applicable to Voxels3.
3. In the relevant design note, distinguish observed source facts from ideas
   Voxels3 adopts, rejects, or adapts. A reference never silently becomes a
   project requirement.
4. Add a source only when it is durable, materially useful to a recurring
   Voxels3 research question, and not already represented by a better source.
5. Every new entry must identify what the source is, when to route to it, and
   its important compatibility limits. Do not add unlabeled links, generic
   inspiration, duplicates, or implementation tasks.

Prefer primary papers, official algorithm material, maintained source code, and
measured production writeups. Clearly label community analysis, demos, legacy
code, and closed-source examples because they provide different kinds of
evidence.

## Research-Question Router

| Research question | Start with | Why route here |
| --- | --- | --- |
| Regular-cell isosurface topology and interpolation | [Marching Cubes paper](https://graphics.stanford.edu/courses/cs164-10-spring/Handouts/paper_p163-lorensen.pdf) | Primary description of the baseline surface-extraction algorithm. |
| Crack-free 2:1 voxel LOD transitions | [Transvoxel site](https://transvoxel.org/), [official tables](https://github.com/EricLengyel/Transvoxel), [dissertation](https://transvoxel.org/) | Authoritative theory, diagrams, and lookup data for regular and transition cells. |
| Practical chunk LOD, transitions, streaming, and editing | [Godot Voxel](https://github.com/Zylann/godot_voxel), [UnrealSandboxTerrain](https://github.com/bw2012/UnrealSandboxTerrain), [Voxel Plugin Legacy](https://github.com/VoxelPlugin/VoxelPluginFreeLegacy) | Mature or production-oriented systems expose ownership, paging, invalidation, and integration concerns. Their architectures are not Voxels3 templates. |
| GPU-first generation or meshing work division | [UE4VoxelTerrain](https://github.com/bw2012/UE4VoxelTerrain), [GPU-Based Geometry Clipmaps](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry) | Concrete GPU-oriented examples for minimizing CPU work and reusing persistent GPU data. |
| Bounded viewer-centered LOD and incremental updates | [Geometry Clipmaps](https://hhoppe.com/geomclipmap.pdf), [GPU-Based Geometry Clipmaps](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry) | Foundational cache, snapping, and exposed-region update concepts, though the geometry is heightfield-specific. |
| Keeping streaming ahead of a fast player without holes | [Voxels3 GPU streaming research](Research/GpuVoxelTerrainStreaming.md), [Far Cry 5 terrain rendering](https://www.gdcvault.com/play/1025261/Terrain-Rendering-in-Far-Cry), [Sunset Overdrive streaming](https://www.gdcvault.com/play/1022268/Streaming-in-Sunset-Overdrive-s) | Connects requested-versus-resident LOD, coarse coverage, incremental refinement, movement budgets, and explicit teleport handling to current Voxels3 evidence. |
| Alternative sharp-feature surface extraction | [Dual Contouring paper](https://www.cs.rice.edu/~jwarren/papers/dualcontour.pdf) | Primary comparison point for Hermite data, QEFs, and feature preservation. |
| Sparse large-world volume representation | [OpenVDB](https://github.com/AcademySoftwareFoundation/openvdb), [Efficient Sparse Voxel Octrees](https://research.nvidia.com/publication/2010-02_efficient-sparse-voxel-octrees), [HashDAG](https://github.com/Phyronnaz/HashDAG) | Contrasting hierarchical and compressed representations for storage and traversal research. |
| Compact implementations for algorithm inspection | [stoyannk/voxels](https://github.com/stoyannk/voxels), [Marching-Cubes-Terrain](https://github.com/Eldemarkki/Marching-Cubes-Terrain), [Fast Unity Marching Cubes](https://github.com/Fobri/Fast-Unity-Marching-Cubes), [Scrawk/Marching-Cubes](https://github.com/Scrawk/Marching-Cubes) | Smaller codebases make topology, chunking, threading, and hot-path details easier to isolate. |
| Shipped-game behavior and production failure modes | [Planet Nomads](https://planet-nomads.com/), [No Man's Sky community analysis](https://github.com/gistya/nomansterrain), Astroneer talks and postmortems | Useful for forming questions about scale and player experience; closed or inferred implementations are not architectural evidence. |

## Open-Source Terrain Systems

| Reference | What it is | Route here when | Transfer limits for Voxels3 |
| --- | --- | --- | --- |
| [Zylann/godot_voxel](https://github.com/Zylann/godot_voxel) | Maintained Godot module for blocky and smooth voxel terrain, paging, generators, editing, LOD, and Transvoxel transitions. | Investigating mature chunk ownership, LOD, transitions, streaming, editing, or engine integration. | Primarily CPU-oriented and shaped by Godot APIs. Study responsibilities and failure modes; do not treat its CPU work division as the GPU-first Voxels3 design. |
| [bw2012/UnrealSandboxTerrain](https://github.com/bw2012/UnrealSandboxTerrain) | Active Unreal smooth-terrain plugin with procedural landscapes and caves, runtime edits, LOD, materials, and multiplayer concerns. | Investigating game-oriented terrain ownership, streaming, editing, or integration at production scale. | Unreal-specific lifecycle and rendering assumptions do not directly transfer; public documentation is limited. |
| [bw2012/UE4VoxelTerrain](https://github.com/bw2012/UE4VoxelTerrain) | Discontinued but inspectable UE4 smooth-terrain implementation using per-chunk LOD, Transvoxel concepts, and substantial CUDA work. | Studying CPU/GPU separation, GPU terrain generation, transition implementation, caves, foliage, or edits. | Legacy UE4 and CUDA constraints differ from current s&box compute and resource ownership. Treat it as a design case study, not a compatibility target. |
| [bw2012/UE5VoxelTerrainDemo](https://github.com/bw2012/UE5VoxelTerrainDemo) | UE5 example continuing concepts from the UnrealSandbox terrain family. | Checking how older terrain ideas were adapted to a newer Unreal environment. | A demo rather than a deeply documented or complete production architecture. |
| [VoxelPlugin/VoxelPluginFreeLegacy](https://github.com/VoxelPlugin/VoxelPluginFreeLegacy) | Legacy open-source Unreal Voxel Plugin for large volumetric, editable procedural worlds. | Researching broad production responsibilities across generation, streaming, editing, rendering, and engine integration. | Very large legacy codebase; current commercial versions have diverged, and Unreal ownership patterns are not s&box contracts. |
| [stoyannk/voxels](https://github.com/stoyannk/voxels) | Compact terrain implementation containing Transvoxel-related code, compression, LOD, and dynamic editing. | Isolating algorithm details without navigating a full engine plugin. | Smaller and less production-proven; implementation choices are not evidence of scalability or compatibility. |

## Primary Algorithms and Papers

| Reference | What it is | Route here when | Transfer limits for Voxels3 |
| --- | --- | --- | --- |
| [The Transvoxel Algorithm](https://transvoxel.org/) | Eric Lengyel's official explanation of crack-free transitions between voxel meshes sampled at 2:1 resolutions. | Researching transition-cell topology, case construction, lookup data, or LOD seam behavior. | Solves topology across LOD boundaries, not Voxels3 streaming, scheduling, allocation, GPU resource ownership, or engine integration. |
| [EricLengyel/Transvoxel](https://github.com/EricLengyel/Transvoxel) | Official Transvoxel lookup-table repository. | Needing canonical regular- or transition-cell tables and case data. | Tables are authoritative inputs to an algorithm, not a terrain system or implementation plan. |
| [Voxel-Based Terrain for Real-Time Virtual Simulations](https://transvoxel.org/) | Lengyel's dissertation covering smooth volumetric terrain and multiresolution transitions. | Needing the deeper theory and implementation context behind Transvoxel. | CPU-era assumptions require explicit translation to the Voxels3 GPU-first pipeline. |
| [Marching Cubes paper](https://graphics.stanford.edu/courses/cs164-10-spring/Handouts/paper_p163-lorensen.pdf) | Lorensen and Cline's foundational isosurface-extraction paper. | Verifying regular-cell topology, edge interpolation, and the baseline algorithm. | Does not provide adaptive LOD, streaming, scheduling, or crack handling between resolutions. |
| [Geometry Clipmaps paper](https://hhoppe.com/geomclipmap.pdf) | Losasso and Hoppe's viewer-centered nested-grid terrain cache. | Studying bounded complexity, snapped levels, persistent caches, and incremental exposed-region updates. | Designed for 2D heightfields; ring and trim geometry cannot represent arbitrary volumetric SDF surfaces or caves. |
| [GPU-Based Geometry Clipmaps](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry) | NVIDIA's GPU-oriented geometry-clipmap implementation. | Studying persistent GPU resources, reusable geometry, and reduced CPU involvement. | Heightfield-specific topology and update rules must not be copied directly into volumetric terrain. |
| [GPU Gems 3: Generating Complex Procedural Terrains Using the GPU](https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-1-generating-complex-procedural-terrains-using-gpu) | NVIDIA implementation chapter using GPU-generated voxel blocks, remembered empty blocks, priority, and a bounded geometry-buffer pool. | Studying GPU extraction stages, inexpensive empty-state retention, visible-block prioritization, or bounded geometry residency. | Uses historical hardware and a different procedural density/rendering pipeline; its reported capacities and timings are not Voxels3 targets. |
| [CUDA Samples: Marching Cubes](https://docs.nvidia.com/cuda/archive/11.0_GA/pdf/CUDA_Samples.pdf) | NVIDIA's official sample documentation for a GPU Marching Cubes pipeline using prefix-scan stream compaction. | Researching GPU classification and compaction of active cells before emission. | CUDA APIs and sample ownership do not map directly to s&box, and the sample is not a complete streaming or LOD system. |
| [Dual Contouring of Hermite Data](https://www.cs.rice.edu/~jwarren/papers/dualcontour.pdf) | Primary paper for a QEF-based surface extractor designed to preserve sharp features. | Comparing Marching Cubes or Transvoxel quality against another extraction family. | Robust adaptive LOD, crack handling, and GPU production integration are separate problems and may be more complex. |

## Sparse Volume and Large-World Structures

| Reference | What it is | Route here when | Transfer limits for Voxels3 |
| --- | --- | --- | --- |
| [AcademySoftwareFoundation/openvdb](https://github.com/AcademySoftwareFoundation/openvdb) | Industry-standard sparse hierarchical volume data structure with a very large 3D index space. | Researching sparse volume hierarchy, storage, traversal, or large dataset organization. | Optimized largely for VFX and data processing, not low-latency game terrain streaming or the current procedural-SDF-plus-derived-mesh model. |
| [Efficient Sparse Voxel Octrees](https://research.nvidia.com/publication/2010-02_efficient-sparse-voxel-octrees) | NVIDIA research on compact GPU sparse-octree representation and traversal. | Investigating hierarchical occupancy, compact GPU storage, or traversal at enormous scale. | Targets voxel representation and ray traversal more than editable polygonized smooth terrain. |
| [Phyronnaz/HashDAG](https://github.com/Phyronnaz/HashDAG) | Research implementation of compressed sparse voxel DAGs with interactive modification. | Investigating persistent edited-world storage when memory measurements justify it. | Fundamentally different from Voxels3's current procedural SDF and mesh-cache ownership; compression complexity is not currently a requirement. |
| [GigaVoxels](https://www.icare3d.org/research-cat/publications/gigavoxels-ray-guided-streaming-for-efficient-and-detailed-voxel-rendering.html) | Author-maintained publication page for feedback-guided production and bounded streaming of a sparse multiresolution volume. | Studying view-dependent request feedback, temporal coherence, bounded pools, or quality degradation under a fixed budget. | Uses sparse-volume ray casting rather than live polygonized SDF terrain and is not a renderer architecture for the current slice. |

## Small Algorithm Implementations

| Reference | What it is | Route here when | Transfer limits for Voxels3 |
| --- | --- | --- | --- |
| [Eldemarkki/Marching-Cubes-Terrain](https://github.com/Eldemarkki/Marching-Cubes-Terrain) | Unity smooth infinite-terrain example using Marching Cubes, Jobs, and Burst. | Inspecting clear chunk generation, parallel work organization, or procedural-terrain basics. | Lacks the mature multilevel LOD and transition system needed for large view distances and follows Unity's CPU job model. |
| [Fobri/Fast-Unity-Marching-Cubes](https://github.com/Fobri/Fast-Unity-Marching-Cubes) | Performance-focused Unity Marching Cubes implementation using raw mesh buffers and threaded work. | Studying mesh-generation hot paths and practical CPU optimization tradeoffs. | Not a complete large-world LOD architecture and not a model for Voxels3 GPU resource ownership. |
| [Scrawk/Marching-Cubes](https://github.com/Scrawk/Marching-Cubes) | Straightforward Marching Cubes and Marching Tetrahedra example. | Performing algorithm sanity checks or comparing basic topology. | Educational reference, not a production terrain streamer or performance baseline. |

## Production and Observational References

| Reference | What it is | Route here when | Transfer limits for Voxels3 |
| --- | --- | --- | --- |
| [Planet Nomads](https://planet-nomads.com/) | Shipped game built around procedural smooth voxel terrain, caves, traversal, and dynamic LOD. | Looking for developer-reported terrain streaming, LOD, and player-experience problems from a shipped game. | Source is closed; articles and demos can reveal problems and outcomes, not verified internal architecture. |
| [No Man's Sky terrain community analysis](https://github.com/gistya/nomansterrain) | Community reverse-engineering of a shipped procedural planetary-terrain system. | Forming research questions about density fields, caves, runtime generation, or planetary scale. | Community inference is not authoritative and must not be cited as confirmed implementation fact. |
| [No Man's Sky: Worlds Part I](https://www.nomanssky.com/worlds-part-i-update/) | Official Hello Games release notes describing a dual-Marching-Cubes terrain rewrite and reported vertex, loading, frame-rate, and memory improvements. | Confirming the developer's public claims about the current extraction family and optimization outcomes. | Does not disclose scheduling, residency, LOD publication, or benchmark details, so it cannot justify an internal architecture. |
| [Terrain Rendering in Far Cry 5](https://www.gdcvault.com/play/1025261/Terrain-Rendering-in-Far-Cry) | Ubisoft GDC 2018 production talk on GPU terrain traversal, requested versus loaded LOD, culling, stitching, and indirect rendering. | Studying how a shipped open world retains coarse terrain and refines only through available children. | Heightfield-specific data, edge morphing, and proprietary-engine measurements do not transfer to volumetric SDF topology. |
| [Streaming in Sunset Overdrive's Open World](https://www.gdcvault.com/play/1022268/Streaming-in-Sunset-Overdrive-s) | Insomniac GDC 2015 postmortem on high-speed open-world streaming budgets, runtime initialization, allocation, and teleport constraints. | Relating player speed and cell scale to lead time, diagnosing non-I/O bottlenecks, or defining discontinuous-travel behavior. | Streams authored city assets rather than procedural voxel meshes; use its measured failure modes, not its cell layout, as guidance. |
| [Nanite Virtualized Geometry](https://dev.epicgames.com/documentation/unreal-engine/nanite-virtualized-geometry-in-unreal-engine) | Epic documentation for GPU-driven hierarchical cluster selection, fine-grained streaming, and automatic LOD. | Comparing requested-versus-resident hierarchical detail, indirect rendering, and bounded GPU work in a production renderer. | Primarily imported/static clustered geometry with different deformation and topology constraints; not a live voxel-meshing solution. |
| [World Partition](https://dev.epicgames.com/documentation/unreal-engine/world-partition-in-unreal-engine) | Epic documentation for source-driven spatial streaming, priorities, loaded versus activated states, and destination preloading. | Studying multiple streaming sources, directional importance, bounded concurrent loads, or explicit teleport preparation. | Actor/cell streaming semantics are not terrain-mesh ownership and must be adapted rather than copied. |
| [Virtual Texture Memory Pools](https://dev.epicgames.com/documentation/unreal-engine/virtual-texture-memory-pools-in-unreal-engine) | Epic documentation for fixed GPU page pools, working-set fit, eviction, and residency diagnostics. | Researching bounded-cache behavior, thrash, utilization telemetry, and desired-versus-resident detail. | Texture pages are only an analogy for derived mesh residency; they do not define voxel publication or transition topology. |
| Astroneer talks and postmortems | Public observations and developer material about a shipped smooth, editable, procedural planetary world. | Studying player-facing editing, traversal, visual behavior, and production constraints. | No canonical source is cataloged yet and implementation details are closed; add a specific durable link before relying on a claim. |

## Recording a Research Outcome

When a source materially affects a design decision, record the outcome in the
smallest relevant Voxels3 design note:

- the question being answered;
- the fact or technique observed in the source;
- the source's architecture and evidence type;
- what Voxels3 adopts, rejects, or adapts;
- why that choice fits the canonical Voxels3 path and verified s&box constraints;
- measurements still required before making correctness or performance claims.
