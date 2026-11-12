using System.Collections.Generic;
using Sandbox;

[Title( "Vehicle Cabin" ), Category( "Void Breach/Vehicles" )]
public sealed class VBVehicleCabin : Component
{
	[Property] public VBVehicleController Vehicle { get; set; }
	[Property] public VBVehicleDriverSeat DriverSeat { get; set; }
	[Property] public SkinnedModelRenderer CabinRenderer { get; set; }
	[Property] public VBVehicleDoor FrontDoor { get; set; }
	[Property] public VBVehicleDoor MiddleDoor { get; set; }
	[Property] public BBox BoardingArea { get; set; }
	[Property] public Vector3 SteeringAxis { get; set; }
	[Property] public float SteeringRatio { get; set; } = 9.5f;

	private Model _model;
	private readonly Dictionary<string, (int Index, Transform Bind)> _bones = new();

	internal bool CanReachSeat( PlayerController player )
	{
		return BoardingArea.Contains( WorldTransform.PointToLocal( player.WorldPosition ) );
	}

	[Rpc.Host]
	internal void RequestDriverDoor( PlayerController player, uint seatRevision, bool middle )
	{
		if ( !Networking.IsHost || !player.IsValid() || player.Network.Owner != Rpc.Caller
			|| !DriverSeat.IsValid() || DriverSeat.Driver != player ) return;

		var bridge = player.GetComponent<VBVehiclePlayerDriver>();
		var health = player.GetComponent<VBHealthComponent>();
		if ( !bridge.IsValid() || bridge.ActiveSeat != DriverSeat || bridge.SeatingRevision != seatRevision
			|| (health.IsValid() && (health.IsDead || health.IsDowned)) ) return;

		var door = middle ? MiddleDoor : FrontDoor;
		if ( door.IsValid() ) door.ToggleOnHost();
	}

	protected override void OnPreRender()
	{
		if ( !Vehicle.IsValid() || !Vehicle.IsConfigured || !CabinRenderer.IsValid() ) return;
		float steering = (Vehicle.FrontLeft.SteeringAngle + Vehicle.FrontRight.SteeringAngle) * 0.5f;
		if ( !SteeringAxis.IsNearZeroLength )
			RotateBone( "steering_wheel", SteeringAxis.Normal, steering * SteeringRatio );
		if ( FrontDoor.IsValid() ) FrontDoor.ApplyVisualPose();
		if ( MiddleDoor.IsValid() ) MiddleDoor.ApplyVisualPose();
	}

	internal void RotateBone( string name, Vector3 axis, float degrees, Vector3 offset = default )
	{
		var model = CabinRenderer.Model;
		if ( model is null || model.IsError || CabinRenderer.SceneObject is not SceneModel sceneModel ) return;
		if ( _model != model )
		{
			_model = model;
			_bones.Clear();
		}
		if ( !_bones.TryGetValue( name, out var bone ) )
		{
			var entry = model.Bones.GetBone( name );
			bone = entry is null ? (-1, default) : (entry.Index, model.GetBoneTransform( entry.Index ));
			_bones.Add( name, bone );
		}
		if ( bone.Index < 0 ) return;
		var modelAxis = CabinRenderer.WorldRotation.Inverse * (WorldRotation * axis);
		var delta = Rotation.FromAxis( modelAxis, degrees );
		var position = bone.Bind.Position;
		if ( offset != Vector3.Zero )
		{
			var rendererTransform = CabinRenderer.WorldTransform;
			position += rendererTransform.PointToLocal( WorldTransform.PointToWorld( offset ) )
				- rendererTransform.PointToLocal( WorldPosition );
		}
		sceneModel.SetBoneOverride( bone.Index, new Transform( position, delta * bone.Bind.Rotation, bone.Bind.Scale ) );
	}
}
