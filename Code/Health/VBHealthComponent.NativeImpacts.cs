using System;
using System.Collections.Generic;
using Sandbox;

public sealed partial class VBHealthComponent
{
	private readonly Dictionary<GameObject, float> _nativePlayerImpacts = new();
	private float _nativeImpactTime = float.NegativeInfinity;
	private PlayerController _impactController;

	private void ResetNativeImpacts()
	{
		_nativePlayerImpacts.Clear();
		_nativeImpactTime = float.NegativeInfinity;
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
			// Compound body shapes can report one landing more than once.
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
