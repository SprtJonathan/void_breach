// Creates a new independent preset. Never overwrites a customized prefab or scene.
var target = System.IO.Path.Combine( Sandbox.Project.Current.GetRootPath(), "Assets/environment/prefabs/weather_monsoon.prefab" );
if ( System.IO.File.Exists( target ) ) throw new InvalidOperationException( "Monsoon preset already exists; edit it in the prefab editor." );
var scene = Scene.CreateEditorScene();
try
{
	using var scope = scene.Push();
	var go = GameObject.Clone( PrefabFile.Load( "environment/prefabs/weather_rain.prefab" ) );
	go.BreakFromPrefab(); go.Name = "Tropical downpour";
	var weather = go.GetComponent<VoidBreach.Environment.VBCloudWeather>();
	weather.RainDistribution = VoidBreach.Environment.VBRainDistribution.Everywhere;
	weather.RainIntensity = 8; weather.Coverage = 0.95f; weather.Density = 1.5f;
	var rain = go.GetComponent<VoidBreach.Environment.VBPrecipitation>();
	rain.DistantRain = true; rain.DistantDistance = 24000;
	var prefab = new System.Text.Json.Nodes.JsonObject { ["RootObject"] = go.Serialize(), ["__references"] = new System.Text.Json.Nodes.JsonArray(), ["__version"] = 1 };
	System.IO.File.WriteAllText( target, prefab.ToJsonString() );
	return target;
}
finally { scene.Destroy(); }
