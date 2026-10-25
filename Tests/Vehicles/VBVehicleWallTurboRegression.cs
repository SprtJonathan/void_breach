using System;
using System.Threading.Tasks;
using Sandbox;

// Copy temporarily into Code/Vehicles; leave Play running until FINISHED.
public sealed class VBVehicleWallTurboRegression : Component, IVBVehicleControlSource
{
	private static VBVehicleWallTurboRegression _running;
	private readonly List<GameObject> _objects = new();
	private readonly Vector3 _origin = new(12000f,12000f,3000f);
	private VBVehicleController _car;
	private VBVehicleControls _controls = VBVehicleControls.Parked;
	private int _failures;
	public VBVehicleControls GetVehicleControls(VBVehicleController car) => _controls;
	[ConCmd("vb_vehicle_wall_turbo_regression")]
	public static void Run()
	{
		if(!Networking.IsHost || Game.ActiveScene is null || _running.IsValid())return;
		_running=new GameObject(true,"Vehicle wall turbo regression").AddComponent<VBVehicleWallTurboRegression>();
		_= _running.Verify();
	}
	private void Check(bool pass,string label,string details)
	{
		if(!pass)_failures++;
		Log.Info($"Vehicle wall turbo {(pass?"PASS":"FAIL")}: {label}: {details}");
	}
	private GameObject Box(string name,Vector3 position,Vector3 size)
	{
		var obj=new GameObject(true,name);_objects.Add(obj);obj.WorldPosition=position;
		var shape=obj.AddComponent<BoxCollider>();shape.Scale=size;shape.Friction=1f;return obj;
	}
	private async Task ResetCar()
	{
		_controls=new(0f,0f,0f,false);
		_car.WorldPosition=_origin+Vector3.Up*35f;_car.WorldRotation=Rotation.Identity;
		_car.Body.Velocity=Vector3.Zero;_car.Body.AngularVelocity=Vector3.Zero;_car.GameObject.Network.ClearInterpolation();
		await Task.DelaySeconds(0.1f);_controls=VBVehicleControls.Parked;await Task.DelaySeconds(1f);
	}
	private float TyreClearance() => _car.GetComponentsInChildren<VBVehicleWheel>()
		.Min(w=>w.WorldPosition.z-_car.WorldRotation.Up.z*w.SuspensionLength-w.Radius-_origin.z);
	private async Task LowAim(VBVehicleTurret turret,PlayerController player,Transform view,float limit,string role)
	{
		turret.PitchLimits=new(-35f,limit);
		for(int sample=0;sample<30;sample++){turret.SubmitControls(player,turret.GetAimForView(view),false);await Task.DelaySeconds(0.02f);}
		float minYaw=turret.Yaw,maxYaw=turret.Yaw,minPitch=turret.Pitch,maxPitch=turret.Pitch;
		for(int sample=0;sample<60;sample++)
		{
			turret.SubmitControls(player,turret.GetAimForView(view),false);await Task.DelaySeconds(0.02f);
			minYaw=MathF.Min(minYaw,turret.Yaw);maxYaw=MathF.Max(maxYaw,turret.Yaw);
			minPitch=MathF.Min(minPitch,turret.Pitch);maxPitch=MathF.Max(maxPitch,turret.Pitch);
		}
		Check(maxYaw-minYaw<0.1f && maxPitch-minPitch<0.1f,$"{role} low aim stays stable",$"yaw range={maxYaw-minYaw:F5}, pitch range={maxPitch-minPitch:F5}");
		Check(MathF.Abs(turret.Pitch-limit)<0.15f,$"{role} depression limit",$"pitch={turret.Pitch:F2}, configured={limit}");
	}
	private async Task Verify()
	{
		var fixture=GameObject;
		try
		{
			var turboBinding=Input.GetButtonOrigin("vehicleturbo");
			Check(!string.IsNullOrWhiteSpace(turboBinding),"new turbo input is bound",$"binding={turboBinding}");
			Box("Wall turbo floor",_origin+Vector3.Down*16f,new(12000f,6000f,32f));
			var carObj=GameObject.GetPrefab("vehicles/utility_vehicle.prefab").Clone(_origin+Vector3.Up*35f);_objects.Add(carObj);carObj.NetworkSpawn();
			_car=carObj.GetComponent<VBVehicleController>();_car.ControlSource=this;await Task.DelaySeconds(1.5f);
			var side=Box("Wall beside tyres",_origin+new Vector3(0f,60f,70f),new(500f,20f,140f));
			var wheel=_car.FrontLeft;var blocked=Scene.Trace.Sphere(wheel.Radius,wheel.WorldPosition+Vector3.Up*46f,wheel.WorldPosition-Vector3.Up*16f).IgnoreGameObjectHierarchy(carObj).Run();
			Check(blocked.StartedSolid || blocked.Normal.z<0.25f,"side wall overlaps full tyre probe","reproduces the blocked sphere geometry");
			await Task.DelaySeconds(1.5f);
			Check(_car.GroundedWheels==4 && TyreClearance()>-0.5f,"side wall keeps tyres above road",$"wheels={_car.GroundedWheels}, clearance={TyreClearance():F3}");side.Destroy();
			await ResetCar();var wall=Box("Frontal collision wall",_origin+new Vector3(250f,0f,80f),new(20f,300f,160f));
			_controls=new(0f,0f,0f,false);_car.Body.Velocity=Vector3.Forward*VBVehicleController.KmhToUnits(50f);await Task.DelaySeconds(1f);
			_controls=VBVehicleControls.Parked;await Task.DelaySeconds(1.5f);
			Check(_car.WorldPosition.x<_origin.x+250f && _car.Body.Velocity.Length<10f && _car.GroundedWheels==4 && TyreClearance()>-0.5f,
				"frontal impact recovers all tyre supports",$"wheels={_car.GroundedWheels}, clearance={TyreClearance():F3}, speed={_car.SpeedKmh:F2}");
			_controls=new(1f,0f,0f,false,true);await Task.DelaySeconds(2f);
			Check(_car.WorldPosition.x<_origin.x+250f && _car.GroundedWheels==4 && TyreClearance()>-0.5f,"holding turbo against wall preserves tyre supports",$"wheels={_car.GroundedWheels}, clearance={TyreClearance():F3}");wall.Destroy();
			await ResetCar();_controls=new(1f,0f,0f,false);await Task.DelaySeconds(3f);float ordinary=_car.SpeedKmh;
			await ResetCar();_controls=new(1f,0f,0f,false,true);await Task.DelaySeconds(3f);float boosted=_car.SpeedKmh;
			Check(_car.IsTurboActive && boosted>ordinary*1.2f,"turbo increases acceleration",$"ordinary={ordinary:F1} km/h, turbo={boosted:F1} km/h");
			_controls=new(1f,0f,0f,false,false);await Task.DelaySeconds(0.05f);
			Check(!_car.IsTurboActive && _car.SpeedKmh>boosted*0.9f,"turbo release preserves momentum",$"speed={_car.SpeedKmh:F1}");
			_controls=new(-1f,0f,0f,false,true);await Task.DelaySeconds(0.1f);Check(!_car.IsTurboActive,"reverse/braking refuses turbo","forward propulsion only");
			_car.WorldPosition=_origin+Vector3.Up*600f;_car.WorldRotation=Rotation.Identity;_car.Body.Velocity=Vector3.Forward*150f;_car.Body.AngularVelocity=Vector3.Zero;_controls=new(1f,0f,0f,false,true);
			await Task.DelaySeconds(0.35f);Check(!_car.IsTurboActive && _car.GroundedWheels==0 && _car.Body.Velocity.x<=151f && _car.Body.Velocity.z<-50f,"airborne turbo has no thrust","gravity and drag remain physical");
			await ResetCar();
			var pawn=GameObject.GetPrefab("player/player controller.prefab").Clone(_origin+new Vector3(0f,120f,2f));_objects.Add(pawn);
			var player=pawn.GetComponent<PlayerController>();player.UseInputControls=false;player.UseLookControls=false;player.UseCameraControls=false;player.EnablePressing=false;player.WishVelocity=Vector3.Zero;
			pawn.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput=false;
			var hud=pawn.Children.FirstOrDefault(x=>x.Name=="HUD");if(hud.IsValid())hud.Enabled=false;
			pawn.NetworkSpawn(Connection.Local);await Task.DelaySeconds(1f);
			var seat=carObj.GetComponent<VBVehicleDriverSeat>();_car.ControlSource=seat;((Component.IPressable)seat).Press(new Component.IPressable.Event(player,null));await Task.DelaySeconds(0.4f);_car.ControlSource=this;
			var turret=carObj.GetComponent<VBVehicleTurret>();
			Check(seat.Occupant==player,"driver fixture occupies vehicle","native chair entry");
			player.EyeAngles=new(60f,0f,0f);await Task.DelaySeconds(0.05f);await LowAim(turret,player,player.EyeTransform,45f,"driver");
			seat.RequestSeatChange(player,turret.GunnerSeat);await Task.DelaySeconds(0.4f);
			await LowAim(turret,player,new Transform(turret.CameraPosition,Rotation.From(60f,0f,0f)),45f,"gunner");
			await LowAim(turret,player,new Transform(turret.CameraPosition,Rotation.From(80f,0f,0f)),65f,"adjusted gunner");turret.PitchLimits=new(-35f,45f);
			var target=_origin+new Vector3(400f,50f,40f);var desired=turret.GetAimForPoint(target,new Angles());
			for(int sample=0;sample<40;sample++){turret.SubmitControls(player,desired,false);await Task.DelaySeconds(0.02f);}
			var toTarget=target-turret.MuzzlePosition;var miss=Vector3.Cross(toTarget,turret.GunRotation.Forward).Length;
			Check(miss<1f && Vector3.Dot(toTarget,turret.GunRotation.Forward)>0f,"fixed pivot aim preserves parallax accuracy",$"miss={miss:F3} units");
			seat.SubmitPlayerControls(player,new(1f,0f,0f,false,true));await Task.DelaySeconds(0.1f);
			Check(!_car.IsTurboActive && seat.Occupant is null,"gunner cannot submit driver turbo","seat authorization enforced");
			_car.ControlSource=seat;turret.GunnerSeat.RequestSeatChange(player,seat);await Task.DelaySeconds(0.4f);
			for(int sample=0;sample<8;sample++){seat.SubmitPlayerControls(player,new(1f,0f,0f,false,true));await Task.DelaySeconds(0.02f);}
			Check(_car.IsTurboActive && _car.CurrentControls.Turbo,"driver command carries turbo intent","same control contract as AI");
			Log.Info($"Vehicle wall turbo FINISHED: {_failures} failure(s).");
		}
		catch(TaskCanceledException){Log.Info("Vehicle wall turbo interrupted by scene stop.");}
		catch(Exception ex){Log.Error($"Vehicle wall turbo EXCEPTION: {ex}");}
		finally{foreach(var obj in _objects.Where(x=>x.IsValid()).ToArray())obj.Destroy();if(fixture.IsValid())fixture.Destroy();}
	}
}
