# Phase 2 — Prototype minimal : résultats

Date : 2026-11-23. Poste : Windows 11 Pro 26H2 build 26300.9974 (fr-FR), Outil Capture 11.2609.15.0, 3 écrans 1920×1080 à 100 %.

## Build

| Élément | Résultat |
|---|---|
| Solution | `WindowsCaptureController.sln` (Core, Cli, Tests), cible `net8.0-windows10.0.19041.0` |
| Compilation | **0 erreur, 0 avertissement** |
| Tests unitaires | **28 / 28 réussis** (URI, anti-rebond, rotation des journaux, masquage des chemins, correspondance des modes) |

```powershell
dotnet build WindowsCaptureController.sln -c Debug
```

```powershell
dotnet test WindowsCaptureController.sln
```

## Tests sur la machine réelle

| Test | Commande | Résultat |
|---|---|---|
| Diagnostic | `diagnose` | ✅ Windows 11 26H2, Outil Capture 11.2609.15, Options+ 2.9, Plugin Service 6.4.2, Keypad **et** Dialpad détectés, dossier Captures `D:\Image\Screenshots` |
| Afficher l'outil minimisé | `test launch` | ✅ restauré et mis au premier plan ; **une seule instance** (2 appuis = 1 processus) |
| Lancer à froid | `test launch` (outil fermé) | ✅ `IApplicationActivationManager`, fenêtre visible en ≈ 1 s |
| Premier plan | `test front` | ✅ |
| Nouvelle capture | `test newcapture --cancel` | ✅ overlay ouvert par `Win+Maj+S` (≈ 1 s) |
| Rectangle | `test capture rectangle --cancel` | ✅ overlay + mode lu = 0 (rectangle), fermé par `CloseButton` |
| Fenêtre | `test capture window --cancel` | ✅ mode lu = 1 |
| Forme libre | `test capture freeform --cancel` | ✅ mode lu = 3 |
| Aucune capture pendant les tests | comptage du dossier Captures avant/après | ✅ 43 → 43 |
| Anti-rebond | `test debounce` | ✅ 2e appui à 0 ms ignoré, accepté à +450 ms |
| Dossier captures | `folders` | ✅ Known Folder `FOLDERID_Screenshots` (redirection `D:` respectée) |
| Plein écran (tous les écrans) | `test fullscreen --confirm` | ⏸ **non exécuté par moi** : il prend une vraie capture de vos 3 écrans. À lancer vous-même (voir plus bas). Sans `--confirm`, la commande refuse (code 4) ✅ |
| Journaux | `%LOCALAPPDATA%\WindowsCaptureController\logs\wcc.log` | ✅ actions, méthode choisie, résultat ; aucun contenu |

## Découvertes qui changent le plan

1. **L'URI documenté `ms-screenclip://capture/image?…` n'ouvre pas l'overlay depuis une application non packagée**
   sur cette version. Le lancement par `ShellExecute`, avec ou sans `redirect-uri`, outil ouvert ou fermé,
   affiche seulement la fenêtre principale. `Launcher.LaunchUriForResultsAsync`, la voie documentée pour les applications
   non packagées, répond `reason=Not Implemented`.
   → **Méthode retenue** : `Win+Maj+S` (raccourci documenté), puis sélection du mode **par UI Automation dans l'overlay**.
   Le chemin URI est conservé derrière l'option `NewCaptureMethod = "uri"` pour une future version de l'outil.
2. **L'overlay est un processus `SnippingTool.exe` séparé**, avec une fenêtre `SnipOverlayRootWindow` (5760×1080) et une fenêtre `XamlWindow` par écran.
   Sa barre d'outils expose des AutomationId stables : `ScreenshotOverlayToolbar`, `SnippingModeComboBox`,
   `CaptureModeToggleSwitch` (image/vidéo), `LiveAnnotationModeToggleButton`, `CloseButton`.
3. **Sélectionner « Plein écran » dans l'overlay capture immédiatement** : le code le refuse (`SelectMode`).
   Le plein écran passe par `Win+Impr.écran`.
4. **Un overlay laissé ouvert puis abandonné (perte du focus) a produit 2 PNG quasi vides** (145 et 146 octets, 11:48:00 et 11:49:55)
   dans `D:\Image\Screenshots`, pendant mes sondes manuelles. Les tests de la CLI ferment désormais toujours l'overlay par
   `CloseButton`, et aucun fichier n'a été créé depuis.
5. **Des processus `SnippingTool.exe` d'overlay restent en arrière-plan** après fermeture. Le nombre de processus ne
   prouve donc rien sur l'état (confirmé pour la détection d'état, phase 4).
6. Le registre affiche toujours « Windows 10 » dans `ProductName` : le diagnostic déduit Windows 11 du build (≥ 22000).

## Test plein écran à lancer vous-même

Il prend une vraie capture de vos écrans et l'enregistre dans `D:\Image\Screenshots` :

```powershell
& "D:\MXCreativKeydad\WindowsCaptureController\src\WindowsCaptureController.Cli\bin\Debug\net8.0-windows10.0.19041.0\WindowsCaptureController.exe" test fullscreen --confirm
```

## Fichiers produits

```
WindowsCaptureController.sln, Directory.Build.props
src/WindowsCaptureController.Core/
  Automation/SnippingToolUiMap.cs            AutomationId (seul endroit fragile)
  Automation/CaptureOverlayAutomation.cs     overlay : attente, mode, fermeture
  WindowsCapture/IWindowsCaptureController.cs  interface complète (39 membres)
  WindowsCapture/WindowsCaptureController.cs   orchestrateur (phase 2 + « prévu en phase N » explicites)
  WindowsCapture/ScreenClipUri.cs, ScreenClipLauncher.cs   protocole documenté (conservé, désactivé par défaut)
  WindowsCapture/SnippingToolLocator.cs, WindowActivator.cs, OverlayWatcher.cs, ActionDebouncer.cs, Model.cs
  Native/Win32.cs, KeyboardInput.cs          Win32 / SendInput documentés
  Storage/KnownFolders.cs                    Known Folders
  Diagnostics/SystemDiagnostics.cs           rapport sans données personnelles
  Logging/Log.cs                             journal, rotation 5 × 5 Mo
src/WindowsCaptureController.Cli/            WindowsCaptureController.exe (diagnose, test …, list-actions)
tests/WindowsCaptureController.Tests/        28 tests unitaires
```

## Prochaine étape : phase 3

Plugin Logi minimal (touches *Nouvelle capture*, *Ouvrir l'Outil Capture*, *Plein écran*, *Dossier captures*),
avec l'agent hors processus qui héberge ce noyau (UI Automation et `SendInput` hors du Logi Plugin Service).
