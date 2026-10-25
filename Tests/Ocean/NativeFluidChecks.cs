using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Sandbox;
using Sandbox.Rendering;
using VoidBreach.Ocean;
using VoidBreach.Water;

// Temporary, command-line-gated native validation. Does not run in the user's editor session.
public static class FluidWaterValidation
{
	private static string Root => Environment.GetEnvironmentVariable( "VB_FLUID_TEST_ROOT" )
		?? Path.GetDirectoryName( Environment.GetCommandLineArgs()[Array.IndexOf( Environment.GetCommandLineArgs(), "-project" ) + 1] );
	private static string Output => Path.Combine( Root, "tmp", "fluid-native.log" );
	private static SceneEditorSession _session;
	private static int _ticks;
	private static DateTime _playStarted;
	private static bool _checkedSwimming, _complete;
	private static int _extraTicks;
	private static DateTime _riverStarted;
	private static VBFFTOcean _pausedWater;
	[EditorEvent.Frame]
	public static void Frame()
	{
		if ( !Environment.GetCommandLineArgs().Contains( "-vb-fluid-validation" ) ) return;
		if ( _complete ) { Extended(); return; }
		try
		{
			_ticks++;
			if ( _ticks == 120 ) Open( "lake" );
			if ( _ticks == 240 ) { var water = _session.Scene.GetAll<VBFFTOcean>().First(); water.ApplySurfaceImpulse( new Vector3( -150, -200, 0 ), 80, 130 ); }
			if ( _ticks == 270 ) Capture( "lake" );
			if ( _ticks == 300 ) Open( "river" );
			if ( _ticks == 450 ) Capture( "river" );
			if ( _ticks == 480 ) Open( "ocean" );
			if ( _ticks == 630 ) Capture( "ocean" );
			if ( _ticks == 660 )
			{
				_session = SceneEditorSession.CreateFromPath( "water/scenes/fft_physics_showcase.scene" );
				EditorScene.Play( false, _session ); _playStarted = DateTime.MinValue;
				File.AppendAllText( Output, "Started native Play-mode physics.\n" );
			}
			if ( _ticks <= 660 || !Game.IsPlaying || Game.ActiveScene is null ) return;
			var scene = Game.ActiveScene;
			if ( !scene.GetAll<VBFFTOcean>().Any() || !scene.GetAll<PlayerController>().Any() ) return;
			if ( _playStarted == DateTime.MinValue ) _playStarted = DateTime.UtcNow;
			var elapsed = (DateTime.UtcNow - _playStarted).TotalSeconds;
			if ( elapsed > 3 && !_checkedSwimming )
			{
				_checkedSwimming = true;
				var player = scene.GetAll<PlayerController>().First();
				var mode = player.GetComponent<VBMoveModeSwim>();
				File.AppendAllText( Output, $"SWIM isSwimming={player.IsSwimming} score={mode.Score(player)} level={mode.SampledWaterLevel} position={player.WorldPosition}\n" );
			}
			if ( elapsed < 14 ) return;
			foreach ( var body in scene.GetAll<Rigidbody>() )
				File.AppendAllText( Output, $"BODY {body.GameObject.Name}: position={body.WorldPosition} velocity={body.Velocity} managed={body.Tags.Has(VBFluidBody.ManagedTag)}\n" );
			var waterSurface = scene.GetAll<VBFFTOcean>().First();
			var floating = scene.GetAll<Rigidbody>().First( x => x.GameObject.Name.StartsWith( "Floating timber" ) );
			var sinking = scene.GetAll<Rigidbody>().First( x => x.GameObject.Name.StartsWith( "Dense block" ) );
			File.AppendAllText( Output, $"BUOYANCY {(MathF.Abs(floating.WorldPosition.z)<100 && floating.Velocity.WithZ(0).Length>15 ? "PASS" : "FAIL")}\n" );
			File.AppendAllText( Output, $"SINK {(sinking.WorldPosition.z < -70 ? "PASS" : "FAIL")}\n" );
			File.AppendAllText( Output, $"VOLUME native={waterSurface.PhysicsVolume.IsValid()} density={waterSurface.PhysicsVolume?.FluidDensity} retention={waterSurface.PhysicsRetainedEnergy}\n" );
			var energy = waterSurface.InteractionEnergy;
			File.AppendAllText( Output, $"INTERACTIONS energy={energy} {(float.IsFinite(energy) && energy>0 ? "PASS" : "FAIL")}\nDONE\n" );
			_complete = true;
		}
		catch ( Exception e ) { File.AppendAllText( Output, e + "\n" ); _complete = true; }
	}
	private static void Extended()
	{
		try
		{
			_extraTicks++;
			if ( !Game.IsPlaying || Game.ActiveScene is null ) return;
			var scene = Game.ActiveScene;
			if ( _extraTicks == 1 )
			{
				var player = scene.GetAll<PlayerController>().First(); var mode = player.GetComponent<VBMoveModeSwim>();
				mode.Score( player ); var flowMove = mode.UpdateMove( Rotation.Identity, Vector3.Zero );
				File.AppendAllText( Output, $"CURRENT swimMove={flowMove} {(flowMove.x > 15 ? "PASS" : "FAIL")}\n" );
				player.WorldPosition = new Vector3( 0, -350, 200 );
				File.AppendAllText( Output, $"EXIT score={mode.Score(player)} {(mode.Score(player)<0 ? "PASS" : "FAIL")}\n" );
			}
			if ( _extraTicks == 45 )
			{
				_pausedWater = scene.GetAll<VBFFTOcean>().First(); _pausedWater.Enabled = false;
			}
			if ( _extraTicks == 90 )
			{
				var water = _pausedWater;
				File.AppendAllText( Output, $"DISABLE volumeReleased={!water.PhysicsVolume.IsValid()} {(!water.PhysicsVolume.IsValid() ? "PASS" : "FAIL")}\n" ); water.Enabled = true;
			}
			if ( _extraTicks == 150 )
			{
				var water = scene.GetAll<VBFFTOcean>().First();
				File.AppendAllText( Output, $"REENABLE volumeValid={water.PhysicsVolume.IsValid()} {(water.PhysicsVolume.IsValid() ? "PASS" : "FAIL")}\n" );
				EditorScene.Stop(); _session = SceneEditorSession.CreateFromPath( "water/scenes/fft_river_showcase.scene" );
				EditorScene.Play( false, _session ); _riverStarted = DateTime.MinValue;
			}
			if ( _extraTicks > 150 && _extraTicks < 10000 )
			{
				var water = scene.GetAll<VBFFTOcean>().FirstOrDefault();
				if ( water?.Shape != FftWaterShape.River ) return;
				if ( _riverStarted == DateTime.MinValue ) _riverStarted = DateTime.UtcNow;
				if ( (DateTime.UtcNow - _riverStarted).TotalSeconds < 8 ) return;
				var body = scene.GetAll<Rigidbody>().First();
				File.AppendAllText( Output, $"RIVER body={body.WorldPosition} velocity={body.Velocity} {(body.WorldPosition.x>-700 ? "PASS" : "FAIL")}\nNATIVE_DONE\n" ); _extraTicks = 10000;
			}
		}
		catch ( Exception e ) { File.AppendAllText( Output, "EXTENDED ERROR " + e + "\n" ); _extraTicks = 10000; }
	}
	private static void Open( string shape )
	{
		_session = SceneEditorSession.CreateFromPath( $"water/scenes/fft_{shape}_showcase.scene" );
		File.AppendAllText( Output, $"Opened {shape}.\n" );
	}
	private static void Capture( string shape )
	{
		var scene = _session.Scene; var source = scene.GetAll<CameraComponent>().First();
		using var camera = new SceneCamera( "Fluid validation" )
		{
			World = scene.SceneWorld, Position = source.WorldPosition, Rotation = source.WorldRotation,
			FieldOfView = 65, ZNear = 2, ZFar = 500000, EnablePostProcessing = true, Size = new Vector2( 1280, 720 )
		};
		using var target = Texture.CreateRenderTarget().WithSize( 1280, 720 ).Create();
		typeof( SceneCamera ).GetMethod( "RenderToTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )
			.Invoke( camera, new object[] { target, (Vector2?)null, new ViewSetup() } );
		using var bitmap = new Bitmap( 1280, 720, false ); bitmap.SetPixels( target.GetPixels().Select( p => (Color)p ).ToArray() );
		File.WriteAllBytes( Path.Combine( Root, "tmp", $"fluid-{shape}.png" ), bitmap.ToPng() );
		var surface = scene.GetAll<VBFFTOcean>().First();
		File.AppendAllText( Output, $"RENDER {shape}: validVolume={surface.PhysicsVolume.IsValid()} cpuEnergy={surface.PhysicsRetainedEnergy}\n" );
	}
}
