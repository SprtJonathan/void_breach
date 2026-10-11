# Déplacements du joueur

Le prefab `Assets/player/player controller.prefab` conserve le `PlayerController`, son Rigidbody et les modes Swim/Ladder. `VBMoveModeWalk` dérive de Walk et garde ses collisions, marches, saut et accroupissement. Traversal prend la main uniquement pendant une action manuelle validée. Priorités : Walk 1, Traversal 2, Ladder 5, Swim 10.

Le calcul d'inertie, de direction et de transition de sprint est regroupé dans `VBMoveModeWalk.Momentum.cs`. La détection, les réceptions et les trajets de traversal sont dans `VBMoveModeTraversal.Detection.cs`, avec un même type de snapshot pour surveiller le rebord et la réception. Les diagnostics restent dans `.Debug.cs`. Ces fichiers partiels constituent les mêmes composants ; les propriétés du prefab et les états répliqués restent sur leurs types existants.

## Inertie, sprint et slide

Le prefab utilise marche 150, sprint 300 et accroupissement 70 unités/s. Le lissage de la cible native reste actif : `AccelerationTime=0.12`, `DeaccelerationTime=0.09`. `DirectionResponseTime=0.055` et `ReversalResponseTime=0.11` adoucissent les changements de direction sans retarder la visée.

`EnableMomentumMovement=true` remplace l'accélération et le freinage du mode Walk par une friction au sol et une accélération projetée sur la direction demandée, inspirées du [mouvement Source de Valve](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/shared/gamemovement.cpp). Les colliders, les marches, le saut et la gravité en l'air restent natifs. Le drag Rigidbody et la friction des pieds sont retirés uniquement pendant ce mouvement, pour éviter un double freinage. Sur un sol statique praticable, la gravité du corps est suspendue et le grounding natif suit la surface, pour pouvoir s'arrêter en pente ; elle reprend dès la perte du sol. Les supports dynamiques conservent la gravité. Désactiver l'option retrouve le solveur natif et son `BrakePower=0.15`.

| Réglage | Valeur du prefab | Effet |
|---|---:|---|
| GroundAcceleration | 10 | Accélération au sol, proportionnelle à la vitesse demandée |
| GroundDrag / GroundStopSpeed | 5.5 / 60 | Freinage progressif avec arrêt complet à basse vitesse |
| AirAcceleration / AirWishSpeed | 10 / 30 | Accélération en l'air ; le plafond concerne la projection dans la direction d'entrée |
| MaximumAirSpeed | 420 | Plafond de vitesse gagnée par les air strafes ; une impulsion externe plus rapide est conservée |

Relâcher le sprint, les touches de déplacement ou s'accroupir en l'air ne ramène pas la vitesse horizontale à celle de la marche. Les air strafes permettent de gagner de la vitesse en orientant les entrées ; des sauts manuels enchaînés peuvent la conserver. Aucune impulsion gratuite ni saut automatique n'est ajouté. Revenir au sol applique le freinage ; les collisions réduisent réellement la vitesse et aucun ancien vecteur ne la réinjecte après un choc.

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

Seul le propriétaire lit les entrées et simule déplacement, slide et stamina. `IsSliding`, l'état Traversal et son `Grip` sont répliqués. La prise est envoyée au début de l'action ; chaque client calcule ses mains localement. Les effets caméra restent locaux et les serveurs dédiés n'évaluent pas cette présentation.

La santé reste contrôlée par l'hôte. Les impacts Rigidbody natifs sont calculés par le propriétaire, regroupés par source et instant de simulation, puis transmis via un RPC réservé au propriétaire. Plusieurs formes du corps ne facturent que le plus fort contact, avec seulement l'incrément si le suivant est plus fort. Sources distinctes et autres dégâts restent indépendants. Les impacts utilisent le type Blunt. Cette transmission suit l'autorité physique native du propriétaire ; elle n'est pas une validation physique serveur indépendante.

Dans Traversal → **Debug**, `EnableTraversalDebug` active le feedback de test et les traces ; il est désactivé dans le prefab. `DebugDrawTraces`, `DebugIgnoreDepth`, `DebugTraceDuration` règlent le dessin des rayons, capsule et pieds réellement utilisés. Vert : requête passée ; rouge : refus ; cyan : portée testée. Le panneau indique la raison, le volume en collision, la fenêtre de hauteur, le buffer et la dernière transition. Un rebord détecté ne garantit pas une prise : réception, chemin, stamina et support doivent également passer.

## Vérifications dans l'éditeur

Validation avec le MCP s&box, moteur 26.10.02 : 16 scénarios de franchissement/relâchement, 13 scénarios d'entrée et buffer, 8 cas de contact/chute, 5 cas de regroupement des impacts et 10 cas de limites/IK passent. Les os des mains sont mesurés contre leurs cibles réelles. Le slide passe quatre déclenchements avec maintien de posture après un tap, saut, obstacle, désactivation des contrôles et retour du FOV à sa valeur initiale.

Le verrou de combat passe 33 assertions dans les quatre états de traversal : permissions natives, entrées, appels directs sur arme et poings, suppression de l'ADS, annulation d'un rechargement, refus du hook de validation des tirs et rétablissement après sortie. Ce banc local ne remplace pas un test réseau sous latence.

Les rebords fins passent 12 scénarios supplémentaires : clôtures de 4/16/28 unités, confirmation manuelle vers Vaulting, maintien de Climbing sur un dessus large, relâchement définitif des mains, plafond, absence de réception, chute excessive, support retiré, obstacle ajouté, géométrie dynamique et option désactivée. Le mouvement passe 10 scénarios de physique réelle : arrêt progressif depuis 150, saut de sprint et deux sauts manuels à 300 conservés en l'air, saut de slide, air strafe, plafond de 420, impulsion externe de 500 conservée, collision murale, arrêt sur pente de 30° et marche native de 16 unités.

Après le nettoyage des fichiers partiels et des snapshots de support, ces 22 scénarios sont rejoués avec succès dans le moteur. La compilation finale passe sans erreur et sans avertissement dans les composants de mouvement modifiés.

Une session avec deux processus locaux vérifie propriétaire et proxy : état Hanging et prise répliqués, paramètres IK actifs sur les deux machines, impact envoyé par le propriétaire et santé confirmée sur l'hôte et le client. Deux rapports natifs de 60 au même instant ne retirent que 60. Les bancs et géométries temporaires sont retirés du code livré. Une validation sous forte latence et sur serveur dédié reste distincte de ces tests locaux.
