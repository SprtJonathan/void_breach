using System;

namespace VoidBreach.Water;

/// <summary>Entity-independent contact estimates, shared by physical and transform-driven sources.</summary>
public static class FluidContactMath
{
	public static float Strength( float horizontalSpeed, float verticalSpeed, float mass, float immersion, bool impact, float sensitivity )
	{
		if ( !float.IsFinite( horizontalSpeed + verticalSpeed + mass + immersion + sensitivity ) ) return 0;
		var inertia = Math.Clamp( MathF.Sqrt( MathF.Max( mass, 0.1f ) / 80 ), 0.25f, 3 );
		var forcing = horizontalSpeed * sensitivity + MathF.Abs( verticalSpeed ) * sensitivity * (impact ? 2 : 0.5f);
		return Math.Clamp( forcing * inertia * Math.Clamp( immersion * 3, 0.15f, 1 ), 0, 150 );
	}
	public static bool Crossed( float previousBottom, float previousTop, float bottom, float top, float height )
		=> (previousBottom > height && bottom <= height) || (previousTop < height && top >= height);
	public static int TrailSamples( float distance, float radius ) => Math.Clamp( (int)MathF.Ceiling( distance / MathF.Max( radius * 0.6f, 4 ) ), 1, 8 );
}
