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
	protected override void OnEnabled() => _previous = WorldTransform.PointToWorld( ContactOffset );
	protected override void OnUpdate()
	{
		var point = WorldTransform.PointToWorld( ContactOffset );
		var distance = (point - _previous).Length;
		var speed = distance / MathF.Max( Time.Delta, 0.001f );
		_previous = point;
		if ( !Game.IsPlaying || distance > 512 ) { _touching = false; return; }
		var contact = WaterSurface.Find( Scene, point, out var water ) && MathF.Abs( point.z - water.Height ) <= ContactRadius;
		if ( contact && (speed >= MinimumSpeed || !_touching) && Time.Now >= _next )
		{
			_next = Time.Now + MathF.Max( 0.05f, Interval );
			water.Surface.AddRipple( point, Math.Clamp( speed / 140, 0.2f, 1.5f ) );
		}
		_touching = contact;
	}
}
