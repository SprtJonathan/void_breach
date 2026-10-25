using System;
using System.Threading.Tasks;
using Sandbox;

// Temporary: full player prefab regression for the reported vehicle launch.
public sealed class VBVehicleEntryRegression : Component, IVBVehicleControlSource
{
	private static VBVehicleEntryRegression _running;
	private VBVehicleController _car;
	private GameObject _floor, _playerObject;
	public VBVehicleControls GetVehicleControls( VBVehicleController vehicle ) => VBVehicleControls.Parked;

	[ConCmd( "vb_vehicle_entry_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null || _running.IsValid() ) return;
		var carObject = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( new Vector3( 0f, 0f, 1540f ) );
		_running = carObject.AddComponent<VBVehicleEntryRegression>();
		_running._car = carObject.GetComponent<VBVehicleController>();
		carObject.NetworkSpawn();
		_ = _running.Verify();
	}

	private async Task Verify()
	{
		var fixture = GameObject;
		try
		{
			_floor = new GameObject( true, "Vehicle entry regression floor" );
			_floor.WorldPosition = new Vector3( 0f, 0f, 1484f );
			var floorCollider = _floor.AddComponent<BoxCollider>();
			floorCollider.Scale = new Vector3( 10000f, 10000f, 32f );
			floorCollider.Friction = 1f;
			await Task.DelaySeconds( 1.5f );
			_playerObject = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( new Vector3( 0f, 120f, 1504f ) );
			_playerObject.Name = "Vehicle full player regression";
			var player = _playerObject.GetComponent<PlayerController>();
			player.UseCameraControls = false; player.UseLookControls = false; player.EnablePressing = false;
			player.UseInputControls = false; // The human may keep walking during the fixture setup.
			_playerObject.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput = false;
			player.WishVelocity = Vector3.Zero;
			var health = _playerObject.GetComponent<VBHealthComponent>();
			health.OnDamaged += damage => Log.Info( $"Vehicle entry regression DAMAGE: amount={damage.Amount}, type={damage.Type}, attacker={damage.Attacker?.Name}, source={damage.Source?.Name}, position={damage.Position}, force={damage.Force}" );
			health.GodMode = true;
			var hud = _playerObject.Children.FirstOrDefault( x => x.Name == "HUD" );
			if ( hud.IsValid() ) hud.Enabled = false;
			_playerObject.NetworkSpawn( Connection.Local );
			await Task.DelaySeconds( 1f );
			// The actual human remains protected. The isolated fixture must survive
			// entry/exit without invulnerability masking an impact or fall bug.
			health.GodMode = false;
			health.ResetHealth();
			var seat = _car.GetComponent<VBVehicleDriverSeat>();
			var before = _car.WorldPosition;
			var originalContacts = _playerObject.GetComponentsInChildren<ModelPhysics>( includeDisabled: true, includeSelf: true )
				.SelectMany( x => x.Bodies ).Where( x => x.Component.IsValid() && x.Component.PhysicsBody.IsValid() )
				.ToDictionary( x => x.Component.PhysicsBody, x => x.Component.PhysicsBody.EnableSolidCollisions );
			Log.Info( $"Vehicle entry regression BEFORE: pos={before}, v={_car.Body.Velocity}, canEnter={seat.CanEnter( player )}" );
			player.UseInputControls = true;
			((Component.IPressable)seat).Press( new Component.IPressable.Event( player, null ) );
			await Task.DelaySeconds( 0.15f );
			if ( seat.Driver != player ) throw new InvalidOperationException( "Full player fixture did not enter the seat." );
			_car.ControlSource = this; // Park the fixture independently of the human's keyboard.
			float peak = 0f;
			for ( int sample = 0; sample < 20; sample++ )
			{
				await Task.DelaySeconds( 0.2f );
				peak = MathF.Max( peak, _car.Body.Velocity.Length );
				Log.Info( $"Vehicle entry regression SAMPLE {sample}: pos={_car.WorldPosition}, v={_car.Body.Velocity}, angular={_car.Body.AngularVelocity}, wheels={_car.GroundedWheels}, seated={seat.Driver == player}, health={health.CurrentHealth}, pbody={player.Body.Enabled}, ragdoll={player.GetComponent<VBPlayerLifeStateController>().RagdollPhysics?.MotionEnabled}" );
				if ( sample == 0 )
					foreach ( var c in _playerObject.GetComponentsInChildren<Collider>( includeDisabled: true, includeSelf: true ) )
						Log.Info( $"Vehicle entry regression COLLIDER: {c.GameObject.Name}/{c.GetType().Name}: active={c.Active}, body={c.Rigidbody?.GameObject.Name}" );
			}
			Log.Info( $"Vehicle entry regression RESULT: peak={peak:F2}, heightDelta={_car.WorldPosition.z-before.z:F2}, seated={seat.Driver == player}" );
			bool stable = seat.Driver == player && peak < 2f && MathF.Abs( _car.WorldPosition.z - before.z ) < 1f && _car.GroundedWheels == 4;
			Log.Info( $"Vehicle entry regression {(stable ? "PASS" : "FAIL")}: full player entry stays grounded." );
			Log.Info( $"Vehicle entry regression {(health.CurrentHealth == 100f && !health.IsDowned && !health.IsDead ? "PASS" : "FAIL")}: full player stays alive without god mode." );
			seat.RequestExit( player );
			await Task.DelaySeconds( 0.4f );
			Log.Info( $"Vehicle entry regression EXIT: pos={player.WorldPosition}, velocity={player.Body.Velocity}, hp={health.CurrentHealth}, down={health.IsDowned}, dead={health.IsDead}, mode={player.Mode?.GetType().Name}" );
			bool restored = originalContacts.All( x => !x.Key.IsValid() || x.Key.EnableSolidCollisions == x.Value );
			Log.Info( $"Vehicle entry regression {(!player.GetComponent<VBVehiclePlayerDriver>().IsDriving && player.Body.Enabled && player.UseInputControls && restored ? "PASS" : "FAIL")}: exit restores controls and {originalContacts.Count} bone collision states." );
			Log.Info( $"Vehicle entry regression {(health.CurrentHealth == 100f && !health.IsDowned && !health.IsDead ? "PASS" : "FAIL")}: exit produces no stale fall or impact damage." );
		}
		catch ( TaskCanceledException ) { Log.Info( "Vehicle entry regression interrupted by scene stop." ); }
		catch ( Exception e ) { Log.Error( $"Vehicle entry regression EXCEPTION: {e}" ); }
		finally
		{
			if ( _playerObject.IsValid() ) { _playerObject.SetParent( null, true ); _playerObject.Destroy(); }
			if ( _floor.IsValid() ) _floor.Destroy();
			if ( fixture.IsValid() ) fixture.Destroy();
		}
	}
}
