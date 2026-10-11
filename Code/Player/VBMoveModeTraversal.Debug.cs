using System;
using System.Collections.Generic;
using Sandbox;

public sealed partial class VBMoveModeTraversal
{
	[Property, Group( "Debug" )] public bool EnableTraversalDebug { get; set; }
	[Property, Group( "Debug" ), ShowIf( nameof(EnableTraversalDebug), true )] public bool ShowTraversalFeedback { get; set; } = true;
	[Property, Group( "Debug" ), ShowIf( nameof(EnableTraversalDebug), true ), Range( 0.2f, 3f )] public float FeedbackDuration { get; set; } = 1f;
	[Property, Group( "Debug" ), ShowIf( nameof(EnableTraversalDebug), true )] public bool DebugDrawTraces { get; set; } = true;
	[Property, Group( "Debug" ), ShowIf( nameof(EnableTraversalDebug), true )] public bool DebugIgnoreDepth { get; set; } = true;
	[Property, Group( "Debug" ), ShowIf( nameof(EnableTraversalDebug), true ), Range( 0.2f, 10f )] public float DebugTraceDuration { get; set; } = 2f;

	private readonly List<TraversalDebugTrace> _debugTraces = new();
	private bool _debugRecording, _debugHasAttempt, _debugRejected;
	private string _debugOutcome = "vb.traversal.debug.waiting", _debugDetail = string.Empty, _debugMetrics = string.Empty;
	private TimeSince _timeSinceDebugAttempt = 999f;
	private bool HasRecentDebugAttempt => EnableTraversalDebug && _debugHasAttempt
		&& (IsTraversing || _timeSinceDebugAttempt < MathF.Max( DebugTraceDuration, 0.2f ));
	public string DebugLastMotion { get; private set; } = string.Empty;

	public string DebugOutcomeToken => HasRecentDebugAttempt ? _debugOutcome : "vb.traversal.debug.waiting";
	public string DebugDetail => HasRecentDebugAttempt ? _debugDetail : string.Empty;
	public string DebugMetrics => HasRecentDebugAttempt ? _debugMetrics : string.Empty;
	public bool DebugRejected => HasRecentDebugAttempt && _debugRejected;
	public IReadOnlyList<TraversalDebugTrace> DebugTraces => _debugTraces;
	public bool DebugHasRecentAttempt => HasRecentDebugAttempt;
	public string DebugInputSummary => !EnableTraversalDebug ? string.Empty
		: $"State={State} | mode={Controller.Mode?.GetType().Name} | jump={Input.Down( "jump" )} | controls={Controller.UseInputControls} | ground={Controller.IsOnGround} | buffer={_hasBufferedJump} ({MathF.Min( _timeSinceJumpPress, 99f ):F2}/{JumpInputBuffer:F2}s) | cooldown={MathF.Max( Cooldown - _timeSinceEnded, 0f ):F2}s";

	/// <summary>A snapshot of the actual simulation query, never a separate traversal prediction.</summary>
	public readonly record struct TraversalDebugTrace(
		Vector3 Start, Vector3 End, Vector3 HitPosition, Vector3 Normal,
		string Purpose, string HitName, bool IsBody, bool Hit, bool StartedSolid, bool Passed, BBox BodyBounds, Capsule? BodyCapsule, Rotation BodyRotation );

	private bool CheckBodyTrace( Vector3 start, Vector3 end, SceneTraceResult trace, string purpose, BBox bounds, Capsule? capsule = null )
	{
		bool clear = !trace.StartedSolid && !trace.Hit;
		if ( !EnableTraversalDebug ) return clear;
		bool ongoing = !_debugRecording && IsTraversing;
		bool standalone = !_debugRecording && !ongoing && !clear;
		if ( standalone ) BeginDebugAttempt();
		if ( ongoing ) _debugRecording = true;
		RecordDebugTrace( start, end, trace, purpose, true, clear, bounds, capsule );
		if ( !clear ) RejectDebugAttempt( trace.StartedSolid ? "vb.traversal.debug.body_inside" : "vb.traversal.debug.path_blocked",
			$"{purpose} | {(capsule.HasValue ? "capsule" : "feet")} | StartedSolid={trace.StartedSolid} | {DebugHitName( trace )}" );
		if ( standalone || ongoing ) EndDebugAttempt();
		return clear;
	}
	private void BeginDebugAttempt()
	{
		if ( !EnableTraversalDebug || IsProxy ) return;
		_debugTraces.Clear();
		_debugRecording = _debugHasAttempt = true;
		_debugRejected = false;
		_timeSinceDebugAttempt = 0f;
		_debugOutcome = "vb.traversal.debug.checking";
		_debugDetail = string.Empty;
		_debugMetrics = Controller.IsValid() && Controller.Body.IsValid()
			? $"vZ={Controller.Body.Velocity.z:F0} u/s | ray reach={Controller.BodyRadius + MathF.Max( ReachDistance, 8f ):F0} u | capsule diameter={Controller.BodyRadius * MathF.Sqrt( 2f ):F1} u | height={Controller.BodyHeight:F0} u | inset={BodyTraceInset:F2} u"
			: string.Empty;
	}
	private void EndDebugAttempt() => _debugRecording = false;
	private void DebugBlockedAttempt( string token, string detail = "" )
	{
		if ( !EnableTraversalDebug ) return;
		BeginDebugAttempt(); RejectDebugAttempt( token, detail ); EndDebugAttempt();
	}
	private void SetDebugHeightWindow( float minimum, float maximum, float probeHeight, float wallDistance )
	{
		if ( !_debugRecording ) return;
		_debugMetrics += $" | probe Z=feet+{probeHeight:F0} | top Z=feet+[{minimum:F0}..{maximum + Skin:F0}] | wall={wallDistance:F1} u";
	}
	private void RejectDebugAttempt( string token, string detail = "", bool markTrace = false )
	{
		if ( !EnableTraversalDebug || !_debugRecording ) return;
		_debugRejected = true; _debugOutcome = token; _debugDetail = detail;
		if ( markTrace && _debugTraces.Count > 0 )
			_debugTraces[^1] = _debugTraces[^1] with { Passed = false };
	}
	private void AcceptDebugAttempt()
	{
		if ( !_debugRecording ) return;
		DebugMotionState( "moving" );
	}
	// Validation starts a physical move; only PostPhysicsStep confirms that the grip was reached.
	// Keep termination evidence separately so another Space press cannot erase it.
	private void DebugMotionState( string reason, string detail = "", bool ended = false )
	{
		if ( !EnableTraversalDebug || IsProxy ) return;
		string collisionDetail = reason == "collision" && _debugRejected ? $"{_debugOutcome} | {_debugDetail}" : string.Empty;
		_debugHasAttempt = true; _timeSinceDebugAttempt = 0f;
		_debugRejected = ended && reason != "complete";
		if ( string.IsNullOrEmpty( collisionDetail ) ) _debugOutcome = $"vb.traversal.debug.{reason}";
		float error = (WorldPosition - _destination).Length;
		_debugDetail = $"{State}{(ended ? " -> None" : "")} | error={error:F1} u | motion={_motionTime:F2}/{_duration:F2}s | {detail}{collisionDetail}";
		if ( ended && Controller.IsValid() && Controller.Body.IsValid() )
		{
			DebugLastMotion = $"{State} -> None: {reason} | error={error:F1} u | v={Controller.Body.Velocity} | gravity={Controller.Body.Gravity} | mode={Controller.Mode?.GetType().Name} | {detail}{collisionDetail}";
			Log.Info( $"Traversal debug: {DebugLastMotion}" );
		}
	}
	private static string DebugHitName( SceneTraceResult trace ) => trace.GameObject.IsValid()
		? $"{trace.GameObject.Name} ({trace.Component?.GetType().Name ?? "physics"})" : "-";
	private void RecordDebugTrace( Vector3 start, Vector3 end, SceneTraceResult trace, string purpose, bool body, bool passed, BBox bounds = default, Capsule? capsule = null )
	{
		if ( !_debugRecording ) return;
		var hitName = body ? $"{(capsule.HasValue ? "capsule" : "feet")} | {DebugHitName( trace )}" : DebugHitName( trace );
		var snapshot = new TraversalDebugTrace( start, end, trace.EndPosition, trace.Normal, purpose, hitName,
			body, trace.Hit, trace.StartedSolid, passed, bounds, capsule, WorldRotation );
		// Keep detection queries plus the latest actual motion/body sweep, without accumulating each physics tick.
		int previous = !IsTraversing ? -1 : _debugTraces.FindIndex( t => t.Purpose == purpose
			&& t.BodyCapsule.HasValue == capsule.HasValue && purpose is "motion" or "body" );
		if ( previous >= 0 ) _debugTraces[previous] = snapshot;
		else if ( _debugTraces.Count < 40 ) _debugTraces.Add( snapshot );
	}

	protected override void OnPreRender()
	{
		if ( IsProxy || Application.IsDedicatedServer ) return;
		if ( !EnableTraversalDebug )
		{
			_debugTraces.Clear(); _debugHasAttempt = _debugRecording = false; DebugLastMotion = string.Empty;
			return;
		}
		if ( !DebugDrawTraces || !HasRecentDebugAttempt || (_health.IsValid() && (_health.IsDead || _health.IsDowned)) ) return;
		var overlay = Scene.GetSystem<DebugOverlaySystem>();
		for ( int i = 0; i < _debugTraces.Count; i++ )
		{
			var trace = _debugTraces[i];
			var color = trace.Passed ? new Color( 0.45f, 0.85f, 0.3f ) : new Color( 0.95f, 0.25f, 0.22f );
			overlay.Line( trace.Start, trace.End, Color.Cyan.WithAlpha( 0.5f ), overlay: DebugIgnoreDepth );
			overlay.Line( trace.Start, trace.Hit ? trace.HitPosition : trace.End, color, overlay: DebugIgnoreDepth );
			if ( trace.Hit )
			{
				overlay.Box( trace.HitPosition, Vector3.One * 3f, color, overlay: DebugIgnoreDepth );
				overlay.Line( trace.HitPosition, trace.HitPosition + trace.Normal * 12f, Color.Magenta, overlay: DebugIgnoreDepth );
			}
			if ( trace.IsBody )
			{
				if ( trace.BodyCapsule is { } capsule )
				{
					overlay.Capsule( capsule, color.WithAlpha( 0.55f ), transform: new Transform( trace.Start ), overlay: DebugIgnoreDepth );
					overlay.Capsule( capsule, color.WithAlpha( 0.55f ), transform: new Transform( trace.End ), overlay: DebugIgnoreDepth );
				}
				else
				{
					overlay.Box( trace.BodyBounds, color.WithAlpha( 0.55f ), transform: new Transform( trace.Start, trace.BodyRotation ), overlay: DebugIgnoreDepth );
					overlay.Box( trace.BodyBounds, color.WithAlpha( 0.55f ), transform: new Transform( trace.End, trace.BodyRotation ), overlay: DebugIgnoreDepth );
				}
				for ( int cornerIndex = 0; cornerIndex < 4; cornerIndex++ )
				{
					var corner = trace.BodyCapsule is { } shape
						? Rotation.FromYaw( cornerIndex * 90f ).Forward * shape.Radius + shape.CenterA
						: new Vector3( cornerIndex < 2 ? trace.BodyBounds.Mins.x : trace.BodyBounds.Maxs.x,
							cornerIndex % 2 == 0 ? trace.BodyBounds.Mins.y : trace.BodyBounds.Maxs.y, trace.BodyBounds.Maxs.z );
					if ( !trace.BodyCapsule.HasValue ) corner = trace.BodyRotation * corner;
					overlay.Line( trace.Start + corner, trace.End + corner, color.WithAlpha( 0.4f ), overlay: DebugIgnoreDepth );
				}
			}
			// Text size is in world units. Compact indices map to the detailed HUD rows without covering the ledge.
			overlay.Text( trace.End + Vector3.Up * (trace.IsBody ? trace.BodyBounds.Maxs.z + 4f : 4f),
				$"{i + 1}", size: 1f, color: color, overlay: DebugIgnoreDepth );
		}
	}
}
