// Native render and temporal/spatial checks, using an isolated textured fixture.
var scene = Scene.CreateEditorScene();
var checks = new System.Collections.Generic.List<string>();
void Check( bool pass, string label ) { if ( !pass ) throw new Exception( label ); checks.Add( label ); }
try
{
	using var scope = scene.Push();
	scene.LoadFromFile( "environment/scenes/rain_showcase.scene" );
	var camera = scene.GetAllComponents<CameraComponent>().Single();
	var weather = scene.GetAllComponents<VoidBreach.Environment.VBCloudWeather>().Single();
	var rain = scene.GetAllComponents<VoidBreach.Environment.VBPrecipitation>().Single();
	foreach ( var renderer in scene.GetAllComponents<ModelRenderer>().ToArray() ) renderer.GameObject.Destroy();
	var floor = new GameObject( scene, true, "Textured solid ground" );
	// Reuse an engine-supplied material; this fixture needs no imported map textures.
	var floorRenderer = floor.AddComponent<ModelRenderer>();
	floorRenderer.Model = Model.Load( "models/dev/box.vmdl" );
	var material = floorRenderer.Materials.GetOriginal( 0 );
	var mesh = new Mesh( material );
	var vertices = new[] {
		new Vertex( new Vector3(-2000,-2000,0), Vector3.Up, Vector3.Forward, new Vector4(0,0,0,0) ),
		new Vertex( new Vector3(2000,-2000,0), Vector3.Up, Vector3.Forward, new Vector4(32,0,0,0) ),
		new Vertex( new Vector3(2000,2000,0), Vector3.Up, Vector3.Forward, new Vector4(32,32,0,0) ),
		new Vertex( new Vector3(-2000,2000,0), Vector3.Up, Vector3.Forward, new Vector4(0,32,0,0) ) };
	mesh.CreateVertexBuffer( 4, vertices ); mesh.CreateIndexBuffer( 6, new[] {0,1,2,0,2,3} );
	mesh.Bounds = new BBox( new Vector3(-2000,-2000,-1), new Vector3(2000,2000,1) );
	floorRenderer.Model = Model.Builder.WithName( "rain_realism_ground" ).AddMesh( mesh ).Create();
	var collider = floor.AddComponent<BoxCollider>(); collider.Center = new Vector3(0,0,-20); collider.Scale = new Vector3(4000,4000,40);
	camera.WorldPosition = new Vector3(0,-400,85); camera.WorldRotation = Rotation.From(18,90,0);
	weather.PreviewTime = 0; weather.SetWeather( 0.95f, 1.5f, 10, new Vector2(90,30), 0 );
	var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
	System.Collections.IList Items( string field ) => (System.Collections.IList)typeof(VoidBreach.Environment.VBPrecipitation).GetField(field,flags).GetValue(rain);
	T Read<T>( object value, string name ) => (T)value.GetType().GetField(name).GetValue(value);
	var maximumTraces = 0;
	async Task Tick( int frames )
	{
		for ( var i = 0; i < frames; i++ )
		{
			cancellationToken.ThrowIfCancellationRequested(); scene.EditorTick(0,1f/60);
			maximumTraces = Math.Max(maximumTraces,rain.LastTraceCount); await Task.Delay(8,cancellationToken);
		}
	}
	using var bitmap = new Bitmap(1280,720);
	async Task<Color32[]> Capture( string name )
	{
		// Warm asynchronous materials/pipelines on a freshly started editor, while
		// keeping simulation and the isolated effect lists frozen for comparisons.
		for (var frame=0;frame<3;frame++) {camera.RenderToBitmap(bitmap,false);await Task.Delay(20,cancellationToken);}
		System.IO.File.WriteAllBytes(System.IO.Path.Combine(exchangeDirectory,name+".png"),bitmap.ToPng());
		return bitmap.GetPixels32();
	}
	await Tick(360);
	Check(rain.ActiveDrops > 1500 && rain.ExposedNearCells == 256,$"Uniform near field fills exposed cells without waiting for random shower bursts (drops={rain.ActiveDrops}, cells={rain.ExposedNearCells}, traces={rain.LastTraceCount}, intensity={weather.Precipitation}, center={rain.RenderingCenter})");
	Check(rain.GroundContactCount >= 80 && rain.ActiveSplashes >= 160,"Heavy rain sustains dense validated ground impacts");
	var minimum = new int[] {int.MaxValue,int.MaxValue,int.MaxValue,int.MaxValue}; var maximum = new int[4];
	for ( var sample = 0; sample < 48; sample++ )
	{
		await Tick(2); var sectors = new int[4];
		foreach ( var drop in Items("_drops") )
		{
			var start=Read<Vector3>(drop,"Start"); var end=Read<Vector3>(drop,"End");
			var head=Vector3.Lerp(start,end,Read<float>(drop,"Age")/Read<float>(drop,"Lifetime"));
			var delta=head-camera.WorldPosition;
			if(new Vector2(delta.x,delta.y).Length > rain.Radius*0.8f) continue;
			sectors[(delta.x>=0?1:0)+(delta.y>=0?2:0)]++;
		}
		for(var q=0;q<4;q++) {minimum[q]=Math.Min(minimum[q],sectors[q]);maximum[q]=Math.Max(maximum[q],sectors[q]);}
	}
	Check(minimum.Min()>180 && minimum.Min()/(float)maximum.Max()>0.65f,"All four exposed sectors retain comparable rainfall over time");
	await Capture("rain-realistic-textured");
	// Isolate impacts while freezing simulation, including their GPU data/phase.
	Items("_drops").Clear(); rain.DistantRain=false;
	var impacts=await Capture("rain-ground-impacts");
	Items("_splashes").Clear();
	var dry=await Capture("rain-ground-dry");
	var changed=impacts.Zip(dry,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(x=>x>4);
	Check(changed>40,$"Surface impacts visibly affect the actual ground texture (pixels={changed}, captures={exchangeDirectory})");
	// Restore the field and verify refraction/scattering alone is visible.
	rain.DistantRain=true; await Tick(20);
	var optical=await Capture("rain-optical");
	rain.OpticalOpacity=0; rain.Splashes=false; await Tick(2);
	var noOptics=await Capture("rain-optics-off");
	var opticalPixels=optical.Zip(noOptics,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(x=>x>5);
	Check(opticalPixels>150,$"Transmitted scene light and refraction change real pixels on textured ground (pixels={opticalPixels}, copies={rain.LastSceneColorCopies}, captures={exchangeDirectory})");
	Check(maximumTraces<=16 && rain.ActiveDrops<=2048 && rain.LastDistantQuads<=8192 && rain.LastDrawCalls<=2 && rain.LastSceneColorCopies<=1,"Dense impacts and optical rain retain Medium trace/geometry/draw/copy caps");
	return new {passed=checks.Count,checks,minimum,maximum,changed,opticalPixels,maximumTraces,captures=exchangeDirectory};
}
finally { scene.Destroy(); }
