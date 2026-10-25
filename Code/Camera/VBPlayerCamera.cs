using Sandbox;

/// <summary>Owns the prefab camera locally; native PlayerController places it.</summary>
[Title( "Player Camera" ), Category( "Void Breach/Camera" ), Icon( "videocam" )]
public sealed class VBPlayerCamera : Component, Component.INetworkSpawn
{
	[RequireComponent] public PlayerController Controller { get; private set; }
	// This is peer-local presentation. Never serialize a reference to an object
	// excluded from snapshots: remote players cannot resolve that reference.
	public CameraComponent Camera { get; private set; }
	[Property] public float DefaultFieldOfView { get; set; } = 90f;
	[Property] public int CameraPriority { get; set; } = -101;
	[Property] public float NearClip { get; set; } = 5f;
	[Property] public float FarClip { get; set; } = 10000f;

	protected override void OnStart() => RefreshOwnership();
	protected override void OnUpdate() => RefreshOwnership();
	void Component.INetworkSpawn.OnNetworkSpawn( Connection owner )
	{
		// A joined player is a runtime instance, never authored level content.
		GameObject.Flags |= GameObjectFlags.NotSaved;
		RefreshOwnership();
	}

	private void RefreshOwnership()
	{
		// A clone is temporarily unowned before NetworkSpawn assigns the joining
		// client. Do not create the host's camera during that intermediate state.
		bool localOwner = !IsProxy && (!Networking.IsActive || (Network.Active && Network.Owner == Connection.Local));
		bool enabled = !Application.IsDedicatedServer && !Scene.IsEditor && Scene == Game.ActiveScene && localOwner
			&& Controller.IsValid() && Controller.Enabled && Controller.UseCameraControls;
		if ( !enabled )
		{
			if ( Camera.IsValid() ) Camera.Enabled = false;
			return;
		}
		EnsureCamera();
		if ( Camera.Enabled != enabled ) Camera.Enabled = enabled;
	}

	internal CameraComponent EnsureCamera()
	{
		if ( Camera.IsValid() ) return Camera;
		var cameraObject = new GameObject( GameObject, false, "Player Camera" );
		cameraObject.Flags |= GameObjectFlags.NotSaved;
		cameraObject.NetworkMode = NetworkMode.Never;
		cameraObject.LocalPosition = Vector3.Up * (Controller.BodyHeight - Controller.EyeDistanceFromTop);
		Camera = cameraObject.AddComponent<CameraComponent>();
		Camera.Flags |= ComponentFlags.NotNetworked;
		Camera.IsMainCamera = true;
		Camera.Priority = CameraPriority;
		Camera.FieldOfView = DefaultFieldOfView;
		Camera.ZNear = NearClip;
		Camera.ZFar = FarClip;
		Camera.EnablePostProcessing = true;
		cameraObject.Enabled = true;
		return Camera;
	}

	protected override void OnDisabled()
	{
		if ( Camera.IsValid() ) Camera.Enabled = false;
	}

	protected override void OnDestroy()
	{
		if ( Camera.IsValid() ) Camera.GameObject.Destroy();
	}
}
