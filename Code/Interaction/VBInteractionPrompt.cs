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
/// Configuration finale d'un prompt apres application de son etat dynamique.
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
public sealed class VBInteractionPrompt : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Affiche la carte detaillee a portee d'interaction. Cette option ne
	/// controle pas le rond, qui reste independant via ShowMarker.
	/// </summary>
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

	[Property, Group( "Placement" )]
	public bool ShowPlacementGizmo { get; set; } = true;

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
	/// Masque le prompt lorsqu'un collider se trouve entre la camera locale
	/// et son point d'affichage.
	/// </summary>
	[Property, Group( "Visibility" )]
	public bool RequireLineOfSight { get; set; } = true;

	/// <summary>
	/// Hierarchie ignoree par le test d'occlusion. Laisser vide pour utiliser
	/// le GameObject du provider dynamique, ou celui du prompt sans provider.
	/// </summary>
	[Property, Group( "Visibility" )]
	public GameObject OcclusionRoot { get; set; }

	/// <summary>
	/// Composant facultatif implementant IVBInteractionPromptProvider.
	/// Laisser vide pour rechercher automatiquement un provider sur ce
	/// GameObject puis sur ses parents.
	/// </summary>
	[Property, Group( "Dynamic State" )]
	public Component StateProvider { get; set; }

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
	/// Resout une seule representation a afficher.
	/// </summary>
	public VBResolvedInteractionPrompt Resolve( GameObject viewer )
	{
		var state = GetState( viewer );

		return new VBResolvedInteractionPrompt(
			state.IsAvailable,
			state.TextToken,
			state.InputAction,
			state.TitleText,
			GetPromptWorldPosition(),
			CardScreenOffset,
			AppearanceDistance,
			InteractionDistance,
			ShowMarker,
			ShowPrompt && ShowInteractionText,
			ShowPrompt && ShowInputAction,
			ShowPrompt && ShowTitle
		);
	}

	public Vector3 GetPromptWorldPosition()
	{
		var anchor = Anchor.IsValid() ? Anchor : GameObject;
		return anchor.WorldPosition + anchor.WorldRotation * LocalOffset;
	}

	public GameObject GetOcclusionRoot()
	{
		if ( OcclusionRoot.IsValid() )
			return OcclusionRoot;

		if ( ResolveProvider() is Component provider && provider.IsValid() )
			return provider.GameObject;

		return GameObject;
	}

	/// <summary>
	/// Retourne un point situe dans le volume visible de l'objet. Le rond reste
	/// sur sa position configuree, mais le rayon d'occlusion ne termine ainsi
	/// pas dans le sol lorsque l'origine de l'item est au niveau du plancher.
	/// </summary>
	public Vector3 GetOcclusionTestPosition( Vector3 promptPosition )
	{
		var occlusionRoot = GetOcclusionRoot();
		if ( occlusionRoot.IsValid() )
		{
			var bounds = occlusionRoot.GetBounds();
			if ( bounds.Size.LengthSquared > 0.001f )
				return bounds.Center;
		}

		return promptPosition + Vector3.Up * 4f;
	}

	protected override void DrawGizmos()
	{
		if ( !ShowPlacementGizmo )
			return;

		var position = GetPromptWorldPosition();
		Gizmo.Draw.Color = new Color( 0.95f, 0.64f, 0.23f, 0.95f );
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineSphere( position, 4f );

		if ( Anchor.IsValid() && Anchor != GameObject )
			Gizmo.Draw.Line( WorldPosition, position );
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

		for ( var current = GameObject; current.IsValid(); current = current.Parent )
		{
			foreach ( var component in current.Components.GetAll() )
			{
				if ( component is IVBInteractionPromptProvider provider )
				{
					_cachedProvider = provider;
					return;
				}
			}
		}
	}
}
