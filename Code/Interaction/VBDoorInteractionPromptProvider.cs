using Sandbox;
using Sandbox.Mapping;

/// <summary>
/// Traduit l'etat synchronise d'une porte native s&amp;box en contenu de HUD.
/// Ce composant ne declenche pas la porte : il decrit uniquement l'action
/// que le joueur effectuerait en appuyant sur la touche configuree.
/// </summary>
[Title( "Door Interaction Prompt Provider" )]
[Category( "Void Breach/Interaction" )]
[Icon( "door_front" )]
public sealed class VBDoorInteractionPromptProvider : Component, IVBInteractionPromptProvider
{
	[Property]
	public Door Door { get; set; }

	[Property, Group( "Content" ), Placeholder( "#vb.interaction.open" )]
	public string OpenText { get; set; } = "#vb.interaction.open";

	[Property, Group( "Content" ), Placeholder( "#vb.interaction.close" )]
	public string CloseText { get; set; } = "#vb.interaction.close";

	[Property, Group( "Content" ), Placeholder( "#vb.interaction.locked" )]
	public string LockedText { get; set; } = "#vb.interaction.locked";

	[Property, Group( "Content" ), InputAction]
	public string InputAction { get; set; } = "use";

	protected override void OnAwake()
	{
		base.OnAwake();
		Door ??= GetComponent<Door>();
	}

	public VBInteractionPromptState GetInteractionPromptState( GameObject viewer )
	{
		Door ??= GetComponent<Door>();
		if ( !Door.IsValid() || !Door.Enabled || !Door.IsUsable )
			return new VBInteractionPromptState( false );

		var text = Door.IsLocked
			? LockedText
			: Door.State is Door.DoorState.Open or Door.DoorState.Opening
				? CloseText
				: OpenText;

		return new VBInteractionPromptState(
			IsAvailable: true,
			TextToken: text,
			InputAction: InputAction
		);
	}
}
