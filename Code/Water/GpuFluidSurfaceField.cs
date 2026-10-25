using System;

namespace VoidBreach.Water;

/// <summary>Local GPU wave clipmap. Only stamps and a domain/current map travel CPU to GPU.</summary>
internal sealed class GpuFluidSurfaceField : IDisposable
{
	private Texture[] _states;
	private Texture _domain;
	private GpuBuffer<Vector4> _sources;
	private ComputeShader[] _steps, _resolves;
	private int _readIndex;
	private readonly Vector4[] _stamps = new Vector4[128];
	private readonly Vector4[] _domainPixels = new Vector4[64 * 64];
	private int _count, _resolution, _domainHash;
	private bool _clear = true, _domainReady;
	private Vector2 _renderOrigin;
	private float _renderCell, _pendingTime, _remaining;
	private float _sleepDuration = 16;
	public Texture Output { get; private set; }
	public Vector2 Origin { get; private set; }
	public float CellSize { get; private set; }
	public bool Sleeping => _remaining <= 0 && _count == 0;
	public int DispatchCount { get; private set; }

	public void Prepare( FluidSurfaceField cpu, int resolution, int domainHash, Func<Vector2, Vector4> domain )
	{
		resolution = resolution >= 256 ? 256 : resolution >= 128 ? 128 : 64;
		if ( Output is null || _states is null || _steps is null || _resolves is null || _resolution != resolution )
		{
			Dispose(); _resolution = resolution;
			Texture State() => Texture.Create( resolution, resolution, ImageFormat.RGBA32323232F )
				.WithName( $"vb_water_state_{Guid.NewGuid():N}" ).WithAnonymous( false ).WithUAVBinding().WithGPUOnlyUsage().Finish();
			_states = new[] { State(), State() }; Output = State(); _readIndex = 0;
			_domain = Texture.Create( 64, 64, ImageFormat.RGBA32323232F )
				.WithName( $"vb_water_domain_{Guid.NewGuid():N}" ).WithAnonymous( false ).WithDynamicUsage().Finish();
			_sources = new GpuBuffer<Vector4>( 128 );
			_steps = new ComputeShader[2]; _resolves = new ComputeShader[2];
			for ( var i = 0; i < 2; i++ )
			{
				// Each pass owns stable texture handles for its entire lifetime. Rebinding
				// the same attributes during queued dispatches destroys their old handles.
				_steps[i] = new ComputeShader( "water/shaders/fft/interaction_step.shader" );
				_steps[i].Attributes.Set( "StateIn", _states[i] );
				_steps[i].Attributes.Set( "StateOut", _states[i ^ 1] );
				_steps[i].Attributes.Set( "DomainMap", _domain );
				_steps[i].Attributes.Set( "Sources", _sources );
				_resolves[i] = new ComputeShader( "water/shaders/fft/interaction_resolve.shader" );
				_resolves[i].Attributes.Set( "StateIn", _states[i] );
				_resolves[i].Attributes.Set( "SurfaceOut", Output );
			}
			_clear = true; _domainReady = false;
		}
		var changed = !_domainReady || Origin != cpu.Origin || CellSize != cpu.CellSize * 64 / resolution || domainHash != _domainHash;
		if ( _domainReady && (domainHash != _domainHash || CellSize != cpu.CellSize * 64 / resolution) ) _clear = true;
		Origin = cpu.Origin; CellSize = cpu.CellSize * 64 / resolution;
		if ( !changed ) return;
		for ( var y = 0; y < 64; y++ )
		for ( var x = 0; x < 64; x++ ) _domainPixels[y * 64 + x] = domain( Origin + new Vector2( x, y ) * cpu.CellSize );
		_domain.Update<Vector4>( _domainPixels, 0, 0, 64, 64 ); _domainHash = domainHash; _domainReady = true;
	}

	public bool Impulse( Vector2 position, float radius, float strength, Vector2 direction )
	{
		if ( Output is null || _count >= 64 ) return false;
		var p = (position - Origin) / CellSize;
		if ( p.x < 0 || p.y < 0 || p.x >= _resolution - 1 || p.y >= _resolution - 1 ) return false;
		if ( Sleeping ) _clear = true;
		_remaining = _sleepDuration;
		_stamps[_count * 2] = new Vector4( position.x, position.y, Math.Clamp( radius, CellSize, 256 ), Math.Clamp( strength, -160, 160 ) );
		_stamps[_count * 2 + 1] = new Vector4( direction.x, direction.y, 0, 0 ); _count++;
		return true;
	}

	public void Advance( float delta, float damping )
	{
		_sleepDuration = Math.Clamp( 24 / MathF.Max( damping, 0.05f ), 8, 180 );
		if ( _count > 0 ) _remaining = _sleepDuration;
		else _remaining = MathF.Max( 0, _remaining - delta );
		if ( !Sleeping ) _pendingTime = MathF.Min( _pendingTime + delta, 0.1f );
	}

	// Called exclusively by the existing opaque FFT render callback, before water draws.
	public void Render( float speed, float damping, float viscosity, float maximumHeight )
	{
		if ( Output is null || _states is null || _steps is null || _resolves is null ) return;
		var shifted = _renderOrigin != Origin || _renderCell != CellSize;
		if ( Sleeping && !_clear && !shifted ) return;
		if ( _pendingTime <= 0 && _count == 0 && !_clear && !shifted ) return;
		if ( _renderCell != CellSize ) _clear = true;
		if ( _count > 0 ) _sources.SetData( _stamps );
		var time = _pendingTime; _pendingTime = 0;
		// CFL bound for the explicit 2D wave Laplacian. Advection is semi-Lagrangian.
		var steps = Math.Max( 1, (int)MathF.Ceiling( time * speed / (CellSize * 0.45f) ) );
		var dt = time / steps;
		for ( var i = 0; i < steps; i++ )
		{
			// A UAV barrier only orders writes; it does not transition a texture from
			// storage-image writes to sampled-image reads when the ping-pong roles swap.
			var read = _states[_readIndex]; var write = _states[_readIndex ^ 1];
			Graphics.ResourceBarrierTransition( read, Sandbox.Rendering.ResourceState.NonPixelShaderResource );
			Graphics.ResourceBarrierTransition( write, Sandbox.Rendering.ResourceState.UnorderedAccess );
			var step = _steps[_readIndex]; var a = step.Attributes;
			a.Set( "SourceCount", i == 0 ? _count : 0 );
			a.Set( "GridResolution", _resolution ); a.Set( "CellSize", CellSize ); a.Set( "GridOrigin", Origin );
			a.Set( "SourceShift", i == 0 && !_clear ? (Origin - _renderOrigin) / CellSize : Vector2.Zero );
			a.Set( "ClearState", i == 0 && (_clear || Sleeping) ? 1 : 0 );
			a.Set( "Delta", dt ); a.Set( "WaveSpeed", speed ); a.Set( "Damping", damping );
			a.Set( "Viscosity", viscosity ); a.Set( "MaximumHeight", maximumHeight );
			step.Dispatch( _resolution, _resolution, 1 ); Graphics.UavBarrier( write );
			_readIndex ^= 1; DispatchCount++;
		}
		Graphics.ResourceBarrierTransition( _states[_readIndex], Sandbox.Rendering.ResourceState.NonPixelShaderResource );
		Graphics.ResourceBarrierTransition( Output, Sandbox.Rendering.ResourceState.UnorderedAccess );
		var resolve = _resolves[_readIndex]; var r = resolve.Attributes;
		r.Set( "GridResolution", _resolution ); r.Set( "CellSize", CellSize );
		resolve.Dispatch( _resolution, _resolution, 1 ); Graphics.UavBarrier( Output );
		Graphics.ResourceBarrierTransition( Output, Sandbox.Rendering.ResourceState.AllShaderResource );
		_renderOrigin = Origin; _renderCell = CellSize; _count = 0; _clear = false;
	}

	public void Dispose()
	{
		if ( Output is not null )
			FluidGpuResources.Retire( new[] { _states?[0], _states?[1], _domain, Output }, new GpuBuffer[] { _sources },
				new[] { _steps?[0], _steps?[1], _resolves?[0], _resolves?[1] } );
		_states = null; _steps = null; _resolves = null; _domain = null; Output = null; _sources = null;
		_count = 0; _pendingTime = 0; _remaining = 0; _domainReady = false; _clear = true;
	}
}
