using System;

namespace VoidBreach.Water;

/// <summary>Local cosmetic wakes, including replicated props and controller-driven characters.</summary>
[Title( "VB Water Interactor" ), Category( "Void Breach/Water" ), Icon( "water_drop" )]
public sealed class VBWaterInteractor : Component
{
	[Property] public Vector3 ContactOffset { get; set; }
	[Property, Range( 1, 128 )] public float ContactRadius { get; set; } = 24;
	[Property, Range( 0.05f, 2 )] public float Interval { get; set; } = 0.2f;
	[Property, Range( 0, 512 )] public float MinimumSpeed { get; set; } = 12;
	private Vector3 _previous;
	private float _next;
	private bool _touching;
	private PlayerController _controller;
	private Rigidbody _nativeBody;
	private bool _ownsTag;
	public int EntryCount { get; private set; }
	public int ExitCount { get; private set; }
	public int WakeCount { get; private set; }
	internal void RecordUniversalImpact( FluidImpactKind kind )
	{
		if ( kind == FluidImpactKind.Entry ) EntryCount++;
		else if ( kind == FluidImpactKind.Exit ) ExitCount++;
		else WakeCount++;
	}
	public void RegisterNativeBody( Rigidbody body )
	{
		_nativeBody = body;
		if ( !body.Tags.Has( VBFluidBody.ManagedTag ) ) { body.Tags.Add( VBFluidBody.ManagedTag ); _ownsTag = true; }
	}
	protected override void OnDisabled()
	{
		if ( _ownsTag && _nativeBody.IsValid() ) _nativeBody.Tags.Remove( VBFluidBody.ManagedTag );
		_ownsTag = false; _touching = false; _nativeBody = null;
	}
	protected override void OnEnabled() => _previous = WorldTransform.PointToWorld( ContactOffset );
	protected override void OnFixedUpdate()
	{
		var point = WorldTransform.PointToWorld( ContactOffset );
		var movement = (point - _previous) / MathF.Max( Time.Delta, 0.001f );
		var distance = (point - _previous).Length;
		_previous = point;
		if ( !Game.IsPlaying || distance > 512 ) { _touching = false; return; }
		if ( !_controller.IsValid() ) _controller = GetComponent<PlayerController>();
		if ( _controller.IsValid() && _controller.Body.IsValid() && !_nativeBody.IsValid() ) RegisterNativeBody( _controller.Body );
		var height = _controller.IsValid() ? _controller.CurrentHeight : ContactRadius * 2;
		var radius = _controller.IsValid() ? MathF.Max( _controller.BodyRadius * 1.6f, 20 ) : ContactRadius;
		var center = _controller.IsValid() ? point + Vector3.Up * (height * 0.5f) : point;
		var found = WaterSurface.Find( Scene, center, out var water );
		// FFT effects are emitted once by the universal surface tracker, including for this object.
		if ( found && water.Surface is VoidBreach.Ocean.VBFFTOcean ) return;
		var feet = _controller.IsValid() ? point.z : point.z - radius;
		var top = _controller.IsValid() ? point.z + height : point.z + radius;
		var contact = found && feet <= water.Height + 4 && top >= water.Height - radius * 0.25f && top > water.Bottom;
		var relative = movement - (found ? water.Velocity : Vector3.Zero);
		if ( contact && (!_touching || relative.Length >= MinimumSpeed) && Time.Now >= _next )
		{
			_next = Time.Now + MathF.Max( 0.05f, Interval );
			var impact = new Vector3( point.x, point.y, water.Height );
			water.Surface.AddRipple( impact, Math.Clamp( relative.Length / 140, 0.2f, 1.5f ) );
		}
		_touching = contact;
	}
}
