using System;
using System.Threading.Tasks;
using Sandbox;

// Copy temporarily into Code/Vehicles. Keep Play running until FINISHED.
public sealed class VBVehicleFeedbackRegression : Component, IVBVehicleControlSource
{
	private static VBVehicleFeedbackRegression _running;
	private readonly List<GameObject> _objects = new();
	private readonly Vector3 _origin = new( 6000f, 0f, 3000f );
	private VBVehicleController _car;
	private VBVehicleControls _controls = VBVehicleControls.Parked;
	private int _failures;
	public VBVehicleControls GetVehicleControls( VBVehicleController car ) => _controls;

	[ConCmd( "vb_vehicle_feedback_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || _running.IsValid() ) return;
		var obj = new GameObject( true, "Vehicle feedback regression" );
		_running = obj.AddComponent<VBVehicleFeedbackRegression>();
		_ = _running.Verify();
	}
	private void Check( bool pass, string label, string details )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Vehicle feedback {(pass ? "PASS" : "FAIL")}: {label}: {details}" );
	}
	private PlayerController Player( Vector3 position, bool god )
	{
		var obj = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( position ); _objects.Add( obj );
		var player = obj.GetComponent<PlayerController>();
		player.UseInputControls = false; player.UseLookControls = false; player.UseCameraControls = false; player.EnablePressing = false;
		player.WishVelocity = Vector3.Zero; player.ThirdPerson = true;
		obj.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput = false;
		obj.GetComponent<VBHealthComponent>().GodMode = god;
		var hud = obj.Children.FirstOrDefault( x => x.Name == "HUD" ); if ( hud.IsValid() ) hud.Enabled = false;
		obj.NetworkSpawn( Connection.Local ); return player;
	}
	private VBVehicleController Car( Vector3 position )
	{
		var obj = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( position ); _objects.Add( obj ); obj.NetworkSpawn();
		var car = obj.GetComponent<VBVehicleController>(); car.ControlSource = this; return car;
	}
	private void ResetCar( Vector3 position, Rotation rotation )
	{
		_controls = VBVehicleControls.Parked; _car.WorldPosition = position; _car.WorldRotation = rotation;
		_car.Body.Velocity = Vector3.Zero; _car.Body.AngularVelocity = Vector3.Zero; _car.GameObject.Network.ClearInterpolation();
	}
	private async Task Verify()
	{
		var fixture = GameObject;
		try
		{
			var floor = new GameObject( true, "Feedback floor" ); _objects.Add( floor ); floor.WorldPosition = _origin + Vector3.Down * 16f;
			var slab = floor.AddComponent<BoxCollider>(); slab.Scale = new Vector3( 12000f, 6000f, 32f ); slab.Friction = 1f;
			var falling = Player( _origin + new Vector3( -500f, 0f, 260f ), false );
			await Task.DelaySeconds( 1.6f );
			var fallHealth = falling.GetComponent<VBHealthComponent>();
			Check( !fallHealth.GodMode && fallHealth.CurrentHealth < 100f, "native fall damage", $"hp={fallHealth.CurrentHealth}, god={fallHealth.GodMode}, z={falling.WorldPosition.z-_origin.z}" );
			falling.GameObject.Destroy();

			var player = Player( _origin + new Vector3( -300f, 0f, 2f ), true ); await Task.DelaySeconds( 1f );
			var weaponObj = GameObject.GetPrefab( "weapons/mp5/mp5.prefab" ).Clone( _origin + new Vector3( -270f, 0f, 10f ) ); _objects.Add( weaponObj ); weaponObj.NetworkSpawn();
			await Task.DelaySeconds( 0.3f );
			// Reproduce scene instances with serialized world components in addition
			// to those created later by Prop.
			var extraRenderer=weaponObj.AddComponent<ModelRenderer>(); extraRenderer.Model=Model.Load("models/weapons/sbox_smg_mp5/w_mp5.vmdl");
			var extraCollider=weaponObj.AddComponent<ModelCollider>(); extraCollider.Model=extraRenderer.Model;
			weaponObj.AddComponent<Rigidbody>(); await Task.DelaySeconds(0.2f);
			Check( weaponObj.GetComponents<ModelRenderer>(true).Any(x=>x.Enabled), "MP5 starts with a visible ground prop", "world representation present before pickup" );
			var weapon = weaponObj.GetComponent<VBCombatWeapon>(); var inventory = player.GetComponent<BaseInventoryComponent>();
			inventory.PickupWorldItem( weapon ); await Task.DelaySeconds(0.2f); inventory.Switch( weapon, false );
			await Task.DelaySeconds( 1f );
			Check(inventory.ActiveItem==weapon && weapon.WorldModel.IsValid(),"MP5 held model survives pickup","active model remains in the hands");
			Check( weapon.Inventory == inventory && extraRenderer.IsValid() && !extraRenderer.Enabled && weaponObj.GetComponents<ModelRenderer>( true ).All( x => !x.Enabled )
				&& weaponObj.GetComponents<Collider>( true ).All( x => !x.Enabled ) && weaponObj.GetComponents<Rigidbody>( true ).All( x => !x.Enabled ),
				"MP5 root prop hidden after pickup", $"inventory={weapon.Inventory == inventory}, rootRenderers={weaponObj.GetComponents<ModelRenderer>(true).Count()}, heldModel={weapon.WorldModel.IsValid()}, active={inventory.ActiveItem==weapon}, enabled={weaponObj.Enabled}" );
			inventory.Drop( weapon ); await Task.DelaySeconds( 0.4f );
			Check( weapon.Inventory is null && weaponObj.GetComponents<ModelRenderer>( true ).Any( x => x.Enabled ), "MP5 becomes visible when dropped", $"inWorld={weapon.Inventory is null}" );
			weaponObj.Destroy();

			_car = Car( _origin + new Vector3( 0f, 300f, 40f ) ); await Task.DelaySeconds( 1.5f );
			var area = _car.GetComponent<NavMeshArea>();
			Check( area.IsValid() && area.IsBlocker && area.SceneVolume.GetBounds().Size.x > 200f, "native navigation obstacle", $"bounds={area?.SceneVolume.GetBounds()}" );
			player.WorldPosition = _car.WorldPosition + Vector3.Left * 120f; player.WorldPosition = player.WorldPosition.WithZ( _origin.z + 2f );
			player.Body.Velocity = Vector3.Zero; await Task.DelaySeconds( 0.2f );
			var driverSeat = _car.GetComponent<VBVehicleDriverSeat>(); _car.ControlSource = driverSeat;
			((Component.IPressable)driverSeat).Press( new Component.IPressable.Event( player, null ) ); await Task.DelaySeconds( 0.25f ); _car.ControlSource = this;
			var bridge = player.GetComponent<VBVehiclePlayerDriver>();
			Check( bridge.IsDriving && bridge.IsSeated && bridge.Seat == driverSeat, "driver role", $"seated={bridge.IsSeated}, driving={bridge.IsDriving}" );
			Check(!player.ThirdPerson && bridge.IsTurretAiming,"driver enters first person with mounted gun active","automatic driver controls");
			player.GetComponent<VBHealthComponent>().GodMode=false;
			var mounted=_car.GetComponent<VBVehicleTurret>(); var mountedSeat=mounted.GunnerSeat;
			await Task.DelaySeconds(0.1f);
			driverSeat.RequestSeatChange(player,mountedSeat); await Task.DelaySeconds(0.35f);
			Check(bridge.ActiveSeat==mountedSeat && !bridge.IsDriving && !player.Body.Enabled && !driverSeat.Occupant.IsValid(),"internal driver to gunner transfer","native sitting physics stays suspended");
			var cameraObj=new GameObject(true,"Comfort sight camera");_objects.Add(cameraObj);var sightCamera=cameraObj.AddComponent<CameraComponent>();sightCamera.IsMainCamera=false;
			player.EyeAngles=new Angles(-8f,65f,0f);
			float maximumSightError=0f;
			for(int sample=0;sample<40;sample++)
			{
				var sightView = new CameraView { Position=sightCamera.WorldPosition, Rotation=sightCamera.WorldRotation, FieldOfView=80f };
				bridge.ApplyVehicleView(sightCamera,ref sightView);
				maximumSightError=MathF.Max(maximumSightError,(sightView.Rotation.Forward-player.EyeTransform.Rotation.Forward).Length);
				await Task.DelaySeconds(0.02f);
			}
			Check(maximumSightError<0.001f,"gunner sight stays independent of barrel servo",$"direction error={maximumSightError:F6}");cameraObj.Destroy();
			_car.ControlSource=driverSeat; mountedSeat.RequestSeatChange(player,driverSeat);await Task.DelaySeconds(0.35f);_car.ControlSource=this;
			Check(bridge.IsDriving && mountedSeat.Occupant is null && bridge.IsTurretAiming && !player.Body.Enabled,"return to driver","driver seat and mounted gun restored");
			Check(player.GetComponent<VBHealthComponent>().CurrentHealth==100f,"seat changes preserve health without god mode","no stale collision on internal transfer");
			var zombieObj = GameObject.GetPrefab( "creatures/zombie_classic/zombie_classic.prefab" ).Clone( _car.WorldPosition + new Vector3( 160f, 0f, -30f ) ); _objects.Add( zombieObj );
			zombieObj.GetComponent<VBZombieController>().Enabled = false; zombieObj.GetComponent<NavMeshAgent>().Enabled = false; zombieObj.WorldRotation = Rotation.FromYaw(180f);
			zombieObj.GetComponent<VBFactionComponent>().ConfigureDefaults(VBFactionComponent.XenFaction,VBFactionComponent.PlayerFaction);
			var perception = zombieObj.GetComponent<VBAiPerceptionComponent>(); perception.SightRange = 200f; perception.ThinkInterval = 0.05f;
			await Task.DelaySeconds( 0.4f );
			Check( perception.IsViableTarget( player.GameObject ) && perception.CanSee( player.GameObject ) && perception.CurrentTarget == player.GameObject,
				"NPC sees seated player identity", $"visible={perception.TargetVisible}, target={perception.CurrentTarget?.Name}" );
			var wall = new GameObject( true, "Feedback sight wall" ); _objects.Add(wall); wall.WorldPosition=_car.WorldPosition+new Vector3(125f,0f,40f); wall.AddComponent<BoxCollider>().Scale=new Vector3(8f,200f,160f);
			await Task.DelaySeconds(0.15f);
			Check( !perception.CanSee(player.GameObject), "wall still blocks seated target", "occlusion" ); wall.Destroy();

			var gunner = Player( _origin + new Vector3( -60f, 430f, 2f ), true ); await Task.DelaySeconds( 0.8f );
			var turret = _car.GetComponent<VBVehicleTurret>(); var gunnerSeat = turret.GunnerSeat;
			((Component.IPressable)gunnerSeat).Press( new Component.IPressable.Event(gunner,null)); await Task.DelaySeconds(0.25f);
			var gunnerBridge=gunner.GetComponent<VBVehiclePlayerDriver>();
			Check( gunnerBridge.IsSeated && !gunnerBridge.IsDriving && gunnerBridge.Seat is null && gunnerBridge.ActiveSeat == gunnerSeat,
				"gunner cannot drive", $"seated={gunnerBridge.IsSeated}, driving={gunnerBridge.IsDriving}" );
			driverSeat.RequestSeatChange(player,gunnerSeat);await Task.DelaySeconds(0.1f);
			Check(bridge.Seat==driverSeat && gunnerBridge.ActiveSeat==gunnerSeat,"occupied turret refuses seat transfer","both occupants preserved");
			turret.Access=VBVehicleTurretAccess.Driver; turret.SubmitControls(player,new Angles(0f,35f,0f),false); await Task.DelaySeconds(0.2f);
			Check( turret.CanOperate(player) && !turret.CanOperate(gunner) && turret.Operator==player, "driver-operated gun authorization", $"yaw={turret.Yaw}, operator={turret.Operator==player}" );
			gunnerSeat.RequestExit(gunner); driverSeat.RequestExit(player); await Task.DelaySeconds(0.3f); gunner.GameObject.Destroy(); player.GameObject.Destroy();

			var agent=zombieObj.GetComponent<NavMeshAgent>(true); agent.Enabled=true; agent.MaxSpeed=0f; agent.UpdatePosition=true;
			await Task.DelaySeconds(0.2f);
			zombieObj.WorldPosition=_car.WorldPosition+new Vector3(40f,0f,-30f); agent.SetAgentPosition(zombieObj.WorldPosition);
			await Task.DelaySeconds(0.15f);
			Check( zombieObj.WorldPosition.x > _car.WorldPosition.x+110f, "NPC sweep prevents crossing chassis", $"relativeX={zombieObj.WorldPosition.x-_car.WorldPosition.x:F1}" ); zombieObj.Destroy();

			ResetCar(_origin+Vector3.Up*5f,Rotation.Identity); await Task.DelaySeconds(1.5f);
			Check(_car.GroundedWheels==4 && _car.Body.Velocity.Length<5f, "buried hardpoints recover grip", $"wheels={_car.GroundedWheels}, z={_car.WorldPosition.z-_origin.z:F2}, speed={_car.Body.Velocity.Length:F2}" );

			// Same dimensions as the user's 30-degree Rampe, low tip under the floor.
			var ramp=new GameObject(true,"Feedback Rampe replica");_objects.Add(ramp);ramp.WorldRotation=Rotation.FromPitch(-30f);
			ramp.WorldPosition=_origin+new Vector3(500f,0f,174.64f); ramp.AddComponent<BoxCollider>().Scale=new Vector3(761.3847f,130f,8.5492f);
			// Join the crest to a flat deck. Falling nine metres from the unconnected
			// high end would test a rollover crash rather than a tyre transition.
			var deck=new GameObject(true,"Feedback crest deck");_objects.Add(deck);
			deck.WorldPosition=_origin+new Vector3(1730f,0f,352.7f);deck.AddComponent<BoxCollider>().Scale=new Vector3(1800f,1000f,32f);
			ResetCar(_origin+Vector3.Up*34f,Rotation.Identity); _controls=new(1f,0f,0f,false);
			float peakHeight=0f; bool lowGear=false; float shortestSuspension=16f;
			for(int i=0;i<180 && _car.WorldPosition.x<_origin.x+1400f;i++) {await Task.DelaySeconds(0.1f);peakHeight=MathF.Max(peakHeight,_car.WorldPosition.z-_origin.z);lowGear|=_car.IsLowGear;shortestSuspension=MathF.Min(shortestSuspension,_car.GetComponentsInChildren<VBVehicleWheel>().Min(x=>x.SuspensionLength));}
			Check(shortestSuspension>-3f,"ramp transition limits wheel intrusion",$"shortest suspension={shortestSuspension:F2}");
			Check(peakHeight>220f && _car.WorldPosition.x>_origin.x+750f && lowGear,"30-degree ramp ascent",$"peakZ={peakHeight:F1}, x={_car.WorldPosition.x-_origin.x:F1}, lowGear={lowGear}" );
			_controls=VBVehicleControls.Parked; await Task.DelaySeconds(2f);
			Check(_car.GroundedWheels==4 && MathF.Abs(_car.WorldPosition.z-_origin.z-401.35f)<8f && MathF.Abs(_car.WorldRotation.Angles().roll)<10f,"ground contact after ramp crest",$"wheels={_car.GroundedWheels}, z={_car.WorldPosition.z-_origin.z:F1}, roll={_car.WorldRotation.Angles().roll:F1}" );
			deck.Destroy();
			ramp.WorldRotation=Rotation.FromPitch(30f);
			ResetCar(ramp.WorldPosition+ramp.WorldRotation.Forward*(-250f)+ramp.WorldRotation.Up*38f,ramp.WorldRotation);
			_controls=new(0.35f,0f,0f,false);
			for(int i=0;i<120 && _car.WorldPosition.x<_origin.x+1350f;i++) await Task.DelaySeconds(0.1f);
			_controls=VBVehicleControls.Parked; await Task.DelaySeconds(2f);
			Check(_car.GroundedWheels==4 && _car.WorldPosition.x>_origin.x+1000f && _car.WorldPosition.z<_origin.z+40f,"30-degree ramp descent restores rear contacts",$"wheels={_car.GroundedWheels}, x={_car.WorldPosition.x-_origin.x:F1}, z={_car.WorldPosition.z-_origin.z:F1}");
			ramp.Destroy();
			var slope=new GameObject(true,"Feedback 35-degree slope");_objects.Add(slope);slope.WorldPosition=_origin+new Vector3(0f,1000f,200f);slope.WorldRotation=Rotation.FromPitch(-35f);
			slope.AddComponent<BoxCollider>().Scale=new Vector3(3000f,1500f,32f);
			ResetCar(slope.WorldPosition+slope.WorldRotation.Up*50f,slope.WorldRotation);_controls=new(1f,0f,0f,false);
			var start=_car.WorldPosition; await Task.DelaySeconds(4f);
			Check(_car.IsLowGear && Vector3.Dot(_car.WorldPosition-start,slope.WorldRotation.Forward)>120f && _car.GroundedWheels>=3,
				"35-degree hill climbs under power",$"distance={Vector3.Dot(_car.WorldPosition-start,slope.WorldRotation.Forward):F1}, speed={_car.SpeedKmh:F1}, wheels={_car.GroundedWheels}" );
			Log.Info($"Vehicle feedback FINISHED: {_failures} failure(s).");
		}
		catch(TaskCanceledException){Log.Info("Vehicle feedback interrupted by scene stop.");}
		catch(Exception ex){Log.Error($"Vehicle feedback EXCEPTION: {ex}");}
		finally {foreach(var obj in _objects.Where(x=>x.IsValid()).ToArray())obj.Destroy();if(fixture.IsValid())fixture.Destroy();}
	}
}
