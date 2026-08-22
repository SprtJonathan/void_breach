using System;
using Sandbox;

/// <summary>
/// Override facultatif d'un World Interaction Prompt parent.
/// Par defaut, seule la position est remplacee : contenu, affichage,
/// distances et decalage de carte restent herites de la source.
/// </summary>
[Title( "World Interaction Prompt Override" )]
[Category( "Void Breach/Interaction" )]
[Icon( "control_point_duplicate" )]
public sealed class VBInteractionPromptOverride : Component
{
	[Property, Group( "Position" )]
	public bool OverridePosition { get; set; } = true;

	[Property, Group( "Position" )]
	public GameObject Anchor { get; set; }

	[Property, Group( "Position" )]
	public Vector3 LocalOffset { get; set; } = Vector3.Zero;

	[Property, Group( "Card Placement" )]
	public bool OverrideCardPlacement { get; set; }

	[Property, Group( "Card Placement" )]
	public Vector2 CardScreenOffset { get; set; } = new( 0f, -24f );

	[Property, Group( "Content" )]
	public bool OverrideContent { get; set; }

	[Property, Group( "Content" ), Placeholder( "#vb.interaction.use" )]
	public string InteractionText { get; set; } = "#vb.interaction.use";

	[Property, Group( "Content" ), InputAction]
	public string InputAction { get; set; } = "use";

	[Property, Group( "Content" ), Placeholder( "Optional title or #localization.token" )]
	public string TitleText { get; set; }

	[Property, Group( "Display" )]
	public bool OverrideDisplay { get; set; }

	[Property, Group( "Display" )]
	public bool ShowMarker { get; set; } = true;

	[Property, Group( "Display" )]
	public bool ShowInteractionText { get; set; } = true;

	[Property, Group( "Display" )]
	public bool ShowInputAction { get; set; } = true;

	[Property, Group( "Display" )]
	public bool ShowTitle { get; set; } = true;

	[Property, Group( "Distance" )]
	public bool OverrideDistances { get; set; }

	[Property, Group( "Distance" ), Range( 1f, 4096f ), Step( 1f )]
	public float AppearanceDistance { get; set; } = 240f;

	[Property, Group( "Distance" ), Range( 1f, 1024f ), Step( 1f )]
	public float InteractionDistance { get; set; } = 48f;

	protected override void OnValidate()
	{
		AppearanceDistance = MathF.Max( 1f, AppearanceDistance );
		InteractionDistance = Math.Clamp( InteractionDistance, 1f, AppearanceDistance );
	}

	public Vector3 GetPromptWorldPosition()
	{
		var anchor = Anchor.IsValid() ? Anchor : GameObject;
		return anchor.WorldPosition + anchor.WorldRotation * LocalOffset;
	}
}
