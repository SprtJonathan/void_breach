using System;
using System.Threading.Tasks;
using Sandbox;

// Copy temporarily into Code/Vehicles; does not read or move the human player.
public sealed class VBVehicleCameraRegression : Component, IVBVehicleControlSource
{
	private static VBVehicleCameraRegression _running;
	private readonly List<GameObject> _objects = new();
	private readonly Vector3 _origin = new( 18000f, -8000f, 3000f );
	private VBVehicleController _car;
	private PlayerController _player;
	private VBVehiclePlayerDriver _bridge;
	private CameraComponent _camera;
	private VBVehicleControls _controls = VBVehicleControls.Parked;
	private CameraView _latestView;
	private int _failures;
	public VBVehicleControls GetVehicleControls( VBVehicleController car ) => _controls;
	[ConCmd( "vb_vehicle_camera_regression" )]
	public static void Run()
	{
		if ( !Game.IsPlaying || !Networking.IsHost || Game.ActiveScene is null || _running.IsValid() ) return;
		_running = new GameObject( true, "Vehicle camera regression" ).AddComponent<VBVehicleCameraRegression>();
		_ = _running.Verify();
	}
	private void Check( bool pass, string label, string details )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Vehicle camera {(pass ? "PASS" : "FAIL")}: {label}: {details}" );
	}
	protected override void OnUpdate()
	{
		// Camera damping uses render-frame delta. Compose every frame, rather
		// than only at the slower sampling cadence of the async test.
		if ( !_player.IsValid() || !_bridge.IsValid() || !_camera.IsValid() ) return;
		_latestView = ComposeView();
	}
	// First-person effects have no positional history: measure them against
	// the current eyes, rather than comparing a cached previous-frame position
	// with a player that has already moved during this physics step.
	private CameraView View() => _player.ThirdPerson ? _latestView : ComposeView();
	private CameraView ComposeView()
	{
		var view = new CameraView { Position = _player.EyePosition, Rotation = _player.EyeTransform.Rotation, FieldOfView = 80f };
		_bridge.ApplyVehicleView( _camera, ref view );
		return view;
	}
	private async Task DriveFor( float seconds, Vector3 velocity )
	{
		for ( int i = 0; i < seconds / 0.02f; i++ ) { _car.Body.Velocity = velocity; await Task.DelaySeconds( 0.02f ); }
	}
	private async Task Verify()
	{
		var fixture = GameObject;
		try
		{
			var floor = new GameObject( true, "Camera regression floor" ); _objects.Add( floor );
			floor.WorldPosition = _origin + Vector3.Down * 16f; floor.AddComponent<BoxCollider>().Scale = new( 6000f, 3000f, 32f );
			var car = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( _origin + Vector3.Up * 40f ); _objects.Add( car ); car.NetworkSpawn();
			_car = car.GetComponent<VBVehicleController>(); _car.ControlSource = this;
			var pawn = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( _origin + new Vector3( 0f, 100f, 2f ) ); _objects.Add( pawn );
			_player = pawn.GetComponent<PlayerController>(); _player.UseInputControls = _player.UseLookControls = _player.UseCameraControls = _player.EnablePressing = false;
			_player.WishVelocity = Vector3.Zero; _bridge = pawn.GetComponent<VBVehiclePlayerDriver>(); _bridge.UsePlayerInput = false;
			pawn.GetComponent<VBHealthComponent>().GodMode = false;
			var hud = pawn.Children.FirstOrDefault( child => child.Name == "HUD" ); if ( hud.IsValid() ) hud.Enabled = false;
			pawn.NetworkSpawn( Connection.Local );
			var cameraObject = new GameObject( true, "Vehicle cinematic test camera" ); _objects.Add( cameraObject );
			_camera = cameraObject.AddComponent<CameraComponent>(); _camera.IsMainCamera = false;
			await Task.DelaySeconds( 1.5f );
			var driver = car.GetComponent<VBVehicleDriverSeat>(); _car.ControlSource = driver;
			_player.StartPressing( driver ); await Task.DelaySeconds( 0.4f ); _player.StopPressing(); _car.ControlSource = this;
			Check( driver.Occupant == _player && !_player.ThirdPerson, "native first person entry", "camera fixture without god mode" );
			var rest = View();
			Check( (rest.Position - _player.EyePosition).Length < 0.1f && MathF.Abs( rest.FieldOfView - 80f ) < 0.05f, "first person rests at seat eyes", "no idle shake or FOV drift" );
			await DriveFor( 0.8f, Vector3.Forward * VBVehicleController.KmhToUnits( 60f ) );
			var fast = View();
			Check( fast.FieldOfView > 81f && fast.FieldOfView <= 83.1f, "speed opens first person FOV gradually", $"FOV={fast.FieldOfView:F2}" );
			_controls = new( 1f, 0f, 0f, false, true ); await DriveFor( 0.4f, Vector3.Forward * VBVehicleController.KmhToUnits( 60f ) );
			var turbo = View();
			Check( _car.IsTurboActive && turbo.FieldOfView > fast.FieldOfView + 0.6f && turbo.FieldOfView <= 85.1f, "turbo adds a bounded camera boost", $"FOV={turbo.FieldOfView:F2}" ); _controls = VBVehicleControls.Parked;
			await DriveFor( 0.12f, new Vector3( 300f, 250f, 40f ) ); var inertia = View();
			Check( (inertia.Position - _player.EyePosition).Length <= _bridge.InertiaDistance + 0.01f, "first person inertia stays inside cabin", $"offset={(inertia.Position - _player.EyePosition).Length:F3}" );
			Check( (inertia.Rotation.Forward - _player.EyeTransform.Forward).Length < 0.001f, "inertia keeps mounted gun sight direction", "roll without pitch/yaw lag" );
			_bridge.EnableCameraMotion = false; await DriveFor( 1f, Vector3.Forward * 300f ); var disabled = View();
			Check( (disabled.Position - _player.EyePosition).Length < 0.01f && MathF.Abs( disabled.FieldOfView - 80f ) < 0.02f, "camera motion can be disabled", $"FOV={disabled.FieldOfView:F3}" );
			_bridge.EnableCameraMotion = true; _car.Body.Velocity = Vector3.Zero; await Task.DelaySeconds( 1f );
			_player.ThirdPerson = true; _player.EyeAngles = new(); await Task.DelaySeconds( 0.05f ); var parked = View();
			float parkedDistance = (parked.Position - (_car.WorldPosition + Vector3.Up * 48f)).Length;
			for ( int i = 0; i < 50; i++ ) { _car.Body.Velocity = Vector3.Forward * VBVehicleController.KmhToUnits( 60f ); View(); await Task.DelaySeconds( 0.02f ); }
			var chase = View(); float fastDistance = (chase.Position - (_car.WorldPosition + Vector3.Up * 48f)).Length;
			Check( chase.FieldOfView > 83f && chase.FieldOfView < 89f, "third person speed FOV", $"FOV={chase.FieldOfView:F2}" );
			Check( fastDistance > parkedDistance + 10f && fastDistance < parkedDistance + 200f, "speed extends chase distance within bounds", $"parked={parkedDistance:F1}, fast={fastDistance:F1}" );
			// Stop translation before measuring the steady distance of the chase rig.
			_car.Body.Velocity = Vector3.Zero;
			_player.EyeAngles = new( 0f, 90f, 0f ); await Task.DelaySeconds( 0.02f ); var turned = View();
			float step = (turned.Position - chase.Position).Length;
			Check( step > 0f && step < 180f, "chase orbit moves progressively", $"short-window step={step:F2}" );
			_player.EyeAngles = new(); await Task.DelaySeconds( 1f );
			var wall = new GameObject( true, "Camera collision wall" ); _objects.Add( wall );
			wall.WorldPosition = _car.WorldPosition + new Vector3( -140f, 0f, 100f ); wall.AddComponent<BoxCollider>().Scale = new( 8f, 500f, 300f );
			for ( int i = 0; i < 40; i++ ) { View(); await Task.DelaySeconds( 0.02f ); }
			var clipped = View();
			var clearance = Scene.Trace.Sphere( 8f, clipped.Position, clipped.Position ).IgnoreGameObjectHierarchy( car ).Run();
			Check( !clearance.Hit && !clearance.StartedSolid && clipped.Position.x > wall.WorldPosition.x + 8f, "damped chase stays in front of wall", $"cameraX={clipped.Position.x - _car.WorldPosition.x:F1}" ); wall.Destroy();
			var turret = car.GetComponent<VBVehicleTurret>(); driver.RequestSeatChange( _player, turret.GunnerSeat ); await Task.DelaySeconds( 0.4f );
			_player.EyeAngles = new( 35f, 55f, 0f ); await Task.DelaySeconds( 0.02f ); var gunner = View();
			Check( (gunner.Position - turret.CameraPosition).Length < 0.001f && (gunner.Rotation.Forward - _player.EyeTransform.Forward).Length < 0.001f && gunner.FieldOfView == 80f, "gunner sight stays stable", "no chase inertia or zoom applied to dedicated gunner" );
			turret.GunnerSeat.RequestExit( _player ); await Task.DelaySeconds( 0.4f );
			var walking = new CameraView { Position = new Vector3( 4f, 5f, 6f ), Rotation = Rotation.From( 7f, 8f, 9f ), FieldOfView = 77f };
			_bridge.ApplyVehicleView( _camera, ref walking );
			Check( !_bridge.IsSeated && walking.Position == new Vector3( 4f, 5f, 6f ) && walking.FieldOfView == 77f, "exit restores untouched walking view", "no accumulated vehicle FOV" );
			Check( pawn.GetComponent<VBHealthComponent>().CurrentHealth == 100f, "camera tests preserve player health", "god mode remains disabled" );
			Log.Info( $"Vehicle camera FINISHED: {_failures} failure(s)." );
		}
		catch ( TaskCanceledException ) { Log.Info( "Vehicle camera interrupted by scene stop." ); }
		catch ( Exception exception ) { Log.Error( $"Vehicle camera EXCEPTION: {exception}" ); }
		finally { foreach ( var obj in _objects.Where( obj => obj.IsValid() ).ToArray() ) obj.Destroy(); if ( fixture.IsValid() ) fixture.Destroy(); }
	}
}
