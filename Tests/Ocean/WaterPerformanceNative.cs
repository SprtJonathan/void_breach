// Run in basic_example Play. Comparisons include the existing scene workload.
if ( !Game.IsPlaying || Game.ActiveScene is null ) throw new Exception( "Start Play first." );
var scene = Game.ActiveScene;
var water = scene.GetAll<VoidBreach.Ocean.VBFFTOcean>().FirstOrDefault() ?? throw new Exception( "No FFT water in this scene." );
var enabled = water.Enabled; var interactions = water.Interactions;
var results = new System.Collections.Generic.List<object>();
async Task Frames( int count )
{
	var until = Application.FrameCount + (ulong)count;
	while ( Application.FrameCount < until )
	{
		if ( !Game.IsPlaying || Game.ActiveScene != scene ) throw new Exception( "Benchmark interrupted by a scene/Play change." );
		await Task.Delay( 5, cancellationToken );
	}
}
double Percentile( System.Collections.Generic.List<double> values, float fraction )
{
	values.Sort(); return values[Math.Clamp( (int)(values.Count * fraction), 0, values.Count - 1 )];
}
try
{
	for ( var i = 0; i < 600 && water.SimulationClock < 0.3f; i++ ) await Task.Delay( 50, cancellationToken );
	foreach ( var phase in new[] { "full", "without_interactions", "without_water", "full_repeat" } )
	{
		water.Enabled = phase != "without_water";
		water.Interactions = phase != "without_interactions";
		await Frames( 30 );
		var frame = new System.Collections.Generic.List<double>(); var gpu = new System.Collections.Generic.List<double>();
		for ( var i = 0; i < 90; i++ )
		{
			await Frames( 1 ); frame.Add( Sandbox.Diagnostics.PerformanceStats.FrameTime * 1000 );
			gpu.Add( Sandbox.Diagnostics.PerformanceStats.GpuFrametime );
		}
		double contactsMs = 0; var contacts = 0;
		if ( phase is "full" or "full_repeat" )
		{
			var field = typeof(VoidBreach.Ocean.VBFFTOcean).GetField( "_activeContacts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
			var update = typeof(VoidBreach.Ocean.VBFFTOcean).GetMethod( "UpdateContact", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
			var targets = ((System.Collections.IEnumerable)field.GetValue( water )).Cast<object>().ToArray(); contacts = targets.Length;
			// Same stationary bounds, no scene step between calls: isolate idle tracker work.
			var timer = System.Diagnostics.Stopwatch.StartNew();
			for ( var repeat = 0; repeat < 4; repeat++ ) foreach ( var contact in targets ) update.Invoke( water, new object[] { contact, 1f / 60 } );
			contactsMs = timer.Elapsed.TotalMilliseconds / 4;
		}
		results.Add( new { phase, frameMedianMs = Percentile( frame, 0.5f ), frameP95Ms = Percentile( frame, 0.95f ),
			gpuMedianMs = Percentile( gpu, 0.5f ), vertices = water.SurfaceVertexCount, tracked = water.TrackedInteractionCount,
			contacts, stationaryContactPassMs = contactsMs } );
	}
}
finally
{
	if ( water.IsValid() ) { water.Enabled = enabled; water.Interactions = interactions; }
}
return results;
