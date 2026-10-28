using System;
using System.Collections.Generic;

namespace VoidBreach.Environment;

public enum VBRainQuality { Off, Low, Medium, High }

/// <summary>Client-only, bounded rain paths and impact sprites. No networked drops or physics bodies.</summary>
[Title( "VB Precipitation" ), Category( "Void Breach/Environment" ), Icon( "rainy" )]
public sealed partial class VBPrecipitation : Component, Component.ExecuteInEditor
{
	[Property] public VBCloudWeather Weather { get; set; }
	[Property] public VBRainQuality Quality { get; set; } = VBRainQuality.Medium;
	[Property] public bool PreviewInEditor { get; set; } = true;
	[Property, Group( "Volume" ), Range( 128, 2000 )] public float Radius { get; set; } = 700;
	[Property, Group( "Volume" ), Range( 128, 2000 )] public float Height { get; set; } = 550;
	[Property, Group( "Volume" ), Range( 128, 4000 )] public float Depth { get; set; } = 1000;
	[Property, Group( "Motion" ), Range( 400, 4000 )] public float FallSpeed { get; set; } = 1800;
	[Property, Group( "Motion" ), Range( 0, 4 )] public float WindInfluence { get; set; } = 1;
	[Property, Group( "Appearance" )] public Material DropMaterial { get; set; }
	[Property, Group( "Appearance" )] public Material SplashMaterial { get; set; }
	[Property, Group( "Appearance" )] public Color Tint { get; set; } = new( 0.65f, 0.75f, 0.85f, 0.45f );
	[Property, Group( "Appearance" ), Range( 4, 80 )] public float StreakLength { get; set; } = 28;
	[Property, Group( "Appearance" ), Range( 0.2f, 3 )] public float StreakWidth { get; set; } = 0.7f;
	[Property, Group( "Appearance" ), Range( 0, 3 ), Description( "Local background refraction in pixels; this only affects visible drops." )] public float RefractionPixels { get; set; } = 1.25f;
	[Property, Group( "Appearance" ), Range( 0, 1 )] public float OpticalOpacity { get; set; } = 0.8f;
	[Property, Group( "Appearance" ), Range( 0, 1 )] public float LightScattering { get; set; } = 0.3f;
	[Property, Group( "Impacts" )] public bool Splashes { get; set; } = true;
	[Property, Group( "Impacts" ), Range( 0, 1 )] public float SplashChance { get; set; } = 0.35f;
	[Property, Group( "Impacts" ), Range( 1, 12 )] public float SplashSize { get; set; } = 4;
	[Property, Group( "Impacts" ), Range( 0, 4 )] public float SplashDensity { get; set; } = 1;
	[Property, Group( "Impacts" ), Range( 0.2f, 1 )] public float SplashLifetime { get; set; } = 0.45f;
	[Property, Group( "Shelter" ), Description( "Collision tags ignored by sky and rain paths. Roofs and walls need colliders. Tag nonblocking foliage rain_passthrough." )]
	public string[] IgnoreTags { get; set; } = new[] { "player", "npc", "debris", "trigger", "rain_passthrough" };

	public int ActiveDrops => _drops.Count;
	public int ActiveSplashes => _splashes.Count;
	public int LastTraceCount { get; private set; }
	public int LastDrawCalls { get; private set; }
	public int LastSceneColorCopies { get; private set; }
	public int BlockedSpawns { get; private set; }
	public Vector3 RainVelocity { get; private set; }
	public Vector3 RenderingCenter => _lastCamera;
	public static (int Drops, int Splashes, int TracesPerSecond, int TracesPerFrame) Budget( VBRainQuality quality ) => quality switch
	{
		VBRainQuality.Low => (768, 192, 180, 8),
		VBRainQuality.Medium => (2048, 512, 420, 16),
		VBRainQuality.High => (4096, 1024, 800, 24),
		_ => (0, 0, 0, 0)
	};

	private struct Drop
	{
		public Vector3 Start, End;
		public float Age, Lifetime;
	}
	private struct Splash
	{
		public Vector3 Position, Normal;
		public float Age, Seed;
	}
	private struct RainColumn
	{
		public int X, Y, Tier;
		public Vector3 Start, End, Velocity;
		public Collider Blocker;
		public double CheckedAt;
		public float ReferenceHeight, CloudBase, CellSize;
		public bool Valid, Hit;
	}
	// Reserve storage only once a rendering client actually needs rain.
	private readonly List<Drop> _drops = new();
	private readonly List<Splash> _splashes = new();
	private readonly VertexBuffer _vertices = new();
	private SceneCustomObject _renderObject;
	private Material _dropMaterial, _splashMaterial;
	private float _traceTokens;
	private Vector3 _lastCamera;
	private bool _hasCamera;
	private double _rainClock;
	private float _rainIntensity;
	private Vector3 _editorViewPosition, _editorSourcePosition;
	private bool _hasEditorView;

	protected override void OnUpdate()
	{
		LastTraceCount = 0;
		var budget = Budget( Quality );
		if ( Application.IsDedicatedServer || Application.IsHeadless || budget.Drops == 0 || (Scene.IsEditor && !PreviewInEditor) )
		{
			Release(); return;
		}
		if ( !Weather.IsValid() ) Weather = GetComponent<VBCloudWeather>() ?? Scene.GetAllComponents<VBCloudWeather>().FirstOrDefault( x => x.Active );
		var camera = Scene.Camera;
		if ( !camera.IsValid() || !camera.Active || camera.Orthographic || !Weather.IsValid() || !Weather.Active ) { Release(); return; }
		var cameraPosition = camera.WorldPosition;
		if ( Scene.IsEditor )
		{
			// Scene.Camera supplies settings, but the free editor viewport has its
			// own transform. Remember its actual render view for the next update.
			if ( _hasEditorView && cameraPosition.Distance( _editorSourcePosition ) < 0.01f ) cameraPosition = _editorViewPosition;
			else _editorViewPosition = cameraPosition;
			_editorSourcePosition = camera.WorldPosition;
		}
		var intensity = Weather.Precipitation;
		if ( intensity <= 0.001f || cameraPosition.z >= VBCloudWeather.Finite( Weather.Altitude, 16000, 1000, 80000 ) ) { Release(); return; }
		var dt = Math.Clamp( Time.Delta, 0, 0.05f ); // No catch-up storm after a hitch.
		_rainClock += dt;
		_rainIntensity = intensity;
		_drops.EnsureCapacity( budget.Drops );
		_splashes.EnsureCapacity( budget.Splashes );
		var radius = VBCloudWeather.Finite( Radius, 700, 128, 2000 );
		if ( _hasCamera && cameraPosition.Distance( _lastCamera ) > radius ) { _drops.Clear(); _splashes.Clear(); }
		_lastCamera = cameraPosition; _hasCamera = true;
		var wind = Weather.CurrentWind * VBCloudWeather.Finite( WindInfluence, 1, 0, 4 );
		RainVelocity = new Vector3( wind.x, wind.y, -VBCloudWeather.Finite( FallSpeed, 1800, 400, 4000 ) );
		_traceTokens = MathF.Min( budget.TracesPerFrame, _traceTokens + dt * budget.TracesPerSecond );
		UpdateDistantRain( camera, cameraPosition, budget.TracesPerFrame );
		UpdateUniformNearRain( cameraPosition, radius, intensity, budget.Drops, budget.TracesPerFrame );
		UpdateGroundImpacts( cameraPosition, intensity, budget.Splashes, budget.TracesPerFrame );
		EnsureRenderer();
		Weather.WriteAttributes( _renderObject.Attributes );
		_renderObject.Attributes.Set( "CloudNoiseAtlas", Texture.Load( "environment/textures/cloud_noise.tga" ) );
		_renderObject.Attributes.Set( "CloudWeatherMap", Texture.Load( "environment/textures/cloud_weather.tga" ) );
		_renderObject.Attributes.Set( "RainVelocity", RainVelocity );
		_renderObject.Attributes.Set( "RainRefractionPixels", VBCloudWeather.Finite( RefractionPixels, 1.25f, 0, 3 ) );
		_renderObject.Attributes.Set( "RainOpticalOpacity", VBCloudWeather.Finite( OpticalOpacity, 0.8f, 0, 1 ) );
		_renderObject.Attributes.Set( "RainLightScattering", VBCloudWeather.Finite( LightScattering, 0.3f, 0, 1 ) );
		var padding = new Vector3( radius + 128, radius + 128, VBCloudWeather.Finite( Height, 550, 128, 2000 ) + VBCloudWeather.Finite( Depth, 1000, 128, 4000 ) + 128 );
		_renderObject.Bounds = new BBox( cameraPosition - padding, cameraPosition + padding );
		ExpandDistantBounds( cameraPosition );
		// The editor camera can jump outside the previous field. Keep its render
		// callback discoverable, so the next update can recenter the bounded field.
		if ( Scene.IsEditor ) _renderObject.Bounds = new BBox( new Vector3( -10000000 ), new Vector3( 10000000 ) );
	}

	private bool CanTrace( int count, int frameLimit )
	{
		if ( _traceTokens < count || LastTraceCount + count > frameLimit ) return false;
		_traceTokens -= count;
		return true;
	}
	private float CacheLifetime => VBCloudWeather.Finite( ShelterRefreshSeconds, 2, 0.25f, 5 ) + 2;
	private static float HashUnit( uint hash ) => (hash & 0xffffff) / 16777216f;
	private static float Frac( double value ) => (float)(value - Math.Floor( value ));
	private static int WrappedCell( int minimum, int slot, int side ) => minimum + ((slot - minimum) % side + side) % side;
	private static uint ColumnHash( int x, int y, int tier )
	{
		unchecked
		{
			var h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)tier * 2246822519u;
			h = (h ^ (h >> 13)) * 1274126177u;
			return h ^ (h >> 16);
		}
	}
	private void EnsureRenderer()
	{
		_dropMaterial = DropMaterial ?? Material.Load( "environment/materials/rain_drop.vmat" );
		_splashMaterial = SplashMaterial ?? Material.Load( "environment/materials/rain_splash.vmat" );
		if ( _renderObject is not null ) return;
		_renderObject = new SceneCustomObject( Scene.SceneWorld );
		_renderObject.Flags.IsOpaque = false; _renderObject.Flags.IsTranslucent = true;
		_renderObject.Flags.CastShadows = false; _renderObject.Flags.IncludeInCubemap = false;
		_renderObject.RenderOverride = RenderRain;
	}
	private void RenderRain( SceneObject obj )
	{
		if ( Scene.IsEditor ) { _editorViewPosition = Graphics.CameraPosition; _hasEditorView = true; }
		LastDrawCalls = 0;
		LastSceneColorCopies = 0;
		_vertices.Init( true );
		var length = VBCloudWeather.Finite( StreakLength, 28, 4, 80 );
		var width = VBCloudWeather.Finite( StreakWidth, 0.7f, 0.2f, 3 );
		var radius = VBCloudWeather.Finite( Radius, 700, 128, 2000 );
		var opacity = Tint.a * MathF.Min( _rainIntensity, 1 );
		var splashLifetime = VBCloudWeather.Finite( SplashLifetime, 0.45f, 0.2f, 1 );
		var splashSize = VBCloudWeather.Finite( SplashSize, 4, 1, 12 ) * 0.6f;
		AppendDistantRain( length, width );
		foreach ( var drop in _drops )
		{
			var delta = drop.End - drop.Start;
			var head = Vector3.Lerp( drop.Start, drop.End, Math.Clamp( drop.Age / drop.Lifetime, 0, 1 ) );
			var distance = head.Distance( Graphics.CameraPosition );
			var tail = head - delta.Normal * MathF.Min( MathF.Min( length, distance * 0.07f ), head.Distance( drop.Start ) );
			var side = Vector3.Cross( delta.Normal, Graphics.CameraPosition - head ).Normal * MathF.Min( width, MathF.Max( 0.12f, distance * 0.0015f ) );
			var horizontal = new Vector2( head.x - _lastCamera.x, head.y - _lastCamera.y ).Length;
			var distanceFade = Math.Clamp( distance / 64, 0, 1 ) * Math.Clamp( (radius - horizontal) / (radius * 0.2f), 0, 1 );
			Quad( tail - side, tail + side, head + side, head - side, Tint.WithAlpha( opacity * distanceFade ), Vector3.Up );
		}
		// Return this view's copy to the render-target pool after both draws.
		// Refraction remains restricted to depth-tested rain/impact geometry.
		using var sceneColorCopy = _drops.Count > 0 || LastDistantQuads > 0 || _splashes.Count > 0
			? Graphics.GrabFrameTexture( "FrameBufferCopyTexture", obj.Attributes ) : null;
		LastSceneColorCopies = sceneColorCopy is not null ? 1 : 0;
		if ( _drops.Count > 0 || LastDistantQuads > 0 )
		{
			_vertices.Draw( _dropMaterial, obj.Attributes ); LastDrawCalls++;
		}
		_vertices.Init( true );
		foreach ( var splash in _splashes )
		{
			var t = splash.Age / splashLifetime;
			var size = splashSize * (0.5f + t * 0.5f);
			var u = Vector3.Cross( splash.Normal, Vector3.Forward ).Normal * size;
			if ( u.Length < 0.01f ) u = Vector3.Cross( splash.Normal, Vector3.Right ).Normal * size;
			var v = Vector3.Cross( splash.Normal, u ).Normal * size;
			var p = splash.Position;
			var seed = splash.Seed;
			var rotatedU = u * MathF.Cos( seed * MathF.Tau ) + v * MathF.Sin( seed * MathF.Tau );
			v = Vector3.Cross( splash.Normal, rotatedU ).Normal * size; u = rotatedU;
			var alpha = opacity * 0.8f;
			Quad( p - u - v, p + u - v, p + u + v, p - u + v, Tint.WithAlpha( alpha ), splash.Normal, new Vector2( t, seed ) );
			// Millisecond-scale ballistic microdroplets, with no cartoon ring/crown.
			if ( splash.Age >= 0.12f ) continue;
			for ( var bead = 0; bead < 3; bead++ )
			{
				var angle = seed * MathF.Tau + bead * 2.399963f;
				var velocity = 32 + Frac( seed * 7.3 + bead * 0.61 ) * 25;
				var height = MathF.Max( 0, velocity * splash.Age - 400 * splash.Age * splash.Age );
				var center = p + splash.Normal * height + (u * MathF.Cos( angle ) + v * MathF.Sin( angle )) * splash.Age * 10;
				var right = Graphics.CameraRotation.Right * 0.16f;
				var up = Graphics.CameraRotation.Up * (0.18f + splash.Age * 2);
				Quad( center - right - up, center + right - up, center + right + up, center - right + up,
					Tint.WithAlpha( alpha * (1 - splash.Age / 0.12f) ), splash.Normal, new Vector2( t + 2, seed ) );
			}
		}
		if ( _splashes.Count > 0 ) { _vertices.Draw( _splashMaterial, obj.Attributes ); LastDrawCalls++; }
	}
	private void Quad( Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 normal, Vector2 data = default )
	{
		Vertex V( Vector3 p, float x, float y ) => new( p, normal, Vector3.Forward, new Vector4( x, y, 0, 0 ) ) { Color = color, TexCoord1 = new Vector4( data.x, data.y, 0, 0 ) };
		_vertices.AddQuad( V( a, 0, 0 ), V( b, 1, 0 ), V( c, 1, 1 ), V( d, 0, 1 ) );
	}
	private void Release()
	{
		if ( _renderObject is not null ) { _renderObject.RenderOverride = null; _renderObject.Delete(); _renderObject = null; }
		_drops.Clear(); _splashes.Clear(); _vertices.Clear(); _traceTokens = 0; _hasCamera = false; LastDrawCalls = 0;
		ReleaseDistantRain(); ReleaseNearRain(); ReleaseGroundImpacts();
		LastSceneColorCopies = 0;
		_hasEditorView = false;
	}
	protected override void OnDisabled() => Release();
	protected override void OnDestroy() => Release();
}
