using System;
using Sandbox;

/// <summary>
/// Reusable host-authoritative sight and target-memory component. Behaviour
/// controllers decide what to do with the perceived target.
/// </summary>
[Title( "AI Perception" )]
[Category( "Void Breach/AI" )]
[Icon( "visibility" )]
public sealed class VBAiPerceptionComponent : Component, IVBAiSoundListener
{
	[RequireComponent] private VBHealthComponent Health { get; set; }
	[RequireComponent] private VBFactionComponent Faction { get; set; }

	[Property, Group( "Detection" ), Range( 64f, 5000f )] public float SightRange { get; set; } = 1200f;
	[Property, Group( "Detection" ), Range( 10f, 360f )] public float FieldOfView { get; set; } = 220f;
	[Property, Group( "Detection" ), Range( 0.05f, 2f )] public float ThinkInterval { get; set; } = 0.2f;
	[Property, Group( "Detection" )] public Vector3 EyeOffset { get; set; } = new( 0f, 0f, 60f );
	[Property, Group( "Detection" )] public Vector3 TargetOffset { get; set; } = new( 0f, 0f, 40f );
	[Property, Group( "Detection" )] public bool CanTargetDowned { get; set; } = false;

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

	public float TimeSinceTargetSensed => _timeSinceTargetSensed;

	private TimeSince _timeSinceThink;
	private TimeSince _timeSinceTargetSensed;

	protected override void OnStart()
	{
		if ( Networking.IsHost && Health.IsValid() )
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
		if ( !candidate.IsValid() || candidate == GameObject.Root )
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
		if ( !flatDirection.IsNearZeroLength && FieldOfView < 359f )
		{
			float minimumDot = MathF.Cos( FieldOfView * 0.5f * MathF.PI / 180f );
			if ( Vector3.Dot( WorldRotation.Forward.WithZ( 0f ).Normal, flatDirection ) < minimumDot )
				return false;
		}

		var trace = Scene.Trace
			.Ray( WorldPosition + EyeOffset, candidate.WorldPosition + TargetOffset )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.UseHitboxes( true )
			.Run();

		return !trace.Hit || trace.GameObject.Root == candidate.Root;
	}

	public void ForgetTarget()
	{
		if ( !Networking.IsHost )
			return;

		CurrentTarget = null;
		TargetVisible = false;
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
		if ( source == GameObject.Root )
			return;

		if ( source.IsValid() && IgnoredTags.Count > 0 && source.Tags.HasAny( IgnoredTags.ToArray() ) )
			return;

		float effectiveRadius = stimulus.Radius * HearingSensitivity;
		if ( effectiveRadius <= 0f )
			return;

		var trace = Scene.Trace
			.Ray( WorldPosition + EyeOffset, stimulus.Position )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		if ( trace.Hit && (!source.IsValid() || trace.GameObject.Root != source.Root) )
			effectiveRadius *= OccludedHearingScale;

		if ( Vector3.DistanceBetween( WorldPosition, stimulus.Position ) > effectiveRadius )
			return;

		if ( TargetVisible )
			return;

		if ( source.IsValid() && IsViableTarget( source ) )
		{
			CurrentTarget = source;
			TargetVisible = CanSee( source );
			LastKnownPosition = TargetVisible ? source.WorldPosition : stimulus.Position;
			HasInvestigationPoint = !TargetVisible;
			_timeSinceTargetSensed = 0f;
			return;
		}

		if ( !InvestigateUnidentifiedSounds )
			return;

		CurrentTarget = null;
		TargetVisible = false;
		LastKnownPosition = stimulus.Position;
		HasInvestigationPoint = true;
		_timeSinceTargetSensed = 0f;
	}

	private void UpdatePerception()
	{
		if ( IsViableTarget( CurrentTarget ) && CanSee( CurrentTarget ) )
		{
			RememberTarget( CurrentTarget );
			return;
		}

		var nearest = FindNearestVisibleTarget();
		if ( nearest.IsValid() )
		{
			CurrentTarget = nearest;
			RememberTarget( nearest );
			return;
		}

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
			var candidate = candidateHealth.GameObject.Root;
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
		LastKnownPosition = CurrentTarget.WorldPosition;
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
		LastKnownPosition = attacker.WorldPosition;
		TargetVisible = CanSee( attacker );
		HasInvestigationPoint = !TargetVisible;
		_timeSinceTargetSensed = 0f;
	}

	private static GameObject GetActorRoot( GameObject actor )
	{
		return actor.IsValid() ? actor.Root : null;
	}
}
