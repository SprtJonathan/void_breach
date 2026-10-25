using System;
using System.Threading.Tasks;
using Sandbox;

// Host-only fixture, compiled in an isolated project. No human player is controlled.
public sealed class VBSoldierAwarenessRegression : Component
{
	private readonly List<GameObject> _objects = new();
	private readonly Vector3 _origin = new( 72000f, 72000f, 4000f );
	private int _failures;
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Soldier awareness {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private GameObject Box( string name, Vector3 offset, Vector3 size, string tag = "map" )
	{
		var obj = new GameObject( true, name ); _objects.Add( obj ); obj.WorldPosition = _origin + offset; obj.Tags.Add( tag );
		var shape = obj.AddComponent<BoxCollider>(); shape.Static = true; shape.Scale = size;
		return obj;
	}
	private void Floor() => Box( "Awareness floor", new Vector3( 300f, 1600f, -16f ), new Vector3( 5000f, 9500f, 32f ) );
	private GameObject Target( Vector3 offset, bool eyes = false )
	{
		var obj = Box( "Awareness player", offset, new Vector3( 24f, 24f, 72f ), "player" );
		obj.GetComponent<BoxCollider>().Center = Vector3.Up * 36f;
		obj.AddComponent<VBFactionComponent>().FactionId = VBFactionComponent.PlayerFaction;
		var health = obj.AddComponent<VBHealthComponent>(); health.StartingHardMaxHealth = 100000f; health.CanBeDowned = false;
		if ( eyes ) obj.AddComponent<VBAiPerceptionComponent>().EyeOffset = Vector3.Up * 64f;
		return obj;
	}
	private VBSoldierController Soldier( Vector3 offset, float yaw = 0f, bool squad = false, string faction = null )
	{
		var obj = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone(
			new CloneConfig { StartEnabled = false, Transform = new Transform( _origin + offset + Vector3.Up * 2f, Rotation.FromYaw( yaw ) ) } );
		_objects.Add( obj );
		var soldier = obj.GetComponent<VBSoldierController>( true );
		soldier.UseCover = soldier.UseTacticalPositions = soldier.AvoidVisibleHazards = false;
		soldier.UseSquadAlerts = squad; soldier.WatchOpenSectors = false; soldier.SquadAlertRange = 900f;
		soldier.MoveSpeed = soldier.PatrolRadius = 0f; soldier.GrenadeCount = 0;
		obj.GetComponent<VBHealthComponent>( true ).GodMode = true;
		var perception = obj.GetComponent<VBAiPerceptionComponent>( true );
		perception.ThinkInterval = 0.05f; perception.HearingSensitivity = 0f;
		if ( faction is not null ) obj.GetComponent<VBFactionComponent>( true ).FactionId = faction;
		obj.Enabled = true;
		return soldier;
	}
	private void ClearCase()
	{
		foreach ( var obj in _objects.Where( x => x.IsValid() ) ) obj.Destroy();
		_objects.Clear();
	}
	public async Task Verify()
	{
		var nav = Scene.NavMesh;
		bool oldEnabled = nav.IsEnabled, oldCustom = nav.CustomBounds; var oldBounds = nav.Bounds;
		var coordination = Scene.GetSystem<VBSoldierCoordination>();
		int oldInvestigators = coordination.MaxInvestigatorsPerPlayer;
		try
		{
			nav.IsEnabled = false;
			Floor();
			var nearby = Target( new Vector3( 0f, 140f, 0f ) );
			var soldier = Soldier( Vector3.Zero );
			var perception = soldier.GetComponent<VBAiPerceptionComponent>();
			await Task.DelaySeconds( 0.18f );
			Check( perception.HasVisualSuspicion && !perception.TargetVisible && soldier.State == VBSoldierState.Alert,
				"a close side contact interrupts patrol during acquisition, before firing is allowed" );
			await Task.DelaySeconds( 0.5f );
			Check( perception.CurrentTarget == nearby && perception.TargetVisible, "a player standing at the soldier's side is noticed" );
			soldier.WorldRotation = Rotation.Identity;
			var distantSide = Target( new Vector3( 0f, -500f, 0f ) );
			var silentRear = Target( new Vector3( -140f, 0f, 0f ) );
			Check( !perception.CanSee( distantSide ), "close peripheral awareness does not grant panoramic vision at range" );
			Check( !perception.CanSee( silentRear ), "a silent unseen player directly behind retains a blind angle" );
			Box( "Side wall", new Vector3( 0f, 70f, 50f ), new Vector3( 200f, 16f, 100f ) );
			await Task.DelaySeconds( 0.1f );
			Check( !perception.CanSee( nearby ), "close peripheral awareness still respects a solid wall" );
			nearby.Tags.Add( "notarget" );
			Check( !perception.CanSee( nearby ), "awareness preserves explicit notarget filtering" );
			ClearCase(); await Task.DelaySeconds( 0.1f );

			Floor();
			var headTarget = Target( Vector3.Forward * 300f, eyes: true );
			soldier = Soldier( Vector3.Zero ); perception = soldier.GetComponent<VBAiPerceptionComponent>();
			var lowWall = Box( "Chest-high screen", new Vector3( 180f, 0f, 27f ), new Vector3( 20f, 160f, 54f ) );
			await Task.DelaySeconds( 0.2f );
			var chest = Scene.Trace.Ray( soldier.WorldPosition + perception.EyeOffset, headTarget.WorldPosition + perception.TargetOffset )
				.IgnoreGameObjectHierarchy( soldier.GameObject ).UseHitboxes( true ).Run();
			Check( chest.GameObject == lowWall && perception.CanSee( headTarget ), "an exposed head is noticed even when the chest sample is occluded" );
			headTarget.GetComponent<VBAiPerceptionComponent>().EyeOffset = Vector3.Up * 35f;
			Check( !perception.CanSee( headTarget ), "a crouched hidden head does not leave a visibility sample above its body" );
			headTarget.GetComponent<VBAiPerceptionComponent>().EyeOffset = Vector3.Up * 64f;
			lowWall.GetComponent<BoxCollider>().Scale = new Vector3( 20f, 160f, 150f );
			await Task.DelaySeconds( 0.1f );
			Check( !perception.CanSee( headTarget ), "a high wall blocks both head and chest samples" );
			ClearCase(); await Task.DelaySeconds( 0.1f );

			Floor();
			soldier = Soldier( Vector3.Zero ); soldier.WatchOpenSectors = true;
			Box( "Wall facing patrol", new Vector3( 70f, 0f, 75f ), new Vector3( 20f, 300f, 150f ) );
			bool turnCap = true;
			float previousYaw = soldier.WorldRotation.Yaw(), windowStart = Time.Now, windowTravel = 0f, frameMargin = 0f, peakTurn = 0f;
			TimeSince turning = 0f;
			while ( turning < 2f )
			{
				await Task.Frame();
				float yaw = soldier.WorldRotation.Yaw();
				windowTravel += MathF.Abs( MathX.DeltaDegrees( previousYaw, yaw ) );
				frameMargin = MathF.Max( frameMargin, Time.Delta );
				// Async frame continuations can sample on either side of the brain's
				// update. Use a quarter-second window and one boundary-frame margin,
				// so scheduler jitter cannot look like an instantaneous body snap.
				float elapsed = Time.Now - windowStart;
				if ( elapsed >= 0.25f )
				{
					turnCap &= windowTravel <= soldier.MaximumTurnRate * (elapsed + frameMargin) + 0.3f;
					peakTurn = MathF.Max( peakTurn, windowTravel / (elapsed + frameMargin) );
					windowStart = Time.Now; windowTravel = frameMargin = 0f;
				}
				previousYaw = yaw;
			}
			Check( Vector3.Dot( soldier.WorldRotation.Forward, Vector3.Forward ) < 0.8f, "a stopped patrol turns away from a nearby wall to watch open space" );
			Check( turnCap, "sector surveillance preserves the deliberate angular speed limit" );
			Log.Info( $"Soldier awareness turn diagnostic: peak={peakTurn:F2} degrees/s" );
			float firstYaw = soldier.WorldRotation.Yaw(), maxSweep = 0f;
			TimeSince watching = 0f;
			while ( watching < 6f ) { await Task.Frame(); maxSweep = MathF.Max( maxSweep, MathF.Abs( MathX.DeltaDegrees( firstYaw, soldier.WorldRotation.Yaw() ) ) ); }
			Check( maxSweep > 25f, "stationary surveillance inspects another sector instead of fixing one direction forever" );
			ClearCase(); await Task.DelaySeconds( 0.1f );

			Floor();
			var target = Target( new Vector3( 600f, 0f, 0f ) );
			var observer = Soldier( new Vector3( 0f, -160f, 0f ), squad: true );
			var companion = Soldier( new Vector3( 0f, 160f, 0f ), yaw: 180f, squad: true );
			var hidden = Soldier( new Vector3( 0f, 500f, 0f ), yaw: 180f, squad: true );
			var remote = Soldier( new Vector3( 0f, 2000f, 0f ), yaw: 180f, squad: true );
			var foreign = Soldier( new Vector3( -100f, 650f, 0f ), yaw: 180f, squad: true, faction: "other_squad" );
			var nonRelay = Soldier( new Vector3( -850f, 500f, 0f ), yaw: 180f, squad: true );
			var screen = Box( "Squad screen", new Vector3( 300f, 440f, 75f ), new Vector3( 40f, 380f, 150f ) );
			await Task.DelaySeconds( 0.6f );
			Check( observer.GetComponent<VBAiPerceptionComponent>().TargetVisible, "one squad member confirms the actual enemy first" );
			Check( !hidden.GetComponent<VBAiPerceptionComponent>().ReportedTarget.IsValid(), "squad reporting has a human response delay" );
			var patrolMotion = hidden.GetComponent<VBNpcLocomotion>();
			patrolMotion.Agent.MaxSpeed = 65f;
			patrolMotion.WalkDirectlyTo( hidden.WorldPosition + Vector3.Left * 100f );
			await Task.DelaySeconds( 0.08f );
			Check( patrolMotion.IsMovingDirectly, "the unaware teammate is executing a real physical patrol walk" );
			await Task.DelaySeconds( 0.57f );
			var hiddenEyes = hidden.GetComponent<VBAiPerceptionComponent>();
			Check( hiddenEyes.ReportedTarget == target && !hiddenEyes.TargetVisible && !hiddenEyes.LastSeenTarget.IsValid(),
				"a teammate behind a wall receives a report without inventing visual contact" );
			Check( hidden.State == VBSoldierState.Alert && companion.State != VBSoldierState.Patrol && companion.State != VBSoldierState.Idle,
				"nearby teammates interrupt their round and orient towards the reported sector" );
			Check( !patrolMotion.IsMovingDirectly, "a squad alert interrupts the physical patrol instead of waiting for its destination" );
			Check( !remote.GetComponent<VBAiPerceptionComponent>().ReportedTarget.IsValid()
				&& !foreign.GetComponent<VBAiPerceptionComponent>().ReportedTarget.IsValid(), "reports stay within local squad range and faction" );
			await Task.DelaySeconds( 2f );
			Check( companion.GetComponent<VBAiPerceptionComponent>().TargetVisible, "an alerted teammate turns, confirms the enemy itself and joins combat" );
			observer.GameObject.Enabled = companion.GameObject.Enabled = false;
			var recorded = hiddenEyes.LastKnownPosition;
			int rounds = hidden.Weapon.Clip1;
			target.WorldPosition += Vector3.Forward * 200f;
			await Task.DelaySeconds( 1.5f );
			Check( !hiddenEyes.TargetVisible && hiddenEyes.LastKnownPosition.AlmostEqual( recorded, 1f ) && hidden.Weapon.Clip1 == rounds,
				"a radio report never tracks a hidden moving player or permits blind gunfire" );
			Check( !nonRelay.GetComponent<VBAiPerceptionComponent>().ReportedTarget.IsValid(), "a soldier cannot relay second-hand information as a fresh sighting" );
			hiddenEyes.HearingSensitivity = 1f;
			((IVBAiSoundListener)hiddenEyes).OnSoundStimulus( new VBAiSoundStimulus( observer.WorldPosition, 2000f, observer.GameObject, VBAiSoundKind.Gunshot ) );
			Check( hiddenEyes.CurrentTarget == target && hiddenEyes.LastKnownPosition.AlmostEqual( recorded, 1f ), "friendly gunfire preserves an existing enemy clue" );
			Check( !hiddenEyes.ReceiveContactReport( target, target.WorldPosition, Time.Now - 7f ), "stale reports are rejected" );
			Check( !hiddenEyes.ReceiveContactReport( hidden.GameObject, hidden.WorldPosition, Time.Now ), "reports cannot bypass faction filtering" );
			screen.Destroy(); await Task.DelaySeconds( 0.8f );
			Check( hiddenEyes.TargetVisible && hiddenEyes.LastSeenTarget == target && hiddenEyes.LastKnownPosition.AlmostEqual( target.WorldPosition, 1f ),
				"own vision replaces the old report after occlusion ends" );
			ClearCase(); await Task.DelaySeconds( 0.1f );

			Floor();
			target = Target( new Vector3( 600f, 0f, 0f ) );
			var otherTarget = Target( new Vector3( 600f, 4000f, 0f ) );
			Box( "Investigation screen", new Vector3( 300f, 0f, 75f ), new Vector3( 40f, 1100f, 150f ) );
			Box( "Other player screen", new Vector3( 300f, 4000f, 75f ), new Vector3( 40f, 1100f, 150f ) );
			nav.CustomBounds = true; nav.Bounds = new BBox( _origin + new Vector3( -1200f, -1200f, -64f ), _origin + new Vector3( 1800f, 5200f, 180f ) );
			nav.IsEnabled = true; await Task.DelaySeconds( 0.2f );
			Check( await nav.Generate( Scene.PhysicsWorld ), "native routes around the reported contact's screen generate" );
			coordination.MaxInvestigatorsPerPlayer = 1;
			var investigators = new[] { Soldier( new Vector3( 0f, -160f, 0f ) ), Soldier( Vector3.Zero ), Soldier( new Vector3( 0f, 160f, 0f ) ) };
			var independent = Soldier( new Vector3( 0f, 4000f, 0f ) );
			foreach ( var actor in investigators.Append( independent ) ) actor.MoveSpeed = 115f;
			await Task.DelaySeconds( 0.1f );
			foreach ( var actor in investigators ) actor.GetComponent<VBAiPerceptionComponent>().ReceiveContactReport( target, target.WorldPosition, Time.Now );
			independent.GetComponent<VBAiPerceptionComponent>().ReceiveContactReport( otherTarget, otherTarget.WorldPosition, Time.Now );
			bool investigatorCap = true;
			TimeSince searching = 0f;
			while ( searching < 3.2f ) { await Task.Frame(); investigatorCap &= investigators.Count( x => x.IsInvestigating ) <= 1; }
			Check( investigatorCap && investigators.Count( x => x.IsInvestigating ) == 1, "one soldier investigates a lost player while the others hold their sector" );
			Check( independent.IsInvestigating, "a second player has an independent investigation budget" );
			var leading = investigators.FirstOrDefault( x => x.IsInvestigating );
			Check( leading.IsValid() && leading.GetComponent<VBNpcLocomotion>().Velocity.Length > 5f,
				"the designated investigator really walks a native route towards the recorded contact" );
			if ( leading.IsValid() )
			{
				leading.GetComponent<VBAiPerceptionComponent>().Enabled = false;
				Check( coordination.RequestInvestigation( leading, target ), "a suspended visual sensor retains its investigation turn without a null reference" );
			}
			if ( leading.IsValid() ) leading.GameObject.Enabled = false;
			await Task.DelaySeconds( 0.5f );
			Check( investigators.Count( x => x.Active && x.IsInvestigating ) == 1, "another soldier takes over when the investigator becomes unavailable" );
			Log.Info( $"Soldier awareness FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception ex ) { Log.Error( $"Soldier awareness EXCEPTION: {ex}" ); }
		finally
		{
			ClearCase(); coordination.MaxInvestigatorsPerPlayer = oldInvestigators;
			nav.IsEnabled = oldEnabled; nav.CustomBounds = oldCustom; nav.Bounds = oldBounds;
			GameObject.Destroy();
		}
	}
}
