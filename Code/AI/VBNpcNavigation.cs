using System;
using Sandbox;
using Sandbox.Navigation;

/// <summary>Small shared queries and costs. The engine still searches the navigation graph.</summary>
public static class VBNpcNavigation
{
	public const float UnitsPerMetre = 39.37f;
	public static float ClimbMultiplier => ResourceLibrary.Get<Sandbox.Engine.Resources.NavMeshAreaDefinition>( "navigation/npc_climb.navarea" )?.CostMultiplier ?? 2f;
	public static float WalkingCost( float distance ) => MathF.Max( 0f, distance ) / UnitsPerMetre;
	public static float ClimbingCost( float distance, float multiplier = 2f ) => WalkingCost( distance ) * MathF.Max( 1f, multiplier );
	public static float PathLength( NavMeshPath path )
	{
		float length = 0f;
		if ( path.Points is null ) return float.PositiveInfinity;
		for ( int i = 1; i < path.Points.Count; i++ ) length += Vector3.DistanceBetween( path.Points[i - 1].Position, path.Points[i].Position );
		return length;
	}
	public static bool HasLocalMesh( Scene scene, Vector3 point ) => LocalPoint( scene, point ).HasValue;
	private static Vector3? LocalPoint( Scene scene, Vector3 point )
	{
		var nearest = scene.NavMesh.GetClosestPoint( new BBox( point - new Vector3( 32f, 32f, 20f ), point + new Vector3( 32f, 32f, 20f ) ) );
		return nearest.HasValue && MathF.Abs( nearest.Value.z - point.z ) <= 20f ? nearest : null;
	}
	public static bool TryWalkPath( Scene scene, NavMeshAgent agent, Vector3 start, Vector3 target, out NavMeshPath path )
	{
		path = default;
		var from = LocalPoint( scene, start ); var to = LocalPoint( scene, target );
		var supportedTarget = target;
		// Hearing records a muzzle/impact position, which can be above the floor.
		// Resolve its physical support instead of steering towards a point in a wall.
		if ( !to.HasValue )
		{
			var floor = scene.Trace.Ray( target + Vector3.Up * 8f, target - Vector3.Up * 128f ).IgnoreDynamic()
				.WithoutTags( new[] { "player", "npc", "npc_body", "zombie", "npc_ragdoll", "trigger" } ).Run();
			if ( floor.Hit && !floor.StartedSolid && floor.Normal.z > 0.65f )
			{ supportedTarget = floor.EndPosition; to = LocalPoint( scene, supportedTarget ); }
		}
		if ( !from.HasValue || !to.HasValue ) return false;
		var connector = scene.Trace.Ray( start + Vector3.Up * 16f, from.Value + Vector3.Up * 16f )
			.IgnoreGameObjectHierarchy( agent.GameObject ).IgnoreDynamic()
			.WithoutTags( new[] { "player", "npc", "npc_body", "zombie", "npc_ragdoll", "trigger" } ).Run();
		if ( connector.Hit || connector.StartedSolid ) return false;
		var arrival = scene.Trace.Ray( supportedTarget + Vector3.Up * 16f, to.Value + Vector3.Up * 16f )
			.IgnoreGameObjectHierarchy( agent.GameObject ).IgnoreDynamic()
			.WithoutTags( new[] { "player", "npc", "npc_body", "zombie", "npc_ragdoll", "trigger" } ).Run();
		if ( arrival.Hit || arrival.StartedSolid ) return false;
		path = scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = from.Value, Target = to.Value, Agent = agent } );
		return path.Status == NavMeshPathStatus.Complete && path.Points is { Count: > 0 }
			&& Vector3.DistanceBetween( path.Points.Last().Position, to.Value ) < 32f;
	}
}
