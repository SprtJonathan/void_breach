# Combat, détection et dégâts localisés

Les soldats et zombies standards ont 80 PV, sans état à terre. Leurs dégâts de balle utilisent les hitboxes et les tags du moteur, conservés par `VBDamageInfo.FromNative` : tête/cou ×4, torse ×1, bras/jambes ×0,7. Le nom de l'os sert de secours sur les rigs dont les tags diffèrent. Le prélèvement reste unique et côté hôte, y compris pour les tirs distants validés par le moteur. Les capsules de locomotion `npc_body` sont exclues des traces des armes à feu ; les hitboxes du modèle animé fournissent la localisation. Le déplacement et les contacts physiques conservent leurs capsules.

| Arme existante | Dégâts de base | Headshots pour 80 PV | Impacts au torse |
| --- | --- | --- | --- |
| USP | 12 | 2 | 7 |
| P90 | 14 | 2 | 6 |
| MP5 | 16 | 2 | 5 |

Ces chiffres supposent des impacts complets, sans armure supplémentaire. Les dégâts à la tête traversent le tampon d'armure corporelle ; le casque natif est cosmétique. Les explosions et la mêlée ne reçoivent pas le multiplicateur de tête. La localisation est activée sur les deux prefabs ennemis ; le profil de santé du joueur conserve son réglage existant. Un futur ennemi peut utiliser un autre profil depuis `VBHealthComponent`.

Le soldat conserve les dégâts, chargeurs, cadences et rechargements du joueur. La pression baisse par son comportement : vision de 120° sur 1 200 unités, confirmation visuelle de 0,45 seconde, puis visée de 0,65 à 0,85 seconde avant le premier tir ou lancer. Un changement de cible ou une perte de vue de plus de deux secondes impose de réacquérir la visée ; une brève occultation pendant un peek conserve la préparation. Le suivi du point perçu est progressif, sans prédiction du déplacement. `Accuracy Scale = 2` double la dispersion PNJ précédente. Les rafales sont limitées à trois coups, avec les pauses natives de l'arme (0,25 à 0,6 seconde sur les armes livrées). Les armes lentes conservent leurs limites natives plus restrictives. Le couvert, la recherche, les factions et la mêlée restent actifs. Un tir entendu ou un dégât reçu alerte toujours l'ennemi, sans révéler les déplacements cachés.

Le soldat conserve la P90 choisie sur son prefab et le réglage de réserve du pistolet. Les champs de perception et de rythme de combat restent configurables dans l'inspecteur. La vérification chiffrée est disponible avec `python Tests/AI/validate_combat_balance.py`. Le harness `Tests/AI/VBCombatBalanceRegression.cs`, à copier temporairement dans `Code/AI`, expose `vb_combat_balance_regression` : il exerce l'adaptation native des dégâts, les morts en deux headshots, les impacts au torse/membres, l'absence de bonus explosif et le délai/rythme des rafales. La dernière exécution en Play hôte se termine avec zéro échec. Le ressenti sur la carte et le réseau restent à contrôler.

La passe suivante utilise une scène de comparaison MP5/P90/USP en Play. Le rythme de tir accéléré conserve ces dégâts, cette dispersion et les délais de première acquisition.
