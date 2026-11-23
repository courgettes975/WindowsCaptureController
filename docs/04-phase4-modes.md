# Phase 4 — Modes de capture, délai, multi-écran : résultats

Date : 2026-11-23. Plugin **0.4.0**, chargé par Logi Plugin Service (lien de développement), agent qui répond.

## Nouvelles actions (groupe « Windows Capture » dans Options+)

| Action | Comportement | Icône LCD |
|---|---|---|
| Capture rectangulaire | `Win+Maj+S`, puis mode Rectangle par UI Automation | rectangle pointillé |
| Capture de fenêtre | idem, mode Fenêtre | deux fenêtres superposées (fond gris) |
| Capture de forme libre | idem, mode Forme libre | contour libre pointillé |
| Plein écran | selon `multiMonitorMode` / `fullscreenOutput` (voir plus bas) | quatre coins |
| Capture dans 3 / 5 / 10 secondes | compte à rebours **sur la touche** (10 … 1, cadre rouge), puis sélection dans le mode `defaultCaptureMode` | chronomètre |
| Annuler délai | annule le compte à rebours en cours (la touche devient rouge pendant un compte à rebours) ; un nouvel appui sur la touche de délai annule aussi | croix |
| Dernière capture | ouvre l'image la plus récente (session, dossier configuré, Captures d'écran, Images ; premier niveau uniquement) | cadre photo |

Le compte à rebours ne bloque pas le plugin : c'est une tâche asynchrone, annulable instantanément. Les délais 3, 5 et 10 s sont ceux
que propose l'Outil Capture installé (mesuré en phase 1).

## Plein écran et multi-écran

| `multiMonitorMode` | `fullscreenOutput` | Méthode |
|---|---|---|
| `all-screens` (**défaut**) | `file` (**défaut**) | `Win+Impr.écran` documenté : Windows enregistre le PNG dans le dossier Captures d'écran |
| `primary`, `pointer-monitor`, `active-monitor`, ou `all-screens` avec sortie presse-papiers | `file`, `clipboard`, `both` | **module de capture indépendant** (l'Outil Capture ne sait pas capturer un seul écran) : copie d'écran GDI en pixels physiques (agent DPI-aware PerMonitorV2), PNG ou JPEG nommé `Capture_AAAA-MM-JJ_HH-MM-SS.png`, presse-papiers remplacé seulement si l'image est valide |

« active-monitor » = l'écran qui contient la plus grande partie de la fenêtre active ; à défaut, l'écran du pointeur, puis l'écran principal.

## config.json

`%LOCALAPPDATA%\WindowsCaptureController\config.json`, créé au premier démarrage de l'agent et relu à chaque action
(une modification s'applique sans rien redémarrer). Validation stricte, **champ par champ** : une valeur invalide est
remplacée par sa valeur par défaut, un avertissement est écrit dans `wcc.log`, et les autres champs sont conservés.
Les clés inconnues sont signalées. Un fichier illisible fait utiliser les valeurs par défaut, sans planter. Clé ajoutée au cahier des charges : `fullscreenOutput`.

## Vérifications

| Vérification | Résultat |
|---|---|
| Build | ✅ 0 erreur, 0 avertissement |
| Tests unitaires | ✅ **44 / 44** (+16 : validation de la configuration, choix de l'écran sur la disposition réelle des 3 écrans, noms de fichiers, dernière capture, non-écrasement) |
| Module multi-écran, sur la machine | ✅ `primary`, `pointer-monitor`, `active-monitor` → 1920×1080 ; `all-screens` → 5760×1080 ; 84–332 ms ; sous surface synthétique (contenu 100 % synthétique) ; dossier temporaire supprimé ; dossier Captures non touché |
| Rectangle / Fenêtre / Forme libre via l'agent | ✅ mode sélectionné et confirmé (9 essais réussis avec la liste dépliée) |
| Plugin 0.4.0 chargé | ✅ « version '0.4.0' loaded … in 291 ms », agent ping OK |
| **Compte à rebours, annulation, appui réel sur les touches** | ⏸ **à vérifier par vous au Keypad** (le plugin ne peut pas être piloté sans appui physique) |

## Corrections apportées pendant les tests

1. **Overlay en cours de fermeture** : un appel arrivé pendant qu'un overlay précédent se fermait a provoqué une `COMException`.
   L'agent **attend désormais la fin de la fermeture** (1,5 s au maximum), **n'accepte qu'un overlay apparu après** `Win+Maj+S`
   (comparaison des handles), laisse l'overlay se stabiliser 300 ms avant de toucher au menu, et **ne réessaie jamais** après une
   erreur UI Automation : il journalise l'étape et laisse l'overlay dans son mode actuel.
2. Sélection « liste repliée » (`ItemContainer` + `VirtualizedItem`) **essayée puis rejetée** : 2 échecs sur 6. On garde la liste dépliée.
3. L'étape de compilation qui arrête l'agent de développement renvoyait le code 1 quand aucun agent ne tournait (avertissement MSB3073) : corrigé.

## Important : les captures inattendues pendant les tests

Toutes les captures imprévues de cette session (phases 2 à 4) s'expliquent très probablement de la même façon :
**pendant mes tests, l'overlay couvrait vos 3 écrans plusieurs secondes et vos clics de souris devenaient des captures**.
Un clic donne un petit rectangle en mode Rectangle, ou la fenêtre survolée en mode Fenêtre. Les tailles croissantes
(578 → 2 807 octets) correspondent à des mouvements de souris réels.

* Les 6 fichiers créés en phase 4 (12:56:37, 12:58:37, 13:01:12, 13:01:19, 13:01:26, 13:01:32) ne contiennent que la surface
  synthétique verte, ou un gris uniforme sans information.
* **Règle désormais appliquée : je ne lance plus de test qui ouvre l'overlay sur votre machine pendant que vous l'utilisez.**
  Ces vérifications se font avec vous, au Keypad, ou sur une session dédiée.
* Ce comportement est normal en usage réel : l'overlay attend justement votre clic.

## À vérifier au Keypad

1. Assignez dans Options+ : Rectangle, Fenêtre, Forme libre, Délai 5 s, Annuler délai, Dernière capture.
2. **Délai 5 s** : la touche affiche 5, 4, 3, 2, 1 avec un cadre rouge, la touche « Annuler » devient rouge, puis la sélection s'ouvre.
3. **Délai 5 s puis Annuler** : la touche affiche « Annulé » et aucune sélection ne s'ouvre.
4. **Fenêtre** : la sélection s'ouvre en mode Fenêtre ; cliquez une fenêtre pour la capturer ou appuyez sur Échap.
5. **Plein écran** : par défaut, il enregistre tous les écrans dans le dossier Captures. Pour un seul écran, mettez
   `"multiMonitorMode": "active-monitor"` dans config.json.
