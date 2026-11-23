# Phase 7 — Molette (MX Creative Dialpad) : résultats

Date : 2026-11-23. Plugin **0.7.0**. Rappel : **le MX Creative Keypad n'a pas de molette**. La molette est celle du
**MX Creative Dialpad**, connecté sur ce poste. L'état s'affiche sur une touche du Keypad, car le Dialpad n'a pas d'écran.

## Actions (groupe « Windows Capture - Molette »)

| Action | Rotation | Appui |
|---|---|---|
| **Molette Outil Capture** (mode choisi par la touche « Mode molette ») | selon le mode | selon le mode |
| Molette : zoom | zoom avant / arrière (menu Zoom de l'éditeur) | 100 % |
| Molette : épaisseur | stylo ou surligneur ±2 px par cran (borné 1–24 / 12–64) | épaisseur par défaut (4 / 12) |
| Molette : couleur | couleur suivante / précédente, appliquée immédiatement | confirmation (« Rouge ✓ ») |
| Molette : historique | parcourt les 30 dernières captures et vidéos (« 3/20 ▣ 11:30 ») | ouvre l'élément choisi |
| Molette : délai | 0 / 3 / 5 / 10 s | capture après le délai ; nouvel appui pendant le compte à rebours = annulation |
| **Mode molette** (touche du Keypad) | — | Zoom → Épaisseur → Couleur → Historique → Délai ; la touche affiche le mode et la dernière valeur |

* **Appui long : non utilisé.** Le SDK ne fournit pas d'événement d'appui long fiable pour la molette. « Ajuster à la fenêtre »
  reste disponible par touche (« Ajuster »).
* **Rotation de la règle : non implémentée** (la règle n'est pas exposée à UI Automation, voir phase 6).
* **Historique** : noms, dates et types seulement, **pas de miniatures** (choix de performance). L'agent n'ouvre qu'un nom de fichier
  simple situé dans les dossiers de captures et de vidéos : `..\`, chemins absolus et séparateurs sont refusés (testé).

## Rotation lente / rapide (anti-saturation)

`DialAccumulator` (code partagé, testé) :

* **un seul appel à l'Outil Capture à la fois** : les crans arrivant pendant un appel sont cumulés ;
* **zoom : 4 pas au maximum par lot**, les crans en trop sont **abandonnés**, pour qu'un tour rapide ne laisse pas une file de zooms
  continuer après l'arrêt de la molette ;
* épaisseur et couleur : un seul appel avec le cumul (+2 px par cran), borné par l'Outil Capture ;
* **sensibilité** (`dialSensitivity`, 1–10 crans par pas, le reste est conservé) et **sens** (`dialDirection`: `normal` / `inverted`)
  dans `config.json`, relus à chaque changement de mode ;
* sans image ouverte, le refus est immédiat (≈ 30 ms) et le reste du tour est abandonné.

## Mesures (agent déployé, image synthétique)

| Opération | Résultat |
|---|---|
| 4 pas de zoom (lot maximal) | ≈ 200–430 ms par pas après le premier (≈ 1 s le lot) |
| Épaisseur +40 / −40 / défaut | 24 / 1 / 4 (bornes respectées) |
| Couleur −3 / +3 | Gris foncé → Rouge (retour exact) |
| Sans image | refus en ≈ 30 ms par appel |
| Historique (5 éléments) | liste triée, noms non journalisés |

## Tests

**82 / 82** (+16) : accumulateur (lent, rapide, plafond, sensibilité, sens, remise à zéro), sécurité des noms de fichiers de
l'historique, liste récente bornée et sans doublons. `Install.ps1` vérifie désormais le chargement dans **notre** journal
(lignes postérieures au lancement), car celui du service peut être recréé au moment exact du rechargement.

## À vérifier au Dialpad / Keypad

1. Assignez **« Molette Outil Capture »** à la molette du Dialpad et **« Mode molette »** à une touche du Keypad.
2. Ouvrez une capture. En mode Zoom, tournez lentement puis vite (le zoom doit s'arrêter avec la molette), puis appuyez pour revenir à 100 %.
3. Mode Épaisseur, puis Couleur : la touche du Keypad affiche « 8 px » puis le nom de la couleur.
4. Mode Historique : tournez (« 1/30 ▣ 05:59 »), puis appuyez pour ouvrir.
5. Mode Délai : tournez jusqu'à « Délai 5 s », appuyez (compte à rebours), puis appuyez de nouveau pour annuler.
