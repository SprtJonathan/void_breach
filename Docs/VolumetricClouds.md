# Nuages volumétriques

Version destinée aux vues depuis le sol : intégration volumétrique par ray marching, génération Perlin/Worley, bancs définis par une carte météo, vent, éclairage solaire, ciel HDR et ombres approximatives sur les surfaces opaques. Le calcul est écrit en HLSL, piloté par C# ; cette livraison ne contient pas de graphe Shader Graph.

Le système de [précipitations](Precipitations.md) étend cette météo avec pluie, transitions, abris et impacts. Ses deux matériaux disposent de ShaderGraphs éditables et réutilisent le champ volumétrique des nuages.

## Utilisation

Ouvrir `environment/scenes/cloud_showcase.scene` dans l'éditeur pour la démonstration, ou déposer `environment/prefabs/volumetric_clouds.prefab` une seule fois dans une scène existante. Le prefab contient :

- **VB Cloud Weather** : graine, couverture, densité, vent XY, altitude absolue, épaisseur et taille des masses. Les distances utilisent les unités du monde, indépendamment de la position/échelle du prefab.
- **PostProcessVolume** infini : applique le rendu aux caméras de la scène.
- **VB Volumetric Clouds** : qualité locale, distance maximale et ombres.

La caméra doit activer **Enable Post Processing** et utiliser une projection perspective. Les caméras orthographiques sont ignorées. Le composant détecte un soleil directionnel actif ; assigner **Sun** explicitement lorsqu'une scène possède plusieurs lumières directionnelles.

**Coverage** contrôle la formation des masses, **Density** leur opacité et **Erosion** leurs contours. Une valeur effective nulle de couverture ou de densité saute toutes les passes. **Wind** est exprimé en unités/seconde. Les réglages directs s'appliquent immédiatement ; `VBCloudWeather.SetWeather` pilote une transition progressive depuis l'hôte et conserve la continuité du déplacement lors d'un changement de vent. Une demande de pluie fournit automatiquement une couverture et une densité minimales ; les valeurs évaluées sont `CurrentCoverage` et `CurrentDensity`. Changer directement le vent, la graine ou la taille des masses peut déplacer les motifs brusquement.

**Preview Time** fige l'animation d'une scène d'éditeur pour une capture reproductible. `-1` suit l'horloge ; ce réglage est ignoré en jeu.

**Preview In Editor**, activé par défaut sur **VB Volumetric Clouds**, affiche les nuages dans le viewport même si la caméra du joueur est créée uniquement en Play. S&box construit les post-traitements du viewport depuis `Scene.Camera` ; sans caméra de scène, ils ne sont pas évalués. Le composant fournit donc une caméra temporaire masquée lorsqu'il en manque une. Elle cède la place aux caméras de la scène, est exclue des sauvegardes, du réseau et de la copie de jeu, et disparaît lorsque l'aperçu ou le composant est désactivé, ou que la qualité passe à Off.

## Rendu et qualité

| Qualité | Dimensions du rendu volumétrique | Pas de vue | Pas vers le soleil |
| --- | --- | ---: | ---: |
| Off | aucune passe | 0 | 0 |
| Low | largeur et hauteur divisées par 4 | 24 | 3 |
| Medium, défaut | largeur et hauteur divisées par 2 | 48 | 8 |
| High | largeur et hauteur divisées par 2 | 72 | 10 |
| Ultra | résolution native | 96 | 12 |

Les trois passes sont insérées à **AfterSkybox**, avant les transparences et le viewmodel. La composition dessine à la profondeur lointaine reverse-Z et utilise le test de profondeur matériel du GPU : les silhouettes opaques sont protégées, y compris les objets absents de la prépasse et les échantillons MSAA. Aucune copie supplémentaire de profondeur ni variante de lecture MSAA n'est nécessaire. Le résultat passe ensuite dans le tonemapping et les effets de la caméra, y compris le rendu sous-marin existant.

Le ray marching saute les zones entièrement occultées d'après le minimum de profondeur de la pyramide native. Une marge protège la remontée bilinéaire aux silhouettes ; les petits trous de ciel restent pris en compte. Les qualités et les nombres de pas restent inchangés.

Le bruit est un volume périodique de **128³ voxels**, stocké dans un atlas RGBA de 2080 × 1040 pixels avec marges de filtrage. Le canal R combine quatre octaves de Perlin à gradients avec du Worley ; les canaux GBA contiennent trois fréquences de Worley pour les volumes arrondis et l'érosion. La carte météo indépendante de 512² pixels fournit couverture, type/hauteur des nuages et déformation XY. Elle varie les bancs et leurs sommets au lieu de simplement répéter des petits nuages identiques. Chaque échantillon 3D utilise deux lectures bilinéaires de l'atlas. Le générateur reproductible `Tools/generate_cloud_noise.py` nécessite NumPy et Pillow ; le jeu charge les textures fournies.

Ces données représentent environ **9,25 Mio en RGBA8 sans mipmaps**, contre environ 1,06 Mio pour l'ancien atlas : c'est un compromis de mémoire en faveur des détails. Les paramètres météo restent identiques pour tous les niveaux de qualité. L'épaisseur par défaut passe de 6500 à 9000 unités ; les anciennes instances qui ont déjà sérialisé 6500 conservent leur réglage.

L'intégration s'arrête lorsque la transmittance devient faible. La carte météo permet de sauter le bruit détaillé hors des bancs. Les cibles temporaires viennent du pool de rendu et sont rendues au pool dans la même liste de commandes. Aucun historique temporel, génération de bruit à chaque frame ou transfert GPU vers CPU n'est utilisé en production. Le jitter spatial est stable ; il peut rester du grain et des strates près de l'horizon, surtout en Low. Il n'y a pas de reconstruction temporelle pour supprimer complètement ces artefacts.

L'éclairage combine deux lobes de phase, trois approximations d'ordres de diffusion partageant la même profondeur optique, une occlusion ambiante liée à la densité et un effet de poudre. Les deux premiers échantillons vers le soleil conservent les détails d'érosion ; les autres évaluent une densité simplifiée. Les échantillons proches sont plus serrés et légèrement décalés spatialement. L'intensité directe est calibrée pour conserver les reliefs dans les blancs. Ces approximations s'appuient sur les principes décrits dans [Nubis](https://www.guerrilla-games.com/read/nubis-realtime-volumetric-cloudscapes-in-a-nutshell) et la [documentation des nuages volumétriques Epic](https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-clouds?application_version=4.27).

Des marqueurs de profilage sont présents : **VB Clouds / Ray march**, **VB Clouds / Ground shadows**, **VB Clouds / Composite**. Mesurer ces passes dans le viewport ou le jeu avec la résolution et la carte cible. Le chemin de capture `RenderToBitmap` a validé les images, mais les mesures `perf_gpu` recueillies pendant cet essai décrivaient le viewport principal ; elles ne permettent pas d'attribuer un coût fiable aux nuages. Aucun budget GPU chiffré ni gain de FPS n'est revendiqué.

## Ombres et limites

**Ground Shadows** intègre huit échantillons de la même densité détaillée dans la direction du soleil et utilise le même coefficient d'extinction que le ciel. La passe tient compte de l'orientation des surfaces, puis atténue la couleur déjà éclairée. **Shadow Strength** et **Shadow Distance** bornent l'effet. Le facteur de nuage reste commun au sol et à l'ombre native proche : un second masquage par la shadow map pouvait rendre l'ombre du personnage plus claire que son voisinage.

Il s'agit d'une approximation en espace écran : elle ne sépare pas physiquement l'éclairage solaire des autres contributions. Les matériaux émissifs, les lumières locales et le sol déjà ombragé peuvent être assombris. Les surfaces transparentes, réflexions et GI ne reçoivent pas une véritable ombre de nuage. La carte d'ombre et les volumes éclairés du moteur ne sont pas modifiés.

La caméra locale `VBPlayerCamera` active l'occlusion ambiante native pour conserver le contact des personnages avec le sol, notamment sous un toit. **Contact Shading**, **Contact Intensity** et **Contact Radius** permettent de l'ajuster ; les options graphiques du client pilotent son budget. Cette occlusion reste en espace écran et n'ajoute pas une seconde ombre directionnelle lorsque le soleil est déjà entièrement bloqué. Le boîtier opaque des accessoires mixtes est explicitement inclus dans la prépasse profondeur/normales par `VBWeaponAttachment`, en conservant les matériaux translucides de la vitre et du réticule.

Les nuages sont composités uniquement sur le ciel. Ils ne masquent pas une montagne ou un objet situé derrière leur couche, et ne constituent pas un système de vol à travers les nuages. La skybox fournie contient déjà un léger voile à l'horizon : employer une skybox plus neutre si cet effet est indésirable. La scène de démonstration sert à observer le rendu avec une caméra fixe ; elle n'inclut pas de contrôleur de déplacement.

## Multijoueur

Le prefab est configuré en **Network Mode: Object**. Graine, couverture, densité, vent, altitude, épaisseur, taille et érosion sont des propriétés `[Sync(FromHost)]` : modifier ces valeurs depuis l'hôte. Leur synchronisation est déclenchée par les changements, sans RPC par nuage ni flux de textures.

L'animation utilise `Time.NowDouble`, l'horloge de scène ajustée par le moteur sur celle de l'hôte. Les décalages sont calculés en double précision et ramenés à une période commune aux bruits. Un client arrivé tard n'a pas de simulation de particules à rejouer. Les qualités restent locales ; tous les clients partagent la densité, avec des approximations d'échantillonnage différentes. Le serveur dédié ne lance aucune passe graphique. Les réglages de soleil/ambiance doivent être cohérents entre les pairs ; leur réplication relève de l'environnement de la scène.

Cette livraison vérifie le mode réseau du prefab et ses références clonées. Une session à deux clients, l'arrivée tardive, la migration d'hôte et les coûts réseau restent à tester avant de qualifier la cohérence multijoueur de validée.

## Validation native

Sur le moteur installé **26.10.13c**, compilation C# réussie (12 avertissements préexistants) et compilation forcée des trois shaders réussie, programmes VS et PS compris.

`Tests/Environment/CompileClouds.csx`, exécuté avec `sbox-cli --call run_csharp`, recompile les trois shaders et retourne leurs diagnostics. `Tests/Environment/CloudRegression.csx` construit une scène isolée et retourne **25 contrôles réussis** : références du prefab, volume infini, mode réseau, chargement des shaders et des nouvelles textures, visibilité des nuages, mouvement, couvertures faible/forte, stabilité sans vent, protection du sol et d'une silhouette opaque, projection des ombres, budgets des quatre qualités, couverture/densité nulles, désactivation/réactivation et caméra orthographique. Les comparaisons de pixels tolèrent le jitter sous-pixel des ombres natives. Les scènes actives et le joueur de l'utilisateur ne sont pas modifiés.

Sur **26.10.27b**, `WeatherRenderingRegression.csx` ajoute neuf contrôles du vrai modèle de viseur, des opaques hors prépasse, du contraste des ombres, du contact d'un personnage sous un toit et de la prépasse des accessoires mixtes. `CloudViewportRegression.csx` vérifie neuf combinaisons de taille/inclinaison, dont 2659×1369. Ces captures hors écran ne remplacent pas l'observation du viewport réel MSAA 8×, utilisée séparément pour reproduire et vérifier les bandes.

`Tools/CreateCloudShowcase.csx` est un générateur explicite : il **réécrit** la scène de démonstration et le prefab et prend des captures comparatives. Il conserve désormais les GUID de la racine et des composants du prefab, ainsi que les références internes. Deux régénérations successives ont été exécutées avec ces identités inchangées. Ne pas l'utiliser pour un simple test après personnalisation de ces assets ; utiliser la régression. Les captures de référence sont dans `Docs/Images/volumetric-clouds-*.png`.

### Instances de scène et Play

L'ancien générateur recréait les identifiants internes à chaque export. Une instance personnalisée pouvait rester visible en mémoire dans l'éditeur, mais son patch sauvegardé ajoutait ses composants à une ancienne racine de prefab ; au lancement, les composants météo, volume et rendu disparaissaient. Le lien de l'instance dans `scenes/soldier_test.scene` a été réparé et enregistré en conservant ses réglages et les identifiants de ses composants d'instance.

`Tests/Environment/CloudPrefabInstances.csx` inspecte les instances sauvegardées dans les scènes du projet et les désérialise dans une scène isolée : racine source actuelle, absence de doublons et référence météo valide. Il a validé l'instance personnalisée de `soldier_test` après deux régénérations du prefab. `CloudPlayRegression.csx` s'exécute avec Play actif : il contrôle les composants de la vraie scène et prend une capture avec une caméra temporaire qui ne remplace pas celle du joueur. Le test a réussi dans `soldier_test`, avec une seule météo, un seul renderer et des passes de rendu actives. Ces essais ne constituent toujours pas une validation à deux clients.

`CloudEditorPreviewRegression.csx` vérifie séparément onze conditions de création, exclusion de la sauvegarde/réseau, désactivation, réactivation, transfert vers une caméra principale et nettoyage. Un cycle Play → arrêt a aussi été exécuté dans `soldier_test` : aucune caméra d'aperçu dans la copie de jeu, caméra du joueur conservée, rendu des nuages actif en Play puis de nouveau visible dans le viewport après arrêt.
