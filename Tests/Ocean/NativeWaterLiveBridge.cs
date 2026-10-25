using System;
using System.IO;
using System.Text.Json.Nodes;
using Sandbox;
using Sandbox.Diagnostics;
using VoidBreach.Ocean;

// Temporary bridge for the editor that is already open. Never launches a process or opens a scene.
public static class WaterLiveValidation
{
	private static string Root => Project.Current?.GetRootPath();
	private static string _nonce;
	private static DateTime _next;
	[EditorEvent.Frame]
	public static void Frame()
	{
		if ( Root is null || DateTime.UtcNow < _next ) return;
		_next = DateTime.UtcNow.AddMilliseconds( 150 );
		var commandPath = Path.Combine( Root, "tmp", "water-live-command.json" );
		if ( !File.Exists( commandPath ) ) return;
		try
		{
			var command = JsonNode.Parse( File.ReadAllText( commandPath ) );
			if ( command["pid"]?.GetValue<int>() != Environment.ProcessId ) return;
			var nonce = command["nonce"]?.GetValue<string>();
			if ( nonce == _nonce ) return;
			_nonce = nonce;
			var action = command["action"]?.GetValue<string>();
			if ( action == "interactions" ) NativeWaterInteractionChecks.StartLive();
			if ( action is "benchmark" or "benchmark-water" ) NativeWaterPerformance.StartLive( action == "benchmark-water" );
			if ( action == "cancel" ) { NativeWaterInteractionChecks.StopLive(); NativeWaterPerformance.StopLive(); }
			var scene = Game.IsPlaying ? Game.ActiveScene : SceneEditorSession.Active?.Scene;
			var state = new { version = 5, nonce, pid = Environment.ProcessId, action, playing = Game.IsPlaying,
				building = Project.CompileGroup.IsBuilding || Project.CompileGroup.Compilers.Any( c => c.NeedsBuild ),
				compile = Project.CompileGroup.Compilers.Select( c => new { c.Name, success = c.Output?.Successful, errors = c.Diagnostics.Where( d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ).Select( d => d.GetMessage() ).ToArray() } ).ToArray(),
				scene = scene?.Name,
				water = scene?.GetAll<VBFFTOcean>().Select( w => new { w.Enabled, w.Interactions, w.CausticStrength, nativeVolume = w.PhysicsVolume.IsValid() } ).ToArray(),
				gpuMs = PerformanceStats.GpuFrametime, frameMs = PerformanceStats.FrameTime * 1000,
				scenes = SceneEditorSession.All.Select( s => new { name = s.Scene?.Name, path = s.Scene?.Source?.ResourcePath, active = s == SceneEditorSession.Active, dirty = s.HasUnsavedChanges } ).ToArray() };
			File.WriteAllText( Path.Combine( Root, "tmp", "water-live-state.json" ), System.Text.Json.JsonSerializer.Serialize( state ) );
		}
		catch ( Exception e ) { File.WriteAllText( Path.Combine( Root, "tmp", "water-live-state.json" ), e.ToString() ); }
	}
}
