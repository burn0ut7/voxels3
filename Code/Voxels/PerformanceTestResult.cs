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
	public PerformanceMemoryMetrics Memory { get; init; }
	public PerformanceChunkMetrics Chunks { get; init; }
	public PerformanceMeshingMetrics Meshing { get; init; }
	public PerformanceVisibilityMetrics Visibility { get; init; }
	public PerformanceStreamingMetrics Streaming { get; init; }
	public PerformanceProfilerMetrics Profiler { get; init; }
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
	public int Pending { get; init; }
	public int GameplayPending { get; init; }
	public int WarmPending { get; init; }
	public int PoolAvailable { get; init; }
	public long LogicalCapacityBytes { get; init; }
	public long ReservedActiveCellCapacity { get; init; }
	public long ReservedActiveCellCapacityBytes { get; init; }
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
	public string GpuProfilerPath { get; init; }
	public float AverageGpuMilliseconds { get; init; }
	public float MaximumGpuMilliseconds { get; init; }
}

internal sealed class PerformanceVisibilityMetrics
{
	public uint Samples { get; init; }
	public float AverageResidentMeshChunks { get; init; }
	public float AverageVisibleMeshChunks { get; init; }
	public float AverageWarmMeshChunks { get; init; }
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
