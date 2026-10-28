// Native spatial/density regression. Own scene only; never edits the user's scene.
var scene = Scene.CreateEditorScene();
var checks = new System.Collections.Generic.List<string>();
void Check( bool pass, string message ) { if ( !pass ) throw new Exception( message ); checks.Add( message ); }
try
{
	using var scope = scene.Push();
	scene.LoadFromFile( "environment/scenes/rain_showcase.scene" );
	var camera = scene.GetAllComponents<CameraComponent>().Single();
	var weather = scene.GetAllComponents<VoidBreach.Environment.VBCloudWeather>().Single();
	var rain = scene.GetAllComponents<VoidBreach.Environment.VBPrecipitation>().Single();
	var preset = GameObject.Clone( PrefabFile.Load( "environment/prefabs/weather_monsoon.prefab" ) );
	var presetWeather = preset.GetComponent<VoidBreach.Environment.VBCloudWeather>();
	Check( presetWeather.RainIntensity == 8 && presetWeather.RainDistribution == VoidBreach.Environment.VBRainDistribution.Everywhere &&
		preset.GetComponent<VoidBreach.Environment.VBPrecipitation>().Weather == presetWeather && preset.NetworkMode == NetworkMode.Object,
		"Reusable monsoon preset starts at intensity 8 with shared networked weather" );
	preset.Destroy();
	weather.PreviewTime = 0;
	weather.SetWeather( 0.95f, 1.5f, 1, new Vector2( 160, 65 ), 0 );
	var maxTraces = 0; var maxDrops = 0; var maxQuads = 0;
	async Task Tick( int count )
	{
		for ( var i = 0; i < count; i++ )
		{
			cancellationToken.ThrowIfCancellationRequested(); scene.EditorTick( 0, 1f / 60 );
			maxTraces = Math.Max( maxTraces, rain.LastTraceCount ); maxDrops = Math.Max( maxDrops, rain.ActiveDrops );
			await Task.Delay( 8, cancellationToken );
		}
	}
	using var bitmap = new Bitmap( 1280, 720 );
	Color32[] Capture( string label )
	{
		camera.RenderToBitmap( bitmap, false );
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( exchangeDirectory, label + ".png" ), bitmap.ToPng() );
		maxQuads = Math.Max( maxQuads, rain.LastDistantQuads );
		return bitmap.GetPixels32();
	}
	var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
	object[] Paths() => ((System.Collections.IEnumerable)typeof(VoidBreach.Environment.VBPrecipitation).GetField( "_distantPaths", flags ).GetValue( rain )).Cast<object>().ToArray();
	T Read<T>( object path, string name ) => (T)path.GetType().GetField( name ).GetValue( path );
	await Tick( 240 );
	Capture( "rain-shower-wide" );
	var normalDrops = rain.ActiveDrops; var normalFar = rain.LastDistantQuads;
	Check( weather.RainDeck > 0.99f, "Everywhere weather forms a continuous rain cloud deck" );
	Check( rain.EffectiveDistantDistance == 24000 && rain.CachedDistantPaths == 288 && normalFar > 0, $"Medium populates a 24,000-unit world field beyond the near volume (distance={rain.EffectiveDistantDistance}, paths={rain.CachedDistantPaths}, quads={normalFar})" );
	var exposed = Paths().Where( p => Read<bool>( p, "Valid" ) ).ToArray();
	Check( exposed.Any( p => Read<Vector3>( p, "End" ).Distance( camera.WorldPosition ) > 10000 ), "Validated rain trajectories extend beyond 10,000 units" );
	weather.SetWeather( 0.95f, 1.5f, 10, new Vector2( 160, 65 ), 0 );
	await Tick( 160 );
	var monsoon = Capture( "rain-monsoon-wide" );
	var monsoonDrops = rain.ActiveDrops; var monsoonFar = rain.LastDistantQuads;
	Check( weather.Precipitation > 9.9f, "Inspector/API intensity reaches tropical downpour strength" );
	Check( monsoonDrops > normalDrops * 4 && monsoonFar > normalFar * 4, "Downpour increases both detailed and distant density by more than fourfold" );
	// Freeze paths and clear only near drops: the distant pass must change real pixels.
	var drops = (System.Collections.IList)typeof(VoidBreach.Environment.VBPrecipitation).GetField( "_drops", flags ).GetValue( rain );
	drops.Clear();
	var farOnly = Capture( "rain-distant-only" );
	rain.DistantRain = false;
	var noFar = Capture( "rain-distant-off" );
	var farPixels = farOnly.Zip( noFar, (a,b) => Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b) ).Count( x => x > 12 );
	Check( farPixels > 200, "Distant rain visibly renders independently of all near drops" );
	rain.DistantRain = true;
	// A second observer location on the other side of a map gets the same weather.
	camera.WorldPosition += new Vector3( 30000, -20000, 0 );
	await Tick( 240 ); Capture( "rain-monsoon-remote-map" );
	Check( rain.ActiveDrops > 100 && rain.LastDistantQuads > 500, "Rain follows a 36,000-unit traversal and covers a remote map region" );
	Check( Paths().Where( p => Read<bool>( p, "Valid" ) ).Any( p => Read<Vector3>( p, "Start" ).x > 25000 ), "Remote trajectories are anchored in world space" );
	Check( maxTraces <= 16 && maxDrops <= 2048 && maxQuads <= 8192 && rain.LastDrawCalls <= 2, "Monsoon retains Medium physics, geometry and draw-call caps" );
	// A roof spanning all distant cells must clip every refreshed path above it.
	var roof = new GameObject( scene, true, "Wide regression roof" );
	roof.WorldPosition = new Vector3( camera.WorldPosition.x, camera.WorldPosition.y, 500 );
	roof.AddComponent<BoxCollider>().Scale = new Vector3( 200000, 200000, 80 );
	await Tick( 360 );
	exposed = Paths().Where( p => Read<bool>( p, "Valid" ) ).ToArray();
	Check( exposed.Length > 20 && exposed.All( p => Read<Vector3>( p, "End" ).z >= 540 ), "Cached distant paths stop above a roof instead of raining inside it" );
	Check( drops.Cast<object>().All( p => Read<Vector3>( p, "End" ).z >= 540 ), "Shared downpour sweeps also keep detailed drops above the roof" );
	roof.Destroy();
	var viewObject = new GameObject( scene, true, "Independent editor view" );
	viewObject.WorldPosition = camera.WorldPosition + new Vector3( 35000, 0, 0 ); viewObject.WorldRotation = camera.WorldRotation;
	var view = viewObject.AddComponent<CameraComponent>(); view.IsMainCamera = false; view.Priority = int.MaxValue;
	view.EnablePostProcessing = true; view.ZFar = camera.ZFar;
	view.RenderToBitmap( bitmap, false ); await Tick( 160 );
	Check( rain.RenderingCenter.Distance( view.WorldPosition ) < 0.1f && scene.Camera == camera && rain.ActiveDrops > 100,
		"Free editor view gets rain 35,000 units away without moving the authored camera" );
	viewObject.Destroy();
	weather.RainDistribution = VoidBreach.Environment.VBRainDistribution.CloudBanks;
	Check( weather.RainDeck == 0, "Optional CloudBanks restores local cloud holes" );
	weather.SetWeather( 0.95f, 1.5f, 0, weather.Wind, 0 ); await Tick( 2 );
	Check( rain.ActiveDrops == 0 && rain.CachedDistantPaths == 0 && rain.LastDistantQuads == 0 && rain.LastTraceCount == 0, "Dry weather releases both near and distant layers" );
	return new { passed = checks.Count, checks, normalDrops, monsoonDrops, normalFar, monsoonFar, farPixels, maxTraces, maxDrops, maxQuads, captures = exchangeDirectory };
}
finally { scene.Destroy(); }
