# Visual Clipbox Scaling Research

## Decision

Terrain view distance should scale by enabling another ordinary fixed-cache
clipbox level, never by expanding the LOD0 or gameplay cube. The default remains
levels 0 through 2. `VisualChunkRadius` selects the supported maximum level at
tiers `4/16/32/64/128/256/512`, while `GameplayRadius` remains an independent
full-3D simulation-interest setting.

The current gameplay chunk has no stored samples, edits, collision, persistence,
or other mutable payload. Its authoritative content is the deterministic
implicit SDF plus coordinate. The gameplay range is therefore an analytic cube,
and a `VoxelChunk` view is created on demand. Materializing millions of identical
wrappers or submitting them all to the visual mesher adds no world state and is
not gameplay streaming.

## Primary evidence

- Losasso and Hoppe's [Geometry Clipmaps paper](https://hhoppe.com/geomclipmap.pdf)
  bounds terrain cost with nested power-of-two grids, snapped placement, and
  updates only to exposed regions. Its heightfield topology does not represent
  arbitrary caves, so Voxels3 retains volumetric extraction and Transvoxel.
- NVIDIA's [GPU-Based Geometry Clipmaps](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry)
  reinforces persistent reusable level storage and incremental coarse-to-fine
  updates. Voxels3 adapts the cache discipline, not its 2D ring mesh.
- NVIDIA's [GPU procedural terrain chapter](https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-1-generating-complex-procedural-terrains-using-gpu)
  uses 32-cubed blocks, bounded reusable geometry buffers, nearest-first work,
  farthest eviction, and cached empty results. Those ownership principles fit
  the existing single mesher and known-empty broadphase.
- Godot Voxel documents its [clipbox LOD model](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/)
  and [time-budgeted performance controls](https://voxel-tools.readthedocs.io/en/latest/performance/).
  Its concentric-box differences and warnings about task backlog are relevant;
  its CPU task graph is not an implementation template for this GPU-first path.
- Voxel Plugin's official [world-size and LOD documentation](https://docs.voxelplugin.com/1.2/core-systems/voxelworld/world-size-and-level-of-details)
  and [profiling guidance](https://docs.voxelplugin.com/1.2/technical-notes/performance-and-profiling)
  support power-of-two LODs, transition geometry, explicit viewer/invoker policy,
  and bounded priority work. Unreal-specific ownership does not transfer.
- Ubisoft's [Far Cry 5 terrain rendering talk](https://www.gdcvault.com/play/1025480/Terrain-Rendering-in-Far-Cry)
  is evidence for separating requested and resident terrain and retaining coarse
  coverage while detail arrives. Its heightfield culling and stitching do not
  replace the volumetric seam system.
- The author-maintained [GigaVoxels publication page](https://www.icare3d.org/research-cat/publications/gigavoxels-ray-guided-streaming-for-efficient-and-detailed-voxel-rendering.html)
  supports bounded, view-guided multiresolution residency. Its sparse-volume ray
  caster is rejected as a renderer architecture for live polygonized SDF terrain.

## Repository diagnosis

The failing radius-64 session created an inclusive `129^3 = 2,146,689`
gameplay-coordinate set, one wrapper per coordinate, then scheduled every
potential surface coordinate as LOD0 gameplay geometry. It reached about
`297,256` gameplay mesh requests, `318,837` residents, `178` arenas, roughly
`9 GB` of committed vertex/index capacity, CPU p95/p99 of
`1587/1741 ms`, and schedule latency around `92.5 s`. Later Vulkan fence and
present timeouts were a consequence of this unbounded work. The visual clipbox
itself remained fixed and was not the source of the millions of requests.

The replacement keeps the exact simulation range but represents it in constant
space. Only the bounded LOD0 visual warm shell and committed/staged LOD0
coverage enter classification and meshing. Each outer tier adds one fixed
`16^3` cache, one ordinary adjacent transition pair, and linear work through the
existing scheduler, lanes, allocator, visibility, and atomic publication paths.

## Gap and disposition matrix

| Candidate | User-visible reach | Work/memory bound | Full 3D SDF and caves | Disposition |
| --- | --- | --- | --- | --- |
| Expand gameplay or LOD0 cube | Yes | Cubic and unbounded | Yes | Reject; caused the measured failure. |
| Separate heightfield distance renderer | Yes | Bounded | No | Reject; loses caves/material/topology parity and creates competing terrain truth. |
| Recursive octree or new sparse renderer | Yes | Potentially bounded | Yes | Reject for this slice; replaces ownership, publication, and rendering architecture. |
| Fixed-cache N-level clipbox | Yes | Roughly linear per tier | Yes | Adopt; reuses the proven volumetric and Transvoxel paths. |
| Analytic implicit-SDF gameplay cube | No visual policy | Constant until real state exists | Yes | Adopt for the current payload; instantiate on demand. |
| Underground occlusion hierarchy | Potentially | Unknown until measured | Must remain 3D | Defer to an isolated future slice. |

## Measured scaling

Production figure-eight runs with `GameplayRadius=64` settled radius `256` at
`21,190` total residents and `13` arenas, then radius `512` at `25,286`
residents and `14` arenas. Both retained exactly three regular scratch lanes and
`34,310,016/3,884,628` regular/transition scratch bytes. Radius 256 moving
CPU/GPU p95/p99 was `1.396/3.020` and `1.150/1.512 ms`; radius 512 was
`1.452/3.179` and `1.189/1.559 ms`. Every queue drained, all adjacent pairs were
ready, and unsafe publication, seam mismatch, invalid-table use, ordinary
geometry readback, and render-time SDF evaluation remained zero. Exact run IDs
and complete metrics are in `Docs/ValidationResults.md`.

## Full-3D boundary

The outer levels remain 3D volumetric caches. Conservative SDF range analysis
already avoids meshing regions proven entirely air or solid, including much of
the underground volume, but the renderer does not yet perform occlusion-based
rejection of hidden cave surfaces. A future underground-culling slice must
measure visibility and preserve edits, caves, overhangs, and the same SDF and
transition topology. It cannot assume a heightfield, flatten vertical interest,
or become a prerequisite for the view-distance tiers accepted here.
