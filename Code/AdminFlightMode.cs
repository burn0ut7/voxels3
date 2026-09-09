using Sandbox.Movement;

namespace Sandbox;

/// <summary>Host debug flight, integrated with the normal player movement owner.</summary>
public sealed class AdminFlightMode : MoveMode, PlayerController.IEvents
{
	public bool Flying { get; set; }
	public float Speed { get; set; } = 1000f;
	public bool CanAdmin => Networking.IsHost && !IsProxy && Controller.IsValid() && Controller.Enabled && Controller.Body.IsValid();
	private bool _restoreCollision;
	private bool _solidCollisions;

	public override int Score( PlayerController controller ) => Flying && CanAdmin ? 1000 : int.MinValue;

	public override void OnModeBegin()
	{
		_solidCollisions = Controller.Body.PhysicsBody.EnableSolidCollisions;
		_restoreCollision = true;
		Controller.Body.Velocity = Vector3.Zero;
	}

	public override void UpdateRigidBody( Rigidbody body )
	{
		body.Gravity = false;
		body.LinearDamping = 0;
		body.PhysicsBody.EnableSolidCollisions = false;
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		if ( !CanAdmin || AdminMenu.CapturesInput( Scene ) ) return Vector3.Zero;
		var direction = eyes * input;
		direction += Vector3.Up * ((Input.Down( "Jump" ) ? 1 : 0) - (Input.Down( "Duck" ) ? 1 : 0));
		return direction.ClampLength( 1 ) * Speed * (Input.Down( "Run" ) ? 3f : 1f);
	}

	public override void AddVelocity()
	{
		Controller.Body.Velocity = CanAdmin && !AdminMenu.CapturesInput( Scene )
			? Controller.WishVelocity : Vector3.Zero;
	}

	public override void OnModeEnd( MoveMode next ) => RestoreBody();
	protected override void OnDisabled() => RestoreBody();
	protected override void OnDestroy() => RestoreBody();

	private void RestoreBody()
	{
		if ( !_restoreCollision || !Controller.IsValid() || !Controller.Body.IsValid() ) return;
		if ( Controller.Body.PhysicsBody.IsValid() )
			Controller.Body.PhysicsBody.EnableSolidCollisions = _solidCollisions;
		Controller.Body.Gravity = true;
		Controller.Body.Velocity = Vector3.Zero;
		Controller.WishVelocity = Vector3.Zero;
		_restoreCollision = false;
	}

	void PlayerController.IEvents.PreInput()
	{
		if ( IsProxy || !AdminMenu.CapturesInput( Scene ) ) return;
		Input.AnalogMove = Vector3.Zero;
		Input.AnalogLook = default;
		Input.Clear( "Attack1" );
		Input.Clear( "Attack2" );
		Input.Clear( "Jump" );
		Input.Clear( "Duck" );
		Input.Clear( "Use" );
		Input.Clear( "View" );
		Controller.WishVelocity = Vector3.Zero;
	}
}
