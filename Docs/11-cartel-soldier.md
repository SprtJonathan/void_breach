# Soldat du Cartel

La passe de munitions, échanges d'armes, chutes et survie systémique est détaillée
dans [14-systemic-soldier-survival.md](14-systemic-soldier-survival.md). Elle
ajoute la détection locale de rebords sans repère. Ses nouveaux comportements
ont été compilés et leurs calculs testés hors éditeur ; les validations Play
historiques ci-dessous précèdent cette passe.

Le prefab `creatures/cartel_soldier/cartel_soldier.prefab` ajoute un combattant armé inspiré du comportement d'un Combine Soldier. Ouvrir `scenes/soldier_test.scene`, laisser générer la NavMesh puis lancer Play : deux soldats, un zombie, des couverts et un mur de recherche sont placés sur une plateforme au-dessus de la carte. Le joueur utilise son prefab habituel, sans god mode. La scène emploie des instances liées aux prefabs ; les réglages ultérieurs du soldat s'y répercutent.

Le visuel utilise le Human natif et le `Dresser` manuel : Army Helmet, Army Vest, Army Shirt, Army Cargo Pants, Army Boots, Fingerless Gloves et cagoule du catalogue natif, avec leurs modèles Human. Aucun vêtement custom ou package tiers n'est requis. Le Dresser gère les parties du corps masquées par la tenue. La grenade réutilise l'item, le modèle tenu et le projectile du joueur. Le graphe Human installé contient `HoldItem_RH_Throw_Normal_delta` : `holdtype=holditem`, `holdtype_handedness=RH`, `holdtype_attack=0` et `b_attack` déclenchent ce lancer natif. L'arme à feu et son IK de support sont masqués pendant la préparation. `Grenade Warning Sound` permet d'ajouter une annonce audio.

## Réutilisation de s&box

- `NavMeshAgent`, Recast, `CalculatePath` et `SetPath` pour les déplacements. Seuls les chemins `Complete` sont acceptés. Aucun pathfinder supplémentaire.
- `VBWeaponInventoryComponent`, dérivé de l'inventaire natif et identique à celui du joueur, avec `ManualPumping = true` : l'IA ne lit jamais le clavier du joueur hôte et respecte les mêmes limites de port d'armes.
- La MP5 existante et `BaseCombatWeapon.FirePrimary`, `NpcUsage`, `SpreadScale`, les chargeurs, la réserve, les rechargements, les effets de bouche, les douilles, les impacts et les tracers natifs. Le prefab peut recevoir une autre `VBCombatWeapon` compatible PNJ.
- `CitizenAnimationHelper`, le Human et `ModelPhysics` pour les animations et le ragdoll.
- `Rigidbody`, `SphereCollider` et la gravité réelle de `PhysicsWorld` pour la grenade.
- `RadiusDamage` pour les dégâts explosifs, l'occultation de géométrie portant le tag natif `map`, la décroissance et les forces physiques. Les props de couvert ordinaires ne sont pas forcément occultants pour cette requête native. Le tag natif `explosion` arrive dans `VBHealthComponent` par son adaptation `IDamageable` existante.
- `prefabs/engine/explosion_med.prefab` pour les particules, l'audio et leur nettoyage. Ses dégâts sont désactivés sur les clones cosmétiques pour éviter une seconde explosion.

La version moteur installée ne permet pas de renseigner `BaseCombatWeapon.HolderRenderer` pour un PNJ. L'adaptation dans `VBCombatWeapon` attache donc le world model à l'os `HoldBone`, utilise le rayon de visée du soldat et crédite celui-ci comme attaquant. Elle réutilise les fonctions natives de tir et de présentation. Sur serveur dédié, une origine de tir de secours à hauteur d'arme évite de dépendre d'un modèle cosmétique.

L'attache PNJ conserve maintenant la transformation du prefab world model, notamment la translation de l'USP et la rotation de la P90, au lieu de l'écraser par une transformation nulle. `Npc Hold Position Offset` et `Npc Hold Rotation Offset` permettent un ajustement par arme. Avant le rendu, le modèle retrouve l'os courant si le Dresser a reconstruit le squelette. `scenes/soldier_appearance_preview.scene` fournit une vue fixe sans combat pour contrôler la tenue et la prise en main. La MP5 dispose d'une correction spécifique et d'une cible de main gauche pilotée par `CitizenAnimationHelper.IkLeftHand` et `SkinnedModelRenderer.SetIk`. Les prises P90/USP existantes sont conservées. L'IK est libéré pendant rechargement, mêlée, lancer et traversal. La scène compare les trois armes en Play. `vb_soldier_grip_debug` relève les transformations de mains ; `vb_soldier_grip_reload` recharge les profils des prefabs sur les armes PNJ présentes (outil hôte de calibration).

Le profil de combat actuel est décrit dans [Combat et dégâts localisés](13-combat-balance.md) : 80 PV, tête ×4, champ de vision resserré, acquisition/visée retardées et rafales courtes. La P90 sélectionnée sur le prefab est conservée ; les autres armes compatibles restent utilisables.

## Comportements et réglages

### Continuité du combat en espace étroit — 25 octobre 2026

Le tireur qui possède une ligne de tir utile dans sa portée préférée la tient
avant d'envisager un nouveau couvert ou un déplacement volontaire. Son temps
de visée initial n'envoie plus toute l'escouade chercher un abri. Les dégâts,
la recharge, un obstacle ou une distance inadaptée restent des raisons de bouger.
Le tir accroupi devient ainsi accessible avec les tactiques ordinaires actives.
Sa distance minimale par défaut passe à 180 unités, avec la vérification réelle
du canon. `PersonalSpaceRange = 100` limite les petits reculs nécessaires à
proximité immédiate ; `RetreatRange` reste utilisé pour les positions tactiques.

Une destination de soutien doit maintenant offrir une ligne de tir. L'ancien
score récompensait au contraire un point masqué, ce qui pouvait terminer un
contournement dans une zone sans intérêt pour le combat. Les chemins de
repositionnement, de couvert et de reprise de contact sont bornés par
`MaxCombatPathLength = 480` unités. Les flanks utilisent des chemins marchables
et ne déclenchent pas de franchissement. `TacticalSightLossBudget = 3` secondes
borne la perte de vue d'un déplacement volontaire ; un trajet annulé pour cette
raison demande une reprise de contact.

Un abri sans sortie de tir devient un refuge d'urgence pour un soldat blessé
ou en recharge, limité à cinq secondes. Le soldat sain le refuse. Les abris
avec sortie conservent leur alternance protection/exposition. Après l'abandon
d'un couvert, une temporisation empêche de sélectionner immédiatement le même
refuge.

`ContactRecoveryDelay = 6` secondes sans observation personnelle ni signalement
récent déclenche une réévaluation des actions de couvert/soutien/contournement.
Un soldat cherche alors une position de tir locale atteignable, en considérant
notamment son dernier emplacement utile. Cette reprise partage le créneau
d'investigation par joueur. Sans position de tir possible, le contrôleur laisse
enfin son investigation ordinaire rejoindre la dernière position observée.
Il ne renouvelle plus indéfiniment un état de soutien masqué avant de rechercher.
La reprise a une échéance et une vérification de progression, et s'arrête dès
la réacquisition. Aucun de ces objectifs ne lit une position ennemie cachée.

Les grenades peuvent aussi déloger un joueur visible restant dans une zone de
96 unités pendant `GrenadePressureDelay = 5` secondes. `InitialGrenadeDelay = 3`
secondes ouvre la première possibilité ; la récupération normale reste de
douze secondes et le stock est inchangé. Les durées de vol essayées sont
0,45 / 0,6 / 0,8 / 1 / 1,5 seconde, avec le même balayage sphérique, la même
limite de vitesse et les mêmes marges de sécurité. Les arcs plats servent aux
plafonds bas. Le lancer mémorise sa cible et revérifie sa validité et les alliés
à la fin de la préparation. L'exclusion d'une seule grenade de soldat reste
commune à la scène. La portée minimale et le rayon d'explosion peuvent toujours
interdire un lancer dans une pièce trop petite.

`vb_soldier_debug` affiche le motif de combat, l'âge du dernier renseignement,
la destination et l'échéance de reprise de contact et le temps d'immobilité
observé de l'adversaire. Les vitesses, l'accélération et le graphe Human sont
conservés.

### Perception, alerte et rôles d'investigation — 25 octobre 2026

Cette passe s'inspire de la séparation entre conditions, tâches et interruptions
du [Combine dans le Source SDK](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/server/hl2/npc_combine.cpp) :
une ronde cède devant un contact, une alerte oriente le soldat, et le tir exige
une confirmation personnelle. Le code Source n'est pas porté dans le projet.

Le cône initial de 120° reste celui de la vision à distance. À moins de
`CloseAwarenessRange = 180` unités, `CloseAwarenessFieldOfView = 270°` permet
de remarquer un joueur sur le côté. L'angle arrière reste aveugle pour un
adversaire silencieux jamais vu ; les murs, factions, morts et tags d'exclusion
restent contrôlés. La vision essaie le point de torse habituel puis la vraie
hauteur des yeux du joueur ou du PNJ, corrigée pour viser sa tête. Elle peut
donc remarquer une tête exposée sans inventer une tête debout au-dessus d'un
acteur accroupi. Cela ne débloque pas un canon masqué : le tir garde ses traces.

Pendant `AcquisitionDelay`, une silhouette aperçue interrompt déjà la ronde
et provoque l'orientation (`Alert`), sans donner le droit de tirer. Un indice
sonore ou radio déclenche jusqu'à `AlertObserveTime = 2` secondes d'observation,
puis une investigation. Une marche directe de ronde est elle aussi interrompue.
Les routes de couvert, contournements et priorités de survie déjà engagés
conservent leur traitement propre.

Avec `UseSquadAlerts`, un témoin partage sa dernière observation toutes les
0,8 seconde avec les soldats de la même faction, dans `SquadAlertRange = 1400`
unités autour de lui et de ses destinataires. La réponse prend 0,35–0,7 seconde.
Le message transporte une identité, une position observée et sa date. Le
destinataire les conserve dans `ReportedTarget`/`ReportedAt` et sa dernière
position connue ; il n'obtient ni `TargetVisible`, ni une nouvelle date de
vision personnelle. Il se tourne, vérifie lui-même le secteur et applique son
délai d'acquisition. Les messages vieux de plus de six secondes, les cibles
invalides et les informations qui remplaceraient un contact personnel récent
plus utile sont refusés. Seuls les témoins directs émettent : une information
indirecte ne se renouvelle pas en boucle entre soldats.

`MaxInvestigatorsPerPlayer = 1` réserve l'investigation d'une position perdue à
un soldat à la fois, avec une réservation indépendante pour chaque joueur.
Les autres observent des secteurs dégagés ou rechargent ; leurs actions de
couvert et de contournement existantes restent possibles. Une indisponibilité
ou une réacquisition libère la réservation. Les coups de feu alliés et bruits
non identifiés ne remplacent plus une piste ennemie valide.

`WatchOpenSectors` fait inspecter plusieurs directions pendant les pauses de
ronde et de recherche : douze traces évaluent les espaces dégagés, puis le
soldat garde son secteur pendant 2,5–4 secondes avant d'en observer un autre.
La rotation conserve le plafond de vitesse actuel. Un mur juste devant lui
n'est donc plus sa direction de surveillance par défaut. `vb_soldier_debug`
affiche aussi les signalements, la suspicion et les distances/cônes de vision
de chaque joueur, pour distinguer une exclusion de faction d'une obstruction.

### Tir accroupi stabilisé — 25 octobre 2026

`UseCrouchedFire` active une décision de tir distincte du couvert accroupi : un tireur arrêté, disposant d'un créneau contre un joueur visible à au moins 180 unités, peut prendre une position accroupie. Il attend `CrouchedFireDecisionTime` (0,8 s), puis `CrouchedFireSettleTime` (0,65 s, minimum 0,3 s) pour stabiliser sa posture avant de tirer. Les yeux ET le canon projetés à hauteur accroupie doivent avoir une ligne dégagée. Une caisse qui ne laisse tirer que debout empêche donc cette décision. Les couverts et les routes déjà engagées conservent leur priorité.

Le système de scène partage `MaxCrouchedShootersPerPlayer` (un par défaut), en complément des deux créneaux de tir et de la manœuvre volontaire unique. Un tireur peut se stabiliser tandis qu'un autre conserve le tir debout et que les autres cherchent un abri ou un détour. Chaque joueur possède sa propre réservation. La posture dure jusqu'à 5–8 secondes, sous réserve des autres priorités et du créneau de tir, puis observe un délai de 4–7 secondes avant une nouvelle tentative. Une hystérésis de distance évite les changements répétés autour de la distance minimale.

Cette posture est aussi disponible pendant une sortie latérale de couvert, une fois le soldat arrivé au point de tir. L'obtention du créneau de tir ouvre la durée d'exposition normale ; la décision et la stabilisation doivent tenir dans ce délai. L'accroupissement ne prolonge pas cette exposition : le soldat retourne à l'abri à son échéance.

Une fois arrêté et stabilisé, `CrouchedFireSpreadScale = 0.65` réduit de 35 % le cône natif de dispersion. Les dégâts, la cadence, le recul et le stock restent ceux de l'arme. `VBCombatWeapon.CurrentSpread` applique ce facteur à la volée, en conservant le calcul ADS existant ; aucun bonus n'est inscrit dans les caractéristiques permanentes de l'arme. Les armes lâchées ou récupérées par le joueur ne conservent pas ce bonus. Une posture encore en transition ou utilisée seulement pour se cacher ne suffit pas à l'obtenir.

Le joueur qui approche, une perte de ligne/visibilité, une recharge, un déplacement, une menace de survie, une demande de mise à couvert, une mêlée, la mort ou la désactivation interrompent ce rôle. Le budget de tir reste contrôlé pour chaque balle. `IsInFiringStance`, `IsCrouching` et le temps de stabilisation sont répliqués par l'hôte. Le canon de secours utilisé sans modèle cosmétique, notamment sur serveur dédié, descend lui aussi à la hauteur de la posture.

Le rendu utilise l'accroupissement du graphe Human actuel, les vraies collisions et la hauteur des yeux. La validation des paramètres d'animation ne remplace pas l'observation du rendu complet en Play. `vb_soldier_debug` affiche le rôle, le facteur de dispersion et le temps de stabilisation restant.

### Tempo posé, inertie et attention

Le profil livré marche à 115 unités/s en combat, 65 en patrouille et 85 en recherche. Le moteur physique du soldat approche sa vitesse à 240 unités/s² et freine à 320 unités/s², en conservant les collisions, les steps et la correction de foule natifs. Les zombies conservent leur moteur existant. Les pivots du corps sont bornés par `MaximumTurnRate` (110°/s), avec un suivi amorti `TurnSpeed = 4`. Une grenade ou un véhicule menaçant peut déclencher la vitesse d'urgence de 170 unités/s ; ces priorités de survie ne sont pas retardées par les pauses volontaires.

Une menace observée reste mémorisée pendant `CombatAttentionTime` / `AimMemoryTime` (18 secondes). La visée ne disparaît plus après deux secondes d'occultation. `LostContactHoldTime` (quatre secondes) permet d'observer le dernier contact avant d'investiguer ; rejoindre un abri, poursuivre un détour engagé et éviter un danger restent prioritaires. Pendant cette mémoire, le soldat peut redétecter cet adversaire dans un secteur élargi à `EngagedFieldOfView` (240°), toujours soumis aux traces d'occlusion et à la portée. Les adversaires inconnus conservent le champ initial de 120°. Une réapparition du même contact ne recommence pas le délai d'acquisition initial. Le soldat n'actualise jamais la position d'un joueur caché par ce mécanisme.

Le système commun autorise `MaxManeuversPerPlayer = 1` : un seul soldat à la fois effectue volontairement un repositionnement/contournement contre ce joueur. Cette permission est indépendante des deux créneaux de tir. Les autres peuvent tenir leur position, observer, recharger ou rejoindre un couvert nécessaire. `InitialAssessmentTime` (1,2 seconde, légèrement décalée entre soldats) précède la première manœuvre. Après un déplacement, `PositionHoldMin/Max` imposent un temps de stabilisation de 5–9 secondes ; `RepositionInterval` (12 secondes après fin/interruption de route) espace les nouvelles tentatives. De petits déplacements du joueur ne réinitialisent plus aussitôt une route ou un couvert encore utile.

Les positions abritées restent engagées 24–34 secondes avant réévaluation, tant que la protection est valide. Le soutien reste à l'abri 6–10 secondes et sort observer pendant au plus 1,2 seconde sans créneau de tir. Un abri profond sans sortie de tir est réservé à une urgence et limité à cinq secondes ; le contrôle de reprise de contact borne une disparition de toute l'escouade. La posture accroupie évolue plus progressivement. Les rafales conservent trois coups au maximum, puis 1–1,8 seconde de pause minimale ; `UseWeaponBurstTiming = false` sur le prefab applique ce rythme sans changer la cadence native des balles ni les rechargements. Les créneaux de tir tournent désormais par périodes de cinq secondes.

Ces réglages visent une lecture plus stable du combat. Les paramètres d'animation restent ceux du modèle Human ; il n'y a pas de nouvel ensemble de clips ni de simulation biomécanique. `vb_soldier_debug` affiche aussi la mémoire du contact, le temps de stabilisation et la limite de manœuvres.

### Priorités de soutien et couvert bas

Un soldat sans créneau de tir prend une décision de soutien qui ne retombe plus sur l'avance directe vers le centre du joueur : terminer un détour engagé, rejoindre un abri, recharger ou chercher un nouvel angle. Les chemins de soutien peuvent passer hors de vue du joueur, sans lire ses déplacements cachés ; le dernier contact est conservé dix-huit secondes pour poursuivre le détour. Le rayon demandé reste configurable par `SupportRepositionRadius`, avec une limite effective de 480 unités de chemin, une destination offrant le tir et un trajet complet marchable. Un échec de recherche est réessayé après 2–3,5 secondes, sous réserve du temps de stabilisation. Une navigation impossible laisse un état `Support` explicite plutôt qu'une convergence artificielle sur le joueur.

Les destinations déjà réservées par un allié sont exclues à environ quatre rayons d'agent (64 unités sur le prefab). L'espacement plus large reste un score souple. La séparation de foule et les collisions existantes restent identiques : les soldats peuvent partager une porte ou se croiser. Les soldats considèrent aussi les autres adversaires qu'ils voient au moment de chercher leur abri.

La recherche de couvert essaie d'abord la position actuelle et les faces proches des obstacles, puis des points de NavMesh. Le prefab utilise maintenant 24 échantillons. Un abri n'a plus besoin d'offrir immédiatement une position de tir : une position profondément protégée sert à récupérer/recharger, puis le soldat cherche une autre approche. Les trajets vers un abri et les détours de soutien ne déclenchent pas le franchissement dynamique de l'obstacle. Un abri utilisable passe avant une montée vers la cible ; les franchissements restent disponibles pour les autres objectifs.

`UseCrouchCover` permet de se baisser derrière une caisse : `CrouchHeight` vaut 44 unités. Le graphe Human reçoit `DuckLevel`, le `CharacterController`, la capsule de contact et la hauteur de perception suivent la posture répliquée `IsCrouching`. Le soldat vérifie le dégagement avant de se relever ; une caisse basse peut offrir une sortie de tir debout au même endroit. La NavMesh conserve son gabarit debout : cette posture ne débloque pas la navigation dans des tunnels trop bas. Le rechargement de soutien commence sous `SupportReloadThreshold` (65 % du chargeur), contre 25 % pour l'ancien appoint ordinaire. Une recharge abritée doit finir avant la sortie.

Un soldat en soutien reste couvert 6–10 secondes ; sa sortie d'observation sans créneau est limitée à 1,2 seconde. Le couvert peut conserver le contact mémorisé dix-huit secondes, afin que sa propre caisse n'entraîne pas immédiatement une recherche vers le joueur. La mort, la désactivation et le ragdoll annulent aussi une approche de franchissement encore en attente.

### Prefabs auxiliaires et audio

Le projectile utilisé est `weapons/grenade/frag_grenade_projectile.prefab`, commun aux joueurs et aux soldats. L'ancien `creatures/cartel_soldier/frag_grenade.prefab_c`, sans source ni référence actuelle, a été retiré. `npc_climb_link.prefab` reste un outil optionnel de placement pour les échelles/rebords particuliers ; aucune scène sauvegardée actuelle ne le référence. Le soldat n'en a pas besoin pour ses déplacements ordinaires ni ses franchissements détectés par géométrie.

`VBHealthComponent.PlayDownedSound` est un RPC diffusé aux pairs et joue le son sur le GameObject de l'acteur, donc à sa position dans le monde. L'événement fourni `sounds/player/health/downed_sound.sound` n'est pas un son UI et active l'atténuation et l'occlusion. Ce n'est pas un son réservé au joueur local. Sur le prefab soldat, `CanBeDowned = false` et `DownedSound = null` : aucun son de mise à terre n'est donc joué. Une chute temporaire en ragdoll n'est pas l'état de santé « downed ». `GrenadeWarningSound` est également vide ; les sons natifs des armes restent actifs. Aucune banque de voix de soldat n'est ajoutée par ces changements.

### Coordination multijoueur et orientation — 21 octobre 2026

Le système de scène `VBSoldierCoordination` partage un budget de **deux tireurs simultanés par joueur**. Chaque cible possède sa propre file : deux joueurs peuvent donc chacun être engagés par deux soldats. Le budget couvre tous les tirs du contrôleur, y compris les sorties de couvert, la retraite, les déplacements tactiques et la survie. Les cibles PNJ conservent leur combat ordinaire ; la mêlée ne consomme pas de créneau de tir. Les acteurs de faction `player` utilisent aussi ce budget, même sans `PlayerController`.

Les soldats capables de tirer demandent un tour de cinq secondes, conservé pendant leurs pauses entre rafales. À expiration, ils passent derrière les soldats en attente. La mort, le ragdoll, la désactivation, une recharge, une perte de visibilité, une ligne bloquée ou un changement de cible libèrent le tour ; les demandes non rafraîchies expirent après 0,35 seconde. Une absence de créneau ne donne jamais accès au tir par une autre branche de décision. Les soldats en attente privilégient le couvert, la recharge et des positions latérales améliorant l'angle d'approche, avec les mêmes contraintes de NavMesh et de sécurité. Sans position utilisable, ils gardent leur visée en attendant. Les sorties de couvert permettent également l'observation et ne garantissent pas un tour de tir.

Une seule grenade de soldat est autorisée dans toute la scène, de sa préparation jusqu'à la fin de sa mèche, suivie d'une pause collective de 1,5 seconde. Cette exclusion est revérifiée au départ du projectile. Une préparation annulée libère immédiatement le lancer ; un projectile déjà lancé reste bloquant même si son lanceur est détruit. Les grenades des joueurs ne consomment pas cette réservation. Les conditions existantes restent nécessaires : cible récemment vue puis occultée ou joueur visible durablement immobile, stock, distance, arc et absence d'allié dans la zone prévue. Les soldats ne lisent pas la position cachée du joueur pour contourner ces conditions.

Réglages communs dans **Project Settings > Systems > VBSoldierCoordination** : `MaxShootersPerPlayer`, `FireTurnDuration`, `FireSlotGrace`, `GrenadeRecovery`. Un plafond de zéro suspend les tirs contre les joueurs. Ces décisions sont exclusivement exécutées sur l'hôte ; `HasFireSlot` est répliqué pour le diagnostic. Les files sont propres à la scène et sont reconstruites après migration d'hôte. Les préparations et projectiles répliqués empêchent alors les lancers concurrents ; le délai collectif restant après une explosion n'est pas restauré.

Le corps suit la visée tandis que la navigation conserve sa direction indépendante. Le graphe Human existant reçoit la vitesse physique réelle pour ses pas latéraux et arrière et la vitesse angulaire du modèle pour les pieds. La visée utilise le point répliqué, sans suivre une transformation cachée. Au repos, `AimYawDeadZone` (12° par défaut) limite les petits pivots continuels. Les déplacements latéraux utilisent `CombatStrafeSpeedScale` (0,8), le recul `CombatBackpedalSpeedScale` (0,6). Ces réglages sont sur le composant soldat ; la patrouille et la fuite devant un danger gardent leur orientation adaptée. Il s'agit de locomotion directionnelle avec visée indépendante, utilisant les animations déjà disponibles. Aucun système de motion matching ni nouvel ensemble de clips n'est ajouté. La qualité visuelle reste limitée aux animations Human actuelles.

`VBAiPerceptionComponent` conserve la vision, l'audition et la dernière position connue. Le composant optionnel `VBAiThreatComponent` remplace la sélection par proximité pour les soldats uniquement. Il classe les acteurs visibles selon la proximité immédiate, un bonus joueur, les agressions récentes qui décroissent avec le temps et un bonus de stabilité pour la cible courante. Une menace très proche peut donc interrompre le tir sur un adversaire éloigné. Les zombies sans ce composant conservent leur sélection habituelle. Un dégât reçu hors vue utilise son point d'impact comme indice ; il ne révèle pas le déplacement caché de l'agresseur.

Le soldat appartient à `cartel`, hostile à `player` et `xen`. Les zombies sont déjà hostiles au Cartel. Les tags ne déterminent pas les relations. Les factions ignorées, les équipes, `notarget`, `ai_ignore` et les acteurs à terre suivent les règles existantes.

En combat, le soldat tourne vers sa cible, utilise les rafales et pauses `NpcUsage` de son arme, recharge avec la réserve configurée et tente de reculer sous `Retreat Range`. La ligne œil/cible puis la ligne canon/cible doivent être dégagées : un obstacle ou un allié placé devant lui bloque son tir. La dispersion native peut toujours provoquer des dégâts collatéraux.

## Limites communes au joueur et au soldat

Le soldat porte actuellement la P90, le même USP et les mêmes poings que le joueur, avec leurs dégâts, portées, cadences, délais de déploiement et durées de rechargement. L'inventaire limite également le nombre et l'encombrement des armes : la P90 et le pistolet occupent quatre points sur cinq et les deux emplacements principaux. Les poings sont le même placeholder sans poids que chez le joueur.

| Équipement par défaut | Chargeur | Réserve au départ | Plafond de réserve partagé avec le joueur |
| --- | --- | --- | --- |
| P90 (sélection actuelle) | 50 | 100 | 150 |
| USP | 15 | illimitée avec `-1` | 60 en mode fini |
| Grenades | — | 2 | 4 |

Les réserves sont des cartouches, comme chez le joueur, pas des objets chargeurs physiques. `Reserve Magazines` et `Sidearm Reserve Magazines` déterminent seulement la dotation initiale en équivalents de chargeurs, bornée par les ressources de munitions. Un rechargement partiel ne peut transférer que les cartouches restantes. Les armes utilisant la même ressource partagent une réserve. Un changement d'arme n'ajoute aucune munition et ne remplit pas un chargeur.

Exception configurable : **`Sidearm Reserve Magazines = -1` donne une réserve illimitée au pistolet du soldat**. `0` donne seulement le chargeur chargé ; une valeur positive donne ce nombre d'équivalents de chargeurs en réserve. Avec `-1`, l'USP conserve ses 15 coups, sa cadence, son déploiement et son rechargement normal de 1,5 seconde, y compris à couvert. Ce réglage ne remplit pas le stock partagé et ne modifie pas la ressource de munitions de l'arme. Dès que le pistolet quitte l'inventaire du soldat, il retrouve les réserves ordinaires : un joueur qui le ramasse ne reçoit aucune réserve illimitée. Le prefab actuel utilise `-1` ; le mode fini reste disponible avec une valeur positive.

Le tir natif dépense une cartouche automatiquement uniquement pour un porteur `PlayerController`. L'adaptation de `VBCombatWeapon` dépense donc exactement une cartouche via la même API native pour un porteur soldat, côté host, puis conserve les tirs et effets natifs. Le joueur garde son prélèvement natif unique. Cette correction supprime le tir illimité involontaire de la première version du soldat.

Une MP5 au chargeur vide reste utilisée si sa réserve permet de recharger. Quand son chargeur **et** sa réserve sont vides, le soldat déploie l'USP par le changement d'arme natif, avec son délai normal. Si les deux armes sont épuisées, il rejoint la cible pour utiliser ses poings. La mort lâche les deux véritables armes avec leurs chargeurs restants ; les réserves ne sont pas transformées en nouveaux chargeurs au sol.

Une cible visible et à portée des poings interrompt le combat à distance : l'arme est rangée, le rechargement éventuel est annulé par le moteur, et un coup se prépare pendant au moins 0,2 seconde. Le soldat reste engagé sur sa direction initiale. L'impact utilise les mêmes traces sphériques et paramètres que les poings du joueur : actuellement 20 dégâts contondants, portée 96 et délai natif de 0,45 seconde entre coups. L'IA ajoute sa préparation et sa récupération ; elle ne frappe donc pas plus vite que le joueur. Sortir de portée, s'occulter, renverser, désactiver ou tuer le soldat peut empêcher l'impact. Un coup n'inflige ses dégâts qu'une seule fois.

Le rechargement commence une fois arrivé à l'abri, avec une recharge préventive possible sous le quart du chargeur. Le soldat attend sa fin avant de ressortir. Si aucun couvert utilisable n'existe, ou s'il doit reculer face à une menace trop proche, il peut recharger exposé avec les mêmes restrictions de tir et la même durée que le joueur. Il ne bénéficie d'aucune protection spéciale pendant ce délai. Les décisions de couvert restent à valider en Play sur la géométrie réelle.

Le couvert est une décision tactique personnalisée construite sur la NavMesh et les traces natives. Le prefab échantillonne jusqu'à 24 positions dans un rayon de 500 unités. Il vérifie la protection du torse et de la tête, un emplacement adjacent pour tirer, le dégagement du corps et les chemins complets. Il alterne une attente à l'abri et une sortie de tir. Un rechargement ou des dégâts reçus provoquent un retour à l'abri. Un couvert détruit, une menace déplacée, un chemin bloqué ou une durée maximale expirée provoquent une réévaluation. Les couverts peuvent être debout ou accroupis avec les animations Human existantes ; leurs destinations sont réservées et leur qualité dépend de la géométrie et de la NavMesh.

Après perte de vue et observation du contact pendant quatre secondes, il rejoint la dernière position perçue, puis inspecte jusqu'à cinq points autour pendant douze secondes. L'abri n'entraîne pas l'abandon immédiat de la cible : le soldat conserve brièvement sa boucle couvert/sortie. Les déplacements de recherche et de patrouille possèdent une limite de trois secondes sans progression. La patrouille reprend ensuite autour du point de départ.

Deux grenades sont disponibles par défaut dans la même ressource native que le joueur, limitée à quatre, avec douze secondes entre tentatives réussies. La vitesse est bornée à 960 unités/s, soit le maximum de vitesse de lancer et de poussée verticale du joueur. Le lancer exige une cible récemment vue puis occultée ou un joueur visible durablement immobile, une distance suffisante, une zone d'impact sans allié et un arc dégagé. Cinq durées de vol, de 0,45 à 1,5 seconde, sont essayées ; chaque arc est balayé par 24 traces sphériques. Après une préparation de 0,8 seconde, l'arc, le stock et la sécurité des alliés sont revérifiés. Une grenade n'est prélevée que lorsqu'elle est réellement lancée. Mort, désactivation et ragdoll annulent la préparation. Une grenade déjà lancée conserve sa mèche. Les rebonds et déplacements ultérieurs des acteurs peuvent modifier les dégâts : la décision préventive ne garantit pas l'absence de dommages alliés.

## Montage et réseau

Le soldat et le joueur lancent le même projectile `weapons/grenade/frag_grenade_projectile.prefab`, avec les mêmes dégâts, mèche et physique. Son ancien emplacement dans le dossier du soldat a été supprimé ; le GUID reste identique. `weapons/grenade/frag_grenade.prefab` est l'item à tenir et ramasser, qui crée ce projectile lors du lancer. Le soldat utilise directement le projectile depuis sa décision de lancer et prélève la même ressource de grenades.

Le prefab doit être en `NetworkMode.Object`, simulé et spawné par l'hôte. Utiliser `VBWeaponInventoryComponent` en mode `Buckets`, sept slots, sans loadout automatique ni changement automatique d'arme. `Inventory.ManualPumping` doit rester activé. Renseigner `Weapon Prefab`, `Sidearm Prefab`, `Melee Prefab`, `Grenade Prefab` et `Grenade Ammo`. Les références livrées réutilisent les assets du joueur. Laisser `RadiusDamage.DamageOnEnabled = false` sur le prefab grenade : seul le composant de mèche déclenche `Apply`.

Un soldat doit avoir **un seul inventaire** sur sa racine. Une ancienne instance de scène peut conserver une surcharge qui rétablit `Sandbox.BaseInventoryComponent` et un second `VBWeaponInventoryComponent` ajouté par l'éditeur : les armes retrouvent alors le premier inventaire, alors que l'IA utilise le second, ce qui bloque leur déploiement et la mêlée. Les deux instances de `soldier_test.scene` ont été corrigées en préservant leur réserve de pistolet `-1`. Le validateur `Tests/AI/validate_soldier_assets.py` contrôle aussi ces surcharges. Les contrôles de munitions PNJ lisent les vrais chargeurs et réserves ; le comportement natif de `HasPrimaryAmmo` suppose autrement des munitions disponibles sans porteur joueur. La pose Human utilise les noms natifs des armes (`rifle`, `pistol`, `melee_punch`), sans conversion vers les anciens indices Citizen. À sec, le déplacement s'arrête à distance de frappe au lieu de viser le centre du corps adverse.

L'initialisation corrige désormais aussi les anciennes instances en mémoire : avant de créer l'équipement, `PrepareInventory` supprime les composants d'inventaire surnuméraires sur chaque pair et configure l'unique inventaire du soldat en `Buckets`, sept slots et contrôle manuel. L'hôte rafraîchit la structure réseau si elle était déjà active. Le journal indique les réparations. Le nettoyage des deux instances a également retiré les surcharges qui réajoutaient 96 os déjà intégrés au prefab et supprimaient 96 anciens identifiants absents : ces surcharges provoquaient des collisions de GUID et des erreurs de désérialisation dans l'éditeur. Les réglages de placement, de corps et de pistolet restent conservés.

La commande console `vb_soldier_debug` affiche l'état, la capacité à combattre, la cible et sa visibilité, la navigation et le dernier résultat de chemin, ainsi que l'inventaire actif, les chargeurs, les réserves et l'autorisation de tir. Elle permet de distinguer un acteur incapable de combattre, un échec d'équipement, une cible non détectée et un chemin impossible.

Pour une autre scène, activer la NavMesh avec Height 72, Radius 16 et exclure `player,player_ragdoll,zombie,npc,npc_body,npc_ragdoll` de sa génération. Le tag `npc` évite que le nouveau soldat soit pris pour du terrain. La matrice actuelle fournit déjà les relations `player` / `npc_body` et `player` / `npc_ragdoll`. `VBNavLinkTraversal` peut être ajouté pour reprendre les franchissements déjà utilisés par les zombies.

Les décisions, tirs, lancements et dégâts sont exécutés par l'hôte. L'état, le point de visée, les armes, la réserve native et les phases de mêlée sont répliqués. Les modèles et effets sont présentés sur les pairs. La mort lâche les armes et utilise le ragdoll existant. La migration de l'hôte ne conserve pas toutes les décisions tactiques locales en cours ; ce scénario reste à renforcer et tester, comme certains comportements existants du projet.

## Validation

La compilation .NET contre les assemblies s&box installées passe. Les prefabs et la scène ont été vérifiés structurellement. Les cinq harnesses de combat, équipement, présentation, perception et navigation sont documentés dans `Tests/AI/README.md` et leur dernière exécution en Play hôte se termine sans échec. Le multijoueur, les poses sous tous les angles et l'équilibrage des couverts sur les cartes du jeu restent à contrôler.

API de référence : [navigation](https://sbox.game/dev/doc/gameplay/navigation/), [BaseCombatWeapon](https://sbox.game/api/Sandbox.BaseCombatWeapon), [RadiusDamage](https://sbox.game/api/Sandbox.RadiusDamage).

## Déplacements tactiques et liens de traversal

La patrouille marche à 65 unités/s ; la recherche utilise 85, le combat utilise `MoveSpeed` (115 sur le prefab actuel). Les rafales gardent trois coups et la dispersion actuelle, avec des pauses minimales de 1–1,8 s. L'attention mémorisée reste active dix-huit secondes sans visibilité. Le tir reste possible en déplacement vers un couvert ou une position tactique si les yeux et le canon ont une ligne dégagée.

`Use Tactical Positions` essaie jusqu'à douze candidats ordinaires ou seize candidats de soutien, sous réserve de la permission de manœuvre et de la stabilisation après déplacement : positions latérales dont les angles sont adaptés au rayon et à la distance de tir et points natifs aléatoires dans 360 unités. Le score privilégie la ligne de tir, la distance utile, une hauteur modérée et un angle différent des alliés visant la même cible. Une pénalité espace leurs positions à environ 120 unités. Seuls les chemins complets sont acceptés ; la durée, la progression et le déplacement de la cible invalident une décision. Les dégâts reçus donnent priorité au couvert. Il s'agit de coordination locale et de contournements opportunistes ; les soldats ne se transmettent pas les déplacements d'une cible cachée et n'ont pas de rôles d'escouade explicites.

Les liens `NavMeshLink` existants permettent les arcs de franchissement et les grandes descentes en ragdoll. Le nouveau composant **NPC Traversal Link** (`VBNpcTraversalLink`) s'ajoute sur le même objet qu'un lien natif : mode `Climb` pour une échelle avec l'animation Human `b_climb`, ou `Vault` pour un arc paramétrable. Orienter l'objet vers la surface à grimper. Les extrémités doivent rejoindre deux surfaces marchables de la NavMesh ; les `Waypoints` optionnels sont des positions des pieds, ordonnées de LocalStart à LocalEnd et inversées automatiquement en sens retour. Une arrivée au-dessus d'un rebord demande des points intermédiaires dégageant le corps avant le retour horizontal sur la plateforme. Le chemin complet du corps est prévalidé et balayé pendant le déplacement ; une obstruction refuse le départ ou provoque une chute récupérable en cours de route. Mort, ragdoll et désactivation interrompent le déplacement.

Les liens restent facultatifs pour les passages particuliers, dont les échelles. Les rebords statiques peuvent désormais être détectés sans repère avec `UseDynamicTraversal`, via la géométrie partagée avec le joueur. Voir `14-systemic-soldier-survival.md` pour les conditions et limites de cette détection. La montée n'ajoute aucune invulnérabilité. Les passages étroits, les portes/props mobiles et la migration d'hôte pendant un lien demandent une validation en jeu ; les opérations de traversal asynchrones des anciens liens ne sont pas restaurées lors d'une migration d'hôte.

Le prefab `creatures/cartel_soldier/npc_climb_link.prefab` fournit un lien bidirectionnel et un waypoint de dégagement à adapter à la hauteur et à la largeur du rebord. Les tests locaux ont vérifié la montée, le retour en descente et le refus avant départ d'un corps obstrué sur deux plateformes générées. Les géométries de carte particulières restent à contrôler. Voir `Tests/AI/README.md` pour les résultats de la passe du 17 octobre.

`Use Weapon Burst Timing` permet de revenir aux repos natifs `NpcUsage.RestMin/RestMax` de l'arme. Le profil actuel le désactive pour appliquer `BurstRestMin/Max` comme minimums de pause. La cadence de chaque balle reste celle de l'arme.
