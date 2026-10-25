if ( !Game.IsPlaying || Game.ActiveScene is null ) throw new Exception( "Start Play first." );
var go = GameObject.GetPrefab( "water/prefabs/fft_ocean.prefab" ).Clone( new Vector3( 0, 0, -32 ) );
var water = go.GetComponent<VoidBreach.Ocean.VBFFTOcean>();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var results = new System.Collections.Generic.List<object>();
double Measure( Action action, int repeats = 20 )
{
	action(); var t = System.Diagnostics.Stopwatch.StartNew(); for ( var i = 0; i < repeats; i++ ) action(); return t.Elapsed.TotalMilliseconds / repeats;
}
try
{
	await Task.Delay( 2500, cancellationToken );
	var bridge = go.GetComponentInChildren<VoidBreach.Water.VBFluidVolumeBridge>();
	var bridgeTick = bridge.GetType().GetMethod( "OnFixedUpdate", flags );
	var bodies = Game.ActiveScene.GetAll<VoidBreach.Water.VBFluidBody>().ToArray();
	results.Add( new { stage = "counts", touching = bridge.VolumeCollider.Touching.Count(), fluidBodies = bodies.Length,
		dynamicBodies = bodies.Count( b => b.Body.IsValid() && b.Body.MotionEnabled ),
		nativeDynamic = bodies.Count( b => b.Body.IsValid() && b.Body.PhysicsBody.IsValid() && b.Body.PhysicsBody.MotionEnabled ),
		sleeping = bodies.Count( b => b.Body.IsValid() && b.Body.Sleeping ), tracked = water.TrackedInteractionCount } );
	results.Add( new { stage = "bridge", ms = Measure( () => bridgeTick.Invoke( bridge, null ) ) } );
	foreach ( var name in new[] { "EnsureDomain", "EnsureSpectrum", "EnsurePhysicsVolume", "EnsureGeometry", "ApplyAttributes", "Refresh" } )
	{
		var method = water.GetType().GetMethod( name, flags );
		results.Add( new { stage = name, ms = Measure( () => method.Invoke( water, null ) ) } );
	}
	var step = water.GetType().GetMethod( "StepInteractions", flags );
	results.Add( new { stage = "StepInteractions_idle", ms = Measure( () => step.Invoke( water, new object[] { 0f } ) ) } );
	results.Add( new { stage = "fourier_query", ms = Measure( () => water.TrySample( water.WorldPosition, out _ ), 200 ) } );
	foreach ( var b in bodies )
	{
		var watch = System.Diagnostics.Stopwatch.StartNew(); b.GetType().GetMethod( "OnFixedUpdate", flags ).Invoke( b, null );
		if ( watch.Elapsed.TotalMilliseconds > 0.1 ) results.Add( new { stage = b.GameObject.Name, ms = watch.Elapsed.TotalMilliseconds } );
	}
}
finally { go.Destroy(); }
return results;
