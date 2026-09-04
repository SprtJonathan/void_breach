using System.Linq;
using Sandbox;

/// <summary>
/// Maps the project's two weapon slots to the native s&amp;box inventory switch API.
/// Slot ownership, deployment and network authority remain handled by
/// <see cref="BaseInventoryComponent"/>.
/// </summary>
[Title( "Void Breach Weapon Selection" )]
[Category( "Void Breach/Weapons" )]
[Icon( "swap_horiz" )]
public sealed class VBWeaponSelectionController : Component
{
	public const int FirstWeaponSlot = 0;
	public const int WeaponSlotCount = 2;

	[RequireComponent]
	private BaseInventoryComponent Inventory { get; set; }

	protected override void OnUpdate()
	{
		if ( IsProxy || !Inventory.IsValid() || !Inventory.Enabled )
			return;

		if ( Input.Pressed( "Slot1" ) )
		{
			SelectSlot( FirstWeaponSlot );
			return;
		}

		if ( Input.Pressed( "Slot2" ) )
		{
			SelectSlot( FirstWeaponSlot + 1 );
			return;
		}

		if ( Input.Pressed( "SlotNext" ) )
		{
			CycleWeapon( 1 );
			return;
		}

		if ( Input.Pressed( "SlotPrev" ) )
			CycleWeapon( -1 );
	}

	private void SelectSlot( int slot )
	{
		var weapon = Inventory.Items
			.OfType<BaseCombatWeapon>()
			.Where( item => item.Slot == slot )
			.OrderBy( item => item.SlotOrder )
			.FirstOrDefault();

		SwitchTo( weapon );
	}

	private void CycleWeapon( int direction )
	{
		var weapons = Inventory.Items
			.OfType<BaseCombatWeapon>()
			.Where( item => item.Slot >= FirstWeaponSlot )
			.Where( item => item.Slot < FirstWeaponSlot + WeaponSlotCount )
			.OrderBy( item => item.Slot )
			.ThenBy( item => item.SlotOrder )
			.ToList();

		if ( weapons.Count == 0 )
			return;

		var currentIndex = weapons.IndexOf( Inventory.ActiveItem as BaseCombatWeapon );
		var nextIndex = currentIndex < 0
			? direction > 0 ? 0 : weapons.Count - 1
			: (currentIndex + direction + weapons.Count) % weapons.Count;

		SwitchTo( weapons[nextIndex] );
	}

	private void SwitchTo( BaseCombatWeapon weapon )
	{
		if ( !weapon.IsValid() || weapon == Inventory.ActiveItem || !weapon.CanSwitchTo() )
			return;

		Inventory.Switch( weapon, allowHolster: false );
	}
}
