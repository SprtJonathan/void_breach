# Eau stagnante

Le composant `Stagnant Water` génère une surface d’eau rectangulaire et son volume physique. Il utilise le `WaterVolume` natif de s&box pour la flottabilité, la densité et les traînées linéaire/angulaire.

## Mise en place

1. Créer un GameObject vide à la hauteur exacte de la surface de l’eau.
2. Ajouter le composant **Void Breach / Water / Stagnant Water**.
3. Régler `Surface Size` et `Depth`. Le volume s’étend vers le bas depuis le GameObject.
4. Laisser `Material Override` vide pour utiliser le matériau procédural inclus, créé à partir de l’image de référence fournie. L’asset `stagnant_water_default.vmat` est également fourni pour être glissé explicitement dans ce champ ou réutilisé ailleurs.

Le composant ajoute et configure automatiquement un `BoxCollider` trigger, un `WaterVolume` natif et un `ModelRenderer`, puis applique le tag `water`. Les rigidbodies présents dans le volume flottent et créent des rides visibles lorsqu’ils se déplacent ; le mode de nage du `PlayerController` peut également reconnaître le volume via ce tag. Pour conserver l’alignement entre surface visuelle et physique, garder le GameObject horizontal.

## Matériau personnalisé

Un autre matériau peut être glissé dans `Material Override`. Les paramètres visuels du composant pilotent le shader inclus ; un matériau utilisant un shader différent reste utilisable, mais ignorera naturellement ces paramètres spécifiques.

## Réglages utiles

- `Fluid Density` : 1000 correspond à l’eau douce.
- `Linear Drag` et `Angular Drag` : résistance de l’eau sur les objets.
- `Surface Amplitude`, `Surface Scale`, `Surface Speed` : mouvement lent de l’eau stagnante.
- `Ripple Strength`, `Ripple Lifetime`, `Ripple Speed` : réaction visuelle aux objets.
- `Reference Influence` : présence de la texture humide/graveleuse issue de l’image fournie.
