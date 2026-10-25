using System;

namespace VoidBreach.Ocean;

/// <summary>Deterministic, band-limited JONSWAP spectrum, built only when settings change.</summary>
internal static class FftOceanSpectrum
{
	public const int Resolution = 128;
	public const float UnitsPerMeter = 39.3700787f;
	public const float LongPatch = 256f;
	public const float ShortPatch = 32f;
	public const float LoopPeriod = 256f;

	/// <summary>Integral-normalised directional density. A diffuse floor keeps cross waves present.</summary>
	public static float Spreading( float alignment, float diffusion )
	{
		const float backscatter = 0.12f;
		var directed = alignment * alignment * (alignment >= 0 ? 1 : backscatter)
			/ (0.5f * MathF.PI * (1 + backscatter));
		return directed * (1 - diffusion) + diffusion / (2 * MathF.PI);
	}

	public static Vector4[] Build( float windSpeed, Vector2 direction, float significantHeight, int seed, float diffusion = 0.45f )
	{
		windSpeed = Math.Clamp( windSpeed, 1, 25 );
		diffusion = Math.Clamp( diffusion, 0.05f, 1 );
		direction = direction.LengthSquared > 0.0001f ? direction.Normal : new Vector2( 1, 0 );
		const int n = Resolution;
		var noise = new Vector2[n * n * 2];
		var random = new Random( seed );
		double energy = 0;
		for ( var cascade = 0; cascade < 2; cascade++ )
		{
			var patch = cascade == 0 ? LongPatch : ShortPatch;
			var dk = 2f * MathF.PI / patch;
			for ( var y = 0; y < n; y++ )
			for ( var x = 0; x < n; x++ )
			{
				var kx = (x < n / 2 ? x : x - n) * dk;
				var ky = (y < n / 2 ? y : y - n) * dk;
				var k = MathF.Sqrt( kx * kx + ky * ky );
				// Keep bands disjoint. Suppress Nyquist axes so odd derivatives stay Hermitian.
				var split = 2f * MathF.PI / 16f;
				if ( k < 0.00001f || x == n / 2 || y == n / 2
					|| (cascade == 0 ? k >= split : k < split) ) continue;
				var omega = MathF.Sqrt( 9.81f * k );
				const float fetch = 10000f;
				var peak = 22f * MathF.Pow( 9.81f * 9.81f / (windSpeed * fetch), 1f / 3f );
				var alpha = 0.076f * MathF.Pow( windSpeed * windSpeed / (fetch * 9.81f), 0.22f );
				var sigma = omega <= peak ? 0.07f : 0.09f;
				var r = (omega - peak) / (sigma * peak);
				var density = alpha * 9.81f * 9.81f / MathF.Pow( omega, 5f )
					* MathF.Exp( -1.25f * MathF.Pow( peak / omega, 4f ) )
					* MathF.Pow( 3.3f, MathF.Exp( -0.5f * r * r ) );
				var alignment = (kx * direction.x + ky * direction.y) / k;
				var spreading = Spreading( alignment, diffusion );
				// Convert frequency density to a 2D wave-vector density, with short-wave damping.
				var power = density * spreading * (9.81f / (2f * omega)) / k * MathF.Exp( -k * k * 0.0025f );
				var magnitude = MathF.Sqrt( MathF.Max( power, 0 ) * 0.5f ) * dk;
				var radius = MathF.Sqrt( -2f * MathF.Log( MathF.Max( (float)random.NextDouble(), 1e-7f ) ) );
				var angle = (float)random.NextDouble() * 2f * MathF.PI;
				var value = new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * (radius * magnitude);
				noise[(cascade * n + y) * n + x] = value;
				energy += 2.0 * value.LengthSquared;
			}
		}
		// Hs = 4 sigma. The inverse FFT is deliberately unnormalised: coefficients are in metres.
		var gain = significantHeight * 0.25f / MathF.Sqrt( (float)Math.Max( energy, 1e-20 ) );
		var packed = new Vector4[noise.Length];
		for ( var cascade = 0; cascade < 2; cascade++ )
		for ( var y = 0; y < n; y++ )
		for ( var x = 0; x < n; x++ )
		{
			var index = (cascade * n + y) * n + x;
			var a = noise[index] * gain;
			var b = noise[(cascade * n + (n - y) % n) * n + (n - x) % n] * gain;
			packed[index] = new Vector4( a.x, a.y, b.x, -b.y );
		}
		return packed;
	}
}
