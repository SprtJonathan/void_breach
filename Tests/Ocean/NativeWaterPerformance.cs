using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Rendering;
using VoidBreach.Ocean;

// Installed temporarily into Editor by the launcher. Never runs in an ordinary editor session.
public static class NativeWaterPerformance
{
	private static string Root => Environment.GetEnvironmentVariable( "VB_WATER_PERF_ROOT" ) ?? Project.Current?.GetRootPath();
	private static SceneEditorSession _session;
	private static SceneCamera _camera;
	private static Texture _target;
	private static VBFFTOcean _water;
	private static Scene _scene;
	private static bool _live, _savedEnabled, _savedInteractions;
	private static float _savedCaustics;
	private static bool _waterView;
	private static readonly Dictionary<EnvmapProbe, bool> Probes = new();
	private static int _frame, _phase;
	private static readonly List<double> _times = new();
	private static readonly string[] Phases = { "full", "no-caustics", "no-interactions", "no-probe", "no-water" };
	private static string Output => Path.Combine( Root, "tmp", $"water-perf-{(_live ? _waterView ? "live-ocean" : "live" : Environment.GetEnvironmentVariable("VB_WATER_PERF_LABEL"))}.log" );
	public static void StopLive() { if ( _live ) Cleanup(); _phase = Phases.Length; _live = false; }
	public static void StartLive( bool waterView = false )
	{
		if ( _live && _phase < Phases.Length ) throw new InvalidOperationException( "Benchmark already running" );
		_session = SceneEditorSession.Active ?? throw new InvalidOperationException( "No active editor scene" );
		_scene = Game.IsPlaying ? Game.ActiveScene : _session.Scene;
		_live = true; _waterView = waterView; _phase = 0; _frame = 180; _water = null; _times.Clear(); Probes.Clear();
		File.WriteAllText( Output, "Benchmark in existing editor; settings are restored afterwards.\n" );
	}
	private static void Cleanup()
	{
		_camera?.Dispose(); _target?.Dispose(); _camera = null; _target = null;
		if ( _live && _water.IsValid() )
		{
			_water.CausticStrength = _savedCaustics; _water.Interactions = _savedInteractions; _water.Enabled = _savedEnabled;
			foreach ( var p in Probes ) if ( p.Key.IsValid() ) p.Key.Enabled = p.Value;
		}
	}
	[EditorEvent.Frame]
	public static void Frame()
	{
		if ( Root is null || (!_live && !Environment.GetCommandLineArgs().Contains( "-vb-water-perf" )) || _phase >= Phases.Length ) return;
		try
		{
			_frame++;
			if ( _session is null )
			{
				if ( _frame < 120 ) return;
				_session = SceneEditorSession.CreateFromPath( "scenes/basic_example.scene" );
				_scene = _session.Scene;
				File.AppendAllText( Output, "Loaded basic_example\n" ); _frame = 0; return;
			}
			if ( _water is null )
			{
				if ( _frame < 180 ) return;
				_water = _scene.GetAll<VBFFTOcean>().First();
				_savedEnabled = _water.Enabled; _savedCaustics = _water.CausticStrength; _savedInteractions = _water.Interactions;
				foreach ( var probe in _scene.GetAll<EnvmapProbe>() ) Probes[probe] = probe.Enabled;
				_camera = new SceneCamera( "Water benchmark" ) { World = _scene.SceneWorld,
					Position = new Vector3( 0, -700, 180 ), Rotation = Rotation.LookAt( new Vector3( 500, 700, -212 ) ),
					FieldOfView = 70, ZNear = 2, ZFar = 50000, EnablePostProcessing = true, Size = new Vector2( 1280, 720 ) };
				if ( _waterView )
				{
					_camera.Position = _water.WorldPosition + new Vector3( 11000, 11000, 150 );
					_camera.Rotation = Rotation.LookAt( new Vector3( 0, 1, -0.25f ) );
				}
				_target = Texture.CreateRenderTarget().WithSize( 1280, 720 ).Create();
				var sampleMethod = typeof(VBFFTOcean).GetMethod("TrySample");
				var args = new object[] { new Vector3( 100, 100, -32 ), null };
				var watch = Stopwatch.StartNew();
				for(var i=0;i<2000;i++) sampleMethod.Invoke(_water,args);
				File.AppendAllText( Output, $"CPU 2000 queries={watch.Elapsed.TotalMilliseconds:F3}ms\n" );
				var step = typeof(VBFFTOcean).GetMethod("StepInteractions",BindingFlags.Instance|BindingFlags.NonPublic);
				watch.Restart();
				for(var i=0;i<1000;i++) step.Invoke(_water,new object[]{1f/60});
				File.AppendAllText( Output, $"CPU 1000 idle steps={watch.Elapsed.TotalMilliseconds:F3}ms\n" );
				var field = (VoidBreach.Water.FluidSurfaceField)typeof(VBFFTOcean).GetField("_field",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(_water);
				var center = field.Origin + Vector2.One * field.Extent * 0.5f;
				_water.ApplySurfaceImpulse(new Vector3(center.x,center.y,_water.WorldPosition.z),32,100);
				watch.Restart();
				for(var i=0;i<120;i++) step.Invoke(_water,new object[]{1f/60});
				File.AppendAllText( Output, $"CPU 120 active steps={watch.Elapsed.TotalMilliseconds:F3}ms\n" );
				_frame = 0; return;
			}
			var stopwatch = Stopwatch.StartNew();
			typeof(SceneCamera).GetMethod("RenderToTexture",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
				.Invoke(_camera,new object[]{_target,(Vector2?)null,new ViewSetup()});
			Graphics.FlushGPU();
			if ( _frame > 60 ) _times.Add( stopwatch.Elapsed.TotalMilliseconds );
			if ( _frame == 70 && _phase == 0 )
			{
				using var bitmap = new Bitmap(1280,720,false); bitmap.SetPixels(_target.GetPixels().Select(p=>(Color)p).ToArray());
				File.WriteAllBytes(Path.Combine(Root,"tmp","water-perf-view.png"),bitmap.ToPng());
			}
			if ( _frame < 180 ) return;
			_times.Sort();
			File.AppendAllText(Output,$"RENDER {Phases[_phase]} median={_times[_times.Count/2]:F3}ms p95={_times[(int)(_times.Count*.95)]:F3}ms (RenderToTexture+FlushGPU, 1280x720)\n");
			_phase++; _frame=0; _times.Clear();
			if(_phase==1) _water.CausticStrength=0;
			if(_phase==2) _water.Interactions=false;
			if(_phase==3) foreach(var probe in _scene.GetAll<EnvmapProbe>()) probe.Enabled=false;
			if(_phase==4) _water.Enabled=false;
			if(_phase==Phases.Length) { Cleanup(); File.AppendAllText(Output,"PERF_DONE\n"); }
		}
		catch(Exception e) { File.AppendAllText(Output,"ERROR "+e+"\nPERF_DONE\n"); _phase=Phases.Length; Cleanup(); }
	}
}
