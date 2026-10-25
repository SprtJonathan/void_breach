using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[Property, Group( "Awareness" )] public float CloseAwarenessRange { get; set; } = 180f;
	[Property, Group( "Awareness" ), Range( 120f, 300f )] public float CloseAwarenessFieldOfView { get; set; } = 270f;
	[Property, Group( "Awareness" )] public bool UseSquadAlerts { get; set; } = true;
	[Property, Group( "Awareness" )] public float SquadAlertRange { get; set; } = 1400f;
	[Property, Group( "Awareness" )] public float AlertObserveTime { get; set; } = 2f;
	[Property, Group( "Awareness" )] public bool WatchOpenSectors { get; set; } = true;
	private GameObject _pendingContact;
	private Vector3 _pendingContactPosition;
	private float _pendingContactSeenAt, _contactReadyAt, _nextContactReport, _alertObserveUntil;
	private int _investigationRevision, _watchSequence;
	private bool _watchingSector;
	private Vector3 _watchDirection;
	private float _watchAnchorYaw, _watchUntil;
	public bool IsInvestigating => _searching || _recoveringContact;

	private bool InvestigationPermitted( GameObject target )
	{
		if ( !Perception.IsViableTarget( target ) ) { Coordination.ReleaseInvestigation( this ); return true; }
		if ( Coordination.RequestInvestigation( this, target ) ) return true;
		_searching = _approachingLastKnown = false;
		StopMoving(); ReleaseFireSlot();
		AimPosition = Perception.LastKnownPosition + Perception.TargetOffset;
		if ( WatchOpenSectors )
		{
			WatchOpenSector();
			AimPosition = WorldPosition + Perception.EyeOffset + _watchDirection * 420f;
		}
		else Face( AimPosition - WorldPosition );
		TryReloadWeapon( topUp: true, support: true );
		State = VBSoldierState.Alert;
		return false;
	}

	private void TickSquadContacts()
	{
		if ( !UseSquadAlerts ) { _pendingContact = null; return; }
		if ( _pendingContact.IsValid() && Time.Now >= _contactReadyAt )
		{
			if ( Perception.ReceiveContactReport( _pendingContact, _pendingContactPosition, _pendingContactSeenAt )
				&& _aimTarget == _pendingContact ) AimPosition = _pendingContactPosition + Perception.TargetOffset;
			_pendingContact = null;
		}
		if ( !Perception.TargetVisible || !Perception.IsViableTarget( Perception.CurrentTarget )
			|| Time.Now - Perception.LastSeenAt > 0.5f || Time.Now < _nextContactReport ) return;
		_nextContactReport = Time.Now + 0.8f;
		// Relay only a real observation, never a teammate's report or a hidden transform.
		foreach ( var ally in Scene.GetAllComponents<VBSoldierController>() )
		{
			if ( ally == this || !ally.CanFight || !ally.UseSquadAlerts
				|| !string.Equals( Faction.FactionId, ally.Faction.FactionId, StringComparison.OrdinalIgnoreCase )
				|| Faction.IsHostileTo( ally.Faction ) || ally.Faction.IsHostileTo( Faction )
				|| Vector3.DistanceBetween( WorldPosition, ally.WorldPosition ) > MathF.Min( SquadAlertRange, ally.SquadAlertRange ) ) continue;
			ally.QueueContactReport( Perception.CurrentTarget, Perception.LastSeenPosition, Perception.LastSeenAt );
		}
	}

	private void QueueContactReport( GameObject target, Vector3 observedPosition, float observedAt )
	{
		if ( Perception.TargetVisible || !Perception.IsViableTarget( target )
			|| Perception.LastSeenTarget == target && Perception.LastSeenAt >= observedAt ) return;
		if ( _pendingContact.IsValid() && _pendingContact != target
			&& Vector3.DistanceBetween( WorldPosition, _pendingContactPosition ) <= Vector3.DistanceBetween( WorldPosition, observedPosition ) ) return;
		if ( _pendingContact != target ) _contactReadyAt = Time.Now + RandomRange( 0.35f, 0.7f );
		_pendingContact = target; _pendingContactPosition = observedPosition; _pendingContactSeenAt = observedAt;
	}

	private bool TickAlertOrientation()
	{
		if ( Perception.TargetVisible ) { _alertObserveUntil = 0f; _watchingSector = false; return false; }
		bool newClue = _investigationRevision != Perception.InvestigationRevision;
		_investigationRevision = Perception.InvestigationRevision;
		if ( _hasCover || _tacticalMove || _hasTraversalGoal || _recoveringContact || State is VBSoldierState.ThrowGrenade or VBSoldierState.Melee ) return false;
		bool suspicion = Perception.HasVisualSuspicion;
		if ( !suspicion && RemembersTarget( Perception.CurrentTarget ) ) return false;
		if ( newClue && Perception.HasInvestigationPoint && Time.Now - Perception.LastInvestigationAt < 2f )
			_alertObserveUntil = Time.Now + MathF.Max( 0f, AlertObserveTime );
		if ( !suspicion && Time.Now >= _alertObserveUntil ) return false;
		if ( IsInFiringStance ) EndCrouchedFire();
		StopMoving(); ReleaseFireSlot();
		AimPosition = (suspicion ? Perception.VisualSuspicionPosition : Perception.LastKnownPosition) + Perception.TargetOffset;
		Face( AimPosition - WorldPosition );
		State = VBSoldierState.Alert;
		TryReloadWeapon();
		return true;
	}

	private void WatchOpenSector()
	{
		if ( !WatchOpenSectors ) return;
		if ( !_watchingSector )
		{
			_watchingSector = true; _watchAnchorYaw = WorldRotation.Yaw(); _watchUntil = 0f; _watchSequence = 0;
		}
		if ( Time.Now >= _watchUntil )
		{
			// Hold each sector for several seconds and turn gradually. Prefer open
			// space so a stopped patrol does not keep staring into a nearby wall.
			float sweep = (_watchSequence++ % 6) switch { 0 => 0f, 1 => -60f, 2 => 60f, 3 => 120f, 4 => -120f, _ => 180f };
			float desired = _watchAnchorYaw + sweep;
			float best = float.NegativeInfinity;
			var eye = WorldPosition + Perception.EyeOffset;
			for ( int i = 0; i < 12; i++ )
			{
				var direction = Rotation.FromYaw( desired + i * 30f ).Forward;
				var trace = Scene.Trace.Ray( eye, eye + direction * 420f )
					.IgnoreGameObjectHierarchy( GameObject ).IgnoreDynamic().Run();
				float clearance = trace.StartedSolid ? 0f : Vector3.DistanceBetween( eye, trace.EndPosition );
				float score = MathF.Min( clearance, 300f ) - MathF.Abs( MathX.DeltaDegrees( desired, Rotation.LookAt( direction ).Yaw() ) ) * 0.5f;
				if ( score <= best ) continue;
				best = score; _watchDirection = direction;
			}
			_watchUntil = Time.Now + RandomRange( 2.5f, 4f );
		}
		Face( _watchDirection );
	}
}
