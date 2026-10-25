using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox;

// Run in an isolated native scene with navigation initially disabled.
public sealed class VBNpcCrowdRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	[ConCmd( "vb_npc_crowd_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || Game.ActiveScene.NavMesh.IsEnabled )
		{ Log.Warning( "Crowd regression requires an isolated scene with navigation disabled." ); return; }
		_ = new GameObject( true, "Crowd regression" ).AddComponent<VBNpcCrowdRegression>().Verify();
	}
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Crowd {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private GameObject Box( Vector3 position, Vector3 size, string tag = "solid" )
	{
		var obj = new GameObject( true, "Crowd geometry" ); _objects.Add( obj );
		obj.WorldPosition = position; obj.Tags.Add( tag );
		var collider = obj.AddComponent<BoxCollider>(); collider.Static = true; collider.Scale = size;
		return obj;
	}
	private GameObject Actor( Vector3 position, bool zombie )
	{
		var kind = zombie ? "zombie_classic" : "cartel_soldier";
		var obj = GameObject.GetPrefab( $"creatures/{kind}/{kind}.prefab" ).Clone(
			new CloneConfig { StartEnabled = false, Transform = new Transform( position ) } );
		_objects.Add( obj );
		var soldier = obj.GetComponent<VBSoldierController>( true ); if ( soldier.IsValid() ) soldier.Enabled = false;
		var undead = obj.GetComponent<VBZombieController>( true ); if ( undead.IsValid() ) undead.Enabled = false;
		obj.GetComponent<VBAiPerceptionComponent>( true ).Enabled = false;
		obj.Enabled = true;
		return obj;
	}
	private async Task Verify()
	{
		var nav = Scene.NavMesh; bool custom = nav.CustomBounds; var bounds = nav.Bounds;
		try
		{
			var origin = new Vector3( 44000f, 44000f, 4000f );
			Box( origin - Vector3.Up * 16f, new Vector3( 1600f, 1600f, 32f ) );
			var corpseShape = Box( origin + new Vector3( -400f, 0f, 24f ), new Vector3( 32f, 64f, 40f ), "npc_ragdoll" );
			foreach ( bool zombie in new[] { false, true } )
			{
				var actor = Actor( origin + new Vector3( -520f, 0f, 2f ), zombie );
				await Task.DelaySeconds( 0.2f );
				var start = origin + new Vector3( -480f, 0f, 30f ); var end = start + Vector3.Forward * 160f;
				var ordinary = Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( actor ).Run();
				var filtered = Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( actor ).WithCollisionRules( actor.Tags ).Run();
				Check( ordinary.Hit && !filtered.Hit, $"{(zombie ? "zombie" : "soldier")} ignores corpse physics but the corpse still exists" );
				actor.Destroy();
			}
			corpseShape.Destroy();

			// A 112-unit doorway: agents can pass with room to sidestep, not by phasing.
			Box( origin + new Vector3( 0f, 228f, 90f ), new Vector3( 32f, 344f, 180f ) );
			Box( origin + new Vector3( 0f, -228f, 90f ), new Vector3( 32f, 344f, 180f ) );
			nav.CustomBounds = true; nav.Bounds = new BBox( origin - new Vector3( 700f, 700f, 40f ), origin + new Vector3( 700f, 700f, 220f ) );
			nav.IsEnabled = true; await Task.DelaySeconds( 0.2f );
			Check( await nav.Generate( Scene.PhysicsWorld ), "native navigation generates the doorway" );
			var blocker = Actor( origin + new Vector3( 0f, 12f, 2f ), false );
			var mover = Actor( origin + new Vector3( -260f, 0f, 2f ), true );
			await Task.DelaySeconds( 0.4f );
			var motion = mover.GetComponent<VBNpcLocomotion>(); motion.Agent.MaxSpeed = 110f;
			var goal = origin + new Vector3( 260f, 0f, 2f );
			Check( VBNpcNavigation.TryWalkPath( Scene, motion.Agent, mover.WorldPosition, goal, out var path ), "route through doorway exists" );
			motion.Agent.SetPath( path );
			float correction = 0f; float closest = float.PositiveInfinity; TimeSince elapsed = 0f;
			while ( elapsed < 10f && mover.WorldPosition.x < goal.x - 24f )
			{
				await Task.Frame();
				correction = MathF.Max( correction, (motion.Agent.Velocity - motion.Agent.WishVelocity).WithZ( 0f ).Length );
				closest = MathF.Min( closest, (blocker.WorldPosition - mover.WorldPosition).WithZ( 0f ).Length );
			}
			Check( correction > 5f, "native crowd solver contributes an avoidance correction" );
			Check( mover.WorldPosition.x > origin.x + 180f, $"zombie passes stationary soldier in doorway (relative {mover.WorldPosition - origin})" );
			Check( closest >= 28f, "living bodies remain solid while taking the detour" );
			mover.Destroy(); blocker.Destroy();

			var west = Actor( origin + new Vector3( -240f, -12f, 2f ), false );
			var east = Actor( origin + new Vector3( 240f, 12f, 2f ), true );
			await Task.DelaySeconds( 0.3f );
			var westMotion = west.GetComponent<VBNpcLocomotion>(); var eastMotion = east.GetComponent<VBNpcLocomotion>();
			westMotion.Agent.MaxSpeed = eastMotion.Agent.MaxSpeed = 110f;
			Check( VBNpcNavigation.TryWalkPath( Scene, westMotion.Agent, west.WorldPosition, origin + new Vector3( 240f, -12f, 2f ), out var westPath )
				&& VBNpcNavigation.TryWalkPath( Scene, eastMotion.Agent, east.WorldPosition, origin + new Vector3( -240f, 12f, 2f ), out _ ), "both directions have complete native routes" );
			VBNpcNavigation.TryWalkPath( Scene, eastMotion.Agent, east.WorldPosition, origin + new Vector3( -240f, 12f, 2f ), out var eastPath );
			westMotion.Agent.SetPath( westPath ); eastMotion.Agent.SetPath( eastPath );
			await Task.DelaySeconds( 10f );
			Check( west.WorldPosition.x > origin.x + 160f && east.WorldPosition.x < origin.x - 160f,
				$"soldier and zombie cross in opposite directions (west {west.WorldPosition - origin}, east {east.WorldPosition - origin})" );
			west.Destroy(); east.Destroy();

			var fallen = Actor( origin + new Vector3( -300f, -250f, 2f ), false );
			await Task.DelaySeconds( 0.2f );
			fallen.GetComponent<VBNpcRagdollController>().BeginRecoverableRagdoll( Vector3.Zero );
			Check( !fallen.GetComponent<NavMeshAgent>( true ).Enabled, "knocked-down NPC leaves the crowd simulation" );
			await Task.DelaySeconds( 4f );
			Check( fallen.GetComponent<VBNpcRagdollController>().State == VBNpcRagdollState.Animated
				&& fallen.GetComponent<NavMeshAgent>( true ).Enabled, "recovery restores the native navigation agent" );
		}
		catch ( Exception exception ) { _failures++; Log.Error( exception ); }
		finally
		{
			nav.IsEnabled = false; nav.CustomBounds = custom; nav.Bounds = bounds;
			foreach ( var obj in _objects ) if ( obj.IsValid() ) obj.Destroy();
			Log.Info( $"Crowd FINISHED: {_failures} failure(s)." );
			GameObject.Destroy();
		}
	}
}
