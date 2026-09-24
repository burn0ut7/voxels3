using Editor.Mcp;
using System;
using System.Diagnostics;
using System.Linq;
using Input = Sandbox.Input;

/// <summary>Bounded native editor control of the real playable scene's input.</summary>
[McpToolset( "gameplay_input", "Visible play-mode input and runtime readback" )]
public static class GameplayInputTools
{
	private static Playback _playback;

	/// <summary>Hold configured gameplay actions for 0.05–3 seconds. Normal gameplay handlers receive the input. Physical input interrupts playback.</summary>
	/// <param name="actions">Comma-separated configured action names, for example Forward,Run or Use.</param>
	/// <param name="heldActions">Optional modifiers held throughout, for example Duck while pressing Use after a lead-in.</param>
	/// <param name="leadInSeconds">Optional 0–2 second delay before the primary actions. Combined duration cannot exceed three seconds.</param>
	/// <param name="wheel">One mouse-wheel notch: -1, 0 or 1. Pulsed once per input context.</param>
	/// <param name="escape">Pulse Escape once through the normal game input handler.</param>
	[McpTool( "play_gameplay_input" )]
	public static object Play( string actions, float seconds = 0.1f, string heldActions = "", float leadInSeconds = 0f, int wheel = 0, bool escape = false )
	{
		if ( !Game.IsPlaying || Game.IsPaused || !Game.ActiveScene.IsValid() || Game.ActiveScene.IsEditor )
			throw new InvalidOperationException( "An unpaused visible Play session is required." );
		if ( !float.IsFinite( seconds ) || seconds < 0.05f || seconds > 3f )
			throw new ArgumentOutOfRangeException( nameof(seconds), "Use 0.05–3 seconds." );
		if ( !float.IsFinite( leadInSeconds ) || leadInSeconds < 0f || leadInSeconds > 2f || seconds + leadInSeconds > 3f )
			throw new ArgumentOutOfRangeException( nameof(leadInSeconds), "Use 0–2 seconds with a total duration of at most three seconds." );
		if ( wheel < -1 || wheel > 1 ) throw new ArgumentOutOfRangeException( nameof(wheel), "Use -1, 0 or 1." );
		if ( _uiClickPending || _playback is { Done: false } ) throw new InvalidOperationException( "Input is already running. Wait or cancel it first." );
		var configured = Input.ActionNames.ToArray();
		var requested = (actions ?? "").Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries )
			.Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
		var held = (heldActions ?? "").Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries )
			.Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
		var combined = requested.Concat( held ).ToArray();
		if ( (requested.Length < 1 && wheel == 0 && !escape) || combined.Length > 4 || requested.Intersect( held, StringComparer.OrdinalIgnoreCase ).Any() ||
			combined.Any( name => !configured.Contains( name, StringComparer.OrdinalIgnoreCase ) ) )
			throw new ArgumentException( "Provide up to four configured actions, a wheel notch or Escape.", nameof(actions) );
		_playback?.Dispose();
		_playback = new Playback( Game.ActiveScene, combined, configured, seconds + leadInSeconds, requested.Length, leadInSeconds, wheel, escape );
		return Status();
	}

	/// <summary>Cancel any bounded gameplay input. Does not release or disable the human player's controls.</summary>
	[McpTool( "cancel_gameplay_input" )]
	public static object Cancel()
	{
		if ( _playback is { Done: false } ) _playback.RequestStop( "Cancelled" );
		return Status();
	}

	/// <summary>Read the last input command and edges supplied to each scene stage. Gameplay outcome requires separate runtime readback.</summary>
	[McpTool.ReadOnly( "gameplay_input_status" )]
	public static object Status() => _playback is null ? new { State = "Idle" } : (object)new
	{
		_playback.Id, _playback.State, _playback.Actions, _playback.Seconds, _playback.LeadInSeconds, _playback.Wheel, _playback.Escape,
		Update = new { _playback.Update.Frames, _playback.Update.Presses, _playback.Update.Releases },
		FixedUpdate = new { _playback.Fixed.Frames, _playback.Fixed.Presses, _playback.Fixed.Releases }
	};

	/// <summary>Read an object specifically from the running scene, avoiding authored-scene objects with the same prefab GUID.</summary>
	[McpTool.ReadOnly( "inspect_play_object" )]
	public static object InspectObject( string id )
	{
		if ( !Game.IsPlaying || !Game.ActiveScene.IsValid() ) throw new InvalidOperationException( "Start Play first." );
		var go = Game.ActiveScene.Directory.FindByGuid( Guid.Parse( id ) ) as GameObject
			?? throw new ArgumentException( "Object not found in the running scene.", nameof(id) );
		var player = go.Components.Get<PlayerController>();
		return new
		{
			go.Id, go.Name, go.WorldPosition, go.WorldRotation, go.Enabled,
			Eye = player.IsValid() ? player.EyeTransform : (Transform?)null,
			Components = go.Components.GetAll().Select( c => new { c.Id, Type = c.GetType().Name, Properties = c.Serialize() } ).ToArray()
		};
	}

	/// <summary>Read the first solid hit along the local player eye ray, ignoring their own body and held viewmodel.</summary>
	[McpTool.ReadOnly( "trace_player_view" )]
	public static object TracePlayerView( float distance )
	{
		if ( !Game.IsPlaying || !Game.ActiveScene.IsValid() ) throw new InvalidOperationException( "Start Play first." );
		if ( !float.IsFinite( distance ) || distance <= 0 || distance > 4096 ) throw new ArgumentOutOfRangeException( nameof(distance) );
		var player = Game.ActiveScene.GetAllComponents<PlayerController>().Single( p => p.Active && !p.IsProxy );
		var eye = player.EyeTransform;
		var hit = Game.ActiveScene.Trace.Ray( eye.Position, eye.Position + eye.Rotation.Forward * distance )
			.IgnoreGameObjectHierarchy( player.GameObject ).WithoutTags( "deadwake_viewmodel" ).Run();
		return new { Eye = eye, hit.Hit, hit.StartedSolid, hit.HitPosition, hit.Distance,
			ObjectId = hit.GameObject.IsValid() ? hit.GameObject.Id : (Guid?)null,
			ObjectName = hit.GameObject.IsValid() ? hit.GameObject.Name : null };
	}

	/// <summary>Toggle one runtime component without editing its authored prefab or scene. Returns the previous enabled state for restoration.</summary>
	[McpTool( "set_play_component_enabled" )]
	public static object SetComponentEnabled( string objectId, string componentId, bool enabled )
	{
		if ( !Game.IsPlaying || !Game.ActiveScene.IsValid() ) throw new InvalidOperationException( "Start Play first." );
		var go = Game.ActiveScene.Directory.FindByGuid( Guid.Parse( objectId ) ) as GameObject
			?? throw new ArgumentException( "Object not found in the running scene.", nameof(objectId) );
		var component = go.Components.GetAll().FirstOrDefault( c => c.Id == Guid.Parse( componentId ) )
			?? throw new ArgumentException( "Component not found on the runtime object.", nameof(componentId) );
		var previous = component.Enabled;
		component.Enabled = enabled;
		return new { component.Id, PreviousEnabled = previous, component.Enabled };
	}

	private static bool _uiClickPending;
	private static IDisposable _uiClickHook;

	/// <summary>Click one rendered game UI panel by its unique ID through normal mouse events. Never calls gameplay action methods.</summary>
	[McpTool( "click_play_ui" )]
	public static async System.Threading.Tasks.Task<object> ClickUi( string id, string button = "left", bool shift = false )
	{
		if ( !Game.IsPlaying || Game.IsPaused || !Game.ActiveScene.IsValid() ) throw new InvalidOperationException( "Start unpaused Play first." );
		if ( string.IsNullOrWhiteSpace( id ) || (button != "left" && button != "right") ) throw new ArgumentException( "Use a panel ID and left or right button." );
		if ( _uiClickPending || _uiClickHook is not null || _playback is { Done: false } ) throw new InvalidOperationException( "Another input command is running." );
		_uiClickPending = true;
		var scene = Game.ActiveScene;
		var completion = new System.Threading.Tasks.TaskCompletionSource<object>();
		var clock = Stopwatch.StartNew();
		var dispatched = false;
		try
		{
			_uiClickHook = scene.AddHook( GameObjectSystem.Stage.StartUpdate, -900, () =>
			{
				if ( dispatched ) return;
				dispatched = true;
				try
				{
					if ( Game.ActiveScene != scene || Game.IsPaused || clock.Elapsed.TotalSeconds > 1 ) throw new InvalidOperationException( "UI click expired or scene changed." );
					if ( Input.MouseDelta.LengthSquared > 0 || Input.AnalogMove.LengthSquared > 0 || Input.ActionNames.Any( name => Input.Down( name, false ) ) )
						throw new InvalidOperationException( "Interrupted by player input." );
					var allPanels = scene.GetAllComponents<ScreenPanel>().Where( screen => screen.Active )
						.Select( screen => screen.GetPanel() ).Where( root => root is not null )
						.SelectMany( root => root.Descendants.Prepend( root ) ).ToArray();
					var panels = allPanels.Where( panel => panel.Id == id ).ToArray();
					if ( panels.Length != 1 ) throw new InvalidOperationException( "Expected one rendered game panel with that ID." );
					var panel = panels[0];
					if ( panel.Box.Rect.Width < 1 || panel.Box.Rect.Height < 1 || panel.ComputedStyle?.PointerEvents.ToString() == "None" )
						throw new InvalidOperationException( "Panel is not laid out for pointer input." );
					var position = panel.Box.Rect.Position + panel.Box.Rect.Size * 0.5f;
					var receiver = allPanels.LastOrDefault( candidate => candidate.IsVisible && candidate.IsInside( position ) &&
						candidate.ComputedStyle?.PointerEvents.ToString() != "None" );
					if ( receiver is null || (receiver != panel && !panel.Descendants.Contains( receiver )) )
						throw new InvalidOperationException( "Panel is hidden or covered at its center." );
					var key = button == "left" ? "mouseleft" : "mouseright";
					foreach ( var name in new[] { "onmouseover", "onmousedown", "onmouseup", button == "left" ? "onclick" : "onrightclick" } )
						receiver.CreateEvent( new Sandbox.UI.MousePanelEvent( name, receiver, key )
						{
							LocalPosition = position - receiver.Box.Rect.Position,
							KeyboardModifiers = shift ? KeyboardModifiers.Shift : KeyboardModifiers.None
						} );
					completion.TrySetResult( new { Id = id, Button = button, Shift = shift, State = "Queued", Rect = panel.Box.Rect } );
				}
				catch ( Exception exception ) { completion.TrySetException( exception ); }
			}, nameof(GameplayInputTools), "Click game UI" );
			var finished = await System.Threading.Tasks.Task.WhenAny( completion.Task, System.Threading.Tasks.Task.Delay( 1000 ) );
			if ( finished != completion.Task ) throw new TimeoutException( "No active scene update consumed the UI click." );
			return await completion.Task;
		}
		finally { _uiClickPending = false; }
	}

	private static CameraClip _clip;

	/// <summary>Record the actual visible Play camera and UI at up to 30 FPS, without advancing or replacing the scene. Existing files are never overwritten.</summary>
	[McpTool( "record_play_camera" )]
	public static object RecordCamera( string path, float seconds = 3f )
	{
		if ( !Game.IsPlaying || Game.IsPaused || !Game.ActiveScene.IsValid() || !Game.ActiveScene.Camera.IsValid() )
			throw new InvalidOperationException( "Start visible unpaused Play first." );
		if ( !float.IsFinite( seconds ) || seconds < 0.5f || seconds > 10f ) throw new ArgumentOutOfRangeException( nameof(seconds) );
		if ( _clip is { Done: false } ) throw new InvalidOperationException( "A camera clip is already recording or finishing." );
		path = System.IO.Path.GetFullPath( path );
		if ( System.IO.Path.GetExtension( path ) != ".webm" || System.IO.File.Exists( path ) || System.IO.File.Exists( path + ".json" ) )
			throw new ArgumentException( "Use a new WebM path in an existing directory." );
		_clip = new CameraClip( Game.ActiveScene, path, seconds );
		return CameraRecordingStatus();
	}

	[McpTool.ReadOnly( "play_camera_recording_status" )]
	public static object CameraRecordingStatus() => _clip is null ? new { State = "Idle" } : (object)new
	{
		_clip.State, _clip.Path, _clip.Done, Frames = _clip.Times.Count, Elapsed = _clip.Clock.Elapsed.TotalSeconds
	};

	private sealed class CameraClip
	{
		public readonly string Path;
		public readonly Stopwatch Clock = Stopwatch.StartNew();
		public readonly System.Collections.Generic.List<double> Times = new();
		public string State { get; private set; } = "Recording";
		public bool Done { get; private set; }
		private Scene _scene;
		private readonly float _seconds;
		private readonly Bitmap _bitmap = new( 960, 540 );
		private readonly VideoWriter _writer;
		private bool _finishing;
		private double _next;

		public CameraClip( Scene scene, string path, float seconds )
		{
			_scene = scene; Path = path; _seconds = seconds;
			_writer = EditorUtility.CreateVideoWriter( path, new VideoWriter.Config
			{
				Width = 960, Height = 540, FrameRate = 30,
				Codec = VideoWriter.Codec.VP9, Container = VideoWriter.Container.WebM,
				Preset = VideoWriter.EncodingPreset.Fast
			} );
		}

		public void Tick()
		{
			if ( Done || _finishing ) return;
			try
			{
				if ( !Game.IsPlaying || Game.IsPaused || Game.ActiveScene != _scene || !_scene.Camera.IsValid() )
				{ Finish( "Stopped: Play session changed or paused" ); return; }
				var elapsed = Clock.Elapsed.TotalSeconds;
				if ( elapsed >= _seconds ) { Finish( "Completed" ); return; }
				if ( elapsed < _next ) return;
				_scene.Camera.RenderToBitmap( _bitmap, true );
				_writer.AddFrame( _bitmap, TimeSpan.FromSeconds( elapsed ) );
				Times.Add( elapsed );
				_next = elapsed + 1d / 30d;
			}
			catch ( Exception exception ) { Finish( "Failed: " + exception.Message ); }
		}

		private async void Finish( string result )
		{
			_finishing = true;
			Clock.Stop();
			State = "Finishing";
			try { await _writer.FinishAsync(); State = result; }
			catch ( Exception exception ) { State = "Failed: " + exception.Message; }
			finally
			{
				_writer.Dispose(); _bitmap.Dispose(); _scene = null; Done = true;
				try
				{
					System.IO.File.WriteAllText( Path + ".json", System.Text.Json.JsonSerializer.Serialize( new
					{ State, Width = 960, Height = 540, TargetFps = 30, RequestedSeconds = _seconds, FrameSeconds = Times } ) );
				}
				catch ( Exception exception ) { State = "Failed to save capture timing: " + exception.Message; }
			}
		}
	}

	[EditorEvent.Frame]
	public static void Cleanup()
	{
		_clip?.Tick();
		if ( !_uiClickPending && _uiClickHook is not null ) { _uiClickHook.Dispose(); _uiClickHook = null; }
		if ( _playback is null ) return;
		if ( _playback.Done ) { _playback.Dispose(); return; }
		if ( !Game.IsPlaying || Game.IsPaused || !_playback.Scene.IsValid() || Game.ActiveScene != _playback.Scene )
			_playback.Stop( "Play session changed or paused" );
		else if ( !_playback.Done && _playback.Elapsed > _playback.Seconds + 1.0 )
			_playback.Stop( "Expired without all scene stages" );
		if ( _playback.Done ) _playback.Dispose();
	}

	private sealed class Playback : IDisposable
	{
		public readonly Guid Id = Guid.NewGuid();
		public Scene Scene { get; private set; }
		public readonly string[] Actions;
		public readonly float Seconds;
		public readonly float LeadInSeconds;
		public readonly int PrimaryCount;
		public readonly int Wheel;
		public readonly bool Escape;
		public readonly InputStage Update;
		public readonly InputStage Fixed;
		public string State => Done ? (_endReason ?? "Completed") : (_endReason is null ? "Running" : "Cancelling");
		public bool Done { get; private set; }
		public bool Stopping => _endReason is not null;
		private string _endReason;
		public double Elapsed => _clock.Elapsed.TotalSeconds;
		private readonly Stopwatch _clock = Stopwatch.StartNew();
		private readonly string[] _configured;
		private IDisposable[] _hooks;

		public Playback( Scene scene, string[] actions, string[] configured, float seconds, int primaryCount, float leadInSeconds, int wheel, bool escape )
		{
			Scene = scene;
			Actions = actions;
			_configured = configured;
			Seconds = seconds;
			PrimaryCount = primaryCount;
			LeadInSeconds = leadInSeconds;
			Wheel = wheel;
			Escape = escape;
			Update = new InputStage( this );
			Fixed = new InputStage( this );
			_hooks = new[]
			{
				scene.AddHook( GameObjectSystem.Stage.StartUpdate, -1000, Update.Begin, nameof(GameplayInputTools), "Apply input" ),
				scene.AddHook( GameObjectSystem.Stage.FinishUpdate, 1000, Update.End, nameof(GameplayInputTools), "Restore input" ),
				scene.AddHook( GameObjectSystem.Stage.StartFixedUpdate, -1000, Fixed.Begin, nameof(GameplayInputTools), "Apply fixed input" ),
				scene.AddHook( GameObjectSystem.Stage.FinishFixedUpdate, 1000, Fixed.End, nameof(GameplayInputTools), "Restore fixed input" )
			};
		}

		public void RequestStop( string reason ) { if ( !Done ) _endReason ??= reason; }
		public void Stop( string reason ) { RequestStop( reason ); Done = true; }
		public void Complete() => Done = true;

		public bool HumanInput() => Input.EscapePressed || Input.MouseDelta.LengthSquared > 0f || Input.MouseWheel.LengthSquared > 0f ||
			Input.AnalogLook != Angles.Zero || Input.AnalogMove.LengthSquared > 0f || _configured.Any( name => Input.Down( name, false ) );

		public void Dispose()
		{
			if ( _hooks is null ) return;
			foreach ( var hook in _hooks ) hook.Dispose();
			_hooks = null;
			Scene = null;
		}
	}

	private sealed class InputStage
	{
		public int Frames, Presses, Releases;
		private readonly Playback _owner;
		private readonly bool[] _previous;
		private readonly bool[] _current;
		private readonly bool[] _held;
		private bool _applied, _released, _primaryPressed, _pulseSent, _escape, _escapeInjected, _wheelInjected;
		private Vector2 _wheel;
		private Vector3 _move;
		private double _startedAt = -1;

		public InputStage( Playback owner )
		{
			_owner = owner;
			_previous = new bool[owner.Actions.Length];
			_current = new bool[owner.Actions.Length];
			_held = new bool[owner.Actions.Length];
		}

		public void Begin()
		{
			if ( _owner.Done || _released ) return;
			if ( Input.Suppressed ) { _owner.Stop( "Input suppressed" ); return; }
			if ( _owner.HumanInput() ) _owner.RequestStop( "Interrupted by player input" );
			if ( _owner.Stopping && !_held.Any( held => held ) ) { _released = true; return; }
			// Each input context receives the whole press, even when its first tick was delayed.
			if ( _startedAt < 0 ) _startedAt = _owner.Elapsed;
			var elapsed = _owner.Elapsed - _startedAt;
			if ( !_owner.Stopping && elapsed >= _owner.Seconds && !_primaryPressed )
				_owner.RequestStop( "Expired before primary input" );
			var active = !_owner.Stopping && elapsed < _owner.Seconds;
			_move = Input.AnalogMove;
			_wheel = Input.MouseWheel;
			_escape = Input.EscapePressed;
			_applied = true;
			var pressed = false;
			var released = false;
			for ( var i = 0; i < _owner.Actions.Length; i++ )
			{
				var action = _owner.Actions[i];
				var down = active && (i >= _owner.PrimaryCount || elapsed >= _owner.LeadInSeconds);
				_current[i] = Input.Down( action, false );
				_previous[i] = (_current[i] && !Input.Pressed( action )) || Input.Released( action );
				// A human holding the same action takes ownership; never synthesize an up over it.
				if ( _current[i] ) continue;
				Input.SetLastAction( action, _held[i] );
				Input.SetAction( action, down );
				_held[i] = down;
				pressed |= Input.Pressed( action );
				released |= Input.Released( action );
				if ( i < _owner.PrimaryCount && Input.Pressed( action ) ) _primaryPressed = true;
				if ( down )
				{
					if ( action.Equals( "Forward", StringComparison.OrdinalIgnoreCase ) ) Input.AnalogMove += Vector3.Forward;
					if ( action.Equals( "Backward", StringComparison.OrdinalIgnoreCase ) ) Input.AnalogMove += Vector3.Backward;
					if ( action.Equals( "Left", StringComparison.OrdinalIgnoreCase ) ) Input.AnalogMove += Vector3.Left;
					if ( action.Equals( "Right", StringComparison.OrdinalIgnoreCase ) ) Input.AnalogMove += Vector3.Right;
				}
			}
			if ( active && elapsed >= _owner.LeadInSeconds && !_pulseSent && (_owner.Wheel != 0 || _owner.Escape) )
			{
				if ( _owner.Wheel != 0 ) { Input.MouseWheel = new Vector2( 0, _owner.Wheel ); _wheelInjected = true; }
				if ( _owner.Escape ) { Input.EscapePressed = true; _escapeInjected = true; }
				_pulseSent = _primaryPressed = pressed = true;
			}
			if ( pressed ) Presses++;
			if ( released ) Releases++;
			_released = !active;
			Frames++;
		}

		public void End()
		{
			if ( _applied )
			{
				for ( var i = 0; i < _owner.Actions.Length; i++ )
				{
					if ( _current[i] ) continue;
					Input.SetAction( _owner.Actions[i], _current[i] );
					Input.SetLastAction( _owner.Actions[i], _previous[i] );
				}
				Input.AnalogMove = _move;
				if ( _wheelInjected ) Input.MouseWheel = _wheel;
				if ( _escapeInjected ) Input.EscapePressed = _escape;
				_wheelInjected = _escapeInjected = false;
				_applied = false;
			}
			if ( _owner.Update._released && _owner.Fixed._released ) _owner.Complete();
		}
	}
}
