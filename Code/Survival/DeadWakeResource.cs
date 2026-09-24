namespace Sandbox;

/// <summary>One collectible world stack; its quantity transfers once into the inventory.</summary>
public sealed class DeadWakeResource : Component
{
	[Property] public DeadWakeItem Item { get; set; } = DeadWakeItem.Stone;
	[Property] public int Quantity { get; set; } = 2;
	public DeadWakeStack Stack => new( Item, Quantity );
	internal bool Consume()
	{
		if ( !Networking.IsHost || Networking.IsActive || Quantity <= 0 ) return false;
		Quantity = 0;
		GameObject.Destroy();
		return true;
	}
}
