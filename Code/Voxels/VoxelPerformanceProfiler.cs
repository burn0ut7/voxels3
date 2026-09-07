using System;
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
	public const string PreparePlacement = "Voxels3/PreparePlacement";
	public const string IntegrateWarmChunks = "Voxels3/IntegrateWarmChunks";
	public const string ProcessPendingMeshes = "Voxels3/ProcessPendingMeshes";
	public const string RefreshRenderCameras = "Voxels3/RefreshRenderCameras";
	public const string CommitDrawCommands = "Voxels3/CommitDrawCommands";

	private static readonly string[] ScriptTimingNames =
	{
		ManagerUpdate,
		FigureEightMovement,
		PerformanceSampling,
		RebuildDesiredChunks,
		PreparePlacement,
		IntegrateWarmChunks,
		ProcessPendingMeshes,
		RefreshRenderCameras,
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
		var history = timing.History;
		var sampleCount = Math.Min( WindowFrames, history.Size );
		var samples = new float[sampleCount];
		var firstSample = history.Size - sampleCount;
		for ( var index = 0; index < sampleCount; index++ )
		{
			samples[index] = history[firstSample + index].TotalMs;
		}
		Array.Sort( samples );
		var p95Index = Math.Clamp(
			(int)Math.Ceiling( sampleCount * 0.95d ) - 1,
			0,
			Math.Max( 0, sampleCount - 1 ) );
		var p99Index = Math.Clamp(
			(int)Math.Ceiling( sampleCount * 0.99d ) - 1,
			0,
			Math.Max( 0, sampleCount - 1 ) );
		return new PerformanceProfilerTiming
		{
			Name = timing.Name,
			Calls = metric.Calls,
			MinimumMillisecondsPerFrame = metric.Min,
			AverageMillisecondsPerFrame = metric.Avg,
			P95MillisecondsPerFrame = sampleCount > 0 ? samples[p95Index] : 0f,
			P99MillisecondsPerFrame = sampleCount > 0 ? samples[p99Index] : 0f,
			MaximumMillisecondsPerFrame = metric.Max
		};
	}
}
