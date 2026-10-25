using System;
using Sandbox;

// Standalone managed checks against the production assembly. No editor or scene required.
internal static class SurvivalMathChecks
{
	private static int _checks;
	private static void Check( bool condition, string message )
	{
		_checks++;
		if ( !condition ) throw new Exception( message );
	}
	public static void Main()
	{
		foreach ( var sample in new (float Height, float Damage)[] {
			(-100f, 0f), (0f, 0f), (180f, 0f), (192f, 0f), (224f, 5f),
			(256f, 10f), (320f, 15f), (384f, 20f), (448f, 35f), (512f, 50f),
			(640f, 75f), (768f, 100f), (1024f, 200f), (1280f, 300f), (1536f, 400f) })
			Check( MathF.Abs( VBFallDamage.FromDistance( sample.Height ) - sample.Damage ) < 0.001f,
				$"fall from {sample.Height} units costs {sample.Damage} absolute damage" );
		Check( VBFallDamage.FromDistance( float.NaN ) == 0f && VBFallDamage.FromDistance( float.PositiveInfinity ) == 0f
			&& VBFallDamage.FromDistance( float.NegativeInfinity ) == 0f, "invalid observations cannot damage health" );
		Check( float.IsFinite( VBFallDamage.FromDistance( float.MaxValue ) ), "extreme finite heights cannot overflow damage" );
		bool monotonic = true;
		for ( int height = 1; height <= 4096; height++ )
			monotonic &= VBFallDamage.FromDistance( height ) >= VBFallDamage.FromDistance( height - 1 );
		Check( monotonic, "higher falls never become safer" );
		bool continuous = true;
		foreach ( float height in new[] { 192f, 256f, 384f, 512f, 768f, 1024f, 1280f } )
			continuous &= VBFallDamage.FromDistance( height + 0.01f ) - VBFallDamage.FromDistance( height - 0.01f ) < 0.01f;
		Check( continuous, "no damage jumps at profile boundaries" );
		var impact = VBFallDamage.Create( 512f, Vector3.Up * 12f );
		Check( impact.Amount == 50f && impact.Position == Vector3.Up * 12f
			&& impact.Type == VBDamageType.Fall && impact.ArmorPierce == 1f, "fall packets retain absolute damage, location and armour bypass" );
		float Car( Vector3 point, Vector3 velocity ) => VBSurvivalMath.VehicleClearance( Vector3.Zero, velocity, 100f, point, 1.5f, 120f );
		Check( Car( new Vector3( 400f, 0f, 0f ), Vector3.Forward * 500f ) < 0f, "incoming car collision course" );
		Check( Car( new Vector3( 400f, 180f, 0f ), Vector3.Forward * 500f ) > 0f, "sideways dodge leaves the corridor" );
		Check( Car( new Vector3( 400f, 0f, 0f ), Vector3.Backward * 500f ) > 0f, "departing car is safe" );
		Check( Car( new Vector3( 900f, 0f, 0f ), Vector3.Forward * 500f ) > 0f, "distant car is outside reaction horizon" );
		Check( Car( new Vector3( 400f, 0f, 200f ), Vector3.Forward * 500f ) > 0f, "car below an overpass is safe" );
		Check( Car( Vector3.Forward * 10f, Vector3.Zero ) > 0f, "parked cars are not run-over threats" );
		Check( VBSurvivalMath.GrenadeClearance( Vector3.Forward * 600f, Vector3.Backward * 1000f, Vector3.Zero,
			0.35f, 304f, Vector3.Zero ) < 0f, "observed flying grenade can threaten before landing" );
		Check( VBSurvivalMath.GrenadeClearance( Vector3.Zero, Vector3.Zero, Vector3.Zero, 0f, 304f,
			Vector3.Forward * 400f ) > 0f, "escaping the blast radius improves safety" );
		Check( MathF.Abs( VBNpcNavigation.WalkingCost( 39.37f ) - 1f ) < 0.0001f, "one metre of walking costs one point" );
		Check( MathF.Abs( VBNpcNavigation.ClimbingCost( 39.37f ) - 2f ) < 0.0001f, "one metre of climbing costs two points" );
		Check( VBNpcNavigation.WalkingCost( 6f * 39.37f ) < VBNpcNavigation.ClimbingCost( 4f * 39.37f ), "short detour wins over harder climb" );
		Check( VBNpcNavigation.WalkingCost( 12f * 39.37f ) > VBNpcNavigation.ClimbingCost( 4f * 39.37f ), "climb wins over a long detour" );
		Check( VBSurvivalMath.CanRiskDrop( 180f, 1f ), "a harmless wall drop stays harmless even to an injured soldier" );
		Check( !VBSurvivalMath.CanRiskDrop( 768f, 80f ), "a soldier refuses a genuinely lethal edge" );
		Check( VBSurvivalMath.CanRiskDrop( 384f, 80f ), "a healthy soldier can choose a survivable escape drop" );
		Check( !VBSurvivalMath.CanRiskDrop( 384f, 30f ), "an injured soldier refuses the same fixed-damage drop" );
		Check( !VBSurvivalMath.CanRiskDrop( 256f, 20f ), "half-health risk boundary is excluded" );
		Check( !VBSurvivalMath.CanRiskDrop( float.NaN, 80f ) && !VBSurvivalMath.CanRiskDrop( 180f, 0f )
			&& !VBSurvivalMath.CanRiskDrop( 180f, float.NaN ), "invalid observations and dead actors cannot authorize a drop" );
		Console.WriteLine( $"PASS: {_checks} production fall / survival prediction checks, without UI automation." );
	}
}
