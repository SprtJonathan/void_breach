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

	/// <summary>Native touch sweep: take a free slot or duplicate ammunition, never exchange weapons.</summary>
	public override void PickupWorldItem( BaseInventoryItem item )
	{
		if ( item is not VBCombatWeapon incoming || !GetComponent<PlayerController>().IsValid() )
		{
			base.PickupWorldItem( item );
			return;
		}
		if ( !Networking.IsHost ) { if ( !IsProxy ) RequestWeaponPickup( incoming, false ); return; }
		PickupWeapon( incoming, false );
	}

	public bool CanSwapWeapon( VBCombatWeapon incoming ) => incoming.IsValid() && incoming.Inventory is null
		&& ActiveItem is VBCombatWeapon outgoing && outgoing != incoming
		&& outgoing.Slot >= 0 && outgoing.Slot < VBWeaponSelectionController.PrimaryWeaponSlotCount
		&& FitsWeapon( incoming, outgoing );

	public void SwapWeapon( VBCombatWeapon incoming )
	{
		if ( !Networking.IsHost ) { if ( !IsProxy ) RequestWeaponPickup( incoming, true ); return; }
		PickupWeapon( incoming, true );
	}

	private bool FitsWeapon( VBCombatWeapon incoming, VBCombatWeapon outgoing = null )
	{
		var retained = CarriedWeapons.Where( gun => gun != outgoing ).ToArray();
		return retained.Length + 1 <= MaximumWeaponCount
			&& retained.Sum( GetCarrySize ) + GetCarrySize( incoming ) <= MaximumWeaponCarrySize;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestWeaponPickup( VBCombatWeapon incoming, bool swap ) => PickupWeapon( incoming, swap );

	private void PickupWeapon( VBCombatWeapon incoming, bool swap )
	{
		var player = GetComponent<PlayerController>();
		var health = GetComponent<VBHealthComponent>();
		if ( !Networking.IsHost || !player.IsValid() || !player.Enabled
			|| health.IsValid() && (health.IsDead || health.IsDowned)
			|| !incoming.IsValid() || incoming.Scene != Scene || !CanPickupWorldItem( incoming )
			|| Vector3.DistanceBetween( WorldPosition, incoming.WorldPosition ) > (swap ? 120f : PickupRadius + 64f) ) return;
		var body = incoming.GetComponent<Rigidbody>();
		var pickupPoint = body.IsValid() && body.PhysicsBody.IsValid() ? body.PhysicsBody.MassCenter : incoming.WorldPosition + Vector3.Up * 4f;
		var sight = Scene.Trace.Ray( player.EyeTransform.Position, pickupPoint )
			.IgnoreGameObjectHierarchy( GameObject ).Run();
		if ( sight.Hit && (!sight.GameObject.IsValid() || sight.GameObject != incoming.GameObject && !sight.GameObject.IsDescendant( incoming.GameObject )) ) return;

		// Keep the engine's duplicate-weapon ammunition pickup. It is independent
		// of carrying a second firearm, and never drops the current weapon.
		if ( !swap && Items.OfType<VBCombatWeapon>().Any( existing => existing.GameObject.PrefabInstanceSource == incoming.GameObject.PrefabInstanceSource ) )
		{ base.PickupWorldItem( incoming ); return; }
		var outgoing = swap ? ActiveItem as VBCombatWeapon : null;
		int slot = outgoing.IsValid() ? outgoing.Slot : -1;
		if ( swap && !CanSwapWeapon( incoming ) ) return;
		if ( !outgoing.IsValid() )
		{
			outgoing = null;
			slot = Enumerable.Range( 0, VBWeaponSelectionController.PrimaryWeaponSlotCount )
				.FirstOrDefault( index => !Items.Any( existing => existing.Slot == index && existing is VBCombatWeapon ), -1 );
		}
		if ( slot < 0 ) return;
		if ( !FitsWeapon( incoming, outgoing ) ) return;

		// Validate all carry constraints before dropping; native Add/Drop retain ownership and clip state.
		if ( outgoing.IsValid() )
		{
			Drop( outgoing );
			if ( outgoing.Inventory == this ) return;
		}
		if ( Add( incoming, slot ) ) { Switch( incoming, allowHolster: false ); return; }
		// An item hook can veto Add. Roll back instead of leaving the player disarmed.
		if ( outgoing.IsValid() && outgoing.Inventory is null && Add( outgoing, slot ) ) Switch( outgoing, allowHolster: false );
	}

	protected override bool OnAdding( BaseInventoryItem item, int slot )
	{
		if ( !base.OnAdding( item, slot ) )
			return false;

		if ( slot == VBWeaponSelectionController.PdaSlot
			&& (item is not IVBInventorySlotPolicy pdaPolicy
				|| pdaPolicy.RequiredInventorySlot != slot
				|| !pdaPolicy.LocksInventorySlot) )
		{
			return false;
		}

		if ( item is IVBInventorySlotPolicy slotPolicy )
		{
			if ( slotPolicy.RequiredInventorySlot != slot )
				return false;

			if ( slotPolicy.LocksInventorySlot
				&& Items.Any( existing => existing != item && existing.Slot == slot ) )
			{
				return false;
			}
		}

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

	protected override bool OnRemoving( BaseInventoryItem item )
	{
		if ( !base.OnRemoving( item ) )
			return false;

		return item is not IVBInventorySlotPolicy { IsInventoryBound: true };
	}

	protected override bool OnDropping( BaseInventoryItem item )
	{
		if ( !base.OnDropping( item ) )
			return false;

		return item is not IVBInventorySlotPolicy { IsInventoryBound: true };
	}

	protected override bool OnMovingSlot( int fromSlot, int toSlot )
	{
		if ( !base.OnMovingSlot( fromSlot, toSlot ) )
			return false;

		return fromSlot != VBWeaponSelectionController.PdaSlot
			&& toSlot != VBWeaponSelectionController.PdaSlot;
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
