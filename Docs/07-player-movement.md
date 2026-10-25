# Déplacements du joueur

Le prefab `Assets/player/player controller.prefab` conserve le `PlayerController`, son Rigidbody et les modes Swim/Ladder. `VBMoveModeWalk` dérive de Walk et garde ses collisions, marches, saut et accroupissement. Traversal prend la main uniquement pendant une action manuelle validée. Priorités : Walk 1, Traversal 2, Ladder 5, Swim 10.

La marche ordinaire appelle directement `MoveModeWalk.UpdateMove`, `UpdateRigidBody` et `AddVelocity`. Le solveur d'inertie, son air control et ses courbes de sprint ont été retirés, ainsi que `VBMoveModeWalk.Momentum.cs` et ses propriétés sérialisées. `VBMoveModeWalk` conserve le slide et l'intégration du franchissement. La détection et les trajets de traversal restent dans `VBMoveModeTraversal.Detection.cs`, et les diagnostics dans `.Debug.cs`.

## Marche native, sprint et slide

Les valeurs de déplacement du `PlayerController` et du Rigidbody sont réalignées sur le template livré avec s&box : marche 110, sprint 320, accroupissement 70 et saut 300 unités/s, `AccelerationTime=0`, `DeaccelerationTime=0`, `BrakePower=1`, `AirFriction=0.1`, `LinearDamping=0.1`. Les dimensions du corps et les colliders correspondaient déjà au template. Les références du prefab, la caméra FPS, les armes, la santé, la stamina, le slide, le franchissement et les véhicules sont conservés. Les modes Swim/Ladder utilisent leurs réglages natifs ; la priorité Walk reste 1 pour l'intégration du projet.

L'animation est entièrement héritée du `PlayerController` natif : aucun override d'animation dans `VBMoveModeWalk`. L'animgraph Human sélectionne automatiquement marche, jogging et sprint. Le modèle masculin et le modèle `human.vmdl` du template natif référencent tous deux `models/citizen_human/animgraphs/human.vanmgrph` et la même liste d'animations Human. Redémarrer Play après la mise à jour du prefab réinitialise les anciens paramètres d'animation et les états physiques.

Le déplacement et la direction du slide utilisent directement le yaw de `PlayerController.EyeAngles`, avant conversion en rotation. Le solveur natif reçoit cette même orientation horizontale. Regarder exactement vers le sol ou le ciel conserve ainsi les directions des touches, sans extraction instable du yaw depuis la rotation verticale.

La stamina peut toujours refuser le sprint via le point d'entrée natif `PreInput`. En dehors d'un slide ou d'un franchissement, aucune friction, gravité, accélération ou courbe de vitesse supplémentaire n'est appliquée. Le comportement aérien est celui de s&box.

Sprinter vers l'avant, puis presser une fois `duck` déclenche un slide dès 200 unités/s, après 0.15 s de sprint. Relâcher la touche conserve le slide et sa posture. Le saut, la perte du sol, une vitesse insuffisante, un obstacle, la durée limite ou l'état du joueur le terminent. Une nouvelle pression et le cooldown sont nécessaires pour recommencer.

| Réglage du prefab | Valeur |
|---|---:|
| SlideSpeedBoost / SlideMaximumSpeed | 35 / 420 |
| SlideDuration / SlideCooldown | 0.8 / 0.65 s |
| SlideDeceleration / SlideEndSpeed | 280 / 120 |
| SlideSteeringDegreesPerSecond | 70°/s |
| SlideStaminaCost | 8 |
| SlideDownhillAngle / SlideDownhillDragScale | 12° / 0.35 |

Une descente suffisante dans le sens du déplacement réduit le drag et remet à zéro la limite de durée. La gravité est projetée sur le plan du sol une seule fois ; collisions et plafond de vitesse restent actifs. En montée ou sur plat, le freinage et la durée habituels reprennent. `VBSlideStanceSystem` conserve la posture via l'accroupissement natif, sans modifier les actions d'entrée.

`VBSlideCamera` ajoute en première personne : FOV +8°, roll -3°, pitch +0.5°, abaissement de 3 unités, transitions 0.08/0.16 s. Chaque effet est réglable ou désactivable. L'intensité suit la vitesse et se réduit à 20 % en ADS. Le headbob de marche s'atténue pendant le slide.

## Franchissement et accroche manuelle

- `jump` face à un obstacle bas : franchissement avec réception sûre, ou montée sur le dessus.
- `jump` face à une structure plus haute : montée si hauteur, réception et trajet sont valides.
- `jump` en l'air : accroche pendant la montée ou la chute, si le rebord est accessible.
- Suspendu : relâcher `jump` pendant au moins 0.12 s, puis presser à nouveau pour monter. Si le dessus est trop fin, cette confirmation franchit le rebord vers une réception sûre derrière. Les appuis pendant le rattrapage ne déclenchent pas une montée immédiate.
- `duck` : lâcher ou interrompre. Sans appui manuel sur `jump`, aucune accroche ne se produit.

Un appui est mémorisé pendant 0.25 s, y compris entre deux ticks de physique. Chaque nouvelle pression renouvelle cette fenêtre ; maintenir la touche ne la renouvelle pas. La demande est consommée au départ et annulée par l'accroupissement, un slide, un autre mode ou l'expiration.

| Réglage du prefab | Valeur | Effet |
|---|---:|---|
| ReachDistance | 36 | Portée devant le corps ; rayon depuis son centre de 52 avec BodyRadius=16 |
| MinimumHeight / VaultMaximumHeight | 24 / 64 | Petites marches natives / franchissement bas |
| ClimbMaximumHeight | 112 | Hauteur depuis les pieds pour une montée au sol |
| CatchReachAboveHead | 0 | Aucune extension au-delà des 72 unités du corps pour l'accroche en l'air |
| MaximumCatchFallSpeed | 600 | Refuse une chute plus rapide |
| MaximumLandingAngle | 25° | Pente maximale de réception |
| BodyTraceInset | 0.25 | Tolérance de contact des requêtes, plafonnée à 0.5 unité |
| LandingSupportScale | 0.5 | Demi-étendue des quatre points d'appui, en fraction de BodyRadius ; 0.9 retrouve l'exigence précédente |
| VaultForwardDistance / VaultMaximumDrop | 80 / 80 | Recherche de réception derrière l'obstacle |
| EnableThinLedgeVault / LedgeVaultMaximumDrop | true / 128 | Franchissement d'un dessus impropre à la station debout, avec limite de chute à l'arrivée |
| VaultDuration / ClimbDuration | 0.45 / 0.65 s | Durées des mouvements |
| Cooldown / JumpInputBuffer | 0.35 / 0.25 s | Délai après fin / mémoire d'un appui |
| TraversalStaminaCost / HangDrainPerSecond | 8 / 5 | Départ / consommation par seconde suspendu |
| MaximumHangTime | 0 | Pas de timeout dans le prefab ; la stamina limite l'accroche |
| MinimumCatchHoldTime | 0.25 s | Réserve de stamina après le coût initial |

Avec JumpSpeed=300 et la gravité actuelle de 850, le saut natif monte d'environ 51 unités. Une structure de 120 reste accessible avec le bon timing ; 128 demande un départ surélevé. Le plafond réel dépend du saut, de la gravité, de la posture et de la géométrie. Les limites de hauteur sont strictes : la marge des traces ne les étend pas.

La détection exige une face presque verticale, une approche frontale, un dessus praticable, un support sous les quatre coins et un trajet dégagé. Le mouvement suit trois segments (monter, passer au-dessus, descendre), avec des requêtes aux dimensions du capsule et des pieds natifs. Aucune collision n'est désactivée. Les chemins sont revérifiés pendant la physique ; un support retiré, désactivé, déplacé ou une obstruction nouvelle provoque le relâchement. Les structures mobiles ne sont pas prises en charge.

Le rayon du dessus est placé à 0.5 unité derrière la face pour détecter les clôtures fines. La réception du corps reste plus loin derrière la face et exige le support sous ses quatre coins. Si cette place manque, une réception derrière est cherchée et tout le trajet est balayé avant l'accroche, puis à nouveau lors de la confirmation. La clôture et le support de réception sont surveillés pendant l'action. Un dessus large avec un trajet bloqué reste refusé. Les plafonds de hauteur et le verrou de combat s'appliquent aussi à ces franchissements.

Les quatre points d'appui sont espacés de `BodyRadius × LandingSupportScale` autour de la réception. Avec BodyRadius=16 et une face alignée aux axes, la profondeur minimale théorique passe d'environ 34.4 à 28 unités ; un dessus de 32 peut donc accueillir une montée. Le volume du corps et le trajet restent vérifiés à leurs dimensions physiques. Le réglage est disponible dans Traversal → Detection, entre 0.25 et 1.

Cet ajustement passe 16 scénarios dans le moteur : montée et réception stable sur des dessus de 28/32 unités, montée de 32 sans sol derrière, retour au vault avec l'ancienne marge de 0.9, vault des clôtures de 4/16 unités, et refus d'un dessus de 24 sans réception derrière. Les cas de plafond, obstruction nouvelle, support retiré, chute excessive et géométrie dynamique restent validés.

Pendant Vaulting, Climbing, Catching et Hanging, `VBTraversalCombatWeapon` bloque les attaques primaires/secondaires et le rechargement des armes et des poings, y compris les appels directs. Un rechargement déjà commencé est annulé et la visée ADS est retirée. L'hôte refuse aussi les demandes de dégâts de tir lorsque son état de traversal répliqué est actif. Les actions redeviennent disponibles à la fin du traversal, selon les délais natifs de l'arme. Les nouvelles armes de combat doivent hériter de cette base commune.

## Mains sur le rebord par IK

`VBTraversalHands` utilise les chaînes `hand_left` / `hand_right` de l'animgraph Citizen, sur les os `hand_L` / `hand_R`. Le contact vient du rebord réellement validé, jamais d'une prédiction visuelle. L'IK ne décide pas de la réussite et ne déplace pas le collider.

| Réglage | Valeur du prefab | Effet |
|---|---:|---|
| EnableHands | true | Active la présentation IK |
| FaceLedge | true | Oriente visuellement le torse vers le rebord, sans bloquer la caméra |
| HandSpacing | 28 | Écart entre les mains |
| GripOffset | 3,0,1 | Offset local : vers le dessus, latéral, vertical |
| LeftHandRotation / RightHandRotation | 0,0,-90 / 0,0,90 | Orientation locale des paumes |
| BlendInTime / BlendOutTime | 0.12 / 0.15 s | Transition progressive des cibles |
| ReleaseHeight | 36 | Relâchement lorsque les pieds approchent du dessus |
| HideHeldViewModel | true | Remplace temporairement les bras FPS de l'objet tenu par les bras du personnage |

La prise reste fixe dans le monde pendant la suspension. Les mains lâchent progressivement pendant la montée et à la fin de l'action ; une fois relâchées au-dessus du rebord, elles restent libres pendant la descente derrière une clôture. Les paramètres sont nettoyés lors d'une désactivation, destruction ou changement de renderer. En première personne, `CameraPerspectiveTagFilter` révèle le corps local et masque sa tête et les accessoires de tête dans les objets de rendu locaux ; les propriétés du modèle et les snapshots réseau sont conservés. Le viewmodel tenu retrouve ensuite sa visibilité.

Cela donne une pose de bras fonctionnelle, mais ne remplace pas une animation complète du torse, des jambes et des doigts. Un modèle personnalisé doit exposer ces os et chaînes IK. Les modèles Citizen natifs utilisent une dernière option Head vide pour le masquage local.

## Multijoueur, dégâts et debug

Le prefab joueur contient `VBPlayerCamera` et ses réglages de FOV, priorité et plans de coupe. Seul le propriétaire local crée l'enfant `Player Camera` si `PlayerController.UseCameraControls` est actif. Aucun lien vers cette caméra n'est sérialisé dans le joueur : l'enfant est `NetworkMode.Never` et le composant caméra `NotNetworked`. Les proxies et serveurs dédiés n'en créent pas ; une caméra déjà créée est désactivée lors d'une perte de propriété ou des contrôles caméra.

Le contrôleur s&box place cette caméra et conserve les effets existants : FOV, première/troisième personne, headbob, slide, véhicule, ragdoll et spectateur. La caméra est principale avec une priorité de -101, qui la fait sélectionner avant la caméra de map ordinaire dans la version installée du moteur. Une caméra de scène n'est donc plus une dépendance du joueur. Le spawn réseau exige toujours un prefab et un emplacement valides ; il est indépendant de l'activation de la caméra. Les scènes d'éditeur et les modèles de prefab hors de la scène active ne peuvent pas activer cette caméra.

La régression caméra/spawn passe maintenant 20 contrôles dans le moteur dédié, dont absence de caméra sur serveur dédié, création locale unique, exclusion de la caméra et de sa référence du snapshot natif, désérialisation dans une scène distincte, conservation des réglages, placement natif en première/troisième personne, suivi et appel réel du spawn après suppression d'une référence nulle (`tmp/peer-camera-final-native.log`). Les confirmations graphiques hôte et client sont décrites ci-dessous.

Le blocage de SoldierTestMap a ensuite été identifié dans la vraie session de l'éditeur : le prefab avait été chargé à 15:39:40 avant disponibilité de `VBPlayerCamera`, et son cache conservait un `MissingComponent` désactivé. Les nouveaux joueurs héritaient de ce composant manquant. Le rechargement du prefab après disponibilité du type a rétabli le composant, puis un nouveau Stop/Play a rétabli l'image et le contrôle, confirmés par l'utilisateur. Les traces avant/après sont dans `tmp/editor-spawn-diagnosis.log`. Les diagnostics temporaires ont été retirés. Pour ajouter un futur composant au prefab, laisser compiler son type avant d'enregistrer sa référence dans l'asset.

La première version envoyait encore une référence `VBPlayerCamera.Camera` vers un composant exclu des snapshots. Le deuxième client signalait `Component ... CameraComponent was not found` et n'avait aucune vue. Cette référence et la caméra préfabriquée ont été retirées : chaque propriétaire crée sa présentation native localement, à partir des réglages transmis avec le joueur. Un clone n'active rien avant son attribution réseau. La reconnexion réelle d'un deuxième client a confirmé une caméra locale active et sélectionnée sur Smithers, aucune caméra du joueur distant Nelson sur ce client, et le maintien de la vue de l'hôte. L'utilisateur a confirmé le retour de l'image du joueur 2 (`tmp/two-peer-camera-validation.log`). Les diagnostics de cette vérification ont été retirés.

Seul le propriétaire lit les entrées et simule déplacement, slide et stamina. `IsSliding`, l'état Traversal et son `Grip` sont répliqués. La prise est envoyée au début de l'action ; chaque client calcule ses mains localement. Les effets caméra restent locaux et les serveurs dédiés n'évaluent pas cette présentation.

La santé reste contrôlée par l'hôte. Les impacts Rigidbody natifs sont calculés par le propriétaire, regroupés par source et instant de simulation, puis transmis via un RPC réservé au propriétaire. Plusieurs formes du corps ne facturent que le plus fort contact, avec seulement l'incrément si le suivant est plus fort. Sources distinctes et autres dégâts restent indépendants. Les impacts utilisent le type Blunt. Cette transmission suit l'autorité physique native du propriétaire ; elle n'est pas une validation physique serveur indépendante.

Dans Traversal → **Debug**, `EnableTraversalDebug` active le feedback de test et les traces ; il est désactivé dans le prefab. `DebugDrawTraces`, `DebugIgnoreDepth`, `DebugTraceDuration` règlent le dessin des rayons, capsule et pieds réellement utilisés. Vert : requête passée ; rouge : refus ; cyan : portée testée. Le panneau indique la raison, le volume en collision, la fenêtre de hauteur, le buffer et la dernière transition. Un rebord détecté ne garantit pas une prise : réception, chemin, stamina et support doivent également passer.

## Historique des vérifications dans l'éditeur

Les résultats ci-dessous précèdent le retour au déplacement natif. Ils décrivent les versions antérieures et ne constituent pas une validation en jeu de cette révision. La compilation actuelle et la comparaison des propriétés du prefab au template natif passent ; rejouer marche/sprint, collision frontale avec un zombie, slide, saut et franchissement après redémarrage de Play.

Validation avec le MCP s&box, moteur 26.10.02 : 16 scénarios de franchissement/relâchement, 13 scénarios d'entrée et buffer, 8 cas de contact/chute, 5 cas de regroupement des impacts et 10 cas de limites/IK passent. Les os des mains sont mesurés contre leurs cibles réelles. Le slide passe quatre déclenchements avec maintien de posture après un tap, saut, obstacle, désactivation des contrôles et retour du FOV à sa valeur initiale.

Le verrou de combat passe 33 assertions dans les quatre états de traversal : permissions natives, entrées, appels directs sur arme et poings, suppression de l'ADS, annulation d'un rechargement, refus du hook de validation des tirs et rétablissement après sortie. Ce banc local ne remplace pas un test réseau sous latence.

Les rebords fins passent 12 scénarios supplémentaires : clôtures de 4/16/28 unités, confirmation manuelle vers Vaulting, maintien de Climbing sur un dessus large, relâchement définitif des mains, plafond, absence de réception, chute excessive, support retiré, obstacle ajouté, géométrie dynamique et option désactivée. Le mouvement passe 10 scénarios de physique réelle : arrêt progressif depuis 150, saut de sprint et deux sauts manuels à 300 conservés en l'air, saut de slide, air strafe, plafond de 420, impulsion externe de 500 conservée, collision murale, arrêt sur pente de 30° et marche native de 16 unités.

Après le nettoyage des fichiers partiels et des snapshots de support, ces 22 scénarios sont rejoués avec succès dans le moteur. La compilation finale passe sans erreur et sans avertissement dans les composants de mouvement modifiés.

La direction du déplacement est vérifiée dans le moteur sur 448 combinaisons : huit orientations horizontales, sept inclinaisons (dont ±90°), les quatre directions de déplacement, avec et sans le solveur d'inertie. Avant correction, 112 combinaisons échouaient ; après utilisation directe du yaw, les 448 passent.

Une session avec deux processus locaux vérifie propriétaire et proxy : état Hanging et prise répliqués, paramètres IK actifs sur les deux machines, impact envoyé par le propriétaire et santé confirmée sur l'hôte et le client. Deux rapports natifs de 60 au même instant ne retirent que 60. Les bancs et géométries temporaires sont retirés du code livré. Une validation sous forte latence et sur serveur dédié reste distincte de ces tests locaux.
