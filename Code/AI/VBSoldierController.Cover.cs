using System;
using Sandbox;
using Sandbox.Navigation;

public sealed partial class VBSoldierController
{
	[Property, Group( "Cover" )] public bool UseCover { get; set; } = true;
	[Property, Group( "Cover" )] public float CoverRadius { get; set; } = 500f;
	[Property, Group( "Cover" ), Range( 4, 32 )] public int CoverSamples { get; set; } = 24;
	[Property, Group( "Cover" )] public float CoverHoldTime { get; set; } = 5f;
	[Property, Group( "Cover" )] public float CoverPeekTime { get; set; } = 3f;
	[Property, Group( "Cover" )] public float CoverMemoryTime { get; set; } = 18f;
	[Property, Group( "Cover" )] public float SupportCoverHoldMin { get; set; } = 6f;
	[Property, Group( "Cover" )] public float SupportCoverHoldMax { get; set; } = 10f;
	[Property, Group( "Cover" )] public float SupportPeekTime { get; set; } = 1.2f;
	private Vector3 _coverPosition, _peekPosition, _coverThreat;
	private GameObject _coverTarget;
	private bool _hasCover, _peeking, _coverCrouched, _coverHasPeek, _peekHadFireSlot;
	private float _coverPhaseUntil, _coverDeadline, _nextCoverQuery, _seekCoverUntil;
	private (Vector3 Eye, GameObject Actor)[] _knownCoverThreats = Array.Empty<(Vector3, GameObject)>();

	private bool TickCover( GameObject target )
	{
		if ( !UseCover ) { ClearCover(); return false; }
		if ( !_hasCover && Time.Now < _resumeCoverAt ) return false;
		if ( _hasCover && (target != _coverTarget || Time.Now > _coverDeadline
			|| Vector3.DistanceBetween( Perception.LastKnownPosition, _coverThreat ) > 400f && !IsProtected( _coverPosition, target, _coverCrouched )
			|| !PositionAvailable( _coverPosition, Agent.Radius * 4f )) ) ClearCover();
		if ( !_hasCover && Time.Now >= _nextCoverQuery )
		{
			_nextCoverQuery = Time.Now + RandomRange( 2f, 3f );
			if ( FindCover( target ) )
			{
				ClearTacticalMove(); StopMoving(); _hasTraversalGoal = false;
				_hasCover = true; _coverTarget = target; _coverThreat = Perception.LastKnownPosition;
				_coverDeadline = Time.Now + RandomRange( 24f, 34f );
				if ( !_coverHasPeek ) _coverDeadline = Time.Now + 5f;
				_coverPhaseUntil = 0f; _peeking = false; _peekHadFireSlot = false;
			}
		}
		if ( !_hasCover ) return false;
		bool supporting = !HasFireSlot && VBSoldierCoordination.IsPlayerTarget( target );
		if ( _peeking && (Time.Now < _seekCoverUntil || Weapon.IsValid() && Weapon.IsReloading) )
		{ _peeking = false; _coverPhaseUntil = 0f; StopMoving(); }
		var destination = _peeking ? _peekPosition : _coverPosition;
		if ( Vector3.DistanceBetween( WorldPosition, destination ) > 24f )
		{
			State = _peeking ? VBSoldierState.Peek : VBSoldierState.TakeCover;
			if ( _hasDestination && !MovementProgressing() ) { ClearCover(); StopMoving(); return false; }
			// A cover route walks to its sheltered side; it never vaults over the shelter.
			if ( !_hasDestination && !TryMove( destination, allowTraversal: false ) ) { ClearCover(); return false; }
			if ( Perception.TargetVisible && Weapon.IsValid() && !Weapon.IsReloading ) TickFire( target );
			return true;
		}
		StopMoving();
		if ( !SetCrouching( !_peeking && _coverCrouched ) )
		{ _peeking = false; _coverPhaseUntil = Time.Now + 0.5f; }
		if ( !_peeking && !IsProtected( WorldPosition, target, _coverCrouched ) ) { ClearCover(); return false; }
		if ( !_peeking ) TryReloadWeapon( topUp: true, support: supporting );
		bool reload = Weapon.IsValid() && Weapon.IsReloading;
		State = _peeking ? VBSoldierState.Peek : reload ? VBSoldierState.Reload : VBSoldierState.HoldCover;
		// An observation peek becomes a deliberate firing window once a slot is granted.
		if ( _peeking && HasFireSlot && !_peekHadFireSlot )
		{ _peekHadFireSlot = true; _coverPhaseUntil = Time.Now + MathF.Max( 0.2f, CoverPeekTime ); }
		if ( _peeking && !TickCrouchedFire( target, fromCover: true ) ) TickFire( target );
		if ( _coverPhaseUntil == 0f ) _coverPhaseUntil = Time.Now + (_peeking ? CoverPeekTime
			: supporting ? RandomRange( SupportCoverHoldMin, SupportCoverHoldMax ) : MathF.Max( 0.2f, CoverHoldTime ) + RandomRange( 0f, 1f ));
		if ( _peeking && supporting ) _coverPhaseUntil = MathF.Min( _coverPhaseUntil, Time.Now + MathF.Max( 0.2f, SupportPeekTime ) );
		if ( Time.Now < _coverPhaseUntil || reload && !_peeking ) return true;
		if ( !_coverHasPeek )
		{
			// Deep cover need not offer a shooting lane. After recovering, try another angle.
			_hasCover = false;
			if ( TickTacticalMove( target, supporting: true ) ) { _coverTarget = null; return true; }
			_hasCover = true; SetCrouching( _coverCrouched );
			_coverPhaseUntil = Time.Now + RandomRange( SupportCoverHoldMin, SupportCoverHoldMax );
			return true;
		}
		_peeking = !_peeking;
		_peekHadFireSlot = false;
		if ( _peeking && Time.Now < _seekCoverUntil ) _seekCoverUntil = Time.Now;
		_coverPhaseUntil = 0f;
		// Standing in place is a valid peek over low cover.
		if ( Vector3.DistanceBetween( WorldPosition, _peeking ? _peekPosition : _coverPosition ) > 24f
			&& !TryMove( _peeking ? _peekPosition : _coverPosition, allowTraversal: false ) ) ClearCover();
		return _hasCover;
	}

	private void RememberCoverThreats( GameObject target )
	{
		var threats = new List<(Vector3 Eye, GameObject Actor)> { (Perception.LastKnownPosition + Vector3.Up * 60f, target) };
		foreach ( var health in Scene.GetAll<VBHealthComponent>() )
			if ( health.GameObject != target && Perception.CanSee( health.GameObject ) )
				threats.Add( (health.WorldPosition + Vector3.Up * 60f, health.GameObject) );
		_knownCoverThreats = threats.ToArray();
	}

	private Vector3? CoverSample( int index )
	{
		if ( index == 0 ) return Scene.NavMesh.GetClosestPoint( WorldPosition, 48f );
		if ( index <= 8 )
		{
			var direction = Rotation.FromYaw( index * 45f ) * Vector3.Forward;
			var from = WorldPosition + Vector3.Up * 24f;
			var obstacle = Scene.Trace.Ray( from, from + direction * CoverRadius )
				.IgnoreGameObjectHierarchy( GameObject ).IgnoreDynamic()
				.WithoutTags( new[] { "player", "npc", "npc_body", "npc_ragdoll", "trigger" } ).Run();
			if ( obstacle.Hit && !obstacle.StartedSolid && MathF.Abs( obstacle.Normal.z ) < 0.5f )
				return Scene.NavMesh.GetClosestPoint( (obstacle.EndPosition + obstacle.Normal * (Agent.Radius + 12f)).WithZ( WorldPosition.z ), 64f );
		}
		return Scene.NavMesh.GetRandomPoint( WorldPosition, CoverRadius );
	}

	private bool FindCover( GameObject target )
	{
		RememberCoverThreats( target );
		float best = float.PositiveInfinity;
		bool found = false;
		var side = Vector3.Cross( Perception.LastKnownPosition - WorldPosition, Vector3.Up ).WithZ( 0f ).Normal;
		for ( int i = 0; i < Math.Clamp( CoverSamples, 4, 32 ); i++ )
		{
			var sample = CoverSample( i );
			if ( !sample.HasValue ) continue;
			var point = sample.Value;
			if ( Vector3.DistanceBetween( point, WorldPosition ) > CoverRadius
				|| Vector3.DistanceBetween( point, Perception.LastKnownPosition ) < RetreatRange
				|| !PositionAvailable( point, Agent.Radius * 4f ) ) continue;
			bool crouched = !IsProtected( point, target );
			if ( crouched && (!UseCrouchCover || !IsProtected( point, target, true )) ) continue;
			// Consider all currently observed opponents, using their observations only.
			if ( _knownCoverThreats.Any( threat => !BlocksThreatFrom( threat.Eye, point + Vector3.Up * (crouched ? CrouchedHeight - 9f : _standingEyeOffset.z), threat.Actor ) ) ) continue;
			if ( Vector3.DistanceBetween( point, WorldPosition ) > 24f
				&& (!VBNpcNavigation.TryWalkPath( Scene, Agent, WorldPosition, point, out var coverPath )
					|| VBNpcNavigation.PathLength( coverPath ) > MaxCombatPathLength) ) continue;
			bool peekFound = HasFiringLane( point, target, standing: true );
			Vector3 peek = point;
			if ( !peekFound )
			{
				for ( int sign = -1; sign <= 1; sign += 2 )
				{
					var adjacent = Scene.NavMesh.GetClosestPoint( point + side * (sign * 90f), 64f );
					if ( !adjacent.HasValue || Vector3.DistanceBetween( adjacent.Value, point ) > 125f
						|| !PositionAvailable( adjacent.Value, Agent.Radius * 3f ) || !HasFiringLane( adjacent.Value, target, standing: true ) ) continue;
					var hull = new BBox( new Vector3( -Agent.Radius, -Agent.Radius, 4f ), new Vector3( Agent.Radius, Agent.Radius, Agent.Height ) );
					if ( Scene.Trace.Box( hull, point, adjacent.Value ).IgnoreGameObjectHierarchy( GameObject ).Run().Hit
						|| !VBNpcNavigation.TryWalkPath( Scene, Agent, point, adjacent.Value, out _ ) ) continue;
					peek = adjacent.Value; peekFound = true; break;
				}
			}
			// A shelter with no way to re-engage is a short emergency refuge,
			// not a standing assignment for a healthy support rifleman.
			if ( !peekFound && Time.Now >= _seekCoverUntil && !(Weapon.IsValid() && (Weapon.IsReloading || Weapon.Clip1 <= 0)) ) continue;
			float score = Vector3.DistanceBetween( WorldPosition, point )
				+ MathF.Abs( Vector3.DistanceBetween( point, Perception.LastKnownPosition ) - PreferredRange ) * 0.15f
				+ OccupiedPositionPenalty( point ) + (peekFound ? 0f : 80f);
			if ( score >= best ) continue;
			best = score; found = true;
			_coverPosition = point; _peekPosition = peek; _coverCrouched = crouched; _coverHasPeek = peekFound;
		}
		return found;
	}

	private bool IsProtected( Vector3 position, GameObject target, bool crouched = false ) =>
		BlocksThreat( position + Vector3.Up * (crouched ? CrouchedHeight * 0.5f : 40f), target )
		&& BlocksThreat( position + Vector3.Up * (crouched ? CrouchedHeight - 9f : _standingEyeOffset.z), target );
	private bool BlocksThreat( Vector3 position, GameObject target ) =>
		BlocksThreatFrom( Perception.LastKnownPosition + Vector3.Up * 60f, position, target );
	private bool BlocksThreatFrom( Vector3 origin, Vector3 position, GameObject target )
	{
		var trace = Scene.Trace.Ray( origin, position ).IgnoreGameObjectHierarchy( GameObject )
			.IgnoreGameObjectHierarchy( target ).UseHitboxes( true ).Run();
		return trace.Hit && !trace.StartedSolid && trace.GameObject.IsValid()
			&& !trace.GameObject.GetComponentInParent<VBHealthComponent>( includeSelf: true ).IsValid();
	}
	private bool HasFiringLane( Vector3 position, GameObject target, bool standing = false ) =>
		ClearShot( position + (standing ? _standingEyeOffset : Perception.EyeOffset), AimPosition, target );
	private bool ClearShot( Vector3 from, Vector3 to, GameObject target )
	{
		var trace = Scene.Trace.Ray( from, to ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes( true ).Run();
		return !trace.StartedSolid && (!trace.Hit || trace.GameObject.IsValid()
			&& trace.GameObject.GetComponentInParent<VBHealthComponent>( includeSelf: true )?.GameObject == target);
	}
	private void ClearCover()
	{
		if ( _hasCover ) _nextCoverQuery = MathF.Max( _nextCoverQuery, Time.Now + 4f );
		_hasCover = false; _coverTarget = null; _coverPhaseUntil = 0f; SetCrouching( false );
	}
}
