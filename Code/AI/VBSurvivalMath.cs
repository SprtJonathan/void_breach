using System;
using Sandbox;

/// <summary>Prediction using observed state only; does not query a hidden object's live transform.</summary>
public static class VBSurvivalMath
{
	/// <summary>AI policy only: a deliberate drop must cost less than half the health remaining.</summary>
	public static bool CanRiskDrop( float distance, float currentHealth ) => float.IsFinite( distance )
		&& float.IsFinite( currentHealth ) && currentHealth > 0f
		&& VBFallDamage.FromDistance( distance ) < currentHealth * 0.5f;

	public static float VehicleClearance( Vector3 position, Vector3 velocity, float radius, Vector3 point, float lookAhead, float heightTolerance )
	{
		if ( MathF.Abs( position.z - point.z ) > heightTolerance ) return 10000f;
		var delta = (point - position).WithZ( 0f );
		velocity = velocity.WithZ( 0f );
		if ( velocity.LengthSquared < 1f ) return 10000f;
		float time = Vector3.Dot( delta, velocity ) / velocity.LengthSquared;
		if ( time < -0.1f || time > lookAhead ) return 10000f;
		return (delta - velocity * MathF.Max( 0f, time )).Length - radius;
	}
	public static float GrenadeClearance( Vector3 position, Vector3 velocity, Vector3 gravity, float flight, float radius, Vector3 point )
	{
		var predicted = position + velocity * flight + gravity * (0.5f * flight * flight);
		return MathF.Min( Vector3.DistanceBetween( point, position ), Vector3.DistanceBetween( point, predicted ) ) - radius;
	}
}
