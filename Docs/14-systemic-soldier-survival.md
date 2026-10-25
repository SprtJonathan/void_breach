# Munitions, échange d'armes et survie des soldats

## Armes du joueur

Les ressources natives `BaseAmmoResource` sont désormais regroupées par catégorie :

| Catégorie | Ressource | Armes actuelles | Réserve maximale |
|---|---|---|---:|
| Small | `ammo/small.ammo` | USP | 60 |
| Medium | `ammo/medium.ammo` | MP5, P90 | 150 |
| Large | `ammo/large.ammo` | Futurs fusils d'assaut | 120 |
| Spéciale | `ammo/frag_grenade.ammo` | Grenade partagée joueur/PNJ | 4 |

Les anciens fichiers 9 mm et 5,7 mm restent disponibles pour d'anciens contenus ; les trois prefabs livrés utilisent les nouveaux pools. Aucun fusil d'assaut n'est ajouté par cette passe.

Le joueur commence avec les poings et le PDA, sans grenade ni réserve offerte. Le moteur effectue le ramassage automatique au contact (`PickupMode = Touch`). Une arme qui tient dans un emplacement libre et dans le budget de portage est ramassée et équipée. Un doublon fournit ses munitions via le comportement natif. Aucun ramassage automatique ne lâche une arme portée. **E** effectue uniquement un échange volontaire avec l'arme à feu active. Le transfert conserve les objets, les chargeurs et la réserve du joueur ; les contraintes sont vérifiées avant le lâcher. Un refus de l'ajout restaure l'ancienne arme lorsque possible. Les RPC réservés au propriétaire vérifient l'état du joueur, la distance, la scène et les obstacles. Une arme tenue par quelqu'un d'autre n'est pas transférable.

Maintenir **G** pendant 0,85 s lâche l'arme équipée. Le nom d'action est maintenant `Drop`, présent dans Input.config. La dispersion propre au soldat est retirée lorsqu'un joueur récupère son arme. Les pickups d'équipement conservent leur comportement natif.

## Chutes

`PlayerController.IEvents.OnLanded` fournit la distance de chute au composant de santé. Le joueur, les PNJ en déplacement et les ragdolls de PNJ utilisent `VBFallDamage`. Les contacts physiques restent actifs, mais les impacts natifs sur le sol aux pieds du joueur sont ignorés : ils ne doivent ni doubler ni remplacer le barème de hauteur par des dégâts fondés sur la vitesse. Les collisions avec un corps mobile, dont les véhicules, conservent leurs dégâts d'impact. La santé distante reste appliquée par l'hôte après un RPC du propriétaire.

Le barème du 17 octobre laisse les petits rebords et les sauts ordinaires sans pénalité, puis augmente progressivement la gravité des grandes chutes :

| Hauteur de chute (unités) | Dégâts absolus |
|---:|---:|
| Jusqu'à 192 | 0 |
| 256 | 10 |
| 384 | 20 |
| 512 | 50 |
| 768 | 100 |
| 1 024 | 200 |
| 1 280 | 300 |

Interpolation linéaire entre ces repères, sans saut de dégâts : 224 unités coûtent 5, 320 coûtent 15 et 640 coûtent 75. Au-delà de 1 280, la pente du dernier segment continue (+100 dégâts pour 256 unités supplémentaires). Il n'y a ni plafond de dégâts ni mise à mort automatique fondée sur MaxHealth. La chute de 180 unités est désormais sans dégâts. Le même nombre de dégâts s'applique indépendamment de l'espèce ou du maximum de vie de la victime.

Les dégâts portent le type Fall et traversent l'armure. Une chute en ragdoll utilise la perte de hauteur du centre de masse et ne facture qu'un atterrissage. Un nouveau coup pendant le ragdoll conserve la hauteur déjà parcourue. La politique de risque est séparée dans `VBSurvivalMath.CanRiskDrop` : un PNJ compare les dégâts absolus prévus à sa vie actuelle avant une descente volontaire. Le calcul des dégâts n'a aucune dépendance à la santé. La recherche de sol n'utilise plus l'inverse de l'ancien barème linéaire.

## Priorités et dangers perçus

La perte de vue déclenche une approche de la dernière position connue, conservée en mémoire pendant le trajet. Le soldat n'efface plus sa cible au début de cette approche et ne la remplace pas immédiatement par un point aléatoire. La recherche locale commence à l'arrivée ou après un délai de trajet borné ; sa durée propre commence alors. Les positions entendues en hauteur sont projetées sur leur support physique et l'arrivée utilise l'extrémité réelle du chemin. Un coup reçu derrière un obstacle ne remplace plus une observation connue par le point d'impact sur le corps du soldat.

Les grenades servent à déloger une menace récemment vue derrière un obstacle. Après 0,75 s d'occultation, le soldat peut viser une position perçue par le son ou une courte projection de sa dernière vitesse observée (0,6 s maximum). La mémoire visuelle expire après six secondes pour cette décision. Aucune position actuelle d'ennemi caché n'est consultée. Le candidat doit être masqué par le décor, avoir un sol, un arc dégagé et respecter la sécurité du lanceur/des alliés. Une cible exposée est combattue avec l'arme ; une réapparition pendant la préparation annule le lancer avant de consommer la grenade.

Vitesses livrées : patrouille 120, recherche 155, combat/esquive 200 unités/s. Une patrouille choisit un point au même niveau et n'engage pas une descente depuis sa plateforme pour rejoindre un point aléatoire inférieur. Sur une petite plateforme sans NavMesh, de courts déplacements physiques sont permis si le sol et le corps passent les vérifications communes.

Un agresseur qui vient de frapper à moins de `CloseThreatRange` (180 unités) reçoit une priorité supérieure au bonus joueur et à la conservation de cible. Le contact permet de le localiser derrière soi pendant deux secondes, sous réserve d'une ligne dégagée ; il ne révèle pas un ennemi derrière un mur. Le délai de découverte visuelle ne retarde pas cette réaction au contact.

`AvoidVisibleHazards` active une observation toutes les 0,15 s avec les yeux, le champ de vision et les traces d'occlusion existants. Une nouvelle grenade ou voiture demande 0,2 s de réaction. Une voiture doit être en mouvement et avoir une trajectoire de collision dans les 1,5 prochaines secondes. Les prédictions cachées partent uniquement de la dernière observation : mémoire véhicule 0,65 s, grenade jusqu'à sa mèche observée. Les grenades utilisent leur rayon réel et une marge de 64 unités.

Le soldat interrompt une frappe ou un lancer non libéré pour s'éloigner du danger, même hors couvert. Les routes de fuite sont évaluées sur la NavMesh, puis échantillonnées pour ne pas traverser une zone plus dangereuse. Trois ennemis proches localisés déclenchent également une tentative de dégagement avec tir de défense si possible. La décision est réévaluée ; elle ne rend pas le soldat invulnérable et une réaction peut arriver trop tard.

## Traversal dynamique

`VBTraversalGeometry` partage les recherches de mur/sommet, le calcul de position derrière le rebord, l'angle de pente et les balayages de corps avec le joueur. Le joueur conserve ses contrôles, sa stamina, ses captures en l'air et ses diagnostics. Le soldat utilise le volume du CharacterController natif.

Quand un chemin ne rejoint pas l'objectif, `UseDynamicTraversal` teste un franchissement local vers celui-ci et plusieurs angles voisins. Une route partielle permet d'approcher le côté accessible d'un rebord avant de revérifier le passage. La hauteur maximale par défaut est 112 unités. La destination doit supporter le corps et permettre la poursuite de l'objectif sur la NavMesh. Une succession de rebords peut être résolue par étapes si elle rapproche de l'objectif. Aucun lien ou repère de carte n'est créé ou requis pour ces rebords.

La NavMesh fournit la direction ; seul `VBNpcLocomotion` déplace physiquement un PNJ animé par le CharacterController natif, avec friction et gravité. L'agent ne copie jamais sa position projetée sur le personnage. Les passages natifs automatiques sont désactivés dans les prefabs et passent par le même exécuteur que les franchissements détectés. Celui-ci balaie le corps à chaque pas, garde ses phases répliquées et s'interrompt si le support disparaît/bouge ou si un obstacle ferme le passage. Les armes ne tirent pas pendant la montée. Une absence de support laisse agir la gravité, sans téléportation ni poussée ajoutée au bord. Une descente volontaire dangereuse annule la demande de déplacement et freine normalement ; elle ne supprime pas une chute déjà engagée.

Les zombies demandent un chemin natif complet et n'utilisent pas l'aire d'escalade. Les connecteurs de départ et d'arrivée sont vérifiés pour ne pas projeter un point à travers une paroi. Une route impossible respecte le délai de recalcul au lieu de relancer la recherche à chaque image. Les coûts sont en mètres : marche ×1, escalade ×2 (ressource native `navigation/npc_climb.navarea`). Les candidats de traversal comparent approche + franchissement + sortie au détour à pied complet.

En l'absence de fuite au même niveau, ou pour rejoindre un objectif inférieur inaccessible par le chemin ordinaire, le soldat peut quitter un rebord. La descente maximale est 320 unités et les dégâts estimés doivent rester sous la moitié de sa vie actuelle. Le palier et le trajet du corps doivent être libres. Le soldat atteint le bord puis le CharacterController et la gravité assurent la chute.

Limites : détection locale de rebords statiques avec une arrivée marchable, pas de reconnaissance générale d'échelles ni de recherche exhaustive d'un itinéraire de parkour à travers la carte. Les liens natifs explicites restent une option pour ces passages particuliers. Les poses, la récupération sur une géométrie étroite, les clients distants et les réglages sur toutes les cartes demandent encore des essais.

## Architecture et réseau

Le CharacterController possède le déplacement animé et la gravité ; l'agent natif fournit le chemin et l'évitement. Un seul exécuteur gère les franchissements détectés et les liens explicites. Les inventaires, armes, munitions, projectiles physiques et dégâts radiaux utilisent les composants natifs. Aucun graphe de navigation parallèle n'est ajouté.

L'hôte simule les PNJ et applique la santé. La vitesse réelle et l'état au sol sont répliqués aux autres pairs : leur contrôleur physique ne tourne pas, et ses valeurs locales ne peuvent donc pas servir aux animations. Les RPC d'échange et de tir du joueur sont réservés au propriétaire ; les présentations PNJ sont réservées à l'hôte. Les délais de grenade utilisent des TimeUntil répliqués et la position de lancer prévue est répliquée aussi. Les événements de santé sont abonnés sur chaque pair, avec traitement réservé à l'hôte, pour conserver ces réactions après un changement d'hôte. Le sommet de chute et la facturation d'atterrissage en ragdoll sont également répliqués.

La présentation au sol des armes ne réactive plus tous leurs composants physiques à chaque image : elle se réconcilie au démarrage, à l'activation et au changement d'appartenance à l'inventaire. Les contrôles de compatibilité des anciens inventaires restent limités à leur initialisation.

La friction native utilise explicitement un stopSpeed nul : sa valeur native par défaut (140) annulait presque chaque pas de marche avant l'accélération. Un spawn exactement posé sur le sol peut recevoir les deux unités de marge verticale du corps, uniquement si le sol est marchable et le volume relevé libre. Un chevauchement avec un mur ou un véhicule n'engage jamais de recherche aléatoire de position latérale.

## Croisements et corps au sol

La locomotion physique applique désormais `NavMeshAgent.Velocity`. `WishVelocity` désigne la direction du chemin avant l'évitement des voisins et des murs ; l'utiliser annulait ces corrections natives. Les vivants restent solides et contournent les autres agents avec le solveur s&box existant. Aucun second système de foule ou de téléportation n'est ajouté.

Les tags `npc`, `zombie` et `npc_body` ignorent `npc_ragdoll` et `player_ragdoll` dans la matrice de collision, comme le joueur. Les corps conservent leurs collisions avec le décor. Tout PNJ en ragdoll quitte aussi la simulation de foule, même pendant une chute récupérable ; l'agent reprend son état initial lorsqu'il se relève.

`VBNpcCrowdRegression` a été exécuté sur un serveur dédié séparé, sans contrôle de l'éditeur : **11 contrôles réussis, zéro échec** (`tmp/crowd-fixed-native.log`). Le scénario vérifie les règles de collision des deux types de PNJ, le contournement d'un soldat immobile dans une porte de 112 unités, le croisement dans les deux sens avec des corps vivants solides, et le retrait/restauration de l'agent lors d'un knockdown. Cela vérifie l'évitement local ; une sortie entièrement bouchée reste physiquement impraticable.

Après cette correction, `VBNpcMovementRegression` a été réexécuté : **17 contrôles réussis, zéro échec**, notamment détour du zombie autour d'un mur, arrivée du soldat à la dernière position vue, vitesse de marche, freinage et chutes (`tmp/movement-crowd-fixed-native.log`). La compilation principale et celle du nouveau harness réussissent avec les 12 avertissements existants. Les validateurs de survie et d'équilibrage passent. Le validateur général des instances de soldat signale actuellement une suppression de prefab obsolète dans `basic_example.scene` ; cette passe ne modifie pas cette scène en cours d'édition.

## Ancienne scène SoldierTestMap

Un ancien enregistrement de cette scène contient `[null]` dans `NetworkHelper.SpawnPoints`, un objet de spawn sans composant `SpawnPoint` et aucune caméra. L'exception se produit dans `NetworkHelper.FindSpawnLocation`, avant la création du joueur. Les sauvegardes d'un onglet déjà ouvert peuvent réintroduire ces données après une correction du fichier.

Le premier correctif `VBSoldierTestSceneSetup` a été retiré : sa recherche d'une caméra quelconque ne garantissait pas une vue utilisable et dépendait d'identifiants de cette carte. Le prefab joueur porte `VBPlayerCamera` et ses réglages ; ce composant crée la caméra native localement pour le propriétaire. Il ne sérialise aucune référence vers ce composant exclu des snapshots. Le placement, le FOV et les vues première/troisième personne restent assurés par `PlayerController` natif. Une scène n'a plus besoin de fournir la caméra du joueur.

`VBNetworkSceneSetup` traite séparément les références nulles de tous les `NetworkHelper` à l'événement `AfterLoad`, après résolution des références et avant le callback réseau natif. Il conserve les points valides et le mécanisme de repli natif `SpawnPoint`. Il ne crée aucun point à une position devinée : une carte doit toujours configurer ses points de spawn ou placer des composants `SpawnPoint` natifs.

Le premier serveur de validation avait chargé la scène compilée contenant réellement le point nul et aucune caméra (`tmp/spawn-fixed-native.log`). Ce résultat validait la création du joueur, sans prouver que toute caméra existante dans la session de l'éditeur permettait un rendu correct. Le nouveau scénario `VBPlayerCameraRegression` vérifie directement la caméra intégrée au prefab et le contrôleur natif.

## Vérification de cette passe

`Tests/AI/run_survival_checks.ps1` compile vers `tmp/`, vérifie les ressources/input et exécute les assertions sur le code de production (paliers de chute, interpolation, continuité, monotonie, valeurs invalides, armure, décisions de risque, voiture, grenade et coûts de déplacement). Aucune prise de contrôle de l'ordinateur.

La passe a réellement exécuté quatre scénarios dans le moteur installé, sur un serveur dédié distinct, caché et sans contrôle de l'éditeur ou du personnage humain : **66 assertions réussies, zéro échec**. Le projet de test, sa scène compilée et son bootstrap restent dans `tmp/native-survival-115503/`, séparés du code distribué. Le journal final est `tmp/organic-final-native.log`.

- `VBNpcMovementRegression` : 17 contrôles, dont chute mortelle évitée, chute accidentelle facturée, descente de 180 unités sans dégâts, friction, initialisation au sol, vrai détour physique du zombie et approche de la dernière position vue autour d'un mur.
- `VBSystemicSoldierRegression` : 20 contrôles, dont aucune grenade au spawn, ramassage via le balayage physique natif, doublon de munitions, pickup de munitions natif, échange volontaire, conservation des chargeurs/réserves, contacts d'atterrissage, agresseur proche et détection géométrique du rebord.
- `VBSoldierRegression` : 17 contrôles, dont factions, priorités de menace, tir natif, occultation, grenade contre un ennemi récemment vu derrière un obstacle, sécurité alliée, plafond bloquant les arcs et dégâts radiaux occultés.
- `VBDynamicRouteRegression` : 12 contrôles, avec arrivée réelle sur une plateforme de 96 unités sans lien, préférence pour un détour court, préférence pour franchir quand le détour est long, sans téléportation du personnage.

La compilation principale réussit avec zéro erreur et les 12 avertissements existants (HUD obsolète et temporisations de migration). Les 38 assertions de calcul et les trois validateurs de ressources passent aussi. `VBNpcNetworkRegression`, enrichi pour vérifier la vitesse et l'état au sol sur un client distant, a été compilé. **Il n'a pas été exécuté avec un second client** ; ni le transport réel entre deux pairs ni la migration d'hôte ne sont revendiqués comme validés. Un redémarrage de Play est nécessaire pour évaluer les prefabs et inventaires fraîchement instanciés.
