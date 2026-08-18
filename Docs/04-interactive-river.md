# Rivière interactive

Ce module construit une rivière courbe à partir de points de contrôle. Il échantillonne le lit avec des traces verticales, encode la profondeur dans le maillage et ajoute des réactions visuelles aux berges, aux obstacles statiques et aux objets mobiles.

## Mise en place

1. Créer un GameObject vide nommé `River` et lui ajouter **Void Breach/River/River Water**.
2. Créer au minimum deux GameObjects servant de points de contrôle. Trois à six points donnent généralement une courbe plus naturelle.
3. Ajouter **River Control Point** sur chacun d'eux et régler sa largeur locale.
4. Parenter ces GameObjects sous `River`, dans l'ordre de l'amont vers l'aval. Si tu préfères les garder ailleurs dans la hiérarchie, glisse-les explicitement dans la liste **Control Points**.
5. Placer chaque point à la hauteur exacte de la surface. Leur Z peut descendre progressivement avec le terrain.
6. Vérifier que le lit et les berges possèdent des colliders statiques : ils sont nécessaires au calcul de profondeur.

Lorsque la liste **Control Points** est vide, les enfants directs possédant `River Control Point` sont utilisés dans leur ordre hiérarchique. Le ruban est régénéré automatiquement lorsque les points, leur largeur ou les paramètres géométriques changent.

## Profondeur et berges

Pour chaque sommet, le composant lance une trace verticale jusqu'à **Maximum Depth**. La distance touchée est normalisée avec **Deep Color Depth** puis transmise au shader : l'eau peu profonde prend `Shallow Color`, les fosses prennent `Deep Color`.

La proximité des bords du ruban produit aussi une zone moins profonde, plus rugueuse et écumeuse. Pour éviter une limite parfaitement droite, placer les points près du centre du lit et laisser le terrain recouvrir légèrement les côtés du ruban.

Si le terrain change après la création de la rivière, déplacer brièvement un point de contrôle ou modifier un réglage géométrique pour provoquer un nouveau scan.

## Obstacles statiques

Ajouter **River Obstacle** aux rochers, souches ou troncs situés dans l'eau. `Radius` contrôle la zone perturbée et `Strength` la quantité de remous et d'écume. Le shader gère jusqu'à huit obstacles par rivière.

Le résultat est un sillage visuel orienté par la tangente locale de la rivière. Il ne s'agit pas d'une simulation CFD : l'obstacle ne modifie pas physiquement le débit ni la trajectoire du ruban.

## Personnages et objets mobiles

Ajouter **River Water Interactor** au joueur ou aux prefabs physiques qui doivent perturber l'eau. Assigner la rivière dans `River`, ou laisser vide pour utiliser la première rivière trouvée dans la scène.

L'interactor mesure la vitesse du `Rigidbody` ou le déplacement du GameObject. Lorsqu'il se trouve dans la largeur et près de la hauteur de l'eau, il injecte une ride circulaire temporaire. Huit rides peuvent être visibles simultanément.

## Réglages de départ White Forest

- Largeur des points : `300` à `520` unités.
- `Samples Per Segment` : `10` à `16`.
- `Cross Segments` : `6` à `10`.
- `Flow Speed` : `1.1` à `1.8`.
- `Surface Amplitude` : `2` à `5`.
- `Maximum Depth` : `256` à `512`.
- `Deep Color Depth` : `160` à `280`.

Un ciel couvert, des couleurs légèrement vertes/brunes, des rochers humides et une brume basse conviendront mieux à White Forest qu'une eau océanique très bleue.

## Limites actuelles

- La profondeur agit sur la couleur, la transmission PBR et l'écume, mais la surface reste opaque pour conserver un tri et un rendu stables.
- Le système ne fournit pas encore de nage, de flottabilité, de force de courant ou de réfraction réelle du décor.
- Les interactions sont visuelles et locales. Leur réplication multijoueur pourra être ajoutée quand une carte utilisera réellement le système.

Ces fonctions physiques pourront être ajoutées plus tard avec un volume d'eau, des forces calculées depuis la tangente de la rivière et des interactions réseau autoritaires.
