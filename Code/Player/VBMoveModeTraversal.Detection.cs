using System;
using Sandbox;

public sealed partial class VBMoveModeTraversal
{
	private Vector3 _landing;
	private Transform _candidateGrip;
	private TraversalSupport _support, _exitSupport;

	private void TryStartTraversal()
	{
		BeginDebugAttempt();
		try { TryStartTraversalCore(); }
		finally { EndDebugAttempt(); }
	}

	private void TryStartTraversalCore()
	{
		bool grounded = Controller.IsOnGround;
		// Controller.Velocity is the previous post-physics snapshot; use the current body velocity.
		float verticalSpeed = Controller.Body.Velocity.z;
		if ( !grounded && !EnableLedgeGrab ) { RejectDebugAttempt( "vb.traversal.debug.grab_disabled" ); return; }
		if ( !grounded && !CatchWhileRising && verticalSpeed > 0f ) { RejectDebugAttempt( "vb.traversal.debug.rising" ); return; }
		if ( !grounded && verticalSpeed < -MathF.Max( MaximumCatchFallSpeed, 0f ) )
		{ RejectDebugAttempt( "vb.traversal.debug.fall_speed", $"vZ={verticalSpeed:F0}, limit=-{MaximumCatchFallSpeed:F0}" ); return; }
		var forward = Rotation.FromYaw( Controller.EyeAngles.yaw ).Forward;
		float probeHeight = grounded ? MathF.Max( MinimumHeight - Skin, Skin ) : Controller.BodyHeight * 0.5f;
		var origin = WorldPosition + Vector3.Up * probeHeight;
		var wall = StaticRay( origin, origin + forward * (Controller.BodyRadius + MathF.Max( ReachDistance, 8f )), "wall" );
		if ( !wall.Hit || wall.StartedSolid )
		{ RejectDebugAttempt( wall.StartedSolid ? "vb.traversal.debug.wall_inside" : "vb.traversal.debug.no_wall", markTrace: true ); return; }
		if ( MathF.Abs( wall.Normal.z ) > 0.2f )
		{ RejectDebugAttempt( "vb.traversal.debug.wall_angle", $"normal.z={wall.Normal.z:F2}, max=0.20", true ); return; }
		if ( wall.Normal.Dot( forward ) > -0.6f )
		{ RejectDebugAttempt( "vb.traversal.debug.approach_angle", $"dot={wall.Normal.Dot( forward ):F2}, max=-0.60", true ); return; }
		float radius = Controller.BodyRadius * (MathF.Abs( wall.Normal.x ) + MathF.Abs( wall.Normal.y ));
		var inside = wall.EndPosition - wall.Normal * (radius + Skin * 2f);
		// The contact probe must fit a thin fence; standing clearance is validated
		// separately at the actual body destination, farther behind the face.
		var edgeProbe = wall.EndPosition - wall.Normal * 0.5f;
		float minimum = grounded ? MathF.Max( MinimumHeight, 18f ) : Controller.BodyHeight - 24f;
		float maximum = grounded ? MathF.Max( ClimbMaximumHeight, minimum ) : Controller.BodyHeight + MathF.Max( CatchReachAboveHead, 0f );
		SetDebugHeightWindow( minimum, maximum, probeHeight, (wall.EndPosition - origin).Length );
		var top = StaticRay( edgeProbe.WithZ( WorldPosition.z + maximum + Skin ), edgeProbe.WithZ( WorldPosition.z + minimum ), "top" );
		if ( !IsLanding( top ) )
		{
			RejectDebugAttempt( top.StartedSolid ? "vb.traversal.debug.top_inside"
				: !top.Hit ? "vb.traversal.debug.no_top" : "vb.traversal.debug.landing_angle",
				$"normal.z={top.Normal.z:F2} | {DebugHitName( top )}", true );
			return;
		}
		float ledgeHeight = top.EndPosition.z - WorldPosition.z;
		if ( ledgeHeight > maximum || ledgeHeight < minimum )
		{
			if ( _debugRecording ) RejectDebugAttempt( "vb.traversal.debug.height_window", $"height={ledgeHeight:F1} | allowed=[{minimum:F0}..{maximum:F0}]", true );
			return;
		}
		_landing = inside.WithZ( top.EndPosition.z + Skin );
		_candidateGrip = new Transform( wall.EndPosition.WithZ( top.EndPosition.z ), Rotation.LookAt( -wall.Normal, Vector3.Up ) );
		_support = new TraversalSupport( top );

		if ( !grounded )
		{
			var hang = (wall.EndPosition + wall.Normal * (radius + Skin * 2f))
				.WithZ( top.EndPosition.z - (Controller.BodyHeight - 12f) );
			if ( !PathClear( WorldPosition, hang, "catch" ) || !PlanLedgeExit( hang, out _ ) ) return;
			_start = WorldPosition;
			_destination = hang;
			if ( !SpendStartCost( MathF.Max( HangDrainPerSecond, 0f ) * MathF.Max( MinimumCatchHoldTime, 0f ) ) ) return;
			BeginMotion( VBTraversalState.Catching, 0.15f );
			return;
		}

		bool vault = ledgeHeight <= MathF.Max( VaultMaximumHeight, MinimumHeight );
		bool planned = vault && PlanVaultExit( WorldPosition, VaultMaximumDrop );
		if ( !planned )
		{
			if ( !PlanLedgeExit( WorldPosition, out bool thinVault ) ) return;
			vault |= thinVault;
		}
		if ( !SpendStartCost() ) return;
		BeginMotion( vault ? VBTraversalState.Vaulting : VBTraversalState.Climbing, vault ? VaultDuration : ClimbDuration );
	}

	private bool PlanLedgeExit( Vector3 start, out bool vault )
	{
		vault = false;
		_exitSupport = default;
		// A usable top always wins. A blocked path to a broad top must not be
		// bypassed by vaulting farther; only insufficient standing space falls back.
		if ( LandingClear( _landing ) ) return PlanClimb( start, _landing, _landing.z );
		if ( !EnableThinLedgeVault || !PlanVaultExit( start, LedgeVaultMaximumDrop ) ) return false;
		vault = true;
		return true;
	}

	private bool PlanVaultExit( Vector3 start, float maximumDrop )
	{
		var forward = (IsTraversing ? Grip : _candidateGrip).Rotation.Forward;
		var beyond = _landing + forward * MathF.Max( VaultForwardDistance, 32f );
		var far = StaticRay( beyond.WithZ( _landing.z + Skin ),
			beyond.WithZ( _landing.z - Skin - MathF.Max( maximumDrop, 0f ) ), "far" );
		if ( !IsLanding( far ) ) return false;
		var landing = far.EndPosition + Vector3.Up * Skin;
		if ( !LandingClear( landing ) || !PlanClimb( start, landing, _landing.z ) ) return false;
		_exitSupport = new TraversalSupport( far );
		return true;
	}

	private bool PlanClimb( Vector3 start, Vector3 destination, float topHeight )
	{
		var rise = start.WithZ( MathF.Max( topHeight + Skin, start.z ) );
		var over = destination.WithZ( rise.z );
		if ( !PathClear( start, rise, "rise" ) || !PathClear( rise, over, "across" ) || !PathClear( over, destination, "descent" ) ) return false;
		_start = start; _rise = rise; _over = over; _destination = destination;
		return true;
	}

	private SceneTraceResult StaticRay( Vector3 start, Vector3 end, string purpose )
	{
		var trace = Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).IgnoreDynamic().Run();
		RecordDebugTrace( start, end, trace, purpose, false, trace.Hit && !trace.StartedSolid );
		return trace;
	}
	private bool IsLanding( SceneTraceResult hit ) => hit.Hit && !hit.StartedSolid && hit.GameObject.IsValid()
		&& hit.Normal.z >= MathF.Cos( Math.Clamp( MaximumLandingAngle, 0f, 45f ) * MathF.PI / 180f );
	private bool PathClear( Vector3 start, Vector3 end, string purpose = "body" )
	{
		// Match native PlayerController.UpdateBody's standing capsule and feet box.
		// TraceBody(2) encloses the capsule in a wider square and rejects legal wall contact.
		float radius = Controller.BodyRadius / MathF.Sqrt( 2f );
		float height = Controller.BodyHeight;
		float inset = Math.Clamp( BodyTraceInset, 0f, MathF.Min( 0.5f, Controller.BodyRadius * 0.1f ) );
		float upper = height - radius;
		float lower = MathF.Max( height * 0.5f, radius + 1f );
		if ( lower < upper )
		{
			var capsule = new Capsule( Vector3.Up * lower, Vector3.Up * upper, radius - inset );
			var trace = Scene.Trace.Capsule( capsule, start, end ).IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).Run();
			var bounds = new BBox( new Vector3( -capsule.Radius, -capsule.Radius, lower - capsule.Radius ),
				new Vector3( capsule.Radius, capsule.Radius, upper + capsule.Radius ) );
			if ( !CheckBodyTrace( start, end, trace, purpose, bounds, capsule ) ) return false;
		}
		float feetHeight = lower < upper ? height * 0.5f : height;
		float halfWidth = Controller.BodyRadius * 0.5f - inset;
		var feet = new BBox( new Vector3( -halfWidth, -halfWidth, inset ), new Vector3( halfWidth, halfWidth, feetHeight - inset ) );
		var feetTrace = Scene.Trace.Box( feet, start, end ).Rotated( WorldRotation )
			.IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).Run();
		return CheckBodyTrace( start, end, feetTrace, purpose, feet );
	}
	private bool LandingClear( Vector3 landing )
	{
		if ( !PathClear( landing, landing, "landing" ) ) return false;
		float radius = Controller.BodyRadius * 0.9f;
		for ( int x = -1; x <= 1; x += 2 )
		for ( int y = -1; y <= 1; y += 2 )
		{
			var point = landing + new Vector3( x * radius, y * radius, 0f );
			if ( !IsLanding( StaticRay( point + Vector3.Up * Skin, point - Vector3.Up * 6f, "footprint" ) ) )
			{ RejectDebugAttempt( "vb.traversal.debug.no_footing", $"corner=({x},{y})", true ); return false; }
		}
		return true;
	}
	private string SupportFailure() => _support.Failure()
		?? (_exitSupport.HasSupport ? _exitSupport.Failure() : null);

	/// <summary>Immutable support snapshot; destroyed objects remain tracked until the action ends.</summary>
	private readonly struct TraversalSupport
	{
		private readonly GameObject _object;
		private readonly Component _collider;
		private readonly bool _hadCollider;
		private readonly Transform _transform;

		public bool HasSupport => _object is not null;

		public TraversalSupport( SceneTraceResult trace )
		{
			_object = trace.GameObject;
			_collider = trace.Component;
			_hadCollider = _collider.IsValid();
			_transform = _object.WorldTransform;
		}

		public string Failure()
		{
			if ( !_object.IsValid() ) return "support_removed";
			if ( !_object.Active ) return "support_disabled";
			if ( _hadCollider && (!_collider.IsValid() || !_collider.Enabled) ) return "collider_removed";
			if ( !((_object.WorldPosition - _transform.Position).Length < 0.5f
				&& (_object.WorldScale - _transform.Scale).Length < 0.001f
				&& Rotation.Difference( _transform.Rotation, _object.WorldRotation ).Angle() < 0.5f) ) return "support_moved";
			return null;
		}
	}
}
