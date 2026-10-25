# Grenade du joueur

Le joueur ne reçoit ni item grenade ni munition grenade au spawn. Une grenade au sol se ramasse automatiquement au contact et fournit deux unités, dans la limite de quatre. La touche **4** sélectionne ensuite l'emplacement lançable ; le **clic gauche** lance une grenade. Le HUD de munitions existant affiche la réserve. Deux pickups de test se trouvent à distance du spawn dans `scenes/soldier_test.scene`.

L'item est `weapons/grenade/frag_grenade.prefab`. Il réutilise `BaseCombatWeapon` pour le déploiement, le contrôle et la présentation, ainsi que `BaseInventoryComponent` et `ammo/frag_grenade.ammo` pour la réserve et le ramassage. Il occupe exclusivement `ThrowableSlot` et ne consomme pas la capacité réservée aux armes. Le ramassage d'un second item identique donne deux grenades dans la limite de quatre, via le comportement natif de fusion des armes. À réserve pleine, l'objet reste au sol.

`VBGrenadeWeapon` envoie uniquement la direction visée au host par un RPC réservé au propriétaire. L'hôte vérifie que l'item est actif, le joueur vivant et debout, hors franchissement, avec des munitions et hors cooldown. Il balaie une sphère entre les yeux et le point de départ pour refuser un lancer à travers un obstacle. Il consomme ensuite une munition et crée un projectile dont il reste propriétaire, même pour un lanceur distant. Les paramètres de dégâts et de mèche viennent du prefab serveur.

Le projectile commun au joueur et au soldat est `weapons/grenade/frag_grenade_projectile.prefab`. Il utilise la physique et `RadiusDamage` natifs, une mèche de 2,5 secondes et les effets d'explosion du moteur. Son GUID est conservé depuis son ancien emplacement dans le dossier du soldat. L'item `frag_grenade.prefab` représente la grenade à tenir ou ramasser ; le projectile représente cette même grenade une fois lancée, avec sa mèche active. Les explosions peuvent toucher le lanceur et ses alliés. Le modèle tenu est une représentation simple utilisant la sphère native ; aucun modèle ou mouvement de dégoupillage finalisé n'est fourni. Le lancer est immédiat, sans cuisson ni mode secondaire.

Le sélecteur reconnaît désormais les équipements dérivés de `BaseCombatWeapon`, afin de suivre l'emplacement actif lors d'un ramassage automatique. Cette correction couvre aussi le PDA.

## Corps du soldat

Le masque de bodygroups copié du joueur habillé masquait son torse et ses jambes. Le prefab utilise désormais le masque du Human complet ; le contrôleur sélectionne explicitement les choix visibles `Head`, `Chest`, `Legs`, `Hands` et `Feet` par l'API native au démarrage, sur chaque pair. Cela corrige aussi les anciennes instances dont le masque est surchargé. Le Human de test n'a pas encore d'uniforme Combine.

## Validation

La compilation .NET et la vérification des références d'assets sont effectuées contre les assemblies installées. `Tests/Weapons/VBGrenadeRegression.cs` est un harness moteur à copier temporairement dans `Code/Weapons`, puis à lancer en Play hôte avec `vb_grenade_regression`. Il vérifie le loadout, le slot, les munitions, un projectile unique et crédité au joueur, la vitesse initiale, le cooldown, le refus lorsque l'item est rangé, le mur au point de départ, la réserve vide, le réapprovisionnement, la limite de quatre, le blocage à terre/mort et les bodygroups du soldat. Retirer la copie après le test.

Le harness est compilé mais son exécution en jeu reste à effectuer : le serveur MCP de l'éditeur n'était pas accessible pendant cette passe. Tester également en première et troisième personne, puis avec un client distant : propriétaire du projectile, compteur, un seul dégât d'explosion, ramassage et arrivée tardive. Aucun essai réseau n'est revendiqué par la compilation.
