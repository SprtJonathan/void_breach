using System;

namespace VoidBreach.Water;

public enum WaterShape { Rectangle, Ellipse, River }
public enum WaterMotion { Automatic, PondWater, RiverWater, OceanWater }
public enum WaterDebugView { None, Normals, Foam, Reflection, Thickness }

/// <summary>One editable surface for ponds, bounded oceans and spline rivers.</summary>
[Title( "VB Water Body" ), Category( "Void Breach/Water" ), Icon( "water" )]
public sealed partial class VBWaterBody : WaterSurface, Component.ExecuteInEditor
{
	[Property, Group( "Shape" )] public WaterShape Shape { get; set; } = WaterShape.Ellipse;
	[Property, Group( "Motion" )] public WaterMotion Motion { get; set; } = WaterMotion.Automatic;
	[Property, Group( "Debug" )] public WaterDebugView DebugView { get; set; }
	public WaterMotion MotionType => Motion != WaterMotion.Automatic ? Motion : Shape == WaterShape.River ? WaterMotion.RiverWater : Shape == WaterShape.Ellipse ? WaterMotion.PondWater : WaterMotion.OceanWater;
	[Property, Group( "Motion" ), Range( 0, 2 )] public float PondAgitation { get; set; } = 0.35f;
	[Property, Group( "Motion" ), Range( 0, 2 )] public float RiverTurbulence { get; set; } = 0.6f;
	[Property, Group( "Ocean" ), Description( "Unbounded rectangular water with a world-aligned lattice and distant horizon skirt. Use one ocean per scene; give local ponds a higher priority." )]
	public bool InfiniteOcean { get; set; }
	[Property, Group( "Ocean" ), Range( 16384, 500000 )] public float HorizonDistance { get; set; } = 200000;
	private bool IsInfinite => InfiniteOcean && Shape == WaterShape.Rectangle;
	[Property, Group( "Shape" )] public Vector2 Size { get; set; } = new( 1800, 1200 );
	[Property, Group( "Shape" ), Range( 8, 160 )] public int Resolution { get; set; } = 64;
	[Property, Group( "Shape" ), Range( 1, 10000 )] public float Depth { get; set; } = 240;
	[Property, Group( "Shape" )] public bool ShowOutline { get; set; } = true;
	[Property, Group( "River" ), Description( "Ordered local-space centerline. Keep bends wider than the river width; no self intersections." )]
	public List<Vector3> RiverPoints { get; set; } = new() { new( -900, 0, 0 ), new( -300, 150, 0 ), new( 300, -100, -12 ), new( 900, 0, -24 ) };
	[Property, Group( "River" ), Range( 16, 4096 )] public float RiverWidth { get; set; } = 320;
	[Property, Group( "River" ), Range( 2, 32 )] public int RiverSubdivisions { get; set; } = 12;
	[Property, Group( "Waves" ), Range( 0, 200 )] public float WaveAmplitude { get; set; } = 2;
	[Property, Group( "Waves" ), Range( 32, 8192 )] public float WaveLength { get; set; } = 360;
	[Property, Group( "Waves" ), Range( 0, 4 )] public float WaveSpeed { get; set; } = 0.55f;
	[Property, Group( "Waves" )] public Vector2 WindDirection { get; set; } = new( 1, 0.3f );
	[Property, Group( "Waves" ), Range( -512, 512 )] public float CurrentSpeed { get; set; } = 3;
	[Property, Group( "Appearance" )] public Material SurfaceMaterial { get; set; }
	[Property, Group( "Appearance" )] public Color ShallowColor { get; set; } = new( 0.12f, 0.23f, 0.17f );
	[Property, Group( "Appearance" )] public Color DeepColor { get; set; } = new( 0.018f, 0.064f, 0.05f );
	[Property, Group( "Appearance" ), Range( 8, 2000 )] public float Clarity { get; set; } = 140;
	[Property, Group( "Appearance" ), Range( 0.02f, 0.8f )] public float Roughness { get; set; } = 0.12f;
	[Property, Group( "Reflections" ), Range( 0, 2 ), Description( "Environment reflection intensity. One preserves the water Fresnel; zero disables environment reflections. Configure the prefab's Environment reflection child for capture bounds and update rate." )]
	public float ReflectionStrength { get; set; } = 1;
	[Property, Group( "Appearance" ), Range( 16, 2048 )] public float TextureScale { get; set; } = 180;
	[Property, Group( "Appearance" ), Range( 0, 2 )] public float NormalStrength { get; set; } = 0.32f;
	[Property, Group( "Appearance" ), Range( 0, 2 )] public float MicroNormalStrength { get; set; } = 0.22f;
	[Property, Group( "Appearance" ), Range( 0, 1 )] public float ColorTextureInfluence { get; set; } = 0.08f;
	[Property, Group( "Appearance" ), Range( 0, 1 )] public float Foam { get; set; } = 0.4f;
	[Property, Group( "Appearance" ), Range( 1, 128 )] public float ShoreWidth { get; set; } = 18;
	[Property, Group( "Appearance" ), Range( 0, 128 ), Description( "World-space fade at the authored boundary. Set to zero for an ocean whose outer boundary should stay hidden beyond the playable area." )]
	public float EdgeFade { get; set; } = 18;
	[Property, Group( "Appearance" ), Range( 0, 0.05f )] public float Refraction { get; set; } = 0.012f;
	[Property, Group( "Caustics" ), Range( 0, 2 )] public float CausticStrength { get; set; } = 0.28f;
	[Property, Group( "Caustics" ), Range( 16, 512 )] public float CausticScale { get; set; } = 92;
	[Property, Group( "Caustics" ), Range( 0, 4 )] public float CausticSpeed { get; set; } = 0.7f;
	[Property, Group( "Caustics" ), Range( 16, 1024 )] public float CausticDepth { get; set; } = 260;
	[Property, Group( "Interaction" ), Range( 0, 2 )] public float RippleStrength { get; set; } = 0.6f;
	[Property, Group( "Interaction" ), Range( 0.2f, 8 )] public float RippleLifetime { get; set; } = 3;
	[Property, Group( "Interaction" ), Range( 8, 512 )] public float RippleSpeed { get; set; } = 95;
	// A small replicated clock per water body. Network the body as Object for multiplayer.
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float WaveTime { get; set; }
	public float SimulationTime => Game.IsPlaying && Networking.IsActive ? WaveTime : Time.Now;
	private ModelRenderer _renderer;
	private GameObject _visual;
	private Model _model;
	private Material _material;
	private int _geometryHash;
	private readonly Vector4[] _ripples = new Vector4[8];
	private int _nextRipple;
	private bool _warnedTransform;
	private bool _geometryReady;
	private static readonly string[] RippleNames = { "Ripple0", "Ripple1", "Ripple2", "Ripple3", "Ripple4", "Ripple5", "Ripple6", "Ripple7" };
	private Vector2 Direction => WindDirection.LengthSquared > 0.0001f ? WindDirection.Normal : new Vector2( 1, 0 );
	private bool SupportedTransform => Vector3.Dot( WorldRotation.Up, Vector3.Up ) > 0.9999f
		&& WorldScale.x > 0.001f && WorldScale.y > 0.001f && WorldScale.z > 0.001f;

	protected override void OnEnabled() => Refresh();
	protected override void OnUpdate() => Refresh();
	protected override void OnFixedUpdate()
	{
		if ( Game.IsPlaying && Networking.IsHost ) WaveTime = Time.Now;
	}
	protected override void OnDisabled() => ReleaseVisual();
	protected override void OnDestroy() => ReleaseVisual();

	protected override void DrawGizmos()
	{
		if ( !ShowOutline || !SupportedTransform || IsInfinite ) return;
		EnsureGeometry();
		Gizmo.Draw.Color = new Color( 0.15f, 0.75f, 0.85f, 0.7f );
		if ( Shape == WaterShape.River )
		{
			for ( var i = 1; i < _left.Count; i++ )
			{
				Gizmo.Draw.Line( WorldTransform.PointToWorld( _left[i - 1] ), WorldTransform.PointToWorld( _left[i] ) );
				Gizmo.Draw.Line( WorldTransform.PointToWorld( _right[i - 1] ), WorldTransform.PointToWorld( _right[i] ) );
			}
			return;
		}
		var count = Shape == WaterShape.Ellipse ? 64 : 4;
		for ( var i = 0; i < count; i++ )
		{
			Vector3 Point( int n )
			{
				if ( Shape == WaterShape.Ellipse )
				{
					var angle = n * MathF.PI * 2 / count;
					return new Vector3( MathF.Cos( angle ) * Size.x * 0.5f, MathF.Sin( angle ) * Size.y * 0.5f, 0 );
				}
				return new Vector3( (n % 4 is 0 or 3 ? -0.5f : 0.5f) * Size.x, (n % 4 < 2 ? -0.5f : 0.5f) * Size.y, 0 );
			}
			Gizmo.Draw.Line( WorldTransform.PointToWorld( Point( i ) ), WorldTransform.PointToWorld( Point( i + 1 ) ) );
		}
	}

	protected override void OnValidate()
	{
		Size = new Vector2( MathF.Max( 16, Size.x ), MathF.Max( 16, Size.y ) );
		Resolution = Math.Clamp( Resolution, 8, 160 );
		HorizonDistance = Math.Clamp( HorizonDistance, 16384, 500000 );
		Depth = Math.Clamp( Depth, 1, 10000 );
		WaveAmplitude = Math.Clamp( WaveAmplitude, 0, 200 );
		WaveLength = Math.Clamp( WaveLength, 32, 8192 );
		RiverSubdivisions = Math.Clamp( RiverSubdivisions, 2, 32 );
		RiverWidth = Math.Clamp( RiverWidth, 16, 4096 );
		Clarity = MathF.Max( 8, Clarity );
		TextureScale = MathF.Max( 16, TextureScale );
		MicroNormalStrength = Math.Clamp( MicroNormalStrength, 0, 2 );
		ReflectionStrength = Math.Clamp( ReflectionStrength, 0, 2 );
		EdgeFade = Math.Clamp( EdgeFade, 0, 128 );
		CausticScale = Math.Clamp( CausticScale, 16, 512 );
		CausticDepth = Math.Clamp( CausticDepth, 16, 1024 );
		RippleLifetime = MathF.Max( 0.2f, RippleLifetime );
	}

	private void Refresh()
	{
		if ( !SupportedTransform )
		{
			if ( !_warnedTransform ) Log.Warning( "VB Water: use yaw only and positive scale. River point heights can describe a slope." );
			_warnedTransform = true;
			ReleaseVisual();
			return;
		}
		_warnedTransform = false;
		EnsureGeometry();
		if ( Application.IsHeadless ) return;
		if ( !_visual.IsValid() )
		{
			_visual = new GameObject( true, "Water surface (generated)" );
			_visual.SetParent( GameObject, false );
			_visual.Flags = GameObjectFlags.NotSaved;
			_visual.NetworkMode = NetworkMode.Never;
            _visual.Tags.Add( "water_surface" );
			_renderer = _visual.AddComponent<ModelRenderer>();
		}
		if ( !_material.IsValid() ) _material = Material.Load( "water/materials/natural_water.vmat" );
		if ( !_model.IsValid() ) _model = BuildModel( _material );
		_renderer.Model = _model;
		_renderer.MaterialOverride = SurfaceMaterial.IsValid() ? SurfaceMaterial : _material;
		var a = _renderer.Attributes;
		a.Set( "WaterInfinite", IsInfinite ? 1f : 0f );
		a.Set( "WaterDebug", (float)DebugView );
		a.Set( "WaterMotion", (float)MotionType );
		a.Set( "PondAgitation", PondAgitation );
		a.Set( "RiverTurbulence", RiverTurbulence );
		a.Set( "RiverLength", _distance.Count > 0 ? _distance[^1] : 1f );
		a.Set( "RiverWidth", RiverWidth );
		a.Set( "WaterOrigin", WorldPosition );
		a.Set( "WaterTime", SimulationTime );
		a.Set( "WaveDirection", Direction );
		a.Set( "WaveAmplitude", WaveAmplitude );
		a.Set( "WaveLength", WaveLength );
		a.Set( "WaveSpeed", WaveSpeed );
		a.Set( "CurrentSpeed", CurrentSpeed );
		a.Set( "WaterShallow", ShallowColor );
		a.Set( "WaterDeep", DeepColor );
		a.Set( "WaterClarity", Clarity );
		a.Set( "WaterRoughness", Roughness );
		a.Set( "WaterReflectionStrength", ReflectionStrength );
		a.Set( "WaterTextureScale", TextureScale );
		a.Set( "WaterNormalStrength", NormalStrength );
		a.Set( "WaterMicroNormalStrength", MicroNormalStrength );
		a.Set( "WaterColorInfluence", ColorTextureInfluence );
		a.Set( "WaterFoam", Foam );
		a.Set( "WaterShoreWidth", ShoreWidth );
		var edgeSize = Shape == WaterShape.River ? new Vector2( RiverWidth, MathF.Max( _distance.Count > 0 ? _distance[^1] : 1, 1 ) ) : Size;
		a.Set( "WaterEdgeFadeUv", EdgeFade <= 0 || IsInfinite ? Vector2.Zero : new Vector2( EdgeFade / MathF.Max( edgeSize.x, 1 ), EdgeFade / MathF.Max( edgeSize.y, 1 ) ) );
		a.Set( "WaterRefraction", Refraction );
		a.Set( "WaterEllipse", Shape == WaterShape.Ellipse ? 1f : 0f );
		a.Set( "WaterCausticStrength", CausticStrength );
		a.Set( "WaterCausticScale", CausticScale );
		a.Set( "WaterCausticSpeed", CausticSpeed );
		a.Set( "WaterCausticDepth", CausticDepth );
		a.Set( "RippleStrength", RippleStrength );
		a.Set( "RippleLifetime", RippleLifetime );
		a.Set( "RippleSpeed", RippleSpeed );
		for ( var i = 0; i < _ripples.Length; i++ ) a.Set( RippleNames[i], _ripples[i] );
	}

	private void ReleaseVisual()
	{
		if ( _visual.IsValid() ) _visual.Destroy();
		_visual = null;
		_renderer = null;
		_model = null;
		Array.Clear( _ripples );
	}

	public override void AddRipple( Vector3 position, float strength = 1 )
	{
		if ( !Active || !TrySample( position, out _ ) ) return;
		_ripples[_nextRipple] = new Vector4( position.x, position.y, Math.Clamp( strength, 0, 2 ), SimulationTime );
		_nextRipple = (_nextRipple + 1) % _ripples.Length;
	}

	public override bool TrySample( Vector3 position, out WaterSample sample )
	{
		sample = default;
		if ( !Active || !SupportedTransform ) return false;
		if ( !_geometryReady ) EnsureGeometry();
		var local = WorldTransform.PointToLocal( position );
		if ( !TryBaseSurface( local, out var baseHeight, out var flow, out var baseNormal ) ) return false;
		var level = WorldTransform.PointToWorld( new Vector3( local.x, local.y, baseHeight ) ).z;
		var wave = WaterWaves.Evaluate( new Vector2( position.x, position.y ), Direction, WaveAmplitude, WaveLength, WaveSpeed, SimulationTime, MotionType );
		var height = level + wave.x;
		var bottom = level - Depth * WorldScale.z;
		// Keep above-surface queries valid for partially submerged objects and water-entry tests.
		var n = WorldTransform.NormalToWorld( baseNormal ).Normal;
		n = new Vector3( n.x / MathF.Max( n.z, 0.01f ) - wave.y, n.y / MathF.Max( n.z, 0.01f ) - wave.z, 1 ).Normal;
		var worldFlow = WorldTransform.NormalToWorld( flow ).Normal;
		var next = WaterWaves.Evaluate( new Vector2( position.x, position.y ), Direction, WaveAmplitude, WaveLength, WaveSpeed, SimulationTime + 0.01f, MotionType );
		sample = new WaterSample( this, height, bottom, n, worldFlow * CurrentSpeed + Vector3.Up * ((next.x - wave.x) * 100f) );
		return true;
	}
}
