/// <summary>
/// Inventory metadata used by Void Breach's two generic weapon slots.
/// </summary>
public interface IVBWeaponCarryItem
{
	/// <summary>
	/// Amount consumed from the shared weapon carry-size budget.
	/// </summary>
	int CarrySize { get; }

	/// <summary>
	/// Placeholder items represent an empty weapon slot and do not count as
	/// carried weapons.
	/// </summary>
	bool IsWeaponSlotPlaceholder { get; }
}
