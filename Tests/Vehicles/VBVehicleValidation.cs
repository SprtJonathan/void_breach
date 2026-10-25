using System;
using System.Threading.Tasks;
using Sandbox;

// In-engine validation harness. Copy temporarily into Code/Vehicles to run it.
public sealed class VBVehicleValidation : Component, IVBVehicleControlSource
{
	private VBVehicleControls _controls = VBVehicleControls.Parked;
	public VBVehicleControls GetVehicleControls( VBVehicleController vehicle ) => _controls;
	private static VBVehicleValidation _running;
	private int _failures;
	private PlayerController _player;
	private GameObject _floor, _victim;
	private VBVehicleController _vehicle;

	[ConCmd( "vb_humvee_preview" )]
	public static void CreatePreviewCamera()
	{
		var car = Game.ActiveScene?.GetAll<VBVehicleController>().FirstOrDefault();
		if ( !car.IsValid() ) return;
		var obj = new GameObject( true, "Humvee preview camera" );
		obj.WorldPosition = car.WorldPosition + new Vector3( 290f, 290f, 170f );
		obj.WorldRotation = Rotation.LookAt( car.WorldPosition + Vector3.Up * 20f - obj.WorldPosition );
		var camera = obj.AddComponent<CameraComponent>();
		camera.IsMainCamera = false;
		camera.FieldOfView = 55f;
	}

	[ConCmd( "vb_vehicle_model_info" )]
	public static async void InspectModel()
	{
		var model = Model.Load( "models/humvee.vmdl" );
		Log.Info( $"Humvee bounds: {model.Bounds}" );
		for ( int index = 0; index < model.BoneCount; index++ )
			Log.Info( $"Humvee bone {index}: {model.GetBoneName( index )} parent={model.GetBoneParent( index )} tx={model.GetBoneTransform( index )}" );
		var obj = new GameObject( true, "Humvee rig inspection" );
		var renderer = obj.AddComponent<SkinnedModelRenderer>();
		renderer.Model = model;
		renderer.UseAnimGraph = false;
		renderer.CreateBoneObjects = true;
		await System.Threading.Tasks.Task.Delay( 300 );
		foreach ( var name in new[] { "a7287d", "a74083", "a7587d", "3b6462" } )
		{
			renderer.TryGetBoneTransform( name, out var tx );
			var bone = renderer.GetBoneObject( name );
			Log.Info( $"Humvee live bone {name}: {tx}, object={bone?.WorldTransform}" );
		}
		obj.Destroy();
	}

	[ConCmd( "vb_vehicle_validate" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || _running.IsValid() ) return;
		var car = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( new Vector3( 0f, 0f, 1532f ) );
		_running = car.AddComponent<VBVehicleValidation>();
		_running._vehicle = car.GetComponent<VBVehicleController>();
		_running._vehicle.ControlSource = _running;
		car.NetworkSpawn();
		_running._floor = new GameObject( true, "Vehicle validation floor" );
		_running._floor.WorldPosition = new Vector3( 0f, 0f, 1484f );
		var floor = _running._floor.AddComponent<BoxCollider>();
		floor.Scale = new Vector3( 20000f, 12000f, 32f );
		floor.Friction = 1f;
		_ = _running.Verify();
	}

	private void Check( bool pass, string label, string value )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Vehicle validation {(pass ? "PASS" : "FAIL")}: {label}: {value}" );
	}

	private void Reset( Vector3 velocity = default )
	{
		_vehicle.WorldPosition = new Vector3( 0f, 0f, 1523f );
		_vehicle.WorldRotation = Rotation.Identity;
		_vehicle.Body.Velocity = velocity;
		_vehicle.Body.AngularVelocity = Vector3.Zero;
		_vehicle.Transform.ClearInterpolation();
	}

	private async Task Verify()
	{
		var fixture = GameObject;
		try
		{
			await Task.DelaySeconds( 2f );
			Check( _vehicle.GroundedWheels == 4 && _vehicle.Body.Velocity.Length < 5f, "rest/suspension", $"grounded={_vehicle.GroundedWheels}, speed={_vehicle.Body.Velocity.Length:F2}, z={_vehicle.WorldPosition.z:F2}" );
			_controls = new( 1f, 0f, 0f, false );
			await Task.DelaySeconds( 3f );
			float accelerated = _vehicle.Body.Velocity.x;
			Check( accelerated > 350f && MathF.Abs( _vehicle.WorldRotation.Angles().roll ) < 10f, "acceleration/stability", $"vx={accelerated:F1}, roll={_vehicle.WorldRotation.Angles().roll:F1}" );
			_controls = new( -1f, 0f, 0f, false );
			await Task.DelaySeconds( 0.3f );
			Check( _vehicle.Body.Velocity.x > 0 && _vehicle.CurrentControls.Throttle < 0, "opposite pedal brakes before reverse", $"vx={_vehicle.Body.Velocity.x:F1}" );
			_controls = new( 0f, 0f, 1f, false );
			await Task.DelaySeconds( 2f );
			Check( MathF.Abs( _vehicle.Body.Velocity.x ) < 12f, "braking", $"vx={_vehicle.Body.Velocity.x:F2}" );
			_controls = new( -1f, 0f, 0f, false );
			await Task.DelaySeconds( 2f );
			Check( _vehicle.Body.Velocity.x < -80f && _vehicle.Direction == -1, "reverse", $"vx={_vehicle.Body.Velocity.x:F1}" );
			_controls = new( 1f, 0.65f, 0f, false ); Reset();
			await Task.DelaySeconds( 2f );
			Check( _vehicle.WorldRotation.Angles().yaw < -5f, "right steering/Ackermann", $"yaw={_vehicle.WorldRotation.Angles().yaw:F1}, left={_vehicle.FrontLeft.SteeringAngle:F1}, right={_vehicle.FrontRight.SteeringAngle:F1}" );
			Reset( new Vector3( 100f, 0f, 0f ) ); _vehicle.WorldPosition += Vector3.Up * 600f;
			_controls = new( 1f, 1f, 0f, false );
			await Task.DelaySeconds( 0.35f );
			Check( _vehicle.GroundedWheels == 0 && _vehicle.Body.Velocity.x <= 101f && _vehicle.Body.Velocity.z < -50f, "airborne gravity/no wheel thrust", $"grounded={_vehicle.GroundedWheels}, velocity={_vehicle.Body.Velocity}" );
			Reset(); _controls = VBVehicleControls.Parked;
			await Task.DelaySeconds( 1f );
			var zombie = Scene.GetAll<VBZombieController>().FirstOrDefault();
			if ( zombie.IsValid() )
			{
				_victim = GameObject.GetPrefab( "creatures/zombie_classic/zombie_classic.prefab" ).Clone( new Vector3( 460f, 0f, 1502f ) );
				_victim.GetComponent<VBZombieController>().Enabled = false;
				_victim.GetComponent<NavMeshAgent>().Enabled = false;
				await Task.DelaySeconds( 0.3f );
				_controls = new( 0f, 0f, 0f, false );
				Reset( new Vector3( 600f, 0f, 0f ) );
				await Task.DelaySeconds( 1.25f );
				var health = _victim.GetComponent<VBHealthComponent>();
				var ragdoll = _victim.GetComponent<VBNpcRagdollController>();
				Check( health.IsDead && ragdoll.State == VBNpcRagdollState.Dead, "zombie collision/death/ragdoll", $"hp={health.CurrentHealth}, dead={health.IsDead}, ragdoll={ragdoll.State}" );
			}
			else Check( false, "zombie fixture", "no zombie found" );
			Reset(); _controls = VBVehicleControls.Parked;
			await Task.DelaySeconds( 1f );
			var playerObject = new GameObject( false, "Vehicle validation player" );
			playerObject.NetworkMode = NetworkMode.Object;
			playerObject.WorldPosition = _vehicle.WorldPosition + Vector3.Left * 120f;
			var playerBody = playerObject.AddComponent<Rigidbody>();
			playerBody.MassOverride = 80f;
			_player = playerObject.AddComponent<PlayerController>();
			_player.Body = playerBody;
			_player.UseCameraControls = false; _player.UseLookControls = false; _player.EnablePressing = false;
			playerObject.AddComponent<Sandbox.Movement.MoveModeWalk>();
			playerObject.AddComponent<Sandbox.Movement.SitMoveMode>();
			playerObject.AddComponent<VBVehiclePlayerDriver>();
			playerObject.AddComponent<VBHealthComponent>().GodMode = true;
			playerObject.Enabled = true;
			playerObject.NetworkSpawn( Connection.Local );
			await Task.DelaySeconds( 0.3f );
			if ( _player.IsValid() )
			{
				_player.GetComponent<VBHealthComponent>()?.ResetHealth();
				await Task.DelaySeconds( 0.3f );
				_player.WorldPosition = _vehicle.WorldPosition + Vector3.Left * 120f;
				_player.Body.Velocity = Vector3.Zero;
				await Task.DelaySeconds( 0.15f ); // Native EyeTransform must reflect the teleported test position.
				var seat = _vehicle.GetComponent<VBVehicleDriverSeat>();
				_vehicle.ControlSource = seat;
				Log.Info( $"Vehicle validation fixture: hp={_player.GetComponent<VBHealthComponent>()?.CurrentHealth}, input={_player.UseInputControls}, canEnter={seat.CanEnter( _player )}, speed={_vehicle.Body.Velocity.Length}" );
				var testHealth = _player.GetComponent<VBHealthComponent>();
				testHealth.TakeDamage( VBDamageInfo.Create( 10000f, VBDamageType.Blunt ) );
				Check( testHealth.CurrentHealth == 100f && !testHealth.IsDead && !testHealth.IsDowned, "god mode", $"hp={testHealth.CurrentHealth}" );
				((Component.IPressable)seat).Press( new Component.IPressable.Event( _player, null ) );
				await Task.DelaySeconds( 0.7f );
				Check( seat.Driver == _player && _player.GetComponent<VBVehiclePlayerDriver>().Seat == seat && !_player.Body.Enabled,
					"native seating/player bridge", $"driver={seat.Driver.IsValid()}, parent={_player.GameObject.Parent?.Name}, body={_player.Body.Enabled}" );
				seat.RequestExit( _player );
				await Task.DelaySeconds( 0.7f );
				Check( !_player.GetComponent<VBVehiclePlayerDriver>().IsDriving && _player.Body.Enabled && _player.UseInputControls,
					"safe exit/control restoration", $"driving={_player.GetComponent<VBVehiclePlayerDriver>().IsDriving}, body={_player.Body.Enabled}, input={_player.UseInputControls}" );
			}
			Log.Info( $"Vehicle validation FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception e ) { Log.Error( $"Vehicle validation EXCEPTION: {e}" ); }
		finally
		{
			if ( _player.IsValid() ) { _player.GameObject.SetParent( null, true ); _player.GameObject.Destroy(); }
			if ( _victim.IsValid() ) _victim.Destroy();
			if ( _floor.IsValid() ) _floor.Destroy();
			if ( fixture.IsValid() ) fixture.Destroy();
		}
	}
}

