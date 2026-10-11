using System;

namespace VoidBreach.Water;

/// <summary>Non-mutating numerical checks and live physics telemetry for the showcase.</summary>
public static class WaterDiagnostics
{
	[ConCmd( "vb_water_check" )]
	public static void Check()
	{
		var failures = 0;
		var checks = 0;
		void Expect( bool condition, string message )
		{
			checks++;
			if ( condition ) return;
			failures++;
			Log.Warning( $"Water check FAILED: {message}" );
		}
		var sample = new WaterSample( null, 0, -100, Vector3.Up, Vector3.Zero );
		Expect( sample.Submersion( 20, 10 ) == 0, "dry sample" );
		Expect( MathF.Abs( sample.Submersion( 0, 10 ) - 0.5f ) < 0.0001f, "half-submerged sample" );
		Expect( sample.Submersion( -40, 10 ) == 1, "fully submerged sample" );
		Expect( sample.Submersion( -120, 10 ) == 0, "below finite bottom" );
		Expect( MathF.Abs( sample.Submersion( -100, 10 ) - 0.5f ) < 0.0001f, "straddling finite bottom" );
		var direction = new Vector2( 1, 0.3f ).Normal;
		foreach ( var motion in new[] { WaterMotion.PondWater, WaterMotion.RiverWater, WaterMotion.OceanWater } )
		for ( var i = 0; i < 100; i++ )
		{
			var p = new Vector2( i * 27.31f, i * -13.13f );
			var value = WaterWaves.Evaluate( p, direction, 38, 1800, 0.8f, 12.5f, motion );
			var dx = (WaterWaves.Evaluate( p + new Vector2( 0.1f, 0 ), direction, 38, 1800, 0.8f, 12.5f, motion ).x
				- WaterWaves.Evaluate( p - new Vector2( 0.1f, 0 ), direction, 38, 1800, 0.8f, 12.5f, motion ).x) / 0.2f;
			Expect( MathF.Abs( value.x ) <= 38.001f, "wave exceeds culling envelope" );
			Expect( MathF.Abs( dx - value.y ) < 0.003f, "analytic derivative differs from finite difference" );
			var dy = (WaterWaves.Evaluate( p + new Vector2( 0, 0.1f ), direction, 38, 1800, 0.8f, 12.5f, motion ).x
				- WaterWaves.Evaluate( p - new Vector2( 0, 0.1f ), direction, 38, 1800, 0.8f, 12.5f, motion ).x) / 0.2f;
			Expect( MathF.Abs( dy - value.z ) < 0.003f, "cross-wave derivative differs from finite difference" );
			if ( motion == WaterMotion.OceanWater )
			{
				// Recover the height at a horizontally displaced Gerstner vertex, including steep settings.
				var offset = WaterWaves.Displacement( p, direction, 200, 600, 0.8f, 12.5f );
				var recovered = WaterWaves.Evaluate( p + new Vector2( offset.x, offset.y ), direction, 200, 600, 0.8f, 12.5f );
				Expect( MathF.Abs( recovered.x - offset.z ) < 0.03f, "Gerstner inverse does not recover rendered height" );
			}
		}
		Expect( VBWaterBody.TriangleHeight( new Vector3( 2, 2, 0 ), Vector3.Zero,
			new Vector3( 10, 0, 10 ), new Vector3( 0, 10, 20 ), out var height, out _ ) && MathF.Abs( height - 6 ) < 0.001f, "sloping triangle interpolation" );
		Expect( !VBWaterBody.TriangleHeight( new Vector3( 9, 9, 0 ), Vector3.Zero,
			new Vector3( 10, 0, 10 ), new Vector3( 0, 10, 20 ), out _, out _ ), "river footprint rejects outside triangle" );
		Log.Info( $"Water checks: {checks - failures}/{checks} passed." );
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		foreach ( var effect in scene.GetAll<VBWaterUnderwater>() )
			Log.Info( $"Water camera: render calls={effect.RenderCount}, position={effect.LastCameraPosition}, underwater={effect.SubmergedAmount:F2}." );
		foreach ( var buoy in scene.GetAll<VBWaterBuoyancy>() )
		{
			var body = buoy.Body;
			if ( !body.IsValid() ) continue;
			Log.Info( $"Water body '{buoy.GameObject.Name}': position={buoy.WorldPosition}, mass={body.Mass:F2}kg, submerged={buoy.SubmergedFraction:P1}, velocity={body.Velocity}, proxy={body.IsProxy}." );
		}
	}
}
