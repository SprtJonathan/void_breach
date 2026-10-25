using System;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Citizen;

// Host-only isolated fixture. Uses real weapons and normal posture timings.
public sealed class VBSoldierFiringStanceRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private readonly Vector3 _origin = new( 64000f, 64000f, 4000f );
	private int _failures;
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Soldier firing stance {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private GameObject Box( string name, Vector3 offset, Vector3 size, string tag = "map" )
	{
		var obj = new GameObject( true, name ); _objects.Add( obj ); obj.WorldPosition = _origin + offset; obj.Tags.Add( tag );
		var collider = obj.AddComponent<BoxCollider>(); collider.Static = true; collider.Scale = size;
		return obj;
	}
	private GameObject Target( Vector3 offset )
	{
		var actor = Box( "Firing stance target", offset, new Vector3( 120f, 120f, 100f ), "player" );
		actor.GetComponent<BoxCollider>().Center = Vector3.Up * 50f;
		actor.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
		var health = actor.AddComponent<VBHealthComponent>(); health.StartingHardMaxHealth = 100000f; health.CanBeDowned = false;
		return actor;
	}
	private VBSoldierController Soldier( Vector3 offset )
	{
		var obj = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone(
			new CloneConfig { StartEnabled = false, Transform = new Transform( _origin + offset + Vector3.Up * 2f ) } );
		_objects.Add( obj );
		var soldier = obj.GetComponent<VBSoldierController>( true );
		soldier.UseCover = soldier.UseTacticalPositions = soldier.AvoidVisibleHazards = false;
		soldier.MoveSpeed = soldier.PatrolRadius = 0f; soldier.PreferredRange = 850f; soldier.GrenadeCount = 0;
		obj.GetComponent<VBHealthComponent>( true ).GodMode = true;
		var eyes = obj.GetComponent<VBAiPerceptionComponent>( true );
		eyes.AcquisitionDelay = 0f; eyes.ThinkInterval = 0.05f; eyes.HearingSensitivity = 0f;
		obj.Enabled = true;
		return soldier;
	}
	public async Task Verify()
	{
		var coordination = Scene.GetSystem<VBSoldierCoordination>();
		int oldFire = coordination.MaxShootersPerPlayer, oldCrouched = coordination.MaxCrouchedShootersPerPlayer;
		var nav = Scene.NavMesh;
		bool oldNav = nav.IsEnabled, oldCustom = nav.CustomBounds; var oldBounds = nav.Bounds;
		try
		{
			coordination.MaxShootersPerPlayer = 2; coordination.MaxCrouchedShootersPerPlayer = 1;
			Box( "Firing stance floor", new Vector3( 300f, 500f, -16f ), new Vector3( 3000f, 10000f, 32f ) );
			var firstTarget = Target( new Vector3( 600f, 0f, 0f ) );
			Target( new Vector3( 600f, 4000f, 0f ) );
			var squad = new[] { Soldier( new Vector3( 0f, -80f, 0f ) ), Soldier( new Vector3( 0f, 80f, 0f ) ) };
			var otherPlayerSoldier = Soldier( new Vector3( 0f, 4000f, 0f ) );
			await Task.DelaySeconds( 0.3f );
			foreach ( var soldier in squad.Append( otherPlayerSoldier ) ) soldier.Weapon.SetNextPrimaryFire( 30f );
			bool capHeld = true;
			TimeSince waiting = 0f;
			while ( waiting < 3f && (!squad.Any( s => s.IsInFiringStance ) || !otherPlayerSoldier.IsInFiringStance) )
			{
				await Task.Frame();
				capHeld &= squad.Count( s => s.IsInFiringStance ) <= 1;
			}
			var marksman = squad.FirstOrDefault( s => s.IsInFiringStance );
			Check( marksman.IsValid(), "an eligible distant shooter deliberately adopts a crouched firing position" );
			if ( !marksman.IsValid() ) return;
			var standing = squad.First( s => s != marksman );
			Check( capHeld && !standing.IsInFiringStance && !standing.IsCrouching && standing.HasFireSlot,
				"one stabilized shooter is paired with a standing shooter instead of synchronized crouching" );
			Check( otherPlayerSoldier.IsInFiringStance, "another player's independent budget permits its own stabilized shooter" );
			Check( marksman.IsCrouching && marksman.FiringStanceAccuracyMultiplier == 1f,
				"lowering the body does not immediately grant an accuracy bonus" );
			int clip = marksman.Weapon.Clip1;
			marksman.Weapon.SetNextPrimaryFire( 0f );
			await Task.DelaySeconds( 0.2f );
			Check( marksman.Weapon.Clip1 == clip, "the weapon waits for the crouched stance to settle before firing" );
			marksman.Weapon.SetNextPrimaryFire( 30f );
			await Task.DelaySeconds( 0.6f );
			Check( MathF.Abs( marksman.FiringStanceAccuracyMultiplier - 0.65f ) < 0.001f,
				"settled crouching narrows the native dispersion by 35 percent" );
			var gun = marksman.Weapon;
			var rawCone = gun.Ballistics.SpreadBase * gun.SpreadScale;
			Check( (gun.CurrentSpread - rawCone * marksman.CrouchedFireSpreadScale).Length < 0.01f,
				"actual weapon CurrentSpread applies the posture multiplier to native ballistics" );
			Check( (standing.Weapon.CurrentSpread - standing.Weapon.Ballistics.SpreadBase * standing.Weapon.SpreadScale).Length < 0.01f,
				"the standing shooter retains its ordinary dispersion" );
			Check( marksman.GetComponent<CharacterController>().Height == marksman.CrouchHeight
				&& marksman.GetComponent<VBAiPerceptionComponent>().EyeOffset.z < 40f,
				"firing posture lowers both physical height and perception" );
			Check( gun.GetMuzzleTransform().Position.z - marksman.WorldPosition.z < 40f,
				"dedicated-server muzzle follows crouching rather than firing from standing height" );
			var helper = marksman.Components.Get<CitizenAnimationHelper>( FindMode.InSelf | FindMode.InChildren );
			Check( helper.DuckLevel > 0.95f, "native Human animation receives the stable crouched pose" );
			float hp = firstTarget.GetComponent<VBHealthComponent>().CurrentHealth;
			gun.SetNextPrimaryFire( 0f ); await Task.DelaySeconds( 0.3f );
			Check( gun.Clip1 < clip && firstTarget.GetComponent<VBHealthComponent>().CurrentHealth < hp,
				"the crouched soldier fires real rounds and damages the target through native ballistics" );

			var otherGun = otherPlayerSoldier.Weapon;
			otherGun.Clip1 = otherGun.PrimaryClipSize / 2; otherGun.Reload();
			await Task.DelaySeconds( 0.1f );
			Check( otherGun.IsReloading && !otherPlayerSoldier.IsInFiringStance && otherPlayerSoldier.FiringStanceAccuracyMultiplier == 1f,
				"reload cancels the stabilized firing role and its precision bonus" );
			firstTarget.WorldPosition = _origin + new Vector3( 80f, 0f, 0f );
			await Task.DelaySeconds( 0.2f );
			Check( !marksman.IsInFiringStance && !marksman.IsCrouching && marksman.FiringStanceAccuracyMultiplier == 1f,
				"an approaching player makes the shooter stand instead of remaining planted at close range" );
			firstTarget.WorldPosition = _origin + new Vector3( 600f, 0f, 0f );
			waiting = 0f;
			while ( waiting < 3f && !standing.IsInFiringStance ) await Task.Frame();
			Check( standing.IsInFiringStance && !marksman.IsInFiringStance,
				"another shooter can take the role while the previous shooter observes its posture cooldown" );
			Check( standing.FiringStanceTestMove( standing.WorldPosition + Vector3.Backward * 100f )
				&& !standing.IsInFiringStance && !standing.IsCrouching && standing.FiringStanceAccuracyMultiplier == 1f,
				"a movement order releases the firing role and its accuracy bonus" );
			await Task.DelaySeconds( 0.15f );

			Target( new Vector3( 600f, -3000f, 0f ) );
			Box( "Obstacle clear when standing only", new Vector3( 200f, -3000f, 21f ), new Vector3( 32f, 240f, 42f ) );
			var blocked = Soldier( new Vector3( 0f, -3000f, 0f ) );
			await Task.DelaySeconds( 2.5f );
			Check( !blocked.IsInFiringStance && !blocked.IsCrouching && blocked.Weapon.Clip1 < blocked.Weapon.PrimaryClipSize,
				"a low obstacle makes the soldier fire standing when the crouched barrel would be blocked" );

			var health = otherPlayerSoldier.GetComponent<VBHealthComponent>(); health.GodMode = false;
			health.TakeDamage( VBDamageInfo.Create( 10000f, VBDamageType.Bullet, firstTarget ) );
			await Task.DelaySeconds( 0.1f );
			Check( health.IsDead && !otherPlayerSoldier.IsInFiringStance && otherPlayerSoldier.FiringStanceAccuracyMultiplier == 1f,
				"death clears the firing role" );
			Check( !otherGun.Inventory.IsValid() && otherGun.SpreadScale == 1f
				&& (otherGun.CurrentSpread - otherGun.Ballistics.SpreadBase).Length < 0.01f,
				"a dropped gun carries no soldier stance or precision bonus" );

			Target( new Vector3( 600f, -1500f, 0f ) );
			var covered = Soldier( new Vector3( 0f, -1500f, 0f ) );
			covered.UseCover = true; covered.MoveSpeed = 115f;
			covered.SupportCoverHoldMin = covered.SupportCoverHoldMax = covered.CoverHoldTime = 0.4f;
			await Task.DelaySeconds( 0.3f ); // Observe the target before creating the occluder.
			Box( "Firing cover with side exits", new Vector3( 90f, -1500f, 50f ), new Vector3( 40f, 120f, 100f ) );
			nav.CustomBounds = true;
			nav.Bounds = new BBox( _origin + new Vector3( -500f, -2100f, -64f ), _origin + new Vector3( 1200f, -900f, 180f ) );
			nav.IsEnabled = true; await Task.DelaySeconds( 0.2f );
			Check( await nav.Generate( Scene.PhysicsWorld ), "cover firing fixture generates actual side-exit routes" );
			bool sawSettledPeek = false, returnedToCover = false;
			waiting = 0f;
			while ( waiting < 10f )
			{
				await Task.Frame();
				sawSettledPeek |= covered.IsInFiringStance && covered.FiringStanceTestFromCover && covered.FiringStanceAccuracyMultiplier < 1f;
				if ( sawSettledPeek && !covered.IsInFiringStance && covered.State is VBSoldierState.TakeCover or VBSoldierState.HoldCover or VBSoldierState.Reload ) returnedToCover = true;
			}
			Check( sawSettledPeek, "a clear side peek can become a crouched firing position with normal cover enabled" );
			Check( returnedToCover, "stabilized firing respects the exposure deadline and returns behind cover" );
			Log.Info( $"Soldier firing stance FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception ex ) { Log.Error( $"Soldier firing stance EXCEPTION: {ex}" ); }
		finally
		{
			coordination.MaxShootersPerPlayer = oldFire; coordination.MaxCrouchedShootersPerPlayer = oldCrouched;
			foreach ( var obj in _objects.Where( o => o.IsValid() ) ) obj.Destroy();
			nav.IsEnabled = oldNav; nav.CustomBounds = oldCustom; nav.Bounds = oldBounds;
			GameObject.Destroy();
		}
	}
}

public sealed partial class VBSoldierController
{
	internal bool FiringStanceTestMove( Vector3 point ) => TryMove( point, allowTraversal: false );
	internal bool FiringStanceTestFromCover => _firingFromCover;
}
