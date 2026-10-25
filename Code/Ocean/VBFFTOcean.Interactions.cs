using System;
using VoidBreach.Water;

namespace VoidBreach.Ocean;

public sealed partial class VBFFTOcean
{
	private readonly FluidSurfaceField _field = new();
	private readonly FluidSurfaceField _coarseField = new();
	private readonly GpuFluidSurfaceField _gpuField = new(), _gpuCoarseField = new();
	private GameObject _effectsObject;
	private int _maskDomainHash;
	private Vector2 _maskOrigin = new( float.MaxValue );
	private Vector2 _coarseMaskOrigin = new( float.MaxValue );
	private float _interactionAccumulator;
	private bool _interactionsWereEnabled;
	private Func<Vector2, Vector4> _interactionDomainSampler;
	private readonly Vector4[] _distantRipples = new Vector4[8];
	private int _nextDistantRipple;
	public int SurfaceImpactCount { get; private set; }
	public int DistantImpactCount { get; private set; }
	public bool InteractionsSleeping => _field.IsSleeping && _coarseField.IsSleeping;
	public float InteractionEnergy => _field.Energy + _coarseField.Energy;
	public int InteractionGpuDispatches => _gpuField.DispatchCount + _gpuCoarseField.DispatchCount;
	private Vector4 InteractionDomain( Vector2 p )
	{
		var inside = _domain.Sample( DomainTransform.PointToLocal( new Vector3( p.x, p.y, WorldPosition.z ) ), out _, out var tangent, out _ );
		var flow = Shape == FftWaterShape.River ? DomainTransform.NormalToWorld( tangent ) : new Vector3( Direction.x, Direction.y, 0 );
		return new Vector4( flow.x * CurrentSpeed, flow.y * CurrentSpeed, inside ? 1 : 0, 0 );
	}
	private void StepInteractions( float delta )
	{
		var center = Scene.Camera.IsValid() ? Scene.Camera.WorldPosition : WorldPosition;
		if ( Shape != FftWaterShape.Ocean && !_domain.Sample( DomainTransform.PointToLocal( center ), out _, out _, out _ ) )
		{
			center = WorldPosition;
			if ( Shape == FftWaterShape.River )
			{
				var observer = Scene.Camera.IsValid() ? Scene.Camera.WorldPosition : WorldPosition; var nearest = float.MaxValue;
				foreach ( var row in _domain.Rows )
				{
					var candidate = DomainTransform.PointToWorld( (row.Left + row.Right) * 0.5f );
					var distance = new Vector2( observer.x - candidate.x, observer.y - candidate.y ).LengthSquared;
					if ( distance >= nearest ) continue; nearest = distance; center = candidate;
				}
			}
		}
		if ( !Interactions )
		{
			if ( _interactionsWereEnabled ) ReleaseInteractions();
			_interactionsWereEnabled = false; return;
		}
		_interactionsWereEnabled = true;
		var cell = Math.Clamp( InteractionCellSize, 8, 32 );
		_field.MoveTo( new Vector2( center.x, center.y ), cell );
		_coarseField.MoveTo( new Vector2( center.x, center.y ), cell * 8 );
		var maskHash = HashCode.Combine( _domainHash, WorldTransform, cell );
		void Mask( FluidSurfaceField field )
		{
			for ( var y = 0; y < FluidSurfaceField.Resolution; y++ )
			for ( var x = 0; x < FluidSurfaceField.Resolution; x++ )
			{
				var p = field.Origin + new Vector2( x, y ) * field.CellSize;
				var inside = Shape == FftWaterShape.Ocean || _domain.Sample( DomainTransform.PointToLocal( new Vector3( p.x, p.y, WorldPosition.z ) ), out _, out _, out _ );
				field.SetBlocked( x, y, !inside );
			}
		}
		if ( _maskOrigin != _field.Origin || _maskDomainHash != maskHash ) Mask( _field );
		if ( _coarseMaskOrigin != _coarseField.Origin || _maskDomainHash != maskHash ) Mask( _coarseField );
		_maskOrigin = _field.Origin; _coarseMaskOrigin = _coarseField.Origin; _maskDomainHash = maskHash;
		_field.WaveSpeed = _coarseField.WaveSpeed = Math.Clamp( InteractionWaveSpeed, 16, 600 );
		_field.Damping = _coarseField.Damping = Math.Clamp( InteractionDamping, 0.05f, 8 ) + Math.Clamp( InteractionViscosity, 0, 4 );
		_field.MaximumHeight = _coarseField.MaximumHeight = Math.Clamp( InteractionMaximumHeight, 1, 64 );
		var flow = Direction * CurrentSpeed;
		if ( Shape == FftWaterShape.River && _domain.Sample( DomainTransform.PointToLocal( center ), out _, out var tangent, out _ ) )
		{
			var world = DomainTransform.NormalToWorld( tangent ); flow = new Vector2( world.x, world.y ) * CurrentSpeed;
		}
		if ( Game.IsPlaying ) DetectInteractions( delta, center );
		FlushNetworkInteractions();
		// Bounded CPU approximation for physics queries; never a GPU readback.
		var tick = 1f / Math.Clamp( InteractionFrequency, 15, 60 );
		_interactionAccumulator = MathF.Min( _interactionAccumulator + MathF.Max( delta, 0 ), tick * 2 );
		while ( _interactionAccumulator >= tick )
		{
			_field.Step( tick, flow ); _coarseField.Step( tick, flow );
			_gpuField.Advance( tick, _field.Damping ); _gpuCoarseField.Advance( tick, _coarseField.Damping );
			_interactionAccumulator -= tick;
		}
	}
	private void UploadInteractions()
	{
		if ( !Interactions ) return;
		var hash = HashCode.Combine( _domainHash, DomainTransform, CurrentSpeed, Direction );
		_interactionDomainSampler ??= InteractionDomain;
		_gpuField.Prepare( _field, InteractionResolution, hash, _interactionDomainSampler );
		_gpuCoarseField.Prepare( _coarseField, InteractionResolution, hash, _interactionDomainSampler );
	}
	private void RenderInteractions()
	{
		if ( !Interactions ) return;
		var speed = Math.Clamp( InteractionWaveSpeed, 16, 600 ); var damping = Math.Clamp( InteractionDamping, 0.05f, 8 );
		_gpuField.Render( speed, damping, Math.Clamp( InteractionViscosity, 0, 4 ), Math.Clamp( InteractionMaximumHeight, 1, 64 ) );
		_gpuCoarseField.Render( speed, damping, Math.Clamp( InteractionViscosity, 0, 4 ), Math.Clamp( InteractionMaximumHeight, 1, 64 ) );
	}
	private void ApplyInteractionAttributes( RenderAttributes a )
	{
		if ( !Interactions ) { a.Set( "FluidEnabled", 0f ); for ( var i = 0; i < 8; i++ ) a.Set( $"Ripple{i}", Vector4.Zero ); return; }
		a.Set( "FluidTexture", _gpuField.Output ); a.Set( "FluidOrigin", _gpuField.Origin ); a.Set( "FluidCellSize", _gpuField.CellSize );
		a.Set( "FluidCoarseTexture", _gpuCoarseField.Output ); a.Set( "FluidCoarseOrigin", _gpuCoarseField.Origin ); a.Set( "FluidCoarseCellSize", _gpuCoarseField.CellSize );
		a.Set( "FluidResolution", InteractionResolution >= 256 ? 256f : InteractionResolution >= 128 ? 128f : 64f );
		a.Set( "FluidEnabled", Interactions && (!_gpuField.Sleeping || !_gpuCoarseField.Sleeping) ? 1f : 0f );
		for ( var i = 0; i < 8; i++ ) a.Set( $"Ripple{i}", Interactions ? _distantRipples[i] : Vector4.Zero );
	}
	private Vector4 SampleInteractions( Vector2 p )
	{
		var coarse = _coarseField.Sample( p ); var fine = _field.Sample( p );
		var cell = (p - _field.Origin) / _field.CellSize;
		var edge = MathF.Min( MathF.Min( cell.x, cell.y ), MathF.Min( 63 - cell.x, 63 - cell.y ) );
		var t = Math.Clamp( edge / 6, 0, 1 ); var weight = t * t * (3 - 2 * t);
		var gradient = Vector2.Zero;
		if ( t > 0 && t < 1 )
		{
			var slope = t * (1 - t) / _field.CellSize;
			if ( edge == cell.x ) gradient.x = slope; else if ( edge == 63 - cell.x ) gradient.x = -slope;
			else if ( edge == cell.y ) gradient.y = slope; else gradient.y = -slope;
		}
		var result = coarse + (fine - coarse) * weight;
		result.y += (fine.x - coarse.x) * gradient.x; result.z += (fine.x - coarse.x) * gradient.y;
		return result;
	}
	private void ReleaseInteractions()
	{
		// Detach bindings now; the retired GPU batches stay alive through native
		// frame-end destruction and drain their commands before actual disposal.
		if ( _renderer.IsValid() )
		{
			_renderer.Attributes.Set( "FluidEnabled", 0f );
			_renderer.Attributes.Set( "FluidTexture", Texture.Black );
			_renderer.Attributes.Set( "FluidCoarseTexture", Texture.Black );
		}
		_gpuField.Dispose(); _gpuCoarseField.Dispose(); _field.Reset(); _coarseField.Reset();
		_maskOrigin = _coarseMaskOrigin = new Vector2( float.MaxValue ); _interactionAccumulator = 0;
		Array.Clear( _distantRipples ); ClearTrackedInteractions();
	}
	private void EnsureUnderwaterEffects()
	{
		if ( !UnderwaterEffects )
		{
			if ( _effectsObject.IsValid() ) _effectsObject.Destroy(); _effectsObject = null; return;
		}
		if ( _effectsObject.IsValid() || Scene.GetAll<VBWaterUnderwater>().Any( x => x.Active ) ) return;
		_effectsObject = new GameObject( GameObject, false, "FFT underwater camera effects (generated)" );
		_effectsObject.Flags = GameObjectFlags.NotSaved;
		_effectsObject.NetworkMode = NetworkMode.Never;
		var volume = _effectsObject.AddComponent<PostProcessVolume>();
		volume.SceneVolume = new Sandbox.Volumes.SceneVolume { Type = Sandbox.Volumes.SceneVolume.VolumeTypes.Infinite };
		volume.Priority = 10; volume.BlendWeight = 1;
		_effectsObject.AddComponent<VBWaterUnderwater>(); _effectsObject.Enabled = true;
	}
}
