namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;

    // State shared by the dial (CaptureDial, MX Creative Dialpad) and the "Mode molette" key on the Keypad.
    // The Dialpad has no screen: the Keypad key shows the current mode and its last value.
    internal sealed class DialState
    {
        public static readonly string[] Modes = { "zoom", "thickness", "color", "history", "delay" };
        public static readonly int[] Delays = { 0, 3, 5, 10 };

        private readonly ConcurrentDictionary<string, string> _status = new();
        private string _mode = "zoom";

        public event Action Changed;

        public string Mode
        {
            get => this._mode;
            set
            {
                if (Array.IndexOf(Modes, value) >= 0 && value != this._mode)
                {
                    this._mode = value;
                    this.Raise();
                }
            }
        }

        public string NextMode() => this.Mode = Modes[(Array.IndexOf(Modes, this.Mode) + 1) % Modes.Length];

        public string Status(string mode) => this._status.TryGetValue(mode, out var s) ? s : "";

        public void SetStatus(string mode, string text)
        {
            this._status[mode] = text ?? "";
            this.Raise();
        }

        // ---- settings from config.json (via the agent)
        public int Sensitivity { get; set; } = 1;

        public bool Inverted { get; set; }

        // ---- delay mode
        public int DelayIndex { get; set; }

        public int DelaySeconds => Delays[Math.Clamp(this.DelayIndex, 0, Delays.Length - 1)];

        public CancellationTokenSource Countdown { get; set; }

        // ---- history mode
        public List<(string Kind, string Time, string Name)> History { get; set; } = new();

        public int HistoryIndex { get; set; }

        public DateTime HistoryLoadedAt { get; set; } = DateTime.MinValue;

        public static string Label(string mode) => mode switch
        {
            "zoom" => "Zoom",
            "thickness" => "Épaisseur",
            "color" => "Couleur",
            "history" => "Historique",
            "delay" => "Délai",
            _ => mode,
        };

        public void Raise()
        {
            try
            {
                this.Changed?.Invoke();
            }
            catch (Exception ex)
            {
                PluginLog.Warning("dial state listener failed", ex);
            }
        }
    }
}
