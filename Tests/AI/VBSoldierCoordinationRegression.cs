using System;
using System.Threading.Tasks;
using Sandbox;

// Compile only in an isolated test project. No human pawn or camera is controlled.
public sealed class VBSoldierCoordinationRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	[ConCmd( "vb_soldier_coordination_regression" )]
	public static void Run()
	{
		if ( Networking.IsHost && Game.ActiveScene is not null )
			_ = new GameObject( true, "Soldier coordination regression" ).AddComponent<VBSoldierCoordinationRegression>().Verify();
	}
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Soldier coordination {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private GameObject Box( string name, Vector3 position, Vector3 size )
	{
		var obj = new GameObject( true, name ); _objects.Add( obj ); obj.WorldPosition = position;
		var collider = obj.AddComponent<BoxCollider>(); collider.Static = true; collider.Scale = size;
		return obj;
	}
	private VBSoldierController Soldier( Vector3 position )
	{
		var obj = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone(
			new CloneConfig { StartEnabled = false, Transform = new Transform( position ) } );
		_objects.Add( obj );
		var soldier = obj.GetComponent<VBSoldierController>( true );
		soldier.UseCover = soldier.UseTacticalPositions = soldier.AvoidVisibleHazards = false;
		soldier.MoveSpeed = soldier.PatrolRadius = soldier.AimReactionTime = 0f;
		soldier.GrenadeCount = 0;
		var perception = obj.GetComponent<VBAiPerceptionComponent>( true );
		perception.FieldOfView = 360f; perception.ThinkInterval = 0.05f; perception.AcquisitionDelay = 0f;
		obj.Enabled = true;
		return soldier;
	}
	private async Task Verify()
	{
		var coordinator = Scene.GetSystem<VBSoldierCoordination>();
		int oldCap = coordinator.MaxShootersPerPlayer;
		float oldTurn = coordinator.FireTurnDuration;
		var nav = Scene.NavMesh;
		bool oldNavEnabled = nav.IsEnabled, oldCustomBounds = nav.CustomBounds;
		var oldBounds = nav.Bounds;
		try
		{
			coordinator.MaxShootersPerPlayer = 1; coordinator.FireTurnDuration = 0.6f;
			var origin = new Vector3( 52000f, 52000f, 4000f );
			Box( "Coordination floor", origin + new Vector3( 300f, 2000f, -16f ), new Vector3( 3000f, 7000f, 32f ) );
			var squads = new List<VBSoldierController>();
			for ( int group = 0; group < 2; group++ )
			{
				var center = origin + new Vector3( 0f, group * 4000f, 0f );
				var victim = Box( $"Player budget {group}", center + Vector3.Forward * 600f + Vector3.Up * 36f, new Vector3( 24f, 24f, 72f ) );
				victim.GetComponent<BoxCollider>().Center = Vector3.Up * 36f;
				victim.WorldPosition -= Vector3.Up * 36f;
				victim.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
				var health = victim.AddComponent<VBHealthComponent>(); health.StartingHardMaxHealth = 100000f; health.CanBeDowned = false;
				for ( int i = 0; i < 3; i++ ) squads.Add( Soldier( center + new Vector3( 0f, (i - 1) * 160f, 2f ) ) );
			}
			await Task.DelaySeconds( 0.4f );
			Check( squads.All( s => s.Weapon.IsValid() ), "six native inventories equip firearms" );
			var clips = squads.Select( s => s.Weapon.Clip1 ).ToArray();
			var fired = new HashSet<VBSoldierController>();
			bool capHeld = true, independent = false;
			TimeSince sample = 0f;
			while ( sample < 5f )
			{
				await Task.Frame();
				int first = 0, second = 0;
				for ( int i = 0; i < squads.Count; i++ )
				{
					int clip = squads[i].Weapon.Clip1;
					if ( clip < clips[i] ) { fired.Add( squads[i] ); if ( i < 3 ) first++; else second++; }
					clips[i] = clip;
				}
				capHeld &= first <= 1 && second <= 1;
				independent |= first > 0 && second > 0;
			}
			Check( capHeld, "real ammunition consumption respects one shooter per player on every sampled frame" );
			Check( independent, "both player budgets permit fire in the same frame" );
			Check( fired.Count == 6, "all six native weapons receive firing turns" );
			coordinator.MaxShootersPerPlayer = 0;
			clips = squads.Select( s => s.Weapon.Clip1 ).ToArray();
			await Task.DelaySeconds( 0.5f );
			Check( squads.Select( ( s, i ) => s.Weapon.Clip1 == clips[i] ).All( x => x ), "a zero budget stops actual ranged fire" );

			var mover = squads[0];
			foreach ( var soldier in squads ) soldier.Enabled = false;
			mover.MoveSpeed = 120f;
			var start = mover.WorldPosition;
			var motion = mover.GetComponent<VBNpcLocomotion>();
			var look = start + Vector3.Forward * 700f + Vector3.Up * 40f;
			motion.WalkDirectlyTo( start + Vector3.Right * 160f );
			sample = 0f;
			while ( sample < 0.9f ) { await Task.Frame(); mover.CoordinationTestPresentation( look ); }
			Check( Vector3.DistanceBetween( start, mover.WorldPosition ) > 30f
				&& MathF.Abs( Vector3.Dot( mover.WorldRotation.Forward, motion.Velocity.Normal ) ) < 0.35f,
				"collision-resolved lateral motion keeps the body facing its aim" );
			Check( MathF.Abs( mover.BodyRenderer.GetFloat( "move_y" ) ) > 20f, "Human receives lateral locomotion in body space" );
			motion.CancelDirectMove();
			start = mover.WorldPosition;
			motion.WalkDirectlyTo( start - Vector3.Forward * 120f );
			sample = 0f;
			while ( sample < 0.9f ) { await Task.Frame(); mover.CoordinationTestPresentation( look ); }
			Check( Vector3.DistanceBetween( start, mover.WorldPosition ) > 20f
				&& Vector3.Dot( mover.WorldRotation.Forward, motion.Velocity.Normal ) < -0.7f,
				"backpedalling preserves aim and uses reduced combat speed" );
			Check( mover.BodyRenderer.GetFloat( "move_x" ) < -20f, "Human receives backwards locomotion in body space" );
			motion.CancelDirectMove();
			nav.CustomBounds = true;
			nav.Bounds = new BBox( origin + new Vector3( -1200f, -1200f, -64f ), origin + new Vector3( 1200f, 1200f, 150f ) );
			nav.IsEnabled = true;
			Check( await nav.Generate( Scene.PhysicsWorld ), "support-position fixture generates a native navigation mesh" );
			mover = Soldier( origin + new Vector3( 100f, -160f, 2f ) );
			mover.UseDynamicTraversal = false;
			mover.UseTacticalPositions = true;
			mover.MoveSpeed = 160f;
			await Task.DelaySeconds( 0.3f );
			start = mover.WorldPosition;
			int supportClip = mover.Weapon.Clip1;
			bool sawSupportRoute = false;
			sample = 0f;
			while ( sample < 3f )
			{
				await Task.Frame();
				sawSupportRoute |= mover.State is VBSoldierState.Reposition or VBSoldierState.Flank;
			}
			Check( sawSupportRoute && Vector3.DistanceBetween( start, mover.WorldPosition ) > 40f
				&& mover.Weapon.Clip1 == supportClip, "a soldier without a firing slot physically repositions without shooting" );
			Log.Info( $"Support diagnostic: route={sawSupportRoute} from={start} to={mover.WorldPosition} target={mover.GetComponent<VBAiPerceptionComponent>().LastKnownPosition} clip={supportClip}/{mover.Weapon.Clip1} {mover.CoordinationTestDiagnostic()}" );
			mover.UseTacticalPositions = false;
			mover.Enabled = false;
			nav.IsEnabled = false;
			mover.Enabled = true;
			Check( coordinator.CanThrowGrenade( mover ), "an idle squad permits a grenade windup" );
			mover.CoordinationTestWindup();
			Check( !coordinator.CanThrowGrenade( squads[1] ), "another soldier cannot begin a concurrent windup" );
			mover.Enabled = false;
			Check( coordinator.CanThrowGrenade( squads[1] ), "cancelling the throw releases the windup exclusion" );
			var projectileObject = new GameObject( false, "Coordinated grenade" ); _objects.Add( projectileObject );
			var projectile = projectileObject.AddComponent<VBNpcGrenade>();
			projectile.SoldierCoordinated = true; projectile.FuseTime = 100f;
			projectileObject.Enabled = true;
			Check( !coordinator.CanThrowGrenade( squads[1] ), "a live coordinated grenade blocks another even without a living thrower" );
			projectileObject.Destroy();
			await Task.Frame(); // Native scene removal is deferred until the frame boundary.
			Check( coordinator.CanThrowGrenade( squads[1] ), "removing the live grenade frees the squad" );
			Log.Info( $"Soldier coordination FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception ex ) { Log.Error( $"Soldier coordination EXCEPTION: {ex}" ); }
		finally
		{
			coordinator.MaxShootersPerPlayer = oldCap; coordinator.FireTurnDuration = oldTurn;
			nav.IsEnabled = oldNavEnabled; nav.CustomBounds = oldCustomBounds; nav.Bounds = oldBounds;
			foreach ( var obj in _objects.Where( x => x.IsValid() ) ) obj.Destroy();
			GameObject.Destroy();
		}
	}
}

// Test-only entry points; never copied into the distributed game.
public sealed partial class VBSoldierController
{
	internal void CoordinationTestPresentation( Vector3 point ) { AimPosition = point; FaceCombatAim(); UpdateAnimation(); }
	internal void CoordinationTestWindup() { State = VBSoldierState.ThrowGrenade; }
	internal string CoordinationTestDiagnostic() => $"state={State} tactics={UseTacticalPositions} speed={MoveSpeed} cover={_hasCover} pending={_hasTraversalGoal} ammo={Weapon?.HasPrimaryAmmo()} reload={Weapon?.IsReloading} seek={_seekCoverUntil - Time.Now} query={_nextTacticalQuery - Time.Now} aimWait={(float)FireReady} visible={Perception.TargetVisible}";
}
