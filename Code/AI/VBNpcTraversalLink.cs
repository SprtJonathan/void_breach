using Sandbox;

public enum VBNpcLinkMode { Vault, Climb }

/// <summary>Authored traversal on the engine's NavMeshLink; endpoints must connect baked walkable surfaces.</summary>
[Title( "NPC Traversal Link" ), Category( "Void Breach/AI" ), Icon( "hiking" )]
public sealed class VBNpcTraversalLink : Component
{
	[RequireComponent] public NavMeshLink Link { get; set; }
	[Property] public VBNpcLinkMode Mode { get; set; } = VBNpcLinkMode.Climb;
	[Property, Range( 20f, 200f )] public float Speed { get; set; } = 70f;
	[Property, Range( 0f, 160f )] public float ArcHeight { get; set; } = 48f;
	/// <summary>Optional feet positions between native endpoints, ordered from LocalStart to LocalEnd. Reversed automatically on descent.</summary>
	[Property] public GameObject[] Waypoints { get; set; }
	protected override void OnAwake()
	{
		if ( Link.Area is null && Mode == VBNpcLinkMode.Climb )
			Link.Area = ResourceLibrary.Get<Sandbox.Engine.Resources.NavMeshAreaDefinition>( "navigation/npc_climb.navarea" );
	}
}
