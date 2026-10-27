// Native image and lifecycle regression. Runs outside the user's active scene.
var scene = Scene.CreateEditorScene();
var checks = new System.Collections.Generic.List<string>();
void Check( bool pass, string message )
{
	if ( !pass ) throw new Exception( message );
	checks.Add( message );
}
try
{
	using var scope = scene.Push();
	scene.LoadFromFile( "environment/scenes/cloud_showcase.scene" );
	var camera = scene.GetAllComponents<CameraComponent>().First();
	var original = scene.GetAllComponents<VoidBreach.Environment.VBCloudWeather>().First();
	original.GameObject.Destroy();
	var fixture = GameObject.Clone( PrefabFile.Load( "environment/prefabs/volumetric_clouds.prefab" ) );
	var weather = fixture.GetComponent<VoidBreach.Environment.VBCloudWeather>();
	var clouds = fixture.GetComponent<VoidBreach.Environment.VBVolumetricClouds>();
	Check( clouds.Weather == weather, "Prefab clone remaps the internal weather reference" );
	Check( fixture.NetworkMode == NetworkMode.Object, "Prefab is configured for live network property replication" );
	Check( fixture.GetComponent<PostProcessVolume>().IsInfinite, "Prefab applies to cameras throughout the scene" );
	foreach ( var pass in new[] { "march", "composite", "shadows" } )
	{
		var material = Material.Load( $"environment/materials/cloud_{pass}.vmat" );
		Check( Shader.Load( material.ShaderName ).IsValid, $"{pass}: native shader loads" );
	}
	var noise = Texture.Load( "environment/textures/cloud_noise.tga" );
	var weatherMap = Texture.Load( "environment/textures/cloud_weather.tga" );
	Check( !noise.IsError && noise.Width == 2080 && noise.Height == 1040, "Packed 128-cubed volume loads at its original dimensions" );
	Check( !weatherMap.IsError && weatherMap.Width == 512 && weatherMap.Height == 512, "Independent coverage and cloud-type map loads" );
	weather.PreviewTime = 0;
	clouds.GroundShadows = false;
	using var bitmap = new Bitmap( 960, 540 );
	async Task<Color32[]> Capture( string label )
	{
		for ( int j = 0; j < 3; j++ )
		{
			cancellationToken.ThrowIfCancellationRequested();
			scene.EditorTick( 0, 0.016f );
			camera.RenderToBitmap( bitmap, false );
			await Task.Delay( 20, cancellationToken );
		}
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( exchangeDirectory, $"cloud-test-{label}.png" ), bitmap.ToPng() );
		return bitmap.GetPixels32();
	}
	int Changed( Color32[] a, Color32[] b, int firstRow, int lastRow )
	{
		int result = 0;
		for ( int i = firstRow * 960; i < lastRow * 960; i++ )
			if ( Math.Abs( a[i].r - b[i].r ) + Math.Abs( a[i].g - b[i].g ) + Math.Abs( a[i].b - b[i].b ) > 12 ) result++;
		return result;
	}
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.Off;
	var off = await Capture( "off" );
	Check( clouds.RenderCount == 0, "Off records no cloud commands" );
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.Medium;
	var medium = await Capture( "medium" );
	Check( clouds.LastStepCount == 48 && clouds.LastResolutionDivisor == 2, "Medium renders at half resolution with 48 integration steps" );
	Check( Changed( off, medium, 0, 280 ) > 10000, "Native volumetric pass visibly changes the sky" );
	Check( Changed( off, medium, 420, 540 ) < 100, "Sky composition leaves opaque ground intact when shadows are off" );
	int towerChanges = 0;
	for ( int y = 280; y < 360; y++ )
		for ( int x = 205; x < 230; x++ )
		{
			var i = y * 960 + x;
			if ( Math.Abs( off[i].r - medium[i].r ) + Math.Abs( off[i].g - medium[i].g ) + Math.Abs( off[i].b - medium[i].b ) > 12 ) towerChanges++;
		}
	Check( towerChanges < 20, "Full-resolution mask preserves the foreground tower silhouette" );
	weather.PreviewTime = 100;
	var wind = await Capture( "wind" );
	Check( Changed( medium, wind, 0, 280 ) > 10000, "Shared time and wind move the actual cloud density" );
	weather.PreviewTime = 0;
	weather.Coverage = 0.25f;
	var sparse = await Capture( "sparse" );
	weather.Coverage = 0.75f;
	var dense = await Capture( "dense" );
	Check( Changed( off, dense, 0, 280 ) > Changed( off, sparse, 0, 280 ) + 5000, "Increasing coverage forms more cloud banks rather than moving a repeated tile" );
	weather.Coverage = 0.5f;
	var initialWind = weather.Wind;
	weather.Wind = Vector2.Zero;
	var calm = await Capture( "calm" );
	weather.PreviewTime = 100;
	var laterCalm = await Capture( "calm-later" );
	Check( Changed( calm, laterCalm, 0, 280 ) < 100, "Zero wind keeps cloud shapes stable across preview time" );
	weather.Wind = initialWind;
	weather.PreviewTime = 0;
	clouds.GroundShadows = true;
	var shadows = await Capture( "shadows" );
	Check( Changed( medium, shadows, 420, 540 ) > 1000, "Projected cloud density visibly attenuates sunlit ground" );
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.Low;
	await Capture( "low" );
	Check( clouds.LastStepCount == 24 && clouds.LastResolutionDivisor == 4, "Low uses quarter resolution and 24 integration steps" );
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.High;
	await Capture( "high" );
	Check( clouds.LastStepCount == 72 && clouds.LastResolutionDivisor == 2, "High uses half resolution and 72 integration steps" );
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.Ultra;
	await Capture( "ultra" );
	Check( clouds.LastStepCount == 96 && clouds.LastResolutionDivisor == 1, "Ultra renders at native resolution with 96 integration steps" );
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.Medium;
	var before = clouds.RenderCount;
	weather.Coverage = 0;
	var clear = await Capture( "clear" );
	Check( clouds.RenderCount == before && Changed( clear, off, 0, 540 ) < 1000, "Zero coverage removes the effect and records no commands (allows subpixel shadow jitter)" );
	weather.Coverage = 0.5f;
	weather.Density = 0;
	await Capture( "zero-density" );
	Check( clouds.RenderCount == before, "Zero density skips all rendering" );
	weather.Density = 1.1f;
	clouds.Enabled = false;
	await Capture( "disabled" );
	Check( clouds.RenderCount == before, "Disabling the component removes its passes" );
	clouds.Enabled = true;
	await Capture( "reenabled" );
	Check( clouds.RenderCount > before, "Re-enabling restores the passes without persistent targets" );
	before = clouds.RenderCount;
	camera.Orthographic = true;
	await Capture( "orthographic" );
	Check( clouds.RenderCount == before, "Unsupported orthographic cameras are skipped" );
	return new { passed = checks.Count, checks, exchangeDirectory, multiplayer = "Configuration only; no connected second client in this regression." };
}
catch ( Exception ex )
{
	throw new Exception( Json.Serialize( new { error = ex.Message, checks, exchangeDirectory } ) );
}
finally
{
	scene.Destroy();
}
