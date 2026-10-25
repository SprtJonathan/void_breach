using System;
using Sandbox;

/// <summary>
/// Reusable host-authoritative sight and target-memory component. Behaviour
/// controllers decide what to do with the perceived target.
/// </summary>
[Title( "AI Perception" )]
[Category( "Void Breach/AI" )]
[Icon( "visibility" )]
public sealed partial class VBAiPerceptionComponent : Component, IVBAiSoundListener
{
	[RequireComponent] private VBHealthComponent Health { get; set; }
	[RequireComponent] private VBFactionComponent Faction { get; set; }

	[Property, Group( "Detection" ), Range( 64f, 5000f )] public float SightRange { get; set; } = 1200f;
	[Property, Group( "Detection" ), Range( 10f, 360f )] public float FieldOfView { get; set; } = 220f;
	[Property, Group( "Detection" ), Range( 0.05f, 2f )] public float ThinkInterval { get; set; } = 0.2f;
	[Property, Group( "Detection" )] public Vector3 EyeOffset { get; set; } = new( 0f, 0f, 60f );
	[Property, Group( "Detection" )] public Vector3 TargetOffset { get; set; } = new( 0f, 0f, 40f );
	[Property, Group( "Detection" )] public bool CanTargetDowned { get; set; } = false;
	[Property, Group( "Detection" ), Range( 0f, 2f )] public float AcquisitionDelay { get; set; }

	[Property, Group( "Filtering" )]
	[Description( "Un acteur portant l'un de ces tags est toujours ignoré, même si sa faction est hostile." )]
	public List<string> IgnoredTags { get; set; } = new() { "ai_ignore", "notarget" };

	[Property, Group( "Hearing" ), Range( 0f, 3f )] public float HearingSensitivity { get; set; } = 1f;
	[Property, Group( "Hearing" ), Range( 0f, 1f )]
	[Description( "Part de la portée sonore conservée lorsque le bruit est masqué par le décor." )]
	public float OccludedHearingScale { get; set; } = 0.55f;
	[Property, Group( "Hearing" )] public bool InvestigateUnidentifiedSounds { get; set; } = true;

	[Sync( SyncFlags.FromHost )] public GameObject CurrentTarget { get; private set; }
	[Sync( SyncFlags.FromHost )] public Vector3 LastKnownPosition { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool TargetVisible { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool HasInvestigationPoint { get; private set; }
	[Sync( SyncFlags.FromHost )] public GameObject LastSeenTarget { get; private set; }
	[Sync( SyncFlags.FromHost )] public Vector3 LastSeenPosition { get; private set; }
	[Sync( SyncFlags.FromHost )] public Vector3 LastSeenVelocity { get; private set; }
	[Sync( SyncFlags.FromHost )] public float LastSeenAt { get; private set; } = -10000f;

	public float TimeSinceTargetSensed => _timeSinceTargetSensed;

	private TimeSince _timeSinceThink;
	private TimeSince _timeSinceTargetSensed;
	private GameObject _pendingTarget;
	private float _pendingTargetSince;
	private Vector3 _pendingTargetPosition;
	private float _pendingSeenAt = -10000f;
	public bool HasVisualSuspicion => _pendingTarget.IsValid() && IsViableTarget( _pendingTarget )
		&& Time.Now - _pendingSeenAt <= MathF.Max( 0.1f, ThinkInterval * 1.5f );
	public Vector3 VisualSuspicionPosition => _pendingTargetPosition;

	protected override void OnStart()
	{
		if ( Health.IsValid() )
			Health.OnDamaged += OnDamaged;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || Health.IsDead || _timeSinceThink < MathF.Max( 0.05f, ThinkInterval ) )
			return;

		_timeSinceThink = 0f;
		UpdatePerception();
	}

	protected override void OnDestroy()
	{
		if ( Health.IsValid() )
			Health.OnDamaged -= OnDamaged;
	}

	public bool IsViableTarget( GameObject candidate )
	{
		candidate = GetActorRoot( candidate );
		if ( !candidate.IsValid() || candidate == GameObject )
			return false;

		if ( IgnoredTags.Count > 0 && candidate.Tags.HasAny( IgnoredTags.ToArray() ) )
			return false;

		var candidateHealth = candidate.Components.Get<VBHealthComponent>();
		if ( !candidateHealth.IsValid() || candidateHealth.IsDead )
			return false;

		if ( !CanTargetDowned && candidateHealth.IsDowned )
			return false;

		var candidateFaction = candidate.Components.Get<VBFactionComponent>();
		return Faction.IsValid()
			&& Faction.IsConfigured
			&& candidateFaction.IsValid()
			&& candidateFaction.IsConfigured
			&& Faction.IsHostileTo( candidateFaction );
	}

	public bool CanSee( GameObject candidate )
	{
		candidate = GetActorRoot( candidate );
		if ( !IsViableTarget( candidate ) )
			return false;

		var delta = candidate.WorldPosition - WorldPosition;
		if ( delta.LengthSquared > SightRange * SightRange )
			return false;

		var flatDirection = delta.WithZ( 0f ).Normal;
		var soldier = GetComponent<VBSoldierController>();
		float view = soldier.IsValid() && soldier.RemembersTarget( candidate ) ? MathF.Max( FieldOfView, soldier.EngagedFieldOfView ) : FieldOfView;
		if ( soldier.IsValid() && delta.Length <= MathF.Max( 0f, soldier.CloseAwarenessRange ) )
			view = MathF.Max( view, soldier.CloseAwarenessFieldOfView );
		bool contact = GetComponent<VBAiThreatComponent>()?.IsImmediateAttacker( candidate ) ?? false;
		if ( !contact && !flatDirection.IsNearZeroLength && view < 359f )
		{
			float minimumDot = MathF.Cos( view * 0.5f * MathF.PI / 180f );
			if ( Vector3.Dot( WorldRotation.Forward.WithZ( 0f ).Normal, flatDirection ) < minimumDot )
				return false;
		}

		if ( VisibleSample( candidate, candidate.WorldPosition + TargetOffset ) ) return true;
		// Use the actor's real head height, including crouching, rather than empty
		// air above them when only their head is exposed behind low cover.
		var player = candidate.GetComponent<PlayerController>();
		var eyes = candidate.GetComponent<VBAiPerceptionComponent>();
		var head = player.IsValid() ? player.EyePosition - Vector3.Up * 4f
			: eyes.IsValid() ? candidate.WorldPosition + eyes.EyeOffset - Vector3.Up * 4f
			: candidate.WorldPosition + TargetOffset;
		return Vector3.DistanceBetween( head, candidate.WorldPosition + TargetOffset ) > 4f && VisibleSample( candidate, head );
	}

	private bool VisibleSample( GameObject candidate, Vector3 point )
	{
		var trace = Scene.Trace.Ray( WorldPosition + EyeOffset, point )
			.IgnoreGameObjectHierarchy( GameObject ).UseHitboxes( true ).Run();
		if ( trace.StartedSolid ) return false;
		if ( !trace.Hit || GetActorRoot( trace.GameObject ) == candidate ) return true;
		// Seats parent the player under the vehicle. A visible occupied chassis
		// identifies its occupant, while a wall in front of it still blocks sight.
		var seat = candidate.GetComponent<VBVehiclePlayerDriver>()?.ActiveSeat;
		return seat.IsValid() && trace.GameObject.IsValid()
			&& (trace.GameObject == seat.Vehicle.GameObject || trace.GameObject.IsDescendant( seat.Vehicle.GameObject ));
	}

	/// <summary>Visual sensing of objects such as grenades/vehicles, with the same eyes and FOV.</summary>
	public bool CanSeePoint( Vector3 point, GameObject subject )
	{
		var delta = point - (WorldPosition + EyeOffset);
		if ( delta.LengthSquared > SightRange * SightRange ) return false;
		var flat = delta.WithZ( 0f ).Normal;
		if ( FieldOfView < 359f && !flat.IsNearZeroLength
			&& Vector3.Dot( WorldRotation.Forward.WithZ( 0f ).Normal, flat ) < MathF.Cos( FieldOfView * MathF.PI / 360f ) ) return false;
		var trace = Scene.Trace.Ray( WorldPosition + EyeOffset, point ).IgnoreGameObjectHierarchy( GameObject ).Run();
		return !trace.Hit || subject.IsValid() && trace.GameObject.IsValid()
			&& (trace.GameObject == subject || trace.GameObject.IsDescendant( subject ));
	}

	public void ForgetTarget()
	{
		if ( !Networking.IsHost )
			return;

		CurrentTarget = null;
		TargetVisible = false;
		ReportedTarget = null;
	}

	public void ConsumeInvestigationPoint()
	{
		if ( Networking.IsHost )
			HasInvestigationPoint = false;
	}

	void IVBAiSoundListener.OnSoundStimulus( VBAiSoundStimulus stimulus )
	{
		if ( !Networking.IsHost || Health.IsDead || HearingSensitivity <= 0f )
			return;

		var source = GetActorRoot( stimulus.Source );
		if ( source == GameObject )
			return;

		if ( source.IsValid() && IgnoredTags.Count > 0 && source.Tags.HasAny( IgnoredTags.ToArray() ) )
			return;

		float effectiveRadius = stimulus.Radius * HearingSensitivity;
		if ( effectiveRadius <= 0f )
			return;

		var trace = Scene.Trace
			.Ray( WorldPosition + EyeOffset, stimulus.Position )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( trace.Hit && (!source.IsValid() || GetActorRoot( trace.GameObject ) != source) )
			effectiveRadius *= OccludedHearingScale;

		if ( Vector3.DistanceBetween( WorldPosition, stimulus.Position ) > effectiveRadius )
			return;

		if ( TargetVisible )
			return;

		if ( source.IsValid() && IsViableTarget( source ) )
		{
			bool newClue = CurrentTarget != source || Vector3.DistanceBetween( LastKnownPosition, stimulus.Position ) > 96f;
			CurrentTarget = source;
			TargetVisible = CanSee( source );
			if ( TargetVisible ) { RememberTarget( source ); return; }
			LastKnownPosition = TargetVisible ? source.WorldPosition : stimulus.Position;
			HasInvestigationPoint = !TargetVisible;
			_timeSinceTargetSensed = 0f;
			if ( newClue ) MarkInvestigation();
			return;
		}

		if ( !InvestigateUnidentifiedSounds )
			return;
		// An allied shot must not erase an enemy contact or attract every soldier
		// to its own rifleman. Enemy observations are shared separately.
		if ( IsViableTarget( CurrentTarget ) ) return;
		var sourceFaction = source.IsValid() ? source.GetComponent<VBFactionComponent>() : null;
		if ( sourceFaction.IsValid() && sourceFaction.FactionId == Faction.FactionId
			&& stimulus.Kind is not (VBAiSoundKind.Gunshot or VBAiSoundKind.Explosion) ) return;

		bool newNoise = !HasInvestigationPoint || Vector3.DistanceBetween( LastKnownPosition, stimulus.Position ) > 96f;
		CurrentTarget = null;
		TargetVisible = false;
		LastKnownPosition = stimulus.Position;
		HasInvestigationPoint = true;
		_timeSinceTargetSensed = 0f;
		if ( newNoise ) MarkInvestigation();
	}

	private void UpdatePerception()
	{
		var threats = Components.Get<VBAiThreatComponent>();
		if ( !threats.IsValid() && TargetVisible && IsViableTarget( CurrentTarget ) && CanSee( CurrentTarget ) )
		{
			RememberTarget( CurrentTarget );
			return;
		}

		var nearest = threats.IsValid() ? threats.SelectVisibleTarget() : FindNearestVisibleTarget();
		if ( nearest.IsValid() )
		{
			bool remembered = nearest == CurrentTarget && (GetComponent<VBSoldierController>()?.RemembersTarget( nearest ) ?? false);
			if ( AcquisitionDelay > 0f && !remembered && !(threats.IsValid() && threats.IsImmediateAttacker( nearest )) && (nearest != CurrentTarget || !TargetVisible) )
			{
				if ( _pendingTarget != nearest ) { _pendingTarget = nearest; _pendingTargetSince = Time.Now; }
				_pendingTargetPosition = nearest.WorldPosition;
				_pendingSeenAt = Time.Now;
				if ( Time.Now - _pendingTargetSince < AcquisitionDelay )
				{
					TargetVisible = TargetVisible && IsViableTarget( CurrentTarget ) && CanSee( CurrentTarget );
					return;
				}
			}
			_pendingTarget = null;
			CurrentTarget = nearest;
			RememberTarget( nearest );
			return;
		}

		_pendingTarget = null;
		TargetVisible = false;
		if ( !IsViableTarget( CurrentTarget ) )
			CurrentTarget = null;
	}

	private GameObject FindNearestVisibleTarget()
	{
		GameObject nearest = null;
		float nearestDistanceSquared = SightRange * SightRange;

		foreach ( var candidateHealth in Scene.GetAll<VBHealthComponent>() )
		{
			var candidate = candidateHealth.GameObject;
			if ( !IsViableTarget( candidate ) )
				continue;

			float distanceSquared = (candidate.WorldPosition - WorldPosition).LengthSquared;
			if ( distanceSquared >= nearestDistanceSquared || !CanSee( candidate ) )
				continue;

			nearest = candidate;
			nearestDistanceSquared = distanceSquared;
		}

		return nearest;
	}

	private void RememberTarget( GameObject target )
	{
		CurrentTarget = GetActorRoot( target );
		float elapsed = Time.Now - LastSeenAt;
		LastSeenVelocity = LastSeenTarget == CurrentTarget && elapsed >= 0.05f && elapsed < 1f
			? ((CurrentTarget.WorldPosition - LastSeenPosition) / elapsed).WithZ( 0f ).ClampLength( 320f ) : Vector3.Zero;
		LastKnownPosition = CurrentTarget.WorldPosition;
		LastSeenTarget = CurrentTarget;
		LastSeenPosition = LastKnownPosition;
		LastSeenAt = Time.Now;
		TargetVisible = true;
		HasInvestigationPoint = false;
		_timeSinceTargetSensed = 0f;
	}

	private void OnDamaged( VBDamageInfo info )
	{
		if ( !Networking.IsHost )
			return;

		var attacker = GetActorRoot( info.Attacker );
		if ( !IsViableTarget( attacker ) )
			return;

		CurrentTarget = attacker;
		TargetVisible = CanSee( attacker );
		if ( TargetVisible ) { RememberTarget( attacker ); return; }
		// A hit identifies an attacker, but does not reveal their hidden transform.
		// Preserve an existing observation rather than replacing it with our own impact position.
		LastKnownPosition = LastSeenTarget == attacker ? LastSeenPosition : info.Position;
		HasInvestigationPoint = !TargetVisible;
		_timeSinceTargetSensed = 0f;
		MarkInvestigation();
	}

	private static GameObject GetActorRoot( GameObject actor )
	{
		if ( !actor.IsValid() ) return null;
		// Actor identity is the health/faction entity, not a containing chair,
		// vehicle, scene folder or inventory parent.
		var health = actor.GetComponentInParent<VBHealthComponent>( includeSelf: true );
		return health.IsValid() ? health.GameObject : actor;
	}
}
