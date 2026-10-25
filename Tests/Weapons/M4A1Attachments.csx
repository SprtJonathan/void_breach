// Run in an isolated host Play scene without an existing player. Never use on a live player session.
if ( Game.ActiveScene is null || Game.ActiveScene.IsEditor || !Game.IsPlaying
	|| Game.ActiveScene.GetAllComponents<PlayerController>().Any() )
	throw new InvalidOperationException( "Use an isolated Play scene without a player; this harness creates its own test player." );
var objects = new System.Collections.Generic.List<GameObject>();
var results = new System.Collections.Generic.List<object>();
string stage = "setup";
void Check( bool pass, string label )
{
	results.Add( new { pass, label } );
	if ( !pass ) throw new Exception( label );
}
try
{
	var origin = new Vector3( 24000f, -24000f, 4000f );
	var floor = new GameObject( true, "M4 attachment regression floor" ); objects.Add( floor );
	floor.Flags |= GameObjectFlags.NotSaved;
	floor.WorldPosition = origin - Vector3.Up * 16f;
	floor.AddComponent<BoxCollider>().Scale = new Vector3( 2000f, 2000f, 32f );
	var playerObject = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( origin ); objects.Add( playerObject );
	playerObject.Flags |= GameObjectFlags.NotSaved;
	stage = "configure player clone";
	var player = playerObject.GetComponent<PlayerController>();
	player.UseInputControls = false; player.UseLookControls = false; player.UseCameraControls = false;
	player.EnablePressing = false;
	playerObject.GetComponent<VBWeaponSelectionController>().Enabled = false;
	playerObject.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput = false;
	var hud = playerObject.Children.FirstOrDefault( o => o.Name == "HUD" ); if ( hud.IsValid() ) hud.Enabled = false;
	var inventory = playerObject.GetComponent<VBWeaponInventoryComponent>();
	inventory.ManualPumping = true;
	playerObject.NetworkSpawn( Connection.Local );
	await Task.Delay( 400, cancellationToken );
	stage = "pickup M4A1";
	var gun = inventory.Pickup( "weapons/m4a1/m4a1.prefab", 0 ) as VBCombatWeapon;
	Check( gun.IsValid(), "M4A1 native inventory pickup" );
	objects.Add( gun.GameObject ); gun.GameObject.Flags |= GameObjectFlags.NotSaved;
	Check( gun.PrimaryAmmoType.ResourcePath == "ammo/large.ammo" && gun.Clip1 == 30 && gun.Ammo1 == 60, "M4A1 starts with 30/60 large ammo" );
	Check( gun.GetComponent<Prop>( includeDisabled: true ).Model.IsError == false, "M4A1 world model loads" );
	inventory.Switch( gun );
	await Task.Delay( 600, cancellationToken );
	gun.SetNextPrimaryFire( 0f );
	Check( gun.FirePrimary() && gun.Clip1 == 29, "M4A1 shot spends one cartridge" );
	gun.Reload();
	Check( gun.IsReloading, "M4A1 native reload starts" );
	await Task.Delay( 2900, cancellationToken );
	Check( gun.Clip1 == 30 && gun.Ammo1 == 59, "M4A1 partial reload spends one reserve cartridge" );
	var pistol = inventory.Pickup( "weapons/pistol/9mmpistol.prefab", 1 );
	if ( pistol.IsValid() ) { objects.Add( pistol.GameObject ); pistol.GameObject.Flags |= GameObjectFlags.NotSaved; }
	Check( pistol.IsValid() && inventory.CurrentWeaponCarrySize == 5, "M4A1 plus pistol fits five-point budget" );
	var rejected = inventory.Pickup( "weapons/mp5/mp5.prefab", 2 );
	Check( !rejected.IsValid(), "M4A1 plus pistol rejects over-budget MP5" );
	inventory.Drop( gun );
	await Task.Delay( 150, cancellationToken );
	Check( gun.Inventory is null && gun.Clip1 == 30, "M4A1 drop retains its magazine" );
	Check( inventory.Add( gun, 0 ) && gun.Ammo1 == 59, "M4A1 repickup does not duplicate reserve" );
	inventory.Drop( gun );
	objects.Add( gun.GameObject );
	var mp5 = inventory.Pickup( "weapons/mp5/mp5.prefab", 0 ) as VBCombatWeapon;
	if ( mp5.IsValid() ) { objects.Add( mp5.GameObject ); mp5.GameObject.Flags |= GameObjectFlags.NotSaved; }
	Check( mp5.IsValid() && mp5.PrimaryAmmoType.ResourcePath == "ammo/medium.ammo", "MP5 retains separate medium ammo" );
	inventory.Switch( mp5 );
	await Task.Delay( 100, cancellationToken );
	var pickupOptic = mp5.GetComponentsInChildren<VBWeaponAttachment>( includeDisabled: true ).Single( a => a.WorldPickupOnly );
	Check( !pickupOptic.AttachmentRenderer.Enabled, "pickup optic hides while MP5 is carried" );
	inventory.Drop( mp5 ); objects.Add( mp5.GameObject );
	await Task.Delay( 150, cancellationToken );
	Check( pickupOptic.AttachmentRenderer.Enabled && !pickupOptic.AttachmentRenderer.Model.IsError, "pickup optic returns on MP5 drop" );
	Check( inventory.Add( mp5, 0 ), "MP5 repickup succeeds" );
	await Task.Delay( 100, cancellationToken );
	Check( !pickupOptic.AttachmentRenderer.Enabled, "pickup optic hides again after repickup" );
	foreach ( var path in new[] { "weapons/mp5/v_mp5.prefab", "weapons/mp5/w_mp5.prefab" } )
	{
		var model = GameObject.GetPrefab( path ).Clone( origin + Vector3.Up * 200f ); objects.Add( model );
		model.Flags |= GameObjectFlags.NotSaved;
		await Task.Delay( 100, cancellationToken );
		var attachment = model.GetComponentInChildren<VBWeaponAttachment>();
		Check( attachment.IsValid() && attachment.AttachmentRenderer.Enabled && !attachment.AttachmentRenderer.Model.IsError, path + " holographic loaded" );
		var pose = attachment.MountRenderer.WorldTransform;
		if ( attachment.MountRenderer is SkinnedModelRenderer animated && !string.IsNullOrEmpty( attachment.BoneName ) )
		{
			var bone = animated.GetBoneObject( attachment.BoneName );
			Check( bone.IsValid() && attachment.GameObject.Parent == bone, path + " parented to native animated bone" );
			pose = bone.WorldTransform;
		}
		var expected = pose.PointToWorld( attachment.MountPosition );
		Check( Vector3.DistanceBetween( expected, attachment.WorldPosition ) < 0.01f, path + " mount follows current pose" );
		attachment.MountRenderer.Enabled = false;
		await Task.Delay( 50, cancellationToken );
		Check( !attachment.AttachmentRenderer.Enabled, path + " hidden model hides optic" );
	}
	return results;
}
catch ( Exception error )
{
	return new { stage, error = error.ToString(), results };
}
finally
{
	foreach ( var obj in objects ) if ( obj.IsValid() ) obj.Destroy();
}
