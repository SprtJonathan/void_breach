using System;
using Sandbox;

public abstract partial class VBVehicleSeat
{
	private static readonly string[] ExitIgnoredTags = { "trigger", "water" };

	private Vector3 FindExitPosition( PlayerController player )
	{
		// Prefer authored doors and ground when available. Deep water and airborne
		// vehicles need only clearance, never a ground hit or a speed threshold.
		bool cabinExit = this is VBVehicleDriverSeat driver && driver.Cabin.IsValid();
		if ( ExitPoints is not null )
			foreach ( var exit in ExitPoints )
				if ( exit.IsValid() && TryExitCandidate( player, exit.WorldPosition, out var position, cabinExit ) ) return position;

		var hull = Vehicle.Body.IsValid() ? Vehicle.Body.GetWorldBounds() : new BBox( WorldPosition - 64, WorldPosition + 64 );
		var margin = MathF.Max( player.BodyRadius, 1f ) + 8f;
		var center = hull.Center;
		// World-space bounds also work when the car is on its side or upside down.
		for ( var level = 0; level < 3; level++ )
		{
			var z = level == 0 ? center.z : hull.Maxs.z + 8f + (level - 1) * player.BodyHeight;
			for ( var side = 0; side < 8; side++ )
			{
				var angle = side * MathF.PI / 4f;
				var point = new Vector3( center.x + MathF.Cos( angle ) * (hull.Size.x * 0.5f + margin),
					center.y + MathF.Sin( angle ) * (hull.Size.y * 0.5f + margin), z );
				if ( TryExitCandidate( player, point, out var position ) ) return position;
			}
			if ( TryExitCandidate( player, center.WithZ( hull.Maxs.z + 8f + level * player.BodyHeight ), out var roof ) ) return roof;
		}
		// A fully encased vehicle has no collision-free physical exit. Still release
		// the seat: being trapped in geometry must not permanently lock its controls.
		return center.WithZ( hull.Maxs.z + 8f );
	}

	private bool TryExitCandidate( PlayerController player, Vector3 candidate, out Vector3 result, bool cabinExit = false )
	{
		result = candidate;
		var groundTrace = Scene.Trace.Ray( candidate + Vector3.Up * 32f, candidate + Vector3.Down * 96f )
			.IgnoreGameObjectHierarchy( player.GameObject ).WithoutTags( ExitIgnoredTags );
		if ( !cabinExit ) groundTrace = groundTrace.IgnoreGameObjectHierarchy( Vehicle.GameObject );
		var ground = groundTrace.Run();
		if ( ground.Hit && !ground.StartedSolid && ground.Normal.z >= 0.65f )
		{
			var grounded = ground.EndPosition + Vector3.Up * 2f;
			if ( HasExitClearance( player, grounded, cabinExit ) ) { result = grounded; return true; }
		}
		return HasExitClearance( player, candidate, cabinExit );
	}

	private bool HasExitClearance( PlayerController player, Vector3 position, bool cabinExit = false )
	{
		var radius = MathF.Max( player.BodyRadius, 1f );
		var height = MathF.Max( player.BodyHeight, radius * 2f );
		if ( cabinExit )
		{
			// Match the native capsule and lower box. A full-width world-aligned
			// box rejects a narrow aisle when the bus is parked diagonally.
			var geometry = new VBTraversalGeometry( player.GameObject, radius, height );
			if ( !geometry.PathClear( position, position ) ) return false;
		}
		else
		{
			var bounds = new BBox( new Vector3( -radius, -radius, 0 ), new Vector3( radius, radius, height ) );
			var clearance = Scene.Trace.Box( bounds, position, position ).IgnoreGameObjectHierarchy( player.GameObject )
				.WithoutTags( ExitIgnoredTags ).Run();
			if ( clearance.Hit || clearance.StartedSolid ) return false;
		}
		var offset = Vector3.Up * (height * 0.5f);
		var route = Scene.Trace.Sphere( radius, player.WorldPosition + offset, position + offset )
			.IgnoreGameObjectHierarchy( Vehicle.GameObject ).IgnoreGameObjectHierarchy( player.GameObject )
			.WithoutTags( ExitIgnoredTags ).Run();
		return !route.Hit && !route.StartedSolid;
	}
}
