using Sandbox;

/// <summary>
/// Modifie les tags exclus par la caméra selon que le joueur
/// utilise la vue à la première ou à la troisième personne.
/// </summary>
public sealed class CameraPerspectiveTagFilter : Component
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

	private CameraComponent _lastCamera;
	private bool? _lastThirdPerson;

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
}