using System;

namespace VoidBreach.Environment;

public enum VBRainDistribution { Everywhere, CloudBanks }

/// <summary>Shared weather parameters. Rendering and quality settings stay on each client.</summary>
[Title( "VB Cloud Weather" ), Category( "Void Breach/Environment" ), Icon( "cloud" )]
public sealed class VBCloudWeather : Component, Component.ExecuteInEditor
{
	[Property, Sync( SyncFlags.FromHost ), Group( "Weather" )] public int Seed { get; set; } = 1247;
	[Property, Sync( SyncFlags.FromHost ), Group( "Weather" ), Range( 0, 1 )] public float Coverage { get; set; } = 0.5f;
	[Property, Sync( SyncFlags.FromHost ), Group( "Weather" ), Range( 0, 3 )] public float Density { get; set; } = 1.1f;
	[Property, Sync( SyncFlags.FromHost ), Group( "Weather" ), Description( "World units per second, in the XY plane." )]
	public Vector2 Wind { get; set; } = new( 110, 35 );
	[Property, Sync( SyncFlags.FromHost ), Group( "Rain" ), Range( 0, 10 ), Description( "0 = dry, 1 = shower, 4 = heavy rain, 8–10 = tropical downpour." )] public float RainIntensity { get; set; }
	[Property, Sync( SyncFlags.FromHost ), Group( "Rain" ), Description( "Everywhere forms an unbroken rain cloud deck across the map. CloudBanks allows local dry gaps." )]
	public VBRainDistribution RainDistribution { get; set; } = VBRainDistribution.Everywhere;
	[Property, Sync( SyncFlags.FromHost ), Group( "Rain" ), Range( 0.5f, 1 )] public float RainCloudCoverage { get; set; } = 0.85f;
	[Property, Sync( SyncFlags.FromHost ), Group( "Rain" ), Range( 0.5f, 3 )] public float RainCloudDensity { get; set; } = 1.2f;
	[Sync( SyncFlags.FromHost )] public double TransitionStart { get; set; } = -1;
	[Sync( SyncFlags.FromHost )] public float TransitionSeconds { get; set; }
	[Sync( SyncFlags.FromHost )] public Vector3 TransitionFrom { get; set; }
	[Sync( SyncFlags.FromHost )] public Vector2 TransitionWind { get; set; }
	[Sync( SyncFlags.FromHost )] public Vector2 TransitionMotion { get; set; }
	[Property, Sync( SyncFlags.FromHost ), Group( "Layer" ), Range( 1000, 80000 )] public float Altitude { get; set; } = 16000;
	[Property, Sync( SyncFlags.FromHost ), Group( "Layer" ), Range( 500, 20000 )] public float Thickness { get; set; } = 9000;
	[Property, Sync( SyncFlags.FromHost ), Group( "Layer" ), Range( 1000, 40000 )] public float ShapeScale { get; set; } = 18000;
	[Property, Sync( SyncFlags.FromHost ), Group( "Layer" ), Range( 0, 1 )] public float Erosion { get; set; } = 0.4f;
	[Property, Group( "Lighting" )] public DirectionalLight Sun { get; set; }
	[Property, Group( "Lighting" )] public Color AmbientColor { get; set; } = new( 0.3f, 0.4f, 0.58f );
	[Property, Group( "Lighting" ), Range( 0, 4 )] public float AmbientStrength { get; set; } = 0.65f;
	[Property, Group( "Lighting" ), Range( 0, 4 )] public float SunStrength { get; set; } = 1.2f;

	/// <summary>Override only for a frozen editor preview or reproducible capture. Negative uses scene time.</summary>
	[Property, Group( "Preview" )] public double PreviewTime { get; set; } = -1;

	public double WeatherTime => Scene.IsEditor && PreviewTime >= 0 ? PreviewTime : Time.NowDouble;
	private float TransitionFraction => TransitionStart < 0 || TransitionSeconds <= 0 ? 1 :
		(float)Math.Clamp( (WeatherTime - TransitionStart) / TransitionSeconds, 0, 1 );
	private float Blend => TransitionFraction * TransitionFraction * (3 - 2 * TransitionFraction);
	private float RainCloudAmount => Math.Clamp( CurrentRain / MathF.Max( MathF.Max( Finite( RainIntensity, 0, 0, 10 ), TransitionStart >= 0 && TransitionFraction < 1 ? TransitionFrom.z : 0 ), 0.001f ) * 4, 0, 1 );
	public float CurrentRain => MathX.Lerp( TransitionFrom.z, Finite( RainIntensity, 0, 0, 10 ), Blend );
	public float RainDeck => RainDistribution == VBRainDistribution.Everywhere ? RainCloudAmount : 0;
	public Vector2 CurrentWind => Vector2.Lerp( TransitionWind, new Vector2( Finite( Wind.x, 0, -2000, 2000 ), Finite( Wind.y, 0, -2000, 2000 ) ), Blend );
	public float CurrentCoverage => MathF.Max( MathX.Lerp( TransitionFrom.x, Finite( Coverage, 0.5f, 0, 1 ), Blend ),
		Finite( RainCloudCoverage, 0.85f, 0.5f, 1 ) * RainCloudAmount );
	public float CurrentDensity => MathF.Max( MathX.Lerp( TransitionFrom.y, Finite( Density, 1.1f, 0, 3 ), Blend ),
		Finite( RainCloudDensity, 1.2f, 0.5f, 3 ) * RainCloudAmount );
	// Form the cloud layer before showing drops. Overcast with RainIntensity = 0 stays dry.
	public float Precipitation => CurrentRain * Smooth( 0.45f, 0.75f, CurrentCoverage ) * Smooth( 0.1f, 0.7f, CurrentDensity );
	private static float Smooth( float low, float high, float value )
	{
		var t = Math.Clamp( (value - low) / (high - low), 0, 1 );
		return t * t * (3 - 2 * t);
	}

	/// <summary>Host API. Replicates endpoints once; every client evaluates the same smooth transition, including late joins.</summary>
	public void SetWeather( float coverage, float density, float rain, Vector2 wind, float seconds = 10 )
	{
		if ( Game.IsPlaying && !Networking.IsHost ) return;
		var previous = new Vector3( CurrentCoverage, CurrentDensity, CurrentRain );
		var previousWind = CurrentWind;
		var previousMotion = CloudMotion;
		Coverage = Finite( coverage, 0.5f, 0, 1 ); Density = Finite( density, 1.1f, 0, 3 );
		RainIntensity = Finite( rain, 0, 0, 10 );
		Wind = new Vector2( Finite( wind.x, 0, -2000, 2000 ), Finite( wind.y, 0, -2000, 2000 ) );
		TransitionFrom = previous; TransitionWind = previousWind; TransitionMotion = previousMotion;
		TransitionSeconds = Finite( seconds, 10, 0, 3600 ); TransitionStart = WeatherTime;
	}

	private Vector2 CloudMotion
	{
		get
		{
			var clock = double.IsFinite( WeatherTime ) ? WeatherTime : 0;
			var period = Finite( ShapeScale, 18000, 1000, 40000 ) * 8.0;
			var wind = CurrentWind;
			if ( TransitionStart < 0 ) return new( (float)(clock * wind.x % period), (float)(clock * wind.y % period) );
			var elapsed = Math.Max( 0, clock - TransitionStart );
			var t = TransitionFraction;
			// Integral of smoothstep: t^3 - t^4/2. Preserve cloud position when the wind changes.
			var integral = TransitionSeconds * (t * t * t - 0.5 * t * t * t * t) + Math.Max( 0, elapsed - TransitionSeconds );
			var target = new Vector2( Finite( Wind.x, 0, -2000, 2000 ), Finite( Wind.y, 0, -2000, 2000 ) );
			return new( (float)((TransitionMotion.x + TransitionWind.x * elapsed + (target.x - TransitionWind.x) * integral) % period),
				(float)((TransitionMotion.y + TransitionWind.y * elapsed + (target.y - TransitionWind.y) * integral) % period) );
		}
	}

	private void ShapeAttributes( out Vector4 layer, out Vector4 weather, out Vector3 seedOffset )
	{
		var scale = Finite( ShapeScale, 18000, 1000, 40000 );
		// Both weather and shape noises repeat at this period. Reducing in double
		// precision keeps motion smooth during long sessions, including late joins.
		var motion = CloudMotion;
		var seed = unchecked((uint)Seed);
		var offset = new Vector3( (seed & 255) / 17f, ((seed >> 8) & 255) / 19f, ((seed >> 16) & 255) / 23f );
		layer = new Vector4( Finite( Altitude, 16000, 1000, 80000 ), Finite( Thickness, 9000, 500, 20000 ), scale, CurrentDensity );
		weather = new Vector4( CurrentCoverage, Finite( Erosion, 0.4f, 0, 1 ), motion.x, motion.y );
		seedOffset = offset;
	}

	internal void WriteAttributes( RenderAttributes attributes )
	{
		ShapeAttributes( out var layer, out var weather, out var seed );
		attributes.Set( "CloudLayer", layer ); attributes.Set( "CloudWeather", weather ); attributes.Set( "CloudSeed", seed );
		attributes.Set( "CloudRainDeck", RainDeck );
	}

	internal void WriteAttributes( Sandbox.Rendering.CommandList commands )
	{
		ShapeAttributes( out var layer, out var weather, out var seed );
		var sun = Sun.IsValid() && Sun.Active ? Sun : Scene.GetAllComponents<DirectionalLight>().FirstOrDefault( x => x.Active );
		var direction = sun.IsValid() ? -sun.WorldRotation.Forward : new Vector3( -0.5f, -0.3f, 0.8f ).Normal;
		var sunColor = sun.IsValid() ? sun.LightColor : new Color( 1.5f, 1.4f, 1.2f );
		var a = commands.Attributes;
		a.Set( "CloudLayer", layer ); a.Set( "CloudWeather", weather ); a.Set( "CloudSeed", seed );
		a.Set( "CloudRainDeck", RainDeck );
		a.Set( "CloudSunDirection", direction );
		a.Set( "CloudSunColor", new Vector3( sunColor.r, sunColor.g, sunColor.b ) * Finite( SunStrength, 1.2f, 0, 4 ) );
		a.Set( "CloudAmbient", new Vector3( AmbientColor.r, AmbientColor.g, AmbientColor.b ) * Finite( AmbientStrength, 0.65f, 0, 4 ) );
	}

	internal static float Finite( float value, float fallback, float min, float max ) => Math.Clamp( float.IsFinite( value ) ? value : fallback, min, max );

	protected override void OnStart()
	{
		if ( Game.IsPlaying && Networking.IsHost && GameObject.NetworkMode != NetworkMode.Object )
			Log.Warning( "VB Cloud Weather: set this object's Network Mode to Object to replicate live weather changes. Scene time already follows the host." );
	}
}
