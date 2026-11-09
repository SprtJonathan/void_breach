// Run via sbox-cli run_csharp in host Play with one local player and one free gun slot.
// Uses real Razor panels, native shot effects and seat transitions. Restores player settings.
var scene = Game.ActiveScene;
if ( !Game.IsPlaying || scene is null || !Networking.IsHost ) throw new Exception( "Requires host Play" );
var player = scene.GetAll<PlayerController>().Single( p => !p.IsProxy );
var inventory = player.GetComponent<VBWeaponInventoryComponent>();
var selector = player.GetComponent<VBWeaponSelectionController>();
var bridge = player.GetComponent<VBVehiclePlayerDriver>();
var health = player.GetComponent<VBHealthComponent>();
var hud = scene.GetAll<PanelComponent>().OfType<Sandbox.UI.VBMasterHud>().Single();
var reticle = hud.Panel.Descendants.OfType<Sandbox.UI.VBCrosshairHud>().Single();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var aim = typeof(VBCombatWeapon).GetField( "_aimAmount", flags );
var wantsAim = typeof(VBCombatWeapon).GetField( "_wantsToAim", flags );
float Radius() => (float)reticle.GetType().GetField( "_radius", flags ).GetValue( reticle );
Vector2 Offset() => (Vector2)hud.GetType().GetField( "_helmetOffset", flags ).GetValue( hud );
void SetAim( VBCombatWeapon gun, float value, bool requested ) { aim.SetValue( gun, value ); wantsAim.SetValue( gun, requested ); }
var checks = new System.Collections.Generic.List<string>();
var captures = new System.Collections.Generic.List<string>();
void Check( bool pass, string text ) { if ( !pass ) throw new Exception( text ); checks.Add( text ); }
void Capture( string label )
{
	using var bitmap = new Bitmap( 1920, 1080 );
	scene.Camera.RenderToBitmap( bitmap, true );
	string path = System.IO.Path.Combine( exchangeDirectory, $"hud-{label}.png" );
	System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	captures.Add( path );
}
var originalItem = inventory.ActiveItem;
var originalAngles = player.EyeAngles;
var originalPose = player.WorldTransform;
var originalLook = player.UseLookControls;
var originalInput = player.UseInputControls;
var originalThirdPerson = player.ThirdPerson;
var originalPumping = inventory.ManualPumping;
var originalSelector = selector.Enabled;
var originalBridgeInput = bridge.UsePlayerInput;
var originalGodMode = health.GodMode;
var originalMotion = hud.EnableHelmetMotion;
var ammoType = GameObject.GetPrefab( "weapons/mp5/mp5.prefab" ).GetComponent<VBCombatWeapon>().PrimaryAmmoType;
int originalAmmo = inventory.GetAmmo( ammoType );
VBCombatWeapon weapon = null;
GameObject car = null;
GameObject floor = null;
try
{
	player.UseInputControls = false; player.UseLookControls = false; player.ThirdPerson = false;
	inventory.ManualPumping = true; selector.Enabled = false; bridge.UsePlayerInput = false; health.GodMode = true;
	var fists = inventory.Items.OfType<VBFistsWeapon>().Single();
	inventory.Switch( fists, allowHolster: false );
	await Task.Delay( 200, cancellationToken );
	Check( reticle.HasClass( "compact" ) && !reticle.HasClass( "hidden" ), "Fists use the compact visor reticle" );
	Check( reticle.Parent == hud.Panel && reticle.Parent != hud.Panel.Descendants.First( p => p.HasClass( "helmet-layer" ) ),
		"Aim is outside the moving helmet layer" );
	int slot = Enumerable.Range( 0, 2 ).FirstOrDefault( n => !inventory.Items.OfType<VBCombatWeapon>().Any( w => w.Slot == n ), -1 );
	if ( slot < 0 ) throw new Exception( "Needs one free gun slot" );
	weapon = inventory.Pickup( "weapons/mp5/mp5.prefab", slot ) as VBCombatWeapon;
	Check( weapon.IsValid(), "Real MP5 pickup" );
	inventory.Switch( weapon, allowHolster: false );
	await Task.Delay( 700, cancellationToken );
	SetAim( weapon, 0f, false );
	await Task.Delay( 200, cancellationToken );
	Check( reticle.HasClass( "infantry" ) && !reticle.HasClass( "hidden" ), "Hip fire shows the infantry reticle" );
	Capture( "infantry" );
	float idle = Radius();
	weapon.SetNextPrimaryFire( 0f );
	Check( weapon.FirePrimary(), "Native shot succeeds" );
	reticle.Tick();
	Check( Radius() > idle + 0.2f && !reticle.HasClass( "empty" ), $"A successful shot opens the reticle without cooldown colour flicker (idle={idle}, now={Radius()}, age={weapon.HudShotAge}, classes={reticle.Classes})" );
	await Task.Delay( 600, cancellationToken );
	Check( Math.Abs( Radius() - idle ) < 0.5f, "Shot bloom settles back to idle" );
	weapon.Clip1 = 0;
	await Task.Delay( 100, cancellationToken );
	Check( reticle.HasClass( "empty" ), "Empty magazine uses the HUD warning colour" );
	weapon.Clip1 = 29;
	inventory.SetAmmo( ammoType, 60 );
	weapon.Reload();
	await Task.Delay( 100, cancellationToken );
	Check( weapon.IsReloading && reticle.HasClass( "reloading" ), $"Reload is shown without treating cadence as an empty magazine (reload={weapon.IsReloading}, clip={weapon.Clip1}, ammo={weapon.Ammo1}, classes={reticle.Classes})" );
	await Task.Delay( 2800, cancellationToken );
	SetAim( weapon, 0f, true );
	await Task.Delay( 100, cancellationToken );
	Check( reticle.HasClass( "hidden" ), "Crosshair disappears at the start of ADS" );
	SetAim( weapon, 1f, true );
	await Task.Delay( 150, cancellationToken );
	Check( reticle.HasClass( "hidden" ), "No crosshair at full first-person ADS" );
	Capture( "ads" );
	player.ThirdPerson = true;
	await Task.Delay( 100, cancellationToken );
	Check( reticle.HasClass( "hidden" ), "No crosshair in third-person ADS" );
	player.ThirdPerson = false;
	SetAim( weapon, 0.25f, false );
	await Task.Delay( 100, cancellationToken );
	Check( reticle.HasClass( "hidden" ), "Crosshair stays hidden while lowering sights" );
	SetAim( weapon, 0f, false );
	await Task.Delay( 100, cancellationToken );
	Check( !reticle.HasClass( "hidden" ), "Crosshair returns at hip" );
	hud.EnableHelmetMotion = true;
	player.EyeAngles += new Angles( 10f, 25f, 0f );
	await Task.Delay( 60, cancellationToken );
	Check( Offset().Length > 0.01f && Offset().Length < 4f, "Looking produces bounded, subtle helmet inertia" );
	await Task.Delay( 1100, cancellationToken );
	Check( Offset().Length < 0.05f, "Helmet returns to rest without idle oscillation" );
	hud.EnableHelmetMotion = false;
	await Task.Delay( 100, cancellationToken );
	Check( Offset().Length < 0.001f, "Helmet movement can be disabled" );

	// An isolated runtime floor prevents collisions with the user's map during seat tests.
	var origin = new Vector3( 24000f, 24000f, 4000f );
	floor = new GameObject( scene, true, "HUD regression floor" );
	floor.WorldPosition = origin - Vector3.Up * 16f;
	floor.AddComponent<BoxCollider>().Scale = new Vector3( 2000f, 2000f, 32f );
	car = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( origin + Vector3.Up * 34f );
	car.NetworkSpawn();
	player.WorldPosition = origin + Vector3.Left * 110f + Vector3.Up * 5f;
	player.EyeAngles = Angles.Zero;
	await Task.Delay( 1000, cancellationToken );
	var driver = car.GetComponent<VBVehicleDriverSeat>();
	var turret = car.GetComponent<VBVehicleTurret>();
	Check( ((Component.IPressable)driver).Press( new Component.IPressable.Event( player, null ) ), "Enter driver through native seat interaction" );
	await Task.Delay( 500, cancellationToken );
	Check( bridge.IsTurretAiming && reticle.HasClass( "vehicle" ), "Driver with turret access has the vehicle reticle" );
	Capture( "vehicle" );
	player.EyeAngles = new Angles( -20f, 80f, 0f );
	await Task.Delay( 150, cancellationToken );
	Check( reticle.HasClass( "tracking" ), "Barrel lag changes vehicle alignment feedback" );
	Capture( "vehicle-tracking" );
	// Test the same presentation RPC used by actual shots, without shooting into the map.
	turret.GetType().GetMethod( "ShowShot", flags ).Invoke( turret, new object[] { turret.MuzzlePosition, turret.MuzzlePosition + turret.GunRotation.Forward * 200f } );
	Check( turret.HudShotAge < 0.05f, "Turret shot RPC drives local HUD feedback" );
	driver.RequestSeatChange( player, turret.GunnerSeat );
	await Task.Delay( 500, cancellationToken );
	Check( bridge.ActiveSeat == turret.GunnerSeat && reticle.HasClass( "vehicle" ), "Gunner keeps the vehicle reticle after seat transfer" );
	turret.Access = VBVehicleTurretAccess.Driver;
	await Task.Delay( 100, cancellationToken );
	Check( reticle.HasClass( "hidden" ), "Seated player without turret access has no weapon reticle" );
	bridge.ActiveSeat.RequestExit( player );
	await Task.Delay( 300, cancellationToken );
	Check( !bridge.IsSeated && !reticle.HasClass( "vehicle" ), "Leaving a seat restores the infantry HUD" );
	return new { passed = checks.Count, checks, captures };
}
catch ( Exception ex ) { throw new Exception( Json.Serialize( new { error = ex.Message, checks, captures } ) ); }
finally
{
	if ( bridge.IsSeated ) bridge.ActiveSeat.RequestExit( player );
	if ( weapon.IsValid() ) { SetAim( weapon, 0f, false ); weapon.CancelReload(); inventory.Remove( weapon ); weapon.GameObject.Destroy(); }
	inventory.SetAmmo( ammoType, originalAmmo );
	if ( originalItem.IsValid() ) inventory.Switch( originalItem, allowHolster: false );
	if ( car.IsValid() ) car.Destroy();
	if ( floor.IsValid() ) floor.Destroy();
	player.WorldTransform = originalPose; player.EyeAngles = originalAngles;
	player.UseLookControls = originalLook; player.UseInputControls = originalInput; player.ThirdPerson = originalThirdPerson;
	inventory.ManualPumping = originalPumping; selector.Enabled = originalSelector; bridge.UsePlayerInput = originalBridgeInput;
	health.GodMode = originalGodMode; hud.EnableHelmetMotion = originalMotion;
}
