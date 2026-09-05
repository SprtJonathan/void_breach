using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

/// <summary>
/// Extends the native inventory with Void Breach's three weapon slots and
/// shared five-size carrying rule. Equipment items remain governed by the
/// native inventory.
/// </summary>
[Title( "Void Breach Weapon Inventory" )]
[Category( "Void Breach/Weapons" )]
[Icon( "inventory_2" )]
public sealed class VBWeaponInventoryComponent : BaseInventoryComponent
{
	[Property, Group( "Weapon Carrying" ), Range( 1, 3 )]
	public int MaximumWeaponCount { get; set; } = 3;

	[Property, Group( "Weapon Carrying" ), Range( 1, 10 )]
	public int MaximumWeaponCarrySize { get; set; } = 5;

	public IEnumerable<BaseCombatWeapon> CarriedWeapons => Items
		.OfType<BaseCombatWeapon>()
		.Where( IsCarriedWeapon );

	public int CurrentWeaponCarrySize => CarriedWeapons.Sum( GetCarrySize );

	protected override bool OnAdding( BaseInventoryItem item, int slot )
	{
		if ( !base.OnAdding( item, slot ) )
			return false;

		if ( item is not BaseCombatWeapon weapon
			|| weapon is not IVBWeaponCarryItem )
			return true;

		var existingWeapons = Items
			.OfType<BaseCombatWeapon>()
			.Where( existing => existing != weapon );

		if ( IsPlaceholder( weapon ) )
		{
			return !existingWeapons.Any( IsPlaceholder );
		}

		var weightedWeapon = (IVBWeaponCarryItem)weapon;
		var isMelee = weightedWeapon.SlotCategory == VBWeaponSlotCategory.Melee;
		var isValidSlot = isMelee
			? slot == VBWeaponSelectionController.MeleeWeaponSlot
			: slot >= VBWeaponSelectionController.FirstWeaponSlot
				&& slot < VBWeaponSelectionController.FirstWeaponSlot
					+ VBWeaponSelectionController.PrimaryWeaponSlotCount;

		if ( !isValidSlot )
			return false;

		var existingCarriedWeapons = existingWeapons
			.Where( IsCarriedWeapon );

		if ( existingCarriedWeapons.Count() >= MaximumWeaponCount )
			return false;

		var sameCategoryCount = existingCarriedWeapons
			.OfType<IVBWeaponCarryItem>()
			.Count( existing => existing.SlotCategory == weightedWeapon.SlotCategory );

		if ( isMelee && sameCategoryCount >= 1 )
			return false;

		if ( !isMelee
			&& sameCategoryCount >= VBWeaponSelectionController.PrimaryWeaponSlotCount )
			return false;

		var existingCarrySize = existingCarriedWeapons.Sum( GetCarrySize );
		return existingCarrySize + GetCarrySize( weapon ) <= MaximumWeaponCarrySize;
	}

	public static int GetCarrySize( BaseCombatWeapon weapon )
	{
		if ( !weapon.IsValid() )
			return 0;

		return weapon is IVBWeaponCarryItem weighted
			? Math.Max( 0, weighted.CarrySize )
			: 0;
	}

	public static bool IsCarriedWeapon( BaseCombatWeapon weapon )
	{
		return weapon is IVBWeaponCarryItem { IsWeaponSlotPlaceholder: false };
	}

	public static bool IsPlaceholder( BaseCombatWeapon weapon )
	{
		return weapon is IVBWeaponCarryItem { IsWeaponSlotPlaceholder: true };
	}
}
