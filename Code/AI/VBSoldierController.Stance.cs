using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[Property, Group( "Cover" )] public bool UseCrouchCover { get; set; } = true;
	[Property, Group( "Cover" ), Range( 36f, 60f )] public float CrouchHeight { get; set; } = 44f;
	[Sync( SyncFlags.FromHost )] public bool IsCrouching { get; private set; }
	private Vector3 _standingEyeOffset;
	private bool _stanceInitialized;
	private readonly Dictionary<CapsuleCollider, (Vector3 Start, Vector3 End)> _stanceCapsules = new();
	private float CrouchedHeight => Math.Clamp( CrouchHeight, Agent.Radius * 2f + 1f, Agent.Height - 1f );

	private void UpdateStance()
	{
		if ( !_stanceInitialized )
		{
			_stanceInitialized = true;
			_standingEyeOffset = Perception.EyeOffset;
			foreach ( var capsule in GetComponentsInChildren<CapsuleCollider>() )
				if ( capsule.GameObject.Tags.Has( "npc_body" ) ) _stanceCapsules[capsule] = (capsule.Start, capsule.End);
		}
		float height = IsCrouching ? CrouchedHeight : Agent.Height;
		if ( Locomotion.Body.Height != height ) Locomotion.Body.Height = height;
		var eye = _standingEyeOffset.WithZ( IsCrouching ? CrouchedHeight - 9f : _standingEyeOffset.z );
		if ( Perception.EyeOffset != eye ) Perception.EyeOffset = eye;
		foreach ( var pair in _stanceCapsules )
		{
			if ( !pair.Key.IsValid() ) continue;
			var end = IsCrouching ? pair.Value.End.WithZ( height - pair.Key.Radius ) : pair.Value.End;
			if ( pair.Key.Start != pair.Value.Start ) pair.Key.Start = pair.Value.Start;
			if ( pair.Key.End != end ) pair.Key.End = end;
		}
		if ( _animation.IsValid() ) _animation.DuckLevel = MathX.Lerp( _animation.DuckLevel, IsCrouching ? 1f : 0f, Math.Clamp( Time.Delta * 6f, 0f, 1f ) );
	}

	private bool SetCrouching( bool crouch, bool preserveFireSlot = false )
	{
		if ( !Networking.IsHost ) return false;
		if ( IsCrouching && !crouch && CanFight )
		{
			// Test the space ABOVE the crouched body; never stand through a ceiling or prop.
			var headroom = new BBox( new Vector3( -Agent.Radius, -Agent.Radius, CrouchedHeight ),
				new Vector3( Agent.Radius, Agent.Radius, Agent.Height ) );
			var trace = Scene.Trace.Box( headroom, WorldPosition, WorldPosition )
				.IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).Run();
			if ( trace.Hit || trace.StartedSolid ) return false;
		}
		if ( crouch && !IsCrouching && !preserveFireSlot ) ReleaseFireSlot();
		IsCrouching = crouch;
		UpdateStance();
		return true;
	}
}
