// sbox-cli run_csharp method body. Play mode required. Uses the actual vehicle,
// native seat, throttle command buffer and mounted gun; no velocity/transform driving.
if ( !Game.IsPlaying || Game.ActiveScene is null ) throw new InvalidOperationException( "Start Play first." );
var scene = Game.ActiveScene;
var created = new System.Collections.Generic.List<GameObject>();
var savedCameras = new System.Collections.Generic.Dictionary<PlayerController, bool>();
var results = new System.Collections.Generic.List<object>();
var origin = new Vector3( 60000, 60000, 4096 );
VBVehicleController vehicle = null;
var stage = "floor";
GameObject Make( string name, Vector3 position )
{
	var go = new GameObject( false, name ); go.SetParent( scene, false ); go.WorldPosition = position;
	go.NetworkMode = NetworkMode.Never; created.Add( go ); return go;
}
void Check( string name, bool pass, string detail ) { results.Add( new { name, pass, detail } ); Log.Info( $"Vehicle water {name} {pass}: {detail}" ); }
var cameraObject = Make( "Vehicle water regression camera", origin + new Vector3( -100, -160, 140 ) );
var camera = cameraObject.AddComponent<CameraComponent>(); camera.Priority = 20000; camera.IsMainCamera = true;
cameraObject.Enabled = true;
async Task Pause( int ms )
{
	for ( var left = ms; left > 0; left -= 40 )
	{
		await Task.Delay( Math.Min( left, 40 ), cancellationToken );
		foreach ( var controller in scene.GetAll<PlayerController>() )
		{
			if ( !savedCameras.ContainsKey( controller ) ) savedCameras.Add( controller, controller.UseCameraControls );
			controller.UseCameraControls = false;
		}
		var focus = vehicle.IsValid() ? vehicle.WorldPosition : origin;
		cameraObject.WorldPosition = focus + new Vector3( -100, -160, 140 );
		cameraObject.WorldRotation = Rotation.LookAt( focus - cameraObject.WorldPosition ); camera.IsMainCamera = true;
	}
}
try
{
	var floorObject = Make( "Inundated road", origin - Vector3.Up * 16 );
	var floor = floorObject.AddComponent<BoxCollider>(); floor.Scale = new Vector3( 8000, 2000, 32 ); floor.Friction = 1; floorObject.Enabled = true;
	stage = "water";
	var waterObject = Make( "Shallow water 8 units above road", origin + Vector3.Up * 8 );
	waterObject.NetworkMode = NetworkMode.Object;
	var water = waterObject.AddComponent<VoidBreach.Ocean.VBFFTOcean>();
	water.Shape = VoidBreach.Ocean.FftWaterShape.Lake; water.Size = new Vector2( 8000, 2000 ); water.WaveHeight = 0;
	water.NativePhysics = true; water.UnderwaterEffects = false; water.Priority = 3000;
	stage = "spawn water"; waterObject.Enabled = true; waterObject.NetworkSpawn();
	stage = "wait physics";
	await Pause( 1500 );
	var clock = water.SimulationClock;
	for ( var attempt = 0; attempt < 150 && water.SimulationClock - clock < 0.3f; attempt++ ) await Pause( 100 );
	if ( water.SimulationClock - clock < 0.3f ) throw new Exception( "Physics startup timeout" );
	stage = "clone vehicle";
	var car = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( origin + Vector3.Up * 40 );
	stage = "configure vehicle";
	created.Add( car ); vehicle = car.GetComponent<VBVehicleController>(); car.Enabled = true;
	await Pause( 1200 );
	Check( "REAL_VEHICLE_CONFIGURED", vehicle.IsConfigured && vehicle.GroundedWheels == 4, $"grounded={vehicle.GroundedWheels}" );
	var pawn = Make( "Regression driver", vehicle.WorldPosition + Vector3.Left * 100 );
	pawn.NetworkMode = NetworkMode.Object;
	var pawnBody = pawn.AddComponent<Rigidbody>(); pawnBody.MassOverride = 80;
	var player = pawn.AddComponent<PlayerController>(); player.Body = pawnBody; player.UseInputControls = false;
	player.UseCameraControls = false; player.UseLookControls = false; player.EnablePressing = false;
	pawn.AddComponent<Sandbox.Movement.MoveModeWalk>(); pawn.AddComponent<Sandbox.Movement.SitMoveMode>();
	var driver = pawn.AddComponent<VBVehiclePlayerDriver>(); driver.UsePlayerInput = false;
	pawn.AddComponent<VBHealthComponent>().GodMode = true;
	pawn.Enabled = true; pawn.NetworkSpawn( Connection.Local ); await Pause( 400 );
	var seat = vehicle.GetComponent<VBVehicleDriverSeat>();
	((Component.IPressable)seat).Press( new Component.IPressable.Event( player, null ) );
	await Pause( 400 );
	Check( "REAL_DRIVER_SEATED", seat.Driver == player && driver.Seat == seat, $"driver={seat.Driver == player}" );
	var before = water.AutomaticImpactCount; var start = vehicle.WorldPosition;
	var submit = typeof(VBVehicleDriverSeat).GetMethod( "SubmitPlayerControls", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
	for ( var tick = 0; tick < 35; tick++ )
	{
		submit.Invoke( seat, new object[] { player, new VBVehicleControls( 1, 0, 0, false ) } ); await Pause( 40 );
	}
	var chassis = vehicle.Body.GetWorldBounds();
	Check( "THROTTLE_DRIVES", vehicle.WorldPosition.x - start.x > 30, $"distance={vehicle.WorldPosition.x - start.x}, kmh={vehicle.SpeedKmh}" );
	Check( "SHALLOW_WHEELS_EMIT", water.AutomaticImpactCount > before && chassis.Mins.z > water.WorldPosition.z + 3,
		$"events={water.AutomaticImpactCount - before}, chassisBottom={chassis.Mins.z}, water={water.WorldPosition.z}, network={water.Network.Active}" );
	var turret = vehicle.GetComponent<VBVehicleTurret>();
	var aim = typeof(VBVehicleTurret).GetMethod( "SubmitControls", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
	var shotCount = water.ShotImpactCount;
	for ( var tick = 0; tick < 30; tick++ )
	{
		submit.Invoke( seat, new object[] { player, VBVehicleControls.Parked } );
		aim.Invoke( turret, new object[] { player, new Angles( 35, 0, 0 ), true } ); await Pause( 40 );
	}
	Check( "REAL_TURRET_WATER_HITS", water.ShotImpactCount > shotCount, $"shots={water.ShotImpactCount - shotCount}, pitch={turret.Pitch}, operator={turret.Operator == player}" );
	var gpu = typeof(VoidBreach.Ocean.VBFFTOcean).GetField( "_gpuCoarseField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance ).GetValue( water );
	var texture = (Texture)gpu.GetType().GetProperty( "Output" ).GetValue( gpu );
	var pixels = new Vector4[128 * 128];
	texture.GetPixels<Vector4>( (0, 0, 128, 128), 0, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F );
	Check( "VEHICLE_GPU_FINITE", pixels.All( p => float.IsFinite( p.x + p.y + p.z + p.w ) ) && pixels.Any( p => MathF.Abs( p.x ) > 0.001f ), $"height={pixels.Max( p => MathF.Abs( p.x ) )}" );
	// Resource teardown/recreation while graphics commands have recently used them.
	for ( var cycle = 0; cycle < 20; cycle++ )
	{
		water.Interactions = false; await Pause( 60 ); water.Interactions = true; await Pause( 60 );
		water.EmitSurfaceImpact( vehicle.WorldPosition.WithZ( water.WorldPosition.z ), 40, 70 ); await Pause( 60 );
	}
	Check( "GPU_LIFECYCLE_20_CYCLES", water.PhysicsVolume.IsValid() && water.InteractionGpuDispatches > 0, $"dispatches={water.InteractionGpuDispatches}" );
	seat.RequestExit( player );
	await Pause( 250 );
}
catch ( Exception error )
{
	results.Add( new { name = "FIXTURE_EXCEPTION", pass = false, detail = stage + ": " + error.ToString() } );
}
finally
{
	foreach ( var go in created.AsEnumerable().Reverse() ) if ( go.IsValid() ) go.Destroy();
	foreach ( var pair in savedCameras ) if ( pair.Key.IsValid() ) pair.Key.UseCameraControls = pair.Value;
}
// A successful dispatch is insufficient: queued draw commands and finalizers also
// have to survive destruction of the runtime objects and their GPU resources.
await Task.Delay( 1000, cancellationToken );
GC.Collect(); GC.WaitForPendingFinalizers();
await Task.Delay( 5000, cancellationToken );
Check( "POST_TEARDOWN_SURVIVES", created.All( go => !go.IsValid() ), "all temporary objects destroyed; renderer and finalizers survived cleanup" );
return results;
