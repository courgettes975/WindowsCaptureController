# Phase 8 — Touches LCD : icônes, états, profils contextuels

Date : 2026-11-23. Plugin **0.8.0**.

## Icônes : une seule source, en SVG

* **41 sources SVG** dans `assets/icons/` (une par glyphe, `viewBox 0 0 100 100`). Ce sont des dessins originaux, simples et très contrastés,
  sans aucune bibliothèque d'icônes tierce : aucune question de licence. Le script `scripts/New-IconSources.ps1` les régénère.
* Le plugin les embarque comme ressources et les rend avec l'API documentée **`BitmapImage.FromSvg`** du PluginApi installé.
  Vérifié au chargement dans le service : **« SVG icons rendered by the SDK: 41/41 »**.
* Les couleurs suivent l'état grâce à des jetons remplacés à la volée : `FG` (blanc / gris indisponible), `ACCENT` (encre choisie,
  état), `BG` (fond de la touche), `SW` (épaisseur du trait pour l'icône Épaisseur). Les images sont mises en cache (bornées à 600).
* Secours : si un SVG manquait ou était refusé, l'ancien dessin vectoriel (`Icons.DrawGlyph`) prend le relais ; une touche n'est
  jamais vide.
* **Planche de contrôle** : `docs/icons-preview.html` (générée par `scripts/New-IconPreview.ps1`) affiche chaque icône à la taille d'une
  touche (116 px) et en petit, en trois états : normal, actif, indisponible. Relue : toutes lisibles. Les repères de couleur fixes
  (dossier jaune, point rouge, alerte orange, coche verte) ne grisent pas, volontairement.

Icônes demandées dans le cahier des charges, toutes présentes : ciseaux, rectangle, fenêtre, plein écran, forme libre, délai,
caméra, démarrer, arrêter, pause, reprendre (lecture), micro actif, micro coupé, stylo, surligneur, gomme, règle, annuler, rétablir,
copier, enregistrer (et enregistrer sous), dossier, partage, texte/OCR, masquage, zoom +, zoom −, zoom 100 %, ajuster,
paramètres, diagnostic, erreur, succès. Le rapporteur n'a pas d'icône, puisque la fonction n'existe pas.

## États visuels des touches

| Situation | Rendu |
|---|---|
| Action réussie | cadre **vert** 1,5 s (« Copié ✓ » pour Copier) |
| Action indisponible ou impossible | cadre **orange** + « N/D », ou « Erreur » |
| Confirmation (abandonner, tout effacer) | « Encore 1x » en orange, 3 s |
| Outil actif | cadre bleu ; gomme active sur fond bleu |
| Annuler / Rétablir indisponibles | icône grise |
| Enregistrement en cours | « Arrêter » sur fond rouge + minuteur en coin |
| Pause | icône Lecture « Reprendre », cadre orange |
| Compte à rebours | chiffres géants, cadre rouge |
| Erreur de rendu | triangle d'alerte « Erreur » |

Aucune animation : le SDK n'en propose pas. Les changements sont de simples redessins.

## Profils contextuels : « Contexte 1 … 9 »

Le SDK Logi ne permet **ni de réorganiser un profil ni de changer de profil**. Le plugin fournit donc 9 touches « Contexte N » dont la
fonction et l'icône suivent l'état de l'Outil Capture. Placées sur une page du Keypad, elles transforment cette page automatiquement.

| État détecté (`app.state`) | Touches 1 → 9 |
|---|---|
| Fermé / inactif | Nouvelle, Rectangle, Fenêtre, Plein écran, Forme libre, Vidéo, Dernière, Captures, Capture |
| Sélection d'image ouverte | Rectangle, Fenêtre, Forme libre, Fermer |
| Sélection vidéo (zone non tracée) | « Tracez la zone » (information), Fermer |
| Zone vidéo prête | Enregistrer, Micro, Annuler |
| Enregistrement / pause | Arrêter (+ minuteur), Pause/Reprendre, Micro, Durée, Abandonner (2 appuis) |
| Vidéo terminée | Lire, Enreg. sous, Copier, Vidéos, Dern. vidéo, Vidéo, Nouvelle |
| Image ouverte | Stylo, Surligneur, Gomme, Annuler, Rétablir, Copier, Enreg. sous, Partager, Nouvelle |

* **Détection** : d'abord les classes de fenêtres et les AutomationId de l'enregistreur (phases 5 et 6), puis l'éditeur. Un processus
  seul ne vaut jamais « enregistrement ». La lecture est passive : elle n'ouvre et ne ferme rien.
* **Sécurité au moment de l'appui** : l'affichage peut avoir jusqu'à 1,5 s de retard. Avant d'agir, la touche relit l'état ; si la
  touche ne correspond plus à la même action, l'appui est **ignoré** et la touche affiche « Changé ». Aucune action ne s'exécute à la place d'une autre.
* `enableContextProfiles: false` dans `config.json` fige les 9 touches sur la page Capture (sélection manuelle des pages).
* Emplacement OCR (image ouverte, touche 8 du cahier des charges) : réservé à la phase 9. En attendant, la touche 8 affiche Partager.

## Tests

* **104 / 104** (+22) : 9 emplacements par état, actions connues, « Démarrer » proposé seulement quand la zone est tracée,
  contrôles d'enregistrement présents seulement pendant un enregistrement, confirmation des actions destructives, profils désactivés.
* Service : 6 actions dynamiques chargées, SVG 41/41, un seul agent courant.
* État réel lu par l'agent déployé : `Closed` (Outil Capture fermé), cohérent.

## À vérifier sur le Keypad (lisibilité réelle)

1. Créez une page avec **Contexte 1 à 9**. Outil Capture fermé : vous devez voir la page Capture.
2. Faites une capture, et la page doit passer en mode Image en 1,5 s au plus. Enregistrez une vidéo : la page Vidéo, puis la page Enregistrement avec le minuteur.
3. Jugez la lisibilité des icônes et des libellés sur l'écran du Keypad (taille, contraste). Ce point ne peut se vérifier que sur l'appareil.
