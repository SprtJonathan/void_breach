// Run through sbox-cli run_csharp in host Play with exactly one real player.
// Uses that player's native inventory and restores it; never spawns a second player.
var scene = Game.ActiveScene;
var players = scene.GetAllComponents<PlayerController>().Where( p => !p.IsProxy ).ToArray();
if ( players.Length != 1 ) throw new InvalidOperationException( $"Expected one local player, found {players.Length}." );
var player = players[0];
var inventory = player.GetComponent<VBWeaponInventoryComponent>();
var selector = player.GetComponent<VBWeaponSelectionController>();
var originalItem = inventory.ActiveItem;
var originalCarryLimit = inventory.MaximumWeaponCarrySize;
var originalPumping = inventory.ManualPumping;
var originalSelector = selector.Enabled;
var originalLook = player.UseLookControls;
var originalAngles = player.EyeAngles;
var created = new System.Collections.Generic.List<BaseInventoryItem>();
var stashed = new System.Collections.Generic.List<(VBCombatWeapon weapon, int slot)>();
var checks = new System.Collections.Generic.List<string>();
void Check( bool condition, string message )
{
	if ( !condition ) throw new Exception( message );
	checks.Add( message );
}
try
{
	inventory.ManualPumping = true;
	inventory.MaximumWeaponCarrySize = 20;
	selector.Enabled = false;
	player.UseLookControls = false;
	var fists = inventory.Items.OfType<VBFistsWeapon>().Single();
	foreach ( var path in new[] { "weapons/mp5/mp5.prefab", "weapons/pistol/9mmpistol.prefab", "weapons/m4a1/m4a1.prefab" } )
	{
		inventory.Switch( fists, allowHolster: false );
		await Task.Delay( 150, cancellationToken );
		Check( fists.FirstPersonViewModel.IsValid(), path + ": fists deploy before switch" );
		var prefab = GameObject.GetPrefab( path );
		var name = prefab.Name;
		var viewPrefab = prefab.GetComponent<VBCombatWeapon>().ViewModelPrefab;
		var gun = inventory.Items.OfType<VBCombatWeapon>().FirstOrDefault( w => w.ViewModelPrefab == viewPrefab );
		bool temporary = !gun.IsValid();
		if ( temporary )
		{
			int slot = Enumerable.Range( 0, 2 ).FirstOrDefault( n => !inventory.Items.OfType<VBCombatWeapon>().Any( i => i.Slot == n ), -1 );
			if ( slot < 0 )
			{
				var displaced = inventory.Items.OfType<VBCombatWeapon>().First();
				slot = displaced.Slot;
				stashed.Add( (displaced, slot) );
				inventory.Remove( displaced );
				displaced.GameObject.Enabled = false;
			}
			gun = inventory.Pickup( path, slot ) as VBCombatWeapon;
			if ( !gun.IsValid() ) throw new Exception( "Could not create " + path );
			created.Add( gun );
			gun.GameObject.Flags |= GameObjectFlags.NotSaved;
		}
		inventory.Switch( gun, allowHolster: false );
		await Task.Delay( 600, cancellationToken );
		Check( inventory.ActiveItem == gun && gun.FirstPersonViewModel.IsValid(), path + ": native gun deploys" );
		Check( !fists.FirstPersonViewModel.IsValid(), path + ": fists viewmodel is destroyed" );
		Check( scene.GetAllComponents<BaseWeaponModel>().Count( m => m.GameObject.Name.StartsWith( "v_" ) ) == 1,
			path + ": exactly one first-person model" );
		if ( name == "mp5" )
		{
			var view = gun.FirstPersonViewModel;
			var sight = view.GetComponentInChildren<VBWeaponAttachment>();
			Check( sight.IsValid() && sight.AttachmentRenderer.Enabled, "MP5 sight renders" );
			var bone = (sight.MountRenderer as SkinnedModelRenderer).GetBoneObject( sight.BoneName );
			Check( bone.IsValid() && sight.GameObject.Parent == bone, "MP5 sight is parented to animated weapon_root" );
			var originalPose = view.WorldTransform;
			view.WorldPosition += new Vector3( 64f, -32f, 16f );
			view.WorldRotation *= Rotation.From( 15f, 45f, 20f );
			Check( Vector3.DistanceBetween( sight.WorldPosition, bone.WorldTransform.PointToWorld( sight.MountPosition ) ) < 0.001f,
				"MP5 sight follows late camera transforms immediately, without a frame update" );
			view.WorldTransform = originalPose;
			foreach ( var angles in new[] { new Angles( -30f, 45f, 0f ), new Angles( 25f, -80f, 0f ), originalAngles } )
			{
				player.EyeAngles = angles;
				await Task.Delay( 120, cancellationToken );
				Check( sight.GameObject.Parent == bone && Vector3.DistanceBetween( sight.LocalPosition, sight.MountPosition ) < 0.001f,
					"MP5 sight retains its local mount while turning " + angles );
			}
			view.GetComponent<BaseWeaponModel>().OnReloadStart();
			await Task.Delay( 250, cancellationToken );
			Check( sight.GameObject.Parent == bone && Vector3.DistanceBetween( sight.LocalPosition, sight.MountPosition ) < 0.001f,
				"MP5 sight retains its local mount during reload animation" );
			view.GetComponent<BaseWeaponModel>().OnReloadFinish();
		}
		inventory.Switch( fists, allowHolster: false );
		await Task.Delay( 150, cancellationToken );
		Check( !gun.FirstPersonViewModel.IsValid() && fists.FirstPersonViewModel.IsValid(), path + ": switching back destroys gun viewmodel" );
		if ( temporary ) { inventory.Remove( gun ); gun.GameObject.Destroy(); }
	}
	Check( scene.GetAllComponents<PlayerController>().Count() == 1, "No extra player was created" );
	return new { Passed = checks.Count, Checks = checks };
}
finally
{
	foreach ( var item in created )
	{
		if ( !item.IsValid() ) continue;
		if ( item.Inventory == inventory ) inventory.Remove( item );
		item.GameObject.Destroy();
	}
	foreach ( var entry in stashed )
	{
		if ( !entry.weapon.IsValid() ) continue;
		entry.weapon.GameObject.Enabled = true;
		inventory.Add( entry.weapon, entry.slot );
	}
	if ( originalItem.IsValid() ) inventory.Switch( originalItem, allowHolster: false );
	inventory.MaximumWeaponCarrySize = originalCarryLimit;
	inventory.ManualPumping = originalPumping;
	selector.Enabled = originalSelector;
	player.UseLookControls = originalLook;
	player.EyeAngles = originalAngles;
}
