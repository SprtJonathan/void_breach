// Run via sbox-cli run_csharp in host Play with a real player and a free weapon slot.
// Temporarily forces ADS presentation, restores inventory/settings, and never injects global input.
var scene = Game.ActiveScene;
if ( !Game.IsPlaying || scene is null || !Networking.IsHost ) throw new Exception( "Requires host Play" );
for ( int i = 0; i < 100 && !scene.GetAll<PlayerController>().Any( x => !x.IsProxy ); i++ )
	await Task.Delay( 50, cancellationToken );
var player = scene.GetAll<PlayerController>().Single( x => !x.IsProxy );
var inventory = player.GetComponent<VBWeaponInventoryComponent>();
var selector = player.GetComponent<VBWeaponSelectionController>( includeDisabled: true );
var originalItem = inventory.ActiveItem;
var oldLimit = inventory.MaximumWeaponCarrySize;
var oldPumping = inventory.ManualPumping;
var oldSelector = selector.Enabled;
var oldLook = player.UseLookControls;
var created = new System.Collections.Generic.List<VBCombatWeapon>();
var projectiles = new System.Collections.Generic.List<GameObject>();
var checks = new System.Collections.Generic.List<object>();
var aimAmount = typeof(VBCombatWeapon).GetField( "_aimAmount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
var wantsAim = typeof(VBCombatWeapon).GetField( "_wantsToAim", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
try
{
	inventory.ManualPumping = true;
	inventory.MaximumWeaponCarrySize = 20;
	selector.Enabled = false;
	player.UseLookControls = false;
	foreach ( string name in new[] { "mp5", "m4a1" } )
	{
		var prefab = GameObject.GetPrefab( $"weapons/{name}/{name}.prefab" );
		var viewPrefab = prefab.GetComponent<VBCombatWeapon>().ViewModelPrefab;
		var gun = inventory.Items.OfType<VBCombatWeapon>().FirstOrDefault( g => g.ViewModelPrefab == viewPrefab );
		if ( !gun.IsValid() )
		{
			int slot = Enumerable.Range( 0, 2 ).FirstOrDefault( n => !inventory.Items.OfType<VBCombatWeapon>().Any( g => g.Slot == n ), -1 );
			if ( slot < 0 ) throw new Exception( "Use a test session with an available weapon slot" );
			gun = inventory.Pickup( prefab, slot ) as VBCombatWeapon;
			if ( !gun.IsValid() ) throw new Exception( "Pickup failed: " + name );
			created.Add( gun );
		}
		var oldAim = aimAmount.GetValue( gun );
		var oldWants = wantsAim.GetValue( gun );
		try
		{
			inventory.Switch( gun, allowHolster: false );
			await Task.Delay( 600, cancellationToken );
			// Only the presentation is forced to ADS; no global mouse/input state is injected.
			aimAmount.SetValue( gun, 1f ); wantsAim.SetValue( gun, true );
			await Task.Delay( 1200, cancellationToken );
			var sight = gun.FirstPersonViewModel.GetComponentInChildren<VBWeaponAttachment>();
			if ( sight.AttachmentRenderer.Materials.GetOverride( 2 )?.ShaderName != "shaders/vb_holographic_reticle.shader" )
				throw new Exception( "Unsafe player optic: " + name );
			using var bitmap = new Bitmap( 1280, 720 );
			scene.Camera.RenderToBitmap( bitmap, false );
			var pixels = bitmap.GetPixels32();
			var red = Enumerable.Range( 0, pixels.Length ).Where( i => i % 1280 > 450 && i % 1280 < 830 && i / 1280 > 230 && i / 1280 < 490
				&& pixels[i].r > 180 && pixels[i].r - pixels[i].g > 45 && pixels[i].r - pixels[i].b > 45 ).ToArray();
			string before = System.IO.Path.Combine( exchangeDirectory, $"{name}-holographic-ads.png" );
			System.IO.File.WriteAllBytes( before, bitmap.ToPng() );
			if ( red.Length < 5 ) throw new Exception( $"Player {name} reticle not visible: {red.Length}, {before}" );
			for ( int n = 0; n < 3; n++ )
			{
				var grenade = GameObject.GetPrefab( "weapons/grenade/frag_grenade_projectile.prefab" ).Clone(
					new CloneConfig { StartEnabled = false, Transform = new Transform( player.EyePosition + scene.Camera.WorldRotation.Forward * 120 ) } );
				projectiles.Add( grenade );
				var explosive = grenade.GetComponent<VBNpcGrenade>( includeDisabled: true );
				explosive.FuseTime = 0.1f; explosive.Damage = 0; explosive.PhysicsForceScale = 0;
				grenade.GetComponent<Rigidbody>( includeDisabled: true ).Gravity = false;
				grenade.Enabled = true;
				for ( int frame = 0; frame < 20; frame++ )
				{
					scene.Camera.RenderToBitmap( bitmap, false );
					await Task.Delay( 20, cancellationToken );
				}
				if ( grenade.IsValid() ) throw new Exception( "Grenade did not explode" );
			}
			string after = System.IO.Path.Combine( exchangeDirectory, $"{name}-holographic-explosions.png" );
			System.IO.File.WriteAllBytes( after, bitmap.ToPng() );
			checks.Add( new { weapon = name, redPixels = red.Length, x = red.Average( i => i % 1280 ), y = red.Average( i => i / 1280 ), explosions = 3, before, after } );
		}
		finally { aimAmount.SetValue( gun, oldAim ); wantsAim.SetValue( gun, oldWants ); }
		if ( created.Contains( gun ) ) { inventory.Remove( gun ); gun.GameObject.Destroy(); }
	}
	return new { checks, passed = true };
}
finally
{
	foreach ( var g in projectiles.Where( g => g.IsValid() ) ) g.Destroy();
	foreach ( var gun in created.Where( g => g.IsValid() ) ) { if ( gun.Inventory == inventory ) inventory.Remove( gun ); gun.GameObject.Destroy(); }
	if ( originalItem.IsValid() ) inventory.Switch( originalItem, allowHolster: false );
	inventory.ManualPumping = oldPumping; inventory.MaximumWeaponCarrySize = oldLimit;
	selector.Enabled = oldSelector; player.UseLookControls = oldLook;
}
