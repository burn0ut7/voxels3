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
	public float TerrainSurfaceHeight { get; init; }
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
	public long Dispatches { get; init; }
	public int Resident { get; init; }
	public int Pending { get; init; }
	public int PoolAvailable { get; init; }
	public long LogicalCapacityBytes { get; init; }
	public long PoolAllocations { get; init; }
	public long PoolReuses { get; init; }
	public long? GameThreadAllocatedBytes { get; init; }
	public long ScalarReadbacks { get; init; }
	public long GeometryReadbacks { get; init; }
	public string GpuProfilerPath { get; init; }
	public float AverageGpuMilliseconds { get; init; }
	public float MaximumGpuMilliseconds { get; init; }
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
