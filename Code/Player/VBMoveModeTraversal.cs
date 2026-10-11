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
	[Property, Group( "Vault" )] public bool EnableThinLedgeVault { get; set; } = true;
	/// <summary>Maximum safe landing drop when climbing across a top that cannot support the body.</summary>
	[Property, Group( "Vault" ), Range( 0f, 200f )] public float LedgeVaultMaximumDrop { get; set; } = 128f;
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
	private Vector3 _start, _rise, _over, _destination;
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
					if ( PlanLedgeExit( WorldPosition, out bool vault ) )
						BeginMotion( vault ? VBTraversalState.Vaulting : VBTraversalState.Climbing,
							vault ? MathF.Max( VaultDuration, ClimbDuration ) : ClimbDuration );
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
