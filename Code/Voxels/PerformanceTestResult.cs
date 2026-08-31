internal sealed class PerformanceTestResult
{
	public int SchemaVersion { get; init; }
	public string RunId { get; init; }
	public string CapturedAtUtc { get; init; }
	public string Outcome { get; init; }
	public PerformanceTestSource Source { get; init; }
	public PerformanceTestDefinition Test { get; init; }
	public PerformanceWorldContext World { get; init; }
	public PerformanceFrameMetrics Frame { get; init; }
	public PerformanceStationaryMetrics Stationary { get; init; }
	public PerformanceMemoryMetrics Memory { get; init; }
	public PerformanceChunkMetrics Chunks { get; init; }
	public PerformanceMeshingMetrics Meshing { get; init; }
	public PerformanceVisibilityMetrics Visibility { get; init; }
	public PerformanceSubmissionMetrics Submission { get; init; }
	public PerformanceStreamingMetrics Streaming { get; init; }
	public PerformanceBoundsMetrics Bounds { get; init; }
	public PerformanceProfilerMetrics Profiler { get; init; }
	public PerformanceClipboxMetrics Clipbox { get; init; }
	public PerformanceTransitionMetrics Transitions { get; init; }
}

internal sealed class PerformanceTransitionMetrics
{
	public int Desired { get; init; }
	public int Ready { get; init; }
	public int Drawable { get; init; }
	public int Pending { get; init; }
	public int LastEntered { get; init; }
	public int LastLeft { get; init; }
	public int LastRetained { get; init; }
	public long Entered { get; init; }
	public long Left { get; init; }
	public long Scheduled { get; init; }
	public long Published { get; init; }
	public long Cancelled { get; init; }
	public long Stale { get; init; }
	public long ActiveCells { get; init; }
	public long Vertices { get; init; }
	public long Indices { get; init; }
	public long Triangles { get; init; }
	public long UsedVertexBytes { get; init; }
	public long UsedIndexBytes { get; init; }
	public long TransientScratchBytes { get; init; }
	public string TopologyDigest { get; init; }
	public string PositionDigest { get; init; }
	public uint FineFaceMismatchCount { get; init; }
	public uint CoarseFaceMismatchCount { get; init; }
	public uint LateralEdgeDigest { get; init; }
	public uint LateralMismatchCount { get; init; }
	public uint InvalidTableCount { get; init; }
	public IReadOnlyList<PerformanceTransitionFaceMetrics> Faces { get; init; } =
		System.Array.Empty<PerformanceTransitionFaceMetrics>();
	public PerformanceDistributionMetrics ScheduleToPublication { get; init; }
}

internal sealed class PerformanceTransitionFaceMetrics
{
	public PerformanceVector3Int Lod1Coordinate { get; init; }
	public string Face { get; init; }
	public uint Generation { get; init; }
	public int Arena { get; init; }
	public int Slot { get; init; }
	public int VertexOffset { get; init; }
	public int VertexCount { get; init; }
	public int IndexOffset { get; init; }
	public int IndexCount { get; init; }
	public uint ActiveCells { get; init; }
	public float ScheduleToPublicationMilliseconds { get; init; }
	public string TopologyDigest { get; init; }
	public string PositionDigest { get; init; }
	public uint FineFaceMismatchCount { get; init; }
	public uint CoarseFaceMismatchCount { get; init; }
	public uint MinimumUDigest { get; init; }
	public uint MaximumUDigest { get; init; }
	public uint MinimumVDigest { get; init; }
	public uint MaximumVDigest { get; init; }
	public uint InvalidTableCount { get; init; }
}

internal sealed class PerformanceClipboxMetrics
{
	public int Lod0GameplayRadius { get; init; }
	public int Lod0GameplayCoordinates { get; init; }
	public int Lod0ActiveCoordinates { get; init; }
	public PerformanceVector3Int Lod1Anchor { get; init; }
	public PerformanceVector3Int Lod1OuterMinimum { get; init; }
	public PerformanceVector3Int Lod1OuterMaximum { get; init; }
	public PerformanceVector3Int Lod1HoleMinimum { get; init; }
	public PerformanceVector3Int Lod1HoleMaximum { get; init; }
	public int Lod1CachedCoordinates { get; init; }
	public int Lod1ActiveCoordinates { get; init; }
	public int Lod1Pending { get; init; }
	public int Lod1Resident { get; init; }
	public int FullUpdates { get; init; }
	public int IncrementalUpdates { get; init; }
	public long EnteredRegions { get; init; }
	public long LeftRegions { get; init; }
	public int LastEnteredRegions { get; init; }
	public int LastLeftRegions { get; init; }
}

internal sealed class PerformanceBoundsMetrics
{
	public int GameplayQueries { get; set; }
	public int GameplayDefinitelySolid { get; set; }
	public int GameplayDefinitelyAir { get; set; }
	public int GameplayPotentiallySurfaceContaining { get; set; }
	public int WarmQueries { get; set; }
	public int WarmDefinitelySolid { get; set; }
	public int WarmDefinitelyAir { get; set; }
	public int WarmPotentiallySurfaceContaining { get; set; }
	public float TotalCpuMilliseconds { get; set; }
	public float MaximumQueryMilliseconds { get; set; }
	public int StaleOrCancelledQueries { get; set; }
}

internal sealed class PerformanceSubmissionMetrics
{
	public float AverageTerrainIndirectApiSubmissionsPerFrame { get; init; }
	public int MaximumTerrainIndirectApiSubmissionsPerFrame { get; init; }
	public float AverageIndirectArgumentRecordsPerFrame { get; init; }
	public int MaximumIndirectArgumentRecordsPerFrame { get; init; }
	public float AverageTerrainBufferGroups { get; init; }
	public int MaximumTerrainBufferGroups { get; init; }
}

internal sealed class PerformanceProfilerMetrics
{
	public int WindowFrames { get; init; }
	public IReadOnlyList<PerformanceProfilerTiming> Engine { get; init; } =
		System.Array.Empty<PerformanceProfilerTiming>();
	public IReadOnlyList<PerformanceProfilerTiming> Scripts { get; init; } =
		System.Array.Empty<PerformanceProfilerTiming>();
}

internal sealed class PerformanceProfilerTiming
{
	public string Name { get; init; }
	public int Calls { get; init; }
	public float MinimumMillisecondsPerFrame { get; init; }
	public float AverageMillisecondsPerFrame { get; init; }
	public float MaximumMillisecondsPerFrame { get; init; }
}

internal sealed class PerformanceStreamingMetrics
{
	public int FullUpdates { get; set; }
	public int IncrementalUpdates { get; set; }
	public float TotalSynchronousMilliseconds { get; set; }
	public float MaximumSynchronousMilliseconds { get; set; }
	public float TotalDesiredUpdateMilliseconds { get; set; }
	public float TotalPrioritizationMilliseconds { get; set; }
	public float TotalDrawCommitMilliseconds { get; set; }
	public int DrawRebuilds { get; set; }
	public long GameplayCoordinatesTouched { get; set; }
	public long RenderCoordinatesTouched { get; set; }
	public int GenerationBatches { get; set; }
	public int MaximumGenerationBatchSize { get; set; }
	public float MaximumFirstGameplayBatchMilliseconds { get; set; }
	public int WarmCoordinatesClassified { get; set; }
	public int WarmRejectedSolid { get; set; }
	public int WarmRejectedAir { get; set; }
	public int WarmPotentiallySurfaceContaining { get; set; }
	public int WarmTransientChunksConstructed { get; set; }
	public int PeakGameplayMeshBacklog { get; set; }
	public int PeakWarmMeshBacklog { get; set; }
}

internal sealed class PerformanceTestSource
{
	public string Task { get; init; }
	public string Revision { get; init; }
}

internal sealed class PerformanceTestDefinition
{
	public string Name { get; init; }
	public int CompletedLoops { get; init; }
	public float Speed { get; init; }
	public float Distance { get; init; }
	public float WorldHeight { get; init; }
	public float DurationSeconds { get; init; }
	public PerformanceVector2 StartCenter { get; init; }
}

internal sealed class PerformanceWorldContext
{
	public string Scene { get; init; }
	public int CellsPerAxis { get; init; }
	public float CellSize { get; init; }
	public int LoadRadius { get; init; }
	public string Generator { get; init; }
	public int WorldSeed { get; init; }
	public int GeneratorVersion { get; init; }
	public float SurfaceBaseHeight { get; init; }
	public float SurfaceFrequency { get; init; }
	public float SurfaceAmplitude { get; init; }
	public PerformanceVector3Int StreamingCenter { get; init; }
	public PerformanceVector3 TargetPosition { get; init; }
}

internal sealed class PerformanceFrameMetrics
{
	public int Samples { get; init; }
	public int TruncatedSamples { get; init; }
	public float AverageFps { get; init; }
	public float P95Milliseconds { get; init; }
	public float P99Milliseconds { get; init; }
	public float AverageGpuMilliseconds { get; init; }
	public float P95GpuMilliseconds { get; init; }
	public float P99GpuMilliseconds { get; init; }
	public float MaximumGpuMilliseconds { get; init; }
}

internal sealed class PerformanceStationaryMetrics
{
	public float DurationSeconds { get; init; }
	public PerformanceFrameMetrics Frame { get; init; }
	public PerformanceMemoryMetrics Memory { get; init; }
	public PerformanceVisibilityMetrics Visibility { get; init; }
}

internal sealed class PerformanceMemoryMetrics
{
	public ulong StartProcessBytes { get; init; }
	public ulong EndProcessBytes { get; init; }
	public ulong AverageProcessBytes { get; init; }
	public ulong PeakProcessBytes { get; init; }
	public ulong StartGpuBytes { get; init; }
	public ulong EndGpuBytes { get; init; }
	public ulong AverageGpuBytes { get; init; }
	public ulong PeakGpuBytes { get; init; }
	public ulong GpuBudgetBytes { get; init; }
}

internal sealed class PerformanceChunkMetrics
{
	public int Loaded { get; init; }
	public int Pending { get; init; }
	public int Integrated { get; init; }
	public float IntegratedPerSecond { get; init; }
	public int LastStreamGenerated { get; init; }
	public float LastStreamSettleMilliseconds { get; init; }
	public float LastEffectivePerSecond { get; init; }
	public float LastGenerationPerSecond { get; init; }
}

internal sealed class PerformanceMeshingMetrics
{
	public int ConfiguredMaximumDispatchesPerUpdate { get; init; }
	public int ObservedMaximumDispatchesPerUpdate { get; init; }
	public long Dispatches { get; init; }
	public int Resident { get; init; }
	public int GameplayResident { get; init; }
	public int WarmResident { get; init; }
	public int Lod0Resident { get; init; }
	public int Lod1Resident { get; init; }
	public int Pending { get; init; }
	public int GameplayPending { get; init; }
	public int WarmPending { get; init; }
	public int Lod1Pending { get; init; }
	public int PoolAvailable { get; init; }
	public long LogicalCapacityBytes { get; init; }
	public long ReservedActiveCellCapacity { get; init; }
	public long ReservedActiveCellCapacityBytes { get; init; }
	public int SettledSlabs { get; init; }
	public uint SettledSurfaceMeshes { get; init; }
	public uint SettledWarmSurfaceMeshes { get; init; }
	public uint TotalActiveCells { get; init; }
	public float AverageActiveCellsPerSurfaceChunk { get; init; }
	public uint MaximumActiveCellsPerSurfaceChunk { get; init; }
	public float ActiveCellUtilizationPercent { get; init; }
	public long PoolAllocations { get; init; }
	public long PoolReuses { get; init; }
	public long? GameThreadAllocatedBytes { get; init; }
	public long ScalarReadbacks { get; init; }
	public long GeometryReadbacks { get; init; }
	public long OrdinaryRenderSdfEvaluations { get; init; }
	public long UniqueVertices { get; init; }
	public long Triangles { get; init; }
	public long Indices { get; init; }
	public long UsedVertexBytes { get; init; }
	public long UsedIndexBytes { get; init; }
	public long CommittedVertexBytes { get; init; }
	public long CommittedIndexBytes { get; init; }
	public int ArenaCount { get; init; }
	public int FreeRangeCount { get; init; }
	public int LargestFreeVertexRange { get; init; }
	public int LargestFreeIndexRange { get; init; }
	public float FragmentationPercent { get; init; }
	public long TransientScratchBytes { get; init; }
	public long AllocationCountReadbacks { get; init; }
	public long AllocationCountReadbackBytes { get; init; }
	public double AllocationCountReadbackMilliseconds { get; init; }
	public double CountStageSubmissionMilliseconds { get; init; }
	public double EmitStageSubmissionMilliseconds { get; init; }
	public string TopologyDigest { get; init; }
	public string PositionDigest { get; init; }
	public string Lod0TopologyDigest { get; init; }
	public string Lod0PositionDigest { get; init; }
	public string Lod1TopologyDigest { get; init; }
	public string Lod1PositionDigest { get; init; }
	public string GpuProfilerPath { get; init; }
	public float AverageGpuMilliseconds { get; init; }
	public float MaximumGpuMilliseconds { get; init; }
	public PerformanceLatencyMetrics ScheduleToRenderable { get; init; }
	public PerformanceMeshingThroughputMetrics Throughput { get; init; }
}

internal sealed class PerformanceMeshingThroughputMetrics
{
	public int ScratchLanes { get; init; }
	public long RegionsScheduled { get; init; }
	public long RegionsCountSubmitted { get; init; }
	public long RegionsPublished { get; init; }
	public float RegionsScheduledPerSecond { get; init; }
	public float RegionsCountSubmittedPerSecond { get; init; }
	public float RegionsPublishedPerSecond { get; init; }
	public long BatchesSubmitted { get; init; }
	public long BatchesCompleted { get; init; }
	public float BatchesSubmittedPerSecond { get; init; }
	public float BatchesCompletedPerSecond { get; init; }
	public float AverageBatchOccupancy { get; init; }
	public int MinimumBatchOccupancy { get; init; }
	public int MaximumBatchOccupancy { get; init; }
	public IReadOnlyList<int> BatchOccupancyHistogram { get; init; } = System.Array.Empty<int>();
	public PerformanceDistributionMetrics CountSubmissionMilliseconds { get; init; }
	public PerformanceDistributionMetrics CountReadbackMilliseconds { get; init; }
	public PerformanceDistributionMetrics CountCallbackWaitMilliseconds { get; init; }
	public PerformanceDistributionMetrics CpuAllocationMilliseconds { get; init; }
	public PerformanceDistributionMetrics EmitSubmissionMilliseconds { get; init; }
	public PerformanceDistributionMetrics EmitToPublicationMilliseconds { get; init; }
	public PerformanceQueueDepthMetrics GameplayQueue { get; init; }
	public PerformanceQueueDepthMetrics WarmQueue { get; init; }
	public PerformanceQueueDepthMetrics TotalQueue { get; init; }
	public PerformanceDistributionMetrics PlayerRouteLagWorldUnits { get; init; }
	public PerformanceDistributionMetrics PlayerRouteLagChunks { get; init; }
	public float PostLoopDrainMilliseconds { get; init; }
}

internal sealed class PerformanceDistributionMetrics
{
	public int Samples { get; init; }
	public int TruncatedSamples { get; init; }
	public float Average { get; init; }
	public float P50 { get; init; }
	public float P95 { get; init; }
	public float P99 { get; init; }
	public float Maximum { get; init; }
}

internal sealed class PerformanceQueueDepthMetrics
{
	public int Samples { get; init; }
	public int TruncatedSamples { get; init; }
	public float Average { get; init; }
	public float P50 { get; init; }
	public float P95 { get; init; }
	public float P99 { get; init; }
	public int Maximum { get; init; }
}

internal sealed class PerformanceLatencyMetrics
{
	public int Samples { get; init; }
	public int TruncatedSamples { get; init; }
	public float P50Milliseconds { get; init; }
	public float P95Milliseconds { get; init; }
	public float P99Milliseconds { get; init; }
	public float MaximumMilliseconds { get; init; }
	public int Cancelled { get; init; }
	public int Superseded { get; init; }
}

internal sealed class PerformanceVisibilityMetrics
{
	public uint Samples { get; init; }
	public float AverageResidentMeshChunks { get; init; }
	public float AverageVisibleMeshChunks { get; init; }
	public float AverageWarmMeshChunks { get; init; }
	public float AverageLod0ResidentMeshChunks { get; init; }
	public float AverageLod1ResidentMeshChunks { get; init; }
	public float AverageLod0VisibleMeshChunks { get; init; }
	public float AverageLod1VisibleMeshChunks { get; init; }
	public uint SettledLod0SurfaceMeshes { get; init; }
	public uint SettledLod1SurfaceMeshes { get; init; }
	public uint MinimumVisibleMeshChunks { get; init; }
	public uint MaximumVisibleMeshChunks { get; init; }
	public float AverageNonZeroIndirectDraws { get; init; }
	public float AverageCulledDraws { get; init; }
	public float CulledDrawPercentage { get; init; }
	public long LogicalBufferBytes { get; init; }
	public long ScalarReadbacks { get; init; }
}

internal sealed class PerformanceVector2
{
	public float X { get; init; }
	public float Y { get; init; }
}

internal sealed class PerformanceVector3
{
	public float X { get; init; }
	public float Y { get; init; }
	public float Z { get; init; }
}

internal sealed class PerformanceVector3Int
{
	public int X { get; init; }
	public int Y { get; init; }
	public int Z { get; init; }
}
