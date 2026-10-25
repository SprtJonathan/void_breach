using System;
using Sandbox;

/// <summary>One fall profile for players and NPCs. Armour cannot cushion a landing.</summary>
public static class VBFallDamage
{
	public const float SafeDistance = 192f;
	public const float SevereDistance = 1280f;
	private static readonly (float Distance, float Damage)[] Profile =
	{
		(SafeDistance, 0f), (256f, 10f), (384f, 20f), (512f, 50f),
		(768f, 100f), (1024f, 200f), (SevereDistance, 300f)
	};

	/// <summary>Absolute damage from vertical descent, independent of any actor's health.</summary>
	public static float FromDistance( float distance )
	{
		if ( !float.IsFinite( distance ) || distance <= SafeDistance ) return 0f;
		for ( int i = 1; i < Profile.Length; i++ )
		{
			var from = Profile[i - 1];
			var to = Profile[i];
			if ( distance <= to.Distance )
				return from.Damage + (to.Damage - from.Damage) * ((distance - from.Distance) / (to.Distance - from.Distance));
		}
		// No health-based instant kill or damage cap: extreme falls keep getting worse.
		return 300f + (distance - SevereDistance) * (100f / 256f);
	}
	public static VBDamageInfo Create( float distance, Vector3 position )
	{
		var info = VBDamageInfo.Create( FromDistance( distance ), VBDamageType.Fall );
		info.Position = position;
		info.ArmorPierce = 1f;
		return info;
	}
}
