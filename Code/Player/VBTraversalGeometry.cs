using System;
using Sandbox;

/// <summary>Shared geometric traversal queries. No input, navigation, animation or movement ownership.</summary>
public readonly struct VBTraversalGeometry
{
	public const float Skin = 2f;
	public readonly GameObject Actor;
	public readonly float Radius, Height, Inset, SupportScale, LandingAngle;
	public readonly bool BoxBody;
	public VBTraversalGeometry( GameObject actor, float radius, float height, float inset = 0.25f,
		float supportScale = 0.5f, float landingAngle = 25f, bool boxBody = false )
	{
		Actor = actor; Radius = radius; Height = height; Inset = inset;
		SupportScale = supportScale; LandingAngle = landingAngle; BoxBody = boxBody;
	}
	public SceneTraceResult StaticRay( Vector3 from, Vector3 to ) => Actor.Scene.Trace.Ray( from, to )
		.IgnoreGameObjectHierarchy( Actor ).WithCollisionRules( Actor.Tags ).IgnoreDynamic().Run();
	public SceneTraceResult FindWall( Vector3 start, Vector3 forward, float reach, float probeHeight ) =>
		StaticRay( start + Vector3.Up * probeHeight, start + Vector3.Up * probeHeight + forward * (Radius + MathF.Max( reach, 8f )) );
	public SceneTraceResult FindTop( Vector3 start, SceneTraceResult wall, float minimum, float maximum )
	{
		var edge = wall.EndPosition - wall.Normal * 0.5f;
		return StaticRay( edge.WithZ( start.z + maximum + Skin ), edge.WithZ( start.z + minimum ) );
	}
	public Vector3 LandingBehind( SceneTraceResult wall, SceneTraceResult top )
	{
		float radius = Radius * (MathF.Abs( wall.Normal.x ) + MathF.Abs( wall.Normal.y ));
		return (wall.EndPosition - wall.Normal * (radius + Skin * 2f)).WithZ( top.EndPosition.z + Skin );
	}
	public bool IsLanding( SceneTraceResult hit ) => hit.Hit && !hit.StartedSolid && hit.GameObject.IsValid()
		&& hit.Normal.z >= MathF.Cos( Math.Clamp( LandingAngle, 0f, 45f ) * MathF.PI / 180f );

	public delegate bool BodyTraceCheck( Vector3 start, Vector3 end, SceneTraceResult trace, string purpose, BBox bounds, Capsule? capsule );
	public bool PathClear( Vector3 start, Vector3 end, string purpose = "body", BodyTraceCheck check = null )
	{
		float radius = Radius / MathF.Sqrt( 2f );
		float inset = Math.Clamp( Inset, 0f, MathF.Min( 0.5f, Radius * 0.1f ) );
		if ( BoxBody )
		{
			var box = new BBox( new Vector3( -Radius + inset, -Radius + inset, inset ), new Vector3( Radius - inset, Radius - inset, Height - inset ) );
			var hit = Actor.Scene.Trace.Box( box, start, end ).IgnoreGameObjectHierarchy( Actor ).WithCollisionRules( Actor.Tags ).Run();
			return !hit.Hit && !hit.StartedSolid;
		}
		float upper = Height - radius;
		float lower = MathF.Max( Height * 0.5f, radius + 1f );
		if ( lower < upper )
		{
			var capsule = new Capsule( Vector3.Up * lower, Vector3.Up * upper, radius - inset );
			var trace = Actor.Scene.Trace.Capsule( capsule, start, end ).IgnoreGameObjectHierarchy( Actor ).WithCollisionRules( Actor.Tags ).Run();
			var bounds = new BBox( new Vector3( -capsule.Radius, -capsule.Radius, lower - capsule.Radius ),
				new Vector3( capsule.Radius, capsule.Radius, upper + capsule.Radius ) );
			if ( check is not null ? !check( start, end, trace, purpose, bounds, capsule ) : trace.Hit || trace.StartedSolid ) return false;
		}
		float feetHeight = lower < upper ? Height * 0.5f : Height;
		float width = Radius * 0.5f - inset;
		var feet = new BBox( new Vector3( -width, -width, inset ), new Vector3( width, width, feetHeight - inset ) );
		var feetTrace = Actor.Scene.Trace.Box( feet, start, end ).Rotated( Actor.WorldRotation )
			.IgnoreGameObjectHierarchy( Actor ).WithCollisionRules( Actor.Tags ).Run();
		return check is not null ? check( start, end, feetTrace, purpose, feet, null ) : !feetTrace.Hit && !feetTrace.StartedSolid;
	}
	public bool LandingClear( Vector3 landing )
	{
		if ( !PathClear( landing, landing ) ) return false;
		float radius = Radius * Math.Clamp( SupportScale, 0.25f, 1f );
		for ( int x = -1; x <= 1; x += 2 )
		for ( int y = -1; y <= 1; y += 2 )
		{
			var point = landing + new Vector3( x * radius, y * radius, 0f );
			if ( !IsLanding( StaticRay( point + Vector3.Up * Skin, point - Vector3.Up * 6f ) ) ) return false;
		}
		return true;
	}

	public bool TryPlan( Vector3 start, Vector3 forward, float maximumHeight, out VBTraversalPlan plan )
	{
		plan = default;
		var wall = FindWall( start, forward, 36f, 22f );
		if ( !wall.Hit || wall.StartedSolid || MathF.Abs( wall.Normal.z ) > 0.2f || wall.Normal.Dot( forward ) > -0.6f ) return false;
		var top = FindTop( start, wall, 24f, maximumHeight );
		if ( !IsLanding( top ) ) return false;
		var landing = LandingBehind( wall, top );
		bool vault = top.EndPosition.z - start.z <= 64f;
		// Same vault-first / broad-top / thin-ledge fallback as the player's detector.
		if ( vault && TryVault( start, landing, -wall.Normal, 80f, out plan ) ) return true;
		if ( LandingClear( landing ) ) return PlanClimb( start, landing, landing.z, top.GameObject, false, out plan );
		return TryVault( start, landing, -wall.Normal, 128f, out plan );
	}
	private bool TryVault( Vector3 start, Vector3 top, Vector3 forward, float drop, out VBTraversalPlan plan )
	{
		plan = default;
		var beyond = top + forward * 80f;
		var far = StaticRay( beyond + Vector3.Up * Skin, beyond - Vector3.Up * (Skin + drop) );
		if ( !IsLanding( far ) ) return false;
		var landing = far.EndPosition + Vector3.Up * Skin;
		return LandingClear( landing ) && PlanClimb( start, landing, top.z, far.GameObject, true, out plan );
	}
	private bool PlanClimb( Vector3 start, Vector3 landing, float top, GameObject support, bool vault, out VBTraversalPlan plan )
	{
		var rise = start.WithZ( MathF.Max( top + Skin, start.z ) );
		var over = landing.WithZ( rise.z );
		plan = new VBTraversalPlan( start, rise, over, landing, support, vault );
		return PathClear( start, rise ) && PathClear( rise, over ) && PathClear( over, landing );
	}
}

public readonly record struct VBTraversalPlan( Vector3 Start, Vector3 Rise, Vector3 Over, Vector3 Landing, GameObject Support, bool Vault );
