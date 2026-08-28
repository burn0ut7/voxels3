using Editor.Mcp;
using System;

[McpToolset( "voxels3", "Voxels3 production smoke controls" )]
public static class VoxelMcpTools
{
	private static FigureEightDriver _driver;

	/// <summary>
	/// Start or stop moving the active local player around a horizontal figure-eight at world Z zero.
	/// </summary>
	/// <param name="enabled">True to start or reconfigure movement; false to stop at the current position.</param>
	/// <param name="speed">Horizontal movement speed in world units per second.</param>
	/// <param name="distance">Maximum X distance from the starting center; the Y reach is half this value.</param>
	[McpTool( "player_figure_eight" )]
	public static string PlayerFigureEight( bool enabled = true, float speed = 320f, float distance = 1024f )
	{
		if ( !Game.IsPlaying )
		{
			throw new InvalidOperationException( "Start play mode before running the player figure-eight." );
		}

		if ( !enabled )
		{
			_driver?.Stop();
			_driver = null;
			return "Player figure-eight stopped.";
		}

		if ( !float.IsFinite( speed ) || speed <= 0f )
		{
			throw new ArgumentOutOfRangeException( nameof( speed ), "Speed must be finite and greater than zero." );
		}

		if ( !float.IsFinite( distance ) || distance <= 0f )
		{
			throw new ArgumentOutOfRangeException( nameof( distance ), "Distance must be finite and greater than zero." );
		}

		VoxelManager manager = null;
		foreach ( var candidate in Game.ActiveScene.GetAllComponents<VoxelManager>() )
		{
			if ( manager is not null )
			{
				throw new InvalidOperationException( "Exactly one active VoxelManager is required." );
			}

			manager = candidate;
		}

		if ( manager is null )
		{
			throw new InvalidOperationException( "Exactly one active VoxelManager is required." );
		}

		PlayerController player = null;
		foreach ( var candidate in Game.ActiveScene.GetAllComponents<PlayerController>() )
		{
			if ( candidate.IsProxy )
			{
				continue;
			}

			if ( player is not null )
			{
				throw new InvalidOperationException( "Exactly one locally controlled PlayerController is required." );
			}

			player = candidate;
		}

		if ( player is null || manager.StreamingTarget is not null && manager.StreamingTarget != player.GameObject )
		{
			throw new InvalidOperationException( "The local player must be the VoxelManager streaming target." );
		}

		var start = player.WorldPosition;
		_driver?.Stop();
		_driver = new FigureEightDriver( player.GameObject, new Vector2( start.x, start.y ), speed, distance );
		EditorEvent.Register( _driver );
		player.WorldPosition = new Vector3( start.x, start.y, 0f );

		return $"Player figure-eight started at {speed} units/second with distance {distance}.";
	}

	private sealed class FigureEightDriver
	{
		private GameObject _target;
		private readonly Vector2 _center;
		private readonly float _speed;
		private readonly float _distance;
		private float _parameter;

		public FigureEightDriver( GameObject target, Vector2 center, float speed, float distance )
		{
			_target = target;
			_center = center;
			_speed = speed;
			_distance = distance;
		}

		public void Stop()
		{
			EditorEvent.Unregister( this );
			_target = null;
		}

		[EditorEvent.Frame]
		public void Update()
		{
			if ( !Game.IsPlaying || !_target.IsValid() )
			{
				Stop();
				return;
			}

			var tangentX = MathF.Cos( _parameter );
			var tangentY = MathF.Cos( 2f * _parameter );
			var tangentLength = MathF.Sqrt( tangentX * tangentX + tangentY * tangentY );
			_parameter += _speed * RealTime.Delta / (_distance * tangentLength);

			if ( _parameter >= MathF.Tau )
			{
				_parameter -= MathF.Tau;
			}

			var sine = MathF.Sin( _parameter );
			var cosine = MathF.Cos( _parameter );
			_target.WorldPosition = new Vector3(
				_center.x + _distance * sine,
				_center.y + _distance * sine * cosine,
				0f );
		}
	}
}
