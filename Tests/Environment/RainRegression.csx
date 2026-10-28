// Native checks in an isolated scene. Does not change the user's editor or Play scene.
var scene = Scene.CreateEditorScene();
var checks = new System.Collections.Generic.List<string>();
void Check( bool pass, string message ) { if ( !pass ) throw new Exception( message ); checks.Add( message ); }
try
{
	using var scope = scene.Push();
	scene.LoadFromFile( "environment/scenes/rain_showcase.scene" );
	var camera = scene.GetAllComponents<CameraComponent>().First();
	var weather = scene.GetAllComponents<VoidBreach.Environment.VBCloudWeather>().Single();
	var rain = scene.GetAllComponents<VoidBreach.Environment.VBPrecipitation>().Single();
	var clone = GameObject.Clone( PrefabFile.Load( "environment/prefabs/weather_rain.prefab" ) );
	Check( clone.GetComponent<VoidBreach.Environment.VBPrecipitation>().Weather == clone.GetComponent<VoidBreach.Environment.VBCloudWeather>(), "Prefab remaps rain weather reference" );
	Check( clone.GetComponent<VoidBreach.Environment.VBVolumetricClouds>().Weather == clone.GetComponent<VoidBreach.Environment.VBCloudWeather>(), "Clouds and rain share one weather" );
	Check( clone.NetworkMode == NetworkMode.Object, "Weather prefab uses network Object mode" ); clone.Destroy();
	foreach ( var kind in new[] { "drop", "splash" } )
	{
		var material = Material.Load( $"environment/materials/rain_{kind}.vmat" );
		Check( Shader.Load( material.ShaderName ).IsValid, $"{kind}: native ShaderGraph shader loads" );
		var graph = new Editor.ShaderGraph.ShaderGraph();
		graph.Deserialize( System.IO.File.ReadAllText( System.IO.Path.Combine( Sandbox.Project.Current.GetRootPath(), $"Assets/environment/shaders/vb_rain_{kind}.shdrgrph" ) ) );
		var compiler = new Editor.ShaderGraph.GraphCompiler( graph, false );
		Check( compiler.Generate() is not null && !compiler.Errors.Any(), $"{kind}: editable graph round-trips including the custom cloud mask node" );
	}
	weather.PreviewTime = 0; weather.Coverage = 0; weather.Density = 0; weather.RainIntensity = 0;
	Check( weather.Precipitation == 0 && weather.CurrentCoverage == 0, "Clear sky stays dry" );
	weather.Coverage = 1; weather.Density = 2;
	Check( weather.Precipitation == 0, "Overcast can remain dry" );
	weather.Coverage = 0; weather.Density = 0; weather.RainIntensity = 1;
	Check( weather.CurrentCoverage >= 0.8f && weather.CurrentDensity >= 1 && weather.Precipitation > 0.9f, "Rain request supplies a coherent cloud layer even with clear base settings" );
	weather.RainIntensity = 0.05f;
	Check( weather.CurrentCoverage >= 0.8f && weather.Precipitation > 0.04f, "Light drizzle also supplies clouds instead of being suppressed" );
	weather.RainIntensity = 0;
	weather.SetWeather( 0, 0, 1, new Vector2( 180, 60 ), 10 );
	Check( weather.Precipitation == 0, "Transition starts dry" );
	weather.PreviewTime = 0.5;
	Check( weather.Precipitation == 0 && weather.CurrentCoverage > 0, "Clouds appear before the first rain" );
	weather.PreviewTime = 10;
	Check( weather.Precipitation > 0.9f && weather.CurrentWind.Distance( new Vector2( 180, 60 ) ) < 0.1f, "Transition reaches the shared rain and wind endpoints" );
	var motionProperty = typeof(VoidBreach.Environment.VBCloudWeather).GetProperty( "CloudMotion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance );
	var beforeWindChange = (Vector2)motionProperty.GetValue( weather );
	weather.SetWeather( 0.85f, 1.2f, 1, new Vector2( -180, -60 ), 4 );
	Check( beforeWindChange.Distance( (Vector2)motionProperty.GetValue( weather ) ) < 0.01f, "Changing the wind keeps cloud advection continuous" );
	weather.PreviewTime = 16;
	var afterTransition = (Vector2)motionProperty.GetValue( weather );
	weather.PreviewTime = 17;
	Check( (((Vector2)motionProperty.GetValue( weather ) - afterTransition) - new Vector2( -180, -60 )).Length < 0.1f, "Clouds continue at the new wind speed after a transition" );
	weather.SetWeather( 0, 0, 0.05f, new Vector2( 180, 60 ), 4 );
	weather.PreviewTime = 21;
	Check( weather.Precipitation > 0.04f && weather.CurrentCoverage >= 0.8f, "Heavy rain can transition to lasting light drizzle" );
	weather.SetWeather( 0.85f, 1.2f, 1, new Vector2( 180, 60 ), 0 );
	var maxTraces = 0; var maxDrops = 0; var maxSplashes = 0;
	async Task Tick( int count )
	{
		for ( var i = 0; i < count; i++ )
		{
			cancellationToken.ThrowIfCancellationRequested();
			scene.EditorTick( 0, 1f / 60 );
			maxTraces = Math.Max( maxTraces, rain.LastTraceCount ); maxDrops = Math.Max( maxDrops, rain.ActiveDrops ); maxSplashes = Math.Max( maxSplashes, rain.ActiveSplashes );
			await Task.Delay( 8, cancellationToken );
		}
	}
	using var bitmap = new Bitmap( 1280, 720 );
	async Task<Color32[]> Capture( string name )
	{
		for ( var i = 0; i < 3; i++ ) { scene.EditorTick( 0, 1f / 60 ); camera.RenderToBitmap( bitmap, false ); await Task.Delay( 15, cancellationToken ); }
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( exchangeDirectory, name + ".png" ), bitmap.ToPng() );
		return bitmap.GetPixels32();
	}
	rain.Enabled = false;
	var dry = await Capture( "rain-dry" );
	rain.Enabled = true;
	await Tick( 200 );
	Check( rain.ActiveDrops > 0, "Outdoor camera receives local drops" );
	Check( rain.RainVelocity.z < 0 && Math.Abs( rain.RainVelocity.x - weather.CurrentWind.x ) < 0.1f, "Rain falls with the same XY wind as clouds" );
	Check( maxTraces <= 16 && maxDrops <= VoidBreach.Environment.VBPrecipitation.Budget( rain.Quality ).Drops && maxSplashes <= VoidBreach.Environment.VBPrecipitation.Budget( rain.Quality ).Splashes, "Medium enforces per-frame trace, drop and splash limits" );
	Check( maxSplashes > 0, "Exposed ground contacts produce short-lived impacts" );
	var wet = await Capture( "rain-outdoors" );
	// Transmitting water changes contrast subtly; the former opaque-white threshold
	// is not a visibility criterion for the refractive material.
	var changed = dry.Zip( wet, (a,b) => Math.Abs( a.r-b.r ) + Math.Abs( a.g-b.g ) + Math.Abs( a.b-b.b ) ).Count( x => x > 5 );
	Check( changed > 50 && rain.LastDrawCalls > 0 && rain.LastDrawCalls <= 2, $"Rain renders visible native geometry in at most two batches (pixels={changed}, calls={rain.LastDrawCalls}, captures={exchangeDirectory})" );
	var renderObject = (SceneCustomObject)typeof(VoidBreach.Environment.VBPrecipitation).GetField( "_renderObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance ).GetValue( rain );
	renderObject.Attributes.Set( "CloudLayer", new Vector4( weather.Altitude, weather.Thickness, weather.ShapeScale, 0 ) );
	// Freeze simulation while testing the shader's independent no-cloud rejection.
	camera.RenderToBitmap( bitmap, false );
	var masked = bitmap.GetPixels32();
	var maskedChanges = dry.Zip( masked, (a,b) => Math.Abs( a.r-b.r ) + Math.Abs( a.g-b.g ) + Math.Abs( a.b-b.b ) ).Count( x => x > 18 );
	Check( maskedChanges < 30, "Shader rejects rain and impacts when its actual cloud density field is empty" );
	// Put the complete local spawning volume under a high ceiling. No camera-only shortcut.
	rain.Enabled = false; rain.Height = 128; rain.Radius = 128;
	var roof = new GameObject( scene, true, "Regression ceiling" ); roof.WorldPosition = camera.WorldPosition + Vector3.Up * 400;
	var box = roof.AddComponent<BoxCollider>(); box.Scale = new Vector3( 5000, 5000, 40 );
	rain.Enabled = true; await Tick( 100 );
	Check( rain.ActiveDrops == 0 && rain.BlockedSpawns > 0, "Ceiling rejects spawn paths inside a building" );
	await Capture( "rain-sheltered" );
	roof.Destroy(); await Tick( 100 );
	Check( rain.ActiveDrops > 0, "Removing the roof restores exposed rain" );
	rain.Quality = VoidBreach.Environment.VBRainQuality.Off; await Tick( 2 );
	Check( rain.ActiveDrops == 0 && rain.ActiveSplashes == 0 && rain.LastTraceCount == 0 && rain.LastDrawCalls == 0, "Off releases geometry, drops and impacts with no traces" );
	rain.Quality = VoidBreach.Environment.VBRainQuality.Low; await Tick( 80 );
	Check( rain.ActiveDrops > 0 && rain.ActiveDrops <= VoidBreach.Environment.VBPrecipitation.Budget( rain.Quality ).Drops && rain.LastTraceCount <= 8, "Low restarts within its smaller budget" );
	weather.Enabled = false; await Tick( 2 );
	Check( rain.ActiveDrops == 0, "Disabled weather clears precipitation" ); weather.Enabled = true;
	rain.PreviewInEditor = false; await Tick( 2 );
	Check( rain.ActiveDrops == 0 && rain.LastTraceCount == 0, "Editor preview can be disabled" ); rain.PreviewInEditor = true;
	camera.Orthographic = true; await Tick( 2 );
	Check( rain.ActiveDrops == 0 && rain.LastTraceCount == 0, "Orthographic cameras skip local rain" );
	return new { passed = checks.Count, checks, changedPixels = changed, maxTraces, maxDrops, maxSplashes, captures = exchangeDirectory };
}
finally { scene.Destroy(); }
