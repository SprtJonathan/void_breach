using System;
using System.Threading.Tasks;
using Sandbox;

// Copy temporarily to Code/Weapons; run vb_grenade_regression in host Play.
public sealed class VBGrenadeRegression : Component
{
	private static VBGrenadeRegression _running;
	private readonly List<GameObject> _objects = new();
	private int _failures;
	[ConCmd( "vb_grenade_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || _running.IsValid() ) return;
		var obj = new GameObject( true, "Grenade regression" );
		_running = obj.AddComponent<VBGrenadeRegression>();
		_ = _running.Verify();
	}
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Grenade regression {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private async Task Verify()
	{
		var fixture = GameObject;
		try
		{
			var origin = new Vector3( 22000f, -22000f, 4000f );
			var floor = new GameObject( true, "Grenade test floor" ); _objects.Add( floor );
			floor.WorldPosition = origin + Vector3.Down * 16f;
			floor.AddComponent<BoxCollider>().Scale = new Vector3( 4000f, 4000f, 32f );
			var playerObj = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( origin + Vector3.Up * 2f );
			_objects.Add( playerObj );
			var player = playerObj.GetComponent<PlayerController>();
			player.UseInputControls = false; player.UseLookControls = false; player.UseCameraControls = false;
			player.EnablePressing = false;
			playerObj.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput = false;
			var hud = playerObj.Children.FirstOrDefault( x => x.Name == "HUD" ); if ( hud.IsValid() ) hud.Enabled = false;
			playerObj.GetComponent<VBHealthComponent>().GodMode = false;
			// Stop the dummy's weapon/selector from reading the human host's buttons.
			playerObj.GetComponent<VBWeaponSelectionController>().Enabled = false;
			var inventory = playerObj.GetComponent<VBWeaponInventoryComponent>();
			inventory.ManualPumping = true;
			playerObj.NetworkSpawn( Connection.Local );
			await Task.DelaySeconds( 0.4f );
			Check( inventory.GetSlot( VBWeaponSelectionController.ThrowableSlot ) is null, "spawn grants no free grenade item" );
			var grenade = inventory.Pickup( "weapons/grenade/frag_grenade.prefab", VBWeaponSelectionController.ThrowableSlot ) as VBGrenadeWeapon;
			Check( grenade.IsValid() && grenade.Ammo1 == 2 && inventory.CurrentWeaponCarrySize == 0, "picked-up item grants two grenades in slot 4 without using weapon carry capacity" );
			if ( !grenade.IsValid() ) return;
			inventory.Switch( grenade ); player.EyeAngles = Angles.Zero;
			await Task.DelaySeconds( 0.35f );
			grenade.SetNextPrimaryFire( 0f );
			Check( grenade.FirePrimary(), "native trigger accepts an equipped grenade" );
			await Task.DelaySeconds( 0.1f );
			var thrown = Scene.GetAll<VBNpcGrenade>().Where( x => x.Thrower == playerObj ).ToArray();
			foreach ( var projectile in thrown ) _objects.Add( projectile.GameObject );
			Check( grenade.Ammo1 == 1 && thrown.Length == 1, "one throw spends one native ammo and creates exactly one projectile credited to the player" );
			Check( thrown.Length == 1 && thrown[0].GetComponent<Rigidbody>().Velocity.x > 600f, "the physical projectile receives forward launch velocity" );
			grenade.PrimaryAttack();
			await Task.DelaySeconds( 0.05f );
			Check( grenade.Ammo1 == 1, "host cooldown rejects a second direct attack" );
			var fists = inventory.Items.OfType<VBFistsWeapon>().First(); inventory.Switch( fists );
			grenade.PrimaryAttack();
			Check( grenade.Ammo1 == 1, "a holstered grenade cannot throw" );
			inventory.Switch( grenade );
			await Task.DelaySeconds( 1.1f );
			var wall = new GameObject( true, "Grenade clearance wall" ); _objects.Add( wall );
			wall.WorldPosition = player.EyePosition + Vector3.Forward * 18f;
			wall.AddComponent<BoxCollider>().Scale = new Vector3( 4f, 100f, 100f );
			await Task.DelaySeconds( 0.05f );
			grenade.SetNextPrimaryFire( 0f ); grenade.FirePrimary();
			await Task.DelaySeconds( 0.1f );
			Check( grenade.Ammo1 == 1, "an obstructed spawn sweep refuses the throw without consuming ammo" );
			wall.Destroy();
			inventory.SetAmmo( grenade.PrimaryAmmoType, 0 );
			Check( !grenade.CanPrimaryAttack(), "empty grenade ammo prevents firing" );
			inventory.Pickup( GameObject.GetPrefab( "weapons/grenade/frag_grenade.prefab" ) );
			Check( grenade.Ammo1 == 2 && inventory.Items.OfType<VBGrenadeWeapon>().Count() == 1, "native duplicate pickup replenishes the existing throwable" );
			inventory.Pickup( GameObject.GetPrefab( "weapons/grenade/frag_grenade.prefab" ) );
			Check( grenade.Ammo1 == 4, "native ammo resource caps grenades at four" );
			playerObj.GetComponent<VBHealthComponent>().TakeDamage( VBDamageInfo.Create( 10000f, VBDamageType.Bullet ) );
			grenade.PrimaryAttack();
			Check( grenade.Ammo1 == 4 && !grenade.CanPrimaryAttack(), "a dead or downed player cannot throw" );

			var soldierObj = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone( origin + new Vector3( 3000f, 3000f, 0f ) );
			_objects.Add( soldierObj );
			soldierObj.GetComponent<VBSoldierController>().WeaponPrefab = null;
			soldierObj.GetComponent<VBAiPerceptionComponent>().SightRange = 0f;
			soldierObj.NetworkSpawn();
			await Task.DelaySeconds( 0.15f );
			var renderer = soldierObj.GetComponent<VBSoldierController>().BodyRenderer;
			var dresser = soldierObj.GetComponent<Dresser>();
			Check( renderer.IsValid() && dresser.IsValid() && dresser.BodyTarget == renderer && dresser.Clothing.Count >= 7,
				"soldier has a complete native outfit and its Dresser controls the hidden body groups" );
			Log.Info( $"Grenade regression FINISHED: {_failures} failure(s)." );
		}
		catch ( TaskCanceledException ) { Log.Info( "Grenade regression interrupted by scene stop." ); }
		catch ( Exception ex ) { Log.Error( $"Grenade regression EXCEPTION: {ex}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( x => x.IsValid() ).ToArray() ) obj.Destroy();
			if ( fixture.IsValid() ) fixture.Destroy();
		}
	}
}
