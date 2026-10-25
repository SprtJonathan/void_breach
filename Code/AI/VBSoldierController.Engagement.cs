using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[Property, Group( "Engagement" )] public float PersonalSpaceRange { get; set; } = 100f;
	[Property, Group( "Engagement" )] public float MaxCombatPathLength { get; set; } = 480f;
	[Property, Group( "Engagement" )] public float TacticalSightLossBudget { get; set; } = 3f;
	[Property, Group( "Engagement" )] public float ContactRecoveryDelay { get; set; } = 6f;
	private GameObject _engagementTarget;
	private Vector3 _lastFiringPosition, _pressurePosition, _recoveryPosition;
	private bool _hasFiringPosition, _requestContactRecovery, _recoveringContact;
	private float _pressureSince = -1f, _nextContactRecovery, _recoveryDeadline, _recoveryArrivedAt = -1f, _resumeCoverAt;
	private string _combatReason = "patrol";
	private float ContactEvidenceAge => Time.Now - MathF.Max(
		Perception.LastSeenTarget == Perception.CurrentTarget ? Perception.LastSeenAt : -10000f,
		Perception.ReportedTarget == Perception.CurrentTarget ? Perception.ReportedAt : -10000f );

	private void UpdateEngagementContext()
	{
		var target = Perception.CurrentTarget;
		if ( target != _engagementTarget )
		{
			EndContactRecovery();
			_engagementTarget = target; _hasFiringPosition = false; _pressureSince = -1f;
			_requestContactRecovery = false;
		}
		if ( !Perception.TargetVisible ) { _pressureSince = -1f; return; }
		var observed = Perception.LastSeenPosition;
		if ( _pressureSince < 0f || Vector3.DistanceBetween( observed, _pressurePosition ) > 96f )
		{ _pressureSince = Time.Now; _pressurePosition = observed; }
		if ( !_hasCover && !_tacticalMove && !_recoveringContact && Locomotion.Velocity.WithZ( 0f ).Length < 8f
			&& ClearShot( WorldPosition + _standingEyeOffset, observed + Perception.TargetOffset, target ) )
		{ _lastFiringPosition = WorldPosition; _hasFiringPosition = true; }
	}

	private bool TickContactRecovery()
	{
		var target = Perception.CurrentTarget;
		if ( !Perception.IsViableTarget( target ) || Perception.TargetVisible
			|| State is VBSoldierState.ThrowGrenade or VBSoldierState.Melee
			|| _hasTraversalGoal || !Weapon.IsValid() )
		{
			if ( _recoveringContact ) { EndContactRecovery(); StopMoving(); RestAtPosition(); }
			return false;
		}
		if ( _recoveringContact )
		{
			if ( !Coordination.RequestInvestigation( this, target ) || Time.Now >= _recoveryDeadline
				|| _hasDestination && !MovementProgressing() )
			{ EndContactRecovery(); StopMoving(); return false; }
			if ( ReachedDestination() )
			{
				StopMoving();
				if ( _recoveryArrivedAt < 0f ) _recoveryArrivedAt = Time.Now;
				if ( Time.Now - _recoveryArrivedAt > 1.2f ) { EndContactRecovery(); return false; }
			}
			AimPosition = Perception.LastKnownPosition + Perception.TargetOffset;
			FaceCombatAim(); State = VBSoldierState.Reposition;
			return true;
		}
		if ( _searching || MoveSpeed <= 0f || Weapon.IsReloading || Time.Now < _nextContactRecovery
			|| !_requestContactRecovery && ContactEvidenceAge < MathF.Max( LostContactHoldTime, ContactRecoveryDelay ) ) return false;
		_requestContactRecovery = false;
		_nextContactRecovery = Time.Now + 4f;
		// Stop recycling a blind shelter or flank. Even if no reconnecting point
		// exists, the normal bounded investigation must be allowed to run next.
		ClearCover(); ClearTacticalMove(); StopMoving(); EndCrouchedFire();
		_resumeCoverAt = Time.Now + 6f;
		if ( !Coordination.RequestInvestigation( this, target ) ) return false;
		AimPosition = Perception.LastKnownPosition + Perception.TargetOffset;
		bool found = false; float best = float.PositiveInfinity;
		Vector3 selected = default;
		for ( int i = 0; i < 25; i++ )
		{
			Vector3? sample = i == 0 && _hasFiringPosition ? Scene.NavMesh.GetClosestPoint( _lastFiringPosition, 32f )
				: Scene.NavMesh.GetClosestPoint( WorldPosition + Rotation.FromYaw( i * 45f ).Forward * (80f + (i - 1) / 8 * 90f), 48f );
			if ( !sample.HasValue || Vector3.DistanceBetween( sample.Value, Perception.LastKnownPosition ) < PersonalSpaceRange + 32f
				|| !PositionAvailable( sample.Value, Agent.Radius * 3f ) || !HasFiringLane( sample.Value, target, standing: true )
				|| !VBNpcNavigation.TryWalkPath( Scene, Agent, WorldPosition, sample.Value, out var path ) ) continue;
			float length = VBNpcNavigation.PathLength( path );
			if ( length > MaxCombatPathLength || length >= best ) continue;
			best = length; selected = sample.Value; found = true;
		}
		if ( !found || !TryMove( selected, allowTraversal: false ) )
		{ Coordination.ReleaseInvestigation( this ); _combatReason = "investigate recorded contact: no local firing route"; return false; }
		_recoveryPosition = selected; _recoveryArrivedAt = -1f;
		_recoveringContact = true;
		_recoveryDeadline = Time.Now + Math.Clamp( best / MathF.Max( SearchSpeed, 1f ) + 2f, 3f, 10f );
		Agent.MaxSpeed = MathF.Min( MoveSpeed, SearchSpeed );
		State = VBSoldierState.Reposition;
		_combatReason = "rejoin a reachable firing line";
		return true;
	}

	private void EndContactRecovery()
	{
		if ( _recoveringContact ) Coordination.ReleaseInvestigation( this );
		_recoveringContact = false; _recoveryArrivedAt = -1f;
	}
}
