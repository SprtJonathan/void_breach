using Sandbox;

/// <summary>
/// Affiche les composants physiques et visuels d'un objet uniquement
/// lorsqu'il se trouve dans le monde, hors d'un inventaire.
/// </summary>
[Title( "Inventory World Representation" )]
[Category( "Void Breach/Inventory" )]
public sealed class InventoryWorldRepresentation : Component, IVBInteractionPromptProvider
{
	private bool? _presentedInWorld;
	/// <summary>
	/// Objet d'inventaire surveillé.
	/// Il est trouvé automatiquement sur ce GameObject si non renseigné.
	/// </summary>
	[Property]
	public BaseInventoryItem Item { get; set; }

	/// <summary>
	/// Composant Prop utilisé lorsque l'objet est au sol.
	/// Peut rester vide si aucun Prop n'est utilisé.
	/// </summary>
	[Property]
	public Prop WorldProp { get; set; }

	/// <summary>
	/// Modèle visible lorsque l'objet est au sol.
	/// </summary>
	[Property]
	public ModelRenderer WorldRenderer { get; set; }

	/// <summary>
	/// Collider utilisé pour la physique et l'interaction au sol.
	/// </summary>
	[Property]
	public Collider WorldCollider { get; set; }

	/// <summary>
	/// Rigidbody utilisé lorsque l'objet est au sol.
	/// </summary>
	[Property]
	public Rigidbody WorldRigidbody { get; set; }

	/// <summary>
	/// Ajoute automatiquement le repere local de ramassage lorsque l'objet est au sol.
	/// </summary>
	[Property, Group( "Interaction Prompt" )]
	public bool ShowInteractionPrompt { get; set; } = true;

	[Property, Group( "Interaction Prompt" ), Placeholder( "#vb.interaction.pick_up" )]
	public string PickupText { get; set; } = "#vb.interaction.pick_up";

	[Property, Group( "Interaction Prompt" ), InputAction]
	public string PickupInputAction { get; set; } = "use";

	/// <summary>
	/// Point d'accroche facultatif. Sans reference, l'origine de l'objet est utilisee.
	/// </summary>
	[Property, Group( "Interaction Prompt" )]
	public GameObject PromptAnchor { get; set; }

	[Property, Group( "Interaction Prompt" )]
	public Vector3 PromptLocalOffset { get; set; } = Vector3.Zero;

	[Property, Group( "Interaction Prompt" )]
	public Vector2 PromptCardScreenOffset { get; set; } = new( 0f, -24f );

	[Property, Group( "Interaction Prompt" ), Range( 1f, 4096f ), Step( 1f )]
	public float PromptAppearanceDistance { get; set; } = 240f;

	[Property, Group( "Interaction Prompt" ), Range( 1f, 1024f ), Step( 1f )]
	public float PromptInteractionDistance { get; set; } = 48f;

	[Property, Group( "Interaction Prompt" )]
	public bool PromptShowsInputAction { get; set; } = true;

	[Property, Group( "Interaction Prompt" )]
	public bool PromptShowsText { get; set; } = true;

	[Property, Group( "Interaction Prompt" )]
	public bool PromptShowsMarker { get; set; } = true;

	[Property, Group( "Interaction Prompt" )]
	public bool PromptShowsItemName { get; set; } = true;

	/// <summary>
	/// Masque le repere si un mur ou un autre objet physique se trouve entre
	/// la camera locale et l'item.
	/// </summary>
	[Property, Group( "Interaction Prompt" )]
	public bool PromptRequiresLineOfSight { get; set; } = true;

	protected override void OnAwake()
	{
		base.OnAwake();

		Item ??= GetComponent<BaseInventoryItem>();
		WorldProp ??= GetComponent<Prop>();
		WorldRenderer ??= GetComponent<ModelRenderer>();
		WorldCollider ??= GetComponent<Collider>();
		WorldRigidbody ??= GetComponent<Rigidbody>();

		CreateInteractionPrompt();

		UpdateWorldState();
	}

	VBInteractionPromptState IVBInteractionPromptProvider.GetInteractionPromptState( GameObject viewer )
	{
		var inventory = viewer.IsValid() ? viewer.GetComponentInParent<VBWeaponInventoryComponent>( includeSelf: true ) : null;
		bool swap = Item is VBCombatWeapon gun && inventory.IsValid() && inventory.CanSwapWeapon( gun );
		return new VBInteractionPromptState(
			ShowInteractionPrompt && Item.IsValid() && Item.Inventory is null,
			swap ? "#vb.interaction.swap_weapon" : PickupText,
			PickupInputAction,
			Item.IsValid() ? Item.DisplayName : null
		);
	}

	private void CreateInteractionPrompt()
	{
		if ( Application.IsDedicatedServer )
			return;

		// Compatibilite avec un ancien prompt complet place sur un enfant : il
		// devient l'unique source et herite du contenu dynamique de l'item.
		var childPrompt = GetComponentInChildren<VBInteractionPrompt>(
			includeDisabled: true,
			includeSelf: false
		);
		var rootPrompt = GetComponent<VBInteractionPrompt>();

		VBInteractionPrompt prompt;
		if ( childPrompt.IsValid() )
		{
			prompt = childPrompt;
			if ( rootPrompt.IsValid() && rootPrompt != prompt )
				rootPrompt.Enabled = false;
		}
		else
		{
			prompt = rootPrompt.IsValid()
				? rootPrompt
				: GameObject.AddComponent<VBInteractionPrompt>();
			prompt.Enabled = true;
			prompt.Anchor = PromptAnchor;
			prompt.LocalOffset = PromptLocalOffset;
		}

		prompt.InteractionText = PickupText;
		prompt.InputAction = PickupInputAction;
		prompt.AppearanceDistance = PromptAppearanceDistance;
		prompt.InteractionDistance = PromptInteractionDistance;
		prompt.ShowInputAction = PromptShowsInputAction;
		prompt.ShowInteractionText = PromptShowsText;
		prompt.ShowMarker = PromptShowsMarker;
		prompt.ShowTitle = PromptShowsItemName;
		prompt.CardScreenOffset = PromptCardScreenOffset;
		prompt.RequireLineOfSight = PromptRequiresLineOfSight;
		prompt.OcclusionRoot = GameObject;
		prompt.SetProviderIfEmpty( this );
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();

		UpdateWorldState( force: true );
	}
	protected override void OnStart() => UpdateWorldState( force: true );

	protected override void OnUpdate()
	{
		UpdateWorldState();
	}

	private void UpdateWorldState( bool force = false )
	{
		if ( !Item.IsValid() )
			return;

		// Inventory == null : l'objet est posé dans le monde.
		// Inventory != null : l'objet appartient à un joueur/inventaire.
		var isInWorld = Item.Inventory is null;
		if ( !force && _presentedInWorld == isInWorld ) return;
		_presentedInWorld = isInWorld;

		SetComponentEnabled( WorldProp, isInWorld );
		SetComponentEnabled( WorldRenderer, isInWorld );
		SetComponentEnabled( WorldCollider, isInWorld );
		SetComponentEnabled( WorldRigidbody, isInWorld );
		// Prop can create its renderer/collider/body after this component's Awake,
		// including additional components when a prefab already serialized them.
		// Reconcile every root world component, without touching held/view models
		// that the native weapon creates on child GameObjects.
		foreach ( var renderer in GetComponents<ModelRenderer>( includeDisabled: true ) ) SetComponentEnabled( renderer, isInWorld );
		foreach ( var collider in GetComponents<Collider>( includeDisabled: true ) ) SetComponentEnabled( collider, isInWorld );
		foreach ( var body in GetComponents<Rigidbody>( includeDisabled: true ) ) SetComponentEnabled( body, isInWorld );

	}

	private static void SetComponentEnabled(
		Component component,
		bool enabled
	)
	{
		if ( component.IsValid() )
		{
			if ( component.Enabled != enabled ) component.Enabled = enabled;
		}
	}
}
