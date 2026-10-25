using System;
using System.Threading.Tasks;
using Sandbox;

// Copy temporarily into Code/Vehicles. Leave Play active until FINISHED.
public sealed class VBVehicleRecoveryRegression : Component, IVBVehicleControlSource
{
	private static VBVehicleRecoveryRegression _running;
	private readonly Vector3 _origin=new(0f,14000f,3000f);
	private readonly List<GameObject> _objects=new();
	private VBVehicleController _car;
	private PlayerController _player;
	private int _failures;
	public VBVehicleControls GetVehicleControls(VBVehicleController car)=>VBVehicleControls.Parked;
	[ConCmd("vb_vehicle_recovery_regression")]
	public static void Run()
	{
		if(!Game.IsPlaying || !Networking.IsHost || Game.ActiveScene is null || _running.IsValid())return;
		_running=new GameObject(true,"Vehicle recovery regression").AddComponent<VBVehicleRecoveryRegression>();
		_= _running.Verify();
	}
	private void Check(bool pass,string label,string details)
	{
		if(!pass)_failures++;
		Log.Info($"Vehicle recovery {(pass?"PASS":"FAIL")}: {label}: {details}");
	}
	private GameObject Box(string name,Vector3 position,Vector3 size)
	{
		var obj=new GameObject(true,name);_objects.Add(obj);obj.WorldPosition=position;obj.AddComponent<BoxCollider>().Scale=size;return obj;
	}
	private async Task PlacePlayer(Vector3 position)
	{
		_player.WorldPosition=position;_player.Body.Velocity=Vector3.Zero;_player.GameObject.Network.ClearInterpolation();await Task.DelaySeconds(0.15f);
	}
	private async Task Verify()
	{
		var fixture=GameObject;
		try
		{
			Box("Recovery floor",_origin+Vector3.Down*16f,new(2000f,2000f,32f));
			var carObj=GameObject.GetPrefab("vehicles/utility_vehicle.prefab").Clone(_origin+Vector3.Up*40f);_objects.Add(carObj);carObj.NetworkSpawn();
			_car=carObj.GetComponent<VBVehicleController>();_car.ControlSource=this;
			var pawn=GameObject.GetPrefab("player/player controller.prefab").Clone(_origin+new Vector3(0f,100f,2f));_objects.Add(pawn);
			_player=pawn.GetComponent<PlayerController>();_player.UseInputControls=false;_player.UseLookControls=false;_player.UseCameraControls=false;_player.EnablePressing=false;_player.WishVelocity=Vector3.Zero;
			pawn.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput=false;pawn.GetComponent<VBHealthComponent>().GodMode=false;
			var hud=pawn.Children.FirstOrDefault(x=>x.Name=="HUD");if(hud.IsValid())hud.Enabled=false;pawn.NetworkSpawn(Connection.Local);await Task.DelaySeconds(1.5f);
			var recovery=carObj.GetComponent<VBVehicleRecovery>();var driver=carObj.GetComponent<VBVehicleDriverSeat>();var gunner=carObj.GetComponent<VBVehicleTurret>().GunnerSeat;
			Check(!_car.IsOverturned && !recovery.CanRecover(_player),"upright vehicle has no flip interaction","ordinary driving state");
			_car.WorldRotation=Rotation.From(0f,35f,180f);_car.WorldPosition=_origin+Vector3.Up*90f;_car.Body.Velocity=Vector3.Zero;_car.Body.AngularVelocity=Vector3.Zero;carObj.Network.ClearInterpolation();await Task.DelaySeconds(2f);
			Check(_car.IsOverturned && !driver.CanEnter(_player) && !gunner.CanEnter(_player),"roof blocks both seats",$"up={_car.WorldRotation.Up}, z={_car.WorldPosition.z-_origin.z:F1}");
			Check(recovery.CanRecover(_player),"empty settled roof can be flipped",$"speed={_car.Body.Velocity.Length:F3}, angular={_car.Body.AngularVelocity.Length:F3}");
			var prompts=carObj.GetComponents<VBInteractionPrompt>().Select(p=>p.GetState(pawn)).Where(p=>p.IsAvailable).ToArray();
			Check(prompts.Length==1 && prompts[0].TextToken=="#vb.vehicle.recover","flip prompt replaces entry prompt",$"available={prompts.Length}");
			// Run the engine's own hover selection, without accepting human input.
			_player.EyeAngles=Rotation.LookAt(_car.Body.PhysicsBody.GetBounds().Center-_player.EyePosition).Angles();
			_player.UseButton="";_player.EnablePressing=true;_player.UpdateLookAt();var interaction=_player.Hovered;_player.EnablePressing=false;
			Check(interaction==driver && ((Component.IPressable)driver).GetTooltip(new Component.IPressable.Event(_player,null))?.Title=="#vb.vehicle.recover","native hover routes roof use to recovery","seat exposes flip rather than entry");
			await PlacePlayer(_origin+new Vector3(0f,800f,2f));recovery.RequestRecovery(_player);
			Check(_car.IsOverturned && !recovery.CanRecover(_player),"remote recovery refused","host verifies reach");await PlacePlayer(_origin+new Vector3(0f,100f,2f));
			var wall=Box("Recovery sight wall",_origin+new Vector3(0f,70f,70f),new(300f,6f,140f));await Task.DelaySeconds(0.1f);recovery.RequestRecovery(_player);
			Check(_car.IsOverturned && !recovery.CanRecover(_player),"wall blocks recovery","no use through geometry");wall.Destroy();await Task.DelaySeconds(0.1f);
			var blocker=Box("Recovery clearance obstacle",_origin+new Vector3(45f,0f,100f),new(20f,30f,18f));await Task.DelaySeconds(0.1f);recovery.RequestRecovery(_player);
			Check(_car.IsOverturned && !recovery.CanRecover(_player),"blocked upright pose refused","cab clearance is checked");blocker.Destroy();await Task.DelaySeconds(0.1f);
			var side=Vector3.Cross(_car.WorldRotation.Forward,Vector3.Up).Normal;
			var rollBlocker=Box("Recovery roll path obstacle",_origin+side*85f+Vector3.Up*65f,new(16f,16f,16f));await Task.DelaySeconds(0.1f);recovery.RequestRecovery(_player);
			Check(_car.IsOverturned && !recovery.IsRecovering && !recovery.CanRecover(_player),"blocked roll trajectory refused","clear final pose is not enough");rollBlocker.Destroy();await Task.DelaySeconds(0.1f);
			var ceiling=Box("Recovery ceiling",_origin+Vector3.Up*125f,new(400f,240f,14f));await Task.DelaySeconds(0.1f);recovery.RequestRecovery(_player);
			Check(_car.IsOverturned && !recovery.CanRecover(_player),"ceiling is not treated as a floor","no reset above an overhead blocker");ceiling.Destroy();await Task.DelaySeconds(0.1f);
			_car.Body.Velocity=Vector3.Forward*VBVehicleController.KmhToUnits(12f);
			Check(!recovery.CanRecover(_player),"moving roof cannot be flipped","settled vehicle required");_car.Body.Velocity=Vector3.Zero;
			_car.Body.AngularVelocity=new Vector3(0f,0f,3f);Check(!recovery.CanRecover(_player),"rolling roof cannot be flipped","rotation must settle");_car.Body.AngularVelocity=Vector3.Zero;
			var health=pawn.GetComponent<VBHealthComponent>();health.TakeDamage(VBDamageInfo.Create(110f,VBDamageType.Blunt));
			Check(!recovery.CanRecover(_player),"incapacitated player cannot flip","health gate");health.ResetHealth();await Task.DelaySeconds(0.2f);
			float heading=_car.WorldRotation.Forward.WithZ(0f).Normal.Dot(Vector3.Forward);var beforePlayer=_player.WorldPosition;
			// Use the native path used by E: it resolves IPressable again on the
			// selected component's GameObject. A direct Press call misses that step.
			var beforePose=_car.WorldTransform;
			_player.StartPressing(interaction as Component);bool pressed=_player.Pressed.IsValid();
			Check(recovery.IsRecovering && (_car.WorldPosition-beforePose.Position).Length<0.01f && _car.WorldRotation.Distance(beforePose.Rotation)<0.01f,"recovery begins without teleporting","pose unchanged when E is pressed");
			Check(!driver.CanEnter(_player) && !gunner.CanEnter(_player) && !recovery.CanRecover(_player),"recovery locks seats and repeated requests","one physical roll at a time");
			bool sawSide=false;float largestStep=0f;var previousRotation=_car.WorldRotation;
			for(int sample=0;sample<100;sample++) {await Task.DelaySeconds(0.05f);float up=_car.WorldRotation.Up.z;sawSide|=MathF.Abs(up)<0.7f;largestStep=MathF.Max(largestStep,previousRotation.Distance(_car.WorldRotation));previousRotation=_car.WorldRotation;}
			_player.StopPressing();
			Check(sawSide && largestStep<18f && !recovery.IsRecovering,"roll is continuous and finishes physically",$"sawSide={sawSide}, largestStep={largestStep:F2}, active={recovery.IsRecovering}");
			Check(pressed && !_car.IsOverturned && _car.WorldRotation.Up.z>0.98f && _car.GroundedWheels==4,"use restores four grounded wheels",$"wheels={_car.GroundedWheels}, up={_car.WorldRotation.Up.z:F4}, z={_car.WorldPosition.z-_origin.z:F2}, speed={_car.Body.Velocity.Length:F3}");
			Check(MathF.Abs(_car.WorldRotation.Forward.WithZ(0f).Normal.Dot(Vector3.Forward)-heading)<0.02f,"flip preserves heading","direction of travel retained");
			Check(health.CurrentHealth==100f && (_player.WorldPosition-beforePlayer).Length<2f,"recovery leaves actor safe without god mode",$"hp={health.CurrentHealth}");
			_car.ControlSource=driver;Check(driver.CanEnter(_player) && !recovery.CanRecover(_player),"entry returns after recovery","flip interaction disappears");
			_player.StartPressing(driver);await Task.DelaySeconds(0.4f);_player.StopPressing();
			Check(driver.Occupant==_player && !recovery.CanRecover(_player),"native entry works after flip","occupied car cannot be reset");
			_car.WorldRotation=Rotation.From(0f,35f,180f);_car.WorldPosition=_origin+Vector3.Up*90f;_car.Body.Velocity=Vector3.Zero;_car.Body.AngularVelocity=Vector3.Zero;carObj.Network.ClearInterpolation();await Task.DelaySeconds(1.5f);
			Check(_car.IsOverturned && !recovery.CanRecover(_player),"occupied roof cannot be flipped","driver must exit first");
			Check(!driver.CanTransferTo(_player,gunner),"roof also blocks internal seat changes","no turret entry while overturned");
			Log.Info($"Vehicle recovery FINISHED: {_failures} failure(s).");
		}
		catch(TaskCanceledException){Log.Info("Vehicle recovery interrupted by scene stop.");}
		catch(Exception ex){Log.Error($"Vehicle recovery EXCEPTION: {ex}");}
		finally{foreach(var obj in _objects.Where(x=>x.IsValid()).ToArray())obj.Destroy();if(fixture.IsValid())fixture.Destroy();}
	}
}
