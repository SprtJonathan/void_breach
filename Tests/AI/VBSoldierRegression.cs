using System;
using System.Threading.Tasks;
using Sandbox;

// Copy temporarily to Code/AI, then run vb_soldier_regression in host Play.
public sealed class VBSoldierRegression : Component
{
	private static VBSoldierRegression _running;
	private readonly List<GameObject> _objects = new();
	private readonly Vector3 _origin = new( 22000f, 22000f, 4000f );
	private int _failures;

	[ConCmd( "vb_soldier_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || _running.IsValid() ) return;
		var obj = new GameObject( true, "Soldier regression" );
		_running = obj.AddComponent<VBSoldierRegression>();
		_ = _running.Verify();
	}

	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Soldier regression {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private GameObject Box( string name, Vector3 position, Vector3 size )
	{
		var obj = new GameObject( true, name ); _objects.Add( obj );
		obj.WorldPosition = position;
		obj.AddComponent<BoxCollider>().Scale = size;
		return obj;
	}
	private VBHealthComponent Actor( string faction, Vector3 offset )
	{
		var obj = Box( faction, _origin + offset, new Vector3( 24f, 24f, 72f ) );
		obj.GetComponent<BoxCollider>().Center = Vector3.Up * 36f;
		var member = obj.AddComponent<VBFactionComponent>();
		member.FactionId = faction;
		member.HostileFactions.Add( VBFactionComponent.CartelFaction );
		var health = obj.AddComponent<VBHealthComponent>();
		health.StartingHardMaxHealth = 10000f; health.CanBeDowned = false;
		obj.NetworkMode = NetworkMode.Object; obj.NetworkSpawn();
		return health;
	}

	private async Task Verify()
	{
		var fixture = GameObject;
		try
		{
			Box( "Test floor", _origin + Vector3.Down * 16f, new Vector3( 3000f, 3000f, 32f ) );
			var soldierObj = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone( _origin );
			_objects.Add( soldierObj ); soldierObj.NetworkSpawn();
			var soldier = soldierObj.GetComponent<VBSoldierController>();
			soldier.UseCover = false; soldier.MoveSpeed = 0f; soldier.PatrolRadius = 0f;
			soldier.UseWeaponBurstTiming = true; // Native weapon checks; tempo has a dedicated squad fixture.
			soldier.UseCrouchedFire = false; // Native weapon timing; firing posture has its own fixture.
			soldier.GrenadeCount = 0;
			var perception = soldierObj.GetComponent<VBAiPerceptionComponent>();
			perception.FieldOfView = 360f; perception.ThinkInterval = 0.05f;
			perception.AcquisitionDelay = 0f; soldier.AimReactionTime = 0f; // Pacing has its own regression fixture.
			var player = Actor( VBFactionComponent.PlayerFaction, new Vector3( 600f, 0f, 0f ) );
			var xen = Actor( VBFactionComponent.XenFaction, new Vector3( 500f, 140f, 0f ) );
			await Task.DelaySeconds( 0.15f );
			var weapon = soldier.Weapon;
			Check( weapon.IsValid() && weapon.IsActive && !weapon.IsHeld, "native inventory equips the primary gun without a PlayerController" );
			if ( !weapon.IsValid() ) return;
			weapon.SetNextPrimaryFire( 30f );
			var faction = soldierObj.GetComponent<VBFactionComponent>();
			Check( faction.IsHostileTo( xen.GetComponent<VBFactionComponent>() ) && xen.GetComponent<VBFactionComponent>().IsHostileTo( faction ), "Cartel and Xen hostility is reciprocal" );
			Check( perception.CurrentTarget == player.GameObject, "player priority beats a slightly nearer distant Xen" );
			xen.WorldPosition = _origin + new Vector3( 100f, 60f, 0f );
			await Task.DelaySeconds( 0.2f );
			Check( perception.CurrentTarget == xen.GameObject, "an immediate close threat displaces the current target" );
			xen.WorldPosition = _origin + new Vector3( 500f, 140f, 0f );
			var hit = VBDamageInfo.Create( 1f, VBDamageType.Blunt, player.GameObject );
			hit.Position = soldier.WorldPosition;
			soldierObj.GetComponent<VBHealthComponent>().TakeDamage( hit );
			await Task.DelaySeconds( 0.2f );
			Check( perception.CurrentTarget == player.GameObject, "a recent attacker becomes the priority after the close threat recedes" );
			xen.GameObject.Destroy();
			var ally = Actor( VBFactionComponent.CartelFaction, new Vector3( 300f, 0f, 0f ) );
			await Task.DelaySeconds( 0.15f );
			Check( !perception.IsViableTarget( ally.GameObject ), "a Cartel ally is never a viable target" );
			weapon.SetNextPrimaryFire( 0f );
			int clip = weapon.Clip1;
			await Task.DelaySeconds( 0.45f );
			Check( weapon.Clip1 == clip, "an ally blocking the firing lane stops shots" );
			ally.GameObject.Destroy();
			weapon.SpreadScale = 0f;
			float hp = player.CurrentHealth;
			await Task.DelaySeconds( 0.6f );
			Check( weapon.Clip1 < clip && player.CurrentHealth < hp, "native FirePrimary consumes ammunition and damages the hostile actor" );
			Log.Info( $"Soldier combat diagnostic: state={soldier.State} grounded={soldierObj.GetComponent<VBNpcLocomotion>().IsGrounded} z={soldier.WorldPosition.z - _origin.z} target={perception.CurrentTarget?.Name} clip={weapon.Clip1} active={weapon.IsActive}" );
			var wall = Box( "Occlusion wall", _origin + new Vector3( 300f, 0f, 100f ), new Vector3( 32f, 350f, 200f ) );
			await Task.DelaySeconds( 0.2f );
			var remembered = perception.LastKnownPosition;
			player.WorldPosition += new Vector3( 0f, 80f, 0f );
			await Task.DelaySeconds( 0.25f );
			Check( !perception.TargetVisible && Vector3.DistanceBetween( remembered, perception.LastKnownPosition ) < 1f, "hidden movement does not update the last known position" );
			Check( soldier.State == VBSoldierState.Support && soldier.RemembersTarget( player.GameObject ), "loss of sight holds the known contact before beginning a search" );
			wall.Destroy();
			await Task.DelaySeconds( 0.2f );
			var health = soldierObj.GetComponent<VBHealthComponent>();
			health.TakeDamage( VBDamageInfo.Create( 10000f, VBDamageType.Bullet, player.GameObject ) );
			await Task.DelaySeconds( 0.15f );
			Check( health.IsDead && soldier.State == VBSoldierState.Dead && !weapon.Inventory.IsValid(), "death cancels combat and drops the native weapon" );

			var throwerObj = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone( _origin + new Vector3( 0f, -900f, 0f ) );
			_objects.Add( throwerObj ); throwerObj.NetworkSpawn();
			var thrower = throwerObj.GetComponent<VBSoldierController>();
			thrower.UseCover = false; thrower.MoveSpeed = 0f; thrower.PatrolRadius = 0f;
			thrower.GrenadeCount = 1; thrower.GrenadeCooldown = 0.2f; thrower.GrenadeWindup = 0.15f;
			throwerObj.GetComponent<VBAiPerceptionComponent>().SightRange = 750f;
			Actor( VBFactionComponent.PlayerFaction, new Vector3( 600f, -900f, 0f ) );
			var blastAlly = Actor( VBFactionComponent.CartelFaction, new Vector3( 640f, -850f, 0f ) );
			await Task.DelaySeconds( 0.1f );
			thrower.Weapon?.SetNextPrimaryFire( 30f ); // Isolate grenade decisions from finite-gun reloads.
			await Task.DelaySeconds( 1.3f );
			Check( thrower.GrenadesRemaining == 1 && !Scene.GetAll<VBNpcGrenade>().Any( x => x.Thrower == throwerObj ), "an exposed visible enemy is engaged with the gun instead of a grenade" );
			thrower.GrenadeMemoryTime = 15f;
			Box( "Enemy cover", _origin + new Vector3( 300f, -900f, 50f ), new Vector3( 24f, 200f, 100f ) );
			await Task.DelaySeconds( 1.3f );
			Check( thrower.GrenadesRemaining == 1, "an ally near the remembered hidden enemy prevents a grenade" );
			var ceiling = Box( "Arc obstruction", _origin + new Vector3( 300f, -900f, 120f ), new Vector3( 800f, 300f, 16f ) );
			blastAlly.GameObject.Destroy();
			await Task.DelaySeconds( 1.3f );
			Check( thrower.GrenadesRemaining == 1, "a ceiling blocking both ballistic arcs prevents a throw" );
			ceiling.Destroy();
			await Task.DelaySeconds( 1.6f );
			var thrown = Scene.GetAll<VBNpcGrenade>().Where( x => x.Thrower == throwerObj ).ToArray();
			foreach ( var projectile in thrown ) _objects.Add( projectile.GameObject );
			Check( thrower.GrenadesRemaining == 0 && thrown.Length == 1, "a clear arc ending at a hostile actor launches one physical grenade" );
			thrower.Enabled = false;

			// Native radial damage must reach unshielded actors and respect world occlusion.
			var blastOrigin = _origin + new Vector3( 0f, 900f, 8f );
			var exposed = Actor( VBFactionComponent.PlayerFaction, new Vector3( -100f, 900f, 0f ) );
			var protectedActor = Actor( VBFactionComponent.PlayerFaction, new Vector3( 100f, 900f, 0f ) );
			// Native RadiusDamage's occlusion query filters for map geometry.
			Box( "Blast shield", _origin + new Vector3( 50f, 900f, 60f ), new Vector3( 24f, 160f, 120f ) ).Tags.Add( "map" );
			await Task.DelaySeconds( 0.1f );
			float exposedHp = exposed.CurrentHealth, protectedHp = protectedActor.CurrentHealth;
			var grenade = GameObject.GetPrefab( "weapons/grenade/frag_grenade_projectile.prefab" ).Clone( blastOrigin );
			_objects.Add( grenade ); grenade.GetComponent<VBNpcGrenade>().FuseTime = 0.1f; grenade.NetworkSpawn();
			await Task.DelaySeconds( 0.4f );
			Check( !grenade.IsValid() && exposed.CurrentHealth < exposedHp, "native grenade damage reaches an exposed actor and removes the projectile" );
			Check( protectedActor.CurrentHealth == protectedHp, "a wall shields the actor from native RadiusDamage" );
			Log.Info( $"Soldier regression FINISHED: {_failures} failure(s)." );
		}
		catch ( TaskCanceledException ) { Log.Info( "Soldier regression interrupted by scene stop." ); }
		catch ( Exception ex ) { Log.Error( $"Soldier regression EXCEPTION: {ex}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( x => x.IsValid() ).ToArray() ) obj.Destroy();
			if ( fixture.IsValid() ) fixture.Destroy();
		}
	}
}
