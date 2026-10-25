// Dry sleeping bodies must still react when the waterline reaches them.
if ( !Game.IsPlaying || Game.ActiveScene is null ) throw new Exception( "Start Play first." );
var scene = Game.ActiveScene;
var objects = new System.Collections.Generic.List<GameObject>();
var anchor = new Vector3( 61000, 63000, 8000 );
GameObject Make( string name, Vector3 position )
{
	var go = new GameObject( false, name ); go.SetParent( scene, false ); go.WorldPosition = position;
	go.NetworkMode = NetworkMode.Never; objects.Add( go ); return go;
}
var results = new System.Collections.Generic.List<object>();
try
{
	var oceanObject = Make( "Sleeping body water", anchor );
	var ocean = oceanObject.AddComponent<VoidBreach.Ocean.VBFFTOcean>();
	ocean.Shape = VoidBreach.Ocean.FftWaterShape.Lake; ocean.Size = new Vector2( 1200, 1200 );
	ocean.WaveHeight = 0; ocean.NativePhysics = true; ocean.Interactions = false; ocean.UnderwaterEffects = false;
	oceanObject.Enabled = true;
	var floorObject = Make( "Dry support", anchor + Vector3.Up * 16 );
	floorObject.AddComponent<BoxCollider>().Scale = new Vector3( 200, 200, 16 ); floorObject.Enabled = true;
	var propObject = Make( "Ordinary sleeping dry prop", anchor + Vector3.Up * 40 );
	propObject.AddComponent<BoxCollider>().Scale = new Vector3( 24 );
	var body = propObject.AddComponent<Rigidbody>(); body.MassOverride = 2; propObject.Enabled = true;
	await Task.Delay( 2000, cancellationToken ); body.Sleeping = true;
	await Task.Delay( 250, cancellationToken );
	var sleeping = body.Sleeping; var dryHeight = propObject.WorldPosition.z;
	results.Add( new { name = "DRY_BODY_RESTS", pass = sleeping, detail = $"sleeping={sleeping}, z={dryHeight}" } );
	var startClock = ocean.SimulationClock;
	oceanObject.WorldPosition = anchor + Vector3.Up * 80;
	for ( var i = 0; i < 30 && propObject.WorldPosition.z < dryHeight + 8; i++ ) await Task.Delay( 50, cancellationToken );
	results.Add( new { name = "SLEEPING_BODY_WAKES_IN_RISING_WATER", pass = propObject.WorldPosition.z > dryHeight + 8,
		detail = $"rise={propObject.WorldPosition.z - dryHeight}, seconds={ocean.SimulationClock - startClock}, velocity={body.Velocity}" } );
}
finally { foreach ( var go in objects.AsEnumerable().Reverse() ) if ( go.IsValid() ) go.Destroy(); }
return results;
