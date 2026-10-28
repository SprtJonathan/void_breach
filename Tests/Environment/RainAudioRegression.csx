// Silent, isolated geometry and asset regression. Never calls Sound.Play or enters Play.
var scene=Scene.CreateEditorScene();
var checks=new System.Collections.Generic.List<string>();
void Check(bool pass,string message){if(!pass)throw new Exception(message);checks.Add(message);}
try
{
	using var scope=scene.Push();
	var weatherObject=new GameObject(scene,true,"Rain audio regression");
	var weather=weatherObject.AddComponent<VoidBreach.Environment.VBCloudWeather>();weather.RainIntensity=8;
	var audio=weatherObject.AddComponent<VoidBreach.Environment.VBRainAudio>();audio.Weather=weather;
	var probe=audio.GetType().GetMethod("ProbeEnvironment",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
	var geometry=new System.Collections.Generic.List<GameObject>();var maxTraces=0;
	GameObject Box(string name,Vector3 position,Vector3 size)
	{
		var go=new GameObject(scene,true,name);go.WorldPosition=position;go.AddComponent<BoxCollider>().Scale=size;geometry.Add(go);return go;
	}
	void Tick(){scene.EditorTick(0,1f/60);scene.EditorTick(0,1f/60);}
	void Clear(){foreach(var go in geometry)go.Destroy();geometry.Clear();Tick();}
	void Probe(Vector3 position,int count=8)
	{
		Tick();for(int i=0;i<count;i++){probe.Invoke(audio,new object[]{new Transform(position)});maxTraces=Math.Max(maxTraces,audio.LastTraceCount);}
	}
	Probe(new Vector3(0,0,64));
	Check(audio.ExposedToSky && audio.ShelterAudibility==0,"Open sky selects direct outdoor rain");
	Check(audio.LastTraceCount==1,"Outdoor exposure costs one ray per probe");
	Box("Thin shed roof",new Vector3(0,0,128),new Vector3(8000,8000,8));
	Probe(new Vector3(0,0,64));var thin=audio.ShelterAudibility;
	Check(!audio.ExposedToSky && thin>0.3f && audio.ShelterSoundPosition.z>132,"A thin roof admits sheltered rain from the roof surface without a nearby opening");
	Clear();Box("Thick sealed ceiling",new Vector3(0,0,188),new Vector3(8000,8000,128));
	Probe(new Vector3(0,0,64));
	Check(!audio.ExposedToSky && audio.ShelterAudibility==0,"Thick sealed cover does not synthesize audible rain overhead");
	Clear();
	Box("Covered corner",new Vector3(0,0,188),new Vector3(400,400,128));
	Box("Acoustic wall",new Vector3(220,0,200),new Vector3(32,8000,400));
	Probe(new Vector3(0,0,64),1);
	Check(audio.ShelterAudibility>0.4f && scene.Trace.Ray(new Vector3(0,0,64),audio.ShelterSoundPosition).Run().Hit,"Exposed rain behind a wall remains available to native transmission and diffraction");
	Clear();
	Box("Tunnel rock above",new Vector3(-2000,0,168),new Vector3(4000,8000,80));
	Box("Tunnel rock left",new Vector3(-2000,2064,100),new Vector3(4000,3872,200));
	Box("Tunnel rock right",new Vector3(-2000,-2064,100),new Vector3(4000,3872,200));
	Box("Tunnel back",new Vector3(-4016,0,100),new Vector3(32,320,200));
	Probe(new Vector3(-48,0,64));var entrance=audio.ShelterAudibility;
	Check(!audio.ExposedToSky && entrance>0.75f && audio.ShelterSoundPosition.x>=0,"A sheltered tunnel entrance hears rain at the exterior opening");
	Probe(new Vector3(-600,0,64));var middle=audio.ShelterAudibility;
	Check(middle>0 && middle<entrance*0.5f,"Rain audibility decreases while walking deeper into the tunnel");
	Probe(new Vector3(-2000,0,64));
	Check(audio.ShelterAudibility==0,"A deep tunnel beyond hearing range becomes quiet");
	Check(maxTraces<=8,"Shelter discovery stays within eight rays per probe");
	Clear();
	foreach(var kind in new[]{"outdoor","indoor"})
	{
		var asset=Editor.AssetSystem.FindByPath($"sounds/ambience/rain/rain_{kind}_loop.wav");
		var sound=ResourceLibrary.Get<SoundEvent>($"sounds/ambience/rain/rain_{kind}_loop.sound");
		Check(asset.MetaData.Get("loop",false),$"{kind}: native import enables continuous looping");
		Check(sound is not null && sound.Sounds.Count==1 && sound.Sounds[0].ResourcePath.EndsWith(".vsnd"),$"{kind}: sound event references the compiled WAV resource");
		Check(!sound.UI && !sound.DistanceAttenuation && sound.OcclusionEnabled==(kind=="indoor") && sound.ReverbEnabled==(kind=="indoor"),$"{kind}: native acoustic settings match its ambience role");
		var file=sound.Sounds[0];var loaded=await file.LoadAsync();
		Check(loaded && file.IsLoaded && Math.Abs(file.Duration-2)<0.01 && file.Channels==2 && file.Rate==48000,$"{kind}: the compiled stereo loop decodes at 48 kHz for two seconds");
		var samples=await file.GetSamplesInterleavedAsync();double energy=0;
		foreach(var sample in samples)energy+=sample*(double)sample;
		var rms=Math.Sqrt(energy/samples.Length)/32768;
		Check(asset.MetaData.Get("normalize",false) && rms>0.03 && rms<0.3,$"{kind}: native import normalizes the quiet source to a usable level (RMS={rms:F3})");
	}
	foreach(var path in new[]{"environment/prefabs/weather_rain.prefab","environment/prefabs/weather_monsoon.prefab"})
	{
		var go=GameObject.Clone(PrefabFile.Load(path));var instance=go.GetComponent<VoidBreach.Environment.VBRainAudio>();
		Check(instance is not null && instance.Weather==go.GetComponent<VoidBreach.Environment.VBCloudWeather>() && instance.OutdoorSound is not null && instance.ShelteredSound is not null,path+": cloned audio references remain valid");go.Destroy();
	}
	Check(audio.ActiveVoices==0,"Editor geometry probes never start audible preview voices");
	audio.GetType().GetMethod("OnUpdate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(audio,null);
	Check(audio.LastTraceCount==0 && audio.ActiveVoices==0 && audio.OutdoorGain==0 && audio.ShelteredGain==0,"The editor update guard clears local state without starting playback");
	// Run the real update loop in a private runtime scene. Transient events have
	// zero base volume, so voice lifecycle checks cannot emit audible samples.
	var runtime=new Scene();
	try
	{
		using var runtimeScope=runtime.Push();
		var weatherGo=new GameObject(runtime,true,"Silent runtime weather");weatherGo.NetworkMode=NetworkMode.Object;
		var shared=weatherGo.AddComponent<VoidBreach.Environment.VBCloudWeather>();shared.RainIntensity=8;
		var local=weatherGo.AddComponent<VoidBreach.Environment.VBRainAudio>();local.Weather=shared;
		SoundEvent Silent(string kind)=>new SoundEvent{Volume=new RangedFloat(0),Pitch=new RangedFloat(1),Sounds=ResourceLibrary.Get<SoundEvent>($"sounds/ambience/rain/rain_{kind}_loop.sound").Sounds,DefaultMixer="Game"};
		local.OutdoorSound=Silent("outdoor");local.ShelteredSound=Silent("indoor");
		var cam=new GameObject(runtime,true,"Silent listener").AddComponent<CameraComponent>();cam.WorldPosition=new Vector3(0,0,64);cam.IsMainCamera=true;
		SoundHandle Handle(string field)=>(SoundHandle)local.GetType().GetField(field,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(local);
		void Advance(){for(int i=0;i<180;i++)runtime.GameTick(1.0/60);}
		Advance();Check(local.OutdoorGain>0.95f && local.ShelteredGain<0.001f,"Runtime update fades to outdoor ambience without a particle renderer");
		var outside=Handle("_outdoor");Check(outside.IsValid() && outside.Volume==0 && outside.SpacialBlend==0 && !outside.OcclusionEnabled && !outside.ReverbEnabled && outside.TargetMixer.Name=="Game","Outdoor voice uses the game mixer and a silent 2D bed without acoustic simulation");
		var ceilingGo=new GameObject(runtime,true,"Runtime solid cover");ceilingGo.WorldPosition=new Vector3(0,0,188);ceilingGo.AddComponent<BoxCollider>().Scale=new Vector3(8000,8000,128);
		Advance();Check(local.OutdoorGain<0.01f && local.ShelteredGain<0.001f,"Runtime shelter transition fades out the outdoor bed under thick sealed cover");
		Check(ReferenceEquals(outside,Handle("_outdoor")) && outside.Paused,"An inaudible outdoor voice pauses instead of restarting every frame");
		ceilingGo.GetComponent<BoxCollider>().Scale=new Vector3(8000,8000,8);ceilingGo.WorldPosition=new Vector3(0,0,128);
		Advance();var inside=Handle("_sheltered");
		Check(inside.IsValid() && inside.Volume==0 && inside.OcclusionEnabled && inside.ReverbEnabled && inside.AirAbsorption && Math.Abs(inside.SpacialBlend-0.35f)<0.001f && local.ActiveVoices<=2,"Sheltered voice enables native acoustics and bounded partial spatialization");
		ceilingGo.Destroy();Advance();Check(local.OutdoorGain>0.95f,"Leaving cover restores outdoor ambience through the runtime update");
		shared.RainIntensity=0;Advance();Check(local.OutdoorGain==0 && local.ShelteredGain==0 && local.LastTraceCount==0 && local.ActiveVoices==0,"Dry runtime weather releases audio state and stops acoustic probes");
	}
	finally{runtime.Destroy();}
	return new{passed=checks.Count,checks,thin,entrance,middle,maxTraces};
}
finally{scene.Destroy();}
