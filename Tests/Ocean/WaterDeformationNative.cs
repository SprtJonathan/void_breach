// sbox-cli run_csharp method body, in Play. Readbacks/captures are test-only.
// The diagnostic material shades rasterized triangle normals: image changes
// cannot come from foam, normal maps, FFT waves, spray or animated textures.
if ( !Game.IsPlaying ) throw new InvalidOperationException( "Start Play first." );
var scene = Game.ActiveScene;
var created = new System.Collections.Generic.List<GameObject>();
var cameras = new System.Collections.Generic.Dictionary<PlayerController, bool>();
var results = new System.Collections.Generic.List<object>();
var anchor = new Vector3( 60000, 60000, 4096 );
var cameraPosition = anchor + new Vector3( 0, -200, 20 );
var cameraRotation = Rotation.LookAt( anchor - cameraPosition );
Texture target = null;
GameObject Make( string name, Vector3 p )
{
	var go = new GameObject( false, name ); go.SetParent( scene, false ); go.WorldPosition = p;
	go.NetworkMode = NetworkMode.Never; created.Add( go ); return go;
}
void Check( string name, bool pass, string detail )
{
	results.Add( new { name, pass, detail } ); Log.Info( $"Water geometry {name} {pass}: {detail}" );
}
try
{
	var go = Make( "Geometry-only water regression", anchor );
	var water = go.AddComponent<VoidBreach.Ocean.VBFFTOcean>();
	water.Shape = VoidBreach.Ocean.FftWaterShape.Lake; water.Size = new Vector2( 1600, 1200 );
	water.WaveHeight = 0; water.Foam = 0; water.InteractionFoam = 0;
	water.NativePhysics = false; water.UnderwaterEffects = false;
	water.DebugView = VoidBreach.Water.WaterDebugView.SurfaceGeometry;
	water.InteractionMeshSpacing = 4; water.Resolution = 128; go.Enabled = true;
	var cameraGo = Make( "Geometry test camera", cameraPosition );
	cameraGo.WorldRotation = cameraRotation;
	var camera = cameraGo.AddComponent<CameraComponent>(); camera.Priority = 30000;
	camera.IsMainCamera = true; camera.FieldOfView = 65; cameraGo.Enabled = true;
	async Task Pause( int ms )
	{
		for ( var left = ms; left > 0; left -= 40 )
		{
			await Task.Delay( Math.Min( left, 40 ), cancellationToken );
			foreach ( var pc in scene.GetAll<PlayerController>() )
			{
				if ( !cameras.ContainsKey( pc ) ) cameras.Add( pc, pc.UseCameraControls );
				pc.UseCameraControls = false;
			}
			cameraGo.WorldPosition = cameraPosition; cameraGo.WorldRotation = cameraRotation; camera.IsMainCamera = true;
		}
	}
	await Pause( 500 );
	var view = new SceneCamera( "Water deformation capture" ) { World = scene.SceneWorld,
		Position = cameraPosition, Rotation = cameraRotation, FieldOfView = 65,
		ZNear = 1, ZFar = 2000, EnablePostProcessing = false, Size = new Vector2( 960, 540 ) };
	view.RenderTags.Add( "water_surface" );
	target = Texture.CreateRenderTarget().WithSize( 960, 540 ).Create();
	var render = typeof(SceneCamera).GetMethod( "RenderToTexture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic );
	Color[] Capture( string suffix )
	{
		view.Position = cameraPosition; view.Rotation = cameraRotation;
		render.Invoke( view, new object[] { target, (Vector2?)null, new Sandbox.Rendering.ViewSetup() } );
		Graphics.FlushGPU();
		var pixels = target.GetPixels().Select( p => (Color)p ).ToArray();
		using var bitmap = new Bitmap( 960, 540, false ); bitmap.SetPixels( pixels );
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( Project.Current.GetRootPath(), "tmp", "water-geometry-" + suffix + ".png" ), bitmap.ToPng() );
		return pixels;
	}
	var baseline = Capture( "flat" );
	Check( "LOCAL_MESH_REFINED", water.SurfaceVertexCount > 129 * 129, $"vertices={water.SurfaceVertexCount}, spacing=4" );
	water.ApplySurfaceImpulse( anchor, 48, 140 );
	await Pause( 180 );
	var deformed = Capture( "impact" );
	await Pause( 400 );
	var propagated = Capture( "propagated" );
	int Changed( Color[] a, Color[] b ) => a.Where( (p, i) => MathF.Abs( p.r - b[i].r ) + MathF.Abs( p.g - b[i].g ) + MathF.Abs( p.b - b[i].b ) > 0.025f ).Count();
	var initialChanges = Changed( baseline, deformed ); var movingChanges = Changed( deformed, propagated );
	Check( "RASTERIZED_TRIANGLES_MOVE", initialChanges > 1000, $"changedPixels={initialChanges}; geometric normals only, foam=0, FFT=0, no particles" );
	Check( "TRIANGLES_CONTINUE_PROPAGATING", movingChanges > 1000, $"changedPixels={movingChanges}; no new impulse" );
	var gpu = typeof(VoidBreach.Ocean.VBFFTOcean).GetField( "_gpuField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( water );
	var texture = (Texture)gpu.GetType().GetProperty( "Output" ).GetValue( gpu );
	var heights = new Vector4[128 * 128]; texture.GetPixels<Vector4>( (0, 0, 128, 128), 0, 0, heights.AsSpan(), ImageFormat.RGBA32323232F );
	Check( "CREST_AND_TROUGH", heights.Max( p => p.x ) > 0.1f && heights.Min( p => p.x ) < -0.1f, $"min={heights.Min( p => p.x )}, max={heights.Max( p => p.x )}" );
	Check( "NO_SOURCE_PAINT", heights.Max( p => p.w ) < 0.2f, $"maximum foam={heights.Max( p => p.w )}" );
}
finally
{
	foreach ( var go in created.AsEnumerable().Reverse() ) if ( go.IsValid() ) go.Destroy();
	foreach ( var pair in cameras ) if ( pair.Key.IsValid() ) pair.Key.UseCameraControls = pair.Value;
	Graphics.FlushGPU(); target?.Dispose();
}
await Task.Delay( 1000, cancellationToken ); GC.Collect(); GC.WaitForPendingFinalizers();
await Task.Delay( 3000, cancellationToken );
return results;
