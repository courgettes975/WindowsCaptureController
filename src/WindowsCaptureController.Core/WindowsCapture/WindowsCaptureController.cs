using System.Diagnostics;
using WindowsCaptureController.Logging;
using WindowsCaptureController.Native;
using WindowsCaptureController.Storage;

namespace WindowsCaptureController.WindowsCapture;

/// <summary>Options used by the controller (full config.json arrives with the plugin, phase 3).</summary>
public sealed class ControllerOptions
{
    /// <summary>Adds the documented "auto-save" flag to URI requests (respects the user's Snipping Tool setting).</summary>
    public bool SnippingToolAutoSave { get; set; } = true;

    /// <summary>Image modes offered in the capture bar for URI requests.</summary>
    public string EnabledImageModes { get; set; } = ScreenClipUri.AllImageModes;

    /// <summary>
    /// "shortcut" (Win+Shift+S + UI Automation mode selection, default) or "uri" (documented ms-screenclip protocol).
    /// Measured in phase 2 on Snipping Tool 11.2609.15: the URI launched from an unpackaged process only shows the
    /// main window (no overlay), and LaunchUriForResultsAsync answers "Not Implemented". Kept for future builds.
    /// </summary>
    public string NewCaptureMethod { get; set; } = "shortcut";

    /// <summary>Max time to wait for the capture overlay after Win+Shift+S.</summary>
    public int OverlayTimeoutMs { get; set; } = 3000;

    /// <summary>Pause after the overlay appears, before its mode combo is touched (opening animation).</summary>
    public int OverlaySettleMs { get; set; } = 300;

    /// <summary>Initial mode of "Nouvelle capture".</summary>
    public CaptureMode DefaultCaptureMode { get; set; } = CaptureMode.Rectangle;

    public int DebounceMs { get; set; } = 400;
}

/// <summary>
/// Orchestrator: picks the most reliable documented method for each action, in this order:
/// documented URI → documented Windows shortcut → UI Automation (AutomationId) → in-app shortcut (verified window).
/// Phase 2 scope: launch/front, new capture, rectangle/window/freeform, full screen (all screens), folders.
/// </summary>
public sealed class WindowsCaptureController : IWindowsCaptureController
{
    private readonly ControllerOptions _options;
    private readonly ActionDebouncer _debouncer;
    private readonly Func<Configuration.WccConfig> _config;
    private readonly object _delayGate = new();
    private CancellationTokenSource? _delay;
    private string? _lastSavedCapture;

    public WindowsCaptureController(ControllerOptions? options = null, Func<Configuration.WccConfig>? config = null)
    {
        this._options = options ?? new ControllerOptions();
        this._debouncer = new ActionDebouncer(this._options.DebounceMs);
        this._config = config ?? (() => Configuration.ConfigStore.Load());
    }

    /// <summary>Capture saved by this process (session), first candidate of "Dernière capture".</summary>
    public string? LastSavedCapture => this._lastSavedCapture;

    // =====================================================================================
    // Application
    // =====================================================================================

    public ActionResult Launch() => this.Guard(nameof(this.Launch), () =>
    {
        var existing = SnippingToolLocator.FindMainWindow();
        if (existing != null)
        {
            // Already running: never start a second instance, just show it.
            var front = WindowActivator.BringToFront(existing.Handle);
            return front
                ? ActionResult.Ok(ControlMethod.Win32Window, "Outil Capture déjà ouvert : fenêtre affichée au premier plan.")
                : ActionResult.Sent(ControlMethod.Win32Window, "Fenêtre restaurée, mais Windows a refusé le premier plan.",
                    "Cliquez sur la fenêtre ; le prochain appui fonctionnera.");
        }

        if (SnippingToolLocator.FindPackage() == null)
        {
            return ActionResult.Unavailable("Outil Capture d'écran non installé.",
                "Installez « Outil Capture d'écran » depuis le Microsoft Store.");
        }

        var aam = (IApplicationActivationManager)new ApplicationActivationManager();
        var hr = aam.ActivateApplication(SnippingToolLocator.AppUserModelId, null, 0, out var pid);
        if (hr < 0)
        {
            return ActionResult.Fail(ControlMethod.PackageActivation, $"Activation refusée (HRESULT 0x{hr:X8}).");
        }

        Log.Info($"Snipping Tool activated, pid={pid}");
        var window = WaitFor(() => SnippingToolLocator.FindMainWindow(), 6000);
        if (window == null)
        {
            return ActionResult.Sent(ControlMethod.PackageActivation, "Outil Capture lancé, fenêtre pas encore visible.");
        }

        WindowActivator.BringToFront(window.Handle);
        return ActionResult.Ok(ControlMethod.PackageActivation, "Outil Capture lancé.");
    });

    public ActionResult BringToFront() => this.Guard(nameof(this.BringToFront), () =>
    {
        var w = SnippingToolLocator.FindMainWindow();
        if (w == null)
        {
            return ActionResult.Unavailable("Outil Capture fermé.", "Utilisez « Capture Windows » pour le lancer.");
        }

        return WindowActivator.BringToFront(w.Handle)
            ? ActionResult.Ok(ControlMethod.Win32Window, "Fenêtre au premier plan.")
            : ActionResult.Sent(ControlMethod.Win32Window, "Fenêtre restaurée, premier plan refusé par Windows.");
    }, debounce: false);

    /// <summary>Phase 2: Closed / Idle / Unknown only (UI Automation states arrive in phase 4–6).</summary>
    public CaptureAppState GetApplicationState()
    {
        if (SnippingToolLocator.GetProcesses().Count == 0)
        {
            return CaptureAppState.Closed;
        }

        return SnippingToolLocator.FindMainWindow() != null ? CaptureAppState.Idle : CaptureAppState.Unknown;
    }

    // =====================================================================================
    // Image capture
    // =====================================================================================

    /// <summary>Opens the capture overlay, keeping the mode last used by the user (Windows behaviour).</summary>
    public ActionResult StartNewCapture() => this.Guard(nameof(this.StartNewCapture), () => this.OpenOverlay(null));

    public ActionResult StartRectangularCapture() => this.Guard(nameof(this.StartRectangularCapture), () => this.OpenOverlay(CaptureMode.Rectangle));

    public ActionResult StartWindowCapture() => this.Guard(nameof(this.StartWindowCapture), () => this.OpenOverlay(CaptureMode.Window));

    public ActionResult StartFreeformCapture() => this.Guard(nameof(this.StartFreeformCapture), () => this.OpenOverlay(CaptureMode.Freeform));

    /// <summary>
    /// Win+Shift+S (documented) opens the overlay; the requested mode is then selected with UI Automation
    /// (overlay AutomationId SnippingModeComboBox). If the overlay or the control is not found, the overlay
    /// stays usable with the previous mode and the result says so (no coordinate clicks, no guessing).
    /// </summary>
    private ActionResult OpenOverlay(CaptureMode? mode)
    {
        if (string.Equals(this._options.NewCaptureMethod, "uri", StringComparison.OrdinalIgnoreCase))
        {
            return this.LaunchImageUri(mode ?? this._options.DefaultCaptureMode);
        }

        // An overlay that is still listed may be (a) left open by the user, or (b) closing (it lingers ~1–3 s).
        // Wait for (b) to finish; only an overlay that survives the wait is reused. Never act on a dying overlay.
        if (Automation.CaptureOverlayAutomation.IsOpen() && !Automation.CaptureOverlayAutomation.WaitUntilClosed(1500))
        {
            var open = Automation.CaptureOverlayAutomation.FindToolbarWindow();
            if (open != null)
            {
                return mode == null || Automation.CaptureOverlayAutomation.SelectMode(open, ModeIndex(mode.Value))
                    ? ActionResult.Ok(ControlMethod.UiAutomation, "Sélection de capture déjà ouverte" + (mode == null ? "." : $" : mode « {mode} »."))
                    : ActionResult.Sent(ControlMethod.UiAutomation, "Sélection déjà ouverte, mode non modifié.");
            }
        }

        var before = Automation.CaptureOverlayAutomation.CurrentToolbarHandles();
        var sent = SendGlobalShortcut("Win+Maj+S", KeyboardInput.VK_LWIN, KeyboardInput.VK_SHIFT, KeyboardInput.VK_S);
        if (!sent.IsOk)
        {
            return sent;
        }

        // Only an overlay created AFTER the shortcut is accepted.
        var overlay = Automation.CaptureOverlayAutomation.WaitForNewToolbarWindow(before, this._options.OverlayTimeoutMs);
        if (overlay == null)
        {
            return ActionResult.Sent(ControlMethod.WindowsShortcut, "Win+Maj+S envoyé, sélection non confirmée.",
                "Vérifiez que le raccourci n'est pas réaffecté (PowerToys, réglage « Impr. écran »).");
        }

        if (mode == null)
        {
            return ActionResult.Ok(ControlMethod.WindowsShortcut, "Sélection de capture ouverte (mode précédent conservé).");
        }

        // Let the overlay finish its opening animation before touching its controls.
        Thread.Sleep(this._options.OverlaySettleMs);

        return Automation.CaptureOverlayAutomation.SelectMode(overlay, ModeIndex(mode.Value))
            ? ActionResult.Ok(ControlMethod.UiAutomation, $"Sélection de capture ouverte en mode « {mode} ».")
            : ActionResult.Sent(ControlMethod.WindowsShortcut, $"Sélection ouverte, mode « {mode} » non appliqué.",
                "Choisissez le mode dans la barre de l'Outil Capture ; voir le diagnostic.");
    }

    internal static int ModeIndex(CaptureMode mode) => mode switch
    {
        CaptureMode.Rectangle => Automation.SnippingToolUiMap.ModeIndexRectangle,
        CaptureMode.Window => Automation.SnippingToolUiMap.ModeIndexWindow,
        CaptureMode.Freeform => Automation.SnippingToolUiMap.ModeIndexFreeform,
        _ => Automation.SnippingToolUiMap.ModeIndexFullscreen,
    };

    /// <summary>
    /// Full screen according to config.json "multiMonitorMode" / "fullscreenOutput":
    ///  * all-screens + file  → documented Win+PrtScn (Windows saves the PNG in the Screenshots folder);
    ///  * any other combination → independent capture module (one monitor, or clipboard output),
    ///    because the Snipping Tool cannot capture a single monitor.
    /// </summary>
    public ActionResult StartFullscreenCapture() => this.Guard(nameof(this.StartFullscreenCapture), () =>
    {
        var cfg = this._config();
        return cfg.MultiMonitorMode == "all-screens" && cfg.FullscreenOutput == "file"
            ? SendGlobalShortcut("Win+Impr.écran", KeyboardInput.VK_LWIN, KeyboardInput.VK_SNAPSHOT)
            : this.CaptureMonitorCore(cfg.MultiMonitorMode, cfg.FullscreenOutput, null, cfg);
    });

    /// <summary>Independent monitor capture with explicit parameters (agent command, tests with a temp folder).</summary>
    public ActionResult CaptureMonitor(string mode, string output, string? folderOverride = null) =>
        this.Guard(nameof(this.CaptureMonitor), () => this.CaptureMonitorCore(mode, output, folderOverride, this._config()));

    private ActionResult CaptureMonitorCore(string mode, string output, string? folderOverride, Configuration.WccConfig cfg)
    {
        if (!Configuration.WccConfig.MonitorModes.Contains(mode) || !Configuration.WccConfig.Outputs.Contains(output))
        {
            return ActionResult.Fail(ControlMethod.IndependentCapture, $"Paramètres invalides ({mode}, {output}).");
        }

        var monitors = Capture.ScreenCapture.GetMonitors();
        var area = Capture.ScreenCapture.SelectArea(mode, monitors, Capture.ScreenCapture.PointerPosition(), Capture.ScreenCapture.ActiveWindowBounds());
        if (area == null)
        {
            return ActionResult.Fail(ControlMethod.IndependentCapture, "Aucun écran détecté.", "Écran déconnecté ? Réessayez.");
        }

        Log.Info($"Method selected: independent capture ({mode}, {monitors.Count} monitor(s), area {area.Value.Width}x{area.Value.Height}, output {output})");
        using var bmp = Capture.ScreenCapture.CaptureArea(area.Value);
        string? saved = null;
        var copied = false;
        if (output is "file" or "both")
        {
            var folder = folderOverride ?? this.CaptureFolder(cfg);
            if (folder == null)
            {
                return ActionResult.Fail(ControlMethod.IndependentCapture, "Dossier des captures introuvable.");
            }

            saved = Capture.ScreenCapture.Save(bmp, folder, cfg.AutoSaveImageFormat, DateTime.Now);
            this._lastSavedCapture = saved;
        }

        if (output is "clipboard" or "both")
        {
            copied = Capture.ScreenCapture.CopyToClipboard(bmp);
        }

        var what = (saved, copied, output) switch
        {
            (not null, true, _) => "enregistrée et copiée",
            (not null, false, "both") => "enregistrée (presse-papiers occupé)",
            (not null, _, _) => "enregistrée",
            (null, true, _) => "copiée dans le presse-papiers",
            _ => null,
        };
        if (what == null)
        {
            return ActionResult.Fail(ControlMethod.IndependentCapture, "Presse-papiers occupé : capture non copiée.", "Réessayez.");
        }

        var result = ActionResult.Ok(ControlMethod.IndependentCapture, $"Capture plein écran ({mode}) {what}.");
        return saved == null ? result : result with { Hint = Path.GetFileName(saved) };
    }

    /// <summary>Opens the capture overlay in the configured default mode (used at the end of a delay).</summary>
    public ActionResult StartDefaultCapture() =>
        this.Guard(nameof(this.StartDefaultCapture), () => this.OpenOverlay(ParseMode(this._config().DefaultCaptureMode)));

    /// <summary>
    /// Blocking delay then capture (CLI use). The Logi plugin runs its own non-blocking countdown on the LCD
    /// and calls <see cref="StartDefaultCapture"/> at the end, so that "Annuler délai" works instantly.
    /// Allowed values = those of the installed Snipping Tool: 0, 3, 5, 10 s.
    /// </summary>
    public ActionResult StartDelayedCapture(int seconds)
    {
        if (!Configuration.WccConfig.Delays.Contains(seconds))
        {
            return ActionResult.Fail(ControlMethod.None, $"Délai {seconds} s non proposé (0, 3, 5 ou 10 s).");
        }

        CancellationTokenSource cts;
        lock (this._delayGate)
        {
            this._delay?.Cancel();
            cts = this._delay = new CancellationTokenSource();
        }

        Log.Info($"Delayed capture: {seconds} s");
        if (cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(seconds)))
        {
            Log.Info("Delayed capture cancelled");
            return new ActionResult(ActionStatus.Success, ControlMethod.None, "Capture différée annulée.");
        }

        return this.StartDefaultCapture();
    }

    public ActionResult CancelDelayedCapture()
    {
        lock (this._delayGate)
        {
            if (this._delay == null || this._delay.IsCancellationRequested)
            {
                return ActionResult.Unavailable("Aucune capture différée en cours.");
            }

            this._delay.Cancel();
            return ActionResult.Ok(ControlMethod.None, "Capture différée annulée.");
        }
    }

    internal static CaptureMode ParseMode(string mode) => mode switch
    {
        "window" => CaptureMode.Window,
        "freeform" => CaptureMode.Freeform,
        _ => CaptureMode.Rectangle,
    };

    private string? CaptureFolder(Configuration.WccConfig cfg) =>
        Configuration.ConfigStore.ResolveFolder(cfg.CaptureFolder) ?? KnownFolders.ScreenshotsPath;

    // =====================================================================================
    // Files
    // =====================================================================================

    public ActionResult OpenCapturesFolder() => this.Guard(nameof(this.OpenCapturesFolder), () =>
        OpenFolder(this.CaptureFolder(this._config()), "Captures d'écran"));

    /// <summary>
    /// Configured videoFolder, else Videos\Screen Recordings (documented default of Win+Shift+R recordings),
    /// else the Videos known folder itself (handles OneDrive / drive redirection).
    /// </summary>
    public ActionResult OpenVideosFolder() => this.Guard(nameof(this.OpenVideosFolder), () =>
    {
        var configured = Configuration.ConfigStore.ResolveFolder(this._config().VideoFolder);
        if (configured != null)
        {
            return OpenFolder(configured, "Vidéos (configuré)");
        }

        var rec = KnownFolders.ScreenRecordingsPath;
        return OpenFolder(rec != null && Directory.Exists(rec) ? rec : KnownFolders.VideosPath, "Vidéos");
    });

    // =====================================================================================
    // History (dial mode "history") — local files only, top level of the capture / video folders.
    // =====================================================================================

    public sealed record HistoryItem(string Name, string Kind, DateTime Time);

    private string?[] HistoryFolders()
    {
        var cfg = this._config();
        return new[]
        {
            Configuration.ConfigStore.ResolveFolder(cfg.CaptureFolder), KnownFolders.ScreenshotsPath,
            Configuration.ConfigStore.ResolveFolder(cfg.VideoFolder), KnownFolders.ScreenRecordingsPath,
        };
    }

    /// <summary>Newest images and videos (no thumbnails: names and times only, cheap).</summary>
    public IReadOnlyList<HistoryItem> GetHistory(int max = 30)
    {
        var exts = CaptureFiles.ImageExtensions.Concat(CaptureFiles.VideoExtensions).ToArray();
        return CaptureFiles.ListRecent(this.HistoryFolders(), exts, max)
            .Select(f => new HistoryItem(f.Name, CaptureFiles.VideoExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) ? "video" : "image", f.LastWriteTime))
            .ToList();
    }

    /// <summary>Opens one history entry by bare file name (resolved inside the history folders only).</summary>
    public ActionResult OpenHistoryItem(string fileName) => this.Guard(nameof(this.OpenHistoryItem), () =>
    {
        var f = CaptureFiles.ResolveInFolders(fileName, this.HistoryFolders());
        if (f == null)
        {
            return ActionResult.Unavailable("Fichier introuvable dans les dossiers de captures.");
        }

        Process.Start(new ProcessStartInfo(f.FullName) { UseShellExecute = true })?.Dispose();
        return ActionResult.Ok(ControlMethod.Shell, $"Ouvert ({f.LastWriteTime:HH:mm:ss}).");
    });

    /// <summary>
    /// Most recent recording in: configured videoFolder, Videos\Screen Recordings, Videos (top level only).
    /// If none exists, says so clearly — never opens an arbitrary other file.
    /// </summary>
    public ActionResult OpenLastVideo() => this.Guard(nameof(this.OpenLastVideo), () =>
    {
        var latest = CaptureFiles.FindLatest(
            new[] { Configuration.ConfigStore.ResolveFolder(this._config().VideoFolder), KnownFolders.ScreenRecordingsPath, KnownFolders.VideosPath },
            CaptureFiles.VideoExtensions);
        if (latest == null)
        {
            return ActionResult.Unavailable("Aucun enregistrement trouvé.",
                "Dossiers cherchés : dossier configuré, Vidéos\\Screen Recordings, Vidéos.");
        }

        Process.Start(new ProcessStartInfo(latest.FullName) { UseShellExecute = true })?.Dispose();
        return ActionResult.Ok(ControlMethod.Shell, $"Dernier enregistrement ouvert ({latest.LastWriteTime:HH:mm:ss}).");
    });

    /// <summary>
    /// Opens the video selection overlay with the documented Win+Shift+R. Never starts recording by itself:
    /// the user selects the area and presses Start (or the Start key once the probe has validated it).
    /// </summary>
    public ActionResult OpenVideoMode() => this.Guard(nameof(this.OpenVideoMode), () =>
    {
        if (Automation.CaptureOverlayAutomation.IsOpen() && !Automation.CaptureOverlayAutomation.WaitUntilClosed(1500))
        {
            return ActionResult.Sent(ControlMethod.UiAutomation, "Une sélection est déjà ouverte.", "Fermez-la ou terminez-la d'abord.");
        }

        var before = Automation.CaptureOverlayAutomation.CurrentToolbarHandles();
        var sent = SendGlobalShortcut("Win+Maj+R", KeyboardInput.VK_LWIN, KeyboardInput.VK_SHIFT, KeyboardInput.VK_R);
        if (!sent.IsOk)
        {
            return sent;
        }

        return Automation.CaptureOverlayAutomation.WaitForNewToolbarWindow(before, this._options.OverlayTimeoutMs) != null
            ? ActionResult.Ok(ControlMethod.WindowsShortcut, "Sélection vidéo ouverte : choisissez la zone puis Démarrer.")
            : ActionResult.Sent(ControlMethod.WindowsShortcut, "Win+Maj+R envoyé, sélection vidéo non confirmée.");
    });

    /// <summary>
    /// Most recent image among: capture saved in this session, configured folder, Windows Screenshots folder,
    /// Pictures folder (top level only — the disk is never scanned). Opens it with the default image app.
    /// Snipping Tool history / notifications are not accessible (no documented API).
    /// </summary>
    public ActionResult OpenLastCapture() => this.Guard(nameof(this.OpenLastCapture), () =>
    {
        var cfg = this._config();
        var latest = CaptureFiles.FindLatest(
            new[] { Configuration.ConfigStore.ResolveFolder(cfg.CaptureFolder), KnownFolders.ScreenshotsPath, KnownFolders.GetPath(KnownFolders.Pictures) },
            CaptureFiles.ImageExtensions,
            new[] { this._lastSavedCapture });
        if (latest == null)
        {
            return ActionResult.Unavailable("Aucune capture trouvée.",
                "Dossiers cherchés : dossier configuré, Captures d'écran, Images.");
        }

        Process.Start(new ProcessStartInfo(latest.FullName) { UseShellExecute = true })?.Dispose();
        return ActionResult.Ok(ControlMethod.Shell, $"Dernière capture ouverte ({latest.LastWriteTime:HH:mm:ss}).");
    });

    // =====================================================================================
    // Later phases (explicit, never faked)
    // =====================================================================================

    // =====================================================================================
    // Video recording (UI Automation of the recorder toolbar — phase 5 probe)
    // =====================================================================================

    /// <summary>
    /// Presses the recorder's Start button. Only possible once the USER has drawn the area (StartButton
    /// visible): the plugin never chooses the area and never records silently. Refused if already recording.
    /// </summary>
    public ActionResult StartRecording() => this.Guard(nameof(this.StartRecording), () =>
    {
        if (Automation.RecordingAutomation.IsRecordingIndicatorPresent())
        {
            return ActionResult.Unavailable("Un enregistrement est déjà en cours.");
        }

        var start = Automation.RecordingAutomation.FindById(Automation.SnippingToolUiMap.RecordStartButton);
        if (start == null)
        {
            return ActionResult.Unavailable("Aucune zone d'enregistrement préparée.",
                "Appuyez sur « Vidéo » puis tracez la zone à enregistrer.");
        }

        return Automation.RecordingAutomation.Invoke(start, "Start")
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Enregistrement démarré (compte à rebours de 3 s).")
            : ActionResult.Fail(ControlMethod.UiAutomation, "Bouton Démarrer non actionnable.");
    });

    /// <summary>Stops (and keeps) the recording. Waits up to 4 s for the end of the 3 s start countdown.</summary>
    public ActionResult StopRecording() => this.Guard(nameof(this.StopRecording), () =>
    {
        if (!Automation.RecordingAutomation.IsRecordingIndicatorPresent())
        {
            return ActionResult.Unavailable("Aucun enregistrement en cours.");
        }

        var stop = Automation.RecordingAutomation.WaitEnabled(Automation.SnippingToolUiMap.RecordStop, Automation.SnippingToolUiMap.RecordCountdownMs + 1000);
        return Automation.RecordingAutomation.Invoke(stop, "Stop")
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Enregistrement arrêté.")
            : ActionResult.Fail(ControlMethod.UiAutomation, "Bouton Arrêter indisponible.", "Utilisez la barre d'enregistrement.");
    });

    /// <summary>Pause and resume are the same recorder button (PauseResumeButton).</summary>
    public ActionResult PauseRecording() => this.TogglePause(nameof(this.PauseRecording));

    public ActionResult ResumeRecording() => this.TogglePause(nameof(this.ResumeRecording));

    public ActionResult TogglePauseRecording() => this.TogglePause(nameof(this.TogglePauseRecording));

    private ActionResult TogglePause(string action) => this.Guard(action, () =>
    {
        if (!Automation.RecordingAutomation.IsRecordingIndicatorPresent())
        {
            return ActionResult.Unavailable("Aucun enregistrement en cours.");
        }

        var b = Automation.RecordingAutomation.WaitEnabled(Automation.SnippingToolUiMap.RecordPauseResume, Automation.SnippingToolUiMap.RecordCountdownMs + 1000);
        return Automation.RecordingAutomation.Invoke(b, "Pause/Resume")
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Pause / reprise envoyée.")
            : ActionResult.Fail(ControlMethod.UiAutomation, "Bouton Pause indisponible.");
    });

    /// <summary>
    /// Toggles the microphone of the Snipping Tool recorder (never the global Windows microphone).
    /// Available before Start (area selected) and during recording.
    /// </summary>
    public ActionResult ToggleRecordingMicrophone() => this.Guard(nameof(this.ToggleRecordingMicrophone), () =>
    {
        if (this._config().MicrophoneControlMode == "disabled")
        {
            return ActionResult.Unavailable("Contrôle du micro désactivé (config.json).");
        }

        var mic = Automation.RecordingAutomation.FindById(Automation.SnippingToolUiMap.RecordMic)
                  ?? Automation.RecordingAutomation.FindById(Automation.SnippingToolUiMap.RecordMicBeforeStart);
        if (mic == null)
        {
            return ActionResult.Unavailable("Pas de barre d'enregistrement ouverte.", "Le micro se règle dans la barre vidéo.");
        }

        return Automation.RecordingAutomation.Invoke(mic, "Microphone")
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Micro de l'enregistrement basculé.")
            : ActionResult.Fail(ControlMethod.UiAutomation, "Bouton micro non actionnable.");
    });

    /// <summary>
    /// Discards the current recording (data loss, no confirmation dialog in the Snipping Tool — measured).
    /// The caller MUST have obtained a confirmation (double press on the key): <paramref name="confirmed"/>.
    /// </summary>
    public ActionResult DiscardRecording(bool confirmed) => this.Guard(nameof(this.DiscardRecording), () =>
    {
        if (!confirmed)
        {
            return ActionResult.Fail(ControlMethod.None, "Confirmation requise pour abandonner l'enregistrement.");
        }

        var b = Automation.RecordingAutomation.FindById(Automation.SnippingToolUiMap.RecordDiscard)
                ?? Automation.RecordingAutomation.FindById(Automation.SnippingToolUiMap.RecordCancel);
        if (b == null)
        {
            return ActionResult.Unavailable("Aucun enregistrement à abandonner.");
        }

        return Automation.RecordingAutomation.Invoke(b, "Discard/Cancel")
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Enregistrement abandonné.")
            : ActionResult.Fail(ControlMethod.UiAutomation, "Bouton Abandonner non actionnable.");
    }, debounce: false);

    /// <summary>
    /// "Enregistrer sous" of the video editor shown after Stop (saveButton). Measured on this machine:
    /// the recording is NOT auto-saved to Videos; it stays in the Snipping Tool's private temp folder
    /// until the user saves it. The Save As dialog lets the user choose name and folder.
    /// </summary>
    public ActionResult SaveVideoAs() => this.Guard(nameof(this.SaveVideoAs), () =>
    {
        var save = Automation.RecordingAutomation.FindById(Automation.SnippingToolUiMap.VideoSaveAs);
        if (save == null || Automation.RecordingAutomation.FindById(Automation.SnippingToolUiMap.VideoProgressSlider) == null)
        {
            return ActionResult.Unavailable("Aucune vidéo ouverte dans l'Outil Capture.");
        }

        var main = SnippingToolLocator.FindMainWindow();
        if (main != null)
        {
            WindowActivator.BringToFront(main.Handle);
        }

        return Automation.RecordingAutomation.Invoke(save, "Video Save As")
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Boîte « Enregistrer sous » ouverte.")
            : ActionResult.Fail(ControlMethod.UiAutomation, "Bouton Enregistrer sous non actionnable.");
    });

    public CaptureAppState GetRecordingState() => Automation.RecordingAutomation.Snapshot(detectPause: true).State;

    // =====================================================================================
    // Image editor (phase 6) — UI Automation on the editor window only; nothing is ever sent to
    // another application (no Ctrl+Z / Ctrl+C keystrokes at all).
    // =====================================================================================

    private ActionResult InEditor(string action, Func<System.Windows.Automation.AutomationElement, ActionResult> body, bool debounce = true) =>
        this.Guard(action, () =>
        {
            var root = Automation.EditorAutomation.EditorRoot();
            return root == null
                ? ActionResult.Unavailable("Aucune image ouverte dans l'Outil Capture.", "Faites une capture ou ouvrez une image.")
                : body(root);
        }, debounce);

    private ActionResult SelectTool(Automation.AnnotationTool tool, string label) => this.InEditor("Select" + tool, root =>
        Automation.EditorAutomation.SelectTool(root, tool)
            ? ActionResult.Ok(ControlMethod.UiAutomation, $"{label} sélectionné.")
            : ActionResult.Fail(ControlMethod.UiAutomation, $"{label} introuvable dans la barre d'outils."));

    public ActionResult SelectPen() => this.SelectTool(Automation.AnnotationTool.Pen, "Stylo");

    public ActionResult SelectHighlighter() => this.SelectTool(Automation.AnnotationTool.Highlighter, "Surligneur");

    public ActionResult SelectEraser() => this.SelectTool(Automation.AnnotationTool.Eraser, "Gomme");

    /// <summary>
    /// Measured in phase 6: the ruler EXISTS (overflow menu "Afficher / Masquer la règle", RulerButton);
    /// phase 1 had missed it. It is shown/hidden only: the ruler itself is not exposed to UI Automation,
    /// so rotating it with the dial is not implemented (not reliable).
    /// </summary>
    public ActionResult SelectRuler() => this.InEditor(nameof(this.SelectRuler), root =>
        Automation.EditorAutomation.ToggleRuler(root) != null
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Règle affichée / masquée.")
            : ActionResult.Fail(ControlMethod.UiAutomation, "Bouton Règle introuvable."));

    public ActionResult SelectProtractor() =>
        ActionResult.Unavailable("Pas de rapporteur dans l'Outil Capture 11.2609.15 (menus sondés en phase 6).");

    public ActionResult Undo() => this.InEditor(nameof(this.Undo), root =>
        Automation.EditorAutomation.InvokeById(root, Automation.SnippingToolUiMap.UndoButton)
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Annulé.")
            : ActionResult.Unavailable("Rien à annuler."));

    public ActionResult Redo() => this.InEditor(nameof(this.Redo), root =>
        Automation.EditorAutomation.InvokeById(root, Automation.SnippingToolUiMap.RedoButton)
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Rétabli.")
            : ActionResult.Unavailable("Rien à rétablir."));

    public string? GetActiveTool()
    {
        var root = Automation.EditorAutomation.EditorRoot();
        return root == null ? null : Automation.EditorAutomation.ActiveTool(root).ToString().ToLowerInvariant();
    }

    /// <summary>Copies the edited image (CopyBarButton) and checks that the clipboard now holds an image
    /// (format check only — the content is never read or logged).</summary>
    public ActionResult Copy() => this.InEditor(nameof(this.Copy), root =>
    {
        if (!Automation.EditorAutomation.InvokeById(root, Automation.SnippingToolUiMap.CopyButton))
        {
            return ActionResult.Fail(ControlMethod.UiAutomation, "Bouton Copier introuvable.");
        }

        return ClipboardHasImage(1500)
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Image copiée.")
            : ActionResult.Sent(ControlMethod.UiAutomation, "Copie demandée, presse-papiers non confirmé.");
    });

    /// <summary>
    /// The image editor has no plain "Save" (measured): captures are auto-saved by the Snipping Tool when its
    /// "save automatically" setting is on; otherwise "Save" opens "Save as".
    /// </summary>
    public ActionResult Save() => this.SaveAs();

    public ActionResult SaveAs() => this.InEditor(nameof(this.SaveAs), root =>
        Automation.EditorAutomation.InvokeById(root, Automation.SnippingToolUiMap.SaveAsButton)
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Boîte « Enregistrer sous » ouverte.")
            : ActionResult.Fail(ControlMethod.UiAutomation, "Bouton Enregistrer sous introuvable."));

    /// <summary>Opens the Windows share sheet; nothing is sent until the user picks a target.</summary>
    public ActionResult Share() => this.InEditor(nameof(this.Share), root =>
        Automation.EditorAutomation.InvokeById(root, Automation.SnippingToolUiMap.ShareButton)
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Feuille de partage ouverte : choisissez le destinataire.")
            : ActionResult.Fail(ControlMethod.UiAutomation, "Bouton Partager introuvable."));

    /// <summary>"Effacer toutes les marques" of the eraser flyout. Destructive: the caller must confirm.</summary>
    public ActionResult ClearAllAnnotations(bool confirmed) => this.InEditor(nameof(this.ClearAllAnnotations), root =>
    {
        if (!confirmed)
        {
            return ActionResult.Fail(ControlMethod.None, "Confirmation requise pour effacer toutes les annotations.");
        }

        var ok = Automation.EditorAutomation.WithToolFlyout(root, Automation.AnnotationTool.Eraser,
            r => Automation.EditorAutomation.InvokeById(r, Automation.SnippingToolUiMap.EraserClearAll));
        return ok
            ? ActionResult.Ok(ControlMethod.UiAutomation, "Toutes les annotations effacées (Annuler pour revenir).")
            : ActionResult.Fail(ControlMethod.UiAutomation, "« Effacer toutes les marques » introuvable.");
    }, debounce: false);

    /// <summary>Pen or highlighter colour: absolute index, or relative step (wraps around the palette).</summary>
    public ActionResult SetStrokeColor(int? index, int delta = 0) => this.InEditor(nameof(this.SetStrokeColor), root =>
    {
        var tool = StrokeTool(root);
        return Automation.EditorAutomation.WithToolFlyout(root, tool, r =>
        {
            var s = Automation.EditorAutomation.ReadStroke(r, tool);
            if (s == null || s.ColorCount == 0)
            {
                return ActionResult.Fail(ControlMethod.UiAutomation, "Palette introuvable.");
            }

            var target = index ?? WrapIndex(s.ColorIndex, delta, s.ColorCount);
            if (!Automation.EditorAutomation.SelectColor(r, target))
            {
                return ActionResult.Fail(ControlMethod.UiAutomation, $"Couleur {target} indisponible.");
            }

            var now = Automation.EditorAutomation.ReadStroke(r, tool);
            return ActionResult.Ok(ControlMethod.UiAutomation, $"{tool}: couleur {target + 1}/{s.ColorCount}")
                with { Hint = $"{target}|{now?.ColorName}" };
        });
    }, debounce: false);

    /// <summary>Pen or highlighter width: relative steps, absolute value, or reset to the measured default.</summary>
    public ActionResult SetStrokeWidth(double? value, int delta = 0, bool reset = false) => this.InEditor(nameof(this.SetStrokeWidth), root =>
    {
        var tool = StrokeTool(root);
        return Automation.EditorAutomation.WithToolFlyout(root, tool, r =>
        {
            var s = Automation.EditorAutomation.ReadStroke(r, tool);
            if (s == null)
            {
                return ActionResult.Fail(ControlMethod.UiAutomation, "Réglage d'épaisseur introuvable.");
            }

            var def = tool == Automation.AnnotationTool.Highlighter
                ? Automation.SnippingToolUiMap.HighlighterDefaultWidth
                : Automation.SnippingToolUiMap.PenDefaultWidth;
            var target = reset ? def : value ?? s.Width + delta;
            var applied = Automation.EditorAutomation.SetWidth(r, target);
            return ActionResult.Ok(ControlMethod.UiAutomation, $"{tool}: épaisseur {applied:0} ({s.Min:0}–{s.Max:0})")
                with { Hint = $"{applied:0}" };
        });
    }, debounce: false);

    /// <summary>Next / previous colour, wrapping around the palette; an unknown current index counts as 0.</summary>
    internal static int WrapIndex(int current, int delta, int count) =>
        count <= 0 ? 0 : (((Math.Max(0, current) + delta) % count) + count) % count;

    /// <summary>Stroke settings need the pen or highlighter: if the eraser (or nothing) is active, the pen is used.</summary>
    private static Automation.AnnotationTool StrokeTool(System.Windows.Automation.AutomationElement root) =>
        Automation.EditorAutomation.ActiveTool(root) == Automation.AnnotationTool.Highlighter
            ? Automation.AnnotationTool.Highlighter
            : Automation.AnnotationTool.Pen;

    private static bool ClipboardHasImage(int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var has = false;
            var t = new Thread(() =>
            {
                try
                {
                    has = System.Windows.Forms.Clipboard.ContainsImage();
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    // clipboard busy
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(500);
            if (has)
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    public ActionResult ExtractText() => ActionResult.Later("Extraire le texte", 9);
    public ActionResult OpenTextActions() => ActionResult.Later("Actions de texte", 9);
    public ActionResult RedactSensitiveInformation() => ActionResult.Later("Masquer les données", 9);

    // Zoom: editor overflow menu "Zoom" (ZoomInMenuItem / ZoomOutMenuItem / ZoomToActualSizeMenuItem /
    // ZoomFitToWindow). ≈ 0.5 s per step (menu opened and closed each time). No keyboard / wheel injection.
    private ActionResult ZoomCore(string action, string itemId, string done, string limit) => this.InEditor(action, root =>
        Automation.EditorAutomation.Zoom(root, itemId)
            ? ActionResult.Ok(ControlMethod.UiAutomation, done)
            : ActionResult.Unavailable(limit), debounce: false);

    public ActionResult ZoomIn() => this.ZoomCore(nameof(this.ZoomIn), Automation.SnippingToolUiMap.ZoomIn, "Zoom avant.", "Zoom maximal atteint.");

    public ActionResult ZoomOut() => this.ZoomCore(nameof(this.ZoomOut), Automation.SnippingToolUiMap.ZoomOut, "Zoom arrière.", "Zoom minimal atteint.");

    public ActionResult ResetZoom() => this.ZoomCore(nameof(this.ResetZoom), Automation.SnippingToolUiMap.ZoomActualSize, "Zoom 100 %.", "Zoom 100 % indisponible.");

    public ActionResult FitToWindow() => this.ZoomCore(nameof(this.FitToWindow), Automation.SnippingToolUiMap.ZoomFit, "Ajusté à la fenêtre.", "Ajustement indisponible.");

    /// <summary>The editor does not expose its zoom percentage to UI Automation (measured): unknown.</summary>
    public int? GetZoomLevel() => null;

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private ActionResult LaunchImageUri(CaptureMode mode)
    {
        var uri = ScreenClipUri.Image(mode, this._options.EnabledImageModes, this._options.SnippingToolAutoSave);
        Log.Info($"Method selected: documented URI ({uri})");
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        return ActionResult.Sent(ControlMethod.DocumentedUri, $"Sélection de capture « {mode} » demandée.");
    }

    private static ActionResult SendGlobalShortcut(string label, params ushort[] keys)
    {
        // A physically held modifier (e.g. Ctrl from a keyboard) would corrupt the chord: wait for release.
        var waited = 0;
        while (KeyboardInput.AnyModifierDown() && waited < 1000)
        {
            Thread.Sleep(25);
            waited += 25;
        }

        if (KeyboardInput.AnyModifierDown())
        {
            return ActionResult.Fail(ControlMethod.WindowsShortcut, $"{label} non envoyé : une touche de modification est maintenue.",
                "Relâchez Ctrl / Maj / Alt / Windows puis réessayez.");
        }

        Log.Info($"Method selected: Windows shortcut ({label})");
        return KeyboardInput.SendChord(keys)
            ? ActionResult.Sent(ControlMethod.WindowsShortcut, $"Raccourci {label} envoyé.")
            : ActionResult.Fail(ControlMethod.WindowsShortcut, $"Windows a refusé le raccourci {label}.",
                "Une fenêtre administrateur est peut-être au premier plan (UIPI).");
    }

    private static ActionResult OpenFolder(string? path, string label)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ActionResult.Fail(ControlMethod.Shell, $"Dossier « {label} » introuvable (Known Folder non résolu).");
        }

        if (!Directory.Exists(path))
        {
            return ActionResult.Unavailable($"Le dossier « {label} » n'existe pas encore.",
                "Il sera créé par Windows lors de la première capture.");
        }

        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
        return ActionResult.Ok(ControlMethod.Shell, $"Dossier « {label} » ouvert.");
    }

    private static T? WaitFor<T>(Func<T?> probe, int timeoutMs) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var v = probe();
            if (v != null)
            {
                return v;
            }

            Thread.Sleep(100);
        }

        return null;
    }

    /// <summary>Debounce + logging + exception barrier for every action.</summary>
    private ActionResult Guard(string action, Func<ActionResult> body, bool debounce = true)
    {
        Log.Info($"Action requested: {action}");
        if (debounce && !this._debouncer.TryEnter(action))
        {
            Log.Debug($"{action} debounced");
            return ActionResult.Debounced();
        }

        try
        {
            var result = body();
            Log.Write(result.IsOk ? LogLevel.Info : LogLevel.Warning, $"{action} -> {result}");
            return result;
        }
        catch (Exception ex)
        {
            Log.Error($"{action} failed", ex);
            return ActionResult.Fail(ControlMethod.None, $"Erreur inattendue ({ex.GetType().Name}).", "Voir le journal.");
        }
    }
}
