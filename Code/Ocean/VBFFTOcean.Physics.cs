using System;
using VoidBreach.Water;

namespace VoidBreach.Ocean;

public sealed partial class VBFFTOcean
{
	private GameObject _volumeObject;
	private ModelCollider _volumeCollider;
	private WaterVolume _nativeVolume;
	private int _volumeHash;
	private float _impactBudgetReset;
	private int _impactBudgetCount;
	public WaterVolume PhysicsVolume => _nativeVolume;
	private Vector2 Advection => Direction * (CurrentSpeed * (Game.IsPlaying ? SimulationClock : _editorClock));
	private void EnsurePhysicsVolume()
	{
		if ( !NativePhysics && !Interactions ) { ReleasePhysicsVolume(); return; }
		var hash = HashCode.Combine( _domainHash, Depth, WaveHeight, HorizonDistance );
		if ( !_volumeObject.IsValid() || hash != _volumeHash )
		{
			ReleasePhysicsVolume();
			var builder = Model.Builder.WithName( "vb_fft_water_trigger" );
			var depth = Math.Clamp( Depth, 32, 20000 );
			var padding = Math.Clamp( WaveHeight, 0, 5 ) * FftOceanSpectrum.UnitsPerMeter * 1.5f + 24;
			if ( Shape == FftWaterShape.Ocean )
			{
				var extent = Math.Clamp( HorizonDistance, 16384, 500000 );
				builder.AddCollisionBox( new Vector3( extent * 2, extent * 2, depth + padding ), new Vector3( 0, 0, (padding - depth) * 0.5f ) );
			}
			else if ( Shape == FftWaterShape.Lake )
			{
				var hull = new List<Vector3>();
				for ( var i = 0; i < 32; i++ )
				{
					var a = i * MathF.PI * 2 / 32;
					var p = new Vector3( MathF.Cos( a ) * _domain.Size.x * 0.5f, MathF.Sin( a ) * _domain.Size.y * 0.5f, 0 );
					hull.Add( p + Vector3.Up * padding ); hull.Add( p - Vector3.Up * depth );
				}
				builder.AddCollisionHull( hull );
			}
			else
			{
				if ( _domain.ControlRows.Count < 2 ) return;
				for ( var i = 1; i < _domain.ControlRows.Count; i++ )
				{
					var a = _domain.ControlRows[i - 1]; var b = _domain.ControlRows[i];
					var hull = new List<Vector3>();
					foreach ( var p in new[] { a.Left, a.Right, b.Left, b.Right } )
					{ hull.Add( p + Vector3.Up * padding ); hull.Add( p - Vector3.Up * depth ); }
					builder.AddCollisionHull( hull );
				}
			}
			_volumeObject = new GameObject( GameObject, false, "Native FFT water volume (generated)" );
			_volumeObject.Flags = GameObjectFlags.NotSaved;
			_volumeObject.NetworkMode = NetworkMode.Never; _volumeObject.Tags.Add( "water" );
			_volumeCollider = _volumeObject.AddComponent<ModelCollider>();
			_volumeCollider.Model = builder.Create(); _volumeCollider.Static = true; _volumeCollider.IsTrigger = true;
			var material = Surface.FindByName( "water" );
			if ( material.IsValid() ) _volumeCollider.Surface = material;
			var bridge = _volumeObject.AddComponent<VBFluidVolumeBridge>(); bridge.Surface = this; bridge.VolumeCollider = _volumeCollider;
			_nativeVolume = _volumeObject.AddComponent<WaterVolume>();
			_volumeCollider.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotCloned | ComponentFlags.NotNetworked;
			bridge.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotCloned | ComponentFlags.NotNetworked;
			_nativeVolume.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotCloned | ComponentFlags.NotNetworked;
			_nativeVolume.IgnoreTags = new TagSet( new[] { VBFluidBody.ManagedTag } ); _volumeHash = hash;
		}
		_volumeObject.WorldTransform = DomainTransform;
		_nativeVolume.FluidDensity = Math.Clamp( FluidDensity, 1, 15000 );
		_nativeVolume.LinearDrag = Math.Clamp( LinearDrag, 0, 10 ); _nativeVolume.AngularDrag = Math.Clamp( AngularDrag, 0, 10 );
		_nativeVolume.FluidVelocity = new Vector3( Direction.x, Direction.y, 0 ) * CurrentSpeed;
		_nativeVolume.SurfaceOffset = 0; _nativeVolume.WaveAmplitude = 0; _nativeVolume.WaveFrequency = 0;
		_nativeVolume.Enabled = NativePhysics; _volumeObject.Enabled = true;
	}
	private void ReleasePhysicsVolume()
	{
		if ( _volumeObject.IsValid() ) _volumeObject.Destroy();
		_volumeObject = null; _volumeCollider = null; _nativeVolume = null;
	}
	public override bool TrySample( Vector3 position, out WaterSample sample ) => SampleSurface( position, false, out sample );
	internal bool TrySampleForBuoyancy( Vector3 position, out WaterSample sample ) => SampleSurface( position, true, out sample );
	private bool SampleSurface( Vector3 position, bool buoyancy, out WaterSample sample )
	{
		sample = default;
		if ( !Active ) return false;
		EnsureDomain();
		var local = DomainTransform.PointToLocal( position );
		if ( !_domain.Sample( local, out var baseHeight, out var flow, out var baseNormal ) ) return false;
		if ( _spectrumData is null ) EnsureSpectrum();
		var point = new Vector2( position.x, position.y ) - Advection;
		Vector3 displacement, normal, orbitalVelocity;
		if ( buoyancy ) _sampler.SampleForPhysics( point, Math.Clamp( Choppiness, 0, 1.5f ), PhysicsSampleSpacing, out displacement, out normal, out orbitalVelocity );
		else _sampler.Sample( point, Math.Clamp( Choppiness, 0, 1.5f ), out displacement, out normal, out orbitalVelocity );
		var interaction = Interactions && !InteractionsSleeping ? SampleInteractions( new Vector2( position.x, position.y ) ) : Vector4.Zero;
		var height = WorldPosition.z + baseHeight + displacement.z + interaction.x;
		var baseWorldNormal = DomainTransform.NormalToWorld( baseNormal );
		normal = (normal + baseWorldNormal - Vector3.Up + new Vector3( -interaction.y, -interaction.z, 0 )).Normal;
		flow = Shape == FftWaterShape.River ? DomainTransform.NormalToWorld( flow ) : new Vector3( Direction.x, Direction.y, 0 );
		var velocity = flow * CurrentSpeed + orbitalVelocity * Math.Clamp( Speed, 0, 2 );
		sample = new WaterSample( this, height, WorldPosition.z + baseHeight - Math.Clamp( Depth, 32, 20000 ), normal, velocity.ClampLength( 768 ) );
		return true;
	}
	bool IFluidSurfaceSampler.SampleSurface( Vector3 position, out float height, out Vector3 normal )
	{
		var valid = TrySample( position, out var sample ); height = sample.Height; normal = sample.Normal; return valid;
	}
	/// <summary>General surface forcing API: impacts, wakes, rain, effects and future fluid emitters.</summary>
	public bool ApplySurfaceImpulse( Vector3 position, float radius, float strength, Vector2 direction = default )
	{
		if ( !Interactions || !Active || !float.IsFinite( position.x + position.y + position.z + radius + strength ) ) return false;
		if ( Time.Now >= _impactBudgetReset ) { _impactBudgetReset = Time.Now + 1f / Math.Clamp( InteractionFrequency, 15, 60 ); _impactBudgetCount = 0; }
		if ( _impactBudgetCount >= Math.Clamp( InteractionBudget, 1, 64 ) ) { DroppedInteractionCount++; return false; }
		EnsureDomain();
		if ( !_domain.Sample( DomainTransform.PointToLocal( position ), out _, out _, out _ ) ) return false;
		var p = new Vector2( position.x, position.y ); var amount = strength * Math.Clamp( InteractionStrength, 0, 4 );
		if ( MathF.Abs( amount ) < 0.001f ) return false;
		_impactBudgetCount++;
		radius = Math.Clamp( radius, 8, Math.Clamp( InteractionMaximumRadius, 8, 256 ) );
		var fine = _field.Impulse( p, radius, amount );
		var coarse = _coarseField.Impulse( p, radius, amount );
		_gpuField.Impulse( p, radius, amount, direction ); _gpuCoarseField.Impulse( p, radius, amount, direction );
		if ( !fine && !coarse )
		{
			// Bounded normal-only fallback for distant hits, without allocating another simulation.
			_distantRipples[_nextDistantRipple++ % 8] = new Vector4( p.x, p.y, Math.Clamp( MathF.Abs( amount ) / 40, 0.2f, 3 ), Game.IsPlaying ? SimulationClock : _editorClock );
			DistantImpactCount++;
		}
		SurfaceImpactCount++;
		return true;
	}
	public override void AddRipple( Vector3 position, float strength = 1 ) => ApplySurfaceImpulse( position, 40, Math.Clamp( strength, -2, 2 ) * 45 );
}
