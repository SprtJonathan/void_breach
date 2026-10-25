using System;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Citizen;

// Compile only in the isolated native test project, alongside its test-only partial methods.
public sealed class VBSoldierCoverRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	private readonly Vector3 _origin = new( 56000f, 56000f, 4000f );
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Soldier cover {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private GameObject Box( string name, Vector3 offset, Vector3 size, string tag = "map" )
	{
		var obj = new GameObject( true, name ); _objects.Add( obj ); obj.WorldPosition = _origin + offset; obj.Tags.Add( tag );
		var box = obj.AddComponent<BoxCollider>(); box.Static = true; box.Scale = size;
		return obj;
	}
	private GameObject Target( Vector3 offset )
	{
		var target = Box( "Cover target", offset, new Vector3( 24f, 24f, 72f ), "player" );
		target.GetComponent<BoxCollider>().Center = Vector3.Up * 36f;
		target.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
		var health = target.AddComponent<VBHealthComponent>(); health.StartingHardMaxHealth = 100000f; health.CanBeDowned = false;
		return target;
	}
	private VBSoldierController Soldier( Vector3 offset, bool cover = true, bool tactics = false )
	{
		var actor = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone(
			new CloneConfig { StartEnabled = false, Transform = new Transform( _origin + offset + Vector3.Up * 2f ) } );
		_objects.Add( actor );
		var soldier = actor.GetComponent<VBSoldierController>( true );
		soldier.UseCover = cover; soldier.UseTacticalPositions = tactics; soldier.AvoidVisibleHazards = false;
		soldier.PatrolRadius = soldier.AimReactionTime = 0f; soldier.GrenadeCount = 0; soldier.MoveSpeed = 160f;
		soldier.InitialAssessmentTime = 0f; // Geometry tests; deliberate timing is exercised separately.
		var eyes = actor.GetComponent<VBAiPerceptionComponent>( true );
		eyes.AcquisitionDelay = 0f; eyes.ThinkInterval = 0.05f; eyes.SightRange = 900f; eyes.HearingSensitivity = 0f;
		actor.Enabled = true;
		return soldier;
	}
	public async Task Verify()
	{
		var nav = Scene.NavMesh;
		bool enabled = nav.IsEnabled, custom = nav.CustomBounds;
		var bounds = nav.Bounds;
		var coordination = Scene.GetSystem<VBSoldierCoordination>();
		int cap = coordination.MaxShootersPerPlayer;
		try
		{
			coordination.MaxShootersPerPlayer = 0;
			Box( "Cover floor", new Vector3( 300f, 0f, -16f ), new Vector3( 3000f, 4500f, 32f ) );
			Box( "Low cover", new Vector3( 90f, 0f, 24f ), new Vector3( 40f, 160f, 48f ) );
			Box( "Second low cover", new Vector3( 90f, 220f, 24f ), new Vector3( 40f, 160f, 48f ) );
			Target( new Vector3( 600f, 0f, 0f ) );
			nav.CustomBounds = true;
			nav.Bounds = new BBox( _origin + new Vector3( -1100f, -2100f, -64f ), _origin + new Vector3( 1700f, 1800f, 180f ) );
			nav.IsEnabled = true;
			await Task.DelaySeconds( 0.2f );
			Check( await nav.Generate( Scene.PhysicsWorld ), "native mesh generated around real low obstacles" );
			var soldier = Soldier( Vector3.Zero );
			await Task.DelaySeconds( 1f );
			Check( soldier.CoverTestSheltered && soldier.IsCrouching && !soldier.IsDynamicallyTraversing,
				"soldier chooses crouching behind a climbable crate instead of vaulting it" );
			Check( soldier.GetComponent<CharacterController>().Height == soldier.CrouchHeight
				&& soldier.GetComponent<VBAiPerceptionComponent>().EyeOffset.z < 40f,
				"crouch lowers physical hull and perception together" );
			var capsule = soldier.GetComponentsInChildren<CapsuleCollider>().First( x => x.GameObject.Tags.Has( "npc_body" ) );
			Check( capsule.End.z + capsule.Radius == soldier.CrouchHeight, "other actors collide with the crouched capsule height" );
			var helper = soldier.Components.Get<CitizenAnimationHelper>( FindMode.InSelf | FindMode.InChildren );
			Check( helper.DuckLevel > 0.9f, "native Human graph receives the crouched pose" );
			var ceiling = Box( "Late headroom obstruction", new Vector3( 0f, 0f, 62f ), new Vector3( 70f, 70f, 16f ) );
			await Task.DelaySeconds( 0.1f );
			Check( !soldier.CoverTestStand() && soldier.IsCrouching, "a new ceiling prevents standing through geometry" );
			ceiling.Destroy(); await Task.DelaySeconds( 0.1f ); // Physics removal completes at a fixed-step boundary.
			Check( soldier.CoverTestStand() && soldier.GetComponent<CharacterController>().Height == 72f,
				"standing restores the hull after headroom clears" );
			await Task.DelaySeconds( 0.1f );
			soldier.Weapon.Clip1 = soldier.Weapon.PrimaryClipSize / 2;
			await Task.DelaySeconds( 0.15f );
			Check( soldier.Weapon.IsReloading && soldier.IsCrouching, "waiting soldier reloads a half-empty magazine under low cover" );
			await Task.DelaySeconds( 1.2f );
			Check( soldier.CoverTestSheltered && soldier.IsCrouching && soldier.State == VBSoldierState.Reload,
				"own cover occlusion does not abandon shelter for a search" );
			var second = Soldier( new Vector3( -30f, 220f, 0f ) );
			await Task.DelaySeconds( 1f );
			Check( !second.CoverTestCanReserve( soldier.CoverTestPosition ), "another soldier cannot reserve the same sheltered position" );
			Check( second.CoverTestSheltered && Vector3.DistanceBetween( soldier.CoverTestPosition, second.CoverTestPosition ) >= 64f,
				"nearby soldiers find separate actual cover destinations" );

			var deepTarget = Target( new Vector3( 600f, 700f, 0f ) );
			var deep = Soldier( new Vector3( 0f, 700f, 0f ) );
			await Task.DelaySeconds( 0.35f );
			Box( "Deep cover wall", new Vector3( 90f, 700f, 80f ), new Vector3( 40f, 500f, 160f ) );
			await Task.DelaySeconds( 3.2f );
			Check( !deep.CoverTestSheltered || deep.CoverTestPeek,
				"a healthy soldier rejects a blind shelter without a way to resume the fight" );

			var flankTarget = Target( new Vector3( 600f, -1000f, 0f ) );
			var flank = Soldier( new Vector3( 0f, -1000f, 0f ), cover: false, tactics: true );
			await Task.DelaySeconds( 0.5f );
			Check( flank.CoverTestFlanking, "a non-shooter commits to an alternate walking route" );
			var start = flank.WorldPosition;
			var eyes = flank.GetComponent<VBAiPerceptionComponent>();
			Box( "Occluding the active flank", new Vector3( 300f, -1000f, 80f ), new Vector3( 32f, 400f, 160f ) );
			await Task.DelaySeconds( 0.2f );
			var remembered = eyes.LastKnownPosition;
			flankTarget.WorldPosition += Vector3.Right * 60f;
			await Task.DelaySeconds( 0.3f );
			Check( !eyes.TargetVisible && flank.CoverTestFlanking && !flank.CoverTestSearching,
				"losing sight during a flank preserves the route instead of converging on the player" );
			Check( Vector3.DistanceBetween( eyes.LastKnownPosition, remembered ) < 1f,
				"alternate route uses remembered contact without tracking hidden player motion" );
			Check( Vector3.DistanceBetween( start, flank.WorldPosition ) > 35f && flank.Weapon.Clip1 == flank.Weapon.PrimaryClipSize,
				"occluded support route continues physical movement without shooting" );
			Log.Info( $"Soldier cover FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception ex ) { Log.Error( $"Soldier cover EXCEPTION: {ex}" ); }
		finally
		{
			coordination.MaxShootersPerPlayer = cap;
			foreach ( var obj in _objects.Where( x => x.IsValid() ) ) obj.Destroy();
			nav.IsEnabled = enabled; nav.CustomBounds = custom; nav.Bounds = bounds;
			GameObject.Destroy();
		}
	}
}

public sealed partial class VBSoldierController
{
	internal bool CoverTestSheltered => _hasCover;
	internal bool CoverTestPeek => _coverHasPeek;
	internal Vector3 CoverTestPosition => _coverPosition;
	internal bool CoverTestCanReserve( Vector3 point ) => PositionAvailable( point, Agent.Radius * 4f );
	internal bool CoverTestStand() => SetCrouching( false );
	internal bool CoverTestFlanking => _tacticalMove && _supportRoute;
	internal bool CoverTestSearching => _searching;
}
