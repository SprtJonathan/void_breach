using System;
using Sandbox;

/// <summary>Optional threat ranking. Only visible, faction-valid actors enter selection.</summary>
[Title( "AI Threat Assessment" ), Category( "Void Breach/AI" ), Icon( "priority_high" )]
public sealed class VBAiThreatComponent : Component
{
	[RequireComponent] private VBHealthComponent Health { get; set; }
	[RequireComponent] private VBAiPerceptionComponent Perception { get; set; }
	[Property] public float CloseThreatRange { get; set; } = 180f;
	[Property] public float CloseThreatBonus { get; set; } = 5f;
	[Property] public float PlayerBonus { get; set; } = 1f;
	[Property] public float RecentAttackerBonus { get; set; } = 4f;
	[Property] public float AttackerMemory { get; set; } = 8f;
	[Property] public float CurrentTargetBonus { get; set; } = 0.8f;
	private readonly Dictionary<GameObject, float> _attackers = new();

	// Physical contact reveals a nearby aggressor even behind the eyes. The
	// perception component still checks occlusion before treating it as located.
	public bool IsImmediateAttacker( GameObject actor ) => actor.IsValid()
		&& _attackers.TryGetValue( actor, out var hit ) && Time.Now - hit < MathF.Min( AttackerMemory, 2f )
		&& Vector3.DistanceBetween( WorldPosition, actor.WorldPosition ) < CloseThreatRange;

	protected override void OnStart() => Health.OnDamaged += OnDamaged;
	protected override void OnDestroy() { if ( Health.IsValid() ) Health.OnDamaged -= OnDamaged; }

	public GameObject SelectVisibleTarget()
	{
		GameObject best = null;
		float bestScore = float.NegativeInfinity;
		foreach ( var stale in _attackers.Where( x => !x.Key.IsValid() || Time.Now - x.Value > AttackerMemory ).Select( x => x.Key ).ToArray() )
			_attackers.Remove( stale );
		foreach ( var health in Scene.GetAll<VBHealthComponent>() )
		{
			var candidate = health.GameObject;
			if ( !Perception.CanSee( candidate ) ) continue;
			float distance = Vector3.DistanceBetween( WorldPosition, candidate.WorldPosition );
			float score = 1f - distance / MathF.Max( 1f, Perception.SightRange );
			if ( distance < CloseThreatRange ) score += CloseThreatBonus;
			// Survival tiers outrank faction preferences and sticky target bonuses.
			if ( IsImmediateAttacker( candidate ) ) score += 100f;
			else if ( distance < CloseThreatRange * 0.65f ) score += 25f;
			if ( candidate.GetComponent<VBFactionComponent>()?.FactionId == VBFactionComponent.PlayerFaction ) score += PlayerBonus;
			if ( _attackers.TryGetValue( candidate, out float lastHit ) )
				score += RecentAttackerBonus * Math.Clamp( 1f - (Time.Now - lastHit) / MathF.Max( 0.1f, AttackerMemory ), 0f, 1f );
			if ( candidate == Perception.CurrentTarget ) score += CurrentTargetBonus;
			if ( score > bestScore ) { best = candidate; bestScore = score; }
		}
		return best;
	}

	private void OnDamaged( VBDamageInfo info )
	{
		if ( !Networking.IsHost ) return;
		var actor = info.Attacker.IsValid() ? info.Attacker.GetComponentInParent<VBHealthComponent>( includeSelf: true ) : null;
		if ( actor.IsValid() && Perception.IsViableTarget( actor.GameObject ) ) _attackers[actor.GameObject] = Time.Now;
	}
}
