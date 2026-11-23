namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using WindowsCaptureController.Shared;

    // One PluginDynamicCommand exposing each action as a parameter (each one is its own draggable
    // action in Logi Options+, group "Windows Capture").
    //  - presses run on the thread pool, never on the service thread;
    //  - 400 ms anti-bounce per key + one press at a time per key;
    //  - feedback on the LCD for ~1.5 s: green frame (done) / orange frame + "N/D" or "Erreur";
    //  - delayed capture: non-blocking countdown drawn on the pressed key (10 … 1), cancellable with
    //    "Annuler délai" or by pressing the same delay key again; at 0 the overlay opens in the
    //    configured default mode (config.json "defaultCaptureMode").
    public sealed class CaptureKeys : PluginDynamicCommand
    {
        private const int DebounceMs = 400;
        private const int FeedbackMs = 1500;
        private const string Group = "Windows Capture";
        private const string CancelId = "wcc.delay.cancel";

        private sealed record KeyDef(string Id, string Name, string Command, int TimeoutMs, Icons.Spec View, int DelaySeconds = 0);

        private static readonly IReadOnlyList<KeyDef> Keys = new[]
        {
            new KeyDef("wcc.open", "Capture Windows (ouvrir / afficher l'Outil Capture)", AgentCommands.Launch, 9000,
                new Icons.Spec { Glyph = Glyph.Scissors, Label = "Capture", Bg = Palette.Blue }),
            new KeyDef("wcc.new", "Nouvelle capture", AgentCommands.NewCapture, 6000,
                new Icons.Spec { Glyph = Glyph.NewCapture, Label = "Nouvelle" }),
            new KeyDef("wcc.rectangle", "Capture rectangulaire", AgentCommands.Rectangle, 6000,
                new Icons.Spec { Glyph = Glyph.Rectangle, Label = "Rectangle" }),
            new KeyDef("wcc.window", "Capture de fenêtre", AgentCommands.Window, 6000,
                new Icons.Spec { Glyph = Glyph.Window, Label = "Fenêtre", Bg = Palette.DarkGrey }),
            new KeyDef("wcc.freeform", "Capture de forme libre", AgentCommands.Freeform, 6000,
                new Icons.Spec { Glyph = Glyph.Freeform, Label = "Forme libre" }),
            new KeyDef("wcc.fullscreen", "Plein écran (écran selon config.json)", AgentCommands.Fullscreen, 6000,
                new Icons.Spec { Glyph = Glyph.Fullscreen, Label = "Plein écran" }),
            new KeyDef("wcc.delay.3", "Capture dans 3 secondes", AgentCommands.DefaultCapture, 6000,
                new Icons.Spec { Glyph = Glyph.Delay, Label = "Délai 3 s" }, 3),
            new KeyDef("wcc.delay.5", "Capture dans 5 secondes", AgentCommands.DefaultCapture, 6000,
                new Icons.Spec { Glyph = Glyph.Delay, Label = "Délai 5 s" }, 5),
            new KeyDef("wcc.delay.10", "Capture dans 10 secondes", AgentCommands.DefaultCapture, 6000,
                new Icons.Spec { Glyph = Glyph.Delay, Label = "Délai 10 s" }, 10),
            new KeyDef(CancelId, "Annuler délai", null, 0,
                new Icons.Spec { Glyph = Glyph.Cancel, Label = "Annuler", Fg = Palette.Grey }),
            new KeyDef("wcc.last", "Dernière capture", AgentCommands.OpenLastCapture, 5000,
                new Icons.Spec { Glyph = Glyph.LastCapture, Label = "Dernière" }),
            new KeyDef("wcc.folder", "Dossier des captures", AgentCommands.CapturesFolder, 4000,
                new Icons.Spec { Glyph = Glyph.Folder, Label = "Captures" }),
            new KeyDef("wcc.video", "Vidéo (préparer : choisir la zone, ne démarre pas seul)", AgentCommands.VideoMode, 6000,
                new Icons.Spec { Glyph = Glyph.VideoCamera, Label = "Vidéo", Bg = Palette.DarkRed }),
            new KeyDef("wcc.video.last", "Dernière vidéo", AgentCommands.OpenLastVideo, 5000,
                new Icons.Spec { Glyph = Glyph.LastVideo, Label = "Dern. vidéo" }),
            new KeyDef("wcc.video.folder", "Dossier des vidéos", AgentCommands.VideosFolder, 4000,
                new Icons.Spec { Glyph = Glyph.VideoFolder, Label = "Vidéos" }),
        };

        private readonly ConcurrentDictionary<string, DateTime> _lastPress = new();
        private readonly ConcurrentDictionary<string, bool> _busy = new();
        private readonly ConcurrentDictionary<string, (DateTime Until, string Text, BitmapColor Frame)> _flash = new();
        private readonly object _countdownGate = new();
        private Countdown _countdown;

        private sealed class Countdown
        {
            public string KeyId;
            public int Remaining;
            public CancellationTokenSource Cts;
        }

        public CaptureKeys()
            : base()
        {
            foreach (var k in Keys)
            {
                this.AddParameter(k.Id, k.Name, Group);
            }
        }

        private WindowsCaptureControllerPlugin CapturePlugin => this.Plugin as WindowsCaptureControllerPlugin;

        protected override bool OnUnload()
        {
            this.CancelCountdown("plugin unloaded");
            return true;
        }

        protected override void RunCommand(string actionParameter)
        {
            var plugin = this.CapturePlugin;
            var key = Keys.FirstOrDefault(k => k.Id == actionParameter);
            if (plugin == null || key == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (this._lastPress.TryGetValue(actionParameter, out var last) && (now - last).TotalMilliseconds < DebounceMs)
            {
                PluginLog.Info($"{key.Id} debounced");
                return;
            }

            this._lastPress[actionParameter] = now;

            if (key.Id == CancelId)
            {
                if (!this.CancelCountdown("cancel key"))
                {
                    this.Flash(actionParameter, "Aucun délai", Palette.Orange);
                }

                return;
            }

            if (key.DelaySeconds > 0)
            {
                this.StartCountdown(key);
                return;
            }

            this.Execute(plugin, key, actionParameter);
        }

        private void Execute(WindowsCaptureControllerPlugin plugin, KeyDef key, string parameter)
        {
            if (!this._busy.TryAdd(parameter, true))
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    PluginLog.Info($"Key: {key.Id} -> {key.Command}");
                    var agent = plugin.Agent ?? throw new InvalidOperationException("plugin not loaded");
                    var r = agent.Call(key.Command, timeoutMs: key.TimeoutMs);
                    PluginLog.Info($"{key.Id}: ok={r.Ok} status={r.Status} method={r.Method} message={r.Message}");
                    if (r.Ok)
                    {
                        this.Flash(parameter, null, Palette.Green);
                    }
                    else
                    {
                        this.Flash(parameter, r.Status is "NotAvailable" or "NotImplementedYet" ? "N/D" : "Erreur", Palette.Orange);
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Key {key.Id} crashed", ex);
                    this.Flash(parameter, "Erreur", Palette.Red);
                }
                finally
                {
                    this._busy.TryRemove(parameter, out _);
                }
            });
        }

        // ---- Delayed capture ---------------------------------------------------------------------

        private void StartCountdown(KeyDef key)
        {
            Countdown cd;
            lock (this._countdownGate)
            {
                // Same key pressed again during its own countdown = cancel (intuitive toggle).
                if (this._countdown != null && this._countdown.KeyId == key.Id)
                {
                    this.CancelCountdownLocked("same delay key pressed again");
                    return;
                }

                this.CancelCountdownLocked("replaced by another delay");
                cd = this._countdown = new Countdown { KeyId = key.Id, Remaining = key.DelaySeconds, Cts = new CancellationTokenSource() };
            }

            PluginLog.Info($"Delayed capture started: {key.DelaySeconds} s");
            this.RefreshCountdownKeys(key.Id);
            _ = this.RunCountdownAsync(key, cd);
        }

        private async Task RunCountdownAsync(KeyDef key, Countdown cd)
        {
            try
            {
                while (cd.Remaining > 0)
                {
                    await Task.Delay(1000, cd.Cts.Token).ConfigureAwait(false);
                    cd.Remaining--;
                    this.RefreshCountdownKeys(key.Id);
                }

                lock (this._countdownGate)
                {
                    if (!ReferenceEquals(this._countdown, cd) || cd.Cts.IsCancellationRequested)
                    {
                        return;
                    }

                    this._countdown = null;
                }

                this.RefreshCountdownKeys(key.Id);
                var plugin = this.CapturePlugin;
                if (plugin != null)
                {
                    this.Execute(plugin, key, key.Id);
                }
            }
            catch (TaskCanceledException)
            {
                // cancelled: keys already refreshed by CancelCountdown
            }
        }

        private bool CancelCountdown(string reason)
        {
            lock (this._countdownGate)
            {
                return this.CancelCountdownLocked(reason);
            }
        }

        private bool CancelCountdownLocked(string reason)
        {
            var cd = this._countdown;
            if (cd == null)
            {
                return false;
            }

            this._countdown = null;
            cd.Cts.Cancel();
            PluginLog.Info($"Delayed capture cancelled ({reason})");
            this.Flash(cd.KeyId, "Annulé", Palette.Orange);
            this.ActionImageChanged(CancelId);
            return true;
        }

        private void RefreshCountdownKeys(string keyId)
        {
            this.ActionImageChanged(keyId);
            this.ActionImageChanged(CancelId);
        }

        // ---- Rendering -----------------------------------------------------------------------------

        private void Flash(string parameter, string text, BitmapColor frame)
        {
            this._flash[parameter] = (DateTime.UtcNow.AddMilliseconds(FeedbackMs), text, frame);
            this.ActionImageChanged(parameter);
            Task.Delay(FeedbackMs + 50).ContinueWith(_ => this.ActionImageChanged(parameter));
        }

        protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) =>
            Keys.FirstOrDefault(k => k.Id == actionParameter)?.Name ?? actionParameter;

        protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
        {
            try
            {
                var key = Keys.FirstOrDefault(k => k.Id == actionParameter);
                if (key == null)
                {
                    return Icons.Render(imageSize, new Icons.Spec { Glyph = Glyph.Empty, Label = "—", Fg = Palette.Grey });
                }

                var v = key.View;
                var spec = new Icons.Spec { Glyph = v.Glyph, Label = v.Label, Fg = v.Fg, Bg = v.Bg, Frame = v.Frame };

                var cd = this._countdown;
                if (cd != null && cd.KeyId == actionParameter)
                {
                    spec.BigText = cd.Remaining > 0 ? cd.Remaining.ToString() : "…";
                    spec.Label = "Capture";
                    spec.Frame = Palette.Red;
                }
                else if (key.Id == CancelId && cd != null)
                {
                    spec.Fg = Palette.White;
                    spec.Bg = Palette.Red;
                    spec.Label = "Annuler délai";
                }

                if (this._flash.TryGetValue(actionParameter, out var f) && f.Until > DateTime.UtcNow)
                {
                    spec.Frame = f.Frame;
                    if (!string.IsNullOrEmpty(f.Text))
                    {
                        spec.Label = f.Text;
                    }
                }

                return Icons.Render(imageSize, spec);
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Render failed for {actionParameter}", ex);
                return Icons.Render(imageSize, new Icons.Spec { Glyph = Glyph.Empty, Label = "Erreur", Fg = Palette.Red });
            }
        }
    }
}
