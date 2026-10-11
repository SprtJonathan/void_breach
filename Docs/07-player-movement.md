# Déplacements du joueur

Le prefab `Assets/player/player controller.prefab` conserve le `PlayerController`, son Rigidbody et les modes Swim/Ladder. `VBMoveModeWalk` dérive de Walk et garde ses collisions, marches, saut et accroupissement. Traversal prend la main uniquement pendant une action manuelle validée. Priorités : Walk 1, Traversal 2, Ladder 5, Swim 10.

## Inertie, sprint et slide

Le prefab utilise marche 150, sprint 300 et accroupissement 70 unités/s. Le lissage natif reste actif : `AccelerationTime=0.12`, `DeaccelerationTime=0.09`, `BrakePower=0.15`. `DirectionResponseTime=0.055` et `ReversalResponseTime=0.11` adoucissent les changements de direction sans retarder la visée.

La transition marche/sprint utilise une courbe progressive : `SprintAccelerationTime=0.15`, `SprintDecelerationTime=0.35`, `SprintCurveStrength=0.75` dans le prefab. Une force de zéro donne une rampe linéaire ; des temps de zéro retrouvent la cible native immédiate. La stamina peut toujours refuser le sprint.

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
- Suspendu : relâcher `jump` pendant au moins 0.12 s, puis presser à nouveau pour monter. Les appuis pendant le rattrapage ne déclenchent pas une montée immédiate.
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
| VaultForwardDistance / VaultMaximumDrop | 80 / 80 | Recherche de réception derrière l'obstacle |
| VaultDuration / ClimbDuration | 0.45 / 0.65 s | Durées des mouvements |
| Cooldown / JumpInputBuffer | 0.35 / 0.25 s | Délai après fin / mémoire d'un appui |
| TraversalStaminaCost / HangDrainPerSecond | 8 / 5 | Départ / consommation par seconde suspendu |
| MaximumHangTime | 0 | Pas de timeout dans le prefab ; la stamina limite l'accroche |
| MinimumCatchHoldTime | 0.25 s | Réserve de stamina après le coût initial |

Avec JumpSpeed=300 et la gravité actuelle de 850, le saut natif monte d'environ 51 unités. Une structure de 120 reste accessible avec le bon timing ; 128 demande un départ surélevé. Le plafond réel dépend du saut, de la gravité, de la posture et de la géométrie. Les limites de hauteur sont strictes : la marge des traces ne les étend pas.

La détection exige une face presque verticale, une approche frontale, un dessus praticable, un support sous les quatre coins et un trajet dégagé. Le mouvement suit trois segments (monter, passer au-dessus, descendre), avec des requêtes aux dimensions du capsule et des pieds natifs. Aucune collision n'est désactivée. Les chemins sont revérifiés pendant la physique ; un support retiré, désactivé, déplacé ou une obstruction nouvelle provoque le relâchement. Les structures mobiles ne sont pas prises en charge.

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

La prise reste fixe dans le monde pendant la suspension. Les mains lâchent progressivement pendant la montée et à la fin de l'action. Les paramètres sont nettoyés lors d'une désactivation, destruction ou changement de renderer. En première personne, `CameraPerspectiveTagFilter` révèle le corps local et masque sa tête et les accessoires de tête dans les objets de rendu locaux ; les propriétés du modèle et les snapshots réseau sont conservés. Le viewmodel tenu retrouve ensuite sa visibilité.

Cela donne une pose de bras fonctionnelle, mais ne remplace pas une animation complète du torse, des jambes et des doigts. Un modèle personnalisé doit exposer ces os et chaînes IK. Les modèles Citizen natifs utilisent une dernière option Head vide pour le masquage local.

## Multijoueur, dégâts et debug

Seul le propriétaire lit les entrées et simule déplacement, slide et stamina. `IsSliding`, l'état Traversal et son `Grip` sont répliqués. La prise est envoyée au début de l'action ; chaque client calcule ses mains localement. Les effets caméra restent locaux et les serveurs dédiés n'évaluent pas cette présentation.

La santé reste contrôlée par l'hôte. Les impacts Rigidbody natifs sont calculés par le propriétaire, regroupés par source et instant de simulation, puis transmis via un RPC réservé au propriétaire. Plusieurs formes du corps ne facturent que le plus fort contact, avec seulement l'incrément si le suivant est plus fort. Sources distinctes et autres dégâts restent indépendants. Les impacts utilisent le type Blunt. Cette transmission suit l'autorité physique native du propriétaire ; elle n'est pas une validation physique serveur indépendante.

Dans Traversal → **Debug**, `EnableTraversalDebug` active le feedback de test et les traces ; il est désactivé dans le prefab. `DebugDrawTraces`, `DebugIgnoreDepth`, `DebugTraceDuration` règlent le dessin des rayons, capsule et pieds réellement utilisés. Vert : requête passée ; rouge : refus ; cyan : portée testée. Le panneau indique la raison, le volume en collision, la fenêtre de hauteur, le buffer et la dernière transition. Un rebord détecté ne garantit pas une prise : réception, chemin, stamina et support doivent également passer.

## Vérifications dans l'éditeur

Validation avec le MCP s&box, moteur 26.10.02 : 16 scénarios de franchissement/relâchement, 13 scénarios d'entrée et buffer, 8 cas de contact/chute, 5 cas de regroupement des impacts et 10 cas de limites/IK passent. Les os des mains sont mesurés contre leurs cibles réelles. Le slide passe quatre déclenchements avec maintien de posture après un tap, saut, obstacle, désactivation des contrôles et retour du FOV à sa valeur initiale.

Une session avec deux processus locaux vérifie propriétaire et proxy : état Hanging et prise répliqués, paramètres IK actifs sur les deux machines, impact envoyé par le propriétaire et santé confirmée sur l'hôte et le client. Deux rapports natifs de 60 au même instant ne retirent que 60. Les bancs et géométries temporaires sont retirés du code livré. Une validation sous forte latence et sur serveur dédié reste distincte de ces tests locaux.
