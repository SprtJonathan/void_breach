# Eau modulaire — Void Breach

Base de rendu et de physique pour eau naturelle légèrement trouble, destinée à un budget PC milieu de gamme. Implémentation vérifiée dans s&box **26.09.22b**. Les anciens `GerstnerOcean`, `RiverWater` et `StagnantWater` restent indépendants ; cette version ne remplace pas automatiquement leurs instances.

## Essayer immédiatement

Ouvrir **`water/scenes/water_showcase.scene`** dans l’Asset Browser, puis Play. La caméra regarde la mare. La rivière est au nord, l’océan à l’est ; explorer ces zones dans le viewport de l’éditeur. Les objets tombent dans l’eau au lancement : bois et caisse flottent, le bloc dense coule, la rivière entraîne son objet. La scène est un banc d’essai avec des formes simples, pas un environnement artistique final.

Prefabs à glisser dans une scène :

| Asset | Usage |
| --- | --- |
| `water/prefabs/pond.prefab` | Mare elliptique, petites rides, eau verte légèrement trouble |
| `water/prefabs/river.prefab` | Rivière courbe descendante, courant longitudinal |
| `water/prefabs/ocean.prefab` | Surface côtière rectangulaire de 10 000 unités, houle |
| `water/prefabs/infinite_ocean.prefab` | Océan sans limite physique horizontale, maillage centré sur chaque vue |
| `water/prefabs/underwater_effects.prefab` | Effet sous-marin partagé par les caméras de la scène |

**`water/scenes/ocean_horizon.scene`** présente l’océan seul, à faible hauteur, avec le soleil devant la caméra pour vérifier le reflet. La scène mixte conserve son océan fini pour ne pas recouvrir la mare et la rivière.

Ils utilisent tous **Void Breach / Water / VB Water Body**. Garder l’objet horizontal : la rotation autour de Z et les échelles positives sont acceptées. Une rotation en tangage/roulis ou une échelle négative désactive le rendu et l’échantillonnage avec un avertissement. Pour une rivière descendante, modifier la hauteur des points, pas la rotation du parent.

## Régler l’eau

- **Shape / Size / Depth** : emprise et profondeur physique. `Depth` ne creuse pas le terrain ; placer le fond et les berges dans la scène. L’ellipse est découpée au pixel dans le même contour que la physique.
- **River Points** : liste ordonnée des positions locales de la ligne centrale, interpolée par Catmull-Rom. `River Width` est uniforme. Éviter les points répétés, les virages plus serrés que la largeur et les auto-intersections. Limite : 128 points, 32 subdivisions par segment. Les extrémités ont des limites finies ; prévoir une autre étendue d’eau ou du terrain en sortie.
- **Motion** : `Automatic` choisit `PondWater` pour une ellipse, `RiverWater` pour une rivière et `OceanWater` pour un rectangle. Le choix explicite permet notamment un bassin rectangulaire calme. Ces profils partagent le maillage et la flottabilité, mais pas la logique d’animation.
- **PondWater / Pond Agitation** : trois petites ondes croisées de fréquences distinctes et détails lents sans translation de courant des textures. L’écume reste fortement réduite. Les impacts conservent leurs rides locales.
- **RiverWater / Current Speed / River Turbulence** : coordonnées longitudinales calculées à partir de la distance sur le spline ; le courant transporte toutes les couches dans ce repère et les normales sont réorientées selon la tangente locale. La turbulence ajoute des perturbations secondaires. La vitesse négative inverse le flux. Le courant physique suit également le spline.
- **OceanWater / Wave Amplitude / Length / Speed** : six trains de Gerstner dispersifs, avec directions croisées, longueurs non harmoniques et déplacement horizontal/vertical. L’amplitude est l’enveloppe verticale maximale en unités monde. Le déplacement horizontal est borné pour éviter le repliement du maillage. La flottabilité inverse ce déplacement avant de calculer hauteur et normale. Ce n’est pas un spectre FFT ni une simulation de déferlement.
- **Wind Direction** : direction mondiale des vagues. **Current Speed** : vitesse du courant en unités/seconde ; suit la rivière, ou la direction configurée sur une surface plane (transformée avec l’objet).
- **Clarity** : distance d’absorption en unités monde. Plus grand = fond visible plus loin ; plus petit = eau trouble. **Shallow / Deep Color** règlent les teintes.
- **Normal Strength / Micro Normal Strength / Texture Scale** : quatre lectures de la normal map sont orientées, animées et mises à l’échelle différemment. `Normal Strength` pilote les rides larges ; `Micro Normal Strength` ajoute le détail fin. **Roughness** règle les reflets. **Foam / Shore Width** règlent l’écume aux contacts et aux crêtes.
- **Edge Fade** : fond progressivement la limite créée dans le décor. Le calcul suit le contour réel de l’ellipse, les deux berges et extrémités d’une rivière, ou les quatre bords d’un rectangle. Mettre à zéro pour un océan dont les limites restent hors de la zone jouable.
- **Caustic Strength / Scale / Speed / Depth** : projection écran/espace monde visible sur le fond et les objets opaques observés à travers la surface. `Depth` règle son atténuation avec la profondeur.
- **Resolution** : tessellation du rectangle/de l’ellipse (8–160). La rivière utilise 8 subdivisions transversales. Choisir une maille assez fine par rapport aux vagues ; la hauteur CPU est continue, le maillage GPU interpole entre ses sommets.
- **Priority** : arbitre les volumes qui se recouvrent ; à priorité identique, la surface la plus haute l’emporte. La flottabilité choisit uniquement les volumes qui immergent le point testé, sans additionner deux poussées pour ce point.

Le matériau **`water/materials/natural_water.vmat`** conserve les images originales `materials/water/water_01`. Leur redimensionnement vers une puissance de deux se fait à l’import du shader, sans modifier les PNG. La normal map fournit les détails orientés et filtrés ; l’influence de la couleur photographique reste très faible. L’écume utilise désormais un bruit continu déformé et filtré selon son empreinte à l’écran, avec couverture progressive, au lieu du masque AO de la photographie.

Le shader utilise l’éclairage direct standard de s&box et lit les envmaps séparément, avec sa rugosité et un Fresnel diélectrique. Il ajoute une lecture du fond, une réfraction limitée par le depth buffer, une absorption Beer-Lambert colorée, des caustiques et huit rides transitoires par surface. Les prefabs incluent désormais une **EnvmapProbe** ; conserver un **SkyBox2D avec Sky Indirect Lighting** et soigner l’éclairage. Le buffer SSR opaque n’est pas composé sur cette surface réfractive.

## Flottabilité et interactions

Sur chaque objet flottant :

1. Ajouter un **Rigidbody** et un collider solide.
2. Ajouter **VB Water Buoyancy** sur le même objet. Ajuster **Volume Size** et **Volume Center** à son volume local déplacé. Si le Rigidbody est ailleurs, renseigner **Body** explicitement.
3. Régler la masse du Rigidbody en kilogrammes. Le volume est converti des unités Source (pouces) vers des m³ : un cube de 40 unités déplace environ **1,049 m³**, soit environ **1 049 kg** d’eau douce. À 500 kg il flotte ; à 3 000 kg il coule. **Volume Fill** réduit le volume effectif des objets creux ouverts ou irréguliers.
4. **Drag** résiste à la vitesse relative au courant ; **Angular Drag** calme la rotation. **Maximum Lift** plafonne l’accélération de poussée pour éviter les explosions numériques avec des masses trop faibles. Ce plafond modifie volontairement la poussée de ces cas extrêmes.

Huit échantillons répartis dans le volume appliquent des forces aux positions correspondantes, donc aussi un couple de redressement. Leur immersion tient compte du fond fini et de l’orientation. Il s’agit d’une approximation du volume de la coque, pas d’un découpage exact de son maillage.

**VB Water Interactor** crée des rides à partir du déplacement observé. Ajouter ce composant aux personnages, objets répliqués ou objets animés. `Contact Offset` doit se trouver près du contact avec l’eau ; `Contact Radius` limite les émissions à la surface. Les téléportations de plus de 512 unités sont ignorées. Pour un objet combinant les deux composants, décocher **Emit Ripples** sur la flottabilité afin de ne pas doubler les effets locaux ; c’est le réglage de la démonstration.

Les objets sans `VB Water Buoyancy` ne reçoivent pas automatiquement de poussée. Ne pas superposer ce système et un `WaterVolume` natif actif : leurs forces se cumuleraient. Le volume natif renvoie une surface plane ; le système présent utilise les mêmes ondes que le rendu pour que les points de flottabilité suivent la houle.

La nage du PlayerController, le son et les particules de splash ne sont pas inclus. Le composant d’interaction permet déjà de produire les rides d’un personnage ; la nage doit être raccordée au contrôleur de déplacement du jeu.

## Caméra sous l’eau

Glisser **`water/prefabs/underwater_effects.prefab`** une seule fois dans la scène. Il combine un `PostProcessVolume` infini et **VB Water Underwater** : chaque caméra est échantillonnée pendant son rendu, indépendamment de la position du volume. Les deux scènes de démonstration contiennent déjà ce réglage. L’effet s’active entre la surface animée et le fond fini. On peut aussi l’attacher directement à une caméra, mais éviter de cumuler les deux installations. Activer `Enable Post Processing` sur la caméra. Dans l’éditeur, activer les effets de la vue ; sélectionner le volume ou la caméra permet de prévisualiser ses effets.

Le post-process applique : absorption colorée selon la distance parcourue dans l’eau, diffusion de la teinte, contraste réduit, distorsion légère et caustiques sur les surfaces opaques orientées vers le haut. Un rayon qui ressort de l’eau est limité à son intersection approximative avec le plan de surface, ce qui évite de traiter toute la distance aérienne comme de l’eau. Les paramètres `Visibility`, `Tint`, `Contrast`, `Distortion` et `Transition Depth` restent réglables par caméra.

Cette transition utilise la hauteur exacte échantillonnée au niveau de la caméra, puis un plan horizontal pour les rayons de l’image. Elle reste stable et peu coûteuse, mais une vague courbe ou une rivière très inclinée ne produit pas une frontière optique exacte pour chaque pixel. Le moteur permet aussi des `PostProcessVolume`; ils conviennent à un volume statique, tandis que ce composant suit directement les surfaces procédurales et mobiles.

## Multijoueur et coût

- Les prefabs d’eau sont en **NetworkMode.Object**. Conserver ce mode pour répliquer leur horloge `WaveTime` depuis l’hôte. La géométrie et les réglages sont des données de scène ; leur modification en direct pendant une partie n’est pas synchronisée automatiquement.
- Seul le propriétaire du **Rigidbody** applique les forces (`Body.IsProxy` est vérifié). L’hôte simule les objets sans propriétaire. Le Rigidbody doit lui-même être correctement configuré pour le réseau.
- L’horloge interpolée est partagée entre le shader et les requêtes physiques. Les clients peuvent avoir le retard habituel d’interpolation réseau. Les petites rides sont cosmétiques, locales, avec au plus huit événements conservés par étendue ; aucun RPC n’est envoyé pour chaque ride.
- Sur serveur sans rendu, les ressources visuelles ne sont pas créées. Désactiver/détruire le composant supprime son objet visuel temporaire. L’objet généré n’est ni sauvegardé ni répliqué.
- La topologie est reconstruite uniquement lorsque sa configuration change. Les requêtes physiques réutilisent la géométrie. Une rivière vérifie d’abord les limites de ses segments, puis les triangles concernés.
- Coût de départ : 8 échantillons par objet flottant et par pas physique ; 8 192 triangles pour la mare par défaut, 51 200 pour l’océan. Pas de calcul FFT, de capture de réflexion planaire par image ou de simulation de fluide volumétrique.

Ces choix visent un budget raisonnable. **Aucun chiffre de FPS ni validation à plusieurs clients n’est garanti** : il faut profiler la scène finale sur la machine cible et tester les changements de propriétaire/arrivées tardives avec plusieurs clients.

**Infinite Ocean** fonctionne sur une forme rectangulaire. Sa grille régulière de 128 unités se recentre par pas de 128 : les sommets intérieurs restent sur les mêmes coordonnées mondiales. À résolution 160, la zone détaillée couvre environ 20 000 unités de côté. Une jupe extérieure rejoint `Horizon Distance` (200 000 par défaut) ; les vagues y sont aplaties, contrairement à la physique analytique. Garder le gameplay près de la zone détaillée. Les faibles résolutions réduisent cette zone, pas la taille des cellules. La caméra doit avoir un `ZFar` adapté (180 000 dans la démonstration). Ce n’est pas un système de streaming géographique. Utiliser une seule étendue globale avec une priorité inférieure aux bassins locaux. L’altitude de l’eau ne suit jamais la caméra.

## Réflexions du décor

Les quatre prefabs d’eau incluent maintenant un enfant **Environment reflection** avec une `EnvmapProbe` native en mode `Realtime`, résolution `Medium`. La mare et la rivière utilisent une projection `Box` et une capture chaque seconde ; les océans utilisent `Sphere` et une capture toutes les deux secondes. Les volumes locaux ont une priorité de 10 ; l’océan infini a une priorité de −100 et un volume de couverture large, centré sur son origine. Les sondes suivent le déplacement, la rotation et l’échelle du prefab. Si vous modifiez `Size`, `River Points` ou la portée de l’océan, ajustez également leurs `Bounds` : ces limites ne se redimensionnent pas automatiquement avec les propriétés du composant.

Le composant **VB Water Body / Reflections / Reflection Strength** règle l’intensité des envmaps : `1` conserve le Fresnel physique de l’eau, `0` retire leur contribution, `2` l’accentue pour la direction artistique. Le reflet du soleil reste indépendant. **Roughness** pilote le flou et les normales animées déforment les reflets. Les captures excluent `water_surface` pour éviter la récursion, et leur plan proche est à 4 unités pour capturer les objets proches. Pour plusieurs eaux voisines, conserver une sonde partagée et désactiver les enfants redondants ; les captures influencent aussi les autres matériaux réfléchissants dans leurs limites.

La scène **`scenes/water_showcase.scene`**, distincte de `water/scenes/water_showcase.scene`, contient désormais une sonde par étendue d’eau. Validation du 2 octobre 2026 sur s&box **26.10.02** : C# et shader compilés ; rendu en Play vérifié depuis une caméra au-dessus du bassin, avec réflexion du ciel et du décor ; commande `vb_water_check` : **1 007/1 007** contrôles réussis. La caméra d’origine a été restaurée après l’inspection. Aucune mesure de budget GPU n’a été effectuée. Les cubemaps restent une approximation : elles ne produisent pas le placement exact des reflets d’un miroir planaire et les objets mobiles suivent la cadence de capture.

Références officielles : [EnvmapProbe](https://sbox.game/api/Sandbox.EnvmapProbe) et [lecture des envmaps dans un shader](https://sbox.game/dev/doc/rendering/shaders/classes/envmap).

Les scènes de démonstration incluent des `EnvmapProbe` en mode `Realtime`, mises à jour toutes les 1–2 secondes. Le prefab `water/prefabs/reflection_probe.prefab` fournit un point de départ pour les autres scènes. Ajuster position, `Bounds`, `ZFar` et cadence au décor ; une probe n’est pas nécessaire pour chaque petit plan d’eau. Les surfaces générées portent `water_surface`, exclu des captures pour éviter la récursion. La correction par boîte du bassin reste une approximation de la parallaxe. Un miroir exact demanderait une réflexion planaire dédiée. Les captures ont un coût : ces cadences sont des réglages de démonstration, pas un budget PC validé.

## Étendre le système

- `WaterSurface` définit `TrySample`, `AddRipple`, densité et priorité. Dériver un autre composant de cette classe pour une autre représentation : les objets flottants utiliseront automatiquement ses échantillons.
- `WaterSurface.Find(scene, position, out sample)` retourne hauteur, normale, fond et vitesse du fluide. Pour un volume partiellement immergé, fournir aussi la demi-hauteur verticale en quatrième argument.
- `surface.AddRipple(position, strength)` permet de relier impacts de balles, pluie ou événements du jeu. Les rides sont bornées et remplaçables ; un système plus complexe peut remplacer ce traitement sans changer le contrat de flottabilité.
- `WaterWaves` et la fonction `Waves` du shader partagent leurs coefficients. Toute évolution des ondes doit modifier les deux.

Les prochaines étapes pour une production ambitieuse sont des berges adaptées au terrain, une rivière de largeur variable avec outils de placement, un LOD océanique plus fin, puis des splashes et sillages plus détaillés. Les effets d’obstacles actuels sont des contacts visuels/écume : il n’y a pas de déviation hydrodynamique du courant, de vague réfléchie par une île ni de conservation du volume d’eau.

## Vérification et limites observées

Dans la console pendant Play : **`vb_water_check`**. Cette commande n’altère pas la scène et rapporte 1 007 contrôles (immersion, fond fini, enveloppe et dérivées des trois profils dans les deux axes, inversion du déplacement de Gerstner, interpolation des triangles), puis les positions, masses, immersions et vitesses des objets flottants. `Debug View` isole les normales, l’écume, la réflexion et l’épaisseur pour diagnostiquer le rendu ; remettre `None` ensuite.

Validation du 29 septembre 2026 (passe Gerstner) : 1 007/1 007 contrôles réussis. C# et shaders compilés sans erreur ; avertissements C# préexistants dans les armes/PDA et la migration d’hôte. Inspection du bassin, de la rivière, des reflets du décor, des caustiques et de la vue sous-marine ; captures de l’océan depuis plusieurs positions successives. Ces captures ne remplacent pas une mesure temporelle du shimmer ou un benchmark GPU multijoueur. En Play, bois et caisse flottent, le bloc lourd repose au fond à Z ≈ −160 et l’objet de rivière dérive à environ 69 unités/s.

Causes isolées : le recentrage de l’ancienne grille non uniforme changeait les triangles sous la caméra ; la composition SSR standard produisait des traits sombres aux crêtes. Le shader échantillonne désormais directement les probes. Les pointillés noirs sur les berges venaient du fondu avec la copie de réfraction : le fondu de bord utilise maintenant le framebuffer par alpha blending. Les caustiques utilisent un réseau cellulaire irrégulier déformé et filtré, partagé entre surface et effet sous-marin.

La réfraction utilise la copie de l’image visible et le depth buffer : elle ne peut pas retrouver ce qui est hors écran, réfracter correctement plusieurs transparences empilées, ni supprimer tous les halos sur les silhouettes très fines. Les reflets de cette eau utilisent directement les envmaps de la scène, sans composer le buffer SSR opaque. Ils représentent le décor capturé, y compris hors écran, mais la parallaxe des objets proches et le délai de mise à jour restent approximatifs. Les caustiques sont une projection procédurale reconstruite depuis la profondeur, visible dans l’image, pas une lumière réelle injectée dans le monde : elles n’éclairent pas les particules, les transparences ou une autre caméra hors rendu. Une projection lumineuse complète demanderait une passe dédiée ou un projecteur/decal par étendue, plus coûteux et sans stencil d’eau public évident dans l’API actuelle. L’écume utilise elle aussi la profondeur vue depuis la caméra, pas une carte physique persistante du rivage. Ce socle fonctionne et reste extensible ; il ne constitue pas encore une eau AAA finalisée.

Références moteur : [Depth](https://sbox.game/dev/doc/rendering/shaders/classes/depth), [création de post-process](https://sbox.game/dev/doc/rendering/post-processing/creating-postprocesses), [volumes de post-process](https://sbox.game/dev/doc/rendering/post-processing/postprocessvolume), [réflexions dynamiques](https://sbox.game/dev/doc/rendering/shaders/classes/dynamic-reflections), [envmaps](https://sbox.game/dev/doc/rendering/shaders/classes/envmap) et [attributs des shaders](https://sbox.game/dev/doc/rendering/shaders/attributes-and-variables), ainsi que les signatures et shaders de l’installation locale de s&box 26.09.22b.

Base mathématique : [Gerstner / GPU Gems](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models).
