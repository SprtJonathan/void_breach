using System;
using Sandbox;
using Sandbox.Citizen;

public enum VBZombieState
{
	Idle,
	Patrol,
	Alert,
	Chase,
	Search,
	Attack,
	Ragdoll,
	Dead
}

public enum VBZombieAttackPhase
{
	None,
	Windup,
	Strike,
	Recovery
}

/// <summary>
/// Host-authoritative zombie behaviour. Perception and faction relationships
/// are delegated to reusable components so other NPC archetypes can share them.
/// </summary>
[Title( "Zombie Controller" )]
[Category( "Void Breach/AI" )]
[Icon( "psychology" )]
public sealed class VBZombieController : Component
{
	[RequireComponent] private NavMeshAgent Agent { get; set; }
	[RequireComponent] private VBHealthComponent Health { get; set; }
	[RequireComponent] private VBNpcRagdollController Ragdoll { get; set; }
	[RequireComponent] private VBAiPerceptionComponent Perception { get; set; }
	[RequireComponent] private VBFactionComponent Faction { get; set; }
	[RequireComponent] private VBNpcLocomotion Locomotion { get; set; }

	[Property, Group( "Locomotion" ), Range( 10f, 1000f )] public float MoveSpeed { get; set; } = 105f;
	[Property, Group( "Locomotion" ), Range( 10f, 3000f )] public float Acceleration { get; set; } = 500f;
	[Property, Group( "Locomotion" ), Range( 1f, 30f )] public float TurnSpeed { get; set; } = 7f;
	[Property, Group( "Locomotion" ), Range( 0.1f, 2f )] public float PathRefreshInterval { get; set; } = 0.35f;

	[Property, Group( "Patrol" ), Range( 0f, 3000f )] public float PatrolRadius { get; set; } = 500f;
	[Property, Group( "Patrol" )] public Vector2 PatrolWaitRange { get; set; } = new( 0.6f, 2.2f );
	[Property, Group( "Patrol" ), Range( 8f, 200f )] public float PatrolArrivalDistance { get; set; } = 40f;
	[Property, Group( "Patrol" ), Range( 0f, 1000f )] public float PatrolMinimumMoveDistance { get; set; } = 100f;
	[Property, Group( "Patrol" ), Range( 0.5f, 10f )] public float PatrolStuckTimeout { get; set; } = 2.5f;
	[Property, Group( "Patrol" ), Range( 1f, 100f )] public float PatrolProgressDistance { get; set; } = 8f;
	[Property, Group( "Patrol" ), Range( 1, 12 )] public int PatrolPointAttempts { get; set; } = 5;
	[Property, Group( "Patrol" ), Range( 0.05f, 5f )] public float PatrolRetryDelay { get; set; } = 0.4f;
	[Property, Group( "Patrol" )]
	[Description( "Après une recherche infructueuse, recentre le roam autour du dernier endroit où la cible a été perçue." )]
	public bool ReanchorPatrolAfterSearch { get; set; } = true;

	[Property, Group( "Search" ), Range( 32f, 1500f )] public float SearchRadius { get; set; } = 260f;
	[Property, Group( "Search" ), Range( 1, 12 )] public int SearchPointCount { get; set; } = 4;
	[Property, Group( "Search" ), Range( 1f, 30f )] public float SearchDuration { get; set; } = 9f;
	[Property, Group( "Search" ), Range( 8f, 200f )] public float SearchArrivalDistance { get; set; } = 45f;
	[Property, Group( "Search" ), Range( 1f, 30f )] public float LostTargetChaseTimeout { get; set; } = 10f;
	[Property, Group( "Search" )] public Vector2 SearchWaitRange { get; set; } = new( 0.4f, 1.2f );

	[Property, Group( "Melee" ), Range( 1f, 300f )] public float AttackRange { get; set; } = 64f;
	[Property, Group( "Melee" ), Range( 0f, 200f )] public float AttackDamage { get; set; } = 18f;
	/// <summary>Minimum time between the beginnings of two attacks.</summary>
	[Property, Group( "Melee" ), Range( 0.05f, 5f )] public float AttackCooldown { get; set; } = 1.6f;
	[Property, Group( "Melee" ), Range( 0f, 2f )] public float AttackWindup { get; set; } = 0.2f;
	/// <summary>Time from triggering the punch animation to checking its impact.</summary>
	[Property, Group( "Melee" ), Range( 0f, 1f )] public float AttackImpactDelay { get; set; } = 0.18f;
	[Property, Group( "Melee" ), Range( 0f, 2f )] public float AttackRecovery { get; set; } = 0.65f;
	[Property, Group( "Melee" ), Range( 0f, 180f )] public float AttackHalfAngle { get; set; } = 55f;

	[Property, Group( "Audio" )] public SoundEvent AlertSound { get; set; }
	[Property, Group( "Audio" )] public SoundEvent AttackSound { get; set; }
	[Property, Group( "Audio" )] public SoundEvent AttackHitSound { get; set; }
	[Property, Group( "Audio" )] public SoundEvent AttackMissSound { get; set; }
	[Property, Group( "Animation" )] public string AttackAnimationParameter { get; set; } = "b_attack";

	[Sync( SyncFlags.FromHost )] public VBZombieState State { get; private set; } = VBZombieState.Idle;
	[Sync( SyncFlags.FromHost )] public VBZombieAttackPhase AttackPhase { get; private set; }
	[Sync( SyncFlags.FromHost )] private TimeSince TimeSinceAttack { get; set; } = 999f;
	[Sync( SyncFlags.FromHost )] private GameObject AttackTarget { get; set; }

	public GameObject Target => Perception.IsValid() ? Perception.CurrentTarget : null;
	public Vector3 LastKnownTargetPosition => Perception.IsValid() ? Perception.LastKnownPosition : WorldPosition;

	private CitizenAnimationHelper _animationHelper;
	private SkinnedModelRenderer _renderer;
	private Vector3 _visualVelocity;
	private Vector3 _searchOrigin;
	private Vector3 _searchDestination;
	private Vector3 _patrolDestination;
	private Vector3 _patrolProgressPosition;
	private Vector3 _patrolOrigin;
	private TimeSince _timeSincePathRefresh;
	private TimeSince _timeSinceSearchStarted;
	private TimeSince _timeSinceSearchWait;
	private TimeSince _timeSincePatrolWait;
	private TimeSince _timeSincePatrolProgress;
	private float _searchWaitDuration;
	private float _patrolWaitDuration;
	private int _searchPointsRemaining;
	private bool _isSearching;
	private bool _hasPatrolDestination;
	private bool AttackInProgress => AttackPhase != VBZombieAttackPhase.None;
	private GameObject _lastAlertedTarget;
	private Vector3 _chaseGoal, _chaseProgress;
	private float _chaseProgressTime;

	protected override void OnStart()
	{
		ResolveVisuals();
		_patrolOrigin = WorldPosition;
		Agent.MaxSpeed = MoveSpeed;
		Agent.Acceleration = Acceleration;
		Agent.UpdateRotation = false;
		Faction.ConfigureDefaults(
			VBFactionComponent.XenFaction,
			VBFactionComponent.PlayerFaction,
			VBFactionComponent.CartelFaction
		);
		GameObject.Tags.Add( "zombie" );

		if ( GameObject.NetworkMode != NetworkMode.Object )
			Log.Warning( $"[VBZombieController] '{GameObject.Name}' doit être en NetworkMode.Object pour répliquer son mouvement et ses états." );

		Health.OnDeath += OnDied;
	}

	protected override void OnUpdate()
	{
		ResolveVisuals();
		UpdateAnimation();

		if ( !Networking.IsHost )
			return;

		if ( Health.IsDead )
		{
			CancelAttack();
			SetState( VBZombieState.Dead );
			StopAgentUnlessTraversingLink();
			return;
		}

		if ( Ragdoll.IsRagdolled )
		{
			CancelAttack();
			SetState( VBZombieState.Ragdoll );
			StopAgentUnlessTraversingLink();
			return;
		}
		if ( Agent.IsTraversingLink || !Locomotion.IsGrounded ) return;

		// An engaged attack cannot chase, turn or switch victims halfway through.
		if ( AttackInProgress )
		{
			TickAttackSequence();
			return;
		}

		TickBehaviour();
	}

	protected override void OnDisabled()
	{
		if ( !Networking.IsHost ) return;
		CancelAttack();
		if ( Agent.IsValid() ) StopAgentUnlessTraversingLink();
	}

	protected override void OnDestroy()
	{
		if ( Health.IsValid() )
			Health.OnDeath -= OnDied;
	}

	private void ResolveVisuals()
	{
		_animationHelper ??= Components.Get<CitizenAnimationHelper>( FindMode.InSelf | FindMode.InChildren );
		_renderer = _animationHelper.IsValid() && _animationHelper.Target.IsValid()
			? _animationHelper.Target
			: Components.Get<SkinnedModelRenderer>( FindMode.InSelf | FindMode.InChildren );
	}

	private void TickBehaviour()
	{
		var target = Perception.CurrentTarget;
		if ( Perception.IsViableTarget( target ) )
		{
			_hasPatrolDestination = false;

			if ( target != _lastAlertedTarget )
			{
				_lastAlertedTarget = target;
				CancelSearch();
				SetState( VBZombieState.Alert );
				PlayAlertSound();
			}

			if ( Perception.TargetVisible )
			{
				CancelSearch();
				float distance = Vector3.DistanceBetween( WorldPosition, target.WorldPosition );
				if ( distance <= AttackRange )
				{
					TickAttack( target );
					return;
				}

				TickChase( target.WorldPosition );
				return;
			}

			float distanceToLastKnown = Vector3.DistanceBetween( WorldPosition, Perception.LastKnownPosition );
			if ( distanceToLastKnown <= SearchArrivalDistance
				|| Perception.TimeSinceTargetSensed >= LostTargetChaseTimeout )
			{
				BeginSearch( Perception.LastKnownPosition );
				Perception.ForgetTarget();
				Perception.ConsumeInvestigationPoint();
				TickSearch();
				return;
			}

			TickChase( Perception.LastKnownPosition );
			return;
		}

		if ( _isSearching )
		{
			TickSearch();
			return;
		}

		if ( Perception.HasInvestigationPoint )
		{
			BeginSearch( Perception.LastKnownPosition );
			Perception.ConsumeInvestigationPoint();
			TickSearch();
			return;
		}

		_lastAlertedTarget = null;
		TickPatrol();
	}

	private void TickChase( Vector3 position )
	{
		_hasPatrolDestination = false;
		SetState( VBZombieState.Chase );
		FaceTowards( Agent.IsNavigating ? Agent.WishVelocity : position - WorldPosition );
		if ( Vector3.DistanceBetween( _chaseProgress, WorldPosition ) > 8f )
		{ _chaseProgress = WorldPosition; _chaseProgressTime = Time.Now; }

		if ( _timeSincePathRefresh >= PathRefreshInterval )
		{
			_timeSincePathRefresh = 0f;
			bool stalled = Agent.IsNavigating && Time.Now - _chaseProgressTime > 2f;
			if ( Agent.IsNavigating && !stalled && Vector3.DistanceBetween( _chaseGoal, position ) < 48f ) return;
			if ( !VBNpcNavigation.TryWalkPath( Scene, Agent, WorldPosition, position, out var path ) )
			{
				Agent.Stop();
				BeginSearch( position ); Perception.ForgetTarget(); Perception.ConsumeInvestigationPoint();
				return;
			}
			Agent.SetPath( path ); _chaseGoal = position;
			_chaseProgress = WorldPosition; _chaseProgressTime = Time.Now;
		}
	}

	private void BeginSearch( Vector3 origin )
	{
		if ( _isSearching )
			return;

		_isSearching = true;
		_hasPatrolDestination = false;
		_searchOrigin = origin;
		_searchDestination = origin;
		_searchPointsRemaining = Math.Max( 1, SearchPointCount );
		_timeSinceSearchStarted = 0f;
		_searchWaitDuration = 0f;
		_timeSinceSearchWait = 0f;
		Agent.Stop();
	}

	private void TickSearch()
	{
		SetState( VBZombieState.Search );

		if ( _timeSinceSearchStarted >= SearchDuration || _searchPointsRemaining <= 0 )
		{
			if ( ReanchorPatrolAfterSearch )
				_patrolOrigin = _searchOrigin;

			CancelSearch();
			TickPatrol();
			return;
		}

		if ( Agent.IsNavigating )
		{
			FaceTowards( Agent.WishVelocity );
			if ( Vector3.DistanceBetween( WorldPosition, _searchDestination ) > SearchArrivalDistance )
				return;

			Agent.Stop();
			BeginSearchWait();
		}

		if ( _timeSinceSearchWait < _searchWaitDuration )
			return;

		var destination = Scene.NavMesh.GetRandomPoint( _searchOrigin, SearchRadius );
		_searchPointsRemaining--;
		BeginSearchWait();

		if ( !destination.HasValue )
			return;

		_searchDestination = destination.Value;
		if ( VBNpcNavigation.TryWalkPath( Scene, Agent, WorldPosition, _searchDestination, out var searchPath ) ) Agent.SetPath( searchPath );
	}

	private void BeginSearchWait()
	{
		_searchWaitDuration = RandomRange( SearchWaitRange );
		_timeSinceSearchWait = 0f;
	}

	private void CancelSearch()
	{
		_isSearching = false;
		_searchPointsRemaining = 0;
		_searchWaitDuration = 0f;
	}

	private void TickPatrol()
	{
		if ( PatrolRadius <= 0f )
		{
			SetState( VBZombieState.Idle );
			StopAgentUnlessTraversingLink();
			return;
		}

		if ( Agent.IsNavigating )
		{
			SetState( VBZombieState.Patrol );
			FaceTowards( Agent.WishVelocity );

			if ( !_hasPatrolDestination )
			{
				Agent.Stop();
				BeginPatrolWait( PatrolRetryDelay );
				return;
			}

			if ( Vector3.DistanceBetween( WorldPosition, _patrolDestination ) <= PatrolArrivalDistance )
			{
				CompletePatrolLeg();
				return;
			}

			if ( Vector3.DistanceBetween( WorldPosition, _patrolProgressPosition ) >= PatrolProgressDistance )
			{
				_patrolProgressPosition = WorldPosition;
				_timeSincePatrolProgress = 0f;
			}
			else if ( _timeSincePatrolProgress >= PatrolStuckTimeout )
			{
				CompletePatrolLeg( PatrolRetryDelay );
			}

			return;
		}

		if ( _hasPatrolDestination )
		{
			CompletePatrolLeg();
			return;
		}

		if ( _timeSincePatrolWait < _patrolWaitDuration )
		{
			SetState( VBZombieState.Idle );
			return;
		}

		var destination = FindPatrolDestination();
		if ( destination.HasValue )
		{
			_patrolDestination = destination.Value;
			_patrolProgressPosition = WorldPosition;
			_timeSincePatrolProgress = 0f;
			_hasPatrolDestination = true;
			if ( VBNpcNavigation.TryWalkPath( Scene, Agent, WorldPosition, _patrolDestination, out var patrolPath ) ) Agent.SetPath( patrolPath );
			SetState( VBZombieState.Patrol );
		}
		else
		{
			BeginPatrolWait( PatrolRetryDelay );
			SetState( VBZombieState.Idle );
		}
	}

	private Vector3? FindPatrolDestination()
	{
		int attempts = Math.Max( 1, PatrolPointAttempts );
		for ( int attempt = 0; attempt < attempts; attempt++ )
		{
			var destination = Scene.NavMesh.GetRandomPoint( _patrolOrigin, PatrolRadius );
			if ( !destination.HasValue )
				continue;

			if ( Vector3.DistanceBetween( WorldPosition, destination.Value ) < PatrolMinimumMoveDistance )
				continue;
			if ( !VBNpcNavigation.TryWalkPath( Scene, Agent, WorldPosition, destination.Value, out _ ) ) continue;

			return destination;
		}

		return null;
	}

	private void CompletePatrolLeg( float? waitDuration = null )
	{
		Agent.Stop();
		_hasPatrolDestination = false;
		BeginPatrolWait( waitDuration ?? RandomRange( PatrolWaitRange ) );
		SetState( VBZombieState.Idle );
	}

	private void BeginPatrolWait( float duration )
	{
		_patrolWaitDuration = MathF.Max( 0f, duration );
		_timeSincePatrolWait = 0f;
	}

	private void TickAttack( GameObject target )
	{
		SetState( VBZombieState.Attack );
		StopAgentUnlessTraversingLink();
		FaceTowards( target.WorldPosition - WorldPosition );

		if ( Agent.IsTraversingLink || TimeSinceAttack < AttackCooldown ) return;
		AttackTarget = target;
		TimeSinceAttack = 0f;
		AttackPhase = VBZombieAttackPhase.Windup;
		PlayAttackSound();
	}

	private void TickAttackSequence()
	{
		SetState( VBZombieState.Attack );
		StopAgentUnlessTraversingLink();
		float windup = MathF.Max( AttackWindup, 0f );
		float impact = windup + MathF.Max( AttackImpactDelay, 0f );

		if ( AttackPhase == VBZombieAttackPhase.Windup && TimeSinceAttack >= windup )
		{
			AttackPhase = VBZombieAttackPhase.Strike;
			SetAttackAnimation( true );
		}

		if ( AttackPhase == VBZombieAttackPhase.Strike && TimeSinceAttack >= impact )
		{
			// Consume this impact even on a miss; body contact never retries damage.
			AttackPhase = VBZombieAttackPhase.Recovery;
			if ( TryApplyMeleeDamage( AttackTarget ) )
				PlayAttackHitSound();
			else
				PlayAttackMissSound();
		}

		if ( TimeSinceAttack >= impact + MathF.Max( AttackRecovery, 0f ) )
			CancelAttack();
	}

	private void CancelAttack()
	{
		if ( AttackInProgress ) SetAttackAnimation( false );
		AttackTarget = null;
		AttackPhase = VBZombieAttackPhase.None;
	}

	private bool TryApplyMeleeDamage( GameObject target )
	{
		if ( !Perception.IsViableTarget( target ) || !Perception.CanSee( target ) )
			return false;

		var delta = target.WorldPosition - WorldPosition;
		if ( delta.Length > AttackRange )
			return false;

		var flatDirection = delta.WithZ( 0f ).Normal;
		float minimumDot = MathF.Cos( AttackHalfAngle * MathF.PI / 180f );
		if ( !flatDirection.IsNearZeroLength
			&& Vector3.Dot( WorldRotation.Forward.WithZ( 0f ).Normal, flatDirection ) < minimumDot )
			return false;

		var targetHealth = target.Components.Get<VBHealthComponent>();
		var targetSeat = target.GetComponent<VBVehiclePlayerDriver>()?.ActiveSeat;
		if ( targetSeat.IsValid() )
		{
			var player = target.GetComponent<PlayerController>();
			var point = player.IsValid() ? player.EyePosition : target.WorldPosition + Perception.TargetOffset;
			var contact = Scene.Trace.Ray( WorldPosition + Perception.EyeOffset, point )
				.IgnoreGameObjectHierarchy( GameObject ).UseHitboxes( true ).Run();
			if ( contact.Hit && contact.GameObject.IsValid()
				&& contact.GameObject.GetComponentInParent<VBHealthComponent>( includeSelf: true ) != targetHealth ) return false;
		}
		if ( targetHealth.IsValid() )
		{
			var damage = VBDamageInfo.Create( AttackDamage, VBDamageType.Blunt, GameObject, GameObject );
			damage.Position = target.WorldPosition + Perception.TargetOffset;
			damage.Force = flatDirection * AttackDamage * 12f;
			targetHealth.TakeDamage( damage );
			return true;
		}

		var damageable = target.Components.Get<Component.IDamageable>();
		if ( damageable is null )
			return false;

		damageable.OnDamage( new DamageInfo( AttackDamage, GameObject, GameObject )
		{
			Position = target.WorldPosition + Perception.TargetOffset,
			Origin = WorldPosition
		} );
		return true;
	}

	private void FaceTowards( Vector3 direction )
	{
		var flatDirection = direction.WithZ( 0f );
		if ( flatDirection.IsNearZeroLength )
			return;

		var targetRotation = Rotation.LookAt( flatDirection.Normal, Vector3.Up );
		WorldRotation = Rotation.Lerp( WorldRotation, targetRotation, MathX.Clamp( TurnSpeed * Time.Delta, 0f, 1f ) );
	}

	private void UpdateAnimation()
	{
		float deltaTime = MathF.Max( Time.Delta, 0.0001f );
		var measuredVelocity = Locomotion.Velocity;

		if ( Ragdoll.IsRagdolled )
			return;

		_visualVelocity = Vector3.Lerp( _visualVelocity, measuredVelocity, MathX.Clamp( deltaTime * 12f, 0f, 1f ) );
		if ( !_animationHelper.IsValid() )
			return;

		_animationHelper.WithVelocity( _visualVelocity );
		_animationHelper.WithWishVelocity( _visualVelocity.WithZ( 0f ) );
		// Use the same automatic Human gait selection as the default player.
		_animationHelper.MoveStyle = CitizenAnimationHelper.MoveStyles.Auto;
		_animationHelper.HoldType = AttackPhase != VBZombieAttackPhase.None
			? CitizenAnimationHelper.HoldTypes.Punch : CitizenAnimationHelper.HoldTypes.None;
		_animationHelper.IsGrounded = Locomotion.IsGrounded;
		_animationHelper.WithLook( WorldRotation.Forward );
	}

	private void OnDied()
	{
		if ( !Networking.IsHost ) return;
		SetState( VBZombieState.Dead );
		CancelAttack();
	}

	private void StopAgentUnlessTraversingLink()
	{
		if ( !Agent.IsTraversingLink )
			Agent.Stop();
	}

	private void SetState( VBZombieState state )
	{
		State = state;
	}

	private static float RandomRange( Vector2 range )
	{
		float minimum = MathF.Min( range.x, range.y );
		float maximum = MathF.Max( range.x, range.y );
		return minimum + Game.Random.NextSingle() * (maximum - minimum);
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void SetAttackAnimation( bool attacking )
	{
		ResolveVisuals();
		// b_attack belongs to the Punch branch, not the weaponless idle branch.
		var holdType = attacking ? CitizenAnimationHelper.HoldTypes.Punch : CitizenAnimationHelper.HoldTypes.None;
		if ( _animationHelper.IsValid() ) _animationHelper.HoldType = holdType;
		if ( _renderer.IsValid() ) _renderer.Set( "holdtype", (int)holdType );
		if ( _renderer.IsValid() && !string.IsNullOrWhiteSpace( AttackAnimationParameter ) )
			_renderer.Set( AttackAnimationParameter, attacking );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )] private void PlayAlertSound() { if ( AlertSound is not null ) GameObject.PlaySound( AlertSound ); }
	[Rpc.Broadcast( NetFlags.HostOnly )] private void PlayAttackSound() { if ( AttackSound is not null ) GameObject.PlaySound( AttackSound ); }
	[Rpc.Broadcast( NetFlags.HostOnly )] private void PlayAttackHitSound() { if ( AttackHitSound is not null ) GameObject.PlaySound( AttackHitSound ); }
	[Rpc.Broadcast( NetFlags.HostOnly )] private void PlayAttackMissSound() { if ( AttackMissSound is not null ) GameObject.PlaySound( AttackMissSound ); }
}
