using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[Property, Group( "Grenades" )] public GameObject GrenadePrefab { get; set; }
	[Property, Group( "Grenades" )] public GameObject GrenadeItemPrefab { get; set; }
	[Property, Group( "Grenades" )] public BaseAmmoResource GrenadeAmmo { get; set; }
	[Property, Group( "Grenades" )] public int GrenadeCount { get; set; } = 2;
	[Property, Group( "Grenades" )] public float GrenadeCooldown { get; set; } = 12f;
	[Property, Group( "Grenades" )] public float InitialGrenadeDelay { get; set; } = 3f;
	[Property, Group( "Grenades" )] public float GrenadePressureDelay { get; set; } = 5f;
	[Property, Group( "Grenades" )] public float GrenadeWindup { get; set; } = 0.8f;
	[Property, Group( "Grenades" )] public float GrenadeMinRange { get; set; } = 350f;
	[Property, Group( "Grenades" )] public float GrenadeMaxRange { get; set; } = 1100f;
	[Property, Group( "Grenades" )] public float GrenadeMaxSpeed { get; set; } = 960f;
	[Property, Group( "Grenades" )] public float GrenadeOcclusionDelay { get; set; } = 0.75f;
	[Property, Group( "Grenades" )] public float GrenadeMemoryTime { get; set; } = 6f;
	[Property, Group( "Grenades" )] public SoundEvent GrenadeWarningSound { get; set; }
	public int GrenadesRemaining => Inventory.IsValid() && GrenadeAmmo is not null ? Inventory.GetAmmo( GrenadeAmmo ) : 0;
	[Sync( SyncFlags.FromHost )] private TimeUntil _nextGrenade { get; set; }
	[Sync( SyncFlags.FromHost )] private TimeUntil _grenadeRelease { get; set; }
	[Sync( SyncFlags.FromHost )] private Vector3 _grenadeTarget { get; set; }
	[Sync( SyncFlags.FromHost )] private GameObject _grenadeVictim { get; set; }
	[Sync( SyncFlags.FromHost )] private bool _grenadePressureThrow { get; set; }
	private GameObject _heldGrenade;
	private void UpdateGrenadePresentation()
	{
		if ( Application.IsDedicatedServer ) return;
		if ( State != VBSoldierState.ThrowGrenade || !CanFight )
		{
			if ( _heldGrenade.IsValid() ) _heldGrenade.Destroy();
			return;
		}
		var item = GrenadeItemPrefab.IsValid() ? GrenadeItemPrefab.GetComponent<VBGrenadeWeapon>() : null;
		var bone = BodyRenderer.IsValid() ? BodyRenderer.GetBoneObject( "hold_R" ) : null;
		if ( !item.IsValid() || !item.WorldModelPrefab.IsValid() || !bone.IsValid() ) return;
		if ( !_heldGrenade.IsValid() )
		{
			_heldGrenade = item.WorldModelPrefab.Clone( new CloneConfig { Parent = bone, StartEnabled = true, Transform = item.WorldModelPrefab.LocalTransform } );
			_heldGrenade.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		}
		else if ( _heldGrenade.Parent != bone ) _heldGrenade.SetParent( bone, keepWorldPosition: false );
	}

	private bool TryBeginGrenade()
	{
		bool pressure = Perception.TargetVisible && _pressureSince >= 0f
			&& Time.Now - _pressureSince >= MathF.Max( 2f, GrenadePressureDelay );
		if ( !GrenadePrefab.IsValid() || GrenadesRemaining <= 0 || FireReady > 0f || _nextGrenade > 0f
			|| Weapon.IsValid() && Weapon.IsReloading || Perception.TargetVisible && !pressure
			|| !Perception.IsViableTarget( Perception.CurrentTarget )
			|| Perception.LastSeenTarget != Perception.CurrentTarget ) return false;
		float unseen = Time.Now - Perception.LastSeenAt;
		if ( !pressure && (unseen < MathF.Max( 0f, GrenadeOcclusionDelay ) || unseen > GrenadeMemoryTime) ) return false;
		if ( !Coordination.CanThrowGrenade( this ) ) return false;
		_nextGrenade = 1f; // Throttle failed arc and safety probes.
		var config = GrenadePrefab.GetComponent<VBNpcGrenade>();
		if ( !config.IsValid() ) return false;
		bool found = false;
		// A sound can update the remembered point. Otherwise project only the
		// observed movement briefly around a corner; never read a hidden transform.
		foreach ( var point in new[] { Perception.LastKnownPosition,
			Perception.LastSeenPosition + Perception.LastSeenVelocity * MathF.Min( unseen, 0.6f ),
			Perception.LastSeenPosition + Perception.LastSeenVelocity * MathF.Min( unseen, 0.35f ) } )
		{
			var floor = Scene.Trace.Ray( point + Vector3.Up * 24f, point - Vector3.Up * 128f )
				.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( Perception.CurrentTarget ).IgnoreDynamic()
				.WithoutTags( new[] { "player", "npc", "npc_body", "zombie", "npc_ragdoll", "trigger" } ).Run();
			if ( !TraversalGeometry.IsLanding( floor ) ) continue;
			var candidate = floor.EndPosition + Vector3.Up * 8f;
			var cover = Scene.Trace.Ray( WorldPosition + Perception.EyeOffset, candidate + Perception.TargetOffset )
				.IgnoreGameObjectHierarchy( GameObject ).IgnoreDynamic().Run();
			if ( !pressure && !cover.Hit || cover.StartedSolid || !SafeGrenadeTarget( candidate, config.BlastRadius )
				|| !TryGrenadeVelocity( candidate, out _ ) ) continue;
			_grenadeTarget = candidate; found = true; break;
		}
		if ( !found ) return false;
		_grenadeVictim = Perception.CurrentTarget; _grenadePressureThrow = pressure;
		EndCrouchedFire(); EndContactRecovery();
		Coordination.ReleaseInvestigation( this ); _searching = _approachingLastKnown = false;
		ReleaseFireSlot();
		StopMoving(); ClearCover(); ClearTacticalMove(); _burstRemaining = 0;
		State = VBSoldierState.ThrowGrenade;
		_combatReason = pressure ? "flush a stationary visible player" : "flush the last observed cover";
		_grenadeRelease = MathF.Max( 0.1f, GrenadeWindup );
		WarnGrenade();
		return true;
	}

	private void TickGrenade()
	{
		StopMoving();
		Face( _grenadeTarget - WorldPosition );
		if ( _grenadeRelease > 0f ) return;
		State = VBSoldierState.Advance;
		_nextGrenade = GrenadeCooldown;
		var config = GrenadePrefab.IsValid() ? GrenadePrefab.GetComponent<VBNpcGrenade>() : null;
		// Recheck after the telegraph: an ally may have entered the blast area.
		if ( !config.IsValid() || !SafeGrenadeTarget( _grenadeTarget, config.BlastRadius )
			|| !Coordination.CanThrowGrenade( this )
			|| Perception.CurrentTarget != _grenadeVictim || !Perception.IsViableTarget( _grenadeVictim ) || Perception.LastSeenTarget != _grenadeVictim
			|| !_grenadePressureThrow && Perception.TargetVisible || Time.Now - Perception.LastSeenAt > GrenadeMemoryTime
			|| _grenadePressureThrow && Vector3.DistanceBetween( Perception.LastKnownPosition, _grenadeTarget ) > MathF.Min( config.BlastRadius * 0.65f, 128f )
			|| GrenadesRemaining <= 0 || !TryGrenadeVelocity( _grenadeTarget, out var velocity ) ) return;
		var grenade = GrenadePrefab.Clone( GrenadeOrigin );
		var projectile = grenade.GetComponent<VBNpcGrenade>();
		var body = grenade.GetComponent<Rigidbody>();
		if ( !projectile.IsValid() || !body.IsValid() ) { grenade.Destroy(); return; }
		if ( Inventory.TakeAmmo( GrenadeAmmo, 1 ) != 1 ) { grenade.Destroy(); return; }
		projectile.Thrower = GameObject;
		projectile.SoldierCoordinated = true;
		body.Velocity = velocity;
		grenade.NetworkSpawn();
		Coordination.GrenadeThrown( projectile );
	}

	private Vector3 GrenadeOrigin => WorldPosition + Perception.EyeOffset + WorldRotation.Forward * 30f;
	private bool SafeGrenadeTarget( Vector3 point, float radius )
	{
		float distance = Vector3.DistanceBetween( WorldPosition, point );
		if ( distance < MathF.Max( GrenadeMinRange, radius + 64f ) || distance > GrenadeMaxRange ) return false;
		foreach ( var health in Scene.GetAll<VBHealthComponent>() )
		{
			if ( health.IsDead || health.GameObject == GameObject ) continue;
			var faction = health.GetComponent<VBFactionComponent>();
			if ( faction.IsValid() && Faction.IsHostileTo( faction ) ) continue;
			if ( Vector3.DistanceBetween( point, health.WorldPosition ) < radius + 96f ) return false;
		}
		return true;
	}

	private bool TryGrenadeVelocity( Vector3 target, out Vector3 velocity )
	{
		var origin = GrenadeOrigin;
		var gravity = Scene.PhysicsWorld.Gravity;
		// Shorter, flatter throws fit indoor ceilings. The same swept projectile,
		// speed limit and ally/self blast checks apply to every trajectory.
		foreach ( float flight in new[] { 0.45f, 0.6f, 0.8f, 1f, 1.5f } )
		{
			velocity = (target - origin - gravity * (0.5f * flight * flight)) / flight;
			if ( velocity.Length > GrenadeMaxSpeed ) continue;
			bool clear = true;
			var previous = origin;
			for ( int i = 1; i <= 24; i++ )
			{
				float t = flight * i / 24f;
				var point = origin + velocity * t + gravity * (0.5f * t * t);
				var trace = Scene.Trace.Sphere( 4f, previous, point ).IgnoreGameObjectHierarchy( GameObject ).Run();
				if ( trace.StartedSolid ) { clear = false; break; }
				if ( trace.Hit )
				{
					// Reaching the victim's feet/body is a valid end to the arc, not a ceiling obstruction.
					clear = i >= 22 && Vector3.DistanceBetween( trace.EndPosition, target ) < 48f
						&& Perception.IsViableTarget( trace.GameObject );
					break;
				}
				previous = point;
			}
			if ( clear ) return true;
		}
		velocity = Vector3.Zero;
		return false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void WarnGrenade()
	{
		if ( GrenadeWarningSound is not null ) GameObject.PlaySound( GrenadeWarningSound );
		// Human's actions graph selects HoldItem_RH_Throw_Normal_delta for
		// holditem + RH + holdtype_attack=0. b_attack on rifle only plays recoil.
		BodyRenderer?.Set( "holdtype", "holditem" );
		BodyRenderer?.Set( "holdtype_handedness", 1 );
		BodyRenderer?.Set( "holdtype_attack", 0f );
		BodyRenderer?.Set( "b_attack", true );
		UpdateWeaponHands();
	}
}
