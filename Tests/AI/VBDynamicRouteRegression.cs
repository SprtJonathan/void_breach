using System;
using System.Threading.Tasks;
using Sandbox;

// Isolated native scene only. Exercises controller decisions and real movement, without authored links.
public sealed class VBDynamicRouteRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	[ConCmd( "vb_dynamic_route_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || Game.ActiveScene.NavMesh.IsEnabled ) return;
		_ = new GameObject( true, "Dynamic route regression" ).AddComponent<VBDynamicRouteRegression>().Verify();
	}
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Dynamic route {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private GameObject Box( Vector3 position, Vector3 size )
	{
		var obj = new GameObject( true, "Dynamic route geometry" ); _objects.Add( obj ); obj.WorldPosition = position;
		var collider = obj.AddComponent<BoxCollider>(); collider.Static = true; collider.Scale = size;
		return obj;
	}
	private async Task VerifyCase( string label, Vector3 obstacleSize, Vector3 obstacleOffset, Vector3 goalOffset, bool expectClimb )
	{
		var origin = new Vector3( 42000f, 42000f, 4000f );
		Box( origin - Vector3.Up * 16f, new Vector3( 2400f, 2400f, 32f ) );
		Box( origin + obstacleOffset, obstacleSize );
		var nav = Scene.NavMesh;
		nav.CustomBounds = true; nav.Bounds = new BBox( origin - new Vector3( 800f, 800f, 40f ), origin + new Vector3( 800f, 800f, 300f ) );
		nav.IsEnabled = true; await Task.DelaySeconds( 0.1f );
		Check( await nav.Generate( Scene.PhysicsWorld ), $"{label}: native navigation builds" );
		var actor = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" )
			.Clone( new CloneConfig { StartEnabled = false, Transform = new Transform( origin ) } );
		_objects.Add( actor );
		var brain = actor.GetComponent<VBSoldierController>( true );
		brain.UseCover = brain.UseTacticalPositions = brain.AvoidVisibleHazards = false;
		brain.PatrolRadius = 0f; brain.GrenadeCount = 0;
		actor.Enabled = true; await Task.DelaySeconds( 0.3f );
		var goal = origin + goalOffset;
		((IVBAiSoundListener)actor.GetComponent<VBAiPerceptionComponent>()).OnSoundStimulus(
			new VBAiSoundStimulus( goal, 2000f, null, VBAiSoundKind.Generic ) );
		bool climbed = false; float largestStep = 0f; var previous = actor.WorldPosition;
		TimeSince elapsed = 0f;
		while ( elapsed < 8f && (Vector3.DistanceBetween( actor.WorldPosition, goal ) > 48f
			|| brain.IsDynamicallyTraversing || !actor.GetComponent<VBNpcLocomotion>().IsGrounded) )
		{
			climbed |= brain.IsDynamicallyTraversing;
			largestStep = MathF.Max( largestStep, Vector3.DistanceBetween( previous, actor.WorldPosition ) );
			previous = actor.WorldPosition; await Task.Frame();
		}
		Check( Vector3.DistanceBetween( actor.WorldPosition, goal ) <= 48f, $"{label}: physically reaches the perceived objective" );
		Check( climbed == expectClimb, $"{label}: chooses {(expectClimb ? "climbing" : "walking")}" );
		Check( largestStep < 20f, $"{label}: no root teleport" );
		Log.Info( $"Dynamic route diagnostic: {label} position={actor.WorldPosition - origin} state={brain.State} climbed={climbed}" );
		foreach ( var obj in _objects.Where( o => o.IsValid() ) ) obj.Destroy();
		_objects.Clear(); nav.IsEnabled = false;
	}
	private async Task Verify()
	{
		var nav = Scene.NavMesh; bool custom = nav.CustomBounds; var bounds = nav.Bounds;
		try
		{
			await VerifyCase( "raised platform", new Vector3( 200f, 180f, 96f ), new Vector3( 160f, 0f, 48f ), new Vector3( 200f, 0f, 96f ), true );
			await VerifyCase( "short detour", new Vector3( 32f, 80f, 48f ), new Vector3( 100f, 0f, 24f ), new Vector3( 220f, 0f, 0f ), false );
			await VerifyCase( "long detour", new Vector3( 32f, 800f, 48f ), new Vector3( 100f, 0f, 24f ), new Vector3( 220f, 0f, 0f ), true );
			Log.Info( $"Dynamic route FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception exception ) { Log.Error( $"Dynamic route EXCEPTION: {exception}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( o => o.IsValid() ) ) obj.Destroy();
			nav.IsEnabled = false; nav.CustomBounds = custom; nav.Bounds = bounds;
			if ( this.IsValid() ) GameObject.Destroy();
		}
	}
}
