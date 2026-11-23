# Windows Capture Controller - Feasibility Report

Phase 1 : recherche. Analyse faite le 2026-11-23 sur le poste cible, sans écrire de code de production.
Chaque ligne indique sa source : **[Mesuré]** = vérifié sur cette machine, **[Doc]** = documentation officielle,
**[À vérifier]** = test prévu dans une phase suivante (non encore prouvé).

---

## 1. Environnement détecté [Mesuré]

| # | Élément | Résultat |
|---|---|---|
| 1 | Windows | Windows 11 Business, 26H2, build **26300.9974**, x64, interface **fr-FR** |
| 2 | Outil Capture d'écran | `Microsoft.ScreenSketch` **11.2609.15.0**, PFN `Microsoft.ScreenSketch_8wekyb3d8bbwe`, exe `SnippingTool\SnippingTool.exe`, alias `SnippingTool.exe`, protocoles `ms-screenclip` et `ms-screensketch` |
| 3 | Logi Options+ | **2.9.1013853** (processus `logioptionsplus*` actifs) |
| 3b | Logi Plugin Service | **6.4.2.3414** (`C:\Program Files\Logi\LogiPluginService`), `PluginApi.dll` 6.4.2.3414, hôte .NET 10 |
| 4 | Appareils Logitech | **MX Creative Keypad** (USB, `VID_046D&PID_C354`), **MX Dialpad** (Bluetooth), MX Keys S |
| 5 | Keypad disponible | Oui, connecté et vu par Windows |
| — | Écrans | 3 × 1920×1080 (DISPLAY1 principal en 0,0 ; DISPLAY2 en 1920,0 ; DISPLAY3 en 3840,0) |
| — | Dossier Images | **redirigé vers `D:\Image`** (preuve qu'il ne faut jamais coder `C:\Users\…\Pictures`) |
| — | Dossier Vidéos | `C:\Users\<user>\Videos` (pas encore de sous-dossier « Screen Recordings ») |
| — | .NET | SDK 11.0.100 ; `dotnet tool` `logiplugintool` 6.4.2.3599 et `ilspycmd` installés |
| — | Plugins existants | 20 plugins dans `%LOCALAPPDATA%\Logi\LogiPluginService\Plugins` (dont vos MXTeamViewer, SystemGauge) |

## 2. Logitech : SDK et plugins

**6. SDK disponible : oui.** Il s'agit du **Logi Actions SDK** (C#), exécuté par le Logi Plugin Service.
Les types ont été vérifiés dans le `PluginApi.dll` installé [Mesuré] :

* `Plugin`, `ClientApplication` (association à un processus, `GetProcessName`, `GetApplicationStatus`) ;
* `PluginDynamicCommand` : touches ; `PluginDynamicAdjustment` : molettes (`ApplyAdjustment(param, diff)`, `AdjustmentValueChanged`) ;
* `ActionImageChanged(param)` : redessin d'une touche à la demande (états dynamiques) ;
* `DynamicButtonPages`, `DynamicEncoderPages`, `CommandFolders` : pages et dossiers dynamiques fournis par un plugin ;
* `PluginStatus` (message d'état du plugin dans Options+), événements `Native*` (verrouillage de session, veille).
* Enum `DeviceType` : `Loupedeck70` = **MX Creative Keypad**, `Loupedeck71` = **MX Creative Dialpad**,
  famille `LoupedeckExtendedFamily` (« Logitech Creative Family »).

**7. Installation d'un plugin [Mesuré, même méthode que vos plugins existants]**

* Développement : fichier `.link` dans `%LOCALAPPDATA%\Logi\LogiPluginService\Plugins\` pointant vers le dossier de build, puis `loupedeck:plugin/<Nom>/reload`.
* Distribution : paquet `.lplug4` (zip `bin/` + `metadata/LoupedeckPackage.yaml` + icône), produit par `logiplugintool pack`, installé par double clic.
* Manifest : `type: plugin4`, `supportedDevices: [LogitechCreativeFamily]`, `minimumLoupedeckVersion: 6.4`.
* Règle déjà apprise sur ce poste : compiler contre le `PluginApi.dll` **installé** (le service refuse un plugin compilé contre une API plus récente).

**8. Touches LCD** : 9 touches LCD par page sur le Keypad, plus deux boutons de page. Image par touche générée par le plugin
(`BitmapBuilder`, ou PNG/SVG), redessinée via `ActionImageChanged`. Les retours temporaires (« Copié ✓ » 1,5 s,
compte à rebours 5-4-3-2-1) sont donc faisables : il suffit de redessiner, sans animation native.

**9. Molette : point bloquant à connaître.** **Le MX Creative Keypad n'a pas de molette.** La molette (dial) et le
roller appartiennent au **MX Creative Dialpad**, qui est connecté sur ce poste. Les « modes molette » seront donc des
`PluginDynamicAdjustment` assignés au Dialpad. Sur le Keypad, chaque fonction de molette existe aussi sous forme de touches
(Zoom +, Zoom −, Épaisseur +/−, Couleur suivante…). L'appui long sur le dial n'est pas garanti par le SDK [À vérifier, phase 7].

**10. Affichage dynamique et profils**

* Le plugin **peut** : changer l'icône, le texte et l'état de chacune de ses touches à tout moment.
* Le plugin **ne peut pas** : réorganiser lui-même les touches d'un profil Options+ ni changer de profil.
  Options+ bascule de profil selon l'**application au premier plan** (`ClientApplication` → `SnippingTool.exe`).
* Solution retenue (déjà validée dans MXTeamViewer) : des **touches contextuelles** « Contexte 1…9 » dont la fonction et l'icône
  suivent l'état détecté de l'Outil Capture (fermé / image ouverte / vidéo…). Les pages dynamiques (`DynamicButtonPages`)
  sont une alternative, mais leur rendu sur le Keypad avec Options+ 2.9 reste [À vérifier, phase 3].

## 3. Outil Capture : ce qui existe réellement

### 3.1 Protocole URI officiel [Doc : *Launch Snipping Tool*, Microsoft Learn]

`ms-screenclip://capture/image?{rectangle|freeform|window}` et `ms-screenclip://capture/video`, avec
`enabledModes=` (`RectangleSnip, WindowSnip, FreeformSnip, FullscreenSnip, RectangleRecord`), `auto-save`, `user-agent`, `api-version=1.2`.

Limites documentées, importantes pour nous :

* **Le plein écran n'est pas un paramètre de mode** : `FullscreenSnip` ne peut qu'être *proposé* dans la barre (`enabledModes`).
* **La vidéo est toujours en mode rectangle** ; l'URI ouvre la sélection mais ne démarre pas l'enregistrement (c'est conforme à votre exigence de validation de zone).
* La **réponse** (fichier capturé) n'est livrée qu'à une application **packagée MSIX** (`redirect-uri`) ou ayant une identité de package (`LaunchUriForResultsAsync` + jeton). Un plugin Logi non packagé **ne reçoit pas le fichier**. On récupère donc le résultat par le presse-papiers et le dossier Captures (auto-enregistrement).
* L'ancien `ms-screenclip:` (sans `//capture`) est **déprécié** depuis le 1er mai 2025 : il ne sera pas utilisé.
* **[Mesuré en phase 2] Sur 11.2609.15, l'URI ne déclenche pas l'overlay pour un appelant non packagé.** `ShellExecute`
  (avec ou sans `redirect-uri`) ouvre seulement la fenêtre principale, et `LaunchUriForResultsAsync` répond « Not Implemented ».
  Méthode retenue : `Win+Maj+S`, puis choix du mode par UI Automation dans l'overlay (voir `02-phase2-prototype.md`).

### 3.2 Raccourcis [Doc : support.microsoft.com, *Use Snipping Tool* et *Keyboard shortcuts in Windows*]

| Raccourci | Effet documenté | Usage dans le plugin |
|---|---|---|
| `Win+Maj+S` | Overlay de capture d'image | Repli de « Nouvelle capture » |
| `Win+Maj+R` | Overlay d'enregistrement vidéo, sortie MP4 dans `Vidéos\Screen Recordings` | Repli de « Vidéo » |
| `Win+Impr. écran` | Plein écran enregistré dans `Images\Captures d'écran` | **Plein écran « tous les écrans »** (natif, fiable) |
| `Alt+Impr. écran` | Fenêtre active vers le presse-papiers | Option secondaire |
| `Alt+N` (dans l'outil) | Nouvelle capture dans le dernier mode | Interne |
| `Alt+M` / `Alt+D` (dans l'outil) | Choix du mode / du délai | Non utilisés (UIA est plus sûr) |
| `Ctrl+C` / `Ctrl+S` (dans l'outil) | Copier / enregistrer | Repli si le bouton UIA est introuvable |
| `Ctrl+Z` / `Ctrl+Y` | Génériques Windows, **non listés** pour l'outil | Non utilisés : on passe par UIA, qui donne aussi l'état activé/désactivé |

Les deux overlays globaux affichent eux-mêmes « Appuyez sur ⊞ + Maj + S / R » (`WinShiftSTipText`, `WinShiftRTipText`) [Mesuré].
La doc indique « délai 1–5 s » pour `Alt+D`, mais la version installée propose 0/3/5/10 s [Mesuré] : **la machine fait foi.**

### 3.3 UI Automation [Mesuré sur la version 11.2609.15, interface FR]

Les AutomationId sont **stables et indépendants de la langue**. Les libellés sont en français et ne servent qu'à l'affichage.

**Fenêtre principale / barre de capture**

| Contrôle | AutomationId | Pattern | Valeurs observées |
|---|---|---|---|
| Nouvelle capture / Nouvel enregistrement | `NewCaptureButton` | Invoke | — |
| Mode image ↔ vidéo | `CaptureModeToggleSwitch` | Toggle | Off = image, On = vidéo |
| Zone de capture | `SnippingModeComboBox` | Selection, ExpandCollapse | Rectangle, Fenêtre, Plein écran, Forme libre (4 items, sélection **par index**) |
| Délai | `SnipDelayComboBox` | Selection | Aucun, 3 s, 5 s, 10 s |
| Extracteur de texte | `LaunchTextExtractorButton` | Invoke | — |
| Menu « … » | `AppBarMoreOptionsButton` → `OpenFileBarButton`, `SettingsMenuAppBarButton` | Invoke | — |
| Bandeaux | `AutoSaveInfoBar`, `InformationInfoBar` | — | Indices d'état |

**Éditeur (image ouverte)**

| Contrôle | AutomationId | Pattern |
|---|---|---|
| Stylet bille | `InkToolbarBallpointPenButton` | SelectionItem (état « actif » lisible), ExpandCollapse (palette) |
| Surligneur | `InkToolbarHighlighterButton` | idem |
| Gomme | `InkToolbarEraserButton` | idem |
| Formes | `ShapesButton` | Toggle |
| Rogner | `CropButton` | Toggle |
| Analyser le texte (OCR) | `OcrOverlayButton` | Toggle |
| ↳ Copier tout le texte | `CopyAllTextFromOCRButton` | Invoke |
| ↳ Copier en tableau | `CopyTableFromOCRButton` | Invoke (désactivé si aucun tableau) |
| ↳ Suppression rapide (masquage) | `QuickRedactButton`, `QuickRedactOptionsButton` | Invoke |
| Annuler / Rétablir | `UndoBarButton`, `RedoBarButton` | Invoke + **IsEnabled lisible** |
| Éditer dans Paint | `EditInPaintButton` | Invoke |
| Enregistrer sous | `SaveBarButton` | Invoke |
| Copier | `CopyBarButton` | Invoke |
| Partager | `ShareBarButton` | Invoke (ouvre la feuille de partage Windows, sans rien envoyer) |
| Palette du stylet | 30 `ListItem` (Noir, Blanc, Rouge…) | SelectionItem : AutomationId = **nom localisé**, donc sélection par **index** |
| Épaisseur | `PenStrokeWidthSlider` | Slider (RangeValue) |
| Zone de dessin | `AnnotateCanvas` / `AnnotateCanvasScrollViewer` | Scroll |

**Observations importantes**

* **Fenêtre minimisée = arbre UIA vide** en vue Contrôle. Le contrôleur doit restaurer la fenêtre avant toute action UIA.
* La boîte « Ouvrir / Enregistrer sous » appartient au processus **`PickerHost.exe`**, pas à `SnippingTool.exe`. La détection d'état doit en tenir compte.
* `SnippingTool.exe <fichier>` (argument en ligne de commande) **n'ouvre pas** l'image : l'application démarre sur l'écran vide.
* ~~Règle et rapporteur absents~~ — **corrigé en phase 6** : la **règle existe** (menu « … » → « Afficher / Masquer la règle », `RulerButton`), affichage seulement (non exposée pour la rotation). **Pas de rapporteur.** Le **zoom** existe aussi (menu « … » → Zoom : 100 %, Zoom avant, Zoom arrière, Ajuster).
* Pendant les sondages, la fenêtre de l'outil s'est fermée ou minimisée une fois d'elle-même. Le contrôleur doit tolérer ce cas.

## 4. Tableau de faisabilité par fonction

Légende : ✅ fiable · ⚠️ possible, avec limites · ❌ indisponible · 🔬 à vérifier en phase indiquée

| Fonction | API | Raccourci | URI | UI Automation | Méthode retenue / Fallback |
|---|---|---|---|---|---|
| Lancer / afficher l'outil | ❌ | ❌ | `shell:AppsFolder\…!App` ✅ | — | Activation du package + Win32 (restaurer, premier plan), une seule instance |
| Nouvelle capture | ❌ | `Win+Maj+S` ✅ **testé** | ❌ (non packagé, mesuré en phase 2) | overlay `ScreenshotOverlayToolbar` ✅ | **Win+Maj+S** |
| Rectangle | ❌ | `Win+Maj+S` ✅ | ❌ | overlay combo index 0 ✅ **testé** | Win+Maj+S + UIA |
| Fenêtre | ❌ | `Win+Maj+S` ✅ | ❌ | overlay combo index 1 ✅ **testé** | Win+Maj+S + UIA |
| Forme libre | ❌ | `Win+Maj+S` ✅ | ❌ | overlay combo index 3 ✅ **testé** | Win+Maj+S + UIA |
| Plein écran (tous écrans) | ❌ | `Win+Impr.` ✅ | ⚠️ seulement proposé | Combo index 2 + Nouveau ✅ | `Win+Impr.` (fichier) ou UIA (ouvre l'éditeur) |
| Plein écran (principal / pointeur / fenêtre active) | ❌ | ❌ | ❌ | ❌ | **Module de capture indépendant** (DXGI/GDI dans l'agent) vers presse-papiers ou fichier |
| Délai 3 / 5 / 10 s | ❌ | `Alt+D` ⚠️ | ❌ | `SnipDelayComboBox` ✅ | **Compte à rebours côté plugin** (LCD 5-4-3-2-1, annulable), puis URI. Le combo UIA reste disponible en option. |
| Préparer vidéo | ❌ | `Win+Maj+R` ✅ | `capture/video` ✅ 🔬2 | Toggle + Nouveau ✅ | URI → raccourci. Ne démarre jamais seul. |
| Démarrer / arrêter l'enregistrement | ❌ | ❌ | ❌ | 🔬5 (barre d'enregistrement non encore sondée) | UIA sur la barre d'enregistrement ; désactivé si introuvable |
| Pause / reprise | ❌ | ❌ | ❌ | 🔬5 | Créé **seulement** si un contrôle Pause existe |
| Micro / son | ❌ | ❌ | ❌ | 🔬5 | Bascule de la barre si exposée ; sinon micro Windows (avertissement) ou désactivé |
| Copier | ❌ | `Ctrl+C` ✅ | ❌ | `CopyBarButton` ✅ | UIA, vérification du presse-papiers (format image présent, contenu jamais lu ni journalisé) |
| Enregistrer | ❌ | `Ctrl+S` ✅ | ❌ | ⚠️ (seul « Enregistrer sous » est exposé) | Auto-enregistrement de l'outil (dossier Captures) ; sinon `Ctrl+S` vers la fenêtre vérifiée |
| Enregistrer sous | ❌ | ❌ | ❌ | `SaveBarButton` ✅ | UIA |
| Partager | ❌ | ❌ | ❌ | `ShareBarButton` ✅ | UIA (l'utilisateur choisit toujours le destinataire) |
| Stylo / surligneur / gomme | ❌ | ❌ | ❌ | ✅ (+ état actif lisible) | UIA |
| Couleur | ❌ | ❌ | ❌ | ✅ 30 couleurs, par index | UIA ; l'icône LCD prend la couleur choisie |
| Épaisseur | ❌ | ❌ | ❌ | `PenStrokeWidthSlider` ✅ 🔬6 (bornes) | UIA RangeValue |
| Formes, Rogner | ❌ | ❌ | ❌ | ✅ | Bonus possible |
| Règle / rapporteur | ❌ | ❌ | ❌ | Règle ✅ `RulerButton` (phase 6) ; rapporteur ❌ | Règle : afficher / masquer ; rotation non fiable (pas exposée) ; rapporteur désactivé |
| Gomme « tout effacer » | ❌ | ❌ | ❌ | 🔬6 (option du flyout gomme) | Appui long + confirmation, seulement si l'option existe |
| Annuler / rétablir | ❌ | ⚠️ non documentés pour l'outil | ❌ | ✅ avec état activé | UIA uniquement : jamais de Ctrl+Z envoyé à une autre application |
| Zoom | ❌ | non utilisé | ❌ | ✅ menu Zoom (`ZoomInMenuItem`, `ZoomOutMenuItem`, `ZoomToActualSizeMenuItem`, `ZoomFitToWindow`) — phase 6 | UIA, ≈ 0,2–0,8 s par pas ; pourcentage non exposé |
| OCR : extraire le texte | ❌ | ❌ | ❌ | `OcrOverlayButton` + `CopyAllTextFromOCRButton` ✅ | UIA (OCR local selon la doc) |
| Masquer les données | ❌ | ❌ | ❌ | `QuickRedactButton` ✅ | UIA + avertissement « détection incomplète » |
| Extracteur de texte (écran) | ❌ | ❌ | ❌ | `LaunchTextExtractorButton` ✅ | Bonus |
| Dernière capture | ❌ | — | — | — | Dossier Captures (`FOLDERID_Screenshots`) + dossier configuré, fichier le plus récent |
| Dernière vidéo / dossier vidéos | ❌ | — | — | — | `FOLDERID_Videos` + `Screen Recordings` (doc) + dossier configuré ; 🔬5 (emplacement réel) |
| Dossier captures | ❌ | — | — | — | `SHGetKnownFolderPath(FOLDERID_Screenshots)` : gère la redirection (ici `D:\Image`) et OneDrive |
| Retour visuel LCD | `ActionImageChanged` ✅ | — | — | — | SDK |
| Molette (zoom, épaisseur, couleur, délai, historique) | `PluginDynamicAdjustment` ✅ (**Dialpad**) | — | — | — | Accumulateur + limitation à environ 15 commandes/s |

## 5. Fonctions non accessibles de manière fiable (13)

1. **Rapporteur** : absent de la version 11.2609.15 (la règle existe : affichage seulement, rotation non exposée).
2. **Récupération directe du fichier capturé par URI** : réservée aux applications packagées.
3. **Plein écran par moniteur** (principal, pointeur, fenêtre active) : non proposé par l'outil, d'où le module de capture indépendant.
4. **Molette sur le Keypad** : matériel absent. Elle passe par le Dialpad ou par des touches.
5. **Changement automatique de profil Options+ par le plugin** : impossible. On utilise des touches contextuelles.
6. **Démarrer, pause et micro pendant l'enregistrement** : non prouvés tant que la barre d'enregistrement n'a pas été sondée (phase 5, avec votre accord, car il faut lancer un vrai enregistrement sur une zone de test).
7. **Zoom** : pas de raccourci documenté, mais menu Zoom accessible par UI Automation (phase 6) ; le pourcentage n'est pas lisible.
8. **Sélection d'une couleur par son nom** : l'AutomationId est localisé, donc la sélection se fait par index. Fragile si Microsoft réordonne la palette ; le diagnostic le signale.

## 6. Limitations connues (14)

* Toute la partie UIA dépend de l'interface interne de l'Outil Capture. Elle est **isolée dans un module remplaçable**
  (`SnippingToolUiMap`) avec ses AutomationId en un seul endroit, et le diagnostic signale tout contrôle manquant après une mise à jour.
* La fenêtre doit être restaurée (non minimisée) pour l'UIA. Le plugin la ramène au premier plan si la configuration l'autorise.
* Les boîtes de fichier relèvent de `PickerHost.exe`.
* Les AutomationId sont indépendants de la langue, mais les **noms de couleurs** ne le sont pas.
* `Win+Impr. écran` et `Win+Maj+S/R` sont des raccourcis système : ils peuvent être réaffectés (PowerToys, réglage « Impr. écran »).
  Le diagnostic les teste.
* Les touches LCD n'ont pas d'animation native : les comptes à rebours sont des redessins successifs.
* La version de Windows est une Insider récente (26300). Le comportement peut évoluer d'un build à l'autre.

## 7. Architecture technique recommandée (15)

Elle reprend le modèle déjà éprouvé sur ce poste avec **MXTeamViewerPlugin** :

```
WindowsCaptureController/
  src/Plugin            → WindowsCapturePlugin.dll (net8.0, référence PluginApi.dll installé)
     Plugin/            Plugin + ClientApplication (SnippingTool.exe)
     Actions/           PluginDynamicCommand : capture, vidéo, annotation, texte, fichiers, contexte 1..9
     Dial/              PluginDynamicAdjustment : zoom, épaisseur, couleur, délai, historique (Dialpad)
     Profiles/          moteur des touches contextuelles (état → 9 actions)
     Rendering/         icônes SVG → bitmaps, états (actif, désactivé, erreur, compte à rebours, « Copié ✓ »)
     Configuration/     config.json, validation stricte, valeurs par défaut sûres
     Logging/           %LOCALAPPDATA%\WindowsCaptureController\logs, rotation 5 × 5 Mo, sans contenu sensible
     Agent/             client IPC (named pipe) + relance automatique de l'agent
  src/Agent             → WccAgent.exe (net8.0-windows, hors processus)
     WindowsCapture/    IWindowsCaptureController + implémentations :
                          UriController, ShortcutController, UiAutomationController, FallbackController
                          WindowsCaptureController (orchestrateur : choix de la méthode par fonction)
     Automation/        SnippingToolUiMap (AutomationId), FocusGuard (vérifie processus + fenêtre)
     StateDetection/    WindowsCaptureStateDetector (événements UIA + WinEvent, pas de polling serré)
     Capture/           capture multi-écran indépendante (DPI-aware PerMonitorV2)
     Clipboard/         vérification de format image, jamais de lecture du contenu
     Storage/           Known Folders, dernière capture / vidéo, noms horodatés
  src/Cli               → WindowsCaptureController.exe diagnose | test … | list-controls | export-diagnostic
  shared/               protocole IPC (DTO)
  assets/icons/*.svg    tests/ (xUnit, images synthétiques, fenêtres de test)    scripts/*.ps1    docs/
```

Pourquoi un **agent hors processus** : UIA, capture d'écran et presse-papiers peuvent se bloquer ou planter.
Isolés dans `WccAgent.exe`, ils ne peuvent pas faire tomber le Logi Plugin Service, et l'agent est relancé automatiquement.

**Ordre de choix des méthodes** (appliqué par l'orchestrateur) : URI documenté → raccourci système documenté →
UIA par AutomationId → raccourci dans l'outil (fenêtre vérifiée) → sinon action désactivée avec un motif dans le diagnostic.
Aucun clic par coordonnées.

**États** (`WindowsCaptureStateDetector`) : `Closed` (pas de processus) · `Idle` (`NewCaptureButton` présent, pas de canevas) ·
`CaptureSelection` (overlay de capture actif 🔬2) · `ImageEditing` (`AnnotateCanvas` présent) · `VideoSelection`, `VideoReady`, `Recording`, `RecordingPaused`, `VideoEditing` 🔬5 ·
`Saving` (fenêtre `PickerHost` liée) · `Unknown`. Le processus seul ne prouve jamais un enregistrement.

**Cible .NET** : `net8.0` comme demandé. Le service héberge .NET 10, qui charge sans problème un assembly .NET 8.
Si le build échoue faute de pack de ciblage 8 (seul le SDK 11 est installé), on bascule sur `net10.0` comme vos plugins existants. C'est vérifié dès la phase 2.

## 8. Plan de développement (validé phase par phase)

| Phase | Contenu | Critère de sortie |
|---|---|---|
| 2 | Prototype CLI + agent : lancer/afficher, URI `capture/image` (3 modes), `Win+Maj+S`, plein écran `Win+Impr.`, journaux | Build OK, test manuel de chaque action, **comportement URI sans redirect confirmé** |
| 3 | Plugin minimal : Nouvelle capture, Ouvrir l'outil, Plein écran, Dossier captures | Visible dans Options+, assignable au Keypad, `.lplug4` |
| 4 | Rectangle, fenêtre, forme libre, délai avec compte à rebours LCD et annulation, plein écran multi-écran | Test avec les 3 écrans |
| 5 | Vidéo : préparation, puis **sonde de la barre d'enregistrement** (enregistrement d'une zone de test, avec votre accord), démarrer/arrêter/pause/micro selon ce qui existe | Fonctions créées seulement si les contrôles existent |
| 6 | Annotation : stylo, surligneur, gomme, couleurs, épaisseur, annuler/rétablir, test du zoom | État actif sur les touches |
| 7 | Dialpad : 5 modes, accumulateur, rotation lente et rapide | Pas de saturation |
| 8 | Icônes SVG, états, touches contextuelles | Lisibilité vérifiée sur le Keypad |
| 9 | OCR (copier tout le texte), suppression rapide, extracteur | « Texte copié ✓ » |
| 10 | Packaging, scripts d'installation/désinstallation/mise à jour, docs, tests | Critères de validation du cahier des charges |

## 9. Points de décision pour vous

1. **Molette** : on cible le **MX Creative Dialpad** (présent) pour les 5 modes, avec des touches équivalentes sur le Keypad. D'accord ?
2. **Plein écran « tous les écrans »** : `Win+Impr.` (enregistrement direct dans Captures, sans éditeur) ou UIA (ouvre l'éditeur) ? Je propose `Win+Impr.` par défaut, configurable.
3. **Phase 5** : la sonde de la barre d'enregistrement exige de démarrer un vrai enregistrement d'une zone de test (fenêtre synthétique, environ 3 s, puis suppression du fichier). Je vous le redemanderai le moment venu.

---
*État du poste après l'analyse : l'Outil Capture est resté ouvert sur l'image synthétique
`%TEMP%\wcc_synthetic_test.png` (aucune donnée utilisateur). Aucun réglage de l'outil n'a été modifié (bascule vidéo remise sur « image »).*

