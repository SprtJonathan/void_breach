using System;
using VoidBreach.Water;

namespace VoidBreach.Ocean;

/// <summary>Shared spectral water surface: GPU rendering, native fluid physics and local surface dynamics.</summary>
[Title( "VB FFT Water" ), Category( "Void Breach/Water" ), Icon( "water" )]
public sealed partial class VBFFTOcean : WaterSurface, Component.ExecuteInEditor, IFluidSurfaceSampler
{
	[Property, Group( "Domain" )] public FftWaterShape Shape { get; set; } = FftWaterShape.Ocean;
	[Property, Group( "Domain" ), Description( "Lake dimensions in world units; yaw is supported, parent scale is ignored." )]
	public Vector2 Size { get; set; } = new( 2400, 1800 );
	[Property, Group( "Domain" ), Range( 32, 20000 )] public float Depth { get; set; } = 2048;
	[Property, Group( "River" )] public List<Vector3> RiverPoints { get; set; } = new() { new( -1600, 0, 0 ), new( -500, 220, -24 ), new( 500, -150, -64 ), new( 1600, 100, -100 ) };
	[Property, Group( "River" ), Range( 32, 4096 )] public float RiverWidth { get; set; } = 400;
	[Property, Group( "Physics" )] public bool NativePhysics { get; set; } = true;
	[Property, Group( "Physics" ), Range( 0, 10 )] public float LinearDrag { get; set; } = 1.5f;
	[Property, Group( "Physics" ), Range( 0, 10 )] public float AngularDrag { get; set; } = 0.7f;
	[Property, Group( "Physics" ), Range( -512, 512 )] public float CurrentSpeed { get; set; }
	[Property, Group( "Physics" ), Range( 16, 512 ), Description( "Energetic FFT mode pairs retained for CPU queries. Fine rendering ripples are filtered out." )]
	public int PhysicsModes { get; set; } = 128;
	[Property, Group( "Physics" ), Range( 0, 32 ), Description( "Share FFT tangent samples between nearby buoyancy bodies, in world units. 0 uses exact Fourier queries. Public TrySample remains exact." )]
	public float PhysicsSampleSpacing { get; set; } = 8;
	public int PhysicsCacheHits => _sampler.PhysicsCacheHits;
	public int PhysicsCacheMisses => _sampler.PhysicsCacheMisses;
	public float PhysicsRetainedEnergy => _sampler.RetainedEnergy;
	public override bool UsesNativeBuoyancy => NativePhysics;
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float SimulationClock { get; set; }
	[Property, Group( "Interactions" )] public bool Interactions { get; set; } = true;
	[Property, Group( "Interactions" ), Range( 8, 32 ), Description( "Near-field cell size; a second field covers eight times the distance." )] public float InteractionCellSize { get; set; } = 8;
	[Property, Group( "Interactions" ), Range( 16, 600 )] public float InteractionWaveSpeed { get; set; } = 160;
	[Property, Group( "Interactions" ), Range( 0.05f, 8 )] public float InteractionDamping { get; set; } = 1.4f;
	[Property, Group( "Interactions" ), Range( 0, 4 )] public float InteractionStrength { get; set; } = 1;
	[Property, Group( "Interactions" ), Range( 64, 256 ), Description( "GPU grid side, rounded down to 64, 128 or 256. Two local clipmaps per surface." )] public int InteractionResolution { get; set; } = 128;
	[Property, Group( "Interactions" ), Range( 4, 16 ), Description( "Local render mesh spacing. Small waves require actual vertices at this spacing, independently of the GPU texture resolution." )] public float InteractionMeshSpacing { get; set; } = 4;
	[Property, Group( "Interactions" ), Range( 0, 1 ), Description( "Optional foam from steep, energetic disturbances. Zero leaves the surface displacement and reflections visible without white foam." )] public float InteractionFoam { get; set; } = 0.2f;
	[Property, Group( "Interactions" ), Range( 15, 60 )] public int InteractionFrequency { get; set; } = 30;
	[Property, Group( "Interactions" ), Range( 0, 4 )] public float InteractionViscosity { get; set; } = 0.15f;
	[Property, Group( "Interactions" ), Range( 1, 64 )] public float InteractionMaximumHeight { get; set; } = 24;
	[Property, Group( "Interactions" ), Range( 8, 256 )] public float InteractionMaximumRadius { get; set; } = 192;
	[Property, Group( "Interactions" ), Range( 1, 64 )] public int InteractionBudget { get; set; } = 48;
	[Property, Group( "Interactions" ), Range( 16, 512 )] public int InteractionTrackingBudget { get; set; } = 256;
	[Property, Group( "Interactions" ), Range( 256, 16000 )] public float InteractionDistance { get; set; } = 4096;
	[Property, Group( "Interactions" ), Range( 1, 100 )] public float InteractionMinimumSpeed { get; set; } = 10;
	[Property, Group( "Interactions" ), Range( 0.01f, 1 )] public float InteractionVelocitySensitivity { get; set; } = 0.18f;
	[Property, Group( "Interactions" ), Description( "Host emits automatic interactions on networked water. Textures remain local. Enable NetworkMode.Object on the water root." )] public bool ReplicateInteractions { get; set; } = true;
	[Property, Group( "Debug" )] public bool DebugInteractions { get; set; }
	[Property, Group( "Caustics" ), Range( 0, 2 )] public float CausticStrength { get; set; } = 0.18f;
	[Property, Group( "Caustics" ), Range( 16, 512 )] public float CausticScale { get; set; } = 92;
	[Property, Group( "Caustics" ), Range( 16, 2048 )] public float CausticDepth { get; set; } = 400;
	[Property, Group( "Waves" ), Range( 1, 25 ), Description( "Wind speed in metres per second; controls the spectrum shape." )]
	public float WindSpeed { get; set; } = 10f;
	[Property, Group( "Waves" )] public Vector2 WindDirection { get; set; } = new( 1, 0.3f );
	[Property, Group( "Waves" ), Range( 0.05f, 1 ), Description( "Directional diffusion: 1 gives equal energy in every direction." )]
	public float WindDiffusion { get; set; } = 0.55f;
	[Property, Group( "Waves" ), Range( 0, 60 ), Description( "Slow variation around the prevailing wind, in degrees. Does not rotate existing wave phases." )]
	public float WindVariation { get; set; } = 22;
	[Property, Group( "Waves" ), Range( 30, 1200 )] public float WindVariationPeriod { get; set; } = 180;
	[Property, Group( "Swell" ), Range( 0, 0.8f ), Description( "Fraction of wave energy in independent, longer-period swell." )]
	public float SwellAmount { get; set; } = 0.3f;
	[Property, Group( "Swell" )] public Vector2 SwellDirection { get; set; } = new( 0.2f, 1 );
	[Property, Group( "Swell" ), Range( 1, 25 )] public float SwellWindSpeed { get; set; } = 16;
	[Property, Group( "Waves" ), Range( 0, 5 ), Description( "Significant wave height Hs in metres (four times the height standard deviation)." )]
	public float WaveHeight { get; set; } = 1.2f;
	[Property, Group( "Waves" ), Range( 0, 1.5f )] public float Choppiness { get; set; } = 0.85f;
	[Property, Group( "Waves" ), Range( 0, 2 )] public float Speed { get; set; } = 1f;
	[Property, Group( "Waves" )] public int Seed { get; set; } = 17;
	[Property, Group( "Geometry" ), Range( 64, 192 )] public int Resolution { get; set; } = 160;
	[Property, Group( "Geometry" ), Range( 16, 128 )] public float GridSpacing { get; set; } = 32f;
	[Property, Group( "Geometry" ), Range( 16384, 500000 )] public float HorizonDistance { get; set; } = 200000f;
	[Property, Group( "Appearance" )] public Color DeepColor { get; set; } = new( 0.009f, 0.04f, 0.055f );
	[Property, Group( "Appearance" )] public Color ShallowColor { get; set; } = new( 0.07f, 0.2f, 0.19f );
	[Property, Group( "Appearance" ), Range( 16, 2000 )] public float Clarity { get; set; } = 280f;
	[Property, Group( "Appearance" ), Range( 0.04f, 0.5f )] public float Roughness { get; set; } = 0.09f;
	[Property, Group( "Appearance" ), Range( 0, 2 )] public float ReflectionStrength { get; set; } = 1f;
	[Property, Group( "Appearance" ), Range( 0, 1 )] public float Foam { get; set; } = 0.6f;
	[Property, Group( "Appearance" )] public bool UnderwaterEffects { get; set; } = true;
	[Property, Group( "Appearance" )] public VoidBreach.Water.WaterDebugView DebugView { get; set; }

	private GameObject _visual;
	private ModelRenderer _renderer;
	private Model _model;
	private Material _material;
	private GpuBuffer<Vector4> _initial, _initialSwell, _spectrumA, _spectrumB, _rowsA, _rowsB;
	private Texture _displacement, _derivatives;
	private ComputeShader _evolve, _rows, _columns;
	private SceneCustomObject _simulationObject;
	private bool _simulationPending;
	private bool _renderHookAssigned;
	private int _spectrumHash, _geometryHash;
	private bool _hasSpectrum;
	private float _time;
	private float _editorClock;
	private readonly FftSurfaceSampler _sampler = new();
	private Vector4[] _spectrumData, _swellData;
	private Vector2 Direction => WindDirection.LengthSquared > 0.0001f ? WindDirection.Normal : new Vector2( 1, 0 );
	public Vector2 CurrentWindDirection
	{
		get
		{
			var clock = Game.IsPlaying ? SimulationClock : _editorClock;
			var phase = clock * (2 * MathF.PI / Math.Clamp( WindVariationPeriod, 30, 1200 ));
			var angle = (MathF.Sin( phase ) * 0.7f + (MathF.Sin( phase / 2.37f + 1.7f ) - MathF.Sin( 1.7f )) * 0.3f)
				* Math.Clamp( WindVariation, 0, 60 ) * (MathF.PI / 180);
			var (sn, cs) = MathF.SinCos( angle ); var d = Direction;
			return new Vector2( d.x * cs - d.y * sn, d.x * sn + d.y * cs );
		}
	}

	protected override void OnEnabled() => Refresh();
	protected override void OnUpdate() => Refresh();
	protected override void OnDisabled() => Release();
	protected override void OnDestroy() => Release();
	protected override void OnFixedUpdate()
	{
		if ( !Game.IsPlaying ) return;
		if ( !Networking.IsActive || Networking.IsHost || GameObject.NetworkMode == NetworkMode.Never ) SimulationClock += Math.Clamp( Speed, 0, 2 ) * Time.Delta;
		_time = SimulationClock % FftOceanSpectrum.LoopPeriod;
		EnsureDomain(); EnsureSpectrum();
		StepInteractions( Time.Delta );
	}

	protected override void OnValidate()
	{
		WindSpeed = Math.Clamp( WindSpeed, 1, 25 );
		WaveHeight = Math.Clamp( WaveHeight, 0, 5 );
		Choppiness = Math.Clamp( Choppiness, 0, 1.5f );
		Speed = Math.Clamp( Speed, 0, 2 );
		Resolution = Math.Clamp( Resolution, 64, 192 );
		Resolution += Resolution % 2;
		GridSpacing = Math.Clamp( GridSpacing, 16, 128 );
		HorizonDistance = Math.Clamp( HorizonDistance, 16384, 500000 );
	}

	private void Refresh()
	{
		// Editor previews also pump retirement when the outgoing Play context no
		// longer runs. The scene system covers scenes without an enabled surface.
		FluidGpuResources.Collect();
		if ( !Game.IsPlaying ) _editorClock += Time.Delta * Math.Clamp( Speed, 0, 2 );
		_time = (Game.IsPlaying ? SimulationClock : _editorClock) % FftOceanSpectrum.LoopPeriod;
		EnsureDomain(); EnsureSpectrum(); EnsurePhysicsVolume();
		if ( !Game.IsPlaying ) StepInteractions( Time.Delta );
		if ( Application.IsHeadless ) return;
		EnsureUnderwaterEffects();
		EnsureResources();
		if ( !_hasSpectrum )
		{
			_initial.SetData( _spectrumData );
			_initialSwell.SetData( _swellData );
			_hasSpectrum = true;
		}
		_simulationPending = true;
		EnsureGeometry();
		UploadInteractions();
		ApplyAttributes();
	}
	private void EnsureSpectrum()
	{
		var hash = HashCode.Combine( WindSpeed, Direction, WaveHeight, Seed, PhysicsModes, WindDiffusion,
			HashCode.Combine( SwellAmount, SwellDirection, SwellWindSpeed ) );
		if ( _spectrumData is null || hash != _spectrumHash )
		{
			var height = Math.Clamp( WaveHeight, 0, 5 ); var swell = Math.Clamp( SwellAmount, 0, 0.8f );
			_spectrumData = FftOceanSpectrum.Build( WindSpeed, Direction, height * MathF.Sqrt( 1 - swell ), Seed, WindDiffusion );
			_swellData = FftOceanSpectrum.Build( SwellWindSpeed, SwellDirection, height * MathF.Sqrt( swell ), Seed ^ 0x3479, 0.3f );
			_sampler.Build( _spectrumData, PhysicsModes, _swellData ); _spectrumHash = hash; _hasSpectrum = false;
		}
		_sampler.Update( _time, Direction, CurrentWindDirection, Math.Clamp( WindDiffusion, 0.05f, 1 ) );
	}

	private void EnsureResources()
	{
		if ( _initial is not null )
		{
			// Rebind once after upgrading an existing scene from the old anonymous callback.
			if ( !_renderHookAssigned && _simulationObject is not null )
			{
				_simulationObject.RenderOverride = RenderSimulation; _renderHookAssigned = true;
			}
			return;
		}
		const int count = FftOceanSpectrum.Resolution * FftOceanSpectrum.Resolution * 2;
		_initial = new GpuBuffer<Vector4>( count );
		_initialSwell = new GpuBuffer<Vector4>( count );
		_spectrumA = new GpuBuffer<Vector4>( count );
		_spectrumB = new GpuBuffer<Vector4>( count );
		_rowsA = new GpuBuffer<Vector4>( count );
		_rowsB = new GpuBuffer<Vector4>( count );
		Texture CreateTexture() => Texture.Create( 128, 256, ImageFormat.RGBA32323232F )
			.WithName( $"vb_water_fft_{Guid.NewGuid():N}" ).WithAnonymous( false ).WithUAVBinding().WithGPUOnlyUsage().Finish();
		_displacement = CreateTexture();
		_derivatives = CreateTexture();
		_evolve = new ComputeShader( "water/shaders/fft/evolve.shader" );
		_evolve.Attributes.Set( "InitialSpectrum", _initial );
		_evolve.Attributes.Set( "SwellSpectrum", _initialSwell );
		_evolve.Attributes.Set( "SpectrumA", _spectrumA );
		_evolve.Attributes.Set( "SpectrumB", _spectrumB );
		_rows = new ComputeShader( "water/shaders/fft/rows.shader" );
		_rows.Attributes.Set( "InputA", _spectrumA );
		_rows.Attributes.Set( "InputB", _spectrumB );
		_rows.Attributes.Set( "OutputA", _rowsA );
		_rows.Attributes.Set( "OutputB", _rowsB );
		_columns = new ComputeShader( "water/shaders/fft/columns.shader" );
		_columns.Attributes.Set( "InputA", _rowsA );
		_columns.Attributes.Set( "InputB", _rowsB );
		_columns.Attributes.Set( "DisplacementOut", _displacement );
		_columns.Attributes.Set( "DerivativesOut", _derivatives );
		// Execute within a real graphics context, before the translucent water is drawn.
		// An opaque custom object works for game, editor and reflection cameras alike.
		_simulationObject = new SceneCustomObject( Scene.SceneWorld );
		_simulationObject.Flags.IsOpaque = true;
		_simulationObject.Flags.IsTranslucent = false;
		_simulationObject.Flags.CastShadows = false;
		_simulationObject.Flags.IncludeInCubemap = true;
		_simulationObject.RenderOverride = RenderSimulation; _renderHookAssigned = true;
	}
	private void RenderSimulation( SceneObject sceneObject ) => Simulate();

	private void Simulate()
	{
		if ( !_simulationPending || _initial is null ) return;
		_simulationPending = false;
		_evolve.Attributes.Set( "FftTime", _time );
		_evolve.Attributes.Set( "BaseWind", Direction );
		_evolve.Attributes.Set( "CurrentWind", CurrentWindDirection );
		_evolve.Attributes.Set( "WindDiffusion", Math.Clamp( WindDiffusion, 0.05f, 1 ) );
		_evolve.Attributes.Set( "Choppiness", Math.Clamp( Choppiness, 0, 1.5f ) );
		_evolve.Dispatch( 128, 128, 2 );
		Graphics.UavBarrier( _spectrumA ); Graphics.UavBarrier( _spectrumB );
		// Dispatch takes THREAD counts: 64 threads per line, 128 lines, two cascades.
		_rows.Dispatch( 64, 128, 2 );
		Graphics.UavBarrier( _rowsA ); Graphics.UavBarrier( _rowsB );
		_columns.Dispatch( 64, 128, 2 );
		Graphics.UavBarrier( _displacement ); Graphics.UavBarrier( _derivatives );
		RenderInteractions();
	}

	private void ApplyAttributes()
	{
		var a = _renderer.Attributes;
		a.Set( "FftDisplacement", _displacement ); a.Set( "FftDerivatives", _derivatives );
		a.Set( "FftGridSpacing", Interactions ? MathF.Min( Math.Clamp( GridSpacing, 16, 128 ), Math.Clamp( InteractionMeshSpacing, 4, 16 ) ) : Math.Clamp( GridSpacing, 16, 128 ) );
		a.Set( "FluidFoamStrength", Math.Clamp( InteractionFoam, 0, 1 ) );
		a.Set( "WaterInfinite", Shape == FftWaterShape.Ocean ? 1f : 0f ); a.Set( "WaterMotion", Shape == FftWaterShape.River ? 2f : Shape == FftWaterShape.Lake ? 1f : 3f );
		a.Set( "WaterOrigin", WorldPosition ); a.Set( "WaterTime", Game.IsPlaying ? SimulationClock : _editorClock );
		a.Set( "WaveAmplitude", WaveHeight * FftOceanSpectrum.UnitsPerMeter );
		a.Set( "WaterShallow", ShallowColor ); a.Set( "WaterDeep", DeepColor );
		a.Set( "WaterClarity", MathF.Max( Clarity, 16 ) ); a.Set( "WaterRoughness", Roughness );
		a.Set( "WaterReflectionStrength", ReflectionStrength ); a.Set( "WaterFoam", Foam );
		a.Set( "WaterTextureScale", 180f ); a.Set( "WaterNormalStrength", 0.06f );
		a.Set( "WaterMicroNormalStrength", 0.03f ); a.Set( "WaterColorInfluence", 0f );
		a.Set( "PondAgitation", Shape == FftWaterShape.Lake ? 0.12f : 0.35f );
		a.Set( "WaterEdgeFadeUv", Shape == FftWaterShape.Ocean ? Vector2.Zero : Shape == FftWaterShape.River ? new Vector2( 0.025f, 0.005f ) : new Vector2( 0.015f, 0.015f ) );
		a.Set( "WaterEllipse", Shape == FftWaterShape.Lake ? 1f : 0f );
		a.Set( "WaterCausticStrength", CausticStrength ); a.Set( "WaterCausticScale", CausticScale ); a.Set( "WaterCausticDepth", CausticDepth );
		a.Set( "CurrentSpeed", CurrentSpeed ); a.Set( "RiverLength", _domain.Length ); a.Set( "RiverWidth", RiverWidth );
		a.Set( "RiverTurbulence", Math.Clamp( MathF.Abs( CurrentSpeed ) / 160, 0.1f, 2 ) );
		a.Set( "FftAdvection", Advection );
		ApplyInteractionAttributes( a );
		a.Set( "WaterDebug", (float)DebugView );
	}

	private void Release()
	{
		// Detach native render callbacks and material resource references before destroying
		// textures. This also makes disable/destroy and editor -> Play teardown idempotent.
		_simulationPending = false;
		if ( _simulationObject is not null ) { _simulationObject.RenderOverride = null; _simulationObject.Delete(); _simulationObject = null; }
		_renderHookAssigned = false;
		if ( _renderer.IsValid() ) { _renderer.Attributes.Clear(); _renderer.Enabled = false; }
		ReleasePhysicsVolume();
		ReleaseSplashes();
		if ( _effectsObject.IsValid() ) _effectsObject.Destroy(); _effectsObject = null;
		ReleaseInteractions();
		if ( _visual.IsValid() ) _visual.Destroy();
		_visual = null; _renderer = null; _model = null; _material = null;
		if ( _displacement is not null )
			FluidGpuResources.Retire( new[] { _displacement, _derivatives },
				new GpuBuffer[] { _initial, _initialSwell, _spectrumA, _spectrumB, _rowsA, _rowsB },
				new[] { _evolve, _rows, _columns } );
		_evolve = null; _rows = null; _columns = null;
		_initial = null; _initialSwell = null; _spectrumA = null; _spectrumB = null; _rowsA = null; _rowsB = null;
		_displacement = null; _derivatives = null; _hasSpectrum = false;
	}
}
