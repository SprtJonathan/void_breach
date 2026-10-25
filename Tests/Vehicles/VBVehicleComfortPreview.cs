using System.Threading.Tasks;
using Sandbox;
using Sandbox.UI;

// Temporary native HUD preview, rendered only by its dedicated camera.
public sealed class VBVehicleComfortPreview : Component
{
	private static VBVehicleComfortPreview _running;
	private readonly List<GameObject> _objects = new();
	private PlayerController _player;
	private CameraComponent _camera;
	[ConCmd("vb_vehicle_comfort_preview")]
	public static void Run()
	{
		if(!Networking.IsHost || Game.ActiveScene is null || _running.IsValid()) return;
		_running=new GameObject(true,"Vehicle comfort preview").AddComponent<VBVehicleComfortPreview>();
		_= _running.Preview();
	}
	protected override void OnUpdate()
	{
		if(!_player.IsValid() || !_camera.IsValid()) return;
		_camera.WorldTransform=_player.EyeTransform;
		var view = new CameraView { Position=_player.EyePosition, Rotation=_player.EyeTransform.Rotation, FieldOfView=80f };
		_player.GetComponent<VBVehiclePlayerDriver>().ApplyVehicleView(_camera,ref view);
		_camera.WorldPosition=view.Position;_camera.WorldRotation=view.Rotation;_camera.FieldOfView=view.FieldOfView;
	}
	private async Task Preview()
	{
		var fixture=GameObject;
		try
		{
			var origin=new Vector3(18000f,0f,3000f);
			var floor=new GameObject(true,"Comfort preview floor");_objects.Add(floor);floor.WorldPosition=origin+Vector3.Down*16f;floor.AddComponent<BoxCollider>().Scale=new Vector3(1600f,1600f,32f);
			var carObj=GameObject.GetPrefab("vehicles/utility_vehicle.prefab").Clone(origin+Vector3.Up*34f);_objects.Add(carObj);carObj.NetworkSpawn();
			var pawn=GameObject.GetPrefab("player/player controller.prefab").Clone(origin+Vector3.Left*120f+Vector3.Up*2f);_objects.Add(pawn);
			_player=pawn.GetComponent<PlayerController>();_player.UseInputControls=false;_player.UseLookControls=false;_player.UseCameraControls=false;_player.EnablePressing=false;
			pawn.GetComponent<VBHealthComponent>().GodMode=true;pawn.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput=false;
			var ownHud=pawn.Children.FirstOrDefault(x=>x.Name=="HUD");if(ownHud.IsValid())ownHud.Enabled=false;
			pawn.NetworkSpawn(Connection.Local);await Task.DelaySeconds(1.5f);
			var driver=carObj.GetComponent<VBVehicleDriverSeat>();((Component.IPressable)driver).Press(new Component.IPressable.Event(_player,null));await Task.DelaySeconds(0.4f);
			driver.RequestSeatChange(_player,carObj.GetComponent<VBVehicleTurret>().GunnerSeat);await Task.DelaySeconds(0.4f);
			_player.EyeAngles=new Angles(-5f,0f,0f);
			var camObj=new GameObject(true,"Vehicle comfort HUD camera");_objects.Add(camObj);_camera=camObj.AddComponent<CameraComponent>();_camera.IsMainCamera=false;_camera.FieldOfView=75f;
			var ui=new GameObject(true,"Vehicle comfort HUD preview");_objects.Add(ui);ui.AddComponent<ScreenPanel>().TargetCamera=_camera;ui.AddComponent<VBVehicleHudPreview>().Player=pawn;
			Log.Info($"Vehicle comfort preview READY: camera={_camera.Id}, seated={pawn.GetComponent<VBVehiclePlayerDriver>().IsSeated}");
			await Task.DelaySeconds(30f);
		}
		catch(TaskCanceledException) { }
		catch(System.Exception ex) { Log.Error($"Vehicle comfort preview failed: {ex}"); }
		finally
		{
			foreach(var obj in _objects.Where(x=>x.IsValid()).ToArray())obj.Destroy();
			if(fixture.IsValid())fixture.Destroy();
		}
	}
}
