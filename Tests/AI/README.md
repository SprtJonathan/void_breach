# Validation du soldat

## Continuité du combat en couloir — 25 octobre 2026

`VBSoldierEngagementRegression.cs` conserve les tactiques, couverts et délais
livrés. Seuls la ronde initiale et les dégâts subis sont neutralisés pour
isoler l'engagement. Les cibles possèdent une faction joueur, une santé et un
corps physique ; les munitions, projectiles et dégâts restent natifs.

Le scénario principal dure 24 secondes avec cinq soldats, dans un couloir de
288 unités de largeur et 136 unités sous plafond. Il mesure les périodes sans
tir réel, la part de déplacement, l'excursion maximale, les destinations de
manœuvre, les tirs accroupis et les grenades de pression. Il exige le maintien
des plafonds de tir et de grenade. Deux autres cas occultent un contact après
son acquisition : un refuge profond sans sortie immédiate, puis une cloison
avec un détour local. Un allié près de la cible interdit enfin la grenade ;
son départ permet un véritable arc plat sous le plafond.

**20 contrôles passent** dans
`tmp/soldier-engagement-native/stdout-verified.log`, avec zéro erreur de boucle.
Le couloir mesure 17 % de déplacement et 1,40 seconde de silence maximal ;
le soldat du refuge reprend contact en 7,05 secondes et recommence à tirer.
`VerifyCloseRange()` ajoute deux contrôles, dont le tir réellement accroupi et
les dégâts sur une cible à 230 unités (`stdout-close.log`).

Le même scénario est exécuté avec les sources sauvegardées avant cette passe,
dans `tmp/soldier-engagement-baseline-native/stdout-refuge-baseline.log`.
Cette comparaison échoue sur cinq assertions : destination utile, grenade
contre le joueur immobile, refuge aveugle, reprise du tir depuis ce refuge et
arc plat après départ de l'allié. L'ancien soldat reste sans réacquisition ni
tir pendant les quinze secondes du refuge ; le nouveau reprend le combat.
Ces échecs attendus appartiennent à la copie de comparaison, pas aux sources
livrées. Les tirages de NavMesh restent variables : les métriques décrivent
ces exécutions, pas une garantie de ratio sur toute carte.

`VBSoldierMapEngagementRegression.cs` charge aussi les VPK réels de
`maps/test_map.vmap` (basic_example, extérieur) et `maps/interior_testmap.vmap`
(soldier_test, intérieur). Il génère leur navigation puis trouve une ligne
réelle, sous plafond inférieur à 200 unités pour la seconde carte. **Dix
contrôles passent** dans
`tmp/soldier-map-engagement-native/stdout-maps-verified.log` : 21 et 20 tirs,
silences maximaux de 1,93 et 2,65 secondes, tirs accroupis et grenade observée
dans les deux géométries. Il s'agit des cartes compilées ; les surcharges de
scène, objets ajoutés dans l'éditeur, rendu des animations et transport avec
un second client restent à vérifier dans les scènes Play réelles.

Les 161 contrôles antérieurs sont réexécutés dans `stdout-full.log` avec le
premier scénario de continuité : zéro échec et journal d'erreurs vide. Les
seize contrôles de couvert sont également repris dans `stdout-verified.log`
après la limite finale de longueur des chemins. Le test du refuge attend
désormais le refus d'un abri aveugle chez un soldat sain ; le test de sortie
de posture rapproche le joueur à 80 unités pour franchir le nouveau seuil.
Compilation principale : zéro erreur, douze avertissements existants. Les
validateurs d'inventaire et de survie passent. Le contrôle de tenue à sept
pièces reste indépendant de ces modifications de code.

## Perception et alerte d'escouade — 25 octobre 2026

`VBSoldierAwarenessRegression.cs` se compile uniquement dans une copie isolée
du projet et expose `Verify()` au bootstrap de test. Ses **32 contrôles passent**
dans `tmp/soldier-awareness-native/stdout-awareness-final.log`, avec un journal
d'erreurs vide. La dernière version ajoute un contrôle du capteur suspendu aux
31 contrôles initiaux.

Les cas couvrent le contact latéral à courte portée et l'orientation pendant
l'acquisition, l'angle mort arrière silencieux, le cône distant, les murs et
`notarget`, les échantillons de tête/torse avec une tête accroupie réellement
masquée, la surveillance d'un espace dégagé au lieu d'un mur, les changements
lents de secteur et la limite de rotation. La mesure cumule le déplacement
angulaire sur des fenêtres d'au moins 0,25 seconde, avec une marge d'une frame
de bord : les continuations de `Task.Frame()` peuvent échantillonner de part
et d'autre de la mise à jour du cerveau. Une différence isolée entre deux
frames ne fournit donc pas une mesure stable de sa vitesse de rotation.

Un témoin voit un joueur, un voisin tournant le dos finit par le confirmer
lui-même, et un troisième interrompt une vraie marche directe de ronde tout
en restant sans vision derrière un mur. Les assertions vérifient le délai de
transmission, sa portée et sa faction, l'absence de vision ou de tir autorisé
par radio, l'absence de suivi d'un déplacement caché et de relais infini, les
reports périmés et invalides, la résistance aux tirs alliés et la reprise
d'une observation personnelle. Une NavMesh générée autour de murs vérifie
ensuite un seul investigateur mobile par joueur, un second budget indépendant
et le remplacement d'un investigateur désactivé.

La première passe complète a révélé une exception dans le nettoyage des
réservations d'investigation lorsque le scénario de déplacement suspend son
capteur visuel. La consultation inclut désormais les composants désactivés et
vérifie leur validité ; un contrôle appelle directement cette réservation avec
le capteur suspendu pour empêcher qu'une exception de boucle passe inaperçue
malgré la réussite d'une assertion de position.

Les dernières exécutions totalisent **161 contrôles réussis** : les 32 de
perception ci-dessus, les 17 de déplacement/recherche dans `stdout-verified.log`
(sans exception), puis 22 tir accroupi, 18 tempo, 16 couvert, 16 coordination,
17 combat, 11 croisements et 12 choix de route dans `stdout-full.log` du même
dossier. Le serveur isolé est arrêté après les essais.

Les soldats de ces fixtures sont protégés des impacts afin d'isoler leurs
décisions ; le harness de combat conserve ses tirs et dégâts natifs. Les tests
utilisent des acteurs cibles de faction joueur, sans piloter le personnage
humain. Le rendu complet, la carte montrée dans les captures et le transport
avec un véritable second client ne sont pas validés par cette passe dédiée.

La compilation principale réussit avec zéro erreur et douze avertissements
existants. Les validateurs d'inventaires de soldats et de ressources de survie
passent. Le validateur d'équilibrage signale toujours la tenue actuelle de six
pièces alors que son assertion en attend sept ; les profils de dégâts
précédant cette assertion passent. Cette passe ne modifie aucun prefab,
vêtement, arme ou réglage de vitesse.

## Tir accroupi stabilisé — 25 octobre 2026

`VBSoldierFiringStanceRegression.cs` exerce la posture sur serveur dédié, dans
une copie isolée du projet. Ses **22 contrôles passent**, avec tirs réels de
M4A1 : décision après observation, transition sans bonus ni tir, stabilisation,
dispersion native réduite de 35 %, un tireur accroupi par joueur et budgets
indépendants, hauteur physique/yeux/canon, paramètre Human, consommation et
dégâts, annulation par recharge/proximité/déplacement/mort, remplacement du
tireur et absence de bonus sur l'arme lâchée. Un obstacle bas impose le tir
debout si le canon accroupi est bloqué. Une sortie latérale réelle de couvert
permet le tir accroupi puis le retour à l'abri au terme de l'exposition.

Journal final : `tmp/soldier-firing-stance-native/stdout-stance-verified.log`.
La fixture de couvert laisse le soldat observer sa cible avant de poser le
mur, puis génère sa NavMesh ; sa première version cachait la cible dès le
spawn et ne pouvait donc pas exercer une sortie de combat.

Les autres dernières exécutions totalisent **107 contrôles réussis** :
18 tempo, 16 couvert et 17 combat dans `stdout-final.log` du même dossier ;
16 coordination, 11 croisements, 12 choix de route et 17 déplacement/perception
dans `stdout.log`. L'assertion de pause entre rafales mesure désormais la fin
réelle de la rafale plutôt que trois cartouches supposées : la M4A1 actuelle
utilise deux à trois cartouches. Le test de combat général désactive cette
nouvelle posture pour isoler sa cadence ; le nouveau harness la couvre.

Compilation principale : zéro erreur, douze avertissements existants.
Le validateur de survie passe. Deux validateurs de ressources signalent des
écarts antérieurs à cette modification : une suppression de GUID périmée dans
`soldier_test.scene`, et une tenue actuelle de six pièces alors que le contrôle
d'équilibrage en exige sept. Aucun prefab, tenue ou choix d'arme n'a été modifié
par cette passe. Le serveur de test est arrêté après les essais. Ces contrôles
valident les paramètres d'animation et la physique, mais le rendu visuel et
le transport vers un véritable second client restent à observer en Play.

## Rythme posé et attention persistante — 21 octobre 2026

`VBSoldierTempoRegression.cs` exerce cinq soldats avec le profil livré, sur
une NavMesh isolée. Le scénario vérifie le chargement effectif des vitesses
65/85/115, le temps d'observation initial, une seule manœuvre volontaire par
joueur, le passage de cette permission à un autre soldat, les pauses après
déplacement, les plafonds physiques de vitesse et de rotation et la pause
réelle entre rafales consommant des cartouches.

Il vérifie aussi l'attention périphérique sur un adversaire déjà vu, la
persistance de l'identité et de la visée après plus de deux secondes derrière
un mur, l'absence de suivi d'une position cachée, le temps d'observation avant
investigation, la réacquisition du même contact et les phases physiques de
démarrage/freinage. Le cerveau est désactivé avant l'essai séparé du moteur,
avec un délai laissant son nettoyage de route se terminer.

Les tests de géométrie gardent des réglages accélérés explicites quand leur
objectif est de valider un chemin ; le scénario de tempo conserve le rythme
de production. Les assertions de recherche ont été adaptées à la pause de
contact et aux délais de marche plus longs, tout en exigeant l'arrivée réelle
au dernier point observé. Les fixtures restent hors du code distribué.

Dernière passe : **107 contrôles réussis, zéro échec** sur serveur dédié dans
`tmp/soldier-tempo-native/stdout-verified.log` : 18 tempo, 16 couvert,
16 coordination, 17 combat, 11 croisements, 12 choix marche/franchissement
et 17 déplacement/perception. Le test de tempo protège ses cinq soldats des
dégâts pour isoler les décisions et rotations des impacts fortuits ; les
dégâts sont exercés séparément par le harness de combat. Le démarrage physique
mesuré atteint 24 unités/s après 0,1 seconde, avec l'accélération de 240.
Compilation : zéro erreur, douze avertissements existants ; les trois
validateurs de ressources passent. Le ressenti visuel et le transport avec
un véritable second client restent à contrôler en Play. Relancer Play pour
instancier les nouvelles valeurs du prefab.

## Abri bas et soutien actif — 21 octobre 2026

`VBSoldierCoverRegression.cs` s'exécute uniquement dans une copie isolée du
projet. Son composant expose `Verify()` au bootstrap de test ; les méthodes
partielles de contrôle restent hors du code distribué. Le scénario génère sa
NavMesh et ses obstacles puis restaure la navigation et le plafond de tir.

Il exerce seize contrôles : préférence d'un abri accroupi à un franchissement,
hauteur du contrôleur et de la capsule, hauteur des yeux, `DuckLevel` Human,
refus de se relever sous un obstacle, retour à la posture debout après retrait
physique de cet obstacle, recharge d'un demi-chargeur à l'abri, mémoire après
occultation par son propre couvert, réservations et destinations distinctes,
abri sans sortie de tir et détour conservé hors de vue. Le déplacement caché
de la cible ne doit pas mettre à jour sa dernière position connue.

La passe native se trouve sous `tmp/soldier-support-native/`. Les autres
harnesses de coordination, combat, croisements et choix marche/franchissement
s'exécutent dans la même copie. Ces contrôles physiques et de paramètres
d'animation ne remplacent pas l'inspection du rendu en Play ni une connexion
avec un véritable deuxième client.

Dernière passe sur serveur dédié : **72 contrôles réussis, zéro échec** dans
`tmp/soldier-support-native/stdout-verified.log` : 16 couvert/soutien,
16 coordination, 17 combat, 11 croisements et 12 marche/franchissement.
Compilation principale : zéro erreur, douze avertissements existants.
Les trois validateurs de ressources passent également.

## Coordination par joueur et locomotion — 21 octobre 2026

`./Tests/AI/run_attack_slot_checks.ps1` exécute onze contrôles autonomes du
budget de tir : plafonds, indépendance de deux joueurs, rotation sans famine,
ordre de mise à jour inversé sur 500 images, libération, expiration, changement
de cible et réduction du plafond. Les onze contrôles passent.

`VBSoldierCoordinationRegression.cs` se compile uniquement dans un projet de
test isolé et fournit `vb_soldier_coordination_regression`. Ses méthodes
partielles de contrôle ne doivent pas être ajoutées au code distribué. Le test
crée deux cibles de faction joueur et six soldats, mesure la consommation
réelle des chargeurs à chaque image, vérifie la rotation et le plafond zéro,
puis exerce les déplacements physiques latéraux/arrière et leurs paramètres
Human. Une NavMesh temporaire permet également d'exercer le déplacement
tactique sans créneau de tir. Les réglages de navigation et de coordination
sont restaurés en fin de test. Les contrôles de grenades couvrent la préparation
exclusive, son annulation et un projectile dont le lanceur a disparu.

Dernière exécution : **16 contrôles réussis, zéro échec** dans
`tmp/soldier-coordination-native/stdout-final-support.log`. Le déplacement sans
créneau utilise un soldat neuf après génération de la NavMesh ; les tests de
pas latéraux/arrière pilotent séparément leur acteur avec le mover natif.
Les 17 contrôles existants de `VBSoldierRegression` passent également dans
`tmp/soldier-coordination-native/stdout-elevated.log`.

La passe utilise un serveur dédié sans interface et une copie sous
`tmp/soldier-coordination-native/`. Elle ne contrôle aucun joueur humain.
Le scénario à deux cibles exerce les budgets de deux joueurs sur l'hôte ;
il ne valide pas le transport réseau avec deux véritables clients. Les poses
finales, le rendu des pivots et la migration d'hôte demandent encore un contrôle
visuel et multijoueur. Les trois validateurs de ressources passent ; la
compilation principale conserve zéro erreur et douze avertissements existants.

## Dernière passe — moteur dédié sans interface, 17 octobre

66 assertions exécutées dans le moteur installé, zéro échec :

| Scénario | Assertions | Couverture |
|---|---:|---|
| `VBNpcMovementRegression` | 17 | Sol initial, marche, freinage, bords/chutes, détour zombie, dernière position vue |
| `VBSystemicSoldierRegression` | 20 | Loadout sans grenade, vrai balayage de pickup, munitions, échange, atterrissage, agresseur et rebords |
| `VBSoldierRegression` | 17 | Factions, priorités, tir, mémoire, grenade sur cible occultée et sécurité |
| `VBDynamicRouteRegression` | 12 | Montée sans lien et choix marche/franchissement selon le coût total |

Journal : `tmp/organic-final-native.log`. Projet isolé et bootstrap :
`tmp/native-survival-115503/`. Le serveur de test a été arrêté après la passe.
Le lancement local utilise le mécanisme documenté de serveur dédié
([Facepunch](https://sbox.game/dev/doc/networking/dedicated-servers/running-local-projects)),
avec une copie des sources et ressources, et une scène déjà compilée.
Aucune fenêtre ni entrée du joueur humain n'est pilotée.

Compilation principale : zéro erreur, 12 avertissements existants. Trois
validateurs de ressources et 38 assertions de calcul passent.
Les harnesses réseau et présentation sont compilés séparément sans ajout
de composants de test dans le code distribué. `VBNpcNetworkRegression` exige
un vrai second client ; sa dernière version n'a pas été exécutée. Les essais
ci-dessus ne valident pas le transport réseau ni une migration d'hôte.

Les notes plus anciennes ci-dessous conservent le contexte des premières passes ;
les scénarios exécutés ci-dessus remplacent leur statut « compilé seulement ».

## Munitions et survie systémique — 17 octobre

`./Tests/AI/run_survival_checks.ps1` compile vers `tmp/`, exécute les trois
validateurs de ressources et les assertions de `SurvivalMathChecks.cs`
contre l'assembly de production. Ces contrôles passent sans ouvrir ni piloter
l'éditeur.

Le barème de chute commun est désormais indépendant de MaxHealth : 192 unités
sans dégâts, puis 256/10, 384/20, 512/50, 768/100, 1024/200 et 1280/300.
L'interpolation est continue et la dernière pente se poursuit au-delà.
Les contrôles couvrent aussi les limites, les valeurs invalides et la politique
de risque séparée de l'IA. Le harness systémique vérifie les contacts natifs
avant/après l'atterrissage ; celui de déplacement utilise un rebord dangereux
de 768 unités et un rebord sans dégâts de 180. Ces deux harnesses ont été
exécutés dans la dernière passe moteur décrite ci-dessus.

`VBSystemicSoldierRegression.cs` fournit `vb_systemic_soldier_regression`
après copie temporaire sous Code/AI : échange P90/MP5, chargeurs et réserve
conservés, drop, dégâts d'atterrissage, agresseur arrière et mur d'occlusion,
rebord sans NavMeshLink et plafond bloquant. Son exécution et les déplacements
complets passent dans la dernière passe dédiée. Les clients distants restent
à vérifier. Les validations Play plus anciennes ci-dessous ne valident pas
automatiquement les ajouts ultérieurs.

Les sections datées détaillent chaque passe. Les validations Play historiques
concernent les comportements présents au moment de leur exécution ; elles ne
valident pas automatiquement les ajouts de survie systémique ci-dessus.

`python Tests/AI/validate_combat_balance.py` vérifie les profils réels de PV, les seuils tête/torse avec USP/P90/MP5, les délais de détection/visée, les limites de rafales et la tenue native. `VBCombatBalanceRegression.cs`, copié temporairement dans `Code/AI`, ajoute `vb_combat_balance_regression` pour exercer les dégâts natifs et le rythme de tir en Play hôte. Ce harness doit encore être exécuté ; la vérification des hitboxes sur tirs réels et le multijoueur restent à effectuer. Voir `Docs/13-combat-balance.md`.

`VBSoldierRegression.cs` est un harness d'intégration pour le moteur, conservé hors du code distribué. Copier temporairement le fichier dans `Code/AI`, attendre la compilation native, lancer Play en hôte puis exécuter `vb_soldier_regression`. Laisser le jeu tourner jusqu'à `Soldier regression FINISHED: 0 failure(s).` ; retirer ensuite la copie de `Code/AI`.

Le test crée ses propres objets à `(22000,22000,4000)` et les détruit à la fin, sans contrôler le joueur humain. Il vérifie l'inventaire natif et la MP5, les factions Cartel/Xen, la priorité joueur, la réaction à une menace proche puis à un agresseur récent, le filtrage des alliés, la ligne de tir, la consommation de munitions et les dégâts natifs, la mémoire sans vision à travers les murs, la recherche, l'arrêt à la mort et le lâcher d'arme, ainsi que l'occultation des explosions natives. Il n'atteste pas les couverts ni le réseau.

Statut de cette passe : compilation du harness vérifiée ; exécution en jeu non effectuée, car le serveur MCP local de l'éditeur était inaccessible.

`VBSoldierEquipmentRegression.cs`, à copier temporairement dans `Code/AI`, ajoute la commande `vb_soldier_equipment_regression`. Elle vérifie le loadout MP5/USP/poings, l'encombrement identique au joueur, les stocks initiaux, le prélèvement exact d'une cartouche PNJ, le passage au pistolet sans recharge gratuite, le délai de déploiement, la cadence, le blocage pendant le rechargement, une recharge partielle, les rechargements répétés avec réserve de pistolet illimitée (`-1`), le retour aux réserves ordinaires après lâcher, les plafonds de réserve et de grenades, le passage aux poings à sec, les dégâts natifs de mêlée, l'évasion hors de portée, l'interruption par ragdoll et le lâcher des deux armes à la mort. Laisser Play actif jusqu'à `Soldier equipment FINISHED: 0 failure(s).`, puis retirer la copie. Le test dure environ dix secondes et utilise une zone isolée à `(24000,24000,4000)`. Son exécution reste à effectuer ; sa compilation seule ne valide pas ces comportements en jeu.

Le rechargement tactique à couvert et les animations du pistolet/poings doivent aussi être essayés dans `soldier_test.scene` : vider un chargeur avec réserve, observer la mise à l'abri avant recharge et la fin du délai avant sortie ; refaire sans couvert disponible ; épuiser totalement la MP5 puis l'USP ; approcher pendant un rechargement pour engager la mêlée ; renverser le soldat pendant la préparation d'un coup. Refaire avec un client connecté pour contrôler les compteurs et l'unicité des dégâts.

`python Tests/AI/validate_soldier_assets.py` vérifie sans moteur le prefab et les inventaires effectifs des instances de scènes, après leurs surcharges. Il détecte notamment l'ancien inventaire natif rétabli par une surcharge et le second inventaire ajouté automatiquement, qui empêchaient l'équipement et le passage aux poings. Le harness d'équipement vérifie aussi l'unicité des inventaires des soldats présents, la MP5 active au spawn et le maintien de la pose après l'impact. Pour le visuel, relancer Play dans la scène rechargée : la MP5 doit être en main avant tout combat ; à courte portée, le soldat doit s'arrêter et déclencher le coup natif.

Le validateur refuse également les ajouts de GUID déjà présents dans le prefab et les suppressions d'identifiants obsolètes, notamment les anciennes surcharges d'os Human. Le harness d'équipement construit maintenant un acteur avec l'inventaire natif en premier et un inventaire personnalisé aux anciens réglages : il vérifie leur réunion avant l'équipement, le déploiement de la MP5, puis l'acquisition et le tir autonomes sur une menace visible. Ce cas reproduit la panne observée dans les logs du 16 octobre. La commande `vb_soldier_debug`, disponible sans copier de harness, fournit un diagnostic des acteurs de la scène ; l'exécution du harness en Play reste à effectuer.

Dans `scenes/soldier_test.scene`, vérifier manuellement :

1. Génération de NavMesh, déplacements autour des obstacles, couverts atteignables, alternance abri/tir et rechargement à l'abri.
2. Soldats visant joueurs et zombies ; zombies visant les soldats ; aucune acquisition d'un allié Cartel.
3. Joueur occulté qui se déplace : investigation de sa dernière position, puis patrouille après expiration de la recherche.
4. Grenade à 350–1100 unités : arc dégagé, mèche, explosion unique avec son et particules. Plafond bloquant les deux arcs : pas de lancer.
5. Allié dans la zone visée, puis allié entrant pendant la préparation : lancer refusé ou annulé. Refaire avec un ragdoll ou une mort pendant la préparation.
6. Hôte et client : un seul dégât par balle/explosion, arme tenue visible, animations de tir/rechargement, munitions, ragdoll, lâcher d'arme et arrivée tardive. Refaire sur serveur dédié pour l'origine de tir sans modèle cosmétique.

Les résultats des premières passes ci-dessus précèdent la validation du 17 octobre décrite ci-dessous. La densité de couverts, le coût de la sélection des menaces et le partage des positions entre plusieurs soldats doivent être évalués avec les effectifs réels du jeu.

## Passe armes, grenade et navigation — 17 octobre 2026

Les harnesses `VBSoldierEquipmentRegression`, `VBSoldierRegression`, `VBCombatBalanceRegression` et `VBSoldierPresentationRegression` ont été exécutés dans le moteur en Play hôte, avec zéro échec sur leur dernière exécution. Les premiers essais ont corrigé la recherche des composants désactivés dans les fixtures et le tag `map` du mur utilisé pour tester l'occultation native des explosions. Un prefab zombie compilé périmé désactivait encore la localisation ; sa recompilation a été déclenchée et les dégâts natifs tête/torse/membres sont maintenant vérifiés sur les deux types d'ennemis.

`VBSoldierPresentationRegression.cs`, à copier temporairement dans `Code/AI`, ajoute `vb_soldier_presentation_regression` : prise MP5 via l'IK natif, marche au repos, libération de la main pendant une recharge, geste de lancer Human avec déplacement réel de la main, masquage/restauration de l'arme et prélèvement d'une seule grenade. La comparaison MP5/P90/USP a aussi été observée dans `soldier_appearance_preview.scene` en Play.

`VBSoldierNavigationRegression.cs` ajoute `vb_soldier_navigation_regression`, à lancer seulement dans une scène isolée dont la navigation est désactivée, comme la scène d'apparence. Il construit deux plateformes statiques, génère une NavMesh temporaire et teste un lien explicite, montée, descente avec inversion du waypoint, refus d'une obstruction du corps et repositionnement en présence d'un allié et d'une menace visible. Ses réglages de navigation et objets de test sont restaurés/nettoyés en fin d'essai. Dernière exécution dans le moteur : zéro échec, y compris le déplacement tactique réel.

Ces essais locaux ne valident pas le multijoueur, les poses sous tous les angles ni le comportement d'une escouade sur toutes les cartes. La qualité du flank et du couvert reste dépendante de la géométrie, des liens et des effectifs.

## Croisements et ragdolls — 17 octobre 2026

`VBNpcCrowdRegression.cs` fournit `vb_npc_crowd_regression`, à compiler temporairement dans un projet de test séparé et à lancer dans une scène isolée dont la navigation est désactivée. Le scénario crée sa géométrie et restaure les réglages de navigation en fin d'exécution. Dernière exécution sur serveur dédié : **11 contrôles réussis, zéro échec**, journal `tmp/crowd-fixed-native.log`.

Il vérifie que soldats et zombies ignorent les formes physiques de cadavre, que le solveur de foule natif infléchit le mouvement physique, qu'un zombie contourne un soldat immobile dans une porte de 112 unités, que deux PNJ la franchissent dans des sens opposés en conservant leurs corps solides, et qu'un PNJ au sol quitte puis retrouve la simulation de foule après récupération. Les tests ne revendiquent pas la résolution de toute congestion sur une carte réelle ou une validation avec un second client.

Le correctif de spawn de l'ancienne SoldierTestMap est aussi validé dans le moteur à partir de sa scène compilée défectueuse : point nul supprimé après résolution des références, marqueur natif et caméra restaurés, puis appel natif `NetworkHelper.OnActive` créant un joueur (`tmp/spawn-fixed-native.log`).

`VBNpcMovementRegression` a été réexécuté après la correction de foule : 17 contrôles réussis, zéro échec (`tmp/movement-crowd-fixed-native.log`). Le validateur de survie contrôle maintenant les paires de collision entre les joueurs, soldats, zombies, corps de PNJ et ragdolls. Le validateur général des instances de soldat signale séparément une suppression de prefab obsolète dans `basic_example.scene` ; cette scène n'a pas été modifiée pour cette passe.

## Caméra autonome du joueur — 17 octobre 2026

`VBPlayerCameraRegression.cs` fournit `vb_player_camera_regression`, exécuté dans une scène de test séparée sur serveur dédié : **15 contrôles réussis, zéro échec** (`tmp/player-camera-final-native.log`). Il vérifie les références du prefab cloné, les flags locaux après initialisation, l'absence de caméra active sur serveur dédié, la désactivation du contrôle caméra, la priorité sur une caméra de map ordinaire, le placement et le suivi natifs en première personne, le passage en troisième personne et la désactivation du composant propriétaire.

Ce premier résultat précède le correctif du deuxième client décrit ci-dessous ; le harness actuel crée sa caméra localement et n'attend plus de référence de caméra dans le prefab.

Le même scénario appelle le nettoyage générique `VBNetworkSceneSetup.AfterLoad` sur une liste contenant une référence nulle et un point valide, puis le vrai `NetworkHelper.OnActive`. Un seul joueur apparaît au point conservé et porte sa caméra. L'ancien système `VBSoldierTestSceneSetup` a été supprimé.

Les essais du placement natif activent explicitement la caméra de fixture, sans rendu, afin de tester `PlayerController.ICameraModifier` sur serveur dédié. Ils ne valident pas visuellement la session de l'éditeur, l'activation automatique sur un vrai client graphique ni le transfert entre propriétaires distants. La compilation principale et celle du harness réussissent ; l'avertissement de migration supplémentaire du harness ne concerne que son attente asynchrone de test.

La vérification dans la session réelle de SoldierTestMap a ensuite révélé un `MissingComponent` conservé dans le cache du prefab : celui-ci avait été chargé avant compilation du nouveau type `VBPlayerCamera`. Après rechargement du prefab, les traces ont confirmé la présence et l'activation du composant. L'utilisateur a confirmé le retour de l'image et du contrôle après un nouveau Stop/Play. Les traces sont conservées dans `tmp/editor-spawn-diagnosis.log` ; le système temporaire de diagnostic a été retiré. Cela confirme le cas hôte dans l'éditeur, pas le transfert entre clients distants.

## Caméra du deuxième client — 17 octobre 2026

Le journal du client signalait une erreur de désérialisation de `VBPlayerCamera.Camera` : cette propriété référençait un `CameraComponent` volontairement exclu du snapshot. La propriété n'est plus sérialisée et la caméra préfabriquée a été retirée. Le propriétaire local crée un enfant natif non réseau, tandis que les proxies ne créent pas de vue. Le clone intermédiaire attend son attribution réseau avant d'activer une caméra.

Le harness actualisé passe **20 contrôles, zéro échec** sur serveur dédié (`tmp/peer-camera-final-native.log`). Il sérialise le joueur avec les options natives d'un objet réseau, vérifie l'absence de caméra et de référence caméra, désérialise ce snapshot dans une scène distincte et vérifie la conservation des réglages. Il vérifie aussi la création unique, l'absence de caméra dédiée, le placement natif et le spawn.

La reconnexion réelle de Smithers au serveur de Nelson a ensuite été observée uniquement par les journaux : caméra locale du client active et sélectionnée, caméra du joueur distant absente sur ce client, vue de l'hôte préservée (`tmp/two-peer-camera-validation.log`). L'utilisateur confirme que le joueur 2 voit le jeu. Les diagnostics temporaires ont été retirés. Cela valide cette connexion hôte/client, pas la migration d'hôte ou toutes les conditions de latence.
