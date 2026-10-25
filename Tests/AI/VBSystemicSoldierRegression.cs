using System;
using System.Threading.Tasks;
using Sandbox;

// Copy temporarily to Code/AI and invoke only in host Play. No UI automation is required.
public sealed class VBSystemicSoldierRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	[ConCmd( "vb_systemic_soldier_regression" )]
	public static void Run()
	{
		if ( Networking.IsHost && Game.ActiveScene is not null )
			_ = new GameObject( true, "Systemic soldier regression" ).AddComponent<VBSystemicSoldierRegression>().Verify();
	}
	private void Check( bool pass, string message )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Systemic soldier {(pass ? "PASS" : "FAIL")}: {message}" );
	}
	private GameObject Box( string name, Vector3 position, Vector3 size )
	{
		var obj = new GameObject( true, name ); obj.WorldPosition = position; _objects.Add( obj );
		var collider = obj.AddComponent<BoxCollider>(); collider.Scale = size; collider.Static = true;
		return obj;
	}
	private GameObject Clone( string path, Vector3 position )
	{
		var obj = GameObject.GetPrefab( path ).Clone( position ); _objects.Add( obj ); return obj;
	}
	private async Task Verify()
	{
		try
		{
			var origin = new Vector3( 34000f, 34000f, 4000f );
			Box( "Floor", origin - Vector3.Up * 16f, new Vector3( 2400f, 2400f, 32f ) );
			var actor = Clone( "player/player controller.prefab", origin + Vector3.Up * 2f );
			var player = actor.GetComponent<PlayerController>();
			player.UseInputControls = player.UseLookControls = player.UseCameraControls = player.EnablePressing = false;
			actor.GetComponent<VBWeaponSelectionController>().Enabled = false;
			actor.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput = false;
			var hud = actor.Children.FirstOrDefault( o => o.Name == "HUD" ); if ( hud.IsValid() ) hud.Enabled = false;
			var inventory = actor.GetComponent<VBWeaponInventoryComponent>(); inventory.ManualPumping = true;
			var health = actor.GetComponent<VBHealthComponent>(); health.GodMode = false;
			actor.NetworkSpawn( Connection.Local );
			await Task.DelaySeconds( 0.25f );
			Check( !inventory.Items.OfType<VBGrenadeWeapon>().Any()
				&& inventory.GetAmmo( ResourceLibrary.Get<BaseAmmoResource>( "ammo/frag_grenade.ammo" ) ) == 0,
				"fresh native player loadout contains no grenade item or grenade ammunition" );
			var p90 = Clone( "weapons/p90/p90.prefab", actor.WorldPosition + Vector3.Forward * 24f + Vector3.Up * 12f ).GetComponent<VBCombatWeapon>();
			p90.Clip1 = 11;
			await Task.DelaySeconds( 0.8f );
			Check( p90.IsValid() && p90.Inventory == inventory && inventory.ActiveItem == p90,
				"native touch sweep picks up and equips a ground firearm without pressing Use" );
			if ( !p90.IsValid() || p90.Inventory != inventory ) return;
			inventory.Switch( p90, false ); p90.Clip1 = 11; inventory.SetAmmo( p90.PrimaryAmmoType, 73 );
			var duplicate = Clone( "weapons/p90/p90.prefab", actor.WorldPosition + Vector3.Forward * 24f + Vector3.Up * 12f ).GetComponent<VBCombatWeapon>();
			duplicate.Clip1 = 6;
			await Task.DelaySeconds( 0.8f );
			Check( !duplicate.IsValid() && inventory.GetAmmo( p90.PrimaryAmmoType ) == 79 && p90.Clip1 == 11,
				"native touch sweep harvests duplicate ammunition without replacing the equipped gun" );
			inventory.SetAmmo( p90.PrimaryAmmoType, 73 );
			var ammoObject = Box( "Native SMG ammunition pickup", actor.WorldPosition + Vector3.Forward * 24f + Vector3.Up * 8f,
				new Vector3( 8f, 8f, 8f ) );
			var ammoPickup = ammoObject.AddComponent<BaseAmmoPickup>(); ammoPickup.AmmoType = p90.PrimaryAmmoType; ammoPickup.Amount = 5;
			await Task.DelaySeconds( 0.8f );
			Check( inventory.GetAmmo( p90.PrimaryAmmoType ) == 78 && inventory.ActiveItem == p90,
				"native ammunition pickup is automatic and keeps the active firearm" );
			inventory.SetAmmo( p90.PrimaryAmmoType, 73 );
			var mp5 = Clone( "weapons/mp5/mp5.prefab", actor.WorldPosition + Vector3.Forward * 24f + Vector3.Up * 12f ).GetComponent<VBCombatWeapon>();
			mp5.Clip1 = 7;
			Check( mp5.PrimaryAmmoType == p90.PrimaryAmmoType, "MP5 and P90 resolve to one native ammo resource" );
			await Task.DelaySeconds( 0.65f );
			Check( p90.Inventory == inventory && mp5.Inventory is null, "touch cannot swap away a full-size primary" );
			inventory.SwapWeapon( mp5 ); await Task.DelaySeconds( 0.1f );
			Check( mp5.Inventory == inventory && inventory.ActiveItem == mp5 && mp5.Slot == 0, "Use exchanges and equips the actual ground gun in the same slot" );
			Check( p90.Inventory is null && p90.GameObject.Enabled && p90.Clip1 == 11 && mp5.Clip1 == 7, "both physical magazines survive the transfer" );
			Check( inventory.GetAmmo( mp5.PrimaryAmmoType ) == 73 && inventory.CurrentWeaponCarrySize == 3, "shared reserve and carry size do not duplicate during a swap" );
			var pistol = inventory.Pickup( "weapons/pistol/9mmpistol.prefab", 1 ) as VBCombatWeapon;
			Check( pistol.IsValid(), "sidearm fits beside the SMG" );
			if ( pistol.IsValid() )
			{
				inventory.Switch( pistol, false );
				var heavy = Clone( "weapons/p90/p90.prefab", actor.WorldPosition + Vector3.Forward * 24f + Vector3.Up * 12f ).GetComponent<VBCombatWeapon>();
				inventory.SwapWeapon( heavy );
				Check( pistol.Inventory == inventory && heavy.Inventory is null, "overweight replacement is refused before dropping the sidearm" );
			}
			inventory.Switch( mp5, false );
			inventory.Drop( mp5 );
			Check( mp5.Inventory is null && mp5.GameObject.Enabled, "native equipment drop releases the equipped firearm" );
			float before = health.CurrentHealth;
			var groundImpact = new DamageInfo( 70f, null, null )
			{ Position = actor.WorldPosition, Tags = new TagSet( new[] { "impact" } ) };
			health.OnDamage( groundImpact );
			((PlayerController.IEvents)health).OnLanded( 180f, Vector3.Down * 700f );
			Check( health.CurrentHealth == before, "safe landings ignore native speed-based ground damage" );
			((PlayerController.IEvents)health).OnLanded( 512f, Vector3.Down * 900f );
			health.OnDamage( groundImpact );
			Check( MathF.Abs( health.CurrentHealth - (before - 50f) ) < 0.01f, "landing charges the common height profile once, including native ground contacts before and after" );

			var soldierObject = Clone( "creatures/cartel_soldier/cartel_soldier.prefab", origin + Vector3.Right * 600f );
			soldierObject.WorldRotation = Rotation.Identity;
			var soldier = soldierObject.GetComponent<VBSoldierController>(); soldier.MoveSpeed = 0f; soldier.PatrolRadius = 0f;
			soldier.GrenadeCount = 0; soldier.UseCover = soldier.UseTacticalPositions = false;
			var perception = soldierObject.GetComponent<VBAiPerceptionComponent>(); perception.FieldOfView = 120f;
			var zombie = Clone( "creatures/zombie_classic/zombie_classic.prefab", soldierObject.WorldPosition - Vector3.Forward * 90f );
			zombie.GetComponent<VBZombieController>().Enabled = false;
			// This fixture disables its brain before OnStart, so apply its normal faction setup explicitly.
			zombie.GetComponent<VBFactionComponent>().ConfigureDefaults( VBFactionComponent.XenFaction,
				VBFactionComponent.PlayerFaction, VBFactionComponent.CartelFaction );
			await Task.DelaySeconds( 0.3f );
			if ( soldier.PrimaryWeapon.IsValid() ) soldier.PrimaryWeapon.SetNextPrimaryFire( 30f );
			Check( !perception.CanSee( zombie ), "unobserved enemy behind the soldier remains hidden" );
			soldierObject.GetComponent<VBHealthComponent>().TakeDamage( VBDamageInfo.Create( 1f, VBDamageType.Blunt, zombie ) );
			Check( perception.CanSee( zombie ) && soldierObject.GetComponent<VBAiThreatComponent>().SelectVisibleTarget() == zombie,
				"a nearby attacker felt behind the soldier receives survival priority" );
			var blocker = Box( "Opaque obstacle", soldierObject.WorldPosition - Vector3.Forward * 45f + Vector3.Up * 40f, new Vector3( 8f, 100f, 100f ) );
			await Task.DelaySeconds( 0.1f );
			Check( !perception.CanSee( zombie ), "contact awareness still respects an intervening wall" );
			blocker.Destroy();

			var queryRoot = new GameObject( true, "Query body" ); _objects.Add( queryRoot );
			queryRoot.WorldPosition = origin + Vector3.Left * 600f + Vector3.Up * 2f;
			var ledge = Box( "Automatic climb ledge", queryRoot.WorldPosition + new Vector3( 100f, 0f, 46f ), new Vector3( 140f, 140f, 96f ) );
			await Task.DelaySeconds( 0.1f );
			var geometry = new VBTraversalGeometry( queryRoot, 16f, 72f, boxBody: true );
			Check( geometry.TryPlan( queryRoot.WorldPosition, Vector3.Forward, 112f, out var plan ), "static ledge is discovered without any NavMeshLink or marker" );
			if ( plan.Support.IsValid() )
			{
				Check( plan.Landing.z > queryRoot.WorldPosition.z + 80f && geometry.LandingClear( plan.Landing ), "detected climb includes real standing support" );
				Box( "Low ceiling over ledge", plan.Landing + Vector3.Up * 50f, new Vector3( 120f, 140f, 16f ) );
				await Task.DelaySeconds( 0.1f );
				Check( !geometry.TryPlan( queryRoot.WorldPosition, Vector3.Forward, 112f, out _ ), "body clearance rejects a ledge under a low ceiling" );
			}
			ledge.Destroy();
			Log.Info( $"Systemic soldier FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception exception ) { Log.Error( $"Systemic soldier EXCEPTION: {exception}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( o => o.IsValid() ) ) obj.Destroy();
			if ( this.IsValid() ) GameObject.Destroy();
		}
	}
}
