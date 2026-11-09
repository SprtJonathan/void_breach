// Run via sbox-cli run_csharp in host Play. Renders an isolated real lens without player input.
var scene = Game.ActiveScene;
if ( !Game.IsPlaying || scene is null || !Networking.IsHost )
	throw new InvalidOperationException( "Requires host Play." );
var checks = new System.Collections.Generic.List<string>();
var captures = new System.Collections.Generic.List<object>();
void Check( bool pass, string message )
{
	if ( !pass ) throw new Exception( message );
	checks.Add( message );
}
var fixture = new GameObject( scene, true, "Holographic reticle regression" );
fixture.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
fixture.WorldPosition = new Vector3( 24000, -24000, 4000 );
fixture.Tags.Add( "vb_reticle_test" );
try
{
	var material = Material.Load( "materials/weapons/holographic_reticle.vmat" );
	Check( material.ShaderName == "shaders/vb_holographic_reticle.shader" && material.Flags.IsTranslucent,
		"Dedicated shader loads in the transparent pass" );
	var variables = Shader.Load( material.ShaderName ).Schema.Variables;
	var textureInputs = variables.Where( v => v.Type == "Texture" && v.SourceType == "__SetByArtist__" ).Select( v => v.Name ).Distinct().ToArray();
	Check( textureInputs.SequenceEqual( new[] { "g_tReticle" } )
		&& !variables.Any( v => v.Name.Contains( "FrameBuffer" ) || v.Name.Contains( "Depth" ) ),
		"Reticle texture plus native vertex transforms; no scene color/depth textures" );
	var optic = new GameObject( fixture, true, "Real Facepunch holographic lens" );
	// Put the lens center on the camera's +X axis. Use the actual model/UVs, not a proxy quad.
	optic.LocalTransform = new Transform( new Vector3( 30, 0, -1.9606f * 8 ), Rotation.Identity, 8 );
	var renderer = optic.AddComponent<ModelRenderer>();
	renderer.Model = Model.Load( "models/weapons/sbox_attachments/sight_holographic/sight_holographic.vmdl" );
	renderer.Materials.SetOverride( 2, material );
	var cameraObject = new GameObject( fixture, true, "Reticle test camera" );
	var camera = cameraObject.AddComponent<CameraComponent>();
	camera.IsMainCamera = false;
	camera.FieldOfView = 50;
	camera.RenderTags.Add( "vb_reticle_test" );
	using var bitmap = new Bitmap( 1024, 768 );
	await Task.Delay( 100, cancellationToken );
	async Task<(float x, float y, int count, int peak)> Capture( string label )
	{
		cancellationToken.ThrowIfCancellationRequested();
		// Let the renderer advance its per-frame camera/instance constants.
		await Task.Delay( 50, cancellationToken );
		camera.RenderToBitmap( bitmap, false );
		var pixels = bitmap.GetPixels32();
		double sumX = 0, sumY = 0;
		int count = 0, peak = 0;
		for ( int i = 0; i < pixels.Length; i++ )
		{
			var p = pixels[i];
			if ( p.r <= 80 || p.r <= p.g * 2 || p.r <= p.b * 2 ) continue;
			sumX += i % 1024; sumY += i / 1024; count++; peak = Math.Max( peak, (int)p.r );
		}
		var path = System.IO.Path.Combine( exchangeDirectory, $"reticle-{label}.png" );
		System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
		var sample = (x: count > 0 ? (float)(sumX / count) : -1f,
			y: count > 0 ? (float)(sumY / count) : -1f, count, peak);
		captures.Add( new { label, sample.x, sample.y, sample.count, sample.peak, path } );
		return sample;
	}
	var center = await Capture( "center" );
	Check( center.count > 15 && center.peak > 180, "Reticle is bright and visible without scene lighting" );
	Check( Math.Abs( center.x - 511.5f ) < 2 && Math.Abs( center.y - 383.5f ) < 2, "Reticle follows the optical axis" );
	foreach ( float offset in new[] { -2f, 2f } )
	{
		cameraObject.LocalPosition = new Vector3( 0, offset, 0 );
		var moved = await Capture( offset < 0 ? "left-eye" : "right-eye" );
		var lensCenter = optic.WorldTransform.PointToWorld( new Vector3( -0.435f, 0, 1.9606f ) );
		Check( Math.Abs( camera.PointToScreenNormal( lensCenter ).x - 0.5f ) > 0.02f
			&& moved.count > 15 && Math.Abs( moved.x - center.x ) < 2 && Math.Abs( moved.y - center.y ) < 2,
			$"Eye offset {offset}: collimated reticle keeps its direction while the lens moves" );
	}
	cameraObject.LocalPosition = Vector3.Zero;
	fixture.WorldRotation = Rotation.From( 20, 75, 15 );
	var rotated = await Capture( "rotated" );
	Check( rotated.count > 15 && Math.Abs( rotated.x - center.x ) < 2 && Math.Abs( rotated.y - center.y ) < 2,
		"Collimation survives a rotated mount at large world coordinates" );
	fixture.WorldRotation = Rotation.Identity;
	cameraObject.LocalPosition = new Vector3( 0, 12, 0 );
	Check( (await Capture( "outside-lens" )).count == 0, "The lens clips the reticle when the eye leaves its aperture" );
	cameraObject.LocalPosition = new Vector3( 60, 0, 0 );
	cameraObject.LocalRotation = Rotation.FromYaw( 180 );
	Check( (await Capture( "front" )).count == 0, "No reticle is shown looking into the front of the optic" );
	return new { passed = checks.Count, checks, captures };
}
catch ( Exception ex )
{
	throw new Exception( Json.Serialize( new { error = ex.Message, checks, captures } ) );
}
finally
{
	if ( fixture.IsValid() ) fixture.Destroy();
}
