using System;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;

// Isolated native test. It never modifies the user's pawn or editor camera.
public sealed class VBPlayerCameraRegression : Component
{
	private int _failures;
	[ConCmd( "vb_player_camera_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null ) return;
		_ = new GameObject( true, "Player camera regression" ).AddComponent<VBPlayerCameraRegression>().Verify();
	}
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Player camera {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private async Task Verify()
	{
		GameObject player = null, sceneCameraObject = null, manager = null, marker = null, spawned = null;
		Scene replicaScene = null;
		try
		{
			player = GameObject.GetPrefab( "player/player controller.prefab" ).Clone(
				new CloneConfig { StartEnabled = false, Transform = new Transform( new Vector3( 46000f, 46000f, 4000f ) ) } );
			var controller = player.GetComponent<PlayerController>( true );
			controller.UseInputControls = controller.UseLookControls = controller.UseFovFromPreferences = controller.EnablePressing = false;
			controller.Body.MotionEnabled = false;
			var inventory = player.GetComponent<VBWeaponInventoryComponent>( true );
			inventory.ManualPumping = true; inventory.GiveOnStart = false;
			player.GetComponent<VBWeaponSelectionController>( true ).Enabled = false;
			player.GetComponent<VBVehiclePlayerDriver>( true ).UsePlayerInput = false;
			var hud = player.Children.FirstOrDefault( child => child.Name == "HUD" ); if ( hud.IsValid() ) hud.Enabled = false;
			var owner = player.GetComponent<VBPlayerCamera>( true );
			Check( owner.IsValid(), "prefab contains the local camera owner component" );
			if ( !owner.IsValid() ) return;
			Check( !player.GetComponentsInChildren<CameraComponent>( true, true ).Any(), "prefab has no excluded camera reference to send over the network" );
			player.Enabled = true; player.NetworkSpawn( Connection.Local );
			await Task.DelaySeconds( 0.3f );
			Check( !controller.IsProxy, "native player belongs to this test connection" );
			if ( Application.IsDedicatedServer ) Check( !owner.Camera.IsValid(), "dedicated server creates no camera at all" );
			controller.UseCameraControls = false; await Task.DelaySeconds( 0.1f );
			Check( !owner.Camera.IsValid() || !owner.Camera.Enabled, "turning off native camera control leaves no active camera" );

			// Exercise native selection and placement without rendering on the server.
			// Automatic client activation and remote ownership require a real client.
			owner.Enabled = false;
			var camera = owner.EnsureCamera();
			Check( camera.GameObject.Parent == player && camera.GameObject.NetworkMode == NetworkMode.Never,
				"camera factory creates a peer-local child of this player" );
			Check( (camera.Flags & ComponentFlags.NotNetworked) != 0, "camera state is excluded from network snapshots" );
			Check( owner.EnsureCamera() == camera && player.GetComponentsInChildren<CameraComponent>( true, true ).Count() == 1,
				"repeated camera reconciliation reuses one local camera" );
			var wire = player.Serialize( new GameObject.SerializeOptions { SingleNetworkObject = true } );
			var serialized = wire.ToJsonString();
			Check( !serialized.Contains( "Sandbox.CameraComponent" ) && !serialized.Contains( "\"Camera\":" ),
				"native network create payload excludes both camera and camera reference" );
			replicaScene = new Scene();
			using ( replicaScene.Push() )
			{
				var replica = new GameObject( replicaScene, false, "Peer snapshot replica" );
				replica.Deserialize( wire.DeepClone().AsObject() );
				replica.Enabled = true;
				var replicaOwner = replica.GetComponent<VBPlayerCamera>( true );
				Check( replicaOwner.IsValid() && !replica.GetComponentsInChildren<CameraComponent>( true, true ).Any(),
					"snapshot deserializes the camera owner with no missing component reference" );
				Check( replicaOwner.DefaultFieldOfView == owner.DefaultFieldOfView && replicaOwner.CameraPriority == owner.CameraPriority,
					"camera settings survive the native network serialization round trip" );
			}
			sceneCameraObject = new GameObject( true, "Existing map camera" );
			var existing = sceneCameraObject.AddComponent<CameraComponent>(); existing.IsMainCamera = true; existing.Priority = 0;
			camera.Enabled = true;
			Check( Scene.Camera == camera, "player camera takes precedence over the ordinary map camera" );
			controller.UseCameraControls = true; controller.ThirdPerson = false;
			var view = new CameraView { FieldOfView = camera.FieldOfView, ZNear = camera.ZNear, ZFar = camera.ZFar };
			((ICameraModifier)controller).ModifyCamera( camera, ref view );
			Check( Vector3.DistanceBetween( camera.WorldPosition, controller.EyeTransform.Position ) < 3f,
				"native PlayerController places its own camera at the eyes" );
			var first = camera.WorldPosition;
			player.WorldPosition += Vector3.Forward * 120f;
			((ICameraModifier)controller).ModifyCamera( camera, ref view );
			Check( camera.WorldPosition.x > first.x + 115f, "camera follows movement without an external scene camera" );
			controller.ThirdPerson = true;
			((ICameraModifier)controller).ModifyCamera( camera, ref view );
			Check( Vector3.DistanceBetween( camera.WorldPosition, controller.EyeTransform.Position ) > 10f,
				"native third-person camera still works" );
			camera.Enabled = false; owner.Enabled = true; await Task.DelaySeconds( 0.1f );
			owner.Enabled = false;
			Check( !camera.Enabled, "disabling the owner never leaves an active camera behind" );

			manager = new GameObject( false, "Native spawn reference test" );
			var helper = manager.AddComponent<NetworkHelper>(); helper.StartServer = false;
			helper.PlayerPrefab = GameObject.GetPrefab( "player/player controller.prefab" );
			marker = new GameObject( true, "Native spawn marker" );
			marker.WorldPosition = player.WorldPosition + Vector3.Right * 800f;
			helper.SpawnPoints = new() { null, marker };
			manager.Enabled = true;
			((ISceneLoadingEvents)Scene.GetSystem<VBNetworkSceneSetup>()).AfterLoad( Scene );
			Check( helper.SpawnPoints.Count == 1 && helper.SpawnPoints[0] == marker,
				"generic scene setup removes null spawn references and keeps valid ones" );
			var before = Scene.GetAllComponents<PlayerController>().Select( item => item.Id ).ToHashSet();
			helper.OnActive( Connection.Local );
			var created = Scene.GetAllComponents<PlayerController>().Where( item => !before.Contains( item.Id ) ).ToArray();
			Check( created.Length == 1, "native OnActive creates one player after spawn reference cleanup" );
			if ( created.Length == 1 )
			{
				spawned = created[0].GameObject;
				Check( Vector3.DistanceBetween( spawned.WorldPosition, marker.WorldPosition ) < 2f,
					"native spawn uses the configured marker instead of the network manager position" );
				Check( spawned.GetComponent<VBPlayerCamera>().IsValid(), "native player spawn includes its local camera owner" );
			}
		}
		catch ( Exception exception ) { _failures++; Log.Error( exception ); }
		finally
		{
			if ( player.IsValid() ) player.Destroy();
			if ( sceneCameraObject.IsValid() ) sceneCameraObject.Destroy();
			if ( spawned.IsValid() ) spawned.Destroy();
			if ( manager.IsValid() ) manager.Destroy();
			if ( marker.IsValid() ) marker.Destroy();
			if ( replicaScene.IsValid() ) replicaScene.Destroy();
			Log.Info( $"Player camera FINISHED: {_failures} failure(s)." );
			GameObject.Destroy();
		}
	}
}
