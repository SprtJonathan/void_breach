# IA zombie — base multijoueur

Cette première version transpose les idées utiles du zombie HL2 sans reproduire son ancien système de schedules. Le serveur hôte décide de la cible, du chemin, des attaques, des dégâts, des stimuli sonores, des chutes et du point de relevage. Les clients reçoivent l'état et la transformation réseau, puis jouent localement les animations, sons et ragdolls.

## Ce qui a été retenu du code HL2

- une machine d'états courte : repos, patrol, alerte, poursuite, recherche locale, attaque, ragdoll et mort ;
- une attaque de mêlée soumise à la distance, à l'orientation, à la visibilité, à un délai d'impact et à une récupération ;
- une mémoire de la dernière position visible, afin que le zombie ne perde pas instantanément sa cible ;
- une errance servant aussi de repli lorsqu'un chemin échoue ;
- la réaction à l'attaquant et aux fortes impulsions ;
- des sons séparés pour l'alerte, le départ d'attaque, le coup réussi et le coup manqué.

Les portes à défoncer, le lancer d'objets physiques, le démembrement et le headcrab sont volontairement laissés hors du noyau. Ils pourront devenir des composants/capacités optionnels sans alourdir tous les PNJ.

## Montage d'un prefab Citizen de test

Créer un GameObject racine `Zombie Test`, avec le tag `zombie`, en mode réseau `Object`. Tout spawn dynamique de ce prefab doit être effectué par l'hôte, puis suivi de `NetworkSpawn()`.

Sur la racine, ajouter :

1. `NavMeshAgent` : Height `72`, Radius `16`, Max Speed `105`, Acceleration `500`, Update Position activé, Update Rotation désactivé.
2. `VBHealthComponent` : Can Be Downed désactivé, Starting Hard Max Health à la valeur voulue (par exemple `100`).
3. `VBZombieController`.
4. `VBNavLinkTraversal` si le PNJ doit emprunter des liens spéciaux.

Les attributs `RequireComponent` ajoutent et mettent automatiquement en cache `NavMeshAgent`, `VBHealthComponent`, `VBNpcRagdollController`, `VBAiPerceptionComponent` et `VBFactionComponent` sur la racine. Ils ne sont donc plus exposés comme références à remplir dans chaque composant.

Ajouter un enfant `Body` :

1. `SkinnedModelRenderer` avec `models/citizen_human/citizen_human_male.vmdl` et Use Anim Graph activé.
2. `CitizenAnimationHelper`, dont Target référence le renderer.
3. `ModelPhysics`, dont Model et Renderer référencent le Citizen, avec Ignore Root activé et Motion Enabled désactivé.

Ajouter un enfant `Colliders` avec un `CapsuleCollider` adapté au Citizen. Le contrôleur de ragdoll trouve automatiquement les colliders descendants et les coupe pendant la simulation physique.

Les composants visuels situés dans `Body` sont recherchés une fois dans les enfants. Seuls les composants moteur qui en ont intrinsèquement besoin, comme `CitizenAnimationHelper.Target`, conservent leur référence directe vers le renderer.

## Ragdoll existant, pas corpse séparé

`VBNpcRagdollController` ne spawn aucun objet. Il orchestre le `ModelPhysics` du renderer déjà présent : copie de la pose animée, désactivation de l'animgraph et des colliders de locomotion, simulation physique, puis restauration éventuelle. À la mort, ce même mesh reste en ragdoll permanent. Lors d'une chute non létale, le serveur choisit le point de relevage et réactive le même personnage. L'impulsion initiale est diffusée à tous les clients afin que leur simulation cosmétique commence dans des conditions comparables.

Le mesh physique reçoit automatiquement le tag `npc_ragdoll`. La matrice `ProjectSettings/Collision.config` ignore la paire `player` / `npc_ragdoll` : le corps continue donc de heurter le décor et les objets pertinents sans bloquer ni pousser les joueurs.

Un corpse séparé reste une stratégie possible plus tard pour le pooling ou pour détruire rapidement l'objet IA, mais ce n'est pas le fonctionnement actuel.

### Stratégie multijoueur cible

Pour un extraction shooter comportant potentiellement beaucoup de cadavres, la stratégie recommandée est hybride :

- l'hôte reste seul autoritaire sur le passage en ragdoll, l'impulsion, la mort, le relevage et la position finale utile au gameplay ;
- chaque client simule les os localement pendant le mouvement, sans synchroniser chaque os à chaque tick ;
- un ragdoll récupérable reste rattaché à son objet IA réseau, car sa position finale détermine le relevage ;
- un cadavre définitif peut être figé après stabilisation, puis remplacé plus tard par une représentation de corpse allégée et poolée ;
- les cadavres lointains peuvent être limités, simplifiés ou supprimés localement selon un budget visuel, sans modifier la vérité serveur.

Synchroniser continuellement tous les os serait coûteux et inutilement sensible à la latence. La version actuelle constitue le premier palier de cette stratégie ; le gel de pose, le pooling et le budget de cadavres seront à ajouter lorsque les tests de densité d'ennemis fourniront des chiffres réels.

## NavMesh et chutes

Activer le NavMesh dans les propriétés de la scène. Pour un Citizen de test, commencer avec Height `72`, Radius `16`, Step Size `18` et Max Slope `40`, puis régénérer le maillage.

Le NavMesh ne crée pas seul un chemin à travers un vide. Placer un `NavMeshLink` entre le bord supérieur et la zone d'atterrissage. Lorsqu'un lien descend d'au moins `Ragdoll Drop Height` (96 unités par défaut), `VBNavLinkTraversal` :

1. rend la navigation manuelle ;
2. active le `ModelPhysics` à partir de la pose animée courante ;
3. laisse le ragdoll tomber sous l'effet de la physique ;
4. attend qu'il touche le sol et se stabilise ;
5. place la racine au sol, restaure l'animgraph et rend la main au NavMeshAgent.

Les liens moins hauts utilisent une trajectoire en arc. Cela fournit dès maintenant une base pour des sauts, franchissements ou futures échelles sans coupler ces mouvements au cerveau du zombie.

## Perception, factions, recherche et bruit

`VBAiPerceptionComponent` est commun aux futurs archétypes de PNJ. Il mémorise la cible visible et sa dernière position connue, mais ne décide jamais du mouvement ou de l'attaque.

`VBFactionComponent` porte un identifiant de faction, un futur identifiant d'équipe, les factions hostiles et les factions ignorées. Les relations de faction sont la source de vérité du ciblage ; il n'existe plus de fallback par tag. Cela permet la matrice prévue par les documents v0.3 : équipes de joueurs, Cartel, Xen et boss, y compris des relations asymétriques.

- joueur : `FactionId = player`, `HostileFactions = [cartel, xen]`, `HostileToOtherTeams = true` ;
- zombie/headcrab : `FactionId = xen`, `HostileFactions = [player, cartel]` ;
- soldat : `FactionId = cartel`, `HostileFactions = [player, xen]`.

Le prefab joueur possède déjà sa configuration. Le contrôleur zombie initialise la faction Xen si aucune faction explicite n'est configurée. Le futur `TeamService` n'aura qu'à renseigner `TeamId` : deux joueurs de la même équipe restent non hostiles, tandis que deux équipes distinctes peuvent devenir hostiles.

Les tags restent réservés aux filtres techniques et à la physique. Le joueur conserve `player`, le zombie conserve `zombie`, et `VBAiPerceptionComponent.IgnoredTags` contient par défaut `ai_ignore` et `notarget`. Un acteur portant l'un de ces tags est ignoré même si sa faction serait hostile. Les identifiants de faction ne sont volontairement pas recopiés en tags sur la racine : les tags étant hérités par les enfants et utilisés par la matrice physique, cela pourrait modifier les collisions du Rigidbody, des colliders et du ragdoll.

Quand une cible disparaît, le zombie poursuit sa dernière position connue. Une fois arrivé — ou après `Lost Target Chase Timeout` — il choisit plusieurs points aléatoires dans `Search Radius`. Une fois `Search Duration` ou `Search Point Count` épuisé, il reprend sa patrol aléatoire normale.

La patrol possède un watchdog : si l'agent reste déclaré en navigation sans progresser pendant `Patrol Stuck Timeout`, le chemin est abandonné et un autre point est choisi après une courte pause. Plusieurs points NavMesh sont essayés et les destinations trop proches sont rejetées. Le roam reste centré autour du point de spawn, puis se recentre par défaut autour de la dernière zone fouillée après la perte d'une cible. L'état `Idle` correspond donc à une respiration entre deux déplacements ou à un retry bref ; avec `Patrol Radius > 0`, il ne constitue pas un sommeil définitif.

`VBAiSoundStimulus` fournit une perception sonore commune. Un tir de `VBCombatWeapon` envoie au host un stimulus `Gunshot` de rayon `Gunshot Ai Radius` (1800 unités par défaut). Chaque perception applique sa sensibilité, réduit la portée si le décor masque le bruit, puis vérifie les factions et tags d'ignore. Entendre ne donne jamais une vision à travers un mur : le zombie rejoint le point entendu, cherche aléatoirement autour de celui-ci, et ne passe en poursuite visuelle que s'il acquiert réellement la cible. Explosions, impacts, portes et voix pourront réutiliser le même appel :

```csharp
VBAiSoundStimulus.Emit( position, radius, source, VBAiSoundKind.Explosion );
```

Il ne vise pas les joueurs à terre tant que `Can Target Downed` reste désactivé sur la perception. Les dégâts de mêlée passent directement par `VBHealthComponent` avec le type `Blunt`; les autres implémentations de `Component.IDamageable` restent supportées.

Le paramètre d'animgraph d'attaque est exposé sous `Attack Animation Parameter` et vaut `b_attack` par défaut. Si le graph Citizen utilisé ne fournit pas ce paramètre, l'IA et les dégâts fonctionnent quand même ; il faudra brancher un graph/événement d'animation dédié pour obtenir une attaque zombie finalisée.

## Test multijoueur minimal

1. Générer le NavMesh et poser au moins un lien de descente.
2. Placer le prefab zombie dans une scène réseau ou le cloner uniquement côté hôte, puis appeler `NetworkSpawn()`.
3. Lancer avec un hôte et un client.
4. Vérifier que seul l'hôte choisit les chemins et inflige les dégâts.
5. Tirer sur le zombie : une impulsion supérieure au seuil doit le renverser temporairement ; sa mort doit laisser un ragdoll permanent.
6. Attirer le zombie vers le lien : la chute doit être physique, suivie d'un relevage et de la reprise de poursuite.

Le GameObject racine mobile doit être en `NetworkMode.Object`. Ses enfants visuels peuvent rester en `Snapshot`, car leur transform local ne change pas indépendamment. Un objet racine en `Snapshot` n'envoie ni ses transforms ultérieurs ni ses propriétés `[Sync]` après la connexion initiale.

## Compatibilité Headcrab

Le Headcrab pourra réutiliser `VBHealthComponent`, `VBFactionComponent` et `VBAiPerceptionComponent`, mais pas `VBZombieController`. Il aura son propre contrôleur de comportement et un moteur de saut physique : préparation/commit de la cible, calcul d'une vélocité balistique, attaque au contact en vol, détection d'échec contre le décor et cooldown. Il n'a pas besoin du contrôleur de ragdoll bipède récupérable.

## Alignement avec les documents v0.3

La base couvre maintenant les points déjà planifiés : IA simulée par l'hôte, navigation Recast pour les humanoïdes et le zombie, santé/perception/ciblage/factions/navigation partagés entre zombie et soldat, matrice Joueur/Cartel/Xen, zombie attaquant joueurs et Cartel, et critère de validation demandant d'observer les factions s'affronter.

Éléments explicitement prévus mais encore absents : séparation formelle entre perception et sélection de cible, `TeamService` alimentant `TeamId`, soldat du Cartel capable d'alerter les unités proches, routes de patrouille propres aux `RaidZone`, outils de debug IA, profils d'assaut post-boss et variantes de zombies. La traque, l'escalade et le franchissement avancé restent des évolutions ; seul le zombie simple appartient au minimum de la vertical slice.

## Prochaines extensions prévues par la séparation actuelle

- capacité `DoorBash` pour réagir à une porte bloquante ;
- capacité `PhysicsSwat` pour choisir et frapper un objet situé entre le zombie et sa cible ;
- attaque déclenchée par un événement d'animation plutôt que par un délai ;
- variantes de locomotion et de statistiques alimentées par une ressource de configuration commune.
