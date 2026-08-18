using System;
using Sandbox;
using Sandbox.Movement;

/// <summary>
/// Owner-authoritative stamina reservoir used by the native player movement mode
/// and by instant actions.
/// </summary>
public sealed class VBStaminaComponent : Component, PlayerController.IEvents
{
	[Property, Range( 1f, 500f )]
	public float MaxStamina { get; set; } = 100f;

	[Property, Range( 0f, 100f )]
	public float SprintDrainPerSecond { get; set; } = 20f;

	[Property, Range( 0f, 100f )]
	public float RegenPerSecond { get; set; } = 15f;

	[Property, Range( 0f, 10f )]
	public float RegenDelay { get; set; } = 1.5f;

	[Property, Range( 0f, 1f )]
	public float ExhaustedResumeFraction { get; set; } = 0.2f;

	[Property]
	public bool RequireMovementInput { get; set; } = true;

	[Sync] public float CurrentStamina { get; private set; }
	[Sync] public bool IsExhausted { get; private set; }
	[Sync] public bool IsSprinting { get; private set; }

	public float StaminaFraction => MaxStamina > 0f
		? CurrentStamina / MaxStamina
		: 0f;

	public bool CanSprint =>
		!IsExhausted
		&& CurrentStamina > 0f
		&& (_health is null || (!_health.IsDowned && !_health.IsDead));

	public Action<float> OnStaminaSpent;
	public Action<float> OnStaminaRestored;
	public Action OnExhausted;
	public Action OnRecovered;
	public Action OnStaminaChanged;

	private PlayerController _controller;
	private VBHealthComponent _health;
	private TimeSince _timeSinceSpent;
	private float _nativeRunSpeed;

	protected override void OnStart()
	{
		_controller = Components.Get<PlayerController>();
		_health = Components.Get<VBHealthComponent>();
		_nativeRunSpeed = _controller?.RunSpeed ?? 0f;

		if ( IsProxy )
			return;

		ResetStamina();
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy )
			return;

		_controller ??= Components.Get<PlayerController>();

		bool wantsSprint = _controller is not null
			&& _controller.Mode is MoveModeWalk
			&& WantsSprint( _controller, Input.AnalogMove );

		IsSprinting = wantsSprint && CanSprint;

		if ( IsSprinting )
			SpendInternal( SprintDrainPerSecond * Scene.FixedDelta );
		else
			Regenerate( Scene.FixedDelta );
	}

	protected override void OnDisabled()
	{
		if ( !IsProxy && _controller is not null && _nativeRunSpeed > 0f )
			_controller.RunSpeed = _nativeRunSpeed;
	}

	/// <summary>
	/// Runs through PlayerController's native input extension point before its
	/// fixed movement update. The native walk mode remains responsible for all
	/// acceleration and movement calculations.
	/// </summary>
	void PlayerController.IEvents.PreInput()
	{
		_controller ??= Components.Get<PlayerController>();

		if ( _controller is null )
			return;

		if ( _controller.RunSpeed > _controller.WalkSpeed )
			_nativeRunSpeed = MathF.Max( _nativeRunSpeed, _controller.RunSpeed );

		bool blockSprint = _controller.Mode is MoveModeWalk
			&& WantsSprint( _controller, Input.AnalogMove )
			&& !CanSprint;

		_controller.RunSpeed = blockSprint
			? _controller.WalkSpeed
			: MathF.Max( _nativeRunSpeed, _controller.WalkSpeed );
	}

	/// <summary>
	/// Returns whether the native player controller is currently requesting its run speed.
	/// This mirrors PlayerController's AltMoveButton and RunByDefault behavior.
	/// </summary>
	public bool WantsSprint( PlayerController controller, Vector3 movementInput )
	{
		if ( controller is null || controller.IsDucking )
			return false;

		if ( RequireMovementInput && movementInput.LengthSquared <= 0.0001f )
			return false;

		bool altMoveDown = !string.IsNullOrEmpty( controller.AltMoveButton )
			&& Input.Down( controller.AltMoveButton );

		return controller.RunByDefault ? !altMoveDown : altMoveDown;
	}

	public bool TrySpend( float amount )
	{
		if ( IsProxy || amount < 0f )
			return false;

		if ( amount == 0f )
			return true;

		if ( IsExhausted || CurrentStamina < amount )
			return false;

		SpendInternal( amount );
		return true;
	}

	public float Restore( float amount )
	{
		if ( IsProxy || amount <= 0f )
			return 0f;

		float previousStamina = CurrentStamina;
		CurrentStamina = Math.Clamp( CurrentStamina + amount, 0f, MaxStamina );
		float restored = CurrentStamina - previousStamina;

		UpdateExhaustion();

		if ( restored > 0f )
		{
			OnStaminaRestored?.Invoke( restored );
			OnStaminaChanged?.Invoke();
		}

		return restored;
	}

	public void ResetStamina()
	{
		if ( IsProxy )
			return;

		CurrentStamina = MathF.Max( 1f, MaxStamina );
		IsExhausted = false;
		IsSprinting = false;
		_timeSinceSpent = RegenDelay;
		OnStaminaChanged?.Invoke();
	}

	private void SpendInternal( float amount )
	{
		if ( amount <= 0f )
			return;

		float previousStamina = CurrentStamina;
		CurrentStamina = MathF.Max( 0f, CurrentStamina - amount );
		float spent = previousStamina - CurrentStamina;
		_timeSinceSpent = 0f;
		UpdateExhaustion();

		if ( spent > 0f )
		{
			OnStaminaSpent?.Invoke( spent );
			OnStaminaChanged?.Invoke();
		}
	}

	private void Regenerate( float deltaTime )
	{
		if ( _health is not null && (_health.IsDowned || _health.IsDead) )
			return;

		if ( _timeSinceSpent < RegenDelay || CurrentStamina >= MaxStamina )
			return;

		Restore( RegenPerSecond * deltaTime );
	}

	private void UpdateExhaustion()
	{
		if ( !IsExhausted && CurrentStamina <= 0f )
		{
			IsExhausted = true;
			IsSprinting = false;
			OnExhausted?.Invoke();
			return;
		}

		float recoveryThreshold = MaxStamina * Math.Clamp( ExhaustedResumeFraction, 0f, 1f );

		if ( IsExhausted && CurrentStamina >= recoveryThreshold )
		{
			IsExhausted = false;
			OnRecovered?.Invoke();
		}
	}

	[Button( "Debug: Spend 25 Stamina" )]
	public void Debug_Spend25()
	{
		TrySpend( 25f );
	}

	[Button( "Debug: Restore 25 Stamina" )]
	public void Debug_Restore25()
	{
		Restore( 25f );
	}

	[Button( "Debug: Empty Stamina" )]
	public void Debug_Empty()
	{
		SpendInternal( CurrentStamina );
	}
}
