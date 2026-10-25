using System;
using Sandbox;

public sealed partial class VBHealthComponent
{
	[Property, Group( "Hit Locations" )] public bool UseLocalizedBulletDamage { get; set; }
	[Property, Group( "Hit Locations" ), Range( 1f, 10f )] public float HeadDamageMultiplier { get; set; } = 4f;
	[Property, Group( "Hit Locations" ), Range( 0.1f, 2f )] public float TorsoDamageMultiplier { get; set; } = 1f;
	[Property, Group( "Hit Locations" ), Range( 0.1f, 2f )] public float LimbDamageMultiplier { get; set; } = 0.7f;

	private void ApplyHitLocation( ref VBDamageInfo info )
	{
		if ( !UseLocalizedBulletDamage || !info.Type.HasFlag( VBDamageType.Bullet )
			|| info.Type.HasFlag( VBDamageType.Explosion ) || info.Type.HasFlag( VBDamageType.Blunt ) ) return;
		var location = info.IsHeadshot ? VBHitLocation.Head : info.HitLocation;
		float scale = location switch
		{
			VBHitLocation.Head => HeadDamageMultiplier,
			VBHitLocation.Arm or VBHitLocation.Leg => LimbDamageMultiplier,
			_ => TorsoDamageMultiplier
		};
		info.Amount *= MathF.Max( 0f, scale );
		// The shared armor buffer represents body armor, not a ballistic helmet.
		if ( location == VBHitLocation.Head ) info.ArmorPierce = 1f;
	}
}
