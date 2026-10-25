using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[Property, Group( "Tempo" )] public float CombatAttentionTime { get; set; } = 18f;
	[Property, Group( "Tempo" )] public float LostContactHoldTime { get; set; } = 4f;
	[Property, Group( "Tempo" )] public float InitialAssessmentTime { get; set; } = 1.2f;
	[Property, Group( "Tempo" )] public float PositionHoldMin { get; set; } = 5f;
	[Property, Group( "Tempo" )] public float PositionHoldMax { get; set; } = 9f;
	[Property, Group( "Tempo" ), Range( 120f, 300f )] public float EngagedFieldOfView { get; set; } = 240f;
	[Property, Group( "Movement" )] public float MovementAcceleration { get; set; } = 240f;
	[Property, Group( "Movement" )] public float MovementBraking { get; set; } = 320f;
	[Property, Group( "Movement" )] public float MaximumTurnRate { get; set; } = 110f;
	[Property, Group( "Movement" )] public float EmergencyMoveSpeed { get; set; } = 170f;
	private float _holdPositionUntil;
	private float _turnFrameTime = -1f, _turnUsed;
	public bool IsManeuvering => _tacticalMove;

	/// <summary>Attention is memory, not sight: this never reads a hidden actor's position.</summary>
	public bool RemembersTarget( GameObject target ) => target.IsValid() && Perception.IsValid()
		&& Perception.LastSeenTarget == target && Perception.IsViableTarget( target )
		&& Time.Now - Perception.LastSeenAt <= MathF.Max( AimMemoryTime, CombatAttentionTime );

	private void RestAtPosition()
	{
		_holdPositionUntil = MathF.Max( _holdPositionUntil, Time.Now
			+ RandomRange( MathF.Max( 0f, PositionHoldMin ), MathF.Max( 0f, PositionHoldMax ) ) );
	}

	private bool TickLostContactHold( GameObject target )
	{
		if ( !Weapon.IsValid() || _searching || !RemembersTarget( target )
			|| Time.Now - Perception.LastSeenAt >= MathF.Max( 0f, LostContactHoldTime ) ) return false;
		if ( UseCover && TickCover( target ) ) return true;
		StopMoving(); FaceCombatAim();
		TryReloadWeapon( topUp: true, support: true );
		State = Weapon.IsReloading ? VBSoldierState.Reload : VBSoldierState.Support;
		return true;
	}

	internal Vector3 ApproachMovementVelocity( Vector3 current, Vector3 desired, float delta )
	{
		bool braking = desired.Length < current.Length || Vector3.Dot( current, desired ) < 0f;
		float rate = braking ? MovementBraking : MovementAcceleration;
		return current + (desired - current).ClampLength( MathF.Max( 1f, rate ) * MathF.Max( 0f, delta ) );
	}
}
