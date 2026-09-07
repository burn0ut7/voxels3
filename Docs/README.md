# Documentation Map

Each subject has one maintained owner. Update that owner when behavior changes;
link to it elsewhere instead of copying its state, settings, or results.

| Question | Owner |
| --- | --- |
| How should an agent work in this repository? | [AGENTS.md](../AGENTS.md): project rules and route selection. |
| What constraints apply to a domain? | [Agent routes](../AGENTS.md#route-map): domain-specific design and validation requirements, not implementation snapshots. |
| What owns terrain state, coordinates, generation, and CPU preparation? | [Voxel foundation](Architecture/VoxelChunkFoundation.md). |
| How do visual LOD, GPU extraction, allocation, publication, and drawing work? | [GPU meshing](Architecture/GpuVoxelMeshing.md). |
| How is the performance test implemented and invoked? | [Performance overview](Architecture/VoxelChunkFoundation.md#performance-overview). |
| What workload should run, and what actually passed? | [Validation ledger](ValidationResults.md): exact versioned scenarios, measurements, and acceptance decisions. |
| Why was visual scaling designed this way? | [Visual scaling decision](Research/VisualClipboxScaling.md): rationale and alternatives. |
| What performance questions remain worth investigating? | [Performance research](Research/ChunkPerformanceOptimizationFindings.md): evidence-backed questions, not an implementation backlog. |
| What does the September 7 CPU capture show? | [CPU performance review](Research/CpuPerformanceReview20260907.md): sampled attribution, allocation/memory findings, source-backed hypotheses and measurement limits. |
| Which larger CPU/memory capabilities are worth investigating? | [Performance enhancement directions](Research/PerformanceEnhancementDirections.md): bounded metadata, planning, admission and revisit reuse; separate from local optimizations. |
| What should change in GPU extraction, allocation, or rendering? | [GPU meshing study](Research/GpuMeshingOptimizationStudy.md): source audit, external comparisons, deletion candidates, and measurement gates. |
| What did the scan and GPU arena prototypes establish? | [Scan and arena investigation](Research/GpuReductionsAndArenaEfficiency.md): hypotheses, isolated experiments, capacity accounting and acceptance outcomes. |
| What currently owns terrain collision? | [Collision prototype](Architecture/TerrainCollision.md): CPU extraction, physics readiness, lifecycle and unresolved acceptance gates. |
| What is the proposed first terrain collision slice? | [Terrain collision research](Research/TerrainCollisionFirstSlice.md): full-resolution gameplay collision, CPU/GPU separation, engine evidence, and implementation measurement gates; proposal history. |
| Which external sources can answer a research question? | [Research catalog](smooth_procedural_voxel_terrain_resources.md): source descriptions and transfer limits. |

Source code and authored configuration establish actual behavior. Architecture
documents explain its ownership and rationale; they do not prove runtime
correctness or performance acceptance. Read the ledger's explicit decision for
the relevant source state and scenario version before choosing a baseline.

## Validation Navigation

The ledger preserves old implementations, failed experiments, and superseded
scenarios as historical evidence. They are not current instructions. Start with
the relevant scenario family and follow its later runs and decisions:

- [Default hierarchy](ValidationResults.md#clipbox-nlevel-default-001v1---generic-hierarchy-equivalence)
- [Runtime configuration](ValidationResults.md#clipbox-nlevel-config-001v1---validated-runtime-configuration)
- [Visual distance](ValidationResults.md#clipbox-view-distance-001v1---visual-chunk-radius-and-lod3)
- [Gameplay-independent loading](ValidationResults.md#terrain-overhead-load-001v1---gameplay-independent-visual-bootstrap)
- [Visual stress and subsequent default repeats](ValidationResults.md#terrain-visual-stress-001v1---256-goal-and-512-stretch)

## Maintenance

- Keep current contracts in architecture, exact executable values in their
  source owner, and scenario-specific values and results in the ledger.
- Keep research conclusions dated and distinguish implemented decisions from
  proposals. Reassess old hypotheses before treating them as the next task.
- Keep engine failure evidence with the affected subsystem, including the
  observed engine version. The s&box skill owns general engine lookup/tooling.
- Remove obsolete instructions and duplicate descriptions. Preserve useful
  decision rationale and append-only validation history; Git retains removed
  documentation when historical investigation needs it.
- Add a document only for a distinct responsibility that cannot fit an existing
  owner. Update incoming links when moving or removing one.

- [Collision profile review: September 7, 07:22:47](Research/CollisionProfile20260907.md) — supplied capture, measured opportunities, and candidate14 results.
