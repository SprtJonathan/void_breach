using System;
using System.Threading.Tasks;
using Sandbox;

// Isolated host scene only. This fixture uses the shipped tempo, not accelerated test tuning.
public sealed class VBSoldierTempoRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private int _failures;
	private readonly Vector3 _origin = new( 60000f, 60000f, 4000f );
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Soldier tempo {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private GameObject Box( string name, Vector3 offset, Vector3 size, string tag = "map" )
	{
		var obj = new GameObject( true, name ); _objects.Add( obj ); obj.WorldPosition = _origin + offset; obj.Tags.Add( tag );
		var collider = obj.AddComponent<BoxCollider>(); collider.Static = true; collider.Scale = size;
		return obj;
	}
	private GameObject Target( Vector3 offset )
	{
		var obj = Box( "Tempo target", offset, new Vector3( 24f, 24f, 72f ), "player" );
		obj.GetComponent<BoxCollider>().Center = Vector3.Up * 36f;
		obj.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
		var health = obj.AddComponent<VBHealthComponent>(); health.StartingHardMaxHealth = 100000f; health.CanBeDowned = false;
		return obj;
	}
	private VBSoldierController Soldier( Vector3 offset )
	{
		var obj = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone(
			new CloneConfig { StartEnabled = false, Transform = new Transform( _origin + offset + Vector3.Up * 2f ) } );
		_objects.Add( obj );
		var soldier = obj.GetComponent<VBSoldierController>( true );
		soldier.UseCover = soldier.AvoidVisibleHazards = false; soldier.PatrolRadius = 0f; soldier.GrenadeCount = 0;
		obj.GetComponent<VBHealthComponent>( true ).GodMode = true; // Isolate tempo from stray friendly-fire knockdowns.
		var eyes = obj.GetComponent<VBAiPerceptionComponent>( true );
		eyes.AcquisitionDelay = 0f; eyes.ThinkInterval = 0.05f; eyes.HearingSensitivity = 0f;
		obj.Enabled = true;
		return soldier;
	}
	public async Task Verify()
	{
		var nav = Scene.NavMesh;
		bool oldEnabled = nav.IsEnabled, oldCustom = nav.CustomBounds; var oldBounds = nav.Bounds;
		var coordination = Scene.GetSystem<VBSoldierCoordination>();
		int oldShooters = coordination.MaxShootersPerPlayer, oldManeuvers = coordination.MaxManeuversPerPlayer;
		try
		{
			coordination.MaxShootersPerPlayer = 2; coordination.MaxManeuversPerPlayer = 1;
			Box( "Tempo floor", new Vector3( 300f, 0f, -16f ), new Vector3( 3200f, 2600f, 32f ) );
			var target = Target( new Vector3( 600f, 0f, 0f ) );
			nav.CustomBounds = true; nav.Bounds = new BBox( _origin + new Vector3( -1200f, -1200f, -64f ), _origin + new Vector3( 1700f, 1200f, 180f ) );
			nav.IsEnabled = true; await Task.DelaySeconds( 0.2f );
			Check( await nav.Generate( Scene.PhysicsWorld ), "native squad navigation generates" );
			var squad = new List<VBSoldierController>();
			for ( int i = 0; i < 5; i++ ) squad.Add( Soldier( new Vector3( 0f, (i - 2) * 120f, 0f ) ) );
			await Task.DelaySeconds( 0.3f );
			Check( squad.All( s => s.MoveSpeed == 115f && s.PatrolSpeed == 65f && s.SearchSpeed == 85f && !s.UseWeaponBurstTiming ),
				"actual prefab loads deliberate movement and burst timing" );
			var clips = squad.Select( s => s.Weapon.Clip1 ).ToArray();
			var yaws = squad.Select( s => s.WorldRotation.Yaw() ).ToArray();
			var lastBurst = Enumerable.Repeat( -100f, 5 ).ToArray();
			var active = new bool[5]; var stoppedAt = Enumerable.Repeat( -100f, 5 ).ToArray();
			var moved = new HashSet<VBSoldierController>();
			bool maneuverCap = true, speedCap = true, turnCap = true, restHolds = true, burstRests = true;
			float firstManeuver = 100f, previousSample = 0f, maxTurn = 0f; int completeBursts = 0;
			TimeSince elapsed = 0f;
			while ( elapsed < 18f )
			{
				await Task.Frame();
				float sampleDelta = MathF.Max( Time.Delta, (float)elapsed - previousSample );
				previousSample = elapsed;
				maneuverCap &= squad.Count( s => s.IsManeuvering ) <= 1;
				for ( int i = 0; i < squad.Count; i++ )
				{
					var soldier = squad[i];
					bool moving = soldier.IsManeuvering;
					if ( moving ) { moved.Add( soldier ); firstManeuver = MathF.Min( firstManeuver, elapsed ); }
					if ( moving && !active[i] ) restHolds &= elapsed - stoppedAt[i] >= soldier.PositionHoldMin - 0.1f;
					if ( !moving && active[i] ) stoppedAt[i] = elapsed;
					active[i] = moving;
					speedCap &= soldier.GetComponent<VBNpcLocomotion>().Velocity.WithZ( 0f ).Length <= soldier.MoveSpeed + 1f;
					float turn = MathF.Abs( MathX.DeltaDegrees( yaws[i], soldier.WorldRotation.Yaw() ) );
					maxTurn = MathF.Max( maxTurn, turn / MathF.Max( sampleDelta, 0.001f ) );
					turnCap &= turn <= soldier.MaximumTurnRate * sampleDelta + 0.2f;
					yaws[i] = soldier.WorldRotation.Yaw();
					int clip = soldier.Weapon.Clip1;
					if ( clip < clips[i] )
					{
						burstRests &= elapsed - lastBurst[i] >= soldier.BurstRestMin - 0.05f;
						// Weapons may choose two or three rounds; don't mistake a middle shot for a completed burst.
						if ( soldier.HasFireSlot && soldier.TempoTestBurstRemaining == 0 ) { lastBurst[i] = elapsed; completeBursts++; }
					}
					clips[i] = clip;
				}
			}
			Check( maneuverCap, "five soldiers permit at most one voluntary maneuver at a time" );
			Check( firstManeuver >= 0.8f && firstManeuver < 4f, "squad observes before beginning its first maneuver" );
			Check( moved.Count >= 2, "maneuver permission passes to another soldier" );
			Check( restHolds, "a completed maneuver is followed by a sustained stationary phase" );
			Check( speedCap, "physical squad movement stays within walking combat speed" );
			Check( turnCap, "body rotation respects its angular speed limit" );
			Log.Info( $"Tempo turn diagnostic: peak={maxTurn:F2} degrees/s" );
			Check( completeBursts >= 2 && burstRests, "real bursts retain at least one second of observation between them" );

			var camper = squad.First( s => !s.IsManeuvering && s.WorldPosition.x < _origin.x + 50f );
			foreach ( var soldier in squad ) { soldier.UseTacticalPositions = false; if ( soldier != camper ) soldier.Enabled = false; }
			camper.MoveSpeed = 0f; await Task.DelaySeconds( 0.4f );
			var eyes = camper.GetComponent<VBAiPerceptionComponent>();
			camper.WorldRotation = Rotation.FromYaw( Rotation.LookAt( target.WorldPosition - camper.WorldPosition ).Yaw() + 100f );
			Check( eyes.CanSee( target ), "a remembered enemy remains detectable through engaged peripheral attention" );
			var wall = Box( "Tempo occlusion", new Vector3( 300f, 0f, 90f ), new Vector3( 32f, 1800f, 180f ) );
			await Task.DelaySeconds( 0.2f );
			var memory = eyes.LastKnownPosition; var aim = camper.AimPosition;
			target.WorldPosition += Vector3.Left * 60f;
			await Task.DelaySeconds( 3.1f );
			Check( !eyes.TargetVisible && camper.RemembersTarget( target ) && camper.TempoTestAimTarget == target,
				"occlusion longer than two seconds preserves target identity and aiming attention" );
			Check( eyes.LastKnownPosition == memory && camper.AimPosition == aim,
				"sustained attention never tracks hidden player movement" );
			Check( camper.State == VBSoldierState.Support, "soldier observes the known contact instead of immediately wandering" );
			await Task.DelaySeconds( 1.2f );
			Check( camper.State == VBSoldierState.Search && eyes.CurrentTarget == target,
				"measured investigation begins after observation without dropping the actor" );
			eyes.AcquisitionDelay = 0.45f;
			wall.Destroy(); await Task.DelaySeconds( 0.2f );
			Check( eyes.TargetVisible && eyes.CurrentTarget == target, "remembered contact reacquires without restarting the initial acquisition delay" );

			camper.Enabled = false;
			await Task.DelaySeconds( 0.2f ); // Let OnDisabled cancel the brain's route before driving the motor.
			var motion = camper.GetComponent<VBNpcLocomotion>(); motion.Agent.MaxSpeed = 115f;
			TimeSince accelerating = 0f;
			motion.WalkDirectlyTo( camper.WorldPosition + Vector3.Backward * 200f );
			await Task.DelaySeconds( 0.1f );
			Log.Info( $"Tempo motor diagnostic: time={(float)accelerating:F3} speed={motion.Velocity.WithZ( 0f ).Length:F2} grounded={motion.IsGrounded}" );
			Check( motion.Velocity.WithZ( 0f ).Length > 1f && motion.Velocity.WithZ( 0f ).Length < 80f,
				"native physical movement accelerates progressively from rest" );
			await Task.DelaySeconds( 0.35f );
			var stop = camper.WorldPosition; motion.CancelDirectMove();
			await Task.DelaySeconds( 0.04f );
			Check( motion.Velocity.WithZ( 0f ).Length > 20f, "stopping applies braking instead of instantly erasing velocity" );
			await Task.DelaySeconds( 0.6f );
			Check( motion.Velocity.WithZ( 0f ).Length < 2f && Vector3.DistanceBetween( stop, camper.WorldPosition ) > 4f,
				"braking reaches a stationary pose after a short physical stopping distance" );
			Log.Info( $"Soldier tempo FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception ex ) { Log.Error( $"Soldier tempo EXCEPTION: {ex}" ); }
		finally
		{
			coordination.MaxShootersPerPlayer = oldShooters; coordination.MaxManeuversPerPlayer = oldManeuvers;
			foreach ( var obj in _objects.Where( o => o.IsValid() ) ) obj.Destroy();
			nav.IsEnabled = oldEnabled; nav.CustomBounds = oldCustom; nav.Bounds = oldBounds;
			GameObject.Destroy();
		}
	}
}

public sealed partial class VBSoldierController
{
	internal GameObject TempoTestAimTarget => _aimTarget;
	internal int TempoTestBurstRemaining => _burstRemaining;
}
