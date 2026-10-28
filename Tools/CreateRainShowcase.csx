// Explicit generator. Creates only the separate rain showcase and weather_rain prefab.
var scene = Scene.CreateEditorScene();
try
{
	using var scope = scene.Push();
	var cameraObject = new GameObject( scene, true, "Rain showcase camera" );
	cameraObject.WorldPosition = new Vector3( -450, -700, 110 );
	cameraObject.WorldRotation = Rotation.From( 5, 42, 0 );
	var camera = cameraObject.AddComponent<CameraComponent>();
	camera.FieldOfView = 85; camera.ZFar = 300000; camera.EnablePostProcessing = true;
	var skyObject = new GameObject( scene, true, "Sky and ambient" );
	skyObject.AddComponent<SkyBox2D>().SkyMaterial = Material.Load( "materials/skybox/skybox_day_01.vmat" );
	skyObject.AddComponent<AmbientLight>().Color = new Color( 0.35f, 0.4f, 0.5f );
	var sunObject = new GameObject( scene, true, "Sun" );
	sunObject.WorldRotation = Rotation.From( 45, 135, 0 );
	var sun = sunObject.AddComponent<DirectionalLight>(); sun.LightColor = new Color( 1.2f, 1.15f, 1.1f ); sun.Shadows = true;
	var weatherObject = GameObject.Clone( PrefabFile.Load( "environment/prefabs/volumetric_clouds.prefab" ) );
	weatherObject.BreakFromPrefab(); weatherObject.Name = "Clouds, wind and rain";
	var weather = weatherObject.GetComponent<VoidBreach.Environment.VBCloudWeather>();
	weather.Coverage = 0.65f; weather.RainIntensity = 0.85f; weather.Wind = new Vector2( 160, 65 );
	var rain = weatherObject.AddComponent<VoidBreach.Environment.VBPrecipitation>(); rain.Weather = weather;
	var audio = weatherObject.AddComponent<VoidBreach.Environment.VBRainAudio>(); audio.Weather = weather;
	audio.OutdoorSound = ResourceLibrary.Get<SoundEvent>( "sounds/ambience/rain/rain_outdoor_loop.sound" );
	audio.ShelteredSound = ResourceLibrary.Get<SoundEvent>( "sounds/ambience/rain/rain_indoor_loop.sound" );
	void Box( string name, Vector3 position, Vector3 size, Color tint )
	{
		var go = new GameObject( scene, true, name ); go.WorldPosition = position;
		go.WorldScale = size / 50;
		var renderer = go.AddComponent<ModelRenderer>(); renderer.Model = Model.Load( "models/dev/box.vmdl" ); renderer.Tint = tint;
		var collider = go.AddComponent<BoxCollider>(); collider.Scale = new Vector3( 50 );
	}
	Box( "Ground", new Vector3( 0, 0, -20 ), new Vector3( 6000, 6000, 40 ), new Color( 0.25f, 0.28f, 0.3f ) );
	Box( "Shelter roof (collider blocks rain)", new Vector3( 250, 150, 250 ), new Vector3( 550, 650, 30 ), new Color( 0.35f, 0.38f, 0.42f ) );
	Box( "Shelter back wall", new Vector3( 500, 150, 115 ), new Vector3( 30, 650, 230 ), new Color( 0.25f, 0.28f, 0.32f ) );
	foreach ( var x in new[] { 0, 500 } )
	foreach ( var y in new[] { -140, 440 } )
		Box( "Shelter post", new Vector3( x, y, 115 ), new Vector3( 18, 18, 230 ), new Color( 0.3f, 0.32f, 0.36f ) );
	var root = Sandbox.Project.Current.GetRootPath();
	var target = System.IO.Path.Combine( root, "Assets/environment/scenes/rain_showcase.scene" );
	System.IO.File.WriteAllText( target, scene.Serialize().ToJsonString() );
	var prefabTarget = System.IO.Path.Combine( root, "Assets/environment/prefabs/weather_rain.prefab" );
	var prefab = new System.Text.Json.Nodes.JsonObject { ["RootObject"] = weatherObject.Serialize(), ["__references"] = new System.Text.Json.Nodes.JsonArray(), ["__version"] = 1 };
	prefab = System.Text.Json.Nodes.JsonNode.Parse( prefab.ToJsonString() ).AsObject();
	if ( System.IO.File.Exists( prefabTarget ) )
	{
		var previous = System.Text.Json.Nodes.JsonNode.Parse( System.IO.File.ReadAllText( prefabTarget ) );
		var ids = new System.Collections.Generic.Dictionary<string, string> { [prefab["RootObject"]["__guid"].ToString()] = previous["RootObject"]["__guid"].ToString() };
		foreach ( var component in prefab["RootObject"]["Components"].AsArray() )
		{
			var old = previous["RootObject"]["Components"].AsArray().SingleOrDefault( c => c["__type"].ToString() == component["__type"].ToString() );
			if ( old is not null ) ids[component["__guid"].ToString()] = old["__guid"].ToString();
		}
		var json = prefab.ToJsonString(); foreach ( var id in ids ) json = json.Replace( $"\"{id.Key}\"", $"\"{id.Value}\"" );
		prefab = System.Text.Json.Nodes.JsonNode.Parse( json ).AsObject();
	}
	System.IO.File.WriteAllText( prefabTarget, prefab.ToJsonString() );
	return new { target, prefabTarget };
}
finally { scene.Destroy(); }
