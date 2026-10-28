// Isolated render regression: actual optic, late opaque depth and nested shadows.
var scene = Scene.CreateEditorScene();
var checks = new System.Collections.Generic.List<string>();
void Check(bool pass,string text) {if(!pass) throw new Exception(text); checks.Add(text);}
try
{
	using var scope=scene.Push(); scene.LoadFromFile("environment/scenes/cloud_showcase.scene");
	var camera=scene.GetAllComponents<CameraComponent>().First();
	var weather=scene.GetAllComponents<VoidBreach.Environment.VBCloudWeather>().Single();
	var clouds=scene.GetAllComponents<VoidBreach.Environment.VBVolumetricClouds>().Single();
	weather.PreviewTime=0;weather.Coverage=0.9f;weather.Density=1.4f;clouds.GroundShadows=false;
	camera.WorldRotation=Rotation.From(-55,10,0);camera.FieldOfView=70;
	var opticObject=new GameObject(scene,true,"Real optic against sky");
	var optic=opticObject.AddComponent<ModelRenderer>();
	optic.Model=Model.Load("models/weapons/sbox_attachments/sight_holographic/sight_holographic.vmdl");
	optic.Materials.SetOverride(2,Material.Load("materials/weapons/holographic_reticle.vmat"));
	opticObject.WorldTransform=camera.WorldTransform.ToWorld(new Transform(new Vector3(30,0,-1.9606f*8),Rotation.Identity,8));
	using var bitmap=new Bitmap(960,540);
	async Task<Color32[]> Capture(string name)
	{
		for(var i=0;i<4;i++) {scene.EditorTick(0,1f/60); if(optic.SceneObject.IsValid()) optic.SceneObject.Flags.WantsPrePass=false; camera.RenderToBitmap(bitmap,false);await Task.Delay(30,cancellationToken);}
		System.IO.File.WriteAllBytes(System.IO.Path.Combine(exchangeDirectory,name+".png"),bitmap.ToPng());return bitmap.GetPixels32();
	}
	clouds.Quality=VoidBreach.Environment.VBCloudQuality.Off;
	var off=await Capture("weather-optic-off");
	clouds.Quality=VoidBreach.Environment.VBCloudQuality.Medium;
	var on=await Capture("weather-optic-clouds");
	int body=0, damaged=0;
	for(var y=80;y<450;y++) for(var x=230;x<730;x++)
	{
		var i=y*960+x;var a=off[i];var b=on[i];
		if(a.r+a.g+a.b<180 && Math.Abs(a.r-a.b)<45)
		{
			body++;if(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>20) damaged++;
		}
	}
	Check(body>200,"Real holographic housing is visible in the native capture");
	Check(damaged<Math.Max(30,body/50),$"Cloud sky composition preserves late opaque optic housing (body={body}, damaged={damaged}, captures={exchangeDirectory})");
	opticObject.Enabled=false;
	camera.WorldPosition=new Vector3(0,0,500);camera.WorldRotation=Rotation.From(90,0,0);camera.FieldOfView=60;
	foreach(var renderer in scene.GetAllComponents<ModelRenderer>().Where(x=>x.GameObject!=opticObject).ToArray()) renderer.GameObject.Destroy();
	GameObject Box(string name,Vector3 position,Vector3 size)
	{
		var go=new GameObject(scene,true,name);go.WorldPosition=position;go.WorldScale=size/50;
		var model=go.AddComponent<ModelRenderer>();model.Model=Model.Load("models/dev/box.vmdl");model.Tint=new Color(0.55f,0.55f,0.55f);return go;
	}
	Box("Diffuse receiver",new Vector3(0,0,-5),new Vector3(2000,2000,10));
	var caster=Box("Native character-sized shadow caster",new Vector3(0,0,40),new Vector3(20,20,80));
	var sun=scene.GetAllComponents<DirectionalLight>().Single();sun.WorldRotation=Rotation.From(40,135,0);sun.LightColor=new Color(0.35f,0.35f,0.35f);sun.Shadows=true;
	scene.GetAllComponents<AmbientLight>().Single().Color=new Color(0.5f,0.5f,0.5f);
	var direction=-sun.WorldRotation.Forward;
	var shadowPoint=-direction*(40/direction.z);shadowPoint=shadowPoint.WithZ(0.1f);
	var sideways=Vector3.Cross(direction,Vector3.Up).Normal*40;
	float At(Color32[] pixels,Vector3 world)
	{
		var uv=camera.PointToScreenNormal(world);var x=(int)(uv.x*960);var y=(int)(uv.y*540);float sum=0;
		for(var dy=-1;dy<=1;dy++)for(var dx=-1;dx<=1;dx++) {var p=pixels[(y+dy)*960+x+dx];sum+=(p.r+p.g+p.b)/3f;}return sum/9;
	}
	clouds.GroundShadows=false;var plain=await Capture("weather-contact-shadow-off");
	clouds.GroundShadows=true;clouds.ShadowStrength=0.85f;var shaded=await Capture("weather-contact-shadow-clouds");
	var before=At(plain,shadowPoint+sideways)-At(plain,shadowPoint);
	var after=At(shaded,shadowPoint+sideways)-At(shaded,shadowPoint);
	Check(before>1,$"Native shadow exists on an ambient-dominated receiver (contrast={before})");
	Check(after>0,$"Cloud attenuation preserves dark shadow contrast (before={before}, after={after}, captures={exchangeDirectory})");
	Check(At(shaded,shadowPoint+sideways)<At(plain,shadowPoint+sideways)-1,"Cloud shadows still attenuate the receiver");
	// A fully covered sun cannot cast a second darker directional shadow.
	// Test ambient contact occlusion beneath an actual solid roof instead.
	Box("Roof occluding the sun",new Vector3(0,0,500),new Vector3(2000,2000,20));
	caster.Destroy();
	var person=new GameObject(scene,true,"Human under an environmental shadow");
	person.AddComponent<SkinnedModelRenderer>().Model=Model.Load("models/citizen_human/citizen_human_male.vmdl");
	camera.WorldPosition=new Vector3(130,-160,140);camera.WorldRotation=Rotation.LookAt(new Vector3(0,0,20)-camera.WorldPosition);
	var ao=camera.GameObject.AddComponent<AmbientOcclusion>();ao.Intensity=1.5f;ao.Radius=64;
	ao.Enabled=false;var roofPlain=await Capture("weather-roof-no-contact");
	ao.Enabled=true;var roofContact=await Capture("weather-roof-contact");
	var nearFoot=new Vector3(15,0,0.1f);var farFoot=new Vector3(50,0,0.1f);
	var ambientBefore=At(roofPlain,farFoot)-At(roofPlain,nearFoot);
	var ambientAfter=At(roofContact,farFoot)-At(roofContact,nearFoot);
	Check(ambientAfter>1 && ambientAfter>ambientBefore+2,"Native contact shading darkens the ground beside a human under a solid roof");
	Check(Math.Abs(At(roofContact,farFoot)-At(roofPlain,farFoot))<1,"Contact shading does not replace the whole building shadow with a flat dark overlay");
	// Exercise the accessory component, including its mixed opaque/glass model.
	var mount=new GameObject(scene,true,"Accessory depth fixture");mount.WorldPosition=new Vector3(10000,0,0);
	var mountRenderer=mount.AddComponent<ModelRenderer>();mountRenderer.Model=Model.Load("models/dev/box.vmdl");
	var accessory=new GameObject(mount,true,"Mixed housing and glass");
	var accessoryRenderer=accessory.AddComponent<ModelRenderer>();accessoryRenderer.Model=optic.Model;
	accessoryRenderer.Materials.SetOverride(2,Material.Load("materials/weapons/holographic_reticle.vmat"));
	var attachment=accessory.AddComponent<VBWeaponAttachment>();attachment.BoneName="";
	attachment.MountRenderer=mountRenderer;attachment.AttachmentRenderer=accessoryRenderer;
	for(int i=0;i<3;i++)scene.EditorTick(0,1f/60);
	Check(accessoryRenderer.SceneObject.Flags.WantsPrePass,"Mixed accessory housing participates in native depth/normal prepass for AO");
	Check(accessoryRenderer.Materials.GetOriginal(1).Flags.IsTranslucent,"Housing depth correction preserves the separate transparent glass material");
	return new{passed=checks.Count,checks,body,damaged,before,after,ambientBefore,ambientAfter,captures=exchangeDirectory};
}
finally {scene.Destroy();}
