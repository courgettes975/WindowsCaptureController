namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using WindowsCaptureController.Shared;

    // Annotation page (phase 6). Editor state ("edit.state": active tool, undo/redo availability) is polled
    // every 1.5 s; colour / width are known from the last colour/width command (the flyout is only opened
    // when the user changes them). Everything goes through UI Automation of the Snipping Tool editor:
    // no Ctrl+Z / Ctrl+C keystroke is ever sent to any application.
    public sealed class AnnotationKeys : PluginDynamicCommand
    {
        private const string Group = "Windows Capture - Annotation";
        private const int PollMs = 1500;
        private const int ConfirmMs = 3000;
        private const int FeedbackMs = 1500;

        private sealed record Def(string Id, string Name, string Command, Dictionary<string, string> Args = null, bool Confirm = false);

        private static readonly Def[] Defs =
        {
            new("wcc.pen", "Stylo", AgentCommands.EditPen),
            new("wcc.highlighter", "Surligneur", AgentCommands.EditHighlighter),
            new("wcc.eraser", "Gomme", AgentCommands.EditEraser),
            new("wcc.clearall", "Effacer toutes les annotations (double appui)", AgentCommands.EditClearAll,
                new Dictionary<string, string> { ["confirmed"] = "true" }, Confirm: true),
            new("wcc.ruler", "Règle (afficher / masquer)", AgentCommands.EditRuler),
            new("wcc.undo", "Annuler", AgentCommands.EditUndo),
            new("wcc.redo", "Rétablir", AgentCommands.EditRedo),
            new("wcc.copy", "Copier l'image", AgentCommands.EditCopy),
            new("wcc.saveas", "Enregistrer sous", AgentCommands.EditSaveAs),
            new("wcc.share", "Partager (feuille de partage Windows)", AgentCommands.EditShare),
            new("wcc.color.next", "Couleur suivante", AgentCommands.EditColor, new Dictionary<string, string> { ["delta"] = "1" }),
            new("wcc.color.prev", "Couleur précédente", AgentCommands.EditColor, new Dictionary<string, string> { ["delta"] = "-1" }),
            new("wcc.width.up", "Épaisseur +", AgentCommands.EditWidth, new Dictionary<string, string> { ["delta"] = "2" }),
            new("wcc.width.down", "Épaisseur −", AgentCommands.EditWidth, new Dictionary<string, string> { ["delta"] = "-2" }),
            new("wcc.width.reset", "Épaisseur par défaut", AgentCommands.EditWidth, new Dictionary<string, string> { ["reset"] = "true" }),
            new("wcc.zoom.in", "Zoom avant", AgentCommands.ZoomIn),
            new("wcc.zoom.out", "Zoom arrière", AgentCommands.ZoomOut),
            new("wcc.zoom.100", "Zoom 100 %", AgentCommands.ZoomReset),
            new("wcc.zoom.fit", "Ajuster à la fenêtre", AgentCommands.ZoomFit),
        };

        private readonly ConcurrentDictionary<string, DateTime> _lastPress = new();
        private readonly ConcurrentDictionary<string, bool> _busy = new();
        private readonly ConcurrentDictionary<string, (DateTime Until, string Text, BitmapColor Frame)> _flash = new();
        private readonly ConcurrentDictionary<string, DateTime> _armed = new();
        private Timer _timer;
        private int _polling;
        private volatile bool _open;
        private volatile string _tool = "None";
        private volatile bool _undo;
        private volatile bool _redo;
        private volatile string _colorName = "";
        private volatile string _width = "";
        private string _fingerprint = "";
        private int _closedPolls;

        public AnnotationKeys()
            : base()
        {
            foreach (var d in Defs)
            {
                this.AddParameter(d.Id, d.Name, Group);
            }
        }

        private WindowsCaptureControllerPlugin CapturePlugin => this.Plugin as WindowsCaptureControllerPlugin;

        protected override bool OnLoad()
        {
            this._timer = new Timer(_ => this.Poll(), null, 2500, PollMs);
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
                var r = agent.Call(AgentCommands.EditState, timeoutMs: 2500);
                if (!r.Ok)
                {
                    return;
                }

                var open = r.Data.GetValueOrDefault("open") == "True";
                if (!open && this._open && ++this._closedPolls < 2)
                {
                    // A flyout / menu hides the editor from UI Automation for a moment: keep the display
                    // until "closed" is seen twice in a row (3 s).
                    return;
                }

                this._closedPolls = 0;
                this._open = open;
                this._tool = r.Data.GetValueOrDefault("tool", "None");
                this._undo = r.Data.GetValueOrDefault("undo") == "True";
                this._redo = r.Data.GetValueOrDefault("redo") == "True";
                var fp = $"{this._open}|{this._tool}|{this._undo}|{this._redo}";
                if (fp != this._fingerprint)
                {
                    this._fingerprint = fp;
                    this.ActionImageChanged();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning("editor state poll failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref this._polling, 0);
            }
        }

        protected override void RunCommand(string actionParameter)
        {
            var plugin = this.CapturePlugin;
            var def = Defs.FirstOrDefault(d => d.Id == actionParameter);
            if (plugin == null || def == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (this._lastPress.TryGetValue(actionParameter, out var last) && (now - last).TotalMilliseconds < 300)
            {
                return;
            }

            this._lastPress[actionParameter] = now;

            if (def.Confirm)
            {
                // Destructive: first press arms, second press within 3 s executes.
                if (!this._armed.TryGetValue(def.Id, out var armedAt) || (now - armedAt).TotalMilliseconds > ConfirmMs)
                {
                    this._armed[def.Id] = now;
                    this.Flash(actionParameter, "Encore 1x", Palette.Orange, ConfirmMs);
                    return;
                }

                this._armed.TryRemove(def.Id, out _);
            }

            if (!this._busy.TryAdd(actionParameter, true))
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var agent = plugin.Agent ?? throw new InvalidOperationException("plugin not loaded");
                    var r = agent.Call(def.Command, def.Args, timeoutMs: 6000);
                    PluginLog.Info($"{def.Id}: ok={r.Ok} status={r.Status} message={r.Message}");
                    if (r.Ok && def.Command == AgentCommands.EditColor && r.Hint is { } h && h.Contains('|'))
                    {
                        this._colorName = h[(h.IndexOf('|') + 1)..];
                        this.RefreshStrokeKeys();
                    }
                    else if (r.Ok && def.Command == AgentCommands.EditWidth && r.Hint is { } w)
                    {
                        this._width = w;
                        this.RefreshStrokeKeys();
                    }

                    var okText = def.Command == AgentCommands.EditCopy ? "Copié ✓" : null;
                    this.Flash(actionParameter, r.Ok ? okText : (r.Status == "NotAvailable" ? "N/D" : "Erreur"), r.Ok ? Palette.Green : Palette.Orange);
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Annotation key {def.Id} crashed", ex);
                    this.Flash(actionParameter, "Erreur", Palette.Red);
                }
                finally
                {
                    this._busy.TryRemove(actionParameter, out _);
                    this.Poll();
                }
            });
        }

        private void RefreshStrokeKeys()
        {
            foreach (var id in new[] { "wcc.color.next", "wcc.color.prev", "wcc.width.up", "wcc.width.down", "wcc.width.reset", "wcc.pen", "wcc.highlighter" })
            {
                this.ActionImageChanged(id);
            }
        }

        private void Flash(string parameter, string text, BitmapColor frame, int ms = FeedbackMs)
        {
            this._flash[parameter] = (DateTime.UtcNow.AddMilliseconds(ms), text, frame);
            this.ActionImageChanged(parameter);
            Task.Delay(ms + 50).ContinueWith(_ => this.ActionImageChanged(parameter));
        }

        protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) =>
            Defs.FirstOrDefault(d => d.Id == actionParameter)?.Name ?? actionParameter;

        protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
        {
            try
            {
                var dim = this._open ? Palette.White : Palette.Grey;
                var accent = InkColors.Of(this._colorName);
                var widthLabel = string.IsNullOrEmpty(this._width) ? "Épaisseur" : $"{this._width} px";
                float.TryParse(this._width, out var wv);
                Icons.Spec Sel(Glyph g, string label, string tool) => new()
                {
                    Glyph = g, Label = label, Fg = dim, Accent = accent,
                    Frame = this._open && this._tool == tool ? Palette.Blue : null,
                    Bg = this._open && this._tool == tool && tool == "Eraser" ? Palette.Blue : Palette.Background,
                };

                var spec = actionParameter switch
                {
                    "wcc.pen" => Sel(Glyph.Pen, "Stylo", "Pen"),
                    "wcc.highlighter" => Sel(Glyph.Highlighter, "Surligneur", "Highlighter"),
                    "wcc.eraser" => Sel(Glyph.Eraser, "Gomme", "Eraser"),
                    "wcc.clearall" => new Icons.Spec { Glyph = Glyph.Trash, Label = "Tout effacer", Fg = dim },
                    "wcc.ruler" => new Icons.Spec { Glyph = Glyph.Ruler, Label = "Règle", Fg = dim },
                    "wcc.undo" => new Icons.Spec { Glyph = Glyph.Undo, Label = "Annuler", Fg = this._open && this._undo ? Palette.White : Palette.Grey },
                    "wcc.redo" => new Icons.Spec { Glyph = Glyph.Redo, Label = "Rétablir", Fg = this._open && this._redo ? Palette.White : Palette.Grey },
                    "wcc.copy" => new Icons.Spec { Glyph = Glyph.Copy, Label = "Copier", Fg = dim },
                    "wcc.saveas" => new Icons.Spec { Glyph = Glyph.Save, Label = "Enreg. sous", Fg = dim },
                    "wcc.share" => new Icons.Spec { Glyph = Glyph.Share, Label = "Partager", Fg = dim },
                    "wcc.color.next" => new Icons.Spec { Glyph = Glyph.Swatch, Label = "Couleur ›", Fg = dim, Accent = accent },
                    "wcc.color.prev" => new Icons.Spec { Glyph = Glyph.Swatch, Label = "‹ Couleur", Fg = dim, Accent = accent },
                    "wcc.width.up" => new Icons.Spec { Glyph = Glyph.Width, Label = "+ " + widthLabel, Fg = dim, Accent = accent, WidthValue = wv > 0 ? wv : 4 },
                    "wcc.width.down" => new Icons.Spec { Glyph = Glyph.Width, Label = "− " + widthLabel, Fg = dim, Accent = accent, WidthValue = wv > 0 ? wv : 4 },
                    "wcc.width.reset" => new Icons.Spec { Glyph = Glyph.Width, Label = "Défaut", Fg = dim, Accent = accent, WidthValue = 4 },
                    "wcc.zoom.in" => new Icons.Spec { Glyph = Glyph.ZoomIn, Label = "Zoom +", Fg = dim },
                    "wcc.zoom.out" => new Icons.Spec { Glyph = Glyph.ZoomOut, Label = "Zoom −", Fg = dim },
                    "wcc.zoom.100" => new Icons.Spec { Glyph = Glyph.Zoom100, Label = "100 %", Fg = dim },
                    "wcc.zoom.fit" => new Icons.Spec { Glyph = Glyph.ZoomFit, Label = "Ajuster", Fg = dim },
                    _ => new Icons.Spec { Glyph = Glyph.Empty, Label = "—", Fg = Palette.Grey },
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
