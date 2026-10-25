// Native editor script. Fixed camera, actual prefab, reversible ablations.
// GPU timestamps are engine scopes, never RenderToTexture + a CPU fence.
if ( !Game.IsPlaying || Game.ActiveScene is null ) throw new Exception( "Start Play first." );
var scene = Game.ActiveScene;
var objects = new System.Collections.Generic.List<GameObject>();
var cameras = new System.Collections.Generic.Dictionary<PlayerController, bool>();
var profilerEnabled = Sandbox.Diagnostics.GpuProfilerStats.Enabled;
var water = scene.GetAll<VoidBreach.Ocean.VBFFTOcean>().FirstOrDefault();
var existing = water.IsValid();
if ( !existing )
{
	var go = GameObject.GetPrefab( "water/prefabs/fft_ocean.prefab" ).Clone( new Vector3( 0, 0, -32 ) );
	objects.Add( go ); water = go.GetComponent<VoidBreach.Ocean.VBFFTOcean>();
}
var saved = new { water.Enabled, water.Interactions, water.NativePhysics, water.CausticStrength, water.InteractionMeshSpacing, water.DebugView };
var cameraObject = new GameObject( false, "Ocean optimization fixed camera" ); objects.Add( cameraObject );
cameraObject.SetParent( scene, false ); cameraObject.NetworkMode = NetworkMode.Never;
var camera = cameraObject.AddComponent<CameraComponent>(); camera.Priority = 30000; camera.IsMainCamera = true;
camera.FieldOfView = 70; camera.ZFar = 500000; cameraObject.Enabled = true;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var rendererField = typeof(VoidBreach.Ocean.VBFFTOcean).GetField( "_renderer", flags );
var hookField = typeof(VoidBreach.Ocean.VBFFTOcean).GetField( "_simulationObject", flags );
var timelineType = typeof(Graphics).Assembly.GetType( "Sandbox.Diagnostics.GpuFrameTimeline" );
var capture = timelineType.GetMethod( "Capture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static );
var eventProperty = timelineType.GetProperty( "Events" );
var frameProperty = timelineType.GetProperty( "FrameId" );
var hub = typeof(Graphics).Assembly.GetType( "Sandbox.Diagnostics.PerfHub" );
var collectFor = hub.GetMethod( "CollectFor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static );
var results = new System.Collections.Generic.List<object>();
async Task Frame()
{
	var current = Application.FrameCount;
	while ( Application.FrameCount == current ) await Task.Delay( 1, cancellationToken );
	if ( !Game.IsPlaying || Game.ActiveScene != scene ) throw new Exception( "Scene changed during profiling." );
	foreach ( var p in scene.GetAll<PlayerController>() )
	{
		if ( !cameras.ContainsKey( p ) ) cameras.Add( p, p.UseCameraControls ); p.UseCameraControls = false;
	}
	camera.IsMainCamera = true;
	Sandbox.Diagnostics.GpuProfilerStats.Enabled = true;
}
double Percentile( System.Collections.Generic.List<double> values, double q )
{
	if ( values.Count == 0 ) return double.NaN;
	values.Sort(); return values[Math.Min( (int)(values.Count * q), values.Count - 1 )];
}
try
{
	collectFor.Invoke( null, new object[] { "gpu", 300f } );
	for ( var i = 0; i < 100; i++ ) await Frame();
	foreach ( var view in new[] { "ocean", "shore" } )
	{
		cameraObject.WorldPosition = water.WorldPosition + (view == "ocean" ? new Vector3( 11000, 11000, 150 ) : new Vector3( 0, -700, 180 ));
		cameraObject.WorldRotation = Rotation.LookAt( view == "ocean" ? new Vector3( 0, 1, -0.25f ) : new Vector3( 500, 700, -212 ) );
		foreach ( var phase in new[] { "full", "no_caustics", "coarse_mesh", "geometry_only", "no_interactions", "no_physics", "no_surface", "no_water", "full_repeat" } )
		{
			water.Enabled = true; water.Interactions = saved.Interactions; water.NativePhysics = saved.NativePhysics;
			water.CausticStrength = saved.CausticStrength; water.InteractionMeshSpacing = saved.InteractionMeshSpacing; water.DebugView = saved.DebugView;
			if ( phase == "no_caustics" ) water.CausticStrength = 0;
			if ( phase == "coarse_mesh" ) water.InteractionMeshSpacing = 16;
			if ( phase == "geometry_only" ) water.DebugView = VoidBreach.Water.WaterDebugView.SurfaceGeometry;
			if ( phase == "no_interactions" ) water.Interactions = false;
			if ( phase == "no_physics" ) water.NativePhysics = false;
			if ( phase == "no_water" ) water.Enabled = false;
			for ( var i = 0; i < 45; i++ ) await Frame();
			var renderer = rendererField.GetValue( water ) as ModelRenderer;
			if ( phase == "no_surface" && renderer.IsValid() ) renderer.Enabled = false;
			var frames = new System.Collections.Generic.List<double>(); var totals = new System.Collections.Generic.List<double>();
			var cpu = Sandbox.Diagnostics.PerformanceStats.Timings.GetMain().ToDictionary( t => t.Name, t => new System.Collections.Generic.List<double>() );
			var timings = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<double>>();
			string previousFrame = null; var completed = 0;
			for ( var i = 0; i < 150; i++ )
			{
				await Frame(); frames.Add( Sandbox.Diagnostics.PerformanceStats.FrameTime * 1000 );
				totals.Add( Sandbox.Diagnostics.PerformanceStats.GpuFrametime );
				foreach ( var timing in Sandbox.Diagnostics.PerformanceStats.Timings.GetMain() ) if ( timing.History.Size > 0 ) cpu[timing.Name].Add( timing.History[0].TotalMs );
				var timeline = capture.Invoke( null, null ); if ( timeline is null ) continue;
				var frameId = (string)frameProperty.GetValue( timeline ); if ( frameId == previousFrame ) continue; previousFrame = frameId; completed++;
				foreach ( var e in (Array)eventProperty.GetValue( timeline ) )
				{
					var t = e.GetType(); var path = (string)t.GetProperty( "Path" ).GetValue( e ) ?? (string)t.GetProperty( "Name" ).GetValue( e ) ?? "unnamed";
					var duration = Convert.ToDouble( t.GetProperty( "EndMs" ).GetValue( e ) ) - Convert.ToDouble( t.GetProperty( "StartMs" ).GetValue( e ) );
					if ( !timings.TryGetValue( path, out var samples ) ) timings[path] = samples = new(); samples.Add( duration );
				}
			}
			results.Add( new { view, phase, vertices = water.SurfaceVertexCount, tracked = water.TrackedInteractionCount,
				water.PhysicsSampleSpacing, water.PhysicsCacheHits, water.PhysicsCacheMisses,
				frameMedianMs = Percentile( frames, .5 ), frameP95Ms = Percentile( frames, .95 ), gpuMedianMs = Percentile( totals, .5 ), completed,
				cpu = cpu.Select( p => new { scope = p.Key, medianMs = Percentile( p.Value, .5 ) } ).OrderByDescending( p => p.medianMs ).ToArray(),
				scopes = timings.Select( p => new { path = p.Key, medianMs = Percentile( p.Value, .5 ), samples = p.Value.Count } ).OrderByDescending( p => p.medianMs ).Take( 18 ).ToArray() } );
			Log.Info( $"Ocean profile {view}/{phase}: frame={Percentile( frames, .5 ):F3}ms gpu={Percentile( totals, .5 ):F3}ms frames={completed}" );
			if ( renderer.IsValid() ) renderer.Enabled = true;
		}
	}
}
finally
{
	if ( existing && water.IsValid() )
	{
		water.Enabled = saved.Enabled; water.Interactions = saved.Interactions; water.NativePhysics = saved.NativePhysics;
		water.CausticStrength = saved.CausticStrength; water.InteractionMeshSpacing = saved.InteractionMeshSpacing; water.DebugView = saved.DebugView;
		if ( rendererField.GetValue( water ) is ModelRenderer renderer && renderer.IsValid() ) renderer.Enabled = true;
	}
	foreach ( var go in objects.AsEnumerable().Reverse() ) if ( go.IsValid() ) go.Destroy();
	foreach ( var pair in cameras ) if ( pair.Key.IsValid() ) pair.Key.UseCameraControls = pair.Value;
	Sandbox.Diagnostics.GpuProfilerStats.Enabled = profilerEnabled;
	collectFor.Invoke( null, new object[] { "gpu", 0f } );
}
return results;
