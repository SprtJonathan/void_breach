// Completed-depth masking must work at arbitrary viewport sizes, not only 960x540.
var scene=Scene.CreateEditorScene();
var results=new System.Collections.Generic.List<object>();
try
{
	using var scope=scene.Push();scene.LoadFromFile("environment/scenes/cloud_showcase.scene");
	var camera=scene.GetAllComponents<CameraComponent>().First();
	var clouds=scene.GetAllComponents<VoidBreach.Environment.VBVolumetricClouds>().Single();
	var weather=scene.GetAllComponents<VoidBreach.Environment.VBCloudWeather>().Single();
	weather.PreviewTime=0;weather.Coverage=1;weather.Density=1.5f;clouds.GroundShadows=false;
	foreach(var r in scene.GetAllComponents<ModelRenderer>().ToArray())r.GameObject.Destroy();
	var wall=new GameObject(scene,true,"Opaque full-frame depth receiver");
	wall.WorldPosition=new Vector3(200,0,300);wall.WorldScale=new Vector3(20,4000,4000)/50;
	var model=wall.AddComponent<ModelRenderer>();model.Model=Model.Load("models/dev/box.vmdl");model.Tint=new Color(0.25f,0.3f,0.35f);
	camera.WorldPosition=new Vector3(0,0,128);camera.FieldOfView=70;
	foreach(var size in new[]{new Vector2Int(960,540),new Vector2Int(2659,1369),new Vector2Int(1277,719)})
	foreach(var pitch in new[]{-50f,-15f,10f})
	{
		camera.WorldRotation=Rotation.From(pitch,0,0);
		using var bitmap=new Bitmap(size.x,size.y);
		async Task<Color32[]> Capture(string suffix)
		{
			for(int i=0;i<4;i++){scene.EditorTick(0,1f/60);camera.RenderToBitmap(bitmap,false);await Task.Delay(25,cancellationToken);}
			System.IO.File.WriteAllBytes(System.IO.Path.Combine(exchangeDirectory,$"viewport-{size.x}-{pitch}-{suffix}.png"),bitmap.ToPng());return bitmap.GetPixels32();
		}
		clouds.Quality=VoidBreach.Environment.VBCloudQuality.Off;var off=await Capture("off");
		clouds.Quality=VoidBreach.Environment.VBCloudQuality.Medium;var on=await Capture("clouds");
		int changed=0;for(int i=0;i<off.Length;i++)if(Math.Abs(off[i].r-on[i].r)+Math.Abs(off[i].g-on[i].g)+Math.Abs(off[i].b-on[i].b)>12)changed++;
		results.Add(new{size,pitch,changed});
		if(changed>20)throw new Exception($"Cloud depth mask damaged {changed} opaque pixels at {size}, pitch {pitch}; captures={exchangeDirectory}");
	}
	return new{passed=results.Count,results,captures=exchangeDirectory};
}
finally{scene.Destroy();}
