using System;
using System.Collections.Generic;
using Sandbox;

public sealed partial class VBHealthComponent
{
	private readonly Dictionary<GameObject, float> _nativePlayerImpacts = new();
	private float _nativeImpactTime = float.NegativeInfinity;
	private PlayerController _impactController;
	private float _lastFallReport = float.NegativeInfinity;

	void PlayerController.IEvents.OnLanded( float distance, Vector3 velocity )
	{
		if ( IsProxy || !float.IsFinite( distance ) ) return;
		float amount = VBFallDamage.FromDistance( distance );
		if ( amount <= 0f ) return;
		if ( Networking.IsHost ) ApplyLanding( amount );
		else ReportLanding( distance );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void ReportLanding( float distance )
	{
		if ( !Networking.IsHost || !GetComponent<PlayerController>().IsValid()
			|| !float.IsFinite( distance ) || VBFallDamage.FromDistance( distance ) <= 0f
			|| Time.Now - _lastFallReport < 0.15f ) return;
		ApplyLanding( VBFallDamage.FromDistance( distance ) );
	}

	private void ApplyLanding( float amount )
	{
		_lastFallReport = Time.Now;
		var info = VBDamageInfo.Create( amount, VBDamageType.Fall );
		info.Position = WorldPosition;
		info.ArmorPierce = 1f;
		TakeDamage( info );
	}

	private void ResetNativeImpacts()
	{
		_nativePlayerImpacts.Clear();
		_nativeImpactTime = float.NegativeInfinity;
		_lastFallReport = float.NegativeInfinity;
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( !float.IsFinite( damage.Damage ) || damage.Damage <= 0f ) return;
		bool impact = damage.Tags is not null && damage.Tags.HasAny( "impact" );
		if ( impact && !_impactController.IsValid() ) _impactController = Components.Get<PlayerController>();
		bool playerImpact = impact && _impactController.IsValid();
		if ( playerImpact && IsProxy ) return;
		if ( !Networking.IsHost && (!playerImpact || IsProxy) ) return;

		var info = VBDamageInfo.FromNative( damage );
		if ( playerImpact )
		{
			var otherBody = damage.Attacker.IsValid() ? damage.Attacker.GetComponentInParent<Rigidbody>( includeSelf: true ) : null;
			bool worldLanding = (!otherBody.IsValid() || !otherBody.MotionEnabled)
				&& damage.Position.z <= WorldPosition.z + 18f;
			// Ground contacts are priced by OnLanded's height profile, regardless of
			// whether native impact damage arrives before or after that event.
			if ( worldLanding ) return;
			// Compound body shapes can report one impact more than once.
			// Keep the maximum per source and simulation time, charging only a stronger contact's increment.
			if ( _nativeImpactTime != Time.Now )
			{
				_nativePlayerImpacts.Clear();
				_nativeImpactTime = Time.Now;
			}
			var source = damage.Attacker ?? damage.Weapon ?? GameObject;
			_nativePlayerImpacts.TryGetValue( source, out float previous );
			_nativePlayerImpacts[source] = MathF.Max( previous, info.Amount );
			info.Amount = MathF.Max( info.Amount - previous, 0f );
			if ( info.Amount <= 0f ) return;
		}

		if ( Networking.IsHost ) TakeDamage( info );
		else ReportPlayerImpact( info.Amount, info.Attacker, info.Source, info.Position );
	}

	// Native Rigidbody impacts run on the physics owner; health remains controlled by the host.
	// Report only collision events, never each movement tick. Owner-side coalescing precedes this RPC.
	[Rpc.Host( NetFlags.OwnerOnly )]
	private void ReportPlayerImpact( float amount, GameObject attacker, GameObject source, Vector3 position )
	{
		if ( !Networking.IsHost || !float.IsFinite( amount ) || amount <= 0f
			|| !float.IsFinite( position.x ) || !float.IsFinite( position.y ) || !float.IsFinite( position.z ) ) return;
		if ( !_impactController.IsValid() ) _impactController = Components.Get<PlayerController>();
		if ( !_impactController.IsValid() || !_impactController.Body.IsValid() || !_impactController.Body.EnableImpactDamage ) return;
		if ( (attacker.IsValid() && attacker.Scene != Scene) || (source.IsValid() && source.Scene != Scene) ) return;
		var info = VBDamageInfo.Create( amount, VBDamageType.Blunt, attacker, source );
		info.Position = position;
		TakeDamage( info );
	}
}
