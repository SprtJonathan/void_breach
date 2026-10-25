using System;
using System.Collections.Generic;
using System.Linq;
using VoidBreach.Ocean;
using VoidBreach.Water;

internal static class FluidSurfaceChecks
{
	private sealed class WaveSurface : IFluidSurfaceSampler
	{
		public bool SampleSurface( Vector3 p, out float height, out Vector3 normal )
		{
			height = MathF.Cos( p.x * 0.04f ) * 8;
			normal = new Vector3( MathF.Sin( p.x * 0.04f ) * 0.32f, 0, 1 ).Normal;
			return true;
		}
	}
	private static int _checks;
	private static void Check( bool value, string message ) { _checks++; if ( !value ) throw new Exception( message ); }
	public static void Main()
	{
		var fluid = new FluidSurfaceField();
		Check( fluid.IsSleeping, "new field sleeps" );
		Check( fluid.Sample( Vector2.Zero ) == Vector4.Zero, "uninitialized field is empty" );
		fluid.MoveTo( Vector2.Zero, 16 );
		var idleVersion = fluid.Version;
		for ( var i = 0; i < 100; i++ ) fluid.Step( 1f / 30, Vector2.Zero );
		Check( fluid.SimulationSteps == 0 && fluid.Version == idleVersion, "idle solver does no work or texture uploads" );
		Check( fluid.Sample( new Vector2( float.NaN, 0 ) ) == Vector4.Zero, "invalid sampling cannot index arrays" );
		Check( !fluid.Impulse( new Vector2( float.NaN, 0 ), 32, 40 ), "reject invalid impulse" );
		Check( !fluid.Impulse( new Vector2( 10000, 0 ), 32, 40 ), "reject distant impulse" );
		Check( fluid.Impulse( Vector2.Zero, 32, 80 ), "accept surface impact" );
		Check( !fluid.IsSleeping, "impacts wake the solver" );
		fluid.Step( 0.02f, Vector2.Zero );
		Check( fluid.Sample( Vector2.Zero ).x > 0.1f, "impact displaces surface" );
		Check( MathF.Abs( fluid.Sample( new Vector2( 16, 0 ) ).x - fluid.Sample( new Vector2( -16, 0 ) ).x ) < 1e-5f, "radial symmetry" );
		var h = fluid.Sample( Vector2.Zero ).x;
		fluid.MoveTo( new Vector2( 16, 0 ), 16 );
		Check( MathF.Abs( fluid.Sample( Vector2.Zero ).x - h ) < 1e-5f, "clipmap recenter preserves world-space samples" );
		for ( var i = 0; i < 30; i++ ) fluid.Step( 0.02f, Vector2.Zero );
		Check( MathF.Abs( fluid.Sample( new Vector2( 80, 0 ) ).x ) > 0.01f, "waves propagate beyond injection radius" );
		var initialEnergy = fluid.Energy;
		for ( var i = 0; i < 1500; i++ ) fluid.Step( 0.02f, Vector2.Zero );
		Check( fluid.Energy < initialEnergy * 0.001f, "waves dissipate" );
		Check( fluid.IsSleeping, "settled waves put solver back to sleep" );
		for ( var y = 0; y < 64; y++ ) fluid.SetBlocked( 32, y, true );
		Check( fluid.Impulse( Vector2.Zero, 16, 40 ), "masked impulse accepted" ); fluid.Step( 0.02f, Vector2.Zero );
		Check( fluid.Sample( new Vector2( 16, 0 ) ).x == 0, "solid cells remain stationary after recenter" );
		var stress = new FluidSurfaceField { WaveSpeed = 600, Damping = 0.05f };
		stress.MoveTo( Vector2.Zero, 8 );
		for ( var i = 0; i < 400; i++ )
		{
			stress.Impulse( new Vector2( (i % 9 - 4) * 16, 0 ), 16, i % 2 == 0 ? 160 : -160 );
			stress.Step( 0.1f, new Vector2( 512, 0 ) );
		}
		foreach ( var p in stress.GetPixels() ) Check( float.IsFinite( p.x + p.y + p.z + p.w ) && MathF.Abs( p.x ) <= 24.001f && p.w >= 0 && p.w <= 1, "CFL stress remains finite and bounded" );
		var domain = new FftWaterDomain();
		domain.Build( FftWaterShape.Lake, new Vector2( 400, 200 ), 100, null, 32 );
		Check( domain.Sample( Vector3.Zero, out _, out _, out _ ), "lake center" );
		Check( !domain.Sample( new Vector3( 200, 100, 0 ), out _, out _, out _ ), "ellipse excludes bounding-box corners" );
		domain.Build( FftWaterShape.River, Vector2.One, 100, new List<Vector3> { new( -200, 0, 0 ), new( 0, 0, -20 ), new( 200, 100, -40 ) }, 32 );
		Check( domain.ControlRows.Count == 3, "one physical hull per authored segment" );
		var rowCenter = (domain.Rows[3].Left + domain.Rows[3].Right) * 0.5f;
		Check( domain.Sample( rowCenter, out var riverHeight, out var flow, out var normal ) && MathF.Abs( riverHeight - rowCenter.z ) < 0.001f && flow.x > 0.9f && flow.z < 0 && normal.z > 0.9f, "river height and flow follow rendered ribbon" );
		Check( !domain.Sample( new Vector3( -100, 200, 0 ), out _, out _, out _ ), "outside river is dry" );
		var sampler = new FftSurfaceSampler();
		var spectrum = FftOceanSpectrum.Build( 10, new Vector2( 1, 0.3f ).Normal, 1.2f, 17 );
		sampler.Build( spectrum, 128 ); sampler.Update( 12 );
		Check( sampler.RetainedEnergy > 0.75f && sampler.RetainedEnergy <= 1, "energetic physics modes cover most wave energy" );
		foreach ( var position in new[] { Vector2.Zero, new Vector2( 371, -901 ), new Vector2( 1000, 20 ) } )
		{
			sampler.Sample( position, 0, out var d, out var n, out var v );
			sampler.Sample( position + new Vector2( 0.5f, 0 ), 0, out var dx, out _, out _ );
			Check( MathF.Abs( (dx.z - d.z) / 0.5f + n.x / n.z ) < 0.01f, "CPU height normal agrees with finite differences" );
			sampler.Update( 12.001f ); sampler.Sample( position, 0, out var next, out _, out _ ); sampler.Update( 12 );
			Check( MathF.Abs( (next.z - d.z) / 0.001f - v.z ) < 0.5f, "CPU vertical wave velocity agrees with time derivative" );
		}
		var wind = FftOceanSpectrum.Build( 10, new Vector2( 1, 0 ), 1.2f * MathF.Sqrt( 0.7f ), 17, 0.55f );
		var swell = FftOceanSpectrum.Build( 16, new Vector2( 0, 1 ), 1.2f * MathF.Sqrt( 0.3f ), 17 ^ 0x3479, 0.3f );
		sampler.Build( wind, 512, swell );
		for ( var i = 0; i < 20; i++ )
		{
			var angle = i * 0.03f; var direction = new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) );
			sampler.Update( 12, new Vector2( 1, 0 ), direction, 0.55f );
			sampler.Sample( new Vector2( 30, 100 ), 0.85f, out var d, out var n, out var v );
			Check( float.IsFinite( d.z + n.x + v.z ), "mixed swell and changing wind remain finite" );
		}
		var large = new FluidSurfaceField(); large.MoveTo( Vector2.Zero, 8 );
		large.Impulse( new Vector2( 16, 32 ), 24, 120 ); large.Step( 1f / 30, Vector2.Zero );
		var packed = large.GetPixels();
		var index = 30 * 64 + 31; var world = large.Origin + new Vector2( 31, 30 ) * 8;
		Check( (packed[index] - large.Sample( world )).Length < 0.0001f, "packed render gradients agree with physics samples" );
		large.Impulse( large.Origin + new Vector2( 16, 240 ), 32, 120 ); large.Step( 1f / 30, Vector2.Zero );
		packed = large.GetPixels();
		foreach ( var x in new[] { 0, 1, 61, 62, 63 } )
		{
			var p = large.Origin + new Vector2( x, 30 ) * large.CellSize;
			Check( (packed[30 * 64 + x] - large.Sample( p )).Length < 0.0001f, "render gradients agree at absorbing boundaries" );
		}
		var waveSurface = new WaveSurface();
		Check( FluidSurfaceRay.TryHit( waveSurface, new Vector3( 0, 0, 50 ), new Vector3( 0, 0, -50 ), 0, 20, out var hitT )
			&& MathF.Abs( hitT - 0.42f ) < 0.002f, "bullet hits the displaced crest rather than the mean plane" );
		Check( FluidSurfaceRay.TryHit( waveSurface, new Vector3( -60, 0, 1 ), new Vector3( 60, 0, 1 ), 0, 20, out hitT ), "horizontal bullets can intersect a wave" );
		var hitPoint = new Vector3( -60 + 120 * hitT, 0, 1 );
		waveSurface.SampleSurface( hitPoint, out var hitHeight, out _ );
		Check( MathF.Abs( hitPoint.z - hitHeight ) < 0.5f, "grazing intersection lies on the surface" );
		Check( !FluidSurfaceRay.TryHit( waveSurface, new Vector3( 0, 0, 50 ), new Vector3( 100, 0, 50 ), 0, 20, out _ ), "ray above all waves misses" );
		Check( !FluidSurfaceRay.TryHit( waveSurface, Vector3.Zero, Vector3.Zero, 0, 20, out _ ), "zero-length rays are rejected" );
		Check( FluidContactMath.Strength( 0, 0, 80, 1, false, 0.18f ) == 0, "resting bodies emit no forcing" );
		var walking = FluidContactMath.Strength( 80, 0, 80, 0.3f, false, 0.18f );
		Check( FluidContactMath.Strength( 240, 0, 80, 0.3f, false, 0.18f ) > walking, "running displaces more than walking" );
		Check( FluidContactMath.Strength( 0, -400, 80, 0.3f, true, 0.18f ) > walking, "vertical entry produces an impact" );
		Check( FluidContactMath.Strength( 80, 0, 800, 0.3f, false, 0.18f ) > walking, "mass changes the response" );
		Check( FluidContactMath.Strength( 1e9f, -1e9f, 1e9f, 1, true, 1 ) <= 150, "extreme objects remain bounded" );
		Check( FluidContactMath.Strength( float.NaN, 0, 80, 1, true, 1 ) == 0, "invalid contact data rejected" );
		Check( FluidContactMath.Crossed( 100, 120, -120, -100, 0 ), "fast body crossing between ticks is detected" );
		Check( FluidContactMath.Crossed( -120, -100, 100, 120, 0 ), "fast surface exit is detected" );
		Check( !FluidContactMath.Crossed( 100, 120, 80, 100, 0 ), "movement above water produces no crossing" );
		Check( FluidContactMath.TrailSamples( 10000, 8 ) == 8 && FluidContactMath.TrailSamples( 0, 8 ) == 1, "trail interpolation is bounded" );
		large.Reset();
		Check( large.IsSleeping && large.Energy == 0 && large.Sample( Vector2.Zero ) == Vector4.Zero, "disable clears contact field" );
		large.MoveTo( Vector2.Zero, 8 );
		Check( large.Impulse( Vector2.Zero, 24, 80 ), "reenabled field accepts new events" );
		large.Step( 1f / 30, Vector2.Zero );
		Check( large.Sample( Vector2.Zero ).x > 0, "reenabled field propagates again" );
		var coarseAxis = Enumerable.Range( 0, 129 ).Select( i => (i - 64) * 16f ).ToArray();
		var cacheRandom = new Random( 20261025 );
		var cacheError = 0f; var cacheNormalError = 0f; var cacheVelocityError = 0f;
		foreach ( var time in new[] { 0f, 17f, 83f } )
		{
			sampler.Update( time );
			for ( var i = 0; i < 2000; i++ )
			{
				var position = new Vector2( cacheRandom.NextSingle() * 512 - 256, cacheRandom.NextSingle() * 512 - 256 );
				sampler.Sample( position, 0.85f, out var exact, out var exactNormal, out var exactVelocity );
				sampler.SampleForPhysics( position, 0.85f, 8, out var cached, out var cachedNormal, out var cachedVelocity );
				cacheError = MathF.Max( cacheError, MathF.Abs( exact.z - cached.z ) );
				cacheNormalError = MathF.Max( cacheNormalError, (exactNormal - cachedNormal).Length );
				cacheVelocityError = MathF.Max( cacheVelocityError, (exactVelocity - cachedVelocity).Length );
				Check( float.IsFinite( cached.z + cachedNormal.x + cachedVelocity.z ), "cached buoyancy samples stay finite" );
			}
			Check( sampler.PhysicsCacheHits > 0, "nearby bodies share Fourier work" );
			Check( sampler.PhysicsCacheSize <= 1024, "sample cache memory remains bounded for dispersed bodies" );
		}
		Console.WriteLine( $"Buoyancy cache max errors: height={cacheError:F4} units, normal={cacheNormalError:F4}, velocity={cacheVelocityError:F4} units/s." );
		Check( cacheError < .5f && cacheNormalError < 0.2f && cacheVelocityError < 15f, "default cache stays below half a unit and approximately eleven degrees of normal error" );
		sampler.Update( 101 );
		Check( sampler.PhysicsCacheHits == 0 && sampler.PhysicsCacheMisses == 0, "changing wave time invalidates the physics cache" );
		sampler.Sample( new Vector2( 7, 11 ), .85f, out var uncached, out var uncachedNormal, out var uncachedVelocity );
		sampler.SampleForPhysics( new Vector2( 7, 11 ), .85f, 0, out var bypass, out var bypassNormal, out var bypassVelocity );
		Check( uncached == bypass && uncachedNormal == bypassNormal && uncachedVelocity == bypassVelocity, "zero spacing preserves exact Fourier physics" );
		foreach ( var focus in new[] { -2000f, -1000, -128, 0, 128, 1000, 2000 } )
		{
			var refined = FftMeshTessellation.Refine( coarseAxis, focus, 384, 4 );
			Check( refined[0] == coarseAxis[0] && refined[^1] == coarseAxis[^1], "refinement preserves finite water boundaries" );
			Check( refined.Length <= coarseAxis.Length + 194, "local tessellation vertex budget bounded" );
			for ( var i = 1; i < refined.Length; i++ )
			{
				Check( refined[i] > refined[i - 1], "refined coordinates strictly monotonic: no inverted or zero-area triangles" );
				if ( refined[i - 1] >= focus - 384 && refined[i] <= focus + 384 )
					Check( refined[i] - refined[i - 1] <= 4.001f, "near-field waves have geometry at GPU cell spacing" );
			}
			foreach ( var p in coarseAxis.Where( p => MathF.Abs( p - focus ) > 384 ) )
				Check( refined.Contains( p ), "refinement preserves distant FFT mesh samples" );
		}
		Console.WriteLine( $"Fluid surface: {_checks} checks passed; retained physics energy = {sampler.RetainedEnergy:P2}." );
	}
}
