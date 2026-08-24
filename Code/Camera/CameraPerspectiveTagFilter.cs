using Sandbox;

/// <summary>
/// Modifie les tags exclus par la caméra selon que le joueur
/// utilise la vue à la première ou à la troisième personne.
/// </summary>
public sealed class CameraPerspectiveTagFilter : Component, PlayerController.IEvents
{
	/// <summary>
	/// PlayerController dont le mode de caméra doit être surveillé.
	/// </summary>
	[Property]
	public PlayerController Player { get; set; }

	/// <summary>
	/// Tags que la caméra ne doit pas afficher en première personne.
	/// </summary>
	[Property, Group( "First Person" )]
	public TagSet ExcludedInFirstPerson { get; set; } = new();

	/// <summary>
	/// Tags que la caméra ne doit pas afficher en troisième personne.
	/// </summary>
	[Property, Group( "Third Person" )]
	public TagSet ExcludedInThirdPerson { get; set; }
		= new TagSet( new[] { "firstperson" } );

	/// <summary>
	/// Masque uniquement le corps du joueur local en première personne tout en
	/// conservant ses ombres. Le masquage cible le SceneObject local et n'entre
	/// donc jamais dans le snapshot réseau du joueur.
	/// </summary>
	[Property, Group( "First Person" )]
	public bool HideLocalBodyInFirstPerson { get; set; } = true;

	private CameraComponent _lastCamera;
	private bool? _lastThirdPerson;
	private bool _localBodyVisibilityApplied;
	private VBHealthComponent _health;

	/// <summary>
	/// Conserve le corps local dans la passe d'ombres en première personne.
	/// Tous les renderers rattachés au corps sont concernés afin d'inclure
	/// les vêtements générés par le Dresser.
	/// </summary>
	void PlayerController.IEvents.PostCameraSetup( CameraComponent camera )
	{
		RefreshLocalBodyVisibility();
	}

	protected override void OnPreRender()
	{
		// Le Dresser et les armes peuvent créer des renderers après le spawn.
		// Appliquer ceci avant chaque rendu garantit qu'ils héritent eux aussi
		// de la visibilité strictement locale du corps.
		RefreshLocalBodyVisibility();
	}

	protected override void OnDisabled()
	{
		RestoreBodyVisibilityIfNeeded();
	}

	protected override void OnUpdate()
	{
		if ( !Player.IsValid() || Player.IsProxy )
			return;

		var camera = Scene.Camera;

		if ( !camera.IsValid() )
			return;

		var isThirdPerson = Player.ThirdPerson;

		// Ne modifie les tags qu'au démarrage, lors d'un changement
		// de caméra ou lors du switch FPP / TPP.
		if ( _lastCamera == camera
			&& _lastThirdPerson == isThirdPerson )
		{
			return;
		}

		ApplyExclusions( camera, isThirdPerson );

		_lastCamera = camera;
		_lastThirdPerson = isThirdPerson;
	}

	private void RefreshLocalBodyVisibility()
	{
		if ( !Player.IsValid() )
			return;

		if ( Player.IsProxy )
			return;

		_health ??= Player.Components.Get<VBHealthComponent>();
		var showBody = Player.ThirdPerson
			|| (_health.IsValid() && (_health.IsDowned || _health.IsDead));

		ApplyLocalBodyVisibility( showBody );
		_localBodyVisibilityApplied = true;
	}

	private void RestoreBodyVisibilityIfNeeded()
	{
		if ( !_localBodyVisibilityApplied )
			return;

		ApplyLocalBodyVisibility( isThirdPerson: true );
		_localBodyVisibilityApplied = false;
	}

	private void ApplyExclusions(
		CameraComponent camera,
		bool isThirdPerson
	)
	{
		// Tags configurés pour la première personne.
		foreach ( var tag in ExcludedInFirstPerson )
		{
			if ( string.IsNullOrWhiteSpace( tag ) )
				continue;

			// Si le même tag est présent dans les deux listes,
			// il reste exclu dans les deux modes.
			var shouldExclude =
				!isThirdPerson
				|| ExcludedInThirdPerson.Has( tag );

			camera.RenderExcludeTags.Set(
				tag,
				shouldExclude
			);
		}

		// Tags configurés uniquement pour la troisième personne.
		foreach ( var tag in ExcludedInThirdPerson )
		{
			if ( string.IsNullOrWhiteSpace( tag ) )
				continue;

			if ( ExcludedInFirstPerson.Has( tag ) )
				continue;

			camera.RenderExcludeTags.Set(
				tag,
				isThirdPerson
			);
		}
	}

	private void ApplyLocalBodyVisibility( bool isThirdPerson )
	{
		if ( !Player.Renderer.IsValid() )
			return;

		var hideBody = !isThirdPerson && HideLocalBodyInFirstPerson;
		var bodyObject = Player.Renderer.GameObject;

		foreach ( var renderer in bodyObject.GetComponentsInChildren<ModelRenderer>(
			includeDisabled: true,
			includeSelf: true
		) )
		{
			if ( !renderer.IsValid() )
				continue;

			// RenderType est une propriété sérialisée du composant. La mettre sur
			// ShadowsOnly chez l'hôte avant l'arrivée d'un client rendait cette
			// valeur visible dans son snapshot initial. Elle reste donc toujours
			// sur sa valeur normale et seul l'objet de rendu local est filtré.
			if ( renderer.RenderType != ModelRenderer.ShadowRenderType.On )
				renderer.RenderType = ModelRenderer.ShadowRenderType.On;

			var sceneObject = renderer.SceneObject;
			if ( !sceneObject.IsValid() )
				continue;

			sceneObject.Flags.ExcludeGameLayer = hideBody;
		}
	}
}
