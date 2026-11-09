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
	[Property, Group( "Chase Camera" ), Range( 1f, 20f )] public float ChaseResponse { get; set; } = 3.5f;
	[Property, Group( "Chase Camera" ), Range( 0f, 100f )] public float ChaseSpeedDistance { get; set; } = 6f;
	[Property, Group( "Chase Camera" ), Range( 0f, 20f )] public float ChaseSpeedFov { get; set; } = 0f;
	[Property, Group( "Chase Camera" ), Range( 0f, 8f )] public float ChaseRoll { get; set; } = 0f;
	[Property, Group( "Chase Camera" ), Range( 48f, 160f )] public float ChasePivotHeight { get; set; } = 64f;
	[Property, Group( "Chase Camera" ), Range( 0f, 20f )] public float ChaseAimPitch { get; set; } = 4f;
	[Property, Group( "Chase Camera" ), Range( 1f, 20f )] public float ChaseHeightResponse { get; set; } = 4f;
	[Property, Group( "Chase Camera" ), Range( 0f, 4f )] public float ChaseRecenterDelay { get; set; } = 1.25f;
	[Property, Group( "Vehicle Camera" ), Range( 0f, 8f )] public float TurboCameraFov { get; set; } = 2f;
	private VBVehicleSeat _cameraMotionSeat;
	private Vector3 _previousCameraVelocity, _cameraAcceleration, _cameraLocalOffset, _chasePosition, _chaseTarget;
	private float _chaseYaw, _chaseEyeYaw, _chaseEyePitch, _chaseHeight, _chaseDistance, _chaseLastLook;
	private float _cameraSpeedAmount, _cameraTurboAmount, _cameraRoll;
	private bool _chaseInitialized;

	int ICameraModifier.CameraOrder => 100;
	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( !_player.IsValid() || !_player.UseCameraControls || Scene.Camera != camera ) return;
		ApplyVehicleView( camera, ref view );
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view )
	{
		// TPS input must converge from the final camera, including wall compression,
		// rather than the previous frame's camera read during OnUpdate.
		if ( IsProxy || !_player.IsValid() || !_player.UseCameraControls || Scene.Camera != camera
			|| !_player.ThirdPerson || !IsDriving || IsIncapacitated ) return;
		TransmitPlayerInput( ActiveSeat.Vehicle.GetComponent<VBVehicleTurret>(), new Transform( view.Position, view.Rotation ) );
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
		float turboFov = _player.ThirdPerson ? 0f : _cameraTurboAmount * TurboCameraFov;
		view.FieldOfView = Math.Clamp( view.FieldOfView + _cameraSpeedAmount * fov + turboFov, 1f, 170f );
		if ( !_player.ThirdPerson )
		{
			var eyes = GetVehicleFirstPersonView( _player.EyeTransform );
			view.Position = eyes.Position; view.Rotation = eyes.Rotation; _chaseInitialized = false;
			return;
		}
		float dt = Math.Clamp( Time.Delta, 0f, 0.1f );
		float heading = vehicle.WorldRotation.Yaw();
		var orbitInput = _player.EyeAngles;
		var rawTarget = vehicle.WorldPosition + Vector3.Up * ChasePivotHeight;
		bool reset = !_chaseInitialized || (rawTarget - _chaseTarget).Length > 650f;
		if ( reset )
		{
			_chaseYaw = heading + orbitInput.yaw;
			_chaseEyeYaw = orbitInput.yaw;
			_chaseEyePitch = orbitInput.pitch;
			_chaseHeight = rawTarget.z;
			_chaseLastLook = Time.Now;
		}
		float yawInput = WrapChaseAngle( orbitInput.yaw - _chaseEyeYaw );
		float pitchInput = orbitInput.pitch - _chaseEyePitch;
		_chaseEyeYaw = orbitInput.yaw;
		_chaseEyePitch = orbitInput.pitch;
		_chaseYaw = WrapChaseAngle( _chaseYaw + yawInput );
		if ( MathF.Abs( yawInput ) > 0.01f || MathF.Abs( pitchInput ) > 0.01f
			|| (UsePlayerInput && Input.Down( "attack1" )) ) _chaseLastLook = Time.Now;

		// Free look is immediate and independent of steering. Only a moving car
		// recentres the orbit, after the driver has stopped looking/using the gun.
		var velocity = vehicle.IsProxy ? vehicle.LinearVelocity : vehicle.Body.Velocity;
		if ( Time.Now - _chaseLastLook > ChaseRecenterDelay && velocity.WithZ( 0f ).Length > 35f )
			_chaseYaw = WrapChaseAngle( _chaseYaw + WrapChaseAngle( heading - _chaseYaw ) * CameraBlend( ChaseResponse, dt ) );
		var look = Rotation.From( Math.Clamp( orbitInput.pitch, -89f, 89f ), _chaseYaw, 0f );
		_chaseHeight += (rawTarget.z - _chaseHeight) * (EnableCameraMotion ? CameraBlend( ChaseHeightResponse, dt ) : 1f);
		var target = rawTarget.WithZ( _chaseHeight );
		_chaseTarget = rawTarget;
		var offset = ChaseOffset;
		if ( ActiveSeat is VBVehicleDriverSeat { ChaseDistanceOverride: > 0f } driverSeat )
			offset.x = -driverSeat.ChaseDistanceOverride;
		offset += Vector3.Backward * (_cameraSpeedAmount * ChaseSpeedDistance);
		var boom = look * offset;
		var desired = target + boom;
		var obstruction = Scene.Trace.Sphere( 8f, target, desired ).IgnoreGameObjectHierarchy( vehicle.GameObject )
			.WithoutTags( new[] { "trigger", "water" } ).Run();
		float available = obstruction.StartedSolid ? 0f : MathF.Max( 0f,
			(obstruction.EndPosition - target).Length - (obstruction.Hit ? 2f : 0f) );
		// Obstructions push the camera in immediately; release alone is damped.
		// There is no second world-position spring that can oscillate behind the car.
		_chaseDistance = reset || available < _chaseDistance ? available
			: MathX.Lerp( _chaseDistance, available, CameraBlend( 8f, dt ) );
		_chaseInitialized = true;
		_chasePosition = target + boom.Normal * _chaseDistance;
		view.Position = _chasePosition;
		// The pivot places the boom; it is not an aim target on the chassis.
		// Keep the screen centre on the player's look direction even near a wall.
		view.Rotation = Rotation.From( Math.Clamp( orbitInput.pitch + ChaseAimPitch, -89f, 89f ), _chaseYaw, 0f )
			* Rotation.From( 0f, 0f, _cameraRoll * ChaseRoll );
	}

	private static float WrapChaseAngle( float angle ) => ((angle + 180f) % 360f + 360f) % 360f - 180f;
}
