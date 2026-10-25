# Optimisation de l'océan — 25 octobre 2026

Moteur installé : 26.10.13c, shaders compilés par Slang. Cette passe conserve la FFT à deux cascades 128², les deux champs interactifs et le maillage raffiné de **72 361 sommets**. Les gains proviennent du travail supprimé dans les chemins existants.

## Diagnostic et protocole

La copie Play de `basic_example` ouverte pour ces essais ne contenait plus de composant FFT. `Tests/Ocean/OceanOptimizationProfile.cs` instancie temporairement le véritable prefab océan en `(0,0,-32)`, puis le détruit. Deux caméras fixes couvrent le large et le rivage ; chaque configuration attend 45 images, puis en mesure 150. La résolution reste celle du viewport de l'éditeur. Aucune scène n'est sauvegardée.

Les phases comparent l'eau complète, les caustiques coupées, un maillage moins dense, la vue géométrique, les interactions coupées, la physique coupée, la surface masquée, l'eau désactivée, puis une répétition complète. Les sections CPU viennent de `PerformanceStats.Timings`. Les timestamps GPU natifs sont collectés dans une fenêtre `PerfHub.CollectFor("gpu", ...)`, avec dédoublonnage par image. La réflexion vers cette API interne reste réservée au script d'éditeur. Les scopes sont inclusifs : ne pas les additionner. Le premier essai sans fenêtre de collecte n'a fourni aucun snapshot GPU et n'est pas retenu pour les durées GPU.

Le diagnostic CPU séparé a trouvé **520 colliders en contact, 326 corps dynamiques, dont 197 endormis** lors d'une observation. Masquer le renderer conservait une grande partie du ralentissement : le shader de surface n'était pas son unique cause.

## Physique CPU

`FftSurfaceSampler.SampleForPhysics` partage des plans tangents FFT dans des cellules de **8 unités**, entre les corps proches. Chaque changement de temps, de spectre, de vent, de choppiness ou de pas invalide les entrées concernées. La mémoire est bornée à 1 024 entrées ; les pentes très raides reviennent au calcul direct.

Seul `VBFluidBody` utilise cette approximation. Domaines, fonds et champs interactifs restent échantillonnés à la position réelle. `TrySample`, la nage, les tirs et le déplacement GPU conservent leurs chemins exacts existants. Le réglage inspecteur **PhysicsSampleSpacing = 0** coupe le cache approximatif pour une surface nécessitant la flottabilité exacte.

Les corps mobiles ou mouillés sont évalués à chaque pas physique. Un corps endormi et précédemment sec est revérifié à 10 Hz : la détection d'une montée d'eau peut attendre environ 100 ms, avant le mouvement physique. Une boîte entièrement au-dessus du plan tangent évite l'appel natif de flottabilité sans effet. Les corps proxies, sans mouvement physique et les surfaces sans physique native évitent le travail Fourier inutile dans ce chemin.

`VBFluidVolumeBridge` inscrit toujours les nouveaux contacts immédiatement par événement ; sa réconciliation des colliders déjà présents passe à 4 Hz pour récupérer les composants remplacés/désactivés.

Le cache de 32 unités initialement essayé a été rejeté : jusqu'à 4,64 unités d'écart de hauteur et des normales trop éloignées. Les trois prefabs enregistrent explicitement **8**, pour éviter aussi les anciennes valeurs de prototypes conservées en mémoire après hot reload. À ce pas, 6 000 comparaisons sur trois instants, le spectre de référence de 1,2 m et 128 modes ont mesuré au maximum **0,3901 unité de hauteur** (environ 1 cm), **0,1748 de distance entre normales unitaires** (environ 10°), et **10,151 unités/s de vitesse**. Ces maxima observés ne constituent pas une borne mathématique pour tous les réglages de mer. Les mers très agitées et configurations personnalisées peuvent justifier le mode exact.

## Shader

- Chemin de déplacement dédié dans le vertex shader FFT.
- Lecture du champ grossier évitée au centre du champ fin, où le poids fin vaut un ; les bords conservent le mélange et son gradient.
- Écume interactive réutilisée depuis `Field`, sans seconde lecture dédiée.
- Deux lectures de petites normales évitées lorsqu'elles sont entièrement atténuées. Les gradients explicites sont calculés avant le branchement pour conserver des mips valides.
- Texture de couleur ignorée lorsque son influence est nulle ; éclairage indirect diffus de l'écume évité sur les pixels sans écume. Le chemin natif des sondes pour les reflets reste actif.
- Distance caméra réutilisée et lecture RMA inutilisée retirée du source. Le compilateur pouvait déjà éliminer cette dernière : aucun gain spécifique ne lui est attribué.

## Mesures

Comparaison des secondes phases complètes avant / version finale avec les prefabs enregistrés à huit unités :

| Poste | Large avant | Large après | Rivage avant | Rivage après |
| --- | ---: | ---: | ---: | ---: |
| Image complète médiane | 19,68 ms | 11,89 ms | 25,34 ms | 22,27 ms |
| Section CPU Update | 10,65 ms | 3,50 ms | 12,14 ms | 8,46 ms |
| Scope GPU Translucent Forward | 1,43 ms | 1,30 ms | 0,55 ms | 0,79 ms |

La vue au large montre un gain CPU marqué et environ 9 % de réduction du scope transparent observé. Le rivage ne démontre **pas** de gain GPU sur ce passage. L'éditeur a changé de charge pendant les essais et la scène conserve sa simulation : la médiane sans eau est passée de 6,74 à 11,12 ms au large et de 7,89 à 12,52 ms au rivage. Ces mesures ne garantissent donc pas un niveau de FPS, ne représentent pas une charge parfaitement identique et n'isolent pas chaque compute shader. Les mesures froides et les versions intermédiaires au cache de 32 unités ne servent pas de résultats finaux.

Journaux : `tmp/ocean-optimization-before-timings.json`, `tmp/ocean-optimization-delivery-timings.json`. Restent à profiler le LOD géométrique, le matériau à haute résolution, beaucoup de surfaces et les mers très agitées. Le raffinement existant est conservé pour les petites déformations réelles.

## Fichiers et vérifications

Modifiés : `Assets/water/shaders/vb_water.shader`, `Assets/water/shaders/fft/surface.hlsl`, les trois prefabs FFT, `Code/Ocean/FftSurfaceSampler.cs`, `VBFFTOcean.cs`, `VBFFTOcean.Physics.cs`, `Code/Water/VBFluidBody.cs`, `VBFluidVolumeBridge.cs`, `FluidDiagnostics.cs`, `Tests/Ocean/FluidSurfaceChecks.cs` et les documentations FFT. L'éditeur régénère les assets compilés.

Ajoutés : ce document, `Tests/Ocean/OceanOptimizationProfile.cs`, `OceanCpuProfile.cs` et `WaterSleepingBodyNative.cs`. `vb_fft_water_check` affiche désormais le pas et les hits/misses du cache pour le temps Fourier courant.

- Compilation C# : **zéro erreur, 12 avertissements existants**. Shader d'eau compilé par Slang dans l'éditeur : dix combinaisons réussies.
- **427 907 contrôles FFT + 13 157 contrôles de surface/physique**, soit **441 064 réussites**. Le cache est comparé au calcul direct, avec vérification de sa mémoire, de son invalidation et du retour au mode exact.
- **17 interactions génériques**, **5 déformations géométriques**, **8 conduite/tourelle/recréations GPU**, **2 contrôles de corps endormi/réveil par montée d'eau**, tous réussis.
- Le premier banc de naufrage/sortie a été interrompu par un changement de scène. Son rejeu final a réussi **14 contrôles sur 14**, incluant le naufrage sur 45 s et toutes les sorties de siège. Journal : `tmp/ocean-optimization-flooding-exit-final.json`.

La cohérence multijoueur et la stabilité sur une longue session restent à valider pour cette version.

Après arrêt de Play : **zéro lot GPU en attente, zéro lot en erreur**, 156 lots libérés dans cette instance. Aucune des erreurs natives de textures recherchées n'apparaît dans le journal pendant cette séquence. L'éditeur reste ouvert sur `basic_example`, Play arrêté ; les modifications non enregistrées de l'autre scène ouverte sont préservées.
