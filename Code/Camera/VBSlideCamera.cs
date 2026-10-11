using System;
using Sandbox;

/// <summary>Local first-person slide effects composed with native camera and ADS.</summary>
[Title( "Slide Camera" )]
[Category( "Void Breach/Camera" )]
[Icon( "videocam" )]
public sealed class VBSlideCamera : Component, ICameraModifier
{
	[Property, Group( "Slide Camera" )]
	public bool EnableEffect { get; set; } = true;

	/// <summary>Additional field of view, in degrees, at full intensity.</summary>
	[Property, Group( "Slide Camera" ), Range( 0f, 30f )]
	public float FieldOfViewBoost { get; set; } = 8f;

	[Property, Group( "Slide Camera" ), Range( -15f, 15f )]
	public float RollDegrees { get; set; } = -3f;

	[Property, Group( "Slide Camera" ), Range( -10f, 10f )]
	public float PitchDegrees { get; set; } = 0.5f;

	/// <summary>Additional camera lowering in world units, on top of native crouch.</summary>
	[Property, Group( "Slide Camera" ), Range( 0f, 10f )]
	public float CameraDrop { get; set; } = 3f;

	[Property, Group( "Transitions" ), Range( 0f, 1f ), Step( 0.01f )]
	public float BlendInTime { get; set; } = 0.08f;

	[Property, Group( "Transitions" ), Range( 0f, 1f ), Step( 0.01f )]
	public float BlendOutTime { get; set; } = 0.16f;

	/// <summary>Fade from full intensity at sprint speed to half at the slide end speed.</summary>
	[Property, Group( "Intensity" )]
	public bool ScaleWithSpeed { get; set; } = true;

	/// <summary>Effect multiplier while aiming with the held item.</summary>
	[Property, Group( "Intensity" ), Range( 0f, 1f ), Step( 0.05f )]
	public float AimEffectScale { get; set; } = 0.2f;

	[RequireComponent] private PlayerController Controller { get; set; }
	private VBMoveModeWalk _movement;
	private VBHealthComponent _health;
	private BaseInventoryComponent _inventory;
	private float _amount;

	int ICameraModifier.CameraOrder => 50;

	private bool CanApply => !Application.IsDedicatedServer && !IsProxy
		&& Controller.IsValid() && Controller.Enabled && Controller.UseCameraControls
		&& !Controller.ThirdPerson
		&& (!_health.IsValid() || (!_health.IsDowned && !_health.IsDead));

	protected override void OnUpdate()
	{
		if ( IsProxy || Application.IsDedicatedServer ) { _amount = 0f; return; }
		if ( !_movement.IsValid() ) _movement = Components.Get<VBMoveModeWalk>();
		if ( !_health.IsValid() ) _health = Components.Get<VBHealthComponent>();
		if ( !_inventory.IsValid() ) _inventory = Components.Get<BaseInventoryComponent>();
		if ( !CanApply ) { _amount = 0f; return; }

		float target = EnableEffect && _movement.IsValid() && _movement.IsSliding ? 1f : 0f;
		if ( target > 0f && ScaleWithSpeed )
		{
			float endSpeed = MathF.Max( _movement.SlideEndSpeed, 0f );
			float range = MathF.Max( Controller.RunSpeed - endSpeed, 1f );
			float speed = Math.Clamp( (Controller.Velocity.WithZ( 0f ).Length - endSpeed) / range, 0f, 1f );
			target *= 0.5f + 0.5f * speed;
		}
		float response = target > _amount ? BlendInTime : BlendOutTime;
		float blend = response <= 0f ? 1f : 1f - MathF.Exp( -MathF.Max( Time.Delta, 0f ) / response );
		_amount += (target - _amount) * blend;
		if ( target == 0f && _amount < 0.001f ) _amount = 0f;
	}

	protected override void OnDisabled() => _amount = 0f;

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( !CanApply || Scene.Camera != camera || _amount <= 0f ) return;
		float aim = _inventory.IsValid() && _inventory.ActiveItem is IVBFirstPersonHeldItem item
			? Math.Clamp( item.FirstPersonMotionAimAmount, 0f, 1f ) : 0f;
		float amount = _amount * (1f + (Math.Clamp( AimEffectScale, 0f, 1f ) - 1f) * aim);
		view.FieldOfView = Math.Clamp( view.FieldOfView + MathF.Max( FieldOfViewBoost, 0f ) * amount, 1f, 179f );
		view.Rotation *= Rotation.From( PitchDegrees * amount, 0f, RollDegrees * amount );
		view.Position += Vector3.Down * MathF.Max( CameraDrop, 0f ) * amount;
	}
}
