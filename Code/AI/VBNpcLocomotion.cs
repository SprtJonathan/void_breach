using System;
using Sandbox;

/// <summary>One owner of NPC movement: native collision/steps/gravity, with NavMesh steering only.</summary>
[Title( "NPC Locomotion" ), Category( "Void Breach/AI" ), Icon( "directions_walk" )]
public sealed class VBNpcLocomotion : Component
{
	[RequireComponent] public NavMeshAgent Agent { get; private set; }
	[RequireComponent] public CharacterController Body { get; private set; }
	[RequireComponent] private VBHealthComponent Health { get; set; }
	[RequireComponent] private VBNpcRagdollController Ragdoll { get; set; }
	public bool IsGrounded => Networking.IsHost ? Body.IsValid() && Body.IsOnGround : ReplicatedGrounded;
	public Vector3 Velocity => Networking.IsHost ? Body.IsValid() ? Body.Velocity : Vector3.Zero : ReplicatedVelocity;
	[Sync( SyncFlags.FromHost )] private bool ReplicatedGrounded { get; set; }
	[Sync( SyncFlags.FromHost )] private Vector3 ReplicatedVelocity { get; set; }
	public bool IsMovingDirectly => _directMove;
	[Sync( SyncFlags.FromHost )] private float FallPeak { get; set; }
	[Sync( SyncFlags.FromHost )] private bool WasAirborne { get; set; }
	private bool _directMove;
	private Vector3 _directDestination;
	private float _directUntil;

	protected override void OnAwake() => Configure();
	protected override void OnStart()
	{
		Configure();
		if ( Networking.IsHost && !WasAirborne ) FallPeak = WorldPosition.z;
	}
	private void Configure()
	{
		Agent.UpdatePosition = false;
		Agent.UpdateRotation = false;
		Agent.AutoTraverseLinks = false;
		Body.Radius = Agent.Radius;
		Body.Height = Agent.Height;
		Body.StepHeight = 18f;
		Body.UseCollisionRules = true;
		Body.Bounciness = 0f;
		var climb = ResourceLibrary.Get<Sandbox.Engine.Resources.NavMeshAreaDefinition>( "navigation/npc_climb.navarea" );
		if ( climb is not null && GetComponent<VBZombieController>().IsValid() ) Agent.ForbiddenAreas.Add( climb );
	}
	public void WalkDirectlyTo( Vector3 destination )
	{
		if ( !Networking.IsHost ) return;
		Agent.Stop();
		_directDestination = destination;
		_directUntil = Time.Now + 3f;
		_directMove = true;
	}
	public void CancelDirectMove() => _directMove = false;

	protected override void OnFixedUpdate()
	{
		// This stays false on proxies too: the networked root follows the host,
		// never the client's independently projected navigation simulation.
		Agent.UpdatePosition = false;
		if ( !Networking.IsHost || !Body.Active ) return;
		if ( Health.IsDead || Health.IsDowned || Ragdoll.IsRagdolled )
		{ _directMove = false; Body.Velocity = Vector3.Zero; PublishMotion(); return; }
		if ( Agent.IsTraversingLink || GetComponent<VBSoldierController>() is { IsDynamicallyTraversing: true } )
		{ Body.Velocity = Vector3.Zero; FallPeak = WorldPosition.z; WasAirborne = false; PublishMotion(); return; }

		bool grounded = Body.IsOnGround;
		var wish = Vector3.Zero;
		if ( _directMove )
		{
			var delta = (_directDestination - WorldPosition).WithZ( 0f );
			if ( Time.Now >= _directUntil || delta.Length < 8f || WasAirborne ) _directMove = false;
			else wish = delta.Normal * Agent.MaxSpeed;
		}
		// WishVelocity points down the route BEFORE native crowd avoidance and wall
		// steering. Velocity includes both; keep physical movement on that solution.
		else if ( Agent.IsNavigating && VBNpcNavigation.HasLocalMesh( Scene, WorldPosition ) ) wish = Agent.Velocity.WithZ( 0f );

		// Do not invoke native unsticking from an overlapping spawn/vehicle: that
		// method searches a new position. Wait for clearance instead of teleporting.
		var box = Body.BoundingBox;
		var overlap = Scene.Trace.Box( box, WorldPosition, WorldPosition )
			.IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).Run();
		if ( overlap.StartedSolid )
		{
			// Exact floor placement can count as a tiny penetration. Permit only
			// the native two-unit ground skin, with a proven standable floor and a
			// clear raised body. Never search random horizontal unstuck positions.
			var raised = WorldPosition + Vector3.Up * VBTraversalGeometry.Skin;
			var clearance = Scene.Trace.Box( box, raised, raised ).IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).Run();
			var floor = Scene.Trace.Ray( raised, WorldPosition - Vector3.Up * VBTraversalGeometry.Skin )
				.IgnoreGameObjectHierarchy( GameObject ).IgnoreDynamic().Run();
			if ( clearance.StartedSolid || !floor.Hit || floor.StartedSolid
				|| floor.Normal.z < MathF.Cos( Body.GroundAngle * MathF.PI / 180f ) )
			{ Body.Velocity = Vector3.Zero; PublishMotion(); return; }
			WorldPosition = raised;
		}

		float dt = Scene.FixedDelta;
		var gravity = Scene.PhysicsWorld.Gravity;
		if ( grounded && !wish.IsNearZeroLength )
		{
			// Anticipate a dangerous edge and brake using ordinary friction. This
			// rejects the intended route; it does not erase a real fall or teleport.
			var ahead = WorldPosition + wish.Normal * (Agent.Radius + 8f + Velocity.WithZ( 0f ).Length * 0.15f);
			var footing = Scene.Trace.Ray( ahead + Vector3.Up * Body.StepHeight,
				ahead - Vector3.Up * (VBFallDamage.SevereDistance + 32f) )
				.IgnoreGameObjectHierarchy( GameObject ).IgnoreDynamic().Run();
			if ( !footing.StartedSolid && (!footing.Hit || !VBSurvivalMath.CanRiskDrop( WorldPosition.z - footing.EndPosition.z, Health.CurrentHealth )) )
			{
				wish = Vector3.Zero;
				_directMove = false; Agent.Stop();
			}
		}
		if ( grounded || !WasAirborne )
		{
			var soldier = GetComponent<VBSoldierController>( includeDisabled: true );
			if ( soldier.IsValid() )
			{
				// Keep native collision and crowd steering, with finite motor acceleration and braking.
				var horizontal = soldier.ApproachMovementVelocity( Body.Velocity.WithZ( 0f ),
					wish.WithZ( 0f ).ClampLength( MathF.Max( 0f, Agent.MaxSpeed ) ), dt );
				Body.Velocity = horizontal.WithZ( Body.Velocity.z );
			}
			else
			{
				// Native stopSpeed defaults to 140: at walking pace it erased the
				// entire previous velocity every tick, leaving only one acceleration step.
				Body.ApplyFriction( 8f, 0f );
				Body.Accelerate( wish.ClampLength( MathF.Max( 0f, Agent.MaxSpeed ) ) );
				Body.Velocity = Body.Velocity.WithZ( 0f ).ClampLength( MathF.Max( 0f, Agent.MaxSpeed ) ).WithZ( Body.Velocity.z );
			}
		}
		Body.Velocity += gravity * (dt * 0.5f);
		FallPeak = MathF.Max( FallPeak, WorldPosition.z );
		Body.Move();
		if ( Body.IsOnGround )
		{
			if ( WasAirborne ) Health.TakeDamage( VBFallDamage.Create( FallPeak - WorldPosition.z, WorldPosition ) );
			WasAirborne = false;
			FallPeak = WorldPosition.z;
			Body.Velocity = Body.Velocity.WithZ( 0f );
		}
		else { WasAirborne = true; Body.Velocity += gravity * (dt * 0.5f); }
		// Bring the steering agent to the actual collision-resolved position. Its
		// projected position is never copied back into the physical actor.
		Agent.SetAgentPosition( WorldPosition );
		PublishMotion();
	}

	private void PublishMotion()
	{
		ReplicatedVelocity = Body.Velocity;
		ReplicatedGrounded = Body.IsOnGround;
	}
}
