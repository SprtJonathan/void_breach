using System;

namespace VoidBreach.Environment;

/// <summary>Shared weather parameters. Rendering and quality settings stay on each client.</summary>
[Title( "VB Cloud Weather" ), Category( "Void Breach/Environment" ), Icon( "cloud" )]
public sealed class VBCloudWeather : Component, Component.ExecuteInEditor
{
	[Property, Sync( SyncFlags.FromHost ), Group( "Weather" )] public int Seed { get; set; } = 1247;
	[Property, Sync( SyncFlags.FromHost ), Group( "Weather" ), Range( 0, 1 )] public float Coverage { get; set; } = 0.5f;
	[Property, Sync( SyncFlags.FromHost ), Group( "Weather" ), Range( 0, 3 )] public float Density { get; set; } = 1.1f;
	[Property, Sync( SyncFlags.FromHost ), Group( "Weather" ), Description( "World units per second, in the XY plane." )]
	public Vector2 Wind { get; set; } = new( 110, 35 );
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

	internal void WriteAttributes( Sandbox.Rendering.CommandList commands )
	{
		var scale = Finite( ShapeScale, 18000, 1000, 40000 );
		var windX = Finite( Wind.x, 0, -2000, 2000 );
		var windY = Finite( Wind.y, 0, -2000, 2000 );
		// Both weather and shape noises repeat at this period. Reducing in double
		// precision keeps motion smooth during long sessions, including late joins.
		var period = scale * 8.0;
		var clock = WeatherTime;
		if ( !double.IsFinite( clock ) ) clock = 0;
		var motion = new Vector2( (float)(clock * windX % period), (float)(clock * windY % period) );
		var seed = unchecked((uint)Seed);
		var offset = new Vector3( (seed & 255) / 17f, ((seed >> 8) & 255) / 19f, ((seed >> 16) & 255) / 23f );
		var sun = Sun.IsValid() && Sun.Active ? Sun : Scene.GetAllComponents<DirectionalLight>().FirstOrDefault( x => x.Active );
		var direction = sun.IsValid() ? -sun.WorldRotation.Forward : new Vector3( -0.5f, -0.3f, 0.8f ).Normal;
		var sunColor = sun.IsValid() ? sun.LightColor : new Color( 1.5f, 1.4f, 1.2f );
		var a = commands.Attributes;
		a.Set( "CloudLayer", new Vector4( Finite( Altitude, 16000, 1000, 80000 ), Finite( Thickness, 9000, 500, 20000 ), scale, Finite( Density, 1, 0, 3 ) ) );
		a.Set( "CloudWeather", new Vector4( Finite( Coverage, 0.5f, 0, 1 ), Finite( Erosion, 0.4f, 0, 1 ), motion.x, motion.y ) );
		a.Set( "CloudSeed", offset );
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
