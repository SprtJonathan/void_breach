using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Sandbox;
using Sandbox.Rendering;
using VoidBreach.Ocean;
using VoidBreach.Water;

// One editor scene/window, gated by a dedicated launch flag; never modifies the saved scene.
public static class NativeWaterInteractionChecks
{
	private static string Root => Environment.GetEnvironmentVariable( "VB_WATER_INTERACTION_ROOT" ) ?? Project.Current?.GetRootPath();
	private static string Output => Path.Combine( Root, "tmp", "water-interaction-native.log" );
	private static SceneEditorSession _session;
	private static int _ticks, _stage;
	private static DateTime _started;
	private static float _startedClock;
	private static VBFFTOcean _water;
	private static PlayerController _player;
	private static VBCombatWeapon _weapon;
	private static CameraComponent _testCamera;
	private static Rigidbody _floating, _sinking;
	private static Vector3 _anchor;
	private static int _entries;
	private static object _initialSpectrum;
	private static bool _live, _ownsPlay;
	private static readonly System.Collections.Generic.List<GameObject> Created = new();
	private static readonly System.Collections.Generic.Dictionary<PlayerController, bool> CameraControls = new();
	public static void StopLive() { _stage = 9; Cleanup(); _live = false; }
	public static void StartLive()
	{
		if ( _live && _stage != 9 ) throw new InvalidOperationException( "Validation already running" );
		_session = SceneEditorSession.Active ?? throw new InvalidOperationException( "No active scene" );
		File.WriteAllText( Output, "Validating in existing editor\n" );
		_live = true; _ownsPlay = !Game.IsPlaying; _stage = Game.IsPlaying ? 2 : 1; _ticks = 180;
	}
	private static void Cleanup()
	{
		foreach ( var go in Created ) if ( go.IsValid() ) go.Destroy();
		Created.Clear();
		foreach ( var pair in CameraControls ) if ( pair.Key.IsValid() ) pair.Key.UseCameraControls = pair.Value;
		CameraControls.Clear();
		if ( _live && _ownsPlay && Game.IsPlaying ) EditorScene.Stop();
	}
	private static void Check( bool pass, string name, string detail ) => File.AppendAllText( Output, $"{name} {(pass ? "PASS" : "FAIL")} {detail}\n" );
	private static Rigidbody CreateBody( Scene scene, string name, Vector3 position, float mass )
	{
		var go = new GameObject( false, name ); go.SetParent( scene, false ); go.WorldPosition = position; Created.Add( go );
		var box = go.AddComponent<BoxCollider>(); box.Scale = new Vector3( 64, 32, 16 );
		var body = go.AddComponent<Rigidbody>(); body.MassOverride = mass; body.Gravity = true; body.MotionEnabled = true;
		go.Enabled = true; return body;
	}
	[EditorEvent.Frame]
	public static void Frame()
	{
		if ( Root is null || (!_live && !Environment.GetCommandLineArgs().Contains( "-vb-water-interactions" )) || _stage == 9 ) return;
		try
		{
			_ticks++;
			if ( _stage == 0 && _ticks >= 120 )
			{
				_session = SceneEditorSession.CreateFromPath( "scenes/basic_example.scene" ); _ticks = 0; _stage = 1;
				File.AppendAllText( Output, "Opened basic_example\n" ); return;
			}
			if ( _stage == 1 && _ticks >= 180 )
			{
				EditorScene.Play( false, _session ); _stage = 2; return;
			}
			if ( !Game.IsPlaying || Game.ActiveScene is null ) return;
			var scene = Game.ActiveScene;
			using var sceneScope = scene.Push();
			// Player creation is asynchronous. Disable camera writers that appear after setup too.
			if ( _stage >= 3 )
			{
				foreach ( var controller in scene.GetAll<PlayerController>() )
				{
					if ( !CameraControls.ContainsKey( controller ) ) CameraControls[controller] = controller.UseCameraControls;
					controller.UseCameraControls = false;
				}
				if ( _testCamera.IsValid() )
				{
					_testCamera.WorldPosition = _anchor + new Vector3( -110, -135, 100 );
					_testCamera.WorldRotation = Rotation.LookAt( _anchor - _testCamera.WorldPosition );
				}
			}
			if ( _stage == 2 )
			{
				_water = scene.GetAll<VBFFTOcean>().FirstOrDefault();
				if ( !_water.IsValid() || !scene.Camera.IsValid() ) return;
				_anchor = _water.WorldPosition + new Vector3( 11000, 11000, 0 );
				foreach ( var controller in scene.GetAll<PlayerController>() )
				{
					CameraControls[controller] = controller.UseCameraControls; controller.UseCameraControls = false;
				}
				var camObject = new GameObject( false, "Water interaction test camera" ); camObject.SetParent( scene, false );
				Created.Add( camObject );
				camObject.WorldPosition = _anchor + new Vector3( -110, -135, 100 );
				camObject.WorldRotation = Rotation.LookAt( _anchor - camObject.WorldPosition );
				var cam = camObject.AddComponent<CameraComponent>(); cam.Priority = -10000; cam.ZFar = 50000;
				_testCamera = cam;
				cam.IsMainCamera = true; camObject.Enabled = true;
				var playerObject = GameObject.Clone( "player/player controller.prefab", new CloneConfig { StartEnabled = false, Parent = scene,
					Transform = new Transform( _anchor + new Vector3( 70, 0, 200 ) ) } );
				playerObject.NetworkMode = NetworkMode.Never;
				Created.Add( playerObject );
				_player = playerObject.GetComponent<PlayerController>( true );
				_player.UseInputControls = true; _player.UseLookControls = false; _player.UseCameraControls = false;
				_player.UseAnimatorControls = false;
				playerObject.Enabled = true;
				var weaponObject = new GameObject( false, "Native water hitscan test" ); weaponObject.SetParent( scene, false );
				Created.Add( weaponObject );
				weaponObject.WorldPosition = _anchor + new Vector3( 0, 0, 110 );
				weaponObject.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, -1 ) );
				_weapon = weaponObject.AddComponent<VBCombatWeapon>();
				_weapon.Ballistics = new BaseCombatWeapon.BallisticConfig { Damage = 0, Pellets = 1, Range = 800, Radius = 0, Force = 0 };
				weaponObject.Enabled = true;
				_floating = CreateBody( scene, "Water validation light body", _anchor + new Vector3( 220, 90, 100 ), 100 );
				_sinking = CreateBody( scene, "Water validation dense body", _anchor + new Vector3( 220, -60, 100 ), 2000 );
				_initialSpectrum = typeof(VBFFTOcean).GetField("_spectrumData",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(_water);
				_started = DateTime.UtcNow; _startedClock = _water.SimulationClock; _stage = 3; return;
			}
			var elapsed = (_water.SimulationClock - _startedClock) / MathF.Max( _water.Speed, 0.001f );
			if ( (DateTime.UtcNow - _started).TotalSeconds > 45 ) throw new TimeoutException( $"Simulation stalled at {elapsed}s, stage {_stage}" );
			if ( _stage == 3 && elapsed > 0.4 )
			{
				var before = _water.ShotImpactCount;
				var distantBefore = _water.DistantImpactCount;
				File.AppendAllText( Output, $"CAMERA {scene.Camera.GameObject.Name} {scene.Camera.WorldPosition} field={((FluidSurfaceField)typeof(VBFFTOcean).GetField("_field",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(_water)).Origin} clock={elapsed}\n" );
				_weapon.PrimaryAttack(); // Real native hitscan constructs ShotEffect with Origin=null.
				Check( _water.ShotImpactCount > before && _water.DistantImpactCount == distantBefore, "NATIVE_SHOT", $"impacts={_water.ShotImpactCount} energy={_water.InteractionEnergy} distantDelta={_water.DistantImpactCount-distantBefore}" );
				var count = _water.ShotImpactCount;
				_weapon.ShootEffects( new BaseCombatWeapon.ShotEffect( _anchor - Vector3.Up * 100, false, Vector3.Up,
					null, null, null, true ) );
				Check( _water.ShotImpactCount > count, "PELLET", "NoEvents still produces a surface impact" );
				_stage = 4;
			}
			if ( _stage == 4 && elapsed > 0.65 )
			{
				var field = (FluidSurfaceField)typeof(VBFFTOcean).GetField("_field",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(_water);
				var disturbance = field.Sample( new Vector2( _anchor.x, _anchor.y ) );
				Check( field.Energy > 0, "PROPAGATION", $"nearEnergy={field.Energy} atHit={disturbance}" );
				Check( _water.SplashParticleCount > 0 && _water.SplashParticleCount <= 256, "SPRAY", $"particles={_water.SplashParticleCount}" );
				_stage = 5;
			}
			if ( _stage == 5 && elapsed > 2.5 )
			{
				var interactor = _player.GetComponent<VBWaterInteractor>();
				Check( interactor.EntryCount > 0, "PLAYER_ENTRY", $"events={interactor.EntryCount} pos={_player.WorldPosition}" );
				Check( _player.IsSwimming, "PLAYER_SWIM", $"level={_player.GetComponent<VBMoveModeSwim>().SampledWaterLevel}" );
				_entries = interactor.ExitCount;
				_player.WorldPosition = _anchor + new Vector3( 70, 0, -8 ); _player.Body.Velocity = Vector3.Up * 300;
				_stage = 6;
			}
			if ( _stage == 6 && elapsed > 4 )
			{
				Check( _player.GetComponent<VBWaterInteractor>().ExitCount > _entries, "PLAYER_EXIT", $"events={_player.GetComponent<VBWaterInteractor>().ExitCount}" );
				var farBefore = _water.DistantImpactCount;
				FluidImpacts.Segment( scene, _anchor + new Vector3( 7000, 0, 200 ), _anchor + new Vector3( 7000, 0, -200 ) );
				Check( _water.DistantImpactCount > farBefore, "DISTANT_SHOT", $"fallbacks={_water.DistantImpactCount}" );
				Check( ReferenceEquals( _initialSpectrum, typeof(VBFFTOcean).GetField("_spectrumData",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(_water) ),
					"WIND_NO_REBUILD", $"direction={_water.CurrentWindDirection}" );
				Check( _water.PhysicsVolume.IsValid(), "NATIVE_VOLUME", $"density={_water.PhysicsVolume.FluidDensity}" );
				Check( MathF.Abs( _floating.WorldPosition.z - _water.WorldPosition.z ) < 45 && _floating.GetComponent<VBFluidBody>().IsValid(),
					"BUOYANCY", $"mass=100 pos={_floating.WorldPosition} velocity={_floating.Velocity}" );
				Check( _sinking.WorldPosition.z + 8 < _water.WorldPosition.z - 10 && _sinking.WorldPosition.z < _floating.WorldPosition.z - 20,
					"DENSE_SINK", $"mass=2000 pos={_sinking.WorldPosition} velocity={_sinking.Velocity}" );
				File.AppendAllText( Output, "INTERACTION_DONE\n" ); _stage = 9; Cleanup();
			}
		}
		catch ( Exception e ) { File.AppendAllText( Output, "ERROR " + e + "\nINTERACTION_DONE\n" ); _stage = 9; Cleanup(); }
	}
	private static void Capture( Scene scene, string name )
	{
		var source = scene.Camera;
		using var camera = new SceneCamera("Water impact validation") { World=scene.SceneWorld, Position=source.WorldPosition,
			Rotation=source.WorldRotation, FieldOfView=65, ZNear=2, ZFar=50000, EnablePostProcessing=true, Size=new Vector2(1280,720) };
		using var target = Texture.CreateRenderTarget().WithSize(1280,720).Create();
		typeof(SceneCamera).GetMethod("RenderToTexture",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
			.Invoke(camera,new object[]{target,(Vector2?)null,new ViewSetup()});
		using var bitmap = new Bitmap(1280,720,false); bitmap.SetPixels(target.GetPixels().Select(p=>(Color)p).ToArray());
		File.WriteAllBytes(Path.Combine(Root,"tmp",name),bitmap.ToPng());
	}
}
