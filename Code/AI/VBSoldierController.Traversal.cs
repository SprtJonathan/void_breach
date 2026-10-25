using System;
using Sandbox;
using Sandbox.Navigation;

public sealed partial class VBSoldierController
{
	[Property, Group( "Traversal" )] public bool UseDynamicTraversal { get; set; } = true;
	[Property, Group( "Traversal" )] public float MaximumClimbHeight { get; set; } = 112f;
	[Property, Group( "Traversal" )] public float MaximumEscapeDrop { get; set; } = 320f;
	[Sync( SyncFlags.FromHost )] public bool IsDynamicallyTraversing { get; private set; }
	[Sync( SyncFlags.FromHost )] private NetList<Vector3> TraversePoints { get; set; } = new();
	[Sync( SyncFlags.FromHost )] private TimeUntil TraverseEnd { get; set; }
	[Sync( SyncFlags.FromHost )] private float TraverseDuration { get; set; }
	[Sync( SyncFlags.FromHost )] private bool TraverseLinked { get; set; }
	[Sync( SyncFlags.FromHost )] private GameObject TraverseSupport { get; set; }
	[Sync( SyncFlags.FromHost )] private Transform TraverseSupportTransform { get; set; }
	private float _nextTraversalProbe;
	private Vector3 _pendingTraversalGoal;
	private bool _hasTraversalGoal;
	private float _traversalGoalExpiry;
	private VBTraversalGeometry TraversalGeometry => new( GameObject, Agent.Radius, Agent.Height, boxBody: true );

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !Agent.IsValid() ) return;
		if ( IsDynamicallyTraversing )
		{
			if ( !CanFight || TraversePoints.Count < 2 ) { EndDynamicTraversal( false ); return; }
			Agent.UpdatePosition = false;
			float t = Math.Clamp( 1f - (float)TraverseEnd / MathF.Max( 0.1f, TraverseDuration ), 0f, 1f );
			var next = TraversalPosition( t );
			bool supportValid = TraverseSupport.IsValid() && TraverseSupport.Active
				&& Vector3.DistanceBetween( TraverseSupport.WorldPosition, TraverseSupportTransform.Position ) < 0.5f
				&& Rotation.Difference( TraverseSupport.WorldRotation, TraverseSupportTransform.Rotation ).Angle() < 0.5f;
			if ( !supportValid || !TraversalGeometry.LandingClear( TraversePoints.Last() )
				|| !TraversalGeometry.PathClear( WorldPosition, next ) )
			{ EndDynamicTraversal( false ); return; }
			WorldPosition = next;
			Agent.SetAgentPosition( next );
			if ( t >= 1f ) EndDynamicTraversal( true );
			return;
		}
	}

	private Vector3 TraversalPosition( float fraction )
	{
		float length = 0f;
		for ( int i = 1; i < TraversePoints.Count; i++ ) length += Vector3.DistanceBetween( TraversePoints[i - 1], TraversePoints[i] );
		float progress = length * fraction;
		for ( int i = 1; i < TraversePoints.Count; i++ )
		{
			float segment = Vector3.DistanceBetween( TraversePoints[i - 1], TraversePoints[i] );
			if ( progress <= segment && segment > 0.001f ) return Vector3.Lerp( TraversePoints[i - 1], TraversePoints[i], progress / segment );
			progress -= segment;
		}
		return TraversePoints.Last();
	}

	private void EndDynamicTraversal( bool success )
	{
		IsDynamicallyTraversing = false;
		_nextTraversalProbe = Time.Now + 0.6f;
		Agent.SetAgentPosition( WorldPosition );
		if ( TraverseLinked && success ) Agent.CompleteLinkTraversal();
		else if ( TraverseLinked ) Agent.Stop();
		TraverseLinked = false;
		// A blocked/removed support simply releases the actor to native gravity.
	}

	private bool StartDynamicTraversal( VBTraversalPlan plan ) => StartTraversal(
		new[] { WorldPosition, plan.Rise, plan.Over, plan.Landing }, plan.Support, plan.Vault ? 0.65f : 0.9f, false );

	private bool StartTraversal( Vector3[] points, GameObject support, float duration, bool linked )
	{
		if ( !CanFight || !support.IsValid() || points.Length < 2 || points.Length > 16
			|| !TraversalGeometry.LandingClear( points.Last() ) ) return false;
		for ( int i = 1; i < points.Length; i++ )
			if ( !TraversalGeometry.PathClear( points[i - 1], points[i] ) ) return false;
		if ( !linked ) StopMoving();
		ClearCover(); ClearTacticalMove(); _hasTraversalGoal = false;
		if ( Weapon.IsValid() && Weapon.IsReloading ) Weapon.CancelReload();
		TraversePoints.Clear(); foreach ( var point in points ) TraversePoints.Add( point );
		TraverseSupport = support; TraverseSupportTransform = support.WorldTransform;
		TraverseDuration = MathF.Max( duration, 0.3f ); TraverseEnd = TraverseDuration;
		TraverseLinked = linked; IsDynamicallyTraversing = true;
		Locomotion.CancelDirectMove(); Agent.UpdatePosition = false;
		Face( points.Last() - WorldPosition );
		return true;
	}

	internal bool TryTraverseLink( NavMeshAgent.LinkTraversalData link )
	{
		var direction = (link.LinkExitPosition - WorldPosition).WithZ( 0f ).Normal;
		var authored = link.LinkComponent.IsValid() ? link.LinkComponent.GetComponent<VBNpcTraversalLink>() : null;
		if ( link.LinkExitPosition.z < WorldPosition.z - 32f && (!authored.IsValid() || authored.Mode != VBNpcLinkMode.Climb) )
		{
			// Link is planning information; walking over the edge still uses the
			// same collision movement and gravity as every other descent.
			return TryEscapeDrop( direction, link.LinkExitPosition );
		}
		if ( !authored.IsValid() )
		{
			return TraversalGeometry.TryPlan( WorldPosition, direction, MaximumClimbHeight, out var detected )
				&& Vector3.DistanceBetween( detected.Landing, link.LinkExitPosition ) < 64f
				&& StartTraversal( new[] { WorldPosition, detected.Rise, detected.Over, detected.Landing }, detected.Support, 0.9f, true );
		}
		var points = new List<Vector3> { WorldPosition };
		bool reverse = Vector3.DistanceBetween( link.LinkEnterPosition, authored.Link.WorldEndPosition )
			< Vector3.DistanceBetween( link.LinkEnterPosition, authored.Link.WorldStartPosition );
		foreach ( var point in reverse ? (authored.Waypoints ?? Array.Empty<GameObject>()).Reverse() : (authored.Waypoints ?? Array.Empty<GameObject>()).AsEnumerable() )
			if ( point.IsValid() ) points.Add( point.WorldPosition );
		points.Add( link.LinkExitPosition );
		var ground = TraversalGeometry.StaticRay( points.Last() + Vector3.Up * 8f, points.Last() - Vector3.Up * 24f );
		if ( !TraversalGeometry.IsLanding( ground ) ) return false;
		float distance = 0f; for ( int i = 1; i < points.Count; i++ ) distance += Vector3.DistanceBetween( points[i - 1], points[i] );
		return StartTraversal( points.ToArray(), ground.GameObject, distance / MathF.Max( 20f, authored.Speed ), true );
	}

	private bool TryDynamicRoute( Vector3 goal, NavMeshPath walkingPath, bool walkComplete )
	{
		if ( !UseDynamicTraversal || MoveSpeed <= 0f || Time.Now < _nextTraversalProbe || !Locomotion.IsGrounded ) return false;
		_nextTraversalProbe = Time.Now + 0.6f;
		float bestCost = walkComplete ? VBNpcNavigation.WalkingCost( VBNpcNavigation.PathLength( walkingPath ) ) : float.PositiveInfinity;
		VBTraversalPlan selected = default;
		NavMeshPath approachPath = default;
		bool found = false, approachNeeded = false;
		void Evaluate( Vector3 approach, bool local )
		{
			NavMeshPath approachRoute = default;
			if ( !local && !VBNpcNavigation.TryWalkPath( Scene, Agent, WorldPosition, approach, out approachRoute ) ) return;
			var forward = (goal - approach).WithZ( 0f ).Normal;
			if ( forward.IsNearZeroLength || !TraversalGeometry.TryPlan( approach, forward, MaximumClimbHeight, out var plan ) ) return;
			bool onwardComplete = VBNpcNavigation.TryWalkPath( Scene, Agent, plan.Landing, goal, out var onward );
			float goalDistance = Vector3.DistanceBetween( plan.Landing, goal );
			var localStop = goalDistance < 96f ? plan.Landing : goal + (plan.Landing - goal).WithZ( 0f ).Normal * 64f;
			bool localExit = MathF.Abs( plan.Landing.z - goal.z ) < 20f && goalDistance < 160f
				&& TraversalGeometry.LandingClear( localStop ) && TraversalGeometry.PathClear( plan.Landing, localStop );
			// A narrow, physically usable top may be too small to bake a NavMesh
			// polygon. Local arrival still works when the actual body fits throughout.
			if ( !onwardComplete && !localExit ) return;
			float climbLength = Vector3.DistanceBetween( plan.Start, plan.Rise ) + Vector3.DistanceBetween( plan.Rise, plan.Over )
				+ Vector3.DistanceBetween( plan.Over, plan.Landing );
			float cost = VBNpcNavigation.ClimbingCost( climbLength, VBNpcNavigation.ClimbMultiplier )
				+ VBNpcNavigation.WalkingCost( onwardComplete ? VBNpcNavigation.PathLength( onward ) : Vector3.DistanceBetween( plan.Landing, goal ) )
				+ (local ? 0f : VBNpcNavigation.WalkingCost( VBNpcNavigation.PathLength( approachRoute ) ));
			if ( cost >= bestCost ) return;
			bestCost = cost; selected = plan; approachPath = approachRoute; found = true; approachNeeded = !local;
		}
		Evaluate( WorldPosition, true );
		// Look at the reachable face of nearby obstacles as well as the partial
		// path end. This compares a real detour against approach + climb + exit.
		var toward = (goal - WorldPosition).WithZ( 0f ).Normal;
		foreach ( float angle in new[] { 0f, 35f, -35f } )
		{
			var direction = Rotation.FromYaw( angle ) * toward;
			var wall = TraversalGeometry.StaticRay( WorldPosition + Vector3.Up * 22f,
				WorldPosition + Vector3.Up * 22f + direction * 220f );
			if ( !wall.Hit || wall.StartedSolid || MathF.Abs( wall.Normal.z ) > 0.2f ) continue;
			var approach = (wall.EndPosition + wall.Normal * (Agent.Radius + 4f)).WithZ( WorldPosition.z );
			if ( Vector3.DistanceBetween( approach, WorldPosition ) > 24f ) Evaluate( approach, false );
		}
		if ( !walkComplete && walkingPath.Points is { Count: > 1 } ) Evaluate( walkingPath.Points.Last().Position, false );
		if ( found )
		{
			if ( !approachNeeded ) return StartDynamicTraversal( selected );
			Agent.SetPath( approachPath );
			_destination = approachPath.Points.Last().Position; _hasDestination = true;
			_progressPosition = WorldPosition; _progressTime = Time.Now;
			_pendingTraversalGoal = goal; _hasTraversalGoal = true; _traversalGoalExpiry = Time.Now + 8f;
			return true;
		}
		return !walkComplete && goal.z < WorldPosition.z - 32f
			&& TryEscapeDrop( toward, goal );
	}

	private bool TickPendingTraversal()
	{
		if ( !_hasTraversalGoal ) return false;
		if ( !UseDynamicTraversal || Time.Now > _traversalGoalExpiry
			|| Perception.TargetVisible && Perception.IsViableTarget( Perception.CurrentTarget )
			&& Vector3.DistanceBetween( WorldPosition, Perception.LastKnownPosition ) < Threats.CloseThreatRange )
		{ _hasTraversalGoal = false; StopMoving(); return false; }
		if ( Vector3.DistanceBetween( WorldPosition, _destination ) > 20f && MovementProgressing() )
		{ Face( Agent.WishVelocity ); State = VBSoldierState.Advance; return true; }
		_hasTraversalGoal = false;
		_nextTraversalProbe = 0f;
		return TryMove( _pendingTraversalGoal );
	}

	private bool TryEscapeDrop( Vector3 direction, Vector3? goal = null )
	{
		if ( !UseDynamicTraversal || MoveSpeed <= 0f ) return false;
		for ( int step = 1; step <= 3; step++ )
		{
			var outside = WorldPosition + direction.WithZ( 0f ).Normal * (Agent.Radius * 2f + step * 24f);
			var ground = TraversalGeometry.StaticRay( outside + Vector3.Up * 8f, outside - Vector3.Up * MaximumEscapeDrop );
			if ( !TraversalGeometry.IsLanding( ground ) ) continue;
			var landing = ground.EndPosition + Vector3.Up * VBTraversalGeometry.Skin;
			float drop = WorldPosition.z - landing.z;
			if ( drop < 32f || drop > MaximumEscapeDrop || !VBSurvivalMath.CanRiskDrop( drop, Health.CurrentHealth )
				|| !TraversalGeometry.LandingClear( landing ) ) continue;
			if ( goal.HasValue && !VBNpcNavigation.TryWalkPath( Scene, Agent, landing, goal.Value, out _ ) ) continue;
			var over = outside.WithZ( WorldPosition.z + 2f );
			if ( !TraversalGeometry.PathClear( WorldPosition, over ) || !TraversalGeometry.PathClear( over, landing ) ) continue;
			// Walk over the actual edge before gravity takes over, rather than moving
			// the actor to the lower floor or creating an invisible NavMeshLink.
			// A drop deliberately leaves support and uses a dedicated edge phase.
			StopMoving(); ClearCover(); ClearTacticalMove();
			if ( Weapon.IsValid() && Weapon.IsReloading ) Weapon.CancelReload();
			Locomotion.WalkDirectlyTo( outside );
			return true;
		}
		return false;
	}
}
