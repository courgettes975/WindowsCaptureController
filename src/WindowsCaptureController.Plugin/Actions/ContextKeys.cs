namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using WindowsCaptureController.Shared;

    // "Contexte 1…9": nine keys whose function and icon follow the Snipping Tool state (ContextProfiles).
    // Place them on one Keypad page: the page then becomes Capture / Image / Vidéo / Enregistrement by itself.
    // State: "app.state" every 1.5 s; plus "edit.state" (active tool, undo/redo) while an image is open and
    // "video.state" (timer, mic) while recording. A key showing the wrong action is not possible for more than
    // one poll: before executing, a press re-checks the state and does nothing if the slot changed meanwhile.
    public sealed class ContextKeys : PluginDynamicCommand
    {
        private const string Prefix = "wcc.ctx.";
        private const int PollMs = 1500;
        private const int ConfirmMs = 3000;
        private const int FeedbackMs = 1500;

        private readonly ConcurrentDictionary<string, DateTime> _lastPress = new();
        private readonly ConcurrentDictionary<string, (DateTime Until, string Text, BitmapColor Frame)> _flash = new();
        private readonly ConcurrentDictionary<string, DateTime> _armed = new();
        private Timer _timer;
        private int _polling;
        private volatile string _state = "Closed";
        private volatile bool _profiles = true;
        private volatile string _tool = "None";
        private volatile bool _undo;
        private volatile bool _redo;
        private volatile string _clock = "";
        private volatile string _mic = "Unknown";
        private string _fingerprint = "";

        public ContextKeys()
            : base()
        {
            for (var i = 1; i <= ContextProfiles.Slots; i++)
            {
                this.AddParameter(Prefix + i, $"Contexte {i} (s'adapte à l'Outil Capture)", "Windows Capture - Contextuel");
            }
        }

        private WindowsCaptureControllerPlugin CapturePlugin => this.Plugin as WindowsCaptureControllerPlugin;

        private static int SlotOf(string parameter) =>
            parameter != null && parameter.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(parameter.AsSpan(Prefix.Length), out var s) ? s : 0;

        protected override bool OnLoad()
        {
            this._timer = new Timer(_ => this.Poll(), null, 1500, PollMs);
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
                return;
            }

            try
            {
                var r = agent.Call(AgentCommands.AppState, timeoutMs: 3000);
                if (!r.Ok)
                {
                    return;
                }

                this._state = r.Data.GetValueOrDefault("state", "Unknown");
                this._profiles = r.Data.GetValueOrDefault("profiles", "True") == "True";
                if (this._state == "ImageEditing")
                {
                    var e = agent.Call(AgentCommands.EditState, timeoutMs: 2500);
                    this._tool = e.Data.GetValueOrDefault("tool", "None");
                    this._undo = e.Data.GetValueOrDefault("undo") == "True";
                    this._redo = e.Data.GetValueOrDefault("redo") == "True";
                }
                else if (this._state is "Recording" or "RecordingPaused" or "VideoReady")
                {
                    var v = agent.Call(AgentCommands.VideoState, timeoutMs: 2500);
                    this._clock = v.Data.GetValueOrDefault("timer", "");
                    this._mic = v.Data.GetValueOrDefault("mic", "Unknown");
                }

                var fp = $"{this._state}|{this._profiles}|{this._tool}|{this._undo}|{this._redo}|{this._clock}|{this._mic}";
                if (fp != this._fingerprint)
                {
                    this._fingerprint = fp;
                    this.ActionImageChanged();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning("context state poll failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref this._polling, 0);
            }
        }

        protected override void RunCommand(string actionParameter)
        {
            var plugin = this.CapturePlugin;
            var slot = SlotOf(actionParameter);
            var action = ContextProfiles.Resolve(this._state, slot, this._profiles);
            if (plugin == null || action == null || action.DisplayOnly || action.Command == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (this._lastPress.TryGetValue(actionParameter, out var last) && (now - last).TotalMilliseconds < 400)
            {
                return;
            }

            this._lastPress[actionParameter] = now;
            if (action.Confirm)
            {
                if (!this._armed.TryGetValue(action.Id, out var at) || (now - at).TotalMilliseconds > ConfirmMs)
                {
                    this._armed[action.Id] = now;
                    this.Flash(actionParameter, "Encore 1x", Palette.Orange, ConfirmMs);
                    return;
                }

                this._armed.TryRemove(action.Id, out _);
            }

            var expectedState = this._state;
            Task.Run(() =>
            {
                try
                {
                    var agent = plugin.Agent ?? throw new InvalidOperationException("plugin not loaded");

                    // The display may be up to one poll old: re-check that this slot still means the same action.
                    var st = agent.Call(AgentCommands.AppState, timeoutMs: 3000);
                    var nowState = st.Ok ? st.Data.GetValueOrDefault("state", expectedState) : expectedState;
                    if (ContextProfiles.Resolve(nowState, slot, this._profiles)?.Id != action.Id)
                    {
                        this._state = nowState;
                        this.ActionImageChanged();
                        this.Flash(actionParameter, "Changé", Palette.Orange);
                        PluginLog.Info($"context slot {slot}: state changed ({expectedState} -> {nowState}), press ignored");
                        return;
                    }

                    var r = agent.Call(action.Command, action.Args?.ToDictionary(kv => kv.Key, kv => kv.Value), 7000);
                    PluginLog.Info($"context {slot} {action.Id}: ok={r.Ok} status={r.Status} message={r.Message}");
                    this.Flash(actionParameter, r.Ok ? (action.Id is "copy" or "video.copy" ? "Copié ✓" : null) : (r.Status == "NotAvailable" ? "N/D" : "Erreur"),
                        r.Ok ? Palette.Green : Palette.Orange);
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"context key {slot} crashed", ex);
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
            $"Contexte {SlotOf(actionParameter)}";

        protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
        {
            try
            {
                var action = ContextProfiles.Resolve(this._state, SlotOf(actionParameter), this._profiles);
                if (action == null)
                {
                    return Icons.Render(imageSize, new Icons.Spec { Glyph = Glyph.Empty, Label = "", Fg = Palette.Grey });
                }

                var spec = new Icons.Spec
                {
                    Glyph = Enum.TryParse<Glyph>(action.Glyph, out var g) ? g : Glyph.Empty,
                    Label = action.Label,
                };

                switch (action.Id)
                {
                    case "open":
                        spec.Bg = Palette.Blue;
                        break;
                    case "video":
                        spec.Bg = Palette.DarkRed;
                        break;
                    case "rec.stop":
                        spec.Bg = Palette.Red;
                        spec.Corner = this._clock.Length >= 5 ? this._clock[^5..] : "";
                        break;
                    case "rec.pause" when this._state == "RecordingPaused":
                        spec.Glyph = Glyph.Play;
                        spec.Label = "Reprendre";
                        spec.Frame = Palette.Orange;
                        break;
                    case "rec.pause":
                        spec.Fg = Palette.FolderYellow;
                        break;
                    case "rec.timer":
                        spec.Glyph = Glyph.Empty;
                        spec.BigText = this._clock.Length >= 5 ? this._clock[^5..] : "--:--";
                        spec.Label = this._state == "RecordingPaused" ? "En pause" : "Durée";
                        break;
                    case "rec.mic":
                        spec.Label = this._mic == "On" ? "Micro ON" : this._mic == "Off" ? "Micro OFF" : "Micro";
                        spec.Fg = this._mic == "On" ? Palette.Green : this._mic == "Off" ? Palette.Red : Palette.White;
                        break;
                    case "pen" when this._tool == "Pen":
                    case "highlighter" when this._tool == "Highlighter":
                        spec.Frame = Palette.Blue;
                        break;
                    case "eraser" when this._tool == "Eraser":
                        spec.Frame = Palette.Blue;
                        spec.Bg = Palette.Blue;
                        break;
                    case "undo":
                        spec.Fg = this._undo ? Palette.White : Palette.Grey;
                        break;
                    case "redo":
                        spec.Fg = this._redo ? Palette.White : Palette.Grey;
                        break;
                    case "info.area":
                        spec.Fg = Palette.Grey;
                        break;
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
                return Icons.Render(imageSize, new Icons.Spec { Glyph = Glyph.Error, Label = "Erreur", Fg = Palette.Red });
            }
        }
    }
}
