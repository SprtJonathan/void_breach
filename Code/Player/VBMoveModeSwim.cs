using System;
using Sandbox.Movement;
using VoidBreach.Ocean;
using VoidBreach.Water;

/// <summary>Retains native swimming controls and animation, with FFT-aware entry/exit and river currents.</summary>
[Title( "VB FFT Swim" ), Category( "Void Breach/Player" )]
public sealed class VBMoveModeSwim : MoveModeSwim
{
	private Vector3 _current;
	public float SampledWaterLevel { get; private set; }
	public override int Score( PlayerController controller )
	{
		_current = Vector3.Zero; SampledWaterLevel = 0;
		if ( !controller.Body.IsValid() ) return -100;
		var inFftVolume = false;
		WaterSample selected = default;
		foreach ( var collider in controller.Body.Touching )
		{
			if ( !collider.IsValid() || !collider.IsTrigger ) continue;
			var water = collider.GetComponentInParent<VBFFTOcean>();
			if ( !water.IsValid() || !water.Active || !water.NativePhysics ) continue;
			inFftVolume = true;
			var height = MathF.Max( controller.CurrentHeight, 1 );
			var center = controller.WorldPosition + Vector3.Up * (height * 0.5f);
			if ( !water.TrySample( center, out var sample ) ) continue;
			if ( selected.Surface is not null && (water.Priority < selected.Surface.Priority
				|| (water.Priority == selected.Surface.Priority && sample.Height <= selected.Height)) ) continue;
			selected = sample;
			SampledWaterLevel = sample.Submersion( center.z, height * 0.5f );
			_current = sample.Velocity.WithZ( sample.Velocity.z * 0.15f ).ClampLength( 512 );
		}
		return inFftVolume ? SampledWaterLevel > SwimLevel ? Priority : -100 : base.Score( controller );
	}
	public override Vector3 UpdateMove( Rotation eyes, Vector3 input ) => base.UpdateMove( eyes, input ) + _current;
}
