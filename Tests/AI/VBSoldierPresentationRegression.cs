using System;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Citizen;

// Copy into Code/AI temporarily. Run vb_soldier_presentation_regression in host Play.
public sealed class VBSoldierPresentationRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	[ConCmd( "vb_soldier_presentation_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null ) return;
		_ = new GameObject( true, "Soldier presentation regression" ).AddComponent<VBSoldierPresentationRegression>().Verify();
	}
	private void Check( bool result, string message )
	{
		if ( !result ) _failures++;
		Log.Info( $"Soldier presentation {(result ? "PASS" : "FAIL")}: {message}" );
	}
	private async Task Verify()
	{
		try
		{
			var origin = new Vector3( 28000f, 28000f, 4000f );
			var floor = new GameObject( true, "Presentation test floor" ); _objects.Add( floor );
			floor.WorldPosition = origin - Vector3.Up * 16f;
			var floorCollider = floor.AddComponent<BoxCollider>(); floorCollider.Static = true;
			floorCollider.Scale = new Vector3( 2400f, 2400f, 32f );
			var actor = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone( new CloneConfig { StartEnabled = false, Transform = new Transform( origin ) } );
			_objects.Add( actor );
			var soldier = actor.GetComponent<VBSoldierController>( includeDisabled: true );
			soldier.WeaponPrefab = GameObject.GetPrefab( "weapons/mp5/mp5.prefab" );
			soldier.UseCover = false; soldier.UseTacticalPositions = false; soldier.PatrolRadius = 0f;
			soldier.GrenadeCooldown = 0f; soldier.GrenadeWindup = 1.2f;
			soldier.AimReactionTime = 0f;
			actor.GetComponent<VBAiPerceptionComponent>( includeDisabled: true ).AcquisitionDelay = 0f;
			actor.Enabled = true;
			await Task.DelaySeconds( 0.5f );
			var gun = soldier.Weapon;
			var helper = actor.Components.Get<CitizenAnimationHelper>( FindMode.InSelf | FindMode.InChildren );
			Check( gun.IsValid() && gun.NpcSupportHandTarget.IsValid() && helper.IkLeftHand == gun.NpcSupportHandTarget, "MP5 uses the native helper's support-hand target" );
			Check( actor.GetComponent<NavMeshAgent>().MaxSpeed == soldier.PatrolSpeed, "idle patrol uses walking speed" );
			gun.Clip1 = gun.PrimaryClipSize / 2;
			gun.Reload();
			await Task.DelaySeconds( 0.1f );
			Check( gun.IsReloading && !helper.IkLeftHand.IsValid(), "reload releases the support hand" );
			gun.CancelReload();
			await Task.DelaySeconds( 0.1f );
			var threat = new GameObject( true, "Grenade threat" ); _objects.Add( threat );
			threat.WorldPosition = origin + Vector3.Forward * 700f;
			threat.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
			var box = threat.AddComponent<BoxCollider>(); box.Scale = new Vector3( 24f, 24f, 72f ); box.Center = Vector3.Up * 36f;
			var health = threat.AddComponent<VBHealthComponent>(); health.StartingHardMaxHealth = 1000f; health.CanBeDowned = false;
			await Task.DelaySeconds( 0.5f );
			Check( soldier.State != VBSoldierState.ThrowGrenade, "a visible enemy does not trigger a grenade" );
			var cover = new GameObject( true, "Enemy grenade cover" ); _objects.Add( cover );
			cover.WorldPosition = origin + new Vector3( 350f, 0f, 50f );
			var coverCollider = cover.AddComponent<BoxCollider>(); coverCollider.Static = true; coverCollider.Scale = new Vector3( 24f, 200f, 100f );
			await Task.DelaySeconds( 1f );
			Check( soldier.State == VBSoldierState.ThrowGrenade, "a recently observed enemy behind cover starts a grenade windup" );
			Check( soldier.BodyRenderer.GetInt( "holdtype" ) == 4 && soldier.BodyRenderer.GetInt( "holdtype_handedness" ) == 1,
				"Human selects its native held-item/right-hand throw layer" );
			Check( !helper.IkLeftHand.IsValid() && !gun.NpcSupportHandTarget.IsValid(), "throw releases IK and hides the firearm" );
			soldier.BodyRenderer.TryGetBoneTransform( "hand_R", out var before );
			await Task.DelaySeconds( 0.2f );
			soldier.BodyRenderer.TryGetBoneTransform( "hand_R", out var after );
			Check( Vector3.DistanceBetween( before.Position, after.Position ) > 1f, "native throwing gesture moves the hand" );
			soldier.GrenadeCooldown = 1000f;
			await Task.DelaySeconds( 1f );
			Check( soldier.GrenadesRemaining == 1, "throw spends one grenade after the windup" );
			Check( soldier.State != VBSoldierState.ThrowGrenade && helper.IkLeftHand.IsValid(), "firearm and support hand return after throwing" );
			Log.Info( $"Soldier presentation FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception ex ) { Log.Error( $"Soldier presentation EXCEPTION: {ex}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( o => o.IsValid() ) ) obj.Destroy();
			if ( this.IsValid() ) GameObject.Destroy();
		}
	}
}
