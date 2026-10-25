# Véhicule quatre roues — Humvee

Le prefab `vehicles/utility_vehicle.prefab` utilise le [Humvee de Greg World Order](https://sbox.game/greg/humvee), importé depuis l'Asset Store S&box (`greg.humvee#35463`, `models/humvee.vmdl`). La scène `scenes/vehicle_test.scene` contient le véhicule, une route, des bosses, une rampe, un mur et trois zombies. Elle active le god mode des joueurs à leur apparition pour permettre les essais. Le modèle est un prop MGS4 ; sa tourelle est jouable, les portes restent décoratives.

## Utilisation

Ouvrir `scenes/vehicle_test.scene` dans S&box et lancer Play. S'approcher du Humvee et appuyer sur **Use** (`E` avec les bindings actuels). Les commandes de déplacement habituelles donnent l'accélération, le freinage/marche arrière et la direction. **Jump** (`Espace`) est le frein à main ; **View** (`V`) bascule entre vue intérieure et caméra extérieure. **Use** permet de sortir même dans l'eau, sans sol proche, à vitesse élevée ou lorsque le véhicule est retourné. Si les portes sont bloquées, une recherche bornée teste d'autres positions autour du châssis et au-dessus.

Maintenir **VehicleTurbo** (`Shift`, nouvel input dans le groupe Vehicles) pendant l'accélération active le turbo. Le Humvee dispose de `EnableTurbo`, `TurboTorqueMultiplier = 1.55`, `TurboPowerMultiplier = 1.5` et `TurboTopSpeedMultiplier = 1.25` dans le groupe Turbo du contrôleur. Le boost augmente les forces moteur dans la limite d'adhérence des pneus ; relâcher la touche ne coupe pas la vitesse acquise. Il est désactivé pendant le freinage, la marche arrière, le frein à main et lorsque moins de deux roues sont au sol. Il n'a pas de jauge ou de cooldown ; le HUD affiche seulement un petit libellé pendant son activation. Les autres véhicules peuvent le désactiver ou régler ses multiplicateurs.

La console `vb_god true` protège et remet en vie le joueur local sur l'hôte. `vb_god false` coupe cette protection. L'option `VBHealthComponent > Debug > God Mode` est désactivée par défaut. Seule la scène dédiée `vehicle_test` ajoute automatiquement cette protection via `VBVehicleTestEnvironment`. Dans `basic_example`, cette option est désactivée : les dégâts de chute et les autres dégâts restent actifs.

Le prefab joueur possède maintenant le mode natif `Sandbox.Movement.SitMoveMode`, le composant `VBVehiclePlayerDriver` et `VBAnimatedBodyContacts`. Les autres prefabs de joueurs doivent aussi recevoir ces composants. Le conducteur range ses armes pendant la conduite ; leur présentation et les commandes à pied sont rétablies à la sortie.

`VBVehiclePlayerDriver` est l'adaptateur côté joueur : il envoie les entrées au siège, gère la caméra et range l'inventaire pendant l'occupation. `IsSeated` signifie toute place occupée ; `IsDriving` et `Seat` désignent uniquement le conducteur. La physique du véhicule ne dépend pas de cet adaptateur, ce qui permet à une IA de fournir les mêmes commandes. `VBAnimatedBodyContacts` gère les contacts solides du squelette animé et du ragdoll ; il conserve la capsule de locomotion et les dégâts de chute.

### Retourner le véhicule

Lorsqu'il repose sur le toit, les deux places deviennent inaccessibles, y compris le changement de place avec `T`. Sortir si nécessaire, puis s'approcher de la carrosserie : **Use** (`E`) affiche et déclenche **Retourner le véhicule**. Le véhicule doit être vide, presque immobile et posé au sol. L'interaction disparaît après le redressement ; l'entrée redevient disponible.

`VBVehicleRecovery` vérifie la portée depuis les yeux jusqu'à la carrosserie, la visibilité, le sol sous les quatre roues, la pose finale et les poses intermédiaires du châssis. Un obstacle ou un plafond bloque le redressement. L'hôte effectue un retournement progressif avec des forces et un couple amortis calculés depuis l'inertie réelle du rigidbody : aucune affectation de transform ou de vitesse. Il privilégie le côté opposé au joueur quand les deux sens de rotation sont équivalents. Une légère assistance verticale dégage la carrosserie pendant le roulis ; les collisions restent actives. Après environ deux secondes, la gravité et les suspensions reprennent les appuis. Un délai maximal interrompt l'assistance si un nouvel obstacle empêche la fin du mouvement.

Le composant et son `VBInteractionPrompt` sont inclus dans le prefab et peuvent être repris sur un autre véhicule. `VBVehicleController > Recovery > Overturned Up Dot Threshold` règle l'inclinaison qui bloque les places et autorise cette interaction (`-0.2` par défaut). `VBVehicleRecovery` expose la portée (`130` unités), la vitesse maximale (`5` km/h), la vitesse angulaire maximale, les marges de dégagement et `RollDuration` (`1.8` seconde). `IsRecovering` est synchronisé depuis l'hôte : aucune entrée ni seconde activation pendant le mouvement. Les forces de pneus et les dégâts de choc sont suspendus pendant cette assistance, puis reprennent automatiquement.

Le siège relaie l'interaction native vers `VBVehicleRecovery` lorsque le véhicule est retourné. Cela couvre la nouvelle résolution du premier `IPressable` par `PlayerController.StartPressing`, même lorsque le siège et le redressement sont sur le même objet. Les essais utilisent la sélection native `UpdateLookAt` puis `StartPressing`, plutôt qu'un appel direct au composant.

### Tourelle

L'entrée au volant sélectionne la **vue 1PP** et active automatiquement le canon lorsque le réglage d'accès l'autorise. La souris vise, **Attack1** (clic gauche) tire et les commandes de conduite restent disponibles. **VehicleWeapon** (`T`) rejoint la place de tourelle si elle est libre ; depuis cette place, `T` permet de reprendre le volant s'il est libre. Le changement est réservé et vérifié sur l'hôte, sans sortir du véhicule ni réactiver les corps physiques du joueur. La place quittée ne conserve aucune commande de conduite. Le canon suit la visée avec une vitesse limitée et tire dans son orientation effective.

Le marqueur **Occuper la tourelle**, près du côté arrière gauche, permet de rejoindre la place tireur avec **Use**. Cette place utilise une pose debout, des yeux en hauteur et les mêmes protections physiques et sorties que le conducteur. Le tireur ne commande pas les roues.

Dans `VBVehicleTurret > Access`, choisir `Driver`, `Gunner` ou `DriverAndGunner`. Dans le mode partagé, la présence d'un tireur retire le contrôle du canon au conducteur ; le canon redevient automatiquement disponible après son départ. La cadence, les dégâts, la portée, la vitesse de rotation, les limites d'élévation, le son et les pivots sont des propriétés du composant. Les munitions sont illimitées dans cette première version ; elle n'a pas de rechargement ou de surchauffe.

Le tir et les autorisations sont vérifiés sur l'hôte. Les commandes ne contiennent ni cible, ni dégâts choisis par le client. Le son, le `TracerEffect` et son nettoyage sont natifs S&box ; la tourelle et ses descendants utilisent les os `2265cd` et `6a6955`, avec la même correction d'échelle du rig que les roues.

La place tireur possède sa caméra dédiée, avec `CameraOffset` relativement au pivot de tourelle. Elle peut être adaptée au modèle et à la hauteur du tireur indépendamment de la bouche du canon.

Cette caméra suit les yeux du joueur local plutôt que les angles synchronisés du servo, et le headbob de marche est désactivé pendant l'occupation d'un siège. La visée 1PP utilise les yeux du frame courant ; elle ne reprend pas la caméra rendue au frame précédent. `VisualResponse` lisse l'animation du canon sans modifier ses angles physiques ni les autorisations de tir. Le HUD conserve une vitesse et des indications de changement de place/sortie compactes, sans panneau opaque, avec un viseur de 9 pixels et des traits de 1 pixel. La perspective utilisée à pied est restaurée à la sortie.

La convergence du canon vers la cible utilise désormais les pivots fixes, avec une résolution géométrique du décalage de bouche. Elle ne recalcule plus la visée depuis la bouche en cours de rotation : une cible proche au sol pouvait sinon passer alternativement devant et derrière le canon. Une cible trop proche pour être atteinte conserve une orientation stable. `VBVehicleTurret > Gun > Pitch Limits` règle l'élévation (**X négatif**, jusqu'à -85°) et la visée vers le bas (**Y positif**, jusqu'à 85°). Le profil Humvee utilise `-35,45`, soit 35° vers le haut et 45° vers le bas. `GetAimForView` et `GetAimForPoint` permettent aussi à un futur contrôleur de tourelle IA de profiter de ce calcul.

### Caméras en véhicule

`VBVehiclePlayerDriver.Camera.cs` compose la vue via le `ICameraModifier` natif, à l'ordre `100`, après la caméra du joueur. La caméra intérieure suit les yeux du siège, avec un déplacement inertiel limité à une unité, un roulis de 1,5° au maximum et jusqu'à 3° de champ de vision supplémentaires selon la vitesse. Il n'y a pas de retard de pitch/yaw : la direction du viseur reste celle des yeux. Le même déplacement sert au calcul de parallaxe de la tourelle. La place tireur conserve sa vue directe stable, sans ces effets de conduite.

La caméra extérieure amortit le suivi et la rotation du châssis, ajoute jusqu'à 36 unités de recul et 8° de champ de vision avec la vitesse, ainsi qu'un léger roulis en virage. Le turbo ajoute progressivement jusqu'à 2° de champ de vision. Un sweep de rayon 8 est refait après le lissage pour empêcher la caméra de repasser dans un mur. L'entrée, le changement de perspective et la sortie réinitialisent le suivi ; les effets ne s'accumulent pas dans le champ de vision à pied.

Les groupes `Vehicle Camera`, `First Person Camera` et `Chase Camera` du composant joueur exposent les amplitudes et les réponses. `EnableCameraMotion` désactive les effets tout en gardant la collision de la caméra extérieure. Les commandes et la physique restent indépendantes de ces effets locaux.

### Profil Humvee

La masse est de **4 200 kg**, avec un centre de masse abaissé à `(0,0,4)`, un amortissement angulaire de `0.28`, une suspension à `2,4 Hz` et un ratio d'amortissement critique de `1.0`. Une butée progressive commence à 65 % de compression (`BumpStopStrength = 4`) pour résister aux transitions brusques de rampe. La réponse moteur est de `260 unités/s²`, la puissance de `180 kW`, la réponse de pédale de `9`, l'adhérence de `1.8` et l'anti-roulis de `0.95`. Ces valeurs constituent un profil de gameplay pour un Humvee lourd, pas une reproduction certifiée de sa mécanique. Les positions corrigées du siège et des yeux sont conservées.

Un rapport court automatique multiplie le couple disponible par trois en montée au-delà de 12°, sous 20 km/h, sans dépasser la puissance moteur. Il peut reprendre le véhicule qui recule sous son poids. Le couple est réparti vers les roues motrices au sol ; leurs forces restent limitées par l'adhérence. Ces options sont propres au profil et peuvent être désactivées pour un autre véhicule.

## Physique et unités

Le châssis est un `Rigidbody` natif avec deux `BoxCollider`, une masse de 4 200 kg et un centre de masse abaissé. Les quatre roues utilisent des sphere traces orientées selon le châssis, avec ressorts, amortisseurs, appui limité et stabilisation entre les roues d'un même essieu. Les paramètres physiques utilisent les unités S&box (pouces, secondes, kg) ; les vitesses de réglage utilisent les km/h et la puissance les kW.

Les forces sont appliquées aux points de contact. Elles produisent naturellement le transfert de charge et les rotations du châssis. Les objets dynamiques sous les roues reçoivent la force opposée ; la vitesse des supports est prise en compte. La gravité reste celle de la scène. Aucune direction ne modifie directement la rotation ou la vitesse du châssis, et une roue en l'air ne produit pas de traction.

La sonde du Humvee utilise le rayon complet du pneu. Elle part au-dessus du débattement pour retrouver le sol même lorsqu'un hardpoint le pénètre brièvement sur une transition de rampe. La longueur de suspension peut être temporairement négative, ce qui remonte le centre de roue vers son contact au lieu de laisser le pneu traverser le sol. Les sondes plus petites conservent une correction géométrique selon la pente.

Une paroi verticale ne peut pas servir d'appui de suspension, même si le châssis est incliné contre elle. Lorsque le sweep sphérique rencontre cette paroi ou commence en intersection avec elle, un rayon central recherche le premier support valide. Il conserve la projection du rayon du pneu sur la pente. Cela maintient l'appui au sol près d'un mur sans supprimer les collisions physiques du châssis contre la paroi.

Les pneus partagent un budget d'adhérence entre accélération, freinage et correction latérale (cercle de friction). La friction de la surface, la charge de chaque roue et le frein à main modifient ce budget. La direction suit une géométrie Ackermann et diminue avec la vitesse. Une pédale opposée à la marche commence par freiner, puis passe en marche arrière lorsque la vitesse devient faible. La puissance et la vitesse limite réduisent progressivement la force motrice ; la gravité peut entraîner le véhicule au-delà de sa vitesse moteur.

Au stationnement, les pneus conservent un point d'appui sur leur support, avec un ressort tangent amorti et limité par le même budget d'adhérence. Le support peut bouger ; une force dépassant l'adhérence fait glisser le point d'appui. Le châssis n'est jamais figé par cette protection.

Cette base privilégie une physique crédible et réglable pour le gameplay. Les roues sont des contacts calculés, pas quatre corps solides reliés par des joints. Le rapport court est un multiplicateur de couple ; il n'y a pas encore de boîte de vitesses avec régime moteur, de température de pneus ou de simulation complète du glissement longitudinal. Un obstacle qui passe uniquement contre le flanc d'un pneu est traité par les contacts calculés et les colliders du châssis.

### Réglages principaux

| Composant | Réglage | Effet |
|---|---|---|
| Rigidbody | Mass Override / Mass Center Override | Masse et tendance au roulis/retournement |
| VBVehicleWheel | Radius / Suspension Travel | Taille de roue et débattement |
| VBVehicleWheel | Suspension Frequency / Damping Ratio | Raideur et amortissement |
| VBVehicleWheel | Grip / Use Surface Friction | Adhérence et réaction aux matériaux |
| VBVehicleController | Drive Layout | Traction, propulsion ou transmission intégrale |
| VBVehicleController | Engine Acceleration / Engine Power Kw | Réponse à basse vitesse et puissance à haute vitesse |
| VBVehicleController | Automatic Low Gear / Redistribute Drive Torque | Reprise en côte et motricité avec des roues en l'air |
| VBVehicleController | Maximum Steer Angle / High Speed Steer Scale | Rayon de braquage et maniabilité rapide |
| VBVehicleController | Anti Roll Strength | Redistribution de l'appui entre les roues d'un essieu |
| VBVehicleImpactDamage | Minimum / Lethal Speed Kmh | Seuils de dégâts d'impact |

Les hardpoints sont des enfants non animés du châssis. Ne pas leur ajouter de `Rigidbody` ou collider. Conserver l'échelle du véhicule à `1,1,1` et dimensionner le modèle, les colliders et les hardpoints ensemble.

## Roues du modèle Humvee

Les sphères jaunes visibles dans l'éditeur sont les gizmos des sondes de suspension à leur extension maximale ; elles n'ajoutent aucun collider de roue. Leur dessin utilise désormais les coordonnées locales du hardpoint : l'ancienne utilisation de coordonnées monde appliquait sa position deux fois et affichait ces repères en dehors de la voiture. Le modèle et les positions physiques des roues n'étaient pas déplacés par ce défaut de dessin.

La sphère est la forme du balayage qui recherche le support sous le pneu. Son contact alimente les forces de ressort, d'amortissement et d'adhérence ; ce n'est pas un corps de roue solide. Une simulation de cylindres avec joints demanderait une autre architecture et ne corrigerait pas à elle seule la perte de contact près d'un mur. Le sweep sphérique peut rencontrer trop tôt une paroi latérale ; le filtrage des normales et le rayon central de secours traitent ce cas en recherchant le sol.

`VBVehicleWheel` accepte soit un enfant `Visual` indépendant, soit un `SkinnedModelRenderer` et un nom d'os. Le Humvee utilise ses propres roues avec les os `a7287d`, `a74083`, `a7587d` et `3b6462`. Les os sont animés via `SceneModel.SetBoneOverride`, avec la longueur de suspension, l'angle de direction et la vitesse de roulement synchronisés.

Ce modèle ancien conserve des pivots de bind pose 50 fois plus grands que les coordonnées du mesh. `BonePivotScale = 0.02` corrige le centre de rotation visible tout en préservant le transform inverse de bind. Pour un modèle correctement importé, garder `1`. L'animation des roues et de la tourelle ne génère aucune collision supplémentaire. Les os des portes restent dans leur pose initiale.

## Architecture et futur pilote IA

`VBVehicleController` ne lit jamais `Input`. Il demande une `VBVehicleControls` à son `ControlSource`, un composant implémentant `IVBVehicleControlSource`. Le contrat contient :

- `Throttle` : de -1 (marche arrière) à 1 (marche avant) ;
- `Steering` : de -1 (gauche) à 1 (droite) ;
- `Brake` : de 0 à 1 ;
- `Handbrake` : booléen.
- `Turbo` : booléen optionnel, désactivé par défaut ; une IA peut le demander sans utiliser de clavier.

Le siège transforme les commandes du joueur en ce contrat. Une future IA pourra calculer ces mêmes quatre valeurs depuis un chemin et les fournir via son propre composant, assigné à `ControlSource`. Exemple de squelette :

```csharp
public sealed class MyVehicleAiDriver : Component, IVBVehicleControlSource
{
    public VBVehicleControls GetVehicleControls( VBVehicleController vehicle )
    {
        // Remplacer par le suivi de chemin, la perception et les règles d'arrêt.
        return VBVehicleControls.Parked;
    }
}
```

Sans source valide, sans conducteur, ou après expiration des commandes réseau, le véhicule utilise son frein à main. Il ne reçoit pas automatiquement une IA de navigation avec cette interface.

### Créer un autre véhicule

Dupliquer le prefab Humvee, puis remplacer son modèle. Adapter les deux colliders de châssis, le centre de masse, la masse et les quatre hardpoints aux dimensions du nouveau véhicule. Pour les roues, renseigner soit un enfant visuel indépendant, soit les noms d'os du nouveau rig et leur renderer ; remettre `BonePivotScale` à `1` pour un rig normal. Régler ensuite le profil moteur, suspension, adhérence et direction. Placer les positions du conducteur, de ses yeux et les points de sortie.

Les références sérialisées entre composants sont remappées lors de la duplication par l'éditeur. Le système n'utilise pas le nom Humvee pour décider de la physique. `VBVehicleSeat` partage la sécurité et le cycle de vie des places, tandis que `VBVehicleDriverSeat` fournit uniquement les commandes de conduite. La tourelle et la place `VBVehicleGunnerSeat` sont optionnelles : les retirer ensemble pour un véhicule sans arme, puis changer le titre des prompts. Pour une autre tourelle, adapter le renderer, les noms d'os, les pivots et la position de bouche de canon dans `VBVehicleTurret`.

## Immersion et sortie — 25 octobre 2026

Les deux boîtes de collision du Humvee étaient traitées comme des volumes fermés par la flottabilité native : la voiture conservait donc une réserve d'air permanente. `VBVehicleController.Water.cs` implémente maintenant le contrat facultatif `IFluidBuoyancyResponse`, appelé par `VBFluidBody` dans son unique application de flottabilité. Le remplissage augmente au pas physique selon la fraction immergée des bounds du châssis. Il réduit progressivement le volume déplacé effectif, via le facteur de densité de l'appel natif ; les paramètres de traînée et les propriétés du liquide restent ceux de la surface. Aucun deuxième moteur de forces ni composant à poser sur chaque objet n'est ajouté. Les props sans cette politique conservent leur réponse native.

Les réglages du groupe **Water** sont `FloodInWater` (vrai par défaut), `FloodingTime` (12 s à immersion complète, davantage à immersion partielle) et `FloodedBuoyancy` (0,05 du déplacement natif initial). `WaterFill` est un état synchronisé depuis l'hôte, conservé lors d'une sortie du volume ou d'un changement de place. Il n'y a pas encore de vidange/réparation automatique. Le seuil d'entrée de 8 % du châssis évite le remplissage par les seuls contacts des roues dans une flaque. Désactiver `FloodInWater` permet de conserver une coque étanche ; cela ne constitue pas un moteur de bateau. Le modèle est une approximation de perte d'air, sans compartiments, volume d'eau intérieur ni masse mobile simulée. Il concerne actuellement les volumes FFT gérés par `VBFluidBody`.

`VBVehicleSeat.Exit.cs` préfère les points de sortie du prefab et un sol praticable lorsqu'il existe, mais accepte aussi une position libre dans l'eau ou dans les airs. Les traces vérifient l'encombrement du joueur, celui du châssis et le trajet contre les obstacles du monde. Un repli teste jusqu'à 27 positions autour des bounds monde, y compris avec un véhicule retourné ou des marqueurs absents. Une voiture complètement encastrée dans du solide ne possède pas forcément de sortie physique libre : le dernier repli libère quand même le siège au-dessus du châssis, sans garantir l'absence de pénétration dans ce cas extrême. `MaximumExitSpeedKmh` reste un champ sérialisé masqué pour les anciens prefabs ; il ne refuse plus une sortie.

La réservation du siège, les commandes et la tourelle sont libérées par l'hôte. La décision de sortie transmet aussi la vitesse au point d'éjection au propriétaire du joueur, qui retrouve ses collisions, ses commandes et la nage native lorsqu'il est immergé. Le filtre réseau exige toujours le propriétaire et l'occupation réelle : la suppression des limites de sortie ne permet pas d'éjecter un autre joueur.

`Tests/Ocean/VehicleFloodingExitNative.cs` a passé **14 contrôles sur 14** dans `basic_example`, avec les vrais prefabs Humvee/joueur et le callback natif de sortie de `SitMoveMode` : sol sec, eau profonde avec reprise de nage, sortie à 70 km/h avec vitesse transmise, voiture retournée, portes bloquées, marqueurs absents et place tireur immergée. Les roues seules ne remplissent pas la voiture ; le remplissage initial est progressif ; après 45 s d'observation, le haut de la coque est 222,7 unités sous l'eau, alors que le véhicule témoin étanche reste à la surface. La conservation du remplissage hors de l'eau est vérifiée. Résultats structurés : `tmp/vehicle-flooding-exit-native.json`. Compilation : zéro erreur et 12 avertissements préexistants. Le nouveau champ de remplissage et la vitesse de sortie compilent avec les attributs réseau natifs ; ce correctif n'a pas encore été rejoué à deux clients.

Fichiers créés : `Code/Water/IFluidBuoyancyResponse.cs`, `Code/Vehicles/VBVehicleController.Water.cs`, `Code/Vehicles/VBVehicleSeat.Exit.cs` et le banc ci-dessus. Modifiés : `Code/Water/VBFluidBody.cs`, `Code/Vehicles/VBVehicleController.cs`, `VBVehicleSeat.cs`, `VBVehiclePlayerDriver.Seating.cs` et les documentations véhicules/interactions. Aucun changement de prefab ou sauvegarde de scène n'est nécessaire pour bénéficier des valeurs par défaut.

Le banc existant `VehicleWaterRegression.cs` a ensuite repassé **8 contrôles sur 8** : conduite par les commandes réelles, 148 perturbations automatiques, dix impacts de la vraie tourelle, champ GPU fini et non nul, vingt recréations et survie après nettoyage. Journal : `tmp/vehicle-water-after-flooding.json`.

## Réseau et impacts

Le Humvee porte un `NavMeshArea` natif configuré comme obstacle sur son empreinte. `VBNpcVehicleContacts` complète la navigation des zombies par un balayage de leur capsule avant la physique, empêchant leur déplacement de traverser le châssis pendant une mise à jour du navmesh. Un autre prefab de PNJ piloté par `NavMeshAgent` peut recevoir ce même composant.

La perception identifie un acteur par son objet de santé/faction, même lorsque le siège le rattache au véhicule. Voir le véhicule occupé permet de repérer le joueur ; un mur continue à bloquer la détection. La possibilité de le voir ne permet pas aux zombies de donner des coups à travers la carrosserie.

La racine du véhicule utilise `NetworkMode.Object`, avec un propriétaire hôte ou sans propriétaire explicite (simulation par l'hôte), transmission permanente et propriété fixe. Les enfants du prefab utilisent `Snapshot` et partagent cette racine ; ils n'ont aucun flux de transform indépendant. Le contrôleur répare aussi à l'apparition les anciens véhicules placés qui restaient en mode `Snapshot`. Ce mode initial était la cause du châssis figé et enterré sur J2 alors que le joueur attaché continuait de suivre la simulation de J1.

La racine réplique les suspensions, le braquage, la vitesse de roulement, la phase de rotation des quatre roues, les appuis, les vitesses linéaire/angulaire, le turbo, le rapport court, la tourelle et les occupants. La phase des roues avance au pas physique, y compris sur serveur dédié, et reste non bouclée pour éviter une interpolation en sens inverse au passage de 360°. Les effets de caméra et la résolution des contacts du joueur distant utilisent les vitesses répliquées.

Le joueur conserve sa propre propriété réseau. Son `VBVehicleSeatBinding` est une décision atomique de l'hôte : GUID du siège, révision, pose et vitesse de sortie et flags à restaurer. Le GUID est résolu même si le joueur arrive avant le véhicule pendant un snapshot. Un RPC fiable applique immédiatement la décision à chaque pair ; la propriété synchronisée conserve le même état pour une arrivée tardive. Pendant l'occupation, `NoTransformSync` et `NoInterpolation` suppriment le second flux de transform du joueur : sa pose locale reste fixée au siège du châssis interpolé. Les capsules, les os et la présentation de l'inventaire suivent cet état sur tous les pairs. La sortie restitue les flags antérieurs et les contacts physiques ; les transfers internes restent dans le mode assis natif.

Les commandes du joueur partent à 25 Hz via un RPC réservé au propriétaire de son objet. L'hôte vérifie aussi explicitement l'appelant, l'occupant, la révision du siège et le numéro de paquet. Un paquet ancien ou issu d'une occupation précédente ne peut pas relancer le véhicule ni tirer après un changement de place. La visée est transmise dans le repère du châssis pour éviter de soustraire une orientation d'hôte plus récente à une visée monde retardée. Après 0,35 s sans commande, le siège revient au frein à main et la tourelle cesse de tirer. Le siège réserve immédiatement l'occupant pour éviter deux entrées simultanées ; la déconnexion, la désactivation et la sortie libèrent sa réservation.

L'hôte simule les roues et applique les dégâts. Avant la simulation, `VBVehicleImpactDamage` balaie les formes du châssis sur la distance du prochain pas physique, avec une marge de trois unités. Un PNJ touché passe immédiatement en ragdoll : sa capsule de locomotion ne bloque plus le châssis comme un obstacle immobile. Un test d'occlusion empêche de toucher un PNJ derrière un mur. Le véhicule perd une petite quantité de mouvement liée à la masse de la victime ; sa vitesse n'est jamais restaurée après une collision avec le décor.

Le Humvee déclenche des dégâts à partir de **5 km/h**, croissant avec le carré de la vitesse, jusqu'à `300`. Le réglage de référence est `140` dégâts à `30 km/h` : une cible intacte de 100 PV sans armure meurt vers **26 km/h** lors d'un impact frontal. Les impacts obliques utilisent la composante normale de la vitesse. Un choc lent blesse et déclenche le ragdoll récupérable existant ; ses contacts secondaires à faible vitesse ne lui infligent pas une série de nouveaux dégâts. Le PNJ se relève lorsque sa physique est stabilisée. Un nouveau passage rapide peut tuer un PNJ déjà à terre.

Le conducteur est crédité comme attaquant. L'impulsion passe par `VBDamageInfo.Force` et le ragdoll existant du PNJ. Les dégâts natifs du rigidbody du véhicule sont désactivés pour éviter leur cumul avec ces dégâts. Les collisions sans ragdoll de PNJ, notamment celles des joueurs, gardent le traitement par contact et respectent `DamagePlayers`.

L'entrée et la présentation assise s'appuient sur `BaseChair` et `SitMoveMode`. La sortie est adaptée au véhicule, car la sélection de point de sortie native ne teste pas les obstacles. Le véhicule n'a pas encore de dégâts de carrosserie ou de système de destruction.

### Collisions du conducteur et ragdoll

Un `ModelPhysics` avec `MotionEnabled = false` peut conserver des corps d'os solides et keyframed. Le prefab joueur complet les possède même lorsqu'il est vivant. Sans protection, installer le joueur à l'intérieur du châssis provoquait des contacts entre ces os et le véhicule : les os suivaient le siège tout en repoussant continuellement le châssis.

`VBAnimatedBodyContacts`, présent sur les prefabs joueur et zombie, réserve les contacts solides des os à leur ragdoll dynamique. Les os animés restent accessibles aux traces ; la capsule assure les collisions de déplacement. Cela empêche une animation de marche ou de poing de pousser une voiture comme un corps de masse infinie. Les contacts du ragdoll dynamique redeviennent actifs lorsqu'il est libre ; tout personnage assis garde ses contacts d'os désactivés.

Le moteur de marche natif réinjecte une vitesse à chaque pas physique. `VBMoveModeWalk.PrePhysicsStep` appelle donc `VBVehiclePlayerDriver.ResolveHeavyVehicleContacts` **après** cet ajout. Une trace de la capsule du joueur anticipe son contact avec un véhicule lourd presque immobile et retire seulement la vitesse dirigée vers sa paroi, relativement à la vitesse du châssis. Les déplacements tangents et la marche sur le toit restent possibles. Cette règle exige un véhicule au moins quatre fois plus lourd que le joueur, deux roues au sol et une vitesse sous 8 km/h ; elle ne neutralise pas les collisions d'un véhicule lancé. Les autres modes de marche personnalisés peuvent appeler cette même méthode après leur moteur.

À l'entrée, la décision fiable et persistante de siège prépare le joueur sur chaque pair avant son installation. `VBVehiclePlayerDriver` conserve les valeurs de `PhysicsBody.EnableSolidCollisions` de ses os, désactive leurs contacts solides pendant la conduite et restaure exactement leurs valeurs à la sortie. Il renouvelle cette protection avant les étapes physiques pour couvrir les corps créés ou recréés tardivement, y compris sur les proxies. Le renderer et le composant `ModelPhysics` restent actifs ; leurs états sérialisés ne sont pas modifiés pour cette protection temporaire.

À la sortie, les os physiques sont aussi téléportés à la nouvelle position du joueur avant de restaurer les contacts. Leur déplacement progressif natif depuis l'ancien siège produisait sinon un impact artificiel, interprété par le véhicule comme une collision à grande vitesse. La pose locale des os est conservée ; seules les vitesses de déplacement artificiel des os keyframed sont effacées. Les vitesses d'un véritable ragdoll dynamique sont préservées.

## Validation dans le moteur

Essais effectués le 12 octobre 2026 dans l'éditeur S&box, sur le prefab Humvee :

- quatre roues au sol, châssis immobile au repos ;
- accélération à environ 544 unités/s après 3 secondes, sans roulis parasite ;
- pédale opposée qui freine avant d'inverser le mouvement ;
- freinage, marche arrière, direction à droite et angles Ackermann ;
- gravité en l'air et absence de propulsion par les roues hors sol ;
- collision létale avec un zombie et passage de son ragdoll à l'état `Dead` ;
- protection god mode face à 10 000 points de dégâts ;
- entrée native, sortie avec dégagement et restitution des commandes à pied.

Les essais du profil de conduite amélioré vérifient aussi la marche effective contre la carrosserie, les contacts d'os désactivés pendant l'animation et actifs pendant le ragdoll, le choc lent à 12 km/h (environ 87 PV restants, puis récupération), le choc à 50 km/h (environ 94 % de vitesse conservée), et un mur arrêtant le véhicule sans blesser le PNJ derrière. Les autorisations de tourelle sont testées dans les trois modes, avec dégâts réels sur un zombie et sortie des deux places.

La passe des retours utilisateur comporte 17 contrôles sans échec : chute native de 260 unités (environ 35 PV restants sans god mode), MP5 avec composants supplémentaires de prop masqués tout en conservant le modèle équipé, dépôt au sol, détection du conducteur avec occultation par un mur, séparation conducteur/tireur, obstacle de navigation et arrêt du PNJ contre le châssis. Des hardpoints initialement enfoncés retrouvent les quatre contacts. Une réplique de `Rampe` à 30° rejoint un plateau puis, dans l'autre sens, redescend vers le sol avec quatre contacts. Sur une pente de 35°, le rapport court permet une reprise et environ 402 unités parcourues en quatre secondes, à 12 km/h, avec les quatre roues au sol.

La passe de confort étend ces essais à 24 contrôles, tous validés : entrée en 1PP avec canon actif, aller-retour entre places, refus d'une place occupée, santé conservée sans god mode et caméra de tireur indépendante des corrections du servo. Sur la rampe, la longueur minimale mesurée avec le nouveau profil est de 1,43 unité : le centre de roue reste sous son hardpoint au passage. Les quatre appuis sont retrouvés sur le plateau et à la descente. Les impacts lents/récupérables et rapides/létaux conservent leur comportement ; à 50 km/h, environ 94 % de la vitesse subsiste après le choc.

La régression mur/turbo ajoute 19 contrôles sans échec : quatre appuis après un choc frontal à 50 km/h et avec turbo maintenu contre le mur, récupération des appuis malgré un sweep de pneu bloqué par une paroi latérale, accélération à environ 63 km/h après trois secondes contre 50 km/h sans boost, et conservation de vitesse au relâchement. Le binding natif est bien `Left Shift`. Les visées basses depuis les deux places restent stables à 45° ; la limite réglée à 65° est aussi validée. La variation maximale de yaw mesurée est inférieure à 0,003°, et l'écart de convergence vers une cible située devant le canon est inférieur à 0,02 unité. Le tireur ne peut pas transmettre des commandes turbo au siège conducteur. Les anciennes suites de rampe, de sièges et d'impacts sont rejouées avec ces corrections.

La régression de redressement physique comporte 23 contrôles sans échec : remplacement du prompt d'entrée, sélection native de l'interaction, refus à distance, derrière un mur, sous un plafond et lorsque la pose finale ou le trajet de roulis sont occupés. Elle couvre aussi les vitesses excessives, le joueur à terre, le véhicule occupé, les transferts de places sur le toit et les requêtes répétées pendant le mouvement. La pose est inchangée au déclenchement ; le roulis traverse progressivement la position sur le côté, puis retrouve les quatre appuis et l'entrée sans déplacement ni dégâts du joueur. La plus grande variation mesurée par échantillon de 0,05 s est de 8,2°. Les 24 contrôles de conduite et les 19 contrôles mur/turbo sont rejoués sans échec.

La régression caméra comporte 14 contrôles sans échec. À 60 km/h, une vue de base à 80° atteint environ 81,8° en 1PP, 83,7° avec turbo et 84,7° en 3PP. Le suivi extérieur passe d'environ 255 à 347 unités de distance, recul et retard amorti inclus. Après lissage, il reste à 14 unités devant le plan du mur de test, avec sa sphère de collision dégagée. Le déplacement intérieur reste dans sa borne, la direction du canon reste celle des yeux, et la place tireur ainsi que la vue à pied restent sans effets de conduite.

La compilation native passe. Le source des essais et leur procédure sont conservés dans `Tests/Vehicles`, hors du code distribué.

Le 14 octobre 2026, la passe réseau a été exécutée avec un hôte et un second processus de jeu réellement connecté. Les 22 contrôles ont réussi : modèle Humvee chargé sur le client, transform du châssis transmis en continu, entrée native depuis le joueur 2, propriétaire du joueur conservé, absence de deuxième flux de position pendant l'occupation, conduite et turbo depuis le client, suspension et rotation des roues répliquées, transferts conducteur/tireur, angles et opérateur de tourelle, refus des commandes anciennes ou d'une ancienne occupation, sortie et restitution des colliders et flags sur les deux pairs. Pendant la conduite, l'écart maximal mesuré entre le joueur et son siège est de 0 unité ; le modèle parcourt environ 1 532 unités sur le client. La santé reste à 100 sans god mode.

Après cette passe, les 24 contrôles de conduite, les 19 contrôles mur/turbo, les 23 contrôles de redressement et les 14 contrôles caméra ont également réussi, soit 102 contrôles au total. Les processus tournent sur la même machine : ces essais ne valident pas encore une forte latence, une connexion tardive, la déconnexion d'un occupant ou la migration de l'hôte.

La régression d'entrée utilise désormais le prefab joueur complet, avec inventaire, animations et ragdoll. Le premier harness avec un joueur simplifié ne couvrait pas les collisions de ses os. Avant correction, le Humvee montait d'environ 121 unités en quatre secondes, avec un pic de vitesse de 460 unités/s. Après correction, il conserve ses quatre roues au sol, sans gain de hauteur, avec une vitesse résiduelle inférieure à 0,3 unité/s. Le test vérifie aussi la restauration des 16 états de collision des os et la survie du joueur factice sans god mode à l'entrée et à la sortie.

## Références S&box consultées

- [Rigidbody et forces aux points de contact](https://sbox.game/api/Sandbox.Rigidbody)
- [SceneTraceResult](https://sbox.game/api/Sandbox.SceneTraceResult)
- [Obstacles de navigation natifs](https://sbox.game/dev/doc/gameplay/navigation/navmesh-areas/obstacles/) et [NavMeshArea](https://sbox.game/api/Sandbox.NavMeshArea)
- [NavMeshAgent](https://sbox.game/api/Sandbox.NavMeshAgent)
- [WheelJoint natif](https://sbox.game/api/Sandbox.WheelJoint), étudié comme alternative aux contacts calculés
- [BaseChair](https://sbox.game/api/Sandbox.BaseChair) et [son implémentation Facepunch](https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Scene/Components/Game/BaseChair.cs)
- [SitMoveMode Facepunch](https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Scene/Components/Game/PlayerController/Modes/Sit.cs)
