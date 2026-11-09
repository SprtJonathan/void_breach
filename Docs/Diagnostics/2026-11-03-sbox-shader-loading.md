# Shaders invisibles dans l’éditeur s&box 26.11.03

Diagnostic réalisé le 3 novembre 2026 sur Void Breach, avec s&box public build Steam `26414528`.

## Symptôme et cause observée

Dans `basic_example`, la géométrie, les personnages et le véhicule disparaissent alors que les gizmos et le ciel restent visibles. La console répète :

```text
DispatchComputeShader failed - bound pipeline does not have a compute shader
Attempting to create a Vulkan compute pipeline without a compute shader bound, aborting pipeline creation.
Failed to create Vulkan pipeline object.
```

Ces messages suivent un échec de chargement des shaders internes du moteur, dès le démarrage, avant le chargement du projet :

```text
Resource type compiler identifier not found: "ResourceCompiler"
Failed to recalculate special dependency "Compiled reads recorded" ... Forcing recompile.
On-demand recompile of asset shaders/skinning_cs.shader
Failed on-demand recompile for asset shaders/skinning_cs.shader
Error loading resource file "shaders/skinning_cs.shader_c" (Error: ERROR_FILEOPEN: File not found)
```

Le même problème touche notamment `depthaware_mask_cs`, `gtao_cs`, les shaders DDGI et le bloom. Les fichiers compilés correspondants existent pourtant dans `core/shaders`. Le compilateur officiel `ShaderCompiler.exe` les considère à jour. Le message « File not found » est donc trompeur dans ce cas : l’éditeur refuse leur chargement après avoir échoué à les recompiler.

Cela localise le blocage dans le contrôle de péremption et le chargement des shaders de cette installation du moteur. Une régression de la mise à jour est probable ; aucune modification du gameplay n’a été nécessaire pour rétablir le rendu.

## Contournement local appliqué et vérifié

Fichier de l’installation modifié :

```text
D:\Steam\steamapps\common\sbox 590830\bin\assettypes.txt
```

Dans le bloc `CShaderAssetTypeInfo` / `m_Name = "shader_asset"`, une seule propriété a été ajoutée :

```text
m_bSuppressFullFingerprintCalculation = true
m_bIgnoreCompiledState = true
```

Cette option évite le contrôle de péremption défaillant pour le type shader. Elle ne répare pas le compilateur natif et constitue un contournement temporaire. Elle s’applique à cette installation s&box, pour tous les projets qui l’utilisent. Elle peut aussi empêcher la détection automatique de shaders réellement périmés : retirer le contournement après une correction du moteur et recompiler explicitement les shaders modifiés si nécessaire.

Après redémarrage propre de l’éditeur et réouverture du projet :

- `basic_example` affiche de nouveau le décor, le véhicule et les personnages depuis la même vue qu’avant le test.
- `soldier_test` charge et affiche de nouveau sa géométrie.
- Le nouveau log ne contient plus d’erreur de compilateur introuvable, de dispatch compute ni de création de pipeline Vulkan.
- Quelques références à des ressources manquantes subsistent, notamment des matériaux du P90. Elles sont distinctes du blocage global du rendu.

Validation limitée au chargement et au rendu dans l’éditeur ; aucune session multijoueur complète n’a été testée.

## Sauvegarde et retour arrière

Les fichiers du diagnostic sont conservés dans `tmp/sbox-shader-loading-2026-11-03/` :

- `assettypes.original.txt` : copie exacte avant modification.
- `apply-workaround.ps1` : applique la modification uniquement si le fichier correspond encore à la sauvegarde.
- `restore-original.ps1` : restaure l’original uniquement si le fichier correspond encore au contournement attendu.

SHA-256 de l’original :

```text
47241CE13B7C521F13F257C26136FA11F6883B40AE16DFCECCA002310209CFF4
```

Pour revenir à l’original, fermer l’éditeur, exécuter le script suivant dans PowerShell, puis relancer s&box :

```powershell
& 'D:\Docs\s&box projects\void_breach\tmp\sbox-shader-loading-2026-11-03\restore-original.ps1'
```

Une mise à jour ou une vérification des fichiers Steam peut rétablir `assettypes.txt` et retirer le contournement. Les scripts refusent d’écraser un fichier qui aurait changé entre-temps.
