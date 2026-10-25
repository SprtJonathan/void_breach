using System;
using System.Numerics;
using VoidBreach.Ocean;

internal static class FftOceanChecks
{
	private const int N = FftOceanSpectrum.Resolution;
	private static int _checks;
	private static void Check( bool value, string message )
	{
		_checks++;
		if ( !value ) throw new Exception( message );
	}
	private static Complex Height( Vector4 h0, double k, double time )
	{
		var fundamental = 2 * Math.PI / FftOceanSpectrum.LoopPeriod;
		var omega = Math.Floor( Math.Sqrt( 9.81 * k ) / fundamental ) * fundamental;
		var phase = Complex.FromPolarCoordinates( 1, omega * time );
		return new Complex( h0.x, h0.y ) / phase + new Complex( h0.z, h0.w ) * phase;
	}
	private static void Transform( Complex[] values )
	{
		// Reference Cooley-Tukey implementation, compared to direct Fourier sums below.
		for ( int i = 1, j = 0; i < N; i++ )
		{
			var bit = N >> 1;
			for ( ; (j & bit) != 0; bit >>= 1 ) j ^= bit;
			j ^= bit;
			if ( i < j ) (values[i], values[j]) = (values[j], values[i]);
		}
		for ( var length = 2; length <= N; length *= 2 )
		for ( var start = 0; start < N; start += length )
		for ( var offset = 0; offset < length / 2; offset++ )
		{
			var a = values[start + offset];
			var b = values[start + offset + length / 2] * Complex.FromPolarCoordinates( 1, 2 * Math.PI * offset / length );
			values[start + offset] = a + b; values[start + offset + length / 2] = a - b;
		}
	}
	private static Complex[] Transform2D( Complex[] input )
	{
		var result = (Complex[])input.Clone();
		var line = new Complex[N];
		for ( var y = 0; y < N; y++ )
		{
			Array.Copy( result, y * N, line, 0, N ); Transform( line ); Array.Copy( line, 0, result, y * N, N );
		}
		for ( var x = 0; x < N; x++ )
		{
			for ( var y = 0; y < N; y++ ) line[y] = result[y * N + x];
			Transform( line );
			for ( var y = 0; y < N; y++ ) result[y * N + x] = line[y];
		}
		return result;
	}
	public static void Main()
	{
		var direction = new Vector2( 1, 0.3f ).Normal;
		foreach ( var diffusion in new[] { 0.05f, 0.55f, 1f } )
		{
			double integral = 0;
			for ( var i = 0; i < 4096; i++ ) integral += FftOceanSpectrum.Spreading( MathF.Cos( i * 2 * MathF.PI / 4096 ), diffusion ) * 2 * Math.PI / 4096;
			Check( Math.Abs( integral - 1 ) < 0.0001, "directional spreading conserves integrated energy" );
			Check( FftOceanSpectrum.Spreading( 0, diffusion ) > 0, "crosswind waves are never suppressed" );
		}
		foreach ( var wind in new[] { 1f, 10f, 25f } )
		{
			var spectrum = FftOceanSpectrum.Build( wind, direction, 1.2f, 17 );
			var again = FftOceanSpectrum.Build( wind, direction, 1.2f, 17 );
			double energy = 0;
			for ( var cascade = 0; cascade < 2; cascade++ )
			for ( var y = 0; y < N; y++ )
			for ( var x = 0; x < N; x++ )
			{
				var index = (cascade * N + y) * N + x;
				var h = spectrum[index];
				Check( float.IsFinite( h.x + h.y + h.z + h.w ), "finite spectrum" );
				Check( h == again[index], "deterministic seed" );
				var opposite = spectrum[(cascade * N + (N - y) % N) * N + (N - x) % N];
				Check( h.z == opposite.x && h.w == -opposite.y, "conjugate partner" );
				if ( (x == 0 && y == 0) || x == N / 2 || y == N / 2 ) Check( h == Vector4.Zero, "DC/Nyquist suppressed" );
				energy += 2.0 * (h.x * h.x + h.y * h.y);
			}
			Check( Math.Abs( 4 * Math.Sqrt( energy ) - 1.2 ) < 1e-5, "Hs independent of wind and grid normalisation" );
		}
		var zero = FftOceanSpectrum.Build( 10, direction, 0, 17 );
		foreach ( var value in zero ) Check( value == Vector4.Zero, "zero height is exactly flat" );
		var source = FftOceanSpectrum.Build( 10, direction, 1.2f, 17 );
		foreach ( var time in new[] { 0.0, 13.7, 255.99 } )
		for ( var cascade = 0; cascade < 2; cascade++ )
		{
			var fields = new Complex[8][];
			for ( var field = 0; field < 8; field++ ) fields[field] = new Complex[N * N];
			var dk = 2 * Math.PI / (cascade == 0 ? 256 : 32);
			for ( var y = 0; y < N; y++ )
			for ( var x = 0; x < N; x++ )
			{
				var kx = (x < N / 2 ? x : x - N) * dk; var ky = (y < N / 2 ? y : y - N) * dk;
				var k = Math.Sqrt( kx * kx + ky * ky );
				if ( k < 1e-5 ) continue;
				var index = y * N + x;
				var h = Height( source[cascade * N * N + index], k, time );
				Check( Complex.Abs( h - Height( source[cascade * N * N + index], k, time + 256 ) ) < 1e-9, "seamless temporal loop" );
				fields[0][index] = Complex.ImaginaryOne * h * (0.85 * kx / k);
				fields[1][index] = Complex.ImaginaryOne * h * (0.85 * ky / k);
				fields[2][index] = h;
				fields[3][index] = h * (-0.85 * kx * ky / k);
				fields[4][index] = Complex.ImaginaryOne * h * kx;
				fields[5][index] = Complex.ImaginaryOne * h * ky;
				fields[6][index] = h * (-0.85 * kx * kx / k);
				fields[7][index] = h * (-0.85 * ky * ky / k);
			}
			for ( var pair = 0; pair < 4; pair++ )
			{
				var packed = new Complex[N * N];
				for ( var i = 0; i < packed.Length; i++ ) packed[i] = fields[pair * 2][i] + Complex.ImaginaryOne * fields[pair * 2 + 1][i];
				var transformed = Transform2D( packed );
				foreach ( var point in new[] { (0, 0), (1, 127), (37, 61), (127, 127) } )
				for ( var channel = 0; channel < 2; channel++ )
				{
					Complex expected = 0;
					for ( var y = 0; y < N; y++ )
					for ( var x = 0; x < N; x++ ) expected += fields[pair * 2 + channel][y * N + x]
						* Complex.FromPolarCoordinates( 1, 2 * Math.PI * (x * point.Item1 + y * point.Item2) / N );
					var actual = transformed[point.Item2 * N + point.Item1];
					Check( Math.Abs( expected.Imaginary ) < 1e-8, "real spatial field" );
					Check( Math.Abs( expected.Real - (channel == 0 ? actual.Real : actual.Imaginary) ) < 1e-8, "packed inverse FFT matches direct Fourier sum" );
				}
			}
		}
		Console.WriteLine( $"FFT ocean: {_checks} checks passed (spectrum, Hs, Hermitian symmetry, loop, 8 packed spatial fields versus direct Fourier sums)." );
	}
}
