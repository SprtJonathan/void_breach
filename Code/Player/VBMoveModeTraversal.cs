using System;
using Sandbox;
using Sandbox.Movement;

public enum VBTraversalState { None, Vaulting, Climbing, Catching, Hanging }

/// <summary>Manual jump-action traversal over static geometry, using swept native body movement.</summary>
[Title( "MoveMode - Void Breach Traversal" )]
[Category( "Void Breach/Player" )]
[Icon( "hiking" )]
public sealed partial class VBMoveModeTraversal : MoveMode, PlayerController.IEvents
{
	[Property, Group( "Traversal" )] public bool EnableTraversal { get; set; } = true;
	[Property, Group( "Traversal" )] public int Priority { get; set; } = 2;
	[Property, Group( "Detection" ), Range( 8f, 100f )] public float ReachDistance { get; set; } = 36f;
	[Property, Group( "Detection" ), Range( 18f, 80f )] public float MinimumHeight { get; set; } = 24f;
	[Property, Group( "Detection" ), Range( 24f, 100f )] public float VaultMaximumHeight { get; set; } = 64f;
	[Property, Group( "Detection" ), Range( 64f, 200f )] public float ClimbMaximumHeight { get; set; } = 112f;
	[Property, Group( "Detection" ), Range( 0f, 45f )] public float MaximumLandingAngle { get; set; } = 25f;
	/// <summary>Small query inset for resting physics contacts; never expands detection reach or disables collision.</summary>
	[Property, Group( "Detection" ), Range( 0f, 0.5f )] public float BodyTraceInset { get; set; } = 0.25f;
	[Property, Group( "Vault" ), Range( 32f, 160f )] public float VaultForwardDistance { get; set; } = 80f;
	[Property, Group( "Vault" ), Range( 0f, 100f )] public float VaultMaximumDrop { get; set; } = 80f;
	[Property, Group( "Timing" ), Range( 0.2f, 2f )] public float VaultDuration { get; set; } = 0.45f;
	[Property, Group( "Timing" ), Range( 0.2f, 2f )] public float ClimbDuration { get; set; } = 0.65f;
	[Property, Group( "Timing" ), Range( 0f, 2f )] public float Cooldown { get; set; } = 0.35f;
	/// <summary>A recent manual press can catch a ledge reached shortly afterwards; holding never renews it.</summary>
	[Property, Group( "Timing" ), Range( 0f, 0.5f )] public float JumpInputBuffer { get; set; } = 0.25f;
	/// <summary>Neutral jump input required after establishing a grip before another press can climb. Zero disables the guard.</summary>
	[Property, Group( "Timing" ), Range( 0f, 0.5f )] public float HangClimbRearmTime { get; set; } = 0.12f;
	[Property, Group( "Ledge Grab" )] public bool EnableLedgeGrab { get; set; } = true;
	[Property, Group( "Ledge Grab" )] public bool CatchWhileRising { get; set; } = true;
	[Property, Group( "Ledge Grab" ), Range( 0f, 40f )] public float CatchReachAboveHead { get; set; } = 0f;
	[Property, Group( "Ledge Grab" ), Range( 0f, 1200f )] public float MaximumCatchFallSpeed { get; set; } = 600f;
	/// <summary>Zero allows unlimited hanging, subject to stamina.</summary>
	[Property, Group( "Ledge Grab" ), Range( 0f, 15f )] public float MaximumHangTime { get; set; } = 5f;
	[Property, Group( "Stamina" ), Range( 0f, 100f )] public float TraversalStaminaCost { get; set; } = 8f;
	[Property, Group( "Stamina" ), Range( 0f, 50f )] public float HangDrainPerSecond { get; set; } = 8f;
	/// <summary>Reserve required after paying for a catch; avoids a grip that immediately fails from low stamina.</summary>
	[Property, Group( "Stamina" ), Range( 0f, 2f )] public float MinimumCatchHoldTime { get; set; } = 0.25f;

	[Sync] public VBTraversalState State { get; private set; }
	/// <summary>Validated ledge contact, shared once per traversal for cosmetic IK on every client.</summary>
	[Sync] public Transform Grip { get; private set; }
	public bool IsTraversing => State != VBTraversalState.None;
	public string FeedbackToken => IsTraversing || _timeSinceFeedback < FeedbackDuration ? _feedbackToken : string.Empty;
	/// <summary>Remaining hang allowance from time and stamina; one means no active limit.</summary>
	public float HangRemainingFraction => Math.Clamp( MathF.Min(
		MaximumHangTime > 0f ? 1f - _hangTime / MaximumHangTime : 1f,
		_stamina.IsValid() && _stamina.Enabled && HangDrainPerSecond > 0f ? _stamina.StaminaFraction : 1f ), 0f, 1f );
	public override bool AllowGrounding => !IsTraversing;
	public override bool AllowFalling => !IsTraversing;
	public override int Score( PlayerController controller ) => EnableTraversal && IsTraversing ? Priority : -1;

	private VBHealthComponent _health;
	private VBStaminaComponent _stamina;
	private VBMoveModeWalk _walk;
	private bool _jumpWasDown;
	private bool _jumpPressed, _hasBufferedJump;
	private bool _hangClimbInputArmed;
	private TimeSince _timeSinceJumpReleased = 0f;
	private VBTraversalState _stateAtJumpPress;
	private TimeSince _timeSinceJumpPress = 999f;
	private TimeSince _timeSinceFeedback = 999f;
	private string _feedbackToken = string.Empty;
	private Vector3 _start, _rise, _over, _destination, _landing;
	private Transform _candidateGrip;
	private GameObject _support;
	private Component _supportComponent;
	private bool _supportHadComponent;
	private Transform _supportTransform;
	private float _motionTime, _duration, _hangTime;
	private bool _finishPending;
	private TimeSince _timeSinceEnded = 999f;
	private const float Skin = 2f;

	internal void ResetJumpInput()
	{
		_jumpWasDown = Input.Down( "jump" );
		_jumpPressed = _hasBufferedJump = false;
		_hangClimbInputArmed = false; _timeSinceJumpReleased = 0f;
	}

	// Sample rendered frames as well as fixed input so a quick tap is not lost between physics steps.
	void PlayerController.IEvents.PreInput() => CaptureJumpInput();
	private void CaptureJumpInput()
	{
		if ( IsProxy ) return;
		bool down = Input.Down( "jump" );
		if ( State == VBTraversalState.Hanging && !_jumpWasDown
			&& _timeSinceJumpReleased >= MathF.Max( HangClimbRearmTime, 0f ) ) _hangClimbInputArmed = true;
		if ( !down && _jumpWasDown ) _timeSinceJumpReleased = 0f;
		if ( down && !_jumpWasDown ) { _jumpPressed = true; _stateAtJumpPress = State; }
		_jumpWasDown = down;
	}

	private bool CanTraverse => EnableTraversal && Enabled && GameObject.Active
		&& Controller.IsValid() && Controller.Enabled && Controller.UseInputControls
		&& Controller.Body.IsValid() && Controller.Body.MotionEnabled
		&& (!_health.IsValid() || (!_health.IsDead && !_health.IsDowned));

	/// <summary>Called by the walk mode before native fixed input, and by this mode while active.</summary>
	internal void HandleTraversalInput()
	{
		if ( IsProxy ) return;
		ResolveComponents();
		CaptureJumpInput();
		bool pressed = _jumpPressed;
		_jumpPressed = false;
		if ( !CanTraverse )
		{
			if ( pressed ) DebugBlockedAttempt( "vb.traversal.debug.unavailable" );
			_hasBufferedJump = false; EndTraversal( reason: "unavailable" ); return;
		}
		if ( IsTraversing )
		{
			_hasBufferedJump = false;
			if ( Input.Down( "duck" ) ) { EndTraversal( "vb.traversal.released", "ducking" ); return; }
			if ( State == VBTraversalState.Hanging && pressed && _stateAtJumpPress == VBTraversalState.Hanging )
			{
				if ( !_hangClimbInputArmed )
				{
					DebugMotionState( "climb_rearm", $"neutral jump required={HangClimbRearmTime:F2}s" );
					return;
				}
				_hangClimbInputArmed = false;
				BeginDebugAttempt();
				try
				{
					if ( PlanClimb( WorldPosition, _landing, _landing.z ) )
						BeginMotion( VBTraversalState.Climbing, ClimbDuration );
					else SetFeedback( "vb.traversal.blocked" );
				}
				finally { EndDebugAttempt(); }
			}
			return;
		}
		if ( Input.Down( "duck" ) || (Controller.Mode is not MoveModeWalk && Controller.Mode != this)
			|| _walk is { IsSliding: true } )
		{
			if ( pressed ) DebugBlockedAttempt( Input.Down( "duck" ) ? "vb.traversal.debug.ducking"
				: _walk is { IsSliding: true } ? "vb.traversal.debug.sliding" : "vb.traversal.debug.mode" );
			_hasBufferedJump = false; return;
		}
		if ( pressed ) { _hasBufferedJump = true; _timeSinceJumpPress = 0f; }
		if ( !_hasBufferedJump ) return;
		if ( !pressed && _timeSinceJumpPress > MathF.Max( JumpInputBuffer, 0f ) ) { _hasBufferedJump = false; return; }
		if ( _timeSinceEnded < MathF.Max( Cooldown, 0f ) )
		{
			if ( pressed ) DebugBlockedAttempt( "vb.traversal.debug.cooldown", $"{MathF.Max( Cooldown - _timeSinceEnded, 0f ):F2} s" );
			return;
		}
		TryStartTraversal();
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		HandleTraversalInput();
		return Vector3.Zero;
	}

	void PlayerController.IEvents.OnJumped()
	{
		// Native jump input still runs on the first tick of a ground traversal.
		// The traversal owns that tick's velocity, without changing input actions.
		if ( !IsProxy && IsTraversing && Controller.Body.IsValid() ) Controller.Body.Velocity = Vector3.Zero;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		ResolveComponents();
		if ( !CanTraverse )
		{
			ResetJumpInput();
			EndTraversal( "vb.traversal.released", "unavailable" );
			return;
		}
	}

	protected override void OnDisabled()
	{
		if ( !IsProxy ) { ResetJumpInput(); EndTraversal( reason: "disabled" ); }
		base.OnDisabled();
	}

	public override void OnModeEnd( MoveMode next )
	{
		if ( !IsProxy ) EndTraversal( reason: "mode", detail: next?.GetType().Name ?? "-" );
		base.OnModeEnd( next );
	}

	public override void UpdateRigidBody( Rigidbody body )
	{
		if ( IsProxy ) return;
		if ( !IsTraversing ) { base.UpdateRigidBody( body ); return; }
		body.Gravity = false;
		body.LinearDamping = 0f;
		if ( Controller.FeetCollider.IsValid() ) Controller.FeetCollider.Friction = 0f;
	}

	public override void AddVelocity()
	{
		if ( IsProxy || !IsTraversing ) return;
		ResolveComponents();
		if ( !CanTraverse ) { EndTraversal( "vb.traversal.released", "unavailable" ); return; }
		if ( SupportFailure() is { } failure ) { EndTraversal( "vb.traversal.released", failure ); return; }
		float dt = MathF.Max( Time.Delta, 0.0001f );
		Controller.WishVelocity = Vector3.Zero;
		if ( State == VBTraversalState.Hanging )
		{
			if ( (WorldPosition - _destination).Length > 4f )
			{ EndTraversal( reason: "hang_displaced" ); return; }
			if ( !PathClear( WorldPosition, WorldPosition ) ) { EndTraversal( reason: "collision" ); return; }
			_hangTime += dt;
			if ( MaximumHangTime > 0f && _hangTime >= MaximumHangTime )
			{ EndTraversal( "vb.traversal.timeout", "hang_timeout" ); return; }
			if ( _stamina.IsValid() && _stamina.Enabled && !_stamina.TrySpend( MathF.Max( HangDrainPerSecond, 0f ) * dt ) )
			{ EndTraversal( "vb.traversal.exhausted", "hang_stamina" ); return; }
			Controller.Body.Velocity = Vector3.Zero;
			return;
		}
		_motionTime += dt;
		if ( _motionTime > _duration + 0.5f ) { EndTraversal( reason: "motion_timeout" ); return; }
		float t = Math.Clamp( _motionTime / _duration, 0f, 1f );
		var next = PositionAt( t );
		if ( !PathClear( WorldPosition, next, "motion" ) ) { EndTraversal( reason: "collision" ); return; }
		Controller.Body.Velocity = (next - WorldPosition) / dt;
		_finishPending = t >= 1f;
	}

	public override void PostPhysicsStep()
	{
		if ( IsProxy || !_finishPending || !IsTraversing ) return;
		if ( (WorldPosition - _destination).Length > 4f ) return;
		_finishPending = false;
		Controller.Body.Velocity = Vector3.Zero;
		if ( State == VBTraversalState.Catching )
		{
			State = VBTraversalState.Hanging; _hangTime = 0f;
			_hangClimbInputArmed = HangClimbRearmTime <= 0f; _timeSinceJumpReleased = 0f;
			SetFeedback( "vb.traversal.hanging" ); DebugMotionState( "hanging" );
		}
		else EndTraversal( "vb.traversal.complete", "complete" );
	}

	private void TryStartTraversal()
	{
		BeginDebugAttempt();
		try { TryStartTraversalCore(); }
		finally { EndDebugAttempt(); }
	}

	private void TryStartTraversalCore()
	{
		bool grounded = Controller.IsOnGround;
		// Controller.Velocity is the previous post-physics snapshot; use the current body velocity.
		float verticalSpeed = Controller.Body.Velocity.z;
		if ( !grounded && !EnableLedgeGrab ) { RejectDebugAttempt( "vb.traversal.debug.grab_disabled" ); return; }
		if ( !grounded && !CatchWhileRising && verticalSpeed > 0f ) { RejectDebugAttempt( "vb.traversal.debug.rising" ); return; }
		if ( !grounded && verticalSpeed < -MathF.Max( MaximumCatchFallSpeed, 0f ) )
		{ RejectDebugAttempt( "vb.traversal.debug.fall_speed", $"vZ={verticalSpeed:F0}, limit=-{MaximumCatchFallSpeed:F0}" ); return; }
		var forward = Rotation.FromYaw( Controller.EyeAngles.yaw ).Forward;
		float probeHeight = grounded ? MathF.Max( MinimumHeight - Skin, Skin ) : Controller.BodyHeight * 0.5f;
		var origin = WorldPosition + Vector3.Up * probeHeight;
		var wall = StaticRay( origin, origin + forward * (Controller.BodyRadius + MathF.Max( ReachDistance, 8f )), "wall" );
		if ( !wall.Hit || wall.StartedSolid )
		{ RejectDebugAttempt( wall.StartedSolid ? "vb.traversal.debug.wall_inside" : "vb.traversal.debug.no_wall", markTrace: true ); return; }
		if ( MathF.Abs( wall.Normal.z ) > 0.2f )
		{ RejectDebugAttempt( "vb.traversal.debug.wall_angle", $"normal.z={wall.Normal.z:F2}, max=0.20", true ); return; }
		if ( wall.Normal.Dot( forward ) > -0.6f )
		{ RejectDebugAttempt( "vb.traversal.debug.approach_angle", $"dot={wall.Normal.Dot( forward ):F2}, max=-0.60", true ); return; }
		float radius = Controller.BodyRadius * (MathF.Abs( wall.Normal.x ) + MathF.Abs( wall.Normal.y ));
		var inside = wall.EndPosition - wall.Normal * (radius + Skin * 2f);
		float minimum = grounded ? MathF.Max( MinimumHeight, 18f ) : Controller.BodyHeight - 24f;
		float maximum = grounded ? MathF.Max( ClimbMaximumHeight, minimum ) : Controller.BodyHeight + MathF.Max( CatchReachAboveHead, 0f );
		SetDebugHeightWindow( minimum, maximum, probeHeight, (wall.EndPosition - origin).Length );
		var top = StaticRay( inside.WithZ( WorldPosition.z + maximum + Skin ), inside.WithZ( WorldPosition.z + minimum ), "top" );
		if ( !IsLanding( top ) )
		{
			RejectDebugAttempt( top.StartedSolid ? "vb.traversal.debug.top_inside"
				: !top.Hit ? "vb.traversal.debug.no_top" : "vb.traversal.debug.landing_angle",
				$"normal.z={top.Normal.z:F2} | {DebugHitName( top )}", true );
			return;
		}
		float ledgeHeight = top.EndPosition.z - WorldPosition.z;
		if ( ledgeHeight > maximum || ledgeHeight < minimum )
		{
			if ( _debugRecording ) RejectDebugAttempt( "vb.traversal.debug.height_window", $"height={ledgeHeight:F1} | allowed=[{minimum:F0}..{maximum:F0}]", true );
			return;
		}
		_landing = top.EndPosition + Vector3.Up * Skin;
		_candidateGrip = new Transform( wall.EndPosition.WithZ( top.EndPosition.z ), Rotation.LookAt( -wall.Normal, Vector3.Up ) );
		bool vault = grounded && ledgeHeight <= MathF.Max( VaultMaximumHeight, MinimumHeight );
		bool topClear = LandingClear( _landing );
		if ( !topClear && !vault ) return;
		_support = top.GameObject;
		_supportComponent = top.Component;
		_supportHadComponent = _supportComponent.IsValid();
		_supportTransform = _support.WorldTransform;

		if ( !grounded )
		{
			var hang = (wall.EndPosition + wall.Normal * (radius + Skin * 2f))
				.WithZ( top.EndPosition.z - (Controller.BodyHeight - 12f) );
			if ( !PathClear( WorldPosition, hang, "catch" ) || !PlanClimb( hang, _landing, _landing.z ) ) return;
			_start = WorldPosition;
			_destination = hang;
			if ( !SpendStartCost( MathF.Max( HangDrainPerSecond, 0f ) * MathF.Max( MinimumCatchHoldTime, 0f ) ) ) return;
			BeginMotion( VBTraversalState.Catching, 0.15f );
			return;
		}

		var landing = _landing;
		bool hasLanding = topClear;
		if ( vault )
		{
			var beyond = inside + forward * MathF.Max( VaultForwardDistance, 32f );
			var far = StaticRay( beyond.WithZ( _landing.z + Skin ), beyond.WithZ( top.EndPosition.z - MathF.Max( VaultMaximumDrop, 0f ) ), "far" );
			if ( IsLanding( far ) && LandingClear( far.EndPosition + Vector3.Up * Skin ) )
			{
				var farLanding = far.EndPosition + Vector3.Up * Skin;
				if ( PlanClimb( WorldPosition, farLanding, _landing.z ) ) { landing = farLanding; hasLanding = true; }
			}
		}
		if ( !hasLanding || !PlanClimb( WorldPosition, landing, _landing.z ) || !SpendStartCost() ) return;
		BeginMotion( vault ? VBTraversalState.Vaulting : VBTraversalState.Climbing, vault ? VaultDuration : ClimbDuration );
	}

	private bool PlanClimb( Vector3 start, Vector3 destination, float topHeight )
	{
		var rise = start.WithZ( MathF.Max( topHeight + Skin, start.z ) );
		var over = destination.WithZ( rise.z );
		if ( !PathClear( start, rise, "rise" ) || !PathClear( rise, over, "across" ) || !PathClear( over, destination, "descent" ) ) return false;
		_start = start; _rise = rise; _over = over; _destination = destination;
		return true;
	}

	private void BeginMotion( VBTraversalState state, float duration )
	{
		if ( !IsTraversing ) Grip = _candidateGrip;
		State = state;
		_hasBufferedJump = _jumpPressed = false;
		SetFeedback( state switch
		{
			VBTraversalState.Vaulting => "vb.traversal.vaulting",
			VBTraversalState.Climbing => "vb.traversal.climbing",
			_ => "vb.traversal.catching"
		} );
		_motionTime = 0f;
		_duration = MathF.Max( duration, 0.1f );
		_finishPending = false;
		Controller.Body.Velocity = Vector3.Zero;
		Controller.WishVelocity = Vector3.Zero;
		AcceptDebugAttempt();
	}

	private Vector3 PositionAt( float t )
	{
		if ( State == VBTraversalState.Catching ) return Vector3.Lerp( _start, _destination, Ease( t ) );
		if ( t < 0.4f ) return Vector3.Lerp( _start, _rise, Ease( t / 0.4f ) );
		if ( t < 0.75f ) return Vector3.Lerp( _rise, _over, Ease( (t - 0.4f) / 0.35f ) );
		return Vector3.Lerp( _over, _destination, Ease( (t - 0.75f) / 0.25f ) );
	}

	private static float Ease( float t ) => t * t * (3f - 2f * t);
	private SceneTraceResult StaticRay( Vector3 start, Vector3 end, string purpose )
	{
		var trace = Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).IgnoreDynamic().Run();
		RecordDebugTrace( start, end, trace, purpose, false, trace.Hit && !trace.StartedSolid );
		return trace;
	}
	private bool IsLanding( SceneTraceResult hit ) => hit.Hit && !hit.StartedSolid && hit.GameObject.IsValid()
		&& hit.Normal.z >= MathF.Cos( Math.Clamp( MaximumLandingAngle, 0f, 45f ) * MathF.PI / 180f );
	private bool PathClear( Vector3 start, Vector3 end, string purpose = "body" )
	{
		// Match native PlayerController.UpdateBody's standing capsule and feet box.
		// TraceBody(2) encloses the capsule in a wider square and rejects legal wall contact.
		float radius = Controller.BodyRadius / MathF.Sqrt( 2f );
		float height = Controller.BodyHeight;
		float inset = Math.Clamp( BodyTraceInset, 0f, MathF.Min( 0.5f, Controller.BodyRadius * 0.1f ) );
		float upper = height - radius;
		float lower = MathF.Max( height * 0.5f, radius + 1f );
		if ( lower < upper )
		{
			var capsule = new Capsule( Vector3.Up * lower, Vector3.Up * upper, radius - inset );
			var trace = Scene.Trace.Capsule( capsule, start, end ).IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).Run();
			var bounds = new BBox( new Vector3( -capsule.Radius, -capsule.Radius, lower - capsule.Radius ),
				new Vector3( capsule.Radius, capsule.Radius, upper + capsule.Radius ) );
			if ( !CheckBodyTrace( start, end, trace, purpose, bounds, capsule ) ) return false;
		}
		float feetHeight = lower < upper ? height * 0.5f : height;
		float halfWidth = Controller.BodyRadius * 0.5f - inset;
		var feet = new BBox( new Vector3( -halfWidth, -halfWidth, inset ), new Vector3( halfWidth, halfWidth, feetHeight - inset ) );
		var feetTrace = Scene.Trace.Box( feet, start, end ).Rotated( WorldRotation )
			.IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).Run();
		return CheckBodyTrace( start, end, feetTrace, purpose, feet );
	}
	private bool CheckBodyTrace( Vector3 start, Vector3 end, SceneTraceResult trace, string purpose, BBox bounds, Capsule? capsule = null )
	{
		bool clear = !trace.StartedSolid && !trace.Hit;
		if ( !EnableTraversalDebug ) return clear;
		bool ongoing = EnableTraversalDebug && !_debugRecording && IsTraversing;
		bool standalone = EnableTraversalDebug && !_debugRecording && !ongoing && !clear;
		if ( standalone ) BeginDebugAttempt();
		if ( ongoing ) _debugRecording = true;
		RecordDebugTrace( start, end, trace, purpose, true, clear, bounds, capsule );
		if ( !clear ) RejectDebugAttempt( trace.StartedSolid ? "vb.traversal.debug.body_inside" : "vb.traversal.debug.path_blocked",
			$"{purpose} | {(capsule.HasValue ? "capsule" : "feet")} | StartedSolid={trace.StartedSolid} | {DebugHitName( trace )}" );
		if ( standalone || ongoing ) EndDebugAttempt();
		return clear;
	}
	private bool LandingClear( Vector3 landing )
	{
		if ( !PathClear( landing, landing, "landing" ) ) return false;
		float radius = Controller.BodyRadius * 0.9f;
		for ( int x = -1; x <= 1; x += 2 )
		for ( int y = -1; y <= 1; y += 2 )
		{
			var point = landing + new Vector3( x * radius, y * radius, 0f );
			if ( !IsLanding( StaticRay( point + Vector3.Up * Skin, point - Vector3.Up * 6f, "footprint" ) ) )
			{ RejectDebugAttempt( "vb.traversal.debug.no_footing", $"corner=({x},{y})", true ); return false; }
		}
		return true;
	}
	private string SupportFailure()
	{
		if ( !_support.IsValid() ) return "support_removed";
		if ( !_support.Active ) return "support_disabled";
		if ( _supportHadComponent && (!_supportComponent.IsValid() || !_supportComponent.Enabled) ) return "collider_removed";
		if ( !((_support.WorldPosition - _supportTransform.Position).Length < 0.5f
			&& (_support.WorldScale - _supportTransform.Scale).Length < 0.001f
			&& Rotation.Difference( _supportTransform.Rotation, _support.WorldRotation ).Angle() < 0.5f) ) return "support_moved";
		return null;
	}
	private bool SpendStartCost( float holdReserve = 0f )
	{
		if ( !_stamina.IsValid() || !_stamina.Enabled ) return true;
		float cost = MathF.Max( TraversalStaminaCost, 0f );
		if ( _stamina.CurrentStamina >= cost + holdReserve && _stamina.TrySpend( cost ) ) return true;
		_hasBufferedJump = false;
		RejectDebugAttempt( _stamina.CurrentStamina >= cost && !_stamina.IsExhausted
			? "vb.traversal.debug.hold_reserve" : "vb.traversal.debug.stamina",
			$"stamina={_stamina.CurrentStamina:F2} | cost={cost:F2} | hold reserve={holdReserve:F2} | exhausted={_stamina.IsExhausted}" );
		SetFeedback( "vb.traversal.exhausted" );
		return false;
	}
	private void SetFeedback( string token ) { _feedbackToken = token; _timeSinceFeedback = 0f; }
	private void ResolveComponents()
	{
		if ( !_walk.IsValid() ) _walk = Components.Get<VBMoveModeWalk>();
		if ( !_health.IsValid() ) _health = Components.Get<VBHealthComponent>();
		if ( !_stamina.IsValid() ) _stamina = Components.Get<VBStaminaComponent>();
	}
	private void EndTraversal( string feedback = "vb.traversal.blocked", string reason = "collision", string detail = "" )
	{
		if ( !IsTraversing ) return;
		DebugMotionState( reason, detail, ended: true );
		SetFeedback( feedback );
		_hasBufferedJump = _jumpPressed = false;
		State = VBTraversalState.None;
		_finishPending = false;
		_timeSinceEnded = 0f;
		if ( Controller.IsValid() && Controller.Body.IsValid() )
		{
			Controller.Body.Gravity = true;
			Controller.Body.Velocity = Vector3.Zero;
			Controller.WishVelocity = Vector3.Zero;
		}
	}
}
