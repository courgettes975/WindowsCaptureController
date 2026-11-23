# Phase 3 — Plugin Logi minimal : résultats

Date : 2026-11-23. Logi Options+ 2.9.1013853, Logi Plugin Service 6.4.2.3414, MX Creative Keypad (USB) + MX Dialpad (Bluetooth).

## Architecture livrée

```
Logi Plugin Service (.NET 10)
 └─ WindowsCaptureControllerPlugin.dll (net10.0)   touches LCD, icônes, anti-rebond, retour visuel
      │  JSON ligne par ligne (stdin/stdout), aucune donnée utilisateur
      ▼
    bin\agent\WccAgent.exe (net8.0-windows)          hors processus : UI Automation, SendInput, Win32
      └─ WindowsCaptureController.Core               contrôleur des phases 1–2
```

* **Écart assumé par rapport au cahier des charges (.NET 8)** : la DLL du plugin cible `net10.0`, car le `PluginApi.dll`
  installé (6.4.2) est compilé pour .NET 10 et le service héberge .NET 10. Tout le reste (agent, noyau, CLI, tests) est en .NET 8.
* Si l'agent plante ou se bloque, Logi Options+ n'est pas affecté : le plugin attend au plus le délai prévu, affiche
  « Erreur » sur la touche et relance l'agent à l'appui suivant (au plus une fois toutes les 10 s après un échec).
* L'agent se ferme proprement quand le plugin est déchargé (fin de stdin).

## Actions assignables (groupe « Windows Capture »)

| Action dans Options+ | Commande agent | Icône LCD | Méthode |
|---|---|---|---|
| Capture Windows (ouvrir / afficher l'Outil Capture) | `launch` | ciseaux blancs sur fond bleu | activation du package, ou restauration et premier plan (une seule instance) |
| Nouvelle capture | `capture.new` | rectangle pointillé + « + » vert | `Win+Maj+S` (mode précédent conservé) |
| Plein écran (tous les écrans) | `capture.fullscreen` | quatre coins blancs | `Win+Impr.écran`, enregistré dans le dossier Captures |
| Dossier des captures | `folder.captures` | dossier jaune | Known Folder Screenshots |

Retour visuel : cadre **vert** pendant 1,5 s si l'action réussit ; cadre **orange** avec « N/D » ou « Erreur » sinon.
Anti-rebond de 400 ms par touche, une seule exécution à la fois par touche. Si la sélection est déjà ouverte, un second appui ne la rouvre pas.

## Vérifications

| Vérification | Résultat |
|---|---|
| Build Debug et Release | ✅ 0 erreur, 0 avertissement |
| Tests unitaires | ✅ 28 / 28 |
| Chargement par le service | ✅ « Plugin 'WindowsCaptureController' version '0.3.0' loaded … in 280 ms », 1 action dynamique |
| Agent | ✅ démarré par le plugin, « Agent ping OK » |
| Agent déployé, bout en bout | ✅ ping 126 ms, launch 98 ms, front 21 ms, nouvelle capture ≈ 1,8 s (attente de l'overlay comprise), mode fenêtre ≈ 1,9 s |
| Paquet | ✅ `dist\WindowsCaptureController_0.3.0.lplug4` (6,7 Mo), `logiplugintool verify` OK |
| Script d'installation (`-Dev`) | ✅ rapport final : Windows, service, Keypad, Outil Capture, plugin chargé, agent qui répond |
| **Actions visibles dans Options+ et appui réel sur le Keypad** | ⏸ **à faire par vous** (voir plus bas) |

## Incident pendant les tests : capture réelle

Pendant le test de bout en bout de l'agent (11:58:23.964), **une vraie capture de fenêtre (389 692 octets) a été
enregistrée** dans `D:\Image\Screenshots`, au moment où mon script fermait l'overlay en mode Fenêtre par `CloseButton`.
Elle contient la fenêtre qui se trouvait sous le pointeur ; je ne l'ai pas ouverte.

* Je n'ai pas pu reproduire le problème en 10 essais sous une **surface synthétique** couvrant les 3 écrans (0 fichier créé).
* Hypothèse : en mode Fenêtre, fermer l'overlay par UI Automation peut valider la fenêtre survolée. Cela dépend du contenu sous le pointeur.
* Conséquences :
  * Le **plugin ne ferme jamais l'overlay** : c'est l'utilisateur qui capture ou annule. La touche « Fenêtre » ne sera livrée en phase 4
    qu'avec un test de cette situation sous surface synthétique.
  * **Tous les tests automatisés qui ouvrent l'overlay passent désormais sous une surface synthétique** (script de test
    `safe-harness`, à intégrer aux tests d'intégration en phase 4).

## À faire de votre côté

1. Ouvrez Logi Options+ → **MX Creative Keypad** → actions → **Windows Capture Controller**. Vérifiez la présence des 4 actions
   du groupe « Windows Capture » et assignez-les à 4 touches.
2. Appuyez sur chaque touche : l'icône doit s'afficher sur l'écran LCD et un cadre vert doit apparaître après l'action.
3. Journaux en cas de problème : `%LOCALAPPDATA%\WindowsCaptureController\logs\plugin.log` (plugin) et `wcc.log` (agent).

## Commandes

```powershell
.\scripts\Install.ps1 -Dev        # développement : .link vers bin\Debug, rechargement à chaud
.\scripts\Install.ps1             # paquet Release + logiplugintool install
.\scripts\Build-Package.ps1       # produit dist\WindowsCaptureController_<version>.lplug4
.\scripts\Uninstall.ps1           # retire le plugin, conserve la configuration
.\scripts\Uninstall.ps1 -Complete # retire aussi configuration et journaux (après confirmation)
```
