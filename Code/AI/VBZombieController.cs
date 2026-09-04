using System;
using System.Threading.Tasks;
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

	[Property, Group( "Melee" ), Range( 1f, 300f )] public float AttackRange { get; set; } = 78f;
	[Property, Group( "Melee" ), Range( 0f, 200f )] public float AttackDamage { get; set; } = 18f;
	[Property, Group( "Melee" ), Range( 0.05f, 5f )] public float AttackCooldown { get; set; } = 1.35f;
	[Property, Group( "Melee" ), Range( 0f, 2f )] public float AttackWindup { get; set; } = 0.42f;
	[Property, Group( "Melee" ), Range( 0f, 180f )] public float AttackHalfAngle { get; set; } = 55f;

	[Property, Group( "Audio" )] public SoundEvent AlertSound { get; set; }
	[Property, Group( "Audio" )] public SoundEvent AttackSound { get; set; }
	[Property, Group( "Audio" )] public SoundEvent AttackHitSound { get; set; }
	[Property, Group( "Audio" )] public SoundEvent AttackMissSound { get; set; }
	[Property, Group( "Animation" )] public string AttackAnimationParameter { get; set; } = "b_attack";

	[Sync( SyncFlags.FromHost )] public VBZombieState State { get; private set; } = VBZombieState.Idle;

	public GameObject Target => Perception.IsValid() ? Perception.CurrentTarget : null;
	public Vector3 LastKnownTargetPosition => Perception.IsValid() ? Perception.LastKnownPosition : WorldPosition;

	private CitizenAnimationHelper _animationHelper;
	private SkinnedModelRenderer _renderer;
	private Vector3 _previousPosition;
	private Vector3 _visualVelocity;
	private Vector3 _searchOrigin;
	private Vector3 _searchDestination;
	private Vector3 _patrolDestination;
	private Vector3 _patrolProgressPosition;
	private Vector3 _patrolOrigin;
	private TimeSince _timeSinceAttack;
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
	private bool _attackInProgress;
	private GameObject _lastAlertedTarget;

	protected override void OnStart()
	{
		ResolveVisuals();
		_previousPosition = WorldPosition;
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

		if ( Networking.IsHost )
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
			SetState( VBZombieState.Dead );
			StopAgentUnlessTraversingLink();
			return;
		}

		if ( Ragdoll.IsRagdolled )
		{
			SetState( VBZombieState.Ragdoll );
			StopAgentUnlessTraversingLink();
			return;
		}

		TickBehaviour();
	}

	protected override void OnDestroy()
	{
		if ( Health.IsValid() )
			Health.OnDeath -= OnDied;
	}

	private void ResolveVisuals()
	{
		_animationHelper ??= Components.Get<CitizenAnimationHelper>( FindMode.InSelf | FindMode.InChildren );
		_renderer ??= Components.Get<SkinnedModelRenderer>( FindMode.InSelf | FindMode.InChildren );
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
		FaceTowards( position - WorldPosition );

		if ( _timeSincePathRefresh >= PathRefreshInterval || !Agent.IsNavigating )
		{
			_timeSincePathRefresh = 0f;
			Agent.MoveTo( position );
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
		Agent.MoveTo( _searchDestination );
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
			Agent.MoveTo( _patrolDestination );
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

		if ( !_attackInProgress && _timeSinceAttack >= AttackCooldown )
			_ = AttackAsync();
	}

	private async Task AttackAsync()
	{
		_attackInProgress = true;
		_timeSinceAttack = 0f;
		SetAttackAnimation( true );
		PlayAttackSound();

		try
		{
			await Task.DelaySeconds( AttackWindup );
			if ( !this.IsValid() || !Networking.IsHost || Health.IsDead || Ragdoll.IsRagdolled )
				return;

			if ( TryApplyMeleeDamage() )
				PlayAttackHitSound();
			else
				PlayAttackMissSound();
		}
		finally
		{
			if ( this.IsValid() )
				SetAttackAnimation( false );
			_attackInProgress = false;
		}
	}

	private bool TryApplyMeleeDamage()
	{
		var target = Perception.CurrentTarget;
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
		var measuredVelocity = (WorldPosition - _previousPosition) / deltaTime;
		_previousPosition = WorldPosition;

		if ( Ragdoll.IsRagdolled )
			return;

		_visualVelocity = Vector3.Lerp( _visualVelocity, measuredVelocity, MathX.Clamp( deltaTime * 12f, 0f, 1f ) );
		if ( !_animationHelper.IsValid() )
			return;

		_animationHelper.WithVelocity( _visualVelocity );
		_animationHelper.WithWishVelocity( Agent.WishVelocity );
		_animationHelper.IsGrounded = true;
		_animationHelper.WithLook( WorldRotation.Forward );
	}

	private void OnDied()
	{
		SetState( VBZombieState.Dead );
		SetAttackAnimation( false );
		_attackInProgress = false;
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

	[Rpc.Broadcast]
	private void SetAttackAnimation( bool attacking )
	{
		if ( _renderer.IsValid() && !string.IsNullOrWhiteSpace( AttackAnimationParameter ) )
			_renderer.Set( AttackAnimationParameter, attacking );
	}

	[Rpc.Broadcast] private void PlayAlertSound() { if ( AlertSound is not null ) GameObject.PlaySound( AlertSound ); }
	[Rpc.Broadcast] private void PlayAttackSound() { if ( AttackSound is not null ) GameObject.PlaySound( AttackSound ); }
	[Rpc.Broadcast] private void PlayAttackHitSound() { if ( AttackHitSound is not null ) GameObject.PlaySound( AttackHitSound ); }
	[Rpc.Broadcast] private void PlayAttackMissSound() { if ( AttackMissSound is not null ) GameObject.PlaySound( AttackMissSound ); }
}
