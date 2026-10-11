/// <summary>
/// Describes an inventory item that owns a fixed logical slot.
/// The inventory remains responsible for enforcing the policy so every add,
/// move, transfer and drop path observes the same rules.
/// </summary>
public interface IVBInventorySlotPolicy
{
	int RequiredInventorySlot { get; }
	bool LocksInventorySlot { get; }
	bool IsInventoryBound { get; }
}
