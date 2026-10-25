using System;
using Sandbox;
using Sandbox.Navigation;

public sealed partial class VBSoldierController
{
	[Property, Group( "Tactics" )] public bool UseTacticalPositions { get; set; } = true;
	[Property, Group( "Tactics" ), Range( 100f, 800f )] public float RepositionRadius { get; set; } = 360f;
	[Property, Group( "Tactics" ), Range( 2f, 20f )] public float RepositionInterval { get; set; } = 12f;
	[Property, Group( "Tactics" ), Range( 32f, 250f )] public float AllySpacing { get; set; } = 120f;
	[Property, Group( "Tactics" ), Range( 360f, 1600f )] public float SupportRepositionRadius { get; set; } = 900f;
	[Property, Group( "Tactics" )] public float SupportMemoryTime { get; set; } = 18f;
	private bool _tacticalMove, _flanking, _supportRoute;
	private Vector3 _tacticalPosition, _tacticalThreat;
	private GameObject _tacticalTarget;
	private float _nextTacticalQuery, _tacticalDeadline;

	private bool TickTacticalMove( GameObject target, bool supporting = false )
	{
		var gun = Weapon;
		if ( !UseTacticalPositions || MoveSpeed <= 0f || _hasCover
			|| !gun.IsValid() || !supporting && (Time.Now < _seekCoverUntil || gun.IsReloading || !gun.HasPrimaryAmmo()) )
		{
			ClearTacticalMove();
			return false;
		}
		if ( _tacticalMove )
		{
			if ( !Perception.TargetVisible && Time.Now - Perception.LastSeenAt > TacticalSightLossBudget )
			{
				ClearTacticalMove(); StopMoving(); _requestContactRecovery = true;
				return false;
			}
			if ( target != _tacticalTarget || Time.Now >= _tacticalDeadline || ReachedDestination()
				|| !MovementProgressing() || Vector3.DistanceBetween( _tacticalThreat, Perception.LastKnownPosition ) > 400f
				|| !Coordination.RequestManeuver( this, target ) )
			{
				ClearTacticalMove(); StopMoving();
				return false;
			}
			State = _flanking ? VBSoldierState.Flank : VBSoldierState.Reposition;
			return true;
		}
		if ( Time.Now < _nextTacticalQuery || Time.Now < _holdPositionUntil || !supporting && FireReady > 0f ) return false;
		_nextTacticalQuery = Time.Now + RandomRange( 2f, 3.5f );
		if ( !Coordination.RequestManeuver( this, target ) ) return false;
		float best = TacticalPositionScore( WorldPosition, target, supporting, out _ ) + (supporting ? -20f : 30f);
		bool found = false, flank = false;
		Vector3 selected = default;
		var threat = Perception.LastKnownPosition;
		var radial = (WorldPosition - threat).WithZ( 0f ).Normal;
		float range = Math.Clamp( Vector3.DistanceBetween( threat, WorldPosition ), RetreatRange + 80f,
			MathF.Max( RetreatRange + 80f, MathF.Min( PreferredRange, gun.Npc.MaxRange ) ) );
		float radius = MathF.Min( MaxCombatPathLength, supporting ? MathF.Max( RepositionRadius, SupportRepositionRadius ) : RepositionRadius );
		float maxAngle = MathF.Asin( Math.Clamp( radius / (2f * range), 0f, 1f ) ) * (360f / MathF.PI);
		// Explicit lateral candidates plus native nav samples also discover nearby raised surfaces.
		for ( int i = 0; i < (supporting ? 16 : 12); i++ )
		{
			float angle = supporting ? 25f + i / 2 * 22f : i < 2 ? 25f : 50f;
			Vector3? point = i < (supporting ? 8 : 4)
				? Scene.NavMesh.GetClosestPoint( threat + Rotation.FromYaw( (i % 2 == 0 ? 1f : -1f)
					* MathF.Min( angle, maxAngle * 0.9f ) ) * radial * range, 96f )
				: Scene.NavMesh.GetRandomPoint( WorldPosition, radius );
			if ( !point.HasValue ) continue;
			float travel = Vector3.DistanceBetween( WorldPosition, point.Value );
			float distance = Vector3.DistanceBetween( threat, point.Value );
			if ( travel < 80f || travel > radius || distance < RetreatRange + 64f
				|| !PositionAvailable( point.Value, Agent.Radius * 4f )
				|| distance > MathF.Min( gun.Npc.MaxRange, gun.Ballistics.Range ) || !HasFiringLane( point.Value, target, standing: true ) ) continue;
			float score = TacticalPositionScore( point.Value, target, supporting, out bool candidateFlank ) - travel * (supporting ? 0.07f : 0.12f);
			if ( supporting ) score += (1f - Vector3.Dot( radial, (point.Value - threat).WithZ( 0f ).Normal )) * 220f;
			if ( score <= best ) continue;
			var path = Scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = WorldPosition, Target = point.Value, Agent = Agent } );
			if ( path.Status != NavMeshPathStatus.Complete || VBNpcNavigation.PathLength( path ) > MaxCombatPathLength
				|| VBNpcNavigation.PathLength( path ) > travel * 1.8f + 48f ) continue;
			best = score; selected = point.Value; flank = candidateFlank; found = true;
		}
		if ( !found || !TryMove( selected, allowTraversal: false ) ) { Coordination.ReleaseManeuver( this ); return false; }
		_tacticalMove = true; _flanking = flank; _supportRoute = supporting;
		_tacticalPosition = selected; _tacticalThreat = threat; _tacticalTarget = target;
		_tacticalDeadline = Time.Now + MathF.Max( 4f, radius / MathF.Max( MoveSpeed, 1f ) * 2.5f );
		State = flank ? VBSoldierState.Flank : VBSoldierState.Reposition;
		_combatReason = flank ? "short flank to a firing lane" : "local firing position";
		return true;
	}

	private float TacticalPositionScore( Vector3 point, GameObject target, bool supporting, out bool flanking )
	{
		float score = PositionScore( point, target, out flanking );
		// A support destination must permit re-entry into the fight. Hiding from
		// the target is useful along the route, not as the objective of a flank.
		if ( supporting && (IsProtected( point, target ) || UseCrouchCover && IsProtected( point, target, true )) ) score += 100f;
		return score;
	}

	// Reserve destinations, without strengthening physical crowd repulsion or excluding narrow passages.
	private bool PositionAvailable( Vector3 point, float spacing )
	{
		foreach ( var ally in Scene.GetAllComponents<VBSoldierController>() )
		{
			if ( ally == this || !ally.CanFight || Faction.IsHostileTo( ally.Faction ) ) continue;
			if ( ally._hasCover && Vector3.DistanceBetween( point, ally._coverPosition ) < spacing
				|| ally._tacticalMove && Vector3.DistanceBetween( point, ally._tacticalPosition ) < spacing ) return false;
		}
		return true;
	}

	private float OccupiedPositionPenalty( Vector3 point )
	{
		float penalty = 0f;
		foreach ( var ally in Scene.GetAllComponents<VBSoldierController>() )
		{
			if ( ally == this || !ally.CanFight || Faction.IsHostileTo( ally.Faction ) ) continue;
			var occupied = ally._hasCover ? ally._coverPosition : ally._tacticalMove ? ally._tacticalPosition : ally.WorldPosition;
			penalty += MathF.Max( 0f, AllySpacing - Vector3.DistanceBetween( point, occupied ) ) * 2f;
		}
		return penalty;
	}

	private float PositionScore( Vector3 point, GameObject target, out bool flanking )
	{
		float score = HasFiringLane( point, target ) ? 140f : 0f;
		score -= MathF.Abs( Vector3.DistanceBetween( point, Perception.LastKnownPosition ) - PreferredRange ) * 0.12f;
		// Height is only a small bonus, never a reason to choose an occluded/unreachable roof.
		score += Math.Clamp( point.z - Perception.LastKnownPosition.z, 0f, 160f ) * 0.5f;
		flanking = false;
		float bestAngle = 0f;
		foreach ( var ally in Scene.GetAllComponents<VBSoldierController>() )
		{
			if ( ally == this || !ally.CanFight || Faction.IsHostileTo( ally.Faction ) ) continue;
			var occupied = ally._tacticalMove ? ally._tacticalPosition : ally._hasCover ? ally._coverPosition : ally.WorldPosition;
			score -= MathF.Max( 0f, AllySpacing - Vector3.DistanceBetween( point, occupied ) ) * 2f;
			if ( ally.Perception.CurrentTarget != target || !ally.Perception.TargetVisible ) continue;
			var allyDirection = (ally.WorldPosition - Perception.LastKnownPosition).WithZ( 0f ).Normal;
			var candidateDirection = (point - Perception.LastKnownPosition).WithZ( 0f ).Normal;
			float separation = 1f - Vector3.Dot( allyDirection, candidateDirection );
			bestAngle = MathF.Max( bestAngle, MathF.Sqrt( Math.Clamp( separation, 0f, 1f ) ) * 250f );
			flanking |= separation > 0.18f;
		}
		return score + bestAngle;
	}

	private void ClearTacticalMove()
	{
		if ( _tacticalMove )
		{
			RestAtPosition();
			_nextTacticalQuery = Time.Now + MathF.Max( RepositionInterval, PositionHoldMin );
		}
		Coordination.ReleaseManeuver( this );
		_tacticalMove = _supportRoute = false; _tacticalTarget = null;
	}
}
