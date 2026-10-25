using System;
using VoidBreach.Ocean;

namespace VoidBreach.Water;

public enum FluidImpactKind { Custom, Shot, Entry, Exit, Wake, Explosion }

/// <summary>Connects arbitrary world effects or trace segments to the shared surface simulation.</summary>
public static class FluidImpacts
{
	public static bool Splash( Scene scene, Vector3 position, float radius, float strength )
	{
		if ( !WaterSurface.Find( scene, position, out var sample ) || sample.Surface is not VBFFTOcean fft ) return false;
		return fft.EmitSurfaceImpact( position.WithZ( sample.Height ), radius, strength );
	}
	public static bool Segment( Scene scene, Vector3 start, Vector3 end, float radius = 14, float strength = 110 )
	{
		if ( !float.IsFinite( start.x + start.y + start.z + end.x + end.y + end.z ) ) return false;
		var delta = end - start;
		if ( delta.LengthSquared < 1 ) return false;
		var nearest = float.MaxValue; VBFFTOcean selected = null; var hit = Vector3.Zero;
		foreach ( var water in scene.GetAll<VBFFTOcean>() )
		{
			if ( !water.Active || !water.Interactions ) continue;
			var range = water.Shape == FftWaterShape.River ? 20000 : water.WaveHeight * FftOceanSpectrum.UnitsPerMeter * 2 + 32;
			if ( !FluidSurfaceRay.TryHit( water, start, end, water.WorldPosition.z, range, out var t ) || t >= nearest ) continue;
			nearest = t; selected = water; hit = start + delta * t;
		}
		return selected.IsValid() && selected.EmitSurfaceImpact( hit, radius, strength, FluidImpactKind.Shot, delta.Normal * 40 );
	}
}
