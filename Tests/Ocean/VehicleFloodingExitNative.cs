// sbox-cli run_csharp method body. Temporary real prefabs only; no scene saved.
if ( !Game.IsPlaying || Game.ActiveScene is null ) throw new InvalidOperationException( "Start Play first." );
var scene = Game.ActiveScene;
var created = new System.Collections.Generic.List<GameObject>();
var savedCameras = new System.Collections.Generic.Dictionary<PlayerController, bool>();
var results = new System.Collections.Generic.List<object>();
var origin = new Vector3( 60000, 65000, 8000 );
var stage = "setup";
GameObject Make( string name, Vector3 position )
{
	var go = new GameObject( false, name ); go.SetParent( scene, false ); go.WorldPosition = position;
	go.NetworkMode = NetworkMode.Never; created.Add( go ); return go;
}
void Check( string name, bool pass, string detail )
{
	results.Add( new { name, pass, detail } ); Log.Info( $"Vehicle flooding/exit {name} {pass}: {detail}" );
}
var cameraObject = Make( "Flooding regression camera", origin + new Vector3( -400, -400, 300 ) );
var camera = cameraObject.AddComponent<CameraComponent>(); camera.Priority = 20000; camera.IsMainCamera = true;
cameraObject.WorldRotation = Rotation.LookAt( origin - cameraObject.WorldPosition ); cameraObject.Enabled = true;
async Task Pause( int ms )
{
	for ( var remaining = ms; remaining > 0; remaining -= 40 )
	{
		await Task.Delay( Math.Min( remaining, 40 ), cancellationToken );
		if ( !Game.IsPlaying || Game.ActiveScene != scene ) throw new Exception( "Play stopped or scene changed." );
		foreach ( var p in scene.GetAll<PlayerController>() )
		{
			if ( !savedCameras.ContainsKey( p ) ) savedCameras.Add( p, p.UseCameraControls );
			p.UseCameraControls = false;
		}
		camera.IsMainCamera = true;
	}
}
VBVehicleController Car( Vector3 position )
{
	var go = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( position ); created.Add( go );
	return go.GetComponent<VBVehicleController>();
}
async Task ExitScenario( string name, Vector3 position, bool rolled = false, bool blocked = false, bool missing = false, bool moving = false, bool gunner = false )
{
	stage = name;
	var car = Car( position ); car.Body.MotionEnabled = false;
	var pawn = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( position + Vector3.Left * 110f ); created.Add( pawn );
	var player = pawn.GetComponent<PlayerController>(); var bridge = pawn.GetComponent<VBVehiclePlayerDriver>();
	player.UseInputControls = false; player.UseCameraControls = false; player.UseLookControls = false; player.EnablePressing = false;
	bridge.UsePlayerInput = false;
	var hud = pawn.Children.FirstOrDefault( child => child.Name == "HUD" ); if ( hud.IsValid() ) hud.Enabled = false;
	pawn.NetworkSpawn( Connection.Local );
	await Pause( 250 );
	pawn.WorldPosition = position + Vector3.Left * 110f; player.Body.Velocity = Vector3.Zero;
	player.UseInputControls = true; player.EnablePressing = true;
	var seat = gunner ? (VBVehicleSeat)car.GetComponent<VBVehicleTurret>().GunnerSeat : car.GetComponent<VBVehicleDriverSeat>();
	// The gunner marker is higher than the driver marker; entry visibility is still native.
	if ( gunner ) pawn.WorldPosition = seat.WorldPosition + Vector3.Left * 110f;
	((Component.IPressable)seat).Press( new Component.IPressable.Event( player, null ) );
	await Pause( 350 );
	if ( bridge.ActiveSeat != seat || seat.Occupant != player ) throw new Exception( "Native entry failed for " + name );
	if ( rolled ) { car.WorldRotation = Rotation.From( 0, 0, 180 ); await Pause( 150 ); }
	var blockers = new System.Collections.Generic.List<GameObject>();
	if ( blocked ) foreach ( var exit in seat.ExitPoints )
	{
		var wall = Make( "Blocked door", exit.WorldPosition + Vector3.Up * (player.BodyHeight * 0.5f) );
		wall.AddComponent<BoxCollider>().Scale = new Vector3( player.BodyRadius * 2f + 12, player.BodyRadius * 2f + 12, player.BodyHeight + 16 );
		wall.Enabled = true; blockers.Add( wall );
	}
	if ( missing ) seat.ExitPoints = Array.Empty<GameObject>();
	await Pause( 100 );
	if ( moving ) { car.Body.MotionEnabled = true; car.Body.Velocity = Vector3.Forward * VBVehicleController.KmhToUnits( 70 ); }
	var allowed = seat.CanLeave( player );
	// This is the native seated E/failed-use callback, not a forced unparent.
	((PlayerController.IEvents)pawn.GetComponent<Sandbox.Movement.SitMoveMode>()).FailPressing();
	var inherited = bridge.Seating.ExitVelocity;
	await Pause( 250 );
	var clear = scene.Trace.Box( new BBox( new Vector3( -player.BodyRadius, -player.BodyRadius, 0 ),
		new Vector3( player.BodyRadius, player.BodyRadius, player.BodyHeight ) ), player.WorldPosition, player.WorldPosition )
		.IgnoreGameObjectHierarchy( pawn ).WithoutTags( new[] { "trigger", "water" } ).Run();
	Check( name, allowed && !bridge.IsSeated && seat.Occupant is null && pawn.IsRoot && player.Body.Enabled
		&& player.ColliderObject.Enabled && player.UseInputControls && !clear.StartedSolid && !clear.Hit,
		$"allowed={allowed}, seated={bridge.IsSeated}, root={pawn.IsRoot}, input={player.UseInputControls}, body={player.Body.Enabled}, clearance={!(clear.Hit || clear.StartedSolid)}, swimming={player.IsSwimming}" );
	if ( moving ) Check( "EXIT_INHERITS_SPEED", inherited.Length > VBVehicleController.KmhToUnits( 60 ), $"velocity={inherited}" );
	if ( name == "DEEP_WATER_EXIT" ) Check( "EXIT_RESUMES_SWIMMING", player.IsSwimming, $"swimming={player.IsSwimming}" );
	player.UseInputControls = false;
	pawn.Destroy(); car.GameObject.Destroy(); foreach ( var wall in blockers ) wall.Destroy();
	await Pause( 100 );
}
try
{
	var waterObject = Make( "Flooding lake", origin + Vector3.Up * 8 );
	var water = waterObject.AddComponent<VoidBreach.Ocean.VBFFTOcean>();
	water.Shape = VoidBreach.Ocean.FftWaterShape.Lake; water.Size = new Vector2( 6000, 4000 ); water.WaveHeight = 0;
	water.NativePhysics = true; water.Interactions = false; water.UnderwaterEffects = false; water.Depth = 3000; water.Priority = 3000;
	waterObject.Enabled = true;
	var floorObject = Make( "Shallow road", origin - Vector3.Up * 16 );
	floorObject.AddComponent<BoxCollider>().Scale = new Vector3( 6000, 4000, 32 ); floorObject.Enabled = true;
	await Pause( 600 );
	var startClock = water.SimulationClock;
	for ( var retry = 0; retry < 100 && water.SimulationClock - startClock < 0.2f; retry++ ) await Pause( 100 );
	if ( water.SimulationClock - startClock < 0.2f ) throw new Exception( "Physics clock did not start." );
	var flooding = Car( origin + new Vector3( -400, -500, 40 ) );
	var sealedCar = Car( origin + new Vector3( 400, -500, 40 ) ); sealedCar.FloodInWater = false;
	await Pause( 1800 );
	Check( "SHALLOW_WHEELS_DO_NOT_FLOOD", flooding.WaterFill == 0f && flooding.GroundedWheels == 4,
		$"fill={flooding.WaterFill}, wheels={flooding.GroundedWheels}" );
	await ExitScenario( "DRY_EXIT", origin + new Vector3( 0, 600, 40 ) );
	floorObject.Destroy(); waterObject.WorldPosition = origin + Vector3.Up * 180;
	await Pause( 700 );
	var earlyFill = flooding.WaterFill; var earlyHeight = flooding.WorldPosition.z;
	var floodClock = water.SimulationClock;
	Check( "FLOODING_IS_PROGRESSIVE", earlyFill > 0f && earlyFill < 0.3f, $"early fill={earlyFill}, z={earlyHeight}" );
	await ExitScenario( "DEEP_WATER_EXIT", origin + new Vector3( 0, 600, 20 ) );
	await ExitScenario( "HIGH_SPEED_EXIT", origin + new Vector3( 0, 600, 20 ), moving: true );
	await ExitScenario( "OVERTURNED_EXIT", origin + new Vector3( 0, 600, 20 ), rolled: true );
	await ExitScenario( "BLOCKED_DOORS_EXIT", origin + new Vector3( 0, 600, 20 ), blocked: true );
	await ExitScenario( "NO_MARKERS_EXIT", origin + new Vector3( 0, 600, 20 ), missing: true );
	await ExitScenario( "SUBMERGED_GUNNER_EXIT", origin + new Vector3( 0, 600, -80 ), gunner: true );
	stage = "wait for gradual sinking";
	for ( var step = 0; step < 700 && water.SimulationClock - floodClock < 45f; step++ )
	{
		await Pause( 100 );
		if ( step % 50 == 0 ) Log.Info( $"Flood observation: clock={water.SimulationClock - floodClock:F1}, fill={flooding.WaterFill:F3}, z={flooding.WorldPosition.z:F1}, sealedZ={sealedCar.WorldPosition.z:F1}" );
	}
	var floodedBounds = flooding.Body.GetWorldBounds(); var sealedBounds = sealedCar.Body.GetWorldBounds();
	Check( "ROAD_VEHICLE_SINKS", flooding.WaterFill > 0.9f && floodedBounds.Maxs.z < water.WorldPosition.z - 30,
		$"seconds={water.SimulationClock - floodClock}, fill={flooding.WaterFill}, hullTop={floodedBounds.Maxs.z}, water={water.WorldPosition.z}, vz={flooding.Body.Velocity.z}" );
	Check( "SEALED_HULL_OPTION_FLOATS", sealedCar.WaterFill == 0 && sealedBounds.Maxs.z > water.WorldPosition.z
		&& sealedBounds.Mins.z < water.WorldPosition.z,
		$"fill={sealedCar.WaterFill}, bottom={sealedBounds.Mins.z}, top={sealedBounds.Maxs.z}, water={water.WorldPosition.z}" );
	var retained = flooding.WaterFill;
	flooding.GameObject.WorldPosition = origin + Vector3.Up * 500; flooding.Body.MotionEnabled = false; await Pause( 250 );
	Check( "FLOOD_STATE_SURVIVES_REENTRY", flooding.WaterFill == retained, $"fill={flooding.WaterFill}" );
}
catch ( Exception error ) { results.Add( new { name = "FIXTURE_EXCEPTION", pass = false, detail = stage + ": " + error } ); }
finally
{
	foreach ( var go in created.AsEnumerable().Reverse() ) if ( go.IsValid() ) go.Destroy();
	foreach ( var pair in savedCameras ) if ( pair.Key.IsValid() ) pair.Key.UseCameraControls = pair.Value;
}
await Task.Delay( 1000, cancellationToken );
return results;
