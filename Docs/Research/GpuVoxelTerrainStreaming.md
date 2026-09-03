# GPU Voxel Terrain Streaming Research

## Decision

Voxels3 should use one availability-driven clipbox refinement system. The authoritative SDF, GPU mesher, transition topology, allocator, scheduler, and publication protocol remain single-owner production paths.

The system should distinguish desired placement from resident drawable placement. Coarse coverage remains resident, finer work is requested around the player, and a spatial refinement is published only when all of its child meshes and required 2:1 transition meshes are available. The coarse parent is retired in the same publication transaction. Under load, detail may lag; ground coverage may not.

This replaces the current whole-placement readiness barrier as an intended design direction. It does not authorize a second fallback terrain renderer, CPU mesher, or duplicate LOD system.

## Evidence

- [Geometry Clipmaps](https://hhoppe.com/geomclipmap.pdf) keeps nested viewer-centered levels and updates newly exposed regions incrementally. When update bandwidth is insufficient, fine active coverage can be cropped while coarser coverage remains valid.
- [GPU-Based Geometry Clipmaps](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry) demonstrates toroidal L-shaped refresh and coarse-to-fine updates. Its heightfield topology does not transfer directly to a volumetric SDF.
- Ubisoft's [*Far Cry 5* terrain talk](https://www.gdcvault.com/play/1025261/Terrain-Rendering-in-Far-Cry) separates requested and loaded terrain state. GPU traversal begins at an always-resident low LOD and descends only when children are available. Its GPU LOD/culling model is relevant; heightfield edge morphing is not a replacement for Transvoxel.
- Insomniac's [*Sunset Overdrive* streaming postmortem](https://www.gdcvault.com/play/1022268/Streaming-in-Sunset-Overdrive-s) derives load constraints from player speed and cell scale, identifies runtime initialization as a bottleneck, and treats discontinuous teleports as an explicit product/loading case.
- Godot Voxel documents [priority and time-budgeted work](https://voxel-tools.readthedocs.io/en/latest/performance/) and [conservative SDF range analysis](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/) that skips provably empty or solid blocks. Its CPU-oriented architecture is not adopted.
- [GPU Gems 3 procedural terrain](https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-1-generating-complex-procedural-terrains-using-gpu) remembers empty blocks without geometry allocation and manages visible generated blocks through a bounded pool.
- [Transvoxel](https://transvoxel.org/) remains the authority for local 2:1 volumetric transition topology. It does not define a streaming scheduler or temporal publication contract.
- [DirectX Sampler Feedback](https://microsoft.github.io/DirectX-Specs/d3d/SamplerFeedback.html), [Unreal virtual-texture pools](https://dev.epicgames.com/documentation/unreal-engine/virtual-texture-memory-pools-in-unreal-engine), and [Nanite](https://dev.epicgames.com/documentation/unreal-engine/nanite-virtualized-geometry-in-unreal-engine) corroborate requested-versus-resident selection backed by bounded resources. These systems are analogies, not voxel-meshing implementations.

## Repository diagnosis

The atomic placement candidate fixed the temporal hole failure: no active mesh was published without its replacement dependencies. It nevertheless waits for every entering LOD1, LOD2, and transition resource before committing the full target. During the fixed figure-eight workload, targets are superseded while large stages remain in flight, so correct terrain falls behind the player.

The candidate also failed frame-tail acceptance relative to the accepted baseline:

| Metric | Candidate | Baseline | Delta |
| --- | ---: | ---: | ---: |
| Moving CPU p99 | 2.5284 ms | 2.2072 ms | +0.3212 ms |
| Moving GPU p99 | 1.8382 ms | 1.5287 ms | +0.3095 ms |
| Stationary GPU p99 | 1.3294 ms | 0.9460 ms | +0.3834 ms |

Placement telemetry reported 571 requests, 473 commits, and 318 superseded targets. Individual p95 mesh latencies were 101.3 ms for regular work, 215.3 ms for transitions, and 382.0 ms for LOD2, but those metrics do not measure target-to-coverage completion under backlog.

Arena diagnostics found that the first thirteen arenas exhausted all 256 region slots while using only about 16–19% of vertex capacity and 30–36% of index capacity. Descriptor capacity, not geometry bytes, is driving allocation growth. A scheduler stall of about 537 ms under a large backlog confirms that isolated per-mesh latency gates are insufficient.

## Canonical design

### Availability graph

`VoxelManager` owns desired clipbox placement and resident drawable availability. A coarse region may be drawable while its finer replacement is requested or partially generated. A refinement becomes drawable only when its complete child set and required transition are resident. Publication atomically exposes the replacement and hides the covered parent.

The publication unit should be a dependency-complete region or slab, selected by measurement. It must be smaller than a whole placement but large enough to avoid excessive metadata and indirect-draw overhead.

### One scheduler

All meshing work should flow through one bounded deadline-aware scheduler. Priority inputs are coverage dependency, time-to-player, distance, directional lead, whether the resource unblocks publication, request age, and revision validity. Collision and coarse safety work outrank transition dependencies that unblock a ready refinement, which outrank fine visible detail and warm prefetch.

These are priority classes inside one system, not parallel FIFO implementations. Obsolete queued work is canceled before dispatch; in-flight work may finish but cannot publish when its revision is stale.

### Conservative surface rejection

The first optimization slice should add an SDF-owned conservative region bound. A region whose density interval cannot cross the isosurface is recorded as air or solid without entering GPU extraction or consuming geometry storage. The calculation must reuse canonical procedural bounds and participate in normal edit invalidation. A duplicated height/noise approximation is forbidden because a false rejection would produce holes.

### Motion and lead

The desired window moves incrementally with snap hysteresis. Streaming priority combines a symmetric safety bubble with velocity/view lookahead so hard turns remain safe. Required lead is derived from measured publication latency:

`lead distance >= supported speed * (p99 target-to-coverage time + safety margin)`

Teleportation or an editor camera jump uses the same streamer with an explicit destination coarse-ready gate or visible loading state. Arbitrary instantaneous travel is not treated as ordinary movement.

### GPU pipeline and allocation

The installed s&box 26.09.01a API exposes structured/append GPU buffers, counter reset and structure-count copy, indirect compute dispatch, and indexed indirect drawing. A future scan/append/indirect extraction path is feasible to investigate, but API availability is not proof of a safe production design. Previous compaction/device-loss evidence means this work follows scheduling and surface rejection.

Shared arenas remain canonical. Record-slot capacity and geometry-byte capacity must be measured independently and rebalanced without adding a second allocator or mesh representation.

## Adopt, adapt, reject

| Idea | Disposition | Voxels3 use |
| --- | --- | --- |
| Resident coarse coverage with availability-driven refinement | Adopt | Core publication contract. |
| Incremental clipbox movement and coarse-to-fine updates | Adapt | Fixed three volumetric LODs and Transvoxel boundaries. |
| Priority/time-budgeted loading | Adopt | One scheduler using deadlines, dependencies, and aging. |
| Conservative SDF range rejection | Adopt | Implement in the authoritative SDF owner. |
| GPU hierarchy/culling/indirect patterns | Adapt | Preserve current s&box-safe ownership; measure each step. |
| Whole-placement atomic readiness | Reject | Slowest dependency blocks the complete target. |
| Separate fallback heightfield or CPU mesher | Reject | Creates competing terrain truth and duplicated features. |
| GigaVoxels-style raycast renderer | Reject for this slice | Useful feedback/budget evidence, incompatible renderer architecture. |
| Nanite as a direct solution | Reject | Static/imported clustered geometry is not live SDF extraction. |
| Blindly raising dispatch budget | Reject | Can exchange streaming lag for frame-tail stutter. |
| Generalized N-level hierarchy | Reject for this slice | Fixed three-level scope is sufficient. |

## Required implementation measurements

Keep the fixed figure-eight v1 workload unchanged. Add:

- p99 target-to-first-coarse-coverage and target-to-refinement latency;
- maximum target lag in chunks and seconds;
- deadline misses and worst lateness;
- frames/world area using coarse fallback;
- obsolete requests canceled before dispatch;
- potential surface candidates versus guaranteed-air/solid rejections;
- publication attempts and dependency-blocked publication counts;
- record-slot and vertex/index byte utilization per arena;
- queue/candidate/indirect buffer overflows;
- existing CPU/GPU frame percentiles, allocation, memory, digest, and watertightness gates.

The next implementation order is instrumentation, conservative surface rejection, regional availability publication, unified deadline scheduling, allocator record rebalancing, and only then a measured GPU compaction prototype if readback remains a bottleneck.
