# Réticule et mouvement de visière

`VBCrosshairHud` centralise le réticule dans le HUD Razor. Le dessin natif de
`VBTraversalCombatWeapon` est neutralisé pour éviter les doublons, y compris
avec les poings et les grenades. Le point central reste fixe ; l'ouverture
réagit à la dispersion native, à la vitesse, au saut et aux tirs réussis.
Le rouge indique un chargeur vide, l'orange un rechargement. Le délai entre
deux tirs ne change pas la couleur.

`VBCombatWeapon.SuppressHudCrosshair` masque le réticule dès la demande d'ADS
et jusqu'au retour presque complet à la hanche, en première et troisième
personne. La mort, l'état à terre, la traversée et l'inventaire suspendu
masquent également le réticule à pied.

En véhicule, seuls les occupants autorisés à utiliser la tourelle voient
le cadre de visée. Son ouverture réagit aux tirs et au désalignement du canon.
Un petit losange orange projette le point d'impact actuel du canon lorsqu'il
diffère du centre et reste à l'écran. Il ne prédit pas le déplacement des cibles.
Le retour de tir utilise le RPC de présentation existant, sans nouvel état réseau.

`VBMasterHud` déplace uniquement `.helmet-layer` : constantes, munitions,
sélection d'armes, traversée et état du véhicule. Les interactions projetées,
le réticule et les boutons de mort conservent leurs coordonnées.
L'inertie suit le regard, les accélérations et le rythme du headbob existant.
Elle revient au repos, diminue de 70 % en ADS et se réinitialise lors des
changements de joueur/siège ou d'une longue frame. Elle est désactivée en TPS
et en incapacité. Les propriétés **Visor Motion / Enable Helmet Motion** et
**Helmet Motion Strength** permettent de la désactiver ou de régler sa force
(0 à 1, défaut 0,65). Les styles sont actualisés directement sans reconstruire
le tracé ECG à chaque frame.

## Palette du HUD

Dans le composant **VBMasterHud**, ouvrir **Appearance → HUD Colors**.
Ce groupe dépliable contient les couleurs de chaque élément et de chaque stade :
santé (normal, alerte, critique, ligne plate), armure, endurance (dont épuisement),
munitions (dont vide et rechargement), réticule, véhicule/turbo, sélection des
armes, interactions, traversée et états à terre/mort. Les 29 couleurs d'éléments
reprennent les valeurs actuelles par défaut.

Pour une modification globale, activer **Use Master Colors**, puis modifier
**Master → Normal / Warning / Critical / Accent**. Chaque élément possède un
interrupteur **Override Master** pour garder ses propres couleurs. Désactiver
le mode maître restitue les couleurs individuelles sans les écraser.

Les couleurs d'éléments, bordures et lueurs sont reliées : les nuances et les
transparences initiales sont conservées lors d'une recoloration. Les fonds
sombres restent contrastés. Une palette par défaut n'ajoute aucune surcharge
de style ; les propriétés et leurs valeurs initiales rendent donc les anciens
prefabs compatibles sans modification. Les réglages sont sérialisables et
modifiables en Play. Le recalcul des styles dépend des changements de palette,
pas des échantillons ECG ni des changements de stade.

Les règles de couleur sont générées depuis les feuilles SCSS existantes par
`python Tools/generate_hud_palette.py`. Régénérer les deux fichiers générés
après modification des couleurs/sélecteurs SCSS ; les définitions des groupes
et les correspondances de stades sont au début du générateur.

Le 3 novembre 2026, compilation .NET et native s&box 26.11.03 réussies.
`Tests/UI/HudPalette.csx` a passé **25 contrôles natifs** dans `soldier_test` :
création du contrôle d'inspecteur, couleurs calculées pour chaque famille,
héritage maître, exceptions, sérialisation, valeurs manquantes et restauration
des défauts. Deux captures montrent la palette d'origine et une personnalisation.
Le script restaure la palette précédente et détruit ses panneaux de test.

## Vérification du réticule et de la visière

Le 27 octobre 2026 : compilation .NET et compilation native s&box 26.10.13c
réussies, puis **24 contrôles natifs** dans `soldier_test` via
`Tests/UI/HudReticle.csx`. Le script nécessite Play hôte, un joueur local et
un emplacement d'arme libre. Il restaure les réglages/inventaire/munitions,
détruit ses objets et écrit quatre captures dans le dossier d'échange du CLI.

Les contrôles couvrent les poings, le tir et son retour au repos, le chargeur
vide, le rechargement, l'entrée et la sortie d'ADS en FPS/TPS, les bornes et
le retour au repos du casque, sa désactivation, le conducteur, le tireur,
le désalignement du canon, le RPC de tir et la perte d'accès à la tourelle.
Les captures confirment le rendu à pied, l'absence du réticule HUD en ADS
et les cadres en véhicule. Aucun essai à deux clients n'a été effectué.
