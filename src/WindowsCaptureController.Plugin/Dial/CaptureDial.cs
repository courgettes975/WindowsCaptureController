namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using WindowsCaptureController.Shared;

    // MX Creative Dialpad (and Loupedeck encoders). NOTE: the MX Creative Keypad has no dial.
    //
    //   dial.auto        follows the mode chosen with the "Mode molette" key (default: config defaultDialMode)
    //   dial.zoom        rotate: zoom in / out (editor zoom menu)          press: 100 %
    //   dial.thickness   rotate: pen / highlighter width ±2 per step       press: default width
    //   dial.color       rotate: next / previous colour (applied at once)  press: confirms (shows the colour)
    //   dial.history     rotate: browse latest captures / videos           press: open the selected one
    //   dial.delay       rotate: 0 / 3 / 5 / 10 s                          press: capture after the delay
    //                                                                       (press again during it: cancel)
    //
    // Rotation is accumulated (DialAccumulator): one agent command at a time, zoom capped at 4 steps per
    // batch, excess dropped — a fast spin never leaves a backlog running after the user stopped.
    // A long press is not used: the Actions SDK gives no reliable long-press event for the dial.
    public sealed class CaptureDial : PluginDynamicAdjustment
    {
        private const string Group = "Windows Capture - Molette";
        private readonly Dictionary<string, DialAccumulator> _acc = new();

        public CaptureDial()
            : base(true)
        {
            this.AddParameter("dial.auto", "Molette Outil Capture (mode : touche « Mode molette »)", Group);
            this.AddParameter("dial.zoom", "Molette : zoom (appui = 100 %)", Group);
            this.AddParameter("dial.thickness", "Molette : épaisseur (appui = défaut)", Group);
            this.AddParameter("dial.color", "Molette : couleur", Group);
            this.AddParameter("dial.history", "Molette : historique (appui = ouvrir)", Group);
            this.AddParameter("dial.delay", "Molette : délai (appui = capturer)", Group);
            foreach (var m in DialState.Modes)
            {
                this._acc[m] = new DialAccumulator(maxStepsPerFlush: m == "zoom" ? 4 : 6);
            }
        }

        private WindowsCaptureControllerPlugin CapturePlugin => this.Plugin as WindowsCaptureControllerPlugin;

        private DialState State => this.CapturePlugin?.Dial;

        private string ModeOf(string parameter) =>
            parameter == "dial.auto" ? (this.State?.Mode ?? "zoom") : parameter.Replace("dial.", "");

        protected override bool OnLoad()
        {
            if (this.State != null)
            {
                this.State.Changed += this.OnStateChanged;
            }

            return true;
        }

        protected override bool OnUnload()
        {
            if (this.State != null)
            {
                this.State.Changed -= this.OnStateChanged;
            }

            return true;
        }

        private void OnStateChanged()
        {
            foreach (var p in new[] { "dial.auto", "dial.zoom", "dial.thickness", "dial.color", "dial.history", "dial.delay" })
            {
                this.AdjustmentValueChanged(p);
            }
        }

        // ------------------------------------------------------------------ rotation

        protected override void ApplyAdjustment(string actionParameter, int diff)
        {
            var state = this.State;
            var agent = this.CapturePlugin?.Agent;
            if (state == null || agent == null || diff == 0)
            {
                return;
            }

            var mode = this.ModeOf(actionParameter);
            var acc = this._acc[mode];
            acc.Sensitivity = state.Sensitivity;
            acc.Inverted = state.Inverted;

            switch (mode)
            {
                case "delay":
                case "history":
                    // Local selection only (no Snipping Tool command): applied immediately.
                    acc.Add(diff);
                    var steps = acc.TakeSteps();
                    while (acc.TakeSteps() != 0)
                    {
                    }

                    if (steps != 0)
                    {
                        if (mode == "delay")
                        {
                            this.MoveDelay(state, steps);
                        }
                        else
                        {
                            this.MoveHistory(state, agent, steps);
                        }
                    }

                    break;

                default:
                    if (acc.Add(diff))
                    {
                        Task.Run(() => this.FlushLoop(mode, acc, state, agent));
                    }

                    break;
            }
        }

        private void FlushLoop(string mode, DialAccumulator acc, DialState state, AgentClient agent)
        {
            try
            {
                int steps;
                while ((steps = acc.TakeSteps()) != 0)
                {
                    switch (mode)
                    {
                        case "zoom":
                            var cmd = steps > 0 ? AgentCommands.ZoomIn : AgentCommands.ZoomOut;
                            for (var i = 0; i < Math.Abs(steps); i++)
                            {
                                var r = agent.Call(cmd, timeoutMs: 4000);
                                state.SetStatus("zoom", r.Ok ? (steps > 0 ? "Zoom +" : "Zoom −") : (r.Status == "NotAvailable" ? r.Message : "Erreur"));
                                if (!r.Ok)
                                {
                                    acc.Clear(); // limit reached / no image: drop the rest of this spin
                                    break;
                                }
                            }

                            break;

                        case "thickness":
                        {
                            var r = agent.Call(AgentCommands.EditWidth, new Dictionary<string, string> { ["delta"] = (steps * 2).ToString() }, 5000);
                            state.SetStatus("thickness", r.Ok ? $"{r.Hint} px" : (r.Status == "NotAvailable" ? "Pas d'image" : "Erreur"));
                            break;
                        }

                        case "color":
                        {
                            var r = agent.Call(AgentCommands.EditColor, new Dictionary<string, string> { ["delta"] = steps.ToString() }, 5000);
                            var name = r.Ok && r.Hint is { } h && h.Contains('|') ? h[(h.IndexOf('|') + 1)..] : null;
                            state.SetStatus("color", name ?? (r.Status == "NotAvailable" ? "Pas d'image" : "Erreur"));
                            break;
                        }
                    }
                }

                if (acc.DroppedSteps > 0)
                {
                    PluginLog.Info($"dial {mode}: {acc.DroppedSteps} step(s) dropped so far (spin faster than the Snipping Tool)");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error($"dial {mode} failed", ex);
                state.SetStatus(mode, "Erreur");
                acc.Clear();
                while (acc.TakeSteps() != 0)
                {
                }
            }
        }

        private void MoveDelay(DialState state, int steps)
        {
            if (state.Countdown != null)
            {
                return; // rotating during a countdown does not change it
            }

            state.DelayIndex = Math.Clamp(state.DelayIndex + Math.Sign(steps), 0, DialState.Delays.Length - 1);
            state.SetStatus("delay", state.DelaySeconds == 0 ? "Sans délai" : $"Délai {state.DelaySeconds} s");
        }

        private void MoveHistory(DialState state, AgentClient agent, int steps)
        {
            if (state.History.Count == 0 || (DateTime.UtcNow - state.HistoryLoadedAt).TotalSeconds > 20)
            {
                this.LoadHistory(state, agent);
            }

            if (state.History.Count == 0)
            {
                state.SetStatus("history", "Aucune capture");
                return;
            }

            state.HistoryIndex = Math.Clamp(state.HistoryIndex + steps, 0, state.History.Count - 1);
            this.ShowHistory(state);
        }

        private void LoadHistory(DialState state, AgentClient agent)
        {
            var r = agent.Call(AgentCommands.HistoryList, new Dictionary<string, string> { ["max"] = "30" }, 4000);
            var list = new List<(string, string, string)>();
            if (r.Ok && int.TryParse(r.Data.GetValueOrDefault("count", "0"), out var n))
            {
                for (var i = 0; i < n; i++)
                {
                    var parts = r.Data.GetValueOrDefault(i.ToString(), "").Split('|', 3);
                    if (parts.Length == 3)
                    {
                        list.Add((parts[0], parts[1], parts[2]));
                    }
                }
            }

            state.History = list;
            state.HistoryLoadedAt = DateTime.UtcNow;
            state.HistoryIndex = Math.Clamp(state.HistoryIndex, 0, Math.Max(0, list.Count - 1));
        }

        private void ShowHistory(DialState state)
        {
            var (kind, time, _) = state.History[state.HistoryIndex];
            var hhmm = time.Length >= 16 ? time[11..16] : time;
            state.SetStatus("history", $"{state.HistoryIndex + 1}/{state.History.Count} {(kind == "video" ? "▶" : "▣")} {hhmm}");
        }

        // ------------------------------------------------------------------ press

        protected override void RunCommand(string actionParameter)
        {
            var state = this.State;
            var agent = this.CapturePlugin?.Agent;
            if (state == null || agent == null)
            {
                return;
            }

            var mode = this.ModeOf(actionParameter);
            Task.Run(() =>
            {
                try
                {
                    switch (mode)
                    {
                        case "zoom":
                        {
                            var r = agent.Call(AgentCommands.ZoomReset, timeoutMs: 4000);
                            state.SetStatus("zoom", r.Ok ? "100 %" : "Pas d'image");
                            break;
                        }

                        case "thickness":
                        {
                            var r = agent.Call(AgentCommands.EditWidth, new Dictionary<string, string> { ["reset"] = "true" }, 5000);
                            state.SetStatus("thickness", r.Ok ? $"{r.Hint} px (défaut)" : "Pas d'image");
                            break;
                        }

                        case "color":
                            // Each step is already applied in the Snipping Tool: the press just confirms.
                            state.SetStatus("color", (state.Status("color") is { Length: > 0 } c ? c : "Couleur") + " ✓");
                            break;

                        case "history":
                            if (state.History.Count == 0)
                            {
                                this.LoadHistory(state, agent);
                            }

                            if (state.History.Count == 0)
                            {
                                state.SetStatus("history", "Aucune capture");
                                break;
                            }

                            var name = state.History[state.HistoryIndex].Name;
                            var o = agent.Call(AgentCommands.HistoryOpen, new Dictionary<string, string> { ["name"] = name }, 4000);
                            state.SetStatus("history", o.Ok ? "Ouvert ✓" : "Introuvable");
                            break;

                        case "delay":
                            this.ToggleDelayedCapture(state, agent);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"dial press {mode} failed", ex);
                    state.SetStatus(mode, "Erreur");
                }
            });
        }

        private void ToggleDelayedCapture(DialState state, AgentClient agent)
        {
            var running = state.Countdown;
            if (running != null)
            {
                running.Cancel();
                state.Countdown = null;
                state.SetStatus("delay", "Annulé");
                return;
            }

            var seconds = state.DelaySeconds;
            var cts = new CancellationTokenSource();
            state.Countdown = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    for (var left = seconds; left > 0; left--)
                    {
                        state.SetStatus("delay", $"Capture dans {left} s");
                        await Task.Delay(1000, cts.Token).ConfigureAwait(false);
                    }

                    if (!ReferenceEquals(state.Countdown, cts))
                    {
                        return;
                    }

                    state.Countdown = null;
                    var r = agent.Call(AgentCommands.DefaultCapture, timeoutMs: 6000);
                    state.SetStatus("delay", r.Ok ? "Capture !" : "Erreur");
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        protected override string GetAdjustmentValue(string actionParameter)
        {
            var state = this.State;
            if (state == null)
            {
                return "";
            }

            var mode = this.ModeOf(actionParameter);
            var s = state.Status(mode);
            return actionParameter == "dial.auto" ? $"{DialState.Label(mode)} {s}".Trim() : s;
        }
    }
}
