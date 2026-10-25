using System;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Navigation;

// Run in the isolated appearance-preview scene (its navigation must be disabled).
public sealed class VBSoldierNavigationRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	[ConCmd( "vb_soldier_navigation_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || Game.ActiveScene.NavMesh.IsEnabled )
		{
			Log.Info( "Navigation regression requires an isolated scene with navigation disabled." );
			return;
		}
		_ = new GameObject( true, "Soldier navigation regression" ).AddComponent<VBSoldierNavigationRegression>().Verify();
	}
	private void Check( bool pass, string message )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Soldier navigation {(pass ? "PASS" : "FAIL")}: {message}" );
	}
	private GameObject Box( string name, Vector3 point, Vector3 size )
	{
		var obj = new GameObject( true, name ); _objects.Add( obj ); obj.WorldPosition = point;
		var collider = obj.AddComponent<BoxCollider>(); collider.Scale = size; collider.Static = true;
		return obj;
	}
	private async Task Verify()
	{
		var nav = Scene.NavMesh;
		bool custom = nav.CustomBounds;
		var oldBounds = nav.Bounds;
		try
		{
			var origin = new Vector3( 30000f, 30000f, 4000f );
			Box( "Lower landing", origin + Vector3.Down * 16f, new Vector3( 400f, 400f, 32f ) );
			Box( "Upper landing", origin + new Vector3( 300f, 0f, 144f ), new Vector3( 320f, 400f, 32f ) );
			var linkObject = new GameObject( true, "Authored climb link" ); _objects.Add( linkObject );
			linkObject.WorldPosition = origin;
			var link = linkObject.AddComponent<NavMeshLink>();
			link.IsBiDirectional = true;
			link.ConnectionRadius = 48f;
			link.LocalStartPosition = new Vector3( 100f, 0f, 0f );
			link.LocalEndPosition = new Vector3( 220f, 0f, 160f );
			var rise = new GameObject( true, "Clear ledge waypoint" ); _objects.Add( rise ); rise.WorldPosition = origin + new Vector3( 100f, 0f, 160f );
			var climb = linkObject.AddComponent<VBNpcTraversalLink>(); climb.Speed = 150f; climb.Waypoints = new[] { rise };
			nav.CustomBounds = true; nav.Bounds = new BBox( origin + new Vector3( -240f, -240f, -64f ), origin + new Vector3( 500f, 240f, 300f ) );
			nav.IsEnabled = true;
			// Colliders created this frame enter the native physics world at the frame boundary.
			await Task.DelaySeconds( 0.2f );
			Log.Info( $"Soldier navigation settings: static={nav.IncludeStaticBodies} keyframed={nav.IncludeKeyframedBodies} included={nav.IncludedBodies} excluded={nav.ExcludedBodies} bounds={nav.Bounds} floor={Scene.Trace.Ray( origin + Vector3.Up * 80f, origin + Vector3.Down * 20f ).Run().Hit}" );
			Check( await nav.Generate( Scene.PhysicsWorld ), "native navmesh generates both landings" );
			var actor = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone( origin ); _objects.Add( actor );
			var soldier = actor.GetComponent<VBSoldierController>();
			soldier.PatrolRadius = 0f; soldier.PatrolSpeed = 150f;
			actor.GetComponent<VBAiPerceptionComponent>().Enabled = false;
			await Task.DelaySeconds( 0.3f );
			var agent = actor.GetComponent<NavMeshAgent>();
			var traversal = actor.GetComponent<VBNavLinkTraversal>();
			var end = origin + new Vector3( 300f, 0f, 160f );
			var path = nav.CalculatePath( new CalculatePathRequest { Start = actor.WorldPosition, Target = end, Agent = agent } );
			Log.Info( $"Soldier navigation route={path.Status} start={nav.GetClosestPoint( origin )} end={nav.GetClosestPoint( end )} linkStart={link.WorldStartPositionOnNavmesh} linkEnd={link.WorldEndPositionOnNavmesh}" );
			Check( path.Status == NavMeshPathStatus.Complete, "engine finds a complete route through the authored link" );
			if ( path.Status != NavMeshPathStatus.Complete ) return;
			agent.SetPath( path );
			bool sawClimb = false;
			TimeSince started = 0f;
			while ( started < 6f && Vector3.DistanceBetween( actor.WorldPosition, end ) > 28f )
			{
				sawClimb |= traversal.IsClimbing;
				await Task.Frame();
			}
			Check( sawClimb, "link dispatches the explicit climb mode" );
			Check( Vector3.DistanceBetween( actor.WorldPosition, end ) < 28f && !traversal.IsClimbing, "climb reaches the upper landing and releases traversal" );
			path = nav.CalculatePath( new CalculatePathRequest { Start = actor.WorldPosition, Target = origin, Agent = agent } );
			Check( path.Status == NavMeshPathStatus.Complete, "native link supports the return route" );
			agent.SetPath( path ); started = 0f;
			while ( started < 6f && Vector3.DistanceBetween( actor.WorldPosition, origin ) > 28f ) await Task.Frame();
			Check( Vector3.DistanceBetween( actor.WorldPosition, origin ) < 28f && !traversal.IsClimbing, "descent reverses the authored waypoint without a ragdoll drop" );
			Box( "Late obstruction", origin + new Vector3( 100f, 0f, 100f ), new Vector3( 60f, 60f, 30f ) );
			path = nav.CalculatePath( new CalculatePathRequest { Start = actor.WorldPosition, Target = end, Agent = agent } );
			agent.SetPath( path );
			await Task.DelaySeconds( 2f );
			Check( traversal.LastFailure is not null && !traversal.IsClimbing && actor.WorldPosition.z < origin.z + 30f,
				"body obstruction rejects climbing before leaving the lower landing" );
			var allyObject = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone( origin + Vector3.Right * 55f ); _objects.Add( allyObject );
			var ally = allyObject.GetComponent<VBSoldierController>();
			ally.UseCover = false; ally.UseTacticalPositions = false; ally.PatrolRadius = 0f; ally.GrenadeMinRange = 5000f;
			soldier.UseCover = false; soldier.GrenadeMinRange = 5000f; soldier.PreferredRange = 450f;
			soldier.AimReactionTime = 0f; ally.AimReactionTime = 0f;
			actor.GetComponent<VBAiPerceptionComponent>( includeDisabled: true ).Enabled = true;
			await Task.DelaySeconds( 0.2f );
			soldier.Weapon.SetNextPrimaryFire( 30f ); ally.Weapon.SetNextPrimaryFire( 30f );
			var threat = Box( "Tactical threat", origin + Vector3.Forward * 450f + Vector3.Up * 36f, new Vector3( 24f, 24f, 72f ) );
			threat.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
			threat.AddComponent<VBHealthComponent>().StartingHardMaxHealth = 1000f;
			var initial = actor.WorldPosition;
			bool sawReposition = false; started = 0f;
			while ( started < 4f )
			{
				sawReposition |= soldier.State is VBSoldierState.Reposition or VBSoldierState.Flank;
				await Task.Frame();
			}
			Check( sawReposition && Vector3.DistanceBetween( initial, actor.WorldPosition ) > 40f,
				"shared visible threat triggers a reachable tactical move away from the ally" );
			Log.Info( $"Soldier navigation FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception ex ) { Log.Error( $"Soldier navigation EXCEPTION: {ex}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( o => o.IsValid() ) ) obj.Destroy();
			nav.IsEnabled = false; nav.CustomBounds = custom; nav.Bounds = oldBounds;
			if ( this.IsValid() ) GameObject.Destroy();
		}
	}
}
