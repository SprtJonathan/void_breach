using System;
using Sandbox;

public sealed partial class VBMoveModeWalk
{
	[Property, Group( "Momentum" )] public bool EnableMomentumMovement { get; set; } = true;
	/// <summary>Ground acceleration as a multiple of the requested speed per second.</summary>
	[Property, Group( "Momentum" ), Range( 0f, 30f )] public float GroundAcceleration { get; set; } = 10f;
	[Property, Group( "Momentum" ), Range( 0f, 20f )] public float GroundDrag { get; set; } = 5.5f;
	/// <summary>Minimum speed used by ground friction to settle to a full stop.</summary>
	[Property, Group( "Momentum" ), Range( 0f, 200f )] public float GroundStopSpeed { get; set; } = 60f;
	[Property, Group( "Air Control" ), Range( 0f, 30f )] public float AirAcceleration { get; set; } = 10f;
	/// <summary>Cap along the input direction, not a cap on carried jump momentum.</summary>
	[Property, Group( "Air Control" ), Range( 0f, 150f )] public float AirWishSpeed { get; set; } = 30f;
	/// <summary>Limits speed gained by air strafing; faster external momentum is preserved.</summary>
	[Property, Group( "Air Control" ), Range( 150f, 1000f )] public float MaximumAirSpeed { get; set; } = 420f;
	/// <summary>Direction response in seconds; zero gives instant steering.</summary>
	[Property, Group( "Inertia" ), Range( 0f, 0.5f ), Step( 0.005f )]
	public float DirectionResponseTime { get; set; } = 0.055f;

	/// <summary>Response when reversing direction. Turning also reduces speed briefly.</summary>
	[Property, Group( "Inertia" ), Range( 0f, 0.5f ), Step( 0.005f )]
	public float ReversalResponseTime { get; set; } = 0.11f;

	/// <summary>Duration of the S-shaped walk-to-sprint target speed transition.</summary>
	[Property, Group( "Sprint Curve" ), Range( 0f, 2f ), Step( 0.05f )]
	public float SprintAccelerationTime { get; set; } = 0.4f;

	[Property, Group( "Sprint Curve" ), Range( 0f, 2f ), Step( 0.05f )]
	public float SprintDecelerationTime { get; set; } = 0.25f;

	/// <summary>Zero is linear, one is a smooth S curve.</summary>
	[Property, Group( "Sprint Curve" ), Range( 0f, 1f ), Step( 0.05f )]
	public float SprintCurveStrength { get; set; } = 1f;

	private Vector3 _moveDirection;
	private float _sprintBlend, _sprintBlendStart, _sprintBlendTarget, _sprintBlendTime;

	private void AddWalkingVelocity()
	{
		if ( EnableMomentumMovement ) AddMomentumVelocity();
		else base.AddVelocity();
	}

	private void AddMomentumVelocity()
	{
		var body = Controller.Body;
		bool grounded = Controller.IsOnGround;
		var ground = grounded ? Controller.GroundVelocity : Vector3.Zero;
		var velocity = (body.Velocity - ground).WithZ( 0f );
		var wish = Controller.WishVelocity.WithZ( 0f );
		float dt = MathF.Max( Time.Delta, 0f );
		float speed = velocity.Length;
		if ( grounded && speed > 0f )
		{
			float drop = MathF.Max( speed, MathF.Max( GroundStopSpeed, 0f ) ) * MathF.Max( GroundDrag, 0f ) * dt;
			velocity *= MathF.Max( speed - drop, 0f ) / speed;
		}
		float wishSpeed = wish.Length;
		if ( wishSpeed > 0f )
		{
			var direction = wish / wishSpeed;
			float cap = grounded ? wishSpeed : MathF.Min( wishSpeed, MathF.Max( AirWishSpeed, 0f ) );
			float remaining = cap - velocity.Dot( direction );
			float acceleration = grounded ? GroundAcceleration : AirAcceleration;
			if ( remaining > 0f ) velocity += direction * MathF.Min( remaining, MathF.Max( acceleration, 0f ) * wishSpeed * dt );
		}
		if ( !grounded ) velocity = velocity.ClampLength( MathF.Max( speed, MathF.Max( MaximumAirSpeed, 0f ) ) );
		// Preserve vertical physics velocity and ground motion; never restore a cached
		// pre-collision velocity, so a wall collision cannot create phantom momentum.
		body.Velocity = (velocity + ground).WithZ( body.Velocity.z );
	}

	private Vector3 UpdateCurvedSpeed( Rotation eyes, Vector3 input, float dt )
	{
		float target = !IsSliding && WantsSprint( input ) ? 1f : 0f;
		if ( target != _sprintBlendTarget )
		{
			_sprintBlendStart = _sprintBlend;
			_sprintBlendTarget = target;
			_sprintBlendTime = 0f;
		}
		_sprintBlendTime += dt;
		float duration = target > _sprintBlendStart ? SprintAccelerationTime : SprintDecelerationTime;
		float t = duration <= 0f ? 1f : Math.Clamp( _sprintBlendTime / duration, 0f, 1f );
		float smooth = t * t * (3f - 2f * t);
		float curve = t + (smooth - t) * Math.Clamp( SprintCurveStrength, 0f, 1f );
		_sprintBlend = _sprintBlendStart + (_sprintBlendTarget - _sprintBlendStart) * curve;

		// Shape the native target, keeping its acceleration/deceleration solver.
		// Restore inspector speeds immediately so stamina and other modes see them unchanged.
		float walk = Controller.WalkSpeed, run = Controller.RunSpeed;
		float speed = walk + (run - walk) * _sprintBlend;
		try
		{
			Controller.WalkSpeed = Controller.RunSpeed = speed;
			return base.UpdateMove( eyes, input );
		}
		finally
		{
			Controller.WalkSpeed = walk;
			Controller.RunSpeed = run;
		}
	}

	private Vector3 SmoothDirection( Vector3 wish, Vector3 steering, float dt )
	{
		if ( wish.LengthSquared < 0.0001f )
		{
			_moveDirection = Vector3.Zero;
			return Vector3.Zero;
		}

		if ( steering.LengthSquared > 0.0001f )
		{
			var target = steering.Normal;
			float response = _moveDirection.Dot( target ) < 0f ? ReversalResponseTime : DirectionResponseTime;
			if ( _moveDirection.LengthSquared < 0.0001f || response <= 0f )
				_moveDirection = target;
			else
				_moveDirection = Vector3.Lerp( _moveDirection, target, 1f - MathF.Exp( -dt / response ) );
		}

		return _moveDirection * wish.Length;
	}

}
