using System;
using Sandbox;
using Sandbox.Citizen;
using Sandbox.Navigation;

public enum VBSoldierState { Idle, Patrol, Advance, Search, TakeCover, HoldCover, Peek, Fire, Reload, ThrowGrenade, Ragdoll, Dead, Melee, Reposition, Flank, Evade, Support, Alert }

/// <summary>Cartel rifleman. Engine navigation, inventory, weapons and animation drive execution.</summary>
[Title( "Cartel Soldier" ), Category( "Void Breach/AI" ), Icon( "security" )]
public sealed partial class VBSoldierController : Component
{
	[RequireComponent] private NavMeshAgent Agent { get; set; }
	[RequireComponent] private VBHealthComponent Health { get; set; }
	[RequireComponent] private VBFactionComponent Faction { get; set; }
	[RequireComponent] private VBAiPerceptionComponent Perception { get; set; }
	[RequireComponent] private VBAiThreatComponent Threats { get; set; }
	[RequireComponent] private VBNpcRagdollController Ragdoll { get; set; }
	[RequireComponent] private VBWeaponInventoryComponent Inventory { get; set; }
	[RequireComponent] private VBNpcLocomotion Locomotion { get; set; }
	[Property, Group( "Weapon" )] public GameObject WeaponPrefab { get; set; }
	[Property, Group( "Weapon" ), Range( 0f, 8f ), Description( "NPC spread multiplier: larger means less accurate; 2 is the standard profile. Zero is perfect aim for debugging." )]
	public float AccuracyScale { get; set; } = 2f;
	[Property, Group( "Weapon" )] public int ReserveMagazines { get; set; } = 2;
	[Property, Group( "Movement" )] public float MoveSpeed { get; set; } = 115f;
	[Property, Group( "Movement" )] public float PatrolSpeed { get; set; } = 65f;
	[Property, Group( "Movement" )] public float SearchSpeed { get; set; } = 85f;
	[Property, Group( "Movement" )] public float TurnSpeed { get; set; } = 4f;
	[Property, Group( "Movement" )] public float PreferredRange { get; set; } = 600f;
	[Property, Group( "Movement" )] public float RetreatRange { get; set; } = 170f;
	[Property, Group( "Movement" )] public float PatrolRadius { get; set; } = 400f;
	[Property, Group( "Search" )] public float SearchDuration { get; set; } = 12f;
	[Property, Group( "Search" )] public float SearchRadius { get; set; } = 300f;
	[Property, Group( "Search" )] public int SearchPoints { get; set; } = 5;
	[Sync( SyncFlags.FromHost )] public VBSoldierState State { get; private set; }
	[Sync( SyncFlags.FromHost )] public Vector3 AimPosition { get; private set; }
	public VBCombatWeapon Weapon => Inventory.IsValid() ? Inventory.ActiveItem as VBCombatWeapon : null;
	public SkinnedModelRenderer BodyRenderer => _animation.IsValid() ? _animation.Target : null;
	public bool IsWeaponPoseActive => CanFight && State is not (VBSoldierState.ThrowGrenade or VBSoldierState.Melee)
		&& !Agent.IsTraversingLink && !IsDynamicallyTraversing;
	public bool CanFight => Active && Health.IsValid() && !Health.IsDead && !Health.IsDowned && !Ragdoll.IsRagdolled;
	private CitizenAnimationHelper _animation;
	private Vector3 _home, _visualVelocity, _searchOrigin, _destination, _progressPosition;
	private bool _searching, _approachingLastKnown, _hasDestination;
	private int _searchRemaining, _burstRemaining;
	private float _searchStarted, _searchApproachUntil, _nextRoam, _nextPath, _progressTime, _nextBurst;
	private string _lastPathStatus = "not requested";

	protected override void OnAwake()
	{
		PrepareInventory();
	}

	protected override void OnStart()
	{
		PrepareInventory();
		_animation = Components.Get<CitizenAnimationHelper>( FindMode.InSelf | FindMode.InChildren );
		// The Human placeholder needs its body meshes beneath the head.
		if ( BodyRenderer.IsValid() && !GetComponent<Dresser>().IsValid() )
		{
			BodyRenderer.SetBodyGroup( "Head", 0 );
			BodyRenderer.SetBodyGroup( "Chest", 0 );
			BodyRenderer.SetBodyGroup( "Legs", 0 );
			BodyRenderer.SetBodyGroup( "Hands", 0 );
			BodyRenderer.SetBodyGroup( "Feet", 0 );
		}
		_home = WorldPosition;
		Inventory.ManualPumping = true; // NPCs never consume the host player's Input.
		Faction.ConfigureDefaults( VBFactionComponent.CartelFaction, VBFactionComponent.PlayerFaction, VBFactionComponent.XenFaction );
		Agent.UpdateRotation = false;
		// Subscribe on every peer so a future host receives health events too.
		Health.OnDamaged += OnDamaged;
		Health.OnDeath += OnDied;
		if ( !Networking.IsHost ) return;
		Agent.MaxSpeed = MoveSpeed;
		Agent.Acceleration = MathF.Max( 1f, MovementAcceleration );
		InitializeEquipment();
		if ( GrenadeAmmo is not null ) Inventory.GiveAmmo( GrenadeAmmo, Math.Max( 0, GrenadeCount ) );
		_nextGrenade = MathF.Min( GrenadeCooldown, InitialGrenadeDelay );
	}

	protected override void OnUpdate()
	{
		UpdateStance();
		UpdateAnimation();
		UpdateGrenadePresentation();
		if ( !Networking.IsHost ) return;
		if ( !CanFight )
		{
			State = Health.IsDead ? VBSoldierState.Dead : VBSoldierState.Ragdoll;
			CancelCombat();
			return;
		}
		TickSquadContacts();
		UpdateEngagementContext();
		if ( Agent.IsTraversingLink || IsDynamicallyTraversing || !Locomotion.IsGrounded ) { ReleaseFireSlot(); return; }
		if ( TickSurvival() ) return;
		if ( TickAlertOrientation() ) return;
		if ( TickContactRecovery() ) return;
		if ( _searching && !Perception.TargetVisible && !InvestigationPermitted( Perception.CurrentTarget ) ) return;
		if ( Locomotion.IsMovingDirectly )
		{
			bool patrolClue = Perception.HasInvestigationPoint && State is VBSoldierState.Patrol or VBSoldierState.Idle;
			if ( !Perception.TargetVisible && !patrolClue || !_searching && State is not (VBSoldierState.Patrol or VBSoldierState.Idle or VBSoldierState.Alert) ) return;
			StopMoving(); // A local patrol/search walk can immediately react to a newly visible threat.
		}
		// Reassess a pending approach before committing to a climb, while preserving useful routes.
		if ( _hasTraversalGoal && Weapon.IsValid() && Perception.TargetVisible && Perception.IsViableTarget( Perception.CurrentTarget ) )
		{
			UpdateCombatAim( Perception.CurrentTarget );
			UpdateFirePermission( Perception.CurrentTarget );
			if ( !HasFireSlot && VBSoldierCoordination.IsPlayerTarget( Perception.CurrentTarget ) )
			{ _hasTraversalGoal = false; StopMoving(); }
			else if ( TickCover( Perception.CurrentTarget ) ) return;
		}
		if ( TickPendingTraversal() ) return;
		if ( State == VBSoldierState.ThrowGrenade ) { TickGrenade(); return; }
		if ( State == VBSoldierState.Melee ) { TickMelee(); return; }
		var target = Perception.CurrentTarget;
		if ( IsInFiringStance && (!Perception.TargetVisible || !Perception.IsViableTarget( target ) || target != _firingStanceTarget) ) EndCrouchedFire();
		if ( !Perception.TargetVisible )
		{
			ReleaseFireSlot();
			// A short occlusion during a peek does not mean forgetting how to aim.
			if ( !RemembersTarget( target ) ) _aimTarget = null;
			_burstRemaining = 0;
		}
		if ( Perception.IsViableTarget( target ) && Perception.TargetVisible )
		{
			Coordination.ReleaseInvestigation( this );
			Agent.MaxSpeed = MathF.Max( 0f, MoveSpeed );
			_searching = _approachingLastKnown = false;
			UpdateCombatAim( target );
			FaceCombatAim();
			if ( TryBeginMelee( target ) ) return;
			SelectCombatWeapon();
			TickCombat( target );
			return;
		}
		if ( TryBeginGrenade() ) return;
		// Stay committed to a known cover/peek route across a brief occlusion.
		if ( _hasCover && Perception.IsViableTarget( target ) && Time.Now - Perception.LastSeenAt <= CoverMemoryTime )
		{
			FaceCombatAim();
			SelectCombatWeapon();
			if ( TickCover( target ) ) return;
		}
		if ( _tacticalMove && Perception.IsViableTarget( target ) && Time.Now - Perception.LastSeenAt <= SupportMemoryTime )
		{
			FaceCombatAim();
			if ( TickTacticalMove( target, supporting: _supportRoute ) ) return;
		}
		if ( TickLostContactHold( target ) ) return;
		if ( Perception.IsViableTarget( target ) && Weapon.IsValid() && (UseCover || UseTacticalPositions && MoveSpeed > 0f)
			&& Time.Now - Perception.LastSeenAt <= SupportMemoryTime
			&& State is VBSoldierState.Reposition or VBSoldierState.Flank or VBSoldierState.Support or VBSoldierState.HoldCover or VBSoldierState.TakeCover or VBSoldierState.Reload )
		{
			FaceCombatAim();
			if ( TickSupport( target ) ) return;
		}
		ClearCover(); ClearTacticalMove();
		_burstRemaining = 0;
		SelectCombatWeapon();
		TryReloadWeapon();
		if ( !InvestigationPermitted( target ) ) return;
		if ( (!_searching || Vector3.DistanceBetween( _searchOrigin, Perception.LastKnownPosition ) > 48f)
			&& (Perception.IsViableTarget( target ) || Perception.HasInvestigationPoint) )
		{
			_searching = _approachingLastKnown = true;
			_searchOrigin = Perception.LastKnownPosition;
			_searchRemaining = Math.Max( 1, SearchPoints );
			_searchStarted = Time.Now;
			_searchApproachUntil = Time.Now + Math.Clamp( Vector3.DistanceBetween( WorldPosition, _searchOrigin )
				/ MathF.Max( SearchSpeed, 1f ) * 2f + SearchDuration, 8f, 45f );
			StopMoving();
			_nextRoam = Time.Now;
			_nextPath = Time.Now;
			Perception.ConsumeInvestigationPoint();
		}
		TickRoam();
	}

	private void TickCombat( GameObject target )
	{
		UpdateFirePermission( target );
		var weapon = Weapon;
		float distance = Vector3.DistanceBetween( WorldPosition, Perception.LastKnownPosition );
		if ( !weapon.IsValid() )
		{
			State = VBSoldierState.Advance;
			// Stop at punching distance while turning or waiting for an attack.
			// Navigating to the actor's centre keeps pushing through its collider.
			if ( !MeleeWeapon.IsValid() ) { StopMoving(); return; }
			float reach = MeleeWeapon.Ballistics.Range * 0.7f;
			var delta = (Perception.LastKnownPosition - WorldPosition).WithZ( 0f );
			if ( delta.Length <= reach ) StopMoving();
			else if ( Time.Now >= _nextPath ) TryMove( Perception.LastKnownPosition - delta.Normal * reach );
			return;
		}
		if ( distance < PersonalSpaceRange )
		{
			EndCrouchedFire();
			ClearCover(); ClearTacticalMove();
			State = VBSoldierState.Advance;
			var away = (WorldPosition - Perception.LastKnownPosition).WithZ( 0f ).Normal;
			if ( Time.Now >= _nextPath && !_hasDestination ) TryMove( WorldPosition + away * 96f, allowTraversal: false );
			TryReloadWeapon();
			TickFire( target );
			return;
		}
		if ( IsInFiringStance && TickCrouchedFire( target ) ) return;
		if ( TryBeginGrenade() ) return;
		// Acquiring a contact is not a reason for the entire squad to run off
		// before anyone has finished aiming.
		if ( FireReady > 0f && !_hasCover && !_tacticalMove && Time.Now >= _seekCoverUntil )
		{ StopMoving(); State = VBSoldierState.Alert; return; }
		if ( TickSupport( target ) ) return;
		if ( _tacticalMove && TickTacticalMove( target, supporting: _supportRoute ) ) { TickFire( target ); return; }
		// Keep a useful firing line. Voluntary cover/flank queries previously
		// pre-empted this decision, making the crouched firing role almost unreachable.
		if ( !_hasCover && Time.Now >= _seekCoverUntil && weapon.Clip1 > 0 && !weapon.IsReloading
			&& distance <= MathF.Min( PreferredRange, weapon.Npc.MaxRange ) && HasFiringLane( WorldPosition, target ) )
		{
			StopMoving(); State = VBSoldierState.Fire;
			_combatReason = "hold a clear firing line";
			if ( !TickCrouchedFire( target ) ) TickFire( target );
			return;
		}
		// Geometry that already protects us is more useful than climbing towards the enemy.
		if ( TickCover( target ) ) return;
		if ( Perception.LastKnownPosition.z - WorldPosition.z > 32f )
		{
			bool moving = _hasDestination;
			if ( Time.Now >= _nextPath ) moving = TryMove( Perception.LastKnownPosition );
			if ( moving ) { State = VBSoldierState.Advance; TickFire( target ); return; }
		}
		if ( TickTacticalMove( target ) ) { TickFire( target ); return; }
		// If no usable cover exists, remain vulnerable and use the normal reload timer here.
		TryReloadWeapon();
		if ( weapon.IsValid() && weapon.IsReloading ) { State = VBSoldierState.Reload; StopMoving(); return; }
		float range = weapon.IsValid() ? MathF.Min( PreferredRange, weapon.Npc.MaxRange ) : PreferredRange;
		if ( distance > range || !HasFiringLane( WorldPosition, target ) )
		{
			State = VBSoldierState.Advance;
			if ( Time.Now >= _nextPath ) TryMove( Perception.LastKnownPosition );
		}
		else { StopMoving(); State = VBSoldierState.Fire; if ( TickCrouchedFire( target ) ) return; }
		TickFire( target );
	}

	private void TickFire( GameObject target )
	{
		var weapon = Weapon;
		if ( !weapon.IsValid() || !weapon.IsActive || weapon.IsReloading || !Perception.TargetVisible
			|| !Perception.IsViableTarget( target ) || FireReady > 0f || Time.Now < _nextBurst ) return;
		float distance = Vector3.DistanceBetween( WorldPosition, Perception.LastKnownPosition );
		if ( distance < weapon.Npc.MinRange || distance > MathF.Min( weapon.Npc.MaxRange, weapon.Ballistics.Range ) ) return;
		var direction = (AimPosition - WorldPosition).WithZ( 0f ).Normal;
		if ( Vector3.Dot( WorldRotation.Forward, direction ) < 0.92f || !HasFiringLane( WorldPosition, target ) ) return;
		// Check the actual barrel too: an eye above a crate does not permit shooting through it.
		var muzzle = weapon.GetMuzzleTransform().Position;
		if ( !ClearShot( muzzle, AimPosition, target ) ) return;
		// Every firing path (including peeks, retreat and survival) passes this host budget.
		if ( !UpdateFirePermission( target ) ) return;
		if ( _burstRemaining <= 0 )
		{
			int max = Math.Clamp( weapon.Npc.BurstMax, 1, Math.Max( 1, BurstShotLimit ) );
			_burstRemaining = Game.Random.Next( Math.Clamp( weapon.Npc.BurstMin, 1, max ), max + 1 );
		}
		if ( !weapon.FirePrimary() ) return;
		if ( --_burstRemaining <= 0 )
		{
			float min = UseWeaponBurstTiming ? weapon.Npc.RestMin : MathF.Max( BurstRestMin, weapon.Npc.RestMin );
			float max = UseWeaponBurstTiming ? weapon.Npc.RestMax : MathF.Max( BurstRestMax, weapon.Npc.RestMax );
			_nextBurst = Time.Now + RandomRange( MathF.Max( 0.1f, min ), MathF.Max( 0.1f, max ) );
		}
	}

	private void TickRoam()
	{
		Agent.MaxSpeed = MathF.Max( 0f, MathF.Min( MoveSpeed, _searching ? SearchSpeed : PatrolSpeed ) );
		if ( _searching && _approachingLastKnown )
		{
			State = VBSoldierState.Search;
			// Reach the recorded point before searching its surroundings. A failed
			// route is retried while the NavMesh builds, never replaced by random patrol.
			bool arrived = Vector3.DistanceBetween( WorldPosition, _searchOrigin ) < 48f
				|| _hasDestination && ReachedDestination();
			if ( !arrived && Time.Now < _searchApproachUntil )
			{
				if ( _hasDestination && !MovementProgressing() ) StopMoving();
				if ( !_hasDestination && Time.Now >= _nextPath ) TryMove( _searchOrigin );
				if ( RemembersTarget( Perception.CurrentTarget ) ) Face( AimPosition - WorldPosition );
				else Face( _hasDestination ? Agent.WishVelocity : _searchOrigin - WorldPosition );
				return;
			}
			_approachingLastKnown = false;
			_searchStarted = Time.Now;
			if ( !RemembersTarget( Perception.CurrentTarget ) ) Perception.ForgetTarget();
			StopMoving();
			_nextRoam = Time.Now + RandomRange( 2f, 4f );
		}
		if ( _searching && (Time.Now - _searchStarted >= SearchDuration || _searchRemaining <= 0 && !_hasDestination) )
		{
			_searching = _approachingLastKnown = false;
			Coordination.ReleaseInvestigation( this );
			StopMoving();
		}
		State = _searching ? VBSoldierState.Search : VBSoldierState.Patrol;
		if ( _hasDestination )
		{
			Face( Agent.WishVelocity );
			if ( !ReachedDestination() && MovementProgressing() ) return;
			StopMoving();
			_nextRoam = Time.Now + (_searching ? RandomRange( 2f, 4f ) : RandomRange( 3f, 6f ));
		}
		if ( Time.Now < _nextRoam ) { if ( !_searching ) State = VBSoldierState.Idle; WatchOpenSector(); return; }
		_nextRoam = Time.Now + 1f;
		float radius = _searching ? SearchRadius : PatrolRadius;
		if ( radius <= 0f ) { State = VBSoldierState.Idle; WatchOpenSector(); return; }
		if ( _searching ) _searchRemaining--;
		for ( int i = 0; i < 4; i++ )
		{
			var point = Scene.NavMesh.GetRandomPoint( _searching ? _searchOrigin : _home, radius );
			// Idle patrol does not choose a lower floor merely because it lies in
			// the sampling sphere. Combat and investigation may deliberately descend.
			if ( point.HasValue && (_searching || MathF.Abs( point.Value.z - WorldPosition.z ) <= Locomotion.Body.StepHeight )
				&& TryMove( point.Value, allowTraversal: _searching ) ) return;
		}
		if ( !_searching && !VBNpcNavigation.HasLocalMesh( Scene, WorldPosition ) )
		{
			// A small platform may have no baked polygon. A short supported walk
			// still works, using the existing physical mover and shared body sweeps.
			for ( int i = 0; i < 4; i++ )
			{
				var sample = _home + Rotation.FromYaw( RandomRange( -180f, 180f ) ) * Vector3.Forward * RandomRange( 24f, MathF.Min( radius, 120f ) );
				var floor = TraversalGeometry.StaticRay( sample + Vector3.Up * Locomotion.Body.StepHeight,
					sample - Vector3.Up * Locomotion.Body.StepHeight );
				if ( TraversalGeometry.IsLanding( floor ) && TryMove( floor.EndPosition + Vector3.Up * VBTraversalGeometry.Skin, allowTraversal: false ) ) return;
			}
		}
		WatchOpenSector();
	}

	private bool TryMove( Vector3 destination, bool allowTraversal = true )
	{
		if ( IsInFiringStance ) EndCrouchedFire();
		if ( !SetCrouching( false ) ) return false;
		_nextPath = Time.Now + 0.4f;
		bool complete = VBNpcNavigation.TryWalkPath( Scene, Agent, WorldPosition, destination, out var path );
		if ( !complete && VBNpcNavigation.HasLocalMesh( Scene, WorldPosition ) )
			path = Scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = WorldPosition, Target = destination, Agent = Agent } );
		_lastPathStatus = path.Status.ToString();
		if ( allowTraversal && TryDynamicRoute( destination, path, complete ) ) { _watchingSector = false; return true; }
		if ( !complete && MathF.Abs( destination.z - WorldPosition.z ) < 20f
			&& Vector3.DistanceBetween( destination, WorldPosition ) < 160f
			&& TraversalGeometry.LandingClear( destination ) && TraversalGeometry.PathClear( WorldPosition, destination ) )
		{
			Locomotion.WalkDirectlyTo( destination );
			_watchingSector = false;
			_destination = destination; _hasDestination = true;
			_progressPosition = WorldPosition; _progressTime = Time.Now;
			return true;
		}
		if ( !complete ) return false;
		_watchingSector = false;
		Agent.SetPath( path );
		_destination = path.Points.Last().Position;
		_hasDestination = true;
		_progressPosition = WorldPosition;
		_progressTime = Time.Now;
		return true;
	}

	private bool ReachedDestination() => Vector3.DistanceBetween( WorldPosition, _destination ) < 28f;
	private bool MovementProgressing()
	{
		if ( Vector3.DistanceBetween( WorldPosition, _progressPosition ) > 8f ) { _progressPosition = WorldPosition; _progressTime = Time.Now; }
		return Agent.IsNavigating && Time.Now - _progressTime < 3f;
	}
	private void StopMoving() { if ( !Agent.IsTraversingLink ) Agent.Stop(); Locomotion.CancelDirectMove(); _hasDestination = false; }
	private void Face( Vector3 direction )
	{
		if ( direction.WithZ( 0f ).IsNearZeroLength ) return;
		float yaw = WorldRotation.Yaw();
		float difference = MathX.DeltaDegrees( yaw, Rotation.LookAt( direction.WithZ( 0f ).Normal ).Yaw() );
		if ( _turnFrameTime != Time.Now ) { _turnFrameTime = Time.Now; _turnUsed = 0f; }
		float limit = MathF.Max( 0f, MathF.Max( 0f, MaximumTurnRate ) * Time.Delta - _turnUsed );
		float turn = Math.Clamp( difference * Math.Clamp( TurnSpeed * Time.Delta, 0f, 1f ), -limit, limit );
		_turnUsed += MathF.Abs( turn );
		WorldRotation = Rotation.FromYaw( yaw + turn );
	}
	private static float RandomRange( float min, float max ) => MathF.Min( min, max ) + Game.Random.NextSingle() * MathF.Abs( max - min );

	private void UpdateAnimation()
	{
		var velocity = Locomotion.Velocity;
		if ( !_animation.IsValid() || Ragdoll.IsRagdolled ) return;
		_visualVelocity = Vector3.Lerp( _visualVelocity, velocity, Math.Clamp( Time.Delta * 12f, 0f, 1f ) );
		_animation.WithVelocity( _visualVelocity );
		_animation.WithWishVelocity( _visualVelocity.WithZ( 0f ) );
		UpdateTurnAnimation();
		_animation.MoveStyle = CitizenAnimationHelper.MoveStyles.Auto;
		// Human uses named hold types, like the player weapons. Do not overwrite
		// melee_punch with the Citizen helper's older numeric Punch enum.
		var heldGun = Weapon;
		bool punching = MeleeWeapon.IsValid() && Inventory.ActiveItem == MeleeWeapon
			&& (State == VBSoldierState.Melee || MeleeWeapon.IsPunchPoseActive);
		BodyRenderer?.Set( "holdtype", State == VBSoldierState.ThrowGrenade ? "holditem"
			: punching ? MeleeWeapon.HoldType : heldGun.IsValid() ? heldGun.HoldType : "none" );
		if ( State == VBSoldierState.ThrowGrenade ) BodyRenderer?.Set( "holdtype_handedness", 1 );
		_animation.IsGrounded = Locomotion.IsGrounded && !Agent.IsTraversingLink && !IsDynamicallyTraversing;
		BodyRenderer?.Set( "b_climb", IsDynamicallyTraversing || (GetComponent<VBNavLinkTraversal>()?.IsClimbing ?? false) );
		UpdateWeaponHands();
		bool rememberedAim = _aimTarget.IsValid() && State is not (VBSoldierState.Patrol or VBSoldierState.Idle or VBSoldierState.Dead);
		var look = State == VBSoldierState.ThrowGrenade ? _grenadeTarget : AimPosition;
		_animation.WithLook( rememberedAim || State is VBSoldierState.ThrowGrenade or VBSoldierState.Alert
			? (look - (WorldPosition + Perception.EyeOffset)).Normal : WorldRotation.Forward );
	}

	internal void UpdateWeaponHands()
	{
		if ( !_animation.IsValid() || !BodyRenderer.IsValid() ) return;
		var gun = Weapon;
		var hand = IsWeaponPoseActive && gun.IsValid() && !gun.IsReloading ? gun.NpcSupportHandTarget : null;
		_animation.IkLeftHand = hand;
		if ( hand.IsValid() ) BodyRenderer.SetIk( "hand_left", hand.WorldTransform );
		else BodyRenderer.ClearIk( "hand_left" );
	}

	private void OnDamaged( VBDamageInfo info ) { if ( Networking.IsHost ) _seekCoverUntil = Time.Now + 5f; }
	private void OnDied()
	{
		if ( !Networking.IsHost ) return;
		CancelCombat();
		State = VBSoldierState.Dead;
		foreach ( var gun in Inventory.Items.OfType<VBCombatWeapon>().ToArray() ) Inventory.Drop( gun );
	}
	private void CancelCombat()
	{
		EndCrouchedFire();
		EndContactRecovery();
		Coordination.ReleaseInvestigation( this );
		_searching = _approachingLastKnown = false;
		ReleaseFireSlot();
		StopMoving(); ClearCover(); ClearTacticalMove(); _burstRemaining = 0;
		_hasTraversalGoal = false;
		_aimTarget = null;
		if ( Weapon.IsValid() && Weapon.IsReloading ) Weapon.CancelReload();
		_pendingContact = null; _alertObserveUntil = 0f; _watchingSector = false;
		_grenadeRelease = 0f;
		MeleeTarget = null;
		_meleePending = false;
	}
	protected override void OnDisabled()
	{
		if ( Locomotion.IsValid() ) Locomotion.CancelDirectMove();
		if ( _heldGrenade.IsValid() ) _heldGrenade.Destroy();
		if ( _animation.IsValid() ) _animation.IkLeftHand = null;
		BodyRenderer?.ClearIk( "hand_left" );
		if ( Networking.IsHost && Agent.IsValid() ) CancelCombat();
		if ( Networking.IsHost && IsDynamicallyTraversing ) EndDynamicTraversal( false );
	}
	protected override void OnDestroy()
	{
		if ( Networking.IsHost ) ReleaseFireSlot();
		if ( !Health.IsValid() ) return;
		Health.OnDamaged -= OnDamaged; Health.OnDeath -= OnDied;
	}
}
