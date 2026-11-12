using System;
using Sandbox;

[Title( "Vehicle Door" ), Category( "Void Breach/Vehicles" )]
public sealed class VBVehicleDoor : Component, Component.IPressable, IVBInteractionPromptProvider
{
	[Property] public VBVehicleCabin Cabin { get; set; }
	[Property] public BoxCollider FirstLeaf { get; set; }
	[Property] public string FirstBone { get; set; }
	[Property] public BoxCollider SecondLeaf { get; set; }
	[Property] public string SecondBone { get; set; }
	[Property, Range( 0.2f, 3f )] public float TravelTime { get; set; } = 0.95f;

	[Sync( SyncFlags.FromHost )] public bool WantsOpen { get; private set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float OpenFraction { get; private set; }

	private const float Reach = 82f;
	private const float JambClearance = 1.77f;
	private const float FrontSweepClearance = 2.36f;
	private static readonly string[] IgnoredTags = { "trigger", "water" };
	private Vector3 _firstRestPosition;
	private Vector3 _secondRestPosition;
	private VBVehicleController Vehicle => Cabin.IsValid() ? Cabin.Vehicle : null;
	private bool IsOperable => Active && FirstLeaf.IsValid() && Vehicle.IsValid()
		&& Vehicle.IsConfigured && !Vehicle.IsOverturned && !Vehicle.IsRecovering;
	private string Prompt => WantsOpen ? "#vb.interaction.close" : "#vb.interaction.open";

	public bool CanPress( Component.IPressable.Event e ) => e.Source is PlayerController player && CanUseOnFoot( player );

	public bool Press( Component.IPressable.Event e )
	{
		if ( e.Source is not PlayerController player || !CanUseOnFoot( player ) ) return false;
		RequestOnFoot( player );
		return true;
	}

	public Component.IPressable.Tooltip? GetTooltip( Component.IPressable.Event e ) => CanPress( e )
		? new Component.IPressable.Tooltip( Prompt, "door_front", "" ) : null;

	public VBInteractionPromptState GetInteractionPromptState( GameObject viewer ) => new(
		viewer.IsValid() && CanUseOnFoot( viewer.GetComponent<PlayerController>() ), Prompt, "use" );

	private bool CanUseOnFoot( PlayerController player )
	{
		if ( !IsOperable || !player.IsValid() || !player.Active || (player.EyePosition - WorldPosition).Length > Reach )
			return false;
		if ( Cabin.WorldTransform.PointToLocal( player.WorldPosition ).y > -8f ) return false;
		var health = player.GetComponent<VBHealthComponent>();
		if ( health.IsValid() && (health.IsDead || health.IsDowned) ) return false;
		if ( player.GetComponent<VBVehiclePlayerDriver>()?.IsSeated == true ) return false;
		var velocity = Vehicle.IsProxy ? Vehicle.LinearVelocity : Vehicle.Body.Velocity;
		return velocity.Length <= VBVehicleController.KmhToUnits( 5f );
	}

	[Rpc.Host]
	private void RequestOnFoot( PlayerController player )
	{
		if ( !Networking.IsHost || !CanUseOnFoot( player ) || player.Network.Owner != Rpc.Caller ) return;
		var sight = Scene.Trace.Ray( player.EyePosition, WorldPosition )
			.IgnoreGameObjectHierarchy( player.GameObject ).IgnoreGameObjectHierarchy( Cabin.GameObject )
			.WithoutTags( IgnoredTags ).Run();
		if ( !sight.Hit && !sight.StartedSolid ) ToggleOnHost();
	}

	internal void ToggleOnHost()
	{
		if ( Networking.IsHost && IsOperable ) WantsOpen = !WantsOpen;
	}

	protected override void OnStart()
	{
		if ( FirstLeaf.IsValid() ) _firstRestPosition = FirstLeaf.LocalPosition;
		if ( SecondLeaf.IsValid() ) _secondRestPosition = SecondLeaf.LocalPosition;
	}

	protected override void OnFixedUpdate()
	{
		if ( Networking.IsHost && IsOperable ) AdvanceDoors();
		ApplyColliderPose( FirstLeaf, _firstRestPosition, OpenFraction, 1f );
		ApplyColliderPose( SecondLeaf, _secondRestPosition, OpenFraction, -1f );
	}

	private void AdvanceDoors()
	{
		float goal = WantsOpen ? 1f : 0f;
		float step = Scene.FixedDelta / MathF.Max( TravelTime, 0.2f );
		float next = OpenFraction + Math.Clamp( goal - OpenFraction, -step, step );
		if ( next == OpenFraction ) return;
		if ( IsBlocked( FirstLeaf, _firstRestPosition, next, 1f ) || IsBlocked( SecondLeaf, _secondRestPosition, next, -1f ) )
		{
			if ( !WantsOpen ) WantsOpen = true;
			return;
		}
		OpenFraction = next;
	}

	private (float Angle, Rotation Rotation, Vector3 Offset) GetLeafPose( BoxCollider leaf, float fraction, float direction )
	{
		float angle = direction * 90f * fraction;
		var rotation = Rotation.FromYaw( angle );
		// The edge opposite the jamb travels along the doorway while the jamb end moves inboard.
		var edge = leaf.Center.WithX( leaf.Center.x - direction * leaf.Scale.x * 0.5f ).WithZ( 0f );
		float clearance = SecondLeaf.IsValid()
			? JambClearance * fraction * fraction * fraction
			: JambClearance * fraction + FrontSweepClearance * MathF.Sin( MathF.PI * fraction );
		var target = edge.WithX( edge.x * MathF.Cos( fraction * MathF.PI * 0.5f ) - direction * clearance );
		return (angle, rotation, target - rotation * edge);
	}

	private bool IsBlocked( BoxCollider leaf, Vector3 rest, float fraction, float direction )
	{
		if ( !leaf.IsValid() ) return false;
		var pivot = leaf.GameObject;
		var pose = GetLeafPose( leaf, fraction, direction );
		var rotation = pivot.Parent.WorldRotation * pose.Rotation;
		var centre = pivot.Parent.WorldTransform.PointToWorld( rest + pose.Offset ) + rotation * (leaf.Center * pivot.WorldScale);
		var extents = leaf.Scale * pivot.WorldScale * 0.5f;
		var hit = Scene.Trace.Box( new BBox( -extents, extents ), centre, centre ).Rotated( rotation )
			.IgnoreGameObjectHierarchy( Cabin.GameObject ).WithoutTags( IgnoredTags ).Run();
		return hit.Hit || hit.StartedSolid;
	}

	private void ApplyColliderPose( BoxCollider leaf, Vector3 rest, float fraction, float direction )
	{
		if ( !leaf.IsValid() ) return;
		var pivot = leaf.GameObject;
		var pose = GetLeafPose( leaf, fraction, direction );
		var position = rest + pose.Offset;
		if ( pivot.LocalPosition == position && pivot.LocalRotation == pose.Rotation ) return;
		using ( pivot.Transform.DisableProxy() )
			pivot.LocalTransform = pivot.LocalTransform.WithPosition( position ).WithRotation( pose.Rotation );
	}

	internal void ApplyVisualPose()
	{
		ApplyVisualPose( FirstLeaf, FirstBone, 1f );
		ApplyVisualPose( SecondLeaf, SecondBone, -1f );
	}

	private void ApplyVisualPose( BoxCollider leaf, string bone, float direction )
	{
		if ( !leaf.IsValid() || string.IsNullOrEmpty( bone ) ) return;
		var pose = GetLeafPose( leaf, OpenFraction, direction );
		Cabin.RotateBone( bone, Vector3.Up, pose.Angle, pose.Offset );
	}
}
