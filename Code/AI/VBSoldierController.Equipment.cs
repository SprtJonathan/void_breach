using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[Property, Group( "Weapon" )] public GameObject SidearmPrefab { get; set; }
	[Property, Group( "Weapon" ), Range( -1, 100 ), Description( "Initial sidearm reserve in magazine equivalents. -1 gives this soldier unlimited reserve, with normal clip capacity and reloads; 0 gives no spare rounds." )]
	public int SidearmReserveMagazines { get; set; } = 2;
	[Property, Group( "Melee" )] public GameObject MeleePrefab { get; set; }
	[Property, Group( "Melee" )] public float MeleeWindup { get; set; } = 0.2f;
	[Sync( SyncFlags.FromHost )] public VBCombatWeapon PrimaryWeapon { get; private set; }
	[Sync( SyncFlags.FromHost )] public VBCombatWeapon Sidearm { get; private set; }
	[Sync( SyncFlags.FromHost )] public VBFistsWeapon MeleeWeapon { get; private set; }
	[Sync( SyncFlags.FromHost )] public GameObject MeleeTarget { get; private set; }
	[Sync( SyncFlags.FromHost )] private TimeUntil MeleeImpact { get; set; }
	[Sync( SyncFlags.FromHost )] private TimeUntil MeleeRecovery { get; set; }
	[Sync( SyncFlags.FromHost )] private bool _meleePending { get; set; }
	public Ray MeleeAimRay => new( WorldPosition + Perception.EyeOffset,
		(AimPosition - (WorldPosition + Perception.EyeOffset)).Normal );

	private void PrepareInventory()
	{
		// Old scene overrides restore BaseInventoryComponent, then RequireComponent
		// adds a second inventory. Native items always bind to the first ancestor
		// inventory, so remove the obsolete components before any equipment spawns.
		// Run on every peer: native Inventory/IsActive lookups also drive visuals.
		if ( !Inventory.IsValid() ) return;
		int removed = 0;
		foreach ( var other in GetComponents<BaseInventoryComponent>( includeDisabled: true ).ToArray() )
		{
			if ( other == Inventory ) continue;
			other.ManualPumping = true;
			other.UsesLoadout = false;
			other.Enabled = false;
			other.Destroy();
			removed++;
		}
		Inventory.Enabled = true;
		Inventory.ManualPumping = true;
		Inventory.UsesLoadout = false;
		Inventory.GiveOnStart = false;
		Inventory.AutoSwitchOnPickup = false;
		Inventory.AutoSwitchOnEmpty = false;
		Inventory.Behaviour = BaseInventoryComponent.InventoryBehaviour.Buckets;
		Inventory.MaxSlots = VBWeaponSelectionController.InventorySlotCount;
		if ( removed > 0 )
		{
			Log.Info( $"[Cartel Soldier] Repaired {removed} obsolete inventory component(s) on {GameObject.Name}; equipment uses one shared weapon inventory." );
			if ( Networking.IsHost && Network.Active ) Network.Refresh();
		}
	}

	private void InitializeEquipment()
	{
		PrimaryWeapon = EquipGun( WeaponPrefab, VBWeaponSelectionController.FirstWeaponSlot );
		Sidearm = EquipGun( SidearmPrefab, VBWeaponSelectionController.FirstWeaponSlot + 1 );
		if ( MeleePrefab.IsValid() )
			MeleeWeapon = Inventory.Pickup( MeleePrefab, VBWeaponSelectionController.MeleeWeaponSlot ) as VBFistsWeapon;
		// Replace the prefab's initial ammo grant with the configured loadout.
		// -1 seeds no rounds: VBCombatWeapon supplies only this NPC's unlimited reserve.
		// Guns using the same resource share one pool, just as they do for the player.
		var seeded = new HashSet<BaseAmmoResource>();
		SeedReserve( PrimaryWeapon, ReserveMagazines, seeded );
		SeedReserve( Sidearm, SidearmReserveMagazines, seeded );
		SelectCombatWeapon();
	}

	private VBCombatWeapon EquipGun( GameObject prefab, int slot )
	{
		if ( !prefab.IsValid() ) return null;
		var item = Inventory.Pickup( prefab, slot );
		if ( item is not VBCombatWeapon gun || gun.Inventory != Inventory
			|| !gun.UsableByNpcs || !gun.UsesAmmo || gun.PrimaryAmmoType is null )
		{
			if ( item.IsValid() ) Inventory.Remove( item );
			Log.Warning( "[Cartel Soldier] Guns must be NPC-usable VBCombatWeapon items with a finite ammo resource and fit the player's carry limits." );
			return null;
		}
		var accuracy = float.IsFinite( AccuracyScale ) && AccuracyScale >= 0f ? AccuracyScale : 2f;
		gun.SpreadScale = gun.Npc.SpreadScale * accuracy;
		return gun;
	}

	private void SeedReserve( VBCombatWeapon gun, int magazines, HashSet<BaseAmmoResource> seeded )
	{
		if ( !gun.IsValid() ) return;
		var ammo = gun.PrimaryAmmoType;
		if ( seeded.Add( ammo ) ) Inventory.SetAmmo( ammo, 0 );
		int rounds = gun.UsesPrimaryClip ? Math.Max( 0, gun.PrimaryClipSize ) : Math.Max( 0, gun.StartingAmmo );
		int requested = (int)Math.Min( (long)rounds * Math.Max( 0, magazines ), ammo.MaxReserve );
		Inventory.GiveAmmo( ammo, requested );
	}

	private bool HasGunSupplies( VBCombatWeapon gun ) => gun.IsValid() && gun.Inventory == Inventory
		&& (!gun.UsesAmmo || gun.UsesPrimaryClip && gun.Clip1 > 0 || gun.Ammo1 > 0);

	private void SelectCombatWeapon()
	{
		BaseCombatWeapon desired = HasGunSupplies( PrimaryWeapon ) ? PrimaryWeapon
			: HasGunSupplies( Sidearm ) ? Sidearm : MeleeWeapon;
		if ( !desired.IsValid() || Inventory.ActiveItem == desired ) return;
		Inventory.Switch( desired ); // Native holster, reload cancellation and deploy delay.
		_burstRemaining = 0;
		_nextBurst = Time.Now;
	}

	private void TryReloadWeapon( bool topUp = false, bool support = false )
	{
		var gun = Weapon;
		if ( !gun.IsValid() || !gun.UsesPrimaryClip || !gun.CanReload() ) return;
		float threshold = support ? Math.Clamp( SupportReloadThreshold, 0.1f, 0.9f ) : 0.25f;
		if ( gun.Clip1 <= 0 || topUp && gun.Clip1 <= gun.PrimaryClipSize * threshold ) gun.Reload();
	}

	private bool TryBeginMelee( GameObject target )
	{
		if ( !MeleeWeapon.IsValid() || MeleeRecovery > 0f || !Perception.IsViableTarget( target )
			|| !Perception.TargetVisible || !HasFiringLane( WorldPosition, target )
			|| Vector3.DistanceBetween( WorldPosition, target.WorldPosition ) > MeleeWeapon.Ballistics.Range ) return false;
		var direction = (AimPosition - WorldPosition).WithZ( 0f ).Normal;
		if ( Vector3.Dot( WorldRotation.Forward, direction ) < 0.85f ) return false;
		if ( Inventory.ActiveItem != MeleeWeapon ) Inventory.Switch( MeleeWeapon );
		if ( Inventory.ActiveItem != MeleeWeapon ) return false;
		ClearCover(); StopMoving(); _burstRemaining = 0;
		EndCrouchedFire();
		MeleeTarget = target;
		State = VBSoldierState.Melee;
		ReleaseFireSlot();
		MeleeImpact = MathF.Max( MathF.Max( 0f, MeleeWindup ), MeleeWeapon.DeployTime );
		MeleeRecovery = (float)MeleeImpact + MathF.Max( 0.1f, MeleeWeapon.PrimaryDelay );
		_meleePending = true;
		return true;
	}

	private void TickMelee()
	{
		StopMoving();
		// Aim stays committed during the windup; moving away or behind cover can evade it.
		if ( _meleePending && MeleeImpact <= 0f )
		{
			_meleePending = false;
			if ( MeleeWeapon.IsValid() && Inventory.ActiveItem == MeleeWeapon && Perception.IsViableTarget( MeleeTarget )
				&& Perception.CanSee( MeleeTarget ) && HasFiringLane( WorldPosition, MeleeTarget )
				&& Vector3.DistanceBetween( WorldPosition, MeleeTarget.WorldPosition ) <= MeleeWeapon.Ballistics.Range )
				MeleeWeapon.FirePrimary();
		}
		if ( MeleeRecovery > 0f ) return;
		MeleeTarget = null;
		State = VBSoldierState.Advance;
		SelectCombatWeapon();
	}
}
