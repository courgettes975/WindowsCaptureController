# Windows Capture Controller

Plugin **Logi Options+** (Logi Actions SDK) pour piloter l'**Outil Capture d'écran de Windows 11** (Snipping Tool)
depuis le **Logitech MX Creative Keypad** (touches LCD) et le **MX Creative Dialpad** (molette).

> Projet non affilié à Microsoft ni à Logitech. Windows et Snipping Tool sont des marques de Microsoft.
> **État : en développement (v0.8.0, phases 1 à 8 sur 10).** Testé sur Windows 11 26H2 (build 26300),
> Outil Capture 11.2609.15, Logi Options+ 2.9, Logi Plugin Service 6.4.2.

## Ce que fait le plugin

| Page | Actions |
|---|---|
| Capture | Ouvrir l'Outil Capture, Nouvelle capture, Rectangle, Fenêtre, Forme libre, Plein écran (tous écrans ou un seul), Capture dans 3 / 5 / 10 s avec compte à rebours sur la touche, Annuler délai, Dernière capture, Dossier des captures |
| Vidéo | Préparer (zone choisie par l'utilisateur), Démarrer / Arrêter (touche dynamique avec minuteur), Pause / Reprendre, Micro de l'enregistreur, Abandonner (double appui), Enregistrer sous, Dernière vidéo, Dossier vidéos |
| Annotation | Stylo, Surligneur, Gomme, Tout effacer (double appui), Règle, Annuler / Rétablir, Copier, Enregistrer sous, Partager, Couleur ‹ ›, Épaisseur ± / défaut, Zoom ± / 100 % / Ajuster |
| Molette (Dialpad) | 5 modes : zoom, épaisseur, couleur, historique, délai — touche « Mode molette » sur le Keypad |
| Contextuel | 9 touches « Contexte 1…9 » qui deviennent automatiquement la page Capture, Image, Vidéo ou Enregistrement selon l'état de l'Outil Capture |

## Principes

* **Aucune fausse API** : chaque fonction utilise une méthode vérifiée sur la machine réelle
  (raccourcis Windows documentés, UI Automation par `AutomationId`, Known Folders), jamais de clic par coordonnées.
  Ce qui n'existe pas (rapporteur, appui long sur la molette…) est signalé, pas simulé.
* **Stabilité** : toute l'automatisation tourne dans un agent séparé (`WccAgent.exe`) ; un blocage de l'agent ne peut pas
  faire tomber Logi Options+.
* **Confidentialité** : tout est local ; aucun envoi d'image, aucune lecture du presse-papiers, aucun contenu dans les journaux ;
  la « recherche visuelle Bing » de l'Outil Capture n'est jamais déclenchée.

## Architecture

```
src/WindowsCaptureController.Plugin   DLL Logi (net10.0, PluginApi.dll installé) : touches, molette, icônes
src/WindowsCaptureController.Agent    WccAgent.exe (net8.0-windows) : hôte hors processus, protocole JSON stdin/stdout
src/WindowsCaptureController.Core     contrôleur Outil Capture, UI Automation, capture multi-écran, config, journaux
src/WindowsCaptureController.Cli      WindowsCaptureController.exe diagnose | test …
shared/                               protocole agent + accumulateur de molette
tests/                                104 tests unitaires (xUnit)
assets/icons/                         41 icônes SVG (source unique, rendues par BitmapImage.FromSvg)
scripts/                              Install.ps1, Uninstall.ps1, Build-Package.ps1, New-PackageIcon.ps1
docs/                                 rapport de faisabilité, comptes rendus des phases 2 à 8, planche des icônes
```

## Compiler et installer

Prérequis : Windows 11, .NET SDK 10+ (runtime .NET 8 Desktop pour l'agent), Logi Options+, `LogiPluginTool`
(`dotnet tool install --global LogiPluginTool`).

```powershell
dotnet build WindowsCaptureController.sln
dotnet test WindowsCaptureController.sln
.\scripts\Install.ps1 -Dev      # développement : lien vers bin\Debug, rechargement à chaud
.\scripts\Build-Package.ps1     # paquet dist\WindowsCaptureController_<version>.lplug4
.\scripts\Install.ps1           # installe le paquet Release
.\scripts\Uninstall.ps1         # désinstalle (conserve la configuration ; -Complete pour tout retirer)
```

Configuration : `%LOCALAPPDATA%\WindowsCaptureController\config.json` (créée au premier lancement, validée champ par champ).
Journaux : `%LOCALAPPDATA%\WindowsCaptureController\logs`.

## Limites connues (extrait)

* Le protocole documenté `ms-screenclip://capture` n'ouvre pas la sélection pour une application non packagée sur la version testée :
  le plugin utilise `Win+Maj+S` / `Win+Maj+R` puis UI Automation.
* L'interface interne de l'Outil Capture peut changer avec une mise à jour : tous les identifiants sont regroupés dans
  `SnippingToolUiMap.cs`.
* Voir `docs/` pour le détail, phase par phase.

## Licence

MIT — voir [LICENSE](LICENSE).

