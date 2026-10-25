using System;
using System.Threading.Tasks;
using Sandbox;

// Copy temporarily into Code/AI; run vb_soldier_equipment_regression in host Play.
public sealed class VBSoldierEquipmentRegression : Component
{
	private static VBSoldierEquipmentRegression _running;
	private readonly List<GameObject> _objects = new();
	private int _failures;
	[ConCmd( "vb_soldier_equipment_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || _running.IsValid() ) return;
		var obj = new GameObject( true, "Soldier equipment regression" );
		_running = obj.AddComponent<VBSoldierEquipmentRegression>();
		_ = _running.Verify();
	}
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Soldier equipment {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private async Task VerifyLegacyInventory( Vector3 origin )
	{
		// Reproduce the actual saved scene: a native inventory comes first,
		// followed by the required custom inventory with player-style defaults.
		var actor = new GameObject( false, "Legacy soldier inventory" ); _objects.Add( actor );
		actor.WorldPosition = origin;
		var obsolete = actor.AddComponent<BaseInventoryComponent>();
		actor.AddComponent<NavMeshAgent>();
		actor.AddComponent<VBHealthComponent>();
		actor.AddComponent<VBFactionComponent>();
		var inventory = actor.AddComponent<VBWeaponInventoryComponent>();
		inventory.MaxSlots = 6; inventory.AutoSwitchOnPickup = true; inventory.ManualPumping = false;
		actor.AddComponent<VBAiPerceptionComponent>().FieldOfView = 360f;
		actor.AddComponent<VBAiThreatComponent>();
		actor.AddComponent<VBNpcRagdollController>();
		var soldier = actor.AddComponent<VBSoldierController>();
		soldier.WeaponPrefab = GameObject.GetPrefab( "weapons/mp5/mp5.prefab" );
		soldier.SidearmPrefab = GameObject.GetPrefab( "weapons/pistol/9mmpistol.prefab" );
		soldier.MeleePrefab = GameObject.GetPrefab( "weapons/fists/fists.prefab" );
		soldier.UseCover = false; soldier.MoveSpeed = 0f; soldier.PatrolRadius = 0f; soldier.AccuracyScale = 0f;
		var victim = new GameObject( true, "Legacy soldier visible threat" ); _objects.Add( victim );
		victim.WorldPosition = origin + Vector3.Forward * 400f;
		var collider = victim.AddComponent<BoxCollider>(); collider.Scale = new Vector3( 24f, 24f, 72f ); collider.Center = Vector3.Up * 36f;
		victim.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
		var hp = victim.AddComponent<VBHealthComponent>(); hp.StartingHardMaxHealth = 1000f; hp.CanBeDowned = false;
		actor.Enabled = true; actor.NetworkSpawn();
		await Task.DelaySeconds( 0.2f );
		Check( !obsolete.IsValid() && actor.GetComponents<BaseInventoryComponent>( includeDisabled: true ).Count() == 1,
			"legacy-first duplicate inventory is removed before weapons bind to their holder" );
		Check( inventory.ManualPumping && !inventory.AutoSwitchOnPickup && inventory.MaxSlots == 7
			&& soldier.PrimaryWeapon.IsValid() && soldier.PrimaryWeapon.Inventory == inventory && inventory.ActiveItem == soldier.PrimaryWeapon,
			"legacy inventory defaults are repaired and the main weapon deploys" );
		for ( int i = 0; i < 30 && hp.CurrentHealth >= 1000f; i++ ) await Task.DelaySeconds( 0.05f );
		Check( soldier.PrimaryWeapon.IsValid() && soldier.PrimaryWeapon.Clip1 < soldier.PrimaryWeapon.PrimaryClipSize && hp.CurrentHealth < 1000f,
			"repaired soldier autonomously detects and shoots a visible faction threat" );
		actor.Destroy(); victim.Destroy();
	}
	private async Task Verify()
	{
		var fixture = GameObject;
		try
		{
			foreach ( var existing in Scene.GetAllComponents<VBSoldierController>().ToArray() )
			{
				var inventories = existing.GetComponents<BaseInventoryComponent>().ToArray();
				Check( inventories.Length == 1 && inventories[0] is VBWeaponInventoryComponent,
					$"scene soldier {existing.GameObject.Name} has one shared weapon inventory (no legacy scene override)" );
			}
			var origin = new Vector3( 24000f, 24000f, 4000f );
			var floor = new GameObject( true, "Equipment floor" ); _objects.Add( floor );
			floor.WorldPosition = origin + Vector3.Down * 16f;
			floor.AddComponent<BoxCollider>().Scale = new Vector3( 3000f, 3000f, 32f );
			await VerifyLegacyInventory( origin + Vector3.Right * 800f );
			var actor = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone( origin );
			_objects.Add( actor ); actor.NetworkSpawn();
			var soldier = actor.GetComponent<VBSoldierController>();
			soldier.UseCover = false; soldier.MoveSpeed = 0f; soldier.PatrolRadius = 0f;
			soldier.SidearmReserveMagazines = 2; // Exercise the finite mode independently of the chosen prefab default.
			var sight = actor.GetComponent<VBAiPerceptionComponent>(); sight.Enabled = false;
			await Task.DelaySeconds( 0.2f );
			var inventory = actor.GetComponent<VBWeaponInventoryComponent>();
			var primary = soldier.PrimaryWeapon; var pistol = soldier.Sidearm;
			Check( primary.IsValid() && pistol.IsValid() && soldier.MeleeWeapon.IsValid(), "native loadout has the primary gun, pistol and player fists" );
			if ( !primary.IsValid() || !pistol.IsValid() ) return;
			Check( primary.Inventory == inventory && pistol.Inventory == inventory && soldier.MeleeWeapon.Inventory == inventory
				&& inventory.ActiveItem == primary && primary.GameObject.Enabled, "all equipment resolves to one inventory and the primary gun is deployed first" );
			Check( inventory.CurrentWeaponCarrySize == 4 && inventory.CarriedWeapons.Count() == 2, "soldier observes the player's weapon count and carry budget" );
			Check( primary.Clip1 == primary.PrimaryClipSize && primary.Ammo1 == Math.Min( primary.PrimaryClipSize * soldier.ReserveMagazines, primary.PrimaryAmmoType.MaxReserve )
				&& pistol.Clip1 == 15 && pistol.Ammo1 == 30, "configured finite initial magazines and reserve rounds" );
			Check( soldier.GrenadesRemaining == 2, "initial grenades use the bounded native ammo pool" );
			primary.Clip1 = 1; inventory.SetAmmo( primary.PrimaryAmmoType, 0 ); primary.SetNextPrimaryFire( 0f );
			Check( primary.FirePrimary() && primary.Clip1 == 0, "one NPC shot consumes exactly one round" );
			await Task.DelaySeconds( 0.1f );
			Check( soldier.Weapon == pistol && primary.Clip1 == 0 && !primary.CanPrimaryAttack(), "an exhausted primary gun switches to the pistol without refilling" );
			Check( !pistol.CanPrimaryAttack(), "pistol deployment respects its native draw delay" );
			await Task.DelaySeconds( 0.6f );
			int before = pistol.Clip1; pistol.SetNextPrimaryFire( 0f ); pistol.FirePrimary();
			Check( pistol.Clip1 == before - 1 && !pistol.FirePrimary(), "pistol consumes one round and enforces its fire rate" );

			inventory.SetAmmo( primary.PrimaryAmmoType, 5 );
			await Task.DelaySeconds( 0.15f );
			Check( soldier.Weapon == primary && primary.IsReloading && primary.Clip1 == 0, "a usable reserve restores the primary gun and starts a real reload" );
			Check( !primary.FirePrimary(), "firing is blocked during reload" );
			await Task.DelaySeconds( primary.ReloadTime + 0.2f );
			Check( primary.Clip1 == 5 && primary.Ammo1 == 0 && !primary.IsReloading, "partial reserve reloads only the five available rounds" );
			inventory.GiveAmmo( primary.PrimaryAmmoType, 10000 );
			inventory.GiveAmmo( pistol.PrimaryAmmoType, 10000 );
			inventory.GiveAmmo( soldier.GrenadeAmmo, 10000 );
			Check( primary.Ammo1 == primary.PrimaryAmmoType.MaxReserve && pistol.Ammo1 == pistol.PrimaryAmmoType.MaxReserve
				&& soldier.GrenadesRemaining == soldier.GrenadeAmmo.MaxReserve, "same native capacity caps as the player for guns and grenades" );
			inventory.SetAmmo( primary.PrimaryAmmoType, 0 ); inventory.SetAmmo( pistol.PrimaryAmmoType, 0 );
			primary.Clip1 = 0; pistol.Clip1 = 0;
			var pistolAmmo = pistol.PrimaryAmmoType;
			soldier.SidearmReserveMagazines = -1;
			await Task.DelaySeconds( 0.15f );
			Check( soldier.Weapon == pistol && pistol.IsReloading && pistol.Clip1 == 0 && !pistol.FirePrimary(),
				"-1 keeps a finite pistol magazine and blocks shooting during its normal reload" );
			await Task.DelaySeconds( pistol.ReloadTime + 0.2f );
			Check( pistol.Clip1 == pistol.PrimaryClipSize && pistol.Ammo1 == int.MaxValue && inventory.GetAmmo( pistolAmmo ) == 0
				&& pistol.PrimaryAmmoType == pistolAmmo, "-1 reloads from unlimited NPC reserve without changing the shared ammo resource or pool" );
			pistol.Clip1 = 0;
			await Task.DelaySeconds( 0.15f );
			Check( pistol.IsReloading, "unlimited reserve allows another real reload after the pistol empties again" );
			await Task.DelaySeconds( pistol.ReloadTime + 0.2f );
			Check( pistol.Clip1 == pistol.PrimaryClipSize && inventory.GetAmmo( pistolAmmo ) == 0, "repeated unlimited reload stays within the native clip size" );
			soldier.SidearmReserveMagazines = 2; pistol.Clip1 = 0;
			await Task.DelaySeconds( 0.1f );
			Check( inventory.ActiveItem == soldier.MeleeWeapon && !primary.CanPrimaryAttack() && !pistol.CanPrimaryAttack(), "both exhausted guns fall back to the finite-range fists" );

			var victim = new GameObject( true, "Melee victim" ); _objects.Add( victim ); victim.WorldPosition = origin + Vector3.Forward * 50f;
			var collider = victim.AddComponent<BoxCollider>(); collider.Scale = new Vector3( 24f, 24f, 72f ); collider.Center = Vector3.Up * 36f;
			var faction = victim.AddComponent<VBFactionComponent>(); faction.FactionId = VBFactionComponent.PlayerFaction;
			var hp = victim.AddComponent<VBHealthComponent>(); hp.StartingHardMaxHealth = 1000f; hp.CanBeDowned = false;
			victim.NetworkMode = NetworkMode.Object; victim.NetworkSpawn();
			sight.ThinkInterval = 0.05f; sight.FieldOfView = 360f; sight.Enabled = true;
			for ( int i = 0; i < 20 && soldier.State != VBSoldierState.Melee; i++ ) await Task.DelaySeconds( 0.05f );
			float health = hp.CurrentHealth;
			Check( soldier.State == VBSoldierState.Melee && health == 1000f, "close threat starts an interruptible melee windup" );
			await Task.DelaySeconds( 0.3f );
			Check( hp.CurrentHealth == health - soldier.MeleeWeapon.Ballistics.Damage, "one melee strike uses the player's native fist damage" );
			Check( soldier.MeleeWeapon.IsPunchPoseActive, "NPC punch effects keep the native melee pose active after impact" );
			float hitHealth = hp.CurrentHealth;
			victim.WorldPosition = origin + Vector3.Forward * 600f;
			await Task.DelaySeconds( 0.7f );
			Check( hp.CurrentHealth == hitHealth, "moving out of reach stops subsequent melee damage" );
			victim.WorldPosition = origin + Vector3.Forward * 50f;
			for ( int i = 0; i < 20 && soldier.State != VBSoldierState.Melee; i++ ) await Task.DelaySeconds( 0.05f );
			actor.GetComponent<VBNpcRagdollController>().BeginRecoverableRagdoll( Vector3.Zero );
			await Task.DelaySeconds( 0.35f );
			Check( hp.CurrentHealth == hitHealth, "ragdoll interrupts pending melee damage" );
			soldier.SidearmReserveMagazines = -1;
			actor.GetComponent<VBHealthComponent>().TakeDamage( VBDamageInfo.Create( 10000f, VBDamageType.Bullet, victim ) );
			await Task.DelaySeconds( 0.1f );
			Check( !primary.Inventory.IsValid() && !pistol.Inventory.IsValid() && primary.Clip1 == 0 && pistol.Clip1 == 0,
				"death drops both guns with their actual empty magazines" );
			Check( pistol.Ammo1 == 0 && pistol.PrimaryAmmoType == pistolAmmo && !pistol.CanReload(),
				"dropped sidearm loses unlimited NPC reserve and retains its normal player ammo resource" );
			_objects.Add( primary.GameObject ); _objects.Add( pistol.GameObject );
			Log.Info( $"Soldier equipment FINISHED: {_failures} failure(s)." );
		}
		catch ( TaskCanceledException ) { Log.Info( "Soldier equipment interrupted by scene stop." ); }
		catch ( Exception ex ) { Log.Error( $"Soldier equipment EXCEPTION: {ex}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( x => x.IsValid() ).ToArray() ) obj.Destroy();
			if ( fixture.IsValid() ) fixture.Destroy();
		}
	}
}
