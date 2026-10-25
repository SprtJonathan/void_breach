using System;
using Sandbox;

/// <summary>Shared player/NPC timed explosive using native rigidbody flight and scene occlusion.</summary>
[Title( "Frag Grenade Projectile" ), Category( "Void Breach/Weapons" ), Icon( "flare" )]
public sealed class VBNpcGrenade : Component
{
	[RequireComponent] private RadiusDamage Blast { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public GameObject Thrower { get; set; }
	[Sync( SyncFlags.FromHost )] public bool SoldierCoordinated { get; set; }
	[Property] public float FuseTime { get; set; } = 2.5f;
	[Property] public float BlastRadius { get; set; } = 240f;
	[Property] public float Damage { get; set; } = 85f;
	[Property] public float PhysicsForceScale { get; set; } = 1f;
	[Property] public SoundEvent ExplosionSound { get; set; }
	[Property] public GameObject ExplosionEffect { get; set; }
	[Sync( SyncFlags.FromHost )] private TimeSince Age { get; set; }
	private bool _exploded;
	public float RemainingFuse => MathF.Max( 0f, FuseTime - Age );
	public bool IsLive => Active && !_exploded;
	protected override void OnStart() { if ( Networking.IsHost ) Age = 0f; }
	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || _exploded || Age < MathF.Max( 0.1f, FuseTime ) ) return;
		_exploded = true;
		var origin = WorldPosition;
		VBAiSoundStimulus.Emit( origin, 2400f, Thrower, VBAiSoundKind.Explosion );
		PresentExplosion( origin );
		Blast.DamageOnEnabled = false;
		Blast.Attacker = Thrower;
		Blast.Radius = MathF.Max( 1f, BlastRadius );
		Blast.DamageAmount = MathF.Max( 0f, Damage );
		Blast.PhysicsForceScale = PhysicsForceScale;
		Blast.Occlusion = true;
		Blast.DamageTags.Add( "explosion" );
		// The projectile must not occlude its own radial damage rays.
		foreach ( var collider in GetComponents<Collider>() ) collider.Enabled = false;
		Blast.Apply();
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PresentExplosion( Vector3 position )
	{
		if ( ExplosionSound is not null ) Sound.Play( ExplosionSound, position );
		if ( !ExplosionEffect.IsValid() ) return;
		var effect = ExplosionEffect.Clone( new CloneConfig { StartEnabled = false, Transform = new Transform( position ) } );
		effect.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		// Engine explosion prefabs also contain damage. Keep this clone cosmetic on every peer.
		foreach ( var damage in effect.GetComponentsInChildren<RadiusDamage>( includeDisabled: true, includeSelf: true ) )
		{
			damage.DamageOnEnabled = false;
			damage.Enabled = false;
		}
		effect.Enabled = true;
	}
}
