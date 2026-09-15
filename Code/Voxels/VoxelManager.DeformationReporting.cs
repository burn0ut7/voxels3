using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

public sealed partial class VoxelManager
{
	private DeformationCapture _deformationCapture;
	private enum DeformationStage { Tool, Ray, PrepareBrush, CommitAndInvalidate, MeshPump, CollisionIntegration, AdmissionToCommit, AdmissionToPublication, Frame }

	private sealed class DeformationSamples
	{
		private readonly float[] _samples = new float[8192];
		private int _stored;
		private long _count;
		private double _total;
		private float _maximum;
		public void Record( double milliseconds )
		{
			if ( !double.IsFinite( milliseconds ) || milliseconds < 0 ) return;
			_count++;
			_total += milliseconds;
			_maximum = Math.Max( _maximum, (float)milliseconds );
			if ( _stored < _samples.Length ) _samples[_stored++] = (float)milliseconds;
		}
		public object Complete( string stage )
		{
			Array.Sort( _samples, 0, _stored );
			var tails = PerformanceSampleTails.FromSorted( _samples.AsSpan( 0, _stored ) );
			return new { Stage = stage, Count = _count, StoredSamples = _stored, TruncatedSamples = _count - _stored,
				TotalMilliseconds = _total, AverageMilliseconds = _count == 0 ? 0 : _total / _count,
				P50Milliseconds = tails.P50, P95Milliseconds = tails.P95, P99Milliseconds = tails.P99, MaximumMilliseconds = _maximum };
		}
	}

	private sealed class DeformationCapture
	{
		public readonly object Gate = new();
		public readonly string Id = Guid.NewGuid().ToString( "N" );
		public readonly long Started = Stopwatch.GetTimestamp();
		public readonly DeformationSamples[] Stages = Enum.GetValues<DeformationStage>().Select( _ => new DeformationSamples() ).ToArray();
		public readonly Dictionary<string, long> Outcomes = new();
		public readonly double[] HeldGateSeconds = new double[5];
		public bool Finished;
		public double Duration;
		public int StartRevision;
		public long StartCommitted;
		public long StartRejected;
		public Vector3 StartEye;
		public double HeldSeconds;
		public double LastMemorySample;
		public long PeakRetainedSampleBytes;
		public long AllocatedBytes;
		public long GcPauseTicks;
		public long Exceptions;
		public int PeakQueue;
		public long VisualRequestedAt;
		public int VisualRevision;
		public void Record( DeformationStage stage, double milliseconds )
		{
			lock ( Gate ) if ( !Finished ) Stages[(int)stage].Record( milliseconds );
		}
	}

	private readonly struct DeformationTiming : IDisposable
	{
		private readonly DeformationCapture _capture;
		private readonly DeformationStage _stage;
		private readonly long _started;
		private readonly VoxelManager _owner;
		public DeformationTiming( DeformationCapture capture, DeformationStage stage, VoxelManager owner )
		{
			_capture = capture;
			_stage = stage;
			_owner = owner;
			_started = capture is null ? 0 : Stopwatch.GetTimestamp();
		}
		public void Dispose()
		{
			if ( _capture is null ) return;
			lock ( _capture.Gate )
			{
				if ( _capture.Finished ) return;
				_capture.Stages[(int)_stage].Record( Stopwatch.GetElapsedTime( _started ).TotalMilliseconds );
				if ( _stage != DeformationStage.Tool ) return;
				var status = _owner._terrainToolStatus;
				_capture.Outcomes.TryGetValue( status, out var count );
				_capture.Outcomes[status] = count + 1;
			}
		}
	}

	private DeformationTiming MeasureDeformation( DeformationStage stage ) => new( _deformationCapture, stage, this );

	/// <summary>Observe ordinary gameplay without moving the player or driving tool input.</summary>
	[ConCmd( "voxel_deformation_report" )]
	public static void StartDeformationReportCommand( float seconds = 30 )
	{
		if ( !TryGetActiveManager( "deformation.report", out var manager ) ) return;
		if ( !float.IsFinite( seconds ) || seconds < 5 || seconds > 120 )
		{
			Log.Warning( "[DeformationReport] duration must be between 5 and 120 seconds" );
			return;
		}
		if ( manager._deformationCapture is not null )
		{
			Log.Warning( "[DeformationReport] capture already running" );
			return;
		}
		manager._deformationCapture = new DeformationCapture
		{
			Duration = seconds, StartRevision = manager.CurrentField.Revision,
			StartCommitted = manager._terrainEditsCommitted, StartRejected = manager._terrainEditsRejected,
			StartEye = manager.Scene.GetAllComponents<PlayerController>().FirstOrDefault( player => !player.IsProxy )?.EyePosition ?? Vector3.Zero
		};
		Log.Info( $"[DeformationReport] started id={manager._deformationCapture.Id} seconds={seconds}; dig and move normally" );
	}

	[ConCmd( "voxel_deformation_report_stop" )]
	public static void StopDeformationReportCommand()
	{
		if ( TryGetActiveManager( "deformation.report.stop", out var manager ) ) manager.FinishDeformationReport();
	}

	private void ObserveDeformationReport()
	{
		var capture = _deformationCapture;
		if ( capture is null ) return;
		var elapsed = Stopwatch.GetElapsedTime( capture.Started ).TotalSeconds;
		if ( capture.VisualRequestedAt > 0 && _gpuMesher.LastFieldPublicationRevision == capture.VisualRevision )
		{
			capture.Record( DeformationStage.AdmissionToPublication, Stopwatch.GetElapsedTime( capture.VisualRequestedAt,
				_gpuMesher.LastFieldPublicationTimestamp ).TotalMilliseconds );
			capture.VisualRequestedAt = 0;
		}
		if ( elapsed >= capture.Duration ) { FinishDeformationReport(); return; }
		capture.Record( DeformationStage.Frame, RealTime.Delta * 1000d );
		capture.PeakQueue = Math.Max( capture.PeakQueue, _terrainEditQueue.Count );
		capture.AllocatedBytes += Math.Max( 0, global::Sandbox.Diagnostics.PerformanceStats.BytesAllocated );
		capture.GcPauseTicks += Math.Max( 0, global::Sandbox.Diagnostics.PerformanceStats.GcPause );
		capture.Exceptions += Math.Max( 0, global::Sandbox.Diagnostics.PerformanceStats.Exceptions );
		if ( elapsed >= capture.LastMemorySample )
		{
			capture.PeakRetainedSampleBytes = Math.Max( capture.PeakRetainedSampleBytes, TerrainFieldPage.RetainedSampleBytes );
			capture.LastMemorySample = elapsed + 1;
		}
		if ( AdminMenu.CapturesInput( Scene ) || Input.Down( "Attack1" ) == Input.Down( "Attack2" ) ) return;
		capture.HeldSeconds += RealTime.Delta;
		// Mutually exclusive observed gates, sampled before this frame's admission attempt.
		var gate = _terrainEditCapacityDeferred ? 0 : _terrainEditTask is not null ? 1 :
			_gpuMesher.FieldPublicationPending ? 2 : _terrainToolElapsed < TerrainToolTickSeconds ? 3 : 4;
		capture.HeldGateSeconds[gate] += RealTime.Delta;
	}

	private void ReportDeformationCommit( TerrainFieldChange change )
	{
		var capture = _deformationCapture;
		if ( capture is null || _activeTerrainEdit.RequestedAt < capture.Started || _terrainRestorePending ) return;
		capture.Record( DeformationStage.AdmissionToCommit, Stopwatch.GetElapsedTime( _activeTerrainEdit.RequestedAt ).TotalMilliseconds );
		if ( ReferenceEquals( change.Source, change.Result ) ) return;
		capture.VisualRevision = change.Result.Revision;
		capture.VisualRequestedAt = _activeTerrainEdit.RequestedAt;
	}

	private void FinishDeformationReport()
	{
		var capture = _deformationCapture;
		if ( capture is null ) return;
		_deformationCapture = null;
		lock ( capture.Gate )
		{
			capture.Finished = true;
			try
			{
				var result = new
				{
					SchemaVersion = 1, capture.Id, Seconds = Stopwatch.GetElapsedTime( capture.Started ).TotalSeconds,
					World = CurrentField.WorldId, Settings = CurrentField.Settings, GeneratorVersion = ProceduralTerrainSdf.CurrentVersion,
					capture.StartEye, capture.StartRevision, EndRevision = CurrentField.Revision,
					Committed = _terrainEditsCommitted - capture.StartCommitted, Rejected = _terrainEditsRejected - capture.StartRejected,
					capture.HeldSeconds, capture.Outcomes, capture.PeakQueue,
					HeldGateSeconds = new { Capacity = capture.HeldGateSeconds[0], Preparing = capture.HeldGateSeconds[1],
						Publication = capture.HeldGateSeconds[2], Cooldown = capture.HeldGateSeconds[3], Other = capture.HeldGateSeconds[4] },
					Timings = Enum.GetValues<DeformationStage>().Select( stage => capture.Stages[(int)stage].Complete( stage.ToString() ) ).ToArray(),
					capture.AllocatedBytes, GcPauseMilliseconds = capture.GcPauseTicks * 1000d / TimeSpan.TicksPerSecond,
					capture.Exceptions, capture.PeakRetainedSampleBytes,
					SampleBudgetBytes = TerrainField.MaximumSampleBytes, PendingPublicationRevision = capture.VisualRequestedAt > 0 ? capture.VisualRevision : (int?)null,
					Pending = _terrainEditQueue.Count + (_terrainEditTask is null ? 0 : 1), PublicationPending = _gpuMesher.FieldPublicationPending,
					Collision = _collision.Capture(), Failure = _terrainEditFailure,
					Meaning = "Scoped elapsed milliseconds, not exclusive CPU or GPU time. Tool includes Ray; MeshPump and CollisionIntegration include streaming. Latencies begin at admitted request, exclude rejected attempts and display scanout. Held gates are exclusive frame observations; Other includes targeting/collision waits. Tail samples retain first 8192 per stage; count/total/max include later samples. Runtime allocations/GC cover the whole game. Collision is a cumulative snapshot, not a capture-only delta. In-flight work at capture boundaries may be incomplete. No-input captures do not measure digging throughput."
				};
				FileSystem.Data.CreateDirectory( "performance" );
				var path = $"performance/deformation-report-{capture.Id}.json";
				using var stream = FileSystem.Data.OpenWrite( path, FileMode.Create );
				var bytes = Encoding.UTF8.GetBytes( JsonSerializer.Serialize( result, PerformanceJsonOptions ) );
				stream.Write( bytes, 0, bytes.Length );
				Log.Info( $"[DeformationReport] saved path={path} committed={result.Committed} heldSeconds={capture.HeldSeconds:F2} failure={_terrainEditFailure ?? "none"}" );
			}
			catch ( Exception exception ) { Log.Warning( $"[DeformationReport] save.failed reason={exception.Message}" ); }
		}
	}
}
