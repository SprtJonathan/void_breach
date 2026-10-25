using System;
using Sandbox;

/// <summary>Player throwable using native inventory/ammo and the shared physical frag projectile.</summary>
[Title( "Void Breach Frag Grenade" ), Category( "Void Breach/Weapons" ), Icon( "flare" )]
public sealed class VBGrenadeWeapon : VBTraversalCombatWeapon, IVBInventorySlotPolicy, IVBFirstPersonHeldItem
{
	[Property] public GameObject ProjectilePrefab { get; set; }
	[Property, Range( 100f, 1500f )] public float ThrowSpeed { get; set; } = 800f;
	[Property, Range( 0f, 400f )] public float UpwardBoost { get; set; } = 160f;
	[Sync( SyncFlags.FromHost )] private TimeUntil NextThrow { get; set; }
	public int RequiredInventorySlot => VBWeaponSelectionController.ThrowableSlot;
	public bool LocksInventorySlot => true;
	public bool IsInventoryBound => false;
	public GameObject FirstPersonViewModel => ViewModel;
	public float FirstPersonMotionAimAmount => 0f;

	private bool CanThrow()
	{
		if ( !IsActive || !IsHeld || !Inventory.IsValid() || !Inventory.Active
			|| IsTraversalBlockingActions || !ProjectilePrefab.IsValid() ) return false;
		var health = Owner.GetComponent<VBHealthComponent>();
		return health.IsValid() && !health.IsDead && !health.IsDowned;
	}

	public override bool CanPrimaryAttack() => CanThrow() && NextThrow <= 0f && base.CanPrimaryAttack();
	public override bool CanSecondaryAttack() => false;
	public override bool CanReload() => false;
	public override void SecondaryAttack() { }
	public override void Reload() { }

	public override void PrimaryAttack()
	{
		if ( IsProxy || !CanThrow() ) return;
		// Only aim is sent. The host determines origin, ammo, fuse, damage and velocity.
		var aim = AimRay;
		var aimTrace = Scene.Trace.Ray( aim, 4096f ).IgnoreGameObjectHierarchy( Owner.GameObject ).UseHitboxes( true ).Run();
		var direction = aimTrace.EndPosition - Owner.EyePosition;
		RequestThrow( direction.IsNearZeroLength ? aim.Forward : direction.Normal );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestThrow( Vector3 direction )
	{
		if ( !Networking.IsHost || !CanThrow() || NextThrow > 0f || !HasPrimaryAmmo()
			|| !float.IsFinite( direction.x ) || !float.IsFinite( direction.y ) || !float.IsFinite( direction.z )
			|| direction.LengthSquared < 0.9f || direction.LengthSquared > 1.1f ) return;
		var collider = ProjectilePrefab.GetComponent<SphereCollider>();
		if ( !collider.IsValid() || !ProjectilePrefab.GetComponent<VBNpcGrenade>().IsValid()
			|| !ProjectilePrefab.GetComponent<Rigidbody>().IsValid() ) return;
		direction = direction.Normal;
		var eye = Owner.EyePosition;
		var origin = eye + direction * 28f;
		var clearance = Scene.Trace.Sphere( MathF.Max( 1f, collider.Radius ), eye, origin )
			.IgnoreGameObjectHierarchy( Owner.GameObject ).Run();
		if ( clearance.StartedSolid || clearance.Hit ) return;

		var grenade = ProjectilePrefab.Clone( new CloneConfig { StartEnabled = false, Transform = new Transform( origin ) } );
		if ( !TakePrimaryAmmo( 1 ) ) { grenade.Destroy(); return; }
		NextThrow = MathF.Max( 0.1f, PrimaryDelay );
		grenade.GetComponent<VBNpcGrenade>( includeDisabled: true ).Thrower = Owner.GameObject;
		grenade.Enabled = true;
		grenade.GetComponent<Rigidbody>().Velocity = direction * ThrowSpeed + Vector3.Up * UpwardBoost;
		grenade.NetworkSpawn(); // Host owns the projectile, including for remote throwers.
		PresentThrow();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PresentThrow()
	{
		// Invoke native presentation once per peer, without relaying another shot claim/RPC.
		base.OnShootEffects( default );
		HolderRenderer?.Set( "holdtype_attack", 0f );
	}

	protected override void OnUpdate()
	{
		if ( Application.IsDedicatedServer ) return;
		if ( !IsActive ) { DestroyWorldModel(); return; }
		base.OnUpdate();
		bool hasGrenade = HasPrimaryAmmo();
		if ( ViewModel.IsValid() ) ViewModel.Enabled = hasGrenade;
		if ( !hasGrenade ) { DestroyWorldModel(); return; }
		if ( IsHeld && !WorldModel.IsValid() ) CreateWorldModel();
	}
}
