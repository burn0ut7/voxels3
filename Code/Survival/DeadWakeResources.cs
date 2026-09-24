using System;

namespace Sandbox;

/// <summary>Bounded stone placement and world item drops, derived from real terrain support.</summary>
public sealed class DeadWakeResources : Component
{
	[Property, ReadOnly] public int Placed { get; private set; }
	[Property, ReadOnly] public string Status { get; private set; } = "Waiting for safe terrain";
	public Model HatchetModel { get; private set; }
	private readonly List<GameObject> _objects = new();
	private Model[] _models;
	private PlayerController _player;
	private VoxelManager _terrain;
	private Vector3 _origin;
	private bool _started;
	private int _attempts;

	protected override void OnStart()
	{
		_terrain = Components.Get<VoxelManager>();
		_player = Scene.GetAllComponents<PlayerController>().FirstOrDefault( player => !player.IsProxy );
		// Authored tree art and its solid trunk remain the existing owners.
		foreach ( var tree in Scene.GetAllComponents<TreeModelLod>() )
			if ( !tree.Components.Get<DeadWakeTree>().IsValid() ) tree.Components.Create<DeadWakeTree>();
	}

	protected override void OnUpdate()
	{
		if ( Scene.IsEditor || !Networking.IsHost || Networking.IsActive || !_terrain.IsValid() || !_player.IsValid() || Placed >= 12 ) return;
		if ( _attempts >= 24 )
		{
			if ( !_player.IsOnGround || (_player.WorldPosition - _origin).WithZ( 0 ).Length < 512f ) return;
			_origin = _player.WorldPosition;
			_attempts = 0;
		}
		if ( !_started )
		{
			if ( !_player.IsOnGround ) return;
			_origin = _player.WorldPosition;
			_models = DeadWakePropModels.CreateResources();
			HatchetModel = DeadWakePropModels.CreateHatchet();
			_started = true;
		}
		for ( var step = 0; step < 2 && Placed < 12 && _attempts < 24; step++ )
		{
			var index = _attempts++;
			var point = _origin + new Vector3( 140f + (index / 2) * 92f, index % 2 == 0 ? -80f : 80f, 0f );
			var bounds = new BBox( point - new Vector3( 50, 50, 128 ), point + new Vector3( 50, 50, 128 ) );
			if ( !_terrain.IsTerrainCollisionReady( bounds ) ) { _attempts--; return; }
			if ( !TryGround( point, 128, out var position, out var rotation, index * 137.5f ) ) continue;
			if ( _objects.Any( item => item.IsValid() && (item.WorldPosition - position).Length < 80f ) ) continue;
			SpawnStack( new DeadWakeStack( DeadWakeItem.Stone, 2 ), position, rotation );
			Placed++;
		}
		Status = Placed == 12 ? "Loose stone nearby" : _attempts == 24 ? "Explore open dry ground for more stone" : "Placing stone";
	}

	private bool TryGround( Vector3 point, float height, out Vector3 position, out Rotation rotation, float yaw = 0 )
	{
		position = default;
		rotation = Rotation.Identity;
		var hit = Scene.Trace.Ray( point + Vector3.Up * height, point - Vector3.Up * height ).WithTag( "voxel_terrain" ).Run();
		if ( !hit.Hit || hit.StartedSolid || hit.Normal.z < 0.8f ||
			!_terrain.TryGetSurfaceWaterLevel( hit.HitPosition, out var water ) || hit.HitPosition.z <= water + 16f ) return false;
		rotation = Rotation.LookAt( Vector3.Cross( Vector3.Right, hit.Normal ).Normal, hit.Normal ) * Rotation.FromYaw( yaw );
		var offset = -0.5f;
		for ( var edge = 0; edge < 4; edge++ )
		{
			var offsetPoint = edge < 2 ? new Vector3( edge == 0 ? -18 : 18, 0, 0 ) : new Vector3( 0, edge == 2 ? -12 : 12, 0 );
			var foot = hit.HitPosition + rotation * offsetPoint;
			var support = Scene.Trace.Ray( foot + hit.Normal * 8, foot - hit.Normal * 8 ).WithTag( "voxel_terrain" ).Run();
			if ( !support.Hit || support.StartedSolid || (support.HitPosition - foot).Length > 4 ||
				!_terrain.TryGetSurfaceWaterLevel( support.HitPosition, out water ) || support.HitPosition.z <= water + 16 ) return false;
			offset = MathF.Min( offset, Vector3.Dot( support.HitPosition - foot, hit.Normal ) - 0.5f );
		}
		position = hit.HitPosition + hit.Normal * offset;
		return true;
	}

	internal bool TryDrop( PlayerController player, DeadWakeStack stack )
	{
		if ( !_started || stack.IsEmpty || stack.Quantity > DeadWakeStack.Limit( stack.Item ) || !Networking.IsHost || Networking.IsActive ) return false;
		_objects.RemoveAll( item => !item.IsValid() );
		if ( _objects.Count >= 128 ) return false;
		var forward = player.EyeTransform.Rotation.Forward.WithZ( 0 ).Normal;
		if ( forward.LengthSquared < 0.01f ) forward = Vector3.Forward;
		for ( var i = 0; i < 8; i++ )
		{
			var direction = Rotation.FromYaw( i * 45 ) * forward;
			var point = player.WorldPosition + direction * 64;
			if ( !TryGround( point, 64, out var position, out var rotation ) ) continue;
			var path = Scene.Trace.Ray( player.EyeTransform.Position, position + Vector3.Up * 10 )
				.IgnoreGameObjectHierarchy( player.GameObject ).WithoutTags( "deadwake_viewmodel" ).Run();
			if ( path.StartedSolid || path.Hit ) continue;
			if ( _objects.Any( item => item.IsValid() && (item.WorldPosition - position).Length < 48f ) ) continue;
			SpawnStack( stack, position, rotation );
			return true;
		}
		return false;
	}

	private void SpawnStack( DeadWakeStack stack, Vector3 position, Rotation rotation )
	{
		var item = new GameObject( false, stack.Name );
		item.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		item.Parent = GameObject;
		item.WorldPosition = position;
		item.WorldRotation = rotation;
		var renderer = item.Components.Create<ModelRenderer>();
		renderer.Model = stack.Item == DeadWakeItem.StoneAxe ? HatchetModel : _models[stack.Item == DeadWakeItem.Wood ? 0 : 1];
		var collider = item.Components.Create<BoxCollider>();
		collider.Center = new Vector3( 0, 0, 4 );
		collider.Scale = new Vector3( 34, 24, 8 );
		if ( stack.Item == DeadWakeItem.StoneAxe )
		{
			// Lay the dropped axe down while the held model retains its normal pose.
			var view = new GameObject( false, "Dropped axe" ) { Parent = item };
			view.LocalPosition = new Vector3( -5.25f, 2.2f, 2 );
			view.LocalRotation = Rotation.From( 0, 0, 90 ) * Rotation.FromPitch( 90 );
			view.Components.Create<ModelRenderer>().Model = HatchetModel;
			view.Enabled = true;
			renderer.Destroy();
			collider.Center = new Vector3( 0, 0, 2 );
			collider.Scale = new Vector3( 36, 10, 4 );
		}
		var resource = item.Components.Create<DeadWakeResource>();
		resource.Item = stack.Item;
		resource.Quantity = stack.Quantity;
		_objects.Add( item );
		item.Enabled = true;
	}

	protected override void OnDestroy()
	{
		foreach ( var item in _objects ) if ( item.IsValid() ) item.Destroy();
		_objects.Clear();
		_models = null;
		HatchetModel = null;
	}
}
