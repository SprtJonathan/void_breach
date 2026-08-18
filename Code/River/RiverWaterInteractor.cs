using System;

namespace VoidBreach.River;

/// <summary>
/// Adds moving-object ripples to a RiverWater. Add it to players or physics props.
/// </summary>
[Title( "River Water Interactor" )]
[Category( "Void Breach/River" )]
[Icon( "water_drop" )]
public sealed class RiverWaterInteractor : Component
{
	[Property]
	public RiverWater River { get; set; }

	[Property, Range( 8f, 256f )]
	public float RippleRadius { get; set; } = 52f;

	[Property, Range( 0.05f, 1f )]
	public float RippleInterval { get; set; } = 0.16f;

	[Property, Range( 0f, 512f )]
	public float MinimumSpeed { get; set; } = 18f;

	[Property, Range( 8f, 512f )]
	public float VerticalTolerance { get; set; } = 96f;

	private Rigidbody _rigidbody;
	private Vector3 _previousPosition;
	private TimeSince _sinceRipple;

	protected override void OnEnabled()
	{
		_rigidbody = GetComponent<Rigidbody>();
		_previousPosition = WorldPosition;
		_sinceRipple = RippleInterval;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Game.IsPlaying )
		{
			_previousPosition = WorldPosition;
			return;
		}

		River ??= Scene.Get<RiverWater>();
		if ( !River.IsValid() )
		{
			return;
		}

		var velocity = _rigidbody.IsValid()
			? _rigidbody.Velocity
			: (WorldPosition - _previousPosition) / MathF.Max( Scene.FixedDelta, 0.001f );

		_previousPosition = WorldPosition;

		if ( velocity.Length < MinimumSpeed || _sinceRipple < RippleInterval )
		{
			return;
		}

		if ( !River.TryGetSurfaceInfo( WorldPosition, VerticalTolerance, out var surfacePosition, out _ ) )
		{
			return;
		}

		_sinceRipple = 0f;
		River.AddRipple( surfacePosition, RippleRadius );
	}

	protected override void OnValidate()
	{
		RippleRadius = RippleRadius.Clamp( 8f, 256f );
		RippleInterval = RippleInterval.Clamp( 0.05f, 1f );
		MinimumSpeed = MinimumSpeed.Clamp( 0f, 512f );
		VerticalTolerance = VerticalTolerance.Clamp( 8f, 512f );
	}
}
