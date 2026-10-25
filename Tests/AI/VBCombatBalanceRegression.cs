using System;
using System.Threading.Tasks;
using Sandbox;

// Copy temporarily into Code/AI, run vb_combat_balance_regression in host Play.
public sealed class VBCombatBalanceRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	[ConCmd( "vb_combat_balance_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null ) return;
		var fixture = new GameObject( true, "Combat balance regression" );
		_ = fixture.AddComponent<VBCombatBalanceRegression>().Verify();
	}
	private void Check( bool result, string label )
	{
		if ( !result ) _failures++;
		Log.Info( $"Combat balance {(result ? "PASS" : "FAIL")}: {label}" );
	}
	private static DamageInfo Bullet( float amount, string location ) => new( amount, null, null )
	{
		Tags = new TagSet( new[] { "bullet", location } )
	};
	private async Task Verify()
	{
		try
		{
			var origin = new Vector3( 26000f, 26000f, 4000f );
			foreach ( var prefab in new[] { "creatures/cartel_soldier/cartel_soldier.prefab", "creatures/zombie_classic/zombie_classic.prefab" } )
			{
				var actor = GameObject.GetPrefab( prefab ).Clone( new CloneConfig { StartEnabled = false, Transform = new Transform( origin ) } );
				_objects.Add( actor );
				var controller = actor.GetComponent<VBSoldierController>( includeDisabled: true ); if ( controller.IsValid() ) controller.Enabled = false;
				var zombie = actor.GetComponent<VBZombieController>( includeDisabled: true ); if ( zombie.IsValid() ) zombie.Enabled = false;
				actor.GetComponent<NavMeshAgent>( includeDisabled: true ).Enabled = false;
				var health = actor.GetComponent<VBHealthComponent>( includeDisabled: true );
				actor.Enabled = true;
				await Task.DelaySeconds( 0.2f );
				Log.Info( $"Combat balance profile {prefab}: hp={health.StartingHardMaxHealth} localized={health.UseLocalizedBulletDamage} head={health.HeadDamageMultiplier} limb={health.LimbDamageMultiplier} components={actor.GetComponents<VBHealthComponent>( includeDisabled: true ).Count()}" );
				foreach ( var gunPath in new[] { "weapons/pistol/9mmpistol.prefab", "weapons/mp5/mp5.prefab", "weapons/p90/p90.prefab" } )
				{
					float damage = GameObject.GetPrefab( gunPath ).GetComponent<VBCombatWeapon>().Ballistics.Damage;
					health.ResetHealth(); health.OnDamage( Bullet( damage, "head" ) );
					Check( !health.IsDead && MathF.Abs( health.CurrentHealth - (80f - damage * 4f) ) < 0.01f, $"{prefab}/{gunPath}: native head tag applies x4 once" );
					health.OnDamage( Bullet( damage, "head" ) );
					Check( health.IsDead && !health.IsDowned, $"{prefab}/{gunPath}: two headshots kill directly" );
					health.ResetHealth(); health.OnDamage( Bullet( damage, "chest" ) ); health.OnDamage( Bullet( damage, "chest" ) );
					Check( !health.IsDead && MathF.Abs( health.CurrentHealth - (80f - damage * 2f) ) < 0.01f, $"{prefab}/{gunPath}: torso needs more than two shots" );
				}
				health.ResetHealth(); health.OnDamage( Bullet( 12f, "arm" ) );
				Check( MathF.Abs( health.CurrentHealth - 71.6f ) < 0.01f, "arm damage uses the limb scale" );
				health.ResetHealth();
				health.OnDamage( new DamageInfo( 12f, null, null ) { Tags = new TagSet( new[] { "explosion", "head" } ) } );
				Check( MathF.Abs( health.CurrentHealth - 68f ) < 0.01f, "explosions never receive the bullet head multiplier" );
				actor.Destroy();
			}
			var shooterObject = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone( origin );
			_objects.Add( shooterObject );
			var shooter = shooterObject.GetComponent<VBSoldierController>();
			shooter.UseCover = false; shooter.PatrolRadius = 0f; shooter.MoveSpeed = 0f; shooter.GrenadeCooldown = 1000f;
			shooter.AccuracyScale = 0f; shooter.UseWeaponBurstTiming = false; shooter.BurstRestMin = 2f; shooter.BurstRestMax = 2f;
			var sight = shooterObject.GetComponent<VBAiPerceptionComponent>(); sight.ThinkInterval = 0.05f;
			await Task.DelaySeconds( 0.2f );
			var victim = new GameObject( true, "Visible balance threat" ); _objects.Add( victim );
			victim.WorldPosition = origin + Vector3.Forward * 400f;
			var collider = victim.AddComponent<BoxCollider>(); collider.Scale = new Vector3( 24f, 24f, 72f ); collider.Center = Vector3.Up * 36f;
			victim.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
			var hp = victim.AddComponent<VBHealthComponent>(); hp.StartingHardMaxHealth = 1000f; hp.CanBeDowned = false;
			await Task.DelaySeconds( 0.2f );
			Check( !sight.TargetVisible && hp.CurrentHealth == 1000f, "brief exposure does not instantly acquire or damage a target" );
			await Task.DelaySeconds( 0.4f );
			Check( sight.TargetVisible && hp.CurrentHealth == 1000f, "confirmed detection still waits for aiming reaction" );
			await Task.DelaySeconds( 0.95f );
			var gun = shooter.PrimaryWeapon;
			Check( gun.IsValid() && gun.PrimaryClipSize - gun.Clip1 == 3 && hp.CurrentHealth < 1000f, "one short native burst fires after reaction" );
			int clip = gun.Clip1;
			await Task.DelaySeconds( 0.5f );
			Check( gun.Clip1 == clip, "the soldier respects its pause between bursts" );
			Log.Info( $"Combat balance FINISHED: {_failures} failure(s)." );
		}
		catch ( TaskCanceledException ) { Log.Info( "Combat balance interrupted by scene stop." ); }
		catch ( Exception ex ) { Log.Error( $"Combat balance EXCEPTION: {ex}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( x => x.IsValid() ).ToArray() ) obj.Destroy();
			if ( GameObject.IsValid() ) GameObject.Destroy();
		}
	}
}
