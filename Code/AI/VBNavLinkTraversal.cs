using System;
using System.Threading.Tasks;
using Sandbox;

/// <summary>
/// Custom NavMeshLink traversal. Small links use a controlled arc; sufficiently
/// large downward links hand movement to the NPC ragdoll until it settles.
/// </summary>
[Title( "NPC Nav Link Traversal" )]
[Category( "Void Breach/AI" )]
[Icon( "conversion_path" )]
public sealed class VBNavLinkTraversal : Component
{
	[RequireComponent] private NavMeshAgent Agent { get; set; }
	[RequireComponent] private VBNpcRagdollController Ragdoll { get; set; }

	[Property, Group( "Traversal" ), Range( 8f, 500f )] public float RagdollDropHeight { get; set; } = 96f;
	[Property, Group( "Traversal" ), Range( 0f, 500f )] public float ArcHeight { get; set; } = 28f;
	[Property, Group( "Traversal" ), Range( 0.05f, 5f )] public float MinimumArcDuration { get; set; } = 0.35f;
	[Property, Group( "Traversal" ), Range( 0f, 5000f )] public float DropImpulse { get; set; } = 120f;

	private bool _isTraversing;

	protected override void OnEnabled()
	{
		if ( !Agent.IsValid() )
			return;

		Agent.AutoTraverseLinks = false;
		Agent.LinkEnter += OnLinkEnter;
	}

	protected override void OnDisabled()
	{
		if ( Agent.IsValid() )
			Agent.LinkEnter -= OnLinkEnter;
	}

	private void OnLinkEnter()
	{
		if ( !Networking.IsHost || _isTraversing || !Agent.CurrentLinkTraversal.HasValue )
			return;

		var traversal = Agent.CurrentLinkTraversal.Value;
		float verticalDelta = traversal.LinkExitPosition.z - traversal.AgentInitialPosition.z;

		if ( verticalDelta <= -RagdollDropHeight && Ragdoll.IsValid() )
			_ = TraverseAsRagdollAsync( traversal );
		else
			_ = TraverseArcAsync( traversal );
	}

	private async Task TraverseAsRagdollAsync( NavMeshAgent.LinkTraversalData traversal )
	{
		_isTraversing = true;
		Agent.UpdatePosition = false;

		var horizontal = (traversal.LinkExitPosition - traversal.AgentInitialPosition).WithZ( 0f ).Normal;
		Ragdoll.BeginRecoverableRagdoll( horizontal * DropImpulse );

		while ( this.IsValid() && Ragdoll.IsValid() && Ragdoll.CanRecover )
		{
			Agent.SetAgentPosition( Ragdoll.ModelPhysics.IsValid()
				? Ragdoll.ModelPhysics.MassCenter
				: WorldPosition );
			await Task.Frame();
		}

		if ( !this.IsValid() || !Agent.IsValid() )
			return;

		if ( Ragdoll.State == VBNpcRagdollState.Dead )
		{
			_isTraversing = false;
			return;
		}

		Agent.SetAgentPosition( WorldPosition );
		Agent.UpdatePosition = true;
		Agent.CompleteLinkTraversal();
		_isTraversing = false;
	}

	private async Task TraverseArcAsync( NavMeshAgent.LinkTraversalData traversal )
	{
		_isTraversing = true;
		var start = traversal.AgentInitialPosition;
		var end = traversal.LinkExitPosition;
		float distance = Vector3.DistanceBetween( start, end );
		float duration = MathF.Max( MinimumArcDuration, distance / MathF.Max( 1f, Agent.MaxSpeed ) );
		float height = MathF.Max( ArcHeight, MathF.Abs( end.z - start.z ) * 0.25f );
		TimeSince elapsed = 0f;

		while ( this.IsValid() && elapsed < duration )
		{
			float fraction = MathX.Clamp( elapsed / duration, 0f, 1f );
			var position = Vector3.Lerp( start, end, fraction );
			position.z += 4f * height * fraction * (1f - fraction);
			Agent.SetAgentPosition( position );
			await Task.Frame();
		}

		if ( !this.IsValid() || !Agent.IsValid() )
			return;

		Agent.SetAgentPosition( end );
		Agent.CompleteLinkTraversal();
		_isTraversing = false;
	}
}
