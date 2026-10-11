using System;

namespace VoidBreach.Water;

/// <summary>Shared contract for gameplay, buoyancy and future surface implementations.</summary>
public abstract class WaterSurface : Component
{
	[Property, Group( "Physics" )] public int Priority { get; set; }
	[Property, Group( "Physics" ), Range( 1, 15000 )] public float FluidDensity { get; set; } = 1000f;
	public abstract bool TrySample( Vector3 position, out WaterSample sample );
	public virtual void AddRipple( Vector3 position, float strength = 1f ) { }

	/// <summary>Resolve overlaps once per point: priority first, then highest surface.</summary>
	public static bool Find( Scene scene, Vector3 position, out WaterSample sample, float halfHeight = 0 )
	{
		sample = default;
		foreach ( var water in scene.GetAll<WaterSurface>() )
		{
			if ( !water.Active || !water.TrySample( position, out var candidate ) ) continue;
			if ( halfHeight > 0 && candidate.Submersion( position.z, halfHeight ) <= 0 ) continue;
			if ( sample.Surface is null || water.Priority > sample.Surface.Priority
				|| (water.Priority == sample.Surface.Priority && candidate.Height > sample.Height) )
				sample = candidate;
		}
		return sample.Surface is not null;
	}
}

public readonly record struct WaterSample( WaterSurface Surface, float Height, float Bottom,
	Vector3 Normal, Vector3 Velocity )
{
	public float Submersion( float centerHeight, float halfHeight )
	{
		// Clip the sample column against both the waterline and the finite bottom.
		var top = MathF.Min( Height, centerHeight + halfHeight );
		var bottom = MathF.Max( Bottom, centerHeight - halfHeight );
		return Math.Clamp( (top - bottom) / MathF.Max( halfHeight * 2f, 0.01f ), 0f, 1f );
	}
}

/// <summary>Gerstner spectrum shared with vb_water.shader. Queries invert horizontal displacement.</summary>
public static class WaterWaves
{
 public static Vector3 Evaluate(Vector2 position, Vector2 direction, float amplitude,
  float wavelength, float speed, float time, WaterMotion motion = WaterMotion.OceanWater)
 {
  var q = position;
  if (motion == WaterMotion.OceanWater)
   for (var i=0;i<8;i++) { var o=Displacement(q,direction,amplitude,wavelength,speed,time,motion); q=position-new Vector2(o.x,o.y); }
  Field(q,direction,amplitude,wavelength,speed,time,motion,out var displacement,out var tx,out var ty);
  var normal=Vector3.Cross(tx,ty);
  return new Vector3(displacement.z,-normal.x/MathF.Max(normal.z,0.001f),-normal.y/MathF.Max(normal.z,0.001f));
 }
 public static Vector3 Displacement(Vector2 p, Vector2 d, float a, float l, float s, float t, WaterMotion motion = WaterMotion.OceanWater)
 {
  Field(p,d,a,l,s,t,motion,out var offset,out _,out _); return offset;
 }
 private static void Field(Vector2 p, Vector2 d, float a, float l, float s, float t, WaterMotion motion,
  out Vector3 offset,out Vector3 tx,out Vector3 ty)
 {
  offset=Vector3.Zero; tx=new Vector3(1,0,0); ty=new Vector3(0,1,0);
  if(motion!=WaterMotion.OceanWater)
  {
   Add(ref offset,ref tx,ref ty,p,d,a*.6f,l,s*.65f,t,0,0);
   Add(ref offset,ref tx,ref ty,p,new Vector2(-d.y,d.x),a*.25f,l*.61f,s*.48f,t,1.8f,0);
   Add(ref offset,ref tx,ref ty,p,new Vector2(-d.x,-d.y),a*.15f,l*.39f,s*.8f,t,3.2f,0);
   return;
  }
  Add(ref offset,ref tx,ref ty,p,new Vector2(d.x*1.00000000f-d.y*0.00000000f,d.x*0.00000000f+d.y*1.00000000f),a*0.38f,l*1f,s,t,0f,.8f);
  Add(ref offset,ref tx,ref ty,p,new Vector2(d.x*0.75180573f-d.y*0.65938467f,d.x*0.65938467f+d.y*0.75180573f),a*0.25f,l*0.731f,s,t,1.7f,.8f);
  Add(ref offset,ref tx,ref ty,p,new Vector2(d.x*0.61374575f-d.y*-0.78950374f,d.x*-0.78950374f+d.y*0.61374575f),a*0.17f,l*0.479f,s,t,3.1f,.8f);
  Add(ref offset,ref tx,ref ty,p,new Vector2(d.x*-0.29475935f-d.y*0.95557152f,d.x*0.95557152f+d.y*-0.29475935f),a*0.1f,l*0.317f,s,t,0.8f,.8f);
  Add(ref offset,ref tx,ref ty,p,new Vector2(d.x*0.14033161f-d.y*-0.99010456f,d.x*-0.99010456f+d.y*0.14033161f),a*0.06f,l*0.223f,s,t,4.4f,.8f);
  Add(ref offset,ref tx,ref ty,p,new Vector2(d.x*-0.87681789f-d.y*0.48082261f,d.x*0.48082261f+d.y*-0.87681789f),a*0.04f,l*0.157f,s,t,2.3f,.8f);
 }
 private static void Add(ref Vector3 o,ref Vector3 tx,ref Vector3 ty,Vector2 p,Vector2 d,float a,float l,float s,float t,float phase,float chop)
 {
  var k=2*MathF.PI/MathF.Max(l,1); var angle=Vector2.Dot(p,d)*k-MathF.Sqrt(386.09f*k)*s*t+phase;
  var sn=MathF.Sin(angle); var cs=MathF.Cos(angle); var horizontal=MathF.Min(a*chop,.075f/k);
  o+=new Vector3(horizontal*cs*d.x,horizontal*cs*d.y,a*sn);
  tx+=new Vector3(-horizontal*k*sn*d.x*d.x,-horizontal*k*sn*d.y*d.x,a*k*cs*d.x);
  ty+=new Vector3(-horizontal*k*sn*d.x*d.y,-horizontal*k*sn*d.y*d.y,a*k*cs*d.y);
 }
}
