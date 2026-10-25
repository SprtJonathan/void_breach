using System;
using System.Threading.Tasks;
using Sandbox;

// Copy temporarily into Code/Vehicles. Run alone, leave Play running until FINISHED.
public sealed class VBVehicleFunRegression : Component, IVBVehicleControlSource
{
	private static VBVehicleFunRegression _running;
	private VBVehicleController _car;
	private VBVehicleControls _controls = VBVehicleControls.Parked;
	private readonly List<GameObject> _objects = new();
	private readonly Vector3 _origin = new( 20000f, 0f, 3000f );
	private int _failures;
	private bool _pushOnly;
	private bool _previewOnly;
	[ConCmd( "vb_vehicle_turret_preview" )]
	public static void PreviewOnly()
	{
		Run();
		if ( _running.IsValid() ) _running._previewOnly = true;
	}
	[ConCmd( "vb_vehicle_push_regression" )]
	public static void PushOnly()
	{
		Run();
		if ( _running.IsValid() ) _running._pushOnly = true;
	}
	public VBVehicleControls GetVehicleControls( VBVehicleController car ) => _controls;

	[ConCmd( "vb_vehicle_fun_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || _running.IsValid() ) return;
		var fixture = new GameObject( true, "Vehicle fun regression" );
		_running = fixture.AddComponent<VBVehicleFunRegression>();
		_ = _running.Verify();
	}

	private void Check( bool pass, string label, string details )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Vehicle fun {(pass ? "PASS" : "FAIL")}: {label}: {details}" );
	}

	private void Reset( float speed )
	{
		_controls = new( 0f, 0f, 0f, false );
		_car.WorldPosition = _origin + Vector3.Up * 28f;
		_car.WorldRotation = Rotation.Identity;
		_car.Body.Velocity = Vector3.Forward * VBVehicleController.KmhToUnits( speed );
		_car.Body.AngularVelocity = Vector3.Zero;
		_car.GameObject.Network.ClearInterpolation();
	}

	private GameObject Zombie( Vector3 position )
	{
		var victim = GameObject.GetPrefab( "creatures/zombie_classic/zombie_classic.prefab" ).Clone( position );
		victim.GetComponent<VBZombieController>().Enabled = false;
		victim.GetComponent<NavMeshAgent>().Enabled = false;
		victim.GetComponent<VBHealthComponent>().OnDamaged += damage => Log.Info( $"Vehicle fun victim damage: victim={victim.Id}, amount={damage.Amount}, source={damage.Source?.Name}, attacker={damage.Attacker?.Name}, type={damage.Type}, impulse={damage.Force.Length:F1}" );
		_objects.Add( victim );
		return victim;
	}

	private PlayerController Player( Vector3 position )
	{
		var obj = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( position );
		_objects.Add( obj );
		var player = obj.GetComponent<PlayerController>();
		player.UseCameraControls = false; player.UseLookControls = false; player.UseInputControls = false; player.EnablePressing = false;
		obj.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput = false;
		player.WishVelocity = Vector3.Zero;
		obj.GetComponent<VBHealthComponent>().GodMode = true;
		var hud = obj.Children.FirstOrDefault( x => x.Name == "HUD" );
		if ( hud.IsValid() ) hud.Enabled = false;
		obj.NetworkSpawn( Connection.Local );
		return player;
	}

	private async Task Verify()
	{
		var fixture = GameObject;
		try
		{
			var floor = new GameObject( true, "Vehicle fun floor" ); _objects.Add( floor );
			floor.WorldPosition = _origin + Vector3.Down * 16f;
			var floorCollider = floor.AddComponent<BoxCollider>(); floorCollider.Scale = new Vector3( 8000f, 4000f, 32f ); floorCollider.Friction = 1f;
			var obj = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( _origin + Vector3.Up * 40f ); _objects.Add( obj );
			_car = obj.GetComponent<VBVehicleController>(); _car.ControlSource = this; obj.NetworkSpawn();
			await Task.DelaySeconds( 2f );
			Check( _car.Body.Mass >= 4000f && _car.GroundedWheels == 4 && _car.Body.Velocity.Length < 2f,
				"heavy stable chassis", $"mass={_car.Body.Mass}, velocity={_car.Body.Velocity}, wheels={_car.GroundedWheels}" );
			if ( !_previewOnly )
			{
			var before = _car.WorldPosition;
			_car.Body.ApplyImpulseAt( _car.WorldPosition + new Vector3( -80f, 40f, 50f ), new Vector3( 500f, 0f, 0f ) );
			await Task.DelaySeconds( 0.5f );
			Check( (_car.WorldPosition - before).Length < 2f && _car.GroundedWheels == 4, "native fist impulse cannot launch", $"displacement={(_car.WorldPosition - before).Length:F2}" );
			var walker = Player( _origin + new Vector3( 0f, 130f, 2f ) ); await Task.DelaySeconds( 1f );
			var walkMode = walker.GameObject.AddComponent<VBVehicleTestWalkMode>();
			walker.UseInputControls = true; walker.JumpSpeed = 0f;
			before = _car.WorldPosition;
			var walkerBefore = walker.WorldPosition;
			walkMode.Walking = true;
			await Task.DelaySeconds( 1.5f ); walkMode.Walking = false;
			Check( (walker.WorldPosition - walkerBefore).Length > 30f && (_car.WorldPosition - before).Length < 8f && MathF.Abs( _car.WorldRotation.Angles().roll ) < 5f,
				"walking against body", $"playerMoved={(walker.WorldPosition-walkerBefore).Length:F2}, carDisplacement={(_car.WorldPosition - before).Length:F2}, roll={_car.WorldRotation.Angles().roll:F2}" );
			Check( walker.GetComponentsInChildren<ModelPhysics>().SelectMany( x => x.Bodies ).All( x => !x.Component.PhysicsBody.EnableSolidCollisions ),
				"animated bones cannot push chassis", "native locomotion capsule remains active" );
			walker.GameObject.Destroy();
			if ( _pushOnly ) { Log.Info( $"Vehicle push FINISHED: {_failures} failure(s)." ); return; }

			var slow = Zombie( _origin + new Vector3( 210f, 0f, 2f ) ); await Task.DelaySeconds( 0.3f ); Reset( 12f );
			await Task.DelaySeconds( 1.2f );
			var health = slow.GetComponent<VBHealthComponent>(); var ragdoll = slow.GetComponent<VBNpcRagdollController>();
			Check( !health.IsDead && health.CurrentHealth < 100f && ragdoll.State == VBNpcRagdollState.Recoverable,
				"slow bump injures and knocks down", $"hp={health.CurrentHealth}, state={ragdoll.State}, carSpeed={_car.Body.Velocity.Length:F1}" );
			Check( ragdoll.ModelPhysics.Bodies.All( x => x.Component.PhysicsBody.EnableSolidCollisions ), "dynamic ragdoll retains solid contacts", "world and chassis collisions" );
			_controls = VBVehicleControls.Parked;
			await Task.DelaySeconds( 7f );
			Check( !health.IsDead && ragdoll.State == VBNpcRagdollState.Animated, "slow victim gets back up", $"hp={health.CurrentHealth}, state={ragdoll.State}" );
			slow.Destroy();

			var fast = Zombie( _origin + new Vector3( 240f, 0f, 2f ) ); await Task.DelaySeconds( 0.3f ); Reset( 50f );
			await Task.DelaySeconds( 0.65f );
			health = fast.GetComponent<VBHealthComponent>(); ragdoll = fast.GetComponent<VBNpcRagdollController>();
			float retained = _car.Body.Velocity.x / VBVehicleController.KmhToUnits( 50f );
			Check( health.IsDead && ragdoll.State == VBNpcRagdollState.Dead && retained > 0.75f,
				"fast impact kills and retains momentum", $"dead={health.IsDead}, state={ragdoll.State}, retained={retained:P1}" );
			fast.Destroy(); Reset( 0f ); _controls = VBVehicleControls.Parked;
			await Task.DelaySeconds( 0.3f );

			var wall = new GameObject( true, "Vehicle fun wall" ); _objects.Add( wall ); wall.WorldPosition = _origin + new Vector3( 240f, 0f, 70f );
			wall.AddComponent<BoxCollider>().Scale = new Vector3( 24f, 400f, 140f );
			var behindWall = Zombie( _origin + new Vector3( 280f, 0f, 2f ) ); await Task.DelaySeconds( 0.3f ); Reset( 50f );
			await Task.DelaySeconds( 0.7f );
			Check( _car.WorldPosition.x < _origin.x + 150f && _car.Body.Velocity.Length < 30f && behindWall.GetComponent<VBHealthComponent>().CurrentHealth == 100f,
				"wall stops car and shields NPC", $"x={_car.WorldPosition.x-_origin.x:F1}, speed={_car.Body.Velocity.Length:F1}, hp={behindWall.GetComponent<VBHealthComponent>().CurrentHealth}" );
			wall.Destroy(); behindWall.Destroy(); Reset( 0f ); _controls = VBVehicleControls.Parked; await Task.DelaySeconds( 1f );
			}

			var driverSeat = _car.GetComponent<VBVehicleDriverSeat>(); var turret = _car.GetComponent<VBVehicleTurret>(); var gunnerSeat = turret.GunnerSeat;
			var driver = Player( _origin + new Vector3( 0f, 120f, 2f ) ); await Task.DelaySeconds( 1f ); _car.ControlSource = driverSeat;
			driver.ThirdPerson = true;
			((Component.IPressable)driverSeat).Press( new Component.IPressable.Event( driver, null ) ); await Task.DelaySeconds( 0.2f ); _car.ControlSource = this;
			Check( driverSeat.Driver == driver && turret.CanOperate( driver ), "driver has mounted gun", $"seated={driverSeat.Driver == driver}, allowed={turret.CanOperate( driver )}" );
			var target = Zombie( _origin + new Vector3( 520f, 0f, 2f ) ); await Task.DelaySeconds( 0.3f );
			var worldAim = Rotation.LookAt( target.WorldPosition + Vector3.Up * 45f - turret.MuzzlePosition ).Angles();
			for ( int i=0; i<80; i++ ) { turret.SubmitControls( driver, worldAim, true ); await Task.DelaySeconds( 0.02f ); }
			Check( target.GetComponent<VBHealthComponent>().IsDead, "mounted gun damages NPC", $"hp={target.GetComponent<VBHealthComponent>().CurrentHealth}, yaw={turret.Yaw}, pitch={turret.Pitch}" ); target.Destroy();
			var gunner = Player( _origin + new Vector3( -60f, 130f, 2f ) ); await Task.DelaySeconds( 1f );
			gunner.ThirdPerson = true;
			((Component.IPressable)gunnerSeat).Press( new Component.IPressable.Event( gunner, null ) ); await Task.DelaySeconds( 0.25f );
			Check( gunnerSeat.Occupant == gunner && gunner.GetComponent<VBVehiclePlayerDriver>().ActiveSeat == gunnerSeat && !turret.CanOperate( driver ) && turret.CanOperate( gunner ),
				"dedicated gunner has priority", $"gunner={gunnerSeat.Occupant == gunner}, driver={turret.CanOperate( driver )}, gunnerAllowed={turret.CanOperate( gunner )}" );
			turret.Access = VBVehicleTurretAccess.Driver;
			Check( turret.CanOperate( driver ) && !turret.CanOperate( gunner ), "driver-only configuration", "authorization" );
			turret.Access = VBVehicleTurretAccess.Gunner;
			Check( !turret.CanOperate( driver ) && turret.CanOperate( gunner ), "gunner-only configuration", "authorization" );
			gunner.EyeAngles = new Angles( -10f, 90f, 0f );
			for ( int i=0; i<35; i++ ) { turret.SubmitControls( gunner, new Angles( -10f, 90f, 0f ), false ); await Task.DelaySeconds( 0.02f ); }
			Check( MathF.Abs( turret.Yaw-90f ) < 1f && MathF.Abs( turret.Pitch+10f ) < 1f,
				"turret traverses and elevates", $"yaw={turret.Yaw}, pitch={turret.Pitch}" );
			var preview = new GameObject( true, "Vehicle fun turret camera" ); _objects.Add( preview );
			preview.WorldPosition = _car.WorldPosition + new Vector3( 290f, 290f, 170f );
			preview.WorldRotation = Rotation.LookAt( _car.WorldPosition + Vector3.Up * 30f - preview.WorldPosition );
			var camera = preview.AddComponent<CameraComponent>(); camera.IsMainCamera = false; camera.FieldOfView = 55f;
			Log.Info( "Vehicle fun VISUAL: rotated turret and seated gunner ready for camera_screenshot." );
			await Task.DelaySeconds( _previewOnly ? 30f : 6f );
			gunnerSeat.RequestExit( gunner ); driverSeat.RequestExit( driver ); await Task.DelaySeconds( 0.4f );
			Check( !driver.GetComponent<VBVehiclePlayerDriver>().IsSeated && !gunner.GetComponent<VBVehiclePlayerDriver>().IsSeated,
				"both seats exit safely", $"driverBody={driver.Body.Enabled}, gunnerBody={gunner.Body.Enabled}" );
			Log.Info( $"Vehicle fun FINISHED: {_failures} failure(s)." );
		}
		catch ( TaskCanceledException ) { Log.Info( "Vehicle fun interrupted by scene stop." ); }
		catch ( Exception ex ) { Log.Error( $"Vehicle fun EXCEPTION: {ex}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( x => x.IsValid() ).ToArray() ) obj.Destroy();
			if ( fixture.IsValid() ) fixture.Destroy();
		}
	}
}

// Native walking and colliders, with deterministic test input instead of the human's keyboard.
public sealed class VBVehicleTestWalkMode : Sandbox.Movement.MoveModeWalk
{
	public bool Walking { get; set; }
	public override int Score( PlayerController player ) => 1000;
	public override Vector3 UpdateMove( Rotation eyes, Vector3 input ) => Walking ? Vector3.Right * 160f : Vector3.Zero;
	public override void PrePhysicsStep()
	{
		base.PrePhysicsStep();
		Controller.GetComponent<VBVehiclePlayerDriver>()?.ResolveHeavyVehicleContacts();
	}
}
