using System;
using Sandbox;

/// <summary>
/// Configurable armor buffer. Armor absorbs the non-piercing part of incoming
/// damage before it reaches health.
/// </summary>
public sealed class VBArmorComponent : Component
{
	[Property, Range( 0f, 500f )]
	public float MaxArmor { get; set; } = 30f;

	[Property, Range( 0f, 5f )]
	public float ArmorCostPerDamage { get; set; } = 1f;

	[Sync( SyncFlags.FromHost )] public float CurrentArmor { get; private set; }

	public float ArmorFraction => MaxArmor > 0f
		? CurrentArmor / MaxArmor
		: 0f;

	public Action<float> OnArmorAbsorbed;
	public Action<float> OnArmorAdded;
	public Action OnArmorBroken;
	public Action OnArmorChanged;

	protected override void OnStart()
	{
		if ( !Networking.IsHost )
			return;

		CurrentArmor = 0f;
		OnArmorChanged?.Invoke();
	}

	public float AbsorbDamage( VBDamageInfo info )
	{
		if ( !Networking.IsHost || info.Amount <= 0f || CurrentArmor <= 0f )
			return MathF.Max( 0f, info.Amount );

		float pierce = Math.Clamp( info.ArmorPierce, 0f, 1f );
		float bypassDamage = info.Amount * pierce;
		float blockableDamage = info.Amount - bypassDamage;
		float armorCost = MathF.Max( 0.001f, ArmorCostPerDamage );
		float absorbedDamage = MathF.Min( blockableDamage, CurrentArmor / armorCost );

		if ( absorbedDamage <= 0f )
			return info.Amount;

		bool hadArmor = CurrentArmor > 0f;
		CurrentArmor = MathF.Max( 0f, CurrentArmor - absorbedDamage * armorCost );
		OnArmorAbsorbed?.Invoke( absorbedDamage );
		OnArmorChanged?.Invoke();

		if ( hadArmor && CurrentArmor <= 0f )
			OnArmorBroken?.Invoke();

		return bypassDamage + blockableDamage - absorbedDamage;
	}

	public float AddArmor( float amount )
	{
		if ( !Networking.IsHost || amount <= 0f )
			return 0f;

		float previousArmor = CurrentArmor;
		CurrentArmor = Math.Clamp( CurrentArmor + amount, 0f, MaxArmor );
		float addedArmor = CurrentArmor - previousArmor;

		if ( addedArmor > 0f )
		{
			OnArmorAdded?.Invoke( addedArmor );
			OnArmorChanged?.Invoke();
		}

		return addedArmor;
	}

	public void ClearArmor()
	{
		if ( !Networking.IsHost || CurrentArmor <= 0f )
			return;

		CurrentArmor = 0f;
		OnArmorBroken?.Invoke();
		OnArmorChanged?.Invoke();
	}

	[Button( "Debug: Add 30 Armor" )]
	public void Debug_AddArmor()
	{
		AddArmor( 30f );
	}

	[Button( "Debug: Clear Armor" )]
	public void Debug_ClearArmor()
	{
		ClearArmor();
	}
}
