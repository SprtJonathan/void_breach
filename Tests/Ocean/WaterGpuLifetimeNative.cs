// One runtime lifecycle stress cycle, invoked by run_water_gpu_lifetime.ps1.
// No readback: this tests resource ownership and deferred rendering, not pixels.
if ( !Game.IsPlaying || Game.ActiveScene is null ) throw new Exception( "Play mode required." );
var scene = Game.ActiveScene;
var created = new System.Collections.Generic.List<GameObject>();
var waters = new System.Collections.Generic.List<VoidBreach.Ocean.VBFFTOcean>();
var retirement = typeof(VoidBreach.Ocean.VBFFTOcean).Assembly.GetType( "VoidBreach.Water.FluidGpuResources" );
int Counter( string name ) => retirement is null ? 0 : (int)retirement.GetProperty( name ).GetValue( null );
var releasedBefore = Counter( "ReleasedBatches" );
var failedBefore = retirement?.GetProperty( "FailedBatches" ) is null ? 0 : Counter( "FailedBatches" );
async Task WaitUntil( Func<bool> ready, string stage )
{
	for ( var wait = 0; wait < 600 && !ready(); wait++ ) await Task.Delay( 50, cancellationToken );
	if ( !ready() ) throw new Exception( "Timed out: " + stage );
}
object Gpu( VoidBreach.Ocean.VBFFTOcean water ) => typeof(VoidBreach.Ocean.VBFFTOcean)
	.GetField( "_gpuField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( water );
bool HasGpu( VoidBreach.Ocean.VBFFTOcean water ) => Gpu( water ).GetType().GetProperty( "Output" ).GetValue( Gpu( water ) ) is not null;
try
{
	for ( var wait = 0; wait < 300 && !scene.Camera.IsValid(); wait++ ) await Task.Delay( 100, cancellationToken );
	if ( !scene.Camera.IsValid() ) throw new Exception( "No runtime camera after 30 seconds." );
	var position = scene.Camera.WorldPosition - Vector3.Up * 64;
	foreach ( var shape in new[] { VoidBreach.Ocean.FftWaterShape.Lake, VoidBreach.Ocean.FftWaterShape.Ocean, VoidBreach.Ocean.FftWaterShape.River } )
	{
		var go = new GameObject( false, "GPU lifetime " + shape ); go.SetParent( scene, false );
		go.NetworkMode = NetworkMode.Never; go.WorldPosition = position; created.Add( go );
		var water = go.AddComponent<VoidBreach.Ocean.VBFFTOcean>();
		water.Shape = shape; water.Size = new Vector2( 1200, 1200 ); water.NativePhysics = false;
		water.UnderwaterEffects = false; water.WaveHeight = 0; water.Resolution = 64;
		go.Enabled = true; waters.Add( water );
	}
	await WaitUntil( () => waters.All( w => w.SimulationClock > 0.3f && HasGpu( w ) ), "physics and GPU startup" );
	foreach ( var editorSession in Editor.SceneEditorSession.All.Where( s => s.Scene.IsEditor && !ReferenceEquals( s.Scene, scene ) ) )
		foreach ( var editorWater in editorSession.Scene.GetAll<VoidBreach.Ocean.VBFFTOcean>() )
			foreach ( var runtimeWater in scene.GetAll<VoidBreach.Ocean.VBFFTOcean>() )
				if ( ReferenceEquals( Gpu( editorWater ), Gpu( runtimeWater ) ) )
					throw new Exception( "Editor and Play share the same GPU field instance." );
	for ( var tick = 0; tick < 24; tick++ )
	{
		foreach ( var water in waters ) water.ApplySurfaceImpulse( position + new Vector3( tick * 3, 0, 0 ), 40, 120 );
		await Task.Delay( 30, cancellationToken );
	}
	foreach ( var resolution in new[] { 64, 256, 128 } )
	{
		foreach ( var water in waters ) water.InteractionResolution = resolution;
		await WaitUntil( () => waters.All( w => (int)Gpu( w ).GetType().GetField( "_resolution", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( Gpu( w ) ) == resolution ), "GPU resize " + resolution );
		foreach ( var water in waters ) water.ApplySurfaceImpulse( position, 40, 120 );
		var clock = waters[0].SimulationClock;
		await WaitUntil( () => waters[0].SimulationClock - clock > 0.1f, "resize simulation progress" );
	}
	for ( var toggle = 0; toggle < 4; toggle++ )
	{
		foreach ( var water in waters ) water.Interactions = false;
		await WaitUntil( () => waters.All( w => !HasGpu( w ) ), "GPU disabled" );
		foreach ( var water in waters ) water.Interactions = true;
		await WaitUntil( () => waters.All( HasGpu ), "GPU reenabled" );
		foreach ( var water in waters ) water.ApplySurfaceImpulse( position, 40, 120 );
		var clock = waters[0].SimulationClock;
		await WaitUntil( () => waters[0].SimulationClock - clock > 0.1f, "reenabled simulation progress" );
	}
	await Task.Delay( 200, cancellationToken );
	if ( waters.Any( w => w.InteractionGpuDispatches == 0 ) ) throw new Exception( "A liquid never rendered its compute simulation." );
}
finally
{
	foreach ( var go in created ) if ( go.IsValid() ) go.Destroy();
}
await Task.Delay( 500, cancellationToken );
GC.Collect(); GC.WaitForPendingFinalizers();
await Task.Delay( 1000, cancellationToken );
if ( retirement is not null )
{
	await WaitUntil( () => Counter( "PendingBatches" ) == 0, "deferred resources actually released" );
	if ( Counter( "ReleasedBatches" ) <= releasedBefore ) throw new Exception( "No GPU retirement completed" );
	if ( retirement.GetProperty( "FailedBatches" ) is not null && Counter( "FailedBatches" ) != failedBefore ) throw new Exception( "GPU retirement reported an error" );
}
return new { passed = true, liquids = waters.Count, dispatches = waters.Sum( w => w.InteractionGpuDispatches ),
	retired = Counter( "ReleasedBatches" ) - releasedBefore, pending = Counter( "PendingBatches" ) };
