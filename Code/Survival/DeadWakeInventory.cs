using System;

namespace Sandbox;

public enum DeadWakeItem { None, Wood, Stone, StoneAxe }

public readonly record struct DeadWakeStack( DeadWakeItem Item, int Quantity )
{
	public bool IsEmpty => Item == DeadWakeItem.None || Quantity <= 0;
	public string Name => Item switch { DeadWakeItem.Wood => "Wood", DeadWakeItem.Stone => "Stone", DeadWakeItem.StoneAxe => "Stone axe", _ => "Empty" };
	public string Icon => Item switch
	{
		DeadWakeItem.Wood => "ui/deadwake/wood.png",
		DeadWakeItem.Stone => "ui/deadwake/stone.png",
		DeadWakeItem.StoneAxe => "ui/deadwake/stone-axe.png",
		_ => ""
	};
	public static int Limit( DeadWakeItem item ) => item == DeadWakeItem.StoneAxe ? 1 : item == DeadWakeItem.None ? 0 : 64;
}

/// <summary>One session inventory, including the stack being moved by the cursor.</summary>
public sealed class DeadWakeInventory
{
	public const int HotbarSlots = 9;
	public const int SlotCount = 36;
	public const int AxeWoodCost = 4;
	public const int AxeStoneCost = 2;
	private DeadWakeStack[] _slots = new DeadWakeStack[SlotCount];
	private int _cursorOrigin = -1;
	public IReadOnlyList<DeadWakeStack> Slots => _slots;
	public DeadWakeStack Cursor { get; private set; }
	public int Selected { get; private set; }
	public int Revision { get; private set; }
	public DeadWakeStack Equipped => _slots[Selected];
	public DeadWakeStack this[int index] => index >= 0 && index < SlotCount ? _slots[index] : default;
	public int Count( DeadWakeItem item ) => _slots.Sum( stack => stack.Item == item ? stack.Quantity : 0 ) + (Cursor.Item == item ? Cursor.Quantity : 0);
	public bool HasAxeIngredients => Count( DeadWakeItem.Wood ) >= AxeWoodCost && Count( DeadWakeItem.Stone ) >= AxeStoneCost;

	internal void Reset()
	{
		_slots = new DeadWakeStack[SlotCount];
		Cursor = default;
		Selected = 0;
		_cursorOrigin = -1;
		Revision++;
	}

	public void Select( int index )
	{
		if ( index < 0 || index >= HotbarSlots || index == Selected ) return;
		Selected = index;
		Revision++;
	}

	public bool CanAdd( DeadWakeItem item, int quantity )
	{
		if ( item == DeadWakeItem.None || quantity <= 0 ) return false;
		var capacity = 0;
		foreach ( var stack in _slots )
			if ( stack.IsEmpty || stack.Item == item ) capacity += DeadWakeStack.Limit( item ) - stack.Quantity;
		return capacity >= quantity;
	}

	public bool TryAdd( DeadWakeItem item, int quantity )
	{
		if ( !CanAdd( item, quantity ) ) return false;
		AddTo( _slots, item, quantity, 0, SlotCount );
		Revision++;
		return true;
	}

	public bool TryRemoveSelected( int quantity )
	{
		var stack = Equipped;
		if ( quantity <= 0 || stack.IsEmpty || stack.Quantity < quantity ) return false;
		_slots[Selected] = stack.Quantity == quantity ? default : stack with { Quantity = stack.Quantity - quantity };
		Revision++;
		return true;
	}

	public bool TryCraftAxe( out string message )
	{
		if ( !Cursor.IsEmpty ) { message = "Place the held stack before crafting."; return false; }
		if ( !HasAxeIngredients ) { message = "Stone axe needs 4 wood and 2 stone."; return false; }
		// Stage both ingredient debits and the result so a full inventory loses nothing.
		var next = _slots.ToArray();
		foreach ( var ingredient in new[] { new DeadWakeStack( DeadWakeItem.Wood, AxeWoodCost ), new DeadWakeStack( DeadWakeItem.Stone, AxeStoneCost ) } )
		{
			var left = ingredient.Quantity;
			for ( var i = 0; i < SlotCount && left > 0; i++ )
			{
				if ( next[i].Item != ingredient.Item ) continue;
				var taken = Math.Min( left, next[i].Quantity );
				left -= taken;
				next[i] = next[i].Quantity == taken ? default : next[i] with { Quantity = next[i].Quantity - taken };
			}
		}
		if ( AddTo( next, DeadWakeItem.StoneAxe, 1, 0, SlotCount ) != 0 )
		{
			message = "Make room for the stone axe. Ingredients were kept.";
			return false;
		}
		_slots = next;
		Revision++;
		message = "Stone axe crafted. Equip it from the hotbar to chop a tree.";
		return true;
	}

	public void Click( int index, bool right, bool quickMove )
	{
		if ( index < 0 || index >= SlotCount ) return;
		var slot = _slots[index];
		if ( quickMove && Cursor.IsEmpty && !slot.IsEmpty )
		{
			var remaining = AddTo( _slots, slot.Item, slot.Quantity,
				index < HotbarSlots ? HotbarSlots : 0, index < HotbarSlots ? SlotCount : HotbarSlots );
			_slots[index] = remaining == 0 ? default : slot with { Quantity = remaining };
		}
		else if ( Cursor.IsEmpty )
		{
			if ( slot.IsEmpty ) return;
			var quantity = right ? (slot.Quantity + 1) / 2 : slot.Quantity;
			Cursor = slot with { Quantity = quantity };
			_cursorOrigin = index;
			_slots[index] = quantity == slot.Quantity ? default : slot with { Quantity = slot.Quantity - quantity };
		}
		else if ( slot.IsEmpty || slot.Item == Cursor.Item )
		{
			var quantity = Math.Min( right ? 1 : Cursor.Quantity, DeadWakeStack.Limit( Cursor.Item ) - slot.Quantity );
			if ( quantity <= 0 ) return;
			_slots[index] = new DeadWakeStack( Cursor.Item, slot.Quantity + quantity );
			Cursor = Cursor.Quantity == quantity ? default : Cursor with { Quantity = Cursor.Quantity - quantity };
		}
		else if ( !right )
		{
			_slots[index] = Cursor;
			Cursor = slot;
		}
		else return;
		Revision++;
	}

	public bool ReturnCursor()
	{
		if ( Cursor.IsEmpty ) return true;
		if ( !CanAdd( Cursor.Item, Cursor.Quantity ) ) return false;
		var left = Cursor.Quantity;
		if ( _cursorOrigin >= 0 ) left = AddTo( _slots, Cursor.Item, left, _cursorOrigin, _cursorOrigin + 1 );
		if ( left > 0 ) AddTo( _slots, Cursor.Item, left, 0, SlotCount );
		Cursor = default;
		_cursorOrigin = -1;
		Revision++;
		return true;
	}

	private static int AddTo( DeadWakeStack[] slots, DeadWakeItem item, int quantity, int first, int end )
	{
		// Existing stacks first; then empty slots, always in visible slot order.
		for ( var pass = 0; pass < 2 && quantity > 0; pass++ )
		for ( var i = first; i < end && quantity > 0; i++ )
		{
			var stack = slots[i];
			if ( pass == 0 ? stack.IsEmpty || stack.Item != item : !stack.IsEmpty ) continue;
			var added = Math.Min( quantity, DeadWakeStack.Limit( item ) - stack.Quantity );
			if ( added <= 0 ) continue;
			slots[i] = new DeadWakeStack( item, stack.Quantity + added );
			quantity -= added;
		}
		return quantity;
	}
}
