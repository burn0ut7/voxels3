# Chunk Performance Findings and Optimization Roadmap

## Purpose

This document records the current evidence and the strongest future paths for
making terrain chunks appear faster without reducing end-user features. It is a
research and prioritization record, not authorization to change the production
allocator, workload, or validation thresholds.

The product goal is shorter time from a visual-placement request to complete,
stable terrain coverage while preserving:

- the selected `VisualChunkRadius` and every enabled visual level;
- full-resolution LOD0 gameplay and near-field rendering;
- the full volumetric SDF, including caves, overhangs, and negative coordinates;
- current cell sizes, region dimensions, materials, normals, and Transvoxel
  topology;
- every complete adjacent 2:1 transition boundary;
- atomic requested-to-staged-to-committed placement with old coverage retained
  until its replacement is ready;
- deterministic geometry, revision rejection, bounded frame work, and queue
  convergence;
- one canonical GPU mesher and no fallback terrain implementation.

Lower view distance, reduced terrain detail, missing caves, delayed seams,
visible holes, hidden queue growth, and relaxed frame budgets are not
optimizations.

## What "Chunk Loading" Means Now

Voxels3 currently has two independent ranges:

1. `GameplayRadius` is the authoritative simulation-interest cube. The current
   unedited chunk payload is only deterministic implicit-SDF identity and
   settings, so this range is represented analytically. A radius of 64 denotes
   `129^3 = 2,146,689` logical coordinates without allocating millions of chunk
   wrappers, queue entries, density arrays, or LOD0 meshes.
2. `VisualChunkRadius` selects the highest level in the fixed-cache visual
   clipbox. This is the terrain view-distance control. Each additional outer
   level adds one ordinary fixed cache and adjacent transition pair, so cost
   grows approximately linearly rather than cubically.

Consequently, the remaining user-visible chunk-load problem is visual mesh
availability: classification, GPU extraction, allocation, publication, and the
atomic hierarchy handoff. Increasing `GameplayRadius` is not a terrain view
distance mechanism and should not enter this visual work path again.

Related design records:

- [Visual clipbox scaling](VisualClipboxScaling.md)
- [GPU voxel terrain streaming](GpuVoxelTerrainStreaming.md)
- [GPU voxel meshing architecture](../Architecture/GpuVoxelMeshing.md)
- [Canonical validation ledger](../ValidationResults.md)

## Current Production Pipeline

For a potentially surface-containing regular region, the production path is:

```text
request and prioritize
        |
        v
GPU density/classification/count/scan
        |
        v
bounded count metadata readback
        |
        v
exact CPU arena-range allocation
        |
        v
GPU vertex/index emission
        |
        v
render-sequence publication
        |
        v
atomic clipbox placement commit
```

Known-empty regular regions use the conservative broadphase and satisfy
residency without geometry. Transitions use the same ownership principles with
their existing level-aware, face-local pipeline. Three regular scratch lanes
and three transition lanes overlap independent batches; a batch contains at
most eight records. Persistent geometry uses shared arenas and indexed-indirect
drawing.

## Findings Already Established

### Removed catastrophic scaling path

The failed radius-64 implementation materialized an inclusive 2,146,689-entry
gameplay set and submitted potential surface coordinates as LOD0 gameplay
geometry. It reached approximately 297,256 queued meshes, 318,837 residents,
178 arenas, about 9 GB of committed geometry capacity, CPU p95/p99 near
`1587/1741 ms`, and approximately 92.5 seconds of scheduling latency.

The analytic gameplay range and bounded visual clipbox removed that failure.
Clean production play now admits quickly, gameplay radius 64 reports all
2,146,689 logical coordinates with no gameplay mesh queue, and visual radii
64/128/256/512 add ordinary fixed-cost levels instead of expanding LOD0.

### Current visual scaling is healthy

Measured expansion settlement was approximately:

| Visual radius | Highest level | Observed settlement |
| ---: | ---: | ---: |
| 64 | 3 | 1.2 s |
| 128 | 4 | 1.5 s |
| 256 | 5 | 1.8 s |
| 512 | 6 | 2.2 s |

At radius 512, all seven levels and six transition pairs settled with zero final
queue, unsafe commit, seam mismatch, invalid-table use, ordinary geometry
readback, or render-time SDF evaluation. Scratch remained exactly three regular
and three transition lanes. This demonstrates that hierarchy breadth is not the
current scaling failure.

### Settled CPU management is not the load bottleneck

The schema-20 percentile run measured the fully settled stationary path at:

| Scope | p95 | p99 |
| --- | ---: | ---: |
| `VoxelManager.OnUpdate` | 0.0089 ms | 0.0145 ms |
| Complete mesher processing | 0.0037 ms | 0.0044 ms |
| Camera-binding refresh | 0 ms | 0 ms |
| Engine render | 0.7730 ms | 0.9809 ms |
| Editor | 0.2295 ms | 1.6859 ms |

A separate 20-second managed trace attributed only 0.02% inclusive sampled
time to `VoxelManager.OnUpdate`. Adding CPU worker threads, micro-optimizing the
settled manager, or rescanning fewer level records therefore has little expected
effect on visual mesh availability.

### Allocation and editor tails are separate concerns

Moving and stationary measurement observed approximately 26-29 KB of managed
allocation per frame across the game/editor thread, with occasional collection
pauses. Collection frames were too rare to explain the repeated one-percentile
tail. The external trace was dominated by worker waits and existing render
submission work, while the editor timing owned much larger p99 spikes than the
settled terrain manager.

This deserves separate engine/editor diagnosis, but it is not evidence that
chunks are still being generated for minutes. It must not be used to justify
reducing terrain features or changing the visual workload.

### The remaining mesh-latency serialization is measurable

The latest unchanged figure-eight recorded:

- foreground schedule-to-renderable p95: `65.7876 ms`;
- outer schedule-to-renderable p95: `235.7818 ms`;
- outer p99: `395.2820 ms`;
- accumulated count-readback wait: approximately `30.659 s` across all
  overlapping batches during the 121.9-second route;
- count-stage CPU submission: approximately `1.702 s` accumulated;
- emit-stage CPU submission: approximately `0.257 s` accumulated;
- post-loop drain: `29.8303 ms`.

The accumulated readback number is the sum of asynchronous batch latencies, not
30 seconds of main-thread blocking. It still identifies the count-to-allocation
rendezvous as the largest remaining latency component between request and
renderable geometry.

## Ranked Optimization Potential

| Rank | Opportunity | Expected chunk-load benefit | Risk and scope |
| ---: | --- | --- | --- |
| 1 | Remove or keep GPU-local the count/readback/allocation rendezvous | Very high | High; changes allocator and publication contracts |
| 2 | Improve existing lane utilization and eliminate measured pipeline bubbles | Medium to high | Medium; must retain bounded frame work and priority guarantees |
| 3 | Tighten exact conservative empty/solid rejection | Workload-dependent medium | Medium; must never reject caves, edits, or surface geometry |
| 4 | Consolidate arena draw/visibility submissions | Low for loading, high for settled rendering | High; changes draw/visibility memory shape |
| 5 | Attribute engine/editor managed allocations | Low direct loading benefit | Low production risk; requires richer diagnostic tooling |

## Highest-Potential Direction: GPU-Local Allocation

The strongest architectural opportunity is to prevent the CPU from sitting in
the middle of every mesh build solely to learn output sizes and assign exact
contiguous ranges. The desired shape is:

```text
GPU classify/count -> GPU-owned bounded allocation -> GPU emit
                                      |
                                      v
                         bounded completion metadata
                                      |
                                      v
                         existing revision/publication gate
```

This is not yet a selected implementation. The installed s&box API exposes the
buffers, counters, indirect operations, and asynchronous readback used by the
current pipeline, but it does not expose a ready-made GPU variable-range
allocator. A feasibility slice must establish a bounded representation that can
be emitted, drawn, freed, and audited through supported APIs.

Serious designs to compare before implementation are:

### GPU page allocator

Divide persistent vertex and index storage into fixed-size pages managed through
GPU-visible free lists or allocation counters. Count and allocation remain on
the GPU; the CPU receives only bounded completion and ownership metadata.

Potential advantages:

- removes the per-batch CPU allocation rendezvous;
- can make count and emit one continuous GPU lifecycle;
- preserves fixed global capacity and asynchronous publication.

Questions that must be resolved:

- how a mesh spanning multiple pages is represented by the existing indexed
  draw API without multiplying draw calls;
- how pages are reclaimed after cancellation, replacement, reset, or stale
  completion;
- how allocation exhaustion fails without overwriting live geometry;
- how generation tokens, audits, and atomic placement readiness remain exact;
- whether required shader atomics and buffer layouts survive clean Vulkan
  startup under the known s&box parser/resource constraints.

### GPU append followed by bounded compaction

Emit a batch into bounded GPU append storage, then compact or copy completed
ranges into persistent draw storage without reading individual counts through
the CPU.

This may preserve contiguous draw ranges more naturally than pages, but it needs
a verified supported GPU copy/indirect path and cannot introduce unbounded
temporary storage or hierarchy-wide compaction. Live ranges must not move unless
the publication model explicitly changes in a separate approved design.

### Coarser CPU allocation granularity

Keep CPU allocator ownership but reserve larger batch slabs or reusable size
classes so several mesh emissions share one allocation decision and count
readback. This is less ambitious and may waste capacity, but it could reduce
callback and allocation frequency while retaining the existing draw format.

The comparison must measure latency, committed and used bytes, fragmentation,
draw submissions, cancellation cleanup, frame tails, and cold-start stability.
The winner must replace the superseded allocation path; production must not keep
two mesh allocators as fallbacks.

## Safest Near-Term Investigation: Pipeline Utilization

Before allocator implementation, use the existing schema-20 production metrics
to determine where lanes are idle while eligible work exists. Measure regular
and transition lanes separately during cold startup, fast movement, reversal,
teleport-like displacement, and radius expansion.

Investigate only evidence-backed changes such as:

- admitting the next count batch sooner when a lane is genuinely idle;
- consuming ready count metadata without avoidable update-boundary delay;
- reducing underfilled batches when enough same-priority work is already queued;
- coalescing callbacks or metadata handling without mixing priority classes;
- ensuring the 250 ms outer anti-starvation policy does not create foreground
  bubbles or let outer work monopolize a lane.

Simply increasing batch size, dispatch budget, or lane count is not the default
answer. It may move more work into one frame, increase scratch memory, worsen
tail latency, or reproduce the prior dedicated-outer-lane Vulkan failure.

## Conservative Work Rejection

The existing broadphase already publishes regions entirely outside possible
surface/cave support as known empty. A tighter classifier could reduce GPU work
if it proves more all-air or all-solid regions cheaply.

Any candidate must:

- operate on the exact full 3D closed region bounds;
- remain conservative under every generator setting and negative coordinate;
- preserve tunnels, caverns, overhangs, future edits, and vertical movement;
- cost substantially less than the GPU work it prevents;
- avoid recursive per-cell proof, which previously ran for seconds without
  yielding;
- return uncertainty as potentially surface-containing.

A heightfield-only test, fixed underground band, or rejection based on the
current camera cannot become terrain authority. This opportunity is secondary
because its value depends strongly on the generator and world region.

## Draw and Visibility Consolidation

The settled managed trace showed generic indexed-indirect drawing and compute
dispatch as meaningful render-thread costs. The current renderer issues one
indexed-indirect submission per active arena and maintains the visibility data
required by those arenas.

Reducing arena submissions or consolidating visibility dispatches could improve
steady frame performance and indirectly leave more budget for loading. It is
not the first chunk-load optimization because settled manager/mesher CPU cost is
already tiny and the measured request-to-renderable latency is earlier in the
pipeline. It also changes allocator, buffer, and draw-command shape, so it
should be evaluated as a separate slice rather than bundled with GPU allocation.

## Rejected Shortcuts

Do not pursue the following as chunk-performance fixes:

- lower `VisualChunkRadius`, fewer LOD levels, or smaller caches;
- reduce LOD0 extent or resolution;
- simplify the SDF, remove caves, clamp vertical coverage, or assume a
  heightfield;
- skip, delay, or hide transition geometry;
- publish incomplete placements and cover holes with a second renderer;
- increase `GameplayRadius` or expand an LOD0 cube to gain view distance;
- build dense CPU sample arrays or a CPU fallback mesher;
- launch one task per chunk or add worker concurrency without profiler evidence;
- increase per-frame budgets until queues disappear while frame tails regress;
- add per-level queues, shaders, allocators, scratch lanes, or special cases;
- accept lower completed work, memory growth, stale publications, or visual
  popping in exchange for a better average frame time.

## Recommended Next Slice

The next optimization slice should be a read-only feasibility and measurement
pass for GPU-local or coarser-grained allocation. It should answer:

1. Which supported s&box buffer, counter, indirect, copy, and synchronization
   operations can keep allocation decisions off the CPU?
2. Can the selected representation preserve contiguous indexed drawing, or can
   a bounded alternative reduce total submissions rather than multiply them?
3. What is the exact worst-case and typical memory overhead per regular region
   and transition face?
4. How are cancellation, supersession, exhaustion, reset, and stale GPU work
   reclaimed without touching live geometry?
5. Can the existing atomic hierarchy handoff consume a bounded completion token
   without a new publication model?
6. Does a clean editor restart compile and run every required shader without
   parser, resource-limit, Vulkan, or device-loss failure?

Only after those questions have one viable design should implementation begin.
If no supported bounded GPU-local design exists, the fallback research priority
is measured utilization of the current three-lane pipeline—not feature removal.

## Acceptance Principles for Any Candidate

Before the first implementation run, append a versioned fixed scenario to
`Docs/ValidationResults.md`, or reuse an existing scenario without changing its
parameters. Compare an unchanged pre-change baseline and candidate through the
production player path.

A candidate is acceptable only when it:

- materially improves cold first-visible, full-default-settlement, foreground
  p95/p99, outer p95/p99, or drain time in the same workload;
- preserves moving and stationary CPU/GPU frame tails within the scenario's
  existing regression tolerance;
- preserves exact level, resident, active, transition, digest, visibility,
  scratch, and atomic-handoff results;
- keeps queues bounded during forward movement, positive/negative boundaries,
  reversal, backtracking, and the supported radius stress tiers;
- produces zero unsafe commit, stale publication, seam mismatch, invalid table,
  ordinary geometry readback, render-time SDF evaluation, shader/compute error,
  device loss, or managed exception;
- demonstrates continuous terrain from the production game and detached
  overhead cameras while work is pending and after settlement;
- does not obtain its improvement by reducing distance, content, geometry,
  correctness, completed work, or player-visible quality.

Average FPS alone is insufficient. The decision must include availability
latency, frame tails, queue depth, memory high-water behavior, cancellation,
visual evidence, and clean-start engine stability.

## Current Priority Decision

The accepted fixed-cache N-level clipbox and analytic gameplay range should
remain unchanged. They solved the cubic radius failure and scale correctly.

The largest remaining potential for general visual chunk-load speed is the
count-readback and exact CPU-allocation rendezvous. Investigate a bounded
GPU-local or coarser-grained allocation design first. Treat draw consolidation,
exact work rejection, and editor allocation attribution as separate follow-up
slices so their benefits and risks remain measurable.
