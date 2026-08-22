using System;
using Sandbox;

/// <summary>
/// Etat dynamique facultatif fourni par un objet interactible.
/// Les valeurs nulles conservent la configuration du marqueur.
/// </summary>
public readonly record struct VBInteractionPromptState(
	bool IsAvailable,
	string TextToken = null,
	string InputAction = null,
	string TitleText = null
);

/// <summary>
/// Configuration finale d'un prompt apres application de son etat dynamique
/// et, le cas echeant, de son override enfant.
/// </summary>
public readonly record struct VBResolvedInteractionPrompt(
	bool IsAvailable,
	string TextToken,
	string InputAction,
	string TitleText,
	Vector3 WorldPosition,
	Vector2 CardScreenOffset,
	float AppearanceDistance,
	float InteractionDistance,
	bool ShowMarker,
	bool ShowInteractionText,
	bool ShowInputAction,
	bool ShowTitle
);

/// <summary>
/// A implementer sur un composant dont le texte d'interaction change a l'execution.
/// Une porte peut par exemple retourner #vb.interaction.close lorsqu'elle est ouverte
/// et #vb.interaction.open lorsqu'elle est fermee.
/// </summary>
public interface IVBInteractionPromptProvider
{
	VBInteractionPromptState GetInteractionPromptState( GameObject viewer );
}

/// <summary>
/// Decrit un repere d'interaction attache a un objet du monde.
/// Le rendu est effectue uniquement par le HUD du joueur local.
/// </summary>
[Title( "World Interaction Prompt" )]
[Category( "Void Breach/Interaction" )]
[Icon( "ads_click" )]
public sealed class VBInteractionPrompt : Component
{
	[Property, Group( "Display" )]
	public bool ShowPrompt { get; set; } = true;

	[Property, Group( "Display" )]
	public bool ShowMarker { get; set; } = true;

	[Property, Group( "Display" )]
	public bool ShowInteractionText { get; set; } = true;

	[Property, Group( "Display" )]
	public bool ShowInputAction { get; set; } = true;

	[Property, Group( "Display" )]
	public bool ShowTitle { get; set; } = true;

	[Property, Group( "Content" ), Placeholder( "#vb.interaction.use" )]
	public string InteractionText { get; set; } = "#vb.interaction.use";

	[Property, Group( "Content" ), InputAction]
	public string InputAction { get; set; } = "use";

	[Property, Group( "Content" ), Placeholder( "Optional title or #localization.token" )]
	public string TitleText { get; set; }

	[Property, Group( "Placement" )]
	public GameObject Anchor { get; set; }

	[Property, Group( "Placement" )]
	public Vector3 LocalOffset { get; set; } = Vector3.Zero;

	/// <summary>
	/// Decalage du bloc texte en pixels d'interface par rapport au centre du rond.
	/// Le rond reste toujours exactement sur la position 3D du prompt.
	/// </summary>
	[Property, Group( "Placement" )]
	public Vector2 CardScreenOffset { get; set; } = new( 0f, -24f );

	[Property, Group( "Distance" ), Range( 1f, 4096f ), Step( 1f )]
	public float AppearanceDistance { get; set; } = 240f;

	[Property, Group( "Distance" ), Range( 1f, 1024f ), Step( 1f )]
	public float InteractionDistance { get; set; } = 48f;

	/// <summary>
	/// Composant facultatif implementant IVBInteractionPromptProvider.
	/// Laisser vide pour rechercher automatiquement un provider sur ce GameObject.
	/// </summary>
	[Property, Group( "Dynamic State" )]
	public Component StateProvider { get; set; }

	/// <summary>
	/// Override visuel facultatif. S'il n'est pas assigne, le premier override
	/// enfant est utilise automatiquement.
	/// </summary>
	[Property, Group( "Override" )]
	public VBInteractionPromptOverride PromptOverride { get; set; }

	private IVBInteractionPromptProvider _cachedProvider;

	protected override void OnAwake()
	{
		base.OnAwake();
		CacheProvider();
	}

	protected override void OnValidate()
	{
		AppearanceDistance = MathF.Max( 1f, AppearanceDistance );
		InteractionDistance = Math.Clamp( InteractionDistance, 1f, AppearanceDistance );
		CacheProvider();
	}

	/// <summary>
	/// Associe un provider cree ou trouve a l'execution sans remplacer une reference explicite.
	/// </summary>
	public void SetProviderIfEmpty( Component provider )
	{
		if ( StateProvider.IsValid() )
			return;

		StateProvider = provider;
		_cachedProvider = provider as IVBInteractionPromptProvider;
	}

	public VBInteractionPromptState GetState( GameObject viewer )
	{
		if ( !ShowPrompt )
			return new VBInteractionPromptState( false );

		var state = ResolveProvider()?.GetInteractionPromptState( viewer )
			?? new VBInteractionPromptState( true );

		if ( !state.IsAvailable )
			return state;

		return state with
		{
			TextToken = string.IsNullOrWhiteSpace( state.TextToken )
				? InteractionText
				: state.TextToken,
			InputAction = string.IsNullOrWhiteSpace( state.InputAction )
				? InputAction
				: state.InputAction,
			TitleText = string.IsNullOrWhiteSpace( state.TitleText )
				? TitleText
				: state.TitleText
		};
	}

	/// <summary>
	/// Resout une seule representation a afficher. L'override n'est jamais une
	/// seconde source de HUD : seuls les groupes coches remplacent la source.
	/// </summary>
	public VBResolvedInteractionPrompt Resolve( GameObject viewer )
	{
		var state = GetState( viewer );
		var worldPosition = GetPromptWorldPosition();
		var cardScreenOffset = CardScreenOffset;
		var appearanceDistance = AppearanceDistance;
		var interactionDistance = InteractionDistance;
		var showMarker = ShowMarker;
		var showInteractionText = ShowInteractionText;
		var showInputAction = ShowInputAction;
		var showTitle = ShowTitle;
		var textToken = state.TextToken;
		var inputAction = state.InputAction;
		var titleText = state.TitleText;

		var promptOverride = ResolveOverride();
		if ( promptOverride.IsValid() && promptOverride.Enabled )
		{
			if ( promptOverride.OverridePosition )
				worldPosition = promptOverride.GetPromptWorldPosition();

			if ( promptOverride.OverrideCardPlacement )
				cardScreenOffset = promptOverride.CardScreenOffset;

			if ( promptOverride.OverrideContent )
			{
				textToken = promptOverride.InteractionText;
				inputAction = promptOverride.InputAction;
				titleText = promptOverride.TitleText;
			}

			if ( promptOverride.OverrideDisplay )
			{
				showMarker = promptOverride.ShowMarker;
				showInteractionText = promptOverride.ShowInteractionText;
				showInputAction = promptOverride.ShowInputAction;
				showTitle = promptOverride.ShowTitle;
			}

			if ( promptOverride.OverrideDistances )
			{
				appearanceDistance = MathF.Max( 1f, promptOverride.AppearanceDistance );
				interactionDistance = Math.Clamp(
					promptOverride.InteractionDistance,
					1f,
					appearanceDistance
				);
			}
		}

		return new VBResolvedInteractionPrompt(
			state.IsAvailable,
			textToken,
			inputAction,
			titleText,
			worldPosition,
			cardScreenOffset,
			appearanceDistance,
			interactionDistance,
			showMarker,
			showInteractionText,
			showInputAction,
			showTitle
		);
	}

	public Vector3 GetPromptWorldPosition()
	{
		var anchor = Anchor.IsValid() ? Anchor : GameObject;
		return anchor.WorldPosition + anchor.WorldRotation * LocalOffset;
	}

	private VBInteractionPromptOverride ResolveOverride()
	{
		if ( PromptOverride.IsValid() )
			return PromptOverride;

		return GetComponentInChildren<VBInteractionPromptOverride>(
			includeDisabled: true,
			includeSelf: false
		);
	}

	private IVBInteractionPromptProvider ResolveProvider()
	{
		if ( StateProvider.IsValid() )
			return StateProvider as IVBInteractionPromptProvider;

		if ( _cachedProvider is Component cachedComponent && cachedComponent.IsValid() )
			return _cachedProvider;

		CacheProvider();
		return _cachedProvider;
	}

	private void CacheProvider()
	{
		_cachedProvider = StateProvider as IVBInteractionPromptProvider;
		if ( _cachedProvider is not null )
			return;

		foreach ( var component in Components.GetAll() )
		{
			if ( component is IVBInteractionPromptProvider provider )
			{
				_cachedProvider = provider;
				return;
			}
		}
	}
}
