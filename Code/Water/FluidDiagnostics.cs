using System;
using VoidBreach.Ocean;

namespace VoidBreach.Water;

public static class FluidDiagnostics
{
	[ConCmd( "vb_fft_water_check" )]
	public static void Check()
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		Log.Info( $"Water GPU resource retirement: pending={FluidGpuResources.PendingBatches}, released={FluidGpuResources.ReleasedBatches}, failed={FluidGpuResources.FailedBatches}" );
		foreach ( var water in scene.GetAll<VBFFTOcean>() )
			Log.Info( $"FFT water {water.GameObject.Name}: {water.Shape}, nativeVolume={water.PhysicsVolume.IsValid()}, density={water.FluidDensity}, current={water.CurrentSpeed}, retainedFFT={water.PhysicsRetainedEnergy:P1}, clock={water.SimulationClock:F2}, wind={water.CurrentWindDirection}, impacts={water.SurfaceImpactCount}, shots={water.ShotImpactCount}, entries/exits={water.CharacterImpactCount}, energy={water.InteractionEnergy:F2}, asleep={water.InteractionsSleeping}, spray={water.SplashParticleCount}" );
		foreach ( var water in scene.GetAll<VBFFTOcean>() )
			Log.Info( $"Buoyancy sampling {water.GameObject.Name}: spacing={water.PhysicsSampleSpacing}, cacheHits={water.PhysicsCacheHits}, cacheMisses={water.PhysicsCacheMisses} (current Fourier time)" );
		foreach ( var water in scene.GetAll<VBFFTOcean>() )
			Log.Info( $"Universal interactions {water.GameObject.Name}: tracked={water.TrackedInteractionCount}, automatic={water.AutomaticImpactCount}, gpuSteps={water.InteractionGpuDispatches}, dropped={water.DroppedInteractionCount}, resolution={water.InteractionResolution}, tick={water.InteractionFrequency}Hz" );
		foreach ( var body in scene.GetAll<VBFluidBody>() )
			if ( body.Body.IsValid() && body.Active ) Log.Info( $"Fluid body {body.GameObject.Name}: pos={body.WorldPosition}, velocity={body.Body.Velocity}, proxy={body.Body.IsProxy}" );
		foreach ( var swim in scene.GetAll<VBMoveModeSwim>() )
			Log.Info( $"FFT swimmer {swim.GameObject.Name}: waterLevel={swim.SampledWaterLevel:F2}" );
	}
}
