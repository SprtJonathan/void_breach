// Explicit asset generator: regenerates the cloud showcase and prefab, plus captures.
// Run through sbox-cli run_csharp. Uses its own scene; never changes the active editor/Play scene.
var scene = Scene.CreateEditorScene();
try
{
	using var scope = scene.Push();
	var cameraObject = new GameObject( scene, true, "Cloud showcase camera" );
	cameraObject.WorldPosition = new Vector3( -1800, -800, 160 );
	cameraObject.WorldRotation = Rotation.From( -12, 10, 0 );
	var camera = cameraObject.AddComponent<CameraComponent>();
	camera.FieldOfView = 85;
	camera.ZFar = 300000;
	camera.BackgroundColor = new Color( 0.25f, 0.45f, 0.72f );
	camera.EnablePostProcessing = true;
	var skyObject = new GameObject( scene, true, "Clear sky" );
	var sky = skyObject.AddComponent<SkyBox2D>();
	sky.SkyMaterial = Material.Load( "materials/skybox/skybox_day_01.vmat" );
	var ambient = skyObject.AddComponent<AmbientLight>();
	ambient.Color = new Color( 0.35f, 0.4f, 0.5f );
	var sunObject = new GameObject( scene, true, "Sun" );
	sunObject.WorldRotation = Rotation.From( 42, 135, 0 );
	var sun = sunObject.AddComponent<DirectionalLight>();
	sun.LightColor = new Color( 2.1f, 1.95f, 1.75f );
	sun.Shadows = true;
	var cloudsObject = new GameObject( scene, true, "Volumetric clouds" );
	cloudsObject.NetworkMode = NetworkMode.Object;
	var weather = cloudsObject.AddComponent<VoidBreach.Environment.VBCloudWeather>();
	weather.Sun = sun;
	weather.PreviewTime = 0;
	var volume = cloudsObject.AddComponent<PostProcessVolume>();
	volume.SceneVolume = new Sandbox.Volumes.SceneVolume { Type = Sandbox.Volumes.SceneVolume.VolumeTypes.Infinite };
	var clouds = cloudsObject.AddComponent<VoidBreach.Environment.VBVolumetricClouds>();
	clouds.Weather = weather;

	void Box( string name, Vector3 position, Vector3 scale, Color tint )
	{
		var go = new GameObject( scene, true, name );
		go.WorldPosition = position;
		go.WorldScale = scale;
		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		renderer.Tint = tint;
	}
	Box( "Ground", new Vector3( 12000, 0, -45 ), new Vector3( 600, 600, 1 ), new Color( 0.45f, 0.48f, 0.4f ) );
	Box( "Foreground tower", new Vector3( 1800, 1800, 620 ), new Vector3( 6, 7, 20 ), new Color( 0.55f, 0.58f, 0.63f ) );
	Box( "Hangar", new Vector3( 3200, -1500, 280 ), new Vector3( 15, 12, 9 ), new Color( 0.45f, 0.48f, 0.52f ) );
	Box( "Distant tower", new Vector3( 11000, 1800, 1050 ), new Vector3( 9, 9, 35 ), new Color( 0.5f, 0.53f, 0.58f ) );
	Box( "Distant warehouse", new Vector3( 14000, -6000, 400 ), new Vector3( 35, 25, 13 ), new Color( 0.6f, 0.6f, 0.6f ) );
	using var bitmap = new Bitmap( 1280, 720 );
	var captures = new System.Collections.Generic.List<object>();
	async Task Capture( string label )
	{
		cancellationToken.ThrowIfCancellationRequested();
		for ( int j = 0; j < 3; j++ )
		{
			scene.EditorTick( 0, 0.016f );
			camera.RenderToBitmap( bitmap, false );
			await Task.Delay( 30, cancellationToken );
		}
		var path = System.IO.Path.Combine( exchangeDirectory, $"cloud-{label}.png" );
		System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
		captures.Add( new { label, path, clouds.RenderCount, clouds.LastStepCount, clouds.LastResolutionDivisor } );
	}
	await Capture( "medium" );
	weather.PreviewTime = 100;
	await Capture( "wind" );
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.Ultra;
	weather.PreviewTime = 0;
	await Capture( "ultra" );
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.Off;
	await Capture( "off" );
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.Medium;
	weather.Coverage = 0;
	await Capture( "clear" );
	weather.Coverage = 0.5f;
	clouds.GroundShadows = false;
	await Capture( "no-shadows" );
	clouds.GroundShadows = true;
	// Restore a live preview for the saved demo. This writes only a new scene.
	weather.PreviewTime = -1;
	var root = Sandbox.Project.Current.GetRootPath();
	var target = System.IO.Path.Combine( root, "Assets", "environment", "scenes", "cloud_showcase.scene" );
	System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( target ) );
	System.IO.File.WriteAllText( target, scene.Serialize().ToJsonString() );
	weather.Sun = null; // The reusable prefab discovers the destination scene's sun.
	var prefabTarget = System.IO.Path.Combine( root, "Assets", "environment", "prefabs", "volumetric_clouds.prefab" );
	System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( prefabTarget ) );
	var prefab = new System.Text.Json.Nodes.JsonObject
	{
		["RootObject"] = cloudsObject.Serialize(),
		["__references"] = new System.Text.Json.Nodes.JsonArray(),
		["__version"] = 1
	};
	// Existing scene overrides refer to these source IDs. Regenerating them would
	// leave the editor's live objects visible but drop their components on Play.
	if ( System.IO.File.Exists( prefabTarget ) )
	{
		var previous = System.Text.Json.Nodes.JsonNode.Parse( System.IO.File.ReadAllText( prefabTarget ) );
		var previousRoot = previous["RootObject"].AsObject();
		// Serialize() stores GUID-typed JsonValues; normalize them to JSON strings
		// before building the replacement table (Guid JsonValue.ToString adds quotes).
		prefab = System.Text.Json.Nodes.JsonNode.Parse( prefab.ToJsonString() ).AsObject();
		var generatedRoot = prefab["RootObject"].AsObject();
		var ids = new System.Collections.Generic.Dictionary<string, string>();
		ids[generatedRoot["__guid"].ToString()] = previousRoot["__guid"].ToString();
		foreach ( var generatedComponent in generatedRoot["Components"].AsArray() )
		{
			var type = generatedComponent["__type"].GetValue<string>();
			var previousComponent = previousRoot["Components"].AsArray().SingleOrDefault( c => c["__type"].GetValue<string>() == type );
			if ( previousComponent is not null )
				ids[generatedComponent["__guid"].ToString()] = previousComponent["__guid"].ToString();
		}
		var json = prefab.ToJsonString();
		foreach ( var id in ids ) json = json.Replace( $"\"{id.Key}\"", $"\"{id.Value}\"" );
		prefab = System.Text.Json.Nodes.JsonNode.Parse( json ).AsObject();
		if ( prefab["RootObject"]["__guid"].GetValue<string>() != previousRoot["__guid"].GetValue<string>() )
			throw new InvalidOperationException( "Refusing to replace the cloud prefab with a different root identity." );
	}
	System.IO.File.WriteAllText( prefabTarget, prefab.ToJsonString() );
	return new { captures, target, prefabTarget };
}
finally
{
	scene.Destroy();
}
