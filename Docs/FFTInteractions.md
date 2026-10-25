# Interactions universelles FFT

Implémentation du 21 octobre, corrections des 24 et 25 octobre 2026. Dernière compilation avec les DLL s&box **26.10.13c** installées.

La passe d'optimisation du 25 octobre, ses changements de shader et de physique CPU, les mesures par scope et leurs limites sont détaillés dans [OceanOptimization.md](OceanOptimization.md). Les trois prefabs enregistrent un pas de cache physique de huit unités ; le déplacement visuel et les requêtes publiques conservent leur précision existante.

## Stabilité et coût CPU — 25 octobre

Les protections du premier correctif étaient insuffisantes : un rapport natif associait encore une texture `vb_water_state_*` à une libération invalide dans le backend Vulkan. Le ping-pong utilise désormais deux jeux de paramètres à liaisons de textures fixes, un par sens ; les paramètres de texture ne sont plus remplacés entre deux dispatchs en attente. La FFT et son déplacement restent inchangés.

Le nettoyage immédiat a été remplacé par un transfert de propriété vers `FluidGpuResources`. Les textures, buffers et paramètres de compute restent vivants pendant trois images moteur après leur retrait du rendu. Une seule barrière de fin GPU sert tous les lots arrivés à échéance ; les bindings sont ensuite vidés avant la destruction des ressources. Ce chemin commun couvre la FFT, les deux champs interactifs et la texture des projections.

Une première version de ce retrait utilisait une tâche asynchrone : les dix cycles de smoke test ne plantaient plus, mais un contrôle supplémentaire après l'arrêt de Play a révélé **trois lots encore en attente**. Cette version n'est donc pas considérée comme un nettoyage complet. Elle a été remplacée par une file sans tâche ni jeton de scène, traitée par `FluidGpuResourceSystem` dans les scènes de jeu et d'éditeur, ainsi que par la prévisualisation FFT. La file reste ainsi traitable après l'arrêt de la scène qui a créé les ressources. `vb_fft_water_check` affiche les lots en attente, libérés et en erreur.

Le profilage de `basic_example` a également identifié du travail CPU inutile : avec 256 contacts suivis, une passe sur leurs bounds immobiles prenait **6,22 à 7,13 ms**. Le seuil de vitesse était vérifié après les requêtes FFT et la résolution des volumes. Il est maintenant vérifié avant ce travail, tout en conservant l'observation du déplacement et l'initialisation du contact. Un objet statique déplacé ultérieurement par script reste détecté. La liste des surfaces coexistantes est réutilisée une fois par passe plutôt qu'allouée à chaque point de contact.

`Tests/Ocean/WaterPerformanceNative.cs` compare l'eau complète, les interactions désactivées, l'eau désactivée et une seconde mesure complète. Les valeurs comprennent la charge de la scène et le démarrage peut être bruyant : aucune promesse générale de FPS n'en découle. Les fichiers `tmp/water-performance-before.json` et `-after.json` conservent les mesures quand ce test est exécuté.

Mesure du 25 octobre sur `basic_example`, 256 contacts et le même maillage de **72 361 sommets**, déplacement 3D conservé :

| Mesure | Avant | Après |
| --- | --- | --- |
| Passe sur les contacts immobiles, seconde mesure | 6,22 ms | 0,38 ms |
| Médiane par image, seconde phase complète | 19,44 ms | 11,63 ms |
| Plage des deux mesures de contacts | 6,22–7,13 ms | 0,38–0,82 ms |

Ces mesures proviennent de deux sessions avec la charge réelle de la scène ; l'IA, le chargement, la fenêtre et le rythme d'affichage peuvent varier. La première phase complète était particulièrement lente avant correction (99,21 ms médian, contre 18,63 ms après) : elle ne sert pas à promettre un multiplicateur de FPS. Le gain de la passe CPU immobile est la comparaison la plus directement attribuable au correctif.

`run_water_gpu_lifetime.ps1 -Cli <sbox-cli.exe> -EditorPid <pid> -Cycles 10` vérifie trois liquides, les résolutions 64/256/128, quatre désactivations/réactivations, l'avancement physique, les ressources réellement recréées et libérées, l'indépendance des instances éditeur/Play, puis le compteur après arrêt de Play et 45 secondes de nettoyage tardif. Un processus survivant avec des erreurs natives de textures est déclaré en échec. `run_water_gpu_hotload.ps1` prépare trois rechargements C# réels et retire son fichier temporaire même en cas d'échec ; un arrêt de Play pendant ce test est une interruption, pas une validation du hot reload.

Dernier passage du 25 octobre sur le collecteur final : **10 cycles sur 10 réussis**, 51 lots temporaires libérés par cycle et **zéro lot restant après chaque arrêt de Play**, y compris ceux des surfaces de la scène. Le compteur cumulé atteint 600 lots libérés, en incluant les essais précédents dans cette instance. Les 45 secondes supplémentaires avec finalisations forcées sont passées ; aucune des erreurs natives de textures recherchées n'est apparue pendant cette séquence. Journaux : `tmp/water-gpu-lifetime.log` et `tmp/water-gpu-lifetime-final-summary.log`. L'éditeur reste ouvert sur `basic_example`, Play arrêté, compilation réussie et scène sans modifications non enregistrées.

Ce résultat borne la validation : le hot reload C# répété n'a pas été validé jusqu'au bout. La mémoire privée du processus passe d'environ 7 575 à 7 914 Mio entre les cycles 1 et 10 ; les compteurs de la file prouvent le retrait des lots suivis, mais ne démontrent pas un plateau de mémoire globale du moteur ni l'absence de toute fuite. Un profil de longue durée reste nécessaire.

## Correctifs voiture, tourelle et déformation 3D

Les roues du véhicule sont des contacts par raycasts : elles n'avaient aucun collider à détecter lorsque le châssis restait au-dessus de l'eau. `IFluidContactBounds` permet désormais aux systèmes à géométrie virtuelle de fournir leur empreinte ; `VBVehicleWheel` expose la position réelle de sa roue suspendue. Le gestionnaire commun découvre ces contacts sur le corps suivi et émet les mêmes perturbations que pour les autres objets. Aucun composant d'effets n'est ajouté manuellement au véhicule. Les tirs de `VBVehicleTurret.ShowShot` appellent maintenant `FluidImpacts.Segment` dans leur RPC de présentation existant.

Le champ GPU pouvait être plus fin que le maillage : une cellule de 4 unités et des sommets tous les 16 unités laissaient surtout apparaître les normales et l'écume. `FftMeshTessellation` raffine désormais le maillage existant près du champ interactif, à 4 unités par défaut. Les échantillons FFT éloignés et la jupe d'horizon sont conservés. Les lacs se raffinent autour d'un centre quantifié par tuiles de 128 unités ; les rivières subdivisent leurs rangées proches avec un budget supplémentaire borné. Ce n'est pas une seconde surface superposée. Les grandes valeurs d'`InteractionCellSize` et les très larges rivières peuvent encore sous-résoudre les petites perturbations : augmenter la qualité a un coût en sommets.

Le blanc forcé indépendant du réglage d'écume a été supprimé. L'écume interactive découle de la pente et de la vitesse des vagues, disparaît plus vite et se règle séparément avec `InteractionFoam`. Les sources ne peignent plus directement de mousse. Les sillages utilisent un dipôle orienté (soulèvement avant, creux arrière), et la réponse horizontale a été renforcée en conservant ses limites d'amplitude. Le mode `DebugView = SurfaceGeometry` éclaire uniquement les normales des triangles effectivement rasterisés ; il ignore les textures, normales décoratives, reflets et écume.

Un crash natif a été reproduit dans `engine2.dll + 0x33e1ba`, avec libération invalide d'une texture. Les ressources générées ont désormais des identités nommées uniques, des transitions explicites entre lecture/UAV et un ordre de retrait des bindings avant destruction. Une attente de fin des commandes GPU est réservée aux libérations/reconfigurations ; elle peut créer un bref coût à ces moments. Les essais comprennent désormais un contrôle après destruction et finalisation. **Des essais courts réussis ne constituent pas une garantie de stabilité prolongée** ; les dumps natifs restent nécessaires en cas de récidive.

## Intégration

Les trois prefabs `fft_lake`, `fft_ocean` et `fft_river` utilisent toujours `VBFFTOcean`, le matériau FFT et la FFT existants. Aucun composant d'effets n'est nécessaire sur un objet ordinaire. Le suivi appartient à chaque étendue ; `VBFluidBody` ne produit plus de vagues et conserve uniquement son adaptation de flottabilité. `VBWaterInteractor` délègue les effets FFT au suivi universel, tout en conservant la compatibilité historique et la gestion native de nage.

Le domaine existant `FftWaterDomain` sert au maillage, au masque, à la détection de contact et au courant. Les transformations supportées restent celles de l'eau existante : translation et yaw, dénivelé du ruban ; pitch/roll et échelle du parent ne créent pas une surface liquide inclinée.

## Détection

- Le trigger généré fournit les notifications et corps présents au chargement, y compris les entités créées ultérieurement.
- Une requête de broadphase native `Scene.FindInPhysics(BBox)` à 10 Hz retrouve les objets physiques voisins, leurs corps, les membres des ragdolls et les colliders statiques déplacés par script. Elle remplace l'ancienne énorme trace de boîte en narrowphase. Le suivi est borné et les corps invalides, désactivés ou non revus depuis deux secondes sont retirés.
- Une découverte des `ModelRenderer` à 1 Hz couvre les objets visibles sans aucun collider. Elle parcourt les composants de rendu, pas tous les GameObjects à chaque image. Les objets déjà représentés physiquement sont exclus. C'est un compromis explicite pour les objets qui n'appartiennent à aucune structure physique spatiale.
- Les mouvements sont mesurés depuis les bounds monde, indépendamment du contrôleur. La masse vient du corps lorsqu'elle est disponible, sinon du volume des bounds. Un `VBFluidInteractionProfile` facultatif règle la masse estimée, le rayon, la force ou l'exclusion.
- Les empreintes longues utilisent jusqu'à quatre points. Les sillages interpolent jusqu'à huit positions entre deux émissions, avec noyau directionnel allongé. La force dépend des vitesses horizontale/verticale, de la masse et de l'immersion. Les contacts proches sont limités par cellule et par budget global.
- Un objet sans déplacement mesurable n'émet pas continuellement, même si le courant ou les vagues FFT passent autour de lui. Les téléportations supérieures à 1024 unités par tick ne dessinent pas de sillage.
- Les volumes qui se chevauchent sont départagés par priorité puis hauteur, en vérifiant leur recouvrement vertical avec l'objet.

Les objets invisibles sans collider ni renderer n'ont pas d'empreinte détectable : leur système de gameplay doit appeler l'API publique. Les objets extrêmement rapides qui traversent entièrement un petit domaine entre deux observations peuvent échapper à la découverte initiale ; un corps déjà suivi bénéficie du test de traversée entre les bounds précédents et actuels. Les hitscans utilisent déjà `FluidImpacts.Segment` pour traverser la surface FFT déplacée.

## Simulation et rendu

Deux clipmaps par surface suivent l'observateur, ou le domaine fini le plus proche. Avec `InteractionCellSize = 8`, elles couvrent 512 et 4096 unités de côté. La propriété conserve la convention historique de cellule CPU ; le pas GPU est `InteractionCellSize * 64 / InteractionResolution`, donc 4 unités pour la grille proche à 128².

Chaque niveau possède deux textures RGBA32F d'état en ping-pong, une sortie hauteur/pentes/écume, une carte de domaine/courant 64² et un buffer de 64 sources. Les transferts CPU vers GPU concernent les sources et la carte lorsque son origine ou son domaine change. Aucun transfert de texture GPU vers CPU en production.

`interaction_step.shader` intègre l'équation d'onde amortie avec Laplacien 2D, vitesse verticale, advection semi-lagrangienne et sous-pas respectant CFL. Les berges empêchent la propagation à sec ; une bande absorbante amortit les bords des clipmaps. Les impacts ont un centre et une couronne de signe opposé ; les sillages utilisent une pression dipolaire orientée selon le mouvement relatif. Les paramètres de viscosité ajoutent une dissipation apparente ; ce n'est pas un solveur Navier–Stokes complet ni une simulation 3D de déplacement de volume.

`interaction_resolve.shader` calcule hauteur, dérivées spatiales et mousse. Le callback opaque déjà utilisé par la FFT exécute les passes, avec barrières UAV, avant le rendu translucide. Les vues supplémentaires réutilisent le résultat. Il n'y a pas de passe d'intégration lorsque le temps de simulation n'a pas avancé, sauf injection ou recentrage nécessaire.

`fft/surface.hlsl` additionne **FFTHeight + InteractionHeight** au déplacement des sommets. Les dérivées traversent les tangentes FFT et le gradient du mélange entre clipmaps ; les normales, reflets, caustiques et l'écume existants utilisent le même résultat. Le champ est échantillonné en XY monde après déplacement horizontal FFT. Le recentrage conserve les cellules communes ; les nouvelles cellules sont vidées.

À 128², les deux niveaux interactifs occupent environ **1,63 Mio GPU** hors overhead du moteur. Ils ne couvrent pas l'océan entier à haute résolution. Hors de la zone éloignée, le repli historique conserve huit perturbations de normales et des particules, sans déplacement géométrique. Changer fortement de domaine, d'échelle de grille ou de résolution réinitialise le champ GPU.

## Physique et requêtes

`TrySample` / `IFluidSurfaceSampler` continuent à servir la nage et la flottabilité. La FFT CPU existante reçoit une approximation interactive 64² par niveau, alimentée par les mêmes événements. Cette approximation est volontairement distincte du champ visuel plus fin : sources radiales, courant CPU uniforme autour de l'observateur, aucune promesse d'égalité pixel par pixel avec les sillages GPU.

Les forces restent appliquées explicitement par l'adaptation native `ApplyBuoyancy`, uniquement sur le propriétaire du corps. Une déformation GPU ne pousse pas automatiquement les objets. La coque détaillée d'un navire et les échanges bidirectionnels de quantité de mouvement restent des extensions.

Le correctif véhicule du 25 octobre ajoute la politique facultative `IFluidBuoyancyResponse` à cet appel unique : les voitures ordinaires perdent progressivement leur flottabilité lorsqu'elles se remplissent, au lieu de conserver indéfiniment une coque étanche. Il ne change pas les perturbations visuelles ni la flottabilité des props ordinaires. La sortie des sièges accepte maintenant l'eau profonde, l'absence de sol, la vitesse et le retournement. Réglages, fichiers, limites et **14 essais natifs réussis** sont détaillés dans [10-vehicles.md](10-vehicles.md#immersion-et-sortie--25-octobre-2026).

## Réseau

Avec `ReplicateInteractions` et une racine d'eau effectivement active sur le réseau, seul l'hôte émet les contacts automatiques. L'hôte applique immédiatement son effet local ; le RPC ne le lui réapplique pas. Les événements sont regroupés en un lot borné par tick, diffusé avec `Rpc.Broadcast(HostOnly | Unreliable)`. Les clients simulent leurs champs locaux ; aucune texture n'est répliquée. Un identifiant de lot et un historique borné empêchent sa double application. Les limites d'envoi et de réception bornent les émissions.

Les impacts explicites locaux restent locaux pour préserver les systèmes de tirs qui exécutent déjà leurs effets sur chaque client. Depuis du gameplay exécuté par l'hôte, utiliser `EmitReplicatedSurfaceImpact` pour une explosion commune. Cette méthode retourne faux lorsqu'un client tente d'émettre sur une eau répliquée. Elle applique l'effet sur l'hôte puis met l'événement en file pour le prochain tick ; son retour confirme l'acceptation dans le budget, pas la livraison réseau.

L'approche synchronise les sources, pas les pixels : pertes de paquets, positions interpolées, caméra, résolution, démarrage tardif ou migration d'hôte peuvent donner des champs légèrement différents. Les nouveaux clients commencent avec un champ vide ; il n'y a pas d'historique complet à rejouer. Le chemin RPC compile dans le moteur, mais **aucun essai à deux clients n'a été réalisé** dans cette livraison. Il doit être validé avant de considérer la cohérence multijoueur comme acquise.

## Inspecteur

| Propriété | Fonction / valeur des prefabs |
| --- | --- |
| Interactions / InteractionStrength | Activation et multiplicateur global |
| InteractionResolution | 64, 128 ou 256 par côté ; 128 par défaut |
| InteractionMeshSpacing | Pas des vrais sommets proches ; 4 unités par défaut |
| InteractionFoam | Écume interactive facultative ; 0,2 par défaut, 0 pour la couper |
| InteractionCellSize | Étendue des clipmaps ; 8 par défaut |
| InteractionFrequency | 15–60 Hz ; 30 par défaut |
| InteractionWaveSpeed | Vitesse d'onde ; 160 unités/s |
| InteractionDamping | Lac 1,2 ; océan 1,8 ; rivière 1,6 |
| InteractionViscosity | Dissipation supplémentaire ; 0,15 |
| InteractionMaximumHeight / Radius | Hauteur 24, rayon 192 unités |
| InteractionMinimumSpeed | Seuil de mouvement ; 10 unités/s |
| InteractionVelocitySensitivity | Sensibilité ; 0,18 |
| InteractionBudget | Sources par tick ; 48, maximum 64 |
| InteractionTrackingBudget | Corps/objets suivis ; 256, maximum 512 |
| InteractionDistance | Rayon des requêtes locales ; 4096 unités |
| CurrentSpeed / WindDirection | Courant ; tangente locale du ruban pour la rivière |
| ReplicateInteractions | Diffusion des contacts par l'hôte |
| DebugInteractions | Bounds suivies, derniers contacts, clipmaps et compteurs via gizmos |

Les trois prefabs enregistrent ces paramètres et gardent leur composant commun. `GridSpacing` passe à 16 pour mieux représenter les déformations proches ; le maillage du lac passe à 128 subdivisions. Les instances ayant des overrides conservent leurs valeurs. Les scènes de démonstration existantes restent utilisables.

`vb_fft_water_check` affiche les corps suivis, impacts automatiques, dispatchs GPU, rejets, état du champ CPU et flottabilité. L'énergie affichée est celle de l'approximation CPU, pas une mesure GPU. Le GPU s'endort après une durée conservatrice liée à l'amortissement ; son énergie n'est pas relue.

## API

```csharp
// Même champ que les contacts automatiques, sans particules :
water.ApplySurfaceImpulse( position, radius: 48, strength: 70 );

// Impact local avec présentation existante :
water.EmitSurfaceImpact( position, 80, 120, FluidImpactKind.Explosion );

// Sillage directionnel local :
water.EmitSurfaceImpact( position, 36, 30, FluidImpactKind.Wake, velocity );

// Depuis l'hôte, événement commun aux clients :
water.EmitReplicatedSurfaceImpact( position, 80, 120, FluidImpactKind.Explosion );

// Sélection de surface / intersection de trajet existantes :
FluidImpacts.Splash( scene, position, 48, 100 );
FluidImpacts.Segment( scene, start, end );
```

## Fichiers de cette évolution

Créés : `Code/Water/GpuFluidSurfaceField.cs`, `FluidContactMath.cs`, `VBFluidInteractionProfile.cs`, `Code/Ocean/VBFFTOcean.Detection.cs`, les shaders `interaction_step.shader` / `interaction_resolve.shader`, `Tests/Ocean/UniversalWaterNative.cs` et cette documentation. Le moteur produit les fichiers `.meta` / `.shader_c` associés.

Modifiés : `VBFFTOcean.cs`, `.Geometry.cs`, `.Interactions.cs`, `.Physics.cs`, `.Splashes.cs`, `VBFluidVolumeBridge.cs`, `VBFluidBody.cs`, `VBWaterInteractor.cs`, `FluidSurfaceField.cs`, `FluidDiagnostics.cs`, `fft/surface.hlsl`, les trois prefabs FFT, `Tests/Ocean/FluidSurfaceChecks.cs`, `run_fft_checks.ps1` et `Docs/FFTOcean.md`. Le correctif du 24 ajoute `FftMeshTessellation.cs`, `IFluidContactBounds.cs`, `VehicleWaterRegression.cs`, `WaterDeformationNative.cs`, modifie `VBVehicleWheel.cs`, `VBVehicleTurret.cs`, `VBWaterBody.cs` (mode de débogage), `GpuFluidSurfaceField.cs`, `.Detection.cs`, `FluidContactMath.cs`, `interaction_step.shader` et `vb_water.shader`. Le correctif de stabilité/performance ajoute `FluidGpuResources.cs` (file et service de scène), `WaterGpuLifetimeNative.cs`, `run_water_gpu_lifetime.ps1`, `run_water_gpu_hotload.ps1` et `WaterPerformanceNative.cs` ; il modifie les points de retrait des ressources dans `VBFFTOcean.cs`, `.Interactions.cs`, `.Splashes.cs`, les bindings de `GpuFluidSurfaceField.cs`, le suivi dans `.Detection.cs` et les compteurs de `FluidDiagnostics.cs`. Aucun nouveau réglage de prefab n'est obligatoire pour ces correctifs. Les autres modifications déjà présentes dans le projet ne font pas partie de cette évolution.

## Validation reproductible

`Tests/Ocean/run_fft_checks.ps1` : **427 907 contrôles FFT + 7 148 contrôles de surface**, tous réussis. Les nouveaux contrôles couvrent force selon vitesse/masse, traversée rapide, bornes de sillage, remise en service du champ et raffinement géométrique (limites conservées, ordre des sommets, pas proche et budget).

Compilation C# complète : **0 erreur**, avertissements existants hors du module. Compilation native des deux compute shaders et du shader d'eau : réussie via `EditorUtility.CompileShader`.

`run_native_water_interactions.ps1 -EditorPid <pid>` : **12 scénarios réussis le 21 octobre**, couvrant tirs, propagation CPU, particules, entrée/nage/sortie du joueur, repli éloigné, spectre conservé, volume natif et flottabilité de corps légers/denses. Le rejeu du 24 a été annulé proprement lorsque la scène active de l'éditeur a changé ; il n'est pas compté comme réussi pour le correctif courant.

`UniversalWaterNative.cs` est un corps de méthode pour `sbox-cli --call run_csharp` avec `path` absolu, dans une scène en Play. Il crée puis détruit uniquement ses objets temporaires et restaure les contrôles de caméra. Il valide prop sans composant d'interaction, collider déplacé sans Rigidbody, objet rendu sans collider, repos silencieux, ragdoll natif de 16 membres, grande empreinte physique, API, lecture GPU non nulle et finie, normales, propagation après émission, désactivation/réactivation et requêtes CPU. Les lectures GPU de ce fichier sont réservées au test.

Le dernier passage du banc générique a réussi **17 contrôles sur 17**, incluant deux liquides simultanés à des hauteurs différentes, un lac tourné de 35°, le solveur GPU sur une rivière inclinée avec courant et la survie après nettoyage/finalisation. Le banc attend une horloge physique active et maintient sa caméra malgré la création asynchrone du joueur.

`VehicleWaterRegression.cs` a réussi **8 contrôles sur 8** avec le véritable prefab `utility_vehicle`, son conducteur assis, les commandes d'accélérateur (aucune vitesse imposée), les quatre roues en eau peu profonde, la vraie tourelle, la lecture d'un champ GPU fini et non nul, vingt désactivations/réactivations et le nettoyage final. Dernier passage du 25 octobre avec le nouveau collecteur et l'optimisation CPU : **196 événements automatiques, 10 impacts de tirs et hauteur interactive maximale de 14,47 unités** au moment de la mesure. Le banc générique a également repassé ses **17 contrôles** avec ce code. Journaux structurés : `tmp/water-vehicle-final.json` et `tmp/water-universal-final.json`. Ce résultat ne remplace pas une appréciation visuelle prolongée en conduite.

`WaterDeformationNative.cs` a réussi **5 contrôles sur 5** avec FFT et écume à zéro et sans particules. La vue `SurfaceGeometry`, à caméra fixe et filtrée sur l'eau, montre les vraies normales des triangles. Elle valide le raffinement (61 074 sommets dans ce lac temporaire), les changements après impulsion et leur propagation sans nouvelle source, les crêtes/creux et l'absence de mousse directement peinte. Captures `tmp/water-geometry-flat.png`, `-impact.png`, `-propagated.png`. Les lectures GPU et captures sont réservées au test.

Le benchmark natif du 21 octobre, **avant le nouveau raffinement géométrique**, à 1280×720 a mesuré `RenderToTexture + FlushGPU` à **2,208 ms médian** avec l'eau complète, contre **1,502 ms** sans interactions. Il combine attente CPU et rendu et présente du bruit (le cas sans eau était à 2,657 ms) : ce n'est pas un profil GPU isolé ni une garantie de performance en jeu. Sur ce même essai, 2 000 requêtes CPU prenaient 38,973 ms et 120 mises à jour actives 53,557 ms. Le comportement avec beaucoup de surfaces et de joueurs doit encore être profilé dans une scène représentative.

Restent à valider visuellement sur la durée : qualité hydrodynamique des sillages des véhicules/bateaux, stabilité prolongée, coût du raffinement en scène chargée, niveaux de détail à distance, transition entre plusieurs liquides et cohérence à deux clients. La conduite du vrai véhicule est désormais testée ; aucun bateau réel n'a été essayé.
