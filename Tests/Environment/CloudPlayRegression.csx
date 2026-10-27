// Run in host Play. Inspects the real scene, then renders a temporary non-main sky camera.
// It never moves the player, changes weather, or saves runtime objects.
var scene = Game.ActiveScene;
if ( !Game.IsPlaying || scene is null ) throw new InvalidOperationException( "Start Play in the scene containing the cloud prefab." );
var weather = scene.GetAllComponents<VoidBreach.Environment.VBCloudWeather>().FirstOrDefault( x => x.Active );
var clouds = scene.GetAllComponents<VoidBreach.Environment.VBVolumetricClouds>().FirstOrDefault( x => x.Active );
if ( !weather.IsValid() || !clouds.IsValid() ) throw new Exception( "Cloud components are missing from the runtime scene; inspect the saved prefab patch." );
if ( clouds.Weather != weather ) throw new Exception( "Runtime cloud renderer lost its weather reference." );
var cameraObject = new GameObject( scene, false, "Cloud Play regression camera" );
cameraObject.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
cameraObject.NetworkMode = NetworkMode.Never;
try
{
	var main = scene.Camera;
	cameraObject.WorldPosition = new Vector3( main?.WorldPosition.x ?? 0, main?.WorldPosition.y ?? 0, Math.Max( weather.Altitude - 6000, 512 ) );
	cameraObject.WorldRotation = Rotation.From( -40, 10, 0 );
	var camera = cameraObject.AddComponent<CameraComponent>();
	camera.IsMainCamera = false;
	camera.Priority = int.MinValue;
	camera.EnablePostProcessing = true;
	camera.FieldOfView = 85;
	camera.ZFar = 10000; // Same clipping range as the player's actual camera.
	cameraObject.Enabled = true;
	using var bitmap = new Bitmap( 1280, 720 );
	var before = clouds.RenderCount;
	for ( int i = 0; i < 3; i++ )
	{
		await Task.Delay( 50, cancellationToken );
		camera.RenderToBitmap( bitmap, false );
	}
	if ( clouds.RenderCount <= before ) throw new Exception( "Cloud passes do not run in Play." );
	var path = System.IO.Path.Combine( exchangeDirectory, "cloud-play.png" );
	System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	return new { scene = scene.Name, path, weather.Altitude, weather.Thickness, weather.Erosion, weather.ShapeScale,
		clouds.MaximumDistance, clouds.RenderCount, playerCamera = main?.GameObject.Name, passed = true };
}
finally
{
	cameraObject.Destroy();
}
