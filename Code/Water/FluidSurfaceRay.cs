using System;

namespace VoidBreach.Water;

internal interface IFluidSurfaceSampler
{
	bool SampleSurface( Vector3 position, out float height, out Vector3 normal );
}

/// <summary>Bounded surface intersection, including grazing rays and wave crests above the mean plane.</summary>
internal static class FluidSurfaceRay
{
	public static bool TryHit( IFluidSurfaceSampler surface, Vector3 start, Vector3 end, float meanHeight, float heightRange, out float fraction )
	{
		fraction = 0;
		if ( !float.IsFinite( start.x + start.y + start.z + end.x + end.y + end.z ) ) return false;
		var delta = end - start;
		if ( delta.LengthSquared < 0.0001f ) return false;
		var low = 0f; var high = 1f;
		if ( MathF.Abs( delta.z ) > 0.0001f )
		{
			var a = (meanHeight - heightRange - start.z) / delta.z;
			var b = (meanHeight + heightRange - start.z) / delta.z;
			low = MathF.Max( low, MathF.Min( a, b ) ); high = MathF.Min( high, MathF.Max( a, b ) );
		}
		else if ( MathF.Abs( start.z - meanHeight ) > heightRange ) return false;
		if ( low > high ) return false;
		var t = MathF.Abs( delta.z ) > 0.0001f ? Math.Clamp( (meanHeight - start.z) / delta.z, low, high ) : low;
		for ( var i = 0; i < 5; i++ )
		{
			var p = start + delta * t;
			if ( !surface.SampleSurface( p, out var h, out var normal ) ) break;
			var error = p.z - h;
			if ( MathF.Abs( error ) < 0.25f ) { fraction = t; return true; }
			var derivative = delta.z + (normal.x * delta.x + normal.y * delta.y) / MathF.Max( normal.z, 0.1f );
			if ( MathF.Abs( derivative ) < 0.0001f ) break;
			t -= error / derivative;
			if ( t < low || t > high ) break;
		}
		// Safeguard Newton near crests and at finite-water boundaries. Fixed query budget.
		var previousT = low; var previousError = 0f; var previousValid = false;
		for ( var i = 0; i <= 24; i++ )
		{
			var currentT = low + (high - low) * i / 24;
			var p = start + delta * currentT;
			if ( !surface.SampleSurface( p, out var h, out _ ) ) { previousValid = false; continue; }
			var error = p.z - h;
			if ( MathF.Abs( error ) < 0.25f ) { fraction = currentT; return true; }
			if ( previousValid && previousError * error <= 0 )
			{
				var a = previousT; var b = currentT;
				for ( var j = 0; j < 12; j++ )
				{
					var mid = (a + b) * 0.5f; var point = start + delta * mid;
					if ( !surface.SampleSurface( point, out var height, out _ ) ) return false;
					var e = point.z - height;
					if ( MathF.Abs( e ) < 0.25f ) { fraction = mid; return true; }
					if ( e * previousError > 0 ) a = mid; else b = mid;
				}
				fraction = (a + b) * 0.5f;
				var hit = start + delta * fraction;
				return surface.SampleSurface( hit, out var finalHeight, out _ ) && MathF.Abs( hit.z - finalHeight ) < 0.5f;
			}
			previousT = currentT; previousError = error; previousValid = true;
		}
		return false;
	}
}
