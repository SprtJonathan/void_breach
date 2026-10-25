using System;
using System.Threading.Tasks;
using Sandbox;

// Run separately in an isolated scene. Loads the compiled map geometry used by
// basic_example and soldier_test, not the user's active Play session.
public sealed class VBSoldierMapEngagementRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Soldier map engagement {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private bool ClearBody( Vector3 position ) => !Scene.Trace.Box(
		new BBox( new Vector3( -18f, -18f, 4f ), new Vector3( 18f, 18f, 72f ) ), position, position ).IgnoreDynamic().Run().StartedSolid;
	private async Task VerifyMap( string path )
	{
		var root = new GameObject( true, "Map engagement geometry" ); _objects.Add( root );
		var map = root.AddComponent<MapInstance>(); map.UseMapFromLaunch = false; map.MapName = path;
		TimeSince load = 0f;
		while ( !map.IsLoaded && load < 20f ) await Task.Frame();
		Check( map.IsLoaded, $"{path}: actual compiled map loads" );
		if ( !map.IsLoaded ) return;
		var nav = Scene.NavMesh; nav.CustomBounds = true; nav.Bounds = map.Bounds; nav.IsEnabled = true;
		await Task.DelaySeconds( 0.3f );
		Check( await nav.Generate( Scene.PhysicsWorld ), $"{path}: native map navigation generates" );
		Vector3 from = default, to = default;
		bool indoor = path.Contains( "interior" ); // basic_example's compiled map is an outdoor test map.
		bool found = false; float roofHeight = 0f;
		for ( int i = 0; i < 240 && !found; i++ )
		{
			var candidate = nav.GetRandomPoint();
			if ( !candidate.HasValue || !ClearBody( candidate.Value ) ) continue;
			var roof = Scene.Trace.Ray( candidate.Value + Vector3.Up * 74f, candidate.Value + Vector3.Up * 200f ).IgnoreDynamic().Run();
			if ( indoor && (!roof.Hit || roof.StartedSolid || roof.Normal.z > -0.6f) ) continue;
			for ( int angle = 0; angle < 12; angle++ )
			{
				var end = nav.GetClosestPoint( candidate.Value + Rotation.FromYaw( angle * 30f ).Forward * 420f, 64f );
				if ( !end.HasValue || MathF.Abs( candidate.Value.z - end.Value.z ) > 12f
					|| Vector3.DistanceBetween( candidate.Value, end.Value ) < 340f || !ClearBody( end.Value ) ) continue;
				var sight = Scene.Trace.Ray( candidate.Value + Vector3.Up * 60f, end.Value + Vector3.Up * 40f ).IgnoreDynamic().Run();
				if ( sight.Hit ) continue;
				from = candidate.Value; to = end.Value; found = true; roofHeight = roof.Hit ? roof.EndPosition.z - from.z : 0f; break;
			}
		}
		Check( found, $"{path}: finds a real firing lane{(indoor ? " below a 200-unit ceiling" : " on the outdoor map")}" );
		if ( !found ) return;
		var target = new GameObject( true, "Map stationary player" ); _objects.Add( target ); target.WorldPosition = to;
		target.Tags.Add( "player" ); target.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
		var hp = target.AddComponent<VBHealthComponent>(); hp.StartingHardMaxHealth = 100000f; hp.CanBeDowned = false;
		var body = target.AddComponent<BoxCollider>(); body.Static = true; body.Center = Vector3.Up * 36f; body.Scale = new Vector3( 28f, 28f, 72f );
		var actor = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone(
			new CloneConfig { StartEnabled = false, Transform = new Transform( from + Vector3.Up * 2f, Rotation.LookAt( (to - from).WithZ( 0f ) ) ) } );
		_objects.Add( actor ); actor.GetComponent<VBHealthComponent>( true ).GodMode = true;
		var soldier = actor.GetComponent<VBSoldierController>( true ); soldier.PatrolRadius = 0f;
		actor.Enabled = true;
		await Task.DelaySeconds( 0.3f );
		int previousClip = soldier.Weapon.Clip1; bool crouchedRound = false, grenade = false;
		float lastShot = 0f, maxSilence = 0f; int shots = 0;
		TimeSince engagement = 0f;
		while ( engagement < 15f )
		{
			await Task.Frame();
			int clip = soldier.Weapon.Clip1;
			if ( clip < previousClip ) { shots += previousClip - clip; lastShot = engagement; crouchedRound |= soldier.FiringStanceAccuracyMultiplier < 1f; }
			previousClip = clip;
			if ( engagement > 3f ) maxSilence = MathF.Max( maxSilence, engagement - lastShot );
			grenade |= Scene.GetAll<VBNpcGrenade>().Any( x => x.SoldierCoordinated && x.Thrower == actor );
		}
		Check( shots >= 8 && maxSilence < 6f, $"{path}: sustained real fire in the actual map geometry" );
		Check( crouchedRound, $"{path}: crouched firing remains accessible with the actual geometry" );
		Log.Info( $"Map engagement metrics: {path} from={from} to={to} ceiling={roofHeight:F0} shots={shots} maxSilence={maxSilence:F2}s pressureGrenade={grenade}" );
	}
	public async Task Verify()
	{
		var nav = Scene.NavMesh; bool oldEnabled = nav.IsEnabled, oldCustom = nav.CustomBounds; var oldBounds = nav.Bounds;
		try
		{
			foreach ( var map in Scene.GetAll<MapInstance>().ToArray() ) map.GameObject.Destroy();
			foreach ( string path in new[] { "maps/test_map.vmap", "maps/interior_testmap.vmap" } )
			{
				await VerifyMap( path );
				foreach ( var grenade in Scene.GetAll<VBNpcGrenade>().Where( x => x.SoldierCoordinated ).ToArray() ) grenade.GameObject.Destroy();
				foreach ( var obj in _objects.Where( x => x.IsValid() ) ) obj.Destroy();
				_objects.Clear(); nav.IsEnabled = false; await Task.DelaySeconds( 0.3f );
			}
			Log.Info( $"Soldier map engagement FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception ex ) { Log.Error( $"Soldier map engagement EXCEPTION: {ex}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( x => x.IsValid() ) ) obj.Destroy();
			nav.IsEnabled = oldEnabled; nav.CustomBounds = oldCustom; nav.Bounds = oldBounds; GameObject.Destroy();
		}
	}
}
