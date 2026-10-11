using System;
using Sandbox;
using Sandbox.Movement;

/// <summary>
/// Extends native walking with configurable ground friction, air acceleration,
/// sprint transitions and sliding; native collision, stepping and jumps stay active.
/// </summary>
[Title( "MoveMode - Void Breach Walk" )]
[Category( "Void Breach/Player" )]
[Icon( "directions_run" )]
public sealed class VBMoveModeWalk : MoveModeWalk, PlayerController.IEvents
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

	[Property, Group( "Slide" )]
	public bool EnableSlide { get; set; } = true;

	[Property, Group( "Slide" ), Range( 0f, 600f )]
	public float SlideMinimumSpeed { get; set; } = 240f;

	[Property, Group( "Slide" ), Range( 0f, 1f ), Step( 0.05f )]
	public float MinimumSprintTime { get; set; } = 0.15f;

	[Property, Group( "Slide" )]
	public bool RequireForwardSprint { get; set; } = true;

	/// <summary>One initial boost in units/second; never reapplied during a slide.</summary>
	[Property, Group( "Slide" ), Range( 0f, 200f )]
	public float SlideSpeedBoost { get; set; } = 35f;

	[Property, Group( "Slide" ), Range( 1f, 800f )]
	public float SlideMaximumSpeed { get; set; } = 420f;

	[Property, Group( "Slide" ), Range( 0.05f, 3f ), Step( 0.05f )]
	public float SlideDuration { get; set; } = 0.8f;

	/// <summary>Horizontal speed lost each second, in addition to slope physics.</summary>
	[Property, Group( "Slide" ), Range( 0f, 1000f )]
	public float SlideDeceleration { get; set; } = 280f;

	/// <summary>A qualifying downhill resets the flat-ground duration limit.</summary>
	[Property, Group( "Slide Slopes" )]
	public bool ExtendSlideDownhill { get; set; } = true;

	/// <summary>Minimum downhill angle along the actual direction of travel.</summary>
	[Property, Group( "Slide Slopes" ), Range( 1f, 45f )]
	public float SlideDownhillAngle { get; set; } = 12f;

	/// <summary>Drag multiplier on qualifying downhills, with gravity projected onto the ground.</summary>
	[Property, Group( "Slide Slopes" ), Range( 0f, 1f ), Step( 0.05f )]
	public float SlideDownhillDragScale { get; set; } = 0.35f;

	[Property, Group( "Slide" ), Range( 0f, 400f )]
	public float SlideEndSpeed { get; set; } = 120f;

	/// <summary>Maximum steering angle per second; looking alone never turns a slide.</summary>
	[Property, Group( "Slide" ), Range( 0f, 360f )]
	public float SlideSteeringDegreesPerSecond { get; set; } = 70f;

	/// <summary>Delay after a slide ends. A fresh crouch press is also required.</summary>
	[Property, Group( "Slide" ), Range( 0f, 3f ), Step( 0.05f )]
	public float SlideCooldown { get; set; } = 0.65f;

	[Property, Group( "Slide" ), Range( 0f, 100f )]
	public float SlideStaminaCost { get; set; } = 8f;

	[Sync] public bool IsSliding { get; private set; }

	private VBStaminaComponent _stamina;
	private VBHealthComponent _health;
	private VBMoveModeTraversal _traversal;
	private Vector3 _moveDirection;
	private Vector3 _slideSteering;
	private bool _duckWasDown;
	private bool _boostPending;
	private float _sprintTime;
	private float _slideTime;
	private bool _slideDownhill;
	private Vector3 _slideGroundNormal = Vector3.Up;
	private float _sprintBlend, _sprintBlendStart, _sprintBlendTarget, _sprintBlendTime;
	private TimeSince _timeSinceSlide = 999f;

	private bool CanMove => Controller.IsValid() && Controller.Enabled
		&& Controller.UseInputControls && Controller.Body.IsValid() && Controller.Body.MotionEnabled
		&& (!_health.IsValid() || (!_health.IsDowned && !_health.IsDead));

	public override void OnModeBegin()
	{
		base.OnModeBegin();
		if ( IsProxy ) return;
		ResolveComponents();
		_moveDirection = Controller.Velocity.WithZ( 0f ).Normal;
		if ( _traversal.IsValid() ) _traversal.ResetJumpInput();
		_duckWasDown = Input.Down( "duck" );
		_sprintTime = 0f;
		_sprintBlend = _sprintBlendStart = _sprintBlendTarget = _sprintBlendTime = 0f;
	}

	public override void OnModeEnd( MoveMode next )
	{
		if ( !IsProxy )
		{
			EndSlide();
			_sprintTime = 0f;
		}
		base.OnModeEnd( next );
	}

	void PlayerController.IEvents.OnJumped()
	{
		if ( !IsProxy ) EndSlide();
	}

	protected override void OnDisabled()
	{
		if ( !IsProxy )
		{
			EndSlide();
			_sprintTime = 0f;
		}
		base.OnDisabled();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( IsProxy ) return;
		ResolveComponents();
		if ( !CanMove || Controller.Mode != this )
		{
			EndSlide();
			_sprintTime = 0f;
			_duckWasDown = Input.Down( "duck" );
		}
		else if ( !EnableSlide ) EndSlide();
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		if ( IsProxy ) return Controller.WishVelocity;
		ResolveComponents();
		var dt = MathF.Max( Time.Delta, 0f );
		if ( _traversal.IsValid() && _traversal.Enabled )
		{
			_traversal.HandleTraversalInput();
			if ( _traversal.IsTraversing ) { EndSlide(); return Vector3.Zero; }
		}
		bool duckDown = Input.Down( "duck" );
		bool duckPressed = duckDown && !_duckWasDown;
		_duckWasDown = duckDown;

		if ( !CanMove )
		{
			EndSlide();
			_sprintTime = 0f;
			_moveDirection = Vector3.Zero;
			return Vector3.Zero;
		}

		// Keep native speed smoothing warm even during the slide, for a smooth exit.
		var wish = UpdateCurvedSpeed( eyes, input, dt ).WithZ( 0f );
		var steering = (Rotation.FromYaw( eyes.Yaw() ) * input.WithZ( 0f )).ClampLength( 1f );
		wish = SmoothDirection( wish, steering, dt );
		_slideSteering = steering;
		if ( EnableMomentumMovement )
		{
			// Friction handles release on the ground; air control follows current
			// input without injecting residual input from native target smoothing.
			if ( steering.IsNearZeroLength ) wish = Vector3.Zero;
			else if ( !Controller.IsOnGround )
				wish = steering * (Controller.IsDucking ? Controller.DuckedSpeed
					: Controller.WalkSpeed + (Controller.RunSpeed - Controller.WalkSpeed) * _sprintBlend);
		}

		if ( IsSliding )
		{
			UpdateSlideSlope();
			_slideTime = _slideDownhill ? 0f : _slideTime + dt;
			if ( !EnableSlide || !Controller.IsOnGround
				|| _slideTime >= MathF.Max( SlideDuration, 0.05f )
				|| Controller.Velocity.WithZ( 0f ).Length < MathF.Max( SlideEndSpeed, 0f ) )
				EndSlide();
		}
		else if ( duckPressed && !Input.Pressed( "jump" ) && CanStartSlide() )
		{
			IsSliding = true;
			_slideTime = 0f;
			_boostPending = true;
			UpdateSlideSlope();
		}

		bool sprinting = Controller.IsOnGround && !duckDown && !Controller.IsDucking
			&& (!RequireForwardSprint || input.x > 0.1f) && WantsSprint( input );
		_sprintTime = sprinting ? _sprintTime + dt : 0f;

		// A slide carries actual collision-resolved velocity, never a stored target
		// that could keep driving the player into a wall after an impact.
		return IsSliding ? Controller.Velocity.WithZ( 0f ) : wish;
	}

	public override void UpdateRigidBody( Rigidbody body )
	{
		if ( IsProxy ) return;
		if ( _traversal.IsValid() && _traversal.IsTraversing ) { _traversal.UpdateRigidBody( body ); return; }
		base.UpdateRigidBody( body );
		if ( EnableMomentumMovement && CanMove )
		{
			// Our solver owns drag. Native collision shapes, gravity and stepping remain active.
			body.LinearDamping = 0f;
			if ( Controller.FeetCollider.IsValid() ) Controller.FeetCollider.Friction = 0f;
			// Without native feet friction, gravity would keep accelerating a resting
			// character down standable static slopes. Native grounding/steps follow
			// the surface; falling gravity resumes immediately after losing support.
			body.Gravity = !Controller.IsOnGround || Controller.GroundIsDynamic;
		}
		if ( !IsSliding ) return;
		body.LinearDamping = 0f;
		// Native regrounding cancels falling velocity on slopes. Apply the ground
		// plane's gravity in AddVelocity instead, exactly once per fixed step.
		body.Gravity = false;
		// Native crouch braking would otherwise stop the slide immediately.
		if ( Controller.FeetCollider.IsValid() ) Controller.FeetCollider.Friction = 0f;
	}

	// Called after native fixed input and before physics updates collision shapes.
	// Keep the native crouch state latched without rewriting the player's input.
	internal void UpdateSlideStance()
	{
		if ( IsProxy || !IsSliding ) return;
		ResolveComponents();
		if ( !Enabled || !GameObject.Active || !CanMove || Controller.Mode != this
			|| !EnableSlide || !Controller.IsOnGround )
		{
			EndSlide();
			return;
		}
		Controller.UpdateDucking( true );
	}

	public override void AddVelocity()
	{
		if ( IsProxy ) return;
		ResolveComponents();
		if ( !CanMove )
		{
			EndSlide();
			return;
		}
		if ( _traversal.IsValid() && _traversal.IsTraversing ) { _traversal.AddVelocity(); return; }

		var body = Controller.Body;
		var relativeVelocity = body.Velocity - Controller.GroundVelocity;
		if ( IsSliding && (!EnableSlide || !Controller.IsOnGround) )
			EndSlide();

		if ( !IsSliding )
		{
			if ( EnableMomentumMovement ) AddMomentumVelocity();
			else base.AddVelocity();
			return;
		}

		var horizontal = relativeVelocity.WithZ( 0f );
		float speed = horizontal.Length;
		if ( speed < MathF.Max( SlideEndSpeed, 1f ) )
		{
			EndSlide();
			if ( EnableMomentumMovement ) AddMomentumVelocity();
			else base.AddVelocity();
			return;
		}

		if ( _boostPending )
		{
			speed += MathF.Max( SlideSpeedBoost, 0f );
			_boostPending = false;
		}

		float dt = MathF.Max( Time.Delta, 0f );
		var gravity = Scene.PhysicsWorld.Gravity * body.GravityScale;
		var slopeGravity = gravity - _slideGroundNormal * gravity.Dot( _slideGroundNormal );
		horizontal = horizontal.Normal * speed + slopeGravity.WithZ( 0f ) * dt;
		speed = horizontal.Length;
		float dragScale = _slideDownhill ? Math.Clamp( SlideDownhillDragScale, 0f, 1f ) : 1f;
		speed = MathF.Max( 0f, MathF.Min( speed, MathF.Max( SlideMaximumSpeed, 1f ) )
			- MathF.Max( SlideDeceleration, 0f ) * dragScale * dt );
		var direction = SteerSlide( horizontal.Normal, _slideSteering, dt );
		var nextHorizontal = direction * speed;
		float vertical = -nextHorizontal.Dot( _slideGroundNormal.WithZ( 0f ) ) / MathF.Max( _slideGroundNormal.z, 0.01f );
		body.Velocity = nextHorizontal.WithZ( vertical ) + Controller.GroundVelocity;
		Controller.WishVelocity = direction * speed;
	}

	private void AddMomentumVelocity()
	{
		var body = Controller.Body;
		var ground = Controller.IsOnGround ? Controller.GroundVelocity : Vector3.Zero;
		var velocity = (body.Velocity - ground).WithZ( 0f );
		var wish = Controller.WishVelocity.WithZ( 0f );
		float dt = MathF.Max( Time.Delta, 0f );
		float speed = velocity.Length;
		if ( Controller.IsOnGround && speed > 0f )
		{
			float drop = MathF.Max( speed, MathF.Max( GroundStopSpeed, 0f ) ) * MathF.Max( GroundDrag, 0f ) * dt;
			velocity *= MathF.Max( speed - drop, 0f ) / speed;
		}
		float wishSpeed = wish.Length;
		if ( wishSpeed > 0f )
		{
			var direction = wish / wishSpeed;
			float cap = Controller.IsOnGround ? wishSpeed : MathF.Min( wishSpeed, MathF.Max( AirWishSpeed, 0f ) );
			float remaining = cap - velocity.Dot( direction );
			float acceleration = Controller.IsOnGround ? GroundAcceleration : AirAcceleration;
			if ( remaining > 0f ) velocity += direction * MathF.Min( remaining, MathF.Max( acceleration, 0f ) * wishSpeed * dt );
		}
		if ( !Controller.IsOnGround ) velocity = velocity.ClampLength( MathF.Max( speed, MathF.Max( MaximumAirSpeed, 0f ) ) );
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

	private void UpdateSlideSlope()
	{
		_slideDownhill = false;
		_slideGroundNormal = Vector3.Up;
		if ( !Controller.IsOnGround ) return;
		var direction = Controller.Velocity.WithZ( 0f ).Normal;
		var ground = Scene.Trace.Ray( WorldPosition + Vector3.Up * 32f, WorldPosition - Vector3.Up * 32f )
			.IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).Run();
		if ( !ground.Hit || ground.StartedSolid
			|| ground.Normal.z < MathF.Cos( GroundAngle * MathF.PI / 180f ) ) return;
		_slideGroundNormal = ground.Normal;
		if ( !ExtendSlideDownhill ) return;
		float grade = ground.Normal.WithZ( 0f ).Dot( direction ) / MathF.Max( ground.Normal.z, 0.01f );
		_slideDownhill = grade >= MathF.Tan( Math.Clamp( SlideDownhillAngle, 1f, 45f ) * MathF.PI / 180f );
	}

	private void ResolveComponents()
	{
		if ( !_traversal.IsValid() ) _traversal = Components.Get<VBMoveModeTraversal>();
		if ( !_stamina.IsValid() ) _stamina = Components.Get<VBStaminaComponent>();
		if ( !_health.IsValid() ) _health = Components.Get<VBHealthComponent>();
	}

	public override void PrePhysicsStep()
	{
		if ( IsProxy ) return;
		if ( !_traversal.IsValid() || !_traversal.IsTraversing ) base.PrePhysicsStep();
	}

	public override void PostPhysicsStep()
	{
		if ( IsProxy ) return;
		if ( _traversal.IsValid() && _traversal.IsTraversing ) _traversal.PostPhysicsStep();
		else base.PostPhysicsStep();
	}

	private bool WantsSprint( Vector3 input )
	{
		if ( _stamina.IsValid() && _stamina.Enabled )
			return _stamina.CanSprint && _stamina.WantsSprint( Controller, input );

		bool altDown = !string.IsNullOrEmpty( Controller.AltMoveButton ) && Input.Down( Controller.AltMoveButton );
		return input.LengthSquared > 0.0001f && (Controller.RunByDefault ? !altDown : altDown);
	}

	private bool CanStartSlide()
	{
		if ( !EnableSlide || !Controller.IsOnGround || Controller.IsDucking
			|| _timeSinceSlide < MathF.Max( SlideCooldown, 0f )
			|| _sprintTime <= 0f || _sprintTime < MathF.Max( MinimumSprintTime, 0f )
			|| Controller.Velocity.WithZ( 0f ).Length < MathF.Max( SlideMinimumSpeed, 1f ) )
			return false;

		if ( _stamina.IsValid() && _stamina.Enabled )
			return _stamina.CanSprint && _stamina.TrySpend( MathF.Max( SlideStaminaCost, 0f ) );

		return true;
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

	private Vector3 SteerSlide( Vector3 direction, Vector3 input, float dt )
	{
		if ( input.LengthSquared < 0.0001f ) return direction;
		float current = MathF.Atan2( direction.y, direction.x );
		float target = MathF.Atan2( input.y, input.x );
		float delta = MathF.Atan2( MathF.Sin( target - current ), MathF.Cos( target - current ) );
		float maxTurn = MathF.Max( SlideSteeringDegreesPerSecond, 0f ) * (MathF.PI / 180f) * dt;
		float angle = current + Math.Clamp( delta, -maxTurn, maxTurn ) * MathF.Min( input.Length, 1f );
		return new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0f );
	}

	private void EndSlide()
	{
		if ( !IsSliding ) return;
		IsSliding = false;
		_slideDownhill = false;
		_slideGroundNormal = Vector3.Up;
		_boostPending = false;
		_timeSinceSlide = 0f;
		if ( Controller.IsValid() && Controller.Body.IsValid() ) base.UpdateRigidBody( Controller.Body );
	}
}
