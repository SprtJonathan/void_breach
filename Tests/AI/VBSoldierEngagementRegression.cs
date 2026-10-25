using System;
using System.Threading.Tasks;
using Sandbox;

// Host-only integration: shipped cover/tactics/tempo remain enabled. Measures
// sustained combat in rooms instead of only checking individual decisions.
public sealed class VBSoldierEngagementRegression : Component
{
	private readonly Vector3 _origin = new( 80000f, 80000f, 4000f );
	private readonly List<GameObject> _objects = new();
	private int _failures;
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Soldier engagement {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	private GameObject Box( string name, Vector3 offset, Vector3 size, string tag = "map" )
	{
		var obj = new GameObject( true, name ); _objects.Add( obj ); obj.WorldPosition = _origin + offset; obj.Tags.Add( tag );
		var shape = obj.AddComponent<BoxCollider>(); shape.Static = true; shape.Scale = size;
		return obj;
	}
	private GameObject Target( Vector3 offset, string faction = VBFactionComponent.PlayerFaction )
	{
		var obj = Box( "Engagement target", offset, new Vector3( 28f, 28f, 72f ), "player" );
		obj.GetComponent<BoxCollider>().Center = Vector3.Up * 36f;
		obj.AddComponent<VBFactionComponent>().FactionId = faction;
		var hp = obj.AddComponent<VBHealthComponent>(); hp.StartingHardMaxHealth = 100000f; hp.CanBeDowned = false;
		return obj;
	}
	private VBSoldierController Soldier( Vector3 offset, int grenades = 2 )
	{
		var obj = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone(
			new CloneConfig { StartEnabled = false, Transform = new Transform( _origin + offset + Vector3.Up * 2f ) } );
		_objects.Add( obj );
		var soldier = obj.GetComponent<VBSoldierController>( true );
		soldier.PatrolRadius = 0f; soldier.GrenadeCount = grenades;
		obj.GetComponent<VBHealthComponent>( true ).GodMode = true;
		obj.Enabled = true;
		return soldier;
	}
	private void ClearCase()
	{
		foreach ( var grenade in Scene.GetAll<VBNpcGrenade>().Where( x => x.SoldierCoordinated ).ToArray() ) grenade.GameObject.Destroy();
		foreach ( var obj in _objects.Where( x => x.IsValid() ) ) obj.Destroy();
		_objects.Clear();
	}
	private async Task Room( float halfWidth = 144f, float length = 1400f )
	{
		Box( "Indoor floor", new Vector3( 200f, 0f, -16f ), new Vector3( length, halfWidth * 2f + 80f, 32f ) );
		Box( "Indoor left wall", new Vector3( 200f, halfWidth + 12f, 80f ), new Vector3( length, 24f, 160f ) );
		Box( "Indoor right wall", new Vector3( 200f, -halfWidth - 12f, 80f ), new Vector3( length, 24f, 160f ) );
		Box( "Indoor ceiling", new Vector3( 200f, 0f, 144f ), new Vector3( length, halfWidth * 2f + 80f, 16f ) );
		var nav = Scene.NavMesh; nav.CustomBounds = true;
		nav.Bounds = new BBox( _origin + new Vector3( 200f - length / 2f, -halfWidth - 20f, -64f ),
			_origin + new Vector3( 200f + length / 2f, halfWidth + 20f, 180f ) );
		nav.IsEnabled = true; await Task.DelaySeconds( 0.2f );
		Check( await nav.Generate( Scene.PhysicsWorld ), "native indoor routes generate under the low ceiling" );
	}
	public async Task VerifyCloseRange()
	{
		var nav = Scene.NavMesh; bool oldEnabled = nav.IsEnabled, oldCustom = nav.CustomBounds; var oldBounds = nav.Bounds;
		try
		{
			await Room();
			var victim = Target( new Vector3( 230f, 0f, 0f ) );
			var actor = Soldier( Vector3.Zero, grenades: 0 );
			bool crouchedShot = false; int clip = -1;
			TimeSince elapsed = 0f;
			while ( elapsed < 6f )
			{
				await Task.Frame();
				if ( !actor.Weapon.IsValid() ) continue;
				int current = actor.Weapon.Clip1;
				if ( clip >= 0 && current < clip ) crouchedShot |= actor.FiringStanceAccuracyMultiplier < 1f;
				clip = current;
			}
			Check( crouchedShot && victim.GetComponent<VBHealthComponent>().CurrentHealth < 100000f,
				"a soldier fires real stabilized crouched rounds at 230 units with production tactics enabled" );
			Log.Info( $"Soldier close engagement FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception ex ) { Log.Error( $"Soldier close engagement EXCEPTION: {ex}" ); }
		finally
		{
			ClearCase(); nav.IsEnabled = oldEnabled; nav.CustomBounds = oldCustom; nav.Bounds = oldBounds; GameObject.Destroy();
		}
	}

	public async Task Verify()
	{
		var nav = Scene.NavMesh; bool oldEnabled = nav.IsEnabled, oldCustom = nav.CustomBounds; var oldBounds = nav.Bounds;
		try
		{
			await Room();
			var victim = Target( new Vector3( 500f, 0f, 0f ) );
			var squad = new[] { Soldier( new Vector3( 0f, -72f, 0f ) ), Soldier( new Vector3( 0f, 72f, 0f ) ),
				Soldier( new Vector3( -120f, -72f, 0f ) ), Soldier( new Vector3( -120f, 72f, 0f ) ), Soldier( new Vector3( -240f, 0f, 0f ) ) };
			await Task.DelaySeconds( 0.3f );
			var clips = squad.Select( x => x.Weapon.Clip1 ).ToArray();
			var start = squad.Select( x => x.WorldPosition ).ToArray();
			bool crouchedFire = false, grenade = false, fireCap = true, grenadeCap = true, usefulPlans = true;
			float lastShot = 0f, longestSilence = 0f, movingTime = 0f, measuredTime = 0f, furthest = 0f;
			int shootingFrames = 0;
			TimeSince elapsed = 0f;
			while ( elapsed < 24f )
			{
				await Task.Frame();
				int shooters = 0;
				for ( int i = 0; i < squad.Length; i++ )
				{
					var actor = squad[i]; int clip = actor.Weapon.Clip1;
					if ( clip < clips[i] ) { shooters++; crouchedFire |= actor.FiringStanceAccuracyMultiplier < 1f; }
					clips[i] = clip;
					furthest = MathF.Max( furthest, Vector3.DistanceBetween( start[i], actor.WorldPosition ) );
					usefulPlans &= actor.EngagementTestPlanUseful;
					if ( elapsed > 2f )
					{
						measuredTime += Time.Delta;
						if ( actor.GetComponent<VBNpcLocomotion>().Velocity.WithZ( 0f ).Length > 10f ) movingTime += Time.Delta;
					}
				}
				if ( shooters > 0 ) { shootingFrames++; lastShot = elapsed; }
				if ( elapsed > 3f ) longestSilence = MathF.Max( longestSilence, elapsed - lastShot );
				fireCap &= shooters <= 2;
				var live = Scene.GetAll<VBNpcGrenade>().Where( x => x.SoldierCoordinated && x.IsLive ).ToArray();
				grenade |= live.Length > 0;
				grenadeCap &= live.Length + squad.Count( x => x.State == VBSoldierState.ThrowGrenade ) <= 1;
			}
			Check( shootingFrames > 12 && longestSilence < 5f, "five soldiers maintain actual gunfire instead of collectively disappearing from combat" );
			Check( movingTime / MathF.Max( measuredTime, 1f ) < 0.4f, "the squad spends most of the engagement holding useful positions rather than wandering" );
			Check( usefulPlans && furthest < 500f, "indoor maneuvers have a firing destination and stay within the local encounter" );
			Check( crouchedFire, "a shooter really fires stabilized crouched rounds with normal tactics enabled" );
			Check( grenade, "a stationary visible player receives a real pressure grenade under the indoor ceiling" );
			Check( fireCap && grenadeCap, "indoor pressure preserves per-player shooting limits and the single-grenade exclusion" );
			Log.Info( $"Engagement corridor metrics: firingFrames={shootingFrames} silence={longestSilence:F2}s moving={movingTime / MathF.Max( measuredTime, 1f ):P0} excursion={furthest:F0}" );
			ClearCase(); nav.IsEnabled = false; await Task.DelaySeconds( 0.2f );

			// A safe-looking dead end close to the rifleman used to outscore every
			// useful peek, because the support planner explicitly rewarded blindness.
			await Room( halfWidth: 380f );
			victim = Target( new Vector3( 600f, 0f, 0f ) );
			var isolated = Soldier( Vector3.Zero, grenades: 0 );
			await Task.DelaySeconds( 0.7f );
			Check( isolated.GetComponent<VBAiPerceptionComponent>().TargetVisible, "blind-refuge case starts with a real enemy contact" );
			Box( "Deep blind refuge partition", new Vector3( 100f, 0f, 80f ), new Vector3( 32f, 500f, 160f ) );
			await Task.DelaySeconds( 0.2f );
			Check( await nav.Generate( Scene.PhysicsWorld ), "deep refuge has a real walking exit around its partition" );
			await Task.DelaySeconds( 0.3f );
			float blindRefugeTime = 0f, refugeReacquiredAt = -1f;
			bool refugeFiredAgain = false; int refugeClip = isolated.Weapon.Clip1;
			elapsed = 0f;
			while ( elapsed < 15f )
			{
				await Task.Frame();
				if ( isolated.EngagementTestBlindRefuge ) blindRefugeTime += Time.Delta;
				if ( isolated.GetComponent<VBAiPerceptionComponent>().TargetVisible && refugeReacquiredAt < 0f ) refugeReacquiredAt = elapsed;
				refugeFiredAgain |= isolated.Weapon.Clip1 < refugeClip; refugeClip = isolated.Weapon.Clip1;
			}
			Check( blindRefugeTime < 3f, "a healthy soldier cannot spend the encounter parked in a shelter with no firing exit" );
			Check( refugeReacquiredAt >= 0f && refugeReacquiredAt < 12f && refugeFiredAgain,
				"a soldier in a blind room walks back into contact and actually resumes shooting" );
			Log.Info( $"Engagement refuge metrics: blindHold={blindRefugeTime:F2}s reacquired={refugeReacquiredAt:F2}s firedAgain={refugeFiredAgain} {isolated.EngagementTestStatus}" );
			ClearCase(); nav.IsEnabled = false; await Task.DelaySeconds( 0.2f );

			await Room( halfWidth: 300f );
			victim = Target( new Vector3( 500f, 0f, 0f ) );
			squad = new[] { Soldier( new Vector3( 0f, -64f, 0f ), grenades: 0 ), Soldier( new Vector3( 0f, 64f, 0f ), grenades: 0 ) };
			await Task.DelaySeconds( 2.5f );
			Check( squad.Any( x => x.GetComponent<VBAiPerceptionComponent>().TargetVisible ), "room combat begins with a confirmed visual contact" );
			Box( "New occluding room partition", new Vector3( 240f, 0f, 80f ), new Vector3( 80f, 190f, 160f ) );
			await Task.DelaySeconds( 0.2f );
			Check( await nav.Generate( Scene.PhysicsWorld ), "room partition has an actual walkable route around it" );
			await Task.DelaySeconds( 0.3f );
			Check( squad.All( x => !x.GetComponent<VBAiPerceptionComponent>().TargetVisible ), "the new partition really occludes the entire squad" );
			elapsed = 0f; float recoveredAt = -1f; bool firedAgain = false;
			clips = squad.Select( x => x.Weapon.Clip1 ).ToArray();
			while ( elapsed < 13f )
			{
				await Task.Frame();
				for ( int i = 0; i < squad.Length; i++ )
				{
					if ( squad[i].GetComponent<VBAiPerceptionComponent>().TargetVisible && recoveredAt < 0f ) recoveredAt = elapsed;
					firedAgain |= squad[i].Weapon.Clip1 < clips[i]; clips[i] = squad[i].Weapon.Clip1;
				}
			}
			Check( recoveredAt >= 0f && recoveredAt < 11f && firedAgain, "an occluded squad takes a local route and resumes real combat instead of staying hidden" );
			Log.Info( $"Engagement recovery metrics: reacquired={recoveredAt:F2}s firedAgain={firedAgain}" );
			ClearCase(); nav.IsEnabled = false; await Task.DelaySeconds( 0.2f );

			await Room();
			victim = Target( new Vector3( 500f, 0f, 0f ) );
			var ally = Target( new Vector3( 530f, 60f, 0f ), VBFactionComponent.CartelFaction );
			var thrower = Soldier( Vector3.Zero );
			await Task.DelaySeconds( 0.3f );
			thrower.Weapon.SetNextPrimaryFire( 100f );
			await Task.DelaySeconds( 7f );
			Check( thrower.GrenadesRemaining == 2 && !Scene.GetAll<VBNpcGrenade>().Any( x => x.SoldierCoordinated ),
				"pressure does not throw into a nearby ally to force activity" );
			ally.Destroy();
			bool flatThrow = false; elapsed = 0f;
			while ( elapsed < 4f )
			{
				await Task.Frame();
				flatThrow |= Scene.GetAll<VBNpcGrenade>().Any( x => x.SoldierCoordinated );
			}
			Check( flatThrow && thrower.GrenadesRemaining < 2, "clearing the ally permits a swept flat grenade trajectory below a low ceiling" );
			Log.Info( $"Soldier engagement FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception ex ) { Log.Error( $"Soldier engagement EXCEPTION: {ex}" ); }
		finally
		{
			ClearCase(); nav.IsEnabled = oldEnabled; nav.CustomBounds = oldCustom; nav.Bounds = oldBounds;
			GameObject.Destroy();
		}
	}
}

public sealed partial class VBSoldierController
{
	internal bool EngagementTestPlanUseful => !_tacticalMove || HasFiringLane( _tacticalPosition, Perception.CurrentTarget, standing: true );
	internal bool EngagementTestBlindRefuge => _hasCover && !_coverHasPeek;
	internal string EngagementTestStatus => $"state={State} cover={_hasCover} peek={_coverHasPeek} position={WorldPosition}";
}
