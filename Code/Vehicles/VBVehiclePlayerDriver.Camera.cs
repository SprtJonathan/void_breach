using System;
using Sandbox;

/// <summary>Local cinematic view, composed after the native player camera.</summary>
public sealed partial class VBVehiclePlayerDriver : ICameraModifier
{
	[Property, Group( "Vehicle Camera" )] public bool EnableCameraMotion { get; set; } = true;
	[Property, Group( "Vehicle Camera" ), Range( 1f, 20f )] public float CameraMotionResponse { get; set; } = 8f;
	[Property, Group( "First Person Camera" ), Range( 0f, 3f )] public float InertiaDistance { get; set; } = 1f;
	[Property, Group( "First Person Camera" ), Range( 0f, 5f )] public float InertiaRoll { get; set; } = 1.5f;
	[Property, Group( "First Person Camera" ), Range( 0f, 8f )] public float FirstPersonSpeedFov { get; set; } = 3f;
	[Property, Group( "Chase Camera" ), Range( 1f, 20f )] public float ChaseResponse { get; set; } = 8f;
	[Property, Group( "Chase Camera" ), Range( 0f, 100f )] public float ChaseSpeedDistance { get; set; } = 36f;
	[Property, Group( "Chase Camera" ), Range( 0f, 20f )] public float ChaseSpeedFov { get; set; } = 8f;
	[Property, Group( "Chase Camera" ), Range( 0f, 8f )] public float ChaseRoll { get; set; } = 2f;
	[Property, Group( "Vehicle Camera" ), Range( 0f, 8f )] public float TurboCameraFov { get; set; } = 2f;
	private VBVehicleSeat _cameraMotionSeat;
	private Vector3 _previousCameraVelocity, _cameraAcceleration, _cameraLocalOffset, _chasePosition;
	private Rotation _chaseHeading;
	private float _cameraSpeedAmount, _cameraTurboAmount, _cameraRoll;
	private bool _chaseInitialized;

	int ICameraModifier.CameraOrder => 100;
	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( !_player.IsValid() || !_player.UseCameraControls || Scene.Camera != camera ) return;
		ApplyVehicleView( camera, ref view );
	}

	private static float CameraBlend( float response, float dt ) => 1f - MathF.Exp( -MathF.Max( response, 0.1f ) * dt );
	private void UpdateVehicleCameraMotion()
	{
		var seat = ActiveSeat;
		if ( !seat.IsValid() || !seat.Vehicle.IsValid() || IsIncapacitated )
		{
			_cameraMotionSeat = null; _chaseInitialized = false;
			_cameraLocalOffset = _cameraAcceleration = Vector3.Zero;
			_cameraSpeedAmount = _cameraTurboAmount = _cameraRoll = 0f;
			return;
		}
		var vehicle = seat.Vehicle;
		var velocity = vehicle.IsProxy ? vehicle.LinearVelocity : vehicle.Body.Velocity;
		if ( seat != _cameraMotionSeat )
		{
			_cameraMotionSeat = seat; _previousCameraVelocity = velocity;
			_cameraAcceleration = _cameraLocalOffset = Vector3.Zero;
			_cameraRoll = _cameraSpeedAmount = _cameraTurboAmount = 0f; _chaseInitialized = false;
		}
		float dt = Math.Clamp( Time.Delta, 0.001f, 0.1f );
		float blend = CameraBlend( CameraMotionResponse, dt );
		var acceleration = ((velocity - _previousCameraVelocity) / dt).ClampLength( 650f );
		_previousCameraVelocity = velocity;
		_cameraAcceleration = Vector3.Lerp( _cameraAcceleration, acceleration, blend );
		var local = vehicle.WorldRotation.Inverse * _cameraAcceleration;
		var offset = new Vector3( -local.x * 0.004f, -local.y * 0.004f, -local.z * 0.002f ).ClampLength( MathF.Max( InertiaDistance, 0f ) );
		_cameraLocalOffset = Vector3.Lerp( _cameraLocalOffset, EnableCameraMotion ? offset : Vector3.Zero, blend );
		float lateral = Math.Clamp( -local.y / 400f, -1f, 1f );
		_cameraRoll += ((EnableCameraMotion ? lateral : 0f) - _cameraRoll) * blend;
		float speed = EnableCameraMotion ? Math.Clamp( velocity.Length / VBVehicleController.KmhToUnits( vehicle.TopSpeedKmh ), 0f, 1f ) : 0f;
		_cameraSpeedAmount += (speed - _cameraSpeedAmount) * blend;
		_cameraTurboAmount += ((EnableCameraMotion && vehicle.IsTurboActive ? 1f : 0f) - _cameraTurboAmount) * blend;
	}

	private Transform GetVehicleFirstPersonView( Transform eyes )
	{
		if ( !IsDriving || !EnableCameraMotion ) return eyes;
		var desired = eyes.Position + ActiveSeat.Vehicle.WorldRotation * _cameraLocalOffset;
		var space = Scene.Trace.Sphere( 1.5f, eyes.Position, desired ).IgnoreGameObjectHierarchy( ActiveSeat.Vehicle.GameObject )
			.WithoutTags( new[] { "trigger", "water" } ).Run();
		eyes.Position = space.StartedSolid ? eyes.Position : space.EndPosition;
		// Roll adds weight without changing the sight's forward direction. The
		// same displaced view feeds turret parallax, so the reticle remains useful.
		eyes.Rotation *= Rotation.From( 0f, 0f, _cameraRoll * InertiaRoll );
		return eyes;
	}

	internal void ApplyVehicleView( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || !IsSeated || !_player.IsValid() || !camera.IsValid() || IsIncapacitated ) return;
		var vehicle = ActiveSeat.Vehicle;
		if ( !vehicle.IsValid() ) return;
		if ( ActiveSeat is VBVehicleGunnerSeat )
		{
			var turret = vehicle.GetComponent<VBVehicleTurret>();
			if ( turret.IsValid() ) { view.Position = turret.CameraPosition; view.Rotation = _player.EyeTransform.Rotation; }
			_chaseInitialized = false;
			return;
		}
		float fov = _player.ThirdPerson ? ChaseSpeedFov : FirstPersonSpeedFov;
		view.FieldOfView = Math.Clamp( view.FieldOfView + _cameraSpeedAmount * fov + _cameraTurboAmount * TurboCameraFov, 1f, 170f );
		if ( !_player.ThirdPerson )
		{
			var eyes = GetVehicleFirstPersonView( _player.EyeTransform );
			view.Position = eyes.Position; view.Rotation = eyes.Rotation; _chaseInitialized = false;
			return;
		}
		float dt = Math.Clamp( Time.Delta, 0f, 0.1f );
		float blend = EnableCameraMotion ? CameraBlend( ChaseResponse, dt ) : 1f;
		var heading = Rotation.FromYaw( vehicle.WorldRotation.Yaw() );
		if ( !_chaseInitialized ) _chaseHeading = heading;
		_chaseHeading = Rotation.Slerp( _chaseHeading, heading, blend );
		var look = _chaseHeading * Rotation.From( _player.EyeAngles );
		var target = vehicle.WorldPosition + Vector3.Up * 48f;
		var offset = ChaseOffset + Vector3.Backward * (_cameraSpeedAmount * ChaseSpeedDistance);
		var desired = target + look * offset;
		if ( !_chaseInitialized || (_chasePosition - desired).Length > 650f ) _chasePosition = desired;
		_chaseInitialized = true;
		var candidate = Vector3.Lerp( _chasePosition, desired, blend );
		// Recheck after damping: smoothing an already-clipped camera can otherwise
		// move it back through a wall when the vehicle or the view rotates.
		var trace = Scene.Trace.Sphere( 8f, target, candidate ).IgnoreGameObjectHierarchy( vehicle.GameObject )
			.WithoutTags( new[] { "trigger", "water" } ).Run();
		_chasePosition = trace.StartedSolid ? target : trace.EndPosition + (trace.Hit ? trace.Normal * 2f : Vector3.Zero);
		view.Position = _chasePosition;
		view.Rotation = Rotation.LookAt( target - view.Position, Vector3.Up ) * Rotation.From( 0f, 0f, _cameraRoll * ChaseRoll );
	}
}
