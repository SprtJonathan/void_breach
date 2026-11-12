if ( !Game.IsPlaying || !Networking.IsHost || Connection.All.Any( c => c.IsActive && c != Connection.Local ) )
    return "Local Play required.";

var results = new System.Collections.Generic.List<object>();
var origin = new Vector3( 6000, 6000, 1000 );
GameObject road = null;
try
{
    road = new GameObject( false, "Soreo5 exit test floor" );
    road.WorldPosition = origin - Vector3.Up * 16;
    var ground = road.AddComponent<BoxCollider>();
    ground.Static = true;
    ground.Scale = new Vector3( 1800, 1800, 32 );
    road.Enabled = true;

    foreach ( float yaw in new[] { -135f, 0f, 45f, 90f } )
    {
        cancellationToken.ThrowIfCancellationRequested();
        GameObject bus = null, actor = null;
        try
        {
            bus = GameObject.GetPrefab( "vehicles/soreo5/soreo5.prefab" ).Clone( origin + Vector3.Up * 40 );
            bus.WorldRotation = Rotation.FromYaw( yaw );
            var car = bus.GetComponent<VBVehicleController>();
            var cabin = bus.GetComponent<VBVehicleCabin>();
            var seat = cabin.DriverSeat;
            await Task.Delay( 1300, cancellationToken );

            var exit = bus.Children.First( child => child.Name == "Driver aisle exit" );
            actor = GameObject.GetPrefab( "player/player controller.prefab" ).Clone( exit.WorldPosition + Vector3.Up * 2 );
            actor.WorldRotation = bus.WorldRotation;
            var player = actor.GetComponent<PlayerController>();
            var bridge = actor.GetComponent<VBVehiclePlayerDriver>();
            actor.GetComponent<VBHealthComponent>().GodMode = true;
            player.UseCameraControls = false;
            player.UseInputControls = false;
            player.UseLookControls = false;
            player.UseButton = "";
            bridge.UsePlayerInput = false;
            if ( actor.Children.FirstOrDefault( child => child.Name == "HUD" ) is { } hud ) hud.Enabled = false;
            actor.NetworkSpawn( Connection.Local );
            await Task.Delay( 400, cancellationToken );
            player.StartPressing( seat );
            player.StopPressing();
            await Task.Delay( 200, cancellationToken );
            if ( !bridge.IsDriving ) throw new InvalidOperationException( $"Could not enter the seat at yaw {yaw}." );

            ((PlayerController.IEvents)player.GetComponent<Sandbox.Movement.SitMoveMode>()).FailPressing();
            await Task.Delay( 600, cancellationToken );
            var local = bus.WorldTransform.PointToLocal( actor.WorldPosition );
            var support = bus.Scene.Trace.Ray( actor.WorldPosition + Vector3.Up * 3, actor.WorldPosition - Vector3.Up * 6 )
                .IgnoreGameObjectHierarchy( actor ).WithoutTags( new[] { "trigger", "water" } ).Run();
            bool passed = !bridge.IsDriving && player.Body.Enabled && player.IsOnGround
                && cabin.BoardingArea.Contains( local ) && (actor.WorldPosition - exit.WorldPosition).Length < 8
                && support.Hit && support.Body == car.Body.PhysicsBody
                && cabin.FrontDoor.OpenFraction < .01f && cabin.MiddleDoor.OpenFraction < .01f;
            results.Add( new { yaw, passed, local = local.ToString(), player.IsOnGround, support = support.GameObject?.Name } );
            if ( !passed ) throw new InvalidOperationException( $"Exit left the bus aisle at yaw {yaw}." );
        }
        finally
        {
            if ( actor.IsValid() ) { actor.SetParent( null, true ); actor.Destroy(); }
            if ( bus.IsValid() ) bus.Destroy();
        }
    }
}
catch ( Exception exception )
{
    results.Add( new { error = exception.ToString() } );
}
finally
{
    if ( road.IsValid() ) road.Destroy();
}
return results;
