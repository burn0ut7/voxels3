# Documentation Map

Each subject has one maintained owner. Update that owner when behavior changes;
link to it elsewhere instead of copying its state, settings, or results.

| Question | Owner |
| --- | --- |
| How should an agent work in this repository? | [AGENTS.md](../AGENTS.md): project rules and route selection. |
| What constraints apply to a domain? | [Agent routes](../AGENTS.md#route-map): domain-specific design and validation requirements, not implementation snapshots. |
| How do host debug flight and teleport work? | [Admin menu](Architecture/AdminMenu.md). |
| What owns terrain state, coordinates, generation, and CPU preparation? | [Voxel foundation](Architecture/VoxelChunkFoundation.md). |
| What owns material identity, soil layers and checker appearance? | [Voxel materials](Architecture/VoxelMaterials.md). |
| What owns sea-level water, its medium queries and blue surface? | [Static surface water](Architecture/SurfaceWater.md); [implementation plan](Plans/SeaLevelWaterFirstSlice.md). |
| What owns live terrain edits and their implementation gates? | [Terrain deformation](Architecture/TerrainDeformation.md): in-progress implementation, validation status and remaining scope. |
| How do visual LOD, GPU extraction, allocation, publication, and drawing work? | [GPU meshing](Architecture/GpuVoxelMeshing.md). |
| How is the performance test implemented and invoked? | [Performance overview](Architecture/VoxelChunkFoundation.md#performance-overview). |
| What workload should run, and what actually passed? | [Validation ledger](ValidationResults.md): exact versioned scenarios, measurements, and acceptance decisions. |
| Why was visual scaling designed this way? | [Visual scaling decision](Research/VisualClipboxScaling.md): rationale and alternatives. |
| What did the first terrain-edit profile reveal? | [Deformation profile](Research/TerrainDeformationProfile20260907.md): correction preparation, collision work and candidate optimizations. |
| What performance questions remain worth investigating? | [Performance research](Research/ChunkPerformanceOptimizationFindings.md): evidence-backed questions, not an implementation backlog. |
| What can we simplify across the current project? | [Project simplification review](Research/ProjectSimplificationReview20260907.md): current-state audit, profiler limits, ranked deletion candidates, preservation requirements and acceptance plan. |
| What does the September 7 CPU capture show? | [CPU performance review](Research/CpuPerformanceReview20260907.md): sampled attribution, allocation/memory findings, source-backed hypotheses and measurement limits. |
| Which larger CPU/memory capabilities are worth investigating? | [Performance enhancement directions](Research/PerformanceEnhancementDirections.md): bounded metadata, planning, admission and revisit reuse; separate from local optimizations. |
| What should change in GPU extraction, allocation, or rendering? | [GPU meshing study](Research/GpuMeshingOptimizationStudy.md): source audit, external comparisons, deletion candidates, and measurement gates. |
| How should forests, grasslands, hills and mountains be generated? | [Biome terrain generation](Research/BiomeTerrainGeneration.md): primary-source comparisons, deep-module ownership, landform/climate ordering, compatible biome transitions, bounded population and future feature boundaries; research only. |
| What is the first terrain-generation slice? | [Regional landform plan](Plans/RegionalLandformsFirstSlice.md): replace the exterior generator, retain caves, expose eight global shaping controls, and qualify landforms before climate/biomes; planned, not implemented. |
| What should the first generated water slice do? | [Sea-level water generation](Research/SeaLevelWaterGeneration.md): Minecraft, Luanti and No Man's Sky comparison; static basin filling, cave policy, medium ownership and qualification proposal. |
| How could caves and mountains occlude hidden terrain? | [Terrain occlusion research](Research/TerrainOcclusion.md): no duplicate terrain depth pass, current visibility limits, engine evidence, alternatives and acceptance requirements. |
| Which collision optimizations are being screened now? | [Focused collision experiments](Research/CollisionOptimizationExperiments.md): one-mechanism trials, measurable gates and keep/revert/hold decisions. |
| What did the scan and GPU arena prototypes establish? | [Scan and arena investigation](Research/GpuReductionsAndArenaEfficiency.md): hypotheses, isolated experiments, capacity accounting and acceptance outcomes. |
| What currently owns terrain collision? | [Collision prototype](Architecture/TerrainCollision.md): CPU extraction, physics readiness, lifecycle and unresolved acceptance gates. |
| What is the proposed first terrain collision slice? | [Terrain collision research](Research/TerrainCollisionFirstSlice.md): full-resolution gameplay collision, CPU/GPU separation, engine evidence, and implementation measurement gates; proposal history. |
| How should the second slice add responsive terrain deformation? | [Terrain deformation research](Research/TerrainDeformationSecondSlice.md): current-source audit, regional edited state, brush behavior, local rebuilds, multiplayer/distant edits and a dedicated benchmark proposal. |
| Which external sources can answer a research question? | [Research catalog](smooth_procedural_voxel_terrain_resources.md): source descriptions and transfer limits. |
| How should mining scraps, dirt stability, and cave-ins behave? | [Terrain stability research](Research/TerrainStabilityAndCollapse.md): source-backed game comparisons, bounded connectivity and support rules, debris policy, and performance gates; proposal only. |
| How should expensive generated terrain be saved and streamed in multiplayer? | [Chunk streaming and storage research](Research/ChunkStreamingStorage.md): server authority, regional persistence, loading/unloading, coherent joins, LOD data and implementation gates. |
| Which loading optimization helped? | [Streaming optimization results](Research/TerrainStreamingOptimizationResults.md): tighter bounds, rejected scheduling candidates, startup/moving gains and qualification limits. |
| What limits the 26-second startup? | [Terrain startup timing breakdown](Research/TerrainStartupBreakdown.md): repeated phase milestones, seam readback latency and scheduling evidence. |
| Does saving generated density improve loading? | [Generated terrain cache experiment](Research/GeneratedTerrainCacheExperiment.md): rejected and removed prototype; cold/reopen/resident comparisons, failed performance results and limits. |
| What is the first authoritative chunk storage prototype? | [Storage prototype plan](Architecture/ChunkAuthoritativeStorage.md): existing live deformation, persisted regional history, real eviction/reload and staged acceptance. |

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

- [Collision profile review: September 7, 07:22:47](Research/CollisionProfile20260907.md) - supplied capture, measured opportunities, and candidate14 results.
