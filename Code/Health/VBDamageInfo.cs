using System;
using Sandbox;

[Flags]
public enum VBDamageType
{
	None = 0,
	Bullet = 1 << 0,
	Blunt = 1 << 1,
	Explosion = 1 << 2,
	Fall = 1 << 3,
	Fire = 1 << 4,
	Poison = 1 << 5,
	Energy = 1 << 6,
	Freeze = 1 << 7
}

/// <summary>
/// Immutable-by-convention description of one Void Breach damage event.
/// </summary>
public struct VBDamageInfo
{
	public float Amount { get; set; }
	public VBDamageType Type { get; set; }
	public float ArmorPierce { get; set; }
	public float SoftMaxReductionRate { get; set; }
	public float SoftMaxRecoveryRate { get; set; }
	public float DrainRateModifier { get; set; }
	public float DrainModifierDuration { get; set; }
	public GameObject Attacker { get; set; }
	public GameObject Source { get; set; }
	public Vector3 Position { get; set; }
	public Vector3 Force { get; set; }
	public bool IsHeadshot { get; set; }

	public static VBDamageInfo Create(
		float amount,
		VBDamageType type,
		GameObject attacker = null,
		GameObject source = null )
	{
		return new VBDamageInfo
		{
			Amount = MathF.Max( 0f, amount ),
			Type = type,
			ArmorPierce = 0f,
			DrainRateModifier = 1f,
			Attacker = attacker,
			Source = source
		};
	}

	public static VBDamageInfo FromBullet(
		float amount,
		GameObject attacker,
		GameObject source,
		Vector3 position,
		Vector3 force,
		bool isHeadshot,
		float armorPierce = 0f )
	{
		var info = Create( amount, VBDamageType.Bullet, attacker, source );
		info.Position = position;
		info.Force = force;
		info.IsHeadshot = isHeadshot;
		info.ArmorPierce = Math.Clamp( armorPierce, 0f, 1f );
		return info;
	}

	public static VBDamageInfo FromExplosion(
		float amount,
		GameObject attacker,
		GameObject source,
		Vector3 position,
		Vector3 force,
		float armorPierce = 0f )
	{
		var info = Create( amount, VBDamageType.Explosion, attacker, source );
		info.Position = position;
		info.Force = force;
		info.ArmorPierce = Math.Clamp( armorPierce, 0f, 1f );
		return info;
	}

	public static VBDamageInfo FromFire(
		float amount,
		GameObject attacker,
		GameObject source,
		float softMaxReductionRate,
		float softMaxRecoveryRate,
		float downedDrainMultiplier = 1.5f,
		float modifierDuration = 5f )
	{
		var info = Create( amount, VBDamageType.Fire, attacker, source );
		info.SoftMaxReductionRate = MathF.Max( 0f, softMaxReductionRate );
		info.SoftMaxRecoveryRate = MathF.Max( 0f, softMaxRecoveryRate );
		info.DrainRateModifier = MathF.Max( 0f, downedDrainMultiplier );
		info.DrainModifierDuration = modifierDuration;
		return info;
	}

	public static VBDamageInfo FromPoison(
		float amount,
		GameObject attacker,
		GameObject source,
		float softMaxReductionRate,
		float softMaxRecoveryRate )
	{
		var info = Create( amount, VBDamageType.Poison, attacker, source );
		info.ArmorPierce = 1f;
		info.SoftMaxReductionRate = MathF.Max( 0f, softMaxReductionRate );
		info.SoftMaxRecoveryRate = MathF.Max( 0f, softMaxRecoveryRate );
		return info;
	}

	public static VBDamageInfo FromNative( in DamageInfo damage )
	{
		var info = Create(
			damage.Damage,
			GetNativeDamageType( damage ),
			damage.Attacker,
			damage.Weapon
		);

		info.Position = damage.Position;
		return info;
	}

	private static VBDamageType GetNativeDamageType( in DamageInfo damage )
	{
		var tags = damage.Tags;
		var type = VBDamageType.None;

		if ( tags is not null )
		{
			if ( tags.HasAny( "bullet" ) )
				type |= VBDamageType.Bullet;
			if ( tags.HasAny( "blunt", "melee" ) )
				type |= VBDamageType.Blunt;
			if ( tags.HasAny( "explosion", "blast" ) )
				type |= VBDamageType.Explosion;
			if ( tags.HasAny( "fall" ) )
				type |= VBDamageType.Fall;
			if ( tags.HasAny( "fire", "burn" ) )
				type |= VBDamageType.Fire;
			if ( tags.HasAny( "poison", "toxic" ) )
				type |= VBDamageType.Poison;
			if ( tags.HasAny( "energy", "electric", "shock" ) )
				type |= VBDamageType.Energy;
			if ( tags.HasAny( "freeze", "cold" ) )
				type |= VBDamageType.Freeze;
		}

		return type == VBDamageType.None
			? VBDamageType.Bullet
			: type;
	}
}
