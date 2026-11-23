namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using WindowsCaptureController.Shared;

    // Dynamic video keys (phase 5). State comes from the agent ("video.state") every 1.5 s:
    // cheap Win32 window-class check first, UI Automation only when a recorder UI exists.
    //
    //   wcc.rec.toggle   Enregistrer (red dot, area prepared) / Arrêter (white square on red, recording)
    //   wcc.rec.pause    Pause / Reprendre — paused state read from the recorder button name (measured),
    //                    frozen-timer fallback for unknown wordings
    //   wcc.rec.mic      Micro ON / OFF of the RECORDER (never the global Windows microphone)
    //   wcc.rec.discard  Abandonner: data loss -> double press within 3 s required
    public sealed class VideoKeys : PluginDynamicCommand
    {
        private const string Group = "Windows Capture - Vidéo";
        private const int PollMs = 1500;
        private const int ConfirmMs = 3000;
        private const int FeedbackMs = 1500;

        private static readonly (string Id, string Name)[] Defs =
        {
            ("wcc.rec.toggle", "Démarrer / Arrêter l'enregistrement"),
            ("wcc.rec.pause", "Pause / Reprendre l'enregistrement"),
            ("wcc.rec.mic", "Micro de l'enregistrement ON / OFF"),
            ("wcc.rec.discard", "Abandonner l'enregistrement (double appui)"),
            ("wcc.rec.saveas", "Enregistrer la vidéo sous… (après Arrêter)"),
        };

        private readonly ConcurrentDictionary<string, DateTime> _lastPress = new();
        private readonly ConcurrentDictionary<string, (DateTime Until, string Text, BitmapColor Frame)> _flash = new();
        private Timer _timer;
        private volatile string _state = "Unknown";
        private volatile string _mic = "Unknown";
        private volatile string _clock = "";
        private volatile bool _enabled;
        private DateTime _discardArmedAt = DateTime.MinValue;
        private string _fingerprint = "";
        private int _polling;

        public VideoKeys()
            : base()
        {
            foreach (var d in Defs)
            {
                this.AddParameter(d.Id, d.Name, Group);
            }
        }

        private WindowsCaptureControllerPlugin CapturePlugin => this.Plugin as WindowsCaptureControllerPlugin;

        private bool Recording => this._state is "Recording" or "RecordingPaused";

        protected override bool OnLoad()
        {
            this._timer = new Timer(_ => this.Poll(), null, 2000, PollMs);
            return true;
        }

        protected override bool OnUnload()
        {
            this._timer?.Dispose();
            return true;
        }

        private void Poll()
        {
            var agent = this.CapturePlugin?.Agent;
            if (agent == null || agent.IsDisposed || Interlocked.Exchange(ref this._polling, 1) == 1)
            {
                return; // not loaded yet, or stale generation after a reload: no polling, no agent start
            }

            try
            {
                var r = agent.Call(AgentCommands.VideoState, timeoutMs: 2500);
                if (!r.Ok)
                {
                    return;
                }

                this._state = r.Data.GetValueOrDefault("state", "Unknown");
                this._mic = r.Data.GetValueOrDefault("mic", "Unknown");
                this._clock = r.Data.GetValueOrDefault("timer", "");
                this._enabled = r.Data.GetValueOrDefault("enabled", "False") == "True";
                var fp = $"{this._state}|{this._mic}|{this._clock}|{this._enabled}";
                if (fp != this._fingerprint)
                {
                    this._fingerprint = fp;
                    this.ActionImageChanged();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning("video state poll failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref this._polling, 0);
            }
        }

        protected override void RunCommand(string actionParameter)
        {
            var plugin = this.CapturePlugin;
            if (plugin == null || Defs.All(d => d.Id != actionParameter))
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (this._lastPress.TryGetValue(actionParameter, out var last) && (now - last).TotalMilliseconds < 400)
            {
                return;
            }

            this._lastPress[actionParameter] = now;

            string cmd = null;
            Dictionary<string, string> args = null;
            switch (actionParameter)
            {
                case "wcc.rec.toggle":
                    if (this.Recording)
                    {
                        cmd = AgentCommands.VideoStop;
                    }
                    else if (this._state == "VideoReady")
                    {
                        cmd = AgentCommands.VideoStart;
                    }
                    else
                    {
                        // Never start without an area chosen by the user.
                        this.Flash(actionParameter, "Zone ?", Palette.Orange);
                        return;
                    }

                    break;

                case "wcc.rec.pause":
                    if (!this.Recording)
                    {
                        this.Flash(actionParameter, "Pas d'enreg.", Palette.Orange);
                        return;
                    }

                    cmd = AgentCommands.VideoPause;
                    break;

                case "wcc.rec.mic":
                    cmd = AgentCommands.VideoMic;
                    break;

                case "wcc.rec.saveas":
                    if (this._state != "VideoEditing")
                    {
                        this.Flash(actionParameter, "Pas de vidéo", Palette.Orange);
                        return;
                    }

                    cmd = AgentCommands.VideoSaveAs;
                    break;

                case "wcc.rec.discard":
                    if (!this.Recording && this._state != "VideoReady")
                    {
                        this.Flash(actionParameter, "Rien à annuler", Palette.Orange);
                        return;
                    }

                    if ((now - this._discardArmedAt).TotalMilliseconds > ConfirmMs)
                    {
                        this._discardArmedAt = now;
                        this.Flash(actionParameter, "Encore 1x", Palette.Orange, ConfirmMs);
                        return;
                    }

                    this._discardArmedAt = DateTime.MinValue;
                    cmd = AgentCommands.VideoDiscard;
                    args = new Dictionary<string, string> { ["confirmed"] = "true" };
                    break;
            }

            Task.Run(() =>
            {
                try
                {
                    PluginLog.Info($"Video key: {actionParameter} -> {cmd}");
                    // Stop / pause may wait for the end of the 3 s start countdown.
                    var agent = plugin.Agent ?? throw new InvalidOperationException("plugin not loaded");
                    var r = agent.Call(cmd, args, timeoutMs: 7000);
                    PluginLog.Info($"{actionParameter}: ok={r.Ok} status={r.Status} message={r.Message}");
                    this.Flash(actionParameter, r.Ok ? null : (r.Status == "NotAvailable" ? "N/D" : "Erreur"), r.Ok ? Palette.Green : Palette.Orange);
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Video key {actionParameter} crashed", ex);
                    this.Flash(actionParameter, "Erreur", Palette.Red);
                }
                finally
                {
                    this.Poll();
                }
            });
        }

        private void Flash(string parameter, string text, BitmapColor frame, int ms = FeedbackMs)
        {
            this._flash[parameter] = (DateTime.UtcNow.AddMilliseconds(ms), text, frame);
            this.ActionImageChanged(parameter);
            Task.Delay(ms + 50).ContinueWith(_ => this.ActionImageChanged(parameter));
        }

        protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) =>
            Defs.FirstOrDefault(d => d.Id == actionParameter).Name ?? actionParameter;

        protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
        {
            try
            {
                var spec = actionParameter switch
                {
                    "wcc.rec.toggle" when this.Recording => new Icons.Spec
                    {
                        Glyph = Glyph.StopSquare, Label = "Arrêter", Bg = Palette.Red, Fg = Palette.White,
                        Corner = this._clock.Length >= 5 ? this._clock[^5..] : "",
                    },
                    "wcc.rec.toggle" when this._state == "VideoReady" => new Icons.Spec { Glyph = Glyph.RecordDot, Label = "Enregistrer" },
                    "wcc.rec.toggle" => new Icons.Spec { Glyph = Glyph.RecordDot, Label = "Enregistrer", Fg = Palette.Grey },
                    "wcc.rec.pause" when this._state == "RecordingPaused" => new Icons.Spec { Glyph = Glyph.RecordDot, Label = "Reprendre", Frame = Palette.Orange },
                    "wcc.rec.pause" when this.Recording => new Icons.Spec { Glyph = Glyph.PauseBars, Label = "Pause", Fg = Palette.FolderYellow },
                    "wcc.rec.pause" => new Icons.Spec { Glyph = Glyph.PauseBars, Label = "Pause", Fg = Palette.Grey },
                    "wcc.rec.mic" when this._mic == "On" => new Icons.Spec { Glyph = Glyph.Microphone, Label = "Micro ON", Fg = Palette.Green },
                    "wcc.rec.mic" when this._mic == "Off" => new Icons.Spec { Glyph = Glyph.Microphone, Label = "Micro OFF", Fg = Palette.Red },
                    "wcc.rec.mic" => new Icons.Spec { Glyph = Glyph.Microphone, Label = "Micro", Fg = Palette.Grey },
                    "wcc.rec.discard" when this.Recording || this._state == "VideoReady" => new Icons.Spec { Glyph = Glyph.Trash, Label = "Abandonner", Fg = Palette.White },
                    "wcc.rec.saveas" when this._state == "VideoEditing" => new Icons.Spec { Glyph = Glyph.VideoFolder, Label = "Enreg. sous", Frame = Palette.Orange },
                    "wcc.rec.saveas" => new Icons.Spec { Glyph = Glyph.VideoFolder, Label = "Enreg. sous", Fg = Palette.Grey },
                    _ => new Icons.Spec { Glyph = Glyph.Trash, Label = "Abandonner", Fg = Palette.Grey },
                };

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

