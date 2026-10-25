using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[Property, Group( "Movement" ), Range( 0f, 20f )] public float AimYawDeadZone { get; set; } = 12f;
	[Property, Group( "Movement" ), Range( 0.2f, 1f )] public float CombatStrafeSpeedScale { get; set; } = 0.8f;
	[Property, Group( "Movement" ), Range( 0.2f, 1f )] public float CombatBackpedalSpeedScale { get; set; } = 0.6f;
	private float _previousVisualYaw;
	private bool _hasVisualYaw;

	private void FaceCombatAim()
	{
		var aim = (AimPosition - (WorldPosition + Perception.EyeOffset)).WithZ( 0f );
		if ( aim.IsNearZeroLength ) return;
		// The root faces the threat; navigation and collision remain free to travel sideways/backwards.
		float error = MathX.DeltaDegrees( WorldRotation.Yaw(), Rotation.LookAt( aim.Normal ).Yaw() );
		if ( Locomotion.Velocity.WithZ( 0f ).Length > 10f || MathF.Abs( error ) > Math.Clamp( AimYawDeadZone, 0f, 20f ) ) Face( aim );
		var travel = Agent.WishVelocity.WithZ( 0f );
		float forward = travel.IsNearZeroLength ? 1f : Vector3.Dot( aim.Normal, travel.Normal );
		float scale = forward < -0.25f ? CombatBackpedalSpeedScale : forward < 0.75f ? CombatStrafeSpeedScale : 1f;
		Agent.MaxSpeed = MathF.Max( 0f, MoveSpeed ) * Math.Clamp( scale, 0.2f, 1f );
	}

	private void UpdateTurnAnimation()
	{
		float yaw = BodyRenderer.IsValid() ? BodyRenderer.WorldRotation.Yaw() : WorldRotation.Yaw();
		float speed = _hasVisualYaw && Time.Delta > 0f
			? MathX.DeltaDegrees( _previousVisualYaw, yaw ) / Time.Delta : 0f;
		_previousVisualYaw = yaw;
		_hasVisualYaw = true;
		// This is evaluated from each peer's interpolated transform, including remote soldiers.
		_animation.MoveRotationSpeed = Math.Clamp( speed, -720f, 720f );
	}
}
