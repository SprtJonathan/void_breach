using System;
using System.Collections.Generic;
using Sandbox;

/// <summary>
/// Identifies one temporary modifier applied to the downed bleed-out rate.
/// Multiple modifiers stack multiplicatively.
/// </summary>
internal sealed class VBDrainModifier
{
	public object Key { get; }
	public float Multiplier { get; }
	public float Duration { get; }
	public TimeSince Elapsed { get; set; }

	public VBDrainModifier( object key, float multiplier, float duration )
	{
		Key = key;
		Multiplier = multiplier;
		Duration = duration;
		Elapsed = 0f;
	}
}

/// <summary>
/// Manages Void Breach's three-tier health model, downed state, bleed-out,
/// revival and temporary SoftMax health effects.
/// </summary>
public sealed class VBHealthComponent : Component, Component.IDamageable
{
	[Property, Group( "Life Cycle" )]
	public bool CanBeDowned { get; set; } = true;

	[Property, Range( 1f, 500f )]
	public float StartingHardMaxHealth { get; set; } = 100f;

	[Property, Range( 0.1f, 50f )]
	public float BaseDrainRate { get; set; } = 5f;

	[Property, Range( 0.01f, 1f )]
	public float WakePercent { get; set; } = 0.25f;

	[Property, Range( 1f, 50f )]
	public float MinWakeHp { get; set; } = 10f;

	[Property, Range( 0.1f, 20f )]
	public float SoftMaxRecoveryRate { get; set; } = 2f;

	[Property, Range( 0f, 60f )]
	public float DefaultDrainModifierDuration { get; set; } = 5f;

	[Property, Group( "Downed State" )]
	public SoundEvent DownedSound { get; set; }

	[Property, Group( "Downed State" ), Title( "HardHealth Damage Types" )]
	public VBDamageType DownedHardHealthDamageTypes { get; set; }
		= VBDamageType.Fire
		| VBDamageType.Poison
		| VBDamageType.Energy
		| VBDamageType.Freeze;

	[Sync( SyncFlags.FromHost )] public float CurrentHealth { get; private set; }
	[Sync( SyncFlags.FromHost )] public float SoftMaxHealth { get; private set; }
	[Sync( SyncFlags.FromHost )] public float HardMaxHealth { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsDowned { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsDead { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsDrainPaused { get; private set; }
	[Sync( SyncFlags.FromHost )] public VBDamageType DownedDamageType { get; private set; }
	[Sync( SyncFlags.FromHost )] public string DownedReasonToken { get; private set; }
	[Sync( SyncFlags.FromHost )] public string DownedInstigatorName { get; private set; }

	public float HealthFraction => SoftMaxHealth > 0f
		? CurrentHealth / SoftMaxHealth
		: 0f;

	public float EffectiveDrainRate
	{
		get
		{
			float rate = BaseDrainRate;

			foreach ( var modifier in _drainModifiers )
				rate *= modifier.Multiplier;

			return MathF.Max( 0f, rate );
		}
	}

	public Action<VBDamageInfo> OnDamaged;
	public Action<float> OnHealed;
	public Action OnDowned;
	public Action OnRevived;
	public Action OnDeath;
	public Action OnHealthChanged;

	private readonly List<VBDrainModifier> _drainModifiers = new();
	private VBArmorComponent _armor;
	private float _softMaxReductionRate;
	private float _activeSoftMaxRecoveryRate;
	private bool _softMaxEffectActive;

	protected override void OnAwake()
	{
		_armor = Components.Get<VBArmorComponent>();
	}

	protected override void OnStart()
	{
		if ( !Networking.IsHost )
			return;

		ResetHealth();
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || IsDead )
			return;

		UpdateDrainModifiers();

		if ( IsDowned )
			UpdateBleedOut();
		else
			UpdateSoftMaxHealth();
	}

	public void ResetHealth()
	{
		if ( !Networking.IsHost )
			return;

		HardMaxHealth = MathF.Max( 1f, StartingHardMaxHealth );
		SoftMaxHealth = HardMaxHealth;
		CurrentHealth = SoftMaxHealth;
		IsDowned = false;
		IsDead = false;
		IsDrainPaused = false;
		DownedDamageType = VBDamageType.None;
		DownedReasonToken = string.Empty;
		DownedInstigatorName = string.Empty;
		_softMaxEffectActive = false;
		_softMaxReductionRate = 0f;
		_activeSoftMaxRecoveryRate = SoftMaxRecoveryRate;
		_drainModifiers.Clear();
		NotifyHealthChanged();
	}

	public void TakeDamage( VBDamageInfo info )
	{
		EnsureInitialized();

		if ( !Networking.IsHost || IsDead || info.Amount <= 0f )
			return;

		if ( IsDowned && !CanDamageHardHealthWhileDowned( info.Type ) )
			return;

		if ( _armor is not null )
			info.Amount = _armor.AbsorbDamage( info );

		if ( info.Amount <= 0f )
		{
			OnDamaged?.Invoke( info );
			return;
		}

		if ( IsDowned )
		{
			HardMaxHealth -= info.Amount;
			ClampHealth();

			if ( info.DrainRateModifier > 0f && info.DrainRateModifier != 1f )
			{
				float duration = info.DrainModifierDuration > 0f
					? info.DrainModifierDuration
					: DefaultDrainModifierDuration;
				object modifierKey = info.Source is not null
					? info.Source
					: info.Type;

				AddDrainModifier( modifierKey, info.DrainRateModifier, duration );
			}
		}
		else
		{
			CurrentHealth -= info.Amount;

			if ( info.SoftMaxReductionRate > 0f )
				ApplySoftMaxEffect( info.SoftMaxReductionRate, info.SoftMaxRecoveryRate );

			if ( CurrentHealth <= 0f )
			{
				if ( CanBeDowned )
					EnterDownedState( info );
				else
					Die();
			}
		}

		OnDamaged?.Invoke( info );
		NotifyHealthChanged();
	}

	public void OnDamage( in DamageInfo damage )
	{
		TakeDamage( VBDamageInfo.FromNative( damage ) );
	}

	public void Heal( float amount )
	{
		EnsureInitialized();

		if ( !Networking.IsHost || IsDead || IsDowned || amount <= 0f )
			return;

		float previousHealth = CurrentHealth;
		CurrentHealth = MathF.Min( CurrentHealth + amount, SoftMaxHealth );
		float healedAmount = CurrentHealth - previousHealth;

		if ( healedAmount <= 0f )
			return;

		OnHealed?.Invoke( healedAmount );
		NotifyHealthChanged();
	}

	public void RestoreHardMax( float amount )
	{
		EnsureInitialized();

		if ( !Networking.IsHost || IsDead || amount <= 0f )
			return;

		HardMaxHealth = MathF.Min(
			HardMaxHealth + amount,
			MathF.Max( 1f, StartingHardMaxHealth )
		);

		if ( !_softMaxEffectActive )
			SoftMaxHealth = HardMaxHealth;

		ClampHealth();
		NotifyHealthChanged();
	}

	public void AddDrainModifier( object key, float multiplier, float duration )
	{
		if ( !Networking.IsHost || key is null || multiplier < 0f )
			return;

		RemoveDrainModifier( key );
		_drainModifiers.Add( new VBDrainModifier( key, multiplier, duration ) );
	}

	public void RemoveDrainModifier( object key )
	{
		if ( !Networking.IsHost || key is null )
			return;

		_drainModifiers.RemoveAll( modifier => Equals( modifier.Key, key ) );
	}

	public void ApplySoftMaxEffect( float reductionRate, float recoveryRate = 0f )
	{
		if ( !Networking.IsHost || IsDead || reductionRate <= 0f )
			return;

		_softMaxEffectActive = true;
		_softMaxReductionRate = MathF.Max( _softMaxReductionRate, reductionRate );
		_activeSoftMaxRecoveryRate = recoveryRate > 0f
			? recoveryRate
			: SoftMaxRecoveryRate;
	}

	public void RemoveSoftMaxEffect()
	{
		if ( !Networking.IsHost )
			return;

		_softMaxEffectActive = false;
		_softMaxReductionRate = 0f;
	}

	public void PauseDrain()
	{
		if ( !Networking.IsHost || !IsDowned || IsDead )
			return;

		IsDrainPaused = true;
	}

	public void ResumeDrain()
	{
		if ( !Networking.IsHost )
			return;

		IsDrainPaused = false;
	}

	public void Revive()
	{
		if ( !Networking.IsHost || !IsDowned || IsDead || HardMaxHealth <= 0f )
			return;

		IsDowned = false;
		IsDrainPaused = false;
		SoftMaxHealth = MathF.Min( SoftMaxHealth, HardMaxHealth );
		CurrentHealth = MathF.Min(
			SoftMaxHealth,
			MathF.Max( HardMaxHealth * WakePercent, MinWakeHp )
		);

		ClampHealth();
		OnRevived?.Invoke();
		NotifyHealthChanged();
	}

	private void EnterDownedState( VBDamageInfo info )
	{
		IsDowned = true;
		IsDrainPaused = false;
		CurrentHealth = 0f;
		DownedDamageType = info.Type;
		DownedReasonToken = string.IsNullOrWhiteSpace( info.DownedReasonToken )
			? GetDefaultDownedReasonToken( info.Type )
			: info.DownedReasonToken;
		DownedInstigatorName = GetInstigatorName( info.Attacker );
		PlayDownedSound();
		OnDowned?.Invoke();
	}

	private string GetInstigatorName( GameObject attacker )
	{
		if ( !attacker.IsValid() || attacker == GameObject )
			return string.Empty;

		return attacker.Network.Owner?.DisplayName ?? string.Empty;
	}

	private static string GetDefaultDownedReasonToken( VBDamageType damageType )
	{
		if ( damageType.HasFlag( VBDamageType.Explosion ) )
			return "vb.hud.death_explosion";
		if ( damageType.HasFlag( VBDamageType.Fall ) )
			return "vb.hud.death_fall";
		if ( damageType.HasFlag( VBDamageType.Fire ) )
			return "vb.hud.death_fire";
		if ( damageType.HasFlag( VBDamageType.Poison ) )
			return "vb.hud.death_poison";
		if ( damageType.HasFlag( VBDamageType.Energy ) )
			return "vb.hud.death_energy";
		if ( damageType.HasFlag( VBDamageType.Freeze ) )
			return "vb.hud.death_freeze";
		if ( damageType.HasFlag( VBDamageType.Blunt ) )
			return "vb.hud.death_blunt";
		if ( damageType.HasFlag( VBDamageType.Bullet ) )
			return "vb.hud.death_bullet";

		return "vb.hud.death_unknown";
	}

	private bool CanDamageHardHealthWhileDowned( VBDamageType damageType )
	{
		return (DownedHardHealthDamageTypes & damageType) != VBDamageType.None;
	}

	[Rpc.Broadcast]
	public void PlayDownedSound()
	{
		if ( DownedSound is null )
			return;

		GameObject.PlaySound( DownedSound );
	}

	private void UpdateBleedOut()
	{
		if ( IsDrainPaused )
			return;

		HardMaxHealth -= EffectiveDrainRate * Time.Delta;
		ClampHealth();

		if ( HardMaxHealth <= 0f )
			Die();
		else
			NotifyHealthChanged();
	}

	private void UpdateSoftMaxHealth()
	{
		float previousSoftMax = SoftMaxHealth;

		if ( _softMaxEffectActive )
		{
			SoftMaxHealth -= _softMaxReductionRate * Time.Delta;
		}
		else if ( SoftMaxHealth < HardMaxHealth )
		{
			SoftMaxHealth += _activeSoftMaxRecoveryRate * Time.Delta;
		}

		ClampHealth();

		if ( previousSoftMax != SoftMaxHealth )
			NotifyHealthChanged();
	}

	private void UpdateDrainModifiers()
	{
		for ( int index = _drainModifiers.Count - 1; index >= 0; index-- )
		{
			var modifier = _drainModifiers[index];

			if ( modifier.Duration >= 0f && modifier.Elapsed >= modifier.Duration )
				_drainModifiers.RemoveAt( index );
		}
	}

	private void ClampHealth()
	{
		HardMaxHealth = Math.Clamp( HardMaxHealth, 0f, MathF.Max( 1f, StartingHardMaxHealth ) );
		SoftMaxHealth = Math.Clamp( SoftMaxHealth, 0f, HardMaxHealth );
		CurrentHealth = Math.Clamp( CurrentHealth, 0f, SoftMaxHealth );
	}

	private void Die()
	{
		if ( IsDead )
			return;

		HardMaxHealth = 0f;
		SoftMaxHealth = 0f;
		CurrentHealth = 0f;
		IsDowned = false;
		IsDead = true;
		IsDrainPaused = false;
		_drainModifiers.Clear();
		OnDeath?.Invoke();
		NotifyHealthChanged();
	}

	private void NotifyHealthChanged()
	{
		OnHealthChanged?.Invoke();
	}

	private void EnsureInitialized()
	{
		if ( !Networking.IsHost || IsDead || HardMaxHealth > 0f )
			return;

		ResetHealth();
	}

	private void LogDebugState( string action )
	{
		if ( !Networking.IsHost )
		{
			Log.Warning( $"[VBHealthComponent] '{action}' ignoré sur '{GameObject.Name}' : la santé est contrôlée par le host." );
			return;
		}

		Log.Info(
			$"[VBHealthComponent] {action} — " +
			$"HP={CurrentHealth:0.##}, Soft={SoftMaxHealth:0.##}, Hard={HardMaxHealth:0.##}, " +
			$"Downed={IsDowned}, Dead={IsDead}"
		);
	}

	[Button( "Debug: Take 10 Damage" )]
	public void Debug_TakeDamage10()
	{
		TakeDamage( VBDamageInfo.FromBullet(
			10f,
			null,
			null,
			Vector3.Zero,
			Vector3.Zero,
			false
		) );
		LogDebugState( "Dégâts 10" );
	}

	[Button( "Debug: Take 50 Damage" )]
	public void Debug_TakeDamage50()
	{
		TakeDamage( VBDamageInfo.FromBullet(
			50f,
			null,
			null,
			Vector3.Zero,
			Vector3.Zero,
			false
		) );
		LogDebugState( "Dégâts 50" );
	}

	[Button( "Debug: Heal 25" )]
	public void Debug_Heal25()
	{
		Heal( 25f );
		LogDebugState( "Soin 25" );
	}

	[Button( "Debug: Restore HardMax 25" )]
	public void Debug_RestoreHardMax25()
	{
		RestoreHardMax( 25f );
		LogDebugState( "Restauration HardMax 25" );
	}

	[Button( "Debug: Kill / Down" )]
	public void Debug_Kill()
	{
		TakeDamage( VBDamageInfo.FromBullet(
			CurrentHealth + 1f,
			null,
			null,
			Vector3.Zero,
			Vector3.Zero,
			false
		) );
		LogDebugState( "Mise à terre" );
	}

	[Button( "Debug: Revive" )]
	public void Debug_Revive()
	{
		Revive();
		LogDebugState( "Réanimation" );
	}
}
