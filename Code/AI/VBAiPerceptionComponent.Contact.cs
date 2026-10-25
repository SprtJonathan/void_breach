using Sandbox;

public sealed partial class VBAiPerceptionComponent
{
	[Sync( SyncFlags.FromHost )] public GameObject ReportedTarget { get; private set; }
	[Sync( SyncFlags.FromHost )] public float ReportedAt { get; private set; } = -10000f;
	// Host-local revisions let behaviour react once to a new clue, rather than
	// restarting its orientation every frame or on every repeated radio report.
	public int InvestigationRevision { get; private set; }
	public float LastInvestigationAt { get; private set; } = -10000f;
	private void MarkInvestigation()
	{
		InvestigationRevision++;
		LastInvestigationAt = Time.Now;
	}

	/// <summary>A teammate's observation is a clue, never our own visual contact.</summary>
	public bool ReceiveContactReport( GameObject target, Vector3 observedPosition, float observedAt )
	{
		if ( !Networking.IsHost || !Active || Health.IsDead || Health.IsDowned || !IsViableTarget( target )
			|| TargetVisible || Time.Now - observedAt > 6f || observedAt > Time.Now + 0.1f
			|| LastSeenTarget == target && LastSeenAt >= observedAt
			|| ReportedTarget == target && ReportedAt >= observedAt ) return false;
		// A local contact remains more useful than a report about another player.
		if ( IsViableTarget( CurrentTarget ) && CurrentTarget != target
			&& (Time.Now - LastSeenAt < 3f || TimeSinceTargetSensed < 2f) ) return false;
		bool changed = CurrentTarget != target || Vector3.DistanceBetween( LastKnownPosition, observedPosition ) > 96f;
		ReportedTarget = target; ReportedAt = observedAt;
		CurrentTarget = target; LastKnownPosition = observedPosition;
		HasInvestigationPoint = true;
		_timeSinceTargetSensed = 0f;
		if ( changed ) MarkInvestigation();
		return true;
	}
}
