// Host Play inspection. No bone setters, IK injection, or manual animation ticks.
// Raw wrist stability must be checked alongside the returned left-side images:
// a stable wrist does not by itself establish contact with the weapon's grip.
var scene = Game.ActiveScene;
if ( !Game.IsPlaying || scene is null || !Networking.IsHost ) throw new Exception( "Requires host Play" );
for ( int i = 0; i < 100 && !scene.GetAll<PlayerController>().Any( p => !p.IsProxy ); i++ )
	await Task.Delay( 50, cancellationToken );
var player = scene.GetAll<PlayerController>().Single( p => !p.IsProxy );
var inventory = player.GetComponent<VBWeaponInventoryComponent>();
for ( int i = 0; i < 100 && !inventory.Items.OfType<VBFistsWeapon>().Any(); i++ )
	await Task.Delay( 50, cancellationToken );
var selector = player.GetComponent<VBWeaponSelectionController>();
var oldItem = inventory.ActiveItem;
var oldAngles = player.EyeAngles;
var oldThird = player.ThirdPerson;
var oldLook = player.UseLookControls;
var oldPumping = inventory.ManualPumping;
var oldSelector = selector.Enabled;
var results = new System.Collections.Generic.List<object>();
var disabled = new System.Collections.Generic.List<VBSoldierController>();
VBCombatWeapon gun = null;
CameraComponent observer = null;

void Capture( CameraComponent camera, string label )
{
	using var bitmap = new Bitmap( 960, 720 );
	camera.RenderToBitmap( bitmap, false );
	var path = System.IO.Path.Combine( exchangeDirectory, label + ".png" );
	System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	results.Add( new { capture = path } );
}

try
{
	foreach ( var npc in scene.GetAll<VBSoldierController>().Where( n => n.Enabled ) )
	{
		disabled.Add( npc );
		npc.Enabled = false;
	}
	inventory.ManualPumping = true;
	selector.Enabled = false;
	player.UseLookControls = false;
	player.ThirdPerson = false;
	inventory.Switch( inventory.Items.OfType<VBFistsWeapon>().Single(), allowHolster: false );
	await Task.Delay( 900, cancellationToken );
	Capture( scene.Camera, "fists-fps" );

	foreach ( var path in new[] { "mp5/mp5", "p90/p90", "pistol/9mmpistol", "m4a1/m4a1" } )
	{
		int slot = Enumerable.Range( 0, 2 ).FirstOrDefault( n => !inventory.Items.OfType<VBCombatWeapon>().Any( w => w.Slot == n ), -1 );
		if ( slot < 0 ) throw new Exception( "Requires a free weapon slot" );
		gun = GameObject.GetPrefab( $"weapons/{path}.prefab" ).Clone( new CloneConfig { StartEnabled = false } ).GetComponent<VBCombatWeapon>( true );
		if ( !inventory.Add( gun, slot ) ) throw new Exception( "Cannot add test weapon" );
		gun.GameObject.Enabled = true;
		inventory.Switch( gun, allowHolster: false );
		player.EyeAngles = oldAngles;
		player.ThirdPerson = false;
		await Task.Delay( 900, cancellationToken );
		string label = path.Split( '/' )[0];
		Capture( scene.Camera, label + "-fps" );
		player.EyeAngles = new Angles( oldAngles.pitch + 15, oldAngles.yaw + 45, 0 );
		await Task.Delay( 200, cancellationToken );
		Capture( scene.Camera, label + "-fps-turn" );
		player.EyeAngles = oldAngles;
		player.ThirdPerson = true;
		await Task.Delay( 500, cancellationToken );
		var samples = new System.Collections.Generic.List<object>();
		Vector3? initial = null;
		for ( int frame = 0; frame < 24; frame++ )
		{
			player.EyeAngles = new Angles( MathF.Sin( frame * .17f ) * 16, oldAngles.yaw + MathF.Sin( frame * .12f ) * 75, 0 );
			await Task.Delay( 33, cancellationToken );
			if ( !player.Renderer.TryGetBoneTransform( "hand_L", out var hand ) ) throw new Exception( "Missing left wrist" );
			var grip = gun.WorldModel.WorldTransform.ToLocal( hand );
			initial ??= grip.Position;
			samples.Add( new { frame, wristInWeapon = grip.Position.ToString(), displacement = Vector3.DistanceBetween( initial.Value, grip.Position ) } );
		}
		results.Add( new { weapon = label, samples } );
		// The chase camera obscures the support hand; inspect from its left.
		observer ??= new GameObject( true, "Grip inspection camera" ).AddComponent<CameraComponent>();
		observer.IsMainCamera = false;
		observer.Priority = int.MaxValue;
		observer.RenderExcludeTags.Add( "firstperson" );
		observer.RenderExcludeTags.Add( "viewmodel" );
		observer.FieldOfView = 55;
		observer.WorldPosition = player.WorldPosition + player.Renderer.WorldRotation.Forward * 55
			+ player.Renderer.WorldRotation.Left * 55 + Vector3.Up * 58;
		observer.WorldRotation = Rotation.LookAt( player.WorldPosition + Vector3.Up * 48 - observer.WorldPosition );
		Capture( observer, label + "-tps-left" );
		inventory.Switch( inventory.Items.OfType<VBFistsWeapon>().Single(), allowHolster: false );
		inventory.Remove( gun );
		gun.GameObject.Destroy();
		gun = null;
	}
	return new { results };
}
finally
{
	if ( oldItem.IsValid() ) inventory.Switch( oldItem, allowHolster: false );
	if ( gun.IsValid() )
	{
		if ( gun.Inventory == inventory ) inventory.Remove( gun );
		gun.GameObject.Destroy();
	}
	if ( observer.IsValid() ) observer.GameObject.Destroy();
	player.EyeAngles = oldAngles;
	player.ThirdPerson = oldThird;
	player.UseLookControls = oldLook;
	inventory.ManualPumping = oldPumping;
	selector.Enabled = oldSelector;
	foreach ( var npc in disabled ) if ( npc.IsValid() ) npc.Enabled = true;
}
