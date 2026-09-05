/// <summary>
/// Inventory metadata used by Void Breach's weapon slots.
/// </summary>
public interface IVBWeaponCarryItem
{
	/// <summary>
	/// Amount consumed from the shared weapon carry-size budget.
	/// </summary>
	int CarrySize { get; }

	/// <summary>
	/// Determines whether this item belongs in a main weapon slot or in the
	/// dedicated melee slot.
	/// </summary>
	VBWeaponSlotCategory SlotCategory { get; }

	/// <summary>
	/// Placeholder items represent an empty weapon slot and do not count as
	/// carried weapons.
	/// </summary>
	bool IsWeaponSlotPlaceholder { get; }
}

public enum VBWeaponSlotCategory
{
	Main,
	Melee
}
