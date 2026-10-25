using System;
using System.Threading.Tasks;
using Sandbox;

// Requires a real connected second client. Does not control either user's pawn or camera.
public sealed class VBNpcNetworkRegression : Component
{
	[Sync( SyncFlags.FromHost )] private Guid ClientId { get; set; }
	private readonly List<GameObject> _objects = new();
	private int _failures, _reports;
	private bool _done;
	[ConCmd( "vb_npc_network_regression" )]
	public static void Run()
	{
		if ( !Networking.IsHost || Game.ActiveScene is null ) return;
		var remote = Connection.All.FirstOrDefault( c => c.IsActive && c != Connection.Local );
		if ( remote is null ) { Log.Warning( "NPC network regression requires a real second client." ); return; }
		var fixture = new GameObject( true, "NPC network regression" ).AddComponent<VBNpcNetworkRegression>();
		fixture.ClientId = remote.Id; fixture.GameObject.NetworkSpawn(); _ = fixture.VerifyHost( remote );
	}
	private void Check( bool pass, string label )
	{
		if ( !pass ) _failures++;
		Log.Info( $"NPC network {(pass ? "PASS" : "FAIL")}: {label}" );
	}
	[Rpc.Host]
	private void Report( bool pass, string label )
	{
		if ( Rpc.Caller?.Id != ClientId ) return;
		_reports++; Check( pass, $"CLIENT {label}" );
	}
	[Rpc.Host] private void Done() { if ( Rpc.Caller?.Id == ClientId ) _done = true; }
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void VerifyMovingClient( VBNpcLocomotion npc )
	{
		if ( Networking.IsHost || Connection.Local.Id != ClientId ) return;
		Report( npc.Velocity.WithZ( 0f ).Length > 30f, "moving NPC publishes velocity for remote walking animation" );
		Report( npc.IsGrounded, "remote animation receives the host's grounded state" );
	}
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void VerifyClient( VBNpcLocomotion npc, Vector3 expected, PlayerController player, VBCombatWeapon mp5, VBCombatWeapon p90 )
	{
		if ( Networking.IsHost || Connection.Local.Id != ClientId ) return;
		_ = VerifyRemote( npc, expected, player, mp5, p90 );
	}
	private async Task VerifyRemote( VBNpcLocomotion npc, Vector3 expected, PlayerController player, VBCombatWeapon mp5, VBCombatWeapon p90 )
	{
		try
		{
			await Task.DelaySeconds( 0.7f );
			Report( npc.IsProxy && !npc.Agent.UpdatePosition, "client navigation cannot drive the NPC root" );
			Report( Vector3.DistanceBetween( npc.WorldPosition, expected ) < 4f, "host physical position is replicated" );
			Report( npc.Velocity.WithZ( 0f ).Length < 3f, "stopped motion has no residual slide on client" );
			var inventory = player.GetComponent<VBWeaponInventoryComponent>();
			Report( !player.IsProxy && player.Network.Owner == Connection.Local, "test inventory belongs to remote player" );
			Report( inventory.GetSlot( VBWeaponSelectionController.ThrowableSlot ) is null, "remote player receives no spawn grenades" );
			inventory.SwapWeapon( mp5 );
			TimeSince elapsed = 0f;
			while ( elapsed < 4f && inventory.ActiveItem != mp5 ) await Task.DelaySeconds( 0.05f );
			Report( inventory.ActiveItem == mp5 && mp5.Inventory == inventory && p90.Inventory is null, "owner RPC performs the voluntary swap on host" );
			Report( mp5.Clip1 == 7 && p90.Clip1 == 11 && inventory.GetAmmo( mp5.PrimaryAmmoType ) == 73, "remote swap conserves magazines and shared ammo" );
			Done();
		}
		catch ( Exception exception ) { Report( false, exception.ToString() ); Done(); }
	}
	private async Task VerifyHost( Connection remote )
	{
		try
		{
			var origin = new Vector3( 40000f, 40000f, 4000f );
			var floor = new GameObject( true, "Network NPC test floor" ); _objects.Add( floor ); floor.WorldPosition = origin - Vector3.Up * 16f;
			var collider = floor.AddComponent<BoxCollider>(); collider.Scale = new Vector3( 1600f, 1600f, 32f ); collider.Static = true; floor.NetworkSpawn();
			var actor = GameObject.GetPrefab( "creatures/cartel_soldier/cartel_soldier.prefab" ).Clone( new CloneConfig { StartEnabled = false, Transform = new Transform( origin + Vector3.Up * 2f ) } );
			_objects.Add( actor ); actor.GetComponent<VBSoldierController>( true ).Enabled = false; actor.GetComponent<VBAiPerceptionComponent>( true ).Enabled = false;
			actor.Enabled = true; actor.NetworkSpawn();
			var playerObject = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( new CloneConfig { StartEnabled = false, Transform = new Transform( origin + Vector3.Right * 300f + Vector3.Up * 2f ) } );
			_objects.Add( playerObject ); var player = playerObject.GetComponent<PlayerController>( true );
			player.UseInputControls = player.UseLookControls = player.UseCameraControls = player.EnablePressing = false;
			playerObject.GetComponent<VBWeaponSelectionController>( true ).Enabled = false;
			playerObject.GetComponent<VBVehiclePlayerDriver>( true ).UsePlayerInput = false;
			var hud = playerObject.Children.FirstOrDefault( o => o.Name == "HUD" ); if ( hud.IsValid() ) hud.Enabled = false;
			var inventory = playerObject.GetComponent<VBWeaponInventoryComponent>( true ); inventory.ManualPumping = true; inventory.UsesLoadout = false; inventory.GiveOnStart = false;
			playerObject.Enabled = true; playerObject.NetworkSpawn( remote );
			await Task.DelaySeconds( 0.4f ); player.Body.MotionEnabled = false;
			var p90 = inventory.Pickup( "weapons/p90/p90.prefab", 0 ) as VBCombatWeapon;
			p90.Clip1 = 11; inventory.SetAmmo( p90.PrimaryAmmoType, 73 ); inventory.Switch( p90, false );
			var mp5Object = GameObject.GetPrefab( "weapons/mp5/mp5.prefab" ).Clone( player.WorldPosition + Vector3.Forward * 24f + Vector3.Up * 12f );
			_objects.Add( mp5Object ); mp5Object.NetworkSpawn(); var mp5 = mp5Object.GetComponent<VBCombatWeapon>(); mp5.Clip1 = 7;
			mp5Object.GetComponent<Rigidbody>().MotionEnabled = false;
			var motion = actor.GetComponent<VBNpcLocomotion>(); motion.Agent.MaxSpeed = 100f;
			motion.WalkDirectlyTo( actor.WorldPosition + Vector3.Forward * 240f );
			await Task.DelaySeconds( 0.8f ); VerifyMovingClient( motion );
			await Task.DelaySeconds( 2f ); motion.CancelDirectMove(); await Task.DelaySeconds( 0.8f );
			Check( !motion.Agent.UpdatePosition && Vector3.DistanceBetween( actor.WorldPosition, origin ) > 40f, "host physically moves NPC without navigation projection" );
			VerifyClient( motion, actor.WorldPosition, player, mp5, p90 );
			TimeSince elapsed = 0f; while ( elapsed < 10f && !_done ) await Task.DelaySeconds( 0.1f );
			Check( _done && _reports >= 9, "real remote client completed its assertions" );
			Check( mp5.Inventory == inventory && p90.Inventory is null, "host agrees with remote weapon ownership" );
			Log.Info( $"NPC network FINISHED: {_failures} failure(s)." );
		}
		catch ( Exception exception ) { Log.Error( $"NPC network EXCEPTION: {exception}" ); }
		finally
		{
			foreach ( var obj in _objects.Where( o => o.IsValid() ) ) obj.Destroy();
			if ( this.IsValid() ) GameObject.Destroy();
		}
	}
}
