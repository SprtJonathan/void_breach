using System.Threading.Tasks;
using Sandbox;

// Isolated view preview. Change the fake player's ThirdPerson property with
// the editor to switch perspective. No input or camera of the human is used.
public sealed class VBVehicleCinematicPreview : Component
{
	private static VBVehicleCinematicPreview _running;
	private readonly List<GameObject> _objects = new();
	private PlayerController _player;
	private CameraComponent _camera;
	[ConCmd( "vb_vehicle_cinematic_preview" )]
	public static void Run()
	{
		if ( !Game.IsPlaying || !Networking.IsHost || Game.ActiveScene is null || _running.IsValid() ) return;
		_running = new GameObject( true, "Vehicle cinematic preview" ).AddComponent<VBVehicleCinematicPreview>();
		_ = _running.Preview();
	}
	protected override void OnUpdate()
	{
		if ( !_player.IsValid() || !_camera.IsValid() ) return;
		foreach ( var renderer in _player.GetComponentsInChildren<SkinnedModelRenderer>() ) renderer.Enabled = _player.ThirdPerson;
		var view = new CameraView { Position = _player.EyePosition, Rotation = _player.EyeTransform.Rotation, FieldOfView = 80f };
		_player.GetComponent<VBVehiclePlayerDriver>().ApplyVehicleView( _camera, ref view );
		_camera.WorldPosition = view.Position; _camera.WorldRotation = view.Rotation; _camera.FieldOfView = view.FieldOfView;
	}
	private async Task Preview()
	{
		var fixture = GameObject;
		try
		{
			var origin = new Vector3( -14000f, -12000f, 3000f );
			var floor = new GameObject( true, "Cinematic preview floor" ); _objects.Add( floor ); floor.WorldPosition = origin + Vector3.Down * 16f;
			floor.AddComponent<BoxCollider>().Scale = new( 2000f, 2000f, 32f );
			var mesh = new GameObject( true, "Cinematic floor mesh" ); mesh.SetParent( floor, false ); mesh.LocalScale = new( 62.5f, 62.5f, 1f );
			var renderer = mesh.AddComponent<ModelRenderer>(); renderer.Model = Model.Load( "models/dev/box.vmdl" ); renderer.Tint = new Color( 0.2f, 0.22f, 0.25f );
			var car = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( origin + Vector3.Up * 40f ); _objects.Add( car ); car.NetworkSpawn();
			var pawn = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( origin + new Vector3( 0f, 100f, 2f ) ); _objects.Add( pawn );
			_player = pawn.GetComponent<PlayerController>(); _player.UseInputControls = _player.UseLookControls = _player.UseCameraControls = _player.EnablePressing = false;
			pawn.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput = false; pawn.GetComponent<VBHealthComponent>().GodMode = false;
			var hud = pawn.Children.FirstOrDefault( child => child.Name == "HUD" ); if ( hud.IsValid() ) hud.Enabled = false;
			pawn.NetworkSpawn( Connection.Local ); await Task.DelaySeconds( 1.5f );
			_player.StartPressing( car.GetComponent<VBVehicleDriverSeat>() ); await Task.DelaySeconds( 0.4f ); _player.StopPressing();
			_player.EyeAngles = new(); car.GetComponent<Rigidbody>().MotionEnabled = false;
			var cameraObject = new GameObject( true, "Vehicle cinematic preview camera" ); _objects.Add( cameraObject );
			_camera = cameraObject.AddComponent<CameraComponent>(); _camera.IsMainCamera = false;
			Log.Info( $"Vehicle cinematic preview READY: camera={_camera.Id}, player={pawn.Id}, seated={pawn.GetComponent<VBVehiclePlayerDriver>().IsSeated}" );
			await Task.DelaySeconds( 45f );
		}
		catch ( TaskCanceledException ) { }
		catch ( System.Exception exception ) { Log.Error( $"Vehicle cinematic preview EXCEPTION: {exception}" ); }
		finally { foreach ( var obj in _objects.Where( obj => obj.IsValid() ).ToArray() ) obj.Destroy(); if ( fixture.IsValid() ) fixture.Destroy(); }
	}
}
