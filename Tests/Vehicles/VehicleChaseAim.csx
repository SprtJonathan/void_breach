// sbox-cli run_csharp, host Play with one local player. Runtime fixtures are destroyed.
var scene = Game.ActiveScene;
if ( !Game.IsPlaying || scene is null || !Networking.IsHost ) throw new Exception( "Requires host Play" );
for ( int i = 0; i < 100 && !scene.GetAll<PlayerController>().Any( p => !p.IsProxy ); i++ )
	await Task.Delay( 50, cancellationToken );
var player = scene.GetAll<PlayerController>().Single( p => !p.IsProxy );
var bridge = player.GetComponent<VBVehiclePlayerDriver>();
if ( bridge.IsSeated ) throw new Exception( "Start the test on foot" );
var health = player.GetComponent<VBHealthComponent>();
var oldPose = player.WorldTransform; var oldAngles = player.EyeAngles;
bool oldInput = player.UseInputControls, oldLook = player.UseLookControls, oldThird = player.ThirdPerson;
bool oldBridgeInput = bridge.UsePlayerInput, oldGod = health.GodMode;
var objects = new System.Collections.Generic.List<GameObject>();
var checks = new System.Collections.Generic.List<string>();
var captures = new System.Collections.Generic.List<string>();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var submit = typeof(VBVehicleTurret).GetMethod( "SubmitControls", flags );
void Check( bool pass, string label ) { if ( !pass ) throw new Exception( label ); checks.Add( label ); }
GameObject Box( string name, Vector3 position, Vector3 size )
{
	var obj = new GameObject( scene, true, name ); objects.Add( obj ); obj.WorldPosition = position;
	obj.AddComponent<BoxCollider>().Scale = size;
	return obj;
}
void Capture( string label )
{
	using var bitmap = new Bitmap( 1920, 1080 ); scene.Camera.RenderToBitmap( bitmap, true );
	string path = System.IO.Path.Combine( exchangeDirectory, $"vehicle-chase-{label}.png" );
	System.IO.File.WriteAllBytes( path, bitmap.ToPng() ); captures.Add( path );
}
try
{
	player.UseInputControls = false; player.UseLookControls = false; bridge.UsePlayerInput = false; health.GodMode = true;
	var origin = new Vector3( 24000f, 24000f, 4000f );
	Box( "Chase regression floor", origin - Vector3.Up * 16f, new Vector3( 8000f, 8000f, 32f ) );
	var car = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( origin + Vector3.Up * 34f );
	objects.Add( car ); car.NetworkSpawn();
	var vehicle = car.GetComponent<VBVehicleController>();
	var turret = car.GetComponent<VBVehicleTurret>();
	var seat = car.GetComponent<VBVehicleDriverSeat>();
	await Task.Delay( 400, cancellationToken );
	vehicle.Body.MotionEnabled = false; vehicle.Body.Velocity = Vector3.Zero; vehicle.Body.AngularVelocity = Vector3.Zero;
	car.WorldPosition = origin + Vector3.Up * 34f; car.WorldRotation = Rotation.Identity;
	player.WorldPosition = origin + Vector3.Left * 110f + Vector3.Up * 5f; player.Body.Velocity = Vector3.Zero;
	await Task.Delay( 100, cancellationToken );
	Check( ((Component.IPressable)seat).Press( new Component.IPressable.Event( player, null ) ), "Native driver entry" );
	await Task.Delay( 400, cancellationToken );
	player.ThirdPerson = true; player.EyeAngles = Angles.Zero;
	await Task.Delay( 400, cancellationToken );
	var camera = scene.Camera;
	Check( Vector3.Dot( camera.View.Rotation.Forward, Rotation.FromPitch( bridge.ChaseAimPitch ).Forward ) > 0.9999f,
		"Neutral TPS look uses the forward framing angle instead of targeting the chassis" );
	var centre = scene.Trace.Ray( camera.View.Position, camera.View.Position + camera.View.Rotation.Forward * turret.Range ).Run();
	Check( centre.GameObject != car && (!centre.Hit || (centre.EndPosition-car.WorldPosition).Length > 400f), $"Neutral screen centre clears the car collision and the nearby ground (hit={centre.GameObject?.Name}, point={centre.EndPosition-origin},camera={camera.View.Position-origin},third={player.ThirdPerson},seated={bridge.IsSeated},chase={bridge.ChaseOffset},pivot={bridge.ChasePivotHeight})" );
	Check( camera.View.Position.z > turret.MuzzlePosition.z && camera.View.Position.x < car.WorldPosition.x - 150f,
		"Raised chase framing keeps the gun below the reticle" );
	Box( "Distant aim wall", origin + new Vector3( 1000f, 0f, 350f ), new Vector3( 20f, 5000f, 700f ) );
	foreach ( var angles in new[] { Angles.Zero, new Angles( -8f, 35f, 0f ), new Angles( 18f, -35f, 0f ), new Angles( 0f, 180f, 0f ) } )
	{
		player.EyeAngles = angles;
		for ( int sample = 0; sample < 30; sample++ )
		{
			var aim = turret.GetAimForView( camera.View.Transform );
			submit.Invoke( turret, new object[] { player, aim, false } );
			await Task.Delay( 30, cancellationToken );
		}
		var impact = turret.TraceBarrel();
		var screen = camera.PointToScreenNormal( impact.EndPosition );
		Check( (screen - new Vector2( 0.5f, 0.5f )).Length < 0.003f,
			$"Barrel converges on TPS centre for look {angles} (screen={screen}, gun={turret.Pitch}/{turret.Yaw}, camera={camera.View.Rotation.Angles()}, wanted={turret.GetAimForView(camera.View.Transform)}, impact={impact.EndPosition-car.WorldPosition}, hit={impact.GameObject?.Name},canOperate={turret.CanOperate(player)},operator={turret.Operator?.Id},command={typeof(VBVehicleTurret).GetField("_aim",flags).GetValue(turret)},lastInput={typeof(VBVehicleTurret).GetField("_lastInput",flags).GetValue(turret)},now={Time.Now})" );
		Check( impact.GameObject != car, $"Shot/HUD ray ignores its own car for look {angles}" );
	}
	player.EyeAngles = Angles.Zero;
	await Task.Delay( 500, cancellationToken );
	for ( int sample = 0; sample < 15; sample++ )
	{
		submit.Invoke( turret, new object[] { player, turret.GetAimForView( camera.View.Transform ), false } );
		await Task.Delay( 30, cancellationToken );
	}
	// A distinct final view proves TPS commands use the callback's view, not Camera.View from Update.
	var finalView = camera.View;
	finalView.Rotation = Rotation.From( -3f, 20f, 0f );
	var expectedAim = turret.GetAimForView( finalView.Transform ).ToRotation();
	typeof(VBVehiclePlayerDriver).GetField( "_nextSend", flags ).SetValue( bridge, float.NegativeInfinity );
	bridge.UsePlayerInput = true;
	((ICameraModifier)bridge).PostCameraSetup( camera, in finalView );
	bridge.UsePlayerInput = false;
	await Task.Delay( 100, cancellationToken );
	var submitted = (Angles)typeof(VBVehicleTurret).GetField( "_aim", flags ).GetValue( turret );
	Check( Vector3.Dot( (car.WorldRotation * submitted.ToRotation()).Forward, expectedAim.Forward ) > 0.9999f,
		"TPS input uses this frame's final camera view" );
	for ( int sample = 0; sample < 15; sample++ )
	{
		submit.Invoke( turret, new object[] { player, turret.GetAimForView( camera.View.Transform ), false } );
		await Task.Delay( 30, cancellationToken );
	}
	Capture( "free" );
	var freeOffset = camera.View.Position - car.WorldPosition;
	var authoredYawRange = seat.YawRange;
	var authoredPitchRange = seat.PitchRange;
	var authoredAccess = turret.Access;
	foreach ( int direction in new[] { 1, -1 } )
	{
		if ( direction < 0 ) turret.Access = VBVehicleTurretAccess.Gunner;
		bool continuous = true;
		for ( int step = 1; step <= 24; step++ )
		{
			player.EyeAngles = new Angles( 0f, player.EyeAngles.yaw + direction * 30f, 0f );
			seat.CalculateEyeTransform( player );
			seat.UpdatePlayerAnimator( player, player.Renderer );
			var orbitView = camera.View;
			((ICameraModifier)bridge).ModifyCamera( camera, ref orbitView );
			var expected = Rotation.From( bridge.ChaseAimPitch, direction * step * 30f, 0f );
			continuous &= Vector3.Dot( orbitView.Rotation.Forward, expected.Forward ) > 0.9999f;
		}
		Check( continuous, $"Two complete TPP rotations remain continuous through the native seat/animator, direction {direction}" );
	}
	turret.Access = authoredAccess;
	foreach ( float pitch in new[] { -70f, 70f } )
	{
		player.EyeAngles = new Angles( pitch, 0f, 0f );
		seat.CalculateEyeTransform( player );
		seat.UpdatePlayerAnimator( player, player.Renderer );
		var orbitView = camera.View;
		((ICameraModifier)bridge).ModifyCamera( camera, ref orbitView );
		Check( MathF.Abs( player.EyeAngles.pitch - pitch ) < 0.01f
			&& Vector3.Dot( orbitView.Rotation.Forward, Rotation.FromPitch( pitch + bridge.ChaseAimPitch ).Forward ) > 0.9999f,
			$"TPP vertical free look is not restricted to the former framing cap ({pitch} degrees)" );
	}
	Check( seat.YawRange == authoredYawRange && seat.PitchRange == authoredPitchRange,
		"Free-orbit animation preserves authored first-person seat limits" );
	player.EyeAngles = Angles.Zero;
	await Task.Delay( 800, cancellationToken );
	// Measure the mechanisms that keep the close chase camera quiet.
	Check( -bridge.ChaseOffset.x <= 280f && bridge.ChaseOffset.z + bridge.ChasePivotHeight <= 120f,
		"Close, low vehicle framing replaces the distant elevated boom" );
	var stableView = camera.View;
	typeof(VBVehiclePlayerDriver).GetField( "_cameraSpeedAmount", flags ).SetValue( bridge, 1f );
	typeof(VBVehiclePlayerDriver).GetField( "_cameraTurboAmount", flags ).SetValue( bridge, 1f );
	typeof(VBVehiclePlayerDriver).GetField( "_cameraRoll", flags ).SetValue( bridge, 1f );
	var probe = stableView; probe.FieldOfView = 90f;
	((ICameraModifier)bridge).ModifyCamera( camera, ref probe );
	Check( MathF.Abs( probe.FieldOfView - 90f ) < 0.001f && MathF.Abs( probe.Rotation.Angles().roll ) < 0.001f,
		"Speed, turbo and lateral acceleration leave TPP FOV and horizon stable" );
	await Task.Delay( 700, cancellationToken );
	float baseHeight = camera.View.Position.z;
	car.WorldPosition += Vector3.Up * 8f;
	await Task.Delay( 80, cancellationToken );
	float rise = camera.View.Position.z - baseHeight;
	Check( rise > 0.05f && rise < 7f, $"Suspension heave is absorbed instead of copied to the camera (rise={rise})" );
	car.WorldPosition -= Vector3.Up * 8f;
	await Task.Delay( 800, cancellationToken );
	car.WorldRotation = Rotation.From( 8f, 90f, 6f );
	player.EyeAngles = new Angles( 0f, 40f, 0f );
	await Task.Delay( 300, cancellationToken );
	Check( MathF.Abs( camera.View.Rotation.Angles().yaw - 40f ) < 0.1f && MathF.Abs( camera.View.Rotation.Angles().roll ) < 0.01f,
		"Free look responds directly without inheriting chassis steering or roll" );
	await Task.Delay( 1400, cancellationToken );
	Check( MathF.Abs( camera.View.Rotation.Angles().yaw - 40f ) < 0.1f,
		"A parked car leaves the driver's free look alone" );
	vehicle.Body.Velocity = Vector3.Forward * 200f;
	await Task.Delay( 600, cancellationToken );
	float followingYaw = camera.View.Rotation.Angles().yaw;
	Check( followingYaw > 55f && followingYaw < 90f,
		$"Moving car recentres smoothly after the free-look delay (yaw={followingYaw})" );
	vehicle.Body.Velocity = Vector3.Zero;
	car.WorldRotation = Rotation.Identity;
	player.ThirdPerson = false;
	for ( int sample = 0; sample < 20 && (bool)typeof(VBVehiclePlayerDriver).GetField( "_chaseInitialized", flags ).GetValue( bridge ); sample++ )
		await Task.Delay( 50, cancellationToken );
	player.EyeAngles = Angles.Zero; player.ThirdPerson = true;
	await Task.Delay( 700, cancellationToken );
	freeOffset = camera.View.Position - car.WorldPosition;
	var wall = Box( "Chase camera obstruction", car.WorldPosition + new Vector3( -150f, 0f, 170f ), new Vector3( 20f, 600f, 340f ) );
	await Task.Delay( 300, cancellationToken );
	Check( camera.View.Position.x > wall.WorldPosition.x + 18f, "Camera compresses in front of a rear wall" );
	Check( !scene.Trace.Sphere( 7f, camera.View.Position, camera.View.Position ).IgnoreGameObjectHierarchy( car ).Run().StartedSolid,
		"Compressed camera stays outside world collision" );
	Check( Vector3.Dot( camera.View.Rotation.Forward, Rotation.FromPitch( bridge.ChaseAimPitch ).Forward ) > 0.9999f,
		$"Wall compression preserves aim direction (view={camera.View.Rotation.Angles()}, heading={car.WorldRotation.Angles()}, input={player.EyeAngles})" );
	for ( int sample = 0; sample < 20; sample++ )
	{
		submit.Invoke( turret, new object[] { player, turret.GetAimForView( camera.View.Transform ), false } );
		await Task.Delay( 30, cancellationToken );
	}
	var compressedImpact = camera.PointToScreenNormal( turret.TraceBarrel().EndPosition );
	Check( (compressedImpact - new Vector2( 0.5f, 0.5f )).Length < 0.003f, "Compressed TPS camera and barrel still converge" );
	Capture( "wall" );
	wall.Destroy();
	await Task.Delay( 700, cancellationToken );
	Check( (camera.View.Position - car.WorldPosition - freeOffset).Length < 1f, "Camera returns to its boom after the wall clears" );
	car.WorldPosition += Vector3.Backward * 300f;
	await Task.Delay( 80, cancellationToken );
	Check( (camera.View.Position - car.WorldPosition - freeOffset).Length < 2f,
		"Chassis translation cannot catch up with its damped chase camera" );
	var nearWall = Box( "Aim point behind the gun", car.WorldPosition + new Vector3( -160f, 0f, 150f ), new Vector3( 10f, 300f, 300f ) );
	await Task.Delay( 100, cancellationToken );
	var blockedView = new Transform( car.WorldPosition + new Vector3( -230f, 0f, 120f ), Rotation.Identity );
	Check( Vector3.Dot( turret.GetAimForView( blockedView ).ToRotation().Forward, Vector3.Forward ) > 0.999f,
		"An obstacle behind the gun cannot reverse turret aim" );
	nearWall.Destroy();
	player.ThirdPerson = false; player.EyeAngles = new Angles( -5f, 15f, 0f );
	await Task.Delay( 400, cancellationToken );
	Check( (camera.View.Position - player.EyeTransform.Position).Length < 0.1f
		&& Vector3.Dot( camera.View.Rotation.Forward, player.EyeTransform.Rotation.Forward ) > 0.9999f,
		"First-person driver camera retains its eye transform" );
	seat.RequestSeatChange( player, turret.GunnerSeat );
	await Task.Delay( 400, cancellationToken );
	Check( bridge.ActiveSeat == turret.GunnerSeat && (camera.View.Position - turret.CameraPosition).Length < 0.1f,
		"First-person gunner camera retains its turret position" );
	return new { passed = checks.Count, checks, captures };
}
catch ( Exception ex ) { throw new Exception( Json.Serialize( new { error = ex.Message, checks, captures } ) ); }
finally
{
	if ( bridge.IsSeated ) { bridge.ActiveSeat.RequestExit( player ); await Task.Delay( 300 ); }
	foreach ( var obj in objects.AsEnumerable().Reverse() ) if ( obj.IsValid() ) obj.Destroy();
	player.WorldTransform = oldPose; player.EyeAngles = oldAngles; player.Body.Velocity = Vector3.Zero;
	player.UseInputControls = oldInput; player.UseLookControls = oldLook; player.ThirdPerson = oldThird;
	bridge.UsePlayerInput = oldBridgeInput; health.GodMode = oldGod;
}
