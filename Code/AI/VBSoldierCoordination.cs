using System;
using Sandbox;

/// <summary>One budget per player in this scene, shared by every soldier and simulated only by the host.</summary>
public sealed class VBSoldierCoordination : GameObjectSystem<VBSoldierCoordination>
{
	[Property, Range( 0, 12 )] public int MaxShootersPerPlayer { get; set; } = 2;
	[Property, Range( 0.5f, 8f )] public float FireTurnDuration { get; set; } = 5f;
	[Property, Range( 0, 8 )] public int MaxManeuversPerPlayer { get; set; } = 1;
	[Property, Range( 0, 8 )] public int MaxCrouchedShootersPerPlayer { get; set; } = 1;
	[Property, Range( 0, 8 )] public int MaxInvestigatorsPerPlayer { get; set; } = 1;
	[Property, Range( 0.1f, 1f )] public float FireSlotGrace { get; set; } = 0.35f;
	[Property, Range( 0f, 8f )] public float GrenadeRecovery { get; set; } = 1.5f;
	private readonly VBAttackSlotPool<GameObject, VBSoldierController> _fire = new();
	private readonly VBAttackSlotPool<GameObject, VBSoldierController> _maneuvers = new();
	private readonly VBAttackSlotPool<GameObject, VBSoldierController> _crouchedFire = new();
	private readonly VBAttackSlotPool<GameObject, VBSoldierController> _investigation = new();
	private float _nextGrenade;

	public VBSoldierCoordination( Scene scene ) : base( scene ) { }

	public static bool IsPlayerTarget( GameObject target ) => target.IsValid()
		&& (target.GetComponent<PlayerController>().IsValid()
			|| target.GetComponent<VBFactionComponent>()?.FactionId == VBFactionComponent.PlayerFaction);

	public bool RequestFire( VBSoldierController soldier, GameObject target )
	{
		if ( !Networking.IsHost ) return false;
		_fire.RemoveInvalid( ( victim, actor ) => victim.IsValid() && actor.IsValid() && actor.CanFight
			&& actor.GetComponent<VBAiPerceptionComponent>().CurrentTarget == victim );
		if ( !IsPlayerTarget( target ) ) { _fire.Release( soldier ); return true; }
		return _fire.RequestTurn( target, soldier, Time.Now, MaxShootersPerPlayer, FireTurnDuration, FireSlotGrace );
	}

	public void ReleaseFire( VBSoldierController soldier ) => _fire.Release( soldier );

	public bool RequestManeuver( VBSoldierController soldier, GameObject target )
	{
		if ( !Networking.IsHost ) return false;
		_maneuvers.RemoveInvalid( ( victim, actor ) => victim.IsValid() && actor.IsValid() && actor.CanFight
			&& actor.IsManeuvering && actor.GetComponent<VBAiPerceptionComponent>().CurrentTarget == victim );
		if ( !IsPlayerTarget( target ) ) return true;
		return _maneuvers.RequestTurn( target, soldier, Time.Now, MaxManeuversPerPlayer, 60f, 0.5f );
	}

	public void ReleaseManeuver( VBSoldierController soldier ) => _maneuvers.Release( soldier );

	public bool RequestCrouchedFire( VBSoldierController soldier, GameObject target )
	{
		if ( !Networking.IsHost || !IsPlayerTarget( target ) ) return false;
		_crouchedFire.RemoveInvalid( ( victim, actor ) => victim.IsValid() && actor.IsValid() && actor.CanFight
			&& actor.IsInFiringStance && actor.HasFireSlot && actor.GetComponent<VBAiPerceptionComponent>().CurrentTarget == victim );
		return _crouchedFire.RequestTurn( target, soldier, Time.Now, MaxCrouchedShootersPerPlayer, 12f, 0.5f );
	}

	public void ReleaseCrouchedFire( VBSoldierController soldier ) => _crouchedFire.Release( soldier );

	public bool RequestInvestigation( VBSoldierController soldier, GameObject target )
	{
		if ( !Networking.IsHost ) return false;
		_investigation.RemoveInvalid( ( victim, actor ) =>
		{
			if ( !victim.IsValid() || !actor.IsValid() || !actor.CanFight || !actor.IsInvestigating ) return false;
			// A suspended sensor still owns its last observation. Its controller
			// can legitimately investigate that memory while visual polling is paused.
			var senses = actor.GetComponent<VBAiPerceptionComponent>( includeDisabled: true );
			return senses.IsValid() && senses.CurrentTarget == victim && !senses.TargetVisible;
		} );
		if ( !IsPlayerTarget( target ) ) { _investigation.Release( soldier ); return true; }
		return _investigation.RequestTurn( target, soldier, Time.Now, MaxInvestigatorsPerPlayer, 60f, 0.5f );
	}

	public void ReleaseInvestigation( VBSoldierController soldier ) => _investigation.Release( soldier );

	public bool CanThrowGrenade( VBSoldierController soldier )
	{
		if ( !Networking.IsHost || Time.Now < _nextGrenade ) return false;
		// Replicated windups/projectiles also reconstruct the exclusion after host migration.
		foreach ( var ally in Scene.GetAllComponents<VBSoldierController>() )
			if ( ally != soldier && ally.CanFight && ally.State == VBSoldierState.ThrowGrenade ) return false;
		foreach ( var grenade in Scene.GetAllComponents<VBNpcGrenade>() )
			if ( grenade.IsLive && (grenade.SoldierCoordinated
				|| grenade.Thrower.IsValid() && grenade.Thrower.GetComponent<VBSoldierController>().IsValid()) ) return false;
		return true;
	}

	public void GrenadeThrown( VBNpcGrenade grenade ) => _nextGrenade = Time.Now
		+ MathF.Max( 0.1f, grenade.FuseTime ) + MathF.Max( 0f, GrenadeRecovery );
}
