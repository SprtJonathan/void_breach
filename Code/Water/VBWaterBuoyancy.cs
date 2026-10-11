using System;

namespace VoidBreach.Water;

/// <summary>Eight volume samples produce lift, restoring torque and current drag.</summary>
[Title( "VB Water Buoyancy" ), Category( "Void Breach/Water" ), Icon( "sailing" )]
public sealed class VBWaterBuoyancy : Component
{
	[Property] public Rigidbody Body { get; set; }
	[Property, Description( "Local center and dimensions of the displaced volume; fit these to the object's hull." )]
	public Vector3 VolumeSize { get; set; } = new( 40, 40, 40 );
	[Property] public Vector3 VolumeCenter { get; set; }
	[Property, Range( 0.01f, 1 ), Description( "Fraction of the bounding volume which actually displaces water. Use to fit irregular hulls." )]
	public float VolumeFill { get; set; } = 1;
	[Property, Range( 0, 10 )] public float Drag { get; set; } = 1.8f;
	[Property, Range( 0, 4 )] public float AngularDrag { get; set; } = 0.65f;
	[Property, Range( 1, 10 ), Description( "Safety cap on buoyant acceleration in multiples of gravity." )]
	public float MaximumLift { get; set; } = 4;
	[Property] public bool EmitRipples { get; set; } = true;
	[Property, Range( 0.05f, 2 )] public float RippleInterval { get; set; } = 0.2f;
	public float SubmergedFraction { get; private set; }
	private float _nextRipple;
	private bool _nativeWarning;

	protected override void OnStart()
	{
		if ( !Body.IsValid() ) Body = GetComponent<Rigidbody>();
		if ( !Body.IsValid() ) Log.Warning( "VB Water Buoyancy requires a Rigidbody on the same object, or an explicit Body reference." );
	}
	protected override void OnFixedUpdate()
	{
		SubmergedFraction = 0;
		if ( !Game.IsPlaying || !Body.IsValid() || !Body.Active || !Body.MotionEnabled ) return;
		// Ownership follows the actual Rigidbody, including when explicitly referenced on another object.
		if ( Body.IsProxy ) return;
		var scale = WorldScale;
		var size = new Vector3( MathF.Abs( VolumeSize.x * scale.x ), MathF.Abs( VolumeSize.y * scale.y ), MathF.Abs( VolumeSize.z * scale.z ) );
		if ( size.x < 0.01f || size.y < 0.01f || size.z < 0.01f ) return;
		// Source units are inches. Density is kg/m³, mass is kg, acceleration remains units/s².
		var volume = size.x * size.y * size.z * 0.000016387064f * Math.Clamp( VolumeFill, 0.01f, 1f );
		var mass = MathF.Max( Body.Mass, 0.01f );
		var gravity = Scene.PhysicsWorld.Gravity;
		var dt = MathF.Max( Scene.FixedDelta, 0.001f );
		// Each point is the center of one octant; project its half extents onto world Z.
		var halfHeight = (MathF.Abs( WorldRotation.Forward.z ) * size.x
			+ MathF.Abs( WorldRotation.Left.z ) * size.y + MathF.Abs( WorldRotation.Up.z ) * size.z) * 0.25f;
		WaterSample rippleSample = default;
		var ripplePosition = Vector3.Zero;
		var rippleSpeed = 0f;
		for ( var index = 0; index < 8; index++ )
		{
			var local = VolumeCenter + new Vector3( (index & 1) == 0 ? -VolumeSize.x : VolumeSize.x,
				(index & 2) == 0 ? -VolumeSize.y : VolumeSize.y, (index & 4) == 0 ? -VolumeSize.z : VolumeSize.z ) * 0.25f;
			var point = WorldTransform.PointToWorld( local );
			if ( !WaterSurface.Find( Scene, point, out var sample, halfHeight ) ) continue;
			var submerged = sample.Submersion( point.z, halfHeight );
			if ( submerged <= 0 ) continue;
			SubmergedFraction += submerged / 8f;
			var liftMass = MathF.Min( MathF.Max( sample.Surface.FluidDensity, 1 ) * volume, mass * MaximumLift ) / 8f;
			var relative = Body.GetVelocityAtPoint( point ) - sample.Velocity;
			var dragRate = (1f - MathF.Exp( -MathF.Max( 0, Drag ) * dt )) / dt;
			var force = -gravity * liftMass * submerged - relative * (mass / 8f * dragRate * submerged);
			Body.ApplyForceAt( point, force );
			if ( MathF.Abs( point.z - sample.Height ) <= halfHeight + 4 && relative.Length > rippleSpeed )
			{
				rippleSample = sample; ripplePosition = point; rippleSpeed = relative.Length;
			}
		}
		if ( SubmergedFraction <= 0 ) return;
		// Bounded, exponential damping avoids reversing angular velocity at a long physics timestep.
		Body.AngularVelocity *= MathF.Exp( -MathF.Max( 0, AngularDrag ) * SubmergedFraction * dt );
		if ( !_nativeWarning && Scene.GetAll<WaterVolume>().Any( x => x.Active ) )
		{
			_nativeWarning = true;
			Log.Warning( "VB Water Buoyancy: do not overlap native WaterVolume with sampled water; both would apply buoyancy." );
		}
		if ( EmitRipples && rippleSample.Surface is not null && rippleSpeed > 12 && Time.Now >= _nextRipple )
		{
			_nextRipple = Time.Now + MathF.Max( 0.05f, RippleInterval );
			rippleSample.Surface.AddRipple( ripplePosition, Math.Clamp( rippleSpeed / 160f, 0.15f, 1.5f ) );
		}
	}
}
