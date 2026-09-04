using System;
using System.Collections.Generic;
using Sandbox;

public enum VBNpcRagdollState
{
	Animated,
	Recoverable,
	Dead
}

/// <summary>
/// Reusable in-place ragdoll presentation for networked biped NPCs. The host
/// decides when and where recovery happens; every peer presents the physics.
/// </summary>
[Title( "NPC Ragdoll Controller" )]
[Category( "Void Breach/AI" )]
[Icon( "personal_injury" )]
public sealed class VBNpcRagdollController : Component
{
	[RequireComponent] private NavMeshAgent Agent { get; set; }
	[RequireComponent] private VBHealthComponent Health { get; set; }

	[Property, Group( "Knockdown" ), Range( 0f, 10000f )] public float KnockdownImpulseThreshold { get; set; } = 900f;
	[Property, Group( "Knockdown" ), Range( 0f, 100f )] public float DamageImpulsePerPoint { get; set; } = 25f;
	[Property, Group( "Knockdown" ), Range( 0f, 10f )] public float MinimumRagdollTime { get; set; } = 1.25f;
	[Property, Group( "Knockdown" ), Range( 0.05f, 3f )] public float RequiredSettledTime { get; set; } = 0.45f;
	[Property, Group( "Knockdown" ), Range( 1f, 500f )] public float MaximumSettledSpeed { get; set; } = 28f;
	[Property, Group( "Knockdown" ), Range( 16f, 500f )] public float GroundProbeDistance { get; set; } = 110f;
	[Property, Group( "Knockdown" )] public float StandHeightOffset { get; set; } = 2f;
	[Property, Group( "Collision" )]
	[Description( "Tag appliqué au mesh physique. La matrice de collision l'ignore face au tag player." )]
	public string RagdollCollisionTag { get; set; } = "npc_ragdoll";

	[Sync( SyncFlags.FromHost )]
	public VBNpcRagdollState State { get; private set; } = VBNpcRagdollState.Animated;

	public bool IsRagdolled => State != VBNpcRagdollState.Animated;
	public bool CanRecover => State == VBNpcRagdollState.Recoverable;
	public ModelPhysics ModelPhysics => _modelPhysics;

	public Action OnRecovered;

	private readonly Dictionary<Collider, bool> _colliderDefaults = new();
	private SkinnedModelRenderer _renderer;
	private ModelPhysics _modelPhysics;
	private Transform _rendererLocalTransform;
	private bool _rendererUseAnimGraph;
	private bool _agentUpdatePosition;
	private bool _agentEnabled;
	private bool _defaultsCaptured;
	private VBNpcRagdollState _presentedState = (VBNpcRagdollState)(-1);
	private TimeSince _timeSinceRagdoll;
	private float _settledTime;
	private Vector3 _lastMassCenter;
	private string _appliedCollisionTag;

	protected override void OnStart()
	{
		ResolveReferences();
		CaptureDefaults();

		if ( Networking.IsHost && Health.IsValid() )
		{
			Health.OnDamaged += OnDamaged;
			Health.OnDeath += OnDied;
		}
	}

	protected override void OnValidate()
	{
		ResolveReferences();
		ApplyRagdollCollisionTag();
	}

	protected override void OnUpdate()
	{
		ResolveReferences();
		CaptureDefaults();

		if ( _presentedState != State )
			ApplyPresentation( State );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || State != VBNpcRagdollState.Recoverable || !_modelPhysics.IsValid() )
			return;

		UpdateRecovery();
	}

	protected override void OnDestroy()
	{
		if ( Health.IsValid() )
		{
			Health.OnDamaged -= OnDamaged;
			Health.OnDeath -= OnDied;
		}
	}

	public void BeginRecoverableRagdoll( Vector3 impulse )
	{
		if ( !Networking.IsHost || State == VBNpcRagdollState.Dead )
			return;

		BeginRagdoll( VBNpcRagdollState.Recoverable, impulse );
	}

	public void BeginDeathRagdoll( Vector3 impulse )
	{
		if ( !Networking.IsHost )
			return;

		BeginRagdoll( VBNpcRagdollState.Dead, impulse );
	}

	private void BeginRagdoll( VBNpcRagdollState state, Vector3 impulse )
	{
		ResolveReferences();
		CaptureDefaults();
		State = state;
		_timeSinceRagdoll = 0f;
		_settledTime = 0f;
		PresentRagdoll( state, impulse );
	}

	private void UpdateRecovery()
	{
		var center = GetMassCenter();
		float speed = Vector3.DistanceBetween( center, _lastMassCenter ) / MathF.Max( Scene.FixedDelta, 0.0001f );
		_lastMassCenter = center;

		var groundTrace = TraceGround( center );
		bool settled = _timeSinceRagdoll >= MinimumRagdollTime
			&& groundTrace.Hit
			&& speed <= MaximumSettledSpeed;

		_settledTime = settled
			? _settledTime + Scene.FixedDelta
			: 0f;

		if ( _settledTime >= RequiredSettledTime )
			Recover( groundTrace.EndPosition + Vector3.Up * StandHeightOffset );
	}

	private void Recover( Vector3 standPosition )
	{
		WorldPosition = standPosition;
		GameObject.Network.ClearInterpolation();
		State = VBNpcRagdollState.Animated;
		ApplyPresentation( State );

		if ( Agent.IsValid() )
			Agent.SetAgentPosition( standPosition );

		OnRecovered?.Invoke();
	}

	private void ApplyPresentation( VBNpcRagdollState state )
	{
		if ( !_defaultsCaptured )
			return;

		bool ragdolled = state != VBNpcRagdollState.Animated;
		_presentedState = state;

		if ( ragdolled )
		{
			if ( _modelPhysics.IsValid() && _renderer.IsValid() )
			{
				_modelPhysics.MotionEnabled = false;
				_modelPhysics.Renderer = _renderer;
				_modelPhysics.Model = _renderer.Model;
				_modelPhysics.IgnoreRoot = true;
				_modelPhysics.CopyBonesFrom( _renderer, teleport: true );
				_renderer.UseAnimGraph = false;
				_modelPhysics.MotionEnabled = true;
				ApplyRagdollCollisionTagToPhysics();
			}

			foreach ( var pair in _colliderDefaults )
			{
				if ( pair.Key.IsValid() )
					pair.Key.Enabled = false;
			}

			if ( Agent.IsValid() )
			{
				if ( !Agent.IsTraversingLink )
					Agent.Stop();
				Agent.UpdatePosition = false;

				if ( state == VBNpcRagdollState.Dead )
					Agent.Enabled = false;
			}

			return;
		}

		if ( _modelPhysics.IsValid() )
			_modelPhysics.MotionEnabled = false;

		if ( _renderer.IsValid() )
		{
			_renderer.GameObject.LocalTransform = _rendererLocalTransform;
			_renderer.UseAnimGraph = _rendererUseAnimGraph;
		}

		foreach ( var pair in _colliderDefaults )
		{
			if ( pair.Key.IsValid() )
				pair.Key.Enabled = pair.Value;
		}

		if ( Agent.IsValid() )
		{
			Agent.Enabled = _agentEnabled;
			Agent.UpdatePosition = _agentUpdatePosition;
		}
	}

	private void ResolveReferences()
	{
		_renderer ??= Components.Get<SkinnedModelRenderer>( FindMode.InSelf | FindMode.InChildren );
		_modelPhysics ??= Components.Get<ModelPhysics>( FindMode.InSelf | FindMode.InChildren );
		ApplyRagdollCollisionTag();
	}

	private void ApplyRagdollCollisionTag()
	{
		if ( !_renderer.IsValid() )
			return;

		if ( !string.IsNullOrWhiteSpace( _appliedCollisionTag )
			&& !string.Equals( _appliedCollisionTag, RagdollCollisionTag, StringComparison.OrdinalIgnoreCase ) )
		{
			_renderer.GameObject.Tags.Remove( _appliedCollisionTag );
		}

		_appliedCollisionTag = RagdollCollisionTag?.Trim();
		if ( !string.IsNullOrWhiteSpace( _appliedCollisionTag ) )
			_renderer.GameObject.Tags.Add( _appliedCollisionTag );
	}

	private void ApplyRagdollCollisionTagToPhysics()
	{
		if ( !_modelPhysics.IsValid() || string.IsNullOrWhiteSpace( _appliedCollisionTag ) )
			return;

		foreach ( var body in _modelPhysics.Bodies )
		{
			if ( !body.Component.IsValid() || body.Component.PhysicsBody is null )
				continue;

			foreach ( var shape in body.Component.PhysicsBody.Shapes )
				shape.Tags.Add( _appliedCollisionTag );
		}
	}

	private void CaptureDefaults()
	{
		if ( _defaultsCaptured || !_renderer.IsValid() )
			return;

		_rendererLocalTransform = _renderer.GameObject.LocalTransform;
		_rendererUseAnimGraph = _renderer.UseAnimGraph;
		_agentUpdatePosition = !Agent.IsValid() || Agent.UpdatePosition;
		_agentEnabled = !Agent.IsValid() || Agent.Enabled;

		foreach ( var collider in Components.GetAll<Collider>( FindMode.InSelf | FindMode.InChildren ) )
			_colliderDefaults[collider] = collider.Enabled;

		_defaultsCaptured = true;
	}

	private Vector3 GetMassCenter()
	{
		return _modelPhysics.IsValid() && _modelPhysics.PhysicsWereCreated
			? _modelPhysics.MassCenter
			: _renderer.WorldPosition;
	}

	private SceneTraceResult TraceGround( Vector3 center )
	{
		return Scene.Trace
			.Ray( center + Vector3.Up * 16f, center + Vector3.Down * GroundProbeDistance )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
	}

	private void OnDamaged( VBDamageInfo info )
	{
		var impulse = GetDamageImpulse( info );
		if ( !Networking.IsHost )
			return;

		if ( Health.IsDead )
		{
			if ( State == VBNpcRagdollState.Dead )
				ApplyImpulse( impulse );
			return;
		}

		if ( impulse.Length >= KnockdownImpulseThreshold )
			BeginRecoverableRagdoll( impulse );
	}

	private void OnDied()
	{
		// VBHealthComponent publishes OnDeath before OnDamaged for the killing
		// hit. Start the death ragdoll now; OnDamaged applies that hit's impulse.
		BeginDeathRagdoll( Vector3.Zero );
	}

	private Vector3 GetDamageImpulse( VBDamageInfo info )
	{
		if ( !info.Force.IsNearZeroLength )
			return info.Force;

		var direction = info.Attacker.IsValid()
			? (WorldPosition - info.Attacker.WorldPosition).Normal
			: Vector3.Zero;

		return direction * info.Amount * DamageImpulsePerPoint;
	}

	private void ApplyImpulse( Vector3 impulse )
	{
		if ( impulse.IsNearZeroLength || !_modelPhysics.IsValid() )
			return;

		int bodyCount = Math.Max( 1, _modelPhysics.Bodies.Count );
		foreach ( var body in _modelPhysics.Bodies )
		{
			if ( body.Component.IsValid() )
				body.Component.ApplyImpulse( impulse / bodyCount );
		}
	}

	[Rpc.Broadcast]
	private void PresentRagdoll( VBNpcRagdollState state, Vector3 impulse )
	{
		ResolveReferences();
		CaptureDefaults();
		ApplyPresentation( state );
		_lastMassCenter = GetMassCenter();
		ApplyImpulse( impulse );
	}
}
