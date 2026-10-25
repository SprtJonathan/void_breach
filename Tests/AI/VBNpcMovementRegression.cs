using System;
using System.Threading.Tasks;
using Sandbox;

// Host command in an isolated scene whose navigation is initially disabled.
public sealed class VBNpcMovementRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	[ConCmd( "vb_npc_movement_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || Game.ActiveScene.NavMesh.IsEnabled )
		{ Log.Warning( "NPC movement regression requires an isolated scene with navigation disabled." ); return; }
		_ = new GameObject( true, "NPC movement regression" ).AddComponent<VBNpcMovementRegression>().Verify();
	}
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"NPC movement {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private GameObject Box( Vector3 position, Vector3 size )
	{
		var obj = new GameObject( true, "NPC test geometry" ); _objects.Add( obj ); obj.WorldPosition = position;
		var collider = obj.AddComponent<BoxCollider>(); collider.Static = true; collider.Scale = size;
		return obj;
	}
	private GameObject Soldier( Vector3 position )
	{
		var obj = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" )
			.Clone( new CloneConfig { StartEnabled = false, Transform = new Transform( position ) } );
		_objects.Add( obj );
		obj.GetComponent<VBSoldierController>( true ).Enabled = false;
		obj.GetComponent<VBAiPerceptionComponent>( true ).Enabled = false;
		obj.Enabled = true;
		return obj;
	}
	private async Task Verify()
	{
		var nav = Scene.NavMesh; bool custom = nav.CustomBounds; var bounds = nav.Bounds;
		try
		{
			var origin = new Vector3( 36000f, 36000f, 4000f );
			Box( origin - Vector3.Up * 16f, new Vector3( 2400f, 2400f, 32f ) );
			var platform = Box( origin + Vector3.Up * 758f, new Vector3( 200f, 200f, 20f ) );
			var actor = Soldier( origin + Vector3.Up * 770f );
			var motion = actor.GetComponent<VBNpcLocomotion>(); var health = actor.GetComponent<VBHealthComponent>();
			motion.Agent.MaxSpeed = 120f;
			await Task.DelaySeconds( 0.4f );
			var start = actor.WorldPosition;
			motion.WalkDirectlyTo( origin + new Vector3( 250f, 0f, 768f ) );
			float largestStep = 0f; var previous = start; TimeSince elapsed = 0f;
			while ( elapsed < 2f )
			{
				await Task.Frame();
				largestStep = MathF.Max( largestStep, Vector3.DistanceBetween( previous, actor.WorldPosition ) ); previous = actor.WorldPosition;
			}
			Check( actor.WorldPosition.z > origin.z + 766f && !health.IsDead && health.CurrentHealth == 80f,
				"soldier stays alive by refusing a lethal 768-unit ledge" );
			Check( actor.WorldPosition.x < origin.x + 100f && largestStep < 20f, "approach uses ordinary movement without an edge teleport" );
			motion.CancelDirectMove(); await Task.DelaySeconds( 0.6f );
			Check( motion.Velocity.WithZ( 0f ).Length < 3f, "ground friction stops residual sliding" );
			platform.Destroy();
			await Task.DelaySeconds( 2f );
			Check( health.IsDead, "an accidental fall from the same height really kills the soldier" );
			actor.Destroy();
			var lowPlatform = Box( origin + Vector3.Up * 170f, new Vector3( 200f, 200f, 20f ) );
			var jumper = Soldier( origin + Vector3.Up * 182f );
			var jumperMotion = jumper.GetComponent<VBNpcLocomotion>(); jumperMotion.Agent.MaxSpeed = 120f;
			await Task.DelaySeconds( 0.4f );
			jumperMotion.WalkDirectlyTo( origin + new Vector3( 250f, 0f, 180f ) );
			await Task.DelaySeconds( 3f );
			Check( jumper.WorldPosition.z < origin.z + 4f && jumper.GetComponent<VBHealthComponent>().CurrentHealth == 80f,
				"a soldier can step off a harmless 180-unit ledge and land without damage" );
			jumper.Destroy(); lowPlatform.Destroy();
			var walker = Soldier( origin ); // Exact floor placement also has to initialize grounded state.
			var walkerMotion = walker.GetComponent<VBNpcLocomotion>(); walkerMotion.Agent.MaxSpeed = 120f;
			await Task.DelaySeconds( 0.2f );
			Check( walkerMotion.IsGrounded, "an actor placed exactly on the floor initializes without horizontal unsticking" );
			walkerMotion.WalkDirectlyTo( origin + Vector3.Forward * 150f );
			await Task.DelaySeconds( 0.6f );
			Check( walkerMotion.Velocity.WithZ( 0f ).Length > 110f && walkerMotion.Velocity.WithZ( 0f ).Length <= 120.1f,
				"walking reaches its configured speed instead of losing each step to stop-speed friction" );
			walkerMotion.CancelDirectMove(); await Task.DelaySeconds( 0.6f );
			Check( walkerMotion.Velocity.WithZ( 0f ).Length < 3f, "normal walking still brakes after its goal is cancelled" );
			walker.Destroy();

			var wall = Box( origin + new Vector3( 0f, 0f, 90f ), new Vector3( 24f, 400f, 180f ) );
			nav.CustomBounds = true; nav.Bounds = new BBox( origin + new Vector3( -800f, -800f, -40f ), origin + new Vector3( 800f, 800f, 350f ) );
			nav.IsEnabled = true; await Task.DelaySeconds( 0.2f );
			Check( await nav.Generate( Scene.PhysicsWorld ), "test navigation includes the blocking wall" );
			var zombie = GameObject.GetPrefab( "creatures/zombie_classic/zombie_classic.prefab" ).Clone( origin + Vector3.Backward * 240f + Vector3.Up * 2f );
			_objects.Add( zombie );
			zombie.GetComponent<VBZombieController>().Enabled = false; zombie.GetComponent<VBAiPerceptionComponent>().Enabled = false;
			await Task.DelaySeconds( 0.2f );
			var zombieMotion = zombie.GetComponent<VBNpcLocomotion>(); zombieMotion.Agent.MaxSpeed = 105f;
			var destination = origin + Vector3.Forward * 240f;
			bool route = VBNpcNavigation.TryWalkPath( Scene, zombieMotion.Agent, zombie.WorldPosition, destination, out var path );
			Check( route, "a complete native route goes around the wall" );
			if ( route )
			{
				zombieMotion.Agent.SetPath( path ); elapsed = 0f; float lateral = 0f, nextDiagnostic = 1f;
				while ( elapsed < 12f && Vector3.DistanceBetween( zombie.WorldPosition, destination ) > 28f )
				{
					lateral = MathF.Max( lateral, MathF.Abs( zombie.WorldPosition.y - origin.y ) );
					if ( elapsed > nextDiagnostic )
					{
						nextDiagnostic += 2f;
						Log.Info( $"Zombie route diagnostic: relative={zombie.WorldPosition - origin} velocity={zombieMotion.Velocity} wish={zombieMotion.Agent.WishVelocity} grounded={zombieMotion.IsGrounded} navigating={zombieMotion.Agent.IsNavigating} ragdoll={zombie.GetComponent<VBNpcRagdollController>().State}" );
					}
					await Task.Frame();
				}
				Check( Vector3.DistanceBetween( zombie.WorldPosition, destination ) < 28f && lateral > 200f,
					"physical zombie follows the detour instead of pushing into the wall" );
			}
			zombie.Destroy(); wall.Destroy();
			Check( await nav.Generate( Scene.PhysicsWorld ), "removed wall is reflected in navigation" );
			var hunter = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" )
				.Clone( new CloneConfig { StartEnabled = false, Transform = new Transform( origin + Vector3.Backward * 240f + Vector3.Up * 2f ) } );
			_objects.Add( hunter );
			var brain = hunter.GetComponent<VBSoldierController>( true );
			brain.UseCover = brain.UseTacticalPositions = brain.AvoidVisibleHazards = false;
			brain.GrenadeCount = 0; brain.PatrolRadius = 0f; brain.AimReactionTime = 100f;
			hunter.GetComponent<VBAiPerceptionComponent>( true ).AcquisitionDelay = 0f;
			hunter.Enabled = true;
			var target = new GameObject( true, "Observed search target" ); _objects.Add( target );
			target.WorldPosition = origin + Vector3.Forward * 240f;
			target.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
			target.AddComponent<VBHealthComponent>().StartingHardMaxHealth = 1000f;
			var targetBody = target.AddComponent<BoxCollider>(); targetBody.Center = Vector3.Up * 36f; targetBody.Scale = new Vector3( 24f, 24f, 72f );
			await Task.DelaySeconds( 0.6f );
			var eyes = hunter.GetComponent<VBAiPerceptionComponent>();
			Check( eyes.TargetVisible && eyes.CurrentTarget == target, "soldier records an actual visual observation" );
			wall = Box( origin + new Vector3( 0f, 0f, 90f ), new Vector3( 24f, 400f, 180f ) );
			Check( await nav.Generate( Scene.PhysicsWorld ), "new cover has a complete native detour" );
			await Task.DelaySeconds( 0.4f );
			var remembered = eyes.LastKnownPosition;
			Check( !eyes.TargetVisible && brain.State == VBSoldierState.Support && brain.RemembersTarget( target ), "occlusion retains attention during the deliberate observation pause" );
			eyes.Enabled = false; // Isolate pursuit from reacquisition when rounding the corner.
			target.WorldPosition += Vector3.Left * 500f;
			elapsed = 0f; float nearestMemory = float.PositiveInfinity;
			while ( elapsed < brain.LostContactHoldTime + 12f )
			{
				nearestMemory = MathF.Min( nearestMemory, Vector3.DistanceBetween( hunter.WorldPosition, remembered ) );
				await Task.Frame();
			}
			Check( nearestMemory < 48f, "soldier reaches the last observed position around cover before searching randomly" );
			Check( eyes.LastKnownPosition == remembered, "hidden target movement does not rewrite visual memory" );
			hunter.Destroy(); target.Destroy(); wall.Destroy();
			Log.Info( $"NPC movement FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception exception ) { Log.Error( $"NPC movement EXCEPTION: {exception}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( o => o.IsValid() ) ) obj.Destroy();
			nav.IsEnabled = false; nav.CustomBounds = custom; nav.Bounds = bounds;
			if ( this.IsValid() ) GameObject.Destroy();
		}
	}
}
