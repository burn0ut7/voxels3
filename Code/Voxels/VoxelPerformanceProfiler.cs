using System.Collections.Generic;

/// <summary>
/// Owns the bounded Sandbox profiler snapshot recorded at the end of the
/// canonical player-driven performance route.
/// </summary>
internal static class VoxelPerformanceProfiler
{
	public const int WindowFrames = 200;
	public const string ManagerUpdate = "Voxels3/VoxelManager.OnUpdate";
	public const string FigureEightMovement = "Voxels3/FigureEightMovement";
	public const string PerformanceSampling = "Voxels3/PerformanceSampling";
	public const string RebuildDesiredChunks = "Voxels3/RebuildDesiredChunks";
	public const string IntegrateGameplayChunks = "Voxels3/IntegrateGameplayChunks";
	public const string IntegrateWarmChunks = "Voxels3/IntegrateWarmChunks";
	public const string ProcessPendingMeshes = "Voxels3/ProcessPendingMeshes";
	public const string CommitDrawCommands = "Voxels3/CommitDrawCommands";

	private static readonly string[] ScriptTimingNames =
	{
		ManagerUpdate,
		FigureEightMovement,
		PerformanceSampling,
		RebuildDesiredChunks,
		IntegrateGameplayChunks,
		IntegrateWarmChunks,
		ProcessPendingMeshes,
		CommitDrawCommands
	};

	public static PerformanceProfilerMetrics Capture()
	{
		var engine = new List<PerformanceProfilerTiming>();
		foreach ( var timing in global::Sandbox.Diagnostics.PerformanceStats.Timings.GetMain() )
		{
			engine.Add( CaptureTiming( timing ) );
		}

		var scripts = new List<PerformanceProfilerTiming>( ScriptTimingNames.Length );
		foreach ( var name in ScriptTimingNames )
		{
			scripts.Add( CaptureTiming(
				global::Sandbox.Diagnostics.PerformanceStats.Timings.Get( name ) ) );
		}

		return new PerformanceProfilerMetrics
		{
			WindowFrames = WindowFrames,
			Engine = engine,
			Scripts = scripts
		};
	}

	private static PerformanceProfilerTiming CaptureTiming(
		global::Sandbox.Diagnostics.PerformanceStats.Timings timing )
	{
		var metric = timing.GetMetric( WindowFrames );
		return new PerformanceProfilerTiming
		{
			Name = timing.Name,
			Calls = metric.Calls,
			MinimumMillisecondsPerFrame = metric.Min,
			AverageMillisecondsPerFrame = metric.Avg,
			MaximumMillisecondsPerFrame = metric.Max
		};
	}
}
