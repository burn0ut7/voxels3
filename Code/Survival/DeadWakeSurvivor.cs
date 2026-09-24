using System;

namespace Sandbox;

/// <summary>Local survival input, inventory transactions and contact-timed tree harvesting.</summary>
public sealed class DeadWakeSurvivor : Component, PlayerController.IEvents
{
	public const float Reach = 128f;
	public const float SwingInterval = 0.7f;
	private const float ContactTime = 0.2f;
	private const float RecoveryTime = 0.48f;
	private static readonly string[] SlotActions = ["Slot1", "Slot2", "Slot3", "Slot4", "Slot5", "Slot6", "Slot7", "Slot8", "Slot9"];
	private static DeadWakeSurvivor _local;
	[RequireComponent] public PlayerController Controller { get; set; }
	[Property, ReadOnly] public DeadWakeInventory Inventory { get; } = new();
	[Property, ReadOnly] public bool InventoryOpen { get; private set; }
	[Property, ReadOnly] public string Status { get; private set; } = "Gather branches from a standing tree and pick up loose stone.";
	public string TargetHint { get; private set; } = "";
	public int HoveredSlot { get; set; } = -1;
	public string HoveredItem => HoveredSlot >= 0 && !Inventory[HoveredSlot].IsEmpty ? Inventory[HoveredSlot].Name : "";
	public string NextStep => Inventory.Count( DeadWakeItem.StoneAxe ) == 0
		? "Gather wood from a tree and loose stone. Open your inventory to craft an axe."
		: Inventory.Equipped.Item == DeadWakeItem.StoneAxe ? "Chop a standing tree for wood." : "Equip your stone axe from the hotbar.";
	private float _nextStrike, _nextGather, _nextLook;
	private float _swingStarted = -10f, _closedAt = -1f;
	private bool _pendingStrike, _networkRefused, _capturedControls, _savedInputControls;
	private int _swingSlot, _hintSignature;
	private GameObject _toolView;
	private DeadWakeResources _resources;

	public static bool CapturesInput( Scene scene ) => _local.IsValid() && _local.Active && _local.Scene == scene &&
		(_local.InventoryOpen || RealTime.Now <= _local._closedAt + 0.15f);

	protected override void OnStart()
	{
		Inventory.Reset();
		InventoryOpen = false;
		Status = "Gather branches from a standing tree and pick up loose stone.";
		_resources = Scene.GetAllComponents<DeadWakeResources>().FirstOrDefault();
		if ( Controller.IsValid() && !IsProxy ) { _local = this; Controller.ThirdPerson = false; }
	}

	void PlayerController.IEvents.PreInput()
	{
		if ( IsProxy ) return;
		if ( !Networking.IsActive ) Input.Clear( "View" );
		if ( AdminMenu.CapturesInput( Scene ) ) AdminMenu.SuppressPlayerInput( Controller );
	}

	protected override void OnUpdate()
	{
		if ( Scene.IsEditor || IsProxy || !Controller.IsValid() ) return;
		_local = this;
		if ( Networking.IsActive )
		{
			_pendingStrike = false;
			InventoryOpen = !Inventory.ReturnCursor();
			Status = InventoryOpen
				? "Held item is safe. Leave the network session to arrange your inventory and resume offline play."
				: "Opening prototype is offline only. Leave the network session to play.";
			TargetHint = "";
			_networkRefused = true;
			if ( _toolView.IsValid() ) _toolView.Enabled = false;
			CaptureMovement( AdminMenu.CapturesInput( Scene ) );
			return;
		}
		if ( _networkRefused )
		{
			_networkRefused = false;
			Controller.ThirdPerson = false;
			Status = "Offline play resumed. Your inventory is unchanged.";
		}
		if ( Input.Down( "Inventory" ) && Input.Pressed( "Inventory" ) && (InventoryOpen || !AdminMenu.CapturesInput( Scene )) )
		{
			if ( InventoryOpen ) CloseInventory();
			else { InventoryOpen = true; _pendingStrike = false; HoveredSlot = -1; }
		}
		if ( InventoryOpen && Input.EscapePressed ) { Input.EscapePressed = false; CloseInventory(); }
		CaptureMovement( AdminMenu.CapturesInput( Scene ) );
		if ( AdminMenu.CapturesInput( Scene ) )
		{
			_pendingStrike = false;
			TargetHint = "";
			_hintSignature = 0;
			if ( _toolView.IsValid() ) _toolView.Enabled = false;
			return;
		}
		if ( _pendingStrike && (Inventory.Selected != _swingSlot || Inventory.Equipped.Item != DeadWakeItem.StoneAxe) ) _pendingStrike = false;
		if ( _pendingStrike && Time.Now >= _swingStarted + ContactTime )
		{
			_pendingStrike = false;
			var target = FindTarget();
			if ( !target.Tree.IsValid() || target.Tree.Felled ) Status = "Aim at a nearby standing tree.";
			else if ( !Inventory.CanAdd( DeadWakeItem.Wood, DeadWakeTree.ChopYield ) ) Status = "Inventory full. The tree is unchanged.";
			else if ( target.Tree.Chop( Controller.EyeTransform.Position ) )
			{
				Inventory.TryAdd( DeadWakeItem.Wood, DeadWakeTree.ChopYield );
				Status = target.Tree.Felled ? $"+{DeadWakeTree.ChopYield} wood · Tree felled. Stand clear." : $"+{DeadWakeTree.ChopYield} wood";
			}
		}
		UpdateToolView();
		if ( Time.Now < _nextLook ) return;
		_nextLook = Time.Now + 0.1f;
		var look = FindTarget();
		var signature = HashCode.Combine( look.Pickup, look.Tree, look.Pickup.IsValid() ? look.Pickup.Quantity : 0,
			look.Tree.IsValid() ? look.Tree.Branches : 0, look.Tree.IsValid() ? look.Tree.ChopsRemaining : 0, Inventory.Equipped.Item );
		if ( signature == _hintSignature ) return;
		_hintSignature = signature;
		TargetHint = look.Pickup.IsValid() ? $"{Input.GetButtonOrigin( "Use" )} Take {look.Pickup.Stack.Name} ×{look.Pickup.Quantity}"
			: !look.Tree.IsValid() || look.Tree.Felled ? ""
			: Inventory.Equipped.Item == DeadWakeItem.StoneAxe ? $"{Input.GetButtonOrigin( "Attack1" )} Chop tree · {look.Tree.ChopsRemaining} strikes left"
			: look.Tree.Branches > 0 ? $"{Input.GetButtonOrigin( "Use" )} Gather branch · +1 wood" : "Branches gathered · Use a stone axe to chop this tree";
	}

	protected override void OnFixedUpdate()
	{
		if ( Scene.IsEditor || IsProxy || !Controller.IsValid() || Networking.IsActive || Input.Suppressed ||
			AdminMenu.CapturesInput( Scene ) || Input.EscapePressed || Input.Down( "Inventory" ) ||
			Input.Down( "Menu" ) || Input.Down( "BiomeDebug" ) ) return;
		// The engine accumulates action edges and wheel input for each simulation tick.
		// Poll once per tick instead of allocating action lookups at render frequency.
		for ( var i = 0; i < SlotActions.Length; i++ )
			if ( Input.Down( SlotActions[i] ) && Input.Pressed( SlotActions[i] ) ) Inventory.Select( i );
		if ( (Input.Down( "SlotNext" ) && Input.Pressed( "SlotNext" )) || Input.MouseWheel.y < 0 ) Inventory.Select( (Inventory.Selected + 1) % 9 );
		if ( (Input.Down( "SlotPrev" ) && Input.Pressed( "SlotPrev" )) || Input.MouseWheel.y > 0 ) Inventory.Select( (Inventory.Selected + 8) % 9 );
		if ( Time.Now >= _nextGather && Input.Down( "Use" ) && Input.Pressed( "Use" ) ) Gather();
		if ( Time.Now >= _nextStrike && Input.Down( "Attack1" ) && !Input.Down( "Attack2" ) ) BeginSwing();
		if ( !Inventory.Equipped.IsEmpty && Input.Down( "Drop" ) && Input.Pressed( "Drop" ) ) DropSelected();
	}

	private void CaptureMovement( bool capture )
	{
		if ( !Controller.IsValid() ) return;
		if ( capture && !_capturedControls ) _savedInputControls = Controller.UseInputControls;
		if ( capture ) { Controller.UseInputControls = false; Controller.WishVelocity = Vector3.Zero; }
		else if ( _capturedControls ) Controller.UseInputControls = _savedInputControls;
		_capturedControls = capture;
	}

	protected override void OnDisabled()
	{
		_pendingStrike = false;
		_swingStarted = -10f;
		CaptureMovement( false );
		if ( _toolView.IsValid() ) _toolView.Enabled = false;
	}

	public void CloseInventory()
	{
		if ( !Inventory.ReturnCursor() ) { Status = "Place the held stack before closing."; return; }
		InventoryOpen = false;
		HoveredSlot = -1;
		_closedAt = RealTime.Now;
	}

	public void ClickSlot( int index, bool right, bool quickMove )
	{
		if ( !InventoryOpen || IsProxy || !Networking.IsHost || Networking.IsActive ) return;
		Inventory.Click( index, right, quickMove );
	}

	public void CraftAxe()
	{
		if ( !InventoryOpen || IsProxy || !Networking.IsHost || Networking.IsActive ) return;
		Inventory.TryCraftAxe( out var message );
		Status = message;
	}

	private (DeadWakeResource Pickup, DeadWakeTree Tree) FindTarget()
	{
		var eye = Controller.EyeTransform;
		var hit = Scene.Trace.Ray( eye.Position, eye.Position + eye.Rotation.Forward * Reach )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "deadwake_viewmodel" ).Run();
		if ( !hit.Hit || hit.StartedSolid || !hit.GameObject.IsValid() ) return default;
		var pickup = hit.GameObject.Components.Get<DeadWakeResource>();
		if ( pickup.IsValid() ) return (pickup, null);
		for ( var obj = hit.GameObject; obj.IsValid(); obj = obj.Parent )
		{
			var tree = obj.Components.Get<DeadWakeTree>();
			if ( tree.IsValid() ) return (null, tree);
		}
		return default;
	}

	private void Gather()
	{
		if ( !Networking.IsHost || Networking.IsActive ) return;
		var target = FindTarget();
		if ( target.Pickup.IsValid() )
		{
			var stack = target.Pickup.Stack;
			if ( !Inventory.CanAdd( stack.Item, stack.Quantity ) ) { Status = "Inventory full. Item left in the world."; return; }
			if ( !target.Pickup.Consume() ) return;
			Inventory.TryAdd( stack.Item, stack.Quantity );
			Status = $"+{stack.Quantity} {stack.Name.ToLowerInvariant()}";
		}
		else if ( target.Tree.IsValid() && !target.Tree.Felled )
		{
			if ( target.Tree.Branches <= 0 ) { Status = "No reachable branches left. Craft an axe to chop the trunk."; return; }
			if ( !Inventory.CanAdd( DeadWakeItem.Wood, 1 ) ) { Status = "Inventory full. Branch left on the tree."; return; }
			if ( !target.Tree.GatherBranch() ) return;
			Inventory.TryAdd( DeadWakeItem.Wood, 1 );
			_nextGather = Time.Now + 0.8f;
			Status = "+1 wood · Branch gathered";
		}
		else Status = "Look at a nearby tree or loose stone.";
	}

	private void BeginSwing()
	{
		if ( !Networking.IsHost || Networking.IsActive ) return;
		_nextStrike = Time.Now + SwingInterval;
		if ( Inventory.Equipped.Item != DeadWakeItem.StoneAxe ) { Status = "Equip a stone axe to chop the trunk, or gather branches by hand."; return; }
		Status = "";
		_swingStarted = Time.Now;
		_swingSlot = Inventory.Selected;
		_pendingStrike = true;
	}

	private void DropSelected()
	{
		if ( !Networking.IsHost || Networking.IsActive || !_resources.IsValid() || Inventory.Equipped.IsEmpty ) return;
		var stack = Inventory.Equipped;
		var amount = Input.Down( "Run" ) ? stack.Quantity : 1;
		if ( !_resources.TryDrop( Controller, stack with { Quantity = amount } ) ) { Status = "No clear ground to drop this item. It remains in your inventory."; return; }
		Inventory.TryRemoveSelected( amount );
		_pendingStrike = false;
		Status = $"Dropped {amount} {stack.Name.ToLowerInvariant()}";
	}

	private void UpdateToolView()
	{
		if ( Inventory.Equipped.Item != DeadWakeItem.StoneAxe || !_resources.IsValid() || _resources.HatchetModel is null )
		{
			if ( _toolView.IsValid() ) _toolView.Enabled = false;
			return;
		}
		if ( !_toolView.IsValid() )
		{
			_toolView = new GameObject( false, "Stone axe view" ) { Parent = GameObject };
			_toolView.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_toolView.Tags.Add( "deadwake_viewmodel" );
			_toolView.Components.Create<ModelRenderer>().Model = _resources.HatchetModel;
		}
		var age = Time.Now - _swingStarted;
		var position = new Vector3( 34, -14, -17 );
		var angles = new Angles( -8, 55, -10 );
		if ( age >= 0 && age < RecoveryTime )
		{
			// Short anticipation, contact, then eased recovery to the same resting pose.
			var windup = new Vector3( 32, -18, -13 );
			var contact = new Vector3( 38, -5, -11 );
			if ( age < 0.1f )
			{
				var t = age / 0.1f;
				position = Vector3.Lerp( position, windup, t );
				angles = new Angles( -8 - t * 18, 55, -10 - t * 10 );
			}
			else if ( age < ContactTime )
			{
				var t = (age - 0.1f) / (ContactTime - 0.1f);
				position = Vector3.Lerp( windup, contact, t );
				angles = new Angles( -26 + t * 64, 55 - t * 20, -20 + t * 12 );
			}
			else
			{
				var t = (age - ContactTime) / (RecoveryTime - ContactTime);
				t = t * t * (3 - 2 * t);
				position = Vector3.Lerp( contact, position, t );
				angles = new Angles( 38 - t * 46, 35 + t * 20, -8 - t * 2 );
			}
		}
		var eye = Controller.EyeTransform;
		_toolView.WorldScale = Vector3.One * 0.65f;
		_toolView.WorldPosition = eye.Position + eye.Rotation * position;
		_toolView.WorldRotation = eye.Rotation * Rotation.From( angles );
		_toolView.Enabled = !Controller.ThirdPerson;
	}

	protected override void OnDestroy()
	{
		CaptureMovement( false );
		if ( _toolView.IsValid() ) _toolView.Destroy();
		if ( _local == this ) _local = null;
	}
}
