using System;
using VoidBreach.Ocean;

namespace VoidBreach.Water;

/// <summary>One native buoyancy call per body, using the selected FFT surface's local tangent plane.</summary>
[Title( "VB Fluid Body" ), Category( "Void Breach/Water" )]
public sealed class VBFluidBody : Component
{
	public const string ManagedTag = "vb_fluid_managed";
	public Rigidbody Body { get; private set; }
	private readonly HashSet<VBFFTOcean> _surfaces = new();
	private bool _ownsTag;
	private bool _wasWet;
	private float _nextDryCheck;
	public void Register( Rigidbody body )
	{
		Body = body;
		_nextDryCheck = 0;
		if ( !Enabled ) Enabled = true;
		if ( !body.Tags.Has( ManagedTag ) ) { body.Tags.Add( ManagedTag ); _ownsTag = true; }
	}
	protected override void OnDisabled() => ClearTag();
	protected override void OnDestroy() => ClearTag();
	private void ClearTag()
	{
		if ( _ownsTag && Body.IsValid() ) Body.Tags.Remove( ManagedTag );
		_ownsTag = false; _surfaces.Clear();
	}
	protected override void OnFixedUpdate()
	{
		if ( !Game.IsPlaying || !Body.IsValid() || !Body.Active ) return;
		if ( Body.IsProxy || !Body.MotionEnabled || Body.GetComponent<PlayerController>().IsValid() ) return;
		// A sleeping dry prop needs a periodic waterline check, not a Fourier query
		// at every physics step. Moving and wetted bodies always run at full rate.
		if ( !_wasWet && Body.Sleeping && Time.Now < _nextDryCheck ) return;
		_nextDryCheck = Time.Now + 0.1f; _wasWet = false;
		_surfaces.Clear();
		foreach ( var collider in Body.Touching )
		{
			if ( !collider.IsValid() || !collider.IsTrigger || !collider.Tags.Has( "water" ) ) continue;
			var water = collider.GetComponentInParent<VBFFTOcean>();
			if ( water.IsValid() && water.Active ) _surfaces.Add( water );
		}
		if ( _surfaces.Count == 0 ) { Enabled = false; return; }
		var bounds = Body.GetWorldBounds(); var point = bounds.Center;
		WaterSample selected = default;
		foreach ( var water in _surfaces )
		{
			if ( !water.NativePhysics ) continue;
			// The trigger includes crest padding. Bodies wholly above it need no Fourier query.
			if ( water.Shape != FftWaterShape.River && bounds.Mins.z > water.WorldPosition.z + water.WaveHeight * FftOceanSpectrum.UnitsPerMeter * 2 + 24 ) continue;
			if ( !water.TrySampleForBuoyancy( point, out var sample ) || point.z < sample.Bottom - bounds.Size.z * 0.5f ) continue;
			if ( selected.Surface is null || water.Priority > selected.Surface.Priority
				|| (water.Priority == selected.Surface.Priority && sample.Height > selected.Height) ) selected = sample;
		}
		if ( selected.Surface is not VBFFTOcean surface ) return;
		var controller = Body.GetComponent<PlayerController>();
		if ( !Body.IsProxy && Body.MotionEnabled && surface.NativePhysics && surface.PhysicsVolume.IsValid() && !controller.IsValid() )
		{
			var volume = surface.PhysicsVolume;
			// Native WaterVolume ignores our tag; this is the sole buoyancy/drag application.
			var normal = selected.Normal;
			if ( normal.z < 0.5f ) normal = new Vector3( normal.x, normal.y, 0.5f ).Normal;
			var half = bounds.Size * 0.5f;
			var reach = MathF.Abs( normal.x ) * half.x + MathF.Abs( normal.y ) * half.y + normal.z * half.z;
			if ( (point.z - selected.Height) * normal.z > reach ) return;
			_wasWet = true;
			var immersion = selected.Submersion( point.z, bounds.Size.z * 0.5f );
			var response = Body.GetComponent<IFluidBuoyancyResponse>();
			var liftScale = response?.UpdateBuoyancy( immersion, MathF.Max( Scene.FixedDelta, 0.001f ) ) ?? 1f;
			if ( !float.IsFinite( liftScale ) ) liftScale = 1f;
			Body.ApplyBuoyancy( new Plane( new Vector3( point.x, point.y, selected.Height ), normal ),
				volume.FluidDensity * Math.Clamp( liftScale, 0f, 1f ), volume.LinearDrag, volume.AngularDrag, selected.Velocity, Time.Delta );
		}
	}
}
