using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[Property, Group( "Firing Stance" )] public bool UseCrouchedFire { get; set; } = true;
	[Property, Group( "Firing Stance" )] public float CrouchedFireMinRange { get; set; } = 180f;
	[Property, Group( "Firing Stance" )] public float CrouchedFireDecisionTime { get; set; } = 0.8f;
	[Property, Group( "Firing Stance" )] public float CrouchedFireSettleTime { get; set; } = 0.65f;
	[Property, Group( "Firing Stance" ), Range( 0.25f, 1f )] public float CrouchedFireSpreadScale { get; set; } = 0.65f;
	[Sync( SyncFlags.FromHost )] public bool IsInFiringStance { get; private set; }
	[Sync( SyncFlags.FromHost )] private TimeUntil FiringStanceSettled { get; set; }
	private GameObject _firingStanceTarget, _firingOpportunityTarget;
	private bool _firingFromCover;
	private float _firingOpportunitySince = -1f, _firingStanceUntil, _nextFiringStance;

	public float FiringStanceAccuracyMultiplier => IsInFiringStance && IsCrouching && FiringStanceSettled <= 0f
		&& CanFight && HasFireSlot && Locomotion.Velocity.WithZ( 0f ).Length < 8f
		? Math.Clamp( CrouchedFireSpreadScale, 0.25f, 1f ) : 1f;
	// Dedicated servers have no cosmetic weapon model; the ballistic muzzle must still follow posture.
	public Vector3 FallbackMuzzlePosition => WorldPosition + Vector3.Up * (IsCrouching ? CrouchedHeight - 14f : 55f)
		+ WorldRotation.Forward * 30f;

	private bool CanStabilizeFire( GameObject target, bool committed, bool fromCover )
	{
		var gun = Weapon;
		if ( !UseCrouchedFire || !CanFight || !HasFireSlot || !Locomotion.IsGrounded
			|| !Perception.TargetVisible || !Perception.IsViableTarget( target ) || !VBSoldierCoordination.IsPlayerTarget( target )
			|| !gun.IsValid() || !gun.IsActive || gun.IsReloading || gun.Clip1 <= 0 || FireReady > 0f
			|| Agent.IsTraversingLink || IsDynamicallyTraversing || _survivalActive || Time.Now < _seekCoverUntil
			|| _hasCover && (!fromCover || !_peeking || Vector3.DistanceBetween( WorldPosition, _peekPosition ) > 24f)
			|| _tacticalMove || _hasDestination || Locomotion.IsMovingDirectly
			|| Locomotion.Velocity.WithZ( 0f ).Length >= 8f ) return false;
		float distance = Vector3.DistanceBetween( WorldPosition, Perception.LastKnownPosition );
		float minimum = MathF.Max( PersonalSpaceRange + 32f, CrouchedFireMinRange * (committed ? 0.8f : 1f) );
		return distance >= minimum && distance >= gun.Npc.MinRange
			&& distance <= MathF.Min( MathF.Max( PreferredRange + (committed || fromCover ? 150f : 0f), minimum ), MathF.Min( gun.Npc.MaxRange, gun.Ballistics.Range ) );
	}

	private bool HasCrouchedFiringLane( GameObject target )
	{
		var eye = WorldPosition + _standingEyeOffset.WithZ( CrouchedHeight - 9f );
		var muzzle = WorldPosition + Vector3.Up * (CrouchedHeight - 14f) + WorldRotation.Forward * 30f;
		// Both must clear the obstacle; a standing eye above a crate is insufficient.
		return ClearShot( eye, AimPosition, target ) && ClearShot( muzzle, AimPosition, target );
	}

	private bool TickCrouchedFire( GameObject target, bool fromCover = false )
	{
		if ( IsInFiringStance )
		{
			if ( target != _firingStanceTarget || Time.Now >= _firingStanceUntil || !CanStabilizeFire( target, true, _firingFromCover )
				|| _firingFromCover && (!_hasCover || !_peeking || Time.Now >= _coverPhaseUntil || Time.Now >= _coverDeadline)
				|| !HasCrouchedFiringLane( target ) || !ClearShot( Weapon.GetMuzzleTransform().Position, AimPosition, target )
				|| !Coordination.RequestCrouchedFire( this, target ) )
			{ EndCrouchedFire(); return false; }
			StopMoving(); State = VBSoldierState.Fire;
			if ( FiringStanceSettled <= 0f ) TickFire( target );
			return true;
		}
		if ( Time.Now < _nextFiringStance || !CanStabilizeFire( target, false, fromCover )
			|| fromCover && _coverPhaseUntil > 0f && _coverPhaseUntil - Time.Now < MathF.Max( 0.3f, CrouchedFireSettleTime ) + 0.4f )
		{ _firingOpportunitySince = -1f; return false; }
		if ( _firingOpportunitySince < 0f || _firingOpportunityTarget != target )
		{ _firingOpportunityTarget = target; _firingOpportunitySince = Time.Now; return false; }
		if ( Time.Now - _firingOpportunitySince < MathF.Max( 0f, CrouchedFireDecisionTime )
			|| !HasCrouchedFiringLane( target ) || !Coordination.RequestCrouchedFire( this, target ) ) return false;
		StopMoving();
		if ( !SetCrouching( true, preserveFireSlot: true ) ) { Coordination.ReleaseCrouchedFire( this ); return false; }
		IsInFiringStance = true; _firingStanceTarget = target;
		_firingFromCover = fromCover;
		_firingOpportunitySince = -1f;
		FiringStanceSettled = MathF.Max( 0.3f, CrouchedFireSettleTime );
		_firingStanceUntil = Time.Now + RandomRange( 5f, 8f );
		if ( fromCover ) _firingStanceUntil = MathF.Min( _firingStanceUntil, _coverPhaseUntil );
		State = VBSoldierState.Fire;
		return true;
	}

	private void EndCrouchedFire()
	{
		Coordination.ReleaseCrouchedFire( this );
		if ( IsInFiringStance )
		{
			IsInFiringStance = false;
			_nextFiringStance = Time.Now + RandomRange( 4f, 7f );
			SetCrouching( false );
		}
		_firingStanceTarget = null; _firingOpportunitySince = -1f; _firingFromCover = false;
	}
}
