// Run via sbox-cli run_csharp in host Play. Uses temporary fixtures and no player input.
var scene = Game.ActiveScene;
if ( !Game.IsPlaying || scene is null || !Networking.IsHost )
	throw new InvalidOperationException( "Requires host Play." );
var checks = new System.Collections.Generic.List<string>();
void Check( bool pass, string message )
{
	if ( !pass ) throw new Exception( message );
	checks.Add( message );
}
var fixture = new GameObject( true, "Grenade rendering regression" );
fixture.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
fixture.WorldPosition = new Vector3( 24000, -24000, 4000 );
string stage = "material checks";
try
{
	var safe = Material.Load( "materials/weapons/holographic_reticle.vmat" );
	var original = Model.Load( "models/weapons/sbox_attachments/sight_holographic/sight_holographic.vmdl" ).Materials[2];
	Check( safe.IsValid && safe.ShaderName == "shaders/complex.shader" && safe.Flags.IsTranslucent,
		"Local reticle uses a transparent native mesh shader" );
	var pixels = safe.GetTexture( "g_tColor" ).GetPixels();
	Check( pixels.Any( p => p.r > 50 && p.a > 50 ) && pixels.Any( p => p.a == 0 ),
		"Reticle has visible red pixels and a transparent background" );
	Check( safe.GetFeature( "F_SELF_ILLUM" ) == 1 && safe.GetTexture( "g_tSelfIllumMask" ).GetPixel( 0.5f, 0.5f ).r > 128,
		"Reticle remains emissive in an unlit scene" );
	Check( original.ShaderName == "shaders/glass_scope.shader", "Shared cloud material remains unchanged" );

	foreach ( var path in new[] { "weapons/mp5/mp5.prefab", "weapons/mp5/w_mp5.prefab", "weapons/mp5/v_mp5.prefab",
		"weapons/m4a1/w_m4a1.prefab", "weapons/m4a1/v_m4a1.prefab" } )
	{
		stage = path;
		var weapon = GameObject.GetPrefab( path ).Clone( new CloneConfig { Parent = fixture, StartEnabled = false } );
		weapon.LocalPosition = new Vector3( 0, 300, 0 );
		weapon.Enabled = true;
		await Task.Delay( 200, cancellationToken );
		var attachment = weapon.GetComponentsInChildren<VBWeaponAttachment>( includeSelf: true ).Single();
		var materials = attachment.AttachmentRenderer.Materials;
		Check( materials.GetOverride( 2 ) == safe, path + ": safe reticle on a fresh instance" );
		Check( !materials.HasOverride( 0 ) && !materials.HasOverride( 1 ), path + ": original housing and glass" );
		weapon.Destroy();
	}

	// Render the FPS optic throughout actual native explosions, including overlapping effects.
	stage = "render setup";
	var cameraObject = new GameObject( fixture, true, "Grenade rendering camera" );
	var camera = cameraObject.AddComponent<CameraComponent>();
	camera.IsMainCamera = false;
	camera.FieldOfView = 70f;
	stage = "FPS clone";
	var view = GameObject.GetPrefab( "weapons/mp5/v_mp5.prefab" ).Clone( new CloneConfig { Parent = fixture, StartEnabled = true } );
	stage = "FPS renderer";
	var renderer = view.GetComponent<SkinnedModelRenderer>();
	renderer.Set( "b_deploy_skip", true );
	renderer.Set( "ironsights", "enabled" );
	using var bitmap = new Bitmap( 1280, 720 );
	await Task.Delay( 350, cancellationToken );
	for ( int i = 0; i < 12; i++ )
	{
		stage = $"explosion {i + 1}";
		var grenade = GameObject.GetPrefab( "weapons/grenade/frag_grenade_projectile.prefab" ).Clone(
			new CloneConfig { Parent = fixture, StartEnabled = false } );
		grenade.LocalPosition = new Vector3( 90, i % 2 == 0 ? 12 : -12, 0 );
		var projectile = grenade.GetComponent<VBNpcGrenade>( includeDisabled: true );
		projectile.FuseTime = 0.1f;
		projectile.Damage = 0f;
		projectile.PhysicsForceScale = 0f;
		grenade.GetComponent<Rigidbody>( includeDisabled: true ).Gravity = false;
		grenade.Enabled = true;
		for ( int frame = 0; frame < 20; frame++ )
		{
			camera.RenderToBitmap( bitmap, false );
			await Task.Delay( 20, cancellationToken );
		}
		Check( !grenade.IsValid(), $"Explosion {i + 1}: real projectile detonated while optic was rendered" );
	}
	camera.RenderToBitmap( bitmap, false );
	System.IO.File.WriteAllBytes( System.IO.Path.Combine( exchangeDirectory, "grenade-reticle.png" ), bitmap.ToPng() );
	return new { passed = checks.Count, checks, renderedExplosions = 12, screenshot = System.IO.Path.Combine( exchangeDirectory, "grenade-reticle.png" ) };
}
catch ( Exception ex )
{
	throw new Exception( $"Grenade rendering regression at {stage} after {checks.Count} checks: {ex}" );
}
finally
{
	if ( fixture.IsValid() ) fixture.Destroy();
}
