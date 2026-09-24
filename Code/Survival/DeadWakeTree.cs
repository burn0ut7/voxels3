using System;

namespace Sandbox;

/// <summary>Finite wood on the actual standing tree; one physical fall after depletion.</summary>
public sealed class DeadWakeTree : Component
{
	public const int ChopYield = 4;
	private sealed class CutProfile
	{
		public float Height { get; set; }
		public float[][][] Loops { get; set; }
	}
	private CutProfile _cutProfile;
	private Vector3[][] _cutLoops;

	protected override void OnStart()
	{
		_cutProfile = FileSystem.Mounted.ReadJson<CutProfile>( "models/deadwake/oak-cut-v1.json" );
		_cutLoops = _cutProfile.Loops.Select( loop => loop.Select( p => new Vector3( p[0], p[1], p[2] ) ).ToArray() ).ToArray();
	}
	[Property, ReadOnly] public int Branches { get; private set; } = 6;
	[Property, ReadOnly] public int ChopsRemaining { get; private set; } = 12;
	public bool Felled => ChopsRemaining <= 0;
	private GameObject _stump;
	private Vector3 _felledFrom;
	private bool _fallStarted;

	internal bool GatherBranch()
	{
		if ( !Networking.IsHost || Networking.IsActive || Felled || Branches <= 0 ) return false;
		Branches--;
		return true;
	}

	internal bool Chop( Vector3 playerPosition )
	{
		if ( !Networking.IsHost || Networking.IsActive || Felled ) return false;
		ChopsRemaining--;
		if ( !Felled ) return true;
		Branches = 0;
		_felledFrom = playerPosition;
		return true;
	}

	protected override void OnUpdate()
	{
		if ( Scene.IsEditor || Networking.IsActive || !Felled || _fallStarted ) return;
		_fallStarted = true;
		var standingPosition = WorldPosition;
		var standingRotation = WorldRotation;
		var lod = Components.Get<TreeModelLod>();
		if ( lod.IsValid() ) lod.StopDistanceTransitions();
		foreach ( var collider in GetComponentsInChildren<Collider>( true ) ) collider.Enabled = false;
		_stump = new GameObject( false, "Oak stump" ) { Parent = GameObject.Parent };
		_stump.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_stump.WorldPosition = standingPosition;
		_stump.WorldRotation = standingRotation;
		_stump.WorldScale = WorldScale;
		_stump.Components.Create<ModelRenderer>().Model = DeadWakePropModels.CreateStump( _cutLoops[0] );
		var stumpCollider = _stump.Components.Create<BoxCollider>();
		var minimum = _cutLoops[0].Aggregate( (a, b) => Vector3.Min( a, b ) ).WithZ( 0 );
		var maximum = _cutLoops[0].Aggregate( (a, b) => Vector3.Max( a, b ) );
		stumpCollider.Center = (minimum + maximum) * 0.5f;
		stumpCollider.Scale = maximum - minimum;
		_stump.Enabled = true;
		foreach ( var renderer in GetComponentsInChildren<ModelRenderer>( true ) )
			{
			if ( renderer.GameObject == GameObject ) renderer.LodOverride = 0;
			if ( !renderer.SceneObject.IsValid() ) continue;
			renderer.SceneObject.Attributes.Set( "TreeCutHeight", _cutProfile.Height );
			renderer.SceneObject.Attributes.Set( "TreeWind", Vector4.Zero );
		}
		var cut = new GameObject( false, "Severed trunk face" ) { Parent = GameObject };
		cut.LocalPosition = Vector3.Zero;
		cut.LocalRotation = Rotation.Identity;
		cut.LocalScale = Vector3.One;
		cut.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		cut.Components.Create<ModelRenderer>().Model = DeadWakePropModels.CreateFallenCut( _cutLoops );
		cut.Enabled = true;
		// A simple trunk capsule bounds the first felling simulation. Leaves are visual.
		var model = Components.Get<ModelRenderer>()?.Model;
		var height = model is null ? 320f : MathF.Max( 80f, model.Bounds.Maxs.z * 0.72f );
		var trunk = Components.Create<CapsuleCollider>();
		trunk.Start = Vector3.Up * 26f;
		trunk.End = Vector3.Up * height;
		trunk.Radius = 9f;
		var away = (standingPosition - _felledFrom).WithZ( 0 ).Normal;
		if ( away.LengthSquared < 0.01f ) away = Vector3.Forward;
		var axis = Vector3.Cross( Vector3.Up, away ).Normal;
		WorldRotation = Rotation.FromAxis( axis, 8f ) * standingRotation;
		var body = Components.Create<Rigidbody>();
		body.MassOverride = 200f;
		body.AngularVelocity = axis * 0.7f;
	}

	protected override void OnDestroy()
	{
		if ( _stump.IsValid() ) _stump.Destroy();
	}
}
