using System;
using System.Threading.Tasks;
using Sandbox;

public sealed class VBVehicleNetworkRegression : Component
{
	private static VBVehicleNetworkRegression _running;
	private readonly List<GameObject> _objects = new();
	private readonly Vector3 _origin = new( 28000f, 16000f, 3000f );
	[Sync( SyncFlags.FromHost )] public Guid TestOwner { get; private set; }
	private int _failures, _reports;
	private bool _clientDone;
	[ConCmd( "vb_vehicle_network_regression" )]
	public static void Run()
	{
		if ( !Game.IsPlaying || !Networking.IsHost || _running.IsValid() ) return;
		var remote = Connection.All.FirstOrDefault( connection => connection.IsActive && connection != Connection.Local );
		if ( remote is null ) { Log.Warning( "Vehicle network requires a real connected second instance." ); return; }
		_running = new GameObject( true, "Vehicle network regression" ).AddComponent<VBVehicleNetworkRegression>();
		_running.TestOwner = remote.Id; _running.GameObject.NetworkSpawn(); _ = _running.VerifyHost( remote );
	}
	private void Check( bool pass, string label, string details )
	{
		if ( !pass ) _failures++;
		Log.Info( $"Vehicle network {(pass ? "PASS" : "FAIL")}: {label}: {details}" );
	}
	[Rpc.Host]
	private void Report( string label, bool pass, string details )
	{
		if ( Rpc.Caller.Id != TestOwner ) return;
		_reports++; Check( pass, $"CLIENT {label}", details );
	}
	[Rpc.Host]
	private void Done() { if ( Rpc.Caller.Id == TestOwner ) _clientDone = true; }
	private async Task WaitUntil( Func<bool> condition, float seconds = 4f )
	{
		for ( int sample = 0; sample < seconds / 0.05f && !condition(); sample++ ) await Task.DelaySeconds( 0.05f );
	}
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void BeginClient( VBVehicleController car, PlayerController player )
	{
		if ( Networking.IsHost || Connection.Local.Id != TestOwner ) return;
		_ = VerifyClient( car, player );
	}
	private async Task VerifyClient( VBVehicleController car, PlayerController player )
	{
		try
		{
			await Task.DelaySeconds( 1f );
			var bridge = player.GetComponent<VBVehiclePlayerDriver>(); var driver = car.GetComponent<VBVehicleDriverSeat>(); var turret = car.GetComponent<VBVehicleTurret>();
			Report( "host-owned streamed chassis", car.Network.Active && car.IsProxy && car.GameObject.NetworkMode == NetworkMode.Object, $"mode={car.GameObject.NetworkMode}, proxy={car.IsProxy}, owner={car.Network.Owner?.Id}" );
			var renderer = car.GetComponentInChildren<SkinnedModelRenderer>();
			Report( "Humvee model loaded", renderer.IsValid() && renderer.Model is not null && !renderer.Model.IsError && renderer.SceneObject.IsValid(), "model and scene object exist on remote client" );
			var start = car.WorldPosition;
			player.StartPressing( driver ); await WaitUntil( () => bridge.IsDriving && driver.Occupant == player ); player.StopPressing();
			Report( "native entry from player 2", bridge.IsDriving && driver.Occupant == player && driver.GetOccupant() == player, $"binding={bridge.Seating}, parent={player.GameObject.Parent?.Name}" );
			Report( "player keeps network ownership", player.Network.Owner == Connection.Local && !player.IsProxy, $"owner={player.Network.Owner?.Id}" );
			Report( "seated transform has one source", (player.Network.Flags & NetworkFlags.NoTransformSync) != 0 && !player.Body.Enabled && !player.ColliderObject.Enabled, $"flags={player.Network.Flags}" );
			uint originalRevision = bridge.SeatingRevision;
			float maximumSeatError = 0f;
			for ( int sample = 0; sample < 80; sample++ )
			{
				bridge.TransmitControls( new( 1f, 0.12f, 0f, false, true ), player.EyeTransform.Rotation.Angles(), true, false );
				await Task.DelaySeconds( 0.04f ); maximumSeatError = MathF.Max( maximumSeatError, (player.WorldPosition - driver.SeatPosition.WorldPosition).Length );
			}
			Report( "moving model replicated", (car.WorldPosition - start).Length > 350f && car.SpeedKmh > 10f && car.LinearVelocity.Length > 100f, $"distance={(car.WorldPosition - start).Length:F1}, speed={car.SpeedKmh:F1}, velocity={car.LinearVelocity.Length:F1}" );
			Report( "player stays attached while driving", maximumSeatError < 0.5f, $"maximum error={maximumSeatError:F4}" );
			Report( "wheel and turbo states replicated", car.GroundedWheels >= 2 && car.FrontLeft.Grounded && MathF.Abs( car.FrontLeft.SuspensionLength - car.FrontLeftPresentation.x ) < 0.01f && car.FrontLeftSpin > 360f && car.IsTurboActive, $"grounded={car.GroundedWheels}, spring={car.FrontLeft.SuspensionLength:F2}, spin={car.FrontLeftSpin:F0}, turbo={car.IsTurboActive}" );
			for ( int sample = 0; sample < 60; sample++ ) { bridge.TransmitControls( new( 0f, 0f, 1f, true ), new(), true, false ); await Task.DelaySeconds( 0.04f ); }
			for ( int sample = 0; sample < 5; sample++ ) { bridge.SendControls( 1, originalRevision, new( 1f, 0f, 0f, false ), new(), false, false ); await Task.DelaySeconds( 0.04f ); }
			Report( "reordered controls ignored", car.SpeedKmh < 1f, $"speed={car.SpeedKmh:F2}" );
			driver.RequestSeatChange( player, turret.GunnerSeat ); await WaitUntil( () => bridge.ActiveSeat == turret.GunnerSeat && turret.GunnerSeat.Occupant == player );
			Report( "driver to gunner replicated", bridge.ActiveSeat == turret.GunnerSeat && driver.Occupant is null && turret.GunnerSeat.GetOccupant() == player && !player.Body.Enabled, $"revision={bridge.SeatingRevision}" );
			for ( int sample = 0; sample < 30; sample++ )
			{
				var aim = (car.WorldRotation * Rotation.From( 20f, 45f, 0f )).Angles();
				bridge.TransmitControls( new( 1f, 0f, 0f, false, true ), aim, true, false ); await Task.DelaySeconds( 0.04f );
			}
			Report( "turret servo and operator replicated", turret.Operator == player && MathF.Abs( turret.Yaw - 45f ) < 1f && MathF.Abs( turret.Pitch - 20f ) < 1f, $"yaw={turret.Yaw:F2}, pitch={turret.Pitch:F2}" );
			Report( "gunner cannot drive", car.SpeedKmh < 1f && !car.IsTurboActive, $"speed={car.SpeedKmh:F2}" );
			turret.GunnerSeat.RequestSeatChange( player, driver ); await WaitUntil( () => bridge.IsDriving && driver.Occupant == player );
			Report( "return to driver replicated", bridge.IsDriving && turret.GunnerSeat.Occupant is null, $"revision={bridge.SeatingRevision}" );
			for ( int sample = 0; sample < 10; sample++ ) { bridge.SendControls( 10000u + (uint)sample, originalRevision, new( 1f, 0f, 0f, false ), new(), false, false ); await Task.DelaySeconds( 0.04f ); }
			Report( "previous seat epoch ignored", car.SpeedKmh < 1f, $"speed={car.SpeedKmh:F2}, old={originalRevision}, current={bridge.SeatingRevision}" );
			for ( int sample = 0; sample < 15; sample++ ) { bridge.TransmitControls( VBVehicleControls.Parked, new(), false, false ); await Task.DelaySeconds( 0.04f ); }
			driver.RequestExit( player ); await WaitUntil( () => !bridge.IsSeated && player.GameObject.IsRoot ); await Task.DelaySeconds( 0.4f );
			Report( "exit and flags restored", !bridge.IsSeated && player.GameObject.IsRoot && player.Body.Enabled && player.ColliderObject.Enabled && (player.Network.Flags & NetworkFlags.NoTransformSync) == 0, $"seated={bridge.IsSeated}, root={player.GameObject.IsRoot}, parent={player.GameObject.Parent?.Name}, body={player.Body.Enabled}, capsule={player.ColliderObject.Enabled}, flags={player.Network.Flags}, position={player.WorldPosition}" );
			Report( "health without god mode", player.GetComponent<VBHealthComponent>().CurrentHealth == 100f && !player.GetComponent<VBHealthComponent>().GodMode, "full player stays healthy" );
			Done();
		}
		catch ( TaskCanceledException ) { }
		catch ( Exception exception ) { Report( "exception", false, exception.ToString() ); Done(); }
	}
	private async Task VerifyHost( Connection remote )
	{
		var fixture = GameObject;
		try
		{
			var floor = new GameObject( true, "Network regression floor" ); _objects.Add( floor ); floor.WorldPosition = _origin + Vector3.Down * 16f;
			floor.AddComponent<BoxCollider>().Scale = new( 6000f, 4000f, 32f ); floor.NetworkSpawn();
			var car = GameObject.GetPrefab( "vehicles/utility_vehicle.prefab" ).Clone( _origin + Vector3.Up * 40f ); _objects.Add( car ); car.NetworkSpawn();
			var vehicle = car.GetComponent<VBVehicleController>();
			var pawn = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( _origin + new Vector3( 0f, 100f, 2f ) ); _objects.Add( pawn );
			var player = pawn.GetComponent<PlayerController>(); player.UseInputControls = player.UseLookControls = player.UseCameraControls = player.EnablePressing = false;
			player.WishVelocity = Vector3.Zero; pawn.GetComponent<VBVehiclePlayerDriver>().UsePlayerInput = false; pawn.GetComponent<VBHealthComponent>().GodMode = false;
			var hud = pawn.Children.FirstOrDefault( child => child.Name == "HUD" ); if ( hud.IsValid() ) hud.Enabled = false;
			pawn.NetworkSpawn( remote ); await Task.DelaySeconds( 2f );
			Check( vehicle.Network.Active && !vehicle.IsProxy && vehicle.GroundedWheels == 4, "host chassis configured", $"mode={car.NetworkMode}, owner={vehicle.Network.Owner?.Id}, ground={vehicle.GroundedWheels}" );
			Check( vehicle.GetComponentsInChildren<VBVehicleWheel>().All( wheel => wheel.GameObject.NetworkMode == NetworkMode.Snapshot && wheel.Network.RootGameObject == car ), "wheels share chassis network root", "no independent wheel transform streams" );
			var start = vehicle.WorldPosition; BeginClient( vehicle, player );
			await WaitUntil( () => _clientDone, 40f );
			Check( _clientDone && _reports >= 16, "remote scenario completed", $"reports={_reports}, done={_clientDone}" );
			Check( (vehicle.WorldPosition - start).Length > 350f, "remote input drives host simulation", $"distance={(vehicle.WorldPosition - start).Length:F1}" );
			Check( !player.GetComponent<VBVehiclePlayerDriver>().IsSeated && player.GameObject.IsRoot && car.GetComponent<VBVehicleDriverSeat>().Occupant is null, "host sees same exit state", $"parent={player.GameObject.Parent?.Name}, root={player.GameObject.IsRoot}" );
			Check( player.Network.Owner == remote && !vehicle.IsProxy && vehicle.GroundedWheels == 4, "authority stays split correctly", "client owns actor; host owns physics" );
			Log.Info( $"Vehicle network FINISHED: {_failures} failure(s)." );
		}
		catch ( TaskCanceledException ) { Log.Info( "Vehicle network interrupted by scene stop." ); }
		catch ( Exception exception ) { Log.Error( $"Vehicle network EXCEPTION: {exception}" ); }
		finally { foreach ( var obj in _objects.Where( obj => obj.IsValid() ).ToArray() ) obj.Destroy(); if ( fixture.IsValid() ) fixture.Destroy(); }
	}
	[ConCmd( "vb_vehicle_network_audit" )]
	public static void Audit()
	{
		Log.Info( $"Vehicle network audit: playing={Game.IsPlaying}, host={Networking.IsHost}, active={Networking.IsActive}, Object={(int)NetworkMode.Object}, Snapshot={(int)NetworkMode.Snapshot}, local={Connection.Local?.Id}" );
		if ( !Game.IsPlaying || Game.ActiveScene is null ) return;
		foreach ( var car in Game.ActiveScene.GetAll<VBVehicleController>() )
		{
			Log.Info( $"Vehicle network audit: car={car.GameObject.Id}, mode={car.GameObject.NetworkMode}, active={car.Network.Active}, owner={car.Network.Owner?.Id}, proxy={car.IsProxy}, pos={car.WorldPosition}, ground={car.GroundedWheels}" );
			foreach ( var wheel in car.GetComponentsInChildren<VBVehicleWheel>() ) Log.Info( $"Vehicle network audit: wheel={wheel.Id}, mode={wheel.GameObject.NetworkMode}, owner={wheel.Network.Owner?.Id}, suspension={wheel.SuspensionLength}" );
		}
	}
}
