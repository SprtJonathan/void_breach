# HUD d'interaction dans le monde

Le HUD d'interaction est rendu uniquement par le `VBMasterHud` du joueur local. Les objets
interactibles ne synchronisent donc aucun panneau, aucune position d'ecran et aucun etat
visuel. Le marqueur suit une position du monde projetee dans le HUD, ce qui le maintient
lisible et toujours face a la camera en FPS comme en TPS.

## Objets d'inventaire

`InventoryWorldRepresentation` cree automatiquement un marqueur de ramassage local lorsque
l'objet n'appartient a aucun inventaire. Le groupe **Interaction Prompt** permet de regler :

- l'affichage du marqueur ;
- le texte localise, par defaut `#vb.interaction.pick_up` ;
- l'action d'input, par defaut `use` ;
- la position locale du marqueur, relative a l'origine de l'item ;
- la distance d'apparition et la distance d'interaction ;
- l'affichage du rond, du nom de l'objet, de la touche et du texte.

Par defaut, le rond est place exactement a l'origine de l'item (`PromptLocalOffset = 0,0,0`).
Le bloc texte est decale independamment de 24 pixels vers le haut avec
`PromptCardScreenOffset = 0,-24` : son apparition ne deplace donc jamais le rond et ne change
pas la zone que le joueur vise pour ramasser l'objet.

Pour surcharger proprement le placement dans un prefab, creer un GameObject enfant a l'endroit
voulu et lui ajouter `World Interaction Prompt Override`. Par defaut, son seul groupe actif est
`OverridePosition` : il herite donc automatiquement du texte, de la touche, du nom de l'item,
des options d'affichage, des distances et du decalage de carte definis sur l'item. L'origine de
ce GameObject enfant devient uniquement la position du rond.

Chaque autre groupe peut etre remplace independamment :

- `OverrideCardPlacement` pour le decalage en pixels du bloc texte ;
- `OverrideContent` pour le texte, la touche et le titre ;
- `OverrideDisplay` pour choisir les parties visibles ;
- `OverrideDistances` pour les deux distances.

Cela permet aussi l'inverse : desactiver `OverridePosition` et activer seulement les groupes de
contenu ou d'affichage. `PromptOverride` sur `InventoryWorldRepresentation` permet de choisir
explicitement l'override si le prefab en contient plusieurs ; sinon le premier enfant est trouve
automatiquement. Un ancien composant `World Interaction Prompt` complet place sur un enfant est
encore accepte comme ancre unique pour compatibilite, sans creer un second marqueur.

## Autres objets interactibles

Ajouter `World Interaction Prompt` au GameObject source. `Anchor` peut designer un enfant servant
de point d'accroche precis. Sans ancre, `LocalOffset` part de l'origine de l'objet et vaut zero par
defaut. `CardScreenOffset` deplace uniquement le bloc descriptif, jamais le rond. Le meme composant
`World Interaction Prompt Override` peut etre ajoute sous une porte ou tout autre interactible :
il surcharge les groupes coches et herite du reste, y compris du texte dynamique du provider.

Le texte peut etre un token commencant par `#`, resolu dans le dossier `Localization`. Pour un
texte dynamique, le composant qui porte l'etat de gameplay implemente
`IVBInteractionPromptProvider`. Le marqueur le trouve automatiquement lorsqu'il est place sur
le meme GameObject, ou il peut etre assigne dans `StateProvider`.

Exemple pour une porte dont `IsOpen` est deja synchronise par son composant de gameplay :

```csharp
public sealed class Door : Component, IVBInteractionPromptProvider
{
	[Sync] public bool IsOpen { get; set; }

	public VBInteractionPromptState GetInteractionPromptState( GameObject viewer )
	{
		return new VBInteractionPromptState(
			IsAvailable: true,
			TextToken: IsOpen
				? "#vb.interaction.close"
				: "#vb.interaction.open",
			InputAction: "use"
		);
	}
}
```

Le provider ne declenche jamais l'interaction : il ne fait que decrire son etat visuel. La
logique de porte, de ramassage ou d'autorite reseau reste donc independante du HUD.
