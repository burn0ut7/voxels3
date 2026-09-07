# Terrain Performance Research

Research context: 2026-09-03 measurements, reviewed against the documentation
owners on 2026-09-06. This is a record of investigation questions and alternatives,
not a selected implementation, approved backlog, or performance-acceptance claim.

[GPU meshing](../Architecture/GpuVoxelMeshing.md) owns the current pipeline and
publication contract. [Voxel foundation](../Architecture/VoxelChunkFoundation.md)
owns terrain state and CPU preparation. The [validation ledger](../ValidationResults.md)
owns measurements and decisions. The [research catalog](../smooth_procedural_voxel_terrain_resources.md)
owns external source descriptions and transfer limits.

## Evidence to Start From

- [Visual stress and loading evidence](../ValidationResults.md#terrain-visual-stress-001v1---256-goal-and-512-stretch)
  traces the eager gameplay-allocation failure and the replacement's expansion,
  contraction, geometry, queue, and frame measurements. The
  [visual scaling decision](VisualClipboxScaling.md) retains its design rationale.
- [Settled-frame attribution](../ValidationResults.md#settled-frame-attribution-and-camera-binding-cadence---2026-09-03)
  separates terrain manager/mesher cost from engine/editor tails and records the
  remaining strict performance failures. Functional scaling evidence must not be
  described as full acceptance.
- The schema-20 evidence records asynchronous count-readback latency and stage
  timings. Summed overlapping batch waits are not main-thread blocking time and
  do not alone prove the critical path or predict an optimization's benefit.
- The earlier atomic-handoff results remain under
  [CLIPBOX-LOD-HANDOFF-001](../ValidationResults.md#clipbox-lod-handoff-001v1---atomic-moving-placement-coverage).
  Use the exact source state and subsequent decisions when interpreting them;
  a failed early candidate is not proof that the current design must be replaced.

## Allocation and Pipeline Utilization

The count-to-CPU-allocation dependency is worth investigating, but its benefit
must be measured against total request-to-renderable and request-to-coverage
latency. First identify idle lanes with eligible work, underfilled batches,
callback-to-consumption delay, foreground/outer fairness, and which dependencies
actually delay placement. Use production telemetry before adding concurrency or
raising dispatch limits.

If evidence supports changing allocation, compare these alternatives:

| Alternative | Potential benefit | Main unanswered questions |
| --- | --- | --- |
| Bounded GPU page allocation | Keeps sizing/allocation near extraction. | Indexed draw representation across pages, fragmentation, exhaustion, reclamation, and generation-safe reuse. |
| GPU append followed by bounded compaction/copy | May preserve contiguous draw ranges with fewer CPU rendezvous. | Supported engine operations, temporary capacity, synchronization, and prior Vulkan failure reproduction. |
| Coarser CPU batch reservations or size classes | Reduces allocation/readback frequency while retaining CPU ownership. | Wasted capacity, cancellation granularity, latency, and draw overhead. |
| Better utilization of existing lanes | May remove scheduling bubbles without allocator changes. | Whether measured bubbles are avoidable without hurting priority, fairness, or frame tails. |

Engine API availability is not proof that a proposed allocator is safe. Any
selected design must establish bounded memory, stale/canceled-work cleanup,
publication identity, shader startup stability, and supported draw operations.
The production allocator remains unchanged until a replacement passes its
measured design and validation gates.

## Publication and Scheduling Alternatives

The removed `GpuVoxelTerrainStreaming.md` study proposed regional
availability-driven refinement and deadline-based scheduling. Those proposals
were not the implemented whole-placement contract. Its useful open questions
are retained here; historical prose is available in Git.

- Could a dependency-complete region or slab publish earlier than a whole
  placement while retaining coarse coverage and every required Transvoxel face?
  Establish the exact ownership and transition dependencies before choosing a
  smaller publication unit. This would replace the current contract, not add a
  fallback renderer beside it.
- Could dependency, age, time-to-player, and directional lead improve the
  existing bounded scheduler? Measure target-to-first-coverage, full refinement,
  maximum lag, deadline misses, and obsolete work. Current distance/priority
  behavior must not be described as an implemented deadline scheduler.
- Relate lead distance to supported player speed and measured tail coverage
  latency, including a safety margin. Reversals and sudden destination changes
  need explicit workload and product semantics; arbitrary teleports are not
  evidence about ordinary movement.

Geometry Clipmaps and GPU-Based Geometry Clipmaps inform incremental cache
updates; Far Cry 5 informs requested-versus-resident refinement; Sunset Overdrive
informs movement budgets and teleport constraints. These sources are routed in
[the catalog](../smooth_procedural_voxel_terrain_resources.md#research-question-router).
Their heightfields or authored assets do not define volumetric transition or
publication correctness. Transvoxel supplies local topology, not scheduling.

The old study's fixed-three-level restriction is superseded by the implemented
level-indexed hierarchy. Conservative rejection and arena record sizing now have
production owners in the architecture document; they are not pending tasks just
because the earlier study proposed them.

## Other Bounded Questions

- **Conservative rejection:** can a cheaper full-3D proof reject enough additional
  solid/air work to outweigh its own cost? Preserve uncertainty and caves; avoid
  the rejected recursive per-cell proof and duplicate height/noise approximations.
- **Draw/visibility submission:** does reducing per-arena submission work improve
  measured rendering cost? Evaluate its buffer, allocation, and publication
  implications separately from mesh-availability changes.
- **Engine/editor allocation:** which allocation stacks own the measured bytes
  and pauses? The historical trace did not identify those payload types. A low
  settled-manager cost does not rule out every moving-terrain interaction.

## Choosing a Follow-Up

Choose one question from fresh comparable evidence and record a concrete design
before implementation. Preserve the requested terrain quality, full 3D field,
transitions, coverage, bounded queues, memory, and correctness. A lower workload
is not an equivalent optimization.

Use the existing [figure-eight acceptance rule](../../AGENTS.md#figure-eight-performance-acceptance)
and [performance route](../AgentRoutes/performance-and-testing.md). Put any
additional scenario criteria and measured decision in the ledger rather than
creating a second acceptance checklist here.
