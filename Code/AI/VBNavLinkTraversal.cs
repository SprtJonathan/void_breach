using Sandbox;

/// <summary>Adapter only: links use the soldier's single swept, replicated traversal executor.</summary>
[Title( "NPC Nav Link Traversal" ), Category( "Void Breach/AI" ), Icon( "conversion_path" )]
public sealed class VBNavLinkTraversal : Component
{
	[RequireComponent] private NavMeshAgent Agent { get; set; }
	public bool IsClimbing => GetComponent<VBSoldierController>()?.IsDynamicallyTraversing ?? false;
	public string LastFailure { get; private set; }
	protected override void OnEnabled()
	{
		Agent.AutoTraverseLinks = false;
		Agent.LinkEnter += OnLinkEnter;
	}
	protected override void OnDisabled() => Agent.LinkEnter -= OnLinkEnter;
	private void OnLinkEnter()
	{
		if ( !Networking.IsHost || !Agent.CurrentLinkTraversal.HasValue ) return;
		var soldier = GetComponent<VBSoldierController>();
		if ( soldier.IsValid() && soldier.TryTraverseLink( Agent.CurrentLinkTraversal.Value ) ) { LastFailure = null; return; }
		LastFailure = "No physically supported traversal for this actor";
		Agent.Stop();
	}
}
