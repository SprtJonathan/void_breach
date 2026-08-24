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
- la verification de visibilite entre la camera et l'item.

Par defaut, le rond est place exactement a l'origine de l'item (`PromptLocalOffset = 0,0,0`).
Le bloc texte est decale independamment de 24 pixels vers le haut avec
`PromptCardScreenOffset = 0,-24` : son apparition ne deplace donc jamais le rond et ne change
pas la zone que le joueur vise pour ramasser l'objet.

Pour modifier le placement dans un prefab, creer un GameObject enfant vide a l'endroit voulu et
l'assigner directement dans `PromptAnchor`. Cet enfant ne porte aucun composant de prompt : il
sert uniquement d'ancre. Le texte, la touche, les distances et l'affichage restent configures sur
`InventoryWorldRepresentation`, ce qui evite deux sources concurrentes.

`PromptRequiresLineOfSight` est actif par defaut. Le HUD local lance un rayon depuis la camera
vers le centre des bounds visibles de l'item apres les tests de distance et de cadrage. Le rond
reste bien a la position configuree : ce point de test distinct evite simplement qu'une origine
posee dans le sol soit consideree comme occultee. La hierarchie du joueur et celle de l'item sont
ignorees : l'item ne masque pas son propre prompt, tandis qu'un mur ou un autre collider le fait
disparaitre. Desactiver cette option permet ponctuellement un marqueur visible a travers les
obstacles.

## Autres objets interactibles

Ajouter `World Interaction Prompt` au GameObject source ou directement sur un enfant vide place
au point d'accroche. Sans `Anchor`, le composant utilise toujours l'origine du GameObject qui le
porte. Si `Anchor` est renseigne, sa position prend volontairement priorite. `LocalOffset` applique
une correction locale et vaut zero par defaut. `ShowPlacementGizmo` affiche dans l'editeur le point
orange reellement utilise. `CardScreenOffset` deplace uniquement le bloc descriptif, jamais le rond.

Dans le groupe **Display**, `ShowMarker` controle uniquement le rond et `ShowPrompt` uniquement la
carte detaillee affichee a portee. `ShowTitle`, `ShowInputAction` et `ShowInteractionText` permettent
ensuite de choisir les lignes presentes dans cette carte.

Le texte peut etre un token commencant par `#`, resolu dans le dossier `Localization`. Pour un
texte dynamique, le composant qui porte l'etat de gameplay implemente
`IVBInteractionPromptProvider`. Le marqueur le trouve automatiquement sur son GameObject ou ses
parents, ou il peut etre assigne explicitement dans `StateProvider`.

Pour une porte native `Sandbox.Mapping.Door`, ajouter `Door Interaction Prompt Provider` sur le
GameObject de la porte. Il lit son `State` synchronise et retourne automatiquement `OUVRIR`,
`FERMER` ou `VERROUILLE`. Un prompt place sur un enfant-poignee trouve ce provider parent sans
configuration supplementaire.

Le principe reste identique pour une porte personnalisee :

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
