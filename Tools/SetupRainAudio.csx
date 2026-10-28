// Native asset setup only. Preserves prefab/component IDs and never edits the active scene.
var root=Sandbox.Project.Current.GetRootPath();
var events=new System.Collections.Generic.Dictionary<string,SoundEvent>();
foreach(var kind in new[]{"outdoor","indoor"})
{
	var path=$"sounds/ambience/rain/rain_{kind}_loop";
	var audio=Editor.AssetSystem.FindByPath(path+".wav") ?? throw new Exception("Missing rain WAV: "+path);
	audio.MetaData.Set("loop",true);audio.MetaData.Set("start",0f);audio.MetaData.Set("end",0f);
	audio.MetaData.Set("normalize",true);
	if(!audio.Compile(true))throw new Exception("Rain WAV compile failed: "+path);
	await audio.CompileIfNeededAsync(30);
	var filename=System.IO.Path.Combine(root,"Assets",path+".sound");
	var asset=Editor.AssetSystem.FindByPath(path+".sound") ?? Editor.AssetSystem.CreateResource("sound",filename);
	await asset.CompileIfNeededAsync(30);
	var sound=asset.LoadResource<SoundEvent>() ?? throw new Exception("Sound event not loaded: "+filename);
	sound.UI=false;sound.Volume=new RangedFloat(1);sound.Pitch=new RangedFloat(1);
	sound.Sounds=new(){SoundFile.Load(path+".vsnd")};
	sound.DefaultMixer="Game";sound.DistanceAttenuation=false;
	sound.OcclusionEnabled=kind=="indoor";sound.ReverbEnabled=kind=="indoor";sound.AirAbsorption=kind=="indoor";
	if(!asset.SaveToDisk(sound))throw new Exception("Could not save sound event: "+filename);
	events[kind]=sound;
}
var updated=new System.Collections.Generic.List<string>();
void Configure(GameObject go)
{
	var audio=go.GetOrAddComponent<VoidBreach.Environment.VBRainAudio>();
	audio.Weather=go.GetComponent<VoidBreach.Environment.VBCloudWeather>();
	audio.OutdoorSound=events["outdoor"];audio.ShelteredSound=events["indoor"];
}
foreach(var path in new[]{"environment/prefabs/weather_rain.prefab","environment/prefabs/weather_monsoon.prefab"})
{
	var scene=Scene.CreateEditorScene();
	try
	{
		using var scope=scene.Push();var prefab=PrefabFile.Load(path);
		var expectedRoot=Guid.Parse(prefab.RootObject["__guid"].ToString().Trim('"'));
		var go=new GameObject(scene,false,"Audio setup");go.Deserialize(prefab.RootObject);
		Configure(go);
		if(go.Id!=expectedRoot)throw new Exception("Prefab root ID changed: "+path);
		prefab.RootObject=go.Serialize();
		if(!Editor.AssetSystem.FindByPath(path).SaveToDisk(prefab))throw new Exception("Prefab save failed: "+path);
		updated.Add(path);
	}
	finally{scene.Destroy();}
}
var showcase=Scene.CreateEditorScene();
try
{
	using var scope=showcase.Push();showcase.LoadFromFile("environment/scenes/rain_showcase.scene");
	Configure(showcase.GetAllComponents<VoidBreach.Environment.VBCloudWeather>().Single().GameObject);
	System.IO.File.WriteAllText(System.IO.Path.Combine(root,"Assets/environment/scenes/rain_showcase.scene"),showcase.Serialize().ToJsonString());
}
finally{showcase.Destroy();}
return new{updated,events=events.Select(p=>new{p.Key,p.Value.ResourcePath}).ToArray()};
