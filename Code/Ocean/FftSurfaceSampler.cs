using System;
using System.Collections.Generic;

namespace VoidBreach.Ocean;

/// <summary>CPU projection of energetic FFT modes. No GPU readback; high-frequency detail is filtered for physics.</summary>
internal sealed class FftSurfaceSampler
{
	private struct Mode
	{
		public float Kx, Ky, K, Omega;
		public Vector4 Initial, Swell;
		public float Real, Imaginary, DtReal, DtImaginary;
	}
	private Mode[] _modes = Array.Empty<Mode>();
	private float _lastTime = float.NaN, _lastDiffusion;
	private Vector2 _lastBaseWind, _lastWind;
	private readonly record struct PhysicsSample( Vector3 Displacement, Vector3 Normal, Vector3 Velocity );
	private readonly Dictionary<(int, int), PhysicsSample> _physicsSamples = new();
	private float _cachedSpacing, _cachedChop;
	public int PhysicsCacheHits { get; private set; }
	public int PhysicsCacheMisses { get; private set; }
	public int PhysicsCacheSize => _physicsSamples.Count;
	public float RetainedEnergy { get; private set; }
	public void Build( Vector4[] spectrum, int budget, Vector4[] swell = null )
	{
		_lastTime = float.NaN;
		_physicsSamples.Clear();
		var candidates = new List<Mode>();
		const int n = FftOceanSpectrum.Resolution;
		double total = 0;
		for ( var cascade = 0; cascade < 2; cascade++ )
		for ( var y = 0; y < n; y++ )
		for ( var x = 0; x < n; x++ )
		{
			var ix = x < n / 2 ? x : x - n; var iy = y < n / 2 ? y : y - n;
			if ( iy < 0 || (iy == 0 && ix <= 0) ) continue;
			var dk = 2 * MathF.PI / (cascade == 0 ? FftOceanSpectrum.LongPatch : FftOceanSpectrum.ShortPatch);
			var kx = ix * dk; var ky = iy * dk; var k = MathF.Sqrt( kx * kx + ky * ky );
			var h0 = spectrum[(cascade * n + y) * n + x];
			var hSwell = swell is null ? Vector4.Zero : swell[(cascade * n + y) * n + x];
			var energy = h0.LengthSquared + hSwell.LengthSquared;
			total += energy;
			if ( energy <= 0 || k > 2 * MathF.PI / 2 ) continue;
			var fundamental = 2 * MathF.PI / FftOceanSpectrum.LoopPeriod;
			candidates.Add( new Mode { Kx = kx, Ky = ky, K = k, Omega = MathF.Floor( MathF.Sqrt( 9.81f * k ) / fundamental ) * fundamental, Initial = h0, Swell = hSwell } );
		}
		float Power( Mode m ) => m.Initial.LengthSquared + m.Swell.LengthSquared;
		candidates.Sort( (a, b) => Power( b ).CompareTo( Power( a ) ) );
		var count = Math.Min( Math.Clamp( budget, 16, 512 ), candidates.Count );
		_modes = candidates.GetRange( 0, count ).ToArray();
		double retained = 0;
		foreach ( var m in _modes ) retained += Power( m );
		RetainedEnergy = total > 0 ? (float)(retained / total) : 1;
	}
	public void Update( float time ) => Update( time, Vector2.Zero, Vector2.Zero, 1 );
	public void Update( float time, Vector2 baseWind, Vector2 currentWind, float diffusion )
	{
		if ( time == _lastTime && baseWind == _lastBaseWind && currentWind == _lastWind && diffusion == _lastDiffusion ) return;
		_lastTime = time; _lastBaseWind = baseWind; _lastWind = currentWind; _lastDiffusion = diffusion;
		_physicsSamples.Clear(); PhysicsCacheHits = PhysicsCacheMisses = 0;
		for ( var i = 0; i < _modes.Length; i++ )
		{
			ref var m = ref _modes[i]; var h = m.Initial;
			var alignment = (m.Kx * baseWind.x + m.Ky * baseWind.y) / m.K;
			var current = (m.Kx * currentWind.x + m.Ky * currentWind.y) / m.K;
			var positive = MathF.Sqrt( FftOceanSpectrum.Spreading( current, diffusion ) / FftOceanSpectrum.Spreading( alignment, diffusion ) );
			var negative = MathF.Sqrt( FftOceanSpectrum.Spreading( -current, diffusion ) / FftOceanSpectrum.Spreading( -alignment, diffusion ) );
			h = new Vector4( h.x * positive, h.y * positive, h.z * negative, h.w * negative ) + m.Swell;
			var (sn, cs) = MathF.SinCos( m.Omega * time );
			var ar = h.x * cs + h.y * sn; var ai = h.y * cs - h.x * sn;
			var br = h.z * cs - h.w * sn; var bi = h.w * cs + h.z * sn;
			m.Real = ar + br; m.Imaginary = ai + bi;
			m.DtReal = (ai - bi) * m.Omega; m.DtImaginary = (-ar + br) * m.Omega;
		}
	}
	/// <summary>Shared tangent samples for nearby buoyancy bodies, refreshed with the Fourier coefficients.</summary>
	public void SampleForPhysics( Vector2 position, float chop, float spacing, out Vector3 displacement, out Vector3 normal, out Vector3 velocity )
	{
		if ( spacing <= 0 || !float.IsFinite( spacing ) ) { Sample( position, chop, out displacement, out normal, out velocity ); return; }
		spacing = Math.Clamp( spacing, 4, 32 );
		if ( spacing != _cachedSpacing || chop != _cachedChop )
		{
			_physicsSamples.Clear(); _cachedSpacing = spacing; _cachedChop = chop;
		}
		var key = ((int)MathF.Floor( position.x / spacing ), (int)MathF.Floor( position.y / spacing ));
		var center = new Vector2( (key.Item1 + 0.5f) * spacing, (key.Item2 + 0.5f) * spacing );
		if ( !_physicsSamples.TryGetValue( key, out var sample ) )
		{
			Sample( center, chop, out var d, out var n, out var v );
			sample = new( d, n, v ); PhysicsCacheMisses++;
			// Many separated volumes or bodies cannot grow the cache without a bound.
			if ( _physicsSamples.Count < 1024 ) _physicsSamples.Add( key, sample );
		}
		else PhysicsCacheHits++;
		if ( sample.Normal.z < 0.5f ) { Sample( position, chop, out displacement, out normal, out velocity ); return; }
		displacement = sample.Displacement; normal = sample.Normal; velocity = sample.Velocity;
		var delta = position - center;
		// Preserve the local slope rather than snapping bodies to a flat tile height.
		displacement.z -= (normal.x * delta.x + normal.y * delta.y) / normal.z;
	}

	public void Sample( Vector2 position, float chop, out Vector3 displacement, out Vector3 normal, out Vector3 velocity )
	{
		var q = position / FftOceanSpectrum.UnitsPerMeter;
		Vector3 Evaluate( Vector2 p, bool details, out Vector3 tx, out Vector3 ty, out Vector3 v )
		{
			var d = Vector3.Zero; tx = Vector3.Forward; ty = new Vector3( 0, 1, 0 ); v = Vector3.Zero;
			foreach ( var m in _modes )
			{
				var phase = p.x * m.Kx + p.y * m.Ky; var (sn, cs) = MathF.SinCos( phase );
				var h = 2 * (m.Real * cs - m.Imaginary * sn); var s = 2 * (-m.Real * sn - m.Imaginary * cs);
				var dx = chop * m.Kx / m.K; var dy = chop * m.Ky / m.K;
				d += new Vector3( s * dx, s * dy, h );
				if ( !details ) continue;
				tx += new Vector3( -h * dx * m.Kx, -h * dy * m.Kx, s * m.Kx );
				ty += new Vector3( -h * dx * m.Ky, -h * dy * m.Ky, s * m.Ky );
				var hd = 2 * (m.DtReal * cs - m.DtImaginary * sn); var sd = 2 * (-m.DtReal * sn - m.DtImaginary * cs);
				v += new Vector3( sd * dx, sd * dy, hd );
			}
			return d;
		}
		// Invert horizontal displacement, using the same signs and units as the GPU field.
		var p = q;
		if ( chop > 0.0001f )
			for ( var i = 0; i < 3; i++ ) { var d = Evaluate( p, false, out _, out _, out _ ); p = q - new Vector2( d.x, d.y ); }
		displacement = Evaluate( p, true, out var tangentX, out var tangentY, out velocity ) * FftOceanSpectrum.UnitsPerMeter;
		normal = Vector3.Cross( tangentX, tangentY ).Normal;
		if ( normal.z < 0.1f ) normal = new Vector3( normal.x, normal.y, 0.1f ).Normal;
		velocity *= FftOceanSpectrum.UnitsPerMeter;
	}
}
