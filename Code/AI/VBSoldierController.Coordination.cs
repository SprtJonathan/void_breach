using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[Sync( SyncFlags.FromHost )] public bool HasFireSlot { get; private set; }
	[Property, Group( "Tactics" ), Range( 0.1f, 0.9f )] public float SupportReloadThreshold { get; set; } = 0.65f;
	private VBSoldierCoordination Coordination => Scene.GetSystem<VBSoldierCoordination>();

	private bool UpdateFirePermission( GameObject target )
	{
		var gun = Weapon;
		if ( !CanFight || !Perception.TargetVisible || !Perception.IsViableTarget( target )
			|| !gun.IsValid() || !gun.IsActive || gun.IsReloading || gun.Clip1 <= 0 || FireReady > 0f
			|| Agent.IsTraversingLink || IsDynamicallyTraversing
			|| State is VBSoldierState.ThrowGrenade or VBSoldierState.Melee )
		{ ReleaseFireSlot(); return false; }
		float distance = Vector3.DistanceBetween( WorldPosition, Perception.LastKnownPosition );
		if ( distance < gun.Npc.MinRange || distance > MathF.Min( gun.Npc.MaxRange, gun.Ballistics.Range )
			|| !HasFiringLane( WorldPosition, target ) || !ClearShot( gun.GetMuzzleTransform().Position, AimPosition, target ) )
		{ ReleaseFireSlot(); return false; }
		HasFireSlot = Coordination.RequestFire( this, target );
		if ( !HasFireSlot ) _burstRemaining = 0;
		return HasFireSlot;
	}

	private void ReleaseFireSlot()
	{
		Coordination.ReleaseFire( this );
		HasFireSlot = false;
	}

	private bool TickSupport( GameObject target )
	{
		if ( !VBSoldierCoordination.IsPlayerTarget( target ) || HasFireSlot ) return false;
		if ( !Perception.TargetVisible && !_hasCover && !_tacticalMove
			&& ContactEvidenceAge >= MathF.Max( LostContactHoldTime, 1f ) ) return false;
		// Finish a flank even when the route temporarily loses sight of the opponent.
		if ( _tacticalMove && TickTacticalMove( target, supporting: true ) ) return true;
		if ( TickCover( target ) ) return true;
		if ( TickTacticalMove( target, supporting: true ) ) return true;
		TryReloadWeapon( topUp: true, support: true );
		StopMoving();
		State = Weapon.IsValid() && Weapon.IsReloading ? VBSoldierState.Reload : VBSoldierState.Support;
		_combatReason = State == VBSoldierState.Reload ? "reload while another soldier fires" : "cover the contact sector";
		// No valid route is a temporary failure, never permission to pile onto the player's centre.
		return true;
	}
}
