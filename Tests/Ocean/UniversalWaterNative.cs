// Run with sbox-cli run_csharp { path: <absolute path> } while playing a scene.
// Temporary runtime objects only; no scene is saved. GPU readback is TEST ONLY.
if ( !Game.IsPlaying || Game.ActiveScene is null ) throw new InvalidOperationException( "Start play mode first." );
var scene = Game.ActiveScene;
var created = new System.Collections.Generic.List<GameObject>();
var results = new System.Collections.Generic.List<object>();
var anchor = new Vector3( 60000, 60000, 4096 );
var water = (VoidBreach.Ocean.VBFFTOcean)null;
var cameraControls = new System.Collections.Generic.Dictionary<PlayerController, bool>();
GameObject Object( string name, Vector3 position )
{
	var go = new GameObject( false, name ); go.SetParent( scene, false ); go.WorldPosition = position;
	go.NetworkMode = NetworkMode.Never; created.Add( go ); return go;
}
void Check( string name, bool pass, string detail )
{
	results.Add( new { name, pass, detail } );
	Log.Info( $"Universal water {name} {pass}: {detail}" );
	if ( !pass ) Log.Warning( $"Universal water test FAIL: {name}: {detail}" );
}
try
{
	var waterObject = Object( "Universal water validation", anchor );
	water = waterObject.AddComponent<VoidBreach.Ocean.VBFFTOcean>();
	water.Shape = VoidBreach.Ocean.FftWaterShape.Lake; water.Size = new Vector2( 2000, 1600 );
	water.WaveHeight = 0; water.NativePhysics = false; water.UnderwaterEffects = false; water.Priority = 1000;
	water.InteractionDamping = 2; water.Resolution = 128; water.InteractionResolution = 128;
	waterObject.Enabled = true;
	var cameraObject = Object( "Universal water test camera", anchor + new Vector3( -140, -140, 160 ) );
	cameraObject.WorldRotation = Rotation.LookAt( anchor - cameraObject.WorldPosition );
	var camera = cameraObject.AddComponent<CameraComponent>(); camera.Priority = 20000;
	camera.IsMainCamera = true; camera.ZFar = 10000; cameraObject.Enabled = true;
	async Task Pause( int milliseconds, CancellationToken token )
	{
		for ( var remaining = milliseconds; remaining > 0; remaining -= 40 )
		{
			await Task.Delay( Math.Min( remaining, 40 ), token );
			foreach ( var controller in scene.GetAll<PlayerController>() )
			{
				if ( !cameraControls.ContainsKey( controller ) ) cameraControls[controller] = controller.UseCameraControls;
				controller.UseCameraControls = false;
			}
			cameraObject.WorldPosition = anchor + new Vector3( -140, -140, 160 );
			cameraObject.WorldRotation = Rotation.LookAt( anchor - cameraObject.WorldPosition ); camera.IsMainCamera = true;
		}
	}
	await Pause( 1000, cancellationToken );
	foreach ( var controller in scene.GetAll<PlayerController>() )
	{
		cameraControls[controller] = controller.UseCameraControls; controller.UseCameraControls = false;
	}
	cameraObject.WorldPosition = anchor + new Vector3( -140, -140, 160 );
	cameraObject.WorldRotation = Rotation.LookAt( anchor - cameraObject.WorldPosition );
	camera.IsMainCamera = true;
	await Pause( 200, cancellationToken );
	var startupClock = water.SimulationClock;
	for ( var wait = 0; wait < 150 && water.SimulationClock - startupClock < 0.3f; wait++ ) await Pause( 100, cancellationToken );
	if ( water.SimulationClock - startupClock < 0.3f ) throw new InvalidOperationException( "Scene physics did not start within 15 seconds." );
	var prop = Object( "Ordinary falling prop", anchor + Vector3.Up * 100 );
	var collider = prop.AddComponent<BoxCollider>(); collider.Scale = new Vector3( 48, 32, 32 );
	var body = prop.AddComponent<Rigidbody>(); body.Gravity = false; body.MassOverride = 80;
	prop.Enabled = true; body.Velocity = Vector3.Down * 200;
	await Pause( 1200, cancellationToken );
	Check( "ORDINARY_PROP", water.AutomaticImpactCount > 0, $"automatic={water.AutomaticImpactCount}, tracked={water.TrackedInteractionCount}, camera={scene.Camera.WorldPosition}, prop={prop.WorldPosition}" );
	prop.Destroy();
	var before = water.AutomaticImpactCount;
	var scripted = Object( "Scripted collider without Rigidbody", anchor + new Vector3( -100, 0, 0 ) );
	var scriptedCollider = scripted.AddComponent<BoxCollider>(); scriptedCollider.Scale = new Vector3( 64, 32, 40 );
	scriptedCollider.Static = true; scripted.Enabled = true;
	await Pause( 250, cancellationToken );
	for ( var i = 0; i < 20; i++ )
	{
		cancellationToken.ThrowIfCancellationRequested();
		scripted.WorldPosition += new Vector3( 6, 0, 0 );
		await Pause( 40, cancellationToken );
	}
	Check( "SCRIPTED_WAKE", water.AutomaticImpactCount > before, $"events={water.AutomaticImpactCount - before}, rigidbody={scripted.GetComponent<Rigidbody>().IsValid()}" );
	await Pause( 250, cancellationToken ); before = water.AutomaticImpactCount;
	await Pause( 500, cancellationToken );
	Check( "STATIONARY_QUIET", water.AutomaticImpactCount == before, $"newEvents={water.AutomaticImpactCount - before}" );
	scripted.Destroy();
	before = water.AutomaticImpactCount;
	var visual = Object( "Scripted renderer without collider", anchor + new Vector3( -100, 60, 0 ) );
	var visualRenderer = visual.AddComponent<ModelRenderer>(); visualRenderer.Model = Model.Load( "models/dev/box.vmdl" );
	visual.Enabled = true; await Pause( 1100, cancellationToken );
	for ( var i = 0; i < 12; i++ ) { visual.WorldPosition += new Vector3( 8, 0, 0 ); await Pause( 40, cancellationToken ); }
	Check( "RENDER_ONLY_WAKE", water.AutomaticImpactCount > before, $"events={water.AutomaticImpactCount - before}" );
	visual.Destroy();
	before = water.AutomaticImpactCount;
	var ragdoll = Object( "Native ragdoll", anchor + new Vector3( 0, 0, 100 ) );
	var ragdollRenderer = ragdoll.AddComponent<SkinnedModelRenderer>();
	ragdollRenderer.Model = Model.Load( "models/citizen_human/citizen_human_male.vmdl" ); ragdollRenderer.UseAnimGraph = false;
	var modelPhysics = ragdoll.AddComponent<ModelPhysics>(); modelPhysics.Renderer = ragdollRenderer;
	modelPhysics.Model = ragdollRenderer.Model; modelPhysics.MotionEnabled = true; ragdoll.Enabled = true;
	await Pause( 200, cancellationToken );
	foreach ( var part in modelPhysics.Bodies ) if ( part.Component.IsValid() ) part.Component.Velocity = Vector3.Down * 200;
	var parts = modelPhysics.Bodies.Count;
	await Pause( 1000, cancellationToken );
	Check( "NATIVE_RAGDOLL", parts > 1 && water.AutomaticImpactCount > before, $"limbs={parts}, events={water.AutomaticImpactCount - before}, tracked={water.TrackedInteractionCount}" );
	ragdoll.Destroy();
	before = water.AutomaticImpactCount;
	var vehicle = Object( "Large physical footprint", anchor );
	var vehicleCollider = vehicle.AddComponent<BoxCollider>(); vehicleCollider.Scale = new Vector3( 320, 96, 64 );
	var vehicleBody = vehicle.AddComponent<Rigidbody>(); vehicleBody.MassOverride = 1200; vehicleBody.Gravity = false;
	vehicle.Enabled = true; await Pause( 200, cancellationToken ); vehicleBody.Velocity = new Vector3( 180, 0, 0 );
	await Pause( 500, cancellationToken );
	Check( "LARGE_FOOTPRINT", water.AutomaticImpactCount - before >= 4, $"events={water.AutomaticImpactCount - before}" );
	vehicle.Destroy();
	Check( "PUBLIC_IMPULSE", water.EmitSurfaceImpact( anchor, 48, 120 ), "same solver as automatic contacts" );
	await Pause( 200, cancellationToken );
	var gpu = typeof(VoidBreach.Ocean.VBFFTOcean).GetField( "_gpuField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( water );
	var texture = (Texture)gpu.GetType().GetProperty( "Output" ).GetValue( gpu );
	var pixels = new Vector4[128 * 128];
	texture.GetPixels<Vector4>( (0, 0, 128, 128), 0, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F );
	var height = pixels.Max( p => MathF.Abs( p.x ) );
	Check( "GPU_DISPLACEMENT", height > 0.001f && pixels.All( p => float.IsFinite( p.x + p.y + p.z + p.w ) ), $"maxHeight={height}, dispatches={water.InteractionGpuDispatches}" );
	Check( "GPU_NORMALS", pixels.Any( p => MathF.Abs( p.y ) + MathF.Abs( p.z ) > 0.0001f ), "height derivatives are nonzero" );
	await Pause( 300, cancellationToken );
	var propagated = new Vector4[128 * 128];
	texture.GetPixels<Vector4>( (0, 0, 128, 128), 0, 0, propagated.AsSpan(), ImageFormat.RGBA32323232F );
	Check( "GPU_PROPAGATION", propagated.Any( p => MathF.Abs( p.x ) > 0.001f ) && propagated.Where( (p, i) => MathF.Abs( p.x - pixels[i].x ) > 0.001f ).Any(), "GPU heights continue evolving after the last event" );
	water.Interactions = false; await Pause( 150, cancellationToken );
	Check( "DISABLE", water.TrackedInteractionCount == 0 && water.InteractionsSleeping, "contacts and fields released" );
	water.Interactions = true; await Pause( 150, cancellationToken );
	Check( "REENABLE", water.EmitSurfaceImpact( anchor, 40, 80 ), "resources recreate and accept forcing" );
	await Pause( 150, cancellationToken );
	Check( "QUERY_INTERFACE", water.TrySample( anchor, out var sample ) && float.IsFinite( sample.Height + sample.Normal.z ), $"height={sample.Height}" );
	var upperObject = Object( "Second rotated liquid", anchor + new Vector3( 600, 0, 80 ) );
	upperObject.WorldRotation = Rotation.FromYaw( 35 );
	var upper = upperObject.AddComponent<VoidBreach.Ocean.VBFFTOcean>();
	upper.Shape = VoidBreach.Ocean.FftWaterShape.Lake; upper.Size = new Vector2( 600, 400 ); upper.WaveHeight = 0;
	upper.NativePhysics = false; upper.UnderwaterEffects = false; upper.Priority = 1001; upperObject.Enabled = true;
	await Pause( 200, cancellationToken );
	var oldLower = water.AutomaticImpactCount; var oldUpper = upper.AutomaticImpactCount;
	foreach ( var position in new[] { anchor + new Vector3( -80, 0, 50 ), upperObject.WorldPosition + Vector3.Up * 50 } )
	{
		var falling = Object( "Parallel liquid contact", position );
		var shape = falling.AddComponent<BoxCollider>(); shape.Scale = new Vector3( 32, 32, 32 );
		var physical = falling.AddComponent<Rigidbody>(); physical.Gravity = false; physical.MassOverride = 80;
		falling.Enabled = true; physical.Velocity = Vector3.Down * 120;
	}
	await Pause( 600, cancellationToken );
	Check( "MULTIPLE_LIQUIDS", water.AutomaticImpactCount > oldLower && upper.AutomaticImpactCount > oldUpper,
		$"lower={water.AutomaticImpactCount - oldLower}, upper={upper.AutomaticImpactCount - oldUpper}, yaw=35" );
	upper.Shape = VoidBreach.Ocean.FftWaterShape.River; upper.CurrentSpeed = 150;
	await Pause( 200, cancellationToken );
	Check( "RIVER_DOMAIN_CURRENT", upper.TrySample( upperObject.WorldPosition, out var river ) && river.Height < upperObject.WorldPosition.z
		&& river.Velocity.WithZ( 0 ).Length > 100, $"height={river.Height}, flow={river.Velocity}" );
	var riverSteps = upper.InteractionGpuDispatches;
	upper.EmitSurfaceImpact( upperObject.WorldPosition.WithZ( river.Height ), 48, 100 );
	await Pause( 200, cancellationToken );
	Check( "RIVER_GPU", upper.InteractionGpuDispatches > riverSteps, "shared solver runs with a sloping rotated river and current" );
}
finally
{
	foreach ( var go in created ) if ( go.IsValid() ) go.Destroy();
	foreach ( var pair in cameraControls ) if ( pair.Key.IsValid() ) pair.Key.UseCameraControls = pair.Value;
}
await Task.Delay( 1000, cancellationToken );
GC.Collect(); GC.WaitForPendingFinalizers();
await Task.Delay( 5000, cancellationToken );
Check( "POST_TEARDOWN_SURVIVES", created.All( go => !go.IsValid() ), "temporary objects and GPU resources released" );
return results;

