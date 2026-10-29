# Apparence FPS et prise d’arme

Le rendu des bras FPS utilise les composants natifs de s&box :

- Le viewmodel d’origine conserve ses animations.
- Un `SkinnedModelRenderer` utilisant le modèle du personnage est lié au squelette FPS avec `BoneMergeTarget`.
- Un `Dresser` natif reçoit la tenue et les paramètres d’apparence du personnage. Il crée et fusionne lui-même les vêtements.
- Les parties tête, jambes et pieds du rendu FPS sont masquées ; les vêtements sans os de bras sont désactivés.
- Le maillage nu intégré au viewmodel est masqué, sans désactiver son squelette ni son animation.

L’adaptateur `VBFirstPersonHandsAppearance` réapplique la tenue uniquement lorsque la source change. Il ne copie pas les poses des bras et ne possède pas de système de pose exécuté en fin de frame. Le modèle et les vêtements doivent être compatibles avec les noms d’os Human des viewmodels.

Les vêtements complets contiennent aussi un torse. Sur les rendus FPS des vêtements, les os du corps absents du squelette FPS sont ramenés à l’origine avec une échelle très petite. Les os des bras, mains, doigts et leurs helpers sont conservés ; leur animation reste fournie par `BoneMergeTarget`. Les vêtements sont fusionnés directement avec le squelette FPS, car la physique du modèle Human intermédiaire rétablirait autrement les os du torse à chaque image. Ce masque fixe est réappliqué après le Dresser lorsque la tenue change. Il ne touche ni les ressources de vêtements, ni le corps TPS, ni les PNJ.

La prise d’arme du joueur réutilise les corrections de montage des armes existantes. La fixation entre les mains reste celle du graphe Human, piloté par les paramètres natifs `HoldType` et `Handedness`. Son IK relatif à la main droite est évalué dans la même pose que celle qui porte l’arme. Les soldats conservent leur cible de support spécifique.

L’IK supplémentaire du joueur, son helper ajouté et le système de copie de pose ont été retirés. Une cible mondiale attachée à l’arme réinjectait la pose de l’image précédente et introduisait un retard pendant les rotations. Aucun changement de l’animation de marche n’est nécessaire.

## Vérification

`Tests/Weapons/NativeHandsInspection.csx` est un script d’inspection en Play hôte, avec une place et un budget de port disponibles. Il laisse le moteur mettre à jour les poses et mesure le déplacement de l’os réel `hand_L` dans le repère de l’arme pendant des rotations de caméra. Il fournit les mesures brutes et des captures FPS ainsi qu’une vue latérale de la main gauche en TPS, sur MP5, P90, pistolet et M4A1. Il restaure les contrôles, le mode de vue et l’inventaire. La stabilité d’un poignet n’établit pas à elle seule un contact correct : les captures doivent aussi être inspectées.

Les captures FPS sont prises en 4:3, où le champ vertical plus large exposait le torse de la veste SWAT avec le MP5. Elles permettent de contrôler que le masque retire ce débordement tout en conservant les manches et gants. Aucune variante de manches dédiée n’est nécessaire.

Les anciens tests de suivi de copie et d’attache de modèle ont été retirés : leurs résultats ne démontraient ni la présence des bras dans l’image rendue ni la fixation de la main gauche sur l’arme. Une compilation ou un écart nul entre une copie et son squelette ne remplace pas l’inspection du rendu en mouvement.
