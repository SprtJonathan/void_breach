using Sandbox;

/// <summary>
/// Affiche les composants physiques et visuels d'un objet uniquement
/// lorsqu'il se trouve dans le monde, hors d'un inventaire.
/// </summary>
[Title( "Inventory World Representation" )]
[Category( "Void Breach/Inventory" )]
public sealed class InventoryWorldRepresentation : Component
{
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

	private bool? _lastWorldState;

	protected override void OnAwake()
	{
		base.OnAwake();

		Item ??= GetComponent<BaseInventoryItem>();
		WorldProp ??= GetComponent<Prop>();
		WorldRenderer ??= GetComponent<ModelRenderer>();
		WorldCollider ??= GetComponent<Collider>();
		WorldRigidbody ??= GetComponent<Rigidbody>();

		UpdateWorldState( force: true );
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();

		UpdateWorldState( force: true );
	}

	protected override void OnUpdate()
	{
		UpdateWorldState( force: false );
	}

	private void UpdateWorldState( bool force )
	{
		if ( !Item.IsValid() )
			return;

		// Inventory == null : l'objet est posé dans le monde.
		// Inventory != null : l'objet appartient à un joueur/inventaire.
		var isInWorld = Item.Inventory is null;

		if ( !force && _lastWorldState == isInWorld )
			return;

		SetComponentEnabled( WorldProp, isInWorld );
		SetComponentEnabled( WorldRenderer, isInWorld );
		SetComponentEnabled( WorldCollider, isInWorld );
		SetComponentEnabled( WorldRigidbody, isInWorld );

		_lastWorldState = isInWorld;
	}

	private static void SetComponentEnabled(
		Component component,
		bool enabled
	)
	{
		if ( component.IsValid() )
		{
			component.Enabled = enabled;
		}
	}
}