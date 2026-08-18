# Océan Gerstner

Ce module fournit une surface d'océan autonome, sans texture obligatoire. Une grille procédurale est créée au démarrage puis quatre vagues de Gerstner la déforment sur le GPU. Le shader utilise le pipeline VFX/HLSL actuel de s&box et est compilé par Slang.

## Installation dans une scène

1. Ouvrir la scène concernée dans l'éditeur s&box.
2. Créer un GameObject vide et le nommer `Ocean`.
3. Placer ce GameObject à la hauteur d'eau souhaitée. Sa coordonnée Z définit le niveau moyen de la mer.
4. Cliquer sur **Add Component**, chercher **Gerstner Ocean**, puis ajouter le composant `Void Breach/Ocean/Gerstner Ocean`.
5. Conserver une rotation nulle et une échelle de `(1, 1, 1)`. Utiliser la propriété **Size** plutôt que l'échelle du GameObject.
6. Sauvegarder la scène et lancer le jeu. Le composant est aussi visible en mode édition grâce à `ExecuteInEditor`.

Le `ModelRenderer` nécessaire est ajouté automatiquement. Aucun modèle, matériau ou texture supplémentaire n'est requis.

## Réglages conseillés

- **Size** : largeur totale de la surface. La valeur par défaut `12000` couvre environ 300 mètres. Pour un plan plus grand, augmenter aussi la résolution ou utiliser plusieurs tuiles afin de conserver les petites vagues.
- **Resolution** : densité géométrique. `160` à `192` est un bon compromis; `224` à `256` donne de meilleures silhouettes à courte distance mais augmente le nombre de sommets.
- **Wind Direction** : direction XY principale des vagues.
- **Wave Amplitude** : hauteur de la houle en unités Source.
- **Wave Length** : distance entre les grandes crêtes.
- **Wave Speed** : vitesse globale de l'animation.
- **Choppiness** : déplacement horizontal et netteté des crêtes. Rester sous `0.85` évite la plupart des retournements de surface.
- **Deep/Shallow/Horizon Color** : palette de l'eau et teinte du reflet rasant.
- **Roughness** : netteté des reflets PBR. Une valeur comprise entre `0.05` et `0.12` fonctionne bien.
- **Foam Threshold/Intensity** : apparition et force de l'écume analytique sur les crêtes raides.

## Éclairage réaliste

Pour que le résultat soit convaincant, la scène doit contenir au minimum un `Directional Light`, une skybox/atmosphère et des réflexions d'environnement correctement configurées. Le shader repose sur le modèle d'éclairage standard de Source 2, donc la qualité de l'environnement influence directement les reflets.

La surface est volontairement opaque afin d'éviter les problèmes de tri des très grands plans transparents. Elle simule la profondeur par sa couleur, sa transmission PBR, le Fresnel et l'écume. Une vraie réfraction du décor ou une vue sous-marine demanderait une seconde étape avec capture de couleur/profondeur et un volume d'eau dédié.

## Organisation

- `Code/Ocean/GerstnerOcean.cs` : génération de la grille et réglages d'inspecteur.
- `Assets/ocean/shaders/gerstner_ocean.shader` : déformation Gerstner et rendu PBR.

Le shader n'emploie aucune construction spécifique à DXC. Il conserve la syntaxe HLSL/VFX documentée par s&box, désormais compilée et hot-reloadée par Slang.
